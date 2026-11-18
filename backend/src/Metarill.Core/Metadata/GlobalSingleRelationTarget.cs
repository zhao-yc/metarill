using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

public sealed record GlobalSingleRelationPlan(PageMetadata Page,ColumnMetadata Name);

/// <summary>普通单值全局关系的静态证明，目标和页面必须来自同一明确发布目录。</summary>
public static class GlobalSingleRelationTarget
{
    /// <summary>只允许普通R0来源的物理单值外键；缺项目列不能自动变成全局许可。</summary>
    public static GlobalSingleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,false);
    /// <summary>仅供物理R1消费者的冻结缓存计划使用；普通来源保留行项目范围，全局来源须另证发布目录。</summary>
    public static GlobalSingleRelationPlan RequireCached(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,true);
    /// <summary>带行项目列的自树可以绑定独立项目内父页，关联名称只读取真实父记录。</summary>
    public static GlobalSingleRelationPlan RequireProjectParent(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)
    {
        if(target.Id==owner.Id||GlobalCrossTableTarget.Declared(target)||
            !ReferenceEquals(catalog.ConfigTables.SingleOrDefault(table=>table.Id==target.Id),target)||
            GlobalCachedRelationSource.RowProjectSelfTreeParentPages(catalog,owner,foreign).Count==0)
            throw new NotSupportedException("普通父范围要求带行项目列的全局自树和独立项目父表。");
        GlobalCachedRelationSource.RequireSingle(catalog,owner,foreign);
        return RequirePublishedTarget(catalog,owner,foreign,target,null,projectParent:true);
    }

    /// <summary>嵌套缓存沿同一证明栈验证，不能在递归入口重置循环/深度检查。</summary>
    internal static GlobalSingleRelationPlan RequireCachedValue(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,ISet<string> resolving)=>RequireSource(catalog,owner,foreign,target,true,resolving);
    /// <summary>实时派生与缓存维护分别验证来源存储模式，其余发布和终点约束完全相同。</summary>
    private static GlobalSingleRelationPlan RequireSource(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,bool cached,ISet<string>? resolving=null)
    {
        var fields=catalog.ConfigColumns.Where(column=>column.ConfigTableId==owner.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,owner);var project=fields.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        // 无项目列来源只能进入独立发布证明；原项目来源及R0路径保持原门槛。
        if(!cached&&GlobalCrossTableTarget.Declared(owner)&&owner.IsRongyu==0)GlobalDerivedRelationSource.RequireSingle(catalog,owner,foreign);
        else if(cached&&GlobalCrossTableTarget.Declared(owner))
        {
            GlobalCachedRelationSource.RequireSingle(catalog,owner,foreign);
            if(project is not null&&owner.Id==target.Id)
                throw new NotSupportedException("带行项目列的自树父范围要求独立父表。");
        }
        else if(owner.IsRongyu!=(cached?1:0)||GlobalCrossTableTarget.Declared(owner)||owner.IsView!=0||owner.IsFunctionTable!=0||owner.YzProjectId!=catalog.YzProjectId||project is null||project.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,owner,project))throw new NotSupportedException("全局单值关系要求明确发布且有真实项目列的普通R0来源或已证明的R1消费者。");
        return RequirePublishedTarget(catalog,owner,foreign,target,resolving);
    }
    /// <summary>单值路径共用完整目标证明；来源资格由各自R0实时或R1缓存入口先行核对。</summary>
    internal static GlobalSingleRelationPlan RequirePublishedTarget(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,ISet<string>? resolving,bool projectParent=false)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,owner);
        if(foreign.ConfigTableId!=owner.Id||foreign.ControlKind is not("List" or "SelectPage")||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!storage.IsPhysical(foreign)||foreign.IsVirtualColumn!=0)throw new NotSupportedException("全局单值关系必须使用本表物理List或SelectPage外键。");
        using var document=JsonDocument.Parse(foreign.ControlDetail);var definition=document.RootElement;if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局关系定义必须为无重复键对象。");
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw new InvalidOperationException("全局关系映射必须是标量。");
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");var tableName=Value("table");var declaredPage=Value("pageId");var linkedPage=Extra("linkConfigPageId");
        if(tableId.Length>0&&tableId!=target.Id||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||tableName.Length>0&&tableName!=target.TableName||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||declaredPage.Length>0&&linkedPage.Length>0&&declaredPage!=linkedPage)throw new InvalidOperationException("全局关系的表、页面或显示来源声明相互冲突。");
        var pageId=declaredPage.Length>0?declaredPage:linkedPage;var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&(pageId.Length==0||page.Id==pageId)).ToArray();if(pages.Length!=1)throw new InvalidOperationException("全局单值关系必须指定真实目标页，省略时目标页必须唯一。");var page=pages[0];
        if(projectParent)RequireProjectParentPage(catalog,target,page);else GlobalCrossTableTarget.RequireSingle(catalog,target,page);
        var targetColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);var nameField=Value("nameField");if(nameField.Length==0)nameField="name";var name=targetColumns.SingleOrDefault(column=>column.ColumnName==nameField);
        bool Scalar(ColumnMetadata column)=>GlobalPhysicalRelationValue.IsScalar(targetStorage,target,column);
        // 普通与显式全局来源使用同一终点证明；来源的存储模式和发布身份已由各入口核验。
        var integer = name is not null && name.ColumnName != "id" && name.LogicalType is "int32" or "int64" &&
            owner.IsRongyu is 0 or 1;
        if (name is null || name.LogicalType != "string" && (!integer||projectParent) ||
            !(projectParent?Scalar(name):GlobalPhysicalRelationValue.IsPublishedScalar(catalog, targetStorage, target, name, resolving ?? new HashSet<string>(StringComparer.Ordinal))))
            throw new NotSupportedException("全局单值名称要求已证明的字符串或整数标量。");
        var key=targetColumns.SingleOrDefault(column=>column.ColumnName=="id");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))throw new InvalidOperationException("全局单值目标必须使用非空int64物理主键。");
        if(FixedConditionPlan.Compile(page.WhereSql,targetColumns.Select(column=>column.ColumnName)).Fields.Any(field=>!Scalar(targetColumns.Single(column=>column.ColumnName==field))))throw new NotSupportedException("目标条件必须使用非凭据物理标量。");
        return new(page,name);
    }

    /// <summary>父表及固定页必须属于当前目录，项目列必须真实存在，维护与业务均按当前已验证项目读取。</summary>
    private static void RequireProjectParentPage(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var fields=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var project=fields.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        var deleted=fields.SingleOrDefault(column=>column.ColumnName=="delete_no");
        if(table.IsRongyu is not(0 or 1)||table.IsView!=0||table.IsFunctionTable!=0||table.YzProjectId!=catalog.YzProjectId||
            page.YzProjectId!=catalog.YzProjectId||!catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId)||
            page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0||
            project is not {LogicalType:"int64" or "string"}||!GlobalPhysicalRelationValue.IsScalar(storage,table,project)||
            deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!GlobalPhysicalRelationValue.IsScalar(storage,table,deleted)))
            throw new NotSupportedException("普通父页需要本目录已发布的物理项目归属和活动范围。");
        _=ProjectParentConstants(page,fields.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,table,column)).Select(column=>column.ColumnName));
    }

    /// <summary>拆出已由物理作用域绑定的项目条件，保留原固定常量；禁止未接线的父或账号变量。</summary>
    public static string ProjectParentConstants(PageMetadata page,IEnumerable<string> fields)
    {
        var conditions=FixedConditionPlan.Compile(page.WhereSql,fields).Terms;
        var constants=new List<string>();
        foreach(var term in conditions)
        {
            if(term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable))constants.Add(term.ToDefinition());
            else if(term.Field!="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||
                term.Values.Count!=1||!term.Values[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("普通父页只支持当前项目和已发布的物理常量条件。");
        }
        return string.Join(" AND ",constants);
    }
}
