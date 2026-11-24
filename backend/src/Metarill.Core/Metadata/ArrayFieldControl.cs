namespace Metarill.Core.Metadata;

/// <summary>声明实际以 JSON 数组保存的原控件，普通文本不按内容猜测数组。</summary>
public static class ArrayFieldControl
{
    /// <summary>传统附件在回显时将历史坏值视为空。</summary>
    public static bool IsLegacyMedia(string kind) => kind is "Image" or "MultiImage" or "Upload" or "Video";

    /// <summary>控件和逻辑类型共同决定数据库文本是否存放数组。</summary>
    public static bool UsesJsonArray(string logicalType, string kind) =>
        logicalType.ToLowerInvariant() is ("string" or "text" or "json" or "varchar") &&
        (IsLegacyMedia(kind) || kind is "FileLibrary" or "MultiCheckBoxDatabase" or "MultiSelectPage");
}
