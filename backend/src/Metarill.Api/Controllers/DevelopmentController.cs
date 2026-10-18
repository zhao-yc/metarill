using System.IO.Compression;
using Metarill.Core.Contracts;
using Metarill.Data.Schema;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;
public sealed record SchemaPreviewRequest(string ProjectId,string? TableId);
public sealed record SchemaApplyRequest(string ProjectId,SchemaChangePlan Plan);
public sealed record GenerateRequest(string ProjectId,string? RootNamespace);
[ApiController,Route("api/admin/sys/database")]
public sealed class DatabaseController(MetadataRepository metadata,HttpRuntimeAuthorization authorization,ProjectSchemaService schemas):ControllerBase
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
        return R<SchemaChangePlan>.Success(await Resolve(request.ProjectId).PreviewAsync(catalog,cancellationToken));
    }
    [HttpPost("apply")]public async Task<R<SchemaApplyResult>> Apply(SchemaApplyRequest request,CancellationToken cancellationToken)
    {authorization.RequireAdministrator();return R<SchemaApplyResult>.Success(await Resolve(request.ProjectId).ApplyAsync(request.Plan,cancellationToken));}
}
[ApiController,Route("api/admin/sys/generation")]
public sealed class GenerationController(MetadataRepository metadata,CSharpGenerator generator,HttpRuntimeAuthorization authorization):ControllerBase
{
    [HttpPost("generate")]public async Task<IActionResult> Generate(GenerateRequest request,CancellationToken cancellationToken)
    {
        authorization.RequireAdministrator();var catalog=await metadata.LoadProjectAsync(request.ProjectId,cancellationToken);
        var output=Path.Combine(Path.GetTempPath(),"metarill-generation",Guid.NewGuid().ToString("N"));
        try
        {
            generator.Generate(catalog,output,request.RootNamespace??"Metarill.Application");
            using var stream=new MemoryStream();using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true))
                foreach(var path in Directory.EnumerateFiles(output,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {var entry=archive.CreateEntry(Path.GetRelativePath(output,path).Replace('\\','/'));await using var target=entry.Open();await using var source=System.IO.File.OpenRead(path);await source.CopyToAsync(target,cancellationToken);}
            return File(stream.ToArray(),"application/zip","metarill-"+request.ProjectId+".zip");
        }
        finally{if(Directory.Exists(output))Directory.Delete(output,true);}
    }
}
