using Metarill.Core.Metadata;
using Metarill.Core.Security;
namespace Metarill.Data.Queries;

public static partial class MySqlNonRedundantReadCompiler
{
    /// <summary>所有逻辑父范围都保留自己的目标页证明，物理父键由实际来源行读取。</summary>
    private static HashSet<string> LogicalSourceParentFields(MetadataCatalog catalog,TableMetadata table,MultiSourceParent source)
    {
        var fields=new HashSet<string>(StringComparer.Ordinal){source.Column.ColumnName};
        fields.UnionWith(source.AdditionalColumns.Where(column=>CrossTableStorageColumns.IsLogicalPathField(table,
            catalog.ConfigColumns,column.ColumnName)).Select(column=>column.ColumnName));
        return fields;
    }

    /// <summary>精简发布与实际查询都只接受同一目标的普通物理文本。</summary>
    private static bool PhysicalTextMultiLabel(MetadataStoragePlan policy,ColumnMetadata? label)=>
        label is {LogicalType:"string"}&&label.ControlKind is not ("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase")&&
        !ExactDataManageValues.IsSensitiveField(label.ColumnName)&&policy.IsPhysical(label);

    private sealed partial class Builder
    {
        internal IReadOnlyDictionary<string,NonRedundantMultiTargetScope> ProjectMultiTargets { get; init; }=
            new Dictionary<string,NonRedundantMultiTargetScope>(StringComparer.Ordinal);

        /// <summary>只有带项目归属的普通业务读取可以投影来源本身的多值和文件字段。</summary>
        private bool IsGlobalOwnedBusinessRead()=>
            HasGlobalRowProject(root)&&candidatePage is null&&SelectedMultiPage is null&&!GlobalCrossCandidate&&!matching;

        /// <summary>全局业务只读取本项目行的原多值 ID 及名称，字典候选与精确匹配仍只返回标量。</summary>
        private bool PublishedGlobalBusinessMultiField(string field)
        {
            if(!IsGlobalOwnedBusinessRead())return false;
            foreach(var relation in columns[root.Id].Values.Where(column=>column.ControlKind is "MultiSelectPage" or "MultiCheckBoxDatabase"&&
                column.ColumnName.EndsWith("_ids",StringComparison.Ordinal)))
            {
                var name=Value(Definition(relation),"nameField");
                if(name.Length==0)name="name";
                var display=relation.ColumnName[..^4]+"_"+(name.EndsWith('s')?name:name+"s");
                if(!field.Equals(relation.ColumnName,StringComparison.OrdinalIgnoreCase)&&
                    (name=="id"||!field.Equals(display,StringComparison.OrdinalIgnoreCase)))continue;
                var target=MultiTarget(relation);
                _=GlobalMultipleRelationTarget.Require(catalog,root,relation,target);
                return true;
            }
            return false;
        }

