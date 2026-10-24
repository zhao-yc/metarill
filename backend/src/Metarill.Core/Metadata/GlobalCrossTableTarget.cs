using System.Text.Json;
namespace Metarill.Core.Metadata;

/// <summary>显式全局跨表目标的共享发布证明；全局只改变目标行范围，不改变来源项目或页面授权。</summary>
public static class GlobalCrossTableTarget
{
    /// <summary>只识别原scopeKind精确声明，缺项目字段不能自动推断为全局。</summary>
    public static bool Declared(TableMetadata table)=>table.Extra.TryGetValue("scopeKind",out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString()=="global";
    /// <summary>全局物理实体必须明确发布；请求项目／父变量不能在全局范围中被静默省略。</summary>
    public static void Require(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        if(!Declared(table)||table.IsRongyu!=1||table.IsView!=0||table.IsFunctionTable!=0)throw new NotSupportedException("全局跨表目标要求显式声明的普通R1物理实体。");
        if(table.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||page.ConfigTableId!=table.Id)throw new InvalidOperationException("全局跨表表及页面必须明确发布在当前模型中。");
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,table);var physical=columns.Where(column=>storage.IsPhysical(column)&&column.IsVirtualColumn==0).Select(column=>column.ColumnName);
        if(FixedConditionPlan.Compile(page.WhereSql,physical).Variables.Count!=0)throw new NotSupportedException("全局跨表目标只能使用固定条件，不能混入项目或父上下文变量。");
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString()))throw new NotSupportedException("全局跨表目标的精确管理范围尚未接线。");
    }
}
