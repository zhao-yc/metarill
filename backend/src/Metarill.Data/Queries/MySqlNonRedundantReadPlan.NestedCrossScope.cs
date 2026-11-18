using Metarill.Core.Metadata;
using Metarill.Data.Relations;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>候选与实际父路径使用同一发布页范围；调用端不能替换其中任一逻辑条件。</summary>
    internal static MySqlNonRedundantReadPlan CompileCrossPageCandidate(MetadataCatalog catalog,string tableId,string pageId,
        IReadOnlyList<string> outputs,IReadOnlyList<string> dependencies,
        IReadOnlyList<NonRedundantCrossTableScope>? displayBindings=null)
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
        if(global)
        {
            if(displayBindings is null)GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,table,page);
            else GlobalCrossTableTarget.RequireMultiCandidate(catalog,table,page);
        }
        var columns=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==tableId));
        var syntax=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName));
        var logical=syntax.Fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,columns,field)).ToHashSet(StringComparer.Ordinal);
        if(logical.Count==0&&(!global||displayBindings is not{Count:>0}))
            throw new InvalidOperationException("候选页未声明逻辑跨表条件或已证明的全局跨表投影。");
        var parent=displayBindings is null?null:MultiTargetParentScope.Compile(catalog,table,syntax);
        var condition=CascadeScopeCondition(table,MetadataStoragePolicy.ForTable(catalog,table),columns,syntax,parent?.Target);
        var scopes=logical.Count==0?[]:CrossMembershipBindings(catalog,table,page,logical);
        // 范围的绑定按发布页生成，其它绑定仅用于该独立选择页展示额外的路径。
        var bindings=scopes.Concat(displayBindings??[]).DistinctBy(path=>path.SourceColumnId,StringComparer.Ordinal).ToArray();
        var builder=new Builder(catalog,tableId,crossTables:bindings)
        {
            CrossCandidateCondition=condition,
            MultiCandidateParent=parent,
            MultiCandidatePage=page,
            GlobalCrossCandidate=global,
            ScopedRootControls=scopes.Select(path=>path.SourceColumnId).ToHashSet(StringComparer.Ordinal)
        };
        return builder.Compile(outputs,dependencies.Concat(syntax.Fields).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
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
        return syntax.Fields.Any(member=>CrossTableStorageColumns.IsLogicalPathField(parent,columns,member))
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
        GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,parent.Parent,published);
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
        GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,parent.Parent,pages[0]);
        return pages[0];
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
            var plan=CompileMultiParentPage(catalog,table,published,alias,alias+"_",cascadeCacheOwner,trackReverse);
            scopedGlobalCrossTarget=true;
            plan.CopyParametersTo(crossConstants);
            cascadeLabelPages.Add(published.Id);
            cascadeLabelPages.UnionWith(plan.CascadeLabelPages);
            cascadeCacheOwnerFields.UnionWith(plan.CascadeCacheOwnerFields);
            Use(root,parent.Target);Touch(rootAlias,parent.Target.ColumnName);
            foreach(var reference in plan.PhysicalReferences)references.TryAdd((reference.Table,reference.Column.Name),reference);
            var key=alias+".`id`";
            var same=parent.Target.LogicalType=="string"?"BINARY CAST("+key+" AS CHAR)=BINARY ("+target+")":key+"="+target;
            foreach(var reverse in plan.Reverse)
            {
                var probe="EXISTS (SELECT 1"+reverse.From+" WHERE "+same+" AND "+reverse.TargetPredicate+")";
                var from=" FROM "+Q(root.TableName)+" "+rootAlias;
                var fields=reverse.Outputs.Values.SelectMany(value=>value).ToHashSet(StringComparer.Ordinal);
                customReverse.Add(new(reverse.TargetTable,from,probe,"predicate",new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,fields}}));
            }
            return "EXISTS (SELECT 1"+plan.From+" WHERE "+same+" AND "+key+">0"+(plan.Scope.Length==0?"":" AND "+plan.Scope)+")";
        }

        /// <summary>目标页的条件与名称共用当前记录和联接，维护用途按来源行归属绑定。</summary>
        private string SelectedMultiScope(string scope)
        {
            var page=SelectedMultiPage!;
            if(page.ConfigTableId!=root.Id||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
                throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
            if(GlobalCrossTableTarget.Declared(root))GlobalCrossTableTarget.RequireMultiCandidate(catalog,root,page);
            var condition=CrossTableParentCondition.Compile(catalog,root,page,storage[root.Id],allowLogical:true,
                multiParent:MultiCandidateParent?.Target);
            if(!condition.Allowed||!condition.HasLogical)
                throw new NotSupportedException("多值目标页缺少固定的逻辑级联条件。");
            var plan=condition.Plan;
            var fields=RequireCrossCondition(root,page,plan,MultiCandidateParent?.Target);
            output=cascadeLabelField!;
            var level=new CrossLevel(root,Column(root,cascadeLabelField!),null,new(page.Id,plan),page,fields);
            var selected=NestedCrossCondition(level,rootAlias,multiParent:MultiCandidateParent?.Target);
            return scope.Length==0?selected:scope+" AND "+selected;
        }
        internal bool GlobalCrossCandidate{get;init;}
        internal IReadOnlySet<string> ScopedRootControls{get;init;}=new HashSet<string>(StringComparer.Ordinal);

        /// <summary>固定候选页过滤实际可见的整条路径，查询及计数无法省略范围。</summary>
        private string FixedCrossCandidateScope(string scope,IReadOnlyDictionary<string,string> expressions)
        {
            var clause=CrossCandidateCondition?.RenderAccountProjection(new MySqlDatabaseProvider(),"mt_candidate",
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

        /// <summary>缓存通过已发布候选页求值，目标与内层依赖都进入原传播队列。</summary>
        private (string Sql,string Type) RecursiveCacheValue(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,ColumnMetadata value,RelationCacheDefinition definition)
        {
            var page=CrossTableCacheProof.RequireRecursivePage(catalog,target,value,definition);
            var condition=CrossTableParentCondition.Compile(catalog,target,page,storage[target.Id],allowLogical:true).Plan!;
            var logical=RequireCrossCondition(target,page,condition);
            var sourceId=Resolve(table,owner,foreign.ColumnName);
            var alias=Join(owner,foreign,target,sourceId.Sql,sourceId.Type,
                validatedCrossGlobal:GlobalCrossTableTarget.Declared(target));
            var level=new CrossLevel(target,value,null,new(page.Id,condition),page,logical);
            var visible=NestedCrossCondition(level,alias,new(table,owner));
            var name=Resolve(target,alias,value.ColumnName);
            return ("CASE WHEN "+alias+".`id` IS NOT NULL AND "+visible+" THEN "+MySqlMultiValueLabel.Text(name.Sql,name.Type)+" END","string");
        }

        /// <summary>同一条查询展开所需祖先，写入沿同一事务锁定全部联接。候选内部的存储校验不进入外层 VO。</summary>
        private string NestedCrossCondition(CrossLevel level,string owner,CascadeCacheOwner? cacheOwner=null,ColumnMetadata? multiParent=null)
        {
            var table=level.Table;
            cacheOwner??=cascadeCacheOwner;
            if(table.IsRongyu!=0||cacheOwner is null&&trackReverse)
                throw new NotSupportedException("递归候选要求普通非冗余目标；缓存维护需独立证明实际来源行的范围。");
            if(!activeCrossScopePages.Add(level.Page.Id))
                throw new InvalidOperationException("跨表级联候选页条件存在循环依赖。");
            try
            {
                var bindings=CrossMembershipBindings(catalog,table,level.Page,level.LogicalFields)
                    .ToDictionary(path=>path.SourceColumnId,StringComparer.Ordinal);
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

                    var path=bindings.Values.First(binding=>CrossTableStorageColumns.ContainsPathField(
                        columns[table.Id].Values.Single(source=>source.Id==binding.SourceColumnId),field));
                    var group=SharedCrossGroup(table,path.SourceColumnId);
                    if(group is not null)
                        return SharedCrossValue(table,owner,group,field,scopedBindings:bindings,cacheOwner:cacheOwner).Sql;
                    var source=columns[table.Id].Values.Single(item=>item.Id==path.SourceColumnId);
                    var selection=CrossTableSparseSelection.Compile(table,source.Id,columns[table.Id].Values);
                    return CrossValue(table,owner,source,selection,path,field,scopeOnly:true,cacheOwner:cacheOwner).Sql;
                }
                // 同一可见路径既负责页面条件，也负责恢复可能没有实际落库的逻辑父键。
                if(multiParent is not null&&level.LogicalFields.Contains(multiParent.ColumnName))
                    selectedMultiParentSql=Projection(multiParent.ColumnName);
                string predicate;
                if(cacheOwner is {} row)
                {
                    var proof=AccountScopedCrossCache.CompileRecursive(catalog,row.Table,table,level.Page,multiParent);
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

                // 空选择允许原 IS NULL；已存但不可见或无法恢复的节点不能冒充空值放宽候选。
                var paths=bindings.Keys.Select(source=>
                {
                    var key=owner+":"+source;
                    return scopedCrossProjections.GetValueOrDefault(key)??crossProjections[key];
                });
                return "("+string.Join(" AND ",new[]{predicate}.Concat(ValidScopedPaths(paths))
                    .Where(part=>part.Length>0))+")";
            }
            finally
            {
                activeCrossScopePages.Remove(level.Page.Id);
            }
        }

        /// <summary>已存的路径必须实际可见；只有全部原始槽为空才是未选择。</summary>
        private static IEnumerable<string> ValidScopedPaths(IEnumerable<MySqlCrossTableProjection> paths)=>
            paths.Select(path=>"("+path.Validity+") AND (("+string.Join(" AND ",
                path.RawSlots.Values.Select(value=>"NOT "+Nonempty(value)))+") OR "+path.Reachable+")");
    }
}
