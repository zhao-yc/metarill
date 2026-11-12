using System.Text.Json;
namespace Metarill.Generation;

/// <summary>完整包按跨表实际用途证明；独立R0路径Excel已接线，未验复合用途不能借隐藏控件绕过。</summary>
internal static class NonRedundantCrossTableSubset
{
    /// <summary>坏定义也不能借隐藏控件绕过完整包审计。</summary>
    internal static bool Declared(ColumnModel source)
    {
        if(source.Source.ControlKind!="CascadeTree")return false;
        try{using var json=JsonDocument.Parse(source.Source.ControlDetail);return !json.RootElement.TryGetProperty("sourceMode",out var mode)||mode.ValueKind!=JsonValueKind.String||mode.GetString()!="selfTree";}
        catch(Exception invalid)when(invalid is JsonException or InvalidOperationException){return true;}
    }
    /// <summary>开放固定R0来源与已证明目标的完整读写链，元数据及原按钮保持原样。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(table.Source.IsRongyu!=0||source.Source.ControlKind!="CascadeTree"||CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is null)return false;
        return Reason(model,table,page) is null;
    }
    /// <summary>所有隐藏同伴与实际导入导出列共同检查，不能只验证可见末级。</summary>
    internal static string? Reason(GenerationModel model,TableModel table,PageModel page)
    {
        try
        {
            var companions=table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").ToArray();
            var trees=companions.Where(Declared).ToArray();
            if(table.Source.IsRongyu!=0||trees.Length==0)return "非冗余跨表完整包要求明确的R0来源。";
            // 自树同伴使用真实自树计划，不当成跨表解析失败；Excel组合仍在下方单独证明。
            if(companions.Where(tree=>!Declared(tree)).Any(tree=>!NonRedundantSelfTreeSubset.FixedTarget(model,table,page,tree)))return "混合自树同伴缺少独立目标的完整读写证明。";
            var plans=trees.Select(tree=>CrossTableWriteCompilation.Resolve(model,table,page,tree)).ToArray();
            if(plans.Any(plan=>plan.Definition is null))return "跨表同页完整写入证明缺失，不能忽略隐藏或混合级联。";
            // 原组件按前缀转换属性，原详情还读取显式属性；两者必须与同一后端JSON字段一致。
            foreach(var plan in plans.Select(plan=>plan.Definition!))if(CrossTableFrontendProof.Reason(plan) is{} mismatch)return mismatch;
            _=NonRedundantReadCompilation.ForPage(model,table,page);_=NonRedundantSelfTreeSubset.ParentTables(model,table,page);
            var importColumns=ImportRenderer.Enabled(page)?ImportRenderer.Columns(page):[];
            var imports=importColumns.Any(column=>trees.Any(tree=>tree.Source.Id==column.Source.Id));
            var exports=plans.SelectMany(plan=>plan.Definition!.Levels).Any(level=>ExportRenderer.IncludesColumn(page,level.Id)||ExportRenderer.IncludesColumn(page,level.Name));
            // 同一固定链的逻辑父范围支持追加、首列覆盖及列表和详情路径导出。
            var parentColumns=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
            var hasParent=parentColumns.Length>0;
            // 前面的全部写计划已核对来源范围。共享组的独立物理匹配列由导入和导出各自按动作应用。
            var independent=model.Catalog.DatabaseProvider=="mysql"&&!table.IsGlobal&&
                companions.All(Declared)&&plans.All(plan=>
                    plan.Definition!.Levels.All(level=>level.Read.Target.Source.Id!=table.Source.Id)&&
                    plan.Definition.Levels.Select(level=>level.Read.Target.Source.Id).Distinct(StringComparer.Ordinal).Count()==plan.Definition.Levels.Count);
            var pathSupported=independent&&(!hasParent||SupportsScopedPath(plans,parentColumns));
            var appendImport=ImportRenderer.Mode(page) is "" or "OnlyNew";
            var scopedOverwrite=hasParent&&pathSupported&&(imports||exports)&&SupportsScopedOverwrite(model,table,page,importColumns);
            if(imports&&(!pathSupported||!appendImport&&!(hasParent?scopedOverwrite:
                SupportsOverwrite(model,table,page,plans.Select(plan=>plan.Definition!).ToArray(),importColumns))))
                return CrossTableExcelRenderer.ImportAcceptancePending;
            // 末级控件同时是固定父键时不属于导入列，合并发布仍须校验页面的实际导入模式。
            if(exports&&(!pathSupported||hasParent&&(ExportRenderer.Mode(page) is not ("" or "list" or "edit")||
                ImportRenderer.Enabled(page)&&!appendImport&&!scopedOverwrite)))
                return CrossTableExcelRenderer.ExportAcceptancePending;
            return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return "非冗余跨表完整用途未通过："+error.Message;}
    }

    /// <summary>仅开放已验收的单条三层链；父ID必须是该链的真实逻辑层位。</summary>
    private static bool SupportsScopedPath(IReadOnlyList<CrossTableWriteResult> plans,IReadOnlyList<ColumnModel> parents)
    {
        if(plans is not [{Definition: {Levels: [var root,var middle,var leaf]}}]||parents is not [var parent]||
            middle.Read.Parent?.Type is not ("string?" or "long?")||leaf.Read.Parent?.Type is not ("string?" or "long?"))
            return false;
        var levels=new[]{root,middle,leaf};
        // 来源槽与目标父外键分别允许字符串或 bigint，父范围仍须来自同一条逻辑链。
        return levels.Any(level => level.Id.Source.Id == parent.Source.Id) &&
            levels.All(level => (level.Id.Type is "string?" or "long?") &&
                level.Read.Target.Source.IsRongyu == 1 && !level.Read.Target.IsGlobal && !level.Read.DerivedName);
    }

    /// <summary>逻辑父范围的覆盖使用表级首个可导入的物理文本字段，不扩大到关系键或额外解析依赖。</summary>
    private static bool SupportsScopedOverwrite(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<ColumnModel> columns)
    {
        if(ImportRenderer.Mode(page)!="OverWriteByFirstColumn"||columns.Count==0)return false;
        var key=table.Columns.First(columns.Contains);
        return key.Type=="string?"&&key.Source.ControlKind is "" or "Text"&&
            NonRedundantReadCompilation.Physical(model,table,key)&&!ImportDependencyCompilation.ForPage(model,table,page).Contextual;
    }

    /// <summary>新开放的逻辑父范围路径覆盖可给出固定重名提示；原无父及纯标量路径沿各自协议。</summary>
    internal static bool ReportsScopedOverwriteAmbiguity(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<ColumnModel> columns)=>
        table.Source.IsRongyu==0&&page.Scopes.Any(scope=>scope.Source=="parent")&&
        table.Columns.Any(column=>Declared(column)&&(columns.Contains(column)||ExportRenderer.IncludesColumn(page,column)))&&
        SupportsScopedOverwrite(model,table,page,columns)&&Reason(model,table,page) is null;

    /// <summary>公开覆盖只接受已验收的末级单值键、项目范围和独立或共享级联，不扩张到逻辑祖先或附加上下文。</summary>
    private static bool SupportsOverwrite(GenerationModel model,TableModel table,PageModel page,IReadOnlyList<CrossTableWriteModel> plans,IReadOnlyList<ColumnModel> columns)
    {
        if(ImportRenderer.Mode(page)!="OverWriteByFirstColumn"||page.Condition is not null||
            plans.Count is <1 or >2||plans.Any(plan=>plan.Levels.Count is <2 or >3||!columns.Any(column=>column.Source.Id==plan.Read.Source.Source.Id)))
            return false;
        if(plans.Count==2&&!SharedCrossTableCompilation.ForPage(model,table,page).Groups.Any(group=>group.Plans.Count==2))
            return false;
        var key=table.Columns.First(columns.Contains);
        if(key.Type is not ("string?" or "long?")||plans.Count(plan=>plan.Levels[^1].Id.Source.Id==key.Source.Id)!=1)
            return false;
        var dependency=ImportDependencyCompilation.ForPage(model,table,page);
        return NonRedundantImportMatchRenderer.Resolve(model,table,page,dependency) is {Context.Count:0};
    }
}
