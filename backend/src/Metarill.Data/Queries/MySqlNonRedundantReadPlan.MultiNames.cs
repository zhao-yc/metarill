using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        /// <summary>派生多值名称使用独立固定别名域，联接保持在目标子查询内，不把来源扩成多行。</summary>
        private (string Sql,string Type) DerivedMultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,TableMetadata target,ColumnMetadata value)
        {
            if(table.IsRongyu is not(0 or 1)||target.IsRongyu!=0||relation.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase"))throw new NotSupportedException("派生多选名称要求已证明来源、R0目标及原多选／数据库复选；其它组合尚未接线。");
            var nested=new Builder(catalog,target.Id,matching:true,rootAlias:"multi_value",aliasPrefix:"multi_name_",trackReverse:refreshFields is not null).Compile([value.ColumnName],[]);
            if(nested.Types[value.ColumnName]!="string"||nested.MultiProjections.Count>0||nested.Crosses.Count>0||!nested.Origins.TryGetValue(value.ColumnName,out var valueOrigin)||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(valueOrigin.Column))throw new NotSupportedException("多选派生名称必须落到已证明的非凭据单值字符串，不能借数组或级联路径绕过。");
            var prefix=" FROM "+Q(target.TableName)+" multi_value";
            if(!nested.From.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("多选名称别名域与固定目标不一致。");
            var targetJoins=nested.From[prefix.Length..];var name=nested.Expression(value.ColumnName);var scope=nested.Scope;
            var source=owner+"."+Q(relation.ColumnName);var array=Array(source);
            if(refreshFields is not null)
            {
                if(table.Id!=root.Id||owner!=rootAlias)throw new NotSupportedException("派生多选缓存只允许根数组，不能把数组文本作为中间单值路径。");Use(table,relation);foreach(var reference in nested.PhysicalReferences)references.TryAdd((reference.Table,reference.Column.Name),reference);
                var from=" FROM "+Q(table.TableName)+" "+owner;var rootFields=nested.PhysicalReferences.Where(p=>p.Table==target.TableName).Select(p=>p.Column.Name).ToHashSet(StringComparer.Ordinal);var dependency=new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,rootFields}};
                // 删除树目标时仍按源数组找到消费者，不依赖目标当前还存在。
                customReverse.Add(new(target.TableName,from,ArrayMember(array,"@target_text"),"predicate",dependency));
                foreach(var reverse in nested.Reverse)
                {
                    var match=reverse.ForeignType=="string"?"BINARY "+reverse.ForeignExpression+"=BINARY @target_text":reverse.ForeignExpression+"=@target_id";var predicate="((SELECT 1"+reverse.From+" WHERE "+nested.Scope+" AND "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+" AND "+match+" LIMIT 1) IS NOT NULL)";var fields=reverse.Outputs.Values.SelectMany(v=>v).ToHashSet(StringComparer.Ordinal);customReverse.Add(new(reverse.TargetTable,from,predicate,"predicate",new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,fields}}));
                }
            }
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG("+name+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON CAST(multi_value.`id` AS CHAR)=multi_ids.member_id"+(scope.Length==0?"":" AND "+scope)+targetJoins+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(value.ColumnName),scope,targetJoins,name);
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(scope.Length==0?"":" AND "+scope)+" AND "+name+" LIKE {parameter} ESCAPE '!')";
            origin=valueOrigin;return(expression,"string");
        }
    }
}
