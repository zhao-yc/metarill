using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Metarill.Runtime.Mcp;

/// <summary>仅由宿主从可信发布目录与实际生成程序集构造；不接受MCP请求中的类型名或程序集路径。</summary>
public sealed record McpPublishedApplication(MetadataCatalog Metadata,string RootNamespace,Assembly Assembly);
/// <summary>只提供当前身份可调用的稳定页面标识，不输出连接、SQL、隐藏默认值或完整元数据。</summary>
public sealed record McpPublishedPage(string ProjectId,string Id,string Name,string TableName,IReadOnlyList<string> Operations);

/// <summary>描述call_generated_page的data结构；不包含DTO默认值或完整元数据。</summary>
public sealed record McpPageRequest(string PageId,string Operation,Dictionary<string,object?> DataSchema,string ParentIdLocation,bool RequiresConfirmation,string Notes);

/// <summary>平台与独立应用共用固定Service分派；反射只选择已发布类型，查询与写入仍由生成Service执行。</summary>
public sealed class PublishedPageInvoker
{
    private static readonly string[] Operations=["list","detail","create","update","delete","copy"];
    private sealed record Page(string ProjectId,string Id,string Name,string TableName,string RootNamespace,Assembly Assembly)
    {
        internal string TypeName=>string.Concat(TableName.Split('_').Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
        internal Type? Service=>Assembly.GetType(RootNamespace+".Generated.Services."+TypeName+"Service");
        /// <summary>输入只能使用同程序集对应页面的固定强DTO。</summary>
        internal Type? Request(string operation)=>Assembly.GetType(RootNamespace+".Generated.DTOs."+TypeName+"Page"+Id+(operation=="create"?"Create":"Update")+"Request");
        /// <summary>六动作映射到固定生成命名，不把客户端operation直接作为方法名。</summary>
        internal string Method(string operation)=>(operation switch{"list"=>"Page","detail"=>"Detail","create"=>"Create","update"=>"Update","delete"=>"Delete","copy"=>"Copy",_=>throw new ArgumentException("不支持的页面操作。")})+Id+"Async";
    }
    private readonly FrozenDictionary<string,Page> pages;

