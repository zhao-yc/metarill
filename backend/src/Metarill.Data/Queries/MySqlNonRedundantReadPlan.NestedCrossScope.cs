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
            GlobalCrossCandidate=global,
            ScopedRootControls=scopes.Select(path=>path.SourceColumnId).ToHashSet(StringComparer.Ordinal)
        };
        return builder.Compile(outputs,dependencies.Concat(syntax.Fields).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private sealed partial class Builder
    {
        internal FixedConditionSqlPlan? CrossCandidateCondition{get;init;}
        internal PageMetadata? SelectedMultiPage{get;init;}
        internal MultiTargetParentScope? MultiCandidateParent{get;set;}
        internal (CascadeCacheOwner Owner,ColumnMetadata Column)? MultiParentOwner{get;init;}

        /// <summary>父表必须仍在本项目且可见。字符串键与主键按十进制字节比较，避免高位ID被隐式转成浮点数。</summary>
        private string MultiParentPredicate(MultiTargetParentScope parent)
        {
            var target=rootAlias+"."+Q(parent.Target.ColumnName);
            var alias=aliasPrefix+"multi_parent";
            Use(root,parent.Target);Touch(rootAlias,parent.Target.ColumnName);
            Use(parent.Parent,Column(parent.Parent,"id"));
            var key=alias+".`id`";
            var same=parent.Target.LogicalType=="string"?"BINARY CAST("+key+" AS CHAR)=BINARY "+target:key+"="+target;
            var predicate="EXISTS (SELECT 1 FROM "+Q(parent.Parent.TableName)+" "+alias+" WHERE "+same+" AND "+key+">0 AND "+Scope(parent.Parent,alias)+")";
            if(MultiParentOwner is not {} binding)return predicate;
            var (owner,source)=binding;
            cascadeCacheOwnerFields.Add(source.ColumnName);
            var fromOwner=owner.Alias+"."+Q(source.ColumnName);
            // 批量名称读取把来源值作为文本传入，保持相邻高位整数的精确比较。
            return predicate+" AND BINARY CAST("+target+" AS CHAR)=BINARY CAST("+fromOwner+" AS CHAR)";
        }

        /// <summary>目标页的条件与名称共用当前记录和联接，维护用途按来源行归属绑定。</summary>
        private string SelectedMultiScope(string scope)
        {
            var page=SelectedMultiPage!;
            if(page.ConfigTableId!=root.Id||page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId)
                throw new UnauthorizedAccessException("多值目标页不属于已发布项目。");
            if(GlobalCrossTableTarget.Declared(root))GlobalCrossTableTarget.RequireMultiCandidate(catalog,root,page);
            MultiCandidateParent=MultiParent(catalog,page);
            var condition=CrossTableParentCondition.Compile(catalog,root,page,storage[root.Id],allowLogical:true,
                multiParent:MultiCandidateParent?.Target);
            if(condition is not {Allowed:true,HasLogical:true,Plan:{ } plan})
                throw new NotSupportedException("多值目标页缺少固定的逻辑级联条件。");
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
                var parameterPrefix="mt_nested_"+(multiLabel?aliasPrefix:"")+owner;
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
                string predicate;
                if(cacheOwner is {} row)
                {
                    var proof=AccountScopedCrossCache.CompileRecursive(catalog,row.Table,table,level.Page,multiParent);
                    cascadeCacheOwnerFields.UnionWith(proof.Ownership.Select(binding=>binding.Source.ColumnName));
                    predicate=AccountCacheScope(proof,table,owner,row.Alias,render:true,row.Table,parameterPrefix,Projection);
                }
                else
                {
                    var clause=level.Scope.Condition!.RenderAccountProjection(new MySqlDatabaseProvider(),parameterPrefix,Projection);
                    foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
                    predicate=clause.Sql;
                }

                // 空选择允许原 IS NULL；已存但不可见或无法恢复的节点不能冒充空值放宽候选。
                var paths=bindings.Keys.Select(source=>
                {
                    var key=owner+":"+source;
                    return scopedCrossProjections.GetValueOrDefault(key)??crossProjections[key];
                });
                return "("+string.Join(" AND ",new[]{predicate}.Concat(ValidScopedPaths(paths)))+")";
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
