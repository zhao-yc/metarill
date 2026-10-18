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
public sealed class FileLibraryController(FileLibraryService files):ControllerBase
{
    /// <summary>只列当前项目内有权限的文件，名称搜索不改变所有权边界。</summary>
    [HttpPost("list")]
    public async Task<R<object>> List(FileListRequest request,CancellationToken token)
    {
        var rows=await files.ListAsync(Id(request.FolderId),token);
        return R<object>.Success(rows.Where(r=>string.IsNullOrWhiteSpace(request.Keyword)||r.OriginalName.Contains(request.Keyword,StringComparison.OrdinalIgnoreCase)));
    }
    [HttpPost("upload-plan")]public async Task<R<FileUploadPlan>> Plan(FilePlanRequest request,CancellationToken token)=>R<FileUploadPlan>.Success(await files.PlanUploadAsync(new FileUploadRequest(request.OriginalName,request.MimeType,request.SizeBytes,request.ConfigColumnId,Id(request.FolderId),request.Sha256),token));
    /// <summary>仅接受单个名为 file 的 multipart 文件，不把上传者的路径或 MIME 当作可信信息。</summary>
    [HttpPost("local-upload/{id:long}"),RequestSizeLimit(105_906_176)]
    public async Task<R<FileDescriptor>> Upload(long id,CancellationToken token)
    {
        if(!Request.HasFormContentType)throw new ArgumentException("请使用 multipart 文件上传。");
        var form=await Request.ReadFormAsync(token);
        if(form.Files.Count!=1||form.Files[0].Name!="file")throw new ArgumentException("仅接受一个名为 file 的上传文件。");
        await using var stream=form.Files[0].OpenReadStream();
        return R<FileDescriptor>.Success(await files.UploadAsync(id,stream,token));
    }
    [HttpPost("complete")]public async Task<R<FileDescriptor>> Complete(CompleteFileRequest request,CancellationToken token)=>R<FileDescriptor>.Success(await files.CompleteAsync(request.FileId,token));
    [HttpPost("resolve")]
    public async Task<R<object>> Resolve(ResolveFilesRequest request,CancellationToken token)
    {
        if(request.FileIds.Count>100)throw new ArgumentException("单次最多解析100个文件。");
        var result=new List<FileDescriptor>();
        foreach(var id in request.FileIds.Distinct())result.Add(await files.ResolveAsync(Id(id)??throw new ArgumentException("文件标识为空。"),request.PageId,Id(request.RecordId),token));
        return R<object>.Success(result);
    }
    /// <summary>公开和私有流分开核验，Range/HEAD 由框架处理，响应禁止 MIME 嗅探与共享缓存。</summary>
    [HttpGet("protected-content/{id:long}"),HttpHead("protected-content/{id:long}")]
    public Task<IActionResult> ProtectedContent(long id,CancellationToken token)=>Content(id,false,token);
    [HttpGet("public-content/{id:long}"),HttpHead("public-content/{id:long}")]
    public Task<IActionResult> PublicContent(long id,CancellationToken token)=>Content(id,true,token);
    private async Task<IActionResult> Content(long id,bool isPublic,CancellationToken token)
    {
        var handle=await files.OpenContentAsync(id,isPublic,cancellationToken:token);
        Response.Headers["X-Content-Type-Options"]="nosniff";
        Response.Headers.CacheControl="private, no-store";
        return File(handle.Stream,handle.ContentType,handle.Name,enableRangeProcessing:true);
    }
    [HttpDelete("{id:long}")]public async Task<R<FileDeleteResult>> Delete(long id,CancellationToken token)=>R<FileDeleteResult>.Success(await files.DeleteAsync(id,token));
    [HttpPut("{id:long}/name")]public async Task<R<object>> Rename(long id,RenameFileRequest request,CancellationToken token){await files.RenameAsync(id,request.Name,token);return R<object>.Success();}
    [HttpPost("move")]public async Task<R<object>> Move(MoveFileRequest request,CancellationToken token){await files.MoveAsync(request.FileId,Id(request.TargetFolderId),token);return R<object>.Success();}
    [HttpPost("folders/list")]public async Task<R<object>> Folders(FolderListRequest request,CancellationToken token)=>R<object>.Success(await files.ListFoldersAsync(Id(request.ParentId),token));
    [HttpPost("folders")]public async Task<R<FileFolder>> CreateFolder(CreateFolderRequest request,CancellationToken token)=>R<FileFolder>.Success(await files.CreateFolderAsync(request.Name,Id(request.ParentId),token));
    [HttpPut("folders/{id:long}")]public async Task<R<object>> RenameFolder(long id,RenameFileRequest request,CancellationToken token){await files.RenameFolderAsync(id,request.Name,token);return R<object>.Success();}
    [HttpPut("folders/{id:long}/move")]public async Task<R<object>> MoveFolder(long id,MoveFolderRequest request,CancellationToken token){await files.MoveFolderAsync(id,Id(request.TargetParentId),token);return R<object>.Success();}
    [HttpDelete("folders/{id:long}")]public async Task<R<object>> DeleteFolder(long id,CancellationToken token){await files.DeleteFolderAsync(id,token);return R<object>.Success();}
    private static long? Id(string? value)=>string.IsNullOrWhiteSpace(value)?null:long.TryParse(value,out var id)&&id>0?id:throw new ArgumentException("文件或目录标识格式不正确。");
}
