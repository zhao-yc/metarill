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
            var plans=trees.Select(tree=>CrossTableWriteCompilation.Resolve(model,table,page,tree).Definition)
                .OfType<CrossTableWriteModel>().ToArray();
            if(plans.Length!=trees.Length)return "跨表同页完整写入证明缺失，不能忽略隐藏或混合级联。";
            // 原组件按前缀转换属性，原详情还读取显式属性；两者必须与同一后端JSON字段一致。
            foreach(var plan in plans)if(CrossTableFrontendProof.Reason(model,page,plan) is{} mismatch)return mismatch;
            _=NonRedundantReadCompilation.ForPage(model,table,page);var parentTables=NonRedundantSelfTreeSubset.ParentTables(model,table,page);
            var importColumns=ImportRenderer.Enabled(page)?ImportRenderer.Columns(page):[];
            var imports=importColumns.Any(column=>trees.Any(tree=>tree.Source.Id==column.Source.Id));
            var importsFixedPath=ImportRenderer.Enabled(page)&&CrossTableExcelRenderer.HasFixedPath(page);
            var exports=plans.SelectMany(plan=>plan.Levels).Any(level=>ExportRenderer.IncludesColumn(page,level.Id)||ExportRenderer.IncludesColumn(page,level.Name));
            // 来源物理归属与路径内逻辑父各自用正式计划校验。额外的物理等值仍取交集。
            var parentColumns=page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column).ToArray();
            var pathSupported=model.Catalog.DatabaseProvider=="mysql"&&!table.IsGlobal&&
                SupportsScopedPath(plans,parentColumns,parentTables);
            if((imports||importsFixedPath)&&(!pathSupported||!SupportsImport(model,table,page,plans,importColumns)))
                return CrossTableExcelRenderer.ImportAcceptancePending;
            if(exports&&(!pathSupported||ExportRenderer.Mode(page) is not ("" or "list" or "edit")))
                return CrossTableExcelRenderer.ExportAcceptancePending;
            return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return "非冗余跨表完整用途未通过："+error.Message;}
    }

    /// <summary>同一父参数只接受已证明的同父表，以及至多一个需要反查的逻辑路径层位。</summary>
    private static bool SupportsScopedPath(IReadOnlyList<CrossTableWriteModel> plans,IReadOnlyList<ColumnModel> parents,
        IReadOnlyList<TableModel> parentTables)
    {
        if(parents.Count==0)return true;
        if(parentTables.Count!=1)return false;
        var slots=plans.SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.Id).ToHashSet(StringComparer.Ordinal);
        return parents.Count(parent=>slots.Contains(parent.Source.Id))<=1;
    }

    /// <summary>先证明导入上下文。首列只能用真实文本、整数或能唯一恢复的逻辑末级。</summary>
    private static bool SupportsImport(GenerationModel model,TableModel table,PageModel page,
        IReadOnlyList<CrossTableWriteModel> plans,IReadOnlyList<ColumnModel> columns)
    {
        var mode=ImportRenderer.Mode(page);
        if(mode is "" or "OnlyNew")return true;
        if(mode!="OverWriteByFirstColumn"||columns.Count==0)return false;
        var key=table.Columns.First(columns.Contains);
        if(key.IsJsonArray||key.IsManagedCascadeName)return false;
        var dependency=ImportDependencyCompilation.ForPage(model,table,page);
        var match=NonRedundantImportMatchRenderer.Resolve(model,table,page,dependency);
        if(key.Source.ControlKind=="CascadeTree")
            return match is not null&&plans.Any(plan=>plan.Levels[^1].Id.Source.Id==key.Source.Id);
        return (key.Type is "string?" or "int?" or "long?")&&key.Source.ControlKind is "" or "Text" or "Number"&&
            NonRedundantReadCompilation.Physical(model,table,key);
    }
}
