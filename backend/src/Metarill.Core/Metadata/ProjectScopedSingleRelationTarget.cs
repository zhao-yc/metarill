using System.Text.Json;
using Metarill.Core.Security;

namespace Metarill.Core.Metadata;

/// <summary>项目内冗余单值关系的固定目标页。所有来源入口必须能共享同一个名称副本。</summary>
public static class ProjectScopedSingleRelationTarget
{
    /// <summary>实际使用逻辑或账号条件的关系须证明所有来源页共享同一目标及名称副本。</summary>
    public static (PageMetadata Page, ColumnMetadata Name)? Resolve(
        MetadataCatalog catalog, TableMetadata owner, ColumnMetadata foreign, TableMetadata target)
    {
        if(owner.IsRongyu!=1||target.IsRongyu!=0||GlobalCrossTableTarget.Declared(owner)||
            GlobalCrossTableTarget.Declared(target)||foreign.ControlKind is not("List" or "SelectPage"))return null;
        var targetColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var expanded=RelationStorageColumns.Expand(target,targetColumns);
        // 与生成模型的原目标页回退顺序一致，不依赖目录输入顺序。
        var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id)
            .OrderBy(page=>page.Id,StringComparer.Ordinal).ToArray();
        var scopedPages=pages.Where(page=>
        {
            var condition=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
            return condition.Variables.Any(AccountConditionVariables.IsSupported)||
                condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,field)||
                    CrossTableStorageColumns.IsLogicalPathField(target,expanded,field));
        }).Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        if(scopedPages.Count==0)return null;

        using var document=JsonDocument.Parse(string.IsNullOrWhiteSpace(foreign.ControlDetail)?"{}":foreign.ControlDetail);
        var definition=document.RootElement;
        var linked=foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        if(string.IsNullOrWhiteSpace(linked))linked="";
        var selected=foreign.ControlKind=="SelectPage"?Value(definition,"pageId"):"";
        if(selected.Length==0)selected=linked;
        var nameField=Value(definition,"nameField");
        if(nameField.Length==0)nameField="name";
        var matches=catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id)
            .Select(sourcePage=>TargetFor(sourcePage)).ToArray();
        if(!matches.Any(match=>match.Page is not null&&scopedPages.Contains(match.Page.Id)))return null;
        if(matches.Any(match=>match.Page is null||match.Name!=nameField)||
            matches.Select(match=>match.Page!.Id).Distinct(StringComparer.Ordinal).Count()!=1||
            selected.Length>0&&selected!=matches[0].Page!.Id||
            linked.Length>0&&linked!=matches[0].Page!.Id)throw Invalid();

        var ownerStorage=MetadataStoragePolicy.ForTable(catalog,owner);
        var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);
        bool Project(TableMetadata table,MetadataStoragePlan storage)=>
            storage.PhysicalColumns.Any(column=>column.ColumnName=="yz_project_id"&&
                column.LogicalType is "int64" or "string"&&GlobalPhysicalRelationValue.IsScalar(storage,table,column));
        var targetId=Value(definition,"tableId");
        var targetName=Value(definition,"table");
        if(owner.IsView!=0||owner.IsFunctionTable!=0||target.IsView!=0||target.IsFunctionTable!=0||
            owner.YzProjectId!=catalog.YzProjectId||target.YzProjectId!=catalog.YzProjectId||
            foreign.ConfigTableId!=owner.Id||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||
            foreign.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(ownerStorage,owner,foreign)||
            foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||
            targetId.Length>0&&targetId!=target.Id||targetName.Length>0&&targetName!=target.TableName||
            !Project(owner,ownerStorage)||!Project(target,targetStorage))throw Invalid();
        var name=targetColumns.SingleOrDefault(column=>column.ColumnName==nameField)??throw Invalid();
        if(name.IsVirtualColumn!=0||name.LogicalType is "json" or "binary"||
            ExactDataManageValues.IsSensitiveField(name.ColumnName)||
            name.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "MultiList" or "CheckBoxList")
            throw Invalid();
        return(matches[0].Page!,name);

        (PageMetadata? Page,string Name) TargetFor(PageMetadata sourcePage)
        {
            var targetPageId=selected;
            var display=nameField;
            var detail=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==sourcePage.Id&&field.ConfigColumnId==foreign.Id)
                ?.Extra.GetValueOrDefault("controlDetail").ToString();
            if(!string.IsNullOrWhiteSpace(detail))
            {
                using var overlay=JsonDocument.Parse(detail);
                var pageId=foreign.ControlKind=="SelectPage"?Value(overlay.RootElement,"pageId"):"";
                var label=Value(overlay.RootElement,"nameField");
                if(pageId.Length>0)targetPageId=pageId;
                if(label.Length>0)display=label;
            }
            if(targetPageId.Length>0||foreign.ControlKind=="SelectPage")
                return(pages.SingleOrDefault(page=>page.Id==targetPageId),display);
            // 下拉沿用原后台优先的无父目标页，名称副本只能登记各入口实际共用的页面。
            var unparented=pages.Where(page=>!FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName))
                .Variables.Any(MultiTargetParentScope.IsParameter)).ToArray();
            return(unparented.FirstOrDefault(page=>page.YzAdminTypeId==sourcePage.YzAdminTypeId)??unparented.FirstOrDefault(),display);
        }
    }

    /// <summary>页、表及名称只接受已存固定标量。</summary>
    private static string Value(JsonElement definition,string key)
    {
        if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())
            throw Invalid();
        var value=definition.TryGetProperty(key,out var property)?property:default;
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null=>"",
            JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString())=>value.GetString()!,
            JsonValueKind.String=>"",
            JsonValueKind.Number when key is "tableId" or "pageId"=>value.ToString(),
            _=>throw Invalid()
        };
    }

    private static InvalidOperationException Invalid()=>new("项目内固定单值名称副本要求同一目标页、实际物理外键及一致的名称声明。");
}
