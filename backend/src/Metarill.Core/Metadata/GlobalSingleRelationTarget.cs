using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

public sealed record GlobalSingleRelationPlan(PageMetadata Page,ColumnMetadata Name)
{
    /// <summary>此目标页必须通过完整自树路径执行条件，不能回退到物理列查询。</summary>
    public bool ScopedSelfTree { get; init; }
    /// <summary>跨表范围须以完整的稀疏路径还原实际层位。</summary>
    public bool ScopedCrossTable { get; internal init; }
    /// <summary>纯物理的账号条件同样必须按已发布页绑定。</summary>
    public bool ScopedAccount { get; internal init; }
    /// <summary>全局冗余单选可只使用实际候选页的固定常量范围，缓存不需要来源行账号列。</summary>
    public bool FixedSelectionPage { get; internal init; }
    /// <summary>单选页已明确要求当前项目，实时名称必须沿实际字典行归属读取。</summary>
    public bool ProjectSelectionPage { get; internal init; }
    /// <summary>名称读取和维护从来源行取得持久父键，候选才使用实际父参数。</summary>
    public MultiTargetParentScope? Parent { get; internal init; }
    /// <summary>来源父键的存储方式及其实际页面由同一发布计划证明。</summary>
    public MultiSourceParent? SourceParent { get; internal init; }
    public MultiTargetParentScope? PhysicalParent=>Parent is {Logical:false}?Parent:null;
    public MultiTargetParentScope? AccountParent=>ScopedAccount?PhysicalParent:null;
    /// <summary>已发布单选使用实际物理父键，名称读取不依赖账号条件是否存在。</summary>
    public bool ScopedPhysicalSelectionParent=>PhysicalParent is not null&&Page.Extra.GetValueOrDefault("type").ToString()=="1";
    public bool ScopedLogicalPage=>ScopedSelfTree||ScopedCrossTable;
    /// <summary>候选、名称联接和回填均须执行该页的实际范围。</summary>
    public bool RequiresScopedPage=>ScopedLogicalPage||ScopedAccount||FixedSelectionPage||ScopedPhysicalSelectionParent;

    /// <summary>同一物理副本的各来源页必须使用等价的下拉附加条件。</summary>
    public FixedConditionPlan? CacheControl { get; internal init; }

    /// <summary>名称联接的中间页面及其控件范围也参与副本重算。</summary>
    internal bool NestedCacheScope { get; init; }

    /// <summary>页面或控件条件决定副本可见性，由统一维护计划重算。</summary>
    public bool RequiresScopedCache=>RequiresScopedPage||NestedCacheScope||CacheControl is {Terms.Count:>0}||
        UsesListPageScope&&!string.IsNullOrWhiteSpace(Page.WhereSql);

    /// <summary>全局下拉有同实体的替代页面绑定，读取和副本均须使用实际页。</summary>
    public bool AlternateListPage { get; internal init; }

    internal bool PageOnlyList { get; init; }
    internal bool StoredListAccount { get; init; }

    /// <summary>固定下拉按实际页面解析目标实体或选择替代页时，该页条件贯穿候选、名称读取和维护。</summary>
    public bool UsesListPageScope => AlternateListPage || PageOnlyList || StoredListAccount;
}

