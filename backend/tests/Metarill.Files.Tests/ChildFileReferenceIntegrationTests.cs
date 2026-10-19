using System.Text;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>仅Server-200真实MySQL：父子记录、上传与引用共同验证，不把范围解析单测当事务验收。</summary>
public sealed class ChildFileReferenceIntegrationTests
{
    [FilesIntegrationFact]
    public async Task Parent_child_create_update_omission_null_invalid_file_and_soft_delete_keep_one_transaction()
    {
        await using var fixture = await FilesFixture.CreateAsync(childScope: true);
        var parent = await ParentAsync(fixture);
        var file = await fixture.ReadyAsync("child.txt", Encoding.UTF8.GetBytes("子页面真实上传"));
        var value = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var observer = fixture.Observer();
        var record = fixture.Ids.NextId(); fixture.RecordIds.Add(record);
        await fixture.Session.InTransactionAsync(async () =>
        {
            // 对照生成create先锁父再插子；观察器对相同父行的当前读加入原事务。
            Assert.Single(await fixture.Store.ReadForUpdateAsync(fixture.ParentTableName, new Dictionary<string, object?> { ["id"] = parent }));
            await fixture.Store.InsertAsync(fixture.TableName, new Dictionary<string, object?>
            { ["id"] = record, ["yz_project_id"] = long.Parse(fixture.Actor().ProjectId), ["parent_id"] = parent, ["attachments"] = value });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = value }, creating: true)); return true;
        });
        Assert.Single(await fixture.ReferencesAsync(record));
        await ApplyAsync(fixture, record, new Dictionary<string, object?> { ["notes"] = "省略文件保持引用" });
        Assert.Single(await fixture.ReferencesAsync(record));
        var before = await SnapshotAsync(fixture, record);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(fixture, record, new Dictionary<string, object?>
        { ["notes"] = "非法引用不能留下业务改动", ["attachments"] = "[\"9223372036854775807\"]" }));
        Assert.Equal(before, await SnapshotAsync(fixture, record));
        await ApplyAsync(fixture, record, new Dictionary<string, object?> { ["attachments"] = null });
        Assert.Empty(await fixture.ReferencesAsync(record));
        await ApplyAsync(fixture, record, new Dictionary<string, object?> { ["attachments"] = value });
        await ApplyAsync(fixture, record, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow }, deleting: true);
        Assert.Empty(await fixture.ReferencesAsync(record));
        Assert.NotNull((await fixture.Store.ReadIncludingDeletedAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record })).Single()["delete_no"]);
        Assert.True((await fixture.Service().DeleteAsync(file.FileId)).StorageRemoved);
    }

    [FilesIntegrationFact]
    public async Task String_parent_key_and_string_parent_project_keep_full_long_identity()
    {
        await using var fixture = await FilesFixture.CreateAsync(childScope: true, parentKeysAsText: true);
        var parent = await ParentAsync(fixture);
        var file = await fixture.ReadyAsync("text-parent.txt", Encoding.UTF8.GetBytes("字符串父键与项目"));
        var record = await fixture.RecordAsync(parent: parent);
        await ApplyAsync(fixture, record, new Dictionary<string, object?> { ["attachments"] = JsonSerializer.Serialize(new[] { file.FileId.ToString() }) });
        var source = (await fixture.Store.ReadAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record })).Single();
        var owner = (await fixture.Store.ReadAsync(fixture.ParentTableName, new Dictionary<string, object?> { ["id"] = parent })).Single();
        Assert.Equal(parent.ToString(), Assert.IsType<string>(source["parent_id"]));
        Assert.Equal(fixture.Actor().ProjectId, Assert.IsType<string>(owner["yz_project_id"]));
        Assert.Single(await fixture.ReferencesAsync(record));
    }

    [FilesIntegrationFact]
    public async Task Parent_values_are_from_actual_source_and_missing_deleted_foreign_or_unknown_parent_roll_back()
    {
        await using var fixture = await FilesFixture.CreateAsync(childScope: true);
        var valid = await ParentAsync(fixture);
        var foreign = await ParentAsync(fixture, "999999");
        var deleted = await ParentAsync(fixture, deleted: true);
        var file = await fixture.ReadyAsync("parent-range.txt", Encoding.UTF8.GetBytes("真实父范围"));
        var value = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var record = await fixture.RecordAsync(parent: valid);
        await ApplyAsync(fixture, record, new Dictionary<string, object?> { ["attachments"] = value });
        foreach (var invalid in new long?[] { null, 9223372036854775807, foreign, deleted })
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["parent_id"] = invalid });
            var before = await SnapshotAsync(fixture, record);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(fixture, record, new Dictionary<string, object?> { ["notes"] = "必须回滚", ["attachments"] = value }));
            Assert.Equal(before, await SnapshotAsync(fixture, record));
        }
        await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["parent_id"] = valid });
        fixture.Registry.Platform.ConfigColumns.Single(column => column.ConfigTableId == fixture.Registry.Platform.ConfigPages.Single(page => page.Id == fixture.PageId).ConfigTableId && column.ColumnName == "parent_id").LinkConfigTableId = "unknown";
        var snapshot = await SnapshotAsync(fixture, record);
        await Assert.ThrowsAsync<NotSupportedException>(() => ApplyAsync(fixture, record, new Dictionary<string, object?> { ["notes"] = "未知定义必须回滚", ["attachments"] = value }));
        Assert.Equal(snapshot, await SnapshotAsync(fixture, record));
    }

    [FilesIntegrationFact]
    public async Task Create_hidden_empty_file_is_noop_but_nonempty_forged_existing_reference_and_update_are_rejected()
    {
        await using var fixture = await FilesFixture.CreateAsync(childScope: true);
        var parent = await ParentAsync(fixture);
        var file = await fixture.ReadyAsync("hidden-file.txt", Encoding.UTF8.GetBytes("隐藏空文件权限"));
        var value = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var referenced = await fixture.RecordAsync(parent: parent);
        await ApplyAsync(fixture, referenced, new Dictionary<string, object?> { ["attachments"] = value });
        // 隐藏后重新发布观察器；没有给该字段补IsEdit来掩盖全字段新增通知。
        fixture.Registry.Platform.ConfigPageColumns.Single(field => field.ConfigPageId == fixture.PageId && field.ConfigColumnId == fixture.ProtectedColumnId).IsEdit = 0;
        foreach (var empty in new string?[] { null, "[]" })
        {
            var record = fixture.Ids.NextId(); fixture.RecordIds.Add(record);
            await fixture.Session.InTransactionAsync(async () =>
            {
                await fixture.Store.InsertAsync(fixture.TableName, new Dictionary<string, object?>
                { ["id"] = record, ["yz_project_id"] = long.Parse(fixture.Actor().ProjectId), ["parent_id"] = parent, ["attachments"] = empty });
                await fixture.Observer().BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = empty, ["public_files"] = null }, creating: true)); return true;
            });
            Assert.Empty(await fixture.ReferencesAsync(record));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(fixture, record, new Dictionary<string, object?> { ["attachments"] = empty }));
        }
        var rejected = fixture.Ids.NextId(); fixture.RecordIds.Add(rejected);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.InsertAsync(fixture.TableName, new Dictionary<string, object?>
            { ["id"] = rejected, ["yz_project_id"] = long.Parse(fixture.Actor().ProjectId), ["parent_id"] = parent, ["attachments"] = value });
            await fixture.Observer().BeforeCommitAsync(Context(fixture, rejected, new Dictionary<string, object?> { ["attachments"] = value }, creating: true)); return true;
        }));
        Assert.Empty(await fixture.Store.ReadAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = rejected }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Observer().BeforeCommitAsync(Context(fixture, referenced, new Dictionary<string, object?> { ["attachments"] = null }, creating: true)); return true;
        }));
        // 模拟已有引用与空字段不一致；无效果分支不能偷偷清除原有效引用。
        await fixture.Store.UpdateAsync(fixture.TableName, referenced, new Dictionary<string, object?> { ["attachments"] = null });
        var before = await SnapshotAsync(fixture, referenced);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(fixture, referenced, new Dictionary<string, object?> { ["notes"] = "不得提交", ["attachments"] = null }, creating: true));
        Assert.Equal(before, await SnapshotAsync(fixture, referenced));
        // 删除仍解绑全表文件，即使文件列已不在来源页可写。
        await ApplyAsync(fixture, referenced, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow }, deleting: true);
        Assert.Empty(await fixture.ReferencesAsync(referenced));
    }

    [FilesIntegrationFact]
    public async Task Parent_soft_delete_first_blocks_child_then_current_parent_read_rejects_and_rolls_back()
    {
        await using var fixture = await FilesFixture.CreateAsync(childScope: true);
        var parent = await ParentAsync(fixture);
        var file = await fixture.ReadyAsync("parent-first.txt", Encoding.UTF8.GetBytes("先删除父事务"));
        var record = await fixture.RecordAsync(parent: parent);
        var value = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        using var other = new DatabaseSession(fixture.Session.Options);
        var otherStore = new SqlRowStore(other, fixture.Registry.Platform);
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parentDelete = other.InTransactionAsync(async () =>
        {
            await otherStore.UpdateAsync(fixture.ParentTableName, parent, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            locked.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); return true;
        });
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var before = await SnapshotAsync(fixture, record);
        var saving = ApplyAsync(fixture, record, new Dictionary<string, object?> { ["notes"] = "等待后应回滚", ["attachments"] = value });
        try { await Task.Delay(150); Assert.False(saving.IsCompleted); }
        finally { release.TrySetResult(); }
        await parentDelete.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => saving.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(before, await SnapshotAsync(fixture, record));
    }

    [FilesIntegrationFact]
    public async Task Child_holds_parent_until_commit_before_a_later_parent_soft_delete_can_finish()
    {
        await using var fixture = await FilesFixture.CreateAsync(childScope: true);
        var parent = await ParentAsync(fixture);
        var file = await fixture.ReadyAsync("child-first.txt", Encoding.UTF8.GetBytes("先保存子事务"));
        var record = await fixture.RecordAsync(parent: parent);
        var value = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = value });
            await fixture.Observer().BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = value }));
            locked.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(15)); return true;
        });
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(15));
        using var other = new DatabaseSession(fixture.Session.Options);
        var otherStore = new SqlRowStore(other, fixture.Registry.Platform);
        var deletion = other.InTransactionAsync(async () => await otherStore.UpdateAsync(fixture.ParentTableName, parent, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow }));
        try { await Task.Delay(150); Assert.False(deletion.IsCompleted); }
        finally { release.TrySetResult(); }
        await saving.WaitAsync(TimeSpan.FromSeconds(15)); await deletion.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(await fixture.ReferencesAsync(record));
        // 父在子提交后被另一个合法事务删除；后续文件提交仍重新核验，不宣称永久关系约束。
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ApplyAsync(fixture, record, new Dictionary<string, object?> { ["attachments"] = value }));
    }

    /// <summary>测试数据通过元数据存储写入，结构只由FilesFixture的SchemaService建立。</summary>
    private static async Task<long> ParentAsync(FilesFixture fixture, string? project = null, bool deleted = false)
    {
        var id = fixture.Ids.NextId();
        await fixture.Store.InsertAsync(fixture.ParentTableName, new Dictionary<string, object?>
        { ["id"] = id, ["yz_project_id"] = long.Parse(project ?? fixture.Actor().ProjectId), ["delete_no"] = deleted ? DateTime.UtcNow : null });
        return id;
    }

    /// <summary>模拟标准写入完成后的真实观察入口，所有断言复用同一数据库事务。</summary>
    private static Task<bool> ApplyAsync(FilesFixture fixture, long record, IReadOnlyDictionary<string, object?> fields, bool deleting = false, bool creating = false) =>
        fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, fields);
            await fixture.Observer().BeforeCommitAsync(Context(fixture, record, fields, deleting, creating)); return true;
        });

    /// <summary>上下文没有客户端parentId，父范围证明只能读取锁定源记录。</summary>
    private static RecordMutationContext Context(FilesFixture fixture, long record, IReadOnlyDictionary<string, object?> fields, bool deleting = false, bool creating = false) =>
        new(fixture.PageId, fixture.TableName, record, fixture.Actor().ProjectId, fields, deleting, fixture.Session.CurrentTransaction!, creating);

    /// <summary>失败前后同时比较业务行及含软删的完整引用行，不能只检查引用数量。</summary>
    private static async Task<string> SnapshotAsync(FilesFixture fixture, long record) => JsonSerializer.Serialize(new
    {
        Source = await fixture.Store.ReadIncludingDeletedAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record }),
        References = (await fixture.Store.ReadIncludingDeletedAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = record })).OrderBy(row => SqlRowStore.Id(row)).ToArray()
    });
}
