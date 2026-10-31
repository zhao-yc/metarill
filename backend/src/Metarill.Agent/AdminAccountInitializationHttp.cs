using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Metarill.Runtime.Agent;

/// <summary>原中央按钮与直调URL共用初始化边界；普通包没有初始化注册便继续原分发。</summary>
public static class AdminAccountInitializationHttp
{
    /// <summary>先排除其它按钮；初始化只用真实Web身份和已保存后台，权限/锁/事务由原Business重验。</summary>
    public static async Task<IActionResult?> TryExecuteAsync(OriginalBusinessButtonRequest request,HttpContext context,CancellationToken token=default)
    {
        if(request.ButtonId!=AdminAccountInitializationBusiness.ButtonId)return null;
        var accessor=context.RequestServices.GetService<ISecurityIdentityAccessor>()??throw new UnauthorizedAccessException("初始化账号需要正式Web身份。");
        var identity=accessor.RequireIdentity();
        if(identity.GrantId is not null||context.Items[typeof(AgentRequestIdentity)] is AgentRequestIdentity{Kind:"agent"})
            throw new UnauthorizedAccessException("初始化账号需要正式Web身份。");
        var initializer=context.RequestServices.GetService<IAdminAccountInitializer>();
        if(initializer is null)return null;
        var id=AdminAccountInitializationBusiness.RequireTargetId(request.EntityId,request.Entity);
        await initializer.InitializeAsync(id,token);
        // 保留原R成功回执，不把账号标识、配置密码或内部流程结果返回前端。
        return new OkObjectResult(R<object>.Success(null));
    }

    /// <summary>原直调只接id，查询/表单/JSON不得冲突；客户端口令、归属或文件不能进入业务。</summary>
    public static async Task<string> ReadTargetIdAsync(HttpRequest request,CancellationToken token=default)
    {
        if(request.Query.Keys.Any(key=>key!="id")||request.Query["id"].Count>1)throw new ArgumentException("初始化只接受唯一id。");
        var id=request.Query["id"].ToString();
        // Kestrel明确区分无正文与分块正文；不能仅凭可空ContentLength忽略内容或误读空JSON。
        var hasBody=request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody??request.ContentLength!=0;
        if(request.HasFormContentType)
        {
            var form=await request.ReadFormAsync(token);
            if(form.Files.Count>0||form.Any(field=>field.Key!="id"||field.Value.Count!=1))throw new ArgumentException("初始化表单只接受唯一id。");
            id=Select(id,form["id"].ToString());
        }
        else if(hasBody&&request.ContentType?.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)==true)
        {
            using var body=await JsonDocument.ParseAsync(request.Body,cancellationToken:token);
            if(body.RootElement.ValueKind!=JsonValueKind.Object||body.RootElement.EnumerateObject().Any(field=>field.Name!="id")||
                body.RootElement.EnumerateObject().Count(field=>field.Name=="id")>1)throw new ArgumentException("初始化JSON只接受唯一id。");
            if(body.RootElement.TryGetProperty("id",out var field))id=Select(id,field.ValueKind==JsonValueKind.String?field.GetString()??"":field.GetRawText());
        }
        else if(hasBody)throw new ArgumentException("初始化请求内容类型无效。");
        return AdminAccountInitializationBusiness.RequireTargetId(id);
    }
    /// <summary>双入口只能指向同一已保存后台，不能以请求体覆盖URL中另一对象。</summary>
    private static string Select(string query,string body)
    {
        if(query.Length>0&&body.Length>0&&query!=body)throw new ArgumentException("初始化后台标识冲突。");
        return body.Length>0?body:query;
    }
}
