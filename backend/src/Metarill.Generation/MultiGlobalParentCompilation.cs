using System.Text.Json;

namespace Metarill.Generation;

/// <summary>生成的多值父校验只读实际级联层位的发布页，并沿用调用端的读快照或写事务。</summary>
internal static class MultiGlobalParentCompilation
{
    internal static string Declaration(GenerationModel model,PageModel target,string name)=>
        $"    private static readonly Metarill.Data.Queries.NonRedundantReadPlan {name}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileMultiGlobalParent({Literal(target.Source.ConfigTableId)},{Literal(target.Source.Id)});\n";

    internal static string Check(string plan,string key,string currentRead)=>
        $"        if((await new Metarill.Data.Queries.NonRedundantReader(session).KeyRowsAsync({plan}.BindAccount(actor),[],[{key}],cancellationToken,actor.ProjectId,currentRead:{currentRead})).Count!=1)throw new InvalidOperationException(\"全局父记录不存在、已删除或不在固定父范围。\");";

    /// <summary>独立物理来源父必须实际参与同父多值关系，且同一来源页的关系只能证明同一个目标父范围。</summary>
    internal static PageModel? PhysicalSourceTarget(GenerationModel model,TableModel table,PageModel page)
    {
        if(page.Scopes.Count(scope=>scope.Source=="parent")!=1||NonRedundantCrossParentCompilation.Enabled(model,table,page))return null;
        var targets=MultiSelectCompilation.ForService(model,table,page).Where(multi=>multi.GlobalParent&&multi.SameParent)
            .Select(multi=>multi.TargetPage).Concat(DatabaseCheckboxCompilation.ForService(model,table,page)
                .Where(checkbox=>checkbox.GlobalParent).Select(checkbox=>checkbox.LogicalTargetPage!))
            .DistinctBy(target=>Metarill.Data.Queries.NonRedundantReads.MultiGlobalParentPageId(model.Catalog,
                target.Source.ConfigTableId,target.Source.Id),StringComparer.Ordinal).ToArray();
        if(targets.Length>1)throw new NotSupportedException("同一来源页的全局父范围须绑定同一个实际父页面。");
        return targets.SingleOrDefault();
    }

    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
