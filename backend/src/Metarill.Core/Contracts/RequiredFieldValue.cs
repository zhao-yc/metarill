using System.Text.Json;
namespace Metarill.Core.Contracts;

/// <summary>页面必填按值语义检查，不把数据库NOT NULL等同于NotBlank。</summary>
public static class RequiredFieldValue
{
    /// <summary>与原详情一致，只对已声明的JSON根解码历史文本。无效或非对象值按缺少路径处理。</summary>
    public static JsonElement? VisibilityObject(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        try
        {
            using var document=JsonDocument.Parse(value);
            return document.RootElement.ValueKind==JsonValueKind.Object?document.RootElement.Clone():null;
        }
        catch(JsonException){return null;}
    }

    /// <summary>多值必填不接受null、空白或空数组；无效形状是输入错误，不是非空选择。</summary>
    public static bool IsEmptyArray(string? value) =>
        VisibilityArray(value) is not { } array || array.GetArrayLength() == 0;

    /// <summary>按已声明的数组形状读取持久值，坏数据不能冒充空选项。</summary>
    public static JsonElement? VisibilityArray(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var array = JsonSerializer.Deserialize<JsonElement>(value);
            if (array.ValueKind == JsonValueKind.Null) return null;
            if (array.ValueKind != JsonValueKind.Array) throw new ArgumentException("必填多值字段必须是数组。");
            return array;
        }
        catch (JsonException error) { throw new ArgumentException("必填多值字段不是有效JSON数组。", error); }
    }
}
