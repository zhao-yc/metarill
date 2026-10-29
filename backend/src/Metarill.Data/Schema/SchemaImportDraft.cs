using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;

namespace Metarill.Data.Schema;

/// <summary>只读逻辑草案。符号ID不是配置ID，不能用于保存、DDL执行或代码生成授权。</summary>
public sealed record SchemaImportDraft(SchemaImportInspection Source, MetadataCatalog LogicalMetadata, IReadOnlyList<SchemaImportIssue> Issues)
{
    public bool ColumnsAndIndexesRepresentable => Issues.Count == 0;
    public bool UsesDraftIdentifiers => true;
}

/// <summary>物理类型必须能经既有逻辑编译器无损往返，未知类型/默认值不能回退成字符串。</summary>
public static class SchemaImportDraftCompiler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>编译元数据草案而不分配真实ID、补系统字段、创建页面或修改输入。</summary>
    public static SchemaImportDraft Compile(SchemaImportInspection source, string projectId)
    {
        if (source.Snapshot.Provider != "mysql") throw new NotSupportedException("当前反向逻辑映射仅实现MySQL。");
        var catalog = new MetadataCatalog { YzProjectId = projectId, DatabaseProvider = "mysql" };
        var issues = source.Issues.ToList();
        foreach (var (table, position) in source.Snapshot.Schema.Tables.OrderBy(table => table.Name, StringComparer.Ordinal).Select((table, position) => (table, position)))
        {
            var tableId = "draft_table_" + position.ToString(CultureInfo.InvariantCulture);
            try { MetadataValidation.RequireIdentifier(table.Name); }
            catch (InvalidOperationException) { issues.Add(new(table.Name, null, "IDENTIFIER_UNSUPPORTED", "源表名不符合平台标识符规则。")); continue; }
            catalog.ConfigTables.Add(new() { Id = tableId, YzProjectId = projectId, TableName = table.Name, Name = table.Name });
            if (table.Columns.Count(column => column.PrimaryKey) != 1 || !table.Columns.Any(column => column.Name == "id" && column.PrimaryKey && column.StoreType == "bigint" && !column.Nullable))
                issues.Add(new(table.Name, "id", "PRIMARY_KEY_UNSUPPORTED", "实体必须有唯一的非空有符号bigint主键id；不推断或改造源主键。"));
            var columns = new Dictionary<string, ColumnMetadata>(StringComparer.Ordinal);
            foreach (var (physical, index) in table.Columns.Select((column, index) => (column, index)))
            {
                try
                {
                    var column = Column(physical);
                    column.Id = tableId + "_column_" + index.ToString(CultureInfo.InvariantCulture); column.ConfigTableId = tableId; column.YzProjectId = projectId; column.SortNo = (index + 1) * 10;
                    catalog.ConfigColumns.Add(column); columns.Add(column.ColumnName, column);
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
                { issues.Add(new(table.Name, physical.Name, "COLUMN_UNREPRESENTABLE", error.Message)); }
            }
            foreach (var (index, number) in (table.Indexes ?? []).Select((index, number) => (index, number)))
            {
                try
                {
                    MetadataValidation.RequireIdentifier(index.Name);
                    if (index.Columns.Count is < 1 or > 16 || index.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Columns.Count || index.Columns.Any(name => !columns.ContainsKey(name))) throw new InvalidOperationException("索引包含未映射字段、表达式或不支持的重复字段。 ");
                    if (source.Issues.Any(issue => issue.Table == table.Name && issue.Member == index.Name)) continue;
                    var directions=IndexDirectionRules.Read(index);
                    var id = tableId + "_index_" + number.ToString(CultureInfo.InvariantCulture);
                    catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new { id, configTableId = tableId, yzProjectId = projectId, indexName = index.Name, indexType = index.Unique ? "UNIQUE" : "NORMAL", isUnique = index.Unique ? 1 : 0, isEnabled = 1 }, Json));
                    foreach (var (name, order) in index.Columns.Select((name, order) => (name, order)))
                        catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new { id = id + "_" + order, configIndexId = id, configColumnId = columns[name].Id, yzProjectId = projectId, columnName = name, sortNo = (order + 1) * 10, sortDirection = directions[order] }, Json));
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                { issues.Add(new(table.Name, index.Name, "INDEX_UNREPRESENTABLE", error.Message)); }
            }
        }
        // 两向比较防止漏列/漏索引；DDL原文、约束及触发器不在此声明可重建或已授权。
        if (issues.Count == 0)
        {
            try
            {
                var compiled = MySqlSchemaPlanner.Compile(catalog, source.Snapshot.Schema.Database);
                if (MySqlSchemaPlanner.Compare(source.Snapshot.Schema, compiled).Count != 0 || MySqlSchemaPlanner.Compare(compiled, source.Snapshot.Schema).Count != 0)
                    issues.Add(new("", null, "ROUNDTRIP_MISMATCH", "物理列或索引无法经现有逻辑模型无损往返。"));
            }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
            { issues.Add(new("", null, "ROUNDTRIP_UNSUPPORTED", error.Message)); }
        }
        return new(source, catalog, issues.Distinct().ToArray());
    }

    /// <summary>只解析平台已支持的物理类型与安全常量，再交回正向编译器核验；不执行SQL表达式。</summary>
    private static ColumnMetadata Column(ColumnSchema source)
    {
        MetadataValidation.RequireIdentifier(source.Name);
        if (source.Generated) throw new NotSupportedException("自动递增、生成列或未证明的EXTRA属性不能无损映射。");
        var column = new ColumnMetadata { Name = source.Name, ColumnName = source.Name, IsDbNotNull = source.Nullable ? 0 : 1 };
        var type = source.StoreType;
        var sized = Regex.Match(type, "^(varchar|varbinary)\\(([0-9]+)\\)$", RegexOptions.CultureInvariant);
        var decimalType = Regex.Match(type, "^decimal\\(([0-9]+),([0-9]+)\\)$", RegexOptions.CultureInvariant);
        if (sized.Success) { column.LogicalType = sized.Groups[1].Value == "varchar" ? "string" : "binary"; column.ColumnType = sized.Groups[1].Value; column.ColumnLength = sized.Groups[2].Value; }
        else if (decimalType.Success) { column.LogicalType = "decimal"; column.ColumnType = "decimal"; column.Precision = int.Parse(decimalType.Groups[1].Value, CultureInfo.InvariantCulture); column.Scale = int.Parse(decimalType.Groups[2].Value, CultureInfo.InvariantCulture); }
        else if(type is "datetime" or "time" || MetadataColumnTypePolicy.TryTemporalPhysicalType(type,out _,out _))
        {
            var bare=type.Split('(')[0];var precision=type==bare?0:type[^2]-'0';
            column.LogicalType=bare;column.ColumnType=precision==6?bare:bare+"("+precision.ToString(CultureInfo.InvariantCulture)+")";
        }
        else
        {
            column.LogicalType = type switch { "int" => "int32", "bigint" => "int64", "double" => "double", "tinyint(1)" => "bool", "text" or "longtext" => "string", "date" => "date", "datetime(6)" => "datetime", "time(6)" => "time", "json" => "json", "longblob" => "binary", _ => throw new NotSupportedException("当前逻辑类型不能无损表达物理类型：" + type) };
            column.ColumnType = type switch { "tinyint(1)" => "tinyint", "datetime(6)" => "datetime", "time(6)" => "time", _ => type };
            if (type is "text" or "longtext") column.ColumnLength = type;
        }
        // 配置物理类型沿用原词表；真实varbinary/longblob及长度由逻辑类型编译，不能伪造SQL字段。
        if (column.LogicalType == "binary") column.ColumnType = "blob";
        MetadataColumnTypePolicy.Resolve(column.ColumnName, column.LogicalType, column.ColumnType, true, true, true);
        if (column.LogicalType == "decimal" && column.Precision > 28) throw new NotSupportedException("当前C# decimal不能无损覆盖超过28位精度的全部数值范围。");
        if (source.DefaultSql is { } value)
        {
            if (column.LogicalType == "string")
            {
                if (!MySqlTextDefault.TryNormalize(value, out var normalized)) throw new NotSupportedException("默认值不是已证明的字符串常量。");
                column.DefaultValue = normalized![2..^2].Replace("''", "'", StringComparison.Ordinal);
                if (column.DefaultValue.Length == 0) throw new NotSupportedException("现有元数据不能区分空字符串默认值与没有默认值。");
            }
            else if(column.LogicalType=="time")column.DefaultValue=value.Length>=2&&value[0]=='\''&&value[^1]=='\''?value[1..^1]:value;
            else column.DefaultValue = value;
        }
        if (!MySqlSchemaPlanner.Equivalent(source, MySqlSchemaPlanner.CompileColumn(column))) throw new NotSupportedException("字段类型、可空性或默认值不能无损往返。");
        return column;
    }
}
