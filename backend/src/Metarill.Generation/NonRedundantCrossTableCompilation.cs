using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>接通固定R0／R1目标及来源单层参与的非冗余跨表生成，逻辑读值与稀疏写值明确分界。</summary>
internal static class NonRedundantCrossTableCompilation
{
    /// <summary>物理ID位不能冒充逻辑父范围；尚未接通的组合在整个生成批次写文件前拒绝。</summary>
    internal static void Require(GenerationModel model,TableModel table,PageModel page,CrossTableReadModel plan)
    {
        if(table.Source.IsRongyu!=0||table.IsGlobal)throw new InvalidOperationException("非冗余跨表要求普通项目来源。");
        foreach(var level in plan.Levels.Where(level=>level.Target.IsGlobal))GlobalCrossTableTarget.Require(model.Catalog,level.Target.Source,level.Page.Source);
        var shared=SharedCrossTableCompilation.ForSource(model,table,page,plan.Source.Source.Id);var storage=shared is null?CrossTableSparseSelection.Compile(table.Source,plan.Source.Source.Id,model.Catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Source.Id)):SharedCrossTableSparseSelection.Compile(table.Source,plan.Source.Source.Id,model.Catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Source.Id)).BranchSelection(plan.Source.Source.Id);
        if(!storage.Slots.Select(slot=>slot.ColumnName).SequenceEqual(plan.Levels.Select(level=>level.Prefix+"_id"),StringComparer.Ordinal))throw new InvalidOperationException("页级跨表定义不能改变固定存储层位。");
        var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==plan.Source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();
        if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(plan.Source.Source.ControlDetail)))throw new InvalidOperationException("非冗余跨表页级目标覆盖仍待独立存储证明。");
        var slots=storage.Slots.Select(slot=>slot.ColumnName).ToHashSet(StringComparer.Ordinal);
        if(page.Condition?.Syntax.Fields.Any(slots.Contains)==true||ExactDataManageCompilation.Declared(page))throw new InvalidOperationException("非冗余跨表来源逻辑条件／精确权限仍待独立接线。");
        _=NonRedundantSelfTreeSubset.ParentTables(model,table,page);
    }
    /// <summary>从完整生成模型冻结真实目标页的常量条件，不从精简关系目录猜测条件。</summary>
    internal static IReadOnlyList<NonRedundantCrossTableScope> Bindings(GenerationModel model,TableModel table,PageModel page)=>CrossTableWriteCompilation.ForService(model,table,page)
        .Select(plan=>new NonRedundantCrossTableScope(plan.Read.Source.Source.Id,plan.Levels.Select(level=>new NonRedundantCrossTableTargetScope(level.Read.Page.Source.Id,level.Read.Page.Condition is{} condition?FixedConditionSqlPlan.Compile(condition.Syntax,condition.Types):null)).ToArray())).ToArray();
    /// <summary>直接内联不可变条件表达式，避免ReadPlan先于目标条件字段初始化而丢失范围。</summary>
    internal static string BindingArgument(GenerationModel model,TableModel table,PageModel page)
    {
        var plans=CrossTableWriteCompilation.ForService(model,table,page);if(plans.Count==0)return "";
        return ",new Metarill.Data.Queries.NonRedundantCrossTableScope[]{"+string.Join(",",plans.Select(plan=>"new("+Literal(plan.Read.Source.Source.Id)+",new Metarill.Data.Queries.NonRedundantCrossTableTargetScope[]{"+string.Join(",",plan.Levels.Select(level=>"new("+Literal(level.Read.Page.Source.Id)+","+Condition(level.Read.Page)+")"))+"})"))+"}";
    }
    /// <summary>类型和字段来自已验证的常量计划，所有元数据文本只作为字符串字面量。</summary>
    private static string Condition(PageModel page)=>page.Condition is not{} condition?"null":"Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile("+Literal(condition.Definition)+",new string[]{"+string.Join(",",condition.Syntax.Fields.Select(Literal))+"}),new Dictionary<string,string>{"+string.Join(",",condition.Types.Select(pair=>"["+Literal(pair.Key)+"]="+Literal(pair.Value)))+"})";
    /// <summary>完整逻辑ID和实时名称直接来自同一SQL行，不新增目标逐行查询。</summary>
    internal static string VoAssignments(GenerationModel model,TableModel table,PageModel page)=>string.Concat(CrossTableDisplayRenderer.ForPage(model,table,page).Select(plan=>"        "+plan.Read.Source.Property+"DisplayText=string.Join("+Literal(plan.Read.Joiner)+",new[]{"+string.Join(",",plan.Levels.Select(level=>"NonRedundantReads.Value<string?>(row,"+Literal(level.Name.Source.ColumnName)+")"))+"}.Where(text=>!string.IsNullOrWhiteSpace(text))),\n"));
    /// <summary>原节点负载只选最深级；逻辑ID表达式负责分页前匹配所有祖先来源。</summary>
    internal static string Filter(CrossTableWriteModel plan,PageModel page)
    {
        var source=plan.Read.Source;var result=new StringBuilder().AppendLine($"        if(request.Filters.TryGetValue({Literal(source.JsonName)},out var crossValue{source.Source.Id})&&!(crossValue{source.Source.Id}.ValueKind==JsonValueKind.Array&&crossValue{source.Source.Id}.GetArrayLength()==0))")
            .AppendLine("        {").AppendLine($"            var selected=CrossFilter{page.Source.Id}_{source.Source.Id}.Parse(crossValue{source.Source.Id});if(selected is not null)switch(selected.Level)").AppendLine("            {");
        for(var index=0;index<plan.Levels.Count;index++){var column=plan.Levels[index].Id;var value=column.Type=="string?"?"selected.Id":"long.Parse(selected.Id,NumberStyles.None,CultureInfo.InvariantCulture)";result.AppendLine($"                case {index}:result.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{value}));break;");}
        return result.AppendLine("                default:throw new InvalidOperationException(\"跨表筛选层位无效。\");").AppendLine("            }").AppendLine("        }").ToString();
    }
    /// <summary>统一在逻辑校验／范围之后，写守卫／DML之前编码；未提交关联时保留原始存储。</summary>
    internal static string PackCall(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsRongyu!=0?"":string.Concat(CrossTableWriteCompilation.ForService(model,table,page).Where(plan=>SharedCrossTableCompilation.ForSource(model,table,page,plan.Read.Source.Source.Id) is null).Select(plan=>$"        PackCross{page.Source.Id}_{plan.Read.Source.Source.Id}(entity,submitted);\n"))+SharedCrossTableSparseRenderer.PackCall(model,table,page);
    /// <summary>复制只接受可唯一恢复的旧存储，不能把多位非空历史数据静默选成最深位。</summary>
    internal static string ValidateCopy(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsRongyu!=0?"":string.Concat(CrossTableWriteCompilation.ForService(model,table,page).Where(plan=>SharedCrossTableCompilation.ForSource(model,table,page,plan.Read.Source.Source.Id) is null).Select(plan=>$"        _=CrossStorage{page.Source.Id}_{plan.Read.Source.Source.Id}.ReadStored(new Dictionary<string,object?>{{{string.Join(",",plan.Levels.Select(level=>"["+Literal(level.Id.Source.ColumnName)+"]=source."+level.Id.Property))}}});\n"))+SharedCrossTableSparseRenderer.CopyValidation(model,table,page);
    /// <summary>仅局部提交ID时恢复原逻辑值再合并请求，显式null可退到原祖先，全部清空不依赖旧目标。</summary>
    internal static string HydrateCall(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.Source.IsRongyu!=0)return "";var result=new StringBuilder();
        foreach(var plan in CrossTableWriteCompilation.ForService(model,table,page).Where(plan=>SharedCrossTableCompilation.ForSource(model,table,page,plan.Read.Source.Source.Id) is null))
        {
            var available=plan.Levels.Where(level=>page.Writable.Any(column=>column.Source.Id==level.Id.Source.Id)).ToArray();if(available.Length==0)continue;
            var any=string.Join("||",available.Select(level=>"request."+level.Id.Property+".IsSpecified"));var all=available.Length==plan.Levels.Count?string.Join("&&",available.Select(level=>"request."+level.Id.Property+".IsSpecified")):"false";
            result.AppendLine($"        if(({any})&&!({all}))await HydrateStoredCross{page.Source.Id}_{plan.Read.Source.Source.Id}Async(entity,actor,cancellationToken);");
        }
        return result.Append(SharedCrossTableSparseRenderer.HydrateCall(model,table,page)).ToString();
    }
    /// <summary>编码只接受归一化后的完整路径，实体与观察器的字段都反映真实落库值，名称不持久化。</summary>
    internal static string PackService(GenerationModel model,TableModel table,PageModel page,CrossTableWriteModel plan)
    {
        if(table.Source.IsRongyu!=0)return "";var suffix=page.Source.Id+"_"+plan.Read.Source.Source.Id;
        var result=new StringBuilder().AppendLine($"    private static readonly Metarill.Core.Metadata.CrossTableSparseSelection CrossStorage{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileSelection({Literal(table.Source.Id)},{Literal(plan.Read.Source.Source.Id)});")
            .AppendLine("    /// <summary>省略保持稀疏值；真实提交以最深ID编码，祖先ID及派生名称不缓存。</summary>")
            .AppendLine($"    private static void PackCross{suffix}({table.EntityType} entity,ISet<string> submitted)")
            .AppendLine("    {").AppendLine($"        if(!({CrossTableWriteRenderer.Trigger(plan,"false")}))return;var path=new List<long>();");
        foreach(var level in plan.Levels)result.AppendLine($"        if(!string.IsNullOrWhiteSpace(Convert.ToString(entity.{level.Id.Property},CultureInfo.InvariantCulture)))path.Add(long.Parse(Convert.ToString(entity.{level.Id.Property},CultureInfo.InvariantCulture)!,NumberStyles.None,CultureInfo.InvariantCulture));");
        result.AppendLine($"        var packed=CrossStorage{suffix}.PackValidatedPath(path);");
        foreach(var level in plan.Levels)result.AppendLine($"        entity.{level.Id.Property}=({level.Id.Type})packed[{Literal(level.Id.Source.ColumnName)}];submitted.Add({Literal(level.Id.Source.ColumnName)});entity.{level.Name.Property}=null;submitted.Remove({Literal(level.Name.Source.ColumnName)});");
        result.AppendLine("    }")
            .AppendLine("    /// <summary>先锁原节点路径再合并局部请求，不用共享锁升级；省略整项时完全跳过。</summary>")
            .AppendLine($"    private async Task HydrateStoredCross{suffix}Async({table.EntityType} entity,RuntimeActor actor,CancellationToken cancellationToken)")
            .AppendLine("    {")
            .AppendLine($"        var selected=CrossStorage{suffix}.ReadStored(new Dictionary<string,object?>{{{string.Join(",",plan.Levels.Select(level=>"["+Literal(level.Id.Source.ColumnName)+"]=entity."+level.Id.Property))}}});if(selected is null)return;")
            .AppendLine($"        var path=await CrossReader{suffix}(actor,true,false).PathAsync(selected.Level,selected.Id,cancellationToken);");
        for(var index=0;index<plan.Levels.Count;index++){var level=plan.Levels[index];var value=level.Id.Type=="string?"?$"path.Path[{index}].Id":$"long.Parse(path.Path[{index}].Id,CultureInfo.InvariantCulture)";result.AppendLine($"        entity.{level.Id.Property}=path.Path.Count>{index}?{value}:null;entity.{level.Name.Property}=path.Path.Count>{index}?path.Path[{index}].Name:null;");}
        return result.AppendLine("    }").ToString();
    }
    /// <summary>禁止把配置字符串解释为C#代码。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
