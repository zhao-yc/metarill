using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class ProjectFileRuntimeIntegrationTests
{
    [MetadataIntegrationFact]
    public async Task Business_files_use_the_generated_transaction_and_public_routing_stays_in_the_selected_slot()
    {
        await using var fixture = await ProjectFileDatabaseFixture.CreateAsync();
        var actor = new RuntimeActor(8801, "项目文件验收", true, fixture.Registry.Demo.YzProjectId, "");
        var authorization = new TestFileAuthorization(actor);
        var resolver = new ProjectFileLibraryResolver(fixture.Runtimes, authorization, fixture.Ids);
        var service = resolver.RequireCurrent(); var session = fixture.Sessions.Business;
        var file = await fixture.ReadyAsync(service, "private.txt", fixture.ProtectedColumn);
        var publicly = await fixture.ReadyAsync(service, "public.txt", fixture.PublicColumn);
        Assert.EndsWith("?projectId=" + actor.ProjectId, publicly.Url);
        var record = fixture.Ids.NextId(); fixture.RecordIds.Add(record);
        var original = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var observer = new ProjectFileReferenceMutationObserver(fixture.Runtimes, authorization, fixture.Ids);
        await session.InTransactionAsync(async () =>
        {
            await fixture.BusinessStore.InsertAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record, ["yz_project_id"] = long.Parse(actor.ProjectId), ["attachments"] = original, ["notes"] = "保留值" });
            await observer.BeforeCommitAsync(new(fixture.PageId, fixture.TableName, record, actor.ProjectId, new Dictionary<string, object?> { ["attachments"] = original }, false, session.CurrentTransaction!, true));
            return true;
        });
        Assert.Single(await fixture.ReferencesAsync(record));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(file.FileId));
        // 换成未完成文件时其他字段和引用也必须回滚，不能仅撤销文件引用写入。
        var pending = await service.PlanUploadAsync(new("pending.txt", null, 3, fixture.ProtectedColumn)); fixture.FileIds.Add(pending.FileId);
        var invalid = JsonSerializer.Serialize(new[] { pending.FileId.ToString() });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.InTransactionAsync(async () =>
        {
            await fixture.BusinessStore.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = invalid, ["notes"] = "不得提交" });
            await observer.BeforeCommitAsync(new(fixture.PageId, fixture.TableName, record, actor.ProjectId, new Dictionary<string, object?> { ["attachments"] = invalid }, false, session.CurrentTransaction!));
            return true;
        }));
        var saved = (await fixture.BusinessStore.ReadAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record })).Single();
        Assert.Equal("保留值", SqlRowStore.Text(saved, "notes")); Assert.Equal(original, SqlRowStore.Text(saved, "attachments"));
        Assert.Single(await fixture.ReferencesAsync(record));
        // 即使管理员身份合法，平台事务也不能冒充同项目业务连接。
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Sessions.Platform.InTransactionAsync(async () =>
        {
            await observer.BeforeCommitAsync(new(fixture.PageId, fixture.TableName, record, actor.ProjectId, new Dictionary<string, object?> { ["attachments"] = original }, false, fixture.Sessions.Platform.CurrentTransaction!));
            return true;
        }));
        var anonymous = new TestFileAuthorization(null);
        var publicResolver = new ProjectFileLibraryResolver(fixture.Runtimes, anonymous, fixture.Ids);
        await using (var content = (await publicResolver.OpenPublicAsync(publicly.FileId, actor.ProjectId)).Stream)
        {
            using var reader = new StreamReader(content); Assert.Equal("验收内容", await reader.ReadToEndAsync());
        }
        Assert.Equal(0, anonymous.ActorCalls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => publicResolver.OpenPublicAsync(publicly.FileId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => publicResolver.OpenPublicAsync(file.FileId, actor.ProjectId));
        // 同库中的项目字段仍是公开读边界；项目提示不能把其他项目行解释成当前项目文件。
        var fileStore = fixture.Runtimes.Resolve(actor.ProjectId).Store;
        await fileStore.UpdateAsync("yz_file_library", publicly.FileId, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(fixture.Registry.Platform.YzProjectId) });
        try { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => publicResolver.OpenPublicAsync(publicly.FileId, actor.ProjectId)); }
        finally { await fileStore.UpdateAsync("yz_file_library", publicly.FileId, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(actor.ProjectId) }); }
        Assert.Equal(2, Directory.GetFiles(fixture.Runtimes.Resolve(actor.ProjectId).Options.RootDirectory, "*.blob").Length);
        Assert.False(Directory.EnumerateFiles(fixture.Root, "*.blob", SearchOption.TopDirectoryOnly).Any());
        // 业务删除与清引用仍加入业务事务，提交后文件才允许删除。
        await session.InTransactionAsync(async () =>
        {
            await fixture.BusinessStore.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            await observer.BeforeCommitAsync(new(fixture.PageId, fixture.TableName, record, actor.ProjectId, new Dictionary<string, object?>(), true, session.CurrentTransaction!));
            return true;
        });
        Assert.Empty(await fixture.ReferencesAsync(record));
        Assert.True((await service.DeleteAsync(file.FileId)).StorageRemoved);
    }
}

