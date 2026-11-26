using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

public sealed record GlobalMultipleRelationPlan(PageMetadata Page,ColumnMetadata Name)
{
    public bool UsesStoredStaticPage { get; init; }
    public bool UsesProjectPage { get; init; }
    public bool UsesNonRedundantAccountPage { get; init; }
    public bool UsesNonRedundantExactCheckboxPage { get; init; }
    public bool UsesStoredExactCheckboxPage { get; init; }
    public bool UsesStoredAccountPage { get; init; }
    public bool UsesNonRedundantPhysicalParentPage { get; init; }
    public bool UsesStoredPhysicalParentPage { get; init; }
    public bool UsesPhysicalParentPage=>UsesNonRedundantPhysicalParentPage||UsesStoredPhysicalParentPage;
}

/// <summary>全局多值关系分别证明普通R0实时读取与普通R1缓存消费者，目标名称必须为明确发布的物理值、R1缓存或已证明的R0单值名称。</summary>
public static class GlobalMultipleRelationTarget
{
    /// <summary>读取原多选或复选定义；目标页面只是已发布关系归属，复选仍使用自己的原候选条件。</summary>
    public static GlobalMultipleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,false);
    /// <summary>非冗余多选或复选按实际来源页选择同一全局实体的另一发布页。</summary>
    public static GlobalMultipleRelationPlan RequirePage(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,PageMetadata sourcePage)
    {
        if(foreign.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase")||target.IsRongyu is not(0 or 1)||
            sourcePage.ConfigTableId!=owner.Id||sourcePage.YzProjectId!=catalog.YzProjectId||
            !ReferenceEquals(catalog.ConfigPages.SingleOrDefault(page=>page.Id==sourcePage.Id),sourcePage))
            throw new NotSupportedException("全局多值页面覆盖要求当前项目的非冗余来源和实际字典发布页。");
        return RequireSource(catalog,owner,foreign,target,cached:false,sourcePage:sourcePage);
    }

    /// <summary>冻结物理 R1 名称计划。来源必须有真实项目范围，全局来源另核对实际发布页。</summary>
    public static GlobalMultipleRelationPlan RequireCached(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,true);

    /// <summary>原多选的名称副本只从同一表级字典页读值，备用字段必须由实际来源页和目标投影共同证明。</summary>
    public static GlobalMultipleRelationPlan RequireCachedName(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,ColumnMetadata value,PageMetadata? sourcePage=null)
    {
        var baseline=RequireCached(catalog,owner,foreign,target);
        if(baseline.Name.Id==value.Id)return baseline;
        if(foreign.ControlKind=="MultiSelectPage")
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id&&page.YzProjectId==catalog.YzProjectId&&
                (sourcePage is null||ReferenceEquals(page,sourcePage))))
            {
                var actual=RequireSource(catalog,owner,foreign,target,true,page);
                if(actual.Page.Id==baseline.Page.Id&&actual.Name.Id==value.Id)return actual;
            }
        throw new InvalidOperationException("全局多值缓存名称与同一字典页的发布字段不一致。");
    }
    /// <summary>没有明确候选页时，保留全局复选名称的唯一发布要求。</summary>
    public static bool IsPublishedCheckboxText(MetadataCatalog catalog,TableMetadata target,ColumnMetadata value)
        =>IsPublishedCheckboxText(catalog,target,value,null);

    /// <summary>用复选实际发布页证明文本联接及同类型终点，不借用同表其它发布页。</summary>
    public static bool IsPublishedCheckboxText(MetadataCatalog catalog,TableMetadata target,ColumnMetadata value,PageMetadata? page)
    {
        if(value.LogicalType!="string"||target.IsRongyu!=0||!GlobalCrossTableTarget.Declared(target)||
            page is not null&&(page.ConfigTableId!=target.Id||!catalog.ConfigPages.Contains(page)))return false;
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        return GlobalPhysicalRelationValue.IsScalar(storage,target,value)||
            MultiNameStorageColumns.IsDerivedCheckboxText(target,storage,value)&&
            GlobalPhysicalRelationValue.IsPublishedScalar(catalog,storage,target,value,page);
    }

    /// <summary>原多选要求 type=2，数据库复选可以引用普通字典页，两个用途都必须证明物理账号等值。</summary>
    public static bool IsStoredAccountPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox=false)=>
        table.IsRongyu==1&&IsPublishedMultiPage(table,page,checkbox)&&
        condition.Variables.Any(AccountConditionVariables.IsSupported)&&condition.Terms.All(term=>
            IsProjectScope(term)||term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable)
                .All(value=>AccountConditionVariables.IsSupported(value.Text)));

    /// <summary>无父级冗余精确字典只接受固定条件、当前项目和已发布账号等值。</summary>
    public static bool IsStoredExactMultiAccountPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition)=>
        IsStoredExactMultiAccountPage(table,page,condition,checkbox:false);

    /// <summary>复选允许普通字典页，多选继续要求实际 type=2 页。</summary>
    public static bool IsStoredExactMultiAccountPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox)=>
        table.IsRongyu==1&&page.ConfigTableId==table.Id&&
        (checkbox?GlobalCrossTableTarget.DeclaresExactPage(table,page):GlobalCrossTableTarget.DeclaresExactMulti(table,page))&&
        condition.Terms.Any(IsAccountScope)&&condition.Terms.All(term=>IsFixedOrProject(term)||IsAccountScope(term));

    /// <summary>全局业务数组可使用实际发布的冗余或非冗余字典页，候选范围为固定物理常量或无条件。</summary>
    public static bool IsStoredStaticPage(TableMetadata table,PageMetadata page,FixedConditionPlan? condition,bool checkbox=false,bool allowExact=false)=>
        table.IsRongyu is 0 or 1&&IsPublishedMultiPage(table,page,checkbox,allowExact)&&(condition?.Variables.Count??0)==0;

    /// <summary>当前项目与固定条件可叠加实际发布的账号等值，冗余精确多选须同时证明账号范围。</summary>
    public static bool IsProjectPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox=false,bool allowExact=false)=>
        table.IsRongyu is 0 or 1&&IsPublishedMultiPage(table,page,checkbox,allowExact)&&condition.Terms.Any(IsProjectScope)&&
        (condition.Terms.All(term=>IsFixedOrProject(term)||table.IsRongyu==0&&IsAccountScope(term))||
            allowExact&&IsStoredExactMultiAccountPage(table,page,condition,checkbox));

    /// <summary>已发布的非冗余多值页可同时声明真实项目和父键，物理字段和父归属由调用方证明。</summary>
    public static bool IsProjectParentPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox=false,bool allowExact=false)=>
        table.IsRongyu==0&&IsPublishedMultiPage(table,page,checkbox,allowExact)&&UsesProjectAndParent(condition);

    /// <summary>冗余全局字典的父范围由实际发布页声明，项目列只在页面显式限定项目时使用。</summary>
    public static bool IsStoredParentPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,
        bool checkbox=false,bool allowExact=false)=>
        table.IsRongyu==1&&page.ConfigTableId==table.Id&&
        (IsPublishedMultiPage(table,page,checkbox)||allowExact&&(checkbox
            ?GlobalCrossTableTarget.DeclaresExactPage(table,page):GlobalCrossTableTarget.DeclaresExactMulti(table,page)))&&
        condition.Terms.Any(IsParentScope)&&
        condition.Terms.All(term=>IsFixedOrProject(term)||IsParentScope(term)||IsAccountScope(term));

    /// <summary>仅显式限定项目的冗余父范围页可以证明控件的项目条件。</summary>
    public static bool IsStoredProjectParentPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,
        bool checkbox=false,bool allowExact=false)=>
        IsStoredParentPage(table,page,condition,checkbox,allowExact)&&condition.Terms.Any(IsProjectScope);

    /// <summary>项目、父级和可选账号等值共用同一条件语法，各用途再验证实际页面角色。</summary>
    internal static bool UsesProjectAndParent(FixedConditionPlan condition)=>
        condition.Terms.Any(IsProjectScope)&&condition.Terms.Any(IsParentScope)&&
        condition.Terms.All(term=>IsFixedOrProject(term)||IsParentScope(term)||IsAccountScope(term));

    /// <summary>不限定字典项目的单值业务页可以组合物理父参数和实际账号等值。</summary>
    internal static bool UsesAccountAndParent(FixedConditionPlan condition)=>
        condition.Terms.Any(IsParentScope)&&condition.Terms.Any(IsAccountScope)&&
        condition.Terms.All(term=>term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable)||
            IsParentScope(term)||IsAccountScope(term));

    /// <summary>无父级的原单选可同时声明项目和受支持的账号等值，固定常量继续保留。</summary>
    internal static bool UsesSingleProjectVariables(FixedConditionPlan condition)=>
        condition.Terms.Any(IsProjectScope)&&condition.Terms.All(term=>IsFixedOrProject(term)||IsAccountScope(term));

    private static bool IsFixedOrProject(FixedConditionTerm term)=>
        IsProjectScope(term)||term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable);

    private static bool IsParentScope(FixedConditionTerm term)=>
        term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} value]}&&
            MultiTargetParentScope.IsParameter(value.Text);

    private static bool IsAccountScope(FixedConditionTerm term)=>
        term.Field!="yz_project_id"&&term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} value]}&&
            AccountConditionVariables.IsPageField(value.Text,term.Field);

    /// <summary>全局来源允许绑定仅按物理账号等值及常量限定的非冗余发布页，字典本身不限定项目。</summary>
    public static bool IsNonRedundantAccountPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox)=>
        IsNonRedundantAccountPage(table,page,condition,checkbox,allowExact:false);

    /// <summary>精确字典的角色在原选择时校验，缓存只能使用可从业务行重算的账号条件。</summary>
    private static bool IsNonRedundantAccountPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox,bool allowExact)=>
        table.IsRongyu==0&&IsPublishedMultiPage(table,page,checkbox,allowExact)&&condition.Variables.Count>0&&condition.Terms.All(term=>
            term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable)||
            term.Field!="yz_project_id"&&term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} value]}&&
                AccountConditionVariables.IsPageField(value.Text,term.Field));

    /// <summary>两类多值用途共用字典的页面资格，精确数据管理由独立合同验证。</summary>
    private static bool IsPublishedMultiPage(TableMetadata table,PageMetadata page,bool checkbox,bool allowExact=false)=>
        GlobalCrossTableTarget.Declared(table)&&page.ConfigTableId==table.Id&&
        (checkbox?page.Extra.GetValueOrDefault("type").ToString() is "" or "0" or "1" or "2":
            page.Extra.GetValueOrDefault("type").ToString()=="2")&&
        (string.IsNullOrWhiteSpace(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString())||
            allowExact&&(checkbox
                ?GlobalCrossTableTarget.DeclaresExactPage(table,page)
                :GlobalCrossTableTarget.DeclaresExactMulti(table,page)));

    /// <summary>全局字典的显式项目范围仅接受真实项目列与当前认证项目的等值。</summary>
    public static bool IsProjectScope(FixedConditionTerm term)=>
        term is {Field:"yz_project_id",Comparison:FixedConditionComparison.Equal,Values:[var value]}&&
        value.Kind==FixedConditionValueKind.Variable&&value.Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);

    /// <summary>实时派生与缓存维护分别验证来源存储模式，其余发布和终点约束完全相同。</summary>
    private static GlobalMultipleRelationPlan RequireSource(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,bool cached,PageMetadata? sourcePage=null)
    {
        var sourceColumns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==owner.Id).ToArray();var storage=MetadataStoragePolicy.ForTable(catalog,owner);var project=sourceColumns.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        if(owner.IsRongyu!=(cached?1:0)||owner.IsView!=0||owner.IsFunctionTable!=0||owner.YzProjectId!=catalog.YzProjectId||project is null||project.LogicalType is not("int64" or "string")||!GlobalPhysicalRelationValue.IsScalar(storage,owner,project))throw new NotSupportedException("全局多值关系要求明确发布且有真实项目列的来源。");
        if(foreign.ConfigTableId!=owner.Id||foreign.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase")||!foreign.ColumnName.EndsWith("_ids",StringComparison.Ordinal)||foreign.LogicalType!="string"||foreign.IsVirtualColumn!=0||!storage.IsPhysical(foreign))throw new NotSupportedException("全局多值关系必须使用本表物理JSON文本ID数组。");
        var checkbox=foreign.ControlKind=="MultiCheckBoxDatabase";
        var checkboxDefinition=checkbox?DatabaseCheckboxDefinition.Parse(foreign.ControlDetail):null;
        using var document=checkbox?null:JsonDocument.Parse(foreign.ControlDetail);
        var definition=checkboxDefinition?.ReadDefinition(foreign)??document!.RootElement;
        if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局多值定义必须为无重复键对象。");
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw new InvalidOperationException("全局多值映射必须是标量。");
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");var tableName=Value("table");var pageId=Value("pageId");var linkedPage=Extra("linkConfigPageId");
        if(tableId.Length>0&&tableId!=target.Id||tableName.Length>0&&tableName!=target.TableName||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||pageId.Length>0&&linkedPage.Length>0&&pageId!=linkedPage)throw new InvalidOperationException("全局多值的表、页面或显示来源声明冲突。");
        if(pageId.Length==0)pageId=linkedPage;
        string? pageName=null;
        if(sourcePage is not null)
        {
            var field=catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==sourcePage.Id&&item.ConfigColumnId==foreign.Id);
            var overlay=field?.Extra.GetValueOrDefault("controlDetail").ToString();
            if(checkboxDefinition is not null)
            {
                var actual=checkboxDefinition.WithPageOverride(overlay);
                pageName=actual.NameField;
                if(actual.TableId.Length>0&&actual.TableId!=target.Id||actual.Table.Length>0&&actual.Table!=target.TableName||
                    field?.Extra.GetValueOrDefault("controlKind").ToString() is {Length:>0} kind&&kind!=foreign.ControlKind)
                    throw new InvalidOperationException("全局复选页面覆盖必须沿用表级目标实体和控件。");
                if(actual.PageId.Length>0&&actual.PageId!=pageId)
                {
                    if(pageId.Length==0||!catalog.ConfigPages.Any(item=>item.Id==pageId&&item.ConfigTableId==target.Id))
                        throw new InvalidOperationException("全局复选需要同一字典表的表级发布页。");
                    pageId=actual.PageId;
                }
            }
            else if(!string.IsNullOrWhiteSpace(overlay))
            {
                using var documentOverlay=JsonDocument.Parse(overlay);
                var declared=documentOverlay.RootElement;
                if(declared.ValueKind!=JsonValueKind.Object||
                    declared.EnumerateObject().Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=declared.EnumerateObject().Count())
                    throw new InvalidOperationException("全局多选页面覆盖必须为无重复键对象。");
                string Pick(string key)
                {
                    if(!declared.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null)return "";
                    if(value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                        throw new InvalidOperationException("全局多选页面映射必须为固定标量。");
                    var text=value.ToString();
                    return string.IsNullOrWhiteSpace(text)?"":text;
                }
                if(Pick("tableId") is {Length:>0} overrideId&&overrideId!=target.Id||
                    Pick("table") is {Length:>0} overrideTable&&overrideTable!=target.TableName||
                    field!.Extra.GetValueOrDefault("controlKind").ToString() is {Length:>0} kind&&kind!=foreign.ControlKind)
                    throw new InvalidOperationException("全局多选页面覆盖必须沿用表级目标和控件。");
                var label=Value("nameField");
                if(Pick("nameField") is {Length:>0} overrideName&&overrideName!=(label.Length==0?"name":label))
                    pageName=overrideName;
                if(Pick("pageId") is {Length:>0} overridePage)
                {
                    if(pageId.Length==0||!catalog.ConfigPages.Any(item=>item.Id==pageId&&item.ConfigTableId==target.Id))
                        throw new InvalidOperationException("全局多选需要同一字典表的表级发布页。");
                    pageId=overridePage;
                }
            }
        }
        var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&(pageId.Length==0||page.Id==pageId)).ToArray();
        if(pages.Length!=1)throw new InvalidOperationException("全局多值关系必须明确指定真实目标页，省略时目标页必须唯一。");
        var page=pages[0];
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).ToArray();
        var expanded=RelationStorageColumns.Expand(target,columns);
        var condition=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
        if(sourcePage is not null&&(checkbox
            ?page.Extra.GetValueOrDefault("type").ToString() is not ("" or "0" or "1" or "2")
            :page.Extra.GetValueOrDefault("type").ToString()!="2"))
            throw new NotSupportedException("全局多值必须引用对应控件实际发布的字典页。");
        var joinedFields=GlobalCrossTableTarget.PageJoinedConditionFields(catalog,target,page);
        // 原多选按请求角色校验选值；无父级的冗余名称只使用发布页中可按来源行重现的条件。
        var storedExact=target.IsRongyu==1&&(checkbox
            ?GlobalCrossTableTarget.DeclaresExactPage(target,page)
            :cached&&GlobalCrossTableTarget.DeclaresExactMulti(target,page));
        var cachedNonRedundantExact=cached&&target.IsRongyu==0&&(checkbox
            ?GlobalCrossTableTarget.DeclaresExactPage(target,page)
            :GlobalCrossTableTarget.DeclaresExactMulti(target,page));
        var storedAccount=(checkbox||pageId.Length>0)&&(IsStoredAccountPage(target,page,condition,checkbox)||
            storedExact&&IsStoredExactMultiAccountPage(target,page,condition,checkbox));
        var selfTreeScope=condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,field));
        var scoped=selfTreeScope||target.IsRongyu==0&&condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(target,columns,field));
        var storedStatic=(checkbox||pageId.Length>0)&&(target.IsRongyu==1||!scoped)&&
            IsStoredStaticPage(target,page,condition,checkbox,allowExact:storedExact||cachedNonRedundantExact);
        var projectPage=(checkbox||pageId.Length>0)&&IsProjectPage(target,page,condition,checkbox,allowExact:storedExact||cachedNonRedundantExact);
        var nonRedundantAccountPage=(checkbox||pageId.Length>0)&&
            IsNonRedundantAccountPage(target,page,condition,checkbox,allowExact:cachedNonRedundantExact);
        var exactPage=checkbox?GlobalCrossTableTarget.DeclaresExactPage(target,page):
            GlobalCrossTableTarget.DeclaresExactMulti(target,page);
        // 实时多值关系的字典权限由实际发布页证明，来源仍须具有物理行项目列。
        var exactRealtime=exactPage&&!cached&&(target.IsRongyu==0||!checkbox&&target.IsRongyu==1);
        // 普通与全局业务来源共用真实同父证明；固定字典可以只声明父范围和字面条件。
        var projectParentPage=IsProjectParentPage(target,page,condition,checkbox,allowExact:exactPage);
        var staticParentPage=GlobalCrossTableTarget.UsesOnlyParentVariables(condition);
        // 精确字典可由物理门店或商户限定，普通业务来源仍需证明同一持久父键。
        var exactAccountParentPage=exactPage&&UsesAccountAndParent(condition);
        var physicalParentPage=false;
        var storedParent=(checkbox||pageId.Length>0)
            ?GlobalCrossTableTarget.StoredMultiParent(catalog,target,page,checkbox,allowExact:true):null;
        if(storedParent is not null)
        {
            // 写入和按来源行回填必须从真实字段得到同一个父键。
            var sourceParent=checkbox
                ?sourcePage is null?storedParent.CheckboxSource(catalog,owner,foreign):
                    storedParent.CheckboxSource(catalog,owner,foreign,sourcePage,allowFormParent:true)
                :sourcePage is null?storedParent.Source(catalog,owner,foreign):storedParent.Source(catalog,owner,foreign,sourcePage);
            if(sourceParent.LogicalPages.Count>0)
                throw new NotSupportedException("冗余全局字典要求已发布来源的实际物理父键。");
        }
        if((GlobalCrossTableTarget.Declared(owner)||projectParentPage||staticParentPage||exactAccountParentPage)&&
            (checkbox||pageId.Length>0)&&target.IsRongyu==0&&!scoped&&IsPublishedMultiPage(target,page,checkbox,allowExact:exactPage)&&
            (projectParentPage||staticParentPage||condition.Variables.Any(AccountConditionVariables.IsSupported))&&
            MultiTargetParentScope.CompileGlobalPage(catalog,target,condition,checkbox) is {Logical:false} parent&&
            parent.AdditionalTargets.All(scope=>!scope.Logical))
        {
            var sourceParent=checkbox
                ?sourcePage is null?parent.CheckboxSource(catalog,owner,foreign):
                    parent.CheckboxSource(catalog,owner,foreign,sourcePage,allowFormParent:exactPage)
                :sourcePage is null?parent.Source(catalog,owner,foreign):parent.Source(catalog,owner,foreign,sourcePage);
            // 多个来源物理键须经同一发布父页校验，原单父字段继续沿用已证明的持久键。
            physicalParentPage=sourceParent.LogicalPages.Count==0&&(sourceParent.AdditionalColumns.Count==0||parent.Global);
        }
        if(GlobalCrossTableTarget.Declared(owner))
        {
            // 业务来源仍按行项目隔离。物理副本只能使用维护时也能重现的固定字典范围。
            if(!storedAccount&&!storedStatic&&!projectPage&&!nonRedundantAccountPage&&!physicalParentPage&&
                storedParent is null&&!exactRealtime)
                throw new NotSupportedException("全局多值来源必须绑定受支持的已发布字典页。");
            GlobalDerivedRelationSource.RequireTable(catalog,owner);
        }
        if(cached&&checkbox&&(projectPage||physicalParentPage&&exactPage||storedParent is not null||storedExact&&(storedStatic||storedAccount)||
            cachedNonRedundantExact&&(storedStatic||nonRedundantAccountPage)||
            GlobalCrossTableTarget.Declared(owner)&&(storedStatic||nonRedundantAccountPage))&&
            !checkboxDefinition!.IsCoveredByStaticPage(condition,expanded.Select(column=>column.ColumnName)))
            throw new NotSupportedException("全局复选的冗余名称要求控件条件由实际发布字典页覆盖。");
        if(exactPage)
            _=GlobalCrossTableTarget.ExactPageParent(catalog,target,page);
        else if(selfTreeScope)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,target,page,allowMultiParent:true,checkbox:checkbox);
        else if(projectPage)
            GlobalCrossTableTarget.RequireProjectMultiCandidate(catalog,target,page,checkbox);
        else if(storedAccount)
            GlobalCrossTableTarget.RequireStoredMultiAccountCandidate(catalog,target,page);
        else if(storedParent is null)
        {
            // 冗余父范围已在实际发布目录中校验，其它用途继续走各自的证明。
            if(physicalParentPage||scoped||joinedFields.Count>0||target.IsRongyu==0&&condition.Variables.Any(AccountConditionVariables.IsSupported))
                GlobalCrossTableTarget.RequireMultiCandidate(catalog,target,page,checkbox);
            else GlobalCrossTableTarget.RequireMultiple(catalog,target,page);
        }
        if(target.IsRongyu==0&&foreign.ControlKind=="MultiSelectPage"&&page.Extra.GetValueOrDefault("type").ToString()!="2")
            throw new NotSupportedException("全局R0多选要求原type=2页面；复选继续使用独立候选入口。");
        var physical=MetadataStoragePolicy.ForTable(catalog,target);
        bool Scalar(ColumnMetadata column)=>GlobalPhysicalRelationValue.IsScalar(physical,target,column);
        // 实时复选沿来源页同时选定发布页和显示字段，持久副本仍使用表级字段。
        var nameField=pageName??Value("nameField");if(nameField.Length==0)nameField="name";
        var name=columns.SingleOrDefault(column=>column.ColumnName==nameField);
        var idLabel=name is not null&&name.ColumnName=="id"&&name.LogicalType=="int64"&&name.IsDbNotNull==1&&Scalar(name);
        // 物理列或固定联接名称由发布证明验证实际终点。
        var scalarLabel=name is not null&&(MultiNameStorageColumns.IsPhysicalScalarLabel(owner,foreign,target,name,physical)||
            MultiNameStorageColumns.IsDerivedScalarLabel(target,name,physical)||
            GlobalPhysicalRelationValue.IsPublishedTypedCache(catalog,physical,target,name));
        // 逻辑层名必须来自该全局候选自身的固定级联，实际路径和可见性由数据层完成证明。
        var pathLabel=scoped&&name is {LogicalType:"string",IsVirtualColumn:0}&&
            !ExactDataManageValues.IsSensitiveField(name.ColumnName)&&
            (CrossTableStorageColumns.IsLogicalPathField(target,columns,name.ColumnName)||
                CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,name.ColumnName));
        if(name is null||!idLabel&&name.LogicalType!="string"&&!scalarLabel||!idLabel&&!pathLabel&&!GlobalPhysicalRelationValue.IsPublishedScalar(catalog,physical,target,name,page))
            throw new NotSupportedException("全局多值名称必须为已证明的非凭据标量。");
        // 多选按实际字典页开放的字段投影，另一字典页的字段可见性不能替代本页证明。
        if(!checkbox&&pageName is not null&&!idLabel&&!catalog.ConfigPageColumns.Any(field=>
            field.ConfigPageId==page.Id&&field.ConfigColumnId==name.Id&&
            (Published(field,"isInGrid",field.IsShow)||Published(field,"isInEdit",field.IsEdit))))
            throw new NotSupportedException("全局多选页级名称未在实际字典页发布。");
        var key=columns.SingleOrDefault(column=>column.ColumnName=="id");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))throw new InvalidOperationException("全局多值目标必须使用非空int64物理主键。");
        if(!scoped&&condition.Fields.Any(field=>!joinedFields.Contains(field)&&!Scalar(columns.Single(column=>column.ColumnName==field))))
            throw new NotSupportedException("全局多值目标页条件必须为非凭据物理标量。");
        return new(page,name){UsesStoredStaticPage=storedStatic,UsesProjectPage=projectPage,UsesNonRedundantAccountPage=nonRedundantAccountPage,
            UsesNonRedundantExactCheckboxPage=checkbox&&(exactRealtime||
                cachedNonRedundantExact&&(storedStatic||projectPage||nonRedundantAccountPage||physicalParentPage)),
            UsesStoredExactCheckboxPage=checkbox&&storedExact&&(storedStatic||storedAccount||projectPage),
            UsesStoredAccountPage=storedAccount,UsesNonRedundantPhysicalParentPage=physicalParentPage,
            UsesStoredPhysicalParentPage=storedParent is not null};
    }

    /// <summary>按原表格和详情配置读取字段发布标记，保留旧数值字段的回退。</summary>
    private static bool Published(PageColumnMetadata field,string key,int fallback)=>
        field.Extra.TryGetValue(key,out var value)?value.ValueKind==JsonValueKind.True||value.ToString()=="1":fallback==1;
}
