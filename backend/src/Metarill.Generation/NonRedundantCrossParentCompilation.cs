using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>稀疏跨表的父范围来自唯一固定链；读范围、锁、存储复核使用同一窄计划。</summary>
internal static class NonRedundantCrossParentCompilation
{
    /// <summary>仅查固定读取定义，避免写计划证明父表时递归调用自身。</summary>
    internal static (CrossTableReadModel Read,int Index)? Find(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        if(table.Source.IsRongyu!=0)return null;
        var levels=CrossTableReadCompilation.ForService(model,table,page).SelectMany(read=>read.Levels.Select((level,index)=>(Read:read,Index:index,Name:level.Prefix+"_id"))).Where(level=>level.Name==column.Source.ColumnName).ToArray();
        if(levels.Length>1)
        {
            // 只接受已完成整组证明的固定逻辑位；共享上级始终指向同一真实父表。
            var group=SharedCrossTableCompilation.ForSource(model,table,page,levels[0].Read.Source.Source.Id);if(group is null||levels.Any(level=>!group.Plans.Any(plan=>plan.Read.Source.Source.Id==level.Read.Source.Source.Id)))throw new InvalidOperationException("跨表父范围不能使用未证明的共享存储层位。");
        }
        return levels.Length==0?null:(levels[0].Read,levels[0].Index);
    }
    /// <summary>物理守卫提示不能把隐式逻辑祖先当成已经持久化的外键。</summary>
    internal static bool IsSlot(GenerationModel model,TableModel table,ColumnModel column)=>table.Source.IsRongyu==0&&table.Pages.Any(page=>Find(model,table,page,column) is not null);
    /// <summary>普通父外键继续原范围；本助手只接通被固定跨表链管理的父ID。</summary>
    internal static bool Managed(GenerationModel model,TableModel table,PageModel page,ColumnModel column)=>page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id)&&Find(model,table,page,column) is not null;
    /// <summary>存在逻辑父范围才增加私有参数，不改变普通生成源码。</summary>
    internal static bool Enabled(GenerationModel model,TableModel table,PageModel page)=>page.Scopes.Any(scope=>scope.Source=="parent"&&Find(model,table,page,scope.Column) is not null);
    /// <summary>已发布父层决定输入键协议，文本原字节用于读写及源实体比较。</summary>
    internal static bool IsText(GenerationModel model,TableModel table,PageModel page,ColumnModel column)=>
        Managed(model,table,page,column)&&Find(model,table,page,column) is {} path&&path.Read.Levels[path.Index].TextKey;

    /// <summary>验证稀疏来源与目标各自从同一发布祖先路径还原相同的文本键。</summary>
    internal static ColumnModel? CommonTextParent(GenerationModel model,TableModel source,PageModel sourcePage,
        TableModel target,PageModel targetPage)
    {
        if(source.Source.IsRongyu!=0||target.Source.IsRongyu!=0||source.IsGlobal||target.IsGlobal||
            source.Source.YzProjectId!=model.Catalog.YzProjectId||target.Source.YzProjectId!=model.Catalog.YzProjectId||
            sourcePage.Source.YzProjectId!=model.Catalog.YzProjectId||targetPage.Source.YzProjectId!=model.Catalog.YzProjectId)
            return null;
        var sourceFields=sourcePage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).Take(2).ToArray();
        var targetFields=targetPage.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).Take(2).ToArray();
        if(sourceFields is not [var sourceKey]||targetFields is not [var targetKey]||sourceKey.Type!="string?"||targetKey.Type!="string?")
            return null;
        if(Find(model,source,sourcePage,sourceKey) is not {} left||!left.Read.Levels[left.Index].TextKey||
            Find(model,target,targetPage,targetKey) is not {} right||!right.Read.Levels[right.Index].TextKey)
            return null;
        return SharedRedundantParentCompilation.Identity(new(left.Read,left.Index))==
            SharedRedundantParentCompilation.Identity(new(right.Read,right.Index))?targetKey:null;
    }

    /// <summary>混合链使用逐层键接口，即使当前选用的是整数父层。</summary>
    internal static string PathSuffix(CrossTableReadModel read,int index,string key)=>
        read.HasTextKeys ? $".PathByKeyAsync({index},{key}{(read.Levels[index].TextKey?"":".ToString(CultureInfo.InvariantCulture)")},cancellationToken)" :
            $".PathAsync({index},{key},cancellationToken)";

    /// <summary>共享文本参数从已授权的来源页验证祖先，在原写入事务锁定真实路径。</summary>
    internal static string ValidateTextParent(GenerationModel model,TableModel table,PageModel page,string argument)
    {
        var column=page.Scopes.Single(scope=>scope.Source=="parent").Column;
        var path=Find(model,table,page,column)??throw new InvalidOperationException("共享文本父缺少来源级联路径。");
        return $"        var targetParentId=LogicalParentId{page.Source.Id}({argument});\n"+
            $"        await CrossReader{page.Source.Id}_{path.Read.Source.Source.Id}(actor,true,false){PathSuffix(path.Read,path.Index,"targetParentId")};";
    }

    /// <summary>跨表导出需要验证独立物理父记录仍存在且同项目。纯逻辑父继续由路径 SQL 限定。</summary>
    internal static bool ChecksPhysicalParentOnExport(GenerationModel model,TableModel table,PageModel page)=>
        table.Source.IsRongyu==0&&page.Scopes.Any(scope=>scope.Source=="parent"&&!Managed(model,table,page,scope.Column))&&
        CrossTableWriteCompilation.ForService(model,table,page).Count>0;

    /// <summary>同父关系的重验既看物理父字段，也看各逻辑路径的实际存储层位。</summary>
    internal static IReadOnlyList<ColumnModel> Dependencies(GenerationModel model,TableModel table,PageModel page,
        IReadOnlyList<ColumnModel> scopes)
    {
        var paths=scopes.Select(column=>Find(model,table,page,column)).OfType<(CrossTableReadModel Read,int Index)>()
            .Select(path=>path.Read.Source.Source.Id).ToHashSet(StringComparer.Ordinal);
        if(paths.Count==0)return scopes;
        return scopes.Concat(CrossTableWriteCompilation.ForService(model,table,page)
            .Where(plan=>paths.Contains(plan.Read.Source.Source.Id)).SelectMany(plan=>plan.Levels.Select(level=>level.Id)))
            .DistinctBy(column=>column.Source.Id).ToArray();
    }

    /// <summary>原写事务明确选当前锁读，不能沿用默认快照。</summary>
    internal static string LockArgument(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)||FixedPageConditionCompilation.HasLogicalFields(page)||
        SharedRedundantParentCompilation.HasAccountPath(model,table,page)?",locking:true":"";
    /// <summary>关系写校验使用业务入口确定的来源页范围，写后仍以当前读复核实际稀疏路径。</summary>
    internal static bool UsesRelationParent(GenerationModel model,TableModel table,PageModel page)=>
        Enabled(model,table,page)&&table.Columns.Any(column=>column.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase");
    internal static string RelationParentArgument(GenerationModel model,TableModel table,PageModel page)=>
        UsesRelationParent(model,table,page)?", logicalRelationParentId: parentId":"";

    /// <summary>路径归一化前不先锁祖先；空批次在末尾单独验证真实父记录。</summary>
    internal static string ParentArgument(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?",includeManaged:false":"";
    /// <summary>关系省略时保持原始物理空位；真正提交的关系必须在归一化后满足原父范围。</summary>
    internal static string SubmittedCondition(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        if(!Managed(model,table,page,column))return "";var definition=Find(model,table,page,column)!.Value;
        var plan=CrossTableWriteCompilation.ForService(model,table,page).Single(plan=>plan.Read.Source.Source.Id==definition.Read.Source.Source.Id);
        return "("+CrossTableWriteRenderer.Trigger(plan,"false")+")&&";
    }
    /// <summary>写后或删除前复核一次真实来源及逻辑父链，不能用归一化临时实体替代实际数据库。</summary>
    internal static string VerifyCall(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?$"        await VerifyLogicalParent{page.Source.Id}Async(entity,actor,parentId,cancellationToken);\n":"";
    /// <summary>固定只编译父层位，其他显示关系不增加锁；全部类型与条件由原完整模型冻结。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page))return "";
        var parents=page.Scopes.Where(scope=>scope.Source=="parent"&&Managed(model,table,page,scope.Column)).Select(scope=>scope.Column).ToArray();var suffix=page.Source.Id;
        var textParent=parents.Any(column=>IsText(model,table,page,column));
        var fields=CrossTableWriteCompilation.ForService(model,table,page).SelectMany(plan=>plan.Levels.Select(level=>level.Id)).Concat(page.Scopes.Select(scope=>scope.Column)).DistinctBy(column=>column.Source.Id).ToArray();
        var result=new StringBuilder().AppendLine("    /// <summary>逻辑父范围只编译id及实际父层位，不锁无关显示路径。</summary>")
            .AppendLine($"    private static readonly NonRedundantReadPlan LogicalParentPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileCross({Literal(table.Source.Id)},new string[]{{\"id\"}},new string[]{{{string.Join(",",parents.Select(column=>Literal(column.Source.ColumnName)))}}}{NonRedundantCrossTableCompilation.BindingArgument(model,table,page)});")
            .AppendLine("    /// <summary>父ID保持原字符串接口，禁止宽松数值转换改变当前范围。</summary>")
            .AppendLine($"    private static {(textParent?"string":"long")} LogicalParentId{suffix}(string? parentId)")
            .AppendLine("    {").AppendLine(textParent
                ?"        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求非空文本parentId。\");return parentId;"
                :"        if(!long.TryParse(parentId,NumberStyles.None,CultureInfo.InvariantCulture,out var key)||key<=0||parentId!=key.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");return key;").AppendLine("    }")
            .AppendLine("    /// <summary>来源及目标完整路径同项目且可见，写用途加入原事务的当前锁读。</summary>")
            .AppendLine($"    private ISelect<{table.EntityType}> LogicalParentScope{suffix}(ISelect<{table.EntityType}> query,RuntimeActor actor,string? parentId,bool locking)")
            .AppendLine("    {").AppendLine($"        _=LogicalParentId{suffix}(parentId);");
        foreach(var column in parents)
        {
            var value=column.Type=="string?"?"parentId!":"long.Parse(parentId!,NumberStyles.None,CultureInfo.InvariantCulture)";
            result.AppendLine($"        query=NonRedundantCrossTableScopeSql.Apply(query,session,{NonRedundantCrossTableCompilation.BoundPlan(model,table,page,"LogicalParentPlan"+suffix)},{Literal(column.Source.ColumnName)},{Literal("mt_parent_"+Array.IndexOf(parents,column))},{value},actor.ProjectId,locking);");
        }
        result.AppendLine("        return query;").AppendLine("    }")
            .AppendLine("    /// <summary>保留物理实体与字段存在性；钩子／数据库改写父链或稀疏位时在通知前拒绝整笔提交。</summary>")
            .AppendLine($"    private async Task VerifyLogicalParent{suffix}Async({table.EntityType} entity,RuntimeActor actor,string? parentId,CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine($"        var stored=await LogicalParentScope{suffix}(mapper.Query(),actor,parentId,true).Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {table.EntityType}{{{string.Join(",",fields.Select(column=>column.Property+"=row."+column.Property))}}},cancellationToken);")
            .AppendLine($"        if(stored is null||{string.Join("||",fields.Select(column=>"stored."+column.Property+"!=entity."+column.Property))})throw new InvalidOperationException(\"跨表逻辑父归属或实际稀疏存储发生变化，未提交。\");").AppendLine("    }");
        return result.ToString();
    }
    /// <summary>元数据只作字面量，不允许改变生成源码结构。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
