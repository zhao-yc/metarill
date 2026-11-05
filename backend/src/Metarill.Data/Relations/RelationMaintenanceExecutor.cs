using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;
internal sealed record RelationRowPatch(long Id,IReadOnlyDictionary<string,object?> Values);

/// <summary>维护只执行已发布的整表计划，不接收字段、SQL、连接或项目覆盖。</summary>
public sealed class RelationMaintenanceExecutor(DatabaseSession session)
{
    private const int WriteBatchSize=128;
    private const int CleanReadBatchSize=512;
    private const string ReadConnectionIdentity="SELECT DATABASE(),@@SESSION.max_allowed_packet";
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
        var transaction=session.CurrentTransaction??throw new InvalidOperationException("回填缺少完整业务事务。");var plan=table.Read!;var packetLimit=await ValidatePhysical(plan,transaction,token);var counts=table.Fields.ToDictionary(field=>field.Name,_=>0L,StringComparer.Ordinal);long after=0;
        var scalar=table.Multiples.Count==0&&plan.Crosses.Count==0&&plan.MultiProjections.Count==0
            &&plan.Expression("id")=="b.`id`"&&plan.ImportMatchLock is not null;
        var allowLookahead=scalar&&CanLookAhead(table,plan);
        var readLimit=WriteBatchSize;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            IReadOnlyList<RelationRowPatch> patches;
            if(scalar)
            {
                var batch=await LockScalarBatchAsync(table,catalog.ProjectId,after,readLimit,transaction,token);
                if(batch.Patches.Count>0)
                {
                    // 宽窗口只确认没有变化。发现旧值后按原边界重读，避免使用传播前的后续行投影。
                    if(readLimit!=WriteBatchSize)
                        batch=await LockScalarBatchAsync(table,catalog.ProjectId,after,WriteBatchSize,transaction,token);
                    allowLookahead=false;
                }
                if(batch.LastId==after)break;
                after=batch.LastId;
                patches=batch.Patches;
                readLimit=allowLookahead?CleanReadBatchSize:WriteBatchSize;
                await WriteScalarBatchAsync(table,plan,catalog.ProjectId,patches,packetLimit,transaction,token);
            }
            else
            {
                var rows=await Sources(table,catalog.ProjectId,after,transaction,token);
                if(rows.Count==0)break;
                after=(long)rows[^1]["id"]!;
                patches=await RefreshLockedAsync(table,rows,catalog.ProjectId,null,token);
            }
            foreach(var patch in patches)
            {
                foreach(var field in patch.Values.Keys.Select(name=>table.Fields.Single(item=>item.Name==name)).Select(field=>field.DisplayOf??field.Name).Distinct(StringComparer.Ordinal))counts[field]++;
                await new ProjectRelationMutationPropagator(session).AfterMutationAsync(catalog,table.Option.TableName,patch.Id,catalog.ProjectId,new HashSet<string>(patch.Values.Keys,StringComparer.Ordinal),false,false,token);
            }
        }
        var details=table.Fields.Where(field=>field.DisplayOf is null).Select(field=>{var display=table.Fields.SingleOrDefault(item=>item.DisplayOf==field.Name);return new MaintenanceColumnResult(field.Name,field.Path,field.Relation,field.TargetTable,field.TargetColumn,checked((int)counts[field.Name]),display?.Name,display is null?null:display.Path.Split('.')[^1]);}).ToArray();return new(table.Option.Id,table.Option.RawName,table.Option.TableName,details.Length,checked((int)counts.Values.Sum()),Array.AsReadOnly(details));
    }
    /// <summary>仅单个有界物理文本和物理终点允许扩大无变化窗口，限制投影和锁读资源。</summary>
    private static bool CanLookAhead(MaintenanceTable table,MySqlNonRedundantReadPlan plan)
    {
        if(table.Fields.Count!=1)return false;
        var field=table.Fields[0].Name;
        if(!plan.Origins.TryGetValue(field,out var target))return false;
        return IsShortText(table.Option.TableName,field)&&IsShortText(target.Table,target.Column);

        bool IsShortText(string tableName,string columnName)
        {
            var type=plan.PhysicalReferences.FirstOrDefault(reference=>reference.Table==tableName&&reference.Column.Name==columnName)?.Column.StoreType;
            return type is not null&&type.StartsWith("varchar(",StringComparison.Ordinal)&&type.EndsWith(')')
                &&int.TryParse(type.AsSpan(8,type.Length-9),NumberStyles.None,CultureInfo.InvariantCulture,out var length)
                &&length is >0 and <=255;
        }
    }
    /// <summary>标量维护一次锁读真实来源和已发布目标，来源排他、目标共享，保留两套原范围谓词。</summary>
    private async Task<(long LastId,IReadOnlyList<RelationRowPatch> Patches,long PacketLimit)> LockScalarBatchAsync(
        MaintenanceTable table,string project,long after,int limit,DbTransaction transaction,CancellationToken token,long? exactId=null,string? verifiedFrom=null)
    {
        var plan=table.Read!;
        if(plan.RequiresPublishedProjectIdentity&&project!=plan.ProjectId)
            throw new UnauthorizedAccessException("当前身份不属于已发布全局来源目录。");
        var fields=table.Fields;
        var oldValues=fields.Select(field=>"b."+Q(field.Name));
        var newValues=fields.Select(field=>plan.OutputExpression(field.Name));
        var idExpression=plan.Expression("id");
        await using var command=Command(transaction);
        // 库名不符时，受守卫的主键为 NULL。即使服务端提前执行第二段，也只解析目标库且不能命中任何行。
        string Render(string? qualifiedFrom)
        {
            var exactKey=qualifiedFrom is null?"@exact_id":"CASE WHEN CAST(DATABASE() AS BINARY)=CAST(@expected_database AS BINARY) THEN @exact_id END";
            return (qualifiedFrom is null?"":ReadConnectionIdentity+"; ")+"SELECT "+idExpression+","+string.Join(",",oldValues.Concat(newValues))+(qualifiedFrom??plan.From)+
            " WHERE "+Scope(table,"b.")+(plan.Scope.Length==0?"":" AND ("+plan.Scope+")")+
            " AND "+idExpression+(exactId.HasValue?"="+exactKey:">@after")+" ORDER BY "+idExpression+" LIMIT "+limit.ToString(CultureInfo.InvariantCulture)+plan.ImportMatchLock;
        }
        command.CommandText=Render(verifiedFrom);
        var packetLimit=0L;
        // 尚未读到本会话上限时按最小报文预留参数及协议空间，较长 SQL 保留原两步。
        if(verifiedFrom is not null&&System.Text.Encoding.UTF8.GetByteCount(command.CommandText)
            +2*System.Text.Encoding.UTF8.GetByteCount(session.Options.ExpectedDatabase)+128>1024)
        {
            verifiedFrom=null;
            packetLimit=await ValidatePhysical(plan,transaction,token,reusePinnedStructure:true);
            command.CommandText=Render(null);
        }
        Add(command,"project",Project(table,project));
        Add(command,"project_scope_text",project);
        Add(command,"project_scope_number",long.Parse(project,CultureInfo.InvariantCulture));
        if(exactId is {} idToRefresh)Add(command,"exact_id",idToRefresh);
        else Add(command,"after",after);
        if(verifiedFrom is not null)Add(command,"expected_database",session.Options.ExpectedDatabase);
        foreach(var pair in plan.Constants)Add(command,pair.Key,pair.Value);
        var patches=new List<RelationRowPatch>();
        var lastId=after;
        await using var reader=await command.ExecuteReaderAsync(token);
        if(verifiedFrom is not null)
        {
            packetLimit=await RequireConnectionIdentityAsync(reader,token);
            if(!await reader.NextResultAsync(token))throw new InvalidOperationException("维护连接没有返回已核验的锁读结果。");
        }
        while(await reader.ReadAsync(token))
        {
            var id=reader.GetInt64(0);
            if(id<=lastId||exactId.HasValue&&id!=exactId.Value)throw new InvalidOperationException("回填锁读返回重复或越界的来源。");
            lastId=id;
            Dictionary<string,object?>? changes=null;
            for(var index=0;index<fields.Count;index++)
            {
                var oldIndex=index+1;
                var newIndex=oldIndex+fields.Count;
                var oldValue=await reader.IsDBNullAsync(oldIndex,token)?null:reader.GetValue(oldIndex);
                var newValue=await reader.IsDBNullAsync(newIndex,token)?null:reader.GetValue(newIndex);
                if(!Equals(oldValue,newValue))(changes??=new(StringComparer.Ordinal)).Add(fields[index].Name,newValue);
            }
            if(changes is not null)patches.Add(new(id,new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(changes)));
        }
        return (lastId,patches.AsReadOnly(),packetLimit);
    }
    /// <summary>仅已发布全局标量且来源与投影活动范围一致时，可在一条当前读中取旧副本和新名称。</summary>
    internal static bool CanLockExactScalarSource(MaintenanceTable table)
    {
        var plan=table.Read!;
        // 范围不同会掩盖原先“已锁来源存在，但投影不可见”的错误。联接集也须和原当前读相同。
        return plan.RequiresPublishedProjectIdentity&&table.ProjectType.Length==0
            &&plan.Scope==(table.Deleted?"b.`delete_no` IS NULL":"1=1")
            &&table.Multiples.Count==0&&plan.Crosses.Count==0&&plan.MultiProjections.Count==0
            &&plan.Expression("id")=="b.`id`"&&plan.ImportMatchLock is not null
            &&plan.FromForFields(plan.Outputs)==plan.From;
    }
    /// <summary>刷新刚保存的一条来源。保持实际值读回核验，成功后才把补丁送入传播队列。</summary>
    internal async Task<IReadOnlyList<RelationRowPatch>> RefreshSavedSourceAsync(MaintenanceTable table,long id,string project,bool validate,DbTransaction transaction,CancellationToken token)
    {
        var plan=table.Read!;
        var exact=CanLockExactScalarSource(table);
        // 首次仍单独核库名并取得结构锁，只有同事务的已证明精确读取可以合并。
        var verifiedFrom=validate&&exact&&plan.Constants.Count==0&&transaction.Connection is MySqlConnector.MySqlConnection
            &&session.PinnedRelationLayouts(transaction)?.Contains(plan)==true
            ?plan.QualifiedMaintenanceFrom(Q(session.Options.ExpectedDatabase)):null;
        var packetLimit=validate&&verifiedFrom is null
            ?await ValidatePhysical(plan,transaction,token,reusePinnedStructure:true):0;
        if(!exact)
        {
            var rows=await Sources(table,project,0,transaction,token,[id]);
            return await RefreshLockedAsync(table,rows,project,null,token);
        }
        // 留出第二条的检测槽。若结构不再满足唯一来源，仍按原行为拒绝整笔写入。
        var (_,patches,readPacketLimit)=await LockScalarBatchAsync(table,project,0,2,transaction,token,exactId:id,verifiedFrom:verifiedFrom);
        if(verifiedFrom is not null)packetLimit=readPacketLimit;
        // 仅已锁单主键、单文本副本及保守报文预算允许合并发送，复杂值仍走原独立读回。
        if(patches.Count==1&&patches[0].Values.Count==1&&transaction.Connection is MySqlConnector.MySqlConnection
            &&FitsTextBatch(patches,patches[0].Values.Keys.Single(),packetLimit))
        {
            var patch=patches[0];
            await using var command=UpdateCommand(table,id,project,patch.Values,transaction);
            await RelationCacheStorageVerifier.VerifySingleWriteAsync(command,session.Provider,table.Option.TableName,Scope(table,"c."),patch,token);
        }
        else await WriteScalarBatchAsync(table,table.Read!,project,patches,0,transaction,token);
        return patches;
    }
    /// <summary>仅单文本缓存按来源主键批量写入。所有实际值在传播前由原核验器读回，异常仍回滚整表。</summary>
    private async Task WriteScalarBatchAsync(MaintenanceTable table,MySqlNonRedundantReadPlan plan,string project,
        IReadOnlyList<RelationRowPatch> patches,long packetLimit,DbTransaction transaction,CancellationToken token)
    {
        if(patches.Count>1&&table.Fields.Count==1&&plan.Types[table.Fields[0].Name]=="string"
            &&FitsTextBatch(patches,table.Fields[0].Name,packetLimit))
        {
            var field=table.Fields[0].Name;
            await using var command=Command(transaction);
            var ids=new string[patches.Count];
            var choices=new string[patches.Count];
            Add(command,"project",Project(table,project));
            for(var index=0;index<patches.Count;index++)
            {
                ids[index]="@row"+index;
                choices[index]=" WHEN "+ids[index]+" THEN @value"+index;
                Add(command,"row"+index,patches[index].Id);
                Add(command,"value"+index,patches[index].Values[field]);
            }
            command.CommandText="UPDATE "+Q(table.Option.TableName)+" SET "+Q(field)+"=CASE `id`"+
                string.Concat(choices)+" END WHERE "+Scope(table)+" AND `id` IN ("+string.Join(",",ids)+") ORDER BY `id`";
            if(await command.ExecuteNonQueryAsync(token)!=patches.Count)
                throw new InvalidOperationException("回填来源在同事务不再匹配，整批拒绝提交。");
        }
        else
        {
            foreach(var patch in patches)await Update(table,patch.Id,project,patch.Values,transaction,token);
        }
        await RelationCacheStorageVerifier.VerifyManyAsync(session,table.Option.TableName,Scope(table,"c."),
            Project(table,project),patches,transaction,token);
    }
    /// <summary>按 UTF-8 转义最坏情况预留命令及协议字节，长文本继续使用原逐条写入。</summary>
    internal static bool FitsTextBatch(IReadOnlyList<RelationRowPatch> patches,string field,long packetLimit)
    {
        // 共享服务器即使允许大报文，也只把常见短文本合成一条有界命令。
        var budget=Math.Min(packetLimit,256*1024);
        long estimated=1024;
        foreach(var patch in patches)
        {
            var value=patch.Values[field];
            if(value is not null and not string)return false;
            estimated+=96L+2L*(value is string text?System.Text.Encoding.UTF8.GetByteCount(text):0);
            if(estimated>budget)return false;
        }
        return estimated<=budget;
    }
    /// <summary>调用方已锁来源后统一执行当前读取，只写本次事件允许的派生列，返回实际变化供同事务队列继续传播。</summary>
    internal async Task<IReadOnlyList<RelationRowPatch>> RefreshLockedAsync(MaintenanceTable table,IReadOnlyList<Dictionary<string,object?>> rows,string projectId,IReadOnlySet<string>? allowed,CancellationToken token)
    {
        if(rows.Count==0)return [];var transaction=session.CurrentTransaction??throw new InvalidOperationException("派生刷新缺少当前事务。");var plan=allowed is null?table.Read!:table.Read!.ForCacheFields(allowed);var patches=new List<RelationRowPatch>();
            foreach(var row in rows)foreach(var multiple in table.Multiples)if(row[multiple.Foreign] is{} raw)
            {
                try{using var document=JsonDocument.Parse(Convert.ToString(raw,CultureInfo.InvariantCulture)!);if(document.RootElement.ValueKind==JsonValueKind.Array&&document.RootElement.GetArrayLength()>multiple.MaximumItems)throw new InvalidOperationException("历史多值记录超过发布预算，本次回填未截断数据。");}catch(JsonException){} // 坏历史ID仅按原空集合显示，禁止修补源ID。
            }
            var ids=rows.Select(row=>(long)row["id"]!).ToArray();var projected=await new MySqlNonRedundantReader(session).ReadCurrentRowsAsync(plan,[new("id",FixedReadComparison.In,ids.Cast<object?>().ToArray())],[new("id")],128,token,projectId:projectId);
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
        // 当前批次持有来源行锁。传播前统一读回实际值，截断或改写仍使整笔事务回滚。
        await RelationCacheStorageVerifier.VerifyManyAsync(session, table.Option.TableName, Scope(table, "c."), Project(table, projectId), patches, transaction, token);
        return patches.AsReadOnly();
    }
    /// <summary>先获取事务内元数据锁，再核库名、InnoDB、完整依赖列类型和主键；没有DDL或结构自动修复。</summary>
    internal async Task<long> ValidatePhysical(MySqlNonRedundantReadPlan plan,DbTransaction transaction,CancellationToken token,
        bool reusePinnedStructure=false)
    {
        long packetLimit;
        await using(var identity=Command(transaction))
        {
            identity.CommandText=ReadConnectionIdentity;
            await using var reader=await identity.ExecuteReaderAsync(token);
            packetLimit=await RequireConnectionIdentityAsync(reader,token);
        }
        // 显式事务持有已探测表的元数据锁直至结束。库名与当前会话报文限制仍逐次读取。
        var pinnedLayouts=reusePinnedStructure?session.PinnedRelationLayouts(transaction):null;
        if(pinnedLayouts?.Contains(plan)==true)return packetLimit;
        foreach(var group in plan.PhysicalReferences.GroupBy(item=>item.Table).OrderBy(group=>group.Key,StringComparer.Ordinal))
        {
            await using(var pin=Command(transaction)){pin.CommandText="SELECT "+string.Join(",",group.Select(item=>Q(item.Column.Name)))+" FROM "+Q(group.Key)+" LIMIT 0";await using var reader=await pin.ExecuteReaderAsync(token);}
            await using(var engine=Command(transaction)){engine.CommandText="SELECT ENGINE FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table AND table_type='BASE TABLE'";Add(engine,"table",group.Key);if(!string.Equals(Convert.ToString(await engine.ExecuteScalarAsync(token),CultureInfo.InvariantCulture),"InnoDB",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("回填依赖必须为事务实体表。");}
            var actual=new Dictionary<string,(string Type,bool Nullable,bool Primary,bool Generated)>(StringComparer.Ordinal);
            // TEXT常量默认值的DEFAULT_GENERATED不是计算列；真正生成表达式及未知生成标记仍拒绝。
            await using(var schema=Command(transaction)){schema.CommandText="SELECT column_name,column_type,is_nullable,column_key,extra,generation_expression FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table";Add(schema,"table",group.Key);await using var reader=await schema.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))actual.Add(reader.GetString(0),(reader.GetString(1).ToLowerInvariant(),reader.GetString(2)=="YES",reader.GetString(3)=="PRI",!reader.IsDBNull(5)&&!string.IsNullOrWhiteSpace(reader.GetString(5))||reader.GetString(4).Contains("GENERATED",StringComparison.OrdinalIgnoreCase)&&!reader.GetString(4).Equals("DEFAULT_GENERATED",StringComparison.OrdinalIgnoreCase)));}
            foreach(var item in group)if(!actual.TryGetValue(item.Column.Name,out var column)||column.Type!=item.Column.StoreType||column.Nullable!=item.Column.Nullable||column.Primary!=item.Column.PrimaryKey||column.Generated)throw new InvalidOperationException($"回填物理列 {item.Table}.{item.Column.Name} 与已发布模型不一致（预期 {item.Column.StoreType}／可空={item.Column.Nullable}／主键={item.Column.PrimaryKey}，实际 {column.Type}／可空={column.Nullable}／主键={column.Primary}／生成列={column.Generated}），请先审查结构差异。");
            if(actual.Values.Count(column=>column.Primary)!=1||!actual.TryGetValue("id",out var id)||!id.Primary)throw new InvalidOperationException("回填依赖不是单列id主键。");
        }
        pinnedLayouts?.Add(plan);
        return packetLimit;
    }
    /// <summary>每次使用实际服务器库名和会话报文上限，结构证明不替代连接核验。</summary>
    private async Task<long> RequireConnectionIdentityAsync(DbDataReader reader,CancellationToken token)
    {
        if(!await reader.ReadAsync(token)||await reader.IsDBNullAsync(0,token)||reader.GetString(0)!=session.Options.ExpectedDatabase)
            throw new InvalidOperationException("维护连接与已配置目标库不一致。");
        return reader.GetInt64(1);
    }
    /// <summary>来源按真实项目、活动记录和主键分批取写锁；事件只返回受影响缓存，整表及来源自身刷新保持完整字段。</summary>
    internal async Task<List<Dictionary<string,object?>>> Sources(MaintenanceTable table,string project,long after,DbTransaction transaction,CancellationToken token,IReadOnlyList<long>? ids=null,IReadOnlySet<string>? allowed=null)
    {
        if(table.Read?.RequiresPublishedProjectIdentity==true&&project!=table.Read.ProjectId)throw new UnauthorizedAccessException("当前身份不属于已发布全局来源目录。");
        // 只缩窄缓存值返回列；预算所需ID始终保留，未知名称不能成为标识符。
        if(allowed is not null&&allowed.Any(name=>!table.Fields.Any(field=>field.Name==name)))throw new InvalidOperationException("来源锁读只能选择已声明缓存字段。");
        var fields=new[]{"id"}.Concat(table.Fields.Where(field=>allowed is null||allowed.Contains(field.Name)).Select(field=>field.Name)).Concat(table.Multiples.Select(multiple=>multiple.Foreign)).Distinct(StringComparer.Ordinal).ToArray();await using var command=Command(transaction);command.CommandText="SELECT "+string.Join(",",fields.Select(Q))+" FROM "+Q(table.Option.TableName)+" WHERE "+Scope(table)+" AND `id`>@after"+(ids is null?"":" AND `id` IN ("+string.Join(",",Enumerable.Range(0,ids.Count).Select(index=>"@owned"+index))+")")+" ORDER BY `id` LIMIT 128 FOR UPDATE";Add(command,"project",Project(table,project));Add(command,"after",after);if(ids is not null)for(var index=0;index<ids.Count;index++)Add(command,"owned"+index,ids[index]);var rows=new List<Dictionary<string,object?>>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<fields.Length;index++)row.Add(fields[index],await reader.IsDBNullAsync(index,token)?null:reader.GetValue(index));rows.Add(row);}return rows;
    }
    /// <summary>精确写回已锁来源的派生列，主键、业务字段和审计ID／时间不进入SET。</summary>
    private async Task Update(MaintenanceTable table,long id,string project,IReadOnlyDictionary<string,object?> patch,DbTransaction transaction,CancellationToken token)
    {
        await using var command=UpdateCommand(table,id,project,patch,transaction);
        if(await command.ExecuteNonQueryAsync(token)!=1)throw new InvalidOperationException("回填来源在同事务不再匹配，整批拒绝提交。");
    }
    /// <summary>构造与原独立写入相同的已编译 UPDATE，所有值仍由驱动绑定。</summary>
    private DbCommand UpdateCommand(MaintenanceTable table,long id,string project,IReadOnlyDictionary<string,object?> patch,DbTransaction transaction)
    {
        var command=Command(transaction);
        command.CommandText="UPDATE "+Q(table.Option.TableName)+" SET "+string.Join(",",patch.Keys.Select((field,index)=>Q(field)+"=@value"+index))+" WHERE `id`=@id AND "+Scope(table);
        Add(command,"id",id);
        Add(command,"project",Project(table,project));
        var ordinal=0;
        foreach(var value in patch.Values)Add(command,"value"+ordinal++,value);
        return command;
    }
    /// <summary>活动范围只来自编译后的真实物理项目列和软删列。</summary>
    private static string Scope(MaintenanceTable table,string prefix="") => (table.ProjectType.Length==0 ? "1=1" : prefix+"`yz_project_id`=@project") + (table.Deleted ? " AND "+prefix+"`delete_no` IS NULL" : "");
    /// <summary>项目ID按发布类型绑定，数值不经浮点或隐式转换。</summary>
    private static object Project(MaintenanceTable table,string project)=>table.ProjectType=="string"?project:long.Parse(project,CultureInfo.InvariantCulture);
    /// <summary>标识符只来自编译计划，由适配器引用。</summary>
    private string Q(string name)=>session.Provider.QuoteIdentifier(name);
    /// <summary>所有命令加入同一个已建立事务。</summary>
    private static DbCommand Command(DbTransaction transaction){var command=MySqlFloatingCommand.Wrap(transaction.Connection!.CreateCommand());command.Transaction=transaction;return command;}
    /// <summary>业务值仅通过参数传递。</summary>
    private static void Add(DbCommand command,string name,object? value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value??DBNull.Value;command.Parameters.Add(parameter);}
}
