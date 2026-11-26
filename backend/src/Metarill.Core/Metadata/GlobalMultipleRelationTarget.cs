using System.Text.Json;
using Metarill.Core.Security;
namespace Metarill.Core.Metadata;

public sealed record GlobalMultipleRelationPlan(PageMetadata Page,ColumnMetadata Name)
{
    public bool UsesStoredStaticPage { get; init; }
    public bool UsesProjectPage { get; init; }
    public bool UsesNonRedundantAccountPage { get; init; }
    public bool UsesNonRedundantPhysicalParentPage { get; init; }
    public bool UsesStoredPhysicalParentPage { get; init; }
    public bool UsesPhysicalParentPage=>UsesNonRedundantPhysicalParentPage||UsesStoredPhysicalParentPage;
}

/// <summary>全局多值关系分别证明普通R0实时读取与普通R1缓存消费者，目标名称必须为明确发布的物理值、R1缓存或已证明的R0单值名称。</summary>
public static class GlobalMultipleRelationTarget
{
    /// <summary>读取原多选或复选定义；目标页面只是已发布关系归属，复选仍使用自己的原候选条件。</summary>
    public static GlobalMultipleRelationPlan Require(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,false);
    /// <summary>非冗余多选可以按实际来源页选用同一全局实体的另一发布页，名称合同沿用表级定义。</summary>
    public static GlobalMultipleRelationPlan RequirePage(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,
        TableMetadata target,PageMetadata sourcePage)
    {
        if(foreign.ControlKind!="MultiSelectPage"||target.IsRongyu!=0||
            sourcePage.ConfigTableId!=owner.Id||sourcePage.YzProjectId!=catalog.YzProjectId||
            !ReferenceEquals(catalog.ConfigPages.SingleOrDefault(page=>page.Id==sourcePage.Id),sourcePage))
            throw new NotSupportedException("全局多选页面覆盖要求当前项目实际发布的非冗余来源和字典页。");
        return RequireSource(catalog,owner,foreign,target,cached:false,sourcePage:sourcePage);
    }

    /// <summary>冻结物理 R1 名称计划。来源必须有真实项目范围，全局来源另核对实际发布页。</summary>
    public static GlobalMultipleRelationPlan RequireCached(MetadataCatalog catalog,TableMetadata owner,ColumnMetadata foreign,TableMetadata target)=>RequireSource(catalog,owner,foreign,target,true);
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

    /// <summary>全局业务数组可使用实际发布的冗余或非冗余字典页，候选范围为固定物理常量或无条件。</summary>
    public static bool IsStoredStaticPage(TableMetadata table,PageMetadata page,FixedConditionPlan? condition,bool checkbox=false)=>
        table.IsRongyu is 0 or 1&&IsPublishedMultiPage(table,page,checkbox)&&(condition?.Variables.Count??0)==0;

