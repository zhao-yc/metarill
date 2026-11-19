using Metarill.Core.Metadata;
using Metarill.Data.Relations;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>候选名称调用实际中间页的单值范围，同时完整编译名称投影。</summary>
    internal static MySqlNonRedundantReadPlan CompileProjectRelation(MetadataCatalog catalog,string tableId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantSingleTargetScope> singleTargets)=>
        CompileRelationScope(catalog,tableId,outputs,[],dependencies,deferCascadeNames:false,singleTargets:singleTargets);

    /// <summary>候选与实际父路径使用同一发布页范围；调用端不能替换其中任一逻辑条件。</summary>
    internal static MySqlNonRedundantReadPlan CompileCrossPageCandidate(MetadataCatalog catalog,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,
        IReadOnlyList<NonRedundantCrossTableScope>? displayBindings=null,string rootAlias="b",string aliasPrefix="r",
        HashSet<(string TableId,string ControlId)>? activeSelfTreeScopes=null,IReadOnlyList<string>? listFields=null,
        bool projectSingle=false,string? accountParentPageType=null)
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
        if(table.IsRongyu!=0||page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0||
            page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
            throw new NotSupportedException("候选必须属于已发布项目的普通非冗余目标页。");
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==tableId));
        var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        var logical=syntax.Fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)).ToHashSet(StringComparer.Ordinal);
        var treeNames=SelfTreeConditionFields(catalog,table,page,columns,syntax.Fields);
        var listTrees=SelfTreeConditionFields(catalog,table,page,columns,listFields??[]);
        var listCrosses=(listFields??[]).Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)).ToArray();
        var projectParent=!global?ProjectScopedSingleRelationTarget.LogicalParent(catalog,table,page):null;
        if(listFields is not null&&(!global&&projectParent is null||listTrees.Count==0&&listCrosses.Length==0))
            throw new NotSupportedException("固定下拉附加条件必须引用已发布的逻辑级联字段。");
        // 全局单值候选从自身发布页证明物理父键；旧多选仍满足原独立页合同。
        var physicalParent=global&&!projectSingle&&page.Extra.GetValueOrDefault("type").ToString()!="2"
            ?GlobalCrossTableTarget.SingleParent(catalog,table,page):null;
        var accountParentPage=accountParentPageType is not null;
        var hasParentScope=physicalParent is not null||accountParentPage||displayBindings is not null||
            listFields is null&&treeNames.Count>0&&IsScopedMultiTarget(catalog,table,pageId);
        if(global)
        {
            if(treeNames.Count>0)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,table,page,allowMultiParent:hasParentScope);
            else if(displayBindings is null&&!accountParentPage&&physicalParent is null)GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,table,page);
            else GlobalCrossTableTarget.RequireMultiCandidate(catalog,table,page);
        }
        if(!projectSingle&&!accountParentPage&&physicalParent is null&&logical.Count==0&&treeNames.Count==0&&listFields is null&&(!global||displayBindings is not{Count:>0}))
            throw new InvalidOperationException("候选页未声明逻辑级联条件或已证明的全局跨表投影。");
        var parent=physicalParent??projectParent??(hasParentScope||projectSingle?MultiTargetParentScope.Compile(catalog,table,syntax):null);
        if(accountParentPage&&(!global||page.Extra.GetValueOrDefault("type").ToString()!=accountParentPageType||
            parent is null||(accountParentPageType=="2"
                ?parent.Logical||!syntax.Variables.Any(Metarill.Core.Security.AccountConditionVariables.IsSupported):physicalParent is null)))
            throw new NotSupportedException("全局候选必须使用实际发布的条件和物理父键。");
        var condition=CascadeScopeCondition(table,MetadataStoragePolicy.ForTable(catalog,table),columns,syntax,parent?.Target,treeNames);
        // 附加条件只扩充真实路径与投影，页面条件仍由实际发布页决定。
        logical.UnionWith(listCrosses);
        var projectionTrees=treeNames.Concat(listTrees).ToHashSet(StringComparer.Ordinal);
        var scopes=logical.Count==0?[]:CrossMembershipBindings(catalog,table,page,logical);
        // 范围的绑定按发布页生成，其它绑定仅用于该独立选择页展示额外的路径。
        var bindings=scopes.Concat(displayBindings??[]).DistinctBy(path=>path.SourceColumnId,StringComparer.Ordinal).ToArray();
        var builder=new Builder(catalog,tableId,deferCascadeNames:projectionTrees.Count>0||accountParentPage||physicalParent is not null,crossTables:bindings,rootAlias:rootAlias,aliasPrefix:aliasPrefix)
        {
            CrossCandidateCondition=condition,
            SelfTreeCandidateFields=projectionTrees,
            ActiveSelfTreeScopes=activeSelfTreeScopes??new(),
            MultiCandidateParent=projectSingle?null:parent,
            MultiCandidatePage=page,
            GlobalCrossCandidate=global,
            ScopedRootControls=scopes.Select(path=>path.SourceColumnId).ToHashSet(StringComparer.Ordinal),
            CrossAliasPrefix=rootAlias=="b"?"cross":aliasPrefix+"c"
        };
        return builder.Compile(outputs,dependencies.Concat(syntax.Fields).Concat(listFields??[])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
                cascadeCacheOwner:owner,accountPhysicalCandidate:true).Compile(["id"],[]);
    }

    /// <summary>多个目标页可共享同一个实际全局父页面，独立来源父校验只绑定该发布页。</summary>
    internal static (TableMetadata Table,PageMetadata Page) PublishedMultiGlobalParent(MetadataCatalog catalog,string tableId,string pageId)
    {
        var page=catalog.ConfigPages.Single(item=>item.Id==pageId&&item.ConfigTableId==tableId);
        var table=catalog.ConfigTables.Single(item=>item.Id==tableId);
        if(page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
        var parent=MultiParent(catalog,page);
        if(parent is not{Global:true})throw new NotSupportedException("多值全局父须显式声明全局表关系。");
        if(!parent.Logical)return(parent.Parent,PublishedPhysicalMultiParent(catalog,parent));
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
        return(parent.Parent,published);
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
        internal MultiTargetParentScope? MultiCandidateParent{get;set;}
        internal PageMetadata? MultiCandidatePage{get;init;}
        private string? selectedMultiParentSql;
        internal (CascadeCacheOwner Owner,ColumnMetadata Column)? MultiParentOwner{get;init;}

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
                predicate="EXISTS (SELECT 1 FROM "+Q(parent.Parent.TableName)+" "+alias+" WHERE "+same+" AND "+key+">0 AND "+Scope(parent.Parent,alias)+")";
            }
            else if(!parent.Logical)predicate=PhysicalGlobalParentPredicate(parent,target);
            if(MultiParentOwner is not {} binding)return predicate;
            var (owner,source)=binding;
            cascadeCacheOwnerFields.Add(source.ColumnName);
            var fromOwner=owner.Alias+"."+Q(source.ColumnName);
            // 批量名称读取把来源值作为文本传入，保持相邻高位整数的精确比较。
            return predicate+" AND BINARY CAST("+target+" AS CHAR)=BINARY CAST("+fromOwner+" AS CHAR)";
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

        /// <summary>目标页的条件与名称共用当前记录和联接，维护用途按来源行归属绑定。</summary>
        private string SelectedMultiScope(string scope)
        {
            var page=SelectedMultiPage!;
            if(page.ConfigTableId!=root.Id||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
                throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
            var treeNames=PublishedSelfTreeConditionFields(catalog,root,page,columns[root.Id].Values.ToArray());
            var global=GlobalCrossTableTarget.Declared(root);
            if(global)
            {
                if(treeNames.Count>0)GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(catalog,root,page,allowMultiParent:MultiCandidateParent is not null);
                else GlobalCrossTableTarget.RequireMultiCandidate(catalog,root,page);
            }
            var condition=CrossTableParentCondition.Compile(catalog,root,page,storage[root.Id],allowLogical:true,
                multiParent:MultiCandidateParent?.Target,selfTreeFields:treeNames);
            if(!condition.Allowed)
                throw new NotSupportedException("多值目标页包含未接通的范围。");
            if(!condition.HasLogical)
            {
                if(condition.Plan is not {Variables.Count:>0} account)
                    throw new NotSupportedException("多值目标页缺少固定的级联或账号范围。");
                cascadeLabelPages.Add(page.Id);
                if(cascadeCacheOwner is {} owner)
                {
                    var predicate=PhysicalAccountCacheCondition(root,rootAlias,page,owner,MultiCandidateParent?.Target);
                    return scope.Length==0?predicate:scope+" AND "+predicate;
                }
                // 项目和父参数已由实际范围负责，账号条件只追加已证明的物理字段。
                var clause=account.RenderAccountProjection(new MySqlDatabaseProvider(),ConditionParameterPrefix(aliasPrefix+"multi_account"),field=>
                    rootAlias+"."+Q(Column(root,field).ColumnName));
                foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
                return scope.Length==0?clause.Sql:scope+" AND "+clause.Sql;
            }
            var plan=condition.Plan;
            var fields=RequireCrossCondition(root,page,plan,MultiCandidateParent?.Target,treeNames);
            output=cascadeLabelField!;
            var level=new CrossLevel(root,Column(root,cascadeLabelField!),null,new(page.Id,plan),page,fields);
            var selected=NestedCrossCondition(level,rootAlias,multiParent:MultiCandidateParent?.Target,selfTreeFields:treeNames);
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

        /// <summary>业务页的单值联接只显示其固定候选范围内的名称。目标条件在相关子查询内独立编译。</summary>
        private string ProjectSingleReadCondition(ColumnMetadata relation,TableMetadata target,string alias,
            NonRedundantSingleTargetScope binding)
        {
            if(root.IsRongyu!=0||target.IsRongyu!=0||GlobalCrossTableTarget.Declared(root)||
                relation.ControlKind is not("List" or "SelectPage")||!storage[root.Id].IsPhysical(relation))
                throw new NotSupportedException("项目内单值读取须使用普通非冗余表的实际物理外键。");
            var page=catalog.ConfigPages.Single(item=>item.Id==binding.PageId&&item.ConfigTableId==target.Id);
            var parent=MultiParent(catalog,page);
            if((parent is null)!=(binding.SourceParentId is null)||parent is {Global:true,Logical:false})
                throw new InvalidOperationException("项目内单值读取缺少已证明的父绑定。");

            var prefix=ConditionParameterPrefix("mt_"+aliasPrefix+alias+"_page");
            var plan=CompileCrossPageCandidate(catalog,target.Id,page.Id,["id"],[],rootAlias:prefix,aliasPrefix:prefix+"_",
                activeSelfTreeScopes:ActiveSelfTreeScopes,projectSingle:true);
            plan.CopyParametersTo(crossConstants);
            var predicate=plan.Expression("id")+"="+alias+".`id`";
            if(plan.Scope.Length>0)predicate+=" AND "+plan.Scope;
            if(parent is {Logical:true})
            {
                // 目标由实际可见路径还原父级，来源则按同一父记录的持久键或已发布逻辑路径匹配。
                var sourceParent=parent.SingleSource(catalog,root,relation);
                if(sourceParent.Column.Id!=binding.SourceParentId)
                    throw new InvalidOperationException("项目内单值读取父键与实际级联来源不一致。");
                var sourceValue=SingleSourceParentValue(root,rootAlias,sourceParent);
                predicate+=" AND "+SameSingleParent(plan.Expression(parent.Target.ColumnName),sourceValue);
                foreach(var reference in plan.PhysicalReferences)
                    references.TryAdd((reference.Table,reference.Column.Name),reference);
            }
            else if(parent is not null)
            {
                var field=columns[root.Id].Values.Single(item=>item.Id==binding.SourceParentId);
                if(field.Id==relation.Id||field.LogicalType is not("string" or "int64")||
                    !GlobalPhysicalRelationValue.IsScalar(storage[root.Id],root,field)||
                    (field.LinkConfigTableId.Length>0?field.LinkConfigTableId!=parent.Parent.Id:
                        field.ColumnName!=parent.Parent.TableName+"_id"))
                    throw new InvalidOperationException("项目内单值读取父键与目标发布页不一致。");
                predicate+=" AND "+SinglePhysicalParentCondition(root,rootAlias,prefix,parent,field);
            }
            return "EXISTS (SELECT 1"+plan.From+" WHERE "+predicate+")";
        }

        /// <summary>关联目标先按物理键联接。用固定候选子查询隔离跨表层，避免引用后声明的别名。</summary>
        private string PublishedSingleCrossCondition(TableMetadata table,string alias,PageMetadata page,string? sourceParent=null)
        {
            var prefix=ConditionParameterPrefix("mt_"+aliasPrefix+alias+"_single");
            var plan=CompileCrossPageCandidate(catalog,table.Id,page.Id,["id"],[],rootAlias:prefix,aliasPrefix:prefix+"_",
                activeSelfTreeScopes:ActiveSelfTreeScopes);
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
            ColumnMetadata? parentField=null,string? sourceParent=null)
        {
            var members=columns[table.Id].Values.ToArray();
            var fields=PublishedSelfTreeConditionFields(catalog,table,page,members);
            var syntax=FixedConditionPlan.Compile(page.WhereSql,members.Select(member=>member.ColumnName));
            var condition=CascadeScopeCondition(table,storage[table.Id],members,syntax,parentField,fields);
            var logical=RequireCrossCondition(table,page,condition,parentField,fields);
            if(GlobalCrossTableTarget.Declared(table))scopedGlobalCrossTarget=true;
            if(cacheOwner is {} row&&logical.Count==0)
                return PhysicalAccountCacheCondition(table,alias,page,row,parentField,
                    allowFixedOnly:!GlobalCrossTableTarget.Declared(table));
            return NestedCrossCondition(new(table,Column(table,"id"),null,new(page.Id,condition),page,logical),
                alias,cacheOwner,parentField,fields,sourceParent);
        }

        /// <summary>物理账号条件按来源行归属计算，并记录源字段和目标筛选列的反向依赖。</summary>
        private string PhysicalAccountCacheCondition(TableMetadata table,string alias,PageMetadata page,
            CascadeCacheOwner owner,ColumnMetadata? parentField=null,bool allowFixedOnly=false)
        {
            var ownership=AccountScopedCrossCache.Compile(catalog,owner.Table,table,page,owner.GlobalTreeSource,
                parentField,owner.GlobalSingleSource,allowFixedOnly);
            cascadeCacheOwnerFields.UnionWith(ownership.Ownership.Select(binding=>binding.Source.ColumnName));
            return AccountCacheScope(ownership,table,alias,owner.Alias,render:true,owner.Table);
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

        /// <summary>同一物理外键的联接字段沿用已登记范围和来源行身份，未登记的项目范围仍由普通联接门禁拒绝。</summary>
        private (string Alias,string? Visibility,CascadeCacheOwner? CacheOwner) JoinSinglePath(
            TableMetadata table,string owner,ColumnMetadata foreign,TableMetadata target,CascadeCacheOwner? cacheOwner)
        {
            var source=Resolve(table,owner,foreign.ColumnName);
            if(!GlobalCrossTableTarget.Declared(target)&&refreshFields is not null&&table.Id==root.Id&&owner==rootAlias&&
                refreshMultiples.FirstOrDefault(binding=>!binding.Multiple&&binding.ScopedSinglePageId is not null&&
                    binding.ForeignColumnId==foreign.Id&&binding.TargetTableId==target.Id&&binding.SourceTableId==table.Id) is {} registered)
            {
                var row=new CascadeCacheOwner(table,owner);
                var scoped=JoinRegisteredSingleCache(table,owner,foreign,target,source,registered,row);
                return(scoped.Alias,scoped.Visibility,row);
            }
            if(cacheOwner is not null&&GlobalCrossTableTarget.Declared(target)&&
                JoinNestedSingleCache(table,owner,foreign,target,source,cacheOwner) is {} nested)
                return(nested.Alias,nested.Visibility,cacheOwner);
            return(Join(owner,foreign,target,source.Sql,source.Type,ordinaryRelation:true),null,cacheOwner);
        }

        /// <summary>名称副本和同外键的其他联接字段共用实际发布页，逐项保留各自的反向依赖。</summary>
        private (string Alias,string Visibility) JoinRegisteredSingleCache(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,RelationCacheDefinition definition,CascadeCacheOwner cacheOwner)
        {
            var proof=DerivedCrossTableCaches.ScopedSingleTarget(catalog,table,foreign,target);
            if(proof.Page.Id!=definition.ScopedSinglePageId||proof.Name.Id!=definition.ValueColumnId)
                throw new InvalidOperationException("单值名称缓存的目标范围与发布证明不一致。");
            var parent=MultiParent(catalog,proof.Page);
            var binding=parent?.SingleSource(catalog,table,foreign);
            return JoinScopedSingleCache(table,owner,foreign,target,source,proof.Page,cacheOwner,parent,binding);
        }

        /// <summary>先保留真实外键反查，再按最终缓存来源行核对内层账号、父键和级联范围。</summary>
        private (string Alias,string Visibility)? JoinNestedSingleCache(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,CascadeCacheOwner cacheOwner)
        {
            var proof=table.IsRongyu==1?GlobalSingleRelationTarget.RequireCached(catalog,table,foreign,target)
                :GlobalSingleRelationTarget.Require(catalog,table,foreign,target);
            return proof.RequiresScopedPage
                ?JoinScopedSingleCache(table,owner,foreign,target,source,proof.Page,cacheOwner,proof.Parent,proof.SourceParent)
                :null;
        }

        /// <summary>先登记物理关联用于反查，命名和其他投影的可见性随后按已发布范围计算。</summary>
        private (string Alias,string Visibility) JoinScopedSingleCache(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,PageMetadata page,CascadeCacheOwner cacheOwner,
            MultiTargetParentScope? parent,MultiSourceParent? binding)
        {
            var sourceParent=binding is not null&&(parent?.Logical==true||binding.LogicalPages.Count>0)
                ?SingleSourceParentValue(table,owner,binding,cacheOwner):null;
            // 反查需要从最终写回的根表出发，保存中间记录及逻辑父表达式引用的祖先别名。
            var sourceFrom=owner==rootAlias&&sourceParent is null?null:" FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins);
            var alias=Join(owner,foreign,target,source.Sql,source.Type,validatedCrossGlobal:true,scopedCacheLink:true);
            var visible=PublishedSingleCondition(target,alias,page,cacheOwner,parent?.Target,parent?.Logical==true?sourceParent:null);
            if(parent is {Logical:false})
                visible+=(visible.Length==0?"":" AND ")+SinglePhysicalParentCondition(table,owner,alias,parent,binding!.Column,
                    cacheOwner,sourceParent,sourceFrom);
            return (alias,alias+".`id` IS NOT NULL"+(visible.Length==0?"":" AND ("+visible+")"));
        }

        /// <summary>来源父键来自发布页的可见级联或持久字段，保持高位 ID 和字符串精度。</summary>
        private string SingleSourceParentValue(TableMetadata source,string owner,MultiSourceParent parent,CascadeCacheOwner? cacheOwner=null)
        {
            if(parent.LogicalPages.Count>0)return LogicalRelationSourceParent(source,owner,parent,cacheOwner);
            var binding=parent.Column;
            Use(source,binding);
            Touch(owner,binding.ColumnName);
            if(trackReverse)cascadeCacheOwnerFields.Add(binding.ColumnName);
            return owner+"."+Q(binding.ColumnName);
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
            if(sourceParent is null)Use(source,binding);
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
                (parentScope.Length==0?"":" AND "+parentScope)+")";
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
            var visible=NestedCrossCondition(level,alias,new(table,owner),selfTreeFields:treeNames);
            var name=Resolve(target,alias,value.ColumnName);
            return ("CASE WHEN "+alias+".`id` IS NOT NULL AND "+visible+" THEN "+MySqlMultiValueLabel.Text(name.Sql,name.Type)+" END","string");
        }

        /// <summary>同一条查询展开所需祖先，写入沿同一事务锁定全部联接。候选内部的存储校验不进入外层 VO。</summary>
        private string NestedCrossCondition(CrossLevel level,string owner,CascadeCacheOwner? cacheOwner=null,ColumnMetadata? multiParent=null,IReadOnlySet<string>? selfTreeFields=null,string? sourceParent=null)
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
                string predicate;
                if(cacheOwner is {} row)
                {
                    var proof=AccountScopedCrossCache.CompileRecursive(catalog,row.Table,table,level.Page,multiParent,row.GlobalTreeSource,selfTreeFields,row.GlobalSingleSource);
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
