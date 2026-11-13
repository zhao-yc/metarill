using System.Data.Common;
using System.Globalization;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;

/// <summary>按固定反向外键表达式找到R1消费者，目标本身可缺失；重算仍复用原维护当前读计划。</summary>
internal sealed class MySqlMixedRelationMutationExecutor(DatabaseSession session)
{
    /// <summary>仅重算本次目标事件涉及的输出，原始保存额外刷新其直接物理引用。</summary>
    internal async Task PropagateAsync(MixedRelationMutationPlan plan,string table,long id,string project,IReadOnlySet<string> changed,bool allFields,bool targetSaved,HashSet<string> validated,Action<string,long,IReadOnlyDictionary<string,object?>> onChanged,CancellationToken token)
    {
        var transaction=session.CurrentTransaction??throw new InvalidOperationException("混合传播要求当前业务事务。");var materializer=new RelationMaintenanceExecutor(session);
        foreach(var consumer in plan.Consumers)
        foreach(var reverse in consumer.Read.Reverse.Where(reverse=>reverse.TargetTable==table))
        {
            var allowed=reverse.Outputs.Where(item=>consumer.Table.Fields.Any(field=>field.Name==item.Key)&&reverse.Affects(item.Key,changed,allFields,targetSaved)).Select(item=>item.Key).ToHashSet(StringComparer.Ordinal);if(allowed.Count==0)continue;
            await ValidateAsync(consumer,validated,token);long after=0;
            while(true)
            {
                var ids=await ReverseIds(consumer.Read,reverse,id,project,after,transaction,token);if(ids.Count==0)break;after=ids[^1];var rows=await materializer.Sources(consumer.Table,project,0,transaction,token,ids,allowed);if(rows.Count!=ids.Count)throw new InvalidOperationException("混合传播反查与来源活动范围不一致，整笔拒绝提交。");
                foreach(var patch in await materializer.RefreshLockedAsync(consumer.Table,rows,project,allowed,token))onChanged(consumer.Table.Option.TableName,patch.Id,patch.Values);
            }
        }
    }
    /// <summary>来源自身保存后只对真实输入变化刷新；缓存输入不能成为客户端写入通道。</summary>
    internal async Task RefreshSourceAsync(MixedRelationConsumer consumer,long id,string project,HashSet<string> validated,Action<string,long,IReadOnlyDictionary<string,object?>> onChanged,CancellationToken token)
    {
        var materializer=new RelationMaintenanceExecutor(session);
        foreach(var patch in await materializer.RefreshSavedSourceAsync(consumer.Table,id,project,validated.Add(consumer.Table.Option.Id),session.CurrentTransaction!,token))
            onChanged(consumer.Table.Option.TableName,patch.Id,patch.Values);
    }
    /// <summary>每个受影响消费者只预检一次。两张表合并结构查询，失败仍回滚已写目标和副本。</summary>
    private async Task ValidateAsync(MixedRelationConsumer consumer,HashSet<string> validated,CancellationToken token)
    {
        if(validated.Add(consumer.Table.Option.Id))
            await new RelationMaintenanceExecutor(session).ValidatePhysical(consumer.Read,session.CurrentTransaction!,token,
                reusePinnedStructure:true,batchReverseLayouts:true);
    }
    /// <summary>前缀联接只定位真实来源外键，不依赖已删除目标；稳定主键分批当前锁读，不做OFFSET。</summary>
    private async Task<List<long>> ReverseIds(MySqlNonRedundantReadPlan plan,MySqlReverseProjection reverse,long target,string project,long after,DbTransaction transaction,CancellationToken token)
    {
        await using var command=MySqlFloatingCommand.Wrap(transaction.Connection!.CreateCommand());command.Transaction=transaction;var match=reverse.TargetPredicate;command.CommandText="SELECT b.`id`"+reverse.From+" WHERE "+plan.Scope+" AND b.`id`>@after AND "+match+" ORDER BY b.`id` LIMIT 128 FOR UPDATE";Add(command,"target_id",target);Add(command,"target_text",target.ToString(CultureInfo.InvariantCulture));Add(command,"after",after);Add(command,"project_scope_number",long.Parse(project,CultureInfo.InvariantCulture));Add(command,"project_scope_text",project);foreach(var parameter in plan.Constants)Add(command,parameter.Key,parameter.Value??DBNull.Value);var ids=new List<long>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))ids.Add(reader.GetInt64(0));return ids;
    }
    /// <summary>业务ID和项目值仅通过驱动参数绑定。</summary>
    private static void Add(DbCommand command,string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);}
}
