using System.Data.Common;
using Metarill.Core.Contracts;
namespace Metarill.Data;

public sealed partial class MySqlDatabaseProvider
{
    /// <summary>只读取已写主键的固定时间列，加入原事务，不开新连接或改SQL模式。</summary>
    public async Task<IReadOnlyList<TimeSpan?>> ReadPersistedTimeValuesAsync(DbTransaction transaction,string table,long id,IReadOnlyList<string> columns,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(transaction);ArgumentNullException.ThrowIfNull(columns);cancellationToken.ThrowIfCancellationRequested();
        if(id<=0||columns.Count==0||columns.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=columns.Count)throw new ArgumentException("TIME回读必须具有正主键和不重复的固定列。");
        var fields=columns.Select(QuoteIdentifier).ToArray();var tableName=QuoteIdentifier(table);
        var connection=transaction.Connection??throw new InvalidOperationException("TIME回读需要当前有效事务。");
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="SELECT "+string.Join(",",fields)+" FROM "+tableName+" WHERE "+QuoteIdentifier("id")+"="+ParameterPlaceholder("persisted_time_id");
        var parameter=command.CreateParameter();parameter.ParameterName="persisted_time_id";parameter.Value=id;command.Parameters.Add(parameter);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(reader.FieldCount!=columns.Count||!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("TIME写后回读缺少固定记录或列。");
        var result=new TimeSpan?[columns.Count];
        for(var i=0;i<result.Length;i++)
        {
            if(await reader.IsDBNullAsync(i,cancellationToken))continue;
            result[i]=reader.GetValue(i) switch{TimeSpan time=>time,string text=>SqlTimeValue.ParseText(text)??throw new InvalidOperationException("TIME回读不能将非空文本变为null。"),_=>throw new InvalidOperationException("TIME回读类型与发布模型不匹配。")};
            if(result[i] is {} value)ValidateTimeValue(value);
        }
        if(await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("TIME写后回读主键不唯一。");
        return Array.AsReadOnly(result);
    }
}
