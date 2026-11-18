using System.Collections.ObjectModel;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Data.Queries;

/// <summary>隐藏原始ID位只供存储核验，不能混入公开VO；有效性表达式供同范围COUNT／统计复用。</summary>
internal sealed record MySqlCrossTableProjection(CrossTableSparseSelection Selection,IReadOnlyDictionary<string,string> RawSlots,string Validity,string Reachable,string? SharedGroupKey=null,bool RequireRowValidity=false);

public static partial class MySqlNonRedundantReadCompiler
{
    private sealed partial class Builder
    {
        private readonly Dictionary<string,NonRedundantCrossTableScope> crossTables;
        private readonly Dictionary<string,MySqlCrossTableProjection> crossProjections=new(StringComparer.Ordinal);
        private readonly Dictionary<string,IReadOnlyDictionary<string,(string Sql,string Type)>> crossValues=new(StringComparer.Ordinal);
        private readonly Dictionary<string,object?> crossConstants=new(StringComparer.Ordinal);
        private sealed record CrossLevel(TableMetadata Table,ColumnMetadata Name,ColumnMetadata? Parent,NonRedundantCrossTableTargetScope Scope,PageMetadata Page,IReadOnlySet<string> LogicalFields);

        /// <summary>一个控件只生成一条从末层向根的线性联接链，全部用途复用逻辑字段表达式。</summary>
        private (string Sql,string Type) CrossValue(TableMetadata table,string owner,ColumnMetadata source,CrossTableSparseSelection selection,NonRedundantCrossTableScope binding,string name,bool labelPath=false,bool scopeOnly=false,CascadeCacheOwner? cacheOwner=null)
        {
            origin=null;var key=owner+":"+source.Id;
            if(!crossValues.TryGetValue(key,out var values))
            {
                if(!labelPath&&!scopeOnly&&(table.Id!=root.Id||owner!=rootAlias))
                    throw new NotSupportedException("跨表逻辑投影要求当前实体或已证明的嵌套候选。");
                if(GlobalCrossTableTarget.Declared(table))
                {
                    if(labelPath&&!scopeOnly&&(SelectedMultiPage is null||table.Id!=root.Id||owner!=rootAlias))
                        throw new NotSupportedException("全局递归名称要求实际绑定的多值目标页。");
                    GlobalDerivedRelationSource.RequireTable(catalog,table);
                }
                else if(!columns[table.Id].TryGetValue("yz_project_id",out var sourceProject)||!storage[table.Id].IsPhysical(sourceProject))
                    throw new NotSupportedException("项目内跨表逻辑投影要求真实物理项目列。");
                var rootDefinition=Definition(source);if(HasExtraScope(rootDefinition))throw new NotSupportedException("跨表额外范围不能在固定读取中忽略。");
                var entries=rootDefinition.GetProperty("levels").EnumerateArray().ToArray();if(entries.Length!=binding.Levels.Count)throw new InvalidOperationException("跨表目标页绑定与固定层数不符。");
                var levels=new List<CrossLevel>();
                for(var index=0;index<entries.Length;index++)
                {
                    var entry=entries[index];if(HasExtraScope(entry))throw new NotSupportedException("跨表层级额外范围不能忽略。");
                    var target=tables.Values.SingleOrDefault(item=>item.TableName==Value(entry,"table"))??throw new InvalidOperationException("跨表目标不属于当前发布项目。");
                    if(target.IsRongyu is not(0 or 1))throw new NotSupportedException("跨表读取要求普通实体目标。");ValidateTarget(target);
                    var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==binding.Levels[index].PageId&&item.ConfigTableId==target.Id)??throw new InvalidOperationException("跨表目标页与固定目标表不符。");
                    if(page.YzProjectId.Length>0&&page.YzProjectId!=catalog.YzProjectId||CrossText(page,"exactDataManageMatchField").Length>0)throw new NotSupportedException("跨表目标页的项目／精确权限范围尚未接线。");
                    var global=GlobalCrossTableTarget.Declared(target);if(!global&&(!columns[target.Id].TryGetValue("yz_project_id",out var project)||!storage[target.Id].IsPhysical(project)))throw new InvalidOperationException("跨表目标缺少真实物理项目范围。");
                    var display=Column(target,Value(entry,"nameField") is {Length:>0} field?field:"name");
                    if(display.ColumnName=="id"||display.LogicalType.ToLowerInvariant() is not("string" or "int32" or "int64")||ExactDataManageValues.IsSensitiveField(display.ColumnName))
                        throw new NotSupportedException("跨表名称须为已证明的非凭据单值。");
                    if(global)GlobalCrossTableTarget.RequireCrossTableCandidate(catalog,target,page,display);
                    if(target.IsRongyu==0)
                    {
                        var proof=new Builder(catalog,target.Id,matching:!global).Compile([display.ColumnName],[]);if(proof.Types[display.ColumnName]!=display.LogicalType||proof.MultiProjections.Count>0||proof.Crosses.Count>0||!proof.Origins.TryGetValue(display.ColumnName,out var terminal)||ExactDataManageValues.IsSensitiveField(terminal.Column))throw new NotSupportedException("跨表派生名称不能借数组、级联或凭据绕过固定终点证明。");
                    }
                    else if(!global&&!CrossPhysical(target,display))throw new NotSupportedException("冗余目标联接名称仍需独立物理缓存证明。");
                    ColumnMetadata? parent=null;
                    if(index>0)
                    {
                        parent=Column(target,Value(entry,"parentField"));
                        if(target.Id==table.Id&&selection.Slots.Any(slot=>slot.ColumnName==parent.ColumnName))throw new InvalidOperationException("来源角色的父键必须是独立物理外键，不能使用稀疏选择位。");
                        if(!CrossPhysical(target,parent)||parent.LogicalType.ToLowerInvariant() is not("string" or "int64")||parent.ColumnName is "id" or "yz_project_id" or "insert_manager_id" or "update_manager_id" or "insert_user_id" or "update_user_id"||parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=levels[^1].Table.Id)throw new InvalidOperationException("跨表父键不是上一层的已证明物理外键。");
                    }
                    var slot=columns[table.Id][selection.Slots[index].ColumnName];if(slot.LinkConfigTableId.Length>0&&slot.LinkConfigTableId!=target.Id)throw new InvalidOperationException("跨表来源ID位与目标表声明不一致。");
                    var label=selection.Slots[index].ColumnName[..^3]+"_name";if(columns[table.Id].TryGetValue(label,out var declared)&&declared.LogicalType.ToLowerInvariant()!="string")throw new InvalidOperationException("跨表来源显示字段类型与逻辑名称不符。");
                    var logical=RequireCrossCondition(target,page,binding.Levels[index].Condition);
                    if(global&&(logical.Count>0||scopeOnly&&cacheOwner is not null))scopedGlobalCrossTarget=true;
                    levels.Add(new(target,display,parent,binding.Levels[index],page,logical));
                }
                if(trackReverse&&(labelPath||scopeOnly))
                    foreach(var slot in selection.Slots)
                    {
                        Use(table,columns[table.Id][slot.ColumnName]);
                        Touch(owner,slot.ColumnName);
                    }
                var raw=selection.Slots.ToDictionary(slot=>slot.ColumnName,slot=>owner+"."+Q(slot.ColumnName),StringComparer.Ordinal);var selected=selection.Slots.Select(slot=>Nonempty(raw[slot.ColumnName])).ToArray();
                var valid=string.Join(" AND ",selection.Slots.Select(slot=>slot.LogicalType=="int64"?"("+raw[slot.ColumnName]+" IS NULL OR "+raw[slot.ColumnName]+">0)":"("+raw[slot.ColumnName]+" IS NULL OR BINARY "+raw[slot.ColumnName]+"=BINARY '' OR BINARY "+raw[slot.ColumnName]+"=BINARY "+PositiveText(raw[slot.ColumnName])+")"));
                valid="("+valid+") AND ("+string.Join("+",selected.Select(item=>"(CASE WHEN "+item+" THEN 1 ELSE 0 END)"))+")<=1";
                var targetAliases=new string[levels.Count];
                var rowConditions=new string?[levels.Count];
                for(var index=levels.Count-1;index>=0;index--)
                {
                    var level=levels[index];var alias="cross"+aliases.Count;aliases.Add("cross:"+key+":"+index,alias);targetAliases[index]=alias;
                    var sourceText="CAST("+raw[selection.Slots[index].ColumnName]+" AS CHAR)";var effective="NULLIF(BINARY "+sourceText+",BINARY '')";
                    if(index+1<levels.Count){var parent=levels[index+1].Parent!;var parentValue=targetAliases[index+1]+"."+Q(parent.ColumnName);var parentText=parent.LogicalType.ToLowerInvariant()=="string"?PositiveText(parentValue):"CASE WHEN "+parentValue+">0 THEN CAST("+parentValue+" AS CHAR) END";effective="COALESCE("+effective+","+parentText+")";}
                    if(trackReverse&&(labelPath||scopeOnly))
                    {
                        Touch(alias,"id");
                        if(level.Parent is {} parent)Touch(alias,parent.ColumnName);
                        reverse.Add(alias,(level.Table.TableName," FROM "+Q(root.TableName)+" "+rootAlias+string.Concat(joins),effective,"string"));
                    }
                    var scope=Scope(level.Table,alias,projectScoped:!GlobalCrossTableTarget.Declared(level.Table));
                    if(level.Scope.Condition is{} condition&&level.LogicalFields.Count==0)
                    {
                        var conditionSql=CascadeLabelCondition(level.Table,level.Page,condition,alias,
                            "mt_cross_scope_"+(multiLabel?aliasPrefix:"")+alias,cacheOwner);
                        // 缓存可见性从外层已锁来源行取值，校验有效路径时再使用它。
                        if((cacheOwner??cascadeCacheOwner) is not null&&condition.Variables.Count>0)
                            rowConditions[index]=" AND "+conditionSql;
                        else if(conditionSql.Length>0)scope+=(scope.Length==0?"":" AND ")+conditionSql;
                    }
                    var join=" LEFT JOIN "+Q(level.Table.TableName)+" "+alias+" ON BINARY CAST("+alias+".`id` AS CHAR)=BINARY "+effective+(scope.Length==0?"":" AND "+scope);
                    joins.Add(join);
                    if(refreshFields is not null)maintenanceJoins.Add((alias,join));
                    // 子路径联接使用本层别名，先发布本层联接，再用完整子路径限制本层是否可见。
                    if(level.LogicalFields.Count>0)rowConditions[index]=" AND "+NestedCrossCondition(level,alias,cacheOwner);
                }
                var paths=Enumerable.Range(0,levels.Count).Select(deepest=>"("+selected[deepest]+" AND "+string.Join(" AND ",targetAliases.Take(deepest+1).Select((alias,index)=>alias+".`id` IS NOT NULL"+rowConditions[index]))+")").ToArray();
                var requireVisible=scopeOnly||GlobalCrossCandidate||ScopedRootControls.Contains(source.Id)||levels.Any(level=>level.Scope.Condition?.Variables.Count>0||level.LogicalFields.Count>0);
                var result=new Dictionary<string,(string Sql,string Type)>(StringComparer.OrdinalIgnoreCase);
                for(var index=0;index<levels.Count;index++)
                {
                    var slot=selection.Slots[index];var resolved=targetAliases[index]+".`id`";if(slot.LogicalType=="string")resolved="CAST("+resolved+" AS CHAR)";
                    var ancestors=index+1<levels.Count?string.Join(" OR ",paths.Skip(index+1)):"FALSE";
                    // 账号范围之外的目标不能借直接选择的物理ID参与公开投影、匹配或统计。
                    var direct=requireVisible?paths[index]:selected[index];
                    var idExpression="CASE WHEN "+direct+" THEN "+raw[slot.ColumnName]+" WHEN "+ancestors+" THEN "+resolved+" END";
                    // 来源varchar与目标CAST可能采用不同默认排序规则；逻辑ID显式保持二进制精确比较。
                    if(slot.LogicalType=="string")idExpression="CONVERT(("+idExpression+") USING utf8mb4) COLLATE utf8mb4_bin";
                    result.Add(slot.ColumnName,(idExpression,slot.LogicalType));
                    if(trackReverse&&(labelPath||scopeOnly)&&levels[index].Table.IsRongyu==1)Touch(targetAliases[index],levels[index].Name.ColumnName);
                    var label=levels[index].Table.IsRongyu==0?Resolve(levels[index].Table,targetAliases[index],levels[index].Name.ColumnName).Sql:targetAliases[index]+"."+Q(levels[index].Name.ColumnName);
                    var text=MySqlMultiValueLabel.Text(label,levels[index].Name.LogicalType);
                    result.Add(slot.ColumnName[..^3]+"_name",("CASE WHEN "+string.Join(" OR ",paths.Skip(index))+" THEN "+text+" END","string"));
                }
                (scopeOnly?scopedCrossProjections:crossProjections).Add(key,new(selection,new ReadOnlyDictionary<string,string>(raw),valid,"("+string.Join(" OR ",paths)+")"));values=new ReadOnlyDictionary<string,(string Sql,string Type)>(result);crossValues.Add(key,values);
            }
            // 有条件的整条路径不能冒充单一物理来源；派生名称编译的临时origin不得泄漏到其它输出。
            origin=null;return values[name];
        }
        /// <summary>旧父键允许前导零，但必须是正整数且不越界；按文本规范化，不经浮点或宽松数值转换。</summary>
        private static string PositiveText(string expression)
        {
            var normalized="TRIM(LEADING '0' FROM "+expression+")";return "CASE WHEN "+expression+" REGEXP '^[0-9]+$' AND CHAR_LENGTH("+normalized+") BETWEEN 1 AND 19 AND (CHAR_LENGTH("+normalized+")<19 OR BINARY "+normalized+"<=BINARY '9223372036854775807') THEN "+normalized+" END";
        }
        /// <summary>空文本与null是未选，空格不是规范ID，不依赖数据库忽略尾空格的比较。</summary>
        private static string Nonempty(string expression)=>"("+expression+" IS NOT NULL AND BINARY CAST("+expression+" AS CHAR)<>BINARY '')";
        /// <summary>名称回填和传播用相同来源行范围，目标归属及常量列加入反向依赖。</summary>
        private string AccountCacheScope(AccountScopedCrossCache account,TableMetadata target,string alias,string owner,bool render,
            TableMetadata? sourceTable=null,string? parameterPrefix=null,Func<string,string>? projection=null)
        {
            var predicates=new List<string>();
            foreach(var (targetColumn,sourceColumn) in account.Ownership)
            {
                Use(target,targetColumn);Touch(alias,targetColumn.ColumnName);
                Use(sourceTable??root,sourceColumn);
                if(!render)continue;
                // 文本和整数使用规范十进制字节，拒绝 NULL、前导零和相邻雪花 ID 的近似匹配。
                var sourceText="CAST("+owner+"."+Q(sourceColumn.ColumnName)+" AS CHAR)";
                var targetText="CAST("+alias+"."+Q(targetColumn.ColumnName)+" AS CHAR)";
                predicates.Add("(BINARY "+targetText+"=BINARY "+sourceText+" AND BINARY "+sourceText+"=BINARY "+PositiveText(sourceText)+")");
            }
            if(account.Constants is{} constants)
            {
                var clause=constants.RenderConstants(new MySqlDatabaseProvider(),parameterPrefix??"mt_cross_cache_"+alias,field=>
                {
                    if(projection is not null)return projection(field);
                    Use(target,Column(target,field));Touch(alias,field);
                    return alias+"."+Q(field);
                });
                if(render)
                {
                    foreach(var pair in clause.Parameters)crossConstants.Add(pair.Key,pair.Value);
                    predicates.Add(clause.Sql);
                }
            }
            return string.Join(" AND ",predicates);
        }

