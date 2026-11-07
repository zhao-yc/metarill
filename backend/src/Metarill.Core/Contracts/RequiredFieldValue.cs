using System.Text.Json;
namespace Metarill.Core.Contracts;

/// <summary>页面必填按值语义检查，不把数据库NOT NULL等同于NotBlank。</summary>
public static class RequiredFieldValue
{
    /// <summary>多值必填不接受null、空白或空数组；无效形状是输入错误，不是非空选择。</summary>
    public static bool IsEmptyArray(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return true;
        try
        {
            using var document=JsonDocument.Parse(value);if(document.RootElement.ValueKind==JsonValueKind.Null)return true;
            if(document.RootElement.ValueKind!=JsonValueKind.Array)throw new ArgumentException("必填多值字段必须是数组。");return document.RootElement.GetArrayLength()==0;
        }
        catch(JsonException error){throw new ArgumentException("必填多值字段不是有效JSON数组。",error);}
    }
}
