using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>选择控件的已知有效配置；页级只覆盖给出的非空标量键，原字段定义始终保留。</summary>
internal sealed record SelectionControlDefinition(string PageId,string NameField,string ButtonText,string WhereSql,string OrderBy)
{
    /// <summary>与既有Multi编译合同一致地合并字段和页面；不执行未知JSON或改变元数据。</summary>
    internal static SelectionControlDefinition Resolve(ColumnModel column,PageColumnMetadata? settings)
    {
        var source=Read(column.Source.ControlDetail);
        var overlay=Read(settings is null?"":OriginalDetailContext.Text(settings.Extra,"controlDetail"));
        string Value(string key,string fallback="")
        {
            var value=Scalar(overlay,key);if(string.IsNullOrWhiteSpace(value))value=Scalar(source,key);
            return string.IsNullOrWhiteSpace(value)?fallback:value;
        }
        return new(Value("pageId",column.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString()),Value("nameField","name"),Value("buttonText","选择"),Value("whereSql"),Value("orderBy"));
    }
    /// <summary>重复键及非对象必须拒绝，未知合法键留在原定义而不参与路径/SQL解释。</summary>
    private static JsonElement Read(string text)
    {
        using var document=JsonDocument.Parse(string.IsNullOrWhiteSpace(text)?"{}":text);var value=document.RootElement;
        if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(property=>property.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw new InvalidOperationException("选择页配置必须为无重复键的JSON对象。");
        return value.Clone();
    }
    /// <summary>只解释已知标量；缺省/null/空值继承来源，不把复杂对象转作字段或路由。</summary>
    private static string Scalar(JsonElement source,string key)
    {
        if(!source.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null)return "";
        if(value.ValueKind is not(JsonValueKind.String or JsonValueKind.Number))throw new InvalidOperationException("选择页已知配置必须为标量："+key);
        return value.ToString();
    }
}