    /// <summary>冻结最小发布声明；业务JSON不能在请求时增加页面、项目、命名空间或任意方法。</summary>
    public PublishedPageInvoker(IEnumerable<McpPublishedApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);var values=new Dictionary<string,Page>(StringComparer.Ordinal);
        foreach(var application in applications)
        {
            ArgumentNullException.ThrowIfNull(application);MetadataValidation.Validate(application.Metadata);
            if(application.RootNamespace.Length>200||!Regex.IsMatch(application.RootNamespace,@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$"))throw new ArgumentException("生成程序集命名空间无效。");
            foreach(var page in application.Metadata.ConfigPages)
            {
                var table=application.Metadata.ConfigTables.Single(table=>table.Id==page.ConfigTableId);
                if(!values.TryAdd(page.Id,new(application.Metadata.YzProjectId,page.Id,page.Name,table.TableName,application.RootNamespace,application.Assembly)))throw new InvalidOperationException("MCP发布目录存在重复页面ID。");
            }
        }
        pages=values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>每次按当前身份过滤可用动作；列表本身不授予调用权限，实际执行仍再次校验。</summary>
    public IReadOnlyList<McpPublishedPage> List(IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities)
    {
        var (actor,identity)=Identity(authorization,identities);var result=new List<McpPublishedPage>();
        foreach(var page in pages.Values.OrderBy(page=>page.Id,StringComparer.Ordinal))
        {
            if(identity.GrantId is not null&&page.ProjectId!=actor.ProjectId)continue;
            var allowed=new List<string>();
            foreach(var operation in Operations)
            {
                if(page.Service?.GetMethod(page.Method(operation),BindingFlags.Instance|BindingFlags.Public) is null||operation is "create" or "update"&&page.Request(operation) is null)continue;
                try{authorization.RequirePagePermission(page.Id,operation);}
                catch(Exception error)when(error is UnauthorizedAccessException or SecurityException){continue;}
                if(identity.GrantId is null||operation is "list" or "detail")allowed.Add(operation);
            }
            if(allowed.Count>0)result.Add(new(page.ProjectId,page.Id,page.Name,page.TableName,allowed.AsReadOnly()));
        }
        return result.AsReadOnly();
    }

    /// <summary>逐次验证身份、动作页权与实际发布方法，再描述DTO；不构造Service或读取数据库。</summary>
    public McpPageRequest Describe(string pageId,string operation,IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities)
    {
        var (actor,identity)=Identity(authorization,identities);
        if(!Operations.Contains(operation,StringComparer.Ordinal))throw new ArgumentException("不支持的页面操作。");
        authorization.RequirePagePermission(pageId,operation);
        var page=pages.GetValueOrDefault(pageId)??throw new InvalidOperationException("页面尚未发布。");
        if(identity.GrantId is not null&&(page.ProjectId!=actor.ProjectId||operation is not("list" or "detail")))throw new UnauthorizedAccessException("Agent仅能描述已批准项目的MCP读取动作。");
        if(page.Service?.GetMethod(page.Method(operation),BindingFlags.Instance|BindingFlags.Public) is null)throw new InvalidOperationException("页面未开放该操作。");
        Dictionary<string,object?> schema;
        if(operation=="list")schema=RequestJsonSchema.Create(typeof(PageRequest),includeNullableReferences:true);
        else if(operation is "create" or "update")schema=RequestJsonSchema.Create(page.Request(operation)??throw new InvalidOperationException("页面未开放该写入。"),includeNullableReferences:true);
        else schema=new(){["type"]="object",["properties"]=new Dictionary<string,object?>{["id"]=RequestJsonSchema.Create(typeof(long))},["additionalProperties"]=true};
        // 记录操作沿实际正整数ID解析；更新ID必填，业务字段必填/默认值仍由原Service判断。
        if(operation is not("list" or "create"))schema["required"]=schema.TryGetValue("required",out var required)?((IEnumerable<string>)required!).Append("id").Distinct().ToArray():new[]{"id"};
        return new(pageId,operation,schema,operation=="list"?"data.parentId":"parentId",operation is "create" or "update" or "delete" or "copy","结构来自实际DTO；省略保持，null清空。业务必填、默认值、关系与对象权限仍由原Service校验；记录ID须为正整数。");
    }

    /// <summary>沿原六操作/parent/强DTO合同执行；Agent只读，不给MCP另设弱写入通道或任意SQL入口。</summary>
    public async Task<object?> InvokeAsync(string pageId,string operation,JsonElement data,bool confirmed,IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities,IServiceProvider services,CancellationToken token,string? parentId=null)
    {
        var (actor,identity)=Identity(authorization,identities);authorization.RequirePagePermission(pageId,operation);
        var page=pages.GetValueOrDefault(pageId)??throw new InvalidOperationException("页面尚未发布。");
        if(identity.GrantId is not null&&page.ProjectId!=actor.ProjectId)throw new UnauthorizedAccessException("MCP页面不属于已批准的Agent项目。");
        var writing=operation is "create" or "update" or "delete" or "copy";
        if(writing&&!confirmed)throw new InvalidOperationException("写入前必须检查请求并明确确认。");
        if(writing&&identity.GrantId is not null)throw new UnauthorizedAccessException("Agent 写请求须通过带请求任务保护的能力入口执行。");
        var serviceType=page.Service??throw new InvalidOperationException("服务尚未发布。");string name;object?[] arguments;
        if(operation is "list" or "detail" or "delete" or "copy")
        {
            if(operation=="list"){name=page.Method(operation);arguments=[data.Deserialize<PageRequest>(JsonDefaults.Options)??throw new ArgumentException("分页请求为空。"),actor,token];}
            else
            {
                if(!data.TryGetProperty("id",out var key)||!long.TryParse(key.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0)throw new ArgumentException("请求缺少合法标识。");
                name=page.Method(operation);arguments=[id,parentId,actor,token];
            }
        }
        else if(operation is "create" or "update")
        {
            name=page.Method(operation);var dto=page.Request(operation)??throw new InvalidOperationException("页面未开放该写入。");
            arguments=[data.Deserialize(dto,JsonDefaults.Options)??throw new ArgumentException("请求为空。"),parentId,actor,token];
        }
        else throw new ArgumentException("不支持的页面操作。");
        var method=serviceType.GetMethod(name,BindingFlags.Instance|BindingFlags.Public)??throw new InvalidOperationException("页面未开放该操作。");
        // 只有身份、页权、确认、类型和参数全部通过才解析Service；不直接读Mapper或拼SQL。
        var task=(Task)method.Invoke(services.GetRequiredService(serviceType),arguments)!;await task.ConfigureAwait(false);return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    /// <summary>两种授权接口必须指向同一次已认证持久身份，不能混入开发身份或客户端自报actor。</summary>
    private static (RuntimeActor Actor,AuthenticatedIdentity Identity) Identity(IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities)
    {
        var actor=authorization.RequireActor();var identity=identities.RequireIdentity();if(identity.SessionId<=0||identity.Actor!=actor)throw new UnauthorizedAccessException("MCP需要当前真实会话身份。");return (actor,identity);
    }
}
