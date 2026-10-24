using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留现有配置工作台路由；所有身份、项目、白名单和事务校验由业务入口完成。</summary>
[ApiController]
public sealed class MetadataWorkbenchController(MetadataWorkbenchBusiness business) : ControllerBase
{
    /// <summary>读取独立详情布局快照。</summary>
    [HttpPost("/api/admin/custom/page_detail_layout/snapshot")]
    public async Task<R<object>> LayoutSnapshot([FromBody] JsonElement request, CancellationToken cancellationToken) => R<object>.Success(await business.DetailLayoutSnapshotAsync(request, cancellationToken));
    /// <summary>版本冲突保留原草稿，沿用业务响应409供原工作台识别。</summary>
    [HttpPost("/api/admin/custom/page_detail_layout/save")]
    public async Task<R<object>> SaveLayout([FromBody] JsonElement request, CancellationToken cancellationToken)
    { try { return R<object>.Success(await business.SaveDetailLayoutAsync(request, cancellationToken)); } catch (MetadataRevisionConflictException exception) { return R<object>.Error(exception.Message, 409); } }
    /// <summary>旧页面字段读取仍返回数组，版本在map中独立返回。</summary>
    [HttpGet("/api/admin/custom/index/config_page_columns")]
    public async Task<R<object>> PageFields([FromQuery] string configPageId, CancellationToken cancellationToken) => Rows(await business.PageFieldsAsync(configPageId, cancellationToken));
    /// <summary>数组只包含可写字段，版本由请求头传入而不混入每个元数据实体。</summary>
    [HttpPost("/api/admin/custom/index/save_config_page_columns")]
    public async Task<R<object>> SavePageFields([FromBody] JsonElement request, [FromHeader(Name = "X-Metarill-Metadata-Revision")] string? revision, CancellationToken cancellationToken)
    { try { return Rows(await business.SavePageFieldsAsync(request, revision, cancellationToken)); } catch (MetadataRevisionConflictException exception) { return R<object>.Error(exception.Message, 409); } }
    /// <summary>读取表字段排序，保持原数组外观。</summary>
    [HttpGet("/api/admin/custom/index/config_columns")]
    public async Task<R<object>> Columns([FromQuery] string configTableId, CancellationToken cancellationToken) => Rows(await business.ColumnOrderAsync(configTableId, cancellationToken));
    /// <summary>只修改当前表字段的明确顺序，未知属性由业务层拒绝。</summary>
    [HttpPost("/api/admin/custom/index/save_config_columns")]
    public async Task<R<object>> SaveColumns([FromBody] JsonElement request, [FromHeader(Name = "X-Metarill-Metadata-Revision")] string? revision, CancellationToken cancellationToken)
    { try { return Rows(await business.SaveColumnOrderAsync(request, revision, cancellationToken)); } catch (MetadataRevisionConflictException exception) { return R<object>.Error(exception.Message, 409); } }
    /// <summary>同时兼容原表单请求和Agent的JSON对象，不接受未知表单参数。</summary>
    private async Task<JsonElement> ReadConfigurationRequest(CancellationToken cancellationToken)
    {
        if(Request.HasFormContentType)
        {
            var form=await Request.ReadFormAsync(cancellationToken);
            if(form.Files.Count>0||form.Any(pair=>pair.Value.Count!=1))throw new InvalidOperationException("配置表单参数无效。");
            return JsonSerializer.SerializeToElement(form.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToString()));
        }
        return await Request.ReadFromJsonAsync<JsonElement>(cancellationToken:cancellationToken);
    }
    /// <summary>原批量预览只读返回逐行校验，不能触发DDL。</summary>
    [HttpPost("/api/admin/custom/index/preview_batch_create_columns")]
    public async Task<R<object>> PreviewBatch(CancellationToken cancellationToken) => R<object>.Success(await business.PreviewBatchColumnsAsync(await ReadConfigurationRequest(cancellationToken),cancellationToken));
    /// <summary>保存失败按原协议携带map.preview，成功仅说明配置写入完成。</summary>
    [HttpPost("/api/admin/custom/index/save_batch_create_columns")]
    public async Task<R<object>> SaveBatch(CancellationToken cancellationToken)
    {
        try{return R<object>.Success(await business.SaveBatchColumnsAsync(await ReadConfigurationRequest(cancellationToken),cancellationToken));}
        catch(BatchColumnValidationException exception){var response=R<object>.Error(exception.Message);response.Map["preview"]=exception.Preview;return response;}
    }
    /// <summary>为现有批量字段工作台提供已有列列表。</summary>
    [HttpPost("/api/admin/custom/config_column/workbench_columns")]
    public async Task<R<object>> WorkbenchColumns(CancellationToken cancellationToken) => R<object>.Success(await business.WorkbenchColumnsAsync(await ReadConfigurationRequest(cancellationToken),cancellationToken));
    /// <summary>原联接字段页面按关系分组读取当前配置候选，保留原JSON协议。</summary>
    [HttpPost("/api/admin/custom/config_table/join_column_candidates")]
    public async Task<R<JoinColumnCandidates>> JoinColumnCandidates([FromBody] JsonElement request,CancellationToken token) => R<JoinColumnCandidates>.Success(await business.JoinColumnCandidatesAsync(request,token));
    /// <summary>仅保存勾选配置，事务及完整候选校验由Business承担，不在HTTP边界改表。</summary>
    [HttpPost("/api/admin/custom/config_table/save_join_columns")]
    public async Task<R<object>> SaveJoinColumns([FromBody] JsonElement request,CancellationToken token) => R<object>.Success(await business.SaveJoinColumnsAsync(request,token));
    /// <summary>附加并发版本但不改变既有data数组。</summary>
    private static R<object> Rows(MetadataRowsSnapshot snapshot)
    { var response = R<object>.Success(snapshot.Rows); response.Map["revision"] = snapshot.Revision; return response; }
}
