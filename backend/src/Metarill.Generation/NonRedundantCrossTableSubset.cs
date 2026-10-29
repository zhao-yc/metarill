using System.Text.Json;
namespace Metarill.Generation;

/// <summary>完整包按已经配置的跨表用途证明，尚未完成验收的路径Excel不能借隐藏控件绕过。</summary>
internal static class NonRedundantCrossTableSubset
{
    /// <summary>坏定义也不能借隐藏控件绕过完整包审计。</summary>
    internal static bool Declared(ColumnModel source)
    {
        if(source.Source.ControlKind!="CascadeTree")return false;
        try{using var json=JsonDocument.Parse(source.Source.ControlDetail);return !json.RootElement.TryGetProperty("sourceMode",out var mode)||mode.ValueKind!=JsonValueKind.String||mode.GetString()!="selfTree";}
        catch(Exception invalid)when(invalid is JsonException or InvalidOperationException){return true;}
    }
    /// <summary>只开放固定R0来源与普通R0／R1目标的完整读写链，元数据及原按钮保持原样。</summary>
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
            // 自树同伴使用真实自树计划，不当成跨表解析失败；未知、全局或自关联目标仍不放行。
            if(companions.Where(tree=>!Declared(tree)).Any(tree=>!NonRedundantSelfTreeSubset.FixedTarget(model,table,page,tree)))return "混合自树同伴缺少独立目标的完整读写证明。";
            var plans=trees.Select(tree=>CrossTableWriteCompilation.Resolve(model,table,page,tree)).ToArray();
            if(plans.Any(plan=>plan.Definition is null))return "跨表同页完整写入证明缺失，不能忽略隐藏或混合级联。";
            // 原组件按前缀转换属性，原详情还读取显式属性；两者必须与同一后端JSON字段一致。
            foreach(var plan in plans.Select(plan=>plan.Definition!))if(CrossTableFrontendProof.Reason(plan) is{} mismatch)return mismatch;
            _=NonRedundantReadCompilation.ForPage(model,table,page);_=NonRedundantSelfTreeSubset.ParentTables(model,table,page);
            if(ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>trees.Any(tree=>tree.Source.Id==column.Source.Id)))return "跨表级联无损路径已接生成源头，完整往返及正式事务验收尚待完成，完整包暂不开放该导入用途。";
            if(plans.SelectMany(plan=>plan.Definition!.Levels).Any(level=>ExportRenderer.IncludesColumn(page,level.Id)||ExportRenderer.IncludesColumn(page,level.Name)))return "跨表级联路径单元格导出尚待完整往返证明，完整包不能开放该导出用途。";
            return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return "非冗余跨表完整用途未通过："+error.Message;}
    }
}
