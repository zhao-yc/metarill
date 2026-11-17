namespace Metarill.Generation;

/// <summary>冗余重复来源按层角色证明真实父字段和缓存槽位，不因同表或同ID合并角色。</summary>
internal static class RedundantRepeatedCrossSourceCompilation
{
    /// <summary>仅接通原物理名称链；父键与受管槽位重叠必须另作聚合协调，不能在归一化时覆盖业务意图。</summary>
    internal static void Require(GenerationModel model,TableModel table,PageModel page,CrossTableReadModel plan,IReadOnlySet<string> managed)
    {
        if(table.IsGlobal||plan.Levels.Any(level=>level.Target.IsGlobal||level.Target.Source.IsRongyu!=1||level.DerivedName))throw new InvalidOperationException("冗余重复来源要求普通同项目R1物理名称链。");
        if (!CrossTableFrontendProof.KeepsStorageDefinition(model, page, plan.Source))
            throw new InvalidOperationException("冗余重复来源页级角色覆盖尚未完成统一缓存证明。");
        foreach(var role in plan.Levels.Where(level=>level.Target.Source.Id==table.Source.Id))
        {
            if(managed.Contains(role.Name.Source.ColumnName)||role.Parent is{} parent&&managed.Contains(parent.Source.ColumnName))throw new InvalidOperationException("冗余重复来源名称／真实父键与受管槽位重叠，需要聚合协调。");
        }
        // 固定父等值由父路径校验器绑定。其余条件仍不能用归一化会改写的受管槽位。
        if(page.Condition?.Syntax.Fields.Any(managed.Contains)==true)throw new InvalidOperationException("冗余重复来源缓存槽位不能兼任来源常量条件。");
    }
}
