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
    /// <summary>识别固定共享链和单链的物理父页。</summary>
    internal static bool Enabled(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsRongyu==1&&page.Scopes.Any(scope=>scope.Source=="parent")&&
        (SharedCrossTableCompilation.ForPage(model,table,page).Groups.Count>0||IndependentPlan(model,table,page) is not null);

    /// <summary>单链沿用 Excel 支持的两至三级固定角色，证明失败时保留完整包门槛。</summary>
    private static CrossTableWriteModel? IndependentPlan(GenerationModel model,TableModel table,PageModel page)
    {
        if(model.Catalog.DatabaseProvider!="mysql"||
            table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").ToArray() is not [var source]||
            !NonRedundantCrossTableSubset.Declared(source))return null;
        var plan=CrossTableWriteCompilation.Resolve(model,table,page,source).Definition;
        if(plan is null||plan.Levels.Count is <2 or >3)return null;
        return plan;
    }
    /// <summary>同一物理父键只能指向一个已冻结目标角色，不能从不同页或名字借用证明。</summary>
    internal static IReadOnlyList<SharedRedundantParentPath> ForColumn(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        if(table.Source.IsRongyu!=1||!page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id))return [];
        var shared=SharedCrossTableCompilation.ForPage(model,table,page);
        var plans=shared.Groups.SelectMany(group=>group.Plans);
        if(shared.Groups.Count==0)
        {
            if(IndependentPlan(model,table,page) is not{} independent)return [];
            plans=[independent];
        }
        if(shared.Gaps.Count>0)throw new NotSupportedException("R1父范围的全部共享同伴必须通过完整证明。");
        var paths=plans.SelectMany(plan=>plan.Levels.Select((level,index)=>new SharedRedundantParentPath(plan.Read,index))).ToArray();
        var matches=paths.Where(path=>path.Level.Prefix+"_id"==column.Source.ColumnName).ToArray();
        if(matches.Length==0)
        {
            var target=model.Tables.SingleOrDefault(target=>target.Source.Id==column.Source.LinkConfigTableId);if(target is null||!target.IsGlobal)return [];
            var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(column.Source.ControlDetail)))throw new NotSupportedException("R1全局物理父不能替换固定关系覆盖。");
            var single=GlobalSingleRelationTarget.RequireCached(model.Catalog,table.Source,column.Source,target.Source);matches=paths.Where(path=>path.Level.Target.Source.Id==target.Source.Id&&path.Level.Page.Source.Id==single.Page.Id&&path.Level.Name.Source.Id==single.Name.Id).ToArray();
        }
        var result=matches.DistinctBy(Identity,StringComparer.Ordinal).ToArray();if(result.Length!=1)throw new NotSupportedException("R1跨表父字段缺少唯一固定目标角色。");if(result[0].Level.Target.IsGlobal)GlobalCrossTableTarget.Require(model.Catalog,result[0].Level.Target.Source,result[0].Level.Page.Source);return result;
    }
    /// <summary>完整包逐个证明物理父字段，来源项目与后台必须明确发布。</summary>
    internal static void Require(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page)||table.IsGlobal||table.Source.YzProjectId!=model.Catalog.YzProjectId||page.Source.YzProjectId!=model.Catalog.YzProjectId||!model.Catalog.YzAdminTypes.Any(admin=>admin.Id==page.Source.YzAdminTypeId&&admin.YzProjectId==model.Catalog.YzProjectId))throw new NotSupportedException("R1跨表父范围要求普通已发布来源及明确后台。");
        if(ExactDataManageCompilation.Declared(page)&&
            (!CrossTableReadCompilation.ForService(model,table,page).Any(read=>CrossTableWriteRenderer.Merged(table,read))||
                ExactDataManageCompilation.Resolve(model,table,page) is not {Derived:false}))
            throw new NotSupportedException("R1跨表父范围的精确管理要求同表角色及物理单值范围列。");
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
    private static bool Matches(RedundantSharedParentPlan plan,SharedRedundantParentPath path)=>plan.Levels.Count==path.Index+1&&plan.Levels.Select((level,index)=>level.TableId==path.Read.Levels[index].Target.Source.Id&&level.PageId==path.Read.Levels[index].Page.Source.Id&&level.NameId==path.Read.Levels[index].Name.Source.Id&&level.Parent==path.Read.Levels[index].Parent?.Source.ColumnName).All(value=>value);
    /// <summary>候选只验证来源父，目标候选自身的父上下文仍走原逻辑。</summary>
    internal static IReadOnlyList<SharedRedundantParentPath> ForTable(GenerationModel model,TableModel table,PageModel page,TableModel target)=>page.Scopes.Where(scope=>scope.Source=="parent").SelectMany(scope=>ForColumn(model,table,page,scope.Column)).Where(path=>path.Level.Target.Source.Id==target.Source.Id).DistinctBy(Identity,StringComparer.Ordinal).ToArray();
    /// <summary>共享目标角色按完整祖先定义比较，重复表名不能合并两个角色。</summary>
    private static string Identity(SharedRedundantParentPath path)=>JsonSerializer.Serialize(path.Read.Levels.Take(path.Index+1).Select(level=>new{table=level.Target.Source.Id,page=level.Page.Source.Id,name=level.Name.Source.Id,parent=level.Parent?.Source.Id,condition=level.Page.Source.WhereSql}));
    /// <summary>原ID协议保持规范正整数字符串；候选无锁，变更加入原事务当前读。</summary>
    internal static string Call(SharedRedundantParentPath path,PageModel page,string locking)=>$"        await CrossReader{page.Source.Id}_{path.Read.Source.Source.Id}(actor,{locking},false).PathAsync({path.Index},SharedR1ParentKey{page.Source.Id}(parentId),cancellationToken);\n";
    /// <summary>实际存储复核不把R1缓存父键改为R0推导值，钩子或DML逃逸必须回滚。</summary>
    internal static string VerifyCall(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?$"        await VerifySharedR1Parent{page.Source.Id}Async(entity,actor,parentId,cancellationToken);\n":"";
    /// <summary>为已证明的R1跨表父页生成私有键解析及写后／删除前实际行复核。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page))return "";Require(model,table,page);
        // 写前已按真实动作授权。写后以该事务的主键和项目／父键重新核验，避免要求额外详情权限。
        var exact=ExactDataManageCompilation.Declared(page);
        var fields=FixedPageQueryCompilation.For(table,page).IdentityScopes.Where(scope=>exact||scope.Source=="parent").Select(scope=>scope.Column).DistinctBy(column=>column.Source.Id).ToArray();
        var storedQuery=exact?"query":$"Scope{page.Source.Id}(actor,parentId)";
        var result=new StringBuilder();
        result.AppendLine("    /// <summary>父ID不接受前导零、符号或空白。</summary>").AppendLine($"    private static long SharedR1ParentKey{page.Source.Id}(string? value){{if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||value!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");return id;}}")
            .AppendLine("    /// <summary>按实际物理父及来源项目复核，目标父保持固定范围和当前锁读。</summary>").AppendLine($"    private async Task VerifySharedR1Parent{page.Source.Id}Async({table.EntityType} entity,RuntimeActor actor,string? parentId,CancellationToken cancellationToken)").AppendLine("    {").AppendLine($"        _=SharedR1ParentKey{page.Source.Id}(parentId);")
            .Append(exact?"        var query=mapper.Query();\n"+FixedPageConditionCompilation.Apply(page,"PageCondition"+page.Source.Id,"verify_parent"):"")
            .AppendLine($"        var stored=await {storedQuery}.Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {table.EntityType}{{Id=row.Id,{string.Join(",",fields.Select(column=>column.Property+"=row."+column.Property))}}},cancellationToken);")
            .AppendLine($"        if(stored is null||{string.Join("||",fields.Select(column=>"stored."+column.Property+"!=entity."+column.Property))})throw new InvalidOperationException(\"共享R1实际父归属发生变化，未提交。\");")
            .AppendLine($"        await ValidateParents{page.Source.Id}Async(parentId,actor,cancellationToken);").AppendLine("    }");return result.ToString();
    }
}
