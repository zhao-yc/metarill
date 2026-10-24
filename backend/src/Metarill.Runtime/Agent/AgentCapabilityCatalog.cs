using System.Reflection;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace Metarill.Runtime.Agent;

public sealed record AgentCapabilityParameter(string Name, string Binding, bool Required, IReadOnlyDictionary<string, object?> Schema);
public sealed record AgentCapability(string Id, string Name, string Description, string Operation, string Path, string Method, string PageId, string? ButtonId,
    bool ConfirmationRequired, string ConfirmationMessage, IReadOnlyList<AgentCapabilityParameter> Parameters, IReadOnlyDictionary<string, object?> RequestSchema, IReadOnlyDictionary<string, object?> ResponseSchema);
internal sealed record IndexedCapability(AgentCapability Descriptor, ControllerActionDescriptor Action, string ProjectId, string Permission);

/// <summary>只编目MVC真实注册的生成控制器，不能根据元数据开关宣称不存在的按钮或SQL入口可调用。</summary>
public sealed partial class GeneratedCapabilityIndex(IActionDescriptorCollectionProvider actions, MetadataRegistry registry)
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
            foreach (var action in snapshot.Items.OfType<ControllerActionDescriptor>())
            {
                if (!(action.ControllerTypeInfo.Namespace?.Contains(".Generated.Controllers", StringComparison.Ordinal) ?? false)) continue;
                var route = action.AttributeRouteInfo?.Template ?? ""; var match = RoutePattern().Match(route);
                if (!match.Success) continue;
                var page = registry.FindPage(match.Groups[2].Value); if (page is null) continue;
                var catalog = new[] { registry.Platform, registry.Demo }.First(c => c.ConfigPages.Any(p => p.Id == page.Id));
                var table = catalog.ConfigTables.Single(t => t.Id == page.ConfigTableId);
                if (table.TableName != match.Groups[1].Value) continue;
                var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Distinct().ToArray() ?? [];
                if (methods.Length != 1) continue;
                var method = methods[0].ToUpperInvariant(); var tail = match.Groups[3].Value;
                var operation = Operation(method, tail); if (operation is null) continue;
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
                        if (operation == "create")
                            foreach (var field in catalog.ConfigPageColumns.Where(f => f.ConfigPageId == page.Id && f.IsEdit == 1 && f.IsRequired == 1))
                            {
                                var column = catalog.ConfigColumns.Single(c => c.Id == field.ConfigColumnId);
                                if (column.DefaultValue.Length == 0) required.Add(MetadataRegistry.Camel(column.ColumnName));
                            }
                        if (operation == "import") required.Add("requestJson");
                        if (required.Count > 0) requestSchema["required"] = required.Distinct().ToArray();
                    }
                    parameters.Add(new(parameter.Name, kind, kind is "path" or "body" || parameter.Name == "parentId" && page.WhereSql.Contains("@parent", StringComparison.OrdinalIgnoreCase), schema));
                }
                var read = operation is "list" or "detail" or "count" or "options";
                var descriptor = new AgentCapability(id, page.Name + "：" + operation, "复用当前生成接口与服务端页面数据范围。", operation, path, method, page.Id, null, !read,
                    read ? "" : "此操作会写入数据或创建导出文件，请核对请求后明确确认。", parameters, requestSchema, AgentJsonSchema.Create(ReturnType(action.MethodInfo.ReturnType)));
                result.Add(new(descriptor, action, catalog.YzProjectId, operation == "options" ? "list" : operation));
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
    public AgentCapability Describe(string id) => Allowed().SingleOrDefault(c => c.Descriptor.Id == id)?.Descriptor ?? throw new UnauthorizedAccessException("能力不存在或当前账号无权调用。");
    /// <summary>校验匹配的MVC动作和HTTP方法，客户端不能借目录标识调用其他路径。</summary>
    public AgentCapability RequireInvocation(string id, HttpContext context)
    {
        var found = Allowed().SingleOrDefault(c => c.Descriptor.Id == id) ?? throw new UnauthorizedAccessException("能力不存在或当前账号无权调用。");
        var actual = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (actual is null || actual.MethodInfo != found.Action.MethodInfo || actual.ControllerTypeInfo != found.Action.ControllerTypeInfo || actual.AttributeRouteInfo?.Template != found.Action.AttributeRouteInfo?.Template || context.Request.Method != found.Descriptor.Method)
            throw new UnauthorizedAccessException("能力标识与实际请求路径或方法不一致。");
        return found.Descriptor;
    }
    private IEnumerable<IndexedCapability> Allowed()
    {
        var actor = authorization.RequireActor();
        foreach (var entry in index.Entries())
        {
            if (entry.ProjectId != actor.ProjectId) continue;
            try { authorization.RequirePagePermission(entry.Descriptor.PageId, entry.Permission); }
            catch (UnauthorizedAccessException) { continue; }
            yield return entry;
        }
    }
}
