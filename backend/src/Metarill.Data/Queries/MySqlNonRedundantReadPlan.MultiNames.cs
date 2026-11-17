using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        /// <summary>派生多值名称使用独立固定别名域，联接保持在目标子查询内，不把来源扩成多行。</summary>
        private (string Sql,string Type) DerivedMultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,TableMetadata target,ColumnMetadata value,PageMetadata? publishedPage)
        {
            if(table.IsRongyu is not(0 or 1)||target.IsRongyu!=0||relation.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase"))throw new NotSupportedException("派生多选名称要求已证明来源、R0目标及原多选／数据库复选；其它组合尚未接线。");
            var global=GlobalCrossTableTarget.Declared(target);
            var integerLabel=MultiNameStorageColumns.IsDerivedIntegerLabel(target,value,storage[target.Id]);
            if(value.LogicalType!="string"&&!integerLabel)throw new NotSupportedException("多值派生名称类型未支持。");
            // 全局单值路径已证明字符串键的二进制读取；这里不是仅允许int64键的精确管理匹配用途。
            var prefixForNames=publishedPage is null?"multi_name_":aliasPrefix+"global_array"+publishedArrayScopeCount+++"_";
            var nested=new Builder(catalog,target.Id,matching:!global,rootAlias:"multi_value",aliasPrefix:prefixForNames,
                trackReverse:refreshFields is not null,candidatePage:publishedPage).Compile([value.ColumnName],[]);
            foreach(var pair in nested.Constants)crossConstants.Add(pair.Key,pair.Value);
            if(trackReverse&&nested.RequiresPublishedProjectIdentity)publishedArrayIdentity=true;
            if(nested.Types[value.ColumnName]!=value.LogicalType||nested.MultiProjections.Count>0||nested.Crosses.Count>0||
                !nested.Origins.TryGetValue(value.ColumnName,out var valueOrigin)||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(valueOrigin.Column))
                throw new NotSupportedException("多选派生名称必须落到已证明的同类型非凭据单值终点。");
            var prefix=" FROM "+Q(target.TableName)+" multi_value";
            if(!nested.From.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("多选名称别名域与固定目标不一致。");
            var targetJoins=nested.From[prefix.Length..];var valueSql=nested.Expression(value.ColumnName);var scope=nested.Scope;
            // 聚合、筛选、分批当前读和维护按同一文本名称计算，终点联接仍保持强类型。
            var name=integerLabel?"CAST("+valueSql+" AS CHAR)":valueSql;
            var source=owner+"."+Q(relation.ColumnName);var array=Array(source);
            if(refreshFields is not null)
            {
                if(table.Id!=root.Id||owner!=rootAlias)throw new NotSupportedException("派生多选缓存只允许根数组，不能把数组文本作为中间单值路径。");Use(table,relation);foreach(var reference in nested.PhysicalReferences)references.TryAdd((reference.Table,reference.Column.Name),reference);
                var from=" FROM "+Q(table.TableName)+" "+owner;var rootFields=nested.PhysicalReferences.Where(p=>p.Table==target.TableName).Select(p=>p.Column.Name).ToHashSet(StringComparer.Ordinal);var dependency=new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,rootFields}};
                // 删除树目标时仍按源数组找到消费者，不依赖目标当前还存在。
                customReverse.Add(new(target.TableName,from,ArrayMember(array,"@target_text"),"predicate",dependency));
                foreach(var reverse in nested.Reverse)
                {
                    var match=reverse.TargetPredicate;var predicate="((SELECT 1"+reverse.From+" WHERE "+nested.Scope+" AND "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+" AND "+match+" LIMIT 1) IS NOT NULL)";var fields=reverse.Outputs.Values.SelectMany(v=>v).ToHashSet(StringComparer.Ordinal);customReverse.Add(new(reverse.TargetTable,from,predicate,"predicate",new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,fields}}));
                }
            }
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG("+name+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON "+(global?"CAST(CAST(multi_value.`id` AS CHAR) AS BINARY)=CAST(multi_ids.member_id AS BINARY)":"CAST(multi_value.`id` AS CHAR)=multi_ids.member_id")+(scope.Length==0?"":" AND "+scope)+targetJoins+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(value.ColumnName),scope,targetJoins,name);
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(scope.Length==0?"":" AND "+scope)+" AND "+name+" LIKE {parameter} ESCAPE '!')";
            if(global||integerLabel&&relation.ControlKind=="MultiCheckBoxDatabase")multiPredicates[expression]="((SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+(scope.Length==0?"":" AND "+scope)+" AND "+name+" LIKE {parameter} ESCAPE '!' LIMIT 1) IS NOT NULL)";
            origin=valueOrigin;return(expression,"string");
        }
    }
}