        /// <summary>运行目录重新核对当前页的控件目标，禁止外部请求指定页、换表或读取别的发布项目。</summary>
        private (PageMetadata? Source,PageMetadata? Target,bool Scope,FixedConditionPlan? Control,string? Name) ProjectMultiTarget(TableMetadata table,string alias,
            ColumnMetadata relation,TableMetadata target)
        {
            if(alias!=rootAlias||!ProjectMultiTargets.TryGetValue(relation.Id,out var binding))return default;
            var source=catalog.ConfigPages.Single(page=>page.Id==binding.SourcePageId&&page.ConfigTableId==table.Id);
            var selected=binding.TargetPageId is null?null:
                catalog.ConfigPages.Single(page=>page.Id==binding.TargetPageId&&page.ConfigTableId==target.Id);
            FixedConditionPlan? control=null;
            var overlay=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==source.Id&&field.ConfigColumnId==relation.Id)
                ?.Extra.GetValueOrDefault("controlDetail").ToString();
            string pageId;
            string? name=null;
            var storedGlobalStatic=false;
            var globalAccountPage=false;
            if(relation.ControlKind=="MultiCheckBoxDatabase")
            {
                var baseline=DatabaseCheckboxDefinition.Parse(relation.ControlDetail);
                var definition=baseline.WithPageOverride(overlay);
                pageId=definition.PageId;
                string? globalPageId=null;
                if(binding.ApplyControlScope)
                {
                    control=FixedConditionPlan.Compile(definition.WhereSql,columns[target.Id].Keys);
                    if(control.Terms.Count==0)
                        throw new InvalidOperationException("复选名称读取缺少实际发布的附加条件。");
                }
                if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
                if(GlobalCrossTableTarget.Declared(target))
                {
                    // 非冗余备用页从实际来源字段重证，旧配置仍沿唯一默认页。
                    var published=GlobalMultipleRelationTarget.Require(catalog,table,relation,target);
                    if(pageId.Length>0&&pageId!=published.Page.Id)
                        published=GlobalMultipleRelationTarget.RequirePage(catalog,table,relation,target,source);
                    if(pageId.Length>0&&pageId!=published.Page.Id||selected?.Id!=published.Page.Id||!binding.ApplyPageScope)
                        throw new InvalidOperationException("全局复选名称与已发布目标页不一致。");
                    pageId=globalPageId=published.Page.Id;
                    storedGlobalStatic=GlobalCrossTableTarget.Declared(table)&&published.UsesStoredStaticPage;
                    globalAccountPage=published.UsesNonRedundantAccountPage||published.UsesPhysicalParentPage;
                }
                name=CheckboxPageNameOverride(table,relation,target,selected,baseline,definition,globalPageId,storedGlobalStatic);
            }
            else
            {
                if(relation.ControlKind=="MultiSelectPage"&&GlobalCrossTableTarget.Declared(target))
                {
                    // 固定绑定只提供位置，重新从当前发布页证明实际目标及原名称合同。
                    var proof=GlobalMultipleRelationTarget.RequirePage(catalog,table,relation,target,source);
                    if(!binding.ApplyPageScope||binding.ApplyControlScope||selected?.Id!=proof.Page.Id)
                        throw new InvalidOperationException("全局多选名称与当前页的实际候选配置不一致。");
                    return(source,selected,true,null,null);
                }
                pageId="";
                if(!string.IsNullOrWhiteSpace(overlay))
                {
                    using var definition=System.Text.Json.JsonDocument.Parse(overlay);
                    pageId=MultiPageId(definition.RootElement);
                    var label=Value(definition.RootElement,"nameField");
                    var baseline=Value(Definition(relation),"nameField");
                    if(baseline.Length==0)baseline="name";
                    if(relation.ControlKind=="MultiSelectPage")name=PageMultiNameOverride(target,baseline,label);
                }
                if(pageId.Length==0)pageId=MultiPageId(Definition(relation));
                if(pageId.Length==0)pageId=Text(relation,"linkConfigPageId");
            }
            // 冗余目标的多选页面必须按精简目录重新证明物理范围，不能仅信任生成标志。
            var checkbox=relation.ControlKind=="MultiCheckBoxDatabase";
            var scopedPhysicalCheckbox=checkbox&&target.IsRongyu==1&&(binding.ApplyPageScope||control is not null);
            var storedMultiSelect=relation.ControlKind=="MultiSelectPage"&&target.IsRongyu==1&&selected is not null&&
                (binding.ApplyPageScope?IsProjectFixedMultiPage(catalog,target,selected):
                    IsProjectOnlyStoredMultiSelectPage(catalog,target,selected));
            var physicalNameOverride=name is not null&&target.IsRongyu==1&&!binding.ApplyPageScope;
            var globalOwnedCheckbox=checkbox&&binding.ApplyPageScope&&selected is not null&&
                (storedGlobalStatic||globalAccountPage||StoredIdentityMultiPage(catalog,target,selected,checkbox:true)||
                    IsNonRedundantProjectMultiPage(catalog,target,selected,checkbox:true));
            if(table.IsRongyu!=0||target.IsRongyu!=0&&!scopedPhysicalCheckbox&&!storedMultiSelect&&!physicalNameOverride||
                GlobalCrossTableTarget.Declared(table)&&!globalOwnedCheckbox||
                GlobalCrossTableTarget.Declared(target)&&!checkbox||pageId!=(selected?.Id??"")||
                selected is null&&(binding.ApplyPageScope||control is null&&name is null)||
                binding.ApplyControlScope&&control is null||
                source.YzProjectId.Length>0&&source.YzProjectId!=catalog.YzProjectId||
                selected is not null&&selected.YzProjectId.Length>0&&selected.YzProjectId!=catalog.YzProjectId)
                throw new InvalidOperationException("多值名称目标与当前页的发布配置不一致。");
            return(source,selected,binding.ApplyPageScope,control,name);
        }

