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
    /// <summary>原写事务明确选当前锁读，不能沿用默认快照。</summary>
    internal static string LockArgument(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?",locking:true":"";
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
        var fields=CrossTableWriteCompilation.ForService(model,table,page).SelectMany(plan=>plan.Levels.Select(level=>level.Id)).Concat(page.Scopes.Select(scope=>scope.Column)).DistinctBy(column=>column.Source.Id).ToArray();
        var result=new StringBuilder().AppendLine("    /// <summary>逻辑父范围只编译id及实际父层位，不锁无关显示路径。</summary>")
            .AppendLine($"    private static readonly NonRedundantReadPlan LogicalParentPlan{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileCross({Literal(table.Source.Id)},new string[]{{\"id\"}},new string[]{{{string.Join(",",parents.Select(column=>Literal(column.Source.ColumnName)))}}}{NonRedundantCrossTableCompilation.BindingArgument(model,table,page)});")
            .AppendLine("    /// <summary>父ID保持原字符串接口，禁止宽松数值转换改变当前范围。</summary>")
            .AppendLine($"    private static long LogicalParentId{suffix}(string? parentId)")
            .AppendLine("    {").AppendLine("        if(!long.TryParse(parentId,NumberStyles.None,CultureInfo.InvariantCulture,out var key)||key<=0||parentId!=key.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");return key;").AppendLine("    }")
            .AppendLine("    /// <summary>来源及目标完整路径同项目且可见，写用途加入原事务的当前锁读。</summary>")
            .AppendLine($"    private ISelect<{table.Name}> LogicalParentScope{suffix}(ISelect<{table.Name}> query,RuntimeActor actor,string? parentId,bool locking)")
            .AppendLine("    {").AppendLine($"        _=LogicalParentId{suffix}(parentId);");
        foreach(var column in parents)
        {
            var value=column.Type=="string?"?"parentId!":"long.Parse(parentId!,NumberStyles.None,CultureInfo.InvariantCulture)";
            result.AppendLine($"        query=NonRedundantCrossTableScopeSql.Apply(query,session,LogicalParentPlan{suffix},{Literal(column.Source.ColumnName)},{Literal("mt_parent_"+Array.IndexOf(parents,column))},{value},actor.ProjectId,locking);");
        }
        result.AppendLine("        return query;").AppendLine("    }")
            .AppendLine("    /// <summary>保留物理实体与字段存在性；钩子／数据库改写父链或稀疏位时在通知前拒绝整笔提交。</summary>")
            .AppendLine($"    private async Task VerifyLogicalParent{suffix}Async({table.Name} entity,RuntimeActor actor,string? parentId,CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine($"        var stored=await LogicalParentScope{suffix}(mapper.Query(),actor,parentId,true).Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {table.Name}{{{string.Join(",",fields.Select(column=>column.Property+"=row."+column.Property))}}},cancellationToken);")
            .AppendLine($"        if(stored is null||{string.Join("||",fields.Select(column=>"stored."+column.Property+"!=entity."+column.Property))})throw new InvalidOperationException(\"跨表逻辑父归属或实际稀疏存储发生变化，未提交。\");").AppendLine("    }");
        return result.ToString();
    }
    /// <summary>元数据只作字面量，不允许改变生成源码结构。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
