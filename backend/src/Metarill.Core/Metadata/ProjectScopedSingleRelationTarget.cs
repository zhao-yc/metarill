using System.Text.Json;
using Metarill.Core.Security;

namespace Metarill.Core.Metadata;

/// <summary>项目内冗余单值关系的固定目标页。所有来源入口必须能共享同一个名称副本。</summary>
public static class ProjectScopedSingleRelationTarget
{
    /// <summary>项目内单值页可用已声明的跨表层位限定父级。</summary>
    public static MultiTargetParentScope? LogicalParent(MetadataCatalog catalog,TableMetadata target,PageMetadata page)
    {
        if(target.IsRongyu!=0||GlobalCrossTableTarget.Declared(target)||page.ConfigTableId!=target.Id||
            page.Extra.GetValueOrDefault("type").ToString()=="2")return null;
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var expanded=RelationStorageColumns.Expand(target,columns);
        var condition=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
        if(!condition.Terms.Any(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
            MultiTargetParentScope.IsParameter(value.Text))&&CrossTableStorageColumns.IsLogicalPathField(target,columns,term.Field)))return null;
        return MultiTargetParentScope.Compile(catalog,target,condition) is {Logical:true} parent?parent:null;
    }

    /// <summary>路径任一实际目标页有额外范围时，所有写入入口必须共享同一固定名称来源。</summary>
    public static (PageMetadata Page, ColumnMetadata Name)? Resolve(
        MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)
    {
        if(owner.IsRongyu!=1||!IsProjectRole(owner,foreign,target))return null;
        var pages=TargetPages(catalog,target);
        if(pages.Length==0)return null;
        using var document=Definition(foreign);
        var definition=document.RootElement;
        var selected=SelectedPage(foreign,definition);
        var linked=Text(foreign,"linkConfigPageId");
        var nameField=NameField(definition);
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var names=RelationStorageColumns.Expand(target,columns).Select(column=>column.ColumnName).ToArray();
        var matches=catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id)
            .Select(page=>TargetFor(catalog,page,foreign,selected,nameField,pages,names)).ToArray();
        var hasNameCache=foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
            catalog.ConfigColumns.Any(column=>column.ConfigTableId==owner.Id&&column.ColumnName==foreign.ColumnName[..^3]+"_"+nameField);
        var controlScope=hasNameCache?ControlScope(catalog,owner,foreign,target):null;
        if(controlScope?.Terms.Count is not >0&&!matches.Any(match=>match.Page is not null&&(HasPageScope(match.Page,names)||
            NamePathHasScope(catalog,target,match.Page,[match.Name],new()))))return null;
        if(matches.Any(match=>match.Page is null||match.Name!=nameField)||
            matches.Select(match=>match.Page!.Id).Distinct(StringComparer.Ordinal).Count()!=1||
            selected.Length>0&&selected!=matches[0].Page!.Id||
            linked.Length>0&&linked!=matches[0].Page!.Id||
            owner.YzProjectId!=catalog.YzProjectId||target.YzProjectId!=catalog.YzProjectId)throw Invalid();

        RequirePhysicalRole(catalog,owner,foreign,target,definition);
        var name=columns.SingleOrDefault(column=>column.ColumnName==nameField)??throw Invalid();
        if(name.IsVirtualColumn!=0||name.LogicalType is "json" or "binary"||
            ExactDataManageValues.IsSensitiveField(name.ColumnName)||
            name.ControlKind is "FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "MultiList" or "CheckBoxList")
            throw Invalid();
        return(matches[0].Page!,name);
    }

    /// <summary>沿中间记录的实际页面解析下一个目标，即使当前段没有额外范围也继续保留页归属。</summary>
    public static (PageMetadata Page,bool Scoped)? NestedPage(MetadataCatalog catalog,TableMetadata owner,
        PageMetadata sourcePage,ColumnMetadata foreign,TableMetadata target)
    {
        if(owner.IsRongyu!=0||sourcePage.ConfigTableId!=owner.Id||!IsProjectRole(owner,foreign,target))return null;
        var pages=TargetPages(catalog,target);
        if(pages.Length==0)return null;
        using var document=Definition(foreign);
        var definition=document.RootElement;
        var members=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id))
            .Select(column=>column.ColumnName).ToArray();
        var (page,_)=TargetFor(catalog,sourcePage,foreign,SelectedPage(foreign,definition),NameField(definition),pages,members);
        if(page is null)return null;
        RequirePhysicalRole(catalog,owner,foreign,target,definition);
        // 页面覆盖可以选择同一实体的另一个页，表归属仍由物理外键确定。
        using var overlay=PageDetail(catalog,sourcePage,foreign);
        if(overlay is not null&&!PointsToTarget(overlay.RootElement,target))throw Invalid();
        return(page,HasPageScope(page,members)||ControlScope(catalog,owner,foreign,target,sourcePage).Terms.Count>0);
    }

    /// <summary>下拉的附加条件取当前实际来源页。共享物理副本要求所有入口采用等价的结构化条件。</summary>
    public static FixedConditionPlan ControlScope(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,PageMetadata? sourcePage=null)
    {
        var members=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id))
            .Select(column=>column.ColumnName).ToArray();
        if(foreign.ControlKind!="List")return FixedConditionPlan.Compile(null,members);
        using var definition=Definition(foreign);
        var baseline=Value(definition.RootElement,"whereSql");
        var pages=sourcePage is null?catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id).ToArray():[sourcePage];
        var definitions=pages.Select(page=>
        {
            using var overlay=PageDetail(catalog,page,foreign);
            var current=overlay is null?"":Value(overlay.RootElement,"whereSql");
            return Normalize(current.Length==0?baseline:current);
        }).Distinct(StringComparer.Ordinal).ToArray();
        if(definitions.Length>1)
            throw new InvalidOperationException("同一物理名称副本的来源页附加条件必须一致。");
        return FixedConditionPlan.Compile(definitions.SingleOrDefault(),members);

        string Normalize(string text)=>string.Join(" AND ",FixedConditionPlan.Compile(text,members).Terms
            .Where(term=>!ProjectTerm(term)).Select(term=>term.ToDefinition()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>顺着已声明的单值路径找到需要持久化的范围，循环留给与读取相同的拒绝门禁。</summary>
    private static bool NamePathHasScope(MetadataCatalog catalog,TableMetadata table,PageMetadata page,string[] path,
        HashSet<(string Table,string Field)> active)
    {
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var head=path[0];
        ColumnMetadata? Find(string name)=>columns.FirstOrDefault(column=>column.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase));
        if(path.Length>1)return Step(Find(head+"_id")??Find(head),path[1..]);
        if(Find(head) is {} column)
        {
            var raw=column.Extra.GetValueOrDefault("isJoinColumn").ToString();
            if(raw!="1"&&!raw.Equals("true",StringComparison.OrdinalIgnoreCase))return false;
            if(!active.Add((table.Id,column.Id)))throw new InvalidOperationException("非冗余字段存在循环依赖。");
            try
            {
                var route=Text(column,"columnFullName").Split('.');
                return route.Length>=2&&route.All(segment=>segment.Length>0)&&NamePathHasScope(catalog,table,page,route,active);
            }
            finally{active.Remove((table.Id,column.Id));}
        }

        foreach(var foreign in columns.Where(column=>column.ControlKind is "List" or "SelectPage"&&
            column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
            head.StartsWith(column.ColumnName[..^3]+"_",StringComparison.OrdinalIgnoreCase)))
        {
            using var definition=Definition(foreign);
            var label=NameField(definition.RootElement);
            if(label!="id"&&head.Equals(foreign.ColumnName[..^3]+"_"+label,StringComparison.OrdinalIgnoreCase))
                return Step(foreign,[label]);
        }
        return false;

        bool Step(ColumnMetadata? foreign,string[] tail)
        {
            if(foreign is null||foreign.ControlKind is not("List" or "SelectPage"))return false;
            var target=PathTarget(catalog,foreign);
            return target is not null&&NestedPage(catalog,table,page,foreign,target) is {} next&&
                (next.Scoped||NamePathHasScope(catalog,target,next.Page,tail,active));
        }
    }

    /// <summary>名称路径只识别原单值外键的明确表、页和实体名。</summary>
    private static TableMetadata? PathTarget(MetadataCatalog catalog,ColumnMetadata foreign)
    {
        using var document=Definition(foreign);
        var definition=document.RootElement;
        var targetId=Value(definition,"tableId");
        if(targetId.Length==0)
        {
            var pageId=Value(definition,"pageId");
            targetId=catalog.ConfigPages.SingleOrDefault(page=>page.Id==pageId)?.ConfigTableId??foreign.LinkConfigTableId;
        }
        if(targetId.Length==0)
            targetId=catalog.ConfigPages.SingleOrDefault(page=>page.Id==Text(foreign,"linkConfigPageId"))?.ConfigTableId??"";
        if(targetId.Length>0)return catalog.ConfigTables.SingleOrDefault(table=>table.Id==targetId);
        var name=Value(definition,"table");
        if(name.Length==0)name=Text(foreign,"joinTable");
        if(name.Length==0&&foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal))name=foreign.ColumnName[..^3];
        return catalog.ConfigTables.SingleOrDefault(table=>table.TableName==name);
    }

    /// <summary>下拉回退与原页面一致，同后台优先且不选择带父参数的候选页。</summary>
    private static (PageMetadata? Page,string Name) TargetFor(MetadataCatalog catalog,PageMetadata sourcePage,
        ColumnMetadata foreign,string selected,string name,PageMetadata[] pages,string[] members)
    {
        using var overlay=PageDetail(catalog,sourcePage,foreign);
        if(overlay is not null)
        {
            var pageId=foreign.ControlKind=="SelectPage"?Value(overlay.RootElement,"pageId"):"";
            var label=Value(overlay.RootElement,"nameField");
            if(pageId.Length>0)selected=pageId;
            if(label.Length>0)name=label;
        }
        if(selected.Length>0||foreign.ControlKind=="SelectPage")return(pages.SingleOrDefault(page=>page.Id==selected),name);
        var unparented=pages.Where(page=>!FixedConditionPlan.Compile(page.WhereSql,members)
            .Variables.Any(MultiTargetParentScope.IsParameter)).ToArray();
        return(unparented.FirstOrDefault(page=>page.YzAdminTypeId==sourcePage.YzAdminTypeId)??unparented.FirstOrDefault(),name);
    }

    /// <summary>项目归属已由物理联接处理；额外固定条件和父范围需要参与名称计算。</summary>
    private static bool HasPageScope(PageMetadata page,string[] members)=>
        FixedConditionPlan.Compile(page.WhereSql,members).Terms.Any(term=>!ProjectTerm(term));

    private static bool ProjectTerm(FixedConditionTerm term)=>term.Field=="yz_project_id"&&
        term.Comparison==FixedConditionComparison.Equal&&term.Values.Count==1&&term.Values[0].Kind==FixedConditionValueKind.Variable&&
        term.Values[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);

    private static PageMetadata[] TargetPages(MetadataCatalog catalog,TableMetadata table)=>
        catalog.ConfigPages.Where(page=>page.ConfigTableId==table.Id).OrderBy(page=>page.Id,StringComparer.Ordinal).ToArray();

    private static bool IsProjectRole(TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>
        target.IsRongyu==0&&!GlobalCrossTableTarget.Declared(owner)&&!GlobalCrossTableTarget.Declared(target)&&
        foreign.ControlKind is "List" or "SelectPage";

    /// <summary>所有显式表声明须吻合，并且物理外键和项目键都能参与持久化反查。</summary>
    private static void RequirePhysicalRole(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,JsonElement definition)
    {
        var ownerStorage=MetadataStoragePolicy.ForTable(catalog,owner);
        var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);
        bool Project(TableMetadata table,MetadataStoragePlan storage)=>
            storage.PhysicalColumns.Any(column=>column.ColumnName=="yz_project_id"&&
                column.LogicalType is "int64" or "string"&&GlobalPhysicalRelationValue.IsScalar(storage,table,column));
        if(owner.IsView!=0||owner.IsFunctionTable!=0||target.IsView!=0||target.IsFunctionTable!=0||
            owner.YzProjectId.Length>0&&owner.YzProjectId!=catalog.YzProjectId||
            target.YzProjectId.Length>0&&target.YzProjectId!=catalog.YzProjectId||
            foreign.ConfigTableId!=owner.Id||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||
            foreign.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(ownerStorage,owner,foreign)||
            foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||!PointsToTarget(definition,target)||
            !Project(owner,ownerStorage)||!Project(target,targetStorage))throw Invalid();
    }

    private static bool PointsToTarget(JsonElement definition,TableMetadata target)
    {
        var id=Value(definition,"tableId");
        var name=Value(definition,"table");
        return(id.Length==0||id==target.Id)&&(name.Length==0||name==target.TableName);
    }

    private static string SelectedPage(ColumnMetadata foreign,JsonElement definition)
    {
        var selected=foreign.ControlKind=="SelectPage"?Value(definition,"pageId"):"";
        return selected.Length==0?Text(foreign,"linkConfigPageId"):selected;
    }

    private static string NameField(JsonElement definition)=>Value(definition,"nameField") is {Length:>0} field?field:"name";
    private static string Text(ColumnMetadata column,string key)
    {
        var value=column.Extra.GetValueOrDefault(key).ToString();
        return string.IsNullOrWhiteSpace(value)?"":value;
    }
    private static JsonDocument Definition(ColumnMetadata foreign)=>
        JsonDocument.Parse(string.IsNullOrWhiteSpace(foreign.ControlDetail)?"{}":foreign.ControlDetail);

    private static JsonDocument? PageDetail(MetadataCatalog catalog,PageMetadata page,ColumnMetadata foreign)
    {
        var detail=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==foreign.Id)
            ?.Extra.GetValueOrDefault("controlDetail").ToString();
        return string.IsNullOrWhiteSpace(detail)?null:JsonDocument.Parse(detail);
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
