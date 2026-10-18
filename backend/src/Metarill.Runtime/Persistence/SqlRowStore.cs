using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Metadata;

namespace Metarill.Runtime.Persistence;

/// <summary>仅供元数据、认证及文件基础设施使用；业务 CRUD 由强类型生成 Mapper 执行。</summary>
public sealed class SqlRowStore(DatabaseSession session, MetadataRegistry registry)
{
    public DatabaseSession Session => session;
    private IReadOnlyList<ColumnMetadata> Columns(string table)
    {
        var definition = registry.Platform.ConfigTables.SingleOrDefault(t => t.TableName == table) ?? throw new InvalidOperationException("未登记的基础设施表。");
        return registry.Platform.ConfigColumns.Where(c => c.ConfigTableId == definition.Id && c.IsVirtualColumn == 0).ToArray();
    }
    private string Q(string value) => session.Provider.QuoteIdentifier(value);
    public Task<List<Dictionary<string, object?>>> ReadAsync(string table,IReadOnlyDictionary<string,object?>? filters=null,CancellationToken cancellationToken=default)=>ReadInternalAsync(table,filters,false,cancellationToken);
    public Task<List<Dictionary<string,object?>>> ReadForUpdateAsync(string table,IReadOnlyDictionary<string,object?> filters,CancellationToken cancellationToken=default)
    {
        if(session.CurrentTransaction is null)throw new InvalidOperationException("锁查询必须在事务内。");
        return ReadInternalAsync(table,filters,true,cancellationToken);
    }
    private async Task<List<Dictionary<string, object?>>> ReadInternalAsync(string table,IReadOnlyDictionary<string,object?>? filters,bool forUpdate,CancellationToken cancellationToken)
    {
        var columns = Columns(table); var allowed = columns.Select(c => c.ColumnName).ToHashSet(StringComparer.Ordinal);
        return await CommandAsync(async command =>
        {
            var conditions = new List<string>();
            if (allowed.Contains("delete_no")) conditions.Add(Q("delete_no") + " IS NULL");
            if (filters is not null)
                foreach (var (key, value) in filters)
                {
                    if (!allowed.Contains(key)) throw new InvalidOperationException("查询字段未登记。");
                    if (value is null) conditions.Add(Q(key) + " IS NULL");
                    else { var name = "p" + command.Parameters.Count; conditions.Add(Q(key) + "=" + AddParameter(command, name, value)); }
                }
            command.CommandText = "SELECT " + string.Join(",", columns.Select(c => Q(c.ColumnName))) + " FROM " + Q(table) + (conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions));
            if(forUpdate)command.CommandText=session.Provider.BuildSelectForUpdate(command.CommandText);
            var result = new List<Dictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i=0;i<reader.FieldCount;i++) row[reader.GetName(i)] = await reader.IsDBNullAsync(i,cancellationToken) ? null : reader.GetValue(i);
                result.Add(row);
            }
            return result;
        }, cancellationToken);
    }
    public async Task InsertAsync(string table, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var columns = Columns(table);
        if(table.StartsWith("config_",StringComparison.Ordinal))
        {
            var defaults=values.ToDictionary(p=>p.Key,p=>p.Value);
            foreach(var column in columns.Where(c=>c.IsDbNotNull==1&&c.LogicalType=="int32"&&c.ColumnName.StartsWith("is_")))defaults.TryAdd(column.ColumnName,0);
            values=defaults;
        }
        var selected = columns.Where(c => values.ContainsKey(c.ColumnName)).ToArray();
        if (!selected.Any(c => c.ColumnName == "id")) throw new InvalidOperationException("基础设施写入必须明确标识。");
        await CommandAsync(async command =>
        {
            var placeholders = selected.Select(c => AddParameter(command,"p"+command.Parameters.Count,Coerce(values[c.ColumnName],c))).ToArray();
            command.CommandText = "INSERT INTO " + Q(table) + " (" + string.Join(",",selected.Select(c => Q(c.ColumnName))) + ") VALUES (" + string.Join(",",placeholders) + ")";
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }
    /// <summary>只按明确主键更新；字段白名单来自发布元数据，不能执行客户端 SQL。</summary>
    public async Task UpdateAsync(string table, long id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var columns = Columns(table).Where(c => c.ColumnName != "id" && values.ContainsKey(c.ColumnName)).ToArray();
        if (columns.Length == 0) return;
        await CommandAsync(async command =>
        {
            var assignments = columns.Select(c => Q(c.ColumnName)+"="+AddParameter(command,"p"+command.Parameters.Count,Coerce(values[c.ColumnName],c))).ToArray();
            var key = AddParameter(command,"record_id",id);
            command.CommandText="UPDATE "+Q(table)+" SET "+string.Join(",",assignments)+" WHERE "+Q("id")+"="+key;
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }
    private async Task<T> CommandAsync<T>(Func<DbCommand,Task<T>> operation,CancellationToken cancellationToken)
    {
        var transaction=session.CurrentTransaction;
        if(transaction is not null)
        {
            await using var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;
            return await operation(command);
        }
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync(cancellationToken);
        await using var ownCommand=connection.CreateCommand();return await operation(ownCommand);
    }
    private string AddParameter(DbCommand command,string name,object? value)
    {
        var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);
        return session.Provider.ParameterPlaceholder(name);
    }
    private static object? Coerce(object? value,ColumnMetadata column)
    {
        if(value is JsonElement json)
            value=json.ValueKind switch {JsonValueKind.Null=>null,JsonValueKind.String=>json.GetString(),JsonValueKind.Object or JsonValueKind.Array=>json.GetRawText(),JsonValueKind.True=>1,JsonValueKind.False=>0,_=>json.GetRawText()};
        if(value is null || value is string s && s.Length==0 && column.LogicalType is not ("string" or "json")) return null;
        return column.LogicalType switch
        {
            "int64"=>Convert.ToInt64(value,CultureInfo.InvariantCulture),
            "int32"=>Convert.ToInt32(value,CultureInfo.InvariantCulture),
            "decimal"=>Convert.ToDecimal(value,CultureInfo.InvariantCulture),
            "bool"=>Convert.ToBoolean(value,CultureInfo.InvariantCulture),
            "datetime" or "date"=>value is DateTime date?date:DateTime.Parse(Convert.ToString(value,CultureInfo.InvariantCulture)!,CultureInfo.InvariantCulture),
            "time"=>value is TimeSpan time?time:TimeSpan.Parse(Convert.ToString(value,CultureInfo.InvariantCulture)!,CultureInfo.InvariantCulture),
            _=>value
        };
    }
    public static long Id(IReadOnlyDictionary<string,object?> row,string key="id")=>Convert.ToInt64(row.GetValueOrDefault(key),CultureInfo.InvariantCulture);
    public static string Text(IReadOnlyDictionary<string,object?> row,string key)=>Convert.ToString(row.GetValueOrDefault(key),CultureInfo.InvariantCulture)??"";
}
