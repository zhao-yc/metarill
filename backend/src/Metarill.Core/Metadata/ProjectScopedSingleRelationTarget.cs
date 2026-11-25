using System.Text.Json;
using Metarill.Core.Security;

namespace Metarill.Core.Metadata;

/// <summary>项目内冗余单值关系的固定目标页。所有来源入口必须能共享同一个名称副本。</summary>
public static class ProjectScopedSingleRelationTarget
{
    /// <summary>V8 固定候选用于含非冗余实体的项目，纯冗余项目保留原控件绑定。</summary>
    public static bool UsesFixedListCandidates(MetadataCatalog catalog) =>
        catalog.ConfigTables.Any(table => table is { IsRongyu: 0, IsView: 0, IsFunctionTable: 0 });

    /// <summary>下拉从本页覆盖、字段配置或原关联页取实际目标标识，留空时回到原关联页。</summary>
    public static string ListPageId(MetadataCatalog catalog,PageMetadata sourcePage,ColumnMetadata foreign)
    {
        if (!UsesFixedListCandidates(catalog)) return Text(foreign,"linkConfigPageId");
        using var root = Definition(foreign);
        using var overlay = PageDetail(catalog,sourcePage,foreign);
        return FixedListPageId(foreign,root.RootElement,overlay?.RootElement);
    }

    private static string FixedListPageId(ColumnMetadata foreign,JsonElement definition,JsonElement? overlay)
    {
        var settings = overlay is {} detail && detail.TryGetProperty("pageId",out _) ? detail : definition;
        var configured = Value(settings,"pageId");
        return string.IsNullOrWhiteSpace(configured) ? Text(foreign,"linkConfigPageId") : configured;
    }

    /// <summary>固定候选按原 V8 逐键覆盖附加条件，显式留空只清除控件条件。旧实体查询仍以非空值覆盖。</summary>
    public static string ListWhereSql(MetadataCatalog catalog,PageMetadata sourcePage,ColumnMetadata foreign)
    {
        using var definition=Definition(foreign);
        using var overlay=PageDetail(catalog,sourcePage,foreign);
        return EffectiveListWhereSql(definition.RootElement,overlay?.RootElement,UsesFixedListCandidates(catalog));
    }

    private static string EffectiveListWhereSql(JsonElement definition,JsonElement? overlay,bool fixedQuery)
    {
        if(overlay is {} detail)
        {
            var current=Value(detail,"whereSql");
            if(fixedQuery?detail.TryGetProperty("whereSql",out _):current.Length>0)return current;
        }
        return Value(definition,"whereSql");
    }

    /// <summary>项目内单值页可用已声明的跨表层位限定父级。</summary>
    public static MultiTargetParentScope? LogicalParent(MetadataCatalog catalog,TableMetadata target,PageMetadata page)=>
        PageParent(catalog,target,page) is {Logical:true} parent?parent:null;

