using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Data;
using MySqlConnector;
namespace Metarill.Generation.Tests;

/// <summary>仅解释SqlRowStore固定参数化查询，含严格IN成员匹配；不承担认证、权限或锁判断。</summary>
internal sealed class TrustedSecurityMemoryProvider:IDatabaseProvider
{
    internal Dictionary<string,List<Dictionary<string,object?>>> Rows{get;set;}=new(StringComparer.Ordinal);internal readonly List<string> Commands=[];internal readonly List<bool> WriteTransactions=[];internal int Opens,Commits,Rollbacks;
    internal IFreeSql Orm{get;}=DispatchProxy.Create<IFreeSql,TrustedForbiddenOrm>();
    public string Name=>"offline";public DatabaseCapabilities Capabilities=>new("offline",true,false);public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>Orm;public DbConnection CreateConnection(DatabaseOptions options)=>new MemoryConnection(this);public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);
    private DbDataReader Read(string sql,DbParameterCollection parameters)
    {
        Commands.Add(sql);var match=Regex.Match(sql,@"^SELECT (.+) FROM `([^`]+)`(?: WHERE (.*?))?(?: FOR UPDATE)?$");if(!match.Success)throw new NotSupportedException("测试替身拒绝未知SQL");var columns=match.Groups[1].Value.Split(',').Select(name=>name.Trim('`')).ToArray();var table=new DataTable();foreach(var column in columns)table.Columns.Add(column,typeof(object));
        foreach(var row in Rows.GetValueOrDefault(match.Groups[2].Value)??[]){var accepted=true;foreach(Match filter in Regex.Matches(match.Groups[3].Value,@"`([^`]+)`( IS NULL|=@([A-Za-z0-9_]+))"))accepted&=Equal(row.GetValueOrDefault(filter.Groups[1].Value),filter.Groups[2].Value==" IS NULL"?null:parameters[filter.Groups[3].Value].Value);foreach(Match set in Regex.Matches(match.Groups[3].Value,@"`([^`]+)` IN \((@[A-Za-z0-9_]+(?:,@[A-Za-z0-9_]+)*)\)"))accepted&=set.Groups[2].Value.Split(',').Any(parameter=>Equal(row.GetValueOrDefault(set.Groups[1].Value),parameters[parameter.TrimStart('@')].Value));if(accepted)table.Rows.Add(columns.Select(column=>row.GetValueOrDefault(column)??DBNull.Value).ToArray());}return table.CreateDataReader();
    }
    private int Write(string sql,DbParameterCollection parameters,bool transaction)
    {
        Commands.Add(sql);WriteTransactions.Add(transaction);if(!transaction)throw new InvalidOperationException("测试发现事务外写入");var update=Regex.Match(sql,@"^UPDATE `([^`]+)` SET (.+) WHERE `id`=@record_id$");
        if(update.Success){var row=(Rows.GetValueOrDefault(update.Groups[1].Value)??[]).SingleOrDefault(row=>Equal(row.GetValueOrDefault("id"),parameters["record_id"].Value));if(row is null)return 0;foreach(Match field in Regex.Matches(update.Groups[2].Value,@"`([^`]+)`=@([A-Za-z0-9_]+)"))row[field.Groups[1].Value]=Unwrap(parameters[field.Groups[2].Value].Value);return 1;}
        var insert=Regex.Match(sql,@"^INSERT INTO `([^`]+)` \((.+)\) VALUES \((.+)\)$");if(!insert.Success)throw new NotSupportedException("测试替身拒绝未知写入");var target=insert.Groups[1].Value;var names=insert.Groups[2].Value.Split(',').Select(name=>name.Trim('`')).ToArray();var values=insert.Groups[3].Value.Split(',').Select(name=>Unwrap(parameters[name.TrimStart('@')].Value)).ToArray();if(!Rows.TryGetValue(target,out var rows))Rows[target]=rows=[];var next=names.Select((name,index)=>(name,index)).ToDictionary(pair=>pair.name,pair=>values[pair.index]);if(rows.Any(row=>Equal(row.GetValueOrDefault("id"),next["id"])))throw new InvalidOperationException("模拟主键重复");rows.Add(next);return 1;
    }
    private static object? Unwrap(object? value)=>value is DBNull?null:value;private static bool Equal(object? left,object? right)=>string.Equals(Convert.ToString(Unwrap(left),CultureInfo.InvariantCulture),Convert.ToString(Unwrap(right),CultureInfo.InvariantCulture),StringComparison.Ordinal)&&((Unwrap(left)is null)==(Unwrap(right)is null));
    private sealed class MemoryConnection(TrustedSecurityMemoryProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open(){provider.Opens++;state=ConnectionState.Open;}public override void Close()=>state=ConnectionState.Closed;protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new MemoryTransaction(this,provider);protected override DbCommand CreateDbCommand()=>new MemoryCommand(provider,this);
    }
    private sealed class MemoryTransaction(MemoryConnection connection,TrustedSecurityMemoryProvider provider):DbTransaction
    {
        private readonly Dictionary<string,List<Dictionary<string,object?>>> before=provider.Rows.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(row=>new Dictionary<string,object?>(row)).ToList());public override IsolationLevel IsolationLevel=>IsolationLevel.Unspecified;protected override DbConnection DbConnection=>connection;public override void Commit()=>provider.Commits++;public override void Rollback(){provider.Rollbacks++;provider.Rows=before;}
    }
    private sealed class MemoryCommand(TrustedSecurityMemoryProvider provider,MemoryConnection connection):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override object? ExecuteScalar()=>throw new NotSupportedException();public override int ExecuteNonQuery()=>provider.Write(CommandText,Parameters,DbTransaction is not null);protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Read(CommandText,Parameters);
    }
}
public class TrustedForbiddenOrm:DispatchProxy{protected override object? Invoke(MethodInfo? method,object?[]? arguments)=>method!.Name=="Dispose"?null:throw new InvalidOperationException("认证模板离线测试禁止ORM或联网");}
