using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

/// <summary>业务和附件跨表父级使用同一份目标页条件证明。</summary>
public static class CrossTableParentCondition
{
    /// <summary>目标页的原账号条件只绑定真实物理归属列；父参数与精确页继续不作为固定目标。</summary>
    public static (bool Allowed,FixedConditionSqlPlan? Plan) Compile(MetadataCatalog catalog,TableMetadata table,PageMetadata page,MetadataStoragePlan storage)
    {
        var result=Compile(catalog,table,page,storage,allowLogical:false);
        return(result.Allowed,result.Plan);
    }

    /// <summary>R0 文件父级可使用逻辑目标范围；执行端必须以候选读计划解析该范围。</summary>
    public static (bool Allowed,FixedConditionSqlPlan? Plan,bool HasLogical) Compile(MetadataCatalog catalog,TableMetadata table,
        PageMetadata page,MetadataStoragePlan storage,bool allowLogical,ColumnMetadata? multiParent=null)
    {
        if(page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&exact.ValueKind!=JsonValueKind.Null&&!string.IsNullOrWhiteSpace(exact.ToString()))return(false,null,false);
        var physical=storage.PhysicalColumns.Where(column=>GlobalPhysicalRelationValue.IsScalar(storage,table,column)&&
            !CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,column.ColumnName))
            .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.Ordinal);
        var types=new Dictionary<string,string>(physical,StringComparer.Ordinal);
        if(allowLogical&&table.IsRongyu==0)
            foreach(var column in RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id))
                .Where(column=>CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,column.ColumnName)))
                types.TryAdd(column.ColumnName,column.LogicalType);
        var syntax=FixedConditionPlan.Compile(page.WhereSql,types.Keys);
        var logical=syntax.Fields.Any(field=>!physical.ContainsKey(field));
        var conditions=new List<FixedConditionTerm>();
        foreach(var term in syntax.Terms)
        {
            var variables=term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable).ToArray();
            if(variables.Length==0){conditions.Add(term);continue;}
            if(variables.Any(value=>MultiTargetParentScope.IsParameter(value.Text)))
            {
                if(multiParent?.ColumnName==term.Field&&term.Comparison==FixedConditionComparison.Equal&&term.Values.Count==1)continue;
                return(false,null,false);
            }
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1)
                throw new NotSupportedException("文件跨表目标只支持已证明的账号等值条件。");
            var variable=variables[0].Text;
            if(term.Field=="yz_project_id"&&variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))continue;
            if(term.Field=="yz_project_id"||!AccountConditionVariables.IsPageField(variable,term.Field)||
                !physical.TryGetValue(term.Field,out var type)||type is not("string" or "int32" or "int64"))
                throw new NotSupportedException("文件跨表目标包含未证明的上下文范围。");
            conditions.Add(term);
        }
        if(conditions.Count==0)return(true,null,logical);
        var normalized=FixedConditionPlan.Compile(string.Join(" AND ",conditions.Select(term=>term.ToDefinition())),types.Keys);
        return(true,FixedConditionSqlPlan.Compile(normalized,types),logical);
    }
}