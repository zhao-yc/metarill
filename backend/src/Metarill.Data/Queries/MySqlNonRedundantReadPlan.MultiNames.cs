using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        /// <summary>派生多值名称使用独立固定别名域，联接保持在目标子查询内，不把来源扩成多行。</summary>
        private (string Sql,string Type) DerivedMultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,TableMetadata target,ColumnMetadata value)
        {
            if(refreshFields is not null||table.IsRongyu!=0||target.IsRongyu!=0||relation.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase"))throw new NotSupportedException("派生多选名称要求R0来源、R0目标及原多选／数据库复选，物理缓存与其它控件尚未接线。");
            var nested=new Builder(catalog,target.Id,matching:true,rootAlias:"multi_value",aliasPrefix:"multi_name_").Compile([value.ColumnName],[]);
            if(nested.Types[value.ColumnName]!="string"||nested.MultiProjections.Count>0||nested.Crosses.Count>0||!nested.Origins.TryGetValue(value.ColumnName,out var valueOrigin)||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(valueOrigin.Column))throw new NotSupportedException("多选派生名称必须落到已证明的非凭据单值字符串，不能借数组或级联路径绕过。");
            var prefix=" FROM "+Q(target.TableName)+" multi_value";
            if(!nested.From.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("多选名称别名域与固定目标不一致。");
            var targetJoins=nested.From[prefix.Length..];var name=nested.Expression(value.ColumnName);var scope=nested.Scope;
            var source=owner+"."+Q(relation.ColumnName);var array=Array(source);
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG("+name+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON CAST(multi_value.`id` AS CHAR)=multi_ids.member_id"+(scope.Length==0?"":" AND "+scope)+targetJoins+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(value.ColumnName),scope,targetJoins,name);
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(scope.Length==0?"":" AND "+scope)+" AND "+name+" LIKE {parameter} ESCAPE '!')";
            origin=valueOrigin;return(expression,"string");
        }
    }
}
