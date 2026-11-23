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
            PageMetadata page,FixedConditionPlan condition,CascadeCacheOwner cacheOwner)
        {
            var members=columns[target.Id];
            var tree=SelfTreeConditionFields(catalog,target,page,members.Values.ToArray(),condition.Fields);
            var cross=condition.Fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(target,members.Values.ToArray(),field))
                .ToHashSet(StringComparer.Ordinal);
            var bindings=(cross.Count==0?[]:CrossMembershipBindings(catalog,target,page,cross))
                .ToDictionary(binding=>binding.SourceColumnId,StringComparer.Ordinal);
            bool Logical(string field)=>tree.Contains(field)||cross.Contains(field);
            if(condition.Fields.Any(field=>!Logical(field)&&!GlobalPhysicalRelationValue.IsScalar(storage[target.Id],target,Column(target,field))))
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
                predicates.Add(PhysicalAccountCacheCondition(target,alias,definition,cacheOwner,allowFixedOnly:true));
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
                Use(target,column);Touch(alias,field);
                return alias+"."+Q(field);
            }
        }


        /// <summary>冗余字典按实际页面验证物理条件，实时读取绑定请求账号，维护绑定来源行账号与表单值。</summary>
        private string ProjectStoredListCondition(TableMetadata source,string owner,ColumnMetadata relation,TableMetadata target,
            string targetAlias,PageMetadata sourcePage,PageMetadata page,string? sourceParentId,FixedConditionPlan control,
            CascadeCacheOwner? cacheOwner=null)
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
                parent=MultiTargetParentScope.CompileListPage(catalog,target,terms);
            }
            if (parent is {Logical:true} || (parent is null)!=(sourceParentId is null) ||
                page.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0)
                throw new InvalidOperationException("原下拉的冗余字典需要已证明的物理范围和同一实际父键。");

            if (terms.Any(term=>!GlobalPhysicalRelationValue.IsScalar(storage[target.Id],target,Column(target,term.Field))))
                throw new InvalidOperationException("冗余字典范围只可使用真实的非凭据物理列。");
            // 普通字典已由物理联接限定项目。全局字典只有声明了项目谓词才按当前身份限定。
            static bool Project(FixedConditionTerm term)=>term.Field=="yz_project_id"&&term.Comparison==FixedConditionComparison.Equal&&
                term.Values is [{Kind:FixedConditionValueKind.Variable,Text:var variable}]&&
                variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase);
            var scoped=terms.Where(term=>!term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                MultiTargetParentScope.IsParameter(value.Text))&&!Project(term));
            var validity=new HashSet<string>(StringComparer.Ordinal);
            if(global&&terms.Any(Project))
                validity.Add(Scope(target,targetAlias,includeDeleted:true));
            if(cacheOwner is {} row)
            {
                var account=scoped.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                    AccountConditionVariables.IsSupported(value.Text))).ToArray();
                if(account.Length>0)
                {
                    var accountPage=new PageMetadata{Id=page.Id,ConfigTableId=page.ConfigTableId,Extra=page.Extra,
                        WhereSql=string.Join(" AND ",account.Select(term=>term.ToDefinition()))};
                    validity.Add(PhysicalAccountCacheCondition(target,targetAlias,accountPage,row,allowFixedOnly:true));
                    scoped=scoped.Except(account);
                }
            }
            var syntax=FixedConditionPlan.Compile(string.Join(" AND ",scoped.Select(term=>term.ToDefinition())),members.Keys);
            var plan=FixedConditionSqlPlan.Compile(syntax,syntax.Fields.ToDictionary(field=>field,field=>Column(target,field).LogicalType));
            var clause=plan.RenderRowProjection(ConditionParameterPrefix("mt_stored_list_"+targetAlias),
                Projection,(variable,type)=>AccountConditionVariables.IsSupported(variable)
                    ?null:SingleControlFormColumn(source,owner,validity,variable,type),deferAccount:cacheOwner is null);
            foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
            if (parent is not null)
            {
                var parentSource=parent.SingleSource(catalog,source,relation,sourcePage,
                    allowLogicalListForm:!GlobalCrossTableTarget.Declared(source)&&!global);
                if (parentSource.Column.Id!=sourceParentId)
                    throw new InvalidOperationException("冗余字典选项与来源记录没有相同的已发布父范围。");
                var sourceParent=SingleSourceParentValue(source,owner,parentSource);
                validity.Add(SinglePhysicalParentCondition(source,owner,targetAlias,parent,parentSource.Column,sourceParent:sourceParent));
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
