using System.Collections.Frozen;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed record CascadeCacheOwner(TableMetadata Table,string Alias,bool GlobalTreeSource=false,bool GlobalSingleSource=false);

    /// <summary>只保存多值名称实际依赖的级联候选页面，运行时可重新证明字段和条件。</summary>
    public static IReadOnlySet<string> PublishedCascadeLabelPages(MetadataCatalog catalog)
        =>PublishedCascadeLabelPages(catalog,out _);

    /// <summary>账号缓存额外保留来源页的归属条件，运行时用来重新证明物理行的归属列。</summary>
    public static IReadOnlySet<string> PublishedCascadeLabelPages(MetadataCatalog catalog,out IReadOnlySet<string> ownershipPages)
    {
        var pages=new HashSet<string>(StringComparer.Ordinal);
        var owners=new HashSet<string>(StringComparer.Ordinal);
        foreach(var (source,target,field,relation,sourcePage,namePage) in DeclaredMultiNameTargets(catalog).Distinct())
            try
            {
                var storage=MetadataStoragePolicy.ForTable(catalog,target);
                var column=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(item=>item.ConfigTableId==target.Id))
                    .SingleOrDefault(item=>item.ColumnName==field);
                if(column is null)continue;
                var checkbox=relation.ControlKind=="MultiCheckBoxDatabase";
                var selected=ScopedMultiTargetPage(catalog,target,namePage,checkbox);
                // 普通物理父页如果被同父多路径引用，精简目录也必须保留实际目标和来源页。
                var compoundPhysical=selected is null&&namePage is not null&&source is not null&&sourcePage is not null&&
                    HasCompoundMultiSelectSource(catalog,source,sourcePage,relation,namePage);
                if(compoundPhysical)selected=namePage;
                // 普通备用页没有附加 SQL 条件，但宿主仍需用实际覆盖核对候选页。
                if(selected is null&&sourcePage is not null&&namePage is not null&&!checkbox&&
                    IsProjectOnlyStoredMultiSelectPage(catalog,target,namePage))
                {
                    pages.Add(sourcePage.Id);
                    pages.Add(namePage.Id);
                    continue;
                }
                if(selected is null&&(sourcePage is not null||storage.IsPhysical(column)||column.LogicalType!="string"&&
                    !MultiNameStorageColumns.IsDerivedScalarLabel(target,column,storage)))continue;
                var plan=selected is null?CompileMultiLabel(catalog,target.Id,field,[field],[],cacheOwner:null,relation:relation):
                    CompileScopedMultiLabel(catalog,target.Id,selected.Id,field,[field],[],
                        exactMultiSelection:target.IsRongyu==0&&relation.ControlKind=="MultiSelectPage"&&
                            selected.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0,
                        storedExactCheckbox:relation.ControlKind=="MultiCheckBoxDatabase",allowLiteralMultiScope:compoundPhysical);
                pages.UnionWith(plan.CascadeLabelPages);
                if(selected is not null)
                {
                    // 即使账号条件全在父页，也须冻结已证明的目标页及具体来源覆盖。
                    pages.Add(selected.Id);
                    if(sourcePage is not null)pages.Add(sourcePage.Id);
                }
                if(source is not null&&(plan.RequiresAccount&&source.IsRongyu==1||selected is not null&&plan.RequiredMultiParent is not null))
                    owners.UnionWith(catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id&&
                        (sourcePage is null||page.Id==sourcePage.Id)).Select(page=>page.Id));
                if(source is not null&&selected is not null&&MultiParent(catalog,selected,checkbox) is {} parent)
                {
                    var sourceParent=sourcePage is null?parent.Source(catalog,source,relation):parent.Source(catalog,source,relation,sourcePage);
                    foreach(var logicalPage in sourceParent.LogicalPages)
                    {
                        var fields=LogicalSourceParentFields(catalog,source,sourceParent);
                        pages.Add(logicalPage.Id);
                        pages.UnionWith(CrossMembershipBindings(catalog,source,logicalPage,fields).SelectMany(binding=>binding.Levels.Select(level=>level.PageId)));
                    }
                }
            }
            catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
            {
                // 无效配置由原控件的发布门禁报告，不冻结猜测到的范围。
            }
        ownershipPages=owners;
        return pages;
    }

    /// <summary>只编译固定页面条件依赖的路径，保留祖先范围及实际目标页条件。</summary>
    internal static MySqlNonRedundantReadPlan CompilePageMembership(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        IReadOnlyList<string> conditionFields,IReadOnlySet<string> crossFields,IReadOnlySet<string> selfTreeFields,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null)
    {
        var joinedFields=ProjectScopedSingleRelationTarget.JoinedPageFields(catalog,table,page)
            .Where(conditionFields.Contains).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        if(!catalog.ConfigPages.Contains(page)||page.ConfigTableId!=table.Id||
            table.IsRongyu!=0||(crossFields.Count==0&&selfTreeFields.Count==0&&joinedFields.Count==0)||
            !crossFields.IsSubsetOf(conditionFields)||!selfTreeFields.IsSubsetOf(conditionFields))
            throw new NotSupportedException("固定逻辑页面范围必须属于普通非冗余来源。");

        var declared=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));
        if(!selfTreeFields.SetEquals(SelfTreeConditionFields(catalog,table,page,declared,selfTreeFields)))
            throw new NotSupportedException("固定自树范围缺少已声明的叶名。");

        var bindings=crossFields.Count==0?[]:CrossMembershipBindings(catalog,table,page,crossFields);
        var plan=CompileRelationScope(catalog,table.Id,outputs,bindings,
            conditionFields.Concat(dependencies).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            deferCascadeNames:selfTreeFields.Count>0,singleTargets:singleTargets,conditionJoins:joinedFields,
            conditionSourcePage:joinedFields.Count>0?page:null);
        foreach(var field in joinedFields)
        {
            plan.RequireScalarLabelOrigin(field);
            if(plan.Types[field]!=declared.Single(column=>column.ColumnName==field).LogicalType)
                throw new NotSupportedException("页面联接条件必须与终点逻辑类型一致。");
        }
        return plan;
    }

    /// <summary>从实际发布页发现逻辑叶名，然后校验其实际控件定义。</summary>
    internal static IReadOnlySet<string> PublishedSelfTreeConditionFields(MetadataCatalog catalog,TableMetadata table,
        PageMetadata page,IReadOnlyList<ColumnMetadata> declared)=>
        table.IsRongyu==0?SelfTreeConditionFields(catalog,table,page,declared,
            FixedConditionPlan.Compile(page.WhereSql,declared.Select(column=>column.ColumnName)).Fields):
            new HashSet<string>(StringComparer.Ordinal);

    /// <summary>核对项目内发布页的精确匹配字段，普通候选和已保存名称共用页面归属证明。</summary>
    private static ColumnMetadata? PublishedSingleExactColumn(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        IReadOnlyList<ColumnMetadata> columns)
    {
        if(table.IsRongyu!=0||GlobalCrossTableTarget.Declared(table)||table.IsView!=0||table.IsFunctionTable!=0||
            page.ConfigTableId!=table.Id||page.YzProjectId!=catalog.YzProjectId||table.YzProjectId!=catalog.YzProjectId||
            !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId))
            return null;
        var field=ExactDataManageValues.Field(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString());
        var match=columns.SingleOrDefault(column=>column.ColumnName==field);
        if(match is null||match.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(field))return null;
        _=ExactDataManageValues.Unrestricted.ReadLogical(match.LogicalType);
        return match;
    }

    /// <summary>叶名范围还必须使用与表级一致的冻结自树定义。</summary>
    internal static bool HasPublishedSelfTreeExact(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        IReadOnlyList<ColumnMetadata> columns)=>
        PublishedSingleExactColumn(catalog,table,page,columns) is {} match&&
        SelfTreeConditionFields(catalog,table,page,columns,[match.ColumnName]).Count==1;

    /// <summary>只有与表级控件一致的叶名才能用于固定范围。附件和候选页共用同一份证明。</summary>
    internal static IReadOnlySet<string> SelfTreeConditionFields(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        IReadOnlyList<ColumnMetadata> declared,IEnumerable<string> fields)
    {
        var names=fields.Where(field=>CascadeTreeStorageColumns.IsLogicalNameField(table,declared,field)).ToHashSet(StringComparer.Ordinal);
        foreach(var field in names)
        {
            var control=declared.Single(column=>column.ColumnName==field[..^5]+"_id");
            var overlay=catalog.ConfigPageColumns.SingleOrDefault(column=>column.ConfigPageId==page.Id&&column.ConfigColumnId==control.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();
            if(!CrossTableStorageColumns.KeepsDefinition(control.ControlDetail,overlay))
                throw new NotSupportedException("自树固定页面条件必须沿用表级级联定义。");
        }
        return names;
    }

    /// <summary>查找由原多值关系显式绑定的固定范围页。</summary>
    internal static bool IsScopedMultiTarget(MetadataCatalog catalog,TableMetadata target,string pageId,bool selectOnly=false)=>
        DeclaredMultiNameTargets(catalog).Any(item=>item.Table.Id==target.Id&&item.NamePage?.Id==pageId&&
            (!selectOnly||item.Relation.ControlKind=="MultiSelectPage")&&
            ScopedMultiTargetPage(catalog,target,item.NamePage,item.Relation.ControlKind=="MultiCheckBoxDatabase") is not null);

    /// <summary>复选专用父字段只在实际发布的原复选关系绑定该页时开放。</summary>
    internal static bool IsPublishedCheckboxTarget(MetadataCatalog catalog,TableMetadata table,string pageId)=>
        DeclaredMultiNameTargets(catalog).Any(item=>item.Relation.ControlKind=="MultiCheckBoxDatabase"&&
            item.Table.Id==table.Id&&item.NamePage?.Id==pageId);

    /// <summary>由实际多值控件绑定找到名称所处的页面，纯物理根条件也保留中间页上下文。</summary>
    private static PageMetadata? MultiTargetNamePage(MetadataCatalog catalog,TableMetadata table,ColumnMetadata relation)
    {
        var global=GlobalCrossTableTarget.Declared(table);
        var checkbox=relation.ControlKind=="MultiCheckBoxDatabase";
        var stored=table.IsRongyu==1&&global&&(checkbox||relation.ControlKind=="MultiSelectPage");
        var pageId=MultiPageId(Definition(relation));
        if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
        if(!global&&pageId.Length==0)return null;
        var candidates=catalog.ConfigPages.Where(item=>item.ConfigTableId==table.Id&&
            (item.Id==pageId||global&&pageId.Length==0)).Take(2).ToArray();
        if(candidates.Length!=1)return null;
        var page=candidates[0];
        return table.IsRongyu==0||
            stored&&(checkbox||pageId.Length>0)&&(StoredIdentityMultiPage(catalog,table,page,checkbox)||
                GlobalCrossTableTarget.StoredProjectMultiParent(catalog,table,page,checkbox) is not null)||
            checkbox&&IsProjectFixedMultiPage(catalog,table,page,checkboxAccount:true,allowStoredTextParent:true)||
            relation.ControlKind=="MultiSelectPage"&&IsProjectFixedMultiPage(catalog,table,page)?page:null;
    }

    /// <summary>按完整目录重证冗余字典的当前项目或账号范围，执行器只读取实际发布条件。</summary>
    private static bool StoredIdentityMultiPage(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool checkbox=false)
    {
        if(table.IsRongyu!=1||!GlobalCrossTableTarget.Declared(table))return false;
        var condition=FixedConditionPlan.Compile(page.WhereSql,
            catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).Select(column=>column.ColumnName));
        return GlobalMultipleRelationTarget.IsStoredAccountPage(table,page,condition,checkbox)||
            GlobalMultipleRelationTarget.IsProjectPage(table,page,condition,checkbox);
    }

    /// <summary>非冗余全局字典的项目声明只在固定多值候选及名称计划生效。</summary>
    private static bool IsNonRedundantProjectMultiPage(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool checkbox=false)
    {
        if(table.IsRongyu!=0||!GlobalCrossTableTarget.Declared(table))return false;
        var members=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        if(!members.Any(column=>column.ColumnName=="yz_project_id"))return false;
        // 先以完整模型识别已有的逻辑条件。纯物理项目范围须逐项证明，避免抢占原递归候选。
        var expanded=RelationStorageColumns.Expand(table,members);
        var syntax=FixedConditionPlan.Compile(page.WhereSql,expanded.Select(column=>column.ColumnName));
        if(!GlobalMultipleRelationTarget.IsProjectPage(table,page,syntax,checkbox))return false;
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        return syntax.Fields.All(field=>members.Any(column=>column.ColumnName==field&&
            GlobalPhysicalRelationValue.IsScalar(storage,table,column)));
    }

    /// <summary>普通冗余候选页只依赖当前项目，可保留实际页面而不添加固定父条件。</summary>
    internal static bool IsProjectOnlyStoredMultiSelectPage(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        if(table.IsRongyu!=1||GlobalCrossTableTarget.Declared(table)||table.IsView!=0||table.IsFunctionTable!=0||
            page.ConfigTableId!=table.Id||page.Extra.GetValueOrDefault("type").ToString()!="2"||
            table.YzProjectId.Length>0&&table.YzProjectId!=catalog.YzProjectId||
            page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId||
            page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0)return false;
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var declared=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));
        return storage.PhysicalColumns.Any(column=>column is {ColumnName:"yz_project_id",LogicalType:"string" or "int64"}&&
            GlobalPhysicalRelationValue.IsScalar(storage,table,column))&&
            FixedConditionPlan.Compile(page.WhereSql,declared.Select(column=>column.ColumnName))
                .Terms.All(GlobalMultipleRelationTarget.IsProjectScope);
    }

    /// <summary>项目内多选字典保留物理常量、实际父键和冗余字典的原账号等值条件。</summary>
    internal static bool IsProjectFixedMultiPage(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        bool checkboxAccount=false,bool allowStoredTextParent=false)
    {
        var pageType=page.Extra.GetValueOrDefault("type").ToString();
        if(GlobalCrossTableTarget.Declared(table)||table.IsRongyu is not(0 or 1)||table.IsView!=0||table.IsFunctionTable!=0||
            page.ConfigTableId!=table.Id||page.WhereSql.Length==0||
            (checkboxAccount?table.IsRongyu!=1||pageType is not("" or "0" or "1" or "2"):pageType!="2"))return false;
        var storage=MetadataStoragePolicy.ForTable(catalog,table);
        var exact=page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString();
        // 只有同项目的冗余文本父字典能将精确权限留给请求入口，投影及副本只重放固定范围。
        if(exact.Length>0)
        {
            if(table.IsRongyu!=1||table.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||
                !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId))return false;
            var match=ExactDataManageValues.Field(exact);
            if(!storage.PhysicalColumns.Any(column=>column.ColumnName==match&&GlobalPhysicalRelationValue.IsScalar(storage,table,column)&&
                !CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,column.ColumnName)))return false;
        }
        var syntax=FixedConditionPlan.Compile(page.WhereSql,
            RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
                .Select(column=>column.ColumnName));
        var parents=syntax.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
            MultiTargetParentScope.IsParameter(value.Text))).ToArray();
        if(!syntax.Terms.Any(term=>!GlobalMultipleRelationTarget.IsProjectScope(term))||
            syntax.Terms.Any(term=>!GlobalMultipleRelationTarget.IsProjectScope(term)&&!parents.Contains(term)&&
                term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)&&!IsStoredAccount(term))||
            syntax.Fields.Any(field=>!storage.PhysicalColumns.Any(column=>column.ColumnName==field&&
                GlobalPhysicalRelationValue.IsScalar(storage,table,column))||
                CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,field)))
            return false;
        var usesDictionaryAccount=syntax.Terms.Any(IsStoredAccount);
        if(parents.Length==0)return exact.Length==0&&(!checkboxAccount||usesDictionaryAccount);
        var binding=checkboxAccount
            ?MultiTargetParentScope.CompileCheckboxPage(catalog,table,syntax,allowGlobalParent:true,allowStoredPhysicalParents:true)
            :MultiTargetParentScope.CompileSelectionPage(catalog,table,syntax,allowGlobalParent:true,allowStoredPhysicalParents:true);
        // 冗余字典可由全局父页限定账号，实际父页在候选、读取和维护编译时重新验证。
        return binding is {Logical:false} parent&&
            (exact.Length==0||!parent.Global&&parents.Length==1&&CrossTableStorageColumns.DeclaresStoredTextKey(catalog,table,parent.Target))&&
            (parent.Global?table.IsRongyu==1:!checkboxAccount||usesDictionaryAccount||
                allowStoredTextParent&&CrossTableStorageColumns.DeclaresStoredTextKey(catalog,table,parent.Target));

        // 请求通过认证快照绑定，冗余名称回填则由现有归属计划逐页证明来源物理字段。
        bool IsStoredAccount(FixedConditionTerm term)=>table.IsRongyu==1&&term.Field!="yz_project_id"&&
            term is {Comparison:FixedConditionComparison.Equal,Values:[{Kind:FixedConditionValueKind.Variable} value]}&&
            AccountConditionVariables.IsPageField(value.Text,term.Field)&&storage.PhysicalColumns.Any(column=>
                column.ColumnName==term.Field&&column.LogicalType is "int32" or "int64" or "string");
    }

    /// <summary>按实际目标页，将原逻辑、账号或已证明的固定范围嵌入多值计划。</summary>
    internal static PageMetadata? MultiTargetLogicalPage(MetadataCatalog catalog,TableMetadata table,ColumnMetadata relation)
        =>ScopedMultiTargetPage(catalog,table,MultiTargetNamePage(catalog,table,relation),relation.ControlKind=="MultiCheckBoxDatabase");

    /// <summary>精简维护目录须保留该多值目标页及两端完整级联，宿主重建时仍能验证父路径。</summary>
    public static PageMetadata? StoredTextMultiTargetPage(MetadataCatalog catalog,TableMetadata table,ColumnMetadata relation)
        =>table.IsRongyu==1&&!GlobalCrossTableTarget.Declared(table)&&
            relation.ControlKind is ("MultiSelectPage" or "MultiCheckBoxDatabase")&&
            MultiTargetLogicalPage(catalog,table,relation) is {} page&&
            CrossTableStorageColumns.DeclaresStoredTextParent(catalog,table,page)?page:null;

    /// <summary>比较当前绑定页的逻辑、账号及实际全局父范围，表级和页面覆盖共用同一证明。</summary>
    private static PageMetadata? ScopedMultiTargetPage(MetadataCatalog catalog,TableMetadata table,PageMetadata? page,bool checkbox)
    {
        if(page is null||page.WhereSql.Length==0)return null;
        var members=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var declared=RelationStorageColumns.Expand(table,members);
        var syntax=FixedConditionPlan.Compile(page.WhereSql,declared.Select(column=>column.ColumnName));
        return (!checkbox||CrossTableStorageColumns.DeclaresStoredTextParent(catalog,table,page))&&
            IsProjectFixedMultiPage(catalog,table,page,checkboxAccount:checkbox,allowStoredTextParent:true)||
            GlobalMultipleRelationTarget.IsProjectPage(table,page,syntax,checkbox)||
            GlobalMultipleRelationTarget.IsProjectParentPage(table,page,syntax,checkbox)||
            GlobalMultipleRelationTarget.IsStoredProjectParentPage(table,page,syntax,checkbox)||
            checkbox&&table.IsRongyu==0&&syntax.Terms.Any(term=>term.Values.Any(value=>
                value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text))&&
                members.SingleOrDefault(column=>column.ColumnName==term.Field) is {} key&&
                MultiTargetParentScope.IsCheckboxOnlyParentField(key))||
            syntax.Variables.Any(AccountConditionVariables.IsSupported)||
            table.IsRongyu==0&&GlobalCrossTableTarget.Declared(table)&&
                (checkbox||page.Extra.GetValueOrDefault("type").ToString()=="2")&&GlobalCrossTableTarget.UsesOnlyParentVariables(syntax)||
            (checkbox||page.Extra.GetValueOrDefault("type").ToString()=="2")&&syntax.Terms.Count(term=>term.Values.Any(value=>
                value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text)))>1||
            syntax.Fields.Any(field=>CrossTableStorageColumns.IsLogicalPathField(table,members,field)||
                CascadeTreeStorageColumns.IsLogicalNameField(table,declared,field))||
            syntax.Terms.Any(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                MultiTargetParentScope.IsParameter(value.Text))&&
                members.SingleOrDefault(column=>column.ColumnName==term.Field) is {LinkConfigTableId.Length:>0} field&&
                catalog.ConfigTables.Any(parent=>parent.Id==field.LinkConfigTableId&&GlobalCrossTableTarget.Declared(parent)))?page:null;
    }

    /// <summary>原发布元数据的页面 ID 可为精确整数或字符串。</summary>
    private static string MultiPageId(System.Text.Json.JsonElement definition)=>MultiDefinitionId(definition,"pageId");

    private static string MultiDefinitionId(System.Text.Json.JsonElement definition,string field)=>
        definition.TryGetProperty(field,out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.Number&&value.TryGetInt64(out var id)
            ?id.ToString(System.Globalization.CultureInfo.InvariantCulture):Value(definition,field);

    /// <summary>发布已绑定目标页及名称，原候选、导入、回读共用固定条件。</summary>
    internal static MySqlNonRedundantReadPlan CompileScopedMultiLabel(MetadataCatalog catalog,string tableId,string pageId,
        string field,IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,IReadOnlyList<string>? importFields=null,
        string importPageType="2",bool exactMultiSelection=false,bool storedExactCheckbox=false,bool allowLiteralMultiScope=false)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("多值目标页逻辑条件要求已发布的MySQL读取计划。");
        var page=catalog.ConfigPages.Single(item=>item.ConfigTableId==tableId&&item.Id==pageId);
        if(page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
            throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        if(importPageType=="2"&&!exactMultiSelection&&(importFields?.Count??0)==0&&
            StoredIdentityMultiPage(catalog,table,page,storedExactCheckbox))
            return new Builder(catalog,tableId,candidatePage:page,multiLabel:true,cascadeLabelField:field,
                accountPhysicalCandidate:true)
            {
                MultiNamePathPage=page,
                ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal)
            }.Compile(outputs,dependencies);
        var parent=(importPageType=="1"?ProjectScopedSingleRelationTarget.PageParent(catalog,table,page):null)
            ??MultiParent(catalog,page,storedExactCheckbox);
        var extraFields=(importFields??[]).ToHashSet(StringComparer.Ordinal);
        if(exactMultiSelection)extraFields.Add(RequireExactMultiSelection(catalog,table,page,storedExactCheckbox));
        var crossFields=new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<ColumnMetadata> importColumns=[];
        IReadOnlyList<NonRedundantCrossTableScope> crossBindings=[];
        if(extraFields.Count>0)
        {
            var pageType=page.Extra.GetValueOrDefault("type").ToString();
            var typeMatches=storedExactCheckbox?pageType is "" or "0" or "1" or "2":pageType==importPageType;
            if(table.IsRongyu!=0||table.IsView!=0||table.IsFunctionTable!=0||!typeMatches||
                !exactMultiSelection&&page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0)
                throw new NotSupportedException("Excel 逻辑条件要求实际发布的普通非冗余选择页。");
            var members=catalog.ConfigColumns.Where(column=>column.ConfigTableId==tableId).ToArray();
            var declared=RelationStorageColumns.Expand(table,members);
            // 从当前发布页再次验证稀疏层级和自树覆盖，禁止宿主用备用页代替。
            // 同一逻辑父的表达式由目标页范围建立，附加条件直接使用该表达式。
            crossFields=extraFields.Where(name=>(parent is not {Logical:true}||name!=parent.Target.ColumnName)&&
                CrossTableStorageColumns.IsLogicalPathField(table,members,name)).ToHashSet(StringComparer.Ordinal);
            _=SelfTreeConditionFields(catalog,table,page,declared,extraFields);
            importColumns=declared.Where(column=>extraFields.Contains(column.ColumnName)).ToArray();
            if(crossFields.Count>0)crossBindings=CrossMembershipBindings(catalog,table,page,crossFields);
        }
        var projectFixedScope=IsProjectFixedMultiPage(catalog,table,page,checkboxAccount:storedExactCheckbox,allowStoredTextParent:true);
        var storedParent=GlobalCrossTableTarget.StoredProjectMultiParent(catalog,table,page,storedExactCheckbox);
        var pageJoins=importPageType=="1"?ProjectScopedSingleRelationTarget.JoinedTextPageFields(catalog,table,page):
            FrozenSet<string>.Empty;
        var plan=new Builder(catalog,tableId,matching:true,multiLabel:true,cascadeLabelField:field,crossTables:crossBindings,
            scopedPhysicalCandidate:table.IsRongyu==1&&projectFixedScope,storedMultiParent:storedParent)
        {
            SelectedMultiPage=page,
            MultiCandidateParent=parent,
            AllowsExactMultiSelection=exactMultiSelection,
            AllowsStoredExactCheckbox=storedExactCheckbox,
            MultiNamePathPage=page,
            // 保存及导入的当前读同时锁定候选名称和物理父记录。
            LockPublishedJoinScopes=storedExactCheckbox||storedParent is not null||projectFixedScope&&parent is not null,
            AllowLiteralMultiScope=extraFields.Count>0||allowLiteralMultiScope||projectFixedScope,
            MultiImportFields=extraFields,
            MultiImportCrossFields=crossFields,
            ScopedRootControls=crossBindings.Select(binding=>binding.SourceColumnId).ToHashSet(StringComparer.Ordinal),
            ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal),
            GlobalCrossCandidate=GlobalCrossTableTarget.Declared(table)
        }.Compile(outputs,dependencies.Concat(extraFields).Concat(pageJoins).Concat(parent is null?[]:
                parent.AdditionalTargets.Select(item=>item.Target.ColumnName).Prepend(parent.Target.ColumnName))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        if(importColumns.Count!=extraFields.Count||importColumns.Any(column=>
            !plan.Types.TryGetValue(column.ColumnName,out var type)||type!=column.LogicalType))
            throw new NotSupportedException("Excel 逻辑条件必须落到与声明类型一致的固定字段。");
        foreach(var joined in pageJoins)plan.RequireScalarLabelOrigin(joined);
        return plan;
    }

    /// <summary>仅验证实际发布的多选页或已绑定的原复选目标；名称维护沿来源行归属。</summary>
    private static string RequireExactMultiSelection(MetadataCatalog catalog,TableMetadata table,PageMetadata page,bool storedCheckbox=false)
    {
        var field=ExactDataManageValues.Field(page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString());
        var type=page.Extra.GetValueOrDefault("type").ToString();
        if(field.Length==0||table.IsRongyu!=0&&!(storedCheckbox&&table.IsRongyu==1)||table.IsFunctionTable!=0||table.IsView!=0||
            page.ConfigTableId!=table.Id||type!="2"&&!(storedCheckbox&&type is "" or "0" or "1")||
            page.YzProjectId!=catalog.YzProjectId||table.YzProjectId!=catalog.YzProjectId||
            !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId))
            throw new NotSupportedException("精确多选必须绑定本项目实际发布的非冗余多选页。");
        if(GlobalCrossTableTarget.Declared(table))_=GlobalCrossTableTarget.ExactPageParent(catalog,table,page);
        var match=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
            .SingleOrDefault(column=>column.ColumnName==field);
        if(match is null||match.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(field))
            throw new NotSupportedException("精确多选缺少已声明的单值匹配字段。");
        _=ExactDataManageValues.Unrestricted.ReadLogical(match.LogicalType);
        return field;
    }

    /// <summary>按实际发布页解析原父参数，父列可能未在页面显示。</summary>
    private static MultiTargetParentScope? MultiParent(MetadataCatalog catalog,PageMetadata page,bool checkbox=false)
    {
        var table=catalog.ConfigTables.Single(item=>item.Id==page.ConfigTableId);
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));
        var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        if(GlobalCrossTableTarget.Declared(table))
            return GlobalCrossTableTarget.StoredProjectMultiParent(catalog,table,page,checkbox)??
                MultiTargetParentScope.CompileGlobalPage(catalog,table,syntax,checkbox);
        if(checkbox)return IsProjectFixedMultiPage(catalog,table,page,checkboxAccount:true,allowStoredTextParent:true)
            ?MultiTargetParentScope.CompileCheckboxPage(catalog,table,syntax,allowGlobalParent:true,allowStoredPhysicalParents:true)
            :MultiTargetParentScope.CompileProjectCheckboxPhysicalPage(catalog,table,page)??
                MultiTargetParentScope.CompileCheckboxPage(catalog,table,syntax,allowGlobalParent:true);
        return page.Extra.GetValueOrDefault("type").ToString()=="2"
            ?MultiTargetParentScope.CompileSelectionPage(catalog,table,syntax,allowGlobalParent:true,
                allowStoredPhysicalParents:table.IsRongyu==1&&IsProjectFixedMultiPage(catalog,table,page))
            :MultiTargetParentScope.Compile(catalog,table,syntax);
    }

    /// <summary>范围只冻结实际依赖的控件和共享同伴，页面覆盖必须保留相同路径定义。</summary>
    private static IReadOnlyList<NonRedundantCrossTableScope> CrossMembershipBindings(MetadataCatalog catalog,TableMetadata table,
        PageMetadata page,IReadOnlySet<string> crossFields)
    {
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id).ToArray();
        var selected=columns.Where(column=>column.ControlKind=="CascadeTree"&&
            crossFields.Any(field=>CrossTableStorageColumns.ContainsPathField(column,field))).Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var group in SharedCrossTableSparseSelection.CompileAll(table,columns))
            if(group.Branches.Any(branch=>selected.Contains(branch.SourceColumnId)))
                selected.UnionWith(group.Branches.Select(branch=>branch.SourceColumnId));
        if(selected.Count==0||crossFields.Any(field=>!CrossTableStorageColumns.IsLogicalPathField(table,columns,field)))
            throw new NotSupportedException("固定页面范围找不到已声明的跨表层位。");
        return columns.Where(column=>selected.Contains(column.Id)).Select(column=>
        {
            var overlay=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==column.Id)
                ?.Extra.GetValueOrDefault("controlDetail").ToString();
            if(!CrossTableStorageColumns.KeepsDefinition(column.ControlDetail,overlay))
                throw new NotSupportedException("固定页面范围不能忽略级联页覆盖引起的路径变化。");
            return CascadeLabelScope(catalog,column);
        }).ToArray();
    }

    /// <summary>发现实际多值控件绑定的目标名称，普通非关联输出仍使用原读取计划。</summary>
    private static IEnumerable<(TableMetadata? Source,TableMetadata Table,string Field,ColumnMetadata Relation,
        PageMetadata? SourcePage,PageMetadata? NamePage)> DeclaredMultiNameTargets(MetadataCatalog catalog)
    {
        foreach(var relation in catalog.ConfigColumns.Where(column=>column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"))
        {
            (TableMetadata? Source,TableMetadata Table,string Field,ColumnMetadata Relation)? result=null;
            try
            {
                var definition=Definition(relation);
                var field=Value(definition,"nameField");
                if(field.Length==0)field="name";
                var pageId=MultiPageId(definition);
                if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
                var tableId=MultiDefinitionId(definition,"tableId");
                if(tableId.Length==0)tableId=relation.LinkConfigTableId;
                if(tableId.Length==0&&pageId.Length>0)tableId=catalog.ConfigPages.SingleOrDefault(page=>page.Id==pageId)?.ConfigTableId??"";
                var tableName=Value(definition,"table");
                if(tableName.Length==0)tableName=Text(relation,"joinTable");
                if(tableName.Length==0&&relation.ColumnName.EndsWith("_ids",StringComparison.Ordinal))tableName=relation.ColumnName[..^4];
                var target=catalog.ConfigTables.SingleOrDefault(table=>tableId.Length>0?table.Id==tableId:table.TableName==tableName);
                if(target is null)continue;
                var source=catalog.ConfigTables.SingleOrDefault(table=>table.Id==relation.ConfigTableId);
                // 普通默认页没有固定名称计划，也应继续发现各个来源页明确配置的固定候选。
                if(target.IsRongyu!=0&&MultiTargetNamePage(catalog,target,relation) is null&&
                    !(relation.ControlKind=="MultiSelectPage"&&source is {IsRongyu:0}&&!GlobalCrossTableTarget.Declared(source)&&
                        catalog.ConfigPages.SingleOrDefault(page=>page.Id==pageId&&page.ConfigTableId==target.Id) is {} original&&
                        IsProjectOnlyStoredMultiSelectPage(catalog,target,original)))continue;
                result=(source,target,field,relation);
            }
            catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
            {
                // 无效控件不改变无关页面的读取权限。
            }
            if(result is not {} candidate)continue;
            var (owner,table,name,column)=candidate;
            var defaultPage=MultiTargetNamePage(catalog,table,column);
            yield return (owner,table,name,column,null,defaultPage);

            if(owner is not {IsRongyu:0}||GlobalCrossTableTarget.Declared(owner)||GlobalCrossTableTarget.Declared(table))
                continue;
            foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId==owner.Id&&
                (page.YzProjectId.Length==0||page.YzProjectId==catalog.YzProjectId)))
            {
                var selected=DeclaredMultiPageOverride(catalog,table,name,column,sourcePage)??defaultPage;
                if(selected is not null&&(selected.Id!=defaultPage?.Id||
                    HasCompoundMultiSelectSource(catalog,owner,sourcePage,column,selected)))
                    yield return (owner,table,name,column,sourcePage,selected);
            }
        }
    }

    /// <summary>只保留由普通非冗余来源页证明的多选父范围，无效配置仍由发布门禁报告。</summary>
    private static bool HasCompoundMultiSelectSource(MetadataCatalog catalog,TableMetadata owner,PageMetadata sourcePage,
        ColumnMetadata relation,PageMetadata targetPage)
    {
        if(relation.ControlKind!="MultiSelectPage"||owner.IsRongyu!=0)return false;
        try
        {
            return MultiParent(catalog,targetPage)?.Source(catalog,owner,relation,sourcePage).AdditionalColumns.Count>0;
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or JsonException)
        {
            return false;
        }
    }

    /// <summary>保留实际候选页。原多选可在同一目标表用另一物理文本名称。</summary>
    private static PageMetadata? DeclaredMultiPageOverride(MetadataCatalog catalog,TableMetadata target,string name,
        ColumnMetadata relation,PageMetadata sourcePage)
    {
        try
        {
            var field=catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==sourcePage.Id&&
                item.ConfigColumnId==relation.Id);
            if(field is null)return null;
            var overlay=field.Extra.GetValueOrDefault("controlDetail").ToString();
            var kind=field.Extra.GetValueOrDefault("controlKind").ToString();
            if(string.IsNullOrWhiteSpace(overlay)||kind.Length>0&&kind!=relation.ControlKind)return null;
            var checkbox=relation.ControlKind=="MultiCheckBoxDatabase";
            JsonElement definition;
            if(checkbox)
                definition=DatabaseCheckboxDefinition.Parse(relation.ControlDetail).WithPageOverride(overlay).ReadDefinition(relation);
            else
            {
                using var document=JsonDocument.Parse(overlay);
                definition=document.RootElement.Clone();
                if(definition.ValueKind!=JsonValueKind.Object||
                    definition.EnumerateObject().Select(item=>item.Name).Distinct(StringComparer.Ordinal).Count()!=
                    definition.EnumerateObject().Count()||
                    definition.EnumerateObject().Any(item=>item.Name is "table" or "nameField"&&
                        item.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)))return null;
            }

            var tableId=MultiDefinitionId(definition,"tableId");
            var tableName=Value(definition,"table");
            var nameOverride=Value(definition,"nameField");
            if(tableId.Length>0&&tableId!=target.Id||tableName.Length>0&&tableName!=target.TableName)return null;
            if(nameOverride.Length>0&&nameOverride!=name)
            {
                if(checkbox)return null;
                var storage=MetadataStoragePolicy.ForTable(catalog,target);
                var labels=storage.PhysicalColumns;
                if(!PhysicalTextMultiLabel(storage,labels.SingleOrDefault(column=>column.ColumnName==name))||
                    !PhysicalTextMultiLabel(storage,labels.SingleOrDefault(column=>column.ColumnName==nameOverride)))return null;
            }
            var pageId=MultiPageId(definition);
            if(checkbox&&pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
            return catalog.ConfigPages.SingleOrDefault(page=>page.ConfigTableId==target.Id&&page.Id==pageId&&
                (page.YzProjectId.Length==0||page.YzProjectId==catalog.YzProjectId)&&
                (checkbox||page.Extra.GetValueOrDefault("type").ToString()=="2"));
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException or JsonException)
        {
            // 无效的页级定义由原控件发布门禁报告，不能使其他目标页取得额外范围。
            return null;
        }
    }

    /// <summary>复用固定跨表读取资格，保留每级的唯一可读页、物理条件及原项目范围。</summary>
    private static NonRedundantCrossTableScope CascadeLabelScope(MetadataCatalog catalog,ColumnMetadata source)
    {
        var levels=Definition(source).GetProperty("levels").EnumerateArray().ToArray();
        var scopes=new List<NonRedundantCrossTableTargetScope>();
        for(var index=0;index<levels.Length;index++)
        {
            var level=levels[index];
            var target=catalog.ConfigTables.Single(table=>table.TableName==Value(level,"table"));
            var name=Value(level,"nameField");
            if(name.Length==0)name="name";
            var parent=index==0?"":Value(level,"parentField");
            var (page,condition)=CascadeLabelPage(catalog,target,name,parent,allowSelfTree:true);
            scopes.Add(new(page.Id,condition));
        }
        return new(source.Id,scopes.ToArray());
    }

    /// <summary>读页选择与原级联相同。只接受原账号等值条件与物理常量。</summary>
    internal static (PageMetadata Page,FixedConditionSqlPlan? Condition) CascadeLabelPage(
        MetadataCatalog catalog, TableMetadata target, string name, string parent,bool allowSelfTree=false)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,target);
        var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id));
        var nameColumn=columns.Single(column=>column.ColumnName==name);
        var parentColumn=parent.Length==0?null:columns.Single(column=>column.ColumnName==parent);
        var required=parentColumn is null?new[]{nameColumn.Id}:new[]{nameColumn.Id,parentColumn.Id};
        var allFields=columns.Select(column=>column.ColumnName).ToArray();
        var candidates=catalog.ConfigPages.Where(page=>page.ConfigTableId==target.Id&&page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length==0&&
            required.All(id=>catalog.ConfigPageColumns.Any(column=>column.ConfigPageId==page.Id&&column.ConfigColumnId==id&&
                ((column.Extra.ContainsKey("isInGrid")?CrossFlag(column,"isInGrid"):column.IsShow==1) ||
                (column.Extra.ContainsKey("isInEdit")?CrossFlag(column,"isInEdit"):column.IsEdit==1)))))
            .Select(page=>(Page:page,Syntax:FixedConditionPlan.Compile(page.WhereSql,allFields)))
            .Where(item=>!item.Syntax.Variables.Any(variable=>variable.Equals("parentId",StringComparison.OrdinalIgnoreCase)||variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if(candidates.Length!=1)throw new NotSupportedException("级联多值名称要求每级唯一且无父参数的可读页面。");
        var (chosen,syntax)=candidates[0];
        var treeNames=allowSelfTree?SelfTreeConditionFields(catalog,target,chosen,columns,syntax.Fields):null;
        return(chosen,CascadeScopeCondition(target,storage,columns,syntax,selfTreeFields:treeNames));
    }

    /// <summary>固定页条件保留实际类型与原账号等值，只有已声明逻辑路径可以引用非物理层位。</summary>
    private static FixedConditionSqlPlan? CascadeScopeCondition(TableMetadata target,MetadataStoragePlan storage,
        IReadOnlyList<ColumnMetadata> columns,FixedConditionPlan syntax,ColumnMetadata? multiParent=null,
        IReadOnlySet<string>? selfTreeFields=null,IReadOnlySet<string>? additionalParents=null,IReadOnlySet<string>? joinedFields=null)
    {
        var terms=syntax.Terms.Where(term=>!IsProjectScope(term)&&!((multiParent?.ColumnName==term.Field||additionalParents?.Contains(term.Field)==true)&&
            term.Values.Count==1&&term.Values[0].Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(term.Values[0].Text))).ToArray();
        if(terms.Length==0)return null;
        var physical=columns.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,target,column))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
        foreach(var term in terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||term.Field=="yz_project_id"||
                CrossTableStorageColumns.IsLogicalPathField(target,columns,term.Field)||selfTreeFields?.Contains(term.Field)==true||!AccountConditionVariables.IsPageField(term.Values[0].Text,term.Field)||
                !physical.TryGetValue(term.Field,out var type)||type is not("int32" or "int64" or "string"))
                throw new NotSupportedException("级联多值名称只允许原账号字段的物理等值范围。");
        var types=new Dictionary<string,string>(physical,StringComparer.OrdinalIgnoreCase);
        foreach(var column in columns.Where(column=>CrossTableStorageColumns.IsLogicalPathField(target,columns,column.ColumnName)||
            selfTreeFields?.Contains(column.ColumnName)==true||joinedFields?.Contains(column.ColumnName)==true))
            types.TryAdd(column.ColumnName,column.LogicalType);
        var condition=FixedConditionPlan.Compile(string.Join(" AND ",terms.Select(term=>term.ToDefinition())),types.Keys);
        return FixedConditionSqlPlan.Compile(condition,types);
    }

    /// <summary>项目等值由同一物理范围生成，不在内部候选再绑定请求字段。</summary>
    private static bool IsProjectScope(FixedConditionTerm term)=>term.Field=="yz_project_id"&&
        term.Comparison==FixedConditionComparison.Equal&&term.Values.Count==1&&
        term.Values[0].Kind==FixedConditionValueKind.Variable&&
        term.Values[0].Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);

    private static bool CrossFlag(MetadataRecord column,string key)=>column.Extra.TryGetValue(key,out var flag)&&
        (flag.ValueKind==System.Text.Json.JsonValueKind.True||flag.ToString()=="1");

    private sealed partial class Builder
    {
        private readonly string? cascadeLabelField;
        private readonly HashSet<string> cascadeLabelOutputs=new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> cascadeLabelPages=new(StringComparer.Ordinal);
        private HashSet<string>? declaredMultiLabelFields;
        private readonly CascadeCacheOwner? cascadeCacheOwner;
        private readonly HashSet<string> cascadeCacheOwnerFields=new(StringComparer.Ordinal);

        /// <summary>递归父页会叠加多层别名，长参数前缀压缩后仍按原作用域稳定区分。</summary>
        private static string ConditionParameterPrefix(string prefix)=>prefix.Length<=64?prefix:
            "mt_scope_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix)))[..32];

        /// <summary>读候选绑定请求账号。重算缓存绑定来源记录的物理归属，两条路径复用同一候选条件。</summary>
        private string CascadeLabelCondition(TableMetadata target,PageMetadata page,FixedConditionSqlPlan condition,string alias,string prefix,CascadeCacheOwner? cacheOwner=null)
        {
            prefix=ConditionParameterPrefix(prefix);
            if((cacheOwner??cascadeCacheOwner) is {} owner&&condition.Variables.Count>0)
            {
                var proof=AccountScopedCrossCache.Compile(catalog,owner.Table,target,page,owner.GlobalTreeSource,globalSingleSource:owner.GlobalSingleSource);
                cascadeCacheOwnerFields.UnionWith(proof.Ownership.Select(binding=>binding.Source.ColumnName));
                return AccountCacheScope(proof,target,alias,owner.Alias,render:true,owner.Table,prefix);
            }
            var clause=condition.RenderAccountProjection(new MySqlDatabaseProvider(),prefix,field=>
            {
                Use(target,Column(target,field));
                Touch(alias,field);
                return alias+"."+Q(field);
            });
            foreach(var pair in clause.Parameters)crossConstants.Add(pair.Key,pair.Value);
            return clause.Sql;
        }

        internal IReadOnlySet<string> MultiImportFields { get; init; }=new HashSet<string>(StringComparer.Ordinal);
        internal IReadOnlySet<string> MultiImportCrossFields { get; init; }=new HashSet<string>(StringComparer.Ordinal);

        /// <summary>精确名称和 Excel 固定条件都可读取已证明的级联表达式。</summary>
        private bool AllowsCascadeLabel(TableMetadata table,string alias)
        {
            if(multiLabel&&(output==cascadeLabelField||MultiImportFields.Contains(output)))return true;
            if(matching||refreshFields is not null||table.Id==root.Id&&alias==rootAlias)return false;
            declaredMultiLabelFields??=DeclaredMultiNameTargets(catalog).Where(item=>item.Table.Id==root.Id)
                .Select(item=>item.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return declaredMultiLabelFields.Contains(output);
        }

        /// <summary>多值名称接入完整稀疏路径。坏存储或共享分支冲突不能进入名称数组、筛选或导入。</summary>
        private (string Sql,string Type)? TryCascadeLabel(TableMetadata table,string owner,ColumnMetadata column)
        {
            var field=column.ColumnName;
            var importPath=table.Id==root.Id&&owner==rootAlias&&MultiImportCrossFields.Contains(field);
            if(table.IsRongyu!=0||column.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(field)||
                !importPath&&(column.LogicalType!="string"||storage[table.Id].IsPhysical(column)||!field.EndsWith("_name",StringComparison.Ordinal)))
                return null;
            var control=columns[table.Id].Values.FirstOrDefault(item=>item.ControlKind=="CascadeTree"&&
                (CrossTableStorageColumns.ContainsPathField(item,field)||IsSelfTreeLabel(item,field)));
            if(control is null||!AllowsCascadeLabel(table,owner))return null;
            if(IsSelfTreeLabel(control,field))
            {
                var leaf=SelfTreeLabelExpression(table,owner,control,column);
                cascadeLabelOutputs.Add(output);
                return(leaf,"string");
            }
            var group=SharedCrossGroup(table,control.Id);
            (string Sql,string Type) expression;
            string[] controls;
            if(group is null)
            {
                var binding=CascadeLabelScope(catalog,control);
                cascadeLabelPages.UnionWith(binding.Levels.Select(level=>level.PageId));
                var selection=CrossTableSparseSelection.Compile(table,control.Id,columns[table.Id].Values);
                expression=CrossValue(table,owner,control,selection,binding,field,labelPath:true);
                controls=[control.Id];
            }
            else
            {
                controls=group.Branches.Select(branch=>branch.SourceColumnId).ToArray();
                foreach(var controlId in controls)
                {
                    var member=columns[table.Id].Values.Single(item=>item.Id==controlId);
                    var binding=CascadeLabelScope(catalog,member);
                    cascadeLabelPages.UnionWith(binding.Levels.Select(level=>level.PageId));
                    crossTables[member.Id]=binding;
                }
                expression=SharedCrossValue(table,owner,group,field,labelPath:true);
            }
            var validity=string.Join(" AND ",controls.Select(controlId=>"("+crossProjections[owner+":"+controlId].Validity+")"));
            cascadeLabelOutputs.Add(output);
            return("CASE WHEN "+validity+" THEN "+expression.Sql+" END",expression.Type);
        }
    }
}
