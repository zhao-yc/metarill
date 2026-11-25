using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Metarill.Data.Queries;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

internal sealed record SharedRedundantParentPath(CrossTableReadModel Read,int Index)
{
    internal CrossTableLevelModel Level=>Read.Levels[Index];
}
/// <summary>R1来源父范围使用已持久化的物理父键，单链和共享目标链均验证真实父记录的固定可见范围。</summary>
internal static class SharedRedundantParentCompilation
{
    private static readonly ConditionalWeakTable<GenerationModel,ConcurrentDictionary<(string,string),IReadOnlyDictionary<string,RedundantSharedParentPlan>>> ParentPlans=new();
    /// <summary>固定跨表父页使用同一范围证明，独立自树与其它跨表控件各有自己的路径。</summary>
    internal static bool Enabled(GenerationModel model,TableModel table,PageModel page)=>
        table.Source.IsRongyu==1&&model.Catalog.DatabaseProvider=="mysql"&&page.Scopes.Any(scope=>scope.Source=="parent")&&
        CrossTableWriteCompilation.ForService(model,table,page).Count>0;
    /// <summary>同一物理父键只能指向一个已冻结目标角色，不能从不同页或名字借用证明。</summary>
    internal static IReadOnlyList<SharedRedundantParentPath> ForColumn(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        if(table.Source.IsRongyu!=1||!page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id))return [];
        var shared=SharedCrossTableCompilation.ForPage(model,table,page);
        var plans=CrossTableWriteCompilation.ForService(model,table,page);
        if(shared.Gaps.Count>0)throw new NotSupportedException("R1父范围的全部共享同伴必须通过完整证明。");
        // 原物理 List 父由自己的发布证明处理；仅存在跨表写入链时才匹配级联目标角色。
        if(plans.Count==0)return [];
        var paths=plans.SelectMany(plan=>plan.Levels.Select((level,index)=>new SharedRedundantParentPath(plan.Read,index))).ToArray();
        var matches=paths.Where(path=>path.Level.Prefix+"_id"==column.Source.ColumnName).ToArray();
        var cascadeParent=matches.Length>0;
        if(!cascadeParent)
        {
            var target=model.Tables.SingleOrDefault(target=>target.Source.Id==column.Source.LinkConfigTableId);if(target is null||!target.IsGlobal)return [];
            var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(column.Source.ControlDetail)))throw new NotSupportedException("R1全局物理父不能替换固定关系覆盖。");
            var single=GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,column.Source,target.Source);matches=paths.Where(path=>path.Level.Target.Source.Id==target.Source.Id&&path.Level.AlternateKey is null&&path.Level.Page.Source.Id==single.Page.Id&&path.Level.Name.Source.Id==single.Name.Id).ToArray();
        }
        var result=matches.DistinctBy(Identity,StringComparer.Ordinal).ToArray();
        if(result.Length!=1)throw new NotSupportedException("R1跨表父字段缺少唯一固定目标角色。");
        var level=result[0].Level;
        if(level.Target.IsGlobal)
        {
            // 两类物理父键都走已匹配的级联层，使用相同的账号条件和事务读范围。
            if(FixedPageConditionCompilation.HasLogicalFields(level.Page))
                NonRedundantCrossTargetsCompilation.RequireGlobal(model,level.Target,level.Page,level.Name);
            else
                GlobalCrossTableTarget.RequireCrossTableName(model.Catalog,level.Target.Source,level.Page.Source,level.Name.Source,allowAccount:true);
        }
        return result;
    }
    /// <summary>仅原层级明确声明的文本业务键进入非数值父协议。</summary>
    internal static bool IsText(GenerationModel model,TableModel table,PageModel page,ColumnModel column)=>
        column.Type=="string?"&&ForColumn(model,table,page,column).Any(path=>path.Level.TextKey);

    /// <summary>两页以相同已发布祖先路径使用文本业务键，才能共享原 parentId。</summary>
    internal static ColumnModel? CommonTextParent(GenerationModel model,TableModel source,PageModel sourcePage,
        TableModel target,PageModel targetPage,bool allowSparseTarget=false)
    {
        if(source.Source.IsRongyu!=1||target.Source.IsRongyu!=1&&(!allowSparseTarget||target.Source.IsRongyu!=0)||source.IsGlobal||target.IsGlobal||
            source.Source.YzProjectId!=model.Catalog.YzProjectId||target.Source.YzProjectId!=model.Catalog.YzProjectId||
            sourcePage.Source.YzProjectId!=model.Catalog.YzProjectId||targetPage.Source.YzProjectId!=model.Catalog.YzProjectId)
            return null;
        var sourceFields=sourcePage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).Take(2).ToArray();
        var targetFields=targetPage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).Take(2).ToArray();
        if(sourceFields is not [var sourceKey]||targetFields is not [var targetKey]||sourceKey.Type!="string?"||targetKey.Type!="string?")
            return null;
        if(ForColumn(model,source,sourcePage,sourceKey) is not [var left]||!left.Level.TextKey)
            return null;
        var right=target.Source.IsRongyu==1
            ?ForColumn(model,target,targetPage,targetKey).SingleOrDefault()
            :NonRedundantCrossParentCompilation.Find(model,target,targetPage,targetKey) is {} sparse?new SharedRedundantParentPath(sparse.Read,sparse.Index):null;
        return right is {Level.TextKey:true}&&Identity(left)==Identity(right)?targetKey:null;
    }

    /// <summary>完整包逐个证明物理父字段，来源项目与后台必须明确发布。</summary>
    internal static void Require(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page)||table.IsGlobal||table.Source.YzProjectId!=model.Catalog.YzProjectId||page.Source.YzProjectId!=model.Catalog.YzProjectId||!model.Catalog.YzAdminTypes.Any(admin=>admin.Id==page.Source.YzAdminTypeId&&admin.YzProjectId==model.Catalog.YzProjectId))throw new NotSupportedException("R1跨表父范围要求普通已发布来源及明确后台。");
        if(ExactDataManageCompilation.Declared(page)&&ExactDataManageCompilation.Resolve(model,table,page) is not {Derived:false})
            throw new NotSupportedException("R1跨表父范围的精确管理要求物理单值范围列。");
        var physical=MetadataStoragePolicy.ForTable(model.Catalog,table.Source).PhysicalColumns;
        var plans=ParentPlans.GetValue(model,_=>new()).GetOrAdd((table.Source.Id,page.Source.Id),_=>CompileParents(model,table,page));
        foreach(var scope in page.Scopes.Where(scope=>scope.Source=="parent"))
        {
            // 范围在准备前建立，数组及缓存身份以准备后的同ID列为准。
            var prepared=table.Columns.Single(column=>column.Source.Id==scope.Column.Source.Id);
            if(prepared.Type is not("string?" or "long?")||prepared.IsJsonArray||prepared.IsManagedCascadeName||!physical.Any(column=>column.Id==prepared.Source.Id))throw new NotSupportedException("R1父范围必须使用真实标量身份列。");var typed=ForColumn(model,table,page,prepared);if(typed.Count>0){if(!plans.TryGetValue(prepared.Source.Id,out var plan)||typed.Any(path=>!Matches(plan,path)))throw new NotSupportedException("生成及文件R1父定义不一致。");continue;}
            var parent=model.Tables.SingleOrDefault(parent=>!string.IsNullOrWhiteSpace(prepared.Source.LinkConfigTableId)?parent.Source.Id==prepared.Source.LinkConfigTableId:parent.Source.TableName+"_id"==prepared.Source.ColumnName);if(parent is null||parent.Source.Id==table.Source.Id||parent.IsGlobal||parent.Source.IsView!=0||parent.Source.IsFunctionTable!=0||parent.Source.YzProjectId!=model.Catalog.YzProjectId||!parent.Columns.Any(column=>column.Source.ColumnName=="id"&&column.Type=="long"&&NonRedundantReadCompilation.Physical(model,parent,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))||!parent.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"&&(column.Type is "long?" or "string?")&&NonRedundantReadCompilation.Physical(model,parent,column)&&!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")))throw new NotSupportedException("R1独立物理父缺少原项目身份，不能猜测全局范围。");
        }
    }
    /// <summary>保持原生成器的不支持异常合同，公共编译器仍保留首因供诊断。</summary>
    private static IReadOnlyDictionary<string,RedundantSharedParentPlan> CompileParents(GenerationModel model,TableModel table,PageModel page)
    {
        try{return RedundantSharedParents.Compile(model.Catalog,table.Source.Id,page.Source.Id);}catch(Exception invalid)when(invalid is InvalidOperationException or ArgumentException or JsonException){throw new NotSupportedException("R1公共父定义尚未通过完整证明。",invalid);}
    }
    /// <summary>公共元数据计划与生成器完整读取角色逐层一致，不能单独借用某个有效同伴。</summary>
    private static bool Matches(RedundantSharedParentPlan plan,SharedRedundantParentPath path)=>plan.Levels.Count==path.Index+1&&plan.Levels.Select((level,index)=>level.TableId==path.Read.Levels[index].Target.Source.Id&&level.PageId==path.Read.Levels[index].Page.Source.Id&&level.NameId==path.Read.Levels[index].Name.Source.Id&&level.Parent==path.Read.Levels[index].Parent?.Source.ColumnName&&level.KeyColumn==path.Read.Levels[index].KeyColumn&&level.TextKey==path.Read.Levels[index].TextKey).All(value=>value);
    /// <summary>账号、递归或文本业务键父链需要相关范围，普通整数父页沿用原查询。</summary>
    internal static IReadOnlyList<RedundantSharedParentPlan> AccountPlans(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Source.IsRongyu!=1||!page.Scopes.Any(scope=>scope.Source=="parent")||
            !Enabled(model,table,page)||
            !CrossTableReadCompilation.ForService(model,table,page).Any(read=>read.HasTextKeys)&&
            !model.Tables.SelectMany(candidate=>candidate.Pages).Any(candidate=>
                FixedPageConditionCompilation.HasAccountVariables(candidate)||FixedPageConditionCompilation.HasLogicalFields(candidate)))return [];
        return ParentPlans.GetValue(model,_=>new()).GetOrAdd((table.Source.Id,page.Source.Id),_=>CompileParents(model,table,page))
            .Values.Where(path=>path.Levels.Any(level=>level.RequiresCurrentScope))
            .OrderBy(path=>path.ColumnName,StringComparer.Ordinal).ToArray();
    }

    internal static bool HasAccountPath(GenerationModel model,TableModel table,PageModel page)=>AccountPlans(model,table,page).Count>0;

    /// <summary>固定源码只携带物理路径和类型化原条件，运行时不重新解释页面元数据。</summary>
    internal static string AccountDeclaration(GenerationModel model,TableModel table,PageModel page,string name)
    {
        var paths=AccountPlans(model,table,page);
        if(paths.Count==0)return "";
        string Level(RedundantSharedParentLevel level)
        {
            var target=model.Tables.Single(candidate=>candidate.Source.Id==level.TableId);
            var scope=target.Pages.Single(candidate=>candidate.Source.Id==level.PageId).Condition;
            var declaration=$"new({Literal(level.Key)},{Literal(level.Prefix)},{Literal(level.TableId)},{Literal(level.TableName)},{Literal(level.PageId)},{Literal(level.NameId)},{Literal(level.Parent)},{Literal(level.ProjectType)},{(level.SoftDelete?"true":"false")},{Literal(level.ConditionDefinition)},{FixedPageConditionCompilation.ConditionExpression(scope)})";
            var members=new List<string>();
            if(level.AlternateKey is not null)members.Add("AlternateKey="+Literal(level.AlternateKey));
            if(level.TextKey)members.Add("TextKey=true");
            if(level.RecursiveCandidate is not null)
            {
                var outputs=string.Join(",",new[]{"id",level.Parent,level.AlternateKey}.OfType<string>().Distinct(StringComparer.Ordinal).Select(Literal));
                members.Add($"RecursiveCandidate=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileCrossPageCandidate({Literal(level.TableId)},{Literal(level.PageId)},new string[]{{{outputs}}},new string[]{{}})");
            }
            return members.Count==0?declaration:declaration+"{"+string.Join(",",members)+"}";
        }
        return $"    private static readonly global::Metarill.Data.Queries.RedundantSharedParentPlan[] {name}=[{string.Join(",",paths.Select(path=>$"new({Literal(path.ColumnId)},{Literal(path.ColumnName)},{Literal(path.LogicalType)},{Literal(path.ControlId)},new global::Metarill.Data.Queries.RedundantSharedParentLevel[]{{{string.Join(",",path.Levels.Select(Level))}}})"))}];\n";
    }
    /// <summary>在查询中验证真实物理父键和目标账号范围；当前写入查询明确取锁。</summary>
    internal static string ApplyAccountScope(GenerationModel model,TableModel table,PageModel page,string plan,string prefix,string locking)=>
        HasAccountPath(model,table,page)?$"        query=global::Metarill.Data.Queries.RedundantSharedParentScopeSql.Apply(query,session,{plan},actor,{Literal(prefix)},{locking});\n":"";

    private static string Literal(string? value)=>JsonSerializer.Serialize(value);

    /// <summary>候选只验证来源父，目标候选自身的父上下文仍走原逻辑。</summary>
    internal static IReadOnlyList<SharedRedundantParentPath> ForTable(GenerationModel model,TableModel table,PageModel page,TableModel target)=>page.Scopes.Where(scope=>scope.Source=="parent").SelectMany(scope=>ForColumn(model,table,page,scope.Column)).Where(path=>path.Level.Target.Source.Id==target.Source.Id).DistinctBy(Identity,StringComparer.Ordinal).ToArray();
    /// <summary>共享目标角色按完整祖先定义比较，重复表名不能合并两个角色。</summary>
    internal static string Identity(SharedRedundantParentPath path)=>JsonSerializer.Serialize(path.Read.Levels.Take(path.Index+1).Select(level=>new{table=level.Target.Source.Id,page=level.Page.Source.Id,name=level.Name.Source.Id,parent=level.Parent?.Source.Id,condition=level.Page.Source.WhereSql,key=level.AlternateKey?.Source.Id}));
    /// <summary>混合父链按层选键协议。写入及导入使用当前事务的锁读。</summary>
    internal static string ReadPath(SharedRedundantParentPath path,PageModel page,string locking,string parent)=>
        $"CrossReader{page.Source.Id}_{path.Read.Source.Source.Id}(actor,{locking},false)"+
        NonRedundantCrossParentCompilation.PathSuffix(path.Read,path.Index,$"SharedR1ParentKey{page.Source.Id}({parent})");
    internal static string Call(SharedRedundantParentPath path,PageModel page,string locking)=>$"        await {ReadPath(path,page,locking,"parentId")};\n";
    /// <summary>实际存储复核不把R1缓存父键改为R0推导值，钩子或DML逃逸必须回滚。</summary>
    internal static string VerifyCall(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?$"        await VerifySharedR1Parent{page.Source.Id}Async(entity,actor,parentId,cancellationToken);\n":"";
    /// <summary>为已证明的R1跨表父页生成私有键解析及写后／删除前实际行复核。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page))return "";Require(model,table,page);
        // 写前已按真实动作授权。写后以该事务的主键和项目／父键重新核验，避免要求额外详情权限。
        var exact=ExactDataManageCompilation.Declared(page);
        var fields=FixedPageQueryCompilation.For(table,page).IdentityScopes.Where(scope=>exact||scope.Source=="parent").Select(scope=>scope.Column).DistinctBy(column=>column.Source.Id).ToArray();
        var storedQuery=exact?"query":$"Scope{page.Source.Id}(actor,parentId{(HasAccountPath(model,table,page)?",locking:true":"")})";
        var result=new StringBuilder();
        var textParent=page.Scopes.Any(scope=>scope.Source=="parent"&&IsText(model,table,page,scope.Column));
        if(textParent)
            result.AppendLine("    /// <summary>保留当前页明确发布的文本业务父键。</summary>")
                .AppendLine($"    private static string SharedR1ParentKey{page.Source.Id}(string? value)=>!string.IsNullOrWhiteSpace(value)?value:throw new ArgumentException(\"当前页面要求非空文本parentId。\");");
        else result.AppendLine("    /// <summary>父ID不接受前导零、符号或空白。</summary>").AppendLine($"    private static long SharedR1ParentKey{page.Source.Id}(string? value){{if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||value!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");return id;}}");
        result.AppendLine("    /// <summary>按实际物理父及来源项目复核，目标父保持固定范围和当前锁读。</summary>").AppendLine($"    private async Task VerifySharedR1Parent{page.Source.Id}Async({table.EntityType} entity,RuntimeActor actor,string? parentId,CancellationToken cancellationToken)").AppendLine("    {").AppendLine($"        _=SharedR1ParentKey{page.Source.Id}(parentId);")
            .Append(exact?"        var query=mapper.Query();\n"+FixedPageConditionCompilation.Apply(page,"PageCondition"+page.Source.Id,"verify_parent"):"")
            .AppendLine($"        var stored=await {storedQuery}.Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {table.EntityType}{{Id=row.Id,{string.Join(",",fields.Select(column=>column.Property+"=row."+column.Property))}}},cancellationToken);")
            .AppendLine($"        if(stored is null||{string.Join("||",fields.Select(column=>"stored."+column.Property+"!=entity."+column.Property))})throw new InvalidOperationException(\"共享R1实际父归属发生变化，未提交。\");")
            .AppendLine($"        await ValidateParents{page.Source.Id}Async(parentId,actor,cancellationToken);").AppendLine("    }");return result.ToString();
    }
}
