using System.Data.Common;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

public sealed class FilesIntegrationFactAttribute : FactAttribute
{
    public FilesIntegrationFactAttribute()
    {
        var settings = Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS");
        var metadata = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA");
        if (string.IsNullOrWhiteSpace(settings) || !File.Exists(settings) || string.IsNullOrWhiteSpace(metadata) || !File.Exists(metadata))
            Skip = "未提供隔离 MySQL 配置及平台元数据，真实文件库验收未执行。";
    }
}

internal sealed class FilesFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator SharedIds = new(851);
    public DatabaseSession Session { get; private set; } = null!;
    public MetadataRegistry Registry { get; private set; } = null!;
    public SqlRowStore Store { get; private set; } = null!;
    public FileLibraryOptions Options { get; private set; } = null!;
    public string DirectoryPath { get; private set; } = "";
    public string TableName { get; private set; } = "";
    public string PageId { get; private set; } = "";
    public string ProtectedColumnId { get; private set; } = "";
    public string PublicColumnId { get; private set; } = "";
    public List<long> FileIds { get; } = [];
    public List<long> FolderIds { get; } = [];
    public List<long> RecordIds { get; } = [];
    public IIdGenerator Ids => SharedIds;
    public RuntimeActor Actor(long id = 8001, bool administrator = false, string? project = null) => new(id, "文件测试账号", administrator, project ?? Registry.Platform.YzProjectId, Registry.Platform.YzAdminTypes[0].Id);
    public FileLibraryService Service(RuntimeActor? actor = null) => new(Store, new([Registry.Platform,Registry.Demo]), Ids, new TestAuthorization(actor ?? Actor(), Registry), Options);
    public FileReferenceMutationObserver Observer(RuntimeActor? actor = null) => new(Store, new([Registry.Platform,Registry.Demo]), new TestAuthorization(actor ?? Actor(), Registry), Ids, Options);

    /// <summary>只在指定测试库建立随机临时业务表，文件基础设施表必须已由协调者初始化。</summary>
    public static async Task<FilesFixture> CreateAsync()
    {
        var fixture = new FilesFixture();
        try
        {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.ExpectedDatabase != "metarill_test") throw new InvalidOperationException("文件测试只允许平台隔离测试库。");
        fixture.Session = new DatabaseSession(options);
        var metadataPath = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
        var platform = MetadataRegistry.Load(metadataPath);
        var testId = Guid.NewGuid().ToString("N")[..12];
        fixture.TableName = "mt_test_files_" + testId;
        fixture.DirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), ".local", "file-test-" + testId);
        Directory.CreateDirectory(fixture.DirectoryPath);
        fixture.Options = new FileLibraryOptions { RootDirectory = Path.Combine(fixture.DirectoryPath, "objects"), MaxFileBytes = 4096 };
        var table = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.TableName, Name = "虚构文件测试记录", YzProjectId = platform.YzProjectId };
        ColumnMetadata Column(string name, string type, bool required = false, string control = "", string detail = "") => new()
        {
            Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, ColumnName = name, LogicalType = type, ColumnLength = type == "string" ? "max" : "", IsDbNotNull = required ? 1 : 0,
            ControlKind = control, ControlDetail = detail, YzProjectId = platform.YzProjectId
        };
        var columns = new[] { Column("id", "int64", true), Column("yz_project_id", "int64", true), Column("delete_no", "datetime"),
            Column("attachments", "string", false, "FileLibrary", "{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"),
            Column("public_files", "string", false, "FileLibrary", "{\"accessMode\":\"PUBLIC\"}"), Column("notes", "string") };
        fixture.ProtectedColumnId = columns.Single(c => c.ColumnName == "attachments").Id;
        fixture.PublicColumnId = columns.Single(c => c.ColumnName == "public_files").Id;
        var page = new PageMetadata { Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, YzProjectId = platform.YzProjectId, YzAdminTypeId = platform.YzAdminTypes[0].Id };
        fixture.PageId = page.Id;
        platform.ConfigTables.Add(table); platform.ConfigColumns.AddRange(columns); platform.ConfigPages.Add(page);
        platform.ConfigPageColumns.AddRange(columns.Where(c => c.ControlKind == "FileLibrary").Select(c => new PageColumnMetadata { Id = SharedIds.NextId().ToString(), ConfigPageId = page.Id, ConfigColumnId = c.Id, ColumnName = c.ColumnName, IsEdit = 1 }));
        var testPlatform = Path.Combine(fixture.DirectoryPath, "platform.json");
        await File.WriteAllTextAsync(testPlatform, JsonSerializer.Serialize(platform, JsonDefaults.Options));
        fixture.Registry = new MetadataRegistry(testPlatform, Path.Combine(Path.GetDirectoryName(metadataPath)!, "demo.json"));
        fixture.Store = new SqlRowStore(fixture.Session, fixture.Registry.Platform);
        await fixture.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = -1L });
        var schema = new SchemaService(fixture.Session);
        var managed = new MetadataCatalog { YzProjectId = platform.YzProjectId, ConfigTables = [table], ConfigColumns = columns.ToList() };
        await schema.ApplyAsync(await schema.PreviewAsync(managed));
        return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>每个文件都记录本测试分配的ID，后续清理不会扫整个文件库。</summary>
    public async Task<FileUploadPlan> PlanAsync(string name, byte[] bytes, RuntimeActor? actor = null, bool publicly = false, long? folder = null, string? sha256 = null)
    {
        var plan = await Service(actor).PlanUploadAsync(new(name, "text/html", bytes.Length, publicly ? PublicColumnId : ProtectedColumnId, folder, sha256));
        FileIds.Add(plan.FileId); return plan;
    }
    public async Task<FileDescriptor> ReadyAsync(string name, byte[] bytes, RuntimeActor? actor = null, bool publicly = false)
    {
        var service = Service(actor); var plan = await PlanAsync(name, bytes, actor, publicly);
        await service.UploadAsync(plan.FileId, new MemoryStream(bytes)); return await service.CompleteAsync(plan.FileId);
    }
    public async Task<long> RecordAsync(string? project = null)
    {
        var id = SharedIds.NextId(); RecordIds.Add(id);
        await Store.InsertAsync(TableName, new Dictionary<string, object?> { ["id"] = id, ["yz_project_id"] = long.Parse(project ?? Registry.Platform.YzProjectId) }); return id;
    }
    public async Task<List<Dictionary<string, object?>>> ReferencesAsync(long record) => await Store.ReadAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = record });

    /// <summary>清理范围只包含当前fixture创建的唯一ID和随机临时表。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Session is null) return;
            await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
            foreach (var id in FileIds)
            {
                await ExecuteAsync(connection, "DELETE FROM `yz_file_ref` WHERE `file_id`=@id", id);
                await ExecuteAsync(connection, "DELETE FROM `yz_file_library` WHERE `id`=@id", id);
            }
            foreach (var id in RecordIds) await ExecuteAsync(connection, "DELETE FROM `yz_file_ref` WHERE `record_id`=@id", id);
            foreach (var id in FolderIds) await ExecuteAsync(connection, "DELETE FROM `yz_file_library_folder` WHERE `id`=@id", id);
            if (string.IsNullOrEmpty(TableName)) return;
            if (!TableName.StartsWith("mt_test_files_", StringComparison.Ordinal)) throw new InvalidOperationException("临时表清理范围错误。");
            await using var drop = connection.CreateCommand(); drop.CommandText = "DROP TABLE IF EXISTS " + Session.Provider.QuoteIdentifier(TableName); await drop.ExecuteNonQueryAsync();
        }
        finally
        {
            Session?.Dispose();
            var local = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".local")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(DirectoryPath);
            if (target.StartsWith(local, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target)) Directory.Delete(target, true);
        }
    }
    private static async Task ExecuteAsync(DbConnection connection, string sql, long id)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter); await command.ExecuteNonQueryAsync();
    }
}

internal sealed class TestAuthorization(RuntimeActor actor, MetadataRegistry registry, IReadOnlySet<string>? operations = null) : IRuntimeAuthorization
{
    public RuntimeActor RequireActor() => actor;
    public void RequirePagePermission(string pageId, string operation)
    {
        var page = registry.FindPage(pageId);
        if (operations is not null && !operations.Contains(operation)) throw new UnauthorizedAccessException("测试身份缺少当前操作权限。");
        if (page is null || page.YzProjectId != actor.ProjectId || operation is not ("create" or "update" or "delete" or "detail" or "list")) throw new UnauthorizedAccessException("测试身份无该页面权限。");
    }
}
