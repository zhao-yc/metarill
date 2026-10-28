using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Metarill.Runtime.Agent;

/// <summary>显式动作只绑定MVC真实DTO与生成器发布的实体读取入口，拒绝任意回调或客户端范围。</summary>
internal sealed record AgentActionBinding(AgentPublishedButton Button, Type BodyType, PropertyInfo? RecordProperty,
    string? RecordBinding, string RecordName, PropertyInfo? ParentProperty, string? ParentBinding, string ParentName,
    GeneratedPageAccessAttribute? Access, MethodInfo? Reader)
{
    /// <summary>只收录唯一POST、严格对象请求和同路径按钮；不按方法名猜测业务能力。</summary>
    public static IndexedCapability? Compile(ControllerActionDescriptor action, AgentHostCatalog catalog,
        IReadOnlyList<ControllerActionDescriptor> registered)
    {
        var declaration = action.MethodInfo.GetCustomAttribute<AgentActionAttribute>();
        if (declaration is null || !ValidId(declaration.Id) || string.IsNullOrWhiteSpace(declaration.Name)) return null;
        var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(value => value.HttpMethods).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var route = action.AttributeRouteInfo?.Template ?? "";
        if (methods.Length != 1 || methods[0] != "POST" || !route.TrimStart('/').StartsWith("api/", StringComparison.Ordinal)) return null;
        var path = "/" + route.TrimStart('/')[4..]; var button = catalog.FindButton(declaration.ButtonId);
        if (button is null || !AgentHostCatalog.IsApplicationPath(path) || button.Path != path) return null;
        var parameters = action.Parameters.OfType<ControllerParameterDescriptor>()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken) && parameter.BindingInfo?.BindingSource != BindingSource.Services).ToArray();
        var bodies = parameters.Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body).ToArray();
        if (bodies.Length != 1 || !TypedRequest(bodies[0].ParameterType)) return null;
        var body = bodies[0]; var schema = AgentJsonSchema.Create(body.ParameterType);
        var bindings = new List<(string Name, string Binding, PropertyInfo? Property)>();
        foreach (var property in body.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is null || property.SetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0
                || property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always) continue;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            bindings.Add((name, "body", property));
        }
        var published = new List<AgentCapabilityParameter> { new(body.Name, "body", true, schema) };
        foreach (var parameter in parameters.Where(parameter => parameter != body))
        {
            var source = parameter.BindingInfo?.BindingSource;
            var kind = source == BindingSource.Path ? "path" : source == BindingSource.Query ? "query" : null;
            if (kind is null || !ScalarIdentifier(parameter.ParameterType)) return null;
            var name = parameter.BindingInfo?.BinderModelName ?? parameter.Name;
            bindings.Add((name, kind, null));
            published.Add(new(name, kind, kind == "path" || name == "parentId" && button.Page.RequiresParent, AgentJsonSchema.Create(parameter.ParameterType)));
        }
        var record = bindings.Where(binding => binding.Name == declaration.RecordIdField).ToArray();
        var parent = bindings.Where(binding => binding.Name == "parentId").ToArray();
        if (record.Length > 1 || parent.Length > 1 || !button.Page.IsFunctionTable && (record.Length != 1 || string.IsNullOrWhiteSpace(declaration.RecordIdField))
            || button.Page.RequiresParent && parent.Length != 1) return null;
        if (record.Any(binding => binding.Property is not null && !ScalarIdentifier(binding.Property.PropertyType))
            || parent.Any(binding => binding.Property is not null && !ScalarIdentifier(binding.Property.PropertyType))) return null;
        GeneratedPageAccessAttribute? access = null; MethodInfo? reader = null;
        if (!button.Page.IsFunctionTable)
        {
            var sources = registered.Select(value => value.ControllerTypeInfo.AsType()).Distinct()
                .Where(type => type.Namespace?.Contains(".Generated.Controllers", StringComparison.Ordinal) == true)
                .Select(type => (Type: type, Access: type.GetCustomAttribute<GeneratedPageAccessAttribute>()))
                .Where(value => value.Access?.PageId == button.Page.Id).ToArray();
            if (sources.Length != 1) return null;
            var source = sources[0]; access = source.Access!;
            if (access.TableId != button.Page.TableId || access.ServiceType.Assembly != source.Type.Assembly || access.EntityType.Assembly != source.Type.Assembly
                || source.Type.GetConstructors().Length != 1 || source.Type.GetConstructors()[0].GetParameters().Count(parameter => parameter.ParameterType == access.ServiceType) != 1
                || access.ReadMethod != "ReadButtonRecord" + button.Page.Id + "Async") return null;
            reader = access.ServiceType.GetMethod(access.ReadMethod, BindingFlags.Public | BindingFlags.Instance, [typeof(long), typeof(string), typeof(string), typeof(RuntimeActor), typeof(CancellationToken)]);
            if (reader is null || reader.ReturnType != typeof(Task<>).MakeGenericType(access.EntityType)) return null;
        }
        var recordSource = record.SingleOrDefault(); var parentSource = parent.SingleOrDefault();
        var required = schema.GetValueOrDefault("required") is IEnumerable<string> existing ? existing.ToList() : [];
        if (!button.Page.IsFunctionTable && recordSource.Binding == "body") required.Add(declaration.RecordIdField);
        if (button.Page.RequiresParent && parentSource.Binding == "body") required.Add("parentId");
        if (required.Count > 0) schema["required"] = required.Distinct(StringComparer.Ordinal).ToArray();
        var binding = new AgentActionBinding(button, body.ParameterType, recordSource.Property, recordSource.Binding, declaration.RecordIdField,
            parentSource.Property, parentSource.Binding, "parentId", access, reader);
        var descriptor = new AgentCapability("action:" + declaration.Id, declaration.Name, declaration.Description, "action", path, "POST",
            button.Page.Id, button.Id, button.ConfirmationRequired, button.ConfirmationMessage, published.AsReadOnly(), schema,
            AgentJsonSchema.Create(ReturnType(action.MethodInfo.ReturnType)));
        return new(descriptor, action, button.Page.ProjectId, "button") { BusinessAction = binding };
    }

    /// <summary>使用宿主MVC相同JSON配置绑定实际DTO，再从当前请求及生成记录入口证明范围。</summary>
    public async Task ValidateAsync(HttpContext context, ReadOnlyMemory<byte> body, IRuntimeAuthorization authorization)
    {
        if (!context.Request.HasJsonContentType()) throw InvalidRequest();
        var options = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        object value;
        try { value = JsonSerializer.Deserialize(body.Span, BodyType, options) ?? throw InvalidRequest(); }
        catch (JsonException) { throw InvalidRequest(); }
        var parent = ReadValue(context, value, ParentProperty, ParentBinding, ParentName, options);
        if (Button.Page.RequiresParent && string.IsNullOrWhiteSpace(parent)) throw InvalidRequest();
        if (Button.Page.IsFunctionTable) return;
        var raw = ReadValue(context, value, RecordProperty, RecordBinding, RecordName, options);
        if (!long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) throw InvalidRequest();
        // 每次解析当前scope内真实Service；不能随意选第一个注册项或把bool回调视为记录证明。
        var services = context.RequestServices.GetServices(Access!.ServiceType).ToArray();
        if (services.Length != 1 || services[0] is null) throw new InvalidOperationException("业务动作要求唯一的已发布生成Service。");
        Task task;
        try { task = (Task)Reader!.Invoke(services[0], [id, parent, Button.Id, authorization.RequireActor(), context.RequestAborted])!; }
        catch (TargetInvocationException error) when (error.InnerException is UnauthorizedAccessException or SecurityException)
        { throw new UnauthorizedAccessException("业务记录未授权。"); }
        try { await task; }
        catch (SecurityException) { throw new UnauthorizedAccessException("业务记录未授权。"); }
        if (task.GetType().GetProperty("Result")?.GetValue(task) is null) throw new UnauthorizedAccessException("业务记录不存在或无权访问。");
    }
    /// <summary>拒绝多值/冲突来源，DTO使用实际JSON绑定名，不能校验一条记录后执行另一条。</summary>
    private static string? ReadValue(HttpContext context, object body, PropertyInfo? property, string? binding, string name, JsonSerializerOptions options)
    {
        object? value = null;
        if (binding == "body")
        {
            if (property is null || options.GetTypeInfo(body.GetType()).Properties.Count(field => field.Name == name && field.AttributeProvider is PropertyInfo info && info == property && field.Set is not null) != 1) throw InvalidRequest();
            value = property.GetValue(body);
        }
        else if (binding == "path") value = context.Request.RouteValues.GetValueOrDefault(name);
        else if (binding == "query")
        {
            var values = context.Request.Query[name]; if (values.Count > 1) throw InvalidRequest(); value = values.Count == 1 ? values[0] : null;
        }
        return value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
    /// <summary>记录与父标识只接受现有整数/字符串协议，不把复杂对象转成标识。</summary>
    private static bool ScalarIdentifier(Type type) => (Nullable.GetUnderlyingType(type) ?? type) is var actual && (actual == typeof(string) || actual == typeof(long) || actual == typeof(int));
    /// <summary>明确严格对象DTO才有稳定schema；Entity、字典和原始正文不能作为业务声明。</summary>
    private static bool TypedRequest(Type type) => type.IsClass && !type.IsAbstract && type != typeof(string) && !type.IsArray
        && !typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(object)
        && !(type.Namespace?.Contains(".Entities", StringComparison.Ordinal) ?? false)
        && type.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>()?.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow;
    /// <summary>稳定能力标识不含路径、控制符或任意扩展文本。</summary>
    private static bool ValidId(string id) => id.Length is > 0 and <= 180 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    /// <summary>异步返回按实际响应类型描述，不公开Task内部成员。</summary>
    private static Type ReturnType(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GetGenericArguments()[0] : type;
    /// <summary>所有输入绑定拒绝均发生在台账与业务执行前。</summary>
    private static AgentTaskException InvalidRequest() => new("YZP_INVALID_ACTION_REQUEST", "业务动作请求缺少有效记录、父上下文或严格JSON参数。", 400, "not_applied", "fix_request");
}
