using System.IO.Compression;
using Metarill.Core.Contracts;
using Metarill.Data.Schema;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Files;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;
public sealed record SchemaPreviewRequest(string ProjectId,string? TableId);
public sealed record SchemaApplyRequest(string ProjectId,SchemaChangePlan Plan);
public sealed record GenerateRequest(string ProjectId,string? RootNamespace);
[ApiController,Route("api/admin/sys/database")]
public sealed class DatabaseController(MetadataRepository metadata,HttpRuntimeAuthorization authorization,ProjectSchemaService schemas,ProjectPhysicalModels physicalModels):ControllerBase
{
    private SchemaService Resolve(string project)=>schemas.Resolve(project);
    [HttpPost("preview")]public async Task<R<SchemaChangePlan>> Preview(SchemaPreviewRequest request,CancellationToken cancellationToken)
    {
        authorization.RequireAdministrator();var catalog=await metadata.LoadProjectAsync(request.ProjectId,cancellationToken);
        if(request.TableId is{}table)
        {
            catalog.ConfigTables=catalog.ConfigTables.Where(t=>t.Id==table).ToList();var ids=catalog.ConfigTables.Select(t=>t.Id).ToHashSet();
            catalog.ConfigColumns=catalog.ConfigColumns.Where(c=>ids.Contains(c.ConfigTableId)).ToList();catalog.ConfigPages=[];catalog.ConfigPageColumns=[];
            catalog.ConfigIndexes=catalog.ConfigIndexes.Where(i=>i.TryGetProperty("configTableId",out var id)&&ids.Contains(id.ToString())).ToList();
            var indexIds=catalog.ConfigIndexes.Select(i=>i.GetProperty("id").ToString()).ToHashSet();
            catalog.ConfigIndexColumns=catalog.ConfigIndexColumns.Where(i=>i.TryGetProperty("configIndexId",out var id)&&indexIds.Contains(id.ToString())).ToList();
        }
        // 完整项目与独立应用共用文件/安全/Agent基础结构；单表预览仍只展示指定业务表。
        else catalog=physicalModels.Compose(catalog);
        return R<SchemaChangePlan>.Success(await Resolve(request.ProjectId).PreviewAsync(catalog,cancellationToken));
    }
    [HttpPost("apply")]public async Task<R<SchemaApplyResult>> Apply(SchemaApplyRequest request,CancellationToken cancellationToken)
    {authorization.RequireAdministrator();return R<SchemaApplyResult>.Success(await Resolve(request.ProjectId).ApplyAsync(request.Plan,cancellationToken));}
}
[ApiController,Route("api/admin/sys/generation")]
public sealed class GenerationController(MetadataRepository metadata,ApplicationPackager packager,MetadataRegistry registry,HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpPost("generate")]public async Task<IActionResult> Generate(GenerateRequest request,CancellationToken cancellationToken)
    {
        authorization.RequireAdministrator();var catalog=await metadata.LoadProjectAsync(request.ProjectId,cancellationToken);
        var output=Path.Combine(Path.GetTempPath(),"metarill-generation",Guid.NewGuid().ToString("N"));
        try
        {
            var sourceRoot=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(registry.PlatformPath)!,"..",".."));
            // 平台配置页包含专用控件，显式保留仅后端导出；业务项目默认必须完整通过前端闭包验证。
            var mode=long.Parse(catalog.YzProjectId)==long.Parse(registry.Platform.YzProjectId)?ApplicationPackageMode.BackendOnly:ApplicationPackageMode.FullApplication;
            var package=packager.Create(catalog,output,request.RootNamespace??"Metarill.Application",sourceRoot,mode);
            Response.Headers["X-Metarill-Package-Mode"]=mode.ToString();
            Response.Headers["X-Metarill-Frontend-Included"]=package.FrontendIncluded?"true":"false";
            Response.Headers["X-Metarill-Package-Warnings"]=Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(package.Warnings));
            using var stream=new MemoryStream();using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true))
                foreach(var path in Directory.EnumerateFiles(output,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {var entry=archive.CreateEntry(Path.GetRelativePath(output,path).Replace('\\','/'));await using var target=entry.Open();await using var source=System.IO.File.OpenRead(path);await source.CopyToAsync(target,cancellationToken);}
            return File(stream.ToArray(),"application/zip","metarill-"+request.ProjectId+".zip");
        }
        finally{if(Directory.Exists(output))Directory.Delete(output,true);}
    }
}
