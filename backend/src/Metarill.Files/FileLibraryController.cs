using Metarill.Core.Contracts;
using Metarill.Runtime.Files;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

public sealed record FilePlanRequest(string OriginalName,string? MimeType,long SizeBytes,string? ConfigColumnId=null,string? FolderId=null,string? Sha256=null);
public sealed record FileListRequest(string? ConfigColumnId,string? Keyword,string? FolderId);
public sealed record ResolveFilesRequest(IReadOnlyList<string> FileIds,string? PageId=null,string? RecordId=null);
public sealed record CompleteFileRequest(long FileId);
public sealed record FolderListRequest(string? ConfigColumnId,string? ParentId=null);
public sealed record CreateFolderRequest(string Name,string? ParentId=null,string? ConfigColumnId=null);
public sealed record RenameFileRequest(string Name);
public sealed record MoveFileRequest(long FileId,string? TargetFolderId);
public sealed record MoveFolderRequest(string? TargetParentId);

/// <summary>沿用既有文件库协议；身份、状态与存储键均由文件运行时核验。</summary>
[ApiController,Route("api/admin/sys/file-library")]
public sealed class FileLibraryController(IFileLibraryResolver resolver):ControllerBase
{
    // 只有需要认证的动作才解析当前项目，公开内容允许在无登录身份时定位。
    private FileLibraryService Files => resolver.RequireCurrent();
    /// <summary>沿原字段访问级别及真实读取权浏览，搜索按原规则跨目录。</summary>
    [HttpPost("list")]
    public async Task<R<object>> List(FileListRequest request,CancellationToken token)
    {
        return R<object>.Success(await Files.ListAccessibleAsync(request.ConfigColumnId,request.Keyword,Id(request.FolderId),token));
    }
    [HttpPost("upload-plan")]public async Task<R<FileUploadPlan>> Plan(FilePlanRequest request,CancellationToken token)=>R<FileUploadPlan>.Success(await Files.PlanUploadAsync(new FileUploadRequest(request.OriginalName,request.MimeType,request.SizeBytes,request.ConfigColumnId,Id(request.FolderId),request.Sha256),token));
    /// <summary>仅接受单个名为 file 的 multipart 文件，不把上传者的路径或 MIME 当作可信信息。</summary>
    [HttpPost("local-upload/{id:long}"),RequestSizeLimit(105_906_176)]
    public async Task<R<FileDescriptor>> Upload(long id,CancellationToken token)
    {
        if(!Request.HasFormContentType)throw new ArgumentException("请使用 multipart 文件上传。");
        var form=await Request.ReadFormAsync(token);
        if(form.Files.Count!=1||form.Files[0].Name!="file")throw new ArgumentException("仅接受一个名为 file 的上传文件。");
        await using var stream=form.Files[0].OpenReadStream();
        return R<FileDescriptor>.Success(await Files.UploadAsync(id,stream,token));
    }
    [HttpPost("complete")]public async Task<R<FileDescriptor>> Complete(CompleteFileRequest request,CancellationToken token)=>R<FileDescriptor>.Success(await Files.CompleteAsync(request.FileId,token));
    [HttpPost("resolve")]
    public async Task<R<object>> Resolve(ResolveFilesRequest request,CancellationToken token)
    {
        if(request.FileIds.Count>100)throw new ArgumentException("单次最多解析100个文件。");
        var result=new List<FileDescriptor>();
        foreach(var id in request.FileIds.Distinct())result.Add(await Files.ResolveAsync(Id(id)??throw new ArgumentException("文件标识为空。"),request.PageId,Id(request.RecordId),token));
        return R<object>.Success(result);
    }
    /// <summary>公开和私有流分开核验，Range/HEAD 由框架处理，响应禁止 MIME 嗅探与共享缓存。</summary>
    [HttpGet("protected-content/{id:long}"),HttpHead("protected-content/{id:long}")]
    public Task<IActionResult> ProtectedContent(long id,CancellationToken token)=>Content(id,false,token);
    [HttpGet("public-content/{id:long}"),HttpHead("public-content/{id:long}")]
    public Task<IActionResult> PublicContent(long id,CancellationToken token)=>Content(id,true,token);
    private async Task<IActionResult> Content(long id,bool isPublic,CancellationToken token)
    {
        var handle=isPublic
            ?await resolver.OpenPublicAsync(id,Request.Query.TryGetValue("projectId",out var project)?project.ToString():null,token)
            :await Files.OpenContentAsync(id,cancellationToken:token);
        Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers.CacheControl="private, no-store";
        return File(handle.Stream,handle.ContentType,handle.Name,enableRangeProcessing:true);
    }
    [HttpDelete("{id:long}")]public async Task<R<string>> Delete(long id,CancellationToken token)
    {
        var result=await Files.DeleteAsync(id,token);
        return result.StorageRemoved?R<string>.Success("删除成功"):R<string>.Error("文件记录已失效，物理对象清理失败。");
    }
    [HttpPut("{id:long}/name")]public async Task<R<FileDescriptor>> Rename(long id,RenameFileRequest request,CancellationToken token)=>R<FileDescriptor>.Success(await Files.RenameWithResultAsync(id,request.Name,token));
    [HttpPost("move")]public async Task<R<object>> Move(MoveFileRequest request,CancellationToken token){await Files.MoveAsync(request.FileId,Id(request.TargetFolderId),token);return R<object>.Success("移动成功");}
    [HttpPost("folders/list")]public async Task<R<object>> Folders([FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] FolderListRequest? request,CancellationToken token)=>R<object>.Success(await Files.ListFolderTreeAsync(request?.ConfigColumnId,token));
    [HttpPost("folders")]public async Task<R<FileFolder>> CreateFolder(CreateFolderRequest request,CancellationToken token)=>R<FileFolder>.Success(await Files.CreateLibraryFolderAsync(request.Name,Id(request.ParentId),request.ConfigColumnId,token));
    [HttpPut("folders/{id:long}")]public async Task<R<FileFolder>> RenameFolder(long id,RenameFileRequest request,CancellationToken token)=>R<FileFolder>.Success(await Files.RenameFolderWithResultAsync(id,request.Name,token));
    [HttpPut("folders/{id:long}/move")]public async Task<R<FileFolder>> MoveFolder(long id,MoveFolderRequest request,CancellationToken token)=>R<FileFolder>.Success(await Files.MoveFolderWithResultAsync(id,Id(request.TargetParentId),token));
    [HttpDelete("folders/{id:long}")]public async Task<R<string>> DeleteFolder(long id,CancellationToken token){await Files.DeleteFolderAsync(id,token);return R<string>.Success("删除成功");}
    private static long? Id(string? value)=>string.IsNullOrWhiteSpace(value)?null:long.TryParse(value,out var id)&&id>0?id:throw new ArgumentException("文件或目录标识格式不正确。");
}
