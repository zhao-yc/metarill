using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Runtime.Persistence;
namespace Metarill.Runtime.Files;

/// <summary>文件父范围只沿已锁来源的真实稀疏选择解析，不接收请求父值或伪造祖先列。</summary>
internal sealed class FileCrossParentScope
{
    private sealed record Target(string Table,string ProjectType,bool SoftDelete,string? Parent,FixedConditionSqlPlan? Condition);
    private readonly CrossTableSparseSelection? selection;
    private readonly FileSharedCrossParentScope? shared;
    private readonly Target[] targets;
    internal string SourceId{get;}
    internal int Level{get;}
    internal IReadOnlyList<string> StorageNames=>shared?.StorageNames??selection!.Slots.Select(slot=>slot.ColumnName).ToArray();
    /// <summary>路径字段、目标与常量在发布时冻结；构造器不暴露任意SQL或在线替换入口。</summary>
    private FileCrossParentScope(string sourceId,int level,CrossTableSparseSelection selection,Target[] targets){SourceId=sourceId;Level=level;this.selection=selection;this.targets=targets;}
    /// <summary>共享包装器使用同一组身份与全局槽位，不能冒充某一条分支。</summary>
    internal FileCrossParentScope(string sourceId,int level,FileSharedCrossParentScope shared){SourceId=sourceId;Level=level;this.shared=shared;targets=[];}
    /// <summary>只有普通R0固定跨表的ID位可取得逻辑父证明，selfTree及普通外键继续原边界。</summary>
    internal static FileCrossParentScope? Compile(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata column)
    {
        if(table.IsRongyu!=0)return null;var groups=SharedCrossTableSparseSelection.CompileAll(table,catalog.ConfigColumns.Where(c=>c.ConfigTableId==table.Id));var group=groups.SingleOrDefault(g=>g.Slots.Any(slot=>slot.ColumnName==column.ColumnName));if(group is not null)return FileSharedCrossParentScope.Compile(catalog,table,page,column,group);var members=groups.SelectMany(g=>g.Branches).Select(b=>b.SourceColumnId).ToHashSet(StringComparer.Ordinal);var matches=new List<(ColumnMetadata Source,CrossTableSparseSelection Selection,int Level)>();
        foreach(var source in catalog.ConfigColumns.Where(field=>field.ConfigTableId==table.Id&&field.ControlKind=="CascadeTree"))
        {
            if(members.Contains(source.Id))continue;using var json=JsonDocument.Parse(source.ControlDetail);if(json.RootElement.TryGetProperty("sourceMode",out var mode)&&mode.GetString()=="selfTree")continue;
            var codec=CrossTableSparseSelection.Compile(table,source.Id,catalog.ConfigColumns.Where(field=>field.ConfigTableId==table.Id));var index=codec.Slots.ToList().FindIndex(slot=>slot.ColumnName==column.ColumnName);if(index>=0)matches.Add((source,codec,index));
        }
        if(matches.Count==0)return null;if(matches.Count!=1)throw new NotSupportedException("文件逻辑父范围不能共享稀疏层位。");var match=matches[0];
        var branch=CompileBranch(catalog,table,page,match.Source,match.Selection,match.Level);
        // 单控件继续完整数据层证明；共享组在收齐所有同伴范围后一次证明。
        _=NonRedundantReads.CompileWithCrossTables(catalog,table.Id,["id"],[branch.Binding],[column.ColumnName]);return branch.Scope;
    }
    /// <summary>固定分支的页覆盖、目标页／常量与物理身份共用同一编译器，构造器不提供在线替换入口。</summary>
    internal static (FileCrossParentScope Scope,NonRedundantCrossTableScope Binding) CompileBranch(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata source,CrossTableSparseSelection selection,int levelIndex)
    {
        var overlay=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(source.ControlDetail)))throw new NotSupportedException("文件跨表页覆盖不能改变固定存储链。");
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&exact.ValueKind!=JsonValueKind.Null&&!string.IsNullOrWhiteSpace(exact.ToString()))throw new NotSupportedException("文件跨表精确权限尚需独立证明。");
        using var definition=JsonDocument.Parse(source.ControlDetail);var bindings=new List<NonRedundantCrossTableTargetScope>();var targets=new List<Target>();
        foreach(var level in definition.RootElement.GetProperty("levels").EnumerateArray())
        {
            var target=catalog.ConfigTables.Single(item=>item.TableName==level.GetProperty("table").GetString());var columns=MetadataStoragePolicy.ForTable(catalog,target).PhysicalColumns;
            var name=level.TryGetProperty("nameField",out var label)&&!string.IsNullOrWhiteSpace(label.GetString())?label.GetString()!:"name";var parent=targets.Count==0?null:level.GetProperty("parentField").GetString();var display=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName==name);
            // 名称可来自已证明的单值联接，父键仍必须物理；下方完整跨表编译继续证明类型、终点与范围。
            var required=new[]{display.Id}.Concat(parent is null?Array.Empty<string>():new[]{columns.Single(column=>column.ColumnName==parent).Id}).ToArray();
            var pages=catalog.ConfigPages.Where(candidate=>candidate.ConfigTableId==target.Id&&required.All(id=>catalog.ConfigPageColumns.Any(field=>field.ConfigPageId==candidate.Id&&field.ConfigColumnId==id&&Readable(field)))).Select(candidate=>(Page:candidate,Condition:Condition(candidate,columns))).Where(item=>item.Condition.Allowed).ToArray();if(pages.Length!=1)throw new NotSupportedException("文件跨表每层需要唯一固定可读目标页。");
            var chosen=pages[0];bindings.Add(new(chosen.Page.Id,chosen.Condition.Plan));var project=columns.Single(field=>field.ColumnName=="yz_project_id");targets.Add(new(target.TableName,project.LogicalType,columns.Any(field=>field.ColumnName=="delete_no"),parent,chosen.Condition.Plan));
        }
        return(new FileCrossParentScope(source.Id,levelIndex,selection,targets.ToArray()),new NonRedundantCrossTableScope(source.Id,bindings));
    }
    /// <summary>原页面显式显示／编辑标志优先，与文件可读列及生成目标页规则保持同一语义。</summary>
    private static bool Readable(PageColumnMetadata field)=>Flag(field,"isInGrid",field.IsShow==1)||Flag(field,"isInEdit",field.IsEdit==1);
    /// <summary>无请求上下文的目标页只允许项目等值及真实物理常量；父页和精确页不能作为当前目标。</summary>
    private static (bool Allowed,FixedConditionSqlPlan? Plan) Condition(PageMetadata page,IReadOnlyList<ColumnMetadata> columns)
    {
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&exact.ValueKind!=JsonValueKind.Null&&!string.IsNullOrWhiteSpace(exact.ToString()))return(false,null);
        var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(field=>field.ColumnName));var constants=new List<FixedConditionTerm>();
        foreach(var term in syntax.Terms)
        {
            var variables=term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable).ToArray();if(variables.Length==0){constants.Add(term);continue;}
            if(variables.Any(value=>value.Text is "parentId" or "parent_id"))return(false,null);
            if(term.Field!="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1||!variables[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("文件跨表目标包含未证明的上下文范围。");
        }
        var fields=constants.Select(term=>term.Field).Distinct(StringComparer.Ordinal).ToArray();return(true,constants.Count==0?null:FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(string.Join(" AND ",constants.Select(term=>term.ToDefinition())),fields),fields.ToDictionary(field=>field,field=>columns.Single(column=>column.ColumnName==field).LogicalType,StringComparer.Ordinal)));
    }
    /// <summary>显示标志沿true／1判断，不把字符串true当作许可。</summary>
    private static bool Flag(PageColumnMetadata field,string key,bool fallback)=>field.Extra.TryGetValue(key,out var value)?value.ValueKind==JsonValueKind.True||value.ToString()=="1":fallback;
    /// <summary>同一控件只解析一次完整父链，多条父等值条件共同消费同一组实际节点。</summary>
    internal static async Task<long[]> ValuesAsync(IReadOnlyList<FileCrossParentScope> scopes,SqlRowStore store,IReadOnlyDictionary<string,object?> record,long project,bool locking,CancellationToken token)
    {
        var values=new Dictionary<string,long[]>(StringComparer.Ordinal);
        foreach(var group in scopes.GroupBy(scope=>scope.SourceId,StringComparer.Ordinal))values.Add(group.Key,await group.First().PathAsync(store,record,project,locking,token));
        return scopes.Select(scope=>scope.Level<values[scope.SourceId].Length&&values[scope.SourceId][scope.Level]>0?values[scope.SourceId][scope.Level]:throw new UnauthorizedAccessException("所选层位未提供当前文件父范围。")).ToArray();
    }
    /// <summary>只读使用同一快照，写入及删除复用原事务FOR UPDATE，值与元数据标识符分别处理。</summary>
    internal async Task<long[]> PathAsync(SqlRowStore store,IReadOnlyDictionary<string,object?> record,long project,bool locking,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(shared is not null)return await shared.PathAsync(store,record,project,locking,token);var selected=ReadSelection(record);if(selected is null)throw new UnauthorizedAccessException("文件来源没有真实跨表父选择。");
        var session=store.Session;var transaction=session.CurrentTransaction;if(locking&&transaction is null)throw new InvalidOperationException("文件逻辑父锁必须加入原事务。");
        await using var owned=transaction is null?session.Provider.CreateConnection(session.Options):null;var connection=transaction?.Connection??owned??throw new InvalidOperationException("文件父路径连接不可用。");if(owned is not null)await owned.OpenAsync(token);
        var result=new long[selected.Level+1];var current=selected.Id;
        for(var index=selected.Level;index>=0;index--)
        {
            var target=targets[index];var provider=session.Provider;await using var command=connection.CreateCommand();command.Transaction=transaction;
            string Parameter(string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);return provider.ParameterPlaceholder(name);}
            var sql="SELECT "+provider.QuoteIdentifier("id")+(target.Parent is null?"":","+provider.QuoteIdentifier(target.Parent))+" FROM "+provider.QuoteIdentifier(target.Table)+" WHERE "+provider.QuoteIdentifier("id")+"="+Parameter("node",current)+" AND "+provider.QuoteIdentifier("yz_project_id")+"="+Parameter("project",target.ProjectType=="string"?project.ToString(CultureInfo.InvariantCulture):(object)project);
            if(target.SoftDelete)sql+=" AND "+provider.QuoteIdentifier("delete_no")+" IS NULL";if(target.Condition is{} condition)sql+=" AND "+condition.BindConstants(command,session,"file_cross");command.CommandText=locking?provider.BuildSelectForUpdate(sql):sql;
            await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token)||Convert.ToInt64(reader.GetValue(0),CultureInfo.InvariantCulture)!=current)throw new UnauthorizedAccessException("文件跨表目标缺失、已删除或不在固定范围。");result[index]=current;
            if(index>0){var value=await reader.IsDBNullAsync(1,token)?null:Convert.ToString(reader.GetValue(1),CultureInfo.InvariantCulture);if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out current)||current<=0||value!=current.ToString(CultureInfo.InvariantCulture))throw new UnauthorizedAccessException("文件跨表实际父键无效。");}
        }
        return result;
    }
    /// <summary>仅转换编码器的纯存储异常，数据库与连接异常必须向上传播。</summary>
    private CrossTableSelectedNode? ReadSelection(IReadOnlyDictionary<string,object?> record)
    {
        try{return selection!.ReadStored(selection.Slots.ToDictionary(slot=>slot.ColumnName,slot=>record.TryGetValue(slot.ColumnName,out var value)?value:throw new InvalidOperationException("缺少实际稀疏位。")));}
        catch(InvalidOperationException invalid){throw new UnauthorizedAccessException("文件来源稀疏选择无效。",invalid);}
    }
}
