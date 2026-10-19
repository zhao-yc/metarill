using System.Text.Json;

namespace Metarill.Core.Metadata;

/// <summary>统一旧 isUpdate 与 C# isSave，防止反序列化默认值开启旧禁用页面。</summary>
public static class PageUpdatePolicy
{
    /// <summary>旧显式 isUpdate 优先；IsSave 默认值不能证明来自用户配置，不据此制造冲突。</summary>
    public static bool CanUpdate(PageMetadata page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Extra.TryGetValue("isUpdate", out var value))
        {
            if (value.ValueKind == JsonValueKind.Null) return Flag(page.IsSave);
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number && int.TryParse(value.ToString(), out var flag)) return Flag(flag);
            throw new InvalidOperationException("页面 isUpdate 必须为 0 或 1。");
        }
        return Flag(page.IsSave);
    }

    /// <summary>未知开关不能悄悄降为可写或只读。</summary>
    private static bool Flag(int value) => value switch { 0 => false, 1 => true, _ => throw new InvalidOperationException("页面更新开关必须为 0 或 1。") };
}
