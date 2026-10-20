using System.Globalization;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Files;

internal sealed record FileParentScope(ColumnMetadata Source, TableMetadata Parent, ColumnMetadata Project, bool HasSoftDelete);

/// <summary>文件引用只解释与生成页一致的项目/父键等值子集，不执行配置SQL。</summary>
internal static class FileReferenceScope
{
    /// <summary>父定义绑定发布模型中的真实外键；未知字段、复杂条件或未声明归属均拒绝。</summary>
    internal static IReadOnlyList<FileParentScope> Compile(MetadataCatalog catalog, TableMetadata table, PageMetadata page)
    {
        var result = new List<FileParentScope>();
        if (string.IsNullOrWhiteSpace(page.WhereSql)) return result;
        var columns = MetadataStoragePolicy.ForTable(catalog, table).PhysicalColumns;
        foreach (var clause in Regex.Split(page.WhereSql.Trim(), @"\s+AND\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var match = Regex.Match(clause.Trim(), @"^`?([a-zA-Z_][a-zA-Z0-9_]*)`?\s*=\s*@(current_yz_project_id|parent_id|parentId)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) throw Unsupported();
            var column = columns.SingleOrDefault(candidate => candidate.ColumnName == match.Groups[1].Value.ToLowerInvariant()) ?? throw Unsupported();
            var project = match.Groups[2].Value.Equals("current_yz_project_id", StringComparison.OrdinalIgnoreCase);
            if ((column.ColumnName == "yz_project_id") != project || !IdentityType(column.LogicalType)) throw Unsupported();
            if (project) continue;
            var parents = catalog.ConfigTables.Where(candidate => !string.IsNullOrWhiteSpace(column.LinkConfigTableId)
                ? candidate.Id == column.LinkConfigTableId : candidate.TableName + "_id" == column.ColumnName).ToArray();
            if (parents.Length != 1 || parents[0].IsFunctionTable != 0 || parents[0].IsView != 0) throw Unsupported();
            var parent = parents[0];
            var parentColumns = MetadataStoragePolicy.ForTable(catalog, parent).PhysicalColumns;
            var id = parentColumns.SingleOrDefault(candidate => candidate.ColumnName == "id");
            var owner = parentColumns.SingleOrDefault(candidate => candidate.ColumnName == "yz_project_id");
            if (id is null || !LongType(id.LogicalType) || owner is null || !IdentityType(owner.LogicalType)) throw Unsupported();
            var deleted = parentColumns.SingleOrDefault(candidate => candidate.ColumnName == "delete_no");
            if (deleted is not null && (deleted.LogicalType.ToLowerInvariant() is not ("datetime" or "timestamp") || deleted.IsDbNotNull != 0)) throw Unsupported();
            result.Add(new(column, parent, owner, deleted is not null));
        }
        return result;
    }

    /// <summary>实际父键必须是可表示的正整数，不能把null、浮点或表达式解释为记录身份。</summary>
    internal static long PositiveId(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id : throw new UnauthorizedAccessException("实际记录的父范围无效。");
    }

    /// <summary>查询参数沿用生成器的字符串/整型身份映射，范围不依赖MySQL隐式数值转换。</summary>
    internal static object IdentityValue(ColumnMetadata column, long value) => column.LogicalType.ToLowerInvariant() switch
    {
        "string" or "text" or "varchar" => value.ToString(CultureInfo.InvariantCulture),
        "id" or "long" or "bigint" or "int64" => value,
        "integer" or "int" or "int32" => checked((int)value),
        _ => throw Unsupported()
    };

    /// <summary>只接受生成器明确支持的身份逻辑类型，JSON/日期/小数不作为父键。</summary>
    private static bool IdentityType(string type) => LongType(type) || type.ToLowerInvariant() is "string" or "text" or "varchar" or "integer" or "int" or "int32";
    /// <summary>父主键与生成实体一致，必须为64位整数。</summary>
    private static bool LongType(string type) => type.ToLowerInvariant() is "id" or "long" or "bigint" or "int64";
    /// <summary>能力缺口必须明确拒绝，不能退化成不带范围的查询。</summary>
    private static NotSupportedException Unsupported() => new("该页面包含尚未证明的记录范围或父表定义，文件引用同步拒绝执行。");
}
