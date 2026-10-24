using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

public sealed record GlobalSingleRelationPlan(PageMetadata Page,ColumnMetadata Name);

/// <summary>普通单值全局关系的静态证明，目标和页面必须来自同一明确发布目录。</summary>
public static class GlobalSingleRelationTarget
{
    /// <summary>只允许普通R0来源的物理单值外键；缺项目列不能自动变成全局许可。</summary>
    public static GlobalSingleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,false);
    /// <summary>仅供物理R1消费者的冻结缓存计划使用，来源仍要求真实项目范围；不开放全局来源。</summary>
    public static GlobalSingleRelationPlan RequireCached(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,true);
    /// <summary>实时派生与缓存维护分别验证来源存储模式，其余发布和终点约束完全相同。</summary>
    private static GlobalSingleRelationPlan RequireSource(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,bool cached)
    {
        var fields=catalog.ConfigColumns.Where(column=>column.ConfigTableId==owner.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,owner);var project=fields.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        if(owner.IsRongyu!=(cached?1:0)||GlobalCrossTableTarget.Declared(owner)||owner.IsView!=0||owner.IsFunctionTable!=0||owner.YzProjectId!=catalog.YzProjectId||project is null||project.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,owner,project))throw new NotSupportedException("全局单值关系要求明确发布且有真实项目列的普通R0来源或已证明的R1消费者。");
        if(foreign.ConfigTableId!=owner.Id||foreign.ControlKind is not("List" or "SelectPage")||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!storage.IsPhysical(foreign)||foreign.IsVirtualColumn!=0)throw new NotSupportedException("全局单值关系必须使用本表物理List或SelectPage外键。");
        using var document=JsonDocument.Parse(foreign.ControlDetail);var definition=document.RootElement;if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局关系定义必须为无重复键对象。");
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw new InvalidOperationException("全局关系映射必须是标量。");
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");var tableName=Value("table");var declaredPage=Value("pageId");var linkedPage=Extra("linkConfigPageId");
        if(tableId.Length>0&&tableId!=target.Id||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||tableName.Length>0&&tableName!=target.TableName||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||declaredPage.Length>0&&linkedPage.Length>0&&declaredPage!=linkedPage)throw new InvalidOperationException("全局关系的表、页面或显示来源声明相互冲突。");
        var pageId=declaredPage.Length>0?declaredPage:linkedPage;var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&(pageId.Length==0||page.Id==pageId)).ToArray();if(pages.Length!=1)throw new InvalidOperationException("全局单值关系必须指定真实目标页，省略时目标页必须唯一。");var page=pages[0];GlobalCrossTableTarget.Require(catalog,target,page);
        var targetColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);var nameField=Value("nameField");if(nameField.Length==0)nameField="name";var name=targetColumns.SingleOrDefault(column=>column.ColumnName==nameField);
        bool Scalar(ColumnMetadata column)=>GlobalPhysicalRelationValue.IsScalar(targetStorage,target,column);
        if(name is null||name.LogicalType!="string"||!Scalar(name))throw new NotSupportedException("全局单值名称必须为非凭据物理字符串。");var key=targetColumns.SingleOrDefault(column=>column.ColumnName=="id");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))throw new InvalidOperationException("全局单值目标必须使用非空int64物理主键。");
        if(FixedConditionPlan.Compile(page.WhereSql,targetColumns.Select(column=>column.ColumnName)).Fields.Any(field=>!Scalar(targetColumns.Single(column=>column.ColumnName==field))))throw new NotSupportedException("全局目标条件必须使用非凭据物理标量。");return new(page,name);
    }
}
