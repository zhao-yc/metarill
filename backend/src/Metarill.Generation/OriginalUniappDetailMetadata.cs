using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>原v8详情页编译上下文；所有布局值均从页面/字段原元数据读取，不修改共享模型。</summary>
internal sealed class OriginalDetailContext(GenerationModel model, TableModel table, PageModel page, IReadOnlyList<ChildModel> children)
{
    internal GenerationModel Model { get; } = model;
    internal TableModel Table { get; } = table;
    internal PageModel Page { get; } = page;
    internal IReadOnlyList<ChildModel> Children { get; } = children;
    internal string Api => "/admin/auto/" + Table.Source.TableName + "/" + Page.Source.Id;
    internal int GridColumns => Math.Max(1, Integer(Page.Source.Extra, "cols", 2) <= 0 ? 2 : Integer(Page.Source.Extra, "cols", 2));
    internal string PrimaryColor => Model.Catalog.PrimaryColor;
    internal StringBuilder Fields { get; } = new();
    internal StringBuilder Defaults { get; } = new();
    internal StringBuilder Rules { get; } = new();
    internal StringBuilder Readonly { get; } = new();
    internal StringBuilder Computed { get; } = new();
    internal StringBuilder Methods { get; } = new();
    internal StringBuilder Watches { get; } = new();
    internal StringBuilder OptionWatchers { get; } = new();
    internal StringBuilder Data { get; } = new();
    internal StringBuilder Init { get; } = new();
    internal StringBuilder DynamicInit { get; } = new();
    internal StringBuilder Images { get; } = new();
    internal StringBuilder Imports { get; } = new();
    internal StringBuilder Components { get; } = new();
    internal StringBuilder Popups { get; } = new();
    internal List<OriginalDetailField> FieldsMetadata { get; } = [];
    internal Dictionary<string, string> ImportedComponents { get; } = new(StringComparer.Ordinal);
    internal List<string> Mixins { get; } = [];

    /// <summary>页面控件明细为空才继承字段明细，与原PageColumnSettingsResolver一致。</summary>
    internal OriginalDetailField Field(ColumnModel column)
    {
        var settings = Model.Catalog.ConfigPageColumns.Single(item => item.ConfigPageId == Page.Source.Id && item.ConfigColumnId == column.Source.Id);
        var detail = Text(settings.Extra, "controlDetail"); if (detail.Length == 0) detail = column.Source.ControlDetail;
        var @default = settings.Extra.TryGetValue("defaultValue", out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : column.Source.DefaultValue;
        var span = Integer(column.Source.Extra, "colSpan", 1); span = Math.Min(GridColumns, Math.Max(span, 1));
        var derived=Derived(column);
        // 原冗余页面按页级只读标志生成原输入/校验；派生列的服务端写保护不能改变原DOM。
        var originalJoinInput = Table.Source.IsRongyu == 1 && Enabled(column.Source.Extra, "isJoinColumn") && !column.IsSystem;
        var readOnly = derived || (originalJoinInput ? Enabled(settings.Extra, "isReadOnly") : !Page.Writable.Any(item => item.Source.Id == column.Source.Id));
        return new(column, settings, detail, @default, readOnly, !derived && (settings.IsRequired == 1 || Enabled(settings.Extra, "isMust")), Text(settings.Extra, "detailGroupKey"), span,derived);
    }
    /// <summary>移植原非冗余字段展示归属；联接、审计名称、布局及级联祖先不生成输入/校验/自动保存。</summary>
    private bool Derived(ColumnModel column)
    {
        if(Table.Source.IsRongyu!=0)return false;
        if(Enabled(column.Source.Extra,"isJoinColumn") || column.Source.ColumnName is "insert_manager_real_name" or "update_manager_real_name" or "insert_user_name" or "update_user_name" || column.Source.ControlKind is "Title" or "Tab")return true;
        foreach(var cascade in Table.Columns.Where(item=>item.Source.ControlKind=="CascadeTree"&&item.Source.Id!=column.Source.Id))
        {
            var definition=Object(cascade.Source.ControlDetail,"级联");
            var stem=cascade.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)?cascade.Source.ColumnName[..^3]:cascade.Source.ColumnName;
            if(column.JsonName==GenerationModel.Camel(stem+"_name"))return true;
            if(!definition.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array)continue;
            foreach(var level in levels.EnumerateArray())
            {
                var prefix=Property(level,"columnPrefix",Property(level,"table"));
                if(column.JsonName==Property(level,"idPropertyName",GenerationModel.Camel(prefix+"_id"))||column.JsonName==Property(level,"namePropertyName",GenerationModel.Camel(prefix+"_name")))return true;
            }
        }
        return false;
    }
    /// <summary>导入固定生成组件只登记一次，页面ID不作为任意客户端路径。</summary>
    internal void Import(string component, string path)
    {
        if (ImportedComponents.TryGetValue(component, out var old) && old != path) throw new InvalidOperationException("原详情组件名称冲突。");
        if (ImportedComponents.TryAdd(component, path)) { Imports.AppendLine($"import {component} from {Js(path)};"); Components.Append(component).Append(','); }
    }
    /// <summary>JSON字符串统一编码；HTML属性另行转义，不能让元数据打断模板。</summary>
    internal static string Js(object? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    internal static string Html(string? value) => SecurityElement.Escape(value ?? "") ?? "";
    internal static bool Enabled(IReadOnlyDictionary<string, JsonElement> extra, string key) => Integer(extra, key, 0) == 1;
    internal static string Text(IReadOnlyDictionary<string, JsonElement> extra, string key) => extra.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString() : "";
    /// <summary>旧元数据开关允许整数/布尔，布局枚举拒绝浮点或不可解释的文本。</summary>
    internal static int Integer(IReadOnlyDictionary<string, JsonElement> extra, string key, int fallback) => extra.TryGetValue(key, out var value) ? Integer(value, fallback) : fallback;
    internal static int Integer(JsonElement value, int fallback) => value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()) ? fallback : value.ValueKind == JsonValueKind.True ? 1 : value.ValueKind == JsonValueKind.False ? 0 : int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw new InvalidOperationException("原详情配置必须是明确整数。");
    /// <summary>扩展配置仅接受完整JSON对象，不能静默忽略损坏的控件定义。</summary>
    internal static JsonElement Object(string value, string kind)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("原详情" + kind + "配置必须为对象。"); return document.RootElement.Clone();
    }
    internal static string Property(JsonElement value, string key, string fallback = "") => value.TryGetProperty(key, out var item) && item.ValueKind != JsonValueKind.Null ? item.ToString() : fallback;
    /// <summary>名称列只采用现有元数据声明；没有辅助列时沿用原字段显示，不发明VO属性。</summary>
    internal string DisplayField(ColumnModel column, string nameField = "name")
    {
        var stem = column.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal) ? column.Source.ColumnName[..^4] : column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal) ? column.Source.ColumnName[..^3] : column.Source.ColumnName;
        var expected = stem + "_" + nameField + (column.Source.ColumnName.EndsWith("_ids", StringComparison.Ordinal) ? "s" : "");
        return Table.Columns.SingleOrDefault(item => item.Source.ColumnName == expected)?.JsonName ?? column.JsonName;
    }
}
internal sealed record OriginalDetailField(ColumnModel Column, PageColumnMetadata Settings, string Detail, string Default, bool ReadOnly, bool Required, string GroupKey, int Span, bool Derived = false);
