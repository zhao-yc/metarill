using System.Text.Json;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Runtime.Compatibility;
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
        await RequireEmptyLegacyBodyAsync(Request,token);var deviceId=ReadDeviceId(Request,true);
        var login=Request.Headers["loginStateId"];var bearer=Request.Headers.Authorization;if(login.Count>1||bearer.Count>1||login.Count==1&&bearer.Count==1)throw new UnauthorizedAccessException("登录超时");
        var batch=Request.Headers["X-Yzp-Agent-Request-Id"];if(batch.Count>1)throw new InvalidOperationException("批次编号不能重复。");
        var batchId=batch.Count==1?batch.ToString():Guid.NewGuid().ToString("D");AgentTaskStore.ValidateRequestId(batchId);
        var result=await enqueuer.EnqueueAsync(identity.Identity,deviceId,batchId,login.Count==1?login.ToString():null,bearer.Count==1?bearer.ToString():null,token);Response.Headers.CacheControl="private, no-store";
        // 旧前端R200会直接触发成功toast；尚未应用或失败的结果只能保留逐项数据并明确非成功。
        return LegacyReceipt(result);
    }

    /// <summary>保留旧详情直调地址。浏览器中央按钮和直调共用动作与回执。</summary>
    [HttpPost("/api/admin/custom/index/sync_runtime_project"), RequestSizeLimit(4096)]
    public async Task<R<AssistantRuntimeBatchEnqueueResult>> ExecuteProject(CancellationToken token)
    {
        AgentRequestAuthentication.RequireWeb(HttpContext);
        authorization.RequireAdministrator();
        var id = Request.Query["id"];
        if (Request.Query.Keys.Any(key => key != "id") || id.Count > 1)
            throw new InvalidOperationException("项目同步只接受唯一 id。");
        var target = id.ToString();
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(token);
            if (form.Files.Count != 0 || form.Any(field => field.Key != "id" || field.Value.Count != 1) ||
                (id.Count > 0 && form.ContainsKey("id")))
                throw new InvalidOperationException("项目同步只接受唯一 id。");
            if (form.TryGetValue("id", out var value)) target = value.ToString();
        }
        else await RequireEmptyLegacyBodyAsync(Request, token);
        return await ExecuteProjectAsync(HttpContext, target, token);
    }

    /// <summary>固定原按钮和选中项目，客户端实体、目录及绑定内容不参与任务构造。</summary>
    internal static async Task<R<AssistantRuntimeBatchEnqueueResult>> ExecuteProjectAsync(HttpContext http, string? targetId, CancellationToken token)
    {
        var identity = AgentRequestAuthentication.RequireWeb(http);
        var request = http.Request;
        var projectId = CompatibilityButtonPolicy.RequireId(targetId ?? "");
        if (projectId != targetId) throw new InvalidOperationException("项目标识格式无效。");
        var confirm = request.Headers["X-Yzp-Agent-Confirm"];
        if (confirm.Count != 1 || confirm[0] != "true") throw new InvalidOperationException("请先确认原项目同步按钮。");
        var batch = request.Headers["X-Yzp-Agent-Request-Id"];
        if (batch.Count != 1) throw new InvalidOperationException("项目同步需要唯一批次编号。");
        var batchId = batch.ToString();
        AgentTaskStore.ValidateRequestId(batchId);
        var deviceId = ReadDeviceHeader(request);
        var login = request.Headers["loginStateId"];
        var bearer = request.Headers.Authorization;
        if (login.Count > 1 || bearer.Count > 1 || (login.Count == 1 && bearer.Count == 1))
            throw new UnauthorizedAccessException("登录超时");
        var business = http.RequestServices.GetRequiredService<CompatibilityButtonBusiness>();
        await business.RequireProjectRuntimeButtonAsync(token);
        var result = await http.RequestServices.GetRequiredService<GenerationAssistantBatchEnqueuer>()
            .EnqueueAsync(identity.Identity, deviceId, batchId, login.Count == 1 ? login.ToString() : null,
                bearer.Count == 1 ? bearer.ToString() : null, token, targetProjectId: projectId);
        http.Response.Headers.CacheControl = "private, no-store";
        return LegacyReceipt(result, singleProject: true);
    }

    /// <summary>兼容原已编码sysParams头，设备只作选择且后续仍须真实拥有权查库；不输出原始头或解析正文。</summary>
    public static string ReadDeviceId(HttpRequest request,bool emptyBodyValidated=false)
    {
        if(request.Query.Keys.Any(key=>key!="parentId")||request.Query["parentId"].Count>1||(!emptyBodyValidated&&request.ContentLength>0)||request.Headers.TransferEncoding.Count!=0)
            throw new InvalidOperationException("批量入口只允许原parentId上下文且不接受正文。");
        return ReadDeviceHeader(request);
    }

    /// <summary>设备仅为范围选择，真实所有权仍由助手查库。</summary>
    private static string ReadDeviceHeader(HttpRequest request)
    {
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
    /// <summary>原uni-app共享请求把无data的POST补为{}；有界接受空正文/空对象，任何字段均拒绝。</summary>
    public static async Task RequireEmptyLegacyBodyAsync(HttpRequest request,CancellationToken token=default)
    {
        if(request.ContentLength>256||request.Headers.TransferEncoding.Count!=0)throw new InvalidOperationException("批量入口正文超出原协议范围。");
        var buffer=new byte[257];var count=0;
        while(count<buffer.Length){var read=await request.Body.ReadAsync(buffer.AsMemory(count),token);if(read==0)break;count+=read;}
        if(count>256)throw new InvalidOperationException("批量入口正文过长。");
        if(count==0)return;
        try
        {
            using var body=JsonDocument.Parse(buffer.AsMemory(0,count));
            if(body.RootElement.ValueKind!=JsonValueKind.Object||body.RootElement.EnumerateObject().Any())throw new InvalidOperationException("批量入口只接受原空对象正文。");
        }
        catch(JsonException){throw new InvalidOperationException("批量入口正文无效。");}
    }
    /// <summary>只有全部实际applied且至少一项执行完成才可沿旧成功UI；排队、失败、未知和空集合均明确未完成。</summary>
    public static R<AssistantRuntimeBatchEnqueueResult> LegacyReceipt(AssistantRuntimeBatchEnqueueResult result, bool singleProject = false)
    {
        var applied=result.Items.Count(item=>item.Status=="applied");var skipped=result.Items.Count(item=>item.Status=="skipped");
        if(applied>0&&applied+skipped==result.Items.Length)return R<AssistantRuntimeBatchEnqueueResult>.Success(result);
        string Label(string status)=>status switch
        {
            "queued"=>"已排队，等待本机助手","leased"=>"本机助手处理中","preview_ready"=>"预览完成，尚未应用","dependencies_required"=>"需要补齐依赖确认",
            "awaiting_application"=>"等待应用回执","applied"=>"已应用","failed"=>"执行失败","expired"=>"任务过期","rolled_back"=>"本次修改已回滚","manual_recovery"=>"结果未知，需核对现场","skipped"=>"按原配置跳过",_=>"状态待核对"
        };
        var message = singleProject ? "项目同步尚未应用。" :
            result.Items.Length == 0 ? "当前设备没有可同步的项目配置。" : "批量同步尚未全部应用。";
        return new(500,message+string.Join("；",result.Items.Select(item=>item.ProjectName+"："+Label(item.Status)+(item.RequestId is{} id?"（任务 "+id+"）":""))),result,[]);
    }
}
