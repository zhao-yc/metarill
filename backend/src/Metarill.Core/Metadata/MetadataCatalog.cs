using System.Text.Json;
using System.Text.Json.Serialization;

namespace Metarill.Core.Metadata;

/// <summary>平台与生成器共享的版本化元数据；扩展字段保留原 uni-app 配置能力。</summary>
public sealed class MetadataCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string BackendTarget { get; set; } = "csharp";
    public string YzProjectId { get; set; } = "1608097412978577409";
    public string YzProjectName { get; set; } = "Metarill";
    /// <summary>生成页面沿用项目主色，未配置时与原平台uni.scss一致。</summary>
    public string PrimaryColor { get; set; } = "#0ab3ad";
    public string DatabaseProvider { get; set; } = "mysql";
    public List<TableMetadata> ConfigTables { get; set; } = [];
    public List<ColumnMetadata> ConfigColumns { get; set; } = [];
    public List<PageMetadata> ConfigPages { get; set; } = [];
    public List<PageColumnMetadata> ConfigPageColumns { get; set; } = [];
    public List<AdminTypeMetadata> YzAdminTypes { get; set; } = [];
    public List<MenuMetadata> ConfigMenus { get; set; } = [];
    public List<CustomPageMetadata> ConfigCustomPages { get; set; } = [];
    public List<JsonElement> ConfigButtons { get; set; } = [];
    public List<JsonElement> ConfigChildren { get; set; } = [];
    public List<JsonElement> ConfigIndexes { get; set; } = [];
    public List<JsonElement> ConfigIndexColumns { get; set; } = [];
}
public class MetadataRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string YzProjectId { get; set; } = "";
    public int SortNo { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; } = [];
}
public sealed class TableMetadata : MetadataRecord
{
    public string TableName { get; set; } = "";
    public int IsFunctionTable { get; set; }
    public int IsView { get; set; }
    public int IsRongyu { get; set; } = 1;
}
public sealed class ColumnMetadata : MetadataRecord
{
    public string ConfigTableId { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public string ColumnType { get; set; } = "";
    public string ColumnLength { get; set; } = "";
    public string LogicalType { get; set; } = "string";
    public int IsDbNotNull { get; set; }
    public int IsVirtualColumn { get; set; }
    public int Precision { get; set; } = 18;
    public int Scale { get; set; } = 2;
    public string ControlKind { get; set; } = "";
    public string ControlDetail { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public string LinkConfigTableId { get; set; } = "";
}
public sealed class PageMetadata : MetadataRecord
{
    public string ConfigTableId { get; set; } = "";
    public string YzAdminTypeId { get; set; } = "";
    public int IsNew { get; set; } = 1;
    public int IsSave { get; set; } = 1;
    public int IsDelete { get; set; } = 1;
    public int IsCopy { get; set; }
    public int IsImport { get; set; }
    public int IsExport { get; set; }
    public int PageSize { get; set; } = 20;
    public string OrderBy { get; set; } = "";
    public string WhereSql { get; set; } = "";
}
public sealed class PageColumnMetadata : MetadataRecord
{
    public string ConfigPageId { get; set; } = "";
    public string ConfigColumnId { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public int IsEdit { get; set; } = 1;
    public int IsShow { get; set; } = 1;
    public int IsRequired { get; set; }
}
public sealed class CustomPageMetadata : MetadataRecord { public string PagePath { get; set; } = ""; public string YzAdminTypeId { get; set; } = ""; }
public sealed class AdminTypeMetadata : MetadataRecord { }
public sealed class MenuMetadata : MetadataRecord
{
    public string? ParentMenuId { get; set; }
    public string YzAdminTypeId { get; set; } = "";
    public string Kind { get; set; } = "DIRECTORY";
    public string? ConfigPageId { get; set; }
    public string? ConfigCustomPageId { get; set; }
    public string? NavigateUrl { get; set; }
    public string? IconName { get; set; }
    public int IsHidden { get; set; }
    public string? VisibleScheme { get; set; }
    public int IsShowCount { get; set; }
}

/// <summary>集中验证元数据引用，不把项目选择当成权限。</summary>
public static class MetadataValidation
{
    public static void Validate(MetadataCatalog catalog)
    {
        if (catalog.BackendTarget != "csharp") throw new InvalidOperationException("生成目标必须为 C#。");
        // 主色进入原模板属性/CSS，仅接受颜色字面值，不能夹带模板属性或可执行表达式。
        if (string.IsNullOrWhiteSpace(catalog.PrimaryColor) || !System.Text.RegularExpressions.Regex.IsMatch(catalog.PrimaryColor,
            "^(#[0-9A-Fa-f]{3}|#[0-9A-Fa-f]{4}|#[0-9A-Fa-f]{6}|#[0-9A-Fa-f]{8}|[A-Za-z]{1,32}|(?:rgb|rgba|hsl|hsla)\\([0-9 .,%+-]+\\))$"))
            throw new InvalidOperationException("项目主色必须为合法颜色字面值。");
        var tables = catalog.ConfigTables.ToDictionary(x => x.Id);
        var columns = catalog.ConfigColumns.ToDictionary(x => x.Id);
        var pages = catalog.ConfigPages.ToDictionary(x => x.Id);
        foreach (var table in tables.Values) RequireIdentifier(table.TableName);
        foreach (var column in columns.Values)
        {
            RequireIdentifier(column.ColumnName);
            if((column.Id.StartsWith(CascadeTreeStorageColumns.ReservedIdPrefix,StringComparison.Ordinal)||column.Id.StartsWith(CrossTableStorageColumns.ReservedIdPrefix,StringComparison.Ordinal)))throw new InvalidOperationException("显式配置字段不能占用级联派生列的保留标识。");
            if (!tables.ContainsKey(column.ConfigTableId)) throw new InvalidOperationException("字段引用不存在的表。");
        }
        foreach (var page in pages.Values)
            if (!tables.ContainsKey(page.ConfigTableId)) throw new InvalidOperationException("页面引用不存在的表。");
        foreach (var field in catalog.ConfigPageColumns)
            if (!pages.ContainsKey(field.ConfigPageId) || !columns.ContainsKey(field.ConfigColumnId)) throw new InvalidOperationException("页面字段引用无效。");
    }
    public static void RequireIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || !System.Text.RegularExpressions.Regex.IsMatch(identifier, "^[a-zA-Z_][a-zA-Z0-9_]{0,63}$"))
            throw new InvalidOperationException("数据库标识符无效。");
    }
}
