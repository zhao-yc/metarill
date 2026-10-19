using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

public sealed class SharedFileModuleTests
{
    [Fact]
    public void Published_metadata_is_a_source_independent_snapshot()
    {
        var source = Catalog("1", "10");
        var context = new FileMetadataContext([source]);
        source.ConfigPages[0].IsSave = 0;
        source.ConfigTables.Clear();
        Assert.Equal(1, context.Catalogs[0].ConfigPages[0].IsSave);
        Assert.Single(context.Catalogs[0].ConfigTables);
    }

    [Fact]
    public void Published_metadata_rejects_empty_duplicate_or_invalid_page_ownership()
    {
        Assert.Throws<InvalidOperationException>(() => new FileMetadataContext([]));
        Assert.Throws<InvalidOperationException>(() => new FileMetadataContext([Catalog("1", "10"), Catalog("1", "11")]));
        Assert.Throws<InvalidOperationException>(() => new FileMetadataContext([Catalog("1", "10"), Catalog("2", "10")]));
        var invalid = Catalog("1", "10"); invalid.ConfigPages[0].ConfigTableId = "unknown";
        Assert.Throws<InvalidOperationException>(() => new FileMetadataContext([invalid]));
    }

    /// <summary>构造最小合法发布模型，测试不依赖平台磁盘文件或数据库。</summary>
    private static MetadataCatalog Catalog(string project, string page) => new()
    {
        YzProjectId = project,
        ConfigTables = [new() { Id = "20", TableName = "file_test", YzProjectId = project }],
        ConfigColumns = [new() { Id = "30", ConfigTableId = "20", ColumnName = "id", LogicalType = "int64" }],
        ConfigPages = [new() { Id = page, ConfigTableId = "20", YzProjectId = project }]
    };
}

public sealed class SharedFileModuleIntegrationTests
{
    [FilesIntegrationFact]
    public async Task Folder_moves_use_only_file_tables_and_serialize_two_database_connections()
    {
        await using var fixture = await FilesFixture.CreateAsync();
        var metadata = fixture.Registry.Platform;
        var names = new HashSet<string> { "yz_file_library", "yz_file_library_folder", "yz_file_ref" };
        var tables = metadata.ConfigTables.Where(table => names.Contains(table.TableName)).ToList();
        var tableIds = tables.Select(table => table.Id).ToHashSet();
        var infrastructure = new MetadataCatalog { YzProjectId = metadata.YzProjectId, ConfigTables = tables,
            ConfigColumns = metadata.ConfigColumns.Where(column => tableIds.Contains(column.ConfigTableId)).ToList() };
        using var otherSession = new DatabaseSession(fixture.Session.Options);
        var first = Service(fixture.Session); var second = Service(otherSession);
        FileLibraryService Service(DatabaseSession session) => new(new SqlRowStore(session, infrastructure),
            new([metadata, fixture.Registry.Demo]), fixture.Ids, new TestAuthorization(fixture.Actor(), fixture.Registry), fixture.Options);
        var a = await first.CreateFolderAsync("并发目录甲"); fixture.FolderIds.Add(a.Id);
        var b = await first.CreateFolderAsync("并发目录乙"); fixture.FolderIds.Add(b.Id);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // 第一条连接先持有目录事务，第二条连接尝试反向移动，最终只能一方成功。
        var forward = fixture.Session.InTransactionAsync(async () =>
        {
            await first.MoveFolderAsync(a.Id, b.Id);
            locked.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); return true;
        });
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var backward = second.MoveFolderAsync(b.Id, a.Id);
        try { await Task.Delay(150); Assert.False(backward.IsCompleted); }
        finally { release.TrySetResult(); }
        await forward.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<InvalidOperationException>(() => backward.WaitAsync(TimeSpan.FromSeconds(15)));
        var rows = await fixture.Store.ReadAsync("yz_file_library_folder", new Dictionary<string, object?> { ["id"] = a.Id });
        Assert.Equal(b.Id, SqlRowStore.Id(rows.Single(), "parent_folder_id"));
        rows = await fixture.Store.ReadAsync("yz_file_library_folder", new Dictionary<string, object?> { ["id"] = b.Id });
        Assert.Null(rows.Single()["parent_folder_id"]);
    }

    [FilesIntegrationFact]
    public async Task Upload_permission_can_fall_back_to_update_and_rejects_unreachable_server_size()
    {
        await using var fixture = await FilesFixture.CreateAsync();
        var authorization = new UpdateOnlyAuthorization(fixture.Actor());
        var service = new FileLibraryService(fixture.Store, new([fixture.Registry.Platform, fixture.Registry.Demo]), fixture.Ids, authorization, fixture.Options);
        var plan = await service.PlanUploadAsync(new("update-only.txt", null, 0, fixture.ProtectedColumnId)); fixture.FileIds.Add(plan.FileId);
        Assert.Equal(new[] { "create", "update" }, authorization.Operations);
        var oversized = new FileLibraryService(fixture.Store, new([fixture.Registry.Platform, fixture.Registry.Demo]), fixture.Ids,
            new TestAuthorization(fixture.Actor(), fixture.Registry), fixture.Options with { MaxFileBytes = 100L * 1024 * 1024 + 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => oversized.PlanUploadAsync(new("too-large-policy.txt", null, 1)));
    }

    /// <summary>复现独立宿主使用 SecurityException 拒绝新增但允许修改的授权实现。</summary>
    private sealed class UpdateOnlyAuthorization(RuntimeActor actor) : IRuntimeAuthorization
    {
        public List<string> Operations { get; } = [];
        public RuntimeActor RequireActor() => actor;
        public void RequirePagePermission(string pageId, string operation)
        {
            Operations.Add(operation);
            if (operation != "update") throw new System.Security.SecurityException("测试账号只允许修改。");
        }
    }
}