    /// <summary>单值目标读取实际逻辑父或显式全局父，普通项目内单物理父仍走原绑定。</summary>
    public static MultiTargetParentScope? PageParent(MetadataCatalog catalog,TableMetadata target,PageMetadata page)
    {
        if(target.IsRongyu!=0||GlobalCrossTableTarget.Declared(target)||page.ConfigTableId!=target.Id||
            page.Extra.GetValueOrDefault("type").ToString()=="2")return null;
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var expanded=RelationStorageColumns.Expand(target,columns);
        var condition=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
        var terms=condition.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
            MultiTargetParentScope.IsParameter(value.Text))).ToArray();
        if(terms.Length==0)return null;
        if(terms.Length==1&&!CrossTableStorageColumns.IsLogicalPathField(target,columns,terms[0].Field)&&
            !columns.Any(column=>column.ColumnName==terms[0].Field&&catalog.ConfigTables.Any(parent=>
                parent.Id==column.LinkConfigTableId&&GlobalCrossTableTarget.Declared(parent))))return null;
        return MultiTargetParentScope.CompileSelectionPage(catalog,target,condition,allowGlobalParent:true);
    }

    /// <summary>原弹窗直达已发布的 type=1 页面，父范围只取该页实际物理外键。</summary>
    public static MultiTargetParentScope? PhysicalSelectionParent(MetadataCatalog catalog,TableMetadata target,PageMetadata page)
    {
        if(target.IsRongyu is not (0 or 1)||GlobalCrossTableTarget.Declared(target)||page.ConfigTableId!=target.Id||
            page.Extra.GetValueOrDefault("type").ToString()!="1")return null;
        var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
        var condition=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        var scope=MultiTargetParentScope.CompileListPage(catalog,target,condition.Terms);
        return scope is {Logical:false,Global:false}?scope:null;
    }

    /// <summary>只有项目内冗余字典的已发布单值目标页可用普通物理列声明精确范围。</summary>
    public static bool HasPhysicalSingleExact(MetadataCatalog catalog,TableMetadata target,PageMetadata page,string controlKind)
    {
        var pageType=page.Extra.GetValueOrDefault("type").ToString();
        var validPage=controlKind switch
        {
            "List"=>pageType is "" or "0" or "1",
            "SelectPage"=>pageType=="1",
            _=>false
        };
        if(!validPage||target.IsRongyu!=1||target.IsView!=0||target.IsFunctionTable!=0||GlobalCrossTableTarget.Declared(target)||
            page.ConfigTableId!=target.Id||
            page.YzProjectId!=catalog.YzProjectId||target.YzProjectId!=catalog.YzProjectId||
            !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId))
            return false;
        var field=ExactDataManageValues.Field(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString());
        if(field.Length==0)return false;
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var match=storage.PhysicalColumns.SingleOrDefault(column=>column.ColumnName==field);
        if(match is null||!GlobalPhysicalRelationValue.IsScalar(storage,target,match))return false;
        _=ExactDataManageValues.Unrestricted.ReadLogical(match.LogicalType);
        return true;
    }

    /// <summary>路径任一实际目标页有额外范围时，所有写入入口必须共享同一固定名称来源。</summary>
    public static (PageMetadata Page, ColumnMetadata Name)? Resolve(
        MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)
    {
        // 旧下拉仍按原关联页选取字典。文本级联父也需要带此真实页面登记名称缓存。
        var linkedPage=Text(foreign,"linkConfigPageId");
        var fixedList=foreign.ControlKind=="List"&&UsesFixedListCandidates(catalog);
        var storedList=target.IsRongyu==1&&foreign.ControlKind=="List"&&(fixedList||
            catalog.ConfigPages.Any(page=>page.Id==linkedPage&&CrossTableStorageColumns.DeclaresStoredTextParent(catalog,target,page)));
        var storedSelection=target.IsRongyu==1&&foreign.ControlKind=="SelectPage";
        if(owner.IsRongyu!=1||!IsProjectRole(owner,foreign,target,allowStoredTarget:storedList||storedSelection))return null;
        var sourcePages=catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id).ToArray();
        if(storedList && !sourcePages.Any(page=>ListPageId(catalog,page,foreign).Length>0))return null;
        var pages=TargetPages(catalog,target);
        if(pages.Length==0)return null;
        using var document=Definition(foreign);
        var definition=document.RootElement;
        var selected=SelectedPage(foreign,definition,fixedList);
        var linked=Text(foreign,"linkConfigPageId");
        var nameField=NameField(definition);
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var names=RelationStorageColumns.Expand(target,columns).Select(column=>column.ColumnName).ToArray();
        // 主键显示使用整数外键，名称副本继续按表级定义和同一目标范围维护。
        var integerId=foreign.LogicalType=="int64"&&
            columns.Any(column=>column.ColumnName=="id"&&column.LogicalType=="int64");
        var matches=sourcePages
            .Select(page=>TargetFor(catalog,page,foreign,definition,selected,nameField,pages,names,integerId,fixedList)).ToArray();
        var nameCache=foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)
            ? catalog.ConfigColumns.FirstOrDefault(column=>column.ConfigTableId==owner.Id&&column.ColumnName==foreign.ColumnName[..^3]+"_"+nameField)
            : null;
        // 无父字典的固定条件只约束生成器管理的物理副本，普通同名字段仍由业务维护。
        var managedCache=nameCache is {IsVirtualColumn:0}&&nameCache.Extra.TryGetValue("isJoinColumn",out var joined)&&
            (joined.ValueKind==JsonValueKind.True||joined.ToString()=="1");
        if(storedList&&!fixedList&&!managedCache)return null;
        if(storedSelection&&!matches.Any(match=>match.Page is {} page&&
            (PhysicalSelectionParent(catalog,target,page) is not null||
                managedCache&&HasScopedUnparentedSelection(catalog,target,page,columns,names))))
            return null;
        var controlScope=nameCache is not null?ControlScope(catalog,owner,foreign,target):null;
        if(controlScope?.Terms.Count is not >0&&!matches.Any(match=>match.Page is not null&&(HasPageScope(match.Page,names)||
            NamePathHasScope(catalog,target,match.Page,[match.Name],new()))))return null;
        if(matches.Any(match=>match.Page is null||match.Name!=nameField)||
            matches.Select(match=>match.Page!.Id).Distinct(StringComparer.Ordinal).Count()!=1||
            !fixedList&&(selected.Length>0&&selected!=matches[0].Page!.Id||
                linked.Length>0&&linked!=matches[0].Page!.Id)||
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
        PageMetadata sourcePage,ColumnMetadata foreign,TableMetadata target,bool allowStoredTarget=false)
    {
        if(owner.IsRongyu!=0||sourcePage.ConfigTableId!=owner.Id||!IsProjectRole(owner,foreign,target,allowStoredTarget))return null;
        var pages=TargetPages(catalog,target);
        if(pages.Length==0)return null;
        using var document=Definition(foreign);
        var definition=document.RootElement;
        var members=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id))
            .Select(column=>column.ColumnName).ToArray();
        var fixedList = foreign.ControlKind=="List" && UsesFixedListCandidates(catalog);
        var (page,_)=TargetFor(catalog,sourcePage,foreign,definition,SelectedPage(foreign,definition,fixedList),
            NameField(definition),pages,members,fixedList:fixedList);
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
        var fixedQuery=UsesFixedListCandidates(catalog);
        var pages=sourcePage is null?catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id).ToArray():[sourcePage];
        var definitions=pages.Select(page=>
        {
            using var overlay=PageDetail(catalog,page,foreign);
            return Normalize(EffectiveListWhereSql(definition.RootElement,overlay?.RootElement,fixedQuery));
        }).Distinct(StringComparer.Ordinal).ToArray();
        if(definitions.Length>1)
            throw new InvalidOperationException("同一物理名称副本的来源页附加条件必须一致。");
        return FixedConditionPlan.Compile(definitions.SingleOrDefault(),members);

        string Normalize(string text)=>string.Join(" AND ",FixedConditionPlan.Compile(text,members).Terms
            .Where(term=>GlobalCrossTableTarget.Declared(target)||!ProjectTerm(term))
            .Select(term=>term.ToDefinition()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>无父字典认领物理常量和原账号等值；维护编译再证明所有来源页使用同一持久归属列。</summary>
    private static bool HasScopedUnparentedSelection(MetadataCatalog catalog,TableMetadata target,PageMetadata page,
        IReadOnlyList<ColumnMetadata> columns,string[] members)
    {
        if(page.Extra.GetValueOrDefault("type").ToString()!="1"||
            !string.IsNullOrWhiteSpace(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString())&&
                !HasPhysicalSingleExact(catalog,target,page,"SelectPage"))
            return false;
        var terms=FixedConditionPlan.Compile(page.WhereSql,members).Terms.Where(term=>!ProjectTerm(term)).ToArray();
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        return terms.Length>0 && terms.All(term=>columns.Any(column=>
            column.ColumnName==term.Field&&GlobalPhysicalRelationValue.IsScalar(storage,target,column)&&
            (term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable)||
                term.Field!="yz_project_id"&&column.LogicalType is "int32" or "int64" or "string"&&
                term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} account]}&&
                AccountConditionVariables.IsPageField(account.Text,term.Field))));
    }

    /// <summary>顺着已声明的单值路径找到需要持久化的范围，循环留给与读取相同的拒绝门禁。</summary>
    private static bool NamePathHasScope(MetadataCatalog catalog,TableMetadata table,PageMetadata page,string[] path,
        HashSet<(string Table,string Field)> active)
    {
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        if(table.IsRongyu!=0)return false;
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
            if(target is null)return false;
            if(GlobalCrossTableTarget.Declared(target))
            {
                var global=GlobalSingleRelationTarget.Require(catalog,table,foreign,target,page);
                return global.RequiresScopedPage||global.CacheControl is {Terms.Count:>0}||
                    (tail.Length==1&&tail[0]==global.Name.ColumnName?global.NestedCacheScope:
                        NamePathHasScope(catalog,target,global.Page,tail,active));
            }
            return NestedPage(catalog,table,page,foreign,target) is {} next&&
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
        if (name.Length == 0) name = MetadataNames.LegacySingleTargetName(foreign);
        return catalog.ConfigTables.SingleOrDefault(table=>table.TableName==name);
    }

    /// <summary>下拉回退与原页面一致，同后台优先且不选择带父参数的候选页。</summary>
    private static (PageMetadata? Page,string Name) TargetFor(MetadataCatalog catalog,PageMetadata sourcePage,
        ColumnMetadata foreign,JsonElement definition,string selected,string name,PageMetadata[] pages,string[] members,
        bool integerId=false,bool fixedList=false)
    {
        using var overlay=PageDetail(catalog,sourcePage,foreign);
        if(overlay is not null)
        {
            var pageId=foreign.ControlKind=="SelectPage"?Value(overlay.RootElement,"pageId"):"";
            var label=Value(overlay.RootElement,"nameField");
            if(fixedList) selected=FixedListPageId(foreign,definition,overlay.RootElement);
            else if(pageId.Length>0)selected=pageId;
            // 只接受下拉或真实 type=1 页的主键显示，切换目标页仍按下方既有一致性规则核验。
            var displaysId=integerId&&label=="id"&&(foreign.ControlKind=="List"||
                pages.Any(page=>page.Id==selected&&page.Extra.GetValueOrDefault("type").ToString()=="1"));
            if(label.Length>0&&!displaysId)name=label;
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

    private static bool IsProjectRole(TableMetadata owner,ColumnMetadata foreign,TableMetadata target,bool allowStoredTarget=false)=>
        (target.IsRongyu==0||allowStoredTarget&&target.IsRongyu==1)&&
        !GlobalCrossTableTarget.Declared(owner)&&!GlobalCrossTableTarget.Declared(target)&&
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

    private static string SelectedPage(ColumnMetadata foreign,JsonElement definition,bool fixedList)
    {
        if (fixedList) return FixedListPageId(foreign,definition,null);
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