        /// <summary>原非冗余复选允许从同一个发布页读取已明确声明的派生文本。</summary>
        private string? CheckboxPageNameOverride(TableMetadata table,ColumnMetadata relation,TableMetadata target,
            PageMetadata? selected,DatabaseCheckboxDefinition baseline,DatabaseCheckboxDefinition definition,string? globalPageId,
            bool storedGlobalStatic)
        {
            if(definition.NameField==baseline.NameField)return null;
            var original=Column(target,baseline.NameField);
            var label=Column(target,definition.NameField);
            if(!GlobalCrossTableTarget.Declared(target)&&PhysicalTextMultiLabel(target,original)&&PhysicalTextMultiLabel(target,label))
                return label.ColumnName;
            var fixedPage=globalPageId??(baseline.PageId.Length>0?baseline.PageId:Text(relation,"linkConfigPageId"));
            var storedGlobal=target.IsRongyu==1&&(storedGlobalStatic||globalPageId is not null&&selected is not null&&
                (StoredIdentityMultiPage(catalog,target,selected,checkbox:true)||
                 GlobalCrossTableTarget.StoredMultiParent(catalog,target,selected,checkbox:true) is not null));
            bool TextLabel(ColumnMetadata column)=>storedGlobal
                ?PhysicalTextMultiLabel(target,column)
                :globalPageId is not null
                    ?GlobalMultipleRelationTarget.IsPublishedCheckboxText(catalog,target,column,selected)
                    :PhysicalTextMultiLabel(target,column)||MultiNameStorageColumns.IsDerivedCheckboxText(target,storage[target.Id],column);
            if(table.IsRongyu!=0||target.IsRongyu!=0&&!storedGlobal||selected is null||
                selected.Id!=fixedPage||selected.Extra.GetValueOrDefault("exactDataManageMatchField").ToString().Length>0||
                definition.TableId.Length>0&&definition.TableId!=target.Id||
                definition.Table.Length>0&&definition.Table!=target.TableName||
                !TextLabel(original)||!TextLabel(label)||!MySqlPublishedRelationCondition.CheckboxLabelPublished(catalog,selected.Id,label.Id))
                throw new InvalidOperationException("复选页级文本必须沿用实际固定目标页和已发布的单值名称。");
            return label.ColumnName;
        }

        /// <summary>只解释固定来源页中已发布的名称，原辅助数组、范围和ID顺序保持。</summary>
        private string? PageMultiNameOverride(TableMetadata target,string baseline,string label)
        {
            if(string.IsNullOrWhiteSpace(label)||label==baseline)return null;
            if(GlobalCrossTableTarget.Declared(target)||
                !PhysicalTextMultiLabel(target,Column(target,baseline))||!PhysicalTextMultiLabel(target,Column(target,label)))
                throw new InvalidOperationException("页级多值名称必须是同一项目内目标的物理文本列。");
            return label;
        }

        /// <summary>仅允许可信声明的普通物理文本进入名称投影。</summary>
        private bool PhysicalTextMultiLabel(TableMetadata target,ColumnMetadata label)=>
            MySqlNonRedundantReadCompiler.PhysicalTextMultiLabel(storage[target.Id],label);

