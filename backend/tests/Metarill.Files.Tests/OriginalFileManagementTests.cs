using Metarill.Api.Controllers;
using Metarill.Runtime.Files;
using System.Text;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Files.Tests;

/// <summary>对照原V8文件库真实目录与浏览规则，不把所有权当成项目共享目录或可读文件的唯一边界。</summary>
public sealed class OriginalFileManagementTests
{
    /// <summary>项目成员看到完整父子树并能整理项目目录；同级重名仍拒绝，不能借显示层级跨项目。</summary>
    [FilesIntegrationFact]
    public async Task Shared_project_tree_includes_nested_and_other_owner_folders()
    {
        await using var fixture=await FilesFixture.CreateAsync();var service=fixture.Service();var name="共享目录_"+Guid.NewGuid().ToString("N");
        var root=await service.CreateFolderAsync(name);fixture.FolderIds.Add(root.Id);
        var child=await service.CreateFolderAsync("子目录",root.Id);fixture.FolderIds.Add(child.Id);
        var other=fixture.Service(fixture.Actor(8002));var tree=await other.ListFoldersAsync();
        Assert.Contains(tree,folder=>folder.Id==root.Id);Assert.Contains(tree,folder=>folder.Id==child.Id&&folder.ParentFolderId==root.Id);
        await other.RenameFolderAsync(child.Id,"共同整理");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>other.CreateFolderAsync("共同整理",root.Id));
        await other.MoveFolderAsync(child.Id,null);
        Assert.Contains(await other.ListFoldersAsync(),folder=>folder.Id==child.Id&&folder.ParentFolderId is null);
        await other.DeleteFolderAsync(child.Id);await other.DeleteFolderAsync(root.Id);
    }
    /// <summary>原目录树用parentId，原列表用id；CLR旧构造及解析fileId继续可用。</summary>
    [Fact]
    public void Original_folder_and_file_identifier_json_is_preserved()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var folder=JsonSerializer.SerializeToElement(new FileFolder(1,"目录",2){FileCount=3},options);
        Assert.Equal(2,folder.GetProperty("parentId").GetInt64());Assert.False(folder.TryGetProperty("parentFolderId",out _));Assert.Equal(3,folder.GetProperty("fileCount").GetInt64());
        var file=JsonSerializer.SerializeToElement(new FileDescriptor(9,"文件.txt","text/plain",1,"PROTECTED","ready",null,"LOCAL_STREAM",null,null),options);
        Assert.Equal(9,file.GetProperty("id").GetInt64());Assert.Equal(9,file.GetProperty("fileId").GetInt64());Assert.False(file.TryGetProperty("storageKey",out _));
    }
    /// <summary>字段模式、跨目录分词字符搜索、同一读取规则的数量及他人可读文件整理保持原行为。</summary>
    [FilesIntegrationFact]
    public async Task Original_browse_mode_search_counts_and_readable_file_management_are_consistent()
    {
        await using var fixture=await FilesFixture.CreateAsync();var owner=fixture.Service();var other=fixture.Service(fixture.Actor(8002));
        var folder=await owner.CreateFolderAsync("可见目录_"+Guid.NewGuid().ToString("N"));fixture.FolderIds.Add(folder.Id);
        var bytes=Encoding.UTF8.GetBytes("扩展名只影响展示，不重新解释已验证MIME");var file=await fixture.ReadyAsync("初始.txt",bytes);
        var publicFile=await fixture.ReadyAsync("公开.txt",bytes,publicly:true);var record=await fixture.RecordAsync();var value=JsonSerializer.Serialize(new[]{file.FileId.ToString()});
        await fixture.Session.InTransactionAsync(async()=>{await fixture.Store.UpdateAsync(fixture.TableName,record,new Dictionary<string,object?>{{"attachments",value}});await fixture.Observer().BeforeCommitAsync(new(fixture.PageId,fixture.TableName,record,fixture.Actor().ProjectId,new Dictionary<string,object?>{{"attachments",value}},false,fixture.Session.CurrentTransaction!));return true;});
        var before=Assert.Single(await fixture.Store.ReadAsync("yz_file_library",new Dictionary<string,object?>{{"id",file.FileId}}));
        await other.MoveAsync(file.FileId,folder.Id);var renamed=await other.RenameWithResultAsync(file.FileId," 跨目录文件权限.pdf ");Assert.Equal("跨目录文件权限.pdf",renamed.OriginalName);Assert.Equal("text/plain",renamed.MimeType);
        Assert.Contains(await other.ListAccessibleAsync(fixture.ProtectedColumnId,null,folder.Id),row=>row.FileId==file.FileId);
        Assert.DoesNotContain(await other.ListAccessibleAsync(fixture.PublicColumnId,null,folder.Id),row=>row.FileId==file.FileId);
        Assert.Contains(await other.ListAccessibleAsync(fixture.PublicColumnId,null,null),row=>row.FileId==publicFile.FileId);
        Assert.Contains(await other.ListAccessibleAsync(fixture.ProtectedColumnId,"跨件 权限",null),row=>row.FileId==file.FileId);
        Assert.DoesNotContain(await other.ListAccessibleAsync(fixture.ProtectedColumnId,null,null),row=>row.FileId==file.FileId);
        Assert.Equal(1,Assert.Single(await other.ListFolderTreeAsync(fixture.ProtectedColumnId),row=>row.Id==folder.Id).FileCount);
        Assert.Equal(0,Assert.Single(await other.ListFolderTreeAsync(fixture.PublicColumnId),row=>row.Id==folder.Id).FileCount);
        await using(var stream=(await other.OpenContentAsync(file.FileId)).Stream){using var result=new MemoryStream();await stream.CopyToAsync(result);Assert.Equal(bytes,result.ToArray());}
        var content=await other.OpenContentAsync(file.FileId);Assert.Equal("text/plain",content.ContentType);await content.Stream.DisposeAsync();
        var after=Assert.Single(await fixture.Store.ReadAsync("yz_file_library",new Dictionary<string,object?>{{"id",file.FileId}}));Assert.Equal(before["storage_key"],after["storage_key"]);Assert.Equal(before["sha256"],after["sha256"]);Assert.Equal(before["content_type"],after["content_type"]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>other.DeleteAsync(file.FileId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>other.ListAccessibleAsync("不存在的文件列",null,null));
        await Assert.ThrowsAsync<ArgumentException>(()=>other.ListAccessibleAsync(null,null,null));
        var foreign=fixture.Service(fixture.Actor(project:"999999"));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>foreign.ListAccessibleAsync(fixture.ProtectedColumnId,null,null));
        // 旧坏引用只能拒权/从列表排除，不能把损坏文本当作有效引用或伪装数据库错误。
        await fixture.Store.UpdateAsync(fixture.TableName,record,new Dictionary<string,object?>{{"attachments","损坏JSON"}});
        Assert.DoesNotContain(await other.ListAccessibleAsync(fixture.ProtectedColumnId,null,folder.Id),row=>row.FileId==file.FileId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>other.ResolveAsync(file.FileId));
    }
    /// <summary>上传/完成仍认实际上传者，超级读取身份不能接管他人的上传计划。</summary>
    [FilesIntegrationFact]
    public async Task Platform_administrator_cannot_replace_another_upload_owner()
    {
        await using var fixture=await FilesFixture.CreateAsync();var bytes=Encoding.UTF8.GetBytes("不接管别人上传");var plan=await fixture.PlanAsync("owner-only.txt",bytes);
        var other=fixture.Service(fixture.Actor(8002,administrator:true));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>other.UploadAsync(plan.FileId,new MemoryStream(bytes)));
        await fixture.Service().UploadAsync(plan.FileId,new MemoryStream(bytes));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>other.CompleteAsync(plan.FileId));
        Assert.Equal("ready",(await fixture.Service().CompleteAsync(plan.FileId)).Status);
    }
    /// <summary>Windows真实文件占用时不向原前端返回删除成功；保留数据库失效与物理清理失败的明确边界。</summary>
    [WindowsFilesIntegrationFact]
    public async Task Physical_delete_failure_is_an_original_error_response_instead_of_false_success()
    {
        await using var fixture=await FilesFixture.CreateAsync();var file=await fixture.ReadyAsync("held.txt",Encoding.UTF8.GetBytes("占用删除检查"));
        var row=Assert.Single(await fixture.Store.ReadAsync("yz_file_library",new Dictionary<string,object?>{{"id",file.FileId}}));var path=FileValidation.StoragePath(fixture.Options.RootDirectory,SqlRowStore.Text(row,"storage_key"));
        await using var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None);
        var actor=new TestAuthorization(fixture.Actor(),fixture.Registry);var resolver=new SingleDatabaseFileLibraryResolver(fixture.Service(),new([fixture.Registry.Platform]),actor);
        var result=await new FileLibraryController(resolver).Delete(file.FileId,CancellationToken.None);
        Assert.Equal(500,result.Code);Assert.Null(result.Data);Assert.True(File.Exists(path));Assert.Empty(await fixture.Store.ReadAsync("yz_file_library",new Dictionary<string,object?>{{"id",file.FileId}}));
    }
    /// <summary>就绪所有者无引用不在可见列表，真实可读引用也不能因上传者是别人而被丢弃。</summary>
    [FilesIntegrationFact]
    public async Task File_list_uses_real_read_access_instead_of_only_upload_ownership()
    {
        await using var fixture=await FilesFixture.CreateAsync();var file=await fixture.ReadyAsync("可见规则.txt",Encoding.UTF8.GetBytes("真实浏览权限"));
        Assert.DoesNotContain(await fixture.Service().ListAsync(),row=>row.FileId==file.FileId);
        var record=await fixture.RecordAsync();var value=JsonSerializer.Serialize(new[]{file.FileId.ToString()});
        await fixture.Session.InTransactionAsync(async()=>
        {
            await fixture.Store.UpdateAsync(fixture.TableName,record,new Dictionary<string,object?>{{"attachments",value}});
            await fixture.Observer().BeforeCommitAsync(new(fixture.PageId,fixture.TableName,record,fixture.Actor().ProjectId,new Dictionary<string,object?>{{"attachments",value}},false,fixture.Session.CurrentTransaction!));return true;
        });
        Assert.Contains(await fixture.Service(fixture.Actor(8002)).ListAsync(),row=>row.FileId==file.FileId);
    }
}

/// <summary>只在实际Windows及真实MySQL配置齐备时执行系统文件占用验证，其他环境明确跳过。</summary>
public sealed class WindowsFilesIntegrationFactAttribute:FactAttribute
{
    public WindowsFilesIntegrationFactAttribute(){Skip=OperatingSystem.IsWindows()?new FilesIntegrationFactAttribute().Skip:"仅Windows文件共享语义，未执行真实文件占用验证。";}
}
