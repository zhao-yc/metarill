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

    [Fact]
    public async Task Storage_roots_reject_a_link_in_parent_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "metarill-files-parent-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(directory, "target"); var link = Path.Combine(directory, "linked");
        Directory.CreateDirectory(target);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Windows 目录联接不依赖开发者模式，路径仅经环境变量传递，不拼入脚本。
                var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:METARILL_LINK_PATH -Value $env:METARILL_LINK_TARGET | Out-Null" }) start.ArgumentList.Add(argument);
                start.Environment["METARILL_LINK_PATH"] = link; start.Environment["METARILL_LINK_TARGET"] = target;
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(); await output; await error;
                Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(link, target);
            Assert.Throws<InvalidOperationException>(() => FileValidation.StoragePath(Path.Combine(link, "project"), new string('a', 32) + ".blob"));
            Assert.False(Directory.Exists(Path.Combine(target, "project")));
        }
        finally
        {
            // 先删除联接本身，再清理本次唯一的临时目录；不递归进入联接目标。
            if (Directory.Exists(link)) Directory.Delete(link, false);
            var absolute = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(absolute) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(absolute), "^metarill-files-parent-[a-f0-9]{32}$")) throw new InvalidOperationException("文件父目录测试清理范围无效。");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }
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

[Collection(FilesIntegrationCollection.Name)]
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

    [FilesIntegrationFact]
    public async Task Public_routing_checks_the_published_project_and_actual_file_row()
    {
        await using var fixture = await FilesFixture.CreateAsync();
        var published = new FileMetadataContext([fixture.Registry.Platform]);
        var authorization = new TestAuthorization(fixture.Actor(), fixture.Registry);
        var files = new FileLibraryService(fixture.Store, published, fixture.Ids, authorization,
            fixture.Options with { PublicContentProjectId = fixture.Actor().ProjectId });
        var resolver = new SingleDatabaseFileLibraryResolver(files, published, authorization);
        Assert.Same(files, resolver.RequireCurrent());
        var file = await fixture.ReadyAsync("public-routing.txt", System.Text.Encoding.UTF8.GetBytes("公开路由归属"), publicly: true);
        Assert.EndsWith("?projectId=" + fixture.Actor().ProjectId, (await files.ResolveAsync(file.FileId)).Url);
        var handle = await resolver.OpenPublicAsync(file.FileId, null); await handle.Stream.DisposeAsync();
        handle = await resolver.OpenPublicAsync(file.FileId, fixture.Actor().ProjectId); await handle.Stream.DisposeAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.OpenPublicAsync(file.FileId, "999999"));
        await fixture.Store.UpdateAsync("yz_file_library", file.FileId, new Dictionary<string, object?> { ["yz_project_id"] = 999999L });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.OpenPublicAsync(file.FileId, null));
        var otherActor = new SingleDatabaseFileLibraryResolver(files, published, new TestAuthorization(fixture.Actor(8001, true, "999999"), fixture.Registry));
        Assert.Throws<UnauthorizedAccessException>(() => otherActor.RequireCurrent());
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
