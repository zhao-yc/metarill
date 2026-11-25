using System.Text.Json;
using Metarill.Core.Contracts;

namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    /// <summary>通用分支排除实际名称筛选和明确拒绝的同名键，保持与两种查询实现一致。</summary>
    private static IEnumerable<ColumnModel> RequestFilterColumns(PageModel page, SelectionNameFilterPlan names)
    {
        var claimed = names.ConsumedKeys.Concat(names.Gaps.Select(gap => gap.FilterKey)).ToHashSet(StringComparer.Ordinal);
        return page.Searchable.Where(column => !claimed.Contains(column.JsonName));
    }

    /// <summary>发布实际可执行的页面筛选键及常用值形状，固定范围不进入客户端筛选。</summary>
    private static string PageFiltersAttribute(GenerationModel model, TableModel table, PageModel page)
    {
        var names = SelectionNameFilterCompilation.ForPage(model, table, page);
        var fixedRead = NonRedundantReadCompilation.Requires(model, table);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var name in names.Bindings)
            fields.Add(name.FilterKey, Alternatives(Type("string"), Type("null")));
        if (!fixedRead && FixedRoleAdminNameCompilation.LegacyNameFilter(model, table, page) is not null)
            fields.Add("yzAdminTypeName", Alternatives(Type("string"), Type("null")));
        if (!fixedRead && FixedRoleAdminNameCompilation.LegacyIdFilter(model, table, page) is not null)
            fields.Add("yzAdminTypeId", Alternatives(IntegerShape(), Type("null")));

        foreach (var column in RequestFilterColumns(page, names).Where(column => !ManagerCredentialCompilation.IsCredential(table, column)))
            if (FilterShape(model, table, page, column, fixedRead) is { } shape)
                fields.Add(column.JsonName, shape);
        return "[GeneratedPageFilters(" + Literal(JsonSerializer.Serialize(ObjectShape(fields))) + ")]";
    }

    /// <summary>与生成查询的筛选分支同步，按节点、复选成员、日期范围或普通标量发布描述。</summary>
    private static object? FilterShape(GenerationModel model, TableModel table, PageModel page, ColumnModel column, bool fixedRead)
    {
        if (column.Source.ControlKind == "CascadeTree")
        {
            var levels = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (CrossTableWriteCompilation.Resolve(model, table, page, column).Definition is { } cross)
            {
                foreach (var level in cross.Levels)
                {
                    levels.Add(level.Id.JsonName, Alternatives(PositiveId(allowLeadingZeros: true), Type("null")));
                    levels.Add(level.Name.JsonName, Alternatives(Type("string"), Type("null")));
                }
            }
            else
            {
                if (column.Type is not ("string?" or "long?") || !column.Source.ColumnName.EndsWith("_id", StringComparison.Ordinal) ||
                    CascadeTreeReadCompilation.Resolve(model, table, page, column).Definition is null) return null;
                levels.Add(column.JsonName, Alternatives(PositiveId(allowLeadingZeros: true), Type("null")));
                levels.Add(GenerationModel.Camel(column.Source.ColumnName[..^3] + "_name"), Alternatives(Type("string"), Type("null")));
            }
            return Alternatives(ObjectShape(levels), Type("null"));
        }

        var checkbox = DatabaseCheckboxCompilation.Generic(table, column)
            ? DatabaseCheckboxCompilation.Resolve(model, table, page, column).Definition : null;
        var multiId = ControlRenderer.UsesMultiIdCheckboxFilter(column);
        if (multiId && (fixedRead && table.Source.IsRongyu == 0 || !fixedRead&&
            (checkbox?.SupportsRedundantArrayFilter == true||MultiSelectCompilation.CheckboxFilterOptions(model,table,page,column) is not null)))
            return Alternatives(PositiveId(), Sequence(PositiveId(), max: DatabaseCheckboxValue.MaximumItems),
                new { type = "string", description = "兼容原 JSON 编码的 ID 数组文本。" }, Type("null"));
        if (checkbox is not null && !multiId)
            return Alternatives(PositiveId(), Type("null"));
        if (column.IsManagedMultiNames && DatabaseCheckboxCompilation.ForService(model, table, page).Any(item => item.Names?.Source.Id == column.Source.Id))
            return Alternatives(Type("string"), Type("null"));

        if (column.Type == "DateTime?" && !ControlRenderer.UsesScalarCheckboxFilter(column))
            return Alternatives(Type("string"), Sequence(Type("string"), min: 2, max: 2), Type("null"));

        object scalar = column.Type switch
        {
            "long" or "long?" or "int?" when !fixedRead && ManagerCredentialCompilation.IsEnabled(table, page, column)
                => Alternatives(new { type = "integer", @enum = new[] { 0, 1 } }, new { type = "string", pattern = "^[01]$" }),
            "long" or "long?" or "int?" => IntegerShape(),
            "bool?" when CheckboxFilterBoolean(model, page, column)
                => Alternatives(Type("boolean"), new { type = "integer", @enum = new[] { 0, 1 } }, new { type = "string", pattern = "^[01]$" }),
            "bool?" => Type("boolean"),
            "decimal?" or "double?" => Alternatives(Type("number"), new { type = "string", description = "十进制数值文本。" }),
            "byte[]?" => new { type = "string", contentEncoding = "base64" },
            "TimeSpan?" => new { type = "string", description = TimeFieldCompilation.IsClock(column) ? "日内时间 HH:mm 或 HH:mm:ss，可带小数秒。" : "MySQL TIME 文本，可带符号、总小时及小数秒。" },
            _ => Type("string")
        };
        // 扁平化标量多种编码，使协议消费者能直接读取本字段支持的值形状。
        var variants = scalar is FilterVariants many ? many.anyOf : [scalar];
        return Alternatives([..variants, Sequence(scalar), Type("null")]);
    }

    private sealed record FilterVariants(object[] anyOf);
    private static FilterVariants Alternatives(params object[] variants) => new(variants);
    private static FilterVariants IntegerShape() => Alternatives(Type("integer"), new { type = "string", pattern = @"^[+-]?[0-9]+$" });
    /// <summary>节点允许原字符串前导零，复选使用规范十进制正 ID。</summary>
    private static FilterVariants PositiveId(bool allowLeadingZeros = false) =>
        Alternatives(new { type = "integer", minimum = 1 },
            new { type = "string", pattern = allowLeadingZeros ? "^0*[1-9][0-9]*$" : "^[1-9][0-9]*$" });

    private static object Type(string type) => new { type };
    private static object ObjectShape(Dictionary<string, object?> fields) =>
        new { type = "object", properties = fields, additionalProperties = false };
    private static object Sequence(object items, int? min = null, int? max = null)
    {
        var schema = new Dictionary<string, object> { ["type"] = "array", ["items"] = items };
        if (min is { } minimum) schema["minItems"] = minimum;
        if (max is { } maximum) schema["maxItems"] = maximum;
        return schema;
    }
}