        /// <summary>真实物理标量与凭据排除共用发布类型，不把JSON或文件数组当作父键或名称。</summary>
        private bool CrossPhysical(TableMetadata table,ColumnMetadata column)=>storage[table.Id].IsPhysical(column)&&column.IsVirtualColumn==0&&!CrossEnabled(column,"isJoinColumn")&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CheckBoxList" or "MultiList")&&column.LogicalType.ToLowerInvariant() is not("json" or "binary");
        /// <summary>完整元数据中的目标固定条件必须与绑定相符；精简发布目录也必须提供冻结的常量绑定。</summary>
        private IReadOnlySet<string> RequireCrossCondition(TableMetadata table,PageMetadata page,FixedConditionSqlPlan? binding,ColumnMetadata? multiParent=null)
        {
            var physical=columns[table.Id].Values.Where(column=>CrossPhysical(table,column))
                .ToDictionary(column=>column.ColumnName,column=>column.LogicalType,StringComparer.OrdinalIgnoreCase);
            var syntax=FixedConditionPlan.Compile(page.WhereSql,columns[table.Id].Keys);
            var logical=syntax.Fields.Where(field=>CrossTableStorageColumns.IsLogicalPathField(table,catalog.ConfigColumns,field))
                .ToHashSet(StringComparer.Ordinal);
            var types=new Dictionary<string,string>(physical,StringComparer.OrdinalIgnoreCase);
            foreach(var field in logical)types[field]=Column(table,field).LogicalType;
            binding?.RequirePhysicalTypes(types);
            var conditions=new List<FixedConditionTerm>();
            foreach(var term in syntax.Terms)
            {
                var variables=term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable).ToArray();
                if(variables.Length==0){conditions.Add(term);continue;}
                if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1)
                    throw new NotSupportedException("跨表目标的变量条件必须是物理列等值。");
                var variable=variables[0].Text;
                if(multiParent?.ColumnName==term.Field&&MultiTargetParentScope.IsParameter(variable))continue;
                if(term.Field=="yz_project_id"&&variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase))continue;
                if(term.Field=="yz_project_id"||!AccountConditionVariables.IsPageField(variable,term.Field)||
                    logical.Contains(term.Field)||!physical.TryGetValue(term.Field,out var physicalType)||
                    physicalType is not("string" or "int32" or "int64"))
                    throw new NotSupportedException("跨表目标的父上下文或未知账号范围不能忽略。");
                conditions.Add(term);
            }
            if(conditions.Count>0)
            {
                var expected=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(string.Join(" AND ",conditions.Select(term=>term.ToDefinition())),types.Keys),types);
                if(binding is null||!binding.SameConstants(expected))throw new InvalidOperationException("跨表目标固定条件缺失或与发布目录不一致。");
            }
            if(binding?.Variables.Count>0&&conditions.Count==0)
                throw new InvalidOperationException("跨表账号条件必须来自完整发布页。");
            return logical;
        }
        /// <summary>沿原整数1／布尔true合同判断开关，不额外解释文本true为启用。</summary>
        private static bool CrossEnabled(ColumnMetadata column,string key)=>column.Extra.TryGetValue(key,out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
        /// <summary>仅从记录扩展读取固定标量，不把表／页面元数据转成字段或SQL。</summary>
        private static string CrossText(MetadataRecord record,string key)=>record.Extra.TryGetValue(key,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ToString():"";
        /// <summary>层级结构外的动态SQL／目标页映射仍保留原门禁，不默认为空范围。</summary>
        private static bool HasExtraScope(JsonElement element)=>new[]{"whereSql","where","sql","pageId","filterMatchMode"}.Any(key=>element.TryGetProperty(key,out _));
    }
}
