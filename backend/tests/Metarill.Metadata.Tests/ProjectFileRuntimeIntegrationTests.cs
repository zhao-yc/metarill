using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
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
    /// <summary>平台包装器必须在业务槽复用同一个观察器；平台连接不能借用业务删除证明。</summary>
    [ProjectFileIntegrationFact]
    public Task Physical_deletion_wrapper_keeps_proof_in_the_actual_business_transaction() =>
        VerifyPhysicalDeletionAsync(shareDatabase: false);

    /// <summary>同库下两个会话仍不能交换物理删除证明，失败与成功均验证真实行及引用。</summary>
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_physical_deletion_keeps_proof_in_the_business_transaction() =>
        VerifyPhysicalDeletionAsync(shareDatabase: true);

    private static async Task VerifyPhysicalDeletionAsync(bool shareDatabase)
    {
        await using var f = await ProjectFileDatabaseFixture.CreateAsync(physicalDelete: true, shareDatabase: shareDatabase);
        AssertDatabaseTopology(f, shareDatabase);
        var actor = new RuntimeActor(8801, "项目物理删除", true, f.Registry.Demo.YzProjectId, "");
        var authorization = new TestFileAuthorization(actor);
        var service = new ProjectFileLibraryResolver(f.Runtimes, authorization, f.Ids).RequireCurrent();
        var file = await f.ReadyAsync(service, "physical-wrapper.txt", f.ProtectedColumn);
        var record = f.Ids.NextId();
        f.RecordIds.Add(record);
        var attachment = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var values = new Dictionary<string, object?> { ["attachments"] = attachment };
        var observer = new ProjectFileReferenceMutationObserver(f.Runtimes, authorization, f.Ids);
        var session = f.Sessions.Business;
        var recordKey = new Dictionary<string, object?> { ["id"] = record };
        var referenceKey = new Dictionary<string, object?> { ["record_id"] = record };

        RecordMutationContext Context(bool deleting = false) =>
            new(f.PageId, f.TableName, record, actor.ProjectId, values, deleting, session.CurrentTransaction!);
        await session.InTransactionAsync(async () =>
        {
            await f.BusinessStore.InsertAsync(f.TableName, new Dictionary<string, object?>(values)
            {
                ["id"] = record, ["yz_project_id"] = long.Parse(actor.ProjectId)
            });
            await observer.BeforeCommitAsync(Context());
            return true;
        });
        Assert.Single(await f.ReferencesAsync(record));
        var before = await SnapshotAsync();

        // 即使物理库一致，平台事务也不能作为业务记录的删除证明。
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sessions.Platform.InTransactionAsync(async () =>
        {
            await observer.BeforePhysicalDeleteAsync(Context(true) with { Transaction = f.Sessions.Platform.CurrentTransaction! });
            return true;
        }));
        Assert.Equal(before, await SnapshotAsync());

        // 先确认删除与清引用确实发生，再注入末段故障；必须是该故障触发回滚。
        var tailFailure = new InvalidOperationException("文件物理删除末段故障");
        var actualFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InTransactionAsync<bool>(async () =>
        {
            await DeleteAndUnlinkAsync();
            Assert.Empty(await f.BusinessStore.ReadAsync(f.TableName, recordKey));
            Assert.Empty(await f.ReferencesAsync(record));
            throw tailFailure;
        }));
        Assert.Same(tailFailure, actualFailure);
        Assert.Equal(before, await SnapshotAsync());

        await session.InTransactionAsync(async () => { await DeleteAndUnlinkAsync(); return true; });
        Assert.Empty(await f.BusinessStore.ReadAsync(f.TableName, recordKey));
        Assert.Empty(await f.ReferencesAsync(record));
        Assert.True((await service.DeleteAsync(file.FileId)).StorageRemoved);

        async Task DeleteAndUnlinkAsync()
        {
            await observer.BeforePhysicalDeleteAsync(Context(true));
            await using var command = session.CurrentTransaction!.Connection!.CreateCommand();
            command.Transaction = session.CurrentTransaction;
            command.CommandText = "DELETE FROM " + session.Provider.QuoteIdentifier(f.TableName) + " WHERE id=@id";
            var id = command.CreateParameter();
            id.ParameterName = "id";
            id.Value = record;
            command.Parameters.Add(id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            await observer.BeforeCommitAsync(Context(true));
        }

        async Task<string> SnapshotAsync() => JsonSerializer.Serialize(new
        {
            source = await f.BusinessStore.ReadIncludingDeletedAsync(f.TableName, recordKey),
            refs = await f.Runtimes.Resolve(actor.ProjectId).Store.ReadIncludingDeletedAsync("yz_file_ref", referenceKey)
        });
    }

    [ProjectFileIntegrationFact]
    public Task Business_files_use_the_generated_transaction_and_public_routing_stays_in_the_selected_slot() =>
        VerifyBusinessFilesAsync(shareDatabase: false);

    /// <summary>同库也执行完整的受保护附件回滚、逻辑删除和公开文件路由合同。</summary>
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_preserves_file_ownership_and_transaction_isolation() =>
        VerifyBusinessFilesAsync(shareDatabase: true);

    private static async Task VerifyBusinessFilesAsync(bool shareDatabase)
    {
        await using var fixture = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase);
        AssertDatabaseTopology(fixture, shareDatabase);
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
        var referencesBefore = JsonSerializer.Serialize(await fixture.ReferencesAsync(record));
        Assert.Single(await fixture.ReferencesAsync(record));
        // 文件引用拒绝属于原业务规则，宿主会保留中文提示而非当作内部异常。
        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteAsync(file.FileId));
        Assert.Equal("文件仍被业务记录引用，不能删除。", blocked.Message);
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
        Assert.Equal(referencesBefore, JsonSerializer.Serialize(await fixture.ReferencesAsync(record)));
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
        var platformProject = fixture.Registry.Platform.YzProjectId;
        var platformRows = await fixture.Runtimes.Resolve(platformProject).Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = publicly.FileId });
        if (shareDatabase) Assert.Equal(actor.ProjectId, SqlRowStore.Text(Assert.Single(platformRows), "yz_project_id"));
        else Assert.Empty(platformRows);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => publicResolver.OpenPublicAsync(publicly.FileId, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => publicResolver.OpenPublicAsync(publicly.FileId, platformProject));
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

    /// <summary>即使数据库名相同，平台与业务的事务会话也必须保持独立。</summary>
    private static void AssertDatabaseTopology(ProjectFileDatabaseFixture fixture, bool shareDatabase)
    {
        var platform = fixture.Sessions.Platform;
        var business = fixture.Sessions.Business;
        Assert.NotSame(platform, business);
        Assert.Equal(shareDatabase, platform.Options.ExpectedDatabase == business.Options.ExpectedDatabase);
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
    public static async Task<ProjectFileDatabaseFixture> CreateAsync(bool physicalDelete=false, bool shareDatabase=false)
    {
        var fixture = new ProjectFileDatabaseFixture();
        try
        {
            var leasePath = Environment.GetEnvironmentVariable(ProjectFileFreshLease.SettingsVariable);
            using var leaseDocument = string.IsNullOrWhiteSpace(leasePath) ? null : JsonDocument.Parse(await File.ReadAllTextAsync(leasePath));
            var fresh = leaseDocument is null ? null : ProjectFileFreshLease.Parse(leaseDocument.RootElement);
            if (shareDatabase && fresh is null) throw new InvalidOperationException("同库组合仅允许在本轮独占租约执行。");
            Directory.CreateDirectory(fixture.directory);
            using var settings = fresh is null ? JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!)) : null;
            DatabaseOptions Options(string key, string expected)
            {
                var value = settings!.RootElement.GetProperty("Metarill").GetProperty(key).Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                if (value.Provider != "mysql" || value.ExpectedDatabase != expected) throw new InvalidOperationException("项目文件验收只允许明确的 MySQL 隔离测试库。");
                return value;
            }
            var source = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
            var platform = MetadataRegistry.Load(source); var demo = MetadataRegistry.Load(Path.Combine(Path.GetDirectoryName(source)!, "demo.json"));
            // 库拓扑和删除方式各占一位，四组正式用例不能复用物理表。
            var sampleNumber = (shareDatabase ? 2 : 0) + (physicalDelete ? 1 : 0);
            fixture.TableName = "mt_project_files_" + (fresh is null ? Guid.NewGuid().ToString("N")[..12] : fresh.RunId[..11] + sampleNumber);
            var table = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.TableName, YzProjectId = demo.YzProjectId };
            ColumnMetadata Column(string name, string type, string control = "", string detail = "") => new() { Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, ColumnName = name, LogicalType = type, ColumnLength = type == "string" ? "max" : "", IsDbNotNull = name == "id" ? 1 : 0, ControlKind = control, ControlDetail = detail, YzProjectId = demo.YzProjectId };
            var columns = new[] { Column("id", "int64"), Column("yz_project_id", "int64"), Column("delete_no", "datetime"), Column("notes", "string"),
                Column("attachments", "string", "FileLibrary", "{\"accessMode\":\"PROTECTED\"}"), Column("public_files", "string", "FileLibrary", "{\"accessMode\":\"PUBLIC\"}") };
            if(physicalDelete)columns=columns.Where(column=>column.ColumnName!="delete_no").ToArray();
            fixture.ProtectedColumn = columns.Single(column => column.ColumnName == "attachments").Id;
            fixture.PublicColumn = columns.Single(column => column.ColumnName == "public_files").Id;
            fixture.PageId = SharedIds.NextId().ToString();
            var page = new PageMetadata { Id = fixture.PageId, ConfigTableId = table.Id, YzProjectId = demo.YzProjectId };
            demo.ConfigTables.Add(table); demo.ConfigColumns.AddRange(columns); demo.ConfigPages.Add(page);
            demo.ConfigPageColumns.AddRange(columns.Where(column => column.ControlKind == "FileLibrary").Select(column => new PageColumnMetadata { Id = SharedIds.NextId().ToString(), ConfigPageId = page.Id, ConfigColumnId = column.Id, ColumnName = column.ColumnName, IsEdit = 1 }));
            var platformPath = Path.Combine(fixture.directory, "platform.json"); var demoPath = Path.Combine(fixture.directory, "demo.json");
            await File.WriteAllTextAsync(platformPath, JsonSerializer.Serialize(platform, JsonDefaults.Options)); await File.WriteAllTextAsync(demoPath, JsonSerializer.Serialize(demo, JsonDefaults.Options));
            fixture.Registry = new(platformPath, demoPath);
            fixture.Sessions = new(fresh?.Platform ?? Options("Database", "metarill_test"),
                (shareDatabase ? fresh?.Platform : fresh?.Business) ?? Options("BusinessDatabase", "metarill_business_test"), fixture.Registry);
            foreach (var session in new[] { fixture.Sessions.Platform, fixture.Sessions.Business })
            {
                await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync();
                await using var command = connection.CreateCommand(); command.CommandText = "SELECT DATABASE()";
                if ((string?)await command.ExecuteScalarAsync() != session.Options.ExpectedDatabase) throw new InvalidOperationException("项目文件测试真实数据库目标不符。");
                if (fresh is not null)
                {
                    command.CommandText = "SELECT @@hostname,@@server_uuid";
                    await using var identity = await command.ExecuteReaderAsync();
                    if (!await identity.ReadAsync() || identity.GetString(0) != fresh.Hostname || identity.GetString(1) != fresh.ServerUuid)
                        throw new InvalidOperationException("跨库文件验收实际 MySQL 实例不符。");
                }
                else if(physicalDelete){using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));command.CommandText="SELECT @@hostname,@@server_uuid";await using var identity=await command.ExecuteReaderAsync();if(!await identity.ReadAsync()||identity.GetString(0)!=pin.RootElement.GetProperty("expectedHostname").GetString()||identity.GetString(1)!=pin.RootElement.GetProperty("expectedServerUuid").GetString())throw new InvalidOperationException("跨库物理删除实例pin不匹配。");}
            }
            fixture.cleanupAllowed = fresh is null;
            var infrastructure = MetadataRegistry.Load(Path.Combine(Path.GetDirectoryName(source)!, "files.json"));
            fixture.Runtimes = new(fixture.Sessions, fixture.Registry, infrastructure, new() { RootDirectory = fixture.Root, MaxFileBytes = 4096 });
            fixture.BusinessStore = new(fixture.Sessions.Business, fixture.Registry.Demo);
            var schema = new SchemaService(fixture.Sessions.Business);
            if (fresh is null)
            {
                var plan = await schema.PreviewAsync(fixture.Runtimes.ComposePhysical(fixture.Registry.Demo));
                if(physicalDelete&&plan.Changes.Any(change=>change.Kind!="createTable"||change.Table!=fixture.TableName))throw new InvalidOperationException("物理删除样本仅允许创建本次随机业务表。");
                if (plan.Changes.Count > 0) Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            }
            else
            {
                // 独占库只安装文件基础表及本次样本，不为夹具创建未使用的示例业务表。
                var sample = new MetadataCatalog { YzProjectId = demo.YzProjectId, ConfigTables = [table], ConfigColumns = [..columns] };
                await InstallFreshAsync(new SchemaService(fixture.Sessions.Platform), infrastructure);
                await InstallFreshAsync(schema, fixture.Runtimes.ComposePhysical(sample), fixture.TableName);
                fixture.cleanupAllowed = true;
            }
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    /// <summary>只允许创建该模型声明的表，样本表已存在时拒绝复用。</summary>
    private static async Task InstallFreshAsync(SchemaService schema, MetadataCatalog catalog, string? sample = null)
    {
        var plan = await schema.PreviewAsync(catalog);
        var names = catalog.ConfigTables.Select(table => table.TableName).ToHashSet(StringComparer.Ordinal);
        if (plan.Changes.Any(change => change.Kind != "createTable" || !names.Contains(change.Table)) ||
            (sample is not null && !plan.Changes.Any(change => change.Table == sample)))
            throw new InvalidOperationException("跨库文件验收仅允许创建本轮模型表。");
        if (plan.Changes.Count > 0) Assert.True((await schema.ApplyAsync(plan)).Succeeded);
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
