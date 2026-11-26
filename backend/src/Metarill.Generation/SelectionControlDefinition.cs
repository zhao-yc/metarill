using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>选择控件的已知有效配置，沿平台保存共用的页级继承合同读取。</summary>
internal sealed record SelectionControlDefinition(string PageId,string NameField,string ButtonText,string WhereSql,string OrderBy)
{
    /// <summary>从生成页字段提取原覆盖，保持生成器使用的有效控件配置。</summary>
    internal static SelectionControlDefinition Resolve(ColumnModel column,PageColumnMetadata? settings)
    {
        var definition = SelectionControlDetail.Resolve(column.Source.ControlDetail,
            settings is null ? null : OriginalDetailContext.Text(settings.Extra, "controlDetail"),
            column.Source.Extra.GetValueOrDefault("linkConfigPageId").ToString());
        return new(definition.PageId, definition.NameField, definition.ButtonText, definition.WhereSql, definition.OrderBy);
    }
}
