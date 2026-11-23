using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Relations;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>候选名称调用实际中间页的单值范围，同时完整编译名称投影。</summary>
    internal static MySqlNonRedundantReadPlan CompileProjectRelation(MetadataCatalog catalog,string tableId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope> singleTargets)=>
        CompileRelationScope(catalog,tableId,outputs,[],dependencies,deferCascadeNames:false,singleTargets:singleTargets);

    /// <summary>按实际候选页编译固定名称，沿页级控件绑定字典并校验单值来源。</summary>
    internal static MySqlNonRedundantReadPlan CompilePublishedScalarMatch(MetadataCatalog catalog,string tableId,string pageId,string field)
    {
        MetadataValidation.Validate(catalog);
        var page=catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        var plan=new Builder(catalog,tableId,matching:true){CrossNameSourcePage=page}.Compile([field],[]);
        if(!plan.Origins.TryGetValue(field,out var terminal)||ExactDataManageValues.IsSensitiveField(terminal.Column)||
            plan.MultiProjections.Count>0||plan.Crosses.Count>0)
            throw new NotSupportedException("关系名称必须落到实际发布页的非凭据单值终点。");
        return plan;
    }

    /// <summary>候选与实际父路径使用同一发布页范围；调用端不能替换其中任一逻辑条件。</summary>
    internal static MySqlNonRedundantReadPlan CompileCrossPageCandidate(MetadataCatalog catalog,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,
        IReadOnlyList<NonRedundantCrossTableScope>? displayBindings=null,string rootAlias="b",string aliasPrefix="r",
        HashSet<(string TableId,string ControlId)>? activeSelfTreeScopes=null,IReadOnlyList<string>? listFields=null,
        bool projectSingle=false,string? accountParentPageType=null,
        IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets=null,bool exactSingleParent=false,
        bool exactCheckboxParent=false,string? projectSingleControlKind=null,bool globalExactSingle=false)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(dependencies);
        if(outputs.Count==0||outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count)
            throw new InvalidOperationException("候选输出字段不能为空或重复。");
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("递归候选尚未完成当前方言适配。");
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        var page=catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        var global=GlobalCrossTableTarget.Declared(table);
        var exactField=page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString();
        var exactParent=exactSingleParent||exactCheckboxParent;
        var globalExactParent=globalExactSingle?GlobalCrossTableTarget.ExactPageParent(catalog,table,page):null;
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==tableId));
        var pageType=page.Extra.GetValueOrDefault("type").ToString();
        var publishedMatch=(projectSingle||exactSingleParent)?PublishedSingleExactColumn(catalog,table,page,columns):null;
        // 已保存名称由来源记录授权。候选和写入仍在自己的入口校验目标页精确权限。
        var storedSingle=projectSingle&&publishedMatch is not null&&pageType is "" or "0" or "1"&&
            projectSingleControlKind is "List" or "SelectPage";
        if(table.IsRongyu!=0||exactField.Length>0&&!exactParent&&!storedSingle&&!globalExactSingle||
            page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
            throw new NotSupportedException("候选必须属于已发布项目的普通非冗余目标页。");
        if(exactSingleParent&&(global||exactField.Length==0||
            pageType!="1"&&!(publishedMatch is not null&&pageType is "" or "0")))
            throw new NotSupportedException("精确单值候选必须使用已发布的项目内单值页面。");
        if(exactCheckboxParent&&(global||exactField.Length==0||
            page.Extra.GetValueOrDefault("type").ToString() is not ("" or "0" or "1" or "2")))
            throw new NotSupportedException("精确复选候选必须使用已发布的项目内业务或选择页面。");
        var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        var logical=syntax.Fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)).ToHashSet(StringComparer.Ordinal);
        var treeNames=SelfTreeConditionFields(catalog,table,page,columns,syntax.Fields);
        var listTrees=SelfTreeConditionFields(catalog,table,page,columns,listFields??[]);
        var listCrosses=(listFields??[]).Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)).ToArray();
        // 复选独立使用已发布的稀疏父字段，普通业务和多选页沿用各自页面类型。
        var projectParent=exactCheckboxParent?MultiTargetParentScope.Compile(catalog,table,syntax):
            !global?ProjectScopedSingleRelationTarget.PageParent(catalog,table,page):null;
        if(exactParent&&projectParent is not ({Logical:true} or {AdditionalTargets.Count:>0})&&
            !(exactSingleParent&&projectParent is {Global:true}))
            throw new NotSupportedException("精确候选缺少已发布的父范围。");
        if(listFields is not null&&!globalExactSingle&&!projectSingle&&
            (!global&&projectParent is null||listTrees.Count==0&&listCrosses.Length==0))
            throw new NotSupportedException("固定下拉附加条件必须引用已发布的逻辑级联字段。");
        // 全局单值候选从自身发布页证明物理父键；旧多选仍满足原独立页合同。
        var physicalParent=globalExactSingle?globalExactParent:
            global&&page.Extra.GetValueOrDefault("type").ToString()!="2"?GlobalCrossTableTarget.SingleParent(catalog,table,page):null;
        var accountParentPage=accountParentPageType is not null;
        var hasParentScope=physicalParent is not null||accountParentPage||displayBindings is not null||
            listFields is null&&treeNames.Count>0&&IsScopedMultiTarget(catalog,table,pageId);
        if(global&&!globalExactSingle)
        {
            if(treeNames.Count>0)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,table,page,allowMultiParent:hasParentScope);
            else if(displayBindings is null&&!accountParentPage&&physicalParent is null)GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,table,page);
            else GlobalCrossTableTarget.RequireMultiCandidate(catalog,table,page);
        }
        if(!globalExactSingle&&!projectSingle&&!accountParentPage&&physicalParent is null&&
            projectParent is not ({Global:true} or {AdditionalTargets.Count:>0})&&logical.Count==0&&
            treeNames.Count==0&&listFields is null&&(!global||displayBindings is not{Count:>0}))
            throw new InvalidOperationException("候选页未声明逻辑级联条件或已证明的全局跨表投影。");
        var parent=physicalParent??projectParent??(hasParentScope||projectSingle
            ?global?MultiTargetParentScope.CompileGlobalPage(catalog,table,syntax):MultiTargetParentScope.Compile(catalog,table,syntax)
            :null);
        if(accountParentPage&&(!global||page.Extra.GetValueOrDefault("type").ToString()!=accountParentPageType||
            parent is null||(accountParentPageType=="2"
                ?parent.Logical?parent.AdditionalTargets.Count==0:!syntax.Variables.Any(Metarill.Core.Security.AccountConditionVariables.IsSupported)
                :physicalParent is null)))
            throw new NotSupportedException("全局候选必须使用实际发布的条件和物理父键。");
        var additional=parent?.AdditionalTargets.Select(scope=>scope.Target.ColumnName).ToHashSet(StringComparer.Ordinal);
        var condition=CascadeScopeCondition(table,MetadataStoragePolicy.ForTable(catalog,table),columns,syntax,parent?.Target,treeNames,additional);
        // 私有匹配和 Excel 条件只扩充投影；不把路径有效性强加给有无限定范围的账号。
        var match=exactParent||globalExactSingle?Metarill.Core.Security.ExactDataManageValues.Field(exactField):"";
        var exactCross=globalExactSingle&&CrossTableStorageColumns.IsLogicalPathField(table,columns,match);
        var pageScopes=globalExactSingle&&(exactCross||listCrosses.Length>0)
            ?logical.Count==0?[]:CrossMembershipBindings(catalog,table,page,logical)
            :null;
        logical.UnionWith(listCrosses);
        if(exactCross)logical.Add(match);
        var projectionTrees=treeNames.Concat(listTrees).ToHashSet(StringComparer.Ordinal);
        if(exactSingleParent||globalExactSingle)
        {
            // 隐藏的精确叶名也要按实际发布的树求值，并验证页面覆盖。
            projectionTrees.UnionWith(SelfTreeConditionFields(catalog,table,page,columns,[match]));
        }
        var scopes=logical.Count==0?[]:CrossMembershipBindings(catalog,table,page,logical);
        // 范围的绑定按发布页生成，其它绑定仅用于该独立选择页展示额外的路径。
        var bindings=scopes.Concat(displayBindings??[]).DistinctBy(path=>path.SourceColumnId,StringComparer.Ordinal).ToArray();
        var builder=new Builder(catalog,tableId,deferCascadeNames:projectionTrees.Count>0||accountParentPage||physicalParent is not null,crossTables:bindings,rootAlias:rootAlias,aliasPrefix:aliasPrefix)
        {
            CrossCandidateCondition=condition,
            SelfTreeCandidateFields=projectionTrees,
            ActiveSelfTreeScopes=activeSelfTreeScopes??new(),
            MultiCandidateParent=projectSingle&&parent?.AdditionalTargets.Count is not >0?null:parent,
            MultiCandidatePage=page,
            GlobalCrossCandidate=global,
            CrossNameSourcePage=page,
            MultiNamePathPage=globalExactSingle?page:null,
            ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal),
            ScopedRootControls=(pageScopes??scopes).Select(path=>path.SourceColumnId).ToHashSet(StringComparer.Ordinal),
            CrossAliasPrefix=rootAlias=="b"?"cross":aliasPrefix+"c"
        };
        // 精确范围与父级条件都留在计划中。授权集合由真实读取入口单独绑定。
        return builder.Compile(outputs,dependencies.Concat(syntax.Fields).Concat(listFields??[])
            .Concat(exactParent||globalExactSingle ? new[] { match } : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>写入锁只读取匹配字段的实际联接或级联，目标根的父级和页面范围由外层查询校验。</summary>
    internal static MySqlNonRedundantReadPlan CompileGlobalExactSingleMatch(MetadataCatalog catalog,string tableId,
        string pageId,string field,IReadOnlyList<NonRedundantSingleTargetScope>? singleTargets)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("全局精确单选派生匹配要求 MySQL。");
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        var page=catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        _=GlobalCrossTableTarget.ExactPageParent(catalog,table,page);
        var match=Metarill.Core.Security.ExactDataManageValues.Field(page.Extra["exactDataManageMatchField"].ToString());
        if(field!=match||table.IsRongyu!=0)
            throw new NotSupportedException("全局精确单选必须使用实际发布的派生匹配字段。");
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(item=>item.ConfigTableId==tableId));
        var column=columns.Single(item=>item.ColumnName==match);
        var tree=SelfTreeConditionFields(catalog,table,page,columns,[match]);
        var cross=CrossTableStorageColumns.IsLogicalPathField(table,columns,match);
        // 原自树辅助名称也可能标为联接，优先使用已证明的级联表达式。
        var joined=!cross&&tree.Count==0&&MetadataStoragePolicy.ForTable(catalog,table).Kind(column)==MetadataFieldStorage.Join;
        if(!cross&&tree.Count==0&&!joined)
            throw new NotSupportedException("全局精确单选派生字段缺少已发布联接或级联。");
        var paths=cross?CrossMembershipBindings(catalog,table,page,new HashSet<string>(StringComparer.Ordinal){match}):[];
        var plan=new Builder(catalog,tableId,matching:joined,deferCascadeNames:tree.Count>0,crossTables:paths)
        {
            GlobalCrossCandidate=true,
            MultiNamePathPage=page,
            SelfTreeCandidateFields=tree,
            ProjectSingleTargets=(singleTargets??[]).ToDictionary(binding=>binding.ColumnId,StringComparer.Ordinal)
        }.Compile([field],[]);
        if(cross)_=ExactDataManageSql.CrossExpression(plan,match);
        return plan;
    }

    /// <summary>独立父ID校验取实际全局层的发布页。多个共享分支必须指向同一页，不回退到任意全局候选。</summary>
    internal static MySqlNonRedundantReadPlan CompileMultiGlobalParent(MetadataCatalog catalog,string tableId,string pageId)
    {
        MetadataValidation.Validate(catalog);
        if(catalog.DatabaseProvider!="mysql")throw new NotSupportedException("固定全局父页要求已发布的MySQL读取计划。");
        var (parent,published)=PublishedMultiGlobalParent(catalog,tableId,pageId);
        return CompileMultiParentPage(catalog,parent,published,"b","r",null,false);
    }

    /// <summary>同一父页供请求点查和关联范围使用，维护从实际来源行读取账号归属。</summary>
    private static MySqlNonRedundantReadPlan CompileMultiParentPage(MetadataCatalog catalog,TableMetadata parent,
        PageMetadata published,string alias,string prefix,CascadeCacheOwner? owner,bool trackReverse)
    {
        var columns=RelationStorageColumns.Expand(parent,catalog.ConfigColumns.Where(column=>column.ConfigTableId==parent.Id));
        var syntax=FixedConditionPlan.Compile(published.WhereSql,columns.Select(column=>column.ColumnName));
        return syntax.Fields.Any(member=>CrossTableStorageColumns.IsLogicalPathField(parent,columns,member)||
            CascadeTreeStorageColumns.IsLogicalNameField(parent,columns,member))
            ?new Builder(catalog,parent.Id,rootAlias:alias,aliasPrefix:prefix,trackReverse:trackReverse,multiLabel:true,cascadeLabelField:"id",cascadeCacheOwner:owner)
                {SelectedMultiPage=published,GlobalCrossCandidate=true,CrossAliasPrefix=prefix+"cross"}.Compile(["id"],[])
            :new Builder(catalog,parent.Id,rootAlias:alias,aliasPrefix:prefix,trackReverse:trackReverse,candidatePage:published,
                cascadeCacheOwner:owner,accountPhysicalCandidate:true,
                globalCandidate:parent.IsRongyu==0&&GlobalCrossTableTarget.Declared(parent)).Compile(["id"],[]);
    }

    /// <summary>多个目标页可共享同一个实际全局父页面，独立来源父校验只绑定该发布页。</summary>
    internal static (TableMetadata Table,PageMetadata Page) PublishedMultiGlobalParent(MetadataCatalog catalog,string tableId,string pageId)
    {
        var page=catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        if(page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
        var checkbox=!GlobalCrossTableTarget.Declared(table)&&page.Extra.GetValueOrDefault("type").ToString()!="2"&&
            IsScopedMultiTarget(catalog,table,page.Id);
        var parent=checkbox?MultiParent(catalog,page,checkbox:true):
            ProjectScopedSingleRelationTarget.PageParent(catalog,table,page)??MultiParent(catalog,page);
        if(parent is not{Global:true})throw new NotSupportedException("多值全局父须显式声明全局表关系。");
        var pages=parent.AdditionalTargets.Prepend(parent)
            .Select(scope=>scope.Logical?PublishedLogicalMultiParent(catalog,table,page,scope):
                PublishedPhysicalMultiParent(catalog,scope)).DistinctBy(item=>item.Id).ToArray();
        if(pages.Length!=1)
            throw new InvalidOperationException("同一全局父记录的各物理外键和级联层位必须绑定同一个实际发布父页。");
        return(parent.Parent,pages[0]);
    }

    /// <summary>每条来源路径及物理外键复用目标的实际发布父页；等值字段不能借另一页权限。</summary>
    internal static void RequireCompoundGlobalSourceParent(MetadataCatalog catalog,string tableId,string pageId,MultiSourceParent source)
    {
        if(source.AdditionalColumns.Count==0)return;
        var (parent,published)=PublishedMultiGlobalParent(catalog,tableId,pageId);
        var owner=catalog.ConfigTables.Single(table=>table.Id==source.Column.ConfigTableId);
        foreach(var column in source.AdditionalColumns.Prepend(source.Column))
        {
            var logical=CrossTableStorageColumns.IsLogicalPathField(owner,catalog.ConfigColumns,column.ColumnName);
            var scope=new MultiTargetParentScope(parent,column,logical);
            var actual=logical
                ?source.LogicalPages.Select(page=>PublishedLogicalMultiParent(catalog,owner,page,scope)).ToArray()
                :[PublishedPhysicalMultiParent(catalog,scope)];
            if(actual.Length==0||actual.Any(page=>page.Id!=published.Id))
                throw new InvalidOperationException("同一全局父记录的来源字段与目标字段必须绑定同一个实际发布父页。");
        }
    }

    /// <summary>沿实际固定级联层位核对全局父页，所有共享支路须指向同一页。</summary>
    private static PageMetadata PublishedLogicalMultiParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page,
        MultiTargetParentScope parent)
    {
        var field=parent.Target.ColumnName;
        var bindings=CrossMembershipBindings(catalog,table,page,new HashSet<string>(StringComparer.Ordinal){field});
        var parentPages=bindings.Select(binding=>
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id==binding.SourceColumnId);
            var levels=Definition(control).GetProperty("levels").EnumerateArray().ToArray();
            for(var index=0;index<levels.Length;index++)
            {
                var name=Value(levels[index],"table");
                var prefix=Value(levels[index],"columnPrefix");
                if((prefix.Length==0?name:prefix)+"_id"!=field)continue;
                if(name!=parent.Parent.TableName)throw new InvalidOperationException("多值全局父的层位与发布表不符。");
                return binding.Levels[index].PageId;
            }
            return null;
        }).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        if(parentPages.Length!=1)throw new InvalidOperationException("多值全局父缺少唯一固定候选页。");
        var published=catalog.ConfigPages.Single(item=>item.Id==parentPages[0]&&item.ConfigTableId==parent.Parent.Id);
        RequireFixedGlobalParent(catalog,parent.Parent,published);
        return published;
    }

    /// <summary>物理外键必须直接绑定父表，控件与附加页面声明冲突时拒绝发布。</summary>
    private static PageMetadata PublishedPhysicalMultiParent(MetadataCatalog catalog,MultiTargetParentScope parent)
    {
        var definition=Definition(parent.Target);
        var explicitPage=MultiPageId(definition);
        var linkedPage=Text(parent.Target,"linkConfigPageId");
        if(explicitPage.Length>0&&linkedPage.Length>0&&explicitPage!=linkedPage||
            MultiDefinitionId(definition,"tableId") is {Length:>0} tableId&&tableId!=parent.Parent.Id||
            Value(definition,"table") is {Length:>0} tableName&&tableName!=parent.Parent.TableName||
            Text(parent.Target,"joinTable") is {Length:>0} joinTable&&joinTable!=parent.Parent.TableName)
            throw new InvalidOperationException("全局物理父键的表或页面声明冲突。");
        var pageId=explicitPage.Length>0?explicitPage:linkedPage;
        var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==parent.Parent.Id&&
            (pageId.Length==0||page.Id==pageId)).Take(2).ToArray();
        if(pages.Length!=1)throw new InvalidOperationException("全局物理父键必须绑定唯一发布页。");
        RequireFixedGlobalParent(catalog,parent.Parent,pages[0]);
        return pages[0];
    }

    /// <summary>来自多值页的全局父只使用实际发布页；叶名条件随后由同一读取计划执行。</summary>
    private static void RequireFixedGlobalParent(MetadataCatalog catalog,TableMetadata table,PageMetadata page)
    {
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));
        if(PublishedSelfTreeConditionFields(catalog,table,page,columns).Count>0)
            GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,table,page);
        else GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,table,page);
    }

    private sealed partial class Builder
    {
        internal FixedConditionSqlPlan? CrossCandidateCondition{get;init;}
        internal PageMetadata? SelectedMultiPage{get;init;}
        internal bool AllowLiteralMultiScope{get;init;}
        internal MultiTargetParentScope? MultiCandidateParent{get;set;}
        internal PageMetadata? MultiCandidatePage{get;init;}
        private string? selectedMultiParentSql;
        private readonly Dictionary<string,string> additionalMultiParentSql=new(StringComparer.Ordinal);

        /// <summary>补全其它实际父字段的逻辑投影，并以同一规范父键同时限定所有层位。</summary>
        private string ApplyAdditionalMultiParents(MultiTargetParentScope parent,string canonical,
            IDictionary<string,string> expressions,IDictionary<string,string> types)
        {
            if(parent.AdditionalTargets.Count==0)return "";
            if(parent is {Global:true,Logical:true})
            {
                // 级联已校验可见路径；混合物理外键还必须与该路径共享同一发布父页。
                var page=SelectedMultiPage??MultiCandidatePage??throw new InvalidOperationException("缺少同父范围的实际发布页。");
                _=PublishedMultiGlobalParent(catalog,root.Id,page.Id);
            }
            var predicates=new List<string>();
            foreach(var scope in parent.AdditionalTargets)
            {
                var field=scope.Target.ColumnName;
                var value=scope.Logical
                    ?additionalMultiParentSql.GetValueOrDefault(field)??expressions[field]
                    :rootAlias+"."+Q(field);
                if(scope.Logical)
                {
                    expressions[field]=value;
                    types[field]=scope.Target.LogicalType;
                }
                else
                {
                    Use(root,scope.Target);
                    Touch(rootAlias,field);
                }
                predicates.Add(SameSingleParent(value,canonical));
            }
            return string.Concat(predicates.Select(predicate=>" AND "+predicate));
        }

        /// <summary>仅选中页的逻辑父级交由真实路径恢复。</summary>
        private bool IsSelectedLogicalParent(string field)=>SelectedMultiPage is not null&&
            MultiCandidateParent is {} parent&&(parent.Logical&&parent.Target.ColumnName==field||
                parent.AdditionalTargets.Any(scope=>scope.Logical&&scope.Target.ColumnName==field));
        internal (CascadeCacheOwner Owner,ColumnMetadata Column,IReadOnlyList<ColumnMetadata> Additional)? MultiParentOwner{get;init;}

        /// <summary>普通父表沿用实际项目范围。全局逻辑父由已验证的完整级联表达式确定可见性。</summary>
        private string MultiParentPredicate(MultiTargetParentScope parent,string target)
        {
            var predicate="("+target+") IS NOT NULL";
            if(!parent.Global)
            {
                var alias=aliasPrefix+"multi_parent";
                if(!parent.Logical){Use(root,parent.Target);Touch(rootAlias,parent.Target.ColumnName);}
                Use(parent.Parent,Column(parent.Parent,"id"));
                var key=alias+".`id`";
                var same=parent.Target.LogicalType=="string"?"BINARY CAST("+key+" AS CHAR)=BINARY ("+target+")":key+"="+target;
                predicate="EXISTS (SELECT 1 FROM "+Q(parent.Parent.TableName)+" "+alias+" WHERE "+same+" AND "+key+">0 AND "+Scope(parent.Parent,alias)+PublishedJoinLock+")";
            }
            else if(!parent.Logical)predicate=PhysicalGlobalParentPredicate(parent,target);
            if(MultiParentOwner is not {} binding)return predicate;
            var (owner,source,additional)=binding;
            cascadeCacheOwnerFields.Add(source.ColumnName);
            var fromOwner=owner.Alias+"."+Q(source.ColumnName);
            cascadeCacheOwnerFields.UnionWith(additional.Select(column=>column.ColumnName));
            // 批量名称读取使用真实行的全部父字段，高位整数仍按文本精确比较。
            return predicate+" AND "+SameSingleParent(target,fromOwner)+
                string.Concat(additional.Select(column=>" AND "+SameSingleParent(owner.Alias+"."+Q(column.ColumnName),fromOwner)));
        }

        /// <summary>使用完整固定父页关联现有物理键；内层变化即使使父页失去可见性，也进入反向传播。</summary>
        private string PhysicalGlobalParentPredicate(MultiTargetParentScope parent,string target)
        {
            var targetPage=SelectedMultiPage??MultiCandidatePage??throw new InvalidOperationException("缺少目标父范围的实际发布页。");
            var (table,published)=PublishedMultiGlobalParent(catalog,root.Id,targetPage.Id);
            var alias=aliasPrefix+"global_parent";
            var plan=BindPhysicalGlobalParentPage(table,published,alias,cascadeCacheOwner);
            Use(root,parent.Target);Touch(rootAlias,parent.Target.ColumnName);
            var key=alias+".`id`";
            var same=parent.Target.LogicalType=="string"?"BINARY CAST("+key+" AS CHAR)=BINARY ("+target+")":key+"="+target;
            TrackGlobalParentDependencies(plan," FROM "+Q(root.TableName)+" "+rootAlias,same);
            return "EXISTS (SELECT 1"+plan.From+" WHERE "+same+" AND "+key+">0"+(plan.Scope.Length==0?"":" AND "+plan.Scope)+")";
        }

        /// <summary>单值和多值共用全局父页编译，保留条件参数、物理字段及实际归属依赖。</summary>
        private MySqlNonRedundantReadPlan BindPhysicalGlobalParentPage(TableMetadata table,PageMetadata page,string alias,CascadeCacheOwner? owner)
        {
            var plan=CompileMultiParentPage(catalog,table,page,alias,alias+"_",owner,trackReverse);
            scopedGlobalCrossTarget=true;
            plan.CopyParametersTo(crossConstants);
            cascadeLabelPages.Add(page.Id);
            cascadeLabelPages.UnionWith(plan.CascadeLabelPages);
            cascadeCacheOwnerFields.UnionWith(plan.CascadeCacheOwnerFields);
            foreach(var reference in plan.PhysicalReferences)references.TryAdd((reference.Table,reference.Column.Name),reference);
            return plan;
        }

        /// <summary>保留父页内关联的反查路径，即使父页条件变化后当前行已不可见。</summary>
        private void TrackGlobalParentDependencies(MySqlNonRedundantReadPlan plan,string from,string same)
        {
            foreach(var reverse in plan.Reverse)
            {
                var probe="EXISTS (SELECT 1"+reverse.From+" WHERE "+same+" AND "+reverse.TargetPredicate+")";
                var fields=reverse.Outputs.Values.SelectMany(value=>value).ToHashSet(StringComparer.Ordinal);
                customReverse.Add(new(reverse.TargetTable,from,probe,"predicate",new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,fields}}));
            }
        }

        internal bool AllowsExactMultiSelection { get; init; }
        internal bool AllowsStoredExactCheckbox { get; init; }

        /// <summary>固定目标条件用于记录投影与维护。精确权限由发起选择的请求单独叠加。</summary>
        private bool UsesExactMultiSelection(PageMetadata page,string alias) =>
            (AllowsExactMultiSelection||AllowsStoredExactCheckbox)&&alias==rootAlias&&page.Id==SelectedMultiPage?.Id&&
            page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0;

        /// <summary>目标页的条件与名称共用当前记录和联接，维护用途按来源行归属绑定。</summary>
        private string SelectedMultiScope(string scope)
        {
            var page=SelectedMultiPage!;
            if(page.ConfigTableId!=root.Id||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
                throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
            var treeNames=PublishedSelfTreeConditionFields(catalog,root,page,columns[root.Id].Values.ToArray());
            var global=GlobalCrossTableTarget.Declared(root);
            var exact=UsesExactMultiSelection(page,rootAlias);
            if(exact)_=RequireExactMultiSelection(catalog,root,page,storedCheckbox:AllowsStoredExactCheckbox);
            else if(global)
            {
                if(treeNames.Count>0)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,root,page,allowMultiParent:MultiCandidateParent is not null);
                else GlobalCrossTableTarget.RequireMultiCandidate(catalog,root,page);
            }
            var additional=MultiCandidateParent is {AdditionalTargets.Count:>0} compound
                ?compound.AdditionalTargets.Select(parent=>parent.Target.ColumnName).ToHashSet(StringComparer.Ordinal):null;
            if(additional is not null)cascadeLabelPages.Add(page.Id);
            var condition=CrossTableParentCondition.Compile(catalog,root,page,storage[root.Id],allowLogical:true,
                multiParent:MultiCandidateParent?.Target,selfTreeFields:treeNames,separateExactScope:exact,additionalParents:additional);
            if(!condition.Allowed)
                throw new NotSupportedException("多值目标页包含未接通的范围。");
            if(!condition.HasLogical)
            {
                if(!AllowLiteralMultiScope&&MultiCandidateParent is not {Global:true,Logical:false}&&additional?.Count is not >0&&
                    condition.Plan is not {Variables.Count:>0})
                    throw new NotSupportedException("多值目标页缺少固定的级联或账号范围。");
                if(condition.Plan is not {} account)return scope;
                cascadeLabelPages.Add(page.Id);
                if(cascadeCacheOwner is {} owner)
                {
                    var predicate=PhysicalAccountCacheCondition(root,rootAlias,page,owner,MultiCandidateParent?.Target,
                        allowFixedOnly:AllowLiteralMultiScope,separateExactScope:exact,additionalParents:additional);
                    return scope.Length==0?predicate:scope+" AND "+predicate;
                }
                // 项目和父参数已由实际范围负责，账号条件只追加已证明的物理字段。
                var clause=account.RenderAccountProjection(new MySqlDatabaseProvider(),ConditionParameterPrefix(aliasPrefix+"multi_account"),field=>
                    rootAlias+"."+Q(Column(root,field).ColumnName));
                foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
                return scope.Length==0?clause.Sql:scope+" AND "+clause.Sql;
            }
            var plan=condition.Plan;
            var fields=RequireCrossCondition(root,page,plan,MultiCandidateParent?.Target,treeNames,additional);
            output=cascadeLabelField!;
            var level=new CrossLevel(root,Column(root,cascadeLabelField!),null,new(page.Id,plan),page,fields);
            var selected=NestedCrossCondition(level,rootAlias,multiParent:MultiCandidateParent?.Target,selfTreeFields:treeNames,
                additionalParents:additional);
            return scope.Length==0?selected:scope+" AND "+selected;
        }
        internal bool GlobalCrossCandidate{get;init;}
        internal IReadOnlySet<string> ScopedRootControls{get;init;}=new HashSet<string>(StringComparer.Ordinal);

        /// <summary>固定候选页过滤实际可见的整条路径，查询及计数无法省略范围。</summary>
        private string FixedCrossCandidateScope(string scope,IReadOnlyDictionary<string,string> expressions)
        {
            var clause=CrossCandidateCondition?.RenderAccountProjection(new MySqlDatabaseProvider(),
                rootAlias=="b"?"mt_candidate":ConditionParameterPrefix("mt_"+aliasPrefix+"candidate"),
                field=>expressions[field]);
            if(clause is not null)
                foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
            var paths=ScopedRootControls.Select(source=>crossProjections[rootAlias+":"+source]);
            return string.Join(" AND ",new[]{scope,clause?.Sql??""}.Concat(ValidScopedPaths(paths))
                .Where(part=>part.Length>0));
        }
        // 外层普通页含全局递归候选时，内层实体仍使用发布项目绑定的真实行范围。
        private bool scopedGlobalCrossTarget;
        private readonly HashSet<string> activeCrossScopePages=new(StringComparer.Ordinal);
        private readonly Dictionary<string,MySqlCrossTableProjection> scopedCrossProjections=new(StringComparer.Ordinal);

        internal IReadOnlyDictionary<string,NonRedundantSingleTargetScope> ProjectSingleTargets { get; init; }=
            new Dictionary<string,NonRedundantSingleTargetScope>(StringComparer.Ordinal);

        private readonly Dictionary<string,IReadOnlyDictionary<string,NonRedundantSingleTargetScope>> projectAliasTargets=
            new(StringComparer.Ordinal);

        internal PageMetadata? MultiNamePathPage { get; init; }
        private readonly Dictionary<string,PageMetadata> multiNamePathAliases=new(StringComparer.Ordinal);

        private PageMetadata? MultiNameSourcePage(string alias)=>
            alias==rootAlias?MultiNamePathPage:multiNamePathAliases.GetValueOrDefault(alias);

        /// <summary>按实际来源别名继续读取下一段范围。多值内层沿实际名称页逐段证明。</summary>
        private NonRedundantSingleTargetScope? ProjectSingleBinding(string ownerAlias,ColumnMetadata relation,TableMetadata target)
        {
            var explicitBinding=ExplicitSingleBinding(ownerAlias,relation);
            // 只更换显示列时没有新的目标页范围，沿既有项目联接处理。
            if(explicitBinding is {ApplyPageCondition:false,DisplayNameColumnId:not null,NestedTargets.Count:0})
                return null;
            var crossPage=CrossNamePage(ownerAlias);
            if(explicitBinding is not null||cascadeCacheOwner is not null||
                (MultiNameSourcePage(ownerAlias)??crossPage) is not {} sourcePage)
                return explicitBinding;
            var owner=tables[relation.ConfigTableId];
            if(crossPage is not null&&target.IsRongyu==1&&owner.IsRongyu==0&&GlobalCrossTableTarget.Declared(target))
            {
                var proof=GlobalSingleRelationTarget.Require(catalog,owner,relation,target,sourcePage);
                // 冗余字典的附加下拉条件仍按实际层位的来源行求值。
                if(proof.UsesListPageScope&&proof.CacheControl is {Terms.Count:>0} storedControl)
                {
                    var storedParent=proof.Parent??MultiTargetParentScope.Compile(catalog,target,storedControl);
                    var storedSource=storedParent?.SingleSource(catalog,owner,relation,sourcePage);
                    return new(relation.Id,proof.Page.Id,storedSource?.Column.Id)
                        {SourcePageId=sourcePage.Id,ControlSourcePageId=sourcePage.Id};
                }
            }
            if(multiLabel&&target.IsRongyu==1&&(crossPage is null||MultiNameSourcePage(ownerAlias) is not null)&&ProjectScopedSingleRelationTarget.NestedPage(
                catalog,owner,sourcePage,relation,target,allowStoredTarget:true) is {Scoped:true} stored)
                return new(relation.Id,stored.Page.Id){SourcePageId=sourcePage.Id};
            if(target.IsRongyu!=0||owner.IsRongyu!=0||
                GlobalCrossTableTarget.Declared(owner)&&!GlobalCrossTableTarget.Declared(target))
                return null;
            PageMetadata page;
            bool scoped;
            MultiTargetParentScope? parent;
            if(GlobalCrossTableTarget.Declared(target))
            {
                var proof=GlobalSingleRelationTarget.Require(catalog,owner,relation,target,sourcePage);
                page=proof.Page;
                // 多值名称的内层全局页即使只有物理常量，也须与缓存回填使用相同的可见性范围。
                scoped=proof.RequiresScopedPage||proof.CacheControl is {Terms.Count:>0}||
                    multiLabel&&!string.IsNullOrWhiteSpace(page.WhereSql);
                parent=proof.Parent;
            }
            else
            {
                if(ProjectScopedSingleRelationTarget.NestedPage(catalog,owner,sourcePage,relation,target) is not {} local)
                    return null;
                (page,scoped)=local;
                parent=scoped?MultiParent(catalog,page):null;
            }
            var control=scoped?ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,relation,target,sourcePage):null;
            parent??=control is null?null:MultiTargetParentScope.Compile(catalog,target,control);
            var source=parent?.SingleSource(catalog,owner,relation,sourcePage);
            return new(relation.Id,page.Id,source?.Column.Id)
            {
                ApplyPageCondition=scoped,
                SourcePageId=source is null?null:sourcePage.Id,
                ControlSourcePageId=control is {Terms.Count:>0}?sourcePage.Id:null
            };
        }

        /// <summary>全局单值名称使用生成计划中的实际来源页；同时存在的页级证明必须一致。</summary>
        private PageMetadata? GlobalSingleSourcePage(string alias,TableMetadata source,ColumnMetadata relation)
        {
            var actual=MultiNameSourcePage(alias)??(alias==rootAlias?candidatePage??SelectedMultiPage:null)??CrossNamePage(alias);
            if(relation.ControlKind is not("List" or "SelectPage")||ExplicitSingleBinding(alias,relation) is not {} binding||
                (binding.SourcePageId??binding.ControlSourcePageId) is not {} pageId)return actual;
            if(actual is not null&&actual.Id!=pageId||binding.ControlSourcePageId is {} controlPage&&controlPage!=pageId)
                throw new InvalidOperationException("全局单值名称的读取范围必须来自同一实际来源页。");
            return catalog.ConfigPages.Single(page=>page.Id==pageId&&page.ConfigTableId==source.Id);
        }

        internal bool LockPublishedJoinScopes{get;init;}
        private string PublishedJoinLock=>LockPublishedJoinScopes?NestedReadLockMarker:"";

        /// <summary>业务页的单值联接只显示其固定候选范围内的名称。目标条件在相关子查询内独立编译。</summary>
        private string ProjectSingleReadCondition(TableMetadata owner,string ownerAlias,ColumnMetadata relation,
            TableMetadata target,string alias,NonRedundantSingleTargetScope binding)
        {
            var global=GlobalCrossTableTarget.Declared(target);
            if(target.IsRongyu==1&&binding.SourcePageId is {} storedSourceId&&
                (!global||binding.ControlSourcePageId==storedSourceId||!binding.ApplyPageCondition))
            {
                var storedSource=catalog.ConfigPages.Single(page=>page.Id==storedSourceId&&page.ConfigTableId==owner.Id);
                var stored=global
                    ? GlobalSingleRelationTarget.Require(catalog,owner,relation,target,storedSource) is {UsesListPageScope:true} storedProof
                        ?storedProof.Page:null
                    : ProjectScopedSingleRelationTarget.NestedPage(catalog,owner,storedSource,relation,target,allowStoredTarget:true)?.Page;
                if(stored is null||stored.Id!=binding.PageId)
                    throw new InvalidOperationException("名称联接必须使用冗余字典的实际目标页。");
                if(!binding.ApplyPageCondition)return "";
                cascadeLabelPages.Add(storedSource.Id);
                cascadeLabelPages.Add(stored.Id);
                var storedControl=ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,relation,target,storedSource);
                return ProjectStoredListCondition(owner,ownerAlias,relation,target,alias,storedSource,stored,binding.SourceParentId,storedControl);
            }
            // 全局多值候选的编译入口也会传入名称路径。按来源页重新证明内层字典的物理条件。
            if(global&&target.IsRongyu==1&&binding is
                {SourcePageId:not null, SourceParentId:null, ControlSourcePageId:null, ApplyPageCondition:true, NestedTargets.Count:0})
            {
                var nameSource=catalog.ConfigPages.Single(page=>page.Id==binding.SourcePageId&&page.ConfigTableId==owner.Id);
                var stored=GlobalSingleRelationTarget.StoredMultiName(catalog,owner,nameSource,relation,target);
                if(stored is not {} fixedName||fixedName.Page.Id!=binding.PageId)
                    throw new InvalidOperationException("多值名称联接必须使用已发布的冗余字典页。");
                cascadeLabelPages.Add(nameSource.Id);
                cascadeLabelPages.Add(fixedName.Page.Id);
                return ProjectStoredListCondition(owner,ownerAlias,relation,target,alias,nameSource,fixedName.Page,
                    null,fixedName.Control);
            }
            if(owner.IsRongyu!=0||target.IsRongyu!=0||!global&&GlobalCrossTableTarget.Declared(owner)||
                relation.ControlKind is not("List" or "SelectPage")||!storage[owner.Id].IsPhysical(relation))
                throw new NotSupportedException("单值读取须使用非冗余表的实际物理外键。");
            var page=catalog.ConfigPages.Single(item=>item.Id==binding.PageId&&item.ConfigTableId==target.Id);
            var multiPage=MultiNameSourcePage(ownerAlias);
            if(global&&multiLabel&&binding.SourcePageId is {} nameSourceId)
            {
                multiPage??=catalog.ConfigPages.Single(item=>item.Id==nameSourceId&&item.ConfigTableId==owner.Id);
                if(multiPage.Id!=nameSourceId)
                    throw new InvalidOperationException("多值名称的全局目标范围与实际来源页不一致。");
            }
            var sourcePage=global?GlobalSingleSourcePage(ownerAlias,owner,relation)??multiPage:null;
            var proof=global?(sourcePage is null?GlobalSingleRelationTarget.Require(catalog,owner,relation,target):
                GlobalSingleRelationTarget.Require(catalog,owner,relation,target,sourcePage)):null;
            if(multiPage is not null)
            {
                multiNamePathAliases[alias]=page;
                cascadeLabelPages.Add(multiPage.Id);
                cascadeLabelPages.Add(page.Id);
            }
            if(proof is not null&&proof.Page.Id!=page.Id)
                throw new InvalidOperationException("全局单值读取与发布目标页不一致。");
            // 项目内跨表名称的后续联接也须保留当前字典页，空范围的中间段不能丢失来源。
            if(CrossNamePage(ownerAlias) is not null)crossNameSourcePages[alias]=page;
            if(binding.NestedTargets.Count>0)
                projectAliasTargets.Add(alias,binding.NestedTargets.ToDictionary(item=>item.ColumnId,StringComparer.Ordinal));
            if(!binding.ApplyPageCondition)return "";
            if(global)scopedGlobalCrossTarget=true;

            var controlPage=binding.ControlSourcePageId is null?null:catalog.ConfigPages.Single(
                item=>item.Id==binding.ControlSourcePageId&&item.ConfigTableId==owner.Id);
            var control=controlPage is null?null:ProjectScopedSingleRelationTarget.ControlScope(catalog,owner,relation,target,controlPage);
            var projectParent=global?null:ProjectScopedSingleRelationTarget.PageParent(catalog,target,page);
            var pageParent=global?proof!.Parent:projectParent??MultiParent(catalog,page);
            var parent=SingleParentWithControl(target,relation,page,pageParent,control);
            if((parent is null)!=(binding.SourceParentId is null)||
                !global&&parent is {Global:true,Logical:false}&&projectParent is null)
                throw new InvalidOperationException("项目内单值读取缺少已证明的父绑定。");
            var publishedSource=parent is not null&&binding.SourcePageId is {} pageId
                ?parent.SingleSource(catalog,owner,relation,catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==owner.Id))
                :null;
            if(publishedSource is not null&&publishedSource.Column.Id!=binding.SourceParentId)
                throw new InvalidOperationException("项目内单值读取父键与实际来源页不一致。");
            if(parent is {Global:true}&&publishedSource is {AdditionalColumns.Count:>0})
                RequireCompoundGlobalSourceParent(catalog,target.Id,page.Id,publishedSource);

            var prefix=ConditionParameterPrefix("mt_"+aliasPrefix+alias+"_page");
            var plan=CompileCrossPageCandidate(catalog,target.Id,page.Id,["id"],[],rootAlias:prefix,aliasPrefix:prefix+"_",
                activeSelfTreeScopes:ActiveSelfTreeScopes,listFields:control?.Fields.ToArray(),projectSingle:true,
                projectSingleControlKind:relation.ControlKind);
            plan.CopyParametersTo(crossConstants);
            foreach(var reference in plan.PhysicalReferences)
                references.TryAdd((reference.Table,reference.Column.Name),reference);
            var predicate=plan.Expression("id")+"="+alias+".`id`";
            if(plan.Scope.Length>0)predicate+=" AND "+plan.Scope;
            if(control is {Terms.Count:>0})
            {
                var extra=ProjectSingleControlCondition(owner,ownerAlias,target,control,plan,prefix);
                if(extra.Length>0)predicate+=" AND "+extra;
            }
            if(parent is {Logical:true})
            {
                // 目标由实际可见路径还原父级，来源则按同一父记录的持久键或已发布逻辑路径匹配。
                var sourceParent=publishedSource??parent.SingleSource(catalog,owner,relation);
                if(sourceParent.Column.Id!=binding.SourceParentId)
                    throw new InvalidOperationException("项目内单值读取父键与实际级联来源不一致。");
                var sourceValue=SingleSourceParentValue(owner,ownerAlias,sourceParent);
                predicate+=" AND "+SameSingleParent(plan.Expression(parent.Target.ColumnName),sourceValue);
            }
            else if(parent is not null&&publishedSource is ({LogicalPages.Count:>0} or {AdditionalColumns.Count:>0}))
            {
                var sourceValue=SingleSourceParentValue(owner,ownerAlias,publishedSource);
                predicate+=" AND "+SinglePhysicalParentCondition(owner,ownerAlias,prefix,parent,publishedSource.Column,sourceParent:sourceValue);
            }
            else if(parent is not null)
            {
                var field=columns[owner.Id].Values.Single(item=>item.Id==binding.SourceParentId);
                if(field.Id==relation.Id||field.LogicalType is not("string" or "int64")||
                    !GlobalPhysicalRelationValue.IsScalar(storage[owner.Id],owner,field)||
                    (field.LinkConfigTableId.Length>0?field.LinkConfigTableId!=parent.Parent.Id:
                        field.ColumnName!=parent.Parent.TableName+"_id"))
                    throw new InvalidOperationException("项目内单值读取父键与目标发布页不一致。");
                predicate+=" AND "+SinglePhysicalParentCondition(owner,ownerAlias,prefix,parent,field);
            }
            // 目标页子计划只包含本页的范围，来自控件的物理父字段也必须与同一实际父键相等。
            if(parent is {Logical:false,AdditionalTargets.Count:>0}&&!global&&control is {Terms.Count:>0}&&
                (pageParent?.Target.Id!=parent.Target.Id||parent.AdditionalTargets.Any(item=>pageParent?.HasTarget(item.Target.Id)!=true)))
                foreach(var scope in parent.AdditionalTargets)
                    predicate+=" AND "+SameSingleParent(prefix+"."+Q(scope.Target.ColumnName),prefix+"."+Q(parent.Target.ColumnName));
            // 根记录锁不会传递到目标页的条件子查询，附件在事务里也要核对当前目标范围。
            return "EXISTS (SELECT 1"+plan.From+" WHERE "+predicate+PublishedJoinLock+")";
        }

        /// <summary>关联目标先按物理键联接。用固定候选子查询隔离跨表层，避免引用后声明的别名。</summary>
        private string PublishedSingleCrossCondition(TableMetadata table,string alias,PageMetadata page,string? sourceParent=null)
        {
            var prefix=ConditionParameterPrefix("mt_"+aliasPrefix+alias+"_single");
            var plan=CompileCrossPageCandidate(catalog,table.Id,page.Id,["id"],[],rootAlias:prefix,aliasPrefix:prefix+"_",
                activeSelfTreeScopes:ActiveSelfTreeScopes,globalExactSingle:GlobalCrossTableTarget.DeclaresExactSingle(table,page));
            plan.CopyParametersTo(crossConstants);
            foreach(var reference in plan.PhysicalReferences)
                references.TryAdd((reference.Table,reference.Column.Name),reference);
            scopedGlobalCrossTarget=true;
            var parentScope=sourceParent is null?"":" AND "+SameSingleParent(
                plan.Expression(plan.RequiredMultiParent??throw new InvalidOperationException("固定单值目标缺少逻辑父表达式。")),sourceParent);
            return "EXISTS (SELECT 1"+plan.From+" WHERE "+plan.Expression("id")+"="+alias+".`id` AND "+plan.Scope+parentScope+")";
        }

        /// <summary>单值副本按来源行归属重算，逻辑条件沿目标页的实际级联路径读取。</summary>
        private string PublishedSingleCondition(TableMetadata table,string alias,PageMetadata page,CascadeCacheOwner? cacheOwner=null,
            ColumnMetadata? parentField=null,string? sourceParent=null,bool allowFixedOnly=false,
            IReadOnlyList<MultiTargetParentScope>? additionalScopes=null)
        {
            var members=columns[table.Id].Values.ToArray();
            var fields=PublishedSelfTreeConditionFields(catalog,table,page,members);
            var syntax=FixedConditionPlan.Compile(page.WhereSql,members.Select(member=>member.ColumnName));
            var additional=additionalScopes is {Count:>0}
                ?additionalScopes.Select(scope=>scope.Target.ColumnName).ToHashSet(StringComparer.Ordinal):null;
            // 读取和缓存都按同一个实际父页计算，全局父的各外键或逻辑层位不得各自选择发布页。
            if(additional is not null&&additionalScopes![0].Global)
                _=PublishedMultiGlobalParent(catalog,table.Id,page.Id);
            var condition=CascadeScopeCondition(table,storage[table.Id],members,syntax,parentField,fields,additional);
            var logical=RequireCrossCondition(table,page,condition,parentField,fields,additional);
            if(GlobalCrossTableTarget.Declared(table))scopedGlobalCrossTarget=true;
            // 已保存副本沿来源行归属重算。候选及写入使用当前身份的实际精确匹配值。
            var globalExact=GlobalCrossTableTarget.DeclaresExactSingle(table,page);
            if(globalExact)GlobalCrossTableTarget.RequireExactSingleCandidate(catalog,table,page);
            var storedExact=cacheOwner is not null&&(globalExact||PublishedSingleExactColumn(catalog,table,page,members) is not null);
            if(cacheOwner is {} row&&logical.Count==0)
            {
                var visible=PhysicalAccountCacheCondition(table,alias,page,row,parentField,
                    allowFixedOnly:allowFixedOnly||!GlobalCrossTableTarget.Declared(table),separateExactScope:storedExact,
                    additionalParents:additional);
                if(additional is null)return visible;
                var canonical=alias+"."+Q(parentField!.ColumnName);
                foreach(var scope in additionalScopes!)
                {
                    Use(table,scope.Target);Touch(alias,scope.Target.ColumnName);
                    visible+=(visible.Length==0?"":" AND ")+SameSingleParent(alias+"."+Q(scope.Target.ColumnName),canonical);
                }
                return visible;
            }
            return NestedCrossCondition(new(table,Column(table,"id"),null,new(page.Id,condition),page,logical),
                alias,cacheOwner,parentField,fields,sourceParent,storedSingleExact:storedExact,
                additionalParents:additional,compareAdditionalParents:true);
        }

        /// <summary>物理账号条件按来源行归属计算，并记录源字段和目标筛选列的反向依赖。</summary>
        private string PhysicalAccountCacheCondition(TableMetadata table,string alias,PageMetadata page,
            CascadeCacheOwner owner,ColumnMetadata? parentField=null,bool allowFixedOnly=false,bool separateExactScope=false,
            IReadOnlySet<string>? additionalParents=null)
        {
            var ownership=AccountScopedCrossCache.Compile(catalog,owner.Table,table,page,owner.GlobalTreeSource,
                parentField,owner.GlobalSingleSource,allowFixedOnly,separateExactScope,additionalParents);
            cascadeCacheOwnerFields.UnionWith(ownership.Ownership.Select(binding=>binding.Source.ColumnName));
            // 多份名称可用同一目标别名，常量参数必须沿各自投影前缀隔离。
            var parameterPrefix=multiLabel?"mt_cross_cache_"+aliasPrefix+alias:null;
            return AccountCacheScope(ownership,table,alias,owner.Alias,render:true,owner.Table,parameterPrefix);
        }

        /// <summary>先按物理键联接以便失效后仍可反查，再按来源行归属计算可见名称。</summary>
        private (string Sql,string Type) ScopedSingleCacheValue(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,ColumnMetadata value,RelationCacheDefinition definition)
        {
            var cacheOwner=new CascadeCacheOwner(table,owner,GlobalSingleSource:GlobalCrossTableTarget.Declared(table));
            var source=Resolve(table,owner,foreign.ColumnName);
            var joined=JoinRegisteredSingleCache(table,owner,foreign,target,source,definition,cacheOwner);
            var label=Resolve(target,joined.Alias,value.ColumnName,cacheOwner);
            return ("CASE WHEN "+joined.Visibility+" THEN "+label.Sql+" END",label.Type);
        }

        /// <summary>实际联接别名与关系角色共同决定页级绑定，不能从同表的其他角色借用。</summary>
        private NonRedundantSingleTargetScope? ExplicitSingleBinding(string owner,ColumnMetadata relation)=>
            (owner==rootAlias?ProjectSingleTargets:projectAliasTargets.GetValueOrDefault(owner))?.GetValueOrDefault(relation.Id);

        /// <summary>稳定的辅助字段名沿表级定义，显式名称只在能证明同一联接时采用页级绑定。</summary>
        private (ColumnMetadata Foreign,string Display)? InferredSingleDisplay(TableMetadata table,string name,string? owner=null,ColumnMetadata? declared=null)
        {
            if(declared is not null&&(owner is null||storage[table.Id].Kind(declared)!=MetadataFieldStorage.Join))
                return null;
            foreach(var foreign in columns[table.Id].Values.Where(MetadataStoragePolicy.IsRelationColumn))
            {
                if(!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal))continue;
                var display=Value(Definition(foreign),"nameField");
                if(display.Length==0)display="name";
                if(display=="id"||!name.Equals(foreign.ColumnName[..^3]+"_"+display,StringComparison.OrdinalIgnoreCase))
                    continue;
                var binding=owner is null?null:ExplicitSingleBinding(owner,foreign);
                if(declared is not null&&(binding?.DisplayNameColumnId is null||
                    !MetadataStoragePolicy.IsTextSingleDisplayJoin(foreign,declared,display)))
                    continue;
                if(binding is {DisplayNameColumnId:{} id})
                {
                    var target=Target(foreign);
                    var column=columns[target.Id].Values.SingleOrDefault(item=>item.Id==id);
                    if(foreign.ControlKind is not ("List" or "SelectPage")||GlobalCrossTableTarget.Declared(table)||GlobalCrossTableTarget.Declared(target)||
                        column is null||column.LogicalType!="string"||!storage[target.Id].IsPhysical(column)||
                        Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(column.ColumnName)||
                        !catalog.ConfigPages.Any(page=>page.Id==binding.PageId&&page.ConfigTableId==target.Id))
                        throw new InvalidOperationException("页级关系名称必须属于同一项目内目标的物理文本列。");
                    display=column.ColumnName;
                }
                return(foreign,display);
            }
            return null;
        }

        /// <summary>全局派生标量使用该联接角色的实际来源页；无页面时沿用唯一发布范围。</summary>
        private bool PublishedGlobalValue(ColumnMetadata column)
        {
            var rootStorage=storage[root.Id];
            // 物理文件 ID 只随已隔离的来源记录读取，附件下载及变更继续单独校验真实引用。
            if(IsGlobalOwnedBusinessRead()&&column is {ControlKind:"FileLibrary",LogicalType:"string",IsVirtualColumn:0}&&
                rootStorage.IsPhysical(column)&&!ExactDataManageValues.IsSensitiveField(column.ColumnName))
                return true;
            if(rootStorage.Kind(column)!=MetadataFieldStorage.Join)
                return GlobalPhysicalRelationValue.IsPublishedScalar(catalog,rootStorage,root,column);
            var segment=Text(column,"columnFullName").Split('.',2)[0];
            var relation=columns[root.Id].GetValueOrDefault(segment+"_id")??columns[root.Id].GetValueOrDefault(segment);
            var sourcePage=relation is null?null:GlobalSingleSourcePage(rootAlias,root,relation);
            return GlobalPhysicalRelationValue.IsPublishedScalar(catalog,rootStorage,root,column,sourcePage);
        }

        /// <summary>全局自动名称使用原发布证明，缺失的任意字段、跨目录关系或凭据不能借显示名读取。</summary>
        private void RequireGlobalSingleDisplay(TableMetadata table,string field)
        {
            if(InferredSingleDisplay(table,field) is not {} inferred)
                throw new InvalidOperationException("缺少字段："+field);
            var target=Target(inferred.Foreign);
            var sourcePage=GlobalSingleSourcePage(rootAlias,table,inferred.Foreign);
            var proof=sourcePage is null?GlobalSingleRelationTarget.Require(catalog,table,inferred.Foreign,target):
                GlobalSingleRelationTarget.Require(catalog,table,inferred.Foreign,target,sourcePage);
            if(proof.Name.ColumnName!=inferred.Display)
                throw new NotSupportedException("全局R0的自动名称必须使用已发布的单值显示字段。");
        }

        private readonly Dictionary<string,PageMetadata> singleCachePages=new(StringComparer.Ordinal);
        private readonly Dictionary<string,string?> globalSingleVisibility=new(StringComparer.Ordinal);

        /// <summary>同一物理外键的联接字段沿用已登记范围和来源行身份，未登记的项目范围仍由普通联接门禁拒绝。</summary>
        private (string Alias,string? Visibility,CascadeCacheOwner? CacheOwner) JoinSinglePath(
            TableMetadata table,string owner,ColumnMetadata foreign,TableMetadata target,CascadeCacheOwner? cacheOwner)
        {
            var source=Resolve(table,owner,foreign.ColumnName);
            if(refreshFields is not null&&table.Id==root.Id&&owner==rootAlias&&
                refreshMultiples.FirstOrDefault(binding=>!binding.Multiple&&binding.ScopedSinglePageId is not null&&
                    binding.ForeignColumnId==foreign.Id&&binding.TargetTableId==target.Id&&binding.SourceTableId==table.Id) is {} registered)
            {
                var row=new CascadeCacheOwner(table,owner,GlobalSingleSource:GlobalCrossTableTarget.Declared(table));
                var scoped=JoinRegisteredSingleCache(table,owner,foreign,target,source,registered,row);
                return(scoped.Alias,scoped.Visibility,row);
            }
            if(cacheOwner is not null&&owner==rootAlias&&(MultiNamePathPage??CrossNameSourcePage) is {} namePage)
                singleCachePages.TryAdd(owner,namePage);
            if(multiLabel&&JoinGlobalStoredName(table,owner,foreign,target,source,cacheOwner) is {} globalStored)
                return(globalStored.Alias,globalStored.Visibility,cacheOwner);
            if(cacheOwner is not null&&GlobalCrossTableTarget.Declared(target)&&
                JoinNestedSingleCache(table,owner,foreign,target,source,cacheOwner) is {} globalNested)
                return(globalNested.Alias,globalNested.Visibility,cacheOwner);
            if(cacheOwner is not null&&singleCachePages.TryGetValue(owner,out var sourcePage)&&
                ProjectScopedSingleRelationTarget.NestedPage(catalog,table,sourcePage,foreign,target,
                    allowStoredTarget:multiLabel&&(CrossNamePage(owner) is null||MultiNameSourcePage(owner) is not null)) is {} projectNested)
            {
                if(projectNested.Scoped&&target.IsRongyu==1)
                {
                    // 允许追溯变为不可见的字典记录，条件只控制名称是否写回多值副本。
                    var storedAlias=Join(owner,foreign,target,source.Sql,source.Type,ordinaryRelation:true);
                    var control=ProjectScopedSingleRelationTarget.ControlScope(catalog,table,foreign,target,sourcePage);
                    var visible=ProjectStoredListCondition(table,owner,foreign,target,storedAlias,sourcePage,projectNested.Page,
                        null,control,cacheOwner);
                    return(storedAlias,visible,cacheOwner);
                }
                if(projectNested.Scoped)
                {
                    var control=ProjectScopedSingleRelationTarget.ControlScope(catalog,table,foreign,target,sourcePage);
                    var scoped=JoinSingleCacheWithControl(table,owner,foreign,target,source,projectNested.Page,cacheOwner,control,sourcePage);
                    return(scoped.Alias,scoped.Visibility,cacheOwner);
                }
                // 空条件仍须把中间页传给下一段，并保留实际外键的反查位置。
                var alias=Join(owner,foreign,target,source.Sql,source.Type,ordinaryRelation:true);
                singleCachePages[alias]=projectNested.Page;
                return(alias,null,cacheOwner);
            }
            var joined=Join(owner,foreign,target,source.Sql,source.Type,ordinaryRelation:true);
            if(refreshFields is null&&GlobalCrossTableTarget.Declared(target)&&ProjectSingleBinding(owner,foreign,target) is {} binding)
            {
                // 全局联接继续使用原发布范围。附加条件随当前别名的输出一起计算，零匹配显示为空。
                if(!globalSingleVisibility.TryGetValue(joined,out var visibility))
                {
                    var condition=ProjectSingleReadCondition(table,owner,foreign,target,joined,binding);
                    visibility=condition.Length==0?null:condition;
                    globalSingleVisibility.Add(joined,visibility);
                }
                return(joined,visibility,cacheOwner);
            }
            return(joined,null,cacheOwner);
        }

        /// <summary>全局字典先保留物理关系，固定条件再决定多值名称的可见性及维护依赖。</summary>
        private (string Alias,string Visibility)? JoinGlobalStoredName(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,CascadeCacheOwner? cacheOwner)
        {
            if(target.IsRongyu!=1||!GlobalCrossTableTarget.Declared(target))return null;
            var binding=ExplicitSingleBinding(owner,foreign);
            var sourcePage=singleCachePages.GetValueOrDefault(owner)??MultiNameSourcePage(owner);
            if(binding?.SourcePageId is {} sourcePageId)
            {
                sourcePage??=catalog.ConfigPages.Single(page=>page.Id==sourcePageId&&page.ConfigTableId==table.Id);
                if(sourcePage.Id!=sourcePageId)
                    throw new InvalidOperationException("全局字典的固定范围与实际来源页不一致。");
            }
            if(sourcePage is null)return null;
            var fixedScope=GlobalSingleRelationTarget.StoredMultiName(catalog,table,sourcePage,foreign,target);
            if(binding?.SourcePageId is not null&&fixedScope?.Page.Id!=binding.PageId)
                throw new InvalidOperationException("全局字典的固定范围与实际目标页不一致。");
            if(fixedScope is not {} published)return null;
            cascadeLabelPages.Add(sourcePage.Id);
            cascadeLabelPages.Add(published.Page.Id);
            var alias=Join(owner,foreign,target,source.Sql,source.Type,ordinaryRelation:true,validatedCrossGlobal:true,
                scopedCacheLink:cacheOwner is not null);
            var visibility=ProjectStoredListCondition(table,owner,foreign,target,alias,sourcePage,published.Page,
                null,published.Control,cacheOwner);
            return(alias,visibility);
        }

        /// <summary>名称副本和同外键的其他联接字段共用实际发布页，逐项保留各自的反向依赖。</summary>
        private (string Alias,string Visibility) JoinRegisteredSingleCache(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,RelationCacheDefinition definition,CascadeCacheOwner cacheOwner)
        {
            var proof=DerivedCrossTableCaches.ScopedSingleTarget(catalog,table,foreign,target);
            if(proof.Page.Id!=definition.ScopedSinglePageId||proof.Name.Id!=definition.ValueColumnId)
                throw new InvalidOperationException("单值名称缓存的目标范围与发布证明不一致。");
            if(proof.Control is {} control&&(!GlobalCrossTableTarget.Declared(target)||control.Terms.Count>0))
                return JoinSingleCacheWithControl(table,owner,foreign,target,source,proof.Page,cacheOwner,control);
            var parent=ProjectScopedSingleRelationTarget.PageParent(catalog,target,proof.Page)??MultiParent(catalog,proof.Page);
            var binding=parent?.SingleSource(catalog,table,foreign);
            return JoinScopedSingleCache(table,owner,foreign,target,source,proof.Page,cacheOwner,parent,binding);
        }

        /// <summary>先保留真实外键反查，再按最终缓存来源行核对内层账号、父键和级联范围。</summary>
        private (string Alias,string? Visibility)? JoinNestedSingleCache(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,CascadeCacheOwner cacheOwner)
        {
            var sourcePage=singleCachePages.GetValueOrDefault(owner);
            var proof=table.IsRongyu==1?GlobalSingleRelationTarget.RequireCached(catalog,table,foreign,target)
                :sourcePage is null?GlobalSingleRelationTarget.Require(catalog,table,foreign,target)
                    :GlobalSingleRelationTarget.Require(catalog,table,foreign,target,sourcePage);
            var control=table.IsRongyu==1&&target.IsRongyu==0&&sourcePage is not null
                ?ProjectScopedSingleRelationTarget.ControlScope(catalog,table,foreign,target,sourcePage):proof.CacheControl;
            if(control is {Terms.Count:>0})
                return JoinSingleCacheWithControl(table,owner,foreign,target,source,proof.Page,cacheOwner,control,sourcePage);
            if(proof.RequiresScopedPage||sourcePage is not null)
                return JoinScopedSingleCache(table,owner,foreign,target,source,proof.Page,cacheOwner,proof.Parent,proof.SourceParent);
            return null;
        }

        /// <summary>先登记物理关联用于反查，命名和其他投影的可见性随后按已发布范围计算。</summary>
        private (string Alias,string Visibility) JoinScopedSingleCache(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,PageMetadata page,CascadeCacheOwner cacheOwner,
            MultiTargetParentScope? parent,MultiSourceParent? binding,FixedConditionPlan? control=null)
        {
            if(parent is {Global:true}&&binding is {AdditionalColumns.Count:>0})
                RequireCompoundGlobalSourceParent(catalog,target.Id,page.Id,binding);
            var sourceParent=binding is not null&&(parent?.Logical==true||binding.LogicalPages.Count>0||binding.AdditionalColumns.Count>0)
                ?SingleSourceParentValue(table,owner,binding,cacheOwner):null;
            // 反查需要从最终写回的根表出发，保存中间记录及逻辑父表达式引用的祖先别名。
            var sourceFrom=owner==rootAlias&&sourceParent is null?null:" FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins);
            var alias=Join(owner,foreign,target,source.Sql,source.Type,validatedCrossGlobal:true,scopedCacheLink:true);
            singleCachePages[alias]=page;
            var visible=PublishedSingleCondition(target,alias,page,cacheOwner,parent?.Target,parent?.Logical==true?sourceParent:null,
                allowFixedOnly:true,additionalScopes:parent?.AdditionalTargets);
            if(parent is {Logical:false})
                visible+=(visible.Length==0?"":" AND ")+SinglePhysicalParentCondition(table,owner,alias,parent,binding!.Column,
                    cacheOwner,sourceParent,sourceFrom);
            if(control is {Terms.Count:>0})
            {
                // 控件谓词留在可见性中以保留失效反查，直接目标路径不能简化该计划。
                scalarCacheTarget=null;
                var extra=SingleControlCacheCondition(table,owner,target,alias,page,control,cacheOwner);
                if(extra.Length>0)visible+=(visible.Length==0?"":" AND ")+extra;
            }
            return (alias,alias+".`id` IS NOT NULL"+(visible.Length==0?"":" AND ("+visible+")"));
        }

        /// <summary>来源父键来自发布页的可见级联或持久字段，保持高位 ID 和字符串精度。</summary>
        private string SingleSourceParentValue(TableMetadata source,string owner,MultiSourceParent parent,CascadeCacheOwner? cacheOwner=null)
        {
            if(parent.LogicalPages.Count>0)return LogicalRelationSourceParent(source,owner,parent,cacheOwner);
            string Key(ColumnMetadata column)
            {
                Use(source,column);
                Touch(owner,column.ColumnName);
                if(trackReverse)cascadeCacheOwnerFields.Add(column.ColumnName);
                return owner+"."+Q(column.ColumnName);
            }
            var value=Key(parent.Column);
            if(parent.AdditionalColumns.Count==0)return value;
            var conditions=parent.AdditionalColumns.Select(column=>SameSingleParent(Key(column),value));
            return "CASE WHEN "+string.Join(" AND ",conditions)+" THEN "+value+" END";
        }

        private static string SameSingleParent(string target,string source)=>
            "BINARY CAST("+target+" AS CHAR)=BINARY CAST("+source+" AS CHAR)";

        /// <summary>项目内和全局父均要求来源和目标实际父键相同；全局父再执行唯一发布页范围。</summary>
        private string SinglePhysicalParentCondition(TableMetadata source,string owner,string targetAlias,
            MultiTargetParentScope parent,ColumnMetadata binding,CascadeCacheOwner? cacheOwner=null,string? sourceParent=null,string? sourceFrom=null)
        {
            var parentAlias=aliasPrefix+"single_parent_"+targetAlias;
            var sourceKey=sourceParent??owner+"."+Q(binding.ColumnName);
            var targetKey=targetAlias+"."+Q(parent.Target.ColumnName);
            var primary=parentAlias+".`id`";
            if(sourceParent is null)
            {
                Use(source,binding);
                Touch(owner,binding.ColumnName);
            }
            Use(tables[parent.Target.ConfigTableId],parent.Target);Touch(targetAlias,parent.Target.ColumnName);
            Use(parent.Parent,Column(parent.Parent,"id"));
            var canonical="BINARY CAST("+primary+" AS CHAR)";
            var sameSource=canonical+"=BINARY CAST("+sourceKey+" AS CHAR)";
            var sameTarget=canonical+"=BINARY CAST("+targetKey+" AS CHAR)";
            var ownerFrom=sourceFrom??" FROM "+Q(source.TableName)+" "+owner;
            var parentFrom=" FROM "+Q(parent.Parent.TableName)+" "+parentAlias;
            string parentScope;
            IReadOnlySet<string> dependencies;
            if(parent.Global)
            {
                var page=PublishedPhysicalMultiParent(catalog,parent);
                var plan=BindPhysicalGlobalParentPage(parent.Parent,page,parentAlias,cacheOwner);
                parentFrom=plan.From;
                parentScope=plan.Scope;
                dependencies=plan.PhysicalReferences.Where(reference=>reference.Table==parent.Parent.TableName)
                    .Select(reference=>reference.Column.Name).ToHashSet(StringComparer.Ordinal);
                TrackGlobalParentDependencies(plan,ownerFrom,sameSource);
            }
            else
            {
                parentScope=Scope(parent.Parent,parentAlias);
                dependencies=new HashSet<string>(StringComparer.Ordinal){"id","yz_project_id","delete_no"};
            }
            if(trackReverse)
                customReverse.Add(new(parent.Parent.TableName,ownerFrom,sourceKey,binding.LogicalType,
                    new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,dependencies}}));
            return "EXISTS (SELECT 1"+parentFrom+" WHERE "+primary+">0 AND "+sameSource+" AND "+sameTarget+
                (parentScope.Length==0?"":" AND "+parentScope)+PublishedJoinLock+")";
        }

        /// <summary>缓存通过已发布候选页求值，目标与内层依赖都进入原传播队列。</summary>
        private (string Sql,string Type) RecursiveCacheValue(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,ColumnMetadata value,RelationCacheDefinition definition)
        {
            var (page,condition,treeNames)=CrossTableCacheProof.RequireRecursivePage(catalog,target,value,definition);
            var logical=RequireCrossCondition(target,page,condition,selfTreeFields:treeNames);
            var sourceId=Resolve(table,owner,foreign.ColumnName);
            var alias=Join(owner,foreign,target,sourceId.Sql,sourceId.Type,
                validatedCrossGlobal:GlobalCrossTableTarget.Declared(target));
            var level=new CrossLevel(target,value,null,new(page.Id,condition),page,logical);
            var row=new CascadeCacheOwner(table,owner);
            var visible=NestedCrossCondition(level,alias,row,selfTreeFields:treeNames);
            CascadeCacheOwner? nameOwner=null;
            if(storage[target.Id].Kind(value)==MetadataFieldStorage.Join)
            {
                // 递归条件与派生名称共用实际发布页。保留物理反查，使内层字典变化能触发回填。
                singleCachePages[alias]=page;
                nameOwner=row;
            }
            var name=Resolve(target,alias,value.ColumnName,nameOwner);
            return ("CASE WHEN "+alias+".`id` IS NOT NULL AND "+visible+" THEN "+MySqlMultiValueLabel.Text(name.Sql,name.Type)+" END","string");
        }

        /// <summary>同一条查询展开所需祖先，写入沿同一事务锁定全部联接。候选内部的存储校验不进入外层 VO。</summary>
        private string NestedCrossCondition(CrossLevel level,string owner,CascadeCacheOwner? cacheOwner=null,ColumnMetadata? multiParent=null,
            IReadOnlySet<string>? selfTreeFields=null,string? sourceParent=null,bool storedSingleExact=false,
            IReadOnlySet<string>? additionalParents=null,bool compareAdditionalParents=false)
        {
            var table=level.Table;
            cacheOwner??=cascadeCacheOwner;
            if(table.IsRongyu!=0||cacheOwner is null&&trackReverse)
                throw new NotSupportedException("递归候选要求普通非冗余目标；缓存维护需独立证明实际来源行的范围。");
            if(!activeCrossScopePages.Add(level.Page.Id))
                throw new InvalidOperationException("跨表级联候选页条件存在循环依赖。");
            try
            {
                var crossFields=selfTreeFields is {Count:>0}?level.LogicalFields.Except(selfTreeFields).ToHashSet(StringComparer.Ordinal):level.LogicalFields;
                var pathsForScope=crossFields.Count==0?[]:CrossMembershipBindings(catalog,table,level.Page,crossFields);
                var bindings=pathsForScope.ToDictionary(path=>path.SourceColumnId,StringComparer.Ordinal);
                // 精简运行目录必须保留实际使用的内层候选页条件及控件定义。
                if(multiLabel)
                {
                    cascadeLabelPages.Add(level.Page.Id);
                    cascadeLabelPages.UnionWith(bindings.Values.SelectMany(binding=>binding.Levels.Select(target=>target.PageId)));
                }
                var parameterPrefix=ConditionParameterPrefix("mt_nested_"+(multiLabel?aliasPrefix:"")+owner);
                string Projection(string field)
                {
                    var column=Column(table,field);
                    if(!level.LogicalFields.Contains(field))
                    {
                        Use(table,column);
                        Touch(owner,field);
                        return owner+"."+Q(field);
                    }

                    if(selfTreeFields?.Contains(field)==true)
                    {
                        var control=Column(table,field[..^5]+"_id");
                        if(!IsPhysicalTreeKey(table,storage[table.Id],control)||
                            GlobalCrossTableTarget.Declared(table)&&
                                !GlobalCrossTableTarget.Declared(SelfTreeLabel(catalog,control).Target))
                            throw new NotSupportedException("自树固定条件缺少有效的叶子外键或全局发布证明。");
                        return SelfTreeLabelExpression(table,owner,control,column,conditionOwner:cacheOwner);
                    }

                    return ScopedCrossField(table,owner,bindings,field,cacheOwner);
                }
                // 同一可见路径既负责页面条件，也负责恢复可能没有实际落库的逻辑父键。
                if(sourceParent is null&&multiParent is not null&&level.LogicalFields.Contains(multiParent.ColumnName))
                    selectedMultiParentSql=Projection(multiParent.ColumnName);
                if(additionalParents is not null&&!compareAdditionalParents)
                    foreach(var field in additionalParents.Where(level.LogicalFields.Contains))
                        additionalMultiParentSql[field]=Projection(field);
                string predicate;
                if(cacheOwner is {} row)
                {
                    var proof=AccountScopedCrossCache.CompileRecursive(catalog,row.Table,table,level.Page,multiParent,
                        row.GlobalTreeSource,selfTreeFields,row.GlobalSingleSource,storedSingleExact||UsesExactMultiSelection(level.Page,owner),
                        additionalParents:additionalParents);
                    cascadeCacheOwnerFields.UnionWith(proof.Ownership.Select(binding=>binding.Source.ColumnName));
                    predicate=AccountCacheScope(proof,table,owner,row.Alias,render:true,row.Table,parameterPrefix,Projection);
                }
                else if(level.Scope.Condition is {} condition)
                {
                    var clause=condition.RenderAccountProjection(new MySqlDatabaseProvider(),parameterPrefix,Projection);
                    foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
                    predicate=clause.Sql;
                }
                else predicate="1=1";
                if(sourceParent is not null)
                {
                    if(multiParent is null||!level.LogicalFields.Contains(multiParent.ColumnName))
                        throw new InvalidOperationException("全局单值缓存缺少已发布逻辑父层位。");
                    predicate+=(predicate.Length==0?"":" AND ")+SameSingleParent(Projection(multiParent.ColumnName),sourceParent);
                }

                if(compareAdditionalParents&&additionalParents is not null)
                {
                    var canonical=Projection(multiParent!.ColumnName);
                    foreach(var field in additionalParents)
                        predicate+=(predicate.Length==0?"":" AND ")+SameSingleParent(Projection(field),canonical);
                }

                // 空选择允许原 IS NULL；已存但不可见或无法恢复的节点不能冒充空值放宽候选。
                var paths=ScopedCrossPaths(owner,bindings.Keys);
                return "("+string.Join(" AND ",new[]{predicate}.Concat(ValidScopedPaths(paths))
                    .Where(part=>part.Length>0))+")";
            }
            finally
            {
                activeCrossScopePages.Remove(level.Page.Id);
            }
        }

        /// <summary>内层路径只进入本次范围表达式。路径无效时由调用方隐藏名称或拒绝候选。</summary>
        private string ScopedCrossField(TableMetadata table,string owner,
            IReadOnlyDictionary<string,NonRedundantCrossTableScope> bindings,string field,CascadeCacheOwner? cacheOwner=null)
        {
            var controls=columns[table.Id].Values;
            var control=controls.First(item=>bindings.ContainsKey(item.Id)&&CrossTableStorageColumns.ContainsPathField(item,field));
            var group=SharedCrossGroup(table,control.Id);
            if(group is not null)
                return SharedCrossValue(table,owner,group,field,scopedBindings:bindings,cacheOwner:cacheOwner).Sql;
            var selection=CrossTableSparseSelection.Compile(table,control.Id,controls);
            return CrossValue(table,owner,control,selection,bindings[control.Id],field,scopeOnly:true,cacheOwner:cacheOwner).Sql;
        }

        /// <summary>同一别名已投影过路径时沿用该投影，内层存储校验不进入根记录。</summary>
        private IEnumerable<MySqlCrossTableProjection> ScopedCrossPaths(string owner,IEnumerable<string> sources)=>
            sources.Select(source=>
            {
                var key=owner+":"+source;
                return scopedCrossProjections.GetValueOrDefault(key)??crossProjections[key];
            });

        /// <summary>已存的路径必须实际可见；只有全部原始槽为空才是未选择。</summary>
        private static IEnumerable<string> ValidScopedPaths(IEnumerable<MySqlCrossTableProjection> paths)=>
            paths.Select(path=>"("+path.Validity+") AND (("+string.Join(" AND ",
                path.RawSlots.Values.Select(value=>"NOT "+Nonempty(value)))+") OR "+path.Reachable+")");
    }
}
