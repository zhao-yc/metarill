using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Cascades;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Relations;
namespace Metarill.Data.Queries;

/// <summary>发布时冻结的R1父路径，只保存标量及固定条件，不保留外部可变元数据。</summary>
public sealed record RedundantSharedParentLevel(string Key,string Prefix,string TableId,string TableName,string PageId,string NameId,string? Parent,string? ProjectType,bool SoftDelete,string ConditionDefinition,FixedConditionSqlPlan? Condition);
/// <summary>来源实际物理父键及其唯一已证明目标角色。</summary>
public sealed record RedundantSharedParentPlan(string ColumnId,string ColumnName,string LogicalType,string ControlId,IReadOnlyList<RedundantSharedParentLevel> Levels);
/// <summary>生成与文件运行时共同消费R1共享父定义，不能以R0稀疏编码推导实际父值。</summary>
public static class RedundantSharedParents
{
    private sealed record Branch(ColumnMetadata Control,IReadOnlyList<RedundantSharedParentLevel> Levels,IReadOnlyList<ColumnMetadata> Ids,IReadOnlyList<ColumnMetadata> Names);
    /// <summary>仅识别真正共享的R1父页；普通物理外键、单控件和R0继续原入口。</summary>
    public static IReadOnlyDictionary<string,RedundantSharedParentPlan> Compile(MetadataCatalog catalog,string tableId,string pageId)
    {
        var table=catalog.ConfigTables.Single(t=>t.Id==tableId);var page=catalog.ConfigPages.Single(p=>p.Id==pageId&&p.ConfigTableId==tableId);var controls=catalog.ConfigColumns.Where(c=>c.ConfigTableId==tableId&&c.ControlKind=="CascadeTree").ToArray();if(table.IsRongyu!=1||controls.Length<2)return Empty();
        var storage=MetadataStoragePolicy.ForTable(catalog,table);var syntax=FixedConditionPlan.Compile(page.WhereSql,storage.PhysicalColumns.Select(c=>c.ColumnName));var parents=syntax.Terms.Where(t=>t.Values.Any(v=>v.Kind==FixedConditionValueKind.Variable&&(v.Text.Equals("parentId",StringComparison.OrdinalIgnoreCase)||v.Text.Equals("parent_id",StringComparison.OrdinalIgnoreCase)))).ToArray();if(parents.Length==0)return Empty();
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(c=>c.ConfigTableId==tableId));var branches=controls.Select(c=>CompileBranch(catalog,table,page,c,columns,controls)).ToArray();var remaining=branches.ToList();var groups=new List<Branch[]>();
        while(remaining.Count>0){var group=new List<Branch>{remaining[0]};remaining.RemoveAt(0);bool changed;do{changed=false;foreach(var other in remaining.ToArray())if(group.Any(b=>b.Ids.Any(id=>other.Ids.Any(candidate=>candidate.ColumnName==id.ColumnName)))){group.Add(other);remaining.Remove(other);changed=true;}}while(changed);if(group.Count>1)groups.Add(group.ToArray());}
        if(groups.Count==0)return Empty();Realm(catalog,table,page);var sourceProject=storage.PhysicalColumns.SingleOrDefault(c=>c.ColumnName=="yz_project_id");if(GlobalCrossTableTarget.Declared(table)||sourceProject is null||!Scalar(storage,table,sourceProject)||sourceProject.LogicalType is not("string" or "int64"))throw Unsupported();
        if(Exact(page))throw Unsupported();foreach(var group in groups)Prove(catalog,table,group);
        var result=new Dictionary<string,RedundantSharedParentPlan>(StringComparer.Ordinal);
        foreach(var term in parents)
        {
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||term.Values[0].Kind!=FixedConditionValueKind.Variable)throw Unsupported();var column=columns.Single(c=>c.ColumnName==term.Field);if(column.LogicalType is not("string" or "int64")||!storage.IsPhysical(column))throw Unsupported();if(branches.Any(b=>b.Names.Any(n=>n.Id==column.Id)))throw Unsupported();
            var matches=groups.SelectMany(g=>g).SelectMany(b=>b.Ids.Select((id,index)=>(Branch:b,Index:index,Column:id))).Where(x=>x.Column.Id==column.Id).Select(x=>new RedundantSharedParentPlan(column.Id,column.ColumnName,column.LogicalType,x.Branch.Control.Id,Array.AsReadOnly(x.Branch.Levels.Take(x.Index+1).ToArray()))).ToArray();
            if(matches.Length==0)
            {
                var target=catalog.ConfigTables.SingleOrDefault(t=>t.Id==column.LinkConfigTableId);if(target is null||!GlobalCrossTableTarget.Declared(target))continue;
                var overlay=catalog.ConfigPageColumns.SingleOrDefault(f=>f.ConfigPageId==pageId&&f.ConfigColumnId==column.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(column.ControlDetail)))throw Unsupported();var single=GlobalSingleRelationTarget.RequireCached(catalog,table,column,target);
                matches=groups.SelectMany(g=>g).SelectMany(b=>b.Levels.Select((level,index)=>(b,level,index))).Where(x=>x.level.TableId==target.Id&&x.level.PageId==single.Page.Id&&x.level.NameId==single.Name.Id).Select(x=>new RedundantSharedParentPlan(column.Id,column.ColumnName,column.LogicalType,x.b.Control.Id,Array.AsReadOnly(x.b.Levels.Take(x.index+1).ToArray()))).ToArray();
            }
            var unique=matches.DistinctBy(Identity,StringComparer.Ordinal).ToArray();if(unique.Length!=1)throw Unsupported();result.Add(column.Id,unique[0]);
        }
        return new ReadOnlyDictionary<string,RedundantSharedParentPlan>(result);
    }
    /// <summary>完整目标、页面及每个物理ID／缓存先冻结；坏同伴、变化覆盖和额外SQL不能被忽略。</summary>
    private static Branch CompileBranch(MetadataCatalog catalog,TableMetadata source,PageMetadata page,ColumnMetadata control,IReadOnlyList<ColumnMetadata> sourceColumns,IReadOnlyList<ColumnMetadata> controls)
    {
        var sourceStorage=MetadataStoragePolicy.ForTable(catalog,source);if(control.IsVirtualColumn!=0||Join(control)||!sourceStorage.IsPhysical(control)||control.LogicalType is not("string" or "int64"))throw Unsupported();var overlay=catalog.ConfigPageColumns.SingleOrDefault(f=>f.ConfigPageId==page.Id&&f.ConfigColumnId==control.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(control.ControlDetail)))throw Unsupported();
        using var json=JsonDocument.Parse(control.ControlDetail);var root=json.RootElement;Object(root);if(Text(root,"sourceMode","crossTable")!="crossTable"||Extra(root)||!root.TryGetProperty("levels",out var levels)||levels.ValueKind!=JsonValueKind.Array||levels.GetArrayLength()<2)throw Unsupported();
        var result=new List<RedundantSharedParentLevel>();var ids=new List<ColumnMetadata>();var names=new List<ColumnMetadata>();var keys=new HashSet<string>(StringComparer.Ordinal);var prefixes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);TableMetadata? previous=null;
        foreach(var level in levels.EnumerateArray())
        {
            Object(level);if(Extra(level)||Text(level,"idField","id")!="id")throw Unsupported();var target=catalog.ConfigTables.Single(t=>t.TableName==Text(level,"table",""));if(target.Id==source.Id||target.IsRongyu is not(0 or 1)||target.IsView!=0||target.IsFunctionTable!=0)throw Unsupported();var prefix=Text(level,"columnPrefix",target.TableName);var key=Text(level,"key",target.TableName);MetadataValidation.RequireIdentifier(prefix);if(!prefixes.Add(prefix)||!keys.Add(key)||prefix is "yz_project" or "insert_manager" or "update_manager" or "insert_user" or "update_user")throw Unsupported();
            var targetColumns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(c=>c.ConfigTableId==target.Id));var ts=MetadataStoragePolicy.ForTable(catalog,target);var primary=targetColumns.SingleOrDefault(c=>c.ColumnName=="id");var project=targetColumns.SingleOrDefault(c=>c.ColumnName=="yz_project_id");var global=GlobalCrossTableTarget.Declared(target);if(primary is null||primary.LogicalType!="int64"||primary.IsDbNotNull!=1||!Scalar(ts,target,primary)||!global&&(project is null||project.LogicalType is not("string" or "int64")||!Scalar(ts,target,project)))throw Unsupported();
            var display=targetColumns.Single(c=>c.ColumnName==Text(level,"nameField","name"));if(display.LogicalType!="string"||display.IsVirtualColumn!=0)throw Unsupported();if(!ts.IsPhysical(display)){var read=NonRedundantReads.CompileMatch(catalog,target.Id,display.ColumnName);if(read.OutputTypes[display.ColumnName]!="string"||NonRedundantReads.MatchReadsTable(catalog,target.Id,display.ColumnName,source.TableName))throw Unsupported();}
            var parent=previous is null?null:targetColumns.Single(c=>c.ColumnName==Text(level,"parentField",""));if(parent is not null&&(!Scalar(ts,target,parent)||parent.LogicalType is not("string" or "int64")||parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=previous!.Id))throw Unsupported();
            var required=new[]{display.Id}.Concat(parent is null?[]:new[]{parent.Id}).ToArray();var candidates=catalog.ConfigPages.Where(p=>p.ConfigTableId==target.Id&&required.All(id=>catalog.ConfigPageColumns.Any(f=>f.ConfigPageId==p.Id&&f.ConfigColumnId==id&&Readable(f)))).Select(p=>(Page:p,Condition:Condition(p,ts.PhysicalColumns))).Where(x=>x.Condition.Allowed).ToArray();if(candidates.Length!=1)throw Unsupported();var chosen=candidates[0];Realm(catalog,target,chosen.Page);if(global)GlobalCrossTableTarget.Require(catalog,target,chosen.Page);var deleted=targetColumns.SingleOrDefault(c=>c.ColumnName=="delete_no");if(deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!ts.IsPhysical(deleted)))throw Unsupported();
            var id=sourceColumns.Single(c=>c.ColumnName==prefix+"_id");var name=sourceColumns.Single(c=>c.ColumnName==prefix+"_name");if(id.LogicalType is not("string" or "int64")||id.IsDbNotNull!=0||!sourceStorage.IsPhysical(id)||id.IsVirtualColumn!=0||Join(id)||id.ControlKind.Length>0&&!(id.ControlKind=="CascadeTree"&&controls.Any(c=>c.Id==id.Id))||id.LinkConfigTableId.Length>0&&id.LinkConfigTableId!=target.Id||name.LogicalType!="string"||name.IsDbNotNull!=0||!sourceStorage.IsPhysical(name))throw Unsupported();
            result.Add(new(key,prefix,target.Id,target.TableName,chosen.Page.Id,display.Id,parent?.ColumnName,global?null:project!.LogicalType,deleted is not null,chosen.Page.WhereSql,chosen.Condition.Plan));ids.Add(id);names.Add(name);previous=target;
        }
        if(ids[^1].Id!=control.Id)throw Unsupported();return new(control,Array.AsReadOnly(result.ToArray()),Array.AsReadOnly(ids.ToArray()),Array.AsReadOnly(names.ToArray()));
    }
    /// <summary>共同前缀由协调器验证，缓存归属及派生终点复用正式数据层编译器。</summary>
    private static void Prove(MetadataCatalog catalog,TableMetadata source,IReadOnlyList<Branch> branches)
    {
        _=new SharedCrossTableCoordinator(branches.Select(b=>new SharedCrossTableBranch(b.Control.Id,b.Levels.Select((level,index)=>new SharedCrossTableSlot(b.Ids[index].ColumnName,b.Names[index].ColumnName,b.Ids[index].LogicalType,LevelIdentity(level))).ToArray())).ToArray());
        var physical=new List<RelationCacheDefinition>();var derived=new List<RelationCacheDefinition>();
        foreach(var uses in branches.SelectMany(b=>b.Ids.Select((id,index)=>(Branch:b,Id:id,Index:index))).GroupBy(x=>x.Id.Id,StringComparer.Ordinal))
        {
            var chosen=uses.OrderBy(x=>x.Branch.Control.Id==x.Id.Id?0:1).ThenBy(x=>x.Branch.Control.Id,StringComparer.Ordinal).First();var b=chosen.Branch;var level=b.Levels[chosen.Index];var target=catalog.ConfigTables.Single(t=>t.Id==level.TableId);var value=catalog.ConfigColumns.Single(c=>c.ConfigTableId==target.Id&&c.Id==level.NameId);var globals=b.Levels.Where(l=>l.ProjectType is null).ToDictionary(l=>l.Key,l=>l.PageId,StringComparer.Ordinal);
            var definition=new RelationCacheDefinition(source.Id,chosen.Id.Id,target.Id,value.Id,b.Names[chosen.Index].Id,false){CrossTableColumnId=b.Control.Id,CrossTableLevelKey=level.Key,CrossTablePageId=level.ProjectType is null?level.PageId:null,CrossTableGlobalPages=globals.Count==0?null:new ReadOnlyDictionary<string,string>(globals)};
            if(MetadataStoragePolicy.ForTable(catalog,target).IsPhysical(value))physical.Add(definition);else derived.Add(definition);
        }
        _=RelationMutationPlan.Compile(catalog,physical);DerivedCrossTableCaches.Validate(catalog,derived,physical);
    }
    /// <summary>同页只允许项目变量及固定物理常量，目标父上下文和精确权限另行证明。</summary>
    private static (bool Allowed,FixedConditionSqlPlan? Plan) Condition(PageMetadata page,IReadOnlyList<ColumnMetadata> columns)
    {
        if(Exact(page))return(false,null);var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(c=>c.ColumnName));var constants=new List<FixedConditionTerm>();foreach(var term in syntax.Terms)
        {
            var variables=term.Values.Where(v=>v.Kind==FixedConditionValueKind.Variable).ToArray();if(variables.Length==0){constants.Add(term);continue;}if(variables.Any(v=>(v.Text.Equals("parentId",StringComparison.OrdinalIgnoreCase)||v.Text.Equals("parent_id",StringComparison.OrdinalIgnoreCase))))return(false,null);if(term.Field!="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1||!variables[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))throw Unsupported();
        }
        var fields=constants.Select(t=>t.Field).Distinct(StringComparer.Ordinal).ToArray();return(true,constants.Count==0?null:FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(string.Join(" AND ",constants.Select(t=>t.ToDefinition())),fields),fields.ToDictionary(f=>f,f=>columns.Single(c=>c.ColumnName==f).LogicalType,StringComparer.Ordinal)));
    }
    /// <summary>目标角色包含完整祖先，重复表名或数值ID不代表同一父身份。</summary>
    private static string Identity(RedundantSharedParentPlan plan)=>JsonSerializer.Serialize(plan.Levels.Select(LevelIdentity));
    /// <summary>层内身份及条件冻结为普通文本，不序列化可执行SQL计划的私有状态。</summary>
    private static string LevelIdentity(RedundantSharedParentLevel level)=>JsonSerializer.Serialize(new{level.TableId,level.PageId,level.NameId,level.Parent,level.ConditionDefinition});
    /// <summary>来源和目标均须显式发布，后台选择不是请求授权。</summary>
    private static void Realm(MetadataCatalog catalog,TableMetadata table,PageMetadata page){if(table.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||!catalog.YzAdminTypes.Any(a=>a.Id==page.YzAdminTypeId&&a.YzProjectId==catalog.YzProjectId))throw Unsupported();}
    /// <summary>真实物理标量身份排除虚拟、联接及凭据；字段类型由调用点继续限制。</summary>
    private static bool Scalar(MetadataStoragePlan plan,TableMetadata table,ColumnMetadata column)=>column.ConfigTableId==table.Id&&plan.IsPhysical(column)&&column.IsVirtualColumn==0&&!Join(column)&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList")&&column.LogicalType is not("json" or "binary");
    /// <summary>沿原显示／编辑开关判断候选字段可读性。</summary>
    private static bool Readable(PageColumnMetadata field)=>Flag(field,"isInGrid",field.IsShow==1)||Flag(field,"isInEdit",field.IsEdit==1);
    /// <summary>整数／布尔开关不接受普通字符串true。</summary>
    private static bool Flag(PageColumnMetadata field,string key,bool fallback)=>field.Extra.TryGetValue(key,out var value)?value.ValueKind==JsonValueKind.True||value.ToString()=="1":fallback;
    /// <summary>精确页不能通过另一范围降级。</summary>
    private static bool Exact(PageMetadata page)=>page.Extra.TryGetValue("exactDataManageMatchField",out var value)&&value.ValueKind!=JsonValueKind.Null&&!string.IsNullOrWhiteSpace(value.ToString());
    /// <summary>结构JSON必须为无重复键对象。</summary>
    private static void Object(JsonElement value){if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw Unsupported();}
    /// <summary>固定定义不接受附加查询或动态页面。</summary>
    private static bool Extra(JsonElement value)=>new[]{"whereSql","where","sql","pageId","filterMatchMode"}.Any(k=>value.TryGetProperty(k,out _));
    /// <summary>标识符只接收文本，不将对象或数值隐式转换。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw Unsupported();
    /// <summary>联接标志仅采用原布尔及整数语义。</summary>
    private static bool Join(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
    /// <summary>不适用的旧路径返回只读空计划，不开放新用途。</summary>
    private static IReadOnlyDictionary<string,RedundantSharedParentPlan> Empty()=>new ReadOnlyDictionary<string,RedundantSharedParentPlan>(new Dictionary<string,RedundantSharedParentPlan>(StringComparer.Ordinal));
    /// <summary>拒绝信息不包含连接或业务内容。</summary>
    private static NotSupportedException Unsupported()=>new("R1共享父范围尚未取得完整固定元数据证明。");
}
