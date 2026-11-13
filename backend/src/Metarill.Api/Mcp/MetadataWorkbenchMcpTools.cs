using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Metarill.Api.Mcp;

/// <summary>保留原布局工具名及JSON结果，复用原HTTP聚合业务，不开放独立字段写入或DDL。</summary>
[McpServerToolType]
public sealed class MetadataWorkbenchMcpTools
{
    /// <summary>只返回当前项目原布局快照和revision，不接受客户端身份或项目参数。</summary>
    [McpServerTool(Name="getPageDetailLayout",ReadOnly=true),Description("读取当前授权项目的详情分组及完整字段归属，返回原revision、pageColumnId和columnId；修改前先读取。")]
    public static Task<CallToolResult> Snapshot([Description("页面ID，使用无损字符串")]string pageId,MetadataWorkbenchBusiness business,HttpRuntimeAuthorization authorization,ILogger<MetadataWorkbenchMcpTools> logger,CancellationToken token)=>
        RunAsync(()=>business.DetailLayoutSnapshotAsync(JsonSerializer.SerializeToElement(new{pageId}),token),"详情布局获取成功",authorization,logger);

    /// <summary>原完整定义与字段归属一次提交；明确调用保存即执行，不增加前端步骤。</summary>
    [McpServerTool(Name="savePageDetailLayout",ReadOnly=false),Description("按最近revision一次保存详情布局和全部字段归属。version=1，mode为none/tabs；空detailGroupKey表示常显。仅保存配置，不DDL、不生成或部署；冲突须刷新重编，未知结果先回读，禁止自动重试。")]
    public static Task<CallToolResult> Save([Description("原完整布局保存请求")]DetailLayoutMcpRequest request,MetadataWorkbenchBusiness business,HttpRuntimeAuthorization authorization,ILogger<MetadataWorkbenchMcpTools> logger,CancellationToken token)=>
        RunAsync(()=>business.SaveDetailLayoutAsync(JsonSerializer.SerializeToElement(request,JsonDefaults.Options),token),"详情布局保存成功",authorization,logger);

    /// <summary>已确认的身份前检与版本冲突才声明未写入，其余执行失败保守要求回读且不泄漏异常正文。</summary>
    private static async Task<CallToolResult> RunAsync(Func<Task<object>> action,string message,HttpRuntimeAuthorization authorization,ILogger logger)
    {
        try{authorization.RequireIdentity();}
        catch(UnauthorizedAccessException error){return Failure("YZP_UNAUTHENTICATED","登录超时，请重新授权。","not_applied","reauthorize_and_reconnect",error,logger);}
        try{authorization.RequireAdministrator();}
        catch(UnauthorizedAccessException error){return Failure("YZP_FORBIDDEN","当前身份没有平台配置权限。","not_applied","request_access",error,logger);}
        try{return Response(new{ok=true,message,data=await action()});}
        catch(MetadataLayoutInputException error){return Failure("YZP_INVALID_ARGUMENT","参数格式或取值不符合要求，请检查工具参数。","not_applied","correct_arguments",error,logger);}
        catch(MetadataRevisionConflictException error){return Failure("YZP_CONFLICT","页面或字段配置已变化，请刷新后重新编辑。","not_applied","refresh_and_replan",error,logger);}
        catch(Exception error)
        {
            var timeout=error is OperationCanceledException or TimeoutException;
            return Failure(timeout?"YZP_RESULT_UNKNOWN":"YZP_INTERNAL_ERROR",timeout?"请求超时，尚不能确认操作结果。":"工具执行失败，请根据追踪标识检查服务端日志。","unknown","read_back_before_retry",error,logger);
        }
    }

    /// <summary>错误只记录类型及追踪标识，不把数据库、配置、参数或内部异常文本写入日志及客户端。</summary>
    private static CallToolResult Failure(string code,string message,string outcome,string recovery,Exception failure,ILogger logger)
    {
        var traceId=Guid.NewGuid().ToString("D");
        logger.LogWarning("MCP详情布局失败：traceId={TraceId},code={Code},outcome={Outcome},exceptionType={ExceptionType}",traceId,code,outcome,failure.GetType().FullName);
        return Response(new{ok=false,error=new{code,message,retryable=false,outcome,recovery,traceId}},true);
    }

    /// <summary>直接输出原工具JSON文本，成功与失败都可解析，SDK错误标志与ok保持一致。</summary>
    private static CallToolResult Response(object value,bool error=false)=>new()
    {IsError=error,Content=[new TextContentBlock{Text=JsonSerializer.Serialize(value,JsonDefaults.Options)}]};
}

/// <summary>原MCP保存请求；未知字段与缺失成员在绑定阶段拒绝，防止类型绑定吞掉额外输入。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DetailLayoutMcpRequest(
    [property:JsonRequired,Description("页面ID字符串")]string PageId,
    [property:JsonRequired,Description("最近快照的64位revision")]string Revision,
    [property:JsonRequired]DetailLayoutMcpDefinition DetailLayout,
    [property:JsonRequired,Description("全部页面字段的归属，不允许漏项")]IReadOnlyList<DetailLayoutMcpAssignment> Fields);

/// <summary>布局定义沿用原版本、开关和稳定机器标识。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DetailLayoutMcpDefinition(
    [property:JsonRequired]int Version,[property:JsonRequired]string Mode,
    [property:JsonRequired]IReadOnlyList<DetailLayoutMcpGroup> Groups);

/// <summary>原字段归属仅含页面字段ID和分组key，不夹带展示标志或身份。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DetailLayoutMcpAssignment([property:JsonRequired]string PageColumnId,[property:JsonRequired]string DetailGroupKey);

/// <summary>key遵循共享规则的64字符约束，名称允许中文；改名不改变key。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DetailLayoutMcpGroup([property:JsonRequired]string Key,[property:JsonRequired]string Name);
