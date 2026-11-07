using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Metarill.Runtime.Agent;

/// <summary>普通Agent记录写只绑定生成器的固定scope入口，不能从请求选择表、类型、操作或读取方法。</summary>
internal sealed record AgentRecordWriteBinding(AgentPublishedPage Page,string Operation,GeneratedAgentWriteAccessAttribute Access,
    MethodInfo Reader,string IdBinding,string IdName,bool LegacyBody,string? ParentName,ReadOnlyConditionRule Condition)
{
    /// <summary>旧产物缺合同、类型/路径冲突或只读条件无法证明时不发布记录写能力，查询/创建不受此合同影响。</summary>
    internal static AgentRecordWriteBinding? Compile(ControllerActionDescriptor action,AgentPublishedPage page,string operation,IReadOnlyList<ControllerActionDescriptor> registered)
    {
        var access=action.ControllerTypeInfo.GetCustomAttribute<GeneratedAgentWriteAccessAttribute>();
        if(access is null||access.ProjectId!=page.ProjectId||access.PageId!=page.Id||access.TableId!=page.TableId
            ||access.EntityType.Assembly!=action.ControllerTypeInfo.Assembly||access.ServiceType.Assembly!=action.ControllerTypeInfo.Assembly
            ||access.ReadMethod!="ReadAgentRecord"+page.Id+"Async"||action.ControllerTypeInfo.GetConstructors().Length!=1
            ||action.ControllerTypeInfo.GetConstructors()[0].GetParameters().Count(parameter=>parameter.ParameterType==access.ServiceType)!=1)return null;
        var sources=registered.Select(value=>value.ControllerTypeInfo.AsType()).Distinct().Where(type=>type.Namespace?.Contains(".Generated.Controllers",StringComparison.Ordinal)==true)
            .Where(type=>type.GetCustomAttribute<GeneratedAgentWriteAccessAttribute>() is{} marker&&marker.ProjectId==page.ProjectId&&marker.PageId==page.Id).ToArray();
        if(sources.Length!=1||sources[0]!=action.ControllerTypeInfo.AsType()||page.RecordNames is null)return null;
        var reader=access.ServiceType.GetMethod(access.ReadMethod,BindingFlags.Instance|BindingFlags.Public,[typeof(long),typeof(string),typeof(string),typeof(RuntimeActor),typeof(IRuntimeAuthorization),typeof(CancellationToken)]);
        if(reader is null||reader.ReturnType!=typeof(Task<>).MakeGenericType(access.EntityType))return null;
        ReadOnlyConditionRule condition;try{condition=ReadOnlyConditionRule.Compile(page.ReadOnlyCondition);}catch(ArgumentException){return null;}
        if(condition.Fields.Any(field=>!page.RecordNames.ContainsKey(field.Split('.')[0])))return null;
        var parameters=action.Parameters.OfType<ControllerParameterDescriptor>().Where(value=>value.ParameterType!=typeof(CancellationToken)&&value.BindingInfo?.BindingSource!=BindingSource.Services).ToArray();
        var parents=parameters.Where(value=>value.Name=="parentId").ToArray();
        if(parents.Length>1||parents.Any(value=>value.ParameterType!=typeof(string)||value.BindingInfo?.BindingSource!=BindingSource.Query))return null;
        var parent=parents.SingleOrDefault()?.BindingInfo?.BinderModelName??parents.SingleOrDefault()?.Name;
        if(page.RequiresParent&&parent is null)return null;
        if(operation=="copy")
        {
            var ids=parameters.Where(value=>value.Name=="id").ToArray();
            if(ids.Length!=1||ids[0].ParameterType!=typeof(long)||action.AttributeRouteInfo?.Template?.Contains("{id}",StringComparison.Ordinal)!=true)return null;
            return new(page,operation,access,reader,"path",ids[0].BindingInfo?.BinderModelName??ids[0].Name,false,parent,condition);
        }
        var bodies=parameters.Where(value=>value.BindingInfo?.BindingSource==BindingSource.Body).ToArray();if(bodies.Length!=1)return null;
        var legacy=action.MethodInfo.GetCustomAttribute<LegacyPageWriteRequestAttribute>();var bodyType=legacy?.RequestType??bodies[0].ParameterType;
        if(legacy is not null&&bodies[0].ParameterType!=typeof(JsonElement)||bodyType.Assembly!=action.ControllerTypeInfo.Assembly)return null;
        var property=bodyType.GetProperty("Id",BindingFlags.Instance|BindingFlags.Public);
        if(property?.PropertyType!=typeof(long)||property.GetMethod is not{IsPublic:true})return null;
        var name=property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name??"id";
        // 旧整实体归一器只认原id键；其余字段继续由原归一器处理，不用严格DTO反序列化误拒只读回显。
        if(legacy is not null&&name!="id")return null;
        return new(page,operation,access,reader,"body",name,legacy is not null,parent,condition);
    }
    /// <summary>任务登记前读取实际写scope中的原记录；ID/父来源和规则均来自固定MVC及发布目录。</summary>
    internal async Task ValidateAsync(HttpContext context,ReadOnlyMemory<byte> body,IRuntimeAuthorization authorization)
    {
        var options=context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var parent=ReadParent(context);if(Page.RequiresParent&&string.IsNullOrWhiteSpace(parent))throw InvalidRequest();
        long id;
        if(IdBinding=="path")
        {
            var raw=Convert.ToString(context.Request.RouteValues.GetValueOrDefault(IdName),CultureInfo.InvariantCulture);
            if(!long.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out id))throw InvalidRequest();
        }
        else id=ReadBodyId(context,body,options);
        if(id<=0)throw InvalidRequest();
        var services=context.RequestServices.GetServices(Access.ServiceType).ToArray();if(services.Length!=1||services[0] is null)throw new InvalidOperationException("记录写要求唯一的已发布生成Service。");
        Task task;
        try{task=(Task)Reader.Invoke(services[0],[id,parent,Operation,authorization.RequireActor(),authorization,context.RequestAborted])!;}
        catch(TargetInvocationException error)when(error.InnerException is UnauthorizedAccessException or SecurityException){throw Denied();}
        catch(TargetInvocationException error)when(error.InnerException is ArgumentException){throw InvalidRequest();}
        try{await task;}
        catch(Exception error)when(error is UnauthorizedAccessException or SecurityException){throw Denied();}
        catch(ArgumentException){throw InvalidRequest();}
        var record=task.GetType().GetProperty("Result")?.GetValue(task);if(record is null)throw Denied();
        try
        {
            var values=AgentRecordValues.Read(record,Access.EntityType,Page.RecordNames!,Condition.Fields);
            if(Condition.IsReadOnly(values))throw new AgentTaskException("YZP_CAPABILITY_DENIED","当前记录为只读，不能修改、删除或复制。",403,"not_applied","fix_request");
        }
        catch(Exception error)when(error is ArgumentException or NotSupportedException or InvalidOperationException)
        {throw new AgentTaskException("YZP_CAPABILITY_DENIED","原记录只读条件无法可靠判定，未执行写入。",403,"not_applied","fix_request");}
    }
    /// <summary>父范围只使用实际query绑定，拒绝多值；实体中可编辑的父键不能替代scope参数。</summary>
    private string? ReadParent(HttpContext context)
    {
        if(ParentName is null)return null;var values=context.Request.Query[ParentName];if(values.Count>1)throw InvalidRequest();return values.Count==1?values[0]:null;
    }
    /// <summary>只取MVC实际id，不改变原整实体白名单；ID转换复用宿主JSON设置及字符串长整数协议。</summary>
    private long ReadBodyId(HttpContext context,ReadOnlyMemory<byte> body,JsonSerializerOptions options)
    {
        if(!context.Request.HasJsonContentType())throw InvalidRequest();
        try
        {
            using var json=JsonDocument.Parse(body,new JsonDocumentOptions{AllowTrailingCommas=options.AllowTrailingCommas,CommentHandling=options.ReadCommentHandling,MaxDepth=options.MaxDepth});
            if(json.RootElement.ValueKind!=JsonValueKind.Object)throw InvalidRequest();var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in json.RootElement.EnumerateObject())if(!keys.Add(field.Name))throw InvalidRequest();
            var ids=json.RootElement.EnumerateObject().Where(field=>string.Equals(field.Name,IdName,!LegacyBody&&options.PropertyNameCaseInsensitive?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)).ToArray();
            if(ids.Length!=1||ids[0].Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))throw InvalidRequest();
            return JsonSerializer.Deserialize<long>(ids[0].Value.GetRawText(),options);
        }
        catch(Exception error)when(error is JsonException or FormatException or OverflowException){throw InvalidRequest();}
    }
    private static AgentTaskException InvalidRequest()=>new("YZP_INVALID_ACTION_REQUEST","记录写请求缺少有效ID或父上下文。",400,"not_applied","fix_request");
    private static AgentTaskException Denied()=>new("YZP_CAPABILITY_DENIED","原记录不存在或当前写操作无权访问。",403,"not_applied","fix_request");
}
