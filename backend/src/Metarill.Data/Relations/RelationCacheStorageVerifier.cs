using System.Data.Common;
namespace Metarill.Data.Relations;

/// <summary>内部缓存写入后核对实际物理值，自动传播按有界批次回读，避免逐来源再增加查询。</summary>
internal static class RelationCacheStorageVerifier
{
    /// <summary>维护单行写回也使用同一事务核验，不允许静默截断或触发器改写。</summary>
    internal static Task VerifyAsync(DatabaseSession session,string table,string scope,long id,object project,IReadOnlyDictionary<string,object?> values,DbTransaction transaction,CancellationToken token)=>values.Count==0?Task.CompletedTask:VerifyManyAsync(session,table,scope,project,[new(id,values)],transaction,token);
    /// <summary>scope仅来自编译过的项目／软删固定谓词；一次最多核对原执行器已锁的128行。</summary>
    internal static async Task VerifyManyAsync(DatabaseSession session,string table,string scope,object project,IReadOnlyList<RelationRowPatch> patches,DbTransaction transaction,CancellationToken token)
    {
        if(patches.Count==0)return;if(transaction.Connection is null||session.CurrentTransaction!=transaction)throw new InvalidOperationException("关系缓存核验必须加入原事务。");if(patches.Count>128||patches.Any(x=>x.Id<=0||x.Values.Count==0)||patches.Select(x=>x.Id).Distinct().Count()!=patches.Count)throw new InvalidOperationException("关系缓存核验批次不符合已锁来源范围。");var rows=patches.ToDictionary(x=>x.Id);var fields=patches.SelectMany(x=>x.Values.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();var ordinals=fields.Select((name,index)=>(name,index:index+1)).ToDictionary(x=>x.name,x=>x.index,StringComparer.Ordinal);await using var command=transaction.Connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT c.`id`,"+string.Join(",",fields.Select(x=>"c."+session.Provider.QuoteIdentifier(x)))+" FROM "+session.Provider.QuoteIdentifier(table)+" c WHERE "+scope+" AND c.`id` IN ("+string.Join(",",Enumerable.Range(0,patches.Count).Select(x=>"@row"+x))+") ORDER BY c.`id` FOR UPDATE";Add(command,"project",project);for(var i=0;i<patches.Count;i++)Add(command,"row"+i,patches[i].Id);var seen=new HashSet<long>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)){var id=reader.GetInt64(0);if(!seen.Add(id)||!rows.TryGetValue(id,out var patch))throw new InvalidOperationException("关系缓存在存储后不再匹配，当前事务未提交。");foreach(var item in patch.Values){var index=ordinals[item.Key];var actual=await reader.IsDBNullAsync(index,token)?null:reader.GetValue(index);if(!Equals(actual,item.Value))throw new InvalidOperationException("关系缓存在存储后发生变化，当前事务未提交。");}}
        if(seen.Count!=patches.Count)throw new InvalidOperationException("关系缓存在存储后不再匹配，当前事务未提交。");
    }
    /// <summary>不将ID、项目或缓存内容拼入SQL。</summary>
    private static void Add(DbCommand command,string name,object value){var p=command.CreateParameter();p.ParameterName=name;p.Value=value;command.Parameters.Add(p);}
}