/// <summary>普通单值全局关系的静态证明，目标和页面必须来自同一明确发布目录。</summary>
public static class GlobalSingleRelationTarget
{
    /// <summary>只允许普通R0来源的物理单值外键；缺项目列不能自动变成全局许可。</summary>
    public static GlobalSingleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,false);
    /// <summary>从已发布的实际中间页核对原下拉条件，并继续证明全局名称路径。</summary>
    public static GlobalSingleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,PageMetadata sourcePage)
    {
        if(sourcePage.ConfigTableId!=owner.Id||
            !ReferenceEquals(catalog.ConfigPages.SingleOrDefault(page=>page.Id==sourcePage.Id),sourcePage))
            throw new InvalidOperationException("全局单值关系的来源页必须属于该外键及当前发布目录。");
        return RequireSource(catalog,owner,foreign,target,false,sourcePage:sourcePage);
    }

    /// <summary>多值名称路径沿实际中间页使用全局冗余字典的固定页条件及 List 覆盖。</summary>
    public static (PageMetadata Page,FixedConditionPlan Control)? StoredMultiName(MetadataCatalog catalog,
        TableMetadata owner,PageMetadata sourcePage,ColumnMetadata foreign,TableMetadata target)
    {
        if(owner.IsRongyu!=0||target.IsRongyu!=1||!GlobalCrossTableTarget.Declared(target))return null;
        var proof=Require(catalog,owner,foreign,target,sourcePage);
        if(proof.Parent is not null||proof.ScopedLogicalPage||GlobalCrossTableTarget.DeclaresExactSingle(target,proof.Page))
            return null;
        var control=ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,foreign,target,sourcePage);
        return control.Terms.Count>0||!string.IsNullOrWhiteSpace(proof.Page.WhereSql)?(proof.Page,control):null;
    }

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
    private static GlobalSingleRelationPlan RequireSource(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,bool cached,ISet<string>? resolving=null,
        PageMetadata? sourcePage=null)
    {
        var fields=catalog.ConfigColumns.Where(column=>column.ConfigTableId==owner.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,owner);var project=fields.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        // 带行项目列的来源先证明物理存储，再证明目标固定范围或既有自树父角色。
        if(!cached&&GlobalCrossTableTarget.Declared(owner)&&owner.IsRongyu==0)GlobalDerivedRelationSource.RequireSingle(catalog,owner,foreign);
        else if(cached&&GlobalCrossTableTarget.Declared(owner))
        {
            GlobalCachedRelationSource.RequireCachedSingleSource(catalog,owner,foreign);
            if(project is not null&&owner.Id==target.Id)
                throw new NotSupportedException("带行项目列的全局缓存要求独立目标表。");
            var proof=RequirePublishedTarget(catalog,owner,foreign,target,resolving,allowScopedRowProject:true,cacheControl:true);
            if(project is not null&&!proof.RequiresScopedCache)GlobalCachedRelationSource.RequireSingle(catalog,owner,foreign);
            return proof;
        }
        else if(owner.IsRongyu!=(cached?1:0)||GlobalCrossTableTarget.Declared(owner)||owner.IsView!=0||owner.IsFunctionTable!=0||owner.YzProjectId!=catalog.YzProjectId||project is null||project.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,owner,project))throw new NotSupportedException("全局单值关系要求明确发布且有真实项目列的普通R0来源或已证明的R1消费者。");
        return RequirePublishedTarget(catalog,owner,foreign,target,resolving,cacheControl:cached,sourcePage:sourcePage);
    }
    /// <summary>从原控件的表声明或目标页定位候选实体，实际页面范围和冲突声明继续由发布证明核对。</summary>
    internal static TableMetadata ResolveTarget(MetadataCatalog catalog,ColumnMetadata foreign)
    {
        using var document=ParseDefinition(catalog,foreign);
        var definition=document.RootElement;
        if(definition.ValueKind!=JsonValueKind.Object||
            definition.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())
            throw Invalid();
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null
            ?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw Invalid();

        var id=foreign.LinkConfigTableId.Length>0?foreign.LinkConfigTableId:Value("tableId");
        var page=Value("pageId");
        if(page.Length==0)page=foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        if(id.Length==0&&page.Length>0)
            id=catalog.ConfigPages.SingleOrDefault(candidate=>candidate.Id==page)?.ConfigTableId??throw Invalid();

        var table=Value("table");
        if(table.Length==0)table=foreign.Extra.GetValueOrDefault("joinTable").ToString();
        if(table.Length==0)table=foreign.ColumnName[..^3];
        var matches=catalog.ConfigTables.Where(candidate=>id.Length>0?candidate.Id==id:candidate.TableName==table).ToArray();
        return matches.Length==1?matches[0]:throw Invalid();

        static NotSupportedException Invalid()=>new("全局单值关系须指定当前目录的有效目标表或页面。");
    }

    /// <summary>旧固定下拉可只在关系字段保存关联页，空明细采用原默认名称。</summary>
    private static JsonDocument ParseDefinition(MetadataCatalog catalog,ColumnMetadata foreign)
    {
        var linkedPage=foreign.Extra.GetValueOrDefault("linkConfigPageId").ToString();
        var linkedOnly=foreign.ControlKind=="List"&&string.IsNullOrWhiteSpace(foreign.ControlDetail)&&
            !string.IsNullOrWhiteSpace(linkedPage)&&ProjectScopedSingleRelationTarget.UsesFixedListCandidates(catalog);
        return JsonDocument.Parse(linkedOnly?"{}":foreign.ControlDetail);
    }

    /// <summary>单值路径共用完整目标证明；来源资格由各自R0实时或R1缓存入口先行核对。</summary>
    internal static GlobalSingleRelationPlan RequirePublishedTarget(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,ISet<string>? resolving,bool projectParent=false,bool allowScopedRowProject=false,bool cacheControl=false,PageMetadata? sourcePage=null)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,owner);
        if(foreign.ConfigTableId!=owner.Id||foreign.ControlKind is not("List" or "SelectPage")||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!storage.IsPhysical(foreign)||foreign.IsVirtualColumn!=0)throw new NotSupportedException("全局单值关系必须使用本表物理List或SelectPage外键。");
        using var document=ParseDefinition(catalog,foreign);var definition=document.RootElement;if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局关系定义必须为无重复键对象。");
        static string ReadValue(JsonElement scope,string key)=>!scope.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null
            ?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():
                throw new InvalidOperationException("全局关系标识必须是固定标量。");
        string Value(string key)=>ReadValue(definition,key);
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");
        var tableName=Value("table");
        var declaredPage=Value("pageId");
        var linkedPage=Extra("linkConfigPageId");
        var fixedList=!projectParent&&foreign.ControlKind=="List"&&ProjectScopedSingleRelationTarget.UsesFixedListCandidates(catalog);
        if(tableId.Length>0&&tableId!=target.Id||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||
            tableName.Length>0&&tableName!=target.TableName||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||
            !fixedList&&declaredPage.Length>0&&linkedPage.Length>0&&declaredPage!=linkedPage)
            throw new InvalidOperationException("全局关系的表、页面或显示来源声明相互冲突。");
        var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id).ToArray();
        var baseline=declaredPage.Length>0?declaredPage:linkedPage;
        var pageId=baseline;
        var independentPages=false;
        if(fixedList)
        {
            // 非冗余读取可使用实际来源页；没有实际页的读取与持久副本需要唯一固定目标。
            if(new[]{declaredPage,linkedPage}.Any(id=>id.Length>0&&!pages.Any(page=>page.Id==id)))
                throw new InvalidOperationException("下拉的原关联页及配置页必须属于同一全局目标表。");
            var selected=catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id)
                .Select(page=>ProjectScopedSingleRelationTarget.ListPageId(catalog,page,foreign))
                .Distinct(StringComparer.Ordinal).ToArray();
            independentPages=selected.Length>1;
            if(independentPages&&(cacheControl||owner.IsRongyu!=0||sourcePage is null))
                throw new InvalidOperationException("不同来源页的全局下拉需要实际非冗余来源页；物理名称副本必须使用同一目标页。");
            pageId=independentPages?ProjectScopedSingleRelationTarget.ListPageId(catalog,sourcePage!,foreign):
                selected.SingleOrDefault()??baseline;
        }
        var alternateListPage=fixedList&&(independentPages||pageId.Length>0&&(pageId!=baseline||
            declaredPage.Length>0&&linkedPage.Length>0&&declaredPage!=linkedPage));
        // 只有页面声明时，实体通过该真实页面发布，不能沿无页面的字典联接取名称。
        var pageOnlyList=fixedList&&pageId.Length>0&&tableId.Length==0&&tableName.Length==0&&
            foreign.LinkConfigTableId.Length==0;
        pages=pages.Where(page=>pageId.Length==0||page.Id==pageId).ToArray();
        if(pages.Length!=1)
            throw new InvalidOperationException("全局单值关系必须指定真实目标页，省略时目标页必须唯一。");
        var page=pages[0];
        var targetColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var expanded=RelationStorageColumns.Expand(target,targetColumns);
        var condition=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
        // 行项目列约束来源数据，已证明的全局目标仍执行实际候选页条件。
        var allowsLogical=!projectParent&&(allowScopedRowProject||owner.IsRongyu==0||!GlobalCrossTableTarget.Declared(owner)||
            !catalog.ConfigColumns.Any(column=>column.ConfigTableId==owner.Id&&column.ColumnName=="yz_project_id"));
        var scopedSelfTree=allowsLogical&&condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,field));
        var scopedCrossTable=allowsLogical&&condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(target,expanded,field));
        // 冗余消费者已通过物理来源证明，固定到同一发布页后由回填计划核对所有入口的持久归属。
        var boundStoredList=owner.IsRongyu==0?sourcePage is not null:
            cacheControl&&pageId.Length>0;
        var storedListAccount=allowsLogical&&target.IsRongyu==1&&fixedList&&boundStoredList&&
            condition.Variables.Any(AccountConditionVariables.IsSupported);
        var usesListPageScope=alternateListPage||pageOnlyList||storedListAccount;
        // 原弹窗直达显式发布的 type=1 单选页，账号条件与真实单值关系和名称副本共用。
        var storedSelectionAccount=allowsLogical&&target.IsRongyu==1&&foreign.ControlKind=="SelectPage"&&
            pageId.Length>0&&page.Extra.GetValueOrDefault("type").ToString()=="1"&&
            condition.Variables.Any(AccountConditionVariables.IsSupported);
        var scopedAccount=storedListAccount||storedSelectionAccount||allowsLogical&&target.IsRongyu==0&&!scopedSelfTree&&!scopedCrossTable&&
            condition.Variables.Any(AccountConditionVariables.IsSupported);
        // 原下拉可以引用业务页；弹窗单选继续要求实际发布的 type=1 页面。
        var exactSingle=GlobalCrossTableTarget.DeclaresExactSingle(target,page)&&
            (foreign.ControlKind=="List"||page.Extra.GetValueOrDefault("type").ToString()=="1");
        var fixedParent=exactSingle?GlobalCrossTableTarget.ExactSingleParent(catalog,target,page,resolving):
            allowsLogical?GlobalCrossTableTarget.SingleParent(catalog,target,page):null;
        var sourceParent=sourcePage is not null&&usesListPageScope
            ?fixedParent?.SingleSource(catalog,owner,foreign,sourcePage)
            :fixedParent?.SingleSource(catalog,owner,foreign);
        var projectSelectionPage=foreign.ControlKind=="SelectPage"&&pageId.Length>0&&
            GlobalCrossTableTarget.IsProjectSinglePage(target,page,condition);
        if(projectParent)RequireProjectParentPage(catalog,target,page);
        else if(projectSelectionPage)GlobalCrossTableTarget.RequireProjectSingleCandidate(catalog,target,page);
        else if(!exactSingle&&fixedParent is null)
        {
            if(scopedSelfTree)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,target,page);
            else if(scopedCrossTable)GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,target,page);
            else if(scopedAccount)GlobalCrossTableTarget.RequireAccountCandidate(catalog,target,page);
            else GlobalCrossTableTarget.RequireSingle(catalog,target,page);
        }
        var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);var nameField=Value("nameField");if(nameField.Length==0)nameField="name";var name=targetColumns.SingleOrDefault(column=>column.ColumnName==nameField);
        bool Scalar(ColumnMetadata column)=>GlobalPhysicalRelationValue.IsScalar(targetStorage,target,column);
        var key=targetColumns.SingleOrDefault(column=>column.ColumnName=="id");
        if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))
            throw new InvalidOperationException("全局单值目标必须使用非空int64物理主键。");
        // 原单选可显示字典主键。各存储模式仍须通过本目录、物理外键和发布页范围证明。
        var selectPage=foreign.ControlKind=="SelectPage"&&page.Extra.GetValueOrDefault("type").ToString()=="1";
        var allowsIdDisplay=!projectParent&&foreign.LogicalType=="int64"&&
            (foreign.ControlKind=="List"&&!GlobalCrossTableTarget.Declared(owner)||selectPage);
        // 普通与显式全局来源使用同一终点证明；来源的存储模式和发布身份已由各入口核验。
        var typedName = name is not null && name.ColumnName != "id" && MultiValueLabel.IsTyped(name.LogicalType) &&
            owner.IsRongyu is 0 or 1;
        var hasNameCache=cacheControl&&(target.IsRongyu==0||usesListPageScope)&&name is not null&&name!=key&&
            catalog.ConfigColumns.Any(column=>column.ConfigTableId==owner.Id&&column.ColumnName==foreign.ColumnName[..^3]+"_"+name.ColumnName);
        // 冗余外键即使未声明本地名称副本，仍按已经固定的目标页证明派生名称。
        var namePage=cacheControl||sourcePage is not null?page:null;
        var nestedScope=false;
        if (name is null || name.LogicalType != "string" && (!typedName||projectParent) && !(allowsIdDisplay&&name==key) ||
            !(projectParent?Scalar(name):GlobalPhysicalRelationValue.IsPublishedScalar(catalog,targetStorage,target,name,
                resolving??new HashSet<string>(StringComparer.Ordinal),namePage,out nestedScope)))
            throw new NotSupportedException("全局单值名称要求已证明的非凭据单值标量。");
        // 逻辑范围只允许已声明的级联字段，其他成员仍按非凭据物理标量验证。
        foreach(var field in condition.Fields)
        {
            var column=expanded.Single(item=>item.ColumnName==field);
            if(Scalar(column))continue;
            if(!ExactDataManageValues.IsSensitiveField(field)&&
                (scopedSelfTree&&CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,field)||
                    scopedCrossTable&&CrossTableStorageColumns.IsLogicalPathField(target,expanded,field)))continue;
            throw new NotSupportedException("目标条件必须使用已证明的非凭据标量或逻辑级联字段。");
        }
        // 只有明确声明了物理名称副本时，才要求所有业务入口的附加条件等价。
        var control=foreign.ControlKind!="List"||target.IsRongyu!=0&&!usesListPageScope?null:
            sourcePage is not null?ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,foreign,target,sourcePage):
            hasNameCache?ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,foreign,target):null;
        // 普通项目的冗余名称副本可绑定无条件全局字典，保留来源行项目和已发布目标身份。
        var globalOwner=GlobalCrossTableTarget.Declared(owner);
        var unfilteredStoredSelection=cacheControl&&!globalOwner&&target.IsRongyu==1&&selectPage&&
            condition.Terms.Count==0&&string.IsNullOrWhiteSpace(Value("whereSql"))&&
            targetColumns.Any(column=>column.ColumnName=="yz_project_id");
        // 物理常量已通过实际发布页和字段证明。普通冗余来源同样使用此范围执行保存和名称维护。
        var fixedSelectionPage=projectSelectionPage||unfilteredStoredSelection||cacheControl&&owner.IsRongyu==1&&selectPage&&
            condition.Terms.Count>0&&condition.Variables.Count==0&&
            (globalOwner||!exactSingle&&!scopedCrossTable&&!scopedSelfTree);
        // 实时按实际页证明表和名称。冻结的物理副本仍需跨所有入口使用同一范围。
        var allSourcePages=!independentPages&&(usesListPageScope||fixedParent is not null||!globalOwner&&fixedSelectionPage||
            sourcePage is null&&(nestedScope||control is {Terms.Count:>0}));
        if(allSourcePages||sourcePage is not null)
        {
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId==foreign.Id&&
                (allSourcePages||field.ConfigPageId==sourcePage!.Id)))
            {
                var detail=field.Extra.GetValueOrDefault("controlDetail").ToString();
                if(string.IsNullOrWhiteSpace(detail))continue;
                using var overlay=JsonDocument.Parse(detail);
                var scope=overlay.RootElement;
                if(scope.ValueKind!=JsonValueKind.Object||scope.EnumerateObject().GroupBy(item=>item.Name,StringComparer.Ordinal).Any(group=>group.Count()>1)||
                    ReadValue(scope,"pageId") is {Length:>0} selectedPage&&selectedPage!=page.Id||
                    ReadValue(scope,"tableId") is {Length:>0} selectedTable&&selectedTable!=target.Id||
                    ReadValue(scope,"table") is {Length:>0} selectedName&&selectedName!=target.TableName||
                    ReadValue(scope,"nameField") is {Length:>0} selectedField&&selectedField!=name.ColumnName&&!(allowsIdDisplay&&selectedField=="id"))
                    throw new InvalidOperationException("全局单值关系范围要求来源页使用已发布的目标和名称。");
            }
        }
        var plan=new GlobalSingleRelationPlan(page,name){ScopedSelfTree=scopedSelfTree,ScopedCrossTable=scopedCrossTable,
            ScopedAccount=scopedAccount,FixedSelectionPage=fixedSelectionPage,ProjectSelectionPage=projectSelectionPage,
            Parent=fixedParent,SourceParent=sourceParent,
            CacheControl=control,NestedCacheScope=nestedScope,
            AlternateListPage=alternateListPage,PageOnlyList=pageOnlyList,StoredListAccount=storedListAccount};
        if(owner.IsRongyu==1&&GlobalCrossTableTarget.Declared(owner)&&foreign.ControlKind=="SelectPage"&&
            (!plan.RequiresScopedCache||page.Extra.GetValueOrDefault("type").ToString()!="1"))
            throw new NotSupportedException("全局冗余单选要求有固定数据范围的已发布type=1目标页。");
        return plan;
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
