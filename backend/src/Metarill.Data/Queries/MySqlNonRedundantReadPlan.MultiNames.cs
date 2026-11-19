using Metarill.Core.Metadata;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        internal IReadOnlyDictionary<string,NonRedundantMultiTargetScope> ProjectMultiTargets { get; init; }=
            new Dictionary<string,NonRedundantMultiTargetScope>(StringComparer.Ordinal);

        /// <summary>运行目录重新核对当前页的控件目标，禁止外部请求指定页、换表或读取别的发布项目。</summary>
        private (PageMetadata? Source,PageMetadata? Target,bool Scope) ProjectMultiTarget(TableMetadata table,string alias,
            ColumnMetadata relation,TableMetadata target)
        {
            if(alias!=rootAlias||!ProjectMultiTargets.TryGetValue(relation.Id,out var binding))return default;
            var source=catalog.ConfigPages.Single(page=>page.Id==binding.SourcePageId&&page.ConfigTableId==table.Id);
            var selected=catalog.ConfigPages.Single(page=>page.Id==binding.TargetPageId&&page.ConfigTableId==target.Id);
            var overlay=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==source.Id&&field.ConfigColumnId==relation.Id)
                ?.Extra.GetValueOrDefault("controlDetail").ToString();
            string pageId;
            if(relation.ControlKind=="MultiCheckBoxDatabase")
            {
                pageId=DatabaseCheckboxDefinition.Parse(relation.ControlDetail).WithPageOverride(overlay).PageId;
                if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
            }
            else
            {
                pageId="";
                if(!string.IsNullOrWhiteSpace(overlay))
                {
                    using var definition=System.Text.Json.JsonDocument.Parse(overlay);
                    pageId=MultiPageId(definition.RootElement);
                }
                if(pageId.Length==0)pageId=MultiTargetNamePage(catalog,target,relation)?.Id??"";
            }
            if(table.IsRongyu!=0||target.IsRongyu!=0||GlobalCrossTableTarget.Declared(table)||
                GlobalCrossTableTarget.Declared(target)||pageId!=selected.Id||
                source.YzProjectId.Length>0&&source.YzProjectId!=catalog.YzProjectId||
                selected.YzProjectId.Length>0&&selected.YzProjectId!=catalog.YzProjectId)
                throw new InvalidOperationException("多值名称目标与当前页的发布配置不一致。");
            return(source,selected,binding.ApplyPageScope);
        }

        /// <summary>派生多值名称使用独立固定别名域，联接保持在目标子查询内，不把来源扩成多行。</summary>
        private (string Sql,string Type) DerivedMultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,TableMetadata target,
            ColumnMetadata value,PageMetadata? publishedPage,PageMetadata? selectedScope,PageMetadata? namePage,PageMetadata? sourcePage)
        {
            if(table.IsRongyu is not(0 or 1)||target.IsRongyu!=0||relation.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase"))throw new NotSupportedException("派生多选名称要求已证明来源、R0目标及原多选／数据库复选；其它组合尚未接线。");
            var global=GlobalCrossTableTarget.Declared(target);
            var scalarLabel=storage[target.Id].IsPhysical(value)
                ?MultiNameStorageColumns.IsPhysicalScalarLabel(table,relation,target,value,storage[target.Id])
                :MultiNameStorageColumns.IsDerivedScalarLabel(target,value,storage[target.Id]);
            if(value.LogicalType!="string"&&!scalarLabel)throw new NotSupportedException("多值派生名称类型未支持。");
            // 全局单值路径已证明字符串键的二进制读取；这里不是仅允许int64键的精确管理匹配用途。
            var prefixForNames=publishedPage is null&&publishedArrayScopeCount==0?"multi_name_":aliasPrefix+"array"+publishedArrayScopeCount+"_";
            publishedArrayScopeCount++;
            var parent=selectedScope is null?null:MultiParent(catalog,selectedScope);
            var sourceBinding=sourcePage is null?parent?.Source(catalog,table,relation):parent?.Source(catalog,table,relation,sourcePage);
            var sourceParent=sourceBinding?.Column;
            var logicalSource=sourceBinding?.LogicalPages.Count>0;
            var parentAlias=logicalSource?"mt_parent_input":owner;
            var sourceParentSql=logicalSource?LogicalRelationSourceParent(table,owner,sourceBinding!):null;
            if(sourceParent is not null&&!logicalSource){Use(table,sourceParent);Touch(owner,sourceParent.ColumnName);}
            var nested=new Builder(catalog,target.Id,matching:!global,rootAlias:"multi_value",aliasPrefix:prefixForNames,
                trackReverse:refreshFields is not null,candidatePage:selectedScope is null?publishedPage:null,multiLabel:true,cascadeLabelField:value.ColumnName,
                cascadeCacheOwner:refreshFields is null?null:new(table,owner))
            {
                SelectedMultiPage=selectedScope,
                MultiParentOwner=sourceParent is null?null:(new(table,parentAlias),sourceParent),
                CrossAliasPrefix=logicalSource?prefixForNames+"cross":"cross",
                MultiNamePathPage=namePage,
                AllowLiteralMultiScope=sourcePage is not null,
                GlobalCrossCandidate=global&&selectedScope is not null
            }.Compile([value.ColumnName],[]);
            nested.CopyParametersTo(crossConstants);
            if(nested.RequiresPublishedProjectIdentity)publishedArrayIdentity=true;
            var cascadeLabel=nested.CascadeLabelOutputs.Contains(value.ColumnName);
            nested.Origins.TryGetValue(value.ColumnName,out var valueOrigin);
            if(nested.Types[value.ColumnName]!=value.LogicalType||nested.MultiProjections.Count>0||
                !cascadeLabel&&(nested.Crosses.Count>0||valueOrigin is null||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(valueOrigin.Column)))
                throw new NotSupportedException("多选派生名称必须落到已证明的同类型非凭据单值终点。");
            var prefix=" FROM "+Q(target.TableName)+" multi_value";
            if(!nested.From.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("多选名称别名域与固定目标不一致。");
            var targetJoins=nested.From[prefix.Length..];var valueSql=nested.Expression(value.ColumnName);var scope=nested.Scope;
            // 行内查询使用已经还原的来源层位。批量回读通过专用参数别名绑定完全相同的值。
            var inlineScope=sourceParentSql is null?scope:scope.Replace(parentAlias+"."+Q(sourceParent!.ColumnName),"("+sourceParentSql+")",StringComparison.Ordinal);
            string ScopedName(string predicate)=>MySqlMultiValueLabel.Text(selectedScope is null?valueSql:"CASE WHEN "+predicate+" THEN "+valueSql+" END",value.LogicalType);
            // 反向查找需要包含刚失去可见性的节点，数组的前置联接也不能引用后生成的级联别名。
            var associationScope=selectedScope is null?scope:Scope(target,"multi_value",projectScoped:!global);
            // 聚合、筛选、分批当前读和维护按同一文本名称计算，终点联接仍保持强类型。
            var name=ScopedName(inlineScope);
            var source=owner+"."+Q(relation.ColumnName);var array=Array(source);
            if(refreshFields is not null)
            {
                if(table.Id!=root.Id||owner!=rootAlias)throw new NotSupportedException("派生多选缓存只允许根数组，不能把数组文本作为中间单值路径。");
                Use(table,relation);
                // 来源行的门店或商户变化和目标树变更一样会触发缓存重算。
                foreach(var field in nested.CascadeCacheOwnerFields)Use(table,Column(table,field));
                foreach(var reference in nested.PhysicalReferences)references.TryAdd((reference.Table,reference.Column.Name),reference);
                var from=" FROM "+Q(table.TableName)+" "+owner;var rootFields=nested.PhysicalReferences.Where(p=>p.Table==target.TableName).Select(p=>p.Column.Name).ToHashSet(StringComparer.Ordinal);var dependency=new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,rootFields}};
                // 删除树目标时仍按源数组找到消费者，不依赖目标当前还存在。
                customReverse.Add(new(target.TableName,from,ArrayMember(array,"@target_text"),"predicate",dependency));
                if(parent is not null&&sourceParent is not null)
                {
                    var parentFields=nested.PhysicalReferences.Where(item=>item.Table==parent.Parent.TableName).Select(item=>item.Column.Name).ToHashSet(StringComparer.Ordinal);
                    customReverse.Add(new(parent.Parent.TableName,from,owner+"."+Q(sourceParent.ColumnName),sourceParent.LogicalType,
                        new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,parentFields}}));
                }
                foreach(var reverse in nested.Reverse)
                {
                    var match=reverse.TargetPredicate;var predicate="((SELECT 1"+reverse.From+" WHERE "+associationScope+" AND "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+" AND "+match+" LIMIT 1) IS NOT NULL)";var fields=reverse.Outputs.Values.SelectMany(v=>v).ToHashSet(StringComparer.Ordinal);customReverse.Add(new(reverse.TargetTable,from,predicate,"predicate",new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,fields}}));
                }
            }
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG("+name+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON "+(global?"CAST(CAST(multi_value.`id` AS CHAR) AS BINARY)=CAST(multi_ids.member_id AS BINARY)":"CAST(multi_value.`id` AS CHAR)=multi_ids.member_id")+(associationScope.Length==0?"":" AND "+associationScope)+targetJoins+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(value.ColumnName),scope,targetJoins,ScopedName(scope),
                nested.CascadeCacheOwnerFields.Count==0?null:new(parentAlias,nested.CascadeCacheOwnerFields.Order(StringComparer.Ordinal).ToArray(),
                    sourceParentSql is null?null:new Dictionary<string,string>(StringComparer.Ordinal){{sourceParent!.ColumnName,sourceParentSql}}));
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(inlineScope.Length==0?"":" AND "+inlineScope)+" AND "+name+" LIKE {parameter} ESCAPE '!')";
            if(global||cascadeLabel||scalarLabel&&relation.ControlKind=="MultiCheckBoxDatabase")multiPredicates[expression]="((SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+(inlineScope.Length==0?"":" AND "+inlineScope)+" AND "+name+" LIKE {parameter} ESCAPE '!' LIMIT 1) IS NOT NULL)";
            // 维护报告展示发布的逻辑来源字段，物理列清单由嵌套路径单独记录。
            origin=valueOrigin??new(target.TableName,value.ColumnName);
            return(expression,"string");
        }

        /// <summary>从来源行的固定可见级联还原父键，所有同伴的冲突及不可见路径都必须拒绝。</summary>
        private string LogicalRelationSourceParent(TableMetadata table,string owner,MultiSourceParent source,CascadeCacheOwner? cacheOwner=null)
        {
            var nested=table.Id!=root.Id||owner!=rootAlias;
            if(table.IsRongyu!=0||cacheOwner is null&&(refreshFields is not null||nested&&trackReverse))
                throw new NotSupportedException("逻辑来源父要求非冗余来源，维护用途须证明实际缓存行归属。");
            var field=source.Column.ColumnName;
            var fields=new HashSet<string>(StringComparer.Ordinal){field};
            var bindings=source.LogicalPages.SelectMany(page=>CrossMembershipBindings(catalog,table,page,fields))
                .DistinctBy(binding=>binding.SourceColumnId,StringComparer.Ordinal).ToArray();
            string value;
            if(nested)
            {
                // 先联接中间来源的实际祖先，再用其父键限制后续目标。外层记录不承担中间路径的存储有效性。
                value=ScopedCrossField(table,owner,bindings.ToDictionary(binding=>binding.SourceColumnId,StringComparer.Ordinal),field,cacheOwner);
            }
            else
            {
                foreach(var binding in bindings)
                {
                    if(crossTables.TryGetValue(binding.SourceColumnId,out var existing)&&
                        (existing.Levels.Count!=binding.Levels.Count||existing.Levels.Zip(binding.Levels)
                            .Any(pair=>pair.First.PageId!=pair.Second.PageId||!SameSharedCondition(pair.First.Condition,pair.Second.Condition))))
                        throw new InvalidOperationException("关系来源父键的级联范围与当前读取页不一致。");
                    crossTables[binding.SourceColumnId]=binding;
                }
                value=Resolve(table,owner,field).Sql;
            }
            var visible=ValidScopedPaths(ScopedCrossPaths(owner,bindings.Select(binding=>binding.SourceColumnId)));
            return "CASE WHEN "+string.Join(" AND ",visible)+" THEN "+value+" END";
        }
    }
}