        /// <summary>派生多值名称使用独立固定别名域，联接保持在目标子查询内，不把来源扩成多行。</summary>
        private (string Sql,string Type) DerivedMultiDisplay(TableMetadata table,string owner,ColumnMetadata relation,TableMetadata target,
            ColumnMetadata value,PageMetadata? publishedPage,PageMetadata? selectedScope,PageMetadata? namePage,PageMetadata? sourcePage,FixedConditionPlan? control,
            (MultiTargetParentScope Target,MultiSourceParent Source)? fixedParent=null,bool sharedCheckbox=false)
        {
            var checkbox=relation.ControlKind=="MultiCheckBoxDatabase";
            var storedIdentity=selectedScope is not null&&StoredIdentityMultiPage(catalog,target,selectedScope,checkbox);
            var globalDictionarySource=refreshFields is not null&&selectedScope is not null&&target.IsRongyu==0&&
                GlobalCrossTableTarget.Declared(table)&&GlobalCrossTableTarget.Declared(target)&&
                GlobalMultipleRelationTarget.RequireCached(catalog,table,relation,target) is {Page.Id:var publishedId} proof&&
                publishedId==selectedScope.Id&&
                (proof.UsesNonRedundantAccountPage||proof.UsesStoredStaticPage||proof.UsesNonRedundantPhysicalParentPage);
            var projectFixedScope=selectedScope is not null&&IsProjectFixedMultiPage(catalog,target,selectedScope,
                checkboxAccount:checkbox,allowStoredTextParent:true);
            var storedParent=selectedScope is null?null:GlobalCrossTableTarget.StoredMultiParent(catalog,target,selectedScope,checkbox);
            var ordinaryForm=checkbox&&refreshFields is not null&&selectedScope is not null&&(sharedCheckbox||fixedParent is not null)
                ?DatabaseCheckboxDefinition.Parse(relation.ControlDetail).AdditionalPhysicalFormConditions(catalog,target,selectedScope)
                :null;
            var storedCheckboxOwner=(storedIdentity||projectFixedScope||ordinaryForm is not null)&&checkbox&&refreshFields is not null
                ?new CascadeCacheOwner(table,owner,GlobalSingleSource:GlobalCrossTableTarget.Declared(table)):null;
            if(storedCheckboxOwner is not null)
            {
                // 回填从表级条件和来源实际行重算，与候选和锁读使用同一门店、表单及常量。
                var definition=DatabaseCheckboxDefinition.Parse(relation.ControlDetail);
                var fields=columns[target.Id].Keys;
                var where=ordinaryForm??(projectFixedScope?definition.AdditionalProjectConditions(selectedScope!.WhereSql,fields):definition.WhereSql);
                var syntax=FixedConditionPlan.Compile(where,fields);
                if(syntax.Terms.Count>0)control=syntax;
            }
            var physicalScope=storedIdentity||storedParent is not null||target.IsRongyu==1&&(projectFixedScope||
                selectedScope is not null&&IsStoredStaticMultiPage(catalog,target,selectedScope,checkbox))||
                checkbox&&target.IsRongyu==1&&(fixedParent is not null||ordinaryForm is not null||
                table.IsRongyu==0&&sourcePage is not null&&(selectedScope is not null||control is not null));
            if(table.IsRongyu is not(0 or 1)||target.IsRongyu!=0&&!physicalScope||relation.ControlKind is not("MultiSelectPage" or "MultiCheckBoxDatabase"))
                throw new NotSupportedException("多值名称要求已证明的目标及原多选或数据库复选。");
            var global=GlobalCrossTableTarget.Declared(target);
            // 全局 R1 使用原物理候选证明，R0 继续用目标页的逻辑名称与条件计划。
            var physicalGlobalPage=global&&physicalScope&&storedParent is null?selectedScope:null;
            var selectedPage=physicalGlobalPage is null?selectedScope:null;
            var scalarLabel=storage[target.Id].IsPhysical(value)
                ?MultiNameStorageColumns.IsPhysicalScalarLabel(table,relation,target,value,storage[target.Id])||
                    global&&GlobalPhysicalRelationValue.IsPublishedTypedCache(catalog,storage[target.Id],target,value)
                :MultiNameStorageColumns.IsDerivedScalarLabel(target,value,storage[target.Id]);
            if(value.LogicalType!="string"&&!scalarLabel)throw new NotSupportedException("多值派生名称类型未支持。");
            // 全局单值路径已证明字符串键的二进制读取；这里不是仅允许int64键的精确管理匹配用途。
            var prefixForNames=publishedPage is null&&publishedArrayScopeCount==0?"multi_name_":aliasPrefix+"array"+publishedArrayScopeCount+"_";
            publishedArrayScopeCount++;
            // 冗余固定字典走下面的发布父页证明，普通表单分支仅接受项目内父。
            var formPageParent=checkbox&&!projectFixedScope&&table.IsRongyu==0&&!GlobalCrossTableTarget.Declared(table)&&
                sourcePage is not null&&selectedScope is not null
                ?MultiTargetParentScope.CompileProjectCheckboxPhysicalPage(catalog,target,selectedScope)
                :null;
            var pageParent=fixedParent?.Target??formPageParent??(selectedScope is null?null:MultiParent(catalog,selectedScope,checkbox));
            var controlParents=CheckboxControlParents(target,control);
            var parent=pageParent??controlParents.FirstOrDefault();
            if(parent is not null&&controlParents.Any(candidate=>candidate.Parent.Id!=parent.Parent.Id))
                throw new InvalidOperationException("复选目标页与控件必须绑定同一实际来源父表。");
            // 复选的物理表单父键必须经过实际字典页和来源详情的共同证明，名称读取按来源行绑定。
            var projectCheckbox=checkbox&&projectFixedScope;
            var allowFormParent=projectCheckbox||formPageParent is not null&&
                controlParents.All(range=>formPageParent.HasTarget(range.Target.Id));
            var sourceBinding=fixedParent is {} paired?paired.Source:
                sharedCheckbox||(checkbox&&storedParent is not null&&sourcePage is null)||(projectCheckbox&&sourcePage is null)
                ?parent?.CheckboxSource(catalog,table,relation,
                    allowProjectPhysicalColumns:projectCheckbox):
                sourcePage is null?parent?.Source(catalog,table,relation):
                allowFormParent||controlParents.Length>0||checkbox&&storedParent is not null?parent?.CheckboxSource(catalog,table,relation,sourcePage,
                    allowFormParent:allowFormParent,allowProjectPhysicalColumns:allowFormParent):
                parent?.Source(catalog,table,relation,sourcePage);
            var sourceParent=sourceBinding?.Column;
            var logicalSource=sourceBinding?.LogicalPages.Count>0;
            // 目标只在实际来源路径与物理字典页都证明同一个文本祖先时才能复用来源的可见性。
            var sharedStoredTextParent=target.IsRongyu==1&&!global&&selectedScope is not null&&
                pageParent is {Logical:false,Global:false}&&sourceBinding is {LogicalPages.Count:>0}&&
                UsesSharedStoredTextParent(table,sourceBinding,target,selectedScope,pageParent);
            var sharedSparseTextParent=table.IsRongyu==1&&target.IsRongyu==0&&!global&&
                !GlobalCrossTableTarget.Declared(table)&&selectedScope is not null&&pageParent is {Logical:true,Global:false}&&
                sourceBinding is {LogicalPages.Count:0}&&UsesSharedSparseTextParent(target,selectedScope,pageParent,table,sourceBinding);
            var storedTextPath=selectedScope is not null&&sourceBinding is not null&&pageParent is not null
                ?StoredTextCacheParent(table,sourceBinding,target,selectedScope,pageParent):null;
            if(pageParent is {Global:true}&&sourceBinding is {AdditionalColumns.Count:>0})
                RequireCompoundGlobalSourceParent(catalog,target.Id,selectedScope!.Id,sourceBinding);
            var sourceParentSql=sourceBinding is {LogicalPages.Count:>0} or {AdditionalColumns.Count:>0}
                ?SingleSourceParentValue(table,owner,sourceBinding):null;
            var parentAlias=sourceParentSql is not null?"mt_parent_input":owner;
            if(sourceParent is not null&&sourceParentSql is null){Use(table,sourceParent);Touch(owner,sourceParent.ColumnName);}
            // 已保存名称按实际发布范围读取；用户角色值只在本次选择或写入时应用。
            var candidatePage=physicalGlobalPage??(selectedPage is null?publishedPage:null);
            var storedGlobalExact=candidatePage is not null&&(checkbox
                ?GlobalCrossTableTarget.DeclaresExactPage(target,candidatePage)
                :GlobalCrossTableTarget.DeclaresExactMulti(target,candidatePage));
            var targetBuilder=new Builder(catalog,target.Id,matching:!global,rootAlias:"multi_value",aliasPrefix:prefixForNames,
                trackReverse:refreshFields is not null,candidatePage:candidatePage,multiLabel:true,cascadeLabelField:value.ColumnName,
                cascadeCacheOwner:refreshFields is null||fixedParent is not null&&!projectFixedScope&&storage[target.Id].IsPhysical(value)?null:
                    new(table,owner,GlobalSingleSource:globalDictionarySource||(storedIdentity||storedParent is not null)&&GlobalCrossTableTarget.Declared(table)),
                globalCandidate:global&&target.IsRongyu==0&&candidatePage is not null,
                scopedPhysicalCandidate:physicalScope&&!global,accountPhysicalCandidate:storedIdentity,storedMultiParent:storedParent,
                exactSingleCandidate:storedGlobalExact)
            {
                SelectedMultiPage=selectedPage,
                MultiCandidateParent=fixedParent?.Target,
                SharedStoredMultiTextParent=sharedStoredTextParent,
                StoredMultiTextPath=storedTextPath,
                AllowsExactMultiSelection=!checkbox,
                AllowsStoredExactCheckbox=checkbox,
                MultiParentOwner=sourceParent is null?null:(new(table,parentAlias),sourceParent,[]),
                CrossAliasPrefix=logicalSource?prefixForNames+"cross":"cross",
                MultiNamePathPage=namePage,
                AllowLiteralMultiScope=sourcePage is not null||fixedParent is not null||sharedCheckbox||projectFixedScope,
                GlobalCrossCandidate=global&&(selectedPage is not null||candidatePage is not null)
            };
            var parents=new List<string>();
            foreach(var range in controlParents)
            {
                if(range.Target.Id==pageParent?.Target.Id||pageParent?.AdditionalTargets.Any(parent=>parent.Target.Id==range.Target.Id)==true)continue;
                var targetKey="multi_value."+Q(range.Target.ColumnName);
                Use(target,range.Target);
                if(pageParent is null&&parents.Count==0&&!range.Global)
                    parents.Add(targetBuilder.MultiParentPredicate(range,targetKey));
                else
                {
                    // 目标页已有真实父校验，或来源由完整级联还原全局父。其余同表父列只需与同一键精确比较。
                    if(sourceParent is null||pageParent is null&&range.Global&&!logicalSource)
                        throw new InvalidOperationException("复选控件的全局父级缺少实际来源级联。");
                    parents.Add(SameSingleParent(targetKey,parentAlias+"."+Q(sourceParent.ColumnName)));
                }
            }
            var nested=targetBuilder.Compile([value.ColumnName],[]);
            nested.CopyParametersTo(crossConstants);
            if(nested.RequiresPublishedProjectIdentity)publishedArrayIdentity=true;
            var cascadeLabel=nested.CascadeLabelOutputs.Contains(value.ColumnName);
            nested.Origins.TryGetValue(value.ColumnName,out var valueOrigin);
            if(nested.Types[value.ColumnName]!=value.LogicalType||nested.MultiProjections.Count>0||
                !cascadeLabel&&(nested.Crosses.Count>0||valueOrigin is null||Metarill.Core.Security.ExactDataManageValues.IsSensitiveField(valueOrigin.Column)))
                throw new NotSupportedException("多选派生名称必须落到已证明的同类型非凭据单值终点。");
            var prefix=" FROM "+Q(target.TableName)+" multi_value";
            if(!nested.From.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("多选名称别名域与固定目标不一致。");
            var targetJoins=nested.From[prefix.Length..];var valueSql=nested.Expression(value.ColumnName);
            var formFields=new HashSet<string>(StringComparer.Ordinal);
            var added=ProjectCheckboxControlCondition(table,owner,parentAlias,target,control,prefixForNames,formFields,storedCheckboxOwner);
            var scope=string.Join(" AND ",new[]{nested.Scope,added}.Concat(parents).Where(part=>part.Length>0));
            var ownerFields=nested.CascadeCacheOwnerFields.Concat(formFields)
                .Concat(controlParents.Length>0&&sourceParent is not null?[sourceParent.ColumnName]:[])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            // 行内使用真实来源别名和逻辑父级。批量查询从相同来源行提取每个依赖值后使用参数别名。
            var inlineScope=scope;
            Dictionary<string,string>? sourceExpressions=null;
            if(sourceParentSql is not null)
            {
                sourceExpressions=ownerFields.ToDictionary(field=>field,
                    field=>field==sourceParent!.ColumnName?"("+sourceParentSql+")":owner+"."+Q(field),StringComparer.Ordinal);
                foreach(var pair in sourceExpressions)
                    inlineScope=inlineScope.Replace(parentAlias+"."+Q(pair.Key),pair.Value,StringComparison.Ordinal);
            }
            var conditioned=selectedScope is not null||control is not null;
            string ScopedName(string predicate)=>MySqlMultiValueLabel.Text(conditioned?"CASE WHEN "+predicate+" THEN "+valueSql+" END":valueSql,value.LogicalType);
            // 反查和前置联接不引用晚生成的级联别名，名称可见性在完整路径上判断。
            var associationScope=conditioned?Scope(target,"multi_value",projectScoped:!global):scope;
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
                var from=" FROM "+Q(table.TableName)+" "+owner;
                var rootFields=nested.PhysicalReferences.Where(p=>p.Table==target.TableName).Select(p=>p.Column.Name).ToHashSet(StringComparer.Ordinal);
                if(storedCheckboxOwner is not null&&control is not null)rootFields.UnionWith(control.Fields);
                var dependency=new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,rootFields}};
                // 删除树目标时仍按源数组找到消费者，不依赖目标当前还存在。
                customReverse.Add(new(target.TableName,from,ArrayMember(array,"@target_text"),"predicate",dependency));
                // 文本祖先使用下方目标路径的真实业务键反查，不能按父节点整数主键匹配来源。
                if(parent is not null&&sourceParent is not null&&!sharedSparseTextParent&&storedTextPath is null)
                {
                    var parentFields=nested.PhysicalReferences.Where(item=>item.Table==parent.Parent.TableName).Select(item=>item.Column.Name).ToHashSet(StringComparer.Ordinal);
                    var alternate=parent.Global?null:parent.LogicalAlternateKey(catalog,target);
                    customReverse.Add(new(parent.Parent.TableName,from,owner+"."+Q(sourceParent.ColumnName),sourceParent.LogicalType,
                        new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,parentFields}})
                    {
                        // 反查使用与来源持久父值相同的业务键，包含改键和删除事件的旧值。
                        NodeKey=alternate is null?null:new(alternate.ColumnName,alternate.LogicalType,
                            Column(parent.Parent,"yz_project_id").LogicalType)
                    });
                }
                foreach(var reverse in nested.Reverse)
                {
                    var match=reverse.TargetPredicate;var predicate="((SELECT 1"+reverse.From+" WHERE "+associationScope+" AND "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+" AND "+match+" LIMIT 1) IS NOT NULL)";var fields=reverse.Outputs.Values.SelectMany(v=>v).ToHashSet(StringComparer.Ordinal);customReverse.Add(new(reverse.TargetTable,from,predicate,"predicate",new Dictionary<string,IReadOnlySet<string>>(StringComparer.Ordinal){{output,fields}}){NodeKey=reverse.NodeKey});
                }
            }
            var expression="CAST(COALESCE((SELECT JSON_ARRAYAGG("+name+") OVER (ORDER BY multi_ids.ordinal ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM JSON_TABLE("+array+", '$[*]' COLUMNS(ordinal FOR ORDINALITY, member_id VARCHAR(32) PATH '$' NULL ON ERROR)) multi_ids LEFT JOIN "+Q(target.TableName)+" multi_value ON "+(global||checkbox?"CAST(CAST(multi_value.`id` AS CHAR) AS BINARY)=CAST(multi_ids.member_id AS BINARY)":"CAST(multi_value.`id` AS CHAR)=multi_ids.member_id")+(associationScope.Length==0?"":" AND "+associationScope)+targetJoins+" ORDER BY multi_ids.ordinal LIMIT 1),JSON_ARRAY()) AS CHAR)";
            multiProjections[expression]=new(source,Q(target.TableName),Q(value.ColumnName),scope,targetJoins,ScopedName(scope),
                ownerFields.Length==0?null:new(parentAlias,ownerFields,sourceExpressions,PreserveScalarTypes:formFields.Count>0));
            multiPredicates[expression]="EXISTS (SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE JSON_CONTAINS("+array+",JSON_QUOTE(CAST(multi_value.`id` AS CHAR)))"+(inlineScope.Length==0?"":" AND "+inlineScope)+" AND "+name+" LIKE {parameter} ESCAPE '!')";
            if(global||cascadeLabel||checkbox)multiPredicates[expression]="((SELECT 1 FROM "+Q(target.TableName)+" multi_value"+targetJoins+" WHERE "+ArrayMember(array,"CAST(multi_value.`id` AS CHAR)")+(inlineScope.Length==0?"":" AND "+inlineScope)+" AND "+name+" LIKE {parameter} ESCAPE '!' LIMIT 1) IS NOT NULL)";
            // 维护报告展示发布的逻辑来源字段，物理列清单由嵌套路径单独记录。
            origin=valueOrigin??new(target.TableName,value.ColumnName);
            return(expression,"string");
        }

        /// <summary>逐一证明控件声明的目标物理父键，支持多个列同时限定同一来源父记录。</summary>
        private MultiTargetParentScope[] CheckboxControlParents(TableMetadata target,FixedConditionPlan? control) =>
            control?.Terms.Where(IsCheckboxParentTerm).Select(term=>MultiTargetParentScope.CompileCheckboxParent(catalog,target,
                FixedConditionPlan.Compile(term.ToDefinition(),columns[target.Id].Keys)) ??
                throw new InvalidOperationException("复选控件父级未绑定实际目标。")).ToArray() ?? [];

        private static bool IsCheckboxParentTerm(FixedConditionTerm term)=>term.Values.Any(value=>
            value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text));

        /// <summary>附加条件继承原表单槽。项目和父级已单独绑定，账号值延后从认证身份获取。</summary>
        private string ProjectCheckboxControlCondition(TableMetadata source,string owner,string projectionAlias,TableMetadata target,
            FixedConditionPlan? control,string prefix,HashSet<string> fields,CascadeCacheOwner? cacheOwner=null)
        {
            if(control is null)return "";
            bool Project(FixedConditionTerm term)=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                value.Text.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase));
            var project=control.Terms.Where(Project).ToArray();
            if(project.Any(term=>term.Field!="yz_project_id"||term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1))
                throw new InvalidOperationException("复选附加条件的项目变量必须使用真实项目列。");
            var terms=control.Terms.Where(term=>!Project(term)&&!IsCheckboxParentTerm(term)).ToArray();
            var persistedScope="";
            if(cacheOwner is not null)
            {
                var account=terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable&&
                    AccountConditionVariables.IsSupported(value.Text))).ToArray();
                if(account.Length>0)
                {
                    var accountPage=new PageMetadata{ConfigTableId=target.Id,WhereSql=string.Join(" AND ",account.Select(term=>term.ToDefinition()))};
                    var proof=AccountScopedCrossCache.Compile(catalog,source,target,accountPage,globalTreeSource:false,
                        globalSingleSource:cacheOwner.GlobalSingleSource);
                    fields.UnionWith(proof.Ownership.Select(binding=>binding.Source.ColumnName));
                    persistedScope=AccountCacheScope(proof,target,"multi_value",projectionAlias,render:true,source);
                    terms=terms.Except(account).ToArray();
                }
            }
            var syntax=FixedConditionPlan.Compile(string.Join(" AND ",terms.Select(term=>term.ToDefinition())),columns[target.Id].Keys);
            var types=syntax.Fields.ToDictionary(field=>field,field=>Column(target,field).LogicalType,StringComparer.Ordinal);
            var plan=FixedConditionSqlPlan.Compile(syntax,types);
            var validity=new HashSet<string>(StringComparer.Ordinal);
            var clause=plan.RenderRowProjection(ConditionParameterPrefix(prefix+"control"),field=>
            {
                var column=Column(target,field);
                if(cacheOwner is not null?!GlobalPhysicalRelationValue.IsScalar(storage[target.Id],target,column):
                    !storage[target.Id].IsPhysical(column)||column.IsVirtualColumn!=0||ExactDataManageValues.IsSensitiveField(field))
                    throw new InvalidOperationException("复选名称附加条件必须使用非凭据物理字段。");
                Use(target,column);
                return "multi_value."+Q(field);
            },(variable,type)=>AccountConditionVariables.IsSupported(variable)?null:
                SingleControlFormColumn(source,owner,validity,variable,type,fields,projectionAlias),deferAccount:true);
            foreach(var parameter in clause.Parameters)crossConstants.Add(parameter.Key,parameter.Value);
            var rowProject="";
            if(project.Length>0&&GlobalCrossTableTarget.Declared(target))
            {
                if(!columns[target.Id].TryGetValue("yz_project_id",out var column)||!storage[target.Id].IsPhysical(column))
                    throw new InvalidOperationException("全局复选按项目过滤须有明确的物理归属列。");
                rowProject=Scope(target,"multi_value",includeDeleted:true);
            }
            return string.Join(" AND ",new[]{clause.Sql,rowProject,persistedScope}.Concat(validity).Where(part=>part.Length>0));
        }



        /// <summary>从来源行的固定可见级联还原父键，所有同伴的冲突及不可见路径都必须拒绝。</summary>
        private string LogicalRelationSourceParent(TableMetadata table,string owner,MultiSourceParent source,CascadeCacheOwner? cacheOwner=null)
        {
            var nested=table.Id!=root.Id||owner!=rootAlias;
            if(table.IsRongyu!=0||cacheOwner is null&&(refreshFields is not null||nested&&trackReverse))
                throw new NotSupportedException("逻辑来源父要求非冗余来源，维护用途须证明实际缓存行归属。");
            var field=source.Column.ColumnName;
            var fields=LogicalSourceParentFields(catalog,table,source);
            var bindings=source.LogicalPages.SelectMany(page=>CrossMembershipBindings(catalog,table,page,fields))
                .DistinctBy(binding=>binding.SourceColumnId,StringComparer.Ordinal).ToArray();
            if(!nested)
            {
                foreach(var binding in bindings)
                {
                    if(crossTables.TryGetValue(binding.SourceColumnId,out var existing)&&
                        (existing.Levels.Count!=binding.Levels.Count||existing.Levels.Zip(binding.Levels)
                            .Any(pair=>pair.First.PageId!=pair.Second.PageId||!SameSharedCondition(pair.First.Condition,pair.Second.Condition))))
                        throw new InvalidOperationException("关系来源父键的级联范围与当前读取页不一致。");
                    crossTables[binding.SourceColumnId]=binding;
                }
            }
            // 先还原全部逻辑层位。对内层来源保留独立别名，外层记录不承担其路径存储校验。
            var scopedBindings=nested?bindings.ToDictionary(binding=>binding.SourceColumnId,StringComparer.Ordinal):null;
            var keys=fields.ToDictionary(name=>name,name=>scopedBindings is null?Resolve(table,owner,name).Sql:
                ScopedCrossField(table,owner,scopedBindings,name,cacheOwner),StringComparer.Ordinal);
            var value=keys[field];
            var sameParent=source.AdditionalColumns.Select(column=>
            {
                if(keys.TryGetValue(column.ColumnName,out var logical))return SameSingleParent(logical,value);
                Use(table,column);Touch(owner,column.ColumnName);
                return SameSingleParent(owner+"."+Q(column.ColumnName),value);
            });
            return "CASE WHEN "+string.Join(" AND ",sameParent.Concat(
                ValidScopedPaths(ScopedCrossPaths(owner,bindings.Select(binding=>binding.SourceColumnId)))))+" THEN "+value+" END";
        }
    }
}
