using System.Data.Common;
using Metarill.Core.Metadata;

namespace Metarill.Data.Schema;

public sealed partial class SchemaService
{
    /// <summary>只反查已登记视图的列投影。视图源码及数据由数据库管理，不进入表结构计划。</summary>
    public async Task<SchemaInspectionSnapshot> InspectViewSnapshotAsync(TableMetadata view, CancellationToken token = default)
    {
        if (view.IsView != 1 || view.IsFunctionTable != 0)
            throw new ArgumentException("视图反查要求已登记的数据库视图。", nameof(view));
        var name = view.TableName;
        MetadataValidation.RequireIdentifier(name);
        EnsureMySql();
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync(token);
        await ValidateTargetAsync(connection, token);
        await using var command = session.Provider.CreateCommand(connection);
        // 只访问 information_schema。同名实体表、大小写别名和其他数据库的列均不能替代视图投影。
        command.CommandText = """
            SELECT objects.TABLE_TYPE, columns.COLUMN_NAME, columns.COLUMN_TYPE, columns.IS_NULLABLE
            FROM information_schema.TABLES AS objects
            LEFT JOIN information_schema.COLUMNS AS columns
              ON BINARY columns.TABLE_SCHEMA = BINARY objects.TABLE_SCHEMA
             AND BINARY columns.TABLE_NAME = BINARY objects.TABLE_NAME
            WHERE objects.TABLE_SCHEMA = DATABASE() AND objects.TABLE_NAME = @view
              AND ((@@lower_case_table_names = 0
                    AND BINARY objects.TABLE_SCHEMA = BINARY DATABASE()
                    AND BINARY objects.TABLE_NAME = BINARY @view)
                OR (@@lower_case_table_names <> 0
                    AND BINARY LOWER(objects.TABLE_SCHEMA) = BINARY LOWER(DATABASE())
                    AND BINARY LOWER(objects.TABLE_NAME) = BINARY LOWER(@view)))
            ORDER BY columns.ORDINAL_POSITION
            LIMIT 513
            """;
        AddParameters(command, [("@view", name)]);
        await using var reader = await command.ExecuteReaderAsync(token);
        var projection = await ReadViewProjectionAsync(name, reader, token);
        var schema = new DatabaseSchema(session.Options.ExpectedDatabase, projection is null ? [] : [projection]);
        return new(session.Provider.Name, schema, MySqlSchemaPlanner.Fingerprint(schema));
    }

    /// <summary>视图列没有实体主键、索引与默认值。超出反查预算或缺少完整投影时拒绝返回截断结果。</summary>
    internal static async Task<TableSchema?> ReadViewProjectionAsync(string name, DbDataReader reader, CancellationToken token = default)
    {
        var columns = new List<ColumnSchema>();
        while (await reader.ReadAsync(token))
        {
            if (reader.GetString(0) != "VIEW")
                throw new InvalidOperationException("已登记视图的名称被其它数据库对象占用。");
            if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                throw new InvalidOperationException("数据库未返回完整的视图列投影。");
            if (columns.Count >= 512)
                throw new InvalidOperationException("视图列数超过反查预算，未返回截断投影。");
            columns.Add(new(reader.GetString(1), NormalizeType(reader.GetString(2)), reader.GetString(3) == "YES"));
        }
        return columns.Count == 0 ? null : new(name, columns);
    }
}
