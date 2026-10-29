using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;

namespace Metarill.Runtime.Persistence;

/// <summary>仅供元数据、认证及文件基础设施使用；业务 CRUD 由强类型生成 Mapper 执行。</summary>
public sealed class SqlRowStore(DatabaseSession session, MetadataCatalog infrastructure)
{
    public DatabaseSession Session => session;
    // 发布模型在仓储构造时一次编译，不在每条SQL或每个业务值上重新解释关系元数据。
    private readonly IReadOnlyDictionary<string,IReadOnlyList<ColumnMetadata>> physicalColumns = infrastructure.ConfigTables.ToDictionary(table=>table.TableName,table=>MetadataStoragePolicy.ForTable(infrastructure,table).PhysicalColumns,StringComparer.Ordinal);
    /// <summary>只使用构造时已编译的物理字段白名单，未知表不能进入通用仓储。</summary>
    private IReadOnlyList<ColumnMetadata> Columns(string table) => physicalColumns.TryGetValue(table,out var columns)?columns:throw new InvalidOperationException("未登记的基础设施表。");
    private string Q(string value) => session.Provider.QuoteIdentifier(value);
    public Task<List<Dictionary<string, object?>>> ReadAsync(string table,IReadOnlyDictionary<string,object?>? filters=null,CancellationToken cancellationToken=default)=>ReadInternalAsync(table,filters,false,cancellationToken);
    /// <summary>只用于种子存在性与开发备份，包含逻辑删除记录；业务查询不得使用此入口。</summary>
    public Task<List<Dictionary<string,object?>>> ReadIncludingDeletedAsync(string table,IReadOnlyDictionary<string,object?>? filters=null,CancellationToken cancellationToken=default)=>ReadInternalAsync(table,filters,false,cancellationToken,true);
    /// <summary>账号初始化按用户名唯一索引检查墓碑冲突；只允许在既有事务中读取，不能用于恢复已删除账号。</summary>
    public Task<List<Dictionary<string,object?>>> ReadAccountNameForInitializationAsync(string name,CancellationToken cancellationToken=default)
    {
        if(session.CurrentTransaction is null)throw new InvalidOperationException("锁查询必须在事务内。");
        return ReadInternalAsync("yz_manager",new Dictionary<string,object?>{{"name",name}},true,cancellationToken,true);
    }
    /// <summary>反向导入归属当前读包含墓碑；RR下锁整个配置表范围，兼容非协作新增及不同名称排序规则，防止空集幻影。</summary>
    public async Task<IReadOnlyList<Dictionary<string,object?>>> ReadTableReservationsForImportAsync(CancellationToken token = default)
    {
        var transaction = session.CurrentTransaction ?? throw new InvalidOperationException("导入归属当前读必须在配置事务内。");
        await session.Provider.EnsureReadSnapshotAsync(transaction, token);
        // 配置写属于管理低频入口；选择完整范围锁，不依赖未知索引/排序规则证明不存在。
        return await ReadInternalAsync("config_table", null, true, token, true);
    }
    public Task<List<Dictionary<string,object?>>> ReadForUpdateAsync(string table,IReadOnlyDictionary<string,object?> filters,CancellationToken cancellationToken=default)
    {
        if(session.CurrentTransaction is null)throw new InvalidOperationException("锁查询必须在事务内。");
        return ReadInternalAsync(table,filters,true,cancellationToken);
    }
    /// <summary>锁后安全复核只按已登记的整数ID集合读取；空集不查询，超限或损坏集合不截断。</summary>
    public Task<List<Dictionary<string,object?>>> ReadForUpdateByIdsAsync(string table, string idColumn, IReadOnlyCollection<long> ids,
        IReadOnlyDictionary<string,object?>? filters=null, CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (session.CurrentTransaction is null) throw new InvalidOperationException("锁查询必须在事务内。");
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > 1000 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Count) throw new ArgumentException("锁查询要求最多1000个不重复的正整数ID。");
        var column = Columns(table).SingleOrDefault(column => column.ColumnName == idColumn);
        if (column is null || column.LogicalType is not ("id" or "int64") || idColumn != "id" && !idColumn.EndsWith("_id", StringComparison.Ordinal))
            throw new InvalidOperationException("锁查询只能使用已登记的整数ID字段。");
        if (!Columns(table).Any(column => column.ColumnName == "id" && column.LogicalType is "id" or "int64"))
            throw new InvalidOperationException("集合锁查询必须有已登记的整数主键。");
        if (ids.Count == 0) return Task.FromResult(new List<Dictionary<string,object?>>());
        return ReadInternalAsync(table, filters, true, cancellationToken, idColumn: idColumn, ids: ids.Order().ToArray());
    }
    private async Task<List<Dictionary<string, object?>>> ReadInternalAsync(string table,IReadOnlyDictionary<string,object?>? filters,bool forUpdate,CancellationToken cancellationToken,bool includeDeleted=false, string? idColumn=null, IReadOnlyList<long>? ids=null)
    {
        var columns = Columns(table); var allowed = columns.Select(c => c.ColumnName).ToHashSet(StringComparer.Ordinal);
        return await CommandAsync(async command =>
        {
            var conditions = new List<string>();
            if (!includeDeleted && allowed.Contains("delete_no")) conditions.Add(Q("delete_no") + " IS NULL");
            if (filters is not null)
                foreach (var (key, value) in filters)
                {
                    if (!allowed.Contains(key)) throw new InvalidOperationException("查询字段未登记。");
                    if (value is null) conditions.Add(Q(key) + " IS NULL");
                    else { var name = "p" + command.Parameters.Count; conditions.Add(Q(key) + "=" + AddParameter(command, name, value)); }
                }
            if (idColumn is not null)
            {
                // 字段与数值分别经过发布白名单及正整数集合校验，所有ID仍作为驱动参数。
                if (ids is null || ids.Count == 0) throw new InvalidOperationException("集合锁查询不能退化为全表读取。");
                conditions.Add(Q(idColumn) + " IN (" + string.Join(",", ids.Select(id => AddParameter(command, "p" + command.Parameters.Count, id))) + ")");
            }
            command.CommandText = "SELECT " + string.Join(",", columns.Select(c => Q(c.ColumnName))) + " FROM " + Q(table) + (conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions));
            // 固定查询返回顺序；物理取锁仍依赖项目/后台预锁，不能把ORDER BY或IN参数当取锁证明。
            if (idColumn is not null) command.CommandText += " ORDER BY " + Q("id");
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
            foreach(var column in columns.Where(c=>!string.IsNullOrEmpty(c.DefaultValue)))defaults.TryAdd(column.ColumnName,column.DefaultValue);
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
    public async Task<int> UpdateAsync(string table, long id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var columns = Columns(table).Where(c => c.ColumnName != "id" && values.ContainsKey(c.ColumnName)).ToArray();
        if (columns.Length == 0) return 0;
        return await CommandAsync(async command =>
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
        if(value is TimeSpan time)session.Provider.ValidateTimeValue(time);
        var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);
        return session.Provider.ParameterPlaceholder(name);
    }
    /// <summary>基础设施写入与生成字段共用时间文本，手写TimeSpan也校验原控件范围。</summary>
    private static TimeSpan? CoerceTime(object value,ColumnMetadata column)
    {
        var time=value is TimeSpan span?span:column.ControlKind=="time"?Metarill.Core.Contracts.ClockTimeValue.ParseText(Convert.ToString(value,CultureInfo.InvariantCulture)):Metarill.Core.Contracts.SqlTimeValue.ParseText(Convert.ToString(value,CultureInfo.InvariantCulture));
        if(column.ControlKind=="time"&&time is {} clock&&!Metarill.Core.Contracts.ClockTimeValue.IsClock(clock))throw new System.Text.Json.JsonException("时间必须是有效日内时刻。");
        return time;
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
            "double"=>Metarill.Core.Contracts.ExactNumericValue.TryDouble(Convert.ToString(value,CultureInfo.InvariantCulture),out var floating)?floating:throw new FormatException("浮点数必须是有效有限值，不能溢出或下溢为零。"),
            "bool"=>Convert.ToBoolean(value,CultureInfo.InvariantCulture),
            "datetime" or "date"=>value is DateTime date?date:DateTime.Parse(Convert.ToString(value,CultureInfo.InvariantCulture)!,CultureInfo.InvariantCulture),
            "time"=>CoerceTime(value,column),
            _=>value
        };
    }
    public static long Id(IReadOnlyDictionary<string,object?> row,string key="id")=>Convert.ToInt64(row.GetValueOrDefault(key),CultureInfo.InvariantCulture);
    public static string Text(IReadOnlyDictionary<string,object?> row,string key)=>Convert.ToString(row.GetValueOrDefault(key),CultureInfo.InvariantCulture)??"";
}
