using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

/// <summary>显式全局跨表目标的共享发布证明；全局只改变目标行范围，不改变来源项目或页面授权。</summary>
public static class GlobalCrossTableTarget
{
    /// <summary>只识别原scopeKind精确声明，缺项目字段不能自动推断为全局。</summary>
    public static bool Declared(TableMetadata table)=>table.Extra.TryGetValue("scopeKind",out var value)&&value.ValueKind==JsonValueKind.String&&value.GetString()=="global";
    /// <summary>全局物理实体必须明确发布；请求项目／父变量不能在全局范围中被静默省略。</summary>
    public static void Require(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,false);
    /// <summary>单值及其候选根可另证R0实体；级联和文件父范围继续使用原R1入口。</summary>
    public static void RequireSingle(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,true);
    /// <summary>多值仅复用目标实体资格，源数组、名称和原页面规则仍由多值专用证明核对。</summary>
    public static void RequireMultiple(MetadataCatalog catalog,TableMetadata table,PageMetadata page)=>RequireTarget(catalog,table,page,true);
    /// <summary>跨表与全局候选可绑定受限账号等值；既有其它全局关系仍要求常量。</summary>
    public static void RequireAccountCandidate(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata? name=null)
    {
        if(name is null)RequireTarget(catalog,table,page,true,allowAccount:true);
        else RequirePublishedName(catalog,table,page,name,true,allowAccount:true);
    }

    /// <summary>只按明确用途开放R0，不扩散到旧级联或文件父范围入口。</summary>
    private static void RequireTarget(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool allowR0,bool allowAccount=false)
    {
        if(!Declared(table)||(table.IsRongyu!=1&&!(allowR0&&table.IsRongyu==0))||table.IsView!=0||table.IsFunctionTable!=0)throw new NotSupportedException("全局跨表目标要求显式声明的普通R0或R1实体。");
        if(table.IsRongyu==0)GlobalDerivedRelationSource.RequireTable(catalog,table);
        if(table.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||page.ConfigTableId!=table.Id)throw new InvalidOperationException("全局跨表表及页面必须明确发布在当前模型中。");
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,table);var physical=columns.Where(column=>storage.IsPhysical(column)&&column.IsVirtualColumn==0).Select(column=>column.ColumnName);
        var condition=FixedConditionPlan.Compile(page.WhereSql,physical);
        foreach(var term in condition.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
        {
            if(!allowAccount||term.Field=="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||
                !AccountConditionVariables.IsPageField(term.Values[0].Text,term.Field))
                throw new NotSupportedException("全局跨表目标只允许已发布的固定条件或明确账号等值。");
            var column=columns.Single(item=>item.ColumnName==term.Field);
            if(column.LogicalType is not ("int32" or "int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,table,column))
                throw new NotSupportedException("全局跨表账号条件要求真实非凭据的业务归属列。");
        }
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString()))throw new NotSupportedException("全局跨表目标的精确管理范围尚未接线。");
    }
    /// <summary>原跨表名称保持物理字符串或已证明R1缓存；R0实时名称由独立单值或多值用途入口分别证明。</summary>
    public static void RequireName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)=>RequirePublishedName(catalog,table,page,name,false);
    /// <summary>仅供单值候选证明R0逻辑名称，跨表原入口不会因此放开。</summary>
    public static void RequireSingleName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)=>RequirePublishedName(catalog,table,page,name,true);
    /// <summary>自树选项和缓存使用同一已发布的名称，可按原文本协议展示业务整数。</summary>
    public static void RequireSelfTreeName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name)
        =>RequirePublishedName(catalog,table,page,name,true,allowInteger:true);

    /// <summary>名称资格与页条件共用原完整证明，存储模式资格由调用用途限定。</summary>
    private static void RequirePublishedName(MetadataCatalog catalog,TableMetadata table,PageMetadata page,ColumnMetadata name,bool allowR0,bool allowAccount=false,bool allowInteger=false)
    {
        RequireTarget(catalog,table,page,allowR0,allowAccount);var storage=MetadataStoragePolicy.ForTable(catalog,table);
        if(name.LogicalType!="string"&&!(allowInteger&&name.ColumnName!="id"&&name.LogicalType is "int32" or "int64"))
            throw new NotSupportedException("全局目标名称的类型超出当前用途。");
        if(GlobalPhysicalRelationValue.IsScalar(storage,table,name))return;
        if(!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage,table,name))throw new NotSupportedException("全局跨表名称缺少完整物理或受管缓存证明。");
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToDictionary(column=>column.ColumnName,StringComparer.Ordinal);
        if(FixedConditionPlan.Compile(page.WhereSql,columns.Keys).Fields.Any(field=>!GlobalPhysicalRelationValue.IsScalar(storage,table,columns[field])))throw new NotSupportedException("全局缓存名称目标页条件必须为非凭据物理标量。");
    }

}
