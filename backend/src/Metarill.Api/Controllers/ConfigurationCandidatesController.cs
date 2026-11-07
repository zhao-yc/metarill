using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留原控件编辑器的候选、引用及清空协议；只接收明确允许的请求字段。</summary>
[ApiController, Route("api/admin/custom/config_column")]
public sealed class ConfigurationCandidatesController(ConfigurationCandidatesBusiness business) : ControllerBase
{
    /// <summary>原首页搜索地址直接复用配置身份、固定SQL范围及原表/页响应。</summary>
    [HttpPost("/api/admin/custom/index/search"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<R<object>> Search(CancellationToken token)=>R<object>.Success(await business.SearchAsync(await RequestObject(token),token));
    /// <summary>表候选支持原控件空对象请求及无请求体调用。</summary>
    [HttpPost("list_control_tables")]
    public async Task<R<object>> Tables(CancellationToken token) => R<object>.Success(await business.TablesAsync(await RequestObject(token), token));
    /// <summary>默认单选页，可明确请求多选页；项目与父表归属仍由真实管理员身份校验。</summary>
    [HttpPost("select_page_options")]
    public async Task<R<object>> Pages(CancellationToken token) => R<object>.Success(await business.PagesAsync(await RequestObject(token), token));
    /// <summary>编辑器继承只读字段配置，父表由真实字段推导，不放宽标准页面的 parentId 范围。</summary>
    [HttpPost("control_metadata")]
    public async Task<R<ConfigurationControlMetadata>> ControlMetadata(CancellationToken token) => R<ConfigurationControlMetadata>.Success(await business.ControlMetadataAsync(await RequestObject(token), token));
    /// <summary>批量字段工作台沿用 id 表单请求，同时接受严格 JSON 对象。</summary>
    [HttpPost("references")]
    public async Task<R<ConfigurationColumnReferences>> References(CancellationToken token) => R<ConfigurationColumnReferences>.Success(await business.ReferencesAsync(await RequestObject(token), token));
    /// <summary>清空明确 id 的页面覆盖，控制器不承担元数据写入或事务。</summary>
    [HttpPost("clear_page_control_detail")]
    public async Task<R<string>> Clear(CancellationToken token) => R<string>.Success(await business.ClearPageControlDetailAsync(await RequestObject(token), token));

    /// <summary>合并原查询/表单与 JSON 入口，重复来源或未知键由统一业务白名单拒绝。</summary>
    private async Task<JsonElement> RequestObject(CancellationToken token)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in Request.Query)
        {
            if (value.Count != 1 || !values.TryAdd(key, JsonSerializer.SerializeToElement(value.ToString()))) throw new InvalidOperationException("配置请求参数重复。");
        }
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(token);
            if (form.Files.Count != 0) throw new InvalidOperationException("配置请求不能包含文件。");
            foreach (var (key, value) in form)
                if (value.Count != 1 || !values.TryAdd(key, JsonSerializer.SerializeToElement(value.ToString()))) throw new InvalidOperationException("配置请求参数重复。");
        }
        else if (Request.ContentLength is > 0 || !string.IsNullOrWhiteSpace(Request.ContentType) || Request.Headers.TransferEncoding.Count != 0)
        {
            if (!Request.HasJsonContentType()) throw new InvalidOperationException("配置请求仅支持 JSON 或表单。");
            var body = await Request.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
            if (body.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("配置请求必须是对象。");
            foreach (var property in body.EnumerateObject())
                if (!values.TryAdd(property.Name, property.Value.Clone())) throw new InvalidOperationException("配置请求参数重复。");
        }
        return JsonSerializer.SerializeToElement(values);
    }
}
