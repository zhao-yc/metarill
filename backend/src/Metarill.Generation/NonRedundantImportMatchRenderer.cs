using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>覆盖导入的内部读取用途；逻辑匹配和解析上下文不扩大页面VO。</summary>
internal sealed record NonRedundantImportMatchPlan(ColumnModel Key,IReadOnlyList<ColumnModel> Context,IReadOnlyList<string> Outputs,IReadOnlyList<string> Dependencies);

/// <summary>R0跨表覆盖按已冻结逻辑值当前读，不用可能为空的物理祖先槽匹配。</summary>
internal static class NonRedundantImportMatchRenderer
{
    /// <summary>仅替换已有R0跨表覆盖路径；其它存储和追加导入保持原实现。</summary>
    internal static NonRedundantImportMatchPlan? Resolve(GenerationModel model,TableModel table,PageModel page,ImportDependencyPlan dependency)
    {
        if(table.Source.IsRongyu!=0||ImportRenderer.Mode(page)!="OverWriteByFirstColumn")return null;
        var crosses=CrossTableWriteCompilation.ForService(model,table,page);if(crosses.Count==0)return null;
        var columns=ImportRenderer.Columns(page);if(columns.Count==0)return null;
        var key=table.Columns.First(columns.Contains);var context=dependency.ContextValues.ToArray();
        // 普通物理首列及物理上下文仍走原单次查询；只为需要逻辑恢复的键或解析依赖建立新计划。
        var logicalIds=crosses.SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.Id).ToHashSet(StringComparer.Ordinal);
        if(!logicalIds.Contains(key.Source.Id)&&!context.Any(column=>logicalIds.Contains(column.Source.Id)||!NonRedundantReadCompilation.Physical(model,table,column)))return null;
        var outputs=new[]{"id",key.Source.ColumnName}.Concat(context.Select(column=>column.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // 覆盖匹配先按 import 权限和当前值找记录，隐藏范围列只参与条件。
        var exact=ExactDataManageCompilation.Resolve(model,table,page);
        var dependencies=FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>scope.Column.Source.ColumnName)
            .Concat(page.Condition?.Syntax.Fields??[])
            .Concat(exact is null?[]:new[]{exact.Column.Source.ColumnName})
            .Concat(outputs).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var compiled=NonRedundantReads.CompileWithCrossTables(model.Catalog,table.Source.Id,outputs,NonRedundantCrossTableCompilation.Bindings(model,table,page),dependencies,deferCascadeNames:true);
        if(key.IsJsonArray||compiled.OutputTypes[key.Source.ColumnName] is "json")throw new InvalidOperationException("覆盖匹配字段必须为已支持的单值列。");
        return new(key,context,outputs,dependencies);
    }
    /// <summary>独立的最小计划只输出主键、匹配值及解析依赖，不扩张普通列表读取。</summary>
    internal static string Declaration(GenerationModel model,TableModel table,PageModel page,NonRedundantImportMatchPlan? plan)=>plan is null?"":
        "    /// <summary>覆盖导入只使用服务器冻结的逻辑匹配及上下文列。</summary>\n"+
        $"    private static readonly NonRedundantReadPlan ImportMatchPlan{page.Source.Id}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileCross({L(table.Source.Id)},{Fields(plan.Outputs)},{Fields(plan.Dependencies)}{NonRedundantCrossTableCompilation.BindingArgument(model,table,page)});\n"+
        (plan.Context.Count==0?"":"    /// <summary>解析只读逻辑上下文，不调用手写Entity构造函数，也不补交省略字段。</summary>\n"+
        $"    private sealed record ImportExistingContext{page.Source.Id}("+string.Join(",",plan.Context.Select(column=>$"{column.Type} {column.Property}"))+");\n"+
        $"    private static ImportExistingContext{page.Source.Id} ImportContext{page.Source.Id}(IReadOnlyDictionary<string,object?> row)=>new("+string.Join(",",plan.Context.Select(column=>$"NonRedundantReads.Value<{column.Type}>(row,{L(column.Source.ColumnName)})"))+");\n");
    /// <summary>逻辑当前读负责歧义判断，再按主键锁定并核验物理范围。标准更新负责完整读写。</summary>
    internal static string Match(GenerationModel model,TableModel table,PageModel page,NonRedundantImportMatchPlan plan)=>$$"""
                var importFilters=ReadFilters{{page.Source.Id}}(new PageRequest{ParentId=request.ParentId},actor{{ExactDataManageCompilation.Argument(page,"\"import\"")}});
                importFilters.Add(new({{L(plan.Key.Source.ColumnName)}},FixedReadComparison.Equal,key));
                var importMatches=await new NonRedundantReader(session).ImportMatchesAsync(ImportMatchPlan{{page.Source.Id}},importFilters,cancellationToken,projectId:actor.ProjectId{{FixedPageConditionCompilation.ReadArgument(page,true)}});
                if(importMatches.Count>1)throw new InvalidOperationException("覆盖匹配字段重名，不能确定目标记录。");
                var existing=new List<{{table.EntityType}}>();
                if(importMatches.Count==1){
                    var matchedId=NonRedundantReads.Value<long>(importMatches[0],"id");
                    var stored=await Scope{{page.Source.Id}}(actor,request.ParentId{{ExactDataManageCompilation.Argument(page,"\"import\"")}}{{NonRedundantCrossParentCompilation.LockArgument(model,table,page)}}).Where(entity=>entity.Id==matchedId).ForUpdate().ToOneAsync(entity => new {{table.EntityType}} { Id = entity.Id }, cancellationToken);
                    if(stored is null)throw new InvalidOperationException("覆盖匹配记录已发生变化，请重新导入。");
                    existing.Add(stored);
                }

""";
    /// <summary>只供行解析的私有上下文记录，不构造或修改持久实体，也不向更新DTO补交省略字段。</summary>
    internal static string Context(PageModel page)=>$"                if(existing.Count==1)importExisting=ImportContext{page.Source.Id}(importMatches[0]);\n";
    /// <summary>元数据值始终作为字符串字面量，不拼成任意代码。</summary>
    private static string L(string value)=>JsonSerializer.Serialize(value);
    /// <summary>固定列集合在生成期去重并以字面量输出。</summary>
    private static string Fields(IEnumerable<string> fields)=>"new string[]{"+string.Join(",",fields.Select(L))+"}";
}