    /// <summary>当前项目等值可叠加物理常量，非冗余字典也可叠加已声明账号等值。</summary>
    public static bool IsProjectPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox=false,bool allowExact=false)=>
        table.IsRongyu is 0 or 1&&IsPublishedMultiPage(table,page,checkbox,allowExact)&&condition.Terms.Any(IsProjectScope)&&condition.Terms.All(term=>
            IsFixedOrProject(term)||table.IsRongyu==0&&IsAccountScope(term));

    /// <summary>已发布的非冗余多值页可同时声明真实项目和父键，物理字段和父归属由调用方证明。</summary>
    public static bool IsProjectParentPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox=false,bool allowExact=false)=>
        table.IsRongyu==0&&IsPublishedMultiPage(table,page,checkbox,allowExact)&&UsesProjectAndParent(condition);

    /// <summary>冗余全局字典必须在实际多值发布页明示项目和物理父字段。</summary>
    public static bool IsStoredProjectParentPage(TableMetadata table,PageMetadata page,FixedConditionPlan condition,bool checkbox=false)=>
        table.IsRongyu==1&&IsPublishedMultiPage(table,page,checkbox)&&UsesProjectAndParent(condition);

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
        table.IsRongyu==0&&IsPublishedMultiPage(table,page,checkbox)&&condition.Variables.Count>0&&condition.Terms.All(term=>
            term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable)||
            term.Field!="yz_project_id"&&term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} value]}&&
                AccountConditionVariables.IsPageField(value.Text,term.Field));

    /// <summary>两类多值用途共用字典的页面资格，精确数据管理由独立合同验证。</summary>
    private static bool IsPublishedMultiPage(TableMetadata table,PageMetadata page,bool checkbox,bool allowExact=false)=>
        GlobalCrossTableTarget.Declared(table)&&page.ConfigTableId==table.Id&&
        (checkbox?page.Extra.GetValueOrDefault("type").ToString() is "" or "0" or "1" or "2":
            page.Extra.GetValueOrDefault("type").ToString()=="2")&&
        (string.IsNullOrWhiteSpace(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString())||
            allowExact&&!checkbox&&table.IsRongyu==0&&GlobalCrossTableTarget.DeclaresExactMulti(table,page));

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
        using var document=foreign.ControlKind=="MultiCheckBoxDatabase"?null:JsonDocument.Parse(foreign.ControlDetail);var definition=foreign.ControlKind=="MultiCheckBoxDatabase"?DatabaseCheckboxDefinition.Parse(foreign.ControlDetail).ReadDefinition(foreign):document!.RootElement;
        if(definition.ValueKind!=JsonValueKind.Object||definition.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=definition.EnumerateObject().Count())throw new InvalidOperationException("全局多值定义必须为无重复键对象。");
        string Value(string key)=>!definition.TryGetProperty(key,out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():throw new InvalidOperationException("全局多值映射必须是标量。");
        string Extra(string key)=>foreign.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        var tableId=Value("tableId");var tableName=Value("table");var pageId=Value("pageId");var linkedPage=Extra("linkConfigPageId");
        if(tableId.Length>0&&tableId!=target.Id||tableName.Length>0&&tableName!=target.TableName||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id||Extra("joinTable") is {Length:>0} joined&&joined!=target.TableName||pageId.Length>0&&linkedPage.Length>0&&pageId!=linkedPage)throw new InvalidOperationException("全局多值的表、页面或显示来源声明冲突。");
        if(pageId.Length==0)pageId=linkedPage;
        if(sourcePage is not null)
        {
            var field=catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==sourcePage.Id&&item.ConfigColumnId==foreign.Id);
            var overlay=field?.Extra.GetValueOrDefault("controlDetail").ToString();
            if(!string.IsNullOrWhiteSpace(overlay))
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
                var label=Value("nameField");
                if(Pick("tableId") is {Length:>0} overrideId&&overrideId!=target.Id||
                    Pick("table") is {Length:>0} overrideTable&&overrideTable!=target.TableName||
                    Pick("nameField") is {Length:>0} overrideName&&overrideName!=(label.Length==0?"name":label)||
                    field!.Extra.GetValueOrDefault("controlKind").ToString() is {Length:>0} kind&&kind!=foreign.ControlKind)
                    throw new InvalidOperationException("全局多选页面覆盖必须沿用表级目标和名称合同。");
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
        var checkbox=foreign.ControlKind=="MultiCheckBoxDatabase";
        var joinedFields=GlobalCrossTableTarget.PageJoinedConditionFields(catalog,target,page);
        var storedAccount=(checkbox||pageId.Length>0)&&IsStoredAccountPage(target,page,condition,checkbox);
        var selfTreeScope=condition.Fields.Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(target,expanded,field));
        var scoped=selfTreeScope||target.IsRongyu==0&&condition.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(target,columns,field));
        var storedStatic=(checkbox||pageId.Length>0)&&(target.IsRongyu==1||!scoped)&&IsStoredStaticPage(target,page,condition,checkbox);
        var projectPage=(checkbox||pageId.Length>0)&&IsProjectPage(target,page,condition,checkbox);
        var nonRedundantAccountPage=(checkbox||pageId.Length>0)&&IsNonRedundantAccountPage(target,page,condition,checkbox);
        // 普通与全局业务来源共用真实同父证明；固定字典可以只声明父范围和字面条件。
        var projectParentPage=IsProjectParentPage(target,page,condition,checkbox);
        var staticParentPage=GlobalCrossTableTarget.UsesOnlyParentVariables(condition);
        var physicalParentPage=false;
        var storedParent=(checkbox||pageId.Length>0)?GlobalCrossTableTarget.StoredProjectMultiParent(catalog,target,page,checkbox):null;
        if(storedParent is not null)
        {
            // 写入和按来源行回填必须从真实字段得到同一个父键。
            var sourceParent=checkbox?storedParent.CheckboxSource(catalog,owner,foreign):storedParent.Source(catalog,owner,foreign);
            if(sourceParent.LogicalPages.Count>0)
                throw new NotSupportedException("冗余全局字典要求已发布来源的实际物理父键。");
        }
        if((GlobalCrossTableTarget.Declared(owner)||projectParentPage||staticParentPage)&&
            (checkbox||pageId.Length>0)&&target.IsRongyu==0&&!scoped&&IsPublishedMultiPage(target,page,checkbox)&&
            (projectParentPage||staticParentPage||condition.Variables.Any(AccountConditionVariables.IsSupported))&&
            MultiTargetParentScope.CompileGlobalPage(catalog,target,condition,checkbox) is {Logical:false} parent&&
            parent.AdditionalTargets.All(scope=>!scope.Logical))
        {
            var sourceParent=sourcePage is not null?parent.Source(catalog,owner,foreign,sourcePage):
                checkbox?parent.CheckboxSource(catalog,owner,foreign):parent.Source(catalog,owner,foreign);
            // 多个来源物理键须经同一发布父页校验，原单父字段继续沿用已证明的持久键。
            physicalParentPage=sourceParent.LogicalPages.Count==0&&(sourceParent.AdditionalColumns.Count==0||parent.Global);
        }
        if(GlobalCrossTableTarget.Declared(owner))
        {
            // 业务来源仍按行项目隔离。物理副本只能使用维护时也能重现的固定字典范围。
            if(!storedAccount&&!storedStatic&&!projectPage&&!nonRedundantAccountPage&&!physicalParentPage&&storedParent is null)throw new NotSupportedException("全局多值来源必须绑定受支持的已发布字典页。");
            GlobalDerivedRelationSource.RequireTable(catalog,owner);
        }
        if(cached&&checkbox&&(projectPage||storedParent is not null||GlobalCrossTableTarget.Declared(owner)&&(storedStatic||nonRedundantAccountPage))&&
            !DatabaseCheckboxDefinition.Parse(foreign.ControlDetail)
                .IsCoveredByStaticPage(condition,expanded.Select(column=>column.ColumnName)))
            throw new NotSupportedException("全局复选的冗余名称要求控件条件由实际发布字典页覆盖。");
        if(foreign.ControlKind=="MultiSelectPage"&&GlobalCrossTableTarget.DeclaresExactMulti(target,page)||
            foreign.ControlKind=="MultiCheckBoxDatabase"&&GlobalCrossTableTarget.DeclaresExactPage(target,page))
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
        var nameField=Value("nameField");if(nameField.Length==0)nameField="name";
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
        var key=columns.SingleOrDefault(column=>column.ColumnName=="id");if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!Scalar(key))throw new InvalidOperationException("全局多值目标必须使用非空int64物理主键。");
        if(!scoped&&condition.Fields.Any(field=>!joinedFields.Contains(field)&&!Scalar(columns.Single(column=>column.ColumnName==field))))
            throw new NotSupportedException("全局多值目标页条件必须为非凭据物理标量。");
        return new(page,name){UsesStoredStaticPage=storedStatic,UsesProjectPage=projectPage,UsesNonRedundantAccountPage=nonRedundantAccountPage,UsesNonRedundantPhysicalParentPage=physicalParentPage,UsesStoredPhysicalParentPage=storedParent is not null};
    }
}
