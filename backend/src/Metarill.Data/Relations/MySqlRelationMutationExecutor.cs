using System.Data.Common;
using System.Globalization;
using System.Text.Json;
namespace Metarill.Data.Relations;

/// <summary>执行预编译依赖图，所有读写必须加入调用方事务；不调用消费者Service、钩子或文件观察者。</summary>
internal sealed class MySqlRelationMutationExecutor(DatabaseSession session)
{
    /// <summary>只返回原变动实体自身被刷新字段，生成服务据此保持写回执与同事务数据库一致。</summary>
    public async Task<IReadOnlyDictionary<string,object?>> AfterMutationAsync(RelationMutationPlan plan,string table,long id,string projectId,IReadOnlySet<string> changed,bool allFields,CancellationToken token=default,Action<string,long,IReadOnlyDictionary<string,object?>>? onChanged=null)
    {
        try{return await RunAsync(plan,table,id,projectId,changed,allFields,token,onChanged);}
        catch(MySqlConnector.MySqlException error)when(error.Number is 1213 or 1205){throw new InvalidOperationException("名称传播遇到并发冲突，当前操作不提交，请重试。",error);}
    }
    /// <summary>外层事务统一负责回滚，不重试可能带有业务副作用的整个Service。</summary>
    private async Task<IReadOnlyDictionary<string,object?>> RunAsync(RelationMutationPlan plan,string table,long id,string projectId,IReadOnlySet<string> changed,bool allFields,CancellationToken token,Action<string,long,IReadOnlyDictionary<string,object?>>? onChanged)
    {
        if(session.Options.Provider!="mysql")throw new NotSupportedException("该方言尚未完成关系传播验收。");var transaction=session.CurrentTransaction??throw new InvalidOperationException("关系传播要求当前业务事务。");if(id<=0||string.IsNullOrWhiteSpace(projectId))throw new ArgumentException("关系传播缺少真实记录或项目。");
        var origin=new Dictionary<string,object?>(StringComparer.Ordinal);var pending=new Queue<(string Table,long Id,HashSet<string> Fields,bool All)>();pending.Enqueue((table,id,new(changed,StringComparer.Ordinal),allFields));
        while(pending.TryDequeue(out var change))
        {
            token.ThrowIfCancellationRequested();
            foreach(var group in plan.Bindings.Where(binding=>binding.Target.Name==change.Table&&(change.All||change.Fields.Contains(binding.Value))).GroupBy(binding=>(binding.Source.Name,binding.Foreign)))
            {
                var bindings=group.ToArray();long after=0;
                while(true)
                {
                    var rows=await Consumers(plan.Bindings,bindings,change.Id,after,transaction,projectId,token);if(rows.Count==0)break;after=rows[^1].Id;
                    // 同一来源外键的所有缓存共用本批目标集合和当前锁读取，不逐缓存／逐记录查询名称。
                    var peers=plan.Bindings.Where(binding=>binding.Source.Name==group.Key.Name&&binding.Foreign==group.Key.Foreign).ToArray();var ids=rows.SelectMany(row=>Members(row.Foreign,peers[0])).OfType<long>().Distinct().Order().ToArray();var values=await Targets(peers,ids,transaction,projectId,token);
                    var changedRows=new List<RelationRowPatch>();
                    foreach(var row in rows)
                    {
                        var patch=new Dictionary<string,object?>(StringComparer.Ordinal);var members=Members(row.Foreign,peers[0]);
                        foreach(var binding in peers)
                        {
                            object? value=binding.Multiple?row.Foreign is null?null:JsonSerializer.Serialize(members.Select(member=>member is null?null:values.GetValueOrDefault((member.Value,binding.Value))).ToArray()):members.FirstOrDefault() is{} key?values.GetValueOrDefault((key,binding.Value)):null;
                            if(!Same(row.Values.GetValueOrDefault(binding.Cache),value))patch[binding.Cache]=value;
                        }
                        if(patch.Count==0)continue;await Update(peers[0].Source,row.Id,patch,transaction,projectId,token);changedRows.Add(new(row.Id,new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(patch)));
                    }
                    // 本批写回全部核验后才进入下游队列；失败由同一外层事务回滚全部先前批次。
                    await RelationCacheStorageVerifier.VerifyManyAsync(session,peers[0].Source.Name,Scope(peers[0].Source,"c"),Project(peers[0].Source,projectId),changedRows,transaction,token);
                    foreach(var patch in changedRows){if(group.Key.Name==table&&patch.Id==id)foreach(var item in patch.Values)origin[item.Key]=item.Value;onChanged?.Invoke(group.Key.Name,patch.Id,patch.Values);pending.Enqueue((group.Key.Name,patch.Id,new(patch.Values.Keys,StringComparer.Ordinal),false));}
                }
            }
        }
        return origin;
    }
    private sealed record Consumer(long Id,object? Foreign,IReadOnlyDictionary<string,object?> Values);
    /// <summary>反查真实外键，不依赖可能已被删除的目标联接；按主键分批取得写锁。</summary>
    private async Task<IReadOnlyList<Consumer>> Consumers(IReadOnlyList<RelationCache> plan,RelationCache[] bindings,long target,long after,DbTransaction transaction,string project,CancellationToken token)
    {
        var binding=bindings[0]; // 缓存投影由完整源外键集合补齐，避免另一缓存被误判为缺省。
        var all=plan.Where(item=>item.Source.Name==binding.Source.Name&&item.Foreign==binding.Foreign).ToArray();
        await using var command=Command(transaction);var alias="c";var foreign=alias+"."+Q(binding.Foreign);var condition=binding.Multiple?"((SELECT 1 FROM JSON_TABLE(CASE WHEN JSON_VALID("+foreign+") THEN CASE WHEN JSON_TYPE(CAST("+foreign+" AS JSON))='ARRAY' THEN "+foreign+" ELSE '[]' END ELSE '[]' END, '$[*]' COLUMNS(member_value VARCHAR(32) PATH '$')) relation_member WHERE BINARY relation_member.member_value=BINARY @member LIMIT 1) IS NOT NULL)":binding.ForeignType=="string"?"BINARY "+foreign+"=BINARY @member":foreign+"=@target";
        command.CommandText="SELECT c.`id`,"+foreign+","+string.Join(",",all.Select(item=>"c."+Q(item.Cache)))+" FROM "+Q(binding.Source.Name)+" c WHERE "+Scope(binding.Source,alias)+" AND c.`id`>@after AND "+condition+" ORDER BY c.`id` LIMIT 128 FOR UPDATE";Add(command,"project",Project(binding.Source,project));Add(command,"after",after);Add(command,"member",target.ToString(CultureInfo.InvariantCulture));Add(command,"target",target);var result=new List<Consumer>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var values=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<all.Length;index++)values[all[index].Cache]=await reader.IsDBNullAsync(index+2,token)?null:reader.GetValue(index+2);result.Add(new(reader.GetInt64(0),await reader.IsDBNullAsync(1,token)?null:reader.GetValue(1),values));}return result;
    }
    /// <summary>目标当前读保留项目与软删边界，按真实ID批量查询；缺失目标只置空名称，不修改外键。</summary>
    private async Task<Dictionary<(long,string),object?>> Targets(RelationCache[] bindings,long[] ids,DbTransaction transaction,string project,CancellationToken token)
    {
        var target=bindings[0].Target;var fields=bindings.Select(binding=>binding.Value).Distinct(StringComparer.Ordinal).ToArray();var result=new Dictionary<(long,string),object?>();
        foreach(var batch in ids.Chunk(200))
        {
            await using var command=Command(transaction);command.CommandText="SELECT t.`id`,"+string.Join(",",fields.Select(field=>"t."+Q(field)))+" FROM "+Q(target.Name)+" t WHERE "+Scope(target,"t")+" AND t.`id` IN ("+string.Join(",",Enumerable.Range(0,batch.Length).Select(index=>"@id"+index))+") ORDER BY t.`id` FOR SHARE";Add(command,"project",Project(target,project));for(var index=0;index<batch.Length;index++)Add(command,"id"+index,batch[index]);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))for(var index=0;index<fields.Length;index++)result[(reader.GetInt64(0),fields[index])]=await reader.IsDBNullAsync(index+1,token)?null:reader.GetValue(index+1);
        }
        return result;
    }
    /// <summary>只更新当前项目活动消费者的派生列，不更改业务值、ID、审计字段或触发业务钩子。</summary>
    private async Task Update(RelationTable table,long id,Dictionary<string,object?> patch,DbTransaction transaction,string project,CancellationToken token)
    {
        await using var command=Command(transaction);command.CommandText="UPDATE "+Q(table.Name)+" c SET "+string.Join(",",patch.Keys.Select((field,index)=>Q(field)+"=@value"+index))+" WHERE c.`id`=@id AND "+Scope(table,"c");Add(command,"id",id);Add(command,"project",Project(table,project));var ordinal=0;foreach(var value in patch.Values)Add(command,"value"+ordinal++,value);if(await command.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("名称传播消费者在当前事务中不再匹配，已拒绝提交。");
    }
    /// <summary>历史坏成员仅产生空名称位置，不修补原ID；非数组／坏JSON的显示沿原空数组合同。</summary>
    private static long?[] Members(object? raw,RelationCache binding)
    {
        if(raw is null)return [];if(!binding.Multiple)return [Id(raw)];
        try{using var document=JsonDocument.Parse(Convert.ToString(raw,CultureInfo.InvariantCulture)!);if(document.RootElement.ValueKind!=JsonValueKind.Array)return [];if(document.RootElement.GetArrayLength()>binding.MaximumItems)throw new InvalidOperationException("名称传播引用超过已发布数组预算，未截断数据。");return document.RootElement.EnumerateArray().Select(value=>value.ValueKind is JsonValueKind.String or JsonValueKind.Number?Id(value.ValueKind==JsonValueKind.String?value.GetString():value.GetRawText()):null).ToArray();}catch(JsonException){return [];}
    }
    /// <summary>整数和字符串始终精确解析，不经浮点数或宽松数值转换。</summary>
    private static long? Id(object? value){var text=Convert.ToString(value,CultureInfo.InvariantCulture);return long.TryParse(text,System.Globalization.NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0&&id.ToString(CultureInfo.InvariantCulture)==text?id:null;}
    /// <summary>驱动标量同类型比较，空值一致；缓存JSON只在实际文本变化时写入。</summary>
    private static bool Same(object? before,object? after)=>Equals(before,after);
    /// <summary>所有SQL只引用编译过的标识符，业务值一律绑定参数。</summary>
    private string Q(string name)=>session.Provider.QuoteIdentifier(name);
    /// <summary>每张消费者／目标表独立保留真实项目和软删条件。</summary>
    private string Scope(RelationTable table,string alias)=>alias+".`yz_project_id`=@project"+(table.Deleted?" AND "+alias+".`delete_no` IS NULL":"");
    /// <summary>按元数据逻辑类型绑定项目，数字ID禁止隐式宽松转换。</summary>
    private static object Project(RelationTable table,string project)=>table.ProjectType=="string"?project:long.TryParse(project,System.Globalization.NumberStyles.None,CultureInfo.InvariantCulture,out var id)&&id>0&&id.ToString(CultureInfo.InvariantCulture)==project?id:throw new ArgumentException("传播项目ID不是规范正整数。");
    /// <summary>命令必须使用现有事务连接，不新开连接或自行提交。</summary>
    private static DbCommand Command(DbTransaction transaction){var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;return command;}
    /// <summary>所有业务值通过驱动参数传递，不能拼接为SQL。</summary>
    private static void Add(DbCommand command,string name,object? value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}
}
