using System.Text;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

public sealed class FileLibraryIntegrationTests
{
    [FilesIntegrationFact]
    public async Task Local_upload_complete_private_public_folder_and_delete_lifecycle()
    {
        await using var fixture = await FilesFixture.CreateAsync();
        var service = fixture.Service();
        var folder = await service.CreateFolderAsync("仅本次测试目录"); fixture.FolderIds.Add(folder.Id);
        Assert.Contains(await service.ListFoldersAsync(), f => f.Id == folder.Id);
        var bytes = Encoding.UTF8.GetBytes("真实本地文件库测试");
        var plan = await fixture.PlanAsync("测试资料.txt", bytes, folder: folder.Id);
        Assert.StartsWith("/admin/sys/file-library/local-upload/", plan.Plan.UploadUrl);
        Assert.Equal("local", plan.Plan.ProviderCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(plan.FileId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveAsync(plan.FileId));
        var uploaded = await service.UploadAsync(plan.FileId, new MemoryStream(bytes));
        Assert.Equal("uploaded", uploaded.Status); Assert.Null(uploaded.Url);
        var ready = await service.CompleteAsync(plan.FileId);
        Assert.Equal("ready", ready.Status); Assert.Equal("text/plain", ready.MimeType);
        Assert.StartsWith("/admin/sys/file-library/protected-content/", ready.Url);
        Assert.DoesNotContain(".blob", JsonSerializer.Serialize(ready));
        Assert.Equal(ready.Sha256, (await service.CompleteAsync(plan.FileId)).Sha256);
        var privateFile = await service.OpenContentAsync(plan.FileId);
        await using (privateFile.Stream)
        {
            Assert.True(privateFile.Stream.CanSeek); privateFile.Stream.Position = 3;
            Assert.Equal(bytes[3], privateFile.Stream.ReadByte());
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service(fixture.Actor(8002)).OpenContentAsync(plan.FileId, false, fixture.PageId, 123));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service(fixture.Actor(8002, true, "999999")).ResolveAsync(plan.FileId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenContentAsync(plan.FileId, true));
        Assert.Contains(await service.ListAsync(folder.Id), f => f.FileId == plan.FileId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteFolderAsync(folder.Id));
        Assert.True((await service.DeleteAsync(plan.FileId)).StorageRemoved);
        await service.DeleteFolderAsync(folder.Id);
        Assert.DoesNotContain(await service.ListFoldersAsync(), f => f.Id == folder.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenContentAsync(plan.FileId));
        var publicFile = await fixture.ReadyAsync("公开资料.txt", bytes, publicly: true);
        Assert.StartsWith("/admin/sys/file-library/public-content/", publicFile.Url);
        var handle = await fixture.Service(fixture.Actor(8002, false, "999999")).OpenContentAsync(publicFile.FileId, true);
        await using (handle.Stream) { using var reader = new StreamReader(handle.Stream); Assert.Equal(Encoding.UTF8.GetString(bytes), await reader.ReadToEndAsync()); }
    }

    [FilesIntegrationFact]
    public async Task Upload_size_extension_digest_and_completed_object_are_verified()
    {
        await using var fixture = await FilesFixture.CreateAsync(); var service = fixture.Service();
        var bytes = Encoding.UTF8.GetBytes("abc");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanUploadAsync(new("../outside.txt", null, 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanUploadAsync(new("active.html", "text/plain", 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlanUploadAsync(new("huge.txt", null, fixture.Options.MaxFileBytes + 1)));
        var plan = await fixture.PlanAsync("size.txt", bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(plan.FileId, new MemoryStream(Encoding.UTF8.GetBytes("abcd"))));
        var row = (await fixture.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = plan.FileId })).Single();
        Assert.Equal("planned", SqlRowStore.Text(row, "status"));
        Assert.Empty(Directory.GetFiles(fixture.Options.RootDirectory));
        await service.UploadAsync(plan.FileId, new MemoryStream(bytes));
        var path = FileValidation.StoragePath(fixture.Options.RootDirectory, SqlRowStore.Text(row, "storage_key"));
        await File.WriteAllTextAsync(path, "bad");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(plan.FileId));
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal("ready", (await service.CompleteAsync(plan.FileId)).Status);
        var wrong = await fixture.PlanAsync("digest.txt", bytes, sha256: new string('0', 64));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(wrong.FileId, new MemoryStream(bytes)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service(fixture.Actor(8002)).UploadAsync(wrong.FileId, new MemoryStream(bytes)));
    }

    [FilesIntegrationFact]
    public async Task Reference_changes_join_actual_business_transaction_and_honor_omission_and_null()
    {
        await using var fixture = await FilesFixture.CreateAsync(); var service = fixture.Service();
        var file = await fixture.ReadyAsync("reference.txt", Encoding.UTF8.GetBytes("文件引用"));
        var record = await fixture.RecordAsync(); var observer = fixture.Observer();
        var json = JsonSerializer.Serialize(new[] { new { fileId = file.FileId.ToString() } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.InTransactionAsync<int>(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = json });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json }));
            throw new InvalidOperationException("验证业务与引用同时回滚");
        }));
        Assert.Empty(await fixture.ReferencesAsync(record));
        Assert.Null((await fixture.Store.ReadAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record })).Single()["attachments"]);
        await fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = json });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json })); return true;
        });
        Assert.Single(await fixture.ReferencesAsync(record));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(file.FileId));
        await fixture.Session.InTransactionAsync(async () =>
        {
            const string unrelated = "这不是文件数组，也不应被解析";
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["notes"] = unrelated });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["notes"] = unrelated })); return true;
        });
        Assert.Single(await fixture.ReferencesAsync(record));
        await fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = null });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = null })); return true;
        });
        Assert.Empty(await fixture.ReferencesAsync(record));
        Assert.True((await service.DeleteAsync(file.FileId)).StorageRemoved);
    }

    [FilesIntegrationFact]
    public async Task References_reject_cross_owner_project_scope_readonly_fields_and_forged_values()
    {
        await using var fixture = await FilesFixture.CreateAsync(); var observer = fixture.Observer();
        var file = await fixture.ReadyAsync("other-owner.txt", Encoding.UTF8.GetBytes("不可盗用"), fixture.Actor(8002));
        var record = await fixture.RecordAsync(); var json = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = json });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json })); return true;
        }));
        Assert.Empty(await fixture.ReferencesAsync(record));
        var otherProject = await fixture.RecordAsync("999999");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await observer.BeforeCommitAsync(Context(fixture, otherProject, new Dictionary<string, object?> { ["attachments"] = null })); return true;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json })); return true;
        }));
        fixture.Registry.Platform.ConfigPageColumns.Single(f => f.ConfigColumnId == fixture.ProtectedColumnId).IsEdit = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = null })); return true;
        }));
        fixture.Registry.Platform.ConfigPageColumns.Single(f => f.ConfigColumnId == fixture.ProtectedColumnId).IsEdit = 1;
        fixture.Registry.Platform.ConfigPages.Single(p => p.Id == fixture.PageId).WhereSql = "parent_id=@parent_id";
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = null })); return true;
        }));
    }

    [FilesIntegrationFact]
    public async Task Record_delete_clears_only_registered_file_references_in_same_transaction()
    {
        await using var fixture = await FilesFixture.CreateAsync(); var observer = fixture.Observer();
        var file = await fixture.ReadyAsync("delete-record.txt", Encoding.UTF8.GetBytes("删除记录后解绑"));
        var record = await fixture.RecordAsync(); var json = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        await fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["attachments"] = json });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json })); return true;
        });
        await fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.UpdateAsync(fixture.TableName, record, new Dictionary<string, object?> { ["delete_no"] = DateTime.UtcNow });
            await observer.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?>(), true)); return true;
        });
        Assert.Empty(await fixture.ReferencesAsync(record)); Assert.True((await fixture.Service().DeleteAsync(file.FileId)).StorageRemoved);
    }

    [FilesIntegrationFact]
    public async Task Create_permission_is_distinct_from_update_and_delete_race_cannot_orphan_reference()
    {
        await using var fixture = await FilesFixture.CreateAsync();
        var file = await fixture.ReadyAsync("create-only.txt", Encoding.UTF8.GetBytes("新增权限与引用竞争"));
        var json = JsonSerializer.Serialize(new[] { file.FileId.ToString() });
        var createOnly = new FileReferenceMutationObserver(fixture.Store, fixture.Registry, new TestAuthorization(fixture.Actor(), fixture.Registry, new HashSet<string> { "create" }), fixture.Ids, fixture.Options);
        var record = fixture.Ids.NextId(); fixture.RecordIds.Add(record);
        await fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Store.InsertAsync(fixture.TableName, new Dictionary<string, object?> { ["id"] = record, ["yz_project_id"] = long.Parse(fixture.Actor().ProjectId), ["attachments"] = json });
            await createOnly.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json }) with { IsCreate = true }); return true;
        });
        Assert.Single(await fixture.ReferencesAsync(record));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Session.InTransactionAsync(async () =>
        {
            await createOnly.BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json })); return true;
        }));
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var editing = fixture.Session.InTransactionAsync(async () =>
        {
            await fixture.Observer().BeforeCommitAsync(Context(fixture, record, new Dictionary<string, object?> { ["attachments"] = json }));
            locked.SetResult(); await release.Task; return true;
        });
        await locked.Task;
        var deletion = fixture.Service().DeleteAsync(file.FileId);
        release.SetResult();
        await editing;
        await Assert.ThrowsAsync<InvalidOperationException>(() => deletion);
        Assert.Single(await fixture.ReferencesAsync(record));
        Assert.Equal("ready", (await fixture.Service().ResolveAsync(file.FileId)).Status);
    }
    private static RecordMutationContext Context(FilesFixture fixture, long record, IReadOnlyDictionary<string, object?> fields, bool deleting = false) =>
        new(fixture.PageId, fixture.TableName, record, fixture.Registry.Platform.YzProjectId, fields, deleting, fixture.Session.CurrentTransaction!);
}
