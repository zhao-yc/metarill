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
            var pageParent=MultiParent(catalog,page);
            var controlParent=MultiTargetParentScope.Compile(catalog,target,control);
            if(controlParent is {Logical:true}||pageParent is not null&&controlParent is not null&&pageParent.Target.Id!=controlParent.Target.Id)
                throw new InvalidOperationException("名称副本的目标页与控件父范围不一致。");
            var parent=pageParent??controlParent;
            var binding=sourcePage is null?parent?.SingleSource(catalog,table,foreign):
                parent?.SingleSource(catalog,table,foreign,sourcePage);
            return JoinScopedSingleCache(table,owner,foreign,target,source,page,cacheOwner,parent,binding,control);
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
            string variable,string expectedType)
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
            var expression=owner+"."+Q(column.ColumnName);
            if(type=="int64"&&expectedType=="int32")
                validity.Add("("+expression+" IS NULL OR "+expression+" BETWEEN -2147483648 AND 2147483647)");
            return expression;
        }
    }
}
