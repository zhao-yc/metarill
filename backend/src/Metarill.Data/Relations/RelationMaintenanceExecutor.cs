using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;
internal sealed record RelationRowPatch(long Id,IReadOnlyDictionary<string,object?> Values);

/// <summary>维护只执行已发布的整表计划，不接收字段、SQL、连接或项目覆盖。</summary>
public sealed partial class RelationMaintenanceExecutor(DatabaseSession session)
{
    private const int WriteBatchSize=128;
    private const int CleanReadBatchSize=512;
    private const int LeafCleanReadBatchSize=1024;
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
        var transaction=session.CurrentTransaction??throw new InvalidOperationException("回填缺少完整业务事务。");
        var plan=table.Read!;
        // 合并结构核验命令，仍在当前事务先锁表结构，再核对引擎和物理列。
        var packetLimit=await ValidatePhysical(plan,transaction,token,reusePinnedStructure:true);
        var counts=table.Fields.ToDictionary(field=>field.Name,_=>0L,StringComparer.Ordinal);long after=0;
        var scalar=table.Multiples.Count==0&&plan.Crosses.Count==0&&plan.MultiProjections.Count==0
            &&plan.Expression("id")=="b.`id`"&&plan.ImportMatchLock is not null;
        var allowLookahead=scalar&&CanLookAhead(table,plan);
        // 叶子修复不会传播到后续来源，遇到干净批后可以恢复宽窗口。
        var resumeCleanLookahead=allowLookahead&&!catalog.HasDownstreamCaches(table.Option.TableName);
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
                    allowLookahead=resumeCleanLookahead;
                }
                if(batch.LastId==after)break;
                after=batch.LastId;
                patches=batch.Patches;
                // 叶子连续读满干净窗口才再扩大。宽读发现变化会先回到 128 行重读，不能据此晋级。
                var nextCleanLimit=resumeCleanLookahead&&readLimit>=CleanReadBatchSize&&batch.RowCount==readLimit
                    ?LeafCleanReadBatchSize:CleanReadBatchSize;
                readLimit=allowLookahead&&patches.Count==0?nextCleanLimit:WriteBatchSize;
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
    private async Task<(long LastId,IReadOnlyList<RelationRowPatch> Patches,long PacketLimit,int RowCount)> LockScalarBatchAsync(
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
        var rowCount=0;
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
            rowCount++;
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
        return (lastId,patches.AsReadOnly(),packetLimit,rowCount);
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
        var pinned=session.PinnedRelationLayouts(transaction);
        var pinnedPacketLimit=pinned?.GetValueOrDefault(plan)??0;
        // 首次仍单独核库名并取得结构锁，只有同事务的已证明精确读取可以合并。
        var verifiedFrom=validate&&exact&&plan.Constants.Count==0&&transaction.Connection is MySqlConnector.MySqlConnection
            &&pinnedPacketLimit>0
            ?plan.QualifiedMaintenanceFrom(Q(session.Options.ExpectedDatabase)):null;
        var packetLimit=validate&&verifiedFrom is null
            ?await ValidatePhysical(plan,transaction,token,reusePinnedStructure:true):0;
        if(!exact)
        {
            var rows=await Sources(table,project,0,transaction,token,[id]);
            return await RefreshLockedAsync(table,rows,project,null,token);
        }
        if(validate&&session.HasScalarCacheWriteProof(transaction)&&plan.ScalarCacheTarget is {} target&&plan.Constants.Count==0&&CanLookAhead(table,plan))
        {
            if(project!=plan.ProjectId)throw new UnauthorizedAccessException("当前身份不属于已发布全局来源目录。");
            var batch=await TryWriteExactScalarAsync(table,target,id,Math.Max(packetLimit,pinnedPacketLimit),transaction,token);
            if(batch is not null)return batch;
        }
        // 留出第二条的检测槽。若结构不再满足唯一来源，仍按原行为拒绝整笔写入。
        var (_,patches,readPacketLimit,_)=await LockScalarBatchAsync(table,project,0,2,transaction,token,exactId:id,verifiedFrom:verifiedFrom);
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
    /// <summary>首行仍用原插入及结构预检，后续行只使用同一事务已固定的结构和报文预算。</summary>
    internal async Task<IReadOnlyList<RelationRowPatch>?> TryInsertExactScalarAsync<T>(
        MaintenanceTable table,long id,string project,T entity,DbTransaction transaction,CancellationToken token,
        MySqlExactParentCandidate.BatchRecheck? parent) where T:class
    {
        var plan=table.Read!;
        var packet=session.PinnedRelationLayouts(transaction)?.GetValueOrDefault(plan)??0;
        if(packet<=0||project!=plan.ProjectId||parent is not null&&!ReferenceEquals(parent.Transaction,transaction)||
            !CanLockExactScalarSource(table)||plan.Constants.Count!=0||
            plan.ScalarCacheTarget is not {} target||!CanLookAhead(table,plan))
            return null;
        var insert=MySqlInsertCacheBatch.Prepare(session,table.Option.TableName,id,entity);
        return insert is null?null:await TryWriteExactScalarAsync(table,target,id,packet,transaction,token,insert,parent);
    }

    /// <summary>重复读中保留原来源排他锁和目标共享锁，先取预期值，写后仍由驱动核对物理存储。</summary>
    private async Task<IReadOnlyList<RelationRowPatch>?> TryWriteExactScalarAsync(
        MaintenanceTable table,MySqlScalarCacheTarget target,long id,long packetLimit,DbTransaction transaction,CancellationToken token,MySqlInsertCacheBatch? insert=null,
        MySqlExactParentCandidate.BatchRecheck? parent=null)
    {
        var plan=table.Read!;
        var field=table.Fields[0].Name;
        var output=plan.OutputExpression(field);
        if(!plan.Origins.TryGetValue(field,out var origin)||origin.Table!=target.Table||output!=target.Alias+"."+Q(origin.Column))return null;
        var database=Q(session.Options.ExpectedDatabase);
        var from=plan.QualifiedMaintenanceFrom(database);
        if(from is null)return null;
        var source=database+"."+Q(table.Option.TableName);
        var cached="b."+Q(field);
        var parentGate=parent is null?"":" AND "+MySqlExactParentCandidate.BatchRecheck.Accepted;
        var exact="b.`id`=CASE WHEN "+MySqlExactParentCandidate.BatchRecheck.DatabaseGate+parentGate+" THEN @exact_id END";
        var where=" WHERE "+Scope(table,"b.")+" AND "+exact;
        var value="(SELECT "+output+" FROM "+database+"."+Q(target.Table)+" "+target.Alias+" WHERE "+target.Predicate+" FOR SHARE OF "+target.Alias+")";
        await using var command=Command(transaction);
        // 统一文本编码再做二进制比较，保留 CLR 字符串对尾空格、大小写和 NULL 的区别。
        var guardVariable=MySqlExactParentCandidate.BatchRecheck.GuardVariable;
        var parentRead=parent is null?"":"; SELECT "+parent.Expression+" INTO "+guardVariable+"; SELECT "+guardVariable;
        command.CommandText=ReadConnectionIdentity+parentRead+(insert is null?"; ":"; "+insert.Sql+parentGate+"; SELECT ROW_COUNT(); ")+"SELECT b.`id`,"+cached+","+output+from+where+
            " ORDER BY b.`id` LIMIT 2"+plan.ImportMatchLock+"; UPDATE "+source+" b SET "+Q(field)+"="+value+where+
            " AND NOT (CAST(CONVERT("+cached+" USING utf8mb4) AS BINARY) <=> CAST(CONVERT("+value+" USING utf8mb4) AS BINARY)); "+
            (insert is null?"":"SELECT ROW_COUNT(); ")+"SELECT b.`id`,"+
            cached+" FROM "+source+" b"+where+" FOR UPDATE OF b"+(parent is null?"":"; SET "+guardVariable+"=NULL");
        var budget=insert?.ParameterBudget??0;
        if(parent is not null)
        {
            var reserved=insert!.Parameters.Select(p=>p.ParameterName.TrimStart('@')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(reserved.Contains(guardVariable[1..]))return null;
            foreach(var parameter in parent.Parameters)
            {
                var bytes=MySqlInsertCacheBatch.ParameterBytes(parameter.Value);
                if(bytes<0||reserved.Contains(parameter.Key))return null;
                budget+=bytes;
            }
        }
        if(System.Text.Encoding.UTF8.GetByteCount(command.CommandText)+6L*System.Text.Encoding.UTF8.GetByteCount(session.Options.ExpectedDatabase)+128+budget>packetLimit)
            return null;
        Add(command,"expected_database",session.Options.ExpectedDatabase);
        Add(command,"exact_id",id);
        if(insert is not null)
            foreach(var parameter in insert.Parameters)command.Parameters.Add(parameter);
        if(parent is not null)
            foreach(var parameter in parent.Parameters)Add(command,parameter.Key,parameter.Value);
        await using var reader=await command.ExecuteReaderAsync(token);
        _=await RequireConnectionIdentityAsync(reader,token);
        var status=parent is null?2:await ReadBatchIntegerAsync(reader,"父候选重验没有返回唯一结果。",token);
        if(insert is not null)
            await RequireBatchRowCountAsync(reader,status==2?1:0,"新增失败。",token);
        if(!await reader.NextResultAsync(token))throw new InvalidOperationException("维护连接没有返回已核验的锁读结果。");
        var exists=await reader.ReadAsync(token);
        if(status!=2&&exists)throw new InvalidOperationException("未通过父候选仍读取了来源，当前事务未提交。");
        string? before=null,expected=null;
        if(exists)
        {
            if(reader.GetInt64(0)!=id)throw new InvalidOperationException("回填锁读返回重复或越界的来源。");
            before=await reader.IsDBNullAsync(1,token)?null:reader.GetString(1);
            expected=await reader.IsDBNullAsync(2,token)?null:reader.GetString(2);
            if(await reader.ReadAsync(token))throw new InvalidOperationException("回填锁读返回重复或越界的来源。");
        }
        var changed=exists&&!string.Equals(before,expected,StringComparison.Ordinal);
        if(insert is not null)
            await RequireBatchRowCountAsync(reader,changed?1:0,"回填来源在同事务不再匹配，整批拒绝提交。",token);
        if(!await reader.NextResultAsync(token)||insert is null&&reader.RecordsAffected!=(changed?1:0))
            throw new InvalidOperationException("回填来源在同事务不再匹配，整批拒绝提交。");
        if(await reader.ReadAsync(token)!=exists||exists&&reader.GetInt64(0)!=id)
            throw new InvalidOperationException("关系缓存在存储后不再匹配，当前事务未提交。");
        if(exists)
        {
            var actual=await reader.IsDBNullAsync(1,token)?null:reader.GetString(1);
            if(!string.Equals(actual,expected,StringComparison.Ordinal))
                throw new InvalidOperationException("关系缓存在存储后发生变化，当前事务未提交。");
            if(await reader.ReadAsync(token))throw new InvalidOperationException("关系缓存在存储后不再匹配，当前事务未提交。");
        }
        if(parent is not null)
        {
            if(await reader.NextResultAsync(token))throw new InvalidOperationException("父候选合并写入返回了额外结果。");
            // 拒绝路径也消费完命令，验证无写入并确认会话内标记已经清空。
            MySqlExactParentCandidate.RequireCandidate(checked((int)status));
        }
        return changed?[new RelationRowPatch(id,new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(
            new Dictionary<string,object?>(StringComparer.Ordinal){{field,expected}}))]:[];
    }
    /// <summary>MySqlDataReader 的受影响行数会累计，合并插入必须分别读取服务器结果。</summary>
    private static async Task RequireBatchRowCountAsync(DbDataReader reader,int expected,string message,CancellationToken token)
    {
        if(await ReadBatchIntegerAsync(reader,message,token)!=expected)throw new InvalidOperationException(message);
    }

    /// <summary>每个标量结果只允许一行，不能把额外读取误当成后续写入结果。</summary>
    private static async Task<long> ReadBatchIntegerAsync(DbDataReader reader,string message,CancellationToken token)
    {
        if(!await reader.NextResultAsync(token)||!await reader.ReadAsync(token))throw new InvalidOperationException(message);
        var value=reader.GetInt64(0);
        if(await reader.ReadAsync(token))throw new InvalidOperationException(message);
        return value;
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
        if(pinnedLayouts?.ContainsKey(plan)==true)
        {
            pinnedLayouts[plan]=packetLimit;
            return packetLimit;
        }
        // 此处的字典普通读会建立 MySQL 可重复读快照。同父导入须保留原父候选读取在前，不能直接将结构核验移到父锁之前。
        const string engineSql="SELECT ENGINE FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@table AND table_type='BASE TABLE'";
        const string columnsSql="SELECT column_name,column_type,is_nullable,column_key,extra,generation_expression FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table";
        var groups=plan.PhysicalReferences.GroupBy(item=>item.Table).OrderBy(group=>group.Key,StringComparer.Ordinal).ToArray();
        if(pinnedLayouts is not null&&session.HasScalarCacheWriteProof(transaction)&&plan.ScalarCacheTarget is not null&&
            transaction.Connection is MySqlConnector.MySqlConnection&&groups.Length==2&&
            await ReadAppendLayoutsAsync(groups,transaction,packetLimit,token) is {} layouts)
        {
            foreach(var group in groups)
            {
                var layout=layouts[group.Key];
                RequireEngine(layout.Engine);
                RequireColumns(group,layout.Columns);
            }
            pinnedLayouts.Add(plan,packetLimit);
            return packetLimit;
        }
        foreach(var group in groups)
        {
            var names=PhysicalNames(group);
            var pinSql="SELECT "+string.Join(",",names.Select(Q))+" FROM "+Q(group.Key)+" LIMIT 0";
            var actual=new Dictionary<string,PhysicalColumn>(StringComparer.Ordinal);
            // 零行查询仍先锁表结构。引擎和列同批返回，保留未被投影引用的主键列以识别复合主键。
            var parameters=names.Select((_,index)=>"@column"+index).ToArray();
            var batchSql=pinSql+"; SELECT t.engine,c.column_name,c.column_type,c.is_nullable,c.column_key,c.extra,c.generation_expression"+
                " FROM information_schema.tables t LEFT JOIN information_schema.columns c"+
                " ON c.table_schema=t.table_schema AND c.table_name=t.table_name"+
                " AND (c.column_key='PRI' OR c.column_name IN ("+string.Join(",",parameters)+"))"+
                " WHERE t.table_schema=DATABASE() AND t.table_name=@table AND t.table_type='BASE TABLE'";
            var budget=System.Text.Encoding.UTF8.GetByteCount(batchSql)+128+
                names.Append(group.Key).Sum(name=>4L*System.Text.Encoding.UTF8.GetByteCount(name)+2);
            if(pinnedLayouts is not null&&transaction.Connection is MySqlConnector.MySqlConnection
                &&names.Length<DatabaseCommandObservation.MaximumParameters&&batchSql.Length<=DatabaseCommandObservation.MaximumSqlLength
                &&budget<=packetLimit)
            {
                await using var batch=Command(transaction);
                batch.CommandText=batchSql;
                Add(batch,"table",group.Key);
                for(var index=0;index<names.Length;index++)Add(batch,parameters[index],names[index]);
                await using var reader=await batch.ExecuteReaderAsync(token);
                if(!await reader.NextResultAsync(token)||!await reader.ReadAsync(token))
                    throw new InvalidOperationException("回填依赖必须为事务实体表。");
                RequireEngine(reader.GetValue(0));
                do
                {
                    if(!reader.IsDBNull(1))ReadPhysicalColumn(actual,reader,1);
                }while(await reader.ReadAsync(token));
            }
            else
            {
                await using(var pin=Command(transaction)){pin.CommandText=pinSql;await using var pinReader=await pin.ExecuteReaderAsync(token);}
                await using(var engine=Command(transaction)){engine.CommandText=engineSql;Add(engine,"table",group.Key);RequireEngine(await engine.ExecuteScalarAsync(token));}
                await using var schema=Command(transaction);
                schema.CommandText=columnsSql;
                Add(schema,"table",group.Key);
                await using var reader=await schema.ExecuteReaderAsync(token);
                while(await reader.ReadAsync(token))ReadPhysicalColumn(actual,reader,0);
            }
            RequireColumns(group,actual);
        }
        if(pinnedLayouts is not null)pinnedLayouts.Add(plan,packetLimit);
        return packetLimit;
    }

    private sealed record PhysicalColumn(string Type,bool Nullable,bool Primary,bool Generated);
    private sealed record PhysicalLayout(object Engine,Dictionary<string,PhysicalColumn> Columns);

    /// <summary>追加事务已进入原父候选快照。按原顺序锁定两张表，再一次读取当前库的依赖列与主键。</summary>
    private async Task<Dictionary<string,PhysicalLayout>?> ReadAppendLayoutsAsync(
        IGrouping<string,MySqlPhysicalReference>[] groups,DbTransaction transaction,long packetLimit,CancellationToken token)
    {
        await using var command=Command(transaction);
        var pins=new List<string>(groups.Length);
        var tables=new List<string>(groups.Length);
        var scopes=new List<string>(groups.Length);
        long budget=128;
        for(var index=0;index<groups.Length;index++)
        {
            var group=groups[index];
            var names=PhysicalNames(group);
            var table="append_table"+index;
            var columns=names.Select((_,column)=>"append_column"+index+"_"+column).ToArray();
            pins.Add("SELECT "+string.Join(",",names.Select(Q))+" FROM "+Q(group.Key)+" LIMIT 0");
            tables.Add("@"+table);
            scopes.Add("(BINARY t.table_name=BINARY @"+table+" AND c.column_name IN ("+string.Join(",",columns.Select(column=>"@"+column))+"))");
            Add(command,table,group.Key);
            budget+=MySqlInsertCacheBatch.ParameterBytes(group.Key)*2;
            for(var column=0;column<columns.Length;column++)
            {
                Add(command,columns[column],names[column]);
                budget+=MySqlInsertCacheBatch.ParameterBytes(names[column]);
            }
        }
        command.CommandText="/* metarill:append-layout */ "+string.Join("; ",pins)+
            "; SELECT t.table_name,t.engine,c.column_name,c.column_type,c.is_nullable,c.column_key,c.extra,c.generation_expression"+
            " FROM information_schema.tables t LEFT JOIN information_schema.columns c"+
            " ON c.table_schema=t.table_schema AND c.table_name=t.table_name"+
            " AND (c.column_key='PRI' OR "+string.Join(" OR ",scopes)+")"+
            " WHERE t.table_schema=DATABASE() AND t.table_name IN ("+string.Join(",",tables)+") AND t.table_type='BASE TABLE'";
        if(command.Parameters.Count>DatabaseCommandObservation.MaximumParameters||
            command.CommandText.Length>DatabaseCommandObservation.MaximumSqlLength||
            System.Text.Encoding.UTF8.GetByteCount(command.CommandText)+budget>Math.Min(packetLimit,256*1024))
            return null;

        var known=groups.Select(group=>group.Key).ToHashSet(StringComparer.Ordinal);
        var result=new Dictionary<string,PhysicalLayout>(StringComparer.Ordinal);
        try
        {
            await using var reader=await command.ExecuteReaderAsync(token);
            for(var index=0;index<pins.Count;index++)
                if(!await reader.NextResultAsync(token))return null;
            while(await reader.ReadAsync(token))
            {
                var table=reader.GetString(0);
                if(!known.Contains(table))return null;
                if(!result.TryGetValue(table,out var layout))
                    result.Add(table,layout=new(reader.GetValue(1),new(StringComparer.Ordinal)));
                if(!reader.IsDBNull(2))ReadPhysicalColumn(layout.Columns,reader,2);
            }
        }
        catch(MySqlConnector.MySqlException error)when(error.Number is 1054 or 1146)
        {
            // 仅重试读失败。按原逐表顺序确定首个缺失表或列，未缓存不完整的结构。
            return null;
        }
        return result.Count==groups.Length?result:null;
    }

    private static string[] PhysicalNames(IGrouping<string,MySqlPhysicalReference> group)=>
        group.Select(item=>item.Column.Name).Distinct(StringComparer.Ordinal).ToArray();

    private static void RequireColumns(IGrouping<string,MySqlPhysicalReference> group,Dictionary<string,PhysicalColumn> actual)
    {
        foreach(var item in group)
            if(!actual.TryGetValue(item.Column.Name,out var column)||column.Type!=item.Column.StoreType||column.Nullable!=item.Column.Nullable||column.Primary!=item.Column.PrimaryKey||column.Generated)
                throw new InvalidOperationException($"回填物理列 {item.Table}.{item.Column.Name} 与已发布模型不一致（预期 {item.Column.StoreType}／可空={item.Column.Nullable}／主键={item.Column.PrimaryKey}，实际 {column?.Type}／可空={column?.Nullable??false}／主键={column?.Primary??false}／生成列={column?.Generated??false}），请先审查结构差异。");
        if(actual.Values.Count(column=>column.Primary)!=1||!actual.TryGetValue("id",out var id)||!id.Primary)
            throw new InvalidOperationException("回填依赖不是单列id主键。");
    }

    private static void ReadPhysicalColumn(Dictionary<string,PhysicalColumn> columns,DbDataReader reader,int offset)
    {
        // TEXT常量默认值的DEFAULT_GENERATED不是计算列；真正生成表达式及未知生成标记仍拒绝。
        var extra=reader.GetString(offset+4);
        var generated=!reader.IsDBNull(offset+5)&&!string.IsNullOrWhiteSpace(reader.GetString(offset+5))
            ||extra.Contains("GENERATED",StringComparison.OrdinalIgnoreCase)&&!extra.Equals("DEFAULT_GENERATED",StringComparison.OrdinalIgnoreCase);
        columns.Add(reader.GetString(offset),new(reader.GetString(offset+1).ToLowerInvariant(),reader.GetString(offset+2)=="YES",reader.GetString(offset+3)=="PRI",generated));
    }

    private static void RequireEngine(object? value)
    {
        if(!string.Equals(Convert.ToString(value,CultureInfo.InvariantCulture),"InnoDB",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("回填依赖必须为事务实体表。");
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
