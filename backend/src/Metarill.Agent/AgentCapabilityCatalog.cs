using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace Metarill.Runtime.Agent;

public sealed record AgentCapabilityParameter(string Name, string Binding, bool Required, IReadOnlyDictionary<string, object?> Schema);
public sealed record AgentCapability(string Id, string Name, string Description, string Operation, string Path, string Method, string PageId, string? ButtonId,
    bool ConfirmationRequired, string ConfirmationMessage, IReadOnlyList<AgentCapabilityParameter> Parameters, IReadOnlyDictionary<string, object?> RequestSchema, IReadOnlyDictionary<string, object?> ResponseSchema);
internal sealed record IndexedCapability(AgentCapability Descriptor, ControllerActionDescriptor Action, string ProjectId, string Permission)
{
    public AgentActionBinding? BusinessAction { get; init; }
    public AgentRecordWriteBinding? RecordWrite {get;init;}
}

/// <summary>只编目MVC真实注册的生成控制器及显式业务动作，元数据按钮本身不代表已实现入口。</summary>
public sealed partial class GeneratedCapabilityIndex(IActionDescriptorCollectionProvider actions, IAgentHostResources registry)
{
    private readonly object _sync = new();
    private int _version = -1;
    private IReadOnlyList<IndexedCapability> _entries = [];
    [GeneratedRegex("^/?api/admin/auto/([a-zA-Z_][a-zA-Z0-9_]*)/([0-9]+)(?:/(.*))?$", RegexOptions.CultureInvariant)] private static partial Regex RoutePattern();

    /// <summary>ActionDescriptor版本变化才重建schema，权限始终在请求时重新过滤。</summary>
    internal IReadOnlyList<IndexedCapability> Entries()
    {
        var snapshot = actions.ActionDescriptors;
        lock (_sync)
        {
            if (_version == snapshot.Version) return _entries;
            var result = new List<IndexedCapability>();
            var registered = snapshot.Items.OfType<ControllerActionDescriptor>().ToArray();
            var custom = registered.Select(action => AgentActionBinding.Compile(action, registry.AgentCatalog, registered)).OfType<IndexedCapability>().ToArray();
            // 同标识的多路由/多声明不取第一条；歧义业务动作整组排除。
            result.AddRange(custom.GroupBy(entry => entry.Descriptor.Id, StringComparer.Ordinal).Where(group => group.Count() == 1).Select(group => group.Single()));
            foreach (var action in registered)
            {
                if (action.MethodInfo.GetCustomAttribute<AgentActionAttribute>() is not null) continue;
                if (!(action.ControllerTypeInfo.Namespace?.Contains(".Generated.Controllers", StringComparison.Ordinal) ?? false)) continue;
                var route = action.AttributeRouteInfo?.Template ?? ""; var match = RoutePattern().Match(route);
                if (!match.Success) continue;
                var page = registry.AgentCatalog.FindPage(match.Groups[2].Value); if (page is null) continue;
                if (page.TableName != match.Groups[1].Value) continue;
                var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Distinct().ToArray() ?? [];
                if (methods.Length != 1) continue;
                var method = methods[0].ToUpperInvariant(); var tail = match.Groups[3].Value;
                var operation = Operation(method, tail); if (operation is null) continue;
                var recordWrite=operation is "update" or "delete" or "copy"?AgentRecordWriteBinding.Compile(action,page,operation,registered):null;
                if(operation is "update" or "delete" or "copy"&&recordWrite is null)continue;
                var path = "/" + route.TrimStart('/')[4..];
                var id = "page:" + page.Id + ":" + operation + (operation == "options" ? ":" + tail : "");
                var parameters = new List<AgentCapabilityParameter>(); var requestSchema = new Dictionary<string, object?>();
                foreach (var parameter in action.Parameters.OfType<ControllerParameterDescriptor>())
                {
                    if (parameter.ParameterType == typeof(CancellationToken) || parameter.BindingInfo?.BindingSource == BindingSource.Services) continue;
                    var binding = parameter.BindingInfo?.BindingSource;
                    var kind = binding == BindingSource.Body ? "body" : binding == BindingSource.Query ? "query" : route.Contains("{" + parameter.Name + "}", StringComparison.Ordinal) ? "path" : null;
                    if (kind is null) continue;
                    // 原页面 HTTP 回传适配不扩大 Agent 输入，schema 仍来自生成器固定标注的严格 DTO。
                    var requestType = kind == "body" ? action.MethodInfo.GetCustomAttribute<LegacyPageWriteRequestAttribute>()?.RequestType : null;
                    var schema = AgentJsonSchema.Create(requestType ?? parameter.ParameterType);
                    if (kind == "body")
                    {
                        requestSchema = schema;
                        var required = new List<string>();
                        if (operation is "update" or "delete") required.Add("id");
                        if (operation == "create") required.AddRange(page.RequiredCreateFields);
                        if (operation == "import") required.Add("requestJson");
                        if (required.Count > 0) requestSchema["required"] = required.Distinct().ToArray();
                    }
                    parameters.Add(new(parameter.Name, kind, kind is "path" or "body" || parameter.Name == "parentId" && page.RequiresParent, schema));
                }
                var read = operation is "list" or "detail" or "count" or "options";
                var descriptor = new AgentCapability(id, page.Name + "：" + operation, "复用当前生成接口与服务端页面数据范围。", operation, path, method, page.Id, null, !read,
                    read ? "" : "此操作会写入数据或创建导出文件，请核对请求后明确确认。", parameters, requestSchema, AgentJsonSchema.Create(ReturnType(action.MethodInfo.ReturnType)));
                result.Add(new(descriptor, action, page.ProjectId, operation == "options" ? "list" : operation){RecordWrite=recordWrite});
            }
            if (result.Select(x => x.Descriptor.Id).Distinct().Count() != result.Count) throw new InvalidOperationException("真实生成路由出现重复能力标识。");
            _entries = result; _version = snapshot.Version; return _entries;
        }
    }
    private static string? Operation(string method, string tail) => (method, tail) switch
    {
        ("GET", "detail/{id}") => "detail", ("POST", "page") => "list", ("POST", "count") => "count", ("POST", "add") => "create", ("POST", "update") => "update",
        ("DELETE", "") => "delete", ("POST", "copy/{id}") => "copy", ("POST", "initExport") => "export",
        ("POST", var value) when value.StartsWith("importDatas", StringComparison.Ordinal) => "import",
        ("POST", var value) when value.StartsWith("findNameId", StringComparison.Ordinal) => "options", _ => null
    };
    private static Type ReturnType(Type type) { if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)) return type.GetGenericArguments()[0]; return type; }
}

