using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;
internal sealed record RelationRowPatch(long Id,IReadOnlyDictionary<string,object?> Values);

/// <summary>维护只执行已发布的整表计划，不接收字段、SQL、连接或项目覆盖。</summary>
public sealed class RelationMaintenanceExecutor(DatabaseSession session)
{
    /// <summary>整批使用一个业务事务，结构不匹配、取消、约束或并发失败全部回滚。</summary>
    public async Task<MaintenanceResult> BackfillAsync(RelationMaintenanceCatalog catalog,string tableId,CancellationToken token=default)
    {
        if(!catalog.Plans.TryGetValue(tableId,out var table))throw new ArgumentException("配置表不存在或不属于当前宿主项目。");if(table.Error is{} planError)throw new NotSupportedException(planError);
        if(session.Provider.Name!="mysql")throw new NotSupportedException("维护回填尚未完成该方言验收。");
        if(table.Read is null)return new(table.Option.Id,table.Option.RawName,table.Option.TableName,0,0,[]);
        try{return await session.InTransactionAsync(()=>RunAsync(catalog,table,token),token);}
        catch(MySqlConnector.MySqlException error)when(error.Number is 1213 or 1205){throw new InvalidOperationException("回填遇到并发冲突，本次整批未提交，请重试。",error);}
    }
    /// <summary>当前锁读分批来源和目标，只更新派生列；不调用消费者Service／钩子或文件观察者。</summary>
    private async Task<MaintenanceResult> RunAsync(RelationMaintenanceCatalog catalog,MaintenanceTable table,CancellationToken token)
    {
        var transaction=session.CurrentTransaction??throw new InvalidOperationException("回填缺少完整业务事务。");var plan=table.Read!;await ValidatePhysical(plan,transaction,token);var counts=table.Fields.ToDictionary(field=>field.Name,_=>0L,StringComparer.Ordinal);long after=0;
        while(true)
        {
            token.ThrowIfCancellationRequested();var rows=await Sources(table,catalog.ProjectId,after,transaction,token);if(rows.Count==0)break;after=(long)rows[^1]["id"]!;
            foreach(var patch in await RefreshLockedAsync(table,rows,catalog.ProjectId,null,token))
            {
                foreach(var field in patch.Values.Keys.Select(name=>table.Fields.Single(item=>item.Name==name)).Select(field=>field.DisplayOf??field.Name).Distinct(StringComparer.Ordinal))counts[field]++;
                await new ProjectRelationMutationPropagator(session).AfterMutationAsync(catalog,table.Option.TableName,patch.Id,catalog.ProjectId,new HashSet<string>(patch.Values.Keys,StringComparer.Ordinal),false,false,token);
            }
        }
        var details=table.Fields.Where(field=>field.DisplayOf is null).Select(field=>{var display=table.Fields.SingleOrDefault(item=>item.DisplayOf==field.Name);return new MaintenanceColumnResult(field.Name,field.Path,field.Relation,field.TargetTable,field.TargetColumn,checked((int)counts[field.Name]),display?.Name,display is null?null:display.Path.Split('.')[^1]);}).ToArray();return new(table.Option.Id,table.Option.RawName,table.Option.TableName,details.Length,checked((int)counts.Values.Sum()),Array.AsReadOnly(details));
    }
    /// <summary>调用方已锁来源后统一执行当前读取，只写本次事件允许的派生列，返回实际变化供同事务队列继续传播。</summary>
    internal async Task<IReadOnlyList<RelationRowPatch>> RefreshLockedAsync(MaintenanceTable table,IReadOnlyList<Dictionary<string,object?>> rows,string projectId,IReadOnlySet<string>? allowed,CancellationToken token)
    {
        if(rows.Count==0)return [];var transaction=session.CurrentTransaction??throw new InvalidOperationException("派生刷新缺少当前事务。");var plan=table.Read!;var patches=new List<RelationRowPatch>();
            foreach(var row in rows)foreach(var multiple in table.Multiples)if(row[multiple.Foreign] is{} raw)
            {
                try{using var document=JsonDocument.Parse(Convert.ToString(raw,CultureInfo.InvariantCulture)!);if(document.RootElement.ValueKind==JsonValueKind.Array&&document.RootElement.GetArrayLength()>multiple.MaximumItems)throw new InvalidOperationException("历史多值记录超过发布预算，本次回填未截断数据。");}catch(JsonException){} // 坏历史ID仅按原空集合显示，禁止修补源ID。
            }
            var ids=rows.Select(row=>(long)row["id"]!).ToArray();var projected=(await new MySqlNonRedundantReader(session).ReadPageAsync(plan,[new("id",FixedReadComparison.In,ids.Cast<object?>().ToArray())],[new("id")],1,128,token,projectId:projectId,currentRead:true)).Records;
            if(projected.Count!=rows.Count||!projected.Select(row=>(long)row["id"]!).Order().SequenceEqual(ids.Order()))throw new InvalidOperationException("回填来源锁与读取范围不一致，拒绝提交。");
            foreach(var values in projected)
            {
                var id=(long)values["id"]!;var before=rows.Single(row=>(long)row["id"]! ==id);var patch=new Dictionary<string,object?>(StringComparer.Ordinal);
                foreach(var field in table.Fields.Where(field=>allowed is null||allowed.Contains(field.Name)))
                {
                    var empty=table.Multiples.Any(multiple=>multiple.Caches.Contains(field.Name)&&before[multiple.Foreign] is null);var value=empty?null:values[field.Name];if(!Equals(before[field.Name],value))patch[field.Name]=value;
                }
                if(patch.Count==0)continue;await Update(table,id,projectId,patch,transaction,token);patches.Add(new(id,new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(patch)));
            }
        return patches.AsReadOnly();
    }
    /// <summary>先获取事务内元数据锁，再核库名、InnoDB、完整依赖列类型和主键；没有DDL或结构自动修复。</summary>
    internal async Task ValidatePhysical(MySqlNonRedundantReadPlan plan,DbTransaction transaction,CancellationToken token)
    {
        await using(var identity=Command(transaction)){identity.CommandText="SELECT DATABASE()";if(Convert.ToString(await identity.ExecuteScalarAsync(token),CultureInfo.InvariantCulture)!=session.Options.ExpectedDatabase)throw new InvalidOperationException("维护连接与已配置目标库不一致。");}
        foreach(var group in plan.PhysicalReferences.GroupBy(item=>item.Table).OrderBy(group=>group.Key,StringComparer.Ordinal))
        {
            await using(var pin=Command(transaction)){pin.CommandText="SELECT "+string.Join(",",group.Select(item=>Q(item.Column.Name)))+" FROM "+Q(group.Key)+" LIMIT 0";await using var reader=await pin.ExecuteReaderAsync(token);}
            await using(var engine=Command(transaction)){engine.CommandText="SELECT ENGINE FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table AND table_type='BASE TABLE'";Add(engine,"table",group.Key);if(!string.Equals(Convert.ToString(await engine.ExecuteScalarAsync(token),CultureInfo.InvariantCulture),"InnoDB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("回填依赖必须为事务实体表。");}
            var actual=new Dictionary<string,(string Type,bool Nullable,bool Primary,bool Generated)>(StringComparer.Ordinal);
            await using(var schema=Command(transaction)){schema.CommandText="SELECT column_name,column_type,is_nullable,column_key,extra FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table";Add(schema,"table",group.Key);await using var reader=await schema.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))actual.Add(reader.GetString(0),(reader.GetString(1).ToLowerInvariant(),reader.GetString(2)=="YES",reader.GetString(3)=="PRI",reader.GetString(4).Contains("GENERATED",StringComparison.OrdinalIgnoreCase)));}
            foreach(var item in group)if(!actual.TryGetValue(item.Column.Name,out var column)||column.Type!=item.Column.StoreType||column.Nullable!=item.Column.Nullable||column.Primary!=item.Column.PrimaryKey||column.Generated)throw new InvalidOperationException("回填物理列与已发布模型不一致，请先审查结构差异。");
            if(actual.Values.Count(column=>column.Primary)!=1||!actual.TryGetValue("id",out var id)||!id.Primary)throw new InvalidOperationException("回填依赖不是单列id主键。");
        }
    }
    /// <summary>来源按真实项目、活动记录和主键分批取写锁，不先读旧RR快照。</summary>
    internal async Task<List<Dictionary<string,object?>>> Sources(MaintenanceTable table,string project,long after,DbTransaction transaction,CancellationToken token,IReadOnlyList<long>? ids=null)
    {
        var fields=new[]{"id"}.Concat(table.Fields.Select(field=>field.Name)).Concat(table.Multiples.Select(multiple=>multiple.Foreign)).Distinct(StringComparer.Ordinal).ToArray();await using var command=Command(transaction);command.CommandText="SELECT "+string.Join(",",fields.Select(Q))+" FROM "+Q(table.Option.TableName)+" WHERE "+Scope(table)+" AND `id`>@after"+(ids is null?"":" AND `id` IN ("+string.Join(",",Enumerable.Range(0,ids.Count).Select(index=>"@owned"+index))+")")+" ORDER BY `id` LIMIT 128 FOR UPDATE";Add(command,"project",Project(table,project));Add(command,"after",after);if(ids is not null)for(var index=0;index<ids.Count;index++)Add(command,"owned"+index,ids[index]);var rows=new List<Dictionary<string,object?>>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<fields.Length;index++)row.Add(fields[index],await reader.IsDBNullAsync(index,token)?null:reader.GetValue(index));rows.Add(row);}return rows;
    }
    /// <summary>精确写回已锁来源的派生列，主键、业务字段和审计ID／时间不进入SET。</summary>
    private async Task Update(MaintenanceTable table,long id,string project,IReadOnlyDictionary<string,object?> patch,DbTransaction transaction,CancellationToken token)
    {
        await using var command=Command(transaction);command.CommandText="UPDATE "+Q(table.Option.TableName)+" SET "+string.Join(",",patch.Keys.Select((field,index)=>Q(field)+"=@value"+index))+" WHERE `id`=@id AND "+Scope(table);Add(command,"id",id);Add(command,"project",Project(table,project));var ordinal=0;foreach(var value in patch.Values)Add(command,"value"+ordinal++,value);if(await command.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("回填来源在同事务不再匹配，整批拒绝提交。");
    }
    /// <summary>活动范围只来自编译后的真实物理项目列和软删列。</summary>
    private static string Scope(MaintenanceTable table)=>"`yz_project_id`=@project"+(table.Deleted?" AND `delete_no` IS NULL":"");
    /// <summary>项目ID按发布类型绑定，数值不经浮点或隐式转换。</summary>
    private static object Project(MaintenanceTable table,string project)=>table.ProjectType=="string"?project:long.Parse(project,CultureInfo.InvariantCulture);
    /// <summary>标识符只来自编译计划，由适配器引用。</summary>
    private string Q(string name)=>session.Provider.QuoteIdentifier(name);
    /// <summary>所有命令加入同一个已建立事务。</summary>
    private static DbCommand Command(DbTransaction transaction){var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;return command;}
    /// <summary>业务值仅通过参数传递。</summary>
    private static void Add(DbCommand command,string name,object? value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}
}
