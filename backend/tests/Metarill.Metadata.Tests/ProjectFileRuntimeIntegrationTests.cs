using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Data.Relations;
using Metarill.Runtime;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed partial class ProjectFileRuntimeIntegrationTests
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

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_cannot_remove_file_content_inside_an_outer_transaction() =>
        VerifyFileDeletionBoundaryAsync(shareDatabase: false);

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_cannot_remove_file_content_inside_an_outer_transaction() =>
        VerifyFileDeletionBoundaryAsync(shareDatabase: true);

    /// <summary>外层业务事务回滚后，独立连接和真实下载都必须能读到原文件。</summary>
    private static async Task VerifyFileDeletionBoundaryAsync(bool shareDatabase)
    {
        await using var fixture = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase);
        AssertDatabaseTopology(fixture, shareDatabase);
        var project = fixture.Registry.Demo.YzProjectId;
        var actor = new RuntimeActor(8801, "项目文件删除验收", true, project, "");
        var authorization = new TestFileAuthorization(actor);
        var service = new ProjectFileLibraryResolver(fixture.Runtimes, authorization, fixture.Ids).RequireCurrent();
        var file = await fixture.ReadyAsync(service, "transaction-delete.txt", fixture.ProtectedColumn);
        var runtime = fixture.Runtimes.Resolve(project);
        using var witnessSession = new DatabaseSession(runtime.Session.Options);
        var witness = new SqlRowStore(witnessSession, fixture.Runtimes.ComposePhysical(fixture.Registry.Demo));
        var key = new Dictionary<string, object?> { ["id"] = file.FileId };
        var before = JsonSerializer.Serialize(await witness.ReadIncludingDeletedAsync("yz_file_library", key));

        Exception? nestedFailure = null;
        var rollback = new InvalidOperationException("回滚外层业务事务");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Session.InTransactionAsync<bool>(async () =>
        {
            nestedFailure = await Record.ExceptionAsync(() => service.DeleteAsync(file.FileId));
            throw rollback;
        }));
        Assert.Same(rollback, failure);
        Assert.Equal(before, JsonSerializer.Serialize(await witness.ReadIncludingDeletedAsync("yz_file_library", key)));

        // 仅检查数据库记录无法发现外部对象已被删除，必须经真实文件服务读取。
        await using (var content = (await service.OpenContentAsync(file.FileId)).Stream)
        {
            using var reader = new StreamReader(content);
            Assert.Equal("验收内容", await reader.ReadToEndAsync());
        }
        Assert.Equal("文件对象删除必须在业务事务提交后单独执行。",
            Assert.IsType<InvalidOperationException>(nestedFailure).Message);
        Assert.True((await service.DeleteAsync(file.FileId)).StorageRemoved);
        var deleted = Assert.Single(await witness.ReadIncludingDeletedAsync("yz_file_library", key));
        Assert.NotNull(deleted["delete_no"]);
        Assert.Equal("deleted", SqlRowStore.Text(deleted, "status"));
    }

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_file_deletion_retries_after_object_cleanup_fails() =>
        VerifyFileDeletionRetryAsync(shareDatabase: false);

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_file_deletion_retries_after_object_cleanup_fails() =>
        VerifyFileDeletionRetryAsync(shareDatabase: true);

    /// <summary>数据库失效后只允许原删除权限重试对象清理，墓碑和其他对象保持不变。</summary>
    private static async Task VerifyFileDeletionRetryAsync(bool shareDatabase)
    {
        await using var fixture = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase);
        AssertDatabaseTopology(fixture, shareDatabase);
        var project = fixture.Registry.Demo.YzProjectId;
        var runtime = fixture.Runtimes.Resolve(project);
        var owner = new RuntimeActor(8801, "文件上传者", false, project, "");
        FileLibraryService Service(RuntimeActor actor) => new(runtime.Store, runtime.Metadata, fixture.Ids,
            new TestFileAuthorization(actor), runtime.Options);

        var service = Service(owner);
        var file = await fixture.ReadyAsync(service, "delete-retry.txt", fixture.ProtectedColumn);
        var untouched = await fixture.ReadyAsync(service, "untouched.txt", fixture.ProtectedColumn);
        using var witnessSession = new DatabaseSession(runtime.Session.Options);
        var witness = new SqlRowStore(witnessSession, fixture.Runtimes.ComposePhysical(fixture.Registry.Demo));
        var key = new Dictionary<string, object?> { ["id"] = file.FileId };
        var row = Assert.Single(await witness.ReadAsync("yz_file_library", key));
        var path = Path.Combine(runtime.Options.RootDirectory, SqlRowStore.Text(row, "storage_key"));
        var retained = path + ".retained";
        var original = await File.ReadAllBytesAsync(path);

        // 用同名目录让实际文件系统拒绝删除，恢复原路径后可再次清理同一个对象。
        File.Move(path, retained);
        Directory.CreateDirectory(path);
        Assert.False((await service.DeleteAsync(file.FileId)).StorageRemoved);
        var tombstone = Assert.Single(await witness.ReadIncludingDeletedAsync("yz_file_library", key));
        Assert.Equal("deleted", SqlRowStore.Text(tombstone, "status"));
        Assert.NotNull(tombstone["delete_no"]);
        Assert.Empty(await witness.ReadAsync("yz_file_library", key));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenContentAsync(file.FileId));
        Directory.Delete(path);
        File.Move(retained, path);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(new RuntimeActor(8802, "同项目其他账号", false, project, "")).DeleteAsync(file.FileId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(new RuntimeActor(8801, "外项目管理员", true, fixture.Registry.Platform.YzProjectId, "")).DeleteAsync(file.FileId));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));

        var retry = await Record.ExceptionAsync(async () => Assert.True((await service.DeleteAsync(file.FileId)).StorageRemoved));
        Assert.True(retry is null, "已失效文件无法重试原对象清理。" + retry?.GetType().Name);
        Assert.False(File.Exists(path));
        Assert.True((await service.DeleteAsync(file.FileId)).StorageRemoved);
        Assert.Equal(JsonSerializer.Serialize(tombstone),
            JsonSerializer.Serialize(Assert.Single(await witness.ReadIncludingDeletedAsync("yz_file_library", key))));
        var readerService = Service(new RuntimeActor(8803, "对照读取管理员", true, project, ""));
        await using var otherContent = (await readerService.OpenContentAsync(untouched.FileId)).Stream;
        using var reader = new StreamReader(otherContent);
        Assert.Equal("验收内容", await reader.ReadToEndAsync());
    }

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_upload_retry_preserves_the_original_object() =>
        VerifyUploadRetryAsync(shareDatabase: false);

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_upload_retry_preserves_the_original_object() =>
        VerifyUploadRetryAsync(shareDatabase: true);

    /// <summary>模拟对象已保存但上传事务回滚，同一计划只能用原字节恢复。</summary>
    private static async Task VerifyUploadRetryAsync(bool shareDatabase)
    {
        await using var fixture = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase);
        AssertDatabaseTopology(fixture, shareDatabase);
        var project = fixture.Registry.Demo.YzProjectId;
        var authorization = new TestFileAuthorization(new RuntimeActor(8801, "上传重试验收", true, project, ""));
        var service = new ProjectFileLibraryResolver(fixture.Runtimes, authorization, fixture.Ids).RequireCurrent();
        var runtime = fixture.Runtimes.Resolve(project);
        var original = Encoding.UTF8.GetBytes("原始内容");
        var changed = Encoding.UTF8.GetBytes("替代内容");
        Assert.Equal(original.Length, changed.Length);
        var plan = await service.PlanUploadAsync(new("retry.txt", null, original.Length, fixture.ProtectedColumn));
        fixture.FileIds.Add(plan.FileId);
        using var witnessSession = new DatabaseSession(runtime.Session.Options);
        var witness = new SqlRowStore(witnessSession, fixture.Runtimes.ComposePhysical(fixture.Registry.Demo));
        var key = new Dictionary<string, object?> { ["id"] = plan.FileId };
        var rollback = new InvalidOperationException("回滚上传事务");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Session.InTransactionAsync<bool>(async () =>
        {
            await service.UploadAsync(plan.FileId, new MemoryStream(original));
            throw rollback;
        }));
        Assert.Same(rollback, failure);
        var pending = Assert.Single(await witness.ReadAsync("yz_file_library", key));
        Assert.Equal("planned", SqlRowStore.Text(pending, "status"));
        var storagePath = Path.Combine(runtime.Options.RootDirectory, SqlRowStore.Text(pending, "storage_key"));
        Assert.Equal(original, await File.ReadAllBytesAsync(storagePath));

        var rejected = await Record.ExceptionAsync(() => service.UploadAsync(plan.FileId, new MemoryStream(changed)));
        var stored = await File.ReadAllBytesAsync(storagePath);
        Assert.True(original.SequenceEqual(stored), "重试覆盖了已保存对象的原始字节。");
        Assert.IsType<InvalidOperationException>(rejected);
        Assert.Equal("planned", SqlRowStore.Text(Assert.Single(await witness.ReadAsync("yz_file_library", key)), "status"));

        await service.UploadAsync(plan.FileId, new MemoryStream(original));
        await service.CompleteAsync(plan.FileId);
        Assert.Equal("ready", SqlRowStore.Text(Assert.Single(await witness.ReadAsync("yz_file_library", key)), "status"));
        Assert.Equal(storagePath, Assert.Single(Directory.EnumerateFiles(runtime.Options.RootDirectory)));
        await using (var content = (await service.OpenContentAsync(plan.FileId)).Stream)
        {
            using var reader = new StreamReader(content);
            Assert.Equal("原始内容", await reader.ReadToEndAsync());
        }

        // 原单步公开导入必须仍能在自己的事务中完成计划、写入和发布。
        var legacy = "%PDF-1.4\n测试\n%%EOF"u8.ToArray();
        var imported = await service.ImportLegacyPublicAsync("legacy.pdf", legacy.Length, new MemoryStream(legacy));
        fixture.FileIds.Add(imported.FileId);
        Assert.Equal("ready", imported.Status);
        var publicResolver = new ProjectFileLibraryResolver(fixture.Runtimes, new TestFileAuthorization(null), fixture.Ids);
        await using var publicContent = (await publicResolver.OpenPublicAsync(imported.FileId, project)).Stream;
        using var result = new MemoryStream();
        await publicContent.CopyToAsync(result);
        Assert.Equal(legacy, result.ToArray());
        Assert.Empty(Directory.EnumerateFiles(runtime.Options.RootDirectory, "*.part.*"));
    }

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_legacy_upload_removes_objects_after_rollback() =>
        VerifyLegacyUploadRollbackAsync(shareDatabase: false);

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_legacy_upload_removes_objects_after_rollback() =>
        VerifyLegacyUploadRollbackAsync(shareDatabase: true);

    /// <summary>数据库在写入对象后拒绝发布，外层回滚也必须清理对象，同时保留已提交的对照文件。</summary>
    private static async Task VerifyLegacyUploadRollbackAsync(bool shareDatabase)
    {
        await using var fixture = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase);
        AssertDatabaseTopology(fixture, shareDatabase);
        var project = fixture.Registry.Demo.YzProjectId;
        var authorization = new TestFileAuthorization(new RuntimeActor(8801, "公开上传事务验收", true, project, ""));
        var service = new ProjectFileLibraryResolver(fixture.Runtimes, authorization, fixture.Ids).RequireCurrent();
        var runtime = fixture.Runtimes.Resolve(project);
        var bytes = "%PDF-1.4\n测试\n%%EOF"u8.ToArray();
        var control = await service.ImportLegacyPublicAsync("legacy-committed.pdf", bytes.Length, new MemoryStream(bytes));
        fixture.FileIds.Add(control.FileId);
        var root = runtime.Options.RootDirectory;
        var committedPath = Assert.Single(Directory.EnumerateFiles(root));

        using var witnessSession = new DatabaseSession(runtime.Session.Options);
        var witness = new SqlRowStore(witnessSession, fixture.Runtimes.ComposePhysical(fixture.Registry.Demo));
        await using (var connection = witnessSession.Provider.CreateConnection(witnessSession.Options))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE yz_file_library ADD CONSTRAINT mt_legacy_publish_failure CHECK (name <> 'legacy-db-rejected.pdf' OR status <> 'ready')";
            await command.ExecuteNonQueryAsync();
        }

        var rejected = await Assert.ThrowsAsync<MySqlConnector.MySqlException>(() =>
            service.ImportLegacyPublicAsync("legacy-db-rejected.pdf", bytes.Length, new MemoryStream(bytes)));
        Assert.Equal(3819, rejected.Number);
        var forced = new InvalidOperationException("回滚外层公开上传事务");
        long nestedId = 0;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Session.InTransactionAsync<bool>(async () =>
        {
            var nested = await service.ImportLegacyPublicAsync("legacy-outer-rollback.pdf", bytes.Length, new MemoryStream(bytes));
            nestedId = nested.FileId;
            Assert.True(Directory.EnumerateFiles(root).Count() >= 2);
            throw forced;
        }));
        Assert.Same(forced, failure);
        Assert.True(nestedId > 0);

        var stored = Assert.Single(await witness.ReadAsync("yz_file_library",
            new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(project, System.Globalization.CultureInfo.InvariantCulture) }));
        Assert.Equal(control.FileId, SqlRowStore.Id(stored));
        Assert.Equal("ready", SqlRowStore.Text(stored, "status"));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(committedPath));
        Assert.True(Directory.EnumerateFiles(root).SequenceEqual(new[] { committedPath }), "回滚后留下无文件记录的存储对象。");
    }

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

    /// <summary>分库时关联传播、附件换绑和业务字段由同一个实际业务事务提交或回滚。</summary>
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_rolls_back_relation_propagation_and_file_rebinding_together() =>
        VerifyRelationAndFilesAsync(shareDatabase: false);

    /// <summary>同库仍只使用业务会话，关联传播及引用不经过平台事务。</summary>
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_rolls_back_relation_propagation_and_file_rebinding_together() =>
        VerifyRelationAndFilesAsync(shareDatabase: true);

    /// <summary>分库时验证数据库复选的名称顺序、重复成员和空位与附件引用共同回滚。</summary>
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_rolls_back_multiple_relation_names_and_file_rebinding_together() =>
        VerifyRelationAndFilesAsync(shareDatabase: false, multiple: true);

    /// <summary>同库仍使用独立业务会话，验证相同的多值传播与附件事务。</summary>
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_rolls_back_multiple_relation_names_and_file_rebinding_together() =>
        VerifyRelationAndFilesAsync(shareDatabase: true, multiple: true);

    private static async Task VerifyRelationAndFilesAsync(bool shareDatabase, bool multiple = false)
    {
        await using var f = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase, relation: true, multiple: multiple);
        AssertDatabaseTopology(f, shareDatabase);
        var actor = new RuntimeActor(8801, "关系及附件事务验收", true, f.Registry.Demo.YzProjectId, "");
        var project = long.Parse(actor.ProjectId);
        var auth = new TestFileAuthorization(actor);
        var service = new ProjectFileLibraryResolver(f.Runtimes, auth, f.Ids).RequireCurrent();
        var oldFile = await f.ReadyAsync(service, "old-reference.txt", f.ProtectedColumn);
        var newFile = await f.ReadyAsync(service, "new-reference.txt", f.ProtectedColumn);
        var target = f.Ids.NextId();
        var source = f.Ids.NextId();
        var targetKey = new Dictionary<string, object?> { ["id"] = target };
        var sourceKey = new Dictionary<string, object?> { ["id"] = source };
        var refKey = new Dictionary<string, object?> { ["record_id"] = source };
        var session = f.Sessions.Business;
        var observer = new ProjectFileReferenceMutationObserver(f.Runtimes, auth, f.Ids);
        var plan = RelationMaintenanceCatalog.Compile(f.Registry.Demo, f.MultipleRelation is { } array ? [array] : []);
        var propagator = new ProjectRelationMutationPropagator(session);
        var original = Attachment(oldFile.FileId);
        var replacement = Attachment(newFile.FileId);
        var sourceValues = new Dictionary<string, object?>(sourceKey)
        {
            ["yz_project_id"] = project, ["target_id"] = target, ["target_name"] = "关联旧名",
            ["notes"] = "原业务值", ["attachments"] = original
        };
        string? targetIds = null;

        await session.InTransactionAsync(async () =>
        {
            await f.BusinessStore.InsertAsync(f.RelationTable, new Dictionary<string, object?>(targetKey) { ["yz_project_id"] = project, ["name"] = "关联旧名" });
            if (multiple)
            {
                var stable = f.Ids.NextId();
                var missing = f.Ids.NextId();
                var deleted = f.Ids.NextId();
                var foreignProject = f.Ids.NextId();
                foreach (var (id, projectId, name, deletedAt) in new[]
                {
                    (stable, project, "保留成员", (DateTime?)null),
                    (deleted, project, "软删成员", new DateTime(2026, 1, 1)),
                    (foreignProject, project + 1, "异项目成员", (DateTime?)null)
                })
                    await f.BusinessStore.InsertAsync(f.RelationTable, new Dictionary<string, object?>
                    {
                        ["id"] = id, ["yz_project_id"] = projectId, ["name"] = name, ["delete_no"] = deletedAt
                    });
                targetIds = JsonSerializer.Serialize(new[] { stable, target, missing, target, deleted, foreignProject }
                    .Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                sourceValues["target_ids"] = targetIds;
                sourceValues["target_names"] = JsonSerializer.Serialize(Names("关联旧名"));
            }
            await f.BusinessStore.InsertAsync(f.TableName, sourceValues);
            await BindAsync(original, inserting: true);
            return true;
        });
        using var witnessSession = new DatabaseSession(session.Options);
        var witness = new SqlRowStore(witnessSession, f.Runtimes.ComposePhysical(f.Registry.Demo));
        await AssertStateAsync(witness, "关联旧名", "原业务值", original, oldFile.FileId);
        var before = await SnapshotAsync();
        var tail = new InvalidOperationException("关联与附件写入后故障");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InTransactionAsync<bool>(async () =>
        {
            await RenameAndRebindAsync();
            throw tail;
        }));
        Assert.Same(tail, actual);
        Assert.Equal(before, await SnapshotAsync());
        await AssertStateAsync(witness, "关联旧名", "原业务值", original, oldFile.FileId);

        await session.InTransactionAsync(async () => { await RenameAndRebindAsync(); return true; });
        await AssertStateAsync(witness, "关联新名", "新业务值", replacement, newFile.FileId);
        Assert.True((await service.DeleteAsync(oldFile.FileId)).StorageRemoved);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteAsync(newFile.FileId));

        static string Attachment(long fileId) => JsonSerializer.Serialize(new[] { fileId.ToString() });
        static string?[] Names(string name) => ["保留成员", name, null, name, null, null];

        Task BindAsync(string attachments, bool inserting = false) =>
            observer.BeforeCommitAsync(new(f.PageId, f.TableName, source, actor.ProjectId,
                new Dictionary<string, object?> { ["attachments"] = attachments }, false, session.CurrentTransaction!, inserting));

        async Task RenameAndRebindAsync()
        {
            Assert.Equal(1, await f.BusinessStore.UpdateAsync(f.RelationTable, target, new Dictionary<string, object?> { ["name"] = "关联新名" }));
            await propagator.AfterMutationAsync(plan, f.RelationTable, target, actor.ProjectId,
                new HashSet<string>(StringComparer.Ordinal) { "name" }, allFields: false, refreshSource: false);
            Assert.Equal(1, await f.BusinessStore.UpdateAsync(f.TableName, source, new Dictionary<string, object?>
            {
                ["notes"] = "新业务值", ["attachments"] = replacement
            }));
            await BindAsync(replacement);
            await AssertStateAsync(f.BusinessStore, "关联新名", "新业务值", replacement, newFile.FileId, f.Runtimes.Resolve(actor.ProjectId).Store);
        }

        async Task AssertStateAsync(SqlRowStore store, string name, string notes, string attachments, long fileId, SqlRowStore? files = null)
        {
            var related = Assert.Single(await store.ReadAsync(f.RelationTable, targetKey));
            var row = Assert.Single(await store.ReadAsync(f.TableName, sourceKey));
            var reference = Assert.Single(await (files ?? store).ReadAsync("yz_file_ref", refKey));
            Assert.Equal(name, SqlRowStore.Text(related, "name"));
            Assert.Equal(name, SqlRowStore.Text(row, "target_name"));
            Assert.Equal(notes, SqlRowStore.Text(row, "notes"));
            Assert.Equal(attachments, SqlRowStore.Text(row, "attachments"));
            Assert.Equal(target, SqlRowStore.Id(row, "target_id"));
            if (multiple)
            {
                Assert.Equal(targetIds, SqlRowStore.Text(row, "target_ids"));
                Assert.Equal(Names(name), JsonSerializer.Deserialize<string?[]>(SqlRowStore.Text(row, "target_names")));
            }
            Assert.Equal(fileId, SqlRowStore.Id(reference, "file_id"));
            Assert.Equal(project, SqlRowStore.Id(reference, "yz_project_id"));
        }

        async Task<string> SnapshotAsync() => JsonSerializer.Serialize(new
        {
            targets = (await witness.ReadIncludingDeletedAsync(f.RelationTable)).OrderBy(row => SqlRowStore.Id(row)),
            source = await witness.ReadIncludingDeletedAsync(f.TableName, sourceKey),
            refs = (await witness.ReadIncludingDeletedAsync("yz_file_ref", refKey)).OrderBy(row => SqlRowStore.Id(row))
        });
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
    public string RelationTable { get; private set; } = "";
    public RelationCacheDefinition? MultipleRelation { get; private set; }
    public IReadOnlyList<string> CascadeTables { get; private set; } = [];
    public IReadOnlyList<string> CascadePageIds { get; private set; } = [];
    public string CascadeColumnId { get; private set; } = "";
    public string PageId { get; private set; } = "";
    public string ProtectedColumn { get; private set; } = "";
    public string PublicColumn { get; private set; } = "";
    public List<long> FileIds { get; } = [];
    public List<long> RecordIds { get; } = [];

    /// <summary>先验证真实连接库名，再通过显式 SchemaService 预览、执行文件和唯一业务表计划。</summary>
    public static async Task<ProjectFileDatabaseFixture> CreateAsync(bool physicalDelete=false, bool shareDatabase=false, bool relation=false, bool multiple=false, bool sparseCascade=false)
    {
        var fixture = new ProjectFileDatabaseFixture();
        try
        {
            var leasePath = Environment.GetEnvironmentVariable(ProjectFileFreshLease.SettingsVariable);
            using var leaseDocument = string.IsNullOrWhiteSpace(leasePath) ? null : JsonDocument.Parse(await File.ReadAllTextAsync(leasePath));
            var fresh = leaseDocument is null ? null : ProjectFileFreshLease.Parse(leaseDocument.RootElement);
            if ((shareDatabase || relation || sparseCascade) && fresh is null) throw new InvalidOperationException("同库或关联组合仅允许在本轮独占租约执行。");
            if (sparseCascade && (physicalDelete || relation || multiple))
                throw new InvalidOperationException("稀疏级联使用独占业务样本。");
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
            // 库拓扑、删除方式、关联和多值用途各占一位，正式用例不能复用物理表。
            var sampleNumber = (shareDatabase ? 2 : 0) + (sparseCascade ? 9 : (physicalDelete ? 1 : 0) + (relation ? 4 : 0) + (multiple ? 8 : 0));
            fixture.TableName = "mt_project_files_" + (fresh is null ? Guid.NewGuid().ToString("N")[..12] : fresh.RunId[..11] + sampleNumber.ToString("x", System.Globalization.CultureInfo.InvariantCulture));
            var table = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.TableName, YzProjectId = demo.YzProjectId };
            ColumnMetadata Column(string name, string type, string control = "", string detail = "") => new() { Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, ColumnName = name, LogicalType = type, ColumnLength = type == "string" ? "max" : "", IsDbNotNull = name == "id" ? 1 : 0, ControlKind = control, ControlDetail = detail, YzProjectId = demo.YzProjectId };
            var columns = new[] { Column("id", "int64"), Column("yz_project_id", "int64"), Column("delete_no", "datetime"), Column("notes", "string"),
                Column("attachments", "string", "FileLibrary", "{\"accessMode\":\"PROTECTED\"}"), Column("public_files", "string", "FileLibrary", "{\"accessMode\":\"PUBLIC\"}") };
            if(physicalDelete)columns=columns.Where(column=>column.ColumnName!="delete_no").ToArray();
            var sampleTables = new List<TableMetadata> { table };
            var extraColumns = new List<ColumnMetadata>();
            if (sparseCascade)
            {
                string[] levels = ["country", "area", "place"];
                var targets = levels.Select(level => new TableMetadata
                {
                    Id = SharedIds.NextId().ToString(), TableName = fixture.TableName + "_" + level, YzProjectId = demo.YzProjectId, IsRongyu = 1
                }).ToArray();
                var pages = targets.Select(target => new PageMetadata
                {
                    Id = SharedIds.NextId().ToString(), ConfigTableId = target.Id, YzProjectId = demo.YzProjectId,
                    WhereSql = "yz_project_id=@current_yz_project_id"
                }).ToArray();
                fixture.CascadeTables = targets.Select(target => target.TableName).ToArray();
                fixture.CascadePageIds = pages.Select(target => target.Id).ToArray();
                sampleTables.AddRange(targets);
                demo.ConfigPages.AddRange(pages);
                table.IsRongyu = 0;
                var detail = new List<object>();
                for (var index = 0; index < levels.Length; index++)
                {
                    var target = targets[index];
                    foreach (var (name, type) in new[] { ("id", "int64"), ("yz_project_id", "int64"), ("delete_no", "datetime"), ("name", "string") })
                    {
                        var field = Column(name, type);
                        field.ConfigTableId = target.Id;
                        if (name == "name") field.ColumnLength = "100";
                        extraColumns.Add(field);
                    }
                    if (index == 0)
                        detail.Add(new { table = target.TableName, columnPrefix = levels[index], nameField = "name" });
                    else
                    {
                        var ancestor = targets[index - 1];
                        var parent = Column(ancestor.TableName + "_id", "int64");
                        parent.ConfigTableId = target.Id;
                        parent.LinkConfigTableId = ancestor.Id;
                        extraColumns.Add(parent);
                        detail.Add(new { table = target.TableName, columnPrefix = levels[index], parentField = parent.ColumnName, nameField = "name" });
                    }
                }
                var selection = Column("place_id", "int64", "CascadeTree", JsonSerializer.Serialize(new { sourceMode = "crossTable", levels = detail }));
                selection.LinkConfigTableId = targets[^1].Id;
                columns = [..columns, selection];
                fixture.CascadeColumnId = selection.Id;
            }
            if (relation)
            {
                fixture.RelationTable = fixture.TableName + "_relation";
                var target = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.RelationTable, YzProjectId = demo.YzProjectId, IsRongyu = 0 };
                sampleTables.Add(target);
                table.IsRongyu = 1;
                var foreign = Column("target_id", "int64");
                foreign.LinkConfigTableId = target.Id;
                var cached = Column("target_name", "string");
                cached.ColumnLength = "100";
                cached.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
                cached.Extra["columnFullName"] = JsonSerializer.SerializeToElement("target.name");
                columns = [..columns, foreign, cached];
                foreach (var (name, type) in new[] { ("id", "int64"), ("yz_project_id", "int64"), ("delete_no", "datetime"), ("name", "string") })
                {
                    var column = Column(name, type);
                    column.ConfigTableId = target.Id;
                    if (type == "string") column.ColumnLength = "100";
                    extraColumns.Add(column);
                }
                if (multiple)
                {
                    var ids = Column("target_ids", "string", "MultiCheckBoxDatabase",
                        JsonSerializer.Serialize(new { tableId = target.Id, nameField = "name" }));
                    ids.LinkConfigTableId = target.Id;
                    var names = Column("target_names", "string");
                    names.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
                    names.Extra["columnFullName"] = JsonSerializer.SerializeToElement("target_ids.name");
                    columns = [..columns, ids, names];
                    fixture.MultipleRelation = new(table.Id, ids.Id, target.Id,
                        extraColumns.Single(column => column.ColumnName == "name").Id, names.Id, true);
                }
            }
            fixture.ProtectedColumn = columns.Single(column => column.ColumnName == "attachments").Id;
            fixture.PublicColumn = columns.Single(column => column.ColumnName == "public_files").Id;
            fixture.PageId = SharedIds.NextId().ToString();
            var page = new PageMetadata { Id = fixture.PageId, ConfigTableId = table.Id, YzProjectId = demo.YzProjectId };
            demo.ConfigTables.AddRange(sampleTables); demo.ConfigColumns.AddRange(columns); demo.ConfigColumns.AddRange(extraColumns); demo.ConfigPages.Add(page);
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
                var sample = new MetadataCatalog { YzProjectId = demo.YzProjectId, ConfigTables = sampleTables, ConfigColumns = [..columns, ..extraColumns] };
                await InstallFreshAsync(new SchemaService(fixture.Sessions.Platform), infrastructure);
                await InstallFreshAsync(schema, fixture.Runtimes.ComposePhysical(sample), sampleTables.Select(item => item.TableName).ToArray());
                fixture.cleanupAllowed = true;
            }
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    /// <summary>只允许创建该模型声明的表，样本表已存在时拒绝复用。</summary>
    private static async Task InstallFreshAsync(SchemaService schema, MetadataCatalog catalog, params string[] samples)
    {
        var plan = await schema.PreviewAsync(catalog);
        var names = catalog.ConfigTables.Select(table => table.TableName).ToHashSet(StringComparer.Ordinal);
        if (plan.Changes.Any(change => change.Kind != "createTable" || !names.Contains(change.Table)) ||
            samples.Any(sample => !plan.Changes.Any(change => change.Table == sample)))
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
            foreach (var name in new[] { TableName, RelationTable }.Where(name => name.Length > 0).Concat(CascadeTables))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(TableName, "^mt_project_files_[a-f0-9]{12}$")
                    || (name != TableName && !new[] { "_relation", "_country", "_area", "_place" }.Any(suffix => name == TableName + suffix)))
                    throw new InvalidOperationException("随机业务表清理范围无效。");
                await using var drop = connection.CreateCommand(); drop.CommandText = "DROP TABLE IF EXISTS " + session.Provider.QuoteIdentifier(name); await drop.ExecuteNonQueryAsync();
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
