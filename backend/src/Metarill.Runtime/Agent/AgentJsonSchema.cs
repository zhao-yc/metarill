using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;

namespace Metarill.Runtime.Agent;

/// <summary>从真实DTO反射请求schema，可选包装只影响字段存在性，不能生成虚构输入参数。</summary>
internal static class AgentJsonSchema
{
    public static Dictionary<string, object?> Create(Type type, int depth = 0)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return WithNull(Create(nullable, depth));
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>)) return WithNull(Create(type.GetGenericArguments()[0], depth));
        if (type == typeof(object) || type == typeof(JsonElement) || depth > 5) return [];
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)) return new() { ["type"] = "string" };
        if (type == typeof(bool)) return new() { ["type"] = "boolean" };
        if (type == typeof(long) || type == typeof(ulong)) return new() { ["type"] = new[] { "string", "integer" }, ["format"] = "int64", ["pattern"] = "^-?[0-9]+$" };
        if (type.IsEnum || type == typeof(int) || type == typeof(uint) || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte)) return new() { ["type"] = "integer" };
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return new() { ["type"] = "number" };
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return new() { ["type"] = "string", ["format"] = "date-time", ["description"] = "兼容 yyyy-MM-dd HH:mm:ss 日期时间。" };
        if (type == typeof(DateOnly)) return new() { ["type"] = "string", ["format"] = "date" };
        if (type == typeof(TimeOnly) || type == typeof(TimeSpan)) return new() { ["type"] = "string", ["format"] = "time" };
        var dictionary = type.GetInterfaces().Append(type).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() is var definition && (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)));
        if (dictionary is not null) return new() { ["type"] = "object", ["additionalProperties"] = Create(dictionary.GetGenericArguments()[1], depth + 1) };
        var enumerable = type.GetInterfaces().Append(type).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (type.IsArray || enumerable is not null) return new() { ["type"] = "array", ["items"] = Create(type.IsArray ? type.GetElementType()! : enumerable!.GetGenericArguments()[0], depth + 1) };
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal); var required = new List<string>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always) continue;
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            // 固定CheckBox属性保留旧数字模型及现有bool输入；其余bool仍是普通boolean。
            properties[name] = property.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType == typeof(CheckboxBooleanJsonConverter)
                ? new Dictionary<string, object?> { ["anyOf"] = new object[] { new Dictionary<string, object?> { ["type"] = "boolean" }, new Dictionary<string, object?> { ["type"] = "integer", ["enum"] = new[] { 0, 1 } }, new Dictionary<string, object?> { ["type"] = "string", ["pattern"] = @"^(?:0|1|\s*)$" }, new Dictionary<string, object?> { ["type"] = "null" } } }
                : Create(property.PropertyType, depth + 1);
            if (property.GetCustomAttribute<RequiredAttribute>() is not null || property.GetCustomAttribute<JsonRequiredAttribute>() is not null) required.Add(name);
        }
        var result = new Dictionary<string, object?> { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = type.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>()?.UnmappedMemberHandling != JsonUnmappedMemberHandling.Disallow };
        if (required.Count > 0) result["required"] = required;
        return result;
    }
    private static Dictionary<string, object?> WithNull(Dictionary<string, object?> schema)
    {
        if (schema.TryGetValue("type", out var type)) schema["type"] = type is string text ? new[] { text, "null" } : ((IEnumerable<string>)type!).Append("null").Distinct().ToArray();
        return schema;
    }
}
