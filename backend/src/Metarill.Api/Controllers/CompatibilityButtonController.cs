using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExecuteCompatibilityButtonRequest(string ButtonId, string? EntityId, JsonElement? Entity = null, string? VisibleScheme = null, string? VisibleRuleJson = null);

/// <summary>旧前端按钮协议边界；实体及列表显示规则只保留接收兼容，绝不参与动作和权限判定。</summary>
[ApiController]
public sealed class CompatibilityButtonController(CompatibilityButtonBusiness business,Metarill.Runtime.Agent.OriginalBusinessButtonExecutor buttons) : ControllerBase
{
    /// <summary>已注册业务按钮进入统一记录守卫，平台配置按钮继续原白名单与领域事务。</summary>
    [HttpPost("api/admin/sys/config_button/execute")]
    public async Task<IActionResult> Execute(ExecuteCompatibilityButtonRequest request,CancellationToken cancellationToken)
    {
        var initialized=await AdminAccountInitializationHttp.TryExecuteAsync(new(request.ButtonId,request.EntityId,request.Entity,request.VisibleScheme,request.VisibleRuleJson),HttpContext,cancellationToken);
        if(initialized is not null)return initialized;
        if(buttons.IsRegistered(request.ButtonId))return await buttons.ExecuteAsync(new(request.ButtonId,request.EntityId,request.Entity,request.VisibleScheme,request.VisibleRuleJson),HttpContext,cancellationToken);
        return Ok(R<CompatibilityButtonResult>.Success(await business.ExecuteAsync(request.ButtonId,request.EntityId,cancellationToken)));
    }

    // 首页沿用这些直调入口；具体动作仍必须由数据库按钮及服务端白名单共同确认。
    // action是MVC保留的动作必需值；用operation承载旧URL段，避免copy_page等被误判404。
    [HttpPost("api/admin/custom/config_table/{operation}")]
    [HttpPost("api/admin/custom/config_page/{operation}")]
    [HttpPost("api/admin/custom/config_column/{operation}")]
    [HttpPost("api/admin/custom/index/init_database")]
    public async Task<R<CompatibilityButtonResult>> Legacy(CancellationToken cancellationToken)
    {
        var id = Request.Query["id"].ToString();
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            if (form.Files.Count > 0 || form.Any(pair => pair.Value.Count != 1)) throw new InvalidOperationException("按钮表单参数无效。");
            if (form.Keys.Any(k => k != "id")) throw new InvalidOperationException("按钮别名只接受 id。");
            id = SelectId(id, form["id"].ToString());
        }
        else if (Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var json = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.EnumerateObject().Any(p => p.Name != "id")) throw new InvalidOperationException("按钮别名只接受 id。");
            if (json.RootElement.TryGetProperty("id", out var field)) id = SelectId(id, field.ValueKind == JsonValueKind.String ? field.GetString() ?? "" : field.GetRawText());
        }
        var route = Request.Path.Value![4..]; // 固定移除 /api，不能由请求体传入动作地址。
        return R<CompatibilityButtonResult>.Success(await business.ExecuteLegacyAsync(route, id, cancellationToken));
    }

    /// <summary>原初始化别名使用与中央按钮相同的唯一ID解析、真实Web身份及跨库领域事务。</summary>
    [HttpPost("api/admin/custom/index/init_admin")]
    public async Task<IActionResult> InitializeAdmin(CancellationToken cancellationToken)
    {
        var id=await AdminAccountInitializationHttp.ReadTargetIdAsync(Request,cancellationToken);
        return await AdminAccountInitializationHttp.TryExecuteAsync(new(AdminAccountInitializationBusiness.ButtonId,id),HttpContext,cancellationToken)
            ??throw new InvalidOperationException("当前宿主尚未注册账号初始化事务入口。");
    }

    private static string SelectId(string query, string body)
    {
        if (query.Length > 0 && body.Length > 0 && query != body) throw new InvalidOperationException("按钮对象标识冲突。");
        return body.Length > 0 ? body : query;
    }
}
