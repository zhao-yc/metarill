using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Data.Schema;

/// <summary>结构计划仅由逻辑元数据编译；不接受客户端传入的 SQL 类型或默认表达式。</summary>
public static class MySqlSchemaPlanner
{
    public static DatabaseSchema Compile(MetadataCatalog catalog, string database)
    {
        MetadataValidation.Validate(catalog);
        if (!string.Equals(catalog.DatabaseProvider, "mysql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("元数据数据库类型与 MySQL 适配器不一致。");
        MetadataValidation.RequireIdentifier(database);
        var tables = new List<TableSchema>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in catalog.ConfigTables.Where(t => t.IsFunctionTable == 0 && t.IsView == 0))
        {
            if (!names.Add(table.TableName)) throw new InvalidOperationException("同一数据库不能重复定义表名。");
            var columns = MetadataStoragePolicy.ForTable(catalog, table).PhysicalColumns.Select(CompileColumn).ToArray();
            if (columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Length)
                throw new InvalidOperationException("同一表不能重复定义字段名。");
            var id = columns.SingleOrDefault(c => c.Name == "id");
            if (id is null || id.StoreType != "bigint" || id.Nullable) throw new InvalidOperationException("每张实体表必须显式定义非空 int64 主键 id。");
            tables.Add(new TableSchema(table.TableName, columns.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray(), null, MetadataIndexes.Compile(catalog, table, columns)));
        }
        if (tables.Count == 0) throw new InvalidOperationException("结构同步至少需要一张受管辖的实体表。");
        return new DatabaseSchema(database, tables.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>所有长度和精度先验证，再形成 SQL；未知逻辑类型不能退化成字符串。</summary>
    public static ColumnSchema CompileColumn(ColumnMetadata column)
    {
        MetadataValidation.RequireIdentifier(column.ColumnName);
        if (string.IsNullOrWhiteSpace(column.LogicalType)) throw new InvalidOperationException("必须明确指定字段逻辑类型。");
        var logical = column.LogicalType.ToLowerInvariant();
        var sqlType = logical switch
        {
            "string" => StringType(column.ColumnLength),
            "int32" => "int",
            "int64" => "bigint",
            "decimal" => DecimalType(column.Precision, column.Scale),
            "bool" => "tinyint(1)",
            "date" => "date",
            "datetime" => "datetime(6)",
            "time" => "time(6)",
            "json" => "json",
            "binary" => BinaryType(column.ColumnLength),
            _ => throw new InvalidOperationException($"不支持的逻辑类型：{column.LogicalType}。")
        };
        var isId = column.ColumnName == "id";
        if (isId && logical != "int64") throw new InvalidOperationException("id 必须为 int64。");
        if (column.ColumnName == "delete_no" && (logical != "datetime" || column.IsDbNotNull != 0))
            throw new InvalidOperationException("delete_no 必须是可空 datetime，null 表示活动记录。");
        var defaultSql = CompileDefault(column.DefaultValue, logical);
        // MySQL 8.0.13及以后要求TEXT常量也使用表达式括号；仍不接受原生表达式。
        if(defaultSql is not null&&sqlType is "text" or "longtext")defaultSql="("+defaultSql+")";
        return new ColumnSchema(column.ColumnName, sqlType, column.IsDbNotNull == 0, isId, defaultSql);
    }

    /// <summary>只创建、增列和安全扩宽；未声明字段或索引不自动删除。</summary>
    public static IReadOnlyList<SchemaChange> Compare(DatabaseSchema current, DatabaseSchema desired)
    {
        if (current.Database != desired.Database) throw new InvalidOperationException("结构快照数据库不一致。");
        foreach(var index in current.Tables.Concat(desired.Tables).SelectMany(table=>table.Indexes??[]))_=IndexDirectionRules.Read(index);
        var changes = new List<SchemaChange>();
        foreach (var expectedTable in desired.Tables)
        {
            var actual = current.Tables.SingleOrDefault(t => t.Name == expectedTable.Name);
            if (actual is null)
            {
                changes.Add(new SchemaChange("createTable", expectedTable.Name, null,
                    $"CREATE TABLE {Quote(expectedTable.Name)} ({string.Join(", ", expectedTable.Columns.Select(Definition).Concat((expectedTable.Indexes ?? []).Select(IndexDefinition)))}) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"));
                continue;
            }
            foreach (var column in expectedTable.Columns)
            {
                var found = actual.Columns.SingleOrDefault(c => c.Name == column.Name);
                if (found is null)
                {
                    // 给已存在的行补必填值需要业务回填方案，首期不允许 MySQL 静默填零。
                    if (!column.Nullable && column.DefaultSql is null) throw new InvalidOperationException($"新增必填字段 {actual.Name}.{column.Name} 需要先制定回填方案。");
                    if (column.PrimaryKey) throw new InvalidOperationException("已有表缺少主键，必须单独处理。");
                    changes.Add(new SchemaChange("addColumn", actual.Name, column.Name, $"ALTER TABLE {Quote(actual.Name)} ADD COLUMN {Definition(column)}"));
                }
                else if (!Equivalent(found, column))
                {
                    if (found.Generated || found.PrimaryKey != column.PrimaryKey || (!column.Nullable && found.Nullable) || !CanWiden(found.StoreType, column.StoreType))
                        throw new InvalidOperationException($"字段 {actual.Name}.{column.Name} 涉及不支持或可能破坏数据的结构变化。");
                    changes.Add(new SchemaChange("widenColumn", actual.Name, column.Name, $"ALTER TABLE {Quote(actual.Name)} MODIFY COLUMN {Definition(column)}"));
                }
            }
            foreach (var index in expectedTable.Indexes ?? [])
            {
                var foundIndex = actual.Indexes?.SingleOrDefault(x => x.Name == index.Name);
                if (foundIndex is null)
                    changes.Add(new SchemaChange("addIndex", actual.Name, null, $"ALTER TABLE {Quote(actual.Name)} ADD {IndexDefinition(index)}"));
                else if (foundIndex.Unique != index.Unique || !foundIndex.Columns.SequenceEqual(index.Columns, StringComparer.Ordinal) || !IndexDirectionRules.Read(foundIndex).SequenceEqual(IndexDirectionRules.Read(index),StringComparer.Ordinal))
                    throw new InvalidOperationException($"索引 {actual.Name}.{index.Name} 已存在但结构不同，请单独制定迁移方案。");
            }
            // 未声明列不自动删除：元数据可能仅管辖一部分列，显式删除需后续独立方案。
        }
        return changes.AsReadOnly();
    }

    /// <summary>对完整受管辖快照排序和摘要，识别预览后其他任务造成的结构漂移。</summary>
    public static string Fingerprint(DatabaseSchema schema)
    {
        var normalized = new DatabaseSchema(schema.Database, schema.Tables.OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new TableSchema(t.Name, t.Columns.Select(column=>column with{DefaultSql=CanonicalDefault(column)}).OrderBy(c => c.Name, StringComparer.Ordinal).ToArray(), t.SnapshotDefinition, t.Indexes?.Select(IndexDirectionRules.Normalize).OrderBy(i => i.Name, StringComparer.Ordinal).ToArray())).ToArray());
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized))).ToLowerInvariant();
    }

    /// <summary>只接受单段白名单标识符，禁止跨库、反引号及 SQL 表达式。</summary>
    public static string Quote(string identifier)
    {
        MetadataValidation.RequireIdentifier(identifier);
        return $"`{identifier}`";
    }

    internal static bool Equivalent(ColumnSchema actual, ColumnSchema expected) =>
        actual.StoreType == expected.StoreType && actual.Nullable == expected.Nullable && actual.PrimaryKey == expected.PrimaryKey
        && CanonicalDefault(actual) == CanonicalDefault(expected) && !actual.Generated;

    /// <summary>MySQL为DECIMAL补齐小数位；仅精确文本归一，不使用数值舍入或修改声明。</summary>
    private static string? CanonicalDefault(ColumnSchema column)
    {
        var value=column.DefaultSql;
        if(value is null||!System.Text.RegularExpressions.Regex.IsMatch(column.StoreType,@"^decimal[(][0-9]+,[0-9]+[)]$")||!System.Text.RegularExpressions.Regex.IsMatch(value,@"^[+-]?[0-9]+(?:[.][0-9]+)?$"))return value;
        var negative=value[0]=='-';var parts=value.TrimStart('+','-').Split('.',2);var integer=parts[0].TrimStart('0');if(integer.Length==0)integer="0";var fraction=parts.Length==2?parts[1].TrimEnd('0'):"";
        return (negative&&(integer!="0"||fraction.Length>0)?"-":"")+integer+(fraction.Length==0?"":"."+fraction);
    }

    private static string Definition(ColumnSchema column) => $"{Quote(column.Name)} {column.StoreType} {(column.Nullable ? "NULL" : "NOT NULL")}" +
        (column.DefaultSql is null ? "" : $" DEFAULT {column.DefaultSql}") + (column.PrimaryKey ? " PRIMARY KEY" : "");

    /// <summary>只把已验证方向附在对应的白名单字段上；ASC省略以保持旧DDL逐字一致。</summary>
    private static string IndexDefinition(IndexSchema index)
    {
        var directions=IndexDirectionRules.Read(index);
        return $"{(index.Unique ? "UNIQUE " : "")}INDEX {Quote(index.Name)} ({string.Join(", ",index.Columns.Select((name,position)=>Quote(name)+(directions[position]=="DESC"?" DESC":"")))})";
    }

    private static bool CanWiden(string actual, string desired)
    {
        if (actual == desired) return true;
        if (actual == "int" && desired == "bigint") return true;
        if (TrySize(actual, "varchar", out var oldLength) && TrySize(desired, "varchar", out var newLength)) return newLength >= oldLength;
        if (TrySize(actual, "varbinary", out oldLength) && TrySize(desired, "varbinary", out newLength)) return newLength >= oldLength;
        return false;
    }

    private static bool TrySize(string type, string prefix, out int size)
    {
        size = 0;
        return type.StartsWith(prefix + "(", StringComparison.Ordinal) && type.EndsWith(')') && int.TryParse(type.AsSpan(prefix.Length + 1, type.Length - prefix.Length - 2), out size);
    }

    private static string StringType(string length)
    {
        if (length.Equals("text", StringComparison.OrdinalIgnoreCase)) return "text";
        if (length.Equals("longtext", StringComparison.OrdinalIgnoreCase) || length.Equals("max", StringComparison.OrdinalIgnoreCase)) return "longtext";
        var size = Length(length, 255, 16000);
        return $"varchar({size})";
    }
    private static string BinaryType(string length) => string.IsNullOrEmpty(length) ? "longblob" : $"varbinary({Length(length, 255, 16000)})";
    private static int Length(string value, int fallback, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 1 || size > max)
            throw new InvalidOperationException("字段长度无效或超出首期支持范围。");
        return size;
    }
    private static string DecimalType(int precision, int scale)
    {
        if (precision is < 1 or > 65 || scale is < 0 or > 30 || scale > precision) throw new InvalidOperationException("小数精度或小数位数无效。");
        return $"decimal({precision},{scale})";
    }
    private static string? CompileDefault(string value, string logical)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (logical == "string" && value.Contains((char)92)) throw new InvalidOperationException("首期结构默认值不支持反斜杠，请由 Service 设置默认值。");
        return logical switch
        {
            "string" => "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'",
            "int32" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i.ToString(CultureInfo.InvariantCulture),
            "int64" when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l.ToString(CultureInfo.InvariantCulture),
            "decimal" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d.ToString(CultureInfo.InvariantCulture),
            "bool" when value is "true" or "1" => "1",
            "bool" when value is "false" or "0" => "0",
            _ => throw new InvalidOperationException("该字段默认值不是受支持的安全常量。")
        };
    }
}