/// <summary>目录按当前真实页权限和授权项目过滤，管理员也不会看到其他项目的能力。</summary>
public sealed class AgentCapabilityCatalog(GeneratedCapabilityIndex index, IRuntimeAuthorization authorization)
{
    public object List()
    {
        var actor = authorization.RequireActor(); return new { protocolVersion = 1, projectId = actor.ProjectId, capabilities = Allowed().Select(c => c.Descriptor).ToArray() };
    }
    public AgentCapability Describe(string id) => Allowed(id).SingleOrDefault(c => c.Descriptor.Id == id)?.Descriptor ?? throw new UnauthorizedAccessException("能力不存在或当前账号无权调用。");
    /// <summary>校验匹配的MVC动作和HTTP方法，客户端不能借目录标识调用其他路径。</summary>
    public AgentCapability RequireInvocation(string id, HttpContext context)
    {
        var found = Allowed(id).SingleOrDefault(c => c.Descriptor.Id == id) ?? throw new UnauthorizedAccessException("能力不存在或当前账号无权调用。");
        var actual = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (actual is null || actual.MethodInfo != found.Action.MethodInfo || actual.ControllerTypeInfo != found.Action.ControllerTypeInfo || actual.AttributeRouteInfo?.Template != found.Action.AttributeRouteInfo?.Template || context.Request.Method != found.Descriptor.Method)
            throw new UnauthorizedAccessException("能力标识与实际请求路径或方法不一致。");
        return found.Descriptor;
    }
    /// <summary>在台账登记及真实动作执行前，重新核验当前权限与真实记录范围。</summary>
    public async Task ValidateActionInvocationAsync(string id, HttpContext context, ReadOnlyMemory<byte> body)
    {
        RequireInvocation(id, context);
        var found = Allowed(id).SingleOrDefault(entry => entry.Descriptor.Id == id) ?? throw new UnauthorizedAccessException("业务动作未授权。");
        if (found.BusinessAction is null) throw new UnauthorizedAccessException("该能力不是已发布业务动作。");
        await found.BusinessAction.ValidateAsync(context, body, authorization);
    }
    /// <summary>普通记录写沿本次实际操作scope核验原记录和只读条件；不能借页面query角色替代写权限。</summary>
    public async Task ValidateRecordWriteInvocationAsync(string id,HttpContext context,ReadOnlyMemory<byte> body)
    {
        RequireInvocation(id,context);var found=Allowed(id).SingleOrDefault(entry=>entry.Descriptor.Id==id)??throw new UnauthorizedAccessException("记录写未授权。");
        var binding=found.RecordWrite??throw new UnauthorizedAccessException("缺少已发布的记录写读取合同。");
        await binding.ValidateAsync(context,body,authorization);
    }
    private IEnumerable<IndexedCapability> Allowed(string? capabilityId = null)
    {
        var actor = authorization.RequireActor();
        foreach (var entry in index.Entries())
        {
            if (entry.ProjectId != actor.ProjectId || capabilityId is not null && entry.Descriptor.Id != capabilityId) continue;
            try
            {
                if (entry.BusinessAction is { } action)
                {
                    authorization.RequireButtonPermission(entry.Descriptor.PageId, action.Button.Id);
                    if (action.Button.Visibility == "VisibleWhenWritable") authorization.RequirePagePermission(entry.Descriptor.PageId, "update");
                }
                else authorization.RequirePagePermission(entry.Descriptor.PageId, entry.Permission);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or NotSupportedException) { continue; }
            yield return entry;
        }
    }
}
