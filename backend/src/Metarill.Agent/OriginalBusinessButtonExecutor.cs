using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Security;
using Metarill.Core.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Metarill.Runtime.Agent;

/// <summary>保留原前端按钮正文；实体与可见规则均不能选择服务端动作或权限。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OriginalBusinessButtonRequest(string ButtonId,string? EntityId,JsonElement? Entity=null,string? VisibleScheme=null,string? VisibleRuleJson=null);

/// <summary>手写业务显式注册原按钮及固定发布路径；不会自动公开为Agent能力。</summary>
public interface IOriginalBusinessButtonHandler
{
    string ButtonId {get;}
    string Path {get;}
    /// <summary>权限和记录范围由执行器先验证，处理器负责真实业务、事务及原R响应。</summary>
    Task<IActionResult> ExecuteAsync(OriginalBusinessButtonRequest request,CancellationToken cancellationToken);
}

/// <summary>原中央按钮只分发到唯一手写处理器，先验证真实Web身份、按钮和生成记录范围。</summary>
public sealed class OriginalBusinessButtonExecutor(IAgentHostResources resources,IRuntimeAuthorization authorization,
    IServiceProvider services,IActionDescriptorCollectionProvider actions,IEnumerable<IOriginalBusinessButtonHandler> handlers,IBusinessButtonExecutionGuard execution)
{
    /// <summary>仅声明的手写按钮进入此分支；平台配置按钮继续原数据库白名单。</summary>
    public bool IsRegistered(string buttonId)=>handlers.Any(handler=>handler.ButtonId==buttonId);
    /// <summary>发布按钮决定页面与路径，客户端原实体仅在全部范围校验后交给手写业务。</summary>
    public async Task<IActionResult> ExecuteAsync(OriginalBusinessButtonRequest request,HttpContext context,CancellationToken cancellationToken=default)
    {
        var identity=services.GetService(typeof(ISecurityIdentityAccessor)) is ISecurityIdentityAccessor accessor?accessor.RequireIdentity():throw new UnauthorizedAccessException("原按钮执行需要真实Web身份。");
        if(identity.GrantId is not null||context.Items[typeof(AgentRequestIdentity)] is AgentRequestIdentity{Kind:"agent"}
            ||identity.Actor!=authorization.RequireActor())throw new UnauthorizedAccessException("原按钮执行需要真实Web身份。");
        var matches=handlers.Where(handler=>handler.ButtonId==request.ButtonId).ToArray();
        if(matches.Length!=1)throw new NotSupportedException("业务按钮未注册或存在重复处理器。");
        var button=resources.AgentCatalog.FindButton(request.ButtonId)??throw new NotSupportedException("业务按钮未发布或当前可见规则尚不支持。");
        if(button.Page.ProjectId!=identity.Actor.ProjectId||matches[0].Path!=button.Path)throw new UnauthorizedAccessException("业务按钮与发布路径不匹配。");
        authorization.RequireButtonPermission(button.Page.Id,button.Id);
        if(button.Visibility=="VisibleWhenWritable")authorization.RequirePagePermission(button.Page.Id,"update");
        if(button.Page.RequiresParent)throw new NotSupportedException("该原按钮尚缺父范围传递协议，未执行业务。");
        long id=0;
        if(!button.Page.IsFunctionTable)id=CanonicalId(request.EntityId);
        if(request.Entity is{} entity&&entity.ValueKind!=JsonValueKind.Null)
        {
            if(entity.ValueKind!=JsonValueKind.Object)throw new ArgumentException("按钮实体必须是对象。");
            var keys=entity.EnumerateObject().Where(value=>value.Name.Equals("id",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(keys.Length>1||keys.Length==1&&(keys[0].Name!="id"||CanonicalId(keys[0].Value.ValueKind==JsonValueKind.String?keys[0].Value.GetString():keys[0].Value.GetRawText())!=id))
                throw new ArgumentException("按钮对象标识冲突。");
        }
        if(!button.Page.IsFunctionTable)
        {
            var registered=actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToArray();
            if(!AgentActionBinding.TryRecordAccess(button,registered,out var access,out var reader))throw new NotSupportedException("业务按钮缺少唯一的正式生成记录入口。");
            await AgentActionBinding.RequireRecordAsync(button,access!,reader!,id,null,context,authorization);
        }
        return await execution.ExecuteAsync(button.Page.ProjectId,button.Id,()=>matches[0].ExecuteAsync(request,cancellationToken),cancellationToken);
    }
    /// <summary>原字符串ID不经浮点；拒绝非正数、指数表示及相互冲突的实体来源。</summary>
    private static long CanonicalId(string? raw)=>long.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0&&raw==id.ToString(CultureInfo.InvariantCulture)?id:throw new ArgumentException("按钮对象标识无效。");
}