/// <summary>只在两个明确测试库安装源头派生的文件模型，并创建唯一随机业务表。</summary>
internal sealed class ProjectFileDatabaseFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator SharedIds = new(855);
    private readonly string directory = Path.Combine(Path.GetTempPath(), "metarill-project-file-db", Guid.NewGuid().ToString("N"));
    private bool cleanupAllowed;
    public TargetDatabaseSessions Sessions { get; private set; } = null!;
    public MetadataRegistry Registry { get; private set; } = null!;
    public ProjectFileRuntimes Runtimes { get; private set; } = null!;
    public SqlRowStore BusinessStore { get; private set; } = null!;
    public IIdGenerator Ids => SharedIds;
    public string Root => Path.Combine(directory, "objects");
    public string TableName { get; private set; } = "";
    public string PageId { get; private set; } = "";
    public string ProtectedColumn { get; private set; } = "";
    public string PublicColumn { get; private set; } = "";
    public List<long> FileIds { get; } = [];
    public List<long> RecordIds { get; } = [];

    /// <summary>先验证真实连接库名，再通过显式 SchemaService 预览、执行文件和唯一业务表计划。</summary>
    public static async Task<ProjectFileDatabaseFixture> CreateAsync()
    {
        var fixture = new ProjectFileDatabaseFixture();
        try
        {
            Directory.CreateDirectory(fixture.directory);
            using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            DatabaseOptions Options(string key, string expected)
            {
                var value = settings.RootElement.GetProperty("Metarill").GetProperty(key).Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                if (value.Provider != "mysql" || value.ExpectedDatabase != expected) throw new InvalidOperationException("项目文件验收只允许明确的 MySQL 隔离测试库。");
                return value;
            }
            var source = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
            var platform = MetadataRegistry.Load(source); var demo = MetadataRegistry.Load(Path.Combine(Path.GetDirectoryName(source)!, "demo.json"));
            fixture.TableName = "mt_project_files_" + Guid.NewGuid().ToString("N")[..12];
            var table = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.TableName, YzProjectId = demo.YzProjectId };
            ColumnMetadata Column(string name, string type, string control = "", string detail = "") => new() { Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, ColumnName = name, LogicalType = type, ColumnLength = type == "string" ? "max" : "", IsDbNotNull = name == "id" ? 1 : 0, ControlKind = control, ControlDetail = detail, YzProjectId = demo.YzProjectId };
            var columns = new[] { Column("id", "int64"), Column("yz_project_id", "int64"), Column("delete_no", "datetime"), Column("notes", "string"),
                Column("attachments", "string", "FileLibrary", "{\"accessMode\":\"PROTECTED\"}"), Column("public_files", "string", "FileLibrary", "{\"accessMode\":\"PUBLIC\"}") };
            fixture.ProtectedColumn = columns.Single(column => column.ColumnName == "attachments").Id;
            fixture.PublicColumn = columns.Single(column => column.ColumnName == "public_files").Id;
            fixture.PageId = SharedIds.NextId().ToString();
            var page = new PageMetadata { Id = fixture.PageId, ConfigTableId = table.Id, YzProjectId = demo.YzProjectId };
            demo.ConfigTables.Add(table); demo.ConfigColumns.AddRange(columns); demo.ConfigPages.Add(page);
            demo.ConfigPageColumns.AddRange(columns.Where(column => column.ControlKind == "FileLibrary").Select(column => new PageColumnMetadata { Id = SharedIds.NextId().ToString(), ConfigPageId = page.Id, ConfigColumnId = column.Id, ColumnName = column.ColumnName, IsEdit = 1 }));
            var platformPath = Path.Combine(fixture.directory, "platform.json"); var demoPath = Path.Combine(fixture.directory, "demo.json");
            await File.WriteAllTextAsync(platformPath, JsonSerializer.Serialize(platform, JsonDefaults.Options)); await File.WriteAllTextAsync(demoPath, JsonSerializer.Serialize(demo, JsonDefaults.Options));
            fixture.Registry = new(platformPath, demoPath);
            fixture.Sessions = new(Options("Database", "metarill_test"), Options("BusinessDatabase", "metarill_business_test"), fixture.Registry);
            foreach (var session in new[] { fixture.Sessions.Platform, fixture.Sessions.Business })
            {
                await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync();
                await using var command = connection.CreateCommand(); command.CommandText = "SELECT DATABASE()";
                if ((string?)await command.ExecuteScalarAsync() != session.Options.ExpectedDatabase) throw new InvalidOperationException("项目文件测试真实数据库目标不符。");
            }
            fixture.cleanupAllowed = true;
            var infrastructure = MetadataRegistry.Load(Path.Combine(Path.GetDirectoryName(source)!, "files.json"));
            fixture.Runtimes = new(fixture.Sessions, fixture.Registry, infrastructure, new() { RootDirectory = fixture.Root, MaxFileBytes = 4096 });
            fixture.BusinessStore = new(fixture.Sessions.Business, fixture.Registry.Demo);
            var schema = new SchemaService(fixture.Sessions.Business);
            var plan = await schema.PreviewAsync(fixture.Runtimes.ComposePhysical(fixture.Registry.Demo));
            if (plan.Changes.Count > 0) Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    /// <summary>登记所有自有文件ID后执行真实本地流上传与完成。</summary>
    public async Task<FileDescriptor> ReadyAsync(FileLibraryService service, string name, string column)
    {
        var bytes = Encoding.UTF8.GetBytes("验收内容");
        var plan = await service.PlanUploadAsync(new(name, "text/html", bytes.Length, column)); FileIds.Add(plan.FileId);
        await service.UploadAsync(plan.FileId, new MemoryStream(bytes)); return await service.CompleteAsync(plan.FileId);
    }

    /// <summary>只读取当前唯一业务记录的有效引用。</summary>
    public Task<List<Dictionary<string, object?>>> ReferencesAsync(long record) => Runtimes.Resolve(Registry.Demo.YzProjectId).Store.ReadAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = record });

    /// <summary>只删除本夹具唯一ID和唯一随机业务表，保留已经安装的固定文件基础表。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Sessions is null || !cleanupAllowed) return;
            var session = Sessions.Business;
            await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync();
            foreach (var id in FileIds)
                foreach (var (table, field) in new[] { ("yz_file_ref", "file_id"), ("yz_file_library", "id") })
                {
                    await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM " + session.Provider.QuoteIdentifier(table) + " WHERE " + session.Provider.QuoteIdentifier(field) + "=@id";
                    var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter); await command.ExecuteNonQueryAsync();
                }
            if (TableName.Length > 0)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(TableName, "^mt_project_files_[a-f0-9]{12}$")) throw new InvalidOperationException("随机业务表清理范围无效。");
                await using var drop = connection.CreateCommand(); drop.CommandText = "DROP TABLE IF EXISTS " + session.Provider.QuoteIdentifier(TableName); await drop.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            Sessions?.Dispose();
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "metarill-project-file-db")); var target = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(target) != parent || !Guid.TryParseExact(Path.GetFileName(target), "N", out _)) throw new InvalidOperationException("文件测试目录清理范围无效。");
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }
}
