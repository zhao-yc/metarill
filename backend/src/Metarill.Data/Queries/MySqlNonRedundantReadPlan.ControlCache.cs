using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        /// <summary>页面和控件共用实际父键，允许控件单独限定目标物理父级。</summary>
        private (string Alias,string Visibility) JoinSingleCacheWithControl(TableMetadata table,string owner,ColumnMetadata foreign,
            TableMetadata target,(string Sql,string Type) source,PageMetadata page,CascadeCacheOwner cacheOwner,
            FixedConditionPlan control,PageMetadata? sourcePage=null)
        {
            MultiTargetParentScope? parent;
            if(target.IsRongyu==1&&foreign.ControlKind=="List"&&!GlobalCrossTableTarget.Declared(target))
            {
                var condition=FixedConditionPlan.Compile(page.WhereSql,columns[target.Id].Keys);
                parent=MultiTargetParentScope.CompileListPage(catalog,target,condition.Terms.Concat(control.Terms));
            }
            else if(target.IsRongyu==1&&foreign.ControlKind=="SelectPage"&&!GlobalCrossTableTarget.Declared(target))
                parent=ProjectScopedSingleRelationTarget.PhysicalSelectionParent(catalog,target,page);
            else
            {
                var pageParent=ProjectScopedSingleRelationTarget.PageParent(catalog,target,page)??MultiParent(catalog,page);
                parent=SingleParentWithControl(target,foreign,page,pageParent,control);
            }
            var binding=sourcePage is null?parent?.SingleSource(catalog,table,foreign):
                parent?.SingleSource(catalog,table,foreign,sourcePage);
            return JoinScopedSingleCache(table,owner,foreign,target,source,page,cacheOwner,parent,binding,control);
        }

        /// <summary>项目内下拉允许在实际页面和控件分别声明同父的物理列。</summary>
        private MultiTargetParentScope? SingleParentWithControl(TableMetadata target,ColumnMetadata relation,
            PageMetadata page,MultiTargetParentScope? pageParent,FixedConditionPlan? control)
        {
            if(control is not {Terms.Count:>0})return pageParent;
            if(relation.ControlKind=="List"&&!GlobalCrossTableTarget.Declared(target)&&
                pageParent is not ({Logical:true} or {Global:true}))
            {
                var pageTerms=FixedConditionPlan.Compile(page.WhereSql,columns[target.Id].Keys).Terms;
                var combined=MultiTargetParentScope.CompileListPage(catalog,target,pageTerms.Concat(control.Terms));
                if(combined is {Logical:false,Global:false})return combined;
            }
            var controlParent=MultiTargetParentScope.Compile(catalog,target,control);
            if(controlParent is {Logical:true}||pageParent is not null&&controlParent is not null&&!pageParent.HasTarget(controlParent.Target.Id))
                throw new InvalidOperationException("单值读取的目标页与控件父范围不一致。");
            return pageParent??controlParent;
        }

        /// <summary>控件条件附加到物理联接后的可见性，记录变为不可见时仍可反查并清空副本。</summary>
        private string SingleControlCacheCondition(TableMetadata table,string owner,TableMetadata target,string alias,
            PageMetadata page,FixedConditionPlan condition,CascadeCacheOwner cacheOwner,bool storedPhysicalExact,bool sharedTextParent)
        {
            var members=columns[target.Id];
            var tree=SelfTreeConditionFields(catalog,target,page,members.Values.ToArray(),condition.Fields);
            var cross=condition.Fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(target,members.Values.ToArray(),field))
                .ToHashSet(StringComparer.Ordinal);
            var bindings=(cross.Count==0?[]:CrossMembershipBindings(catalog,target,page,cross))
                .ToDictionary(binding=>binding.SourceColumnId,StringComparer.Ordinal);
            bool Joined(ColumnMetadata column)=>sharedTextParent&&storage[target.Id].Kind(column)==MetadataFieldStorage.Join;
            bool Logical(string field)=>tree.Contains(field)||cross.Contains(field)||Joined(Column(target,field));
            if(condition.Fields.Any(field=>!Logical(field)&&!GlobalPhysicalRelationValue.IsScalar(storage[target.Id],target,Column(target,field))&&
                !(sharedTextParent&&ProjectScopedSingleRelationTarget.IsStoredConditionCache(catalog,target,Column(target,field)))))
                throw new InvalidOperationException("名称副本控件条件缺少非凭据物理字段或固定逻辑层位。");
            var prefix=ConditionParameterPrefix("mt_single_control_"+alias);
            var account=condition.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                AccountConditionVariables.IsSupported(value.Text))).ToArray();
            var project=condition.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                value.Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))).ToArray();
            var predicates=new List<string>();
            if(project.Length>0)
            {
                if(!GlobalCrossTableTarget.Declared(target)||project.Any(term=>term.Field!="yz_project_id"||
                    term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1))
                    throw new InvalidOperationException("全局名称副本的项目条件必须限定实际物理项目列。");
                // 任务使用已发布目录的项目身份。目标虽显式全局，控件仍可只接受本项目的记录。
                predicates.Add(Scope(target,alias));
            }
            if(account.Length>0)
            {
                var definition=new PageMetadata{Id=page.Id,ConfigTableId=target.Id,Extra=page.Extra,
                    WhereSql=string.Join(" AND ",account.Select(term=>term.ToDefinition()))};
                predicates.Add(PhysicalAccountCacheCondition(target,alias,definition,cacheOwner,allowFixedOnly:true,
                    separateExactScope:storedPhysicalExact));
            }
            var terms=condition.Terms.Except(account).Except(project).Where(term=>!term.Values.Any(value=>
                value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text)));
            var syntax=FixedConditionPlan.Compile(string.Join(" AND ",terms.Select(term=>term.ToDefinition())),members.Keys);
            if(syntax.Terms.Count==0)return string.Join(" AND ",predicates);
            var validity=new HashSet<string>(StringComparer.Ordinal);
            var plan=FixedConditionSqlPlan.Compile(syntax,syntax.Fields.ToDictionary(field=>field,field=>Column(target,field).LogicalType,StringComparer.Ordinal));
            var clause=plan.RenderRowProjection(prefix,Projection,
                (variable,type)=>SingleControlFormColumn(table,owner,validity,variable,type));
            foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
            predicates.Add(clause.Sql);
            predicates.AddRange(validity);
            predicates.AddRange(ValidScopedPaths(ScopedCrossPaths(alias,bindings.Keys)));
            return string.Join(" AND ",predicates.Where(part=>part.Length>0));

            string Projection(string field)
            {
                var column=Column(target,field);
                if(tree.Contains(field))
                    return SelfTreeLabelExpression(target,alias,Column(target,field[..^5]+"_id"),column,conditionOwner:cacheOwner);
                if(cross.Contains(field))return ScopedCrossField(target,alias,bindings,field,cacheOwner);
                if(Joined(column))
                {
                    // 与候选读取同样保留字典页可见性，联接终点同时进入反向传播依赖。
                    var value=Resolve(target,alias,field,cacheOwner);
                    if(value.Type!=column.LogicalType)
                        throw new NotSupportedException("名称副本控件联接条件必须与终点逻辑类型一致。");
                    return value.Sql;
                }
                Use(target,column);Touch(alias,field);
                return alias+"."+Q(field);
            }
        }


        private int storedListScopeCount;

        /// <summary>冗余字典按实际页面验证物理条件，实时读取绑定请求账号，维护绑定来源行账号与表单值。</summary>
        private string ProjectStoredListCondition(TableMetadata source,string owner,ColumnMetadata relation,TableMetadata target,
            string targetAlias,PageMetadata sourcePage,PageMetadata page,string? sourceParentId,FixedConditionPlan control,
            CascadeCacheOwner? cacheOwner=null,bool pageScopeApplied=false)
        {
            var members=columns[target.Id];
            var pageCondition=FixedConditionPlan.Compile(page.WhereSql,members.Keys);
            var terms=pageCondition.Terms.Concat(control.Terms).ToArray();
            var global=GlobalCrossTableTarget.Declared(target);
            MultiTargetParentScope? parent;
            if(global)
            {
                var pageParent=MultiTargetParentScope.Compile(catalog,target,pageCondition);
                var controlParent=MultiTargetParentScope.Compile(catalog,target,control);
                if(pageParent is not null&&controlParent is not null&&pageParent.Target.Id!=controlParent.Target.Id)
                    throw new InvalidOperationException("原下拉的目标页与控件必须绑定同一实际父键。");
                parent=pageParent??controlParent;
            }
            else
            {
                parent=relation.ControlKind=="SelectPage"
                    ?ProjectScopedSingleRelationTarget.PhysicalSelectionParent(catalog,target,page)
                    :MultiTargetParentScope.CompileListPage(catalog,target,terms);
            }
            // 已保存的名称按来源行授权，候选和写入的精确匹配仍通过实际目标页处理。
            var physicalExact=ProjectScopedSingleRelationTarget.HasPhysicalSingleExact(catalog,target,page,relation.ControlKind);
            if (parent is {Logical:true} || (parent is null)!=(sourceParentId is null) ||
                !physicalExact&&page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0)
                throw new InvalidOperationException("原下拉的冗余字典需要已证明的物理范围和同一实际父键。");

            var parentSource=parent?.SingleSource(catalog,source,relation,sourcePage,
                allowLogicalListForm:!GlobalCrossTableTarget.Declared(source)&&!global);
            if(parentSource?.Column.Id!=sourceParentId)
                throw new InvalidOperationException("冗余字典选项与来源记录没有相同的已发布父范围。");
            // 两端确实共享已发布文本祖先时，固定常量或来源表单条件才允许读取联接副本。
            var sharedTextParent=parent is not null&&parentSource is {LogicalPages.Count:>0}&&!global&&
                UsesSharedStoredTextParent(source,parentSource,target,page,parent);
            if (terms.Any(term=>!GlobalPhysicalRelationValue.IsScalar(storage[target.Id],target,Column(target,term.Field))&&
                !(sharedTextParent&&term.Values.All(value=>value.Kind!=FixedConditionValueKind.Variable||
                    control.Terms.Contains(term)&&value.Text.StartsWith("formParameters.",StringComparison.Ordinal))&&
                    ProjectScopedSingleRelationTarget.IsStoredConditionCache(catalog,target,Column(target,term.Field)))))
                throw new InvalidOperationException("冗余字典范围只可使用真实的非凭据物理列。");
            // 普通字典已由物理联接限定项目。全局字典只有声明了项目谓词才按当前身份限定。
            static bool Project(FixedConditionTerm term)=>term.Field=="yz_project_id"&&term.Comparison==FixedConditionComparison.Equal&&
                term.Values is [{Kind:FixedConditionValueKind.Variable,Text:var variable}]&&
                variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);
            var scopeTerms=pageScopeApplied?control.Terms:terms;
            var scoped=scopeTerms.Where(term=>!term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                MultiTargetParentScope.IsParameter(value.Text))&&!Project(term));
            var validity=new HashSet<string>(StringComparer.Ordinal);
            if(!pageScopeApplied&&global&&terms.Any(Project))
                validity.Add(Scope(target,targetAlias,includeDeleted:true));
            if(cacheOwner is {} row)
            {
                var account=scoped.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                    AccountConditionVariables.IsSupported(value.Text))).ToArray();
                if(account.Length>0)
                {
                    var accountPage=new PageMetadata{Id=page.Id,ConfigTableId=page.ConfigTableId,Extra=page.Extra,
                        WhereSql=string.Join(" AND ",account.Select(term=>term.ToDefinition()))};
                    validity.Add(PhysicalAccountCacheCondition(target,targetAlias,accountPage,row,allowFixedOnly:true,
                        separateExactScope:physicalExact));
                    scoped=scoped.Except(account);
                }
            }
            var syntax=FixedConditionPlan.Compile(string.Join(" AND ",scoped.Select(term=>term.ToDefinition())),members.Keys);
            var plan=FixedConditionSqlPlan.Compile(syntax,syntax.Fields.ToDictionary(field=>field,field=>Column(target,field).LogicalType));
            // 同一个字典联接可以同时出现在候选条件和显示名中，分别保留各自的参数。
            var prefix=ConditionParameterPrefix("mt_stored_list_"+targetAlias+"_"+storedListScopeCount++);
            var clause=plan.RenderRowProjection(prefix,Projection,(variable,type)=>AccountConditionVariables.IsSupported(variable)
                    ?null:SingleControlFormColumn(source,owner,validity,variable,type),deferAccount:cacheOwner is null);
            foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
            if (parent is not null&&parentSource is not null&&!pageScopeApplied)
            {
                var sourceParent=SingleSourceParentValue(source,owner,parentSource);
                if(sharedTextParent)
                {
                    // 来源已验证此祖先，目标物理列比较原业务键即可复用同一祖先和账号范围。
                    Use(target,parent.Target);
                    Touch(targetAlias,parent.Target.ColumnName);
                    validity.Add(SameSingleParent(targetAlias+"."+Q(parent.Target.ColumnName),sourceParent));
                }
                else validity.Add(SinglePhysicalParentCondition(source,owner,targetAlias,parent,parentSource.Column,sourceParent:sourceParent));
                foreach(var additional in parent.AdditionalTargets)
                {
                    Use(target,additional.Target);
                    Touch(targetAlias,additional.Target.ColumnName);
                    validity.Add(SameSingleParent(targetAlias+"."+Q(additional.Target.ColumnName),sourceParent));
                }
            }
            return string.Join(" AND ",new[]{clause.Sql}.Concat(validity).Where(part=>part.Length>0));

            string Projection(string field)
            {
                // 字典的账号或表单筛选列变化后，也要反查旧选择并重算名称。
                Use(target,Column(target,field));
                Touch(targetAlias,field);
                return targetAlias+"."+Q(field);
            }
        }

        /// <summary>从实际发布元数据复核两端路径，只有完全相同的文本祖先可跳过主键联接。</summary>
        private bool UsesSharedStoredTextParent(TableMetadata source,MultiSourceParent sourceParent,TableMetadata target,
            PageMetadata targetPage,MultiTargetParentScope targetParent)=>
            UsesSharedStoredTextParent(source,sourceParent,target,[targetPage],targetParent.Target,targetParent.AdditionalTargets.Count>0);

        /// <summary>保存、传播和回填也从实际发布的各来源页复核物理文本键，不能按存储类型猜测。</summary>
        private bool UsesSharedStoredTextParent(TableMetadata sparse,MultiSourceParent sparseParent,TableMetadata stored,
            IReadOnlyList<PageMetadata> storedPages,ColumnMetadata storedColumn,bool compoundStored,bool storedToSparse=false)
        {
            var field=sparseParent.Column.ColumnName;
            var bindings=sparseParent.LogicalPages.SelectMany(page=>CrossMembershipBindings(catalog,sparse,page,
                new HashSet<string>(StringComparer.Ordinal){field})).DistinctBy(binding=>binding.SourceColumnId).ToArray();
            var paths=new List<(string TableId,string PageId,string NameId,string? Parent,string Key,bool Text)[]>();
            foreach(var binding in bindings)
            {
                var control=catalog.ConfigColumns.Single(column=>column.Id==binding.SourceColumnId&&column.ConfigTableId==sparse.Id);
                if(!CrossTableStorageColumns.ContainsPathField(control,field))continue;
                var entries=Definition(control).GetProperty("levels").EnumerateArray().ToArray();
                var index=System.Array.FindIndex(entries,entry=>(Value(entry,"columnPrefix") is {Length:>0} prefix?
                    prefix:Value(entry,"table"))+"_id"==field);
                if(index<0)throw new InvalidOperationException("稀疏记录缺少已声明的父路径层位。");
                paths.Add(entries.Take(index+1).Select((entry,levelIndex)=>
                {
                    var table=tables.Values.Single(table=>table.TableName==Value(entry,"table"));
                    var name=Value(entry,"nameField");
                    var key=CrossTableStorageColumns.AlternateLevelKey(catalog,table,entry);
                    return (table.Id,binding.Levels[levelIndex].PageId,Column(table,name.Length==0?"name":name).Id,
                        levelIndex==0?null:Value(entry,"parentField"),key?.ColumnName??"id",key?.LogicalType=="string");
                }).ToArray());
            }
            var sparseText=paths.Any(path=>path[^1].Text);
            if(!sparseText&&(!storedToSparse||storedColumn.LogicalType!="string"))return false;
            var published=storedPages.Select(page=>RedundantSharedParents.Compile(catalog,stored.Id,page.Id)
                .GetValueOrDefault(storedColumn.Id)).ToArray();
            if(!sparseText&&!published.Any(path=>path?.Levels[^1].TextKey==true))return false;
            if(!sparseText||published.Length==0||sparse.YzProjectId!=catalog.YzProjectId||stored.YzProjectId!=catalog.YzProjectId||
                sparseParent.LogicalPages.Concat(storedPages).Any(page=>page.YzProjectId!=catalog.YzProjectId)||
                sparseParent.AdditionalColumns.Count>0||compoundStored||
                published.Any(expected=>expected is null||paths.Any(path=>!path[^1].Text||path.Length!=expected.Levels.Count||
                    path.Where((level,index)=>!level.Equals((expected.Levels[index].TableId,expected.Levels[index].PageId,
                        expected.Levels[index].NameId,expected.Levels[index].Parent,expected.Levels[index].KeyColumn,
                        expected.Levels[index].TextKey))).Any())))
                throw new InvalidOperationException("非冗余与冗余记录必须使用相同发布文本父路径。");
            return true;
        }

        /// <summary>实时名称在目标子查询中求值，表单参数取实际来源行，账号参数由当前身份绑定。</summary>
        private string ProjectSingleControlCondition(TableMetadata source,string alias,TableMetadata target,
            FixedConditionPlan control,MySqlNonRedundantReadPlan targetPlan,string prefix)
        {
            var members=columns[target.Id];
            var project=control.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                value.Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))).ToArray();
            if(project.Any(term=>term.Field!="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1))
                throw new InvalidOperationException("全局名称读取的项目条件必须限定实际物理项目列。");
            var terms=control.Terms.Except(project).Where(term=>!term.Values.Any(value=>
                value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text)));
            var syntax=FixedConditionPlan.Compile(string.Join(" AND ",terms.Select(term=>term.ToDefinition())),members.Keys);
            var plan=FixedConditionSqlPlan.Compile(syntax,syntax.Fields.ToDictionary(field=>field,field=>Column(target,field).LogicalType,StringComparer.Ordinal));
            var validity=new HashSet<string>(StringComparer.Ordinal);
            var clause=plan.RenderRowProjection(prefix+"_control",targetPlan.Expression,(variable,type)=>
                AccountConditionVariables.IsSupported(variable)?null:SingleControlFormColumn(source,alias,validity,variable,type),
                deferAccount:true);
            foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
            if(project.Length>0)validity.Add(Scope(target,prefix,includeDeleted:true));
            return string.Join(" AND ",new[]{clause.Sql}.Concat(validity).Where(part=>part.Length>0));
        }

        /// <summary>表单条件只读当前关系来源的持久标量，禁止凭据和超出目标整数范围的值。</summary>
        private string SingleControlFormColumn(TableMetadata table,string owner,HashSet<string> validity,
            string variable,string expectedType,ISet<string>? fields=null,string? projectionAlias=null)
        {
            const string start="formParameters.";
            if(!variable.StartsWith(start,StringComparison.Ordinal))
                throw new InvalidOperationException("名称控件变量必须来自固定表单字段、父键或账号。");
            var path=variable[start.Length..].Split('.');
            if(path.Length!=1&&(path.Length!=2||path[0]!=MetadataNames.Camel(table.TableName)))
                throw new InvalidOperationException("名称控件表单条件必须取当前关系来源记录。");
            var column=columns[table.Id].Values.SingleOrDefault(field=>MetadataNames.Camel(field.ColumnName)==path[^1]);
            if(column is null||!GlobalPhysicalRelationValue.IsScalar(storage[table.Id],table,column))
                throw new InvalidOperationException("名称控件表单条件缺少真实非凭据物理字段。");
            var type=FixedConditionSqlPlan.Type(column.LogicalType);
            if(type!=expectedType&&!(type is "int32" or "int64"&&expectedType is "int32" or "int64")&&
                !(type is "date" or "datetime"&&expectedType is "date" or "datetime"))
                throw new InvalidOperationException("名称控件表单值与目标逻辑类型不一致。");
            Use(table,column);Touch(owner,column.ColumnName);
            fields?.Add(column.ColumnName);
            var expression=(projectionAlias??owner)+"."+Q(column.ColumnName);
            if(type=="int64"&&expectedType=="int32")
                validity.Add("("+expression+" IS NULL OR "+expression+" BETWEEN -2147483648 AND 2147483647)");
            return expression;
        }
    }
}
