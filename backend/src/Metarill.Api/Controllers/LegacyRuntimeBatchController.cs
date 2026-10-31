using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留原项目列表的批量按钮路由和一次确认；内部转为逐项目助手任务，不重写页面。</summary>
[ApiController,ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class LegacyRuntimeBatchController(GenerationAssistantBatchEnqueuer enqueuer,HttpRuntimeAuthorization authorization):ControllerBase
{
    /// <summary>原页面POST旧地址；设备沿原sysParams取值，parentId仅为列表父上下文，不能选择设备。</summary>
    [HttpPost("/api/admin/custom/index/sync_runtime_all_projects")]
    public async Task<R<AssistantRuntimeBatchEnqueueResult>> Execute(CancellationToken token)
    {
        var identity=AgentRequestAuthentication.RequireWeb(HttpContext);authorization.RequireAdministrator();
        var deviceId=ReadDeviceId(Request);
        var login=Request.Headers["loginStateId"];var bearer=Request.Headers.Authorization;if(login.Count>1||bearer.Count>1||login.Count==1&&bearer.Count==1)throw new UnauthorizedAccessException("登录超时");
        var batch=Request.Headers["X-Yzp-Agent-Request-Id"];if(batch.Count>1)throw new InvalidOperationException("批次编号不能重复。");
        var batchId=batch.Count==1?batch.ToString():Guid.NewGuid().ToString("D");AgentTaskStore.ValidateRequestId(batchId);
        var result=await enqueuer.EnqueueAsync(identity.Identity,deviceId,batchId,login.Count==1?login.ToString():null,bearer.Count==1?bearer.ToString():null,token);Response.Headers.CacheControl="private, no-store";
        // 旧前端R200会直接触发成功toast；尚未应用或失败的结果只能保留逐项数据并明确非成功。
        return LegacyReceipt(result);
    }

    /// <summary>兼容原已编码sysParams头，设备只作选择且后续仍须真实拥有权查库；不输出原始头或解析正文。</summary>
    public static string ReadDeviceId(HttpRequest request)
    {
        if(request.Query.Keys.Any(key=>key!="parentId")||request.Query["parentId"].Count>1||request.ContentLength>0||request.Headers.TransferEncoding.Count!=0)
            throw new InvalidOperationException("批量入口只允许原parentId上下文且不接受正文。");
        var values=request.Headers["sysParams"];
        if(values.Count!=1||values[0] is not{Length:>0 and <=8192} raw)throw new InvalidOperationException("缺少系统参数，请先选择设备。");
        using var json=JsonDocument.Parse(Uri.UnescapeDataString(raw));
        if(json.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("系统参数无效。");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var property in json.RootElement.EnumerateObject())if(!seen.Add(property.Name))throw new InvalidOperationException("系统参数包含重复字段。");
        if(!json.RootElement.TryGetProperty("yzManagerDevicesId",out var value)||value.ValueKind!=JsonValueKind.String||value.GetString() is not{} id||!long.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out var device)||device<=0||device.ToString(CultureInfo.InvariantCulture)!=id)
            throw new InvalidOperationException("缺少系统参数，请先选择有效设备。");
        return id;
    }
    /// <summary>只有全部实际applied且至少一项执行完成才可沿旧成功UI；排队、失败、未知和空集合均明确未完成。</summary>
    public static R<AssistantRuntimeBatchEnqueueResult> LegacyReceipt(AssistantRuntimeBatchEnqueueResult result)
    {
        var applied=result.Items.Count(item=>item.Status=="applied");var skipped=result.Items.Count(item=>item.Status=="skipped");
        if(applied>0&&applied+skipped==result.Items.Length)return R<AssistantRuntimeBatchEnqueueResult>.Success(result);
        string Label(string status)=>status switch
        {
            "queued"=>"已排队，等待本机助手","leased"=>"本机助手处理中","preview_ready"=>"预览完成，尚未应用","dependencies_required"=>"需要补齐依赖确认",
            "awaiting_application"=>"等待应用回执","applied"=>"已应用","failed"=>"执行失败","expired"=>"任务过期","rolled_back"=>"本次修改已回滚","manual_recovery"=>"结果未知，需核对现场","skipped"=>"按原配置跳过",_=>"状态待核对"
        };
        var message=result.Items.Length==0?"当前设备没有可同步的项目配置。":"批量同步尚未全部应用。";
        return new(500,message+string.Join("；",result.Items.Select(item=>item.ProjectName+"："+Label(item.Status)+(item.RequestId is{} id?"（任务 "+id+"）":""))),result,[]);
    }
}
