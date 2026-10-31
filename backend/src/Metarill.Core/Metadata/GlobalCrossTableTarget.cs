using System.Text.Json;
namespace Metarill.Core.Metadata;

/// <summary>显式全局跨表目标的共享发布证明；全局只改变目标行范围，不改变来源项目或页面授权。</summary>
public static class GlobalCrossTableTarget
{
    /// <summary>只识别原scopeKind精确声明，缺项目字段不能自动推断为全局。</summary>
    public static bool Declared(TableMetadata table)=>table.Extra.TryGetValue("scopeKind",out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString()=="global";
    /// <summary>全局物理实体必须明确发布；请求项目／父变量不能在全局范围中被静默省略。</summary>
    public static void Require(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,false);
    /// <summary>单值及其候选根可另证R0实体；多值、级联和文件父范围继续使用原R1入口。</summary>
    public static void RequireSingle(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,true);
    /// <summary>只按明确用途开放R0，不把新单值能力扩散到尚未验收的其它全局组合。</summary>
    private static void RequireTarget(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowR0)
    {
        if(!Declared(table)||(table.IsRongyu!=1&&!(allowR0&&table.IsRongyu==0))||table.IsView!=0||table.IsFunctionTable!=0)throw new NotSupportedException("全局跨表目标要求显式声明的普通R0或R1实体。");
        if(table.IsRongyu==0)GlobalDerivedRelationSource.RequireTable(catalog,table);
        if(table.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||page.ConfigTableId!=table.Id)throw new InvalidOperationException("全局跨表表及页面必须明确发布在当前模型中。");
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,table);var physical=columns.Where(column=>storage.IsPhysical(column)&&column.IsVirtualColumn==0).Select(column=>column.ColumnName);
        if(FixedConditionPlan.Compile(page.WhereSql,physical).Variables.Count!=0)throw new NotSupportedException("全局跨表目标只能使用固定条件，不能混入项目或父上下文变量。");
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString()))throw new NotSupportedException("全局跨表目标的精确管理范围尚未接线。");
    }
    /// <summary>原跨表名称保持物理字符串或已证明R1缓存；R0实时名称只能通过独立单值用途入口。</summary>
    public static void RequireName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)=>RequirePublishedName(catalog,table,page,name,false);
    /// <summary>仅供单值候选证明R0逻辑名称，跨表和多值原入口不会因此放开。</summary>
    public static void RequireSingleName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)=>RequirePublishedName(catalog,table,page,name,true);
    /// <summary>名称资格与页条件共用原完整证明，存储模式资格由调用用途限定。</summary>
    private static void RequirePublishedName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name,bool allowR0)
    {
        RequireTarget(catalog,table,page,allowR0);var storage=MetadataStoragePolicy.ForTable(catalog,table);
        if(name.LogicalType!="string")throw new NotSupportedException("全局跨表名称必须为字符串。");
        if(GlobalPhysicalRelationValue.IsScalar(storage,table,name))return;
        if(!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage,table,name))throw new NotSupportedException("全局跨表名称缺少完整物理或受管缓存证明。");
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToDictionary(column=>column.ColumnName,StringComparer.Ordinal);
        if(FixedConditionPlan.Compile(page.WhereSql,columns.Keys).Fields.Any(field=>!GlobalPhysicalRelationValue.IsScalar(storage,table,columns[field])))throw new NotSupportedException("全局缓存名称目标页条件必须为非凭据物理标量。");
    }

}
