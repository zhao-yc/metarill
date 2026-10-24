using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

public sealed record GlobalMultipleRelationPlan(PageMetadata Page,ColumnMetadata Name);

/// <summary>全局多值关系分别证明普通R0实时读取与普通R1缓存消费者，目标必须为明确发布的物理名称。</summary>
public static class GlobalMultipleRelationTarget
{
    /// <summary>读取原多选或复选定义；目标页面只是已发布关系归属，复选仍使用自己的原候选条件。</summary>
    public static GlobalMultipleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,false);
    /// <summary>仅供物理R1消费者的冻结缓存计划使用，来源仍要求真实项目范围；不开放全局来源。</summary>
    public static GlobalMultipleRelationPlan RequireCached(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,true);
    /// <summary>实时派生与缓存维护分别验证来源存储模式，其余发布和终点约束完全相同。</summary>
    private static GlobalMultipleRelationPlan RequireSource(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,bool cached)
    {
        var sourceColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==owner.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,owner);var project=sourceColumns.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        if(owner.IsRongyu!=(cached?1:0)||GlobalCrossTableTarget.Declared(owner)||owner.IsView!=0||owner.IsFunctionTable!=0||owner.YzProjectId!=catalog.YzProjectId||project is null||project.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,owner,project))throw new NotSupportedException("全局多值关系要求明确发布且有真实项目列的普通R0来源或已证明的R1消费者。");
        if(foreign.ConfigTableId!=owner.Id||foreign.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase")||!foreign.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||foreign.LogicalType!="string"||foreign.IsVirtualColumn!=0||!storage.IsPhysical(foreign))throw new NotSupportedException("全局多值关系必须使用本表物理JSON文本ID数组。");
        using var document=foreign.ControlKind=="MultiCheckBoxDatabase"?null:JsonDocument.Parse(foreign.ControlDetail);var definition=foreign.ControlKind=="MultiCheckBoxDatabase"?DatabaseCheckboxDefinition.Parse(foreign.ControlDetail).ReadDefinition(foreign):document!.RootElement;
        if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局多值定义必须为无重复键对象。");
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw new InvalidOperationException("全局多值映射必须是标量。");
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");var tableName=Value("table");var pageId=Value("pageId");var linkedPage=Extra("linkConfigPageId");
        if(tableId.Length>0&&tableId!=target.Id||tableName.Length>0&&tableName!=target.TableName||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||pageId.Length>0&&linkedPage.Length>0&&pageId!=linkedPage)throw new InvalidOperationException("全局多值的表、页面或显示来源声明冲突。");
        if(pageId.Length==0)pageId=linkedPage;var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&(pageId.Length==0||page.Id==pageId)).ToArray();if(pages.Length!=1)throw new InvalidOperationException("全局多值关系必须明确指定真实目标页，省略时目标页必须唯一。");var page=pages[0];GlobalCrossTableTarget.Require(catalog,target,page);
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();var physical=MetadataStoragePolicy.ForTable(catalog,target);
        bool Scalar(ColumnMetadata column)=>GlobalPhysicalRelationValue.IsScalar(physical,target,column);
        var nameField=Value("nameField");if(nameField.Length==0)nameField="name";var name=columns.SingleOrDefault(column=>column.ColumnName==nameField);if(name is null||name.LogicalType!="string"||!Scalar(name))throw new NotSupportedException("全局多值名称必须为非凭据物理字符串。");var key=columns.SingleOrDefault(column=>column.ColumnName=="id");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))throw new InvalidOperationException("全局多值目标必须使用非空int64物理主键。");
        if(FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName)).Fields.Any(field=>!Scalar(columns.Single(column=>column.ColumnName==field))))throw new NotSupportedException("全局多值目标页条件必须为非凭据物理标量。");return new(page,name);
    }
}
