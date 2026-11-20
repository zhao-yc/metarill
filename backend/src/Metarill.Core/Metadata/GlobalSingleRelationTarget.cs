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
    /// <summary>名称读取和维护从来源行取得持久父键，候选才使用实际父参数。</summary>
    public MultiTargetParentScope? Parent { get; internal init; }
    /// <summary>来源父键的存储方式及其实际页面由同一发布计划证明。</summary>
    public MultiSourceParent? SourceParent { get; internal init; }
    public MultiTargetParentScope? PhysicalParent=>Parent is {Logical:false}?Parent:null;
    public MultiTargetParentScope? AccountParent=>ScopedAccount?PhysicalParent:null;
    public bool ScopedLogicalPage=>ScopedSelfTree||ScopedCrossTable;
    /// <summary>候选、名称联接和回填均须执行该页的实际范围。</summary>
    public bool RequiresScopedPage=>ScopedLogicalPage||ScopedAccount;

    /// <summary>同一物理副本的各来源页必须使用等价的下拉附加条件。</summary>
    public FixedConditionPlan? CacheControl { get; internal init; }

    /// <summary>名称联接的中间页面及其控件范围也参与副本重算。</summary>
    internal bool NestedCacheScope { get; init; }

    /// <summary>页面或控件条件决定副本可见性，由统一维护计划重算。</summary>
    public bool RequiresScopedCache=>RequiresScopedPage||NestedCacheScope||CacheControl is {Terms.Count:>0};
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
        using var document=JsonDocument.Parse(foreign.ControlDetail);
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

    /// <summary>单值路径共用完整目标证明；来源资格由各自R0实时或R1缓存入口先行核对。</summary>
    internal static GlobalSingleRelationPlan RequirePublishedTarget(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target,ISet<string>? resolving,bool projectParent=false,bool allowScopedRowProject=false,bool cacheControl=false,PageMetadata? sourcePage=null)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,owner);
        if(foreign.ConfigTableId!=owner.Id||foreign.ControlKind is not("List" or "SelectPage")||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!storage.IsPhysical(foreign)||foreign.IsVirtualColumn!=0)throw new NotSupportedException("全局单值关系必须使用本表物理List或SelectPage外键。");
        using var document=JsonDocument.Parse(foreign.ControlDetail);var definition=document.RootElement;if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局关系定义必须为无重复键对象。");
        static string ReadValue(JsonElement scope,string key)=>!scope.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null
            ?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():
                throw new InvalidOperationException("全局关系标识必须是固定标量。");
        string Value(string key)=>ReadValue(definition,key);
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");var tableName=Value("table");var declaredPage=Value("pageId");var linkedPage=Extra("linkConfigPageId");
        if(tableId.Length>0&&tableId!=target.Id||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||tableName.Length>0&&tableName!=target.TableName||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||declaredPage.Length>0&&linkedPage.Length>0&&declaredPage!=linkedPage)throw new InvalidOperationException("全局关系的表、页面或显示来源声明相互冲突。");
        var pageId=declaredPage.Length>0?declaredPage:linkedPage;var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&(pageId.Length==0||page.Id==pageId)).ToArray();if(pages.Length!=1)throw new InvalidOperationException("全局单值关系必须指定真实目标页，省略时目标页必须唯一。");var page=pages[0];
        var targetColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var expanded=RelationStorageColumns.Expand(target,targetColumns);
        var condition=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
        // 行项目列约束来源数据，已证明的全局目标仍执行实际候选页条件。
        var allowsLogical=!projectParent&&(allowScopedRowProject||owner.IsRongyu==0||!GlobalCrossTableTarget.Declared(owner)||
            !catalog.ConfigColumns.Any(column=>column.ConfigTableId==owner.Id&&column.ColumnName=="yz_project_id"));
        var scopedSelfTree=allowsLogical&&condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,field));
        var scopedCrossTable=allowsLogical&&condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(target,expanded,field));
        var scopedAccount=allowsLogical&&target.IsRongyu==0&&!scopedSelfTree&&!scopedCrossTable&&
            condition.Variables.Any(AccountConditionVariables.IsSupported);
        var exactSingle=foreign.ControlKind=="SelectPage"&&GlobalCrossTableTarget.DeclaresExactSingle(target,page);
        var fixedParent=exactSingle?GlobalCrossTableTarget.ExactSingleParent(catalog,target,page):
            allowsLogical?GlobalCrossTableTarget.SingleParent(catalog,target,page):null;
        var sourceParent=fixedParent?.SingleSource(catalog,owner,foreign);
        if(projectParent)RequireProjectParentPage(catalog,target,page);
        else if(!exactSingle&&fixedParent is null)
        {
            if(scopedSelfTree)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,target,page);
            else if(scopedCrossTable)GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,target,page);
            else if(scopedAccount)GlobalCrossTableTarget.RequireAccountCandidate(catalog,target,page);
            else GlobalCrossTableTarget.RequireSingle(catalog,target,page);
        }
        var targetStorage=MetadataStoragePolicy.ForTable(catalog,target);var nameField=Value("nameField");if(nameField.Length==0)nameField="name";var name=targetColumns.SingleOrDefault(column=>column.ColumnName==nameField);
        bool Scalar(ColumnMetadata column)=>GlobalPhysicalRelationValue.IsScalar(targetStorage,target,column);
        // 普通与显式全局来源使用同一终点证明；来源的存储模式和发布身份已由各入口核验。
        var typedName = name is not null && name.ColumnName != "id" && MultiValueLabel.IsTyped(name.LogicalType) &&
            owner.IsRongyu is 0 or 1;
        var hasNameCache=cacheControl&&target.IsRongyu==0&&name is not null&&
            catalog.ConfigColumns.Any(column=>column.ConfigTableId==owner.Id&&column.ColumnName==foreign.ColumnName[..^3]+"_"+name.ColumnName);
        var nestedScope=false;
        if (name is null || name.LogicalType != "string" && (!typedName||projectParent) ||
            !(projectParent?Scalar(name):GlobalPhysicalRelationValue.IsPublishedScalar(catalog,targetStorage,target,name,
                resolving??new HashSet<string>(StringComparer.Ordinal),hasNameCache||sourcePage is not null?page:null,out nestedScope)))
            throw new NotSupportedException("全局单值名称要求已证明的非凭据单值标量。");
        var key=targetColumns.SingleOrDefault(column=>column.ColumnName=="id");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))throw new InvalidOperationException("全局单值目标必须使用非空int64物理主键。");
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
        var control=foreign.ControlKind!="List"||target.IsRongyu!=0?null:
            sourcePage is not null?ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,foreign,target,sourcePage):
            hasNameCache?ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,foreign,target):null;
        // 实时名称和回填使用同一张发布页，禁止任何来源页覆盖表、名称或父键所属的目标页。
        var allSourcePages=fixedParent is not null||sourcePage is null&&(nestedScope||control is {Terms.Count:>0});
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
                    ReadValue(scope,"nameField") is {Length:>0} selectedField&&selectedField!=name.ColumnName)
                    throw new InvalidOperationException("全局单值关系范围要求来源页使用已发布的目标和名称。");
            }
        }
        var plan=new GlobalSingleRelationPlan(page,name){ScopedSelfTree=scopedSelfTree,ScopedCrossTable=scopedCrossTable,
            ScopedAccount=scopedAccount,Parent=fixedParent,SourceParent=sourceParent,CacheControl=control,NestedCacheScope=nestedScope};
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
