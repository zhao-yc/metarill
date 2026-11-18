using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record NonRedundantOutput(string Storage,string Property,string JsonName,string Type,bool IsJsonArray=false);
internal sealed record NonRedundantPagePlan(IReadOnlyList<NonRedundantOutput> Outputs,IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> ReadOutputs,IReadOnlyList<NonRedundantSingleTargetScope> SingleTargets);

/// <summary>生成侧提前证明单值／固定多选用途和物理写边界；数据库表达式由数据适配器编译。</summary>
internal static class NonRedundantReadCompilation
{
    /// <summary>声明全局的R0表仍可拥有实际行归属，两种用途必须分别生成。</summary>
    internal static bool HasGlobalRowProject(TableModel table)=>table.IsGlobal&&table.Source.IsRongyu==0&&table.Columns.Any(column=>column.Source.ColumnName=="yz_project_id");

    /// <summary>有行归属的全局选择页及独立物理父多选使用固定全局候选。</summary>
    internal static bool GlobalSelectionPage(TableModel table,PageModel page)=>
        HasGlobalRowProject(table)&&page.Source.Extra.GetValueOrDefault("type").ToString() is "1" or "2"||
        GlobalAccountMultiPage(table,page)||GlobalAccountSinglePage(table,page);

    /// <summary>原独立单选使用带账号及物理父级的候选计划，作用于原分页和详情。</summary>
    internal static bool GlobalAccountSinglePage(TableModel table,PageModel page)=>
        page.Source.Extra.GetValueOrDefault("type").ToString()=="1"&&GlobalAccountParentScope(table,page);

    /// <summary>纯物理账号与父级同时限制全局多选时，独立分页需要同一带父参数的计划。</summary>
    internal static bool GlobalAccountMultiPage(TableModel table,PageModel page)=>
        page.Source.Extra.GetValueOrDefault("type").ToString()=="2"&&GlobalAccountParentScope(table,page);

    /// <summary>账号和真实物理父范围由实际发布页决定，与有没有项目列无关。</summary>
    private static bool GlobalAccountParentScope(TableModel table,PageModel page)=>
        table.IsGlobal&&table.Source.IsRongyu==0&&FixedPageConditionCompilation.HasAccountVariables(page)&&
        !FixedPageConditionCompilation.HasLogicalFields(page)&&page.Scopes.Any(scope=>scope.Source=="parent"&&
            !CrossTableStorageColumns.IsLogicalPathField(table.Source,table.Columns.Select(column=>column.Source),scope.Column.Source.ColumnName));

    /// <summary>候选、已选和Excel从同一发布页取得目标范围。</summary>
    internal static string RelationPlanStart(TableModel table,PageModel? page)=>
        HasGlobalRowProject(table)&&page is not null
            ?"CompileGlobalCandidate("+JsonSerializer.Serialize(table.Source.Id)+","+JsonSerializer.Serialize(page.Source.Id)
            :"CompileRelation("+JsonSerializer.Serialize(table.Source.Id);

    /// <summary>普通多值名称携带固定字段用途，全局目标仍使用原候选页证明。</summary>
    internal static string MultiLabelPlanStart(TableModel table,PageModel? page,ColumnModel name)=>
        page is not null&&FixedPageConditionCompilation.HasMultiTargetScope(table,page)
            ?"CompileScopedMultiLabel("+JsonSerializer.Serialize(table.Source.Id)+","+JsonSerializer.Serialize(page.Source.Id)+","+JsonSerializer.Serialize(name.Source.ColumnName)
            :table.IsGlobal?RelationPlanStart(table,page):"CompileMultiLabel("+JsonSerializer.Serialize(table.Source.Id)+","+JsonSerializer.Serialize(name.Source.ColumnName);

    /// <summary>没有派生关系的原纯物理非冗余页继续使用既有强类型路径。</summary>
    internal static bool Requires(GenerationModel model,TableModel table)=>HasGlobalRowProject(table)||
        table.Pages.Any(page=>GlobalAccountMultiPage(table,page)||GlobalAccountSinglePage(table,page)||
            MultiGlobalParentCompilation.PhysicalAccountTarget(model,table,page) is not null)||table.Source.IsRongyu==0&&model.Catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Source.Id&&column.IsVirtualColumn==0&&(!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column)||MetadataStoragePolicy.IsRelationColumn(column)));
    /// <summary>所有实体写入复用同一存储计划，不把读取派生属性持久化。</summary>
    internal static bool Physical(GenerationModel model,TableModel table,ColumnModel column)=>MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column.Source);
    /// <summary>固定叶名与目标页一起发布；仅显示、顺序等范围不变的页面覆盖可以继续使用表级读取定义。</summary>
    internal static IReadOnlyList<CascadeTreeReadModel> SelfTreeNameQueries(GenerationModel model, TableModel table, PageModel page)
    {
        if (table.Source.IsRongyu != 0) return [];
        var fields = page.Searchable.Concat(page.Sortable)
            .Concat(FixedPageQueryCompilation.For(table, page).DefaultOrder.Select(order => order.Column))
            .Select(column => column.Source.ColumnName).Concat(page.Condition is { } condition ? condition.SelfTreeFields : Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var result = new List<CascadeTreeReadModel>();
        foreach (var tree in CascadeTreeReadCompilation.ForService(model, table, page))
        {
            if (CascadeTreeViewRenderer.Name(table, tree.Source) is not { } name || !fields.Contains(name.Source.ColumnName))
                continue;
            var baseline = CascadeTreeReadCompilation.Resolve(model, table, page, tree.Source, usePageDetail: false).Definition;
            if (baseline is null || baseline.TargetPage.Source.Id != tree.TargetPage.Source.Id ||
                baseline.Parent.Source.Id != tree.Parent.Source.Id || baseline.Name.Source.Id != tree.Name.Source.Id)
                throw new InvalidOperationException("自树名称查询必须与页面实际目标页、父字段及名称字段一致。");
            if(page.Condition?.SelfTreeFields.Contains(name.Source.ColumnName)==true)
            {
                var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==tree.Source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();
                if(!CrossTableStorageColumns.KeepsDefinition(tree.Source.Source.ControlDetail,overlay))
                    throw new InvalidOperationException("自树固定页面条件必须沿用表级级联定义。");
            }
            result.Add(tree);
        }
        return result;
    }

    /// <summary>只编译已经接通的标量用途；完整包还须通过全部控件、范围及独立宿主验证。</summary>
    internal static NonRedundantPagePlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        // 固定候选约束实际发布页，普通业务页可使用自己的项目条件。
        if(HasGlobalRowProject(table))GlobalDerivedRelationSource.RequireTable(model.Catalog,table.Source);
        else if(table.IsGlobal)
        {
            if(GlobalAccountParentScope(table,page))
                GlobalCrossTableTarget.RequireMultiCandidate(model.Catalog,table.Source,page.Source);
            else if(FixedPageConditionCompilation.HasSelfTreeFields(page))
                GlobalCrossTableTarget.RequireSelfTreeConditionCandidate(model.Catalog,table.Source,page.Source,
                    allowMultiParent:FixedPageConditionCompilation.HasMultiTargetScope(table,page));
            else if(table.Columns.Any(NonRedundantCrossTableSubset.Declared))
            {
                if(FixedPageConditionCompilation.HasMultiTargetScope(table,page)&&page.Scopes.Any(scope=>scope.Source=="parent"))
                    GlobalCrossTableTarget.RequireMultiCandidate(model.Catalog,table.Source,page.Source);
                else GlobalCrossTableTarget.RequireCrossTableCandidate(model.Catalog,table.Source,page.Source);
            }
            else GlobalCrossTableTarget.RequireAccountCandidate(model.Catalog,table.Source,page.Source);
        }
        // 导入沿用标准Service写事务，仅开放本页真实物理可写列，派生读取不参与覆盖匹配。
        if(ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>!Physical(model,table,column)))throw new InvalidOperationException("非冗余导入只能写入物理列。");
        // 物理文件ID数组沿用原FileLibrary事务。布局控件可以展示，但不能同时配置为联接字段。
        if (table.Columns.Any(column => (column.Source.ControlKind is "Title" or "Tab") && !column.IsLayoutOnly))
            throw new InvalidOperationException("非冗余标题和标签字段必须是纯布局字段。");
        if(table.Columns.Any(column=>column.IsJsonArray && (column.Source.ControlKind is not ("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase") && !column.IsManagedMultiNames || column.Source.ControlKind=="FileLibrary"&&!Physical(model,table,column))))throw new InvalidOperationException("非冗余多值或派生文件读取仍待接线。");
        if(page.Searchable.Any(column=>column.Source.LogicalType is not("string" or "int64" or "int32" or "decimal" or "double" or "bool" or "datetime" or "date" or "time" or "binary")))throw new InvalidOperationException("非冗余筛选字段类型尚未接线。");
        if(page.Scopes.Any(scope=>!Physical(model,table,scope.Column)))throw new InvalidOperationException("非冗余父范围必须绑定物理外键。");
        if(ManagerCredentialCompilation.IsTable(table)||FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null||FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null||FixedOptionCatalogCompilation.ForPage(model,page).Count!=0)throw new InvalidOperationException("非冗余固定安全模块及目录读取尚需独立验收。");
        foreach(var checkbox in table.Columns.Where(column=>column.Source.ControlKind=="MultiCheckBoxDatabase"))
            if(DatabaseCheckboxCompilation.Resolve(model,table,page,checkbox).Definition is null)throw new InvalidOperationException("非冗余数据库复选缺少已证明的字段与候选范围。");
        foreach(var multi in table.Columns.Where(column=>column.Source.ControlKind=="MultiSelectPage"))
            if(MultiSelectCompilation.Resolve(model,table,page,multi,forService:true).Definition is null)throw new InvalidOperationException("非冗余多选缺少已证明的字段与候选范围。");
        foreach(var tree in table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree"))
        {
            if(CrossTableWriteCompilation.Resolve(model,table,page,tree).Definition is{} cross){NonRedundantCrossTableCompilation.Require(model,table,page,cross.Read);continue;}
            if(CascadeTreeWriteCompilation.Resolve(model,table,page,tree).Definition is null)throw new InvalidOperationException("非冗余级联要求已证明的selfTree读写计划。");
        }
        var crossPlans=CrossTableWriteCompilation.ForService(model,table,page);
        var crossIds=crossPlans.SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var relation in table.Columns.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is "CascadeTree" or "MultiCheckBoxDatabase"||crossIds.Contains(relation.Source.Id))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联写入缺少已接通的明确目标。");ValidateTargetDeclarations(model,relation,target);
            if(target.IsGlobal&&page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==relation.Source.Id)&&
                MultiGlobalParentCompilation.PhysicalScopeTarget(model,table,page) is {} parentPage)
            {
                _=NonRedundantReads.MultiGlobalParentPageId(model.Catalog,parentPage.Source.ConfigTableId,parentPage.Source.Id);
                continue;
            }
            if(target.IsGlobal&&relation.Source.ControlKind is not("List" or "SelectPage")&&
                MultiGlobalParentCompilation.SingleParentBinding(model,table,relation) is not null)
                continue;
            if(target.IsGlobal){if(relation.Source.ControlKind=="MultiSelectPage")_=GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);else _=GlobalSingleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);}
        }
        foreach(var selection in table.Columns.Where(column=>column.Source.ControlKind=="SelectPage"))NonRedundantSelectionCompilation.Resolve(model,table,page,selection);
        var outputs=page.Readable.Select(column=>new NonRedundantOutput(column.Source.ColumnName,column.Property,column.JsonName,column.Type,column.IsJsonArray)).ToList();
        foreach(var name in CascadeTreeViewRenderer.ImplicitNames(model,table,page))outputs.Add(new(name.Source.ColumnName,name.Property,name.JsonName,name.Type));
        foreach(var multi in MultiSelectCompilation.ForPage(model,table,page).Where(multi=>multi.Names is not null))
            if(!outputs.Any(output=>output.Storage==multi.Names!.Source.ColumnName))outputs.Add(new(multi.Names!.Source.ColumnName,multi.Names.Property,multi.Names.JsonName,"string?",true));
        foreach(var checkbox in DatabaseCheckboxCompilation.ForPage(model,table,page).Where(def=>page.Readable.Any(field=>field.Source.Id==def.Source.Source.Id)))
            if(!outputs.Any(output=>output.Storage==checkbox.Names.Source.ColumnName))outputs.Add(new(checkbox.Names.Source.ColumnName,checkbox.Names.Property,checkbox.Names.JsonName,"string?",true));
        foreach(var relation in page.Readable.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is not("List" or "SelectPage"))continue;
            var pageField=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==relation.Source.Id);
            if(relation.Source.ControlKind!="SelectPage"&&pageField?.Extra.TryGetValue("controlDetail",out var detail)==true&&!string.IsNullOrWhiteSpace(detail.ToString())&&HasEffectiveOverride(relation.Source.ControlDetail,detail.ToString())&&!ProvenOptionsOverride(model,page,relation,detail.ToString()))throw new InvalidOperationException("非冗余页级关系控件覆盖尚需接线。");
            var nameField="name";if(!string.IsNullOrWhiteSpace(relation.Source.ControlDetail)){using var document=JsonDocument.Parse(relation.Source.ControlDetail);if(document.RootElement.TryGetProperty("nameField",out var name))nameField=name.GetString()!;}
            if(nameField=="id"||!relation.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联目标缺少已接通的固定候选来源。");
            ValidateTargetDeclarations(model,relation,target);
            var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField);
            if(display is null)throw new InvalidOperationException("未知名称候选仍待完整接线。");
            if(target.IsGlobal)_=GlobalSingleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);
            if(!Physical(model,target,display))
            {
                var candidate=RelationCompilation.ForPage(model,page).SingleOrDefault(candidate=>candidate.SourceColumn.Source.Id==relation.Source.Id)??throw new InvalidOperationException("派生名称缺少固定候选关系。");
                _=DerivedRelationNamesCompilation.Require(model,table,page,candidate);
            }
            var alias=relation.Source.ColumnName[..^3]+"_"+nameField;MetadataValidation.RequireIdentifier(alias);
            if(!outputs.Any(output=>output.Storage.Equals(alias,StringComparison.OrdinalIgnoreCase)))outputs.Add(new(alias,GenerationModel.Pascal(alias),GenerationModel.Camel(alias),""));
        }
        var selectionNames=SelectionNameFilterCompilation.ForPage(model,table,page);
        var dependencies=page.Searchable.Concat(page.Sortable).Concat(ExactDataManageCompilation.Resolve(model,table,page) is {} exact ? new[]{exact.Column} : Array.Empty<ColumnModel>()).Concat(FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>scope.Column)).Concat(FixedPageQueryCompilation.For(table,page).DefaultOrder.Select(item=>item.Column)).Select(column=>column.Source.ColumnName).Concat(selectionNames.Bindings.Select(binding=>binding.StorageColumn.Source.ColumnName)).Concat(page.Condition?.Syntax.Fields??Array.Empty<string>()).Concat(CrossTableFilterCompilation.ForPage(model,table,page).SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // 跨表层名和显式开放的自树叶名都在分页前按实际路径求值；其余级联名称不能作为查询依赖。
        var queryNames=crossPlans.SelectMany(plan=>plan.Levels).Select(level=>level.Name.Source.Id)
            .Concat(SelfTreeNameQueries(model,table,page).Select(tree=>CascadeTreeViewRenderer.Name(table,tree.Source)!.Source.Id))
            .ToHashSet(StringComparer.Ordinal);
        if(CascadeTreeWriteCompilation.ManagedNames(table).Any(name=>!queryNames.Contains(name.Source.Id)&&dependencies.Contains(name.Source.ColumnName,StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("非冗余级联名称的筛选/排序/匹配用途缺少完整路径证明。");
        // 图只取来源已授权行的所选ID；隐藏ID为私有投影依赖，不加入VO或页面字段。
        var readOutputs=outputs.Select(output=>output.Storage).Concat(CascadeTreeViewRenderer.ForPage(model,table,page).Select(plan=>plan.Source.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var singleTargets=ProjectSingleTargets(model,table,page);
        var crossBindings=NonRedundantCrossTableCompilation.Bindings(crossPlans);
        var compiled=CompilePage(model,table,readOutputs,dependencies,crossBindings,singleTargets);
        if(GlobalSelectionPage(table,page))
            _=GlobalAccountMultiPage(table,page)
                ?NonRedundantReads.CompileGlobalAccountMultiPage(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,crossBindings)
                :GlobalAccountSinglePage(table,page)
                    ?NonRedundantReads.CompileGlobalAccountSinglePage(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,crossBindings)
                :crossPlans.Count>0
                ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies,crossBindings)
                :FixedPageConditionCompilation.HasSelfTreeFields(page)
                    ?NonRedundantReads.CompileCrossPageCandidate(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies)
                    :CascadeTreeReadCompilation.ForService(model,table,page).Count>0
                        ?NonRedundantReads.CompileGlobalPage(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies)
                        :NonRedundantReads.CompileGlobalCandidate(model.Catalog,table.Source.Id,page.Source.Id,readOutputs,dependencies);
        outputs=outputs.Select(output=>output.Type.Length>0?output:output with{Type=compiled.OutputTypes[output.Storage] switch{"string"=>"string?","int64"=>"long?","int32"=>"int?","decimal"=>"decimal?","double"=>"double?","bool"=>"bool?","datetime" or "date"=>"DateTime?","time"=>"TimeSpan?",_=>throw new InvalidOperationException("自动显示字段类型尚未接线。")}}).ToList();
        if(outputs.Select(output=>output.Property).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count)throw new InvalidOperationException("派生显示属性冲突。");
        return new(outputs,dependencies,readOutputs,singleTargets);
    }

    /// <summary>业务页单值读取只使用实际项目内非冗余目标和可证明的物理父范围。</summary>
    internal static bool UsesProjectSingleRead(RelationModel relation)=>
        !relation.Target.IsGlobal&&relation.Target.Source.IsRongyu==0&&relation.ParentBinding?.Declared!=true&&
        (relation.TargetPage.Condition is not null||relation.TargetPage.Scopes.Any(scope=>scope.Source=="parent"));

    /// <summary>同一来源表的各页分别固定实际目标页，物理父键按本页同父或表单关系证明。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> ProjectSingleTargets(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.IsGlobal||table.Source.IsRongyu!=0)return [];
        var result=new List<NonRedundantSingleTargetScope>();
        var relations=RelationCompilation.ForPage(model,page).Concat(RelationCompilation.ForPage(model,page,true))
            .DistinctBy(relation=>relation.SourceColumn.Source.Id).Where(UsesProjectSingleRead);
        foreach(var relation in relations)
        {
            ColumnModel? parent=null;
            if(relation.TargetPage.Scopes.Any(scope=>scope.Source=="parent"))
            {
                var context=ControlRenderer.RelationContext(page,relation,false);
                parent=context.SameParent?page.Scopes.Single(scope=>scope.Source=="parent").Column:
                    table.Columns.SingleOrDefault(column=>column.JsonName==context.TargetParentField);
                if(parent is null||!Physical(model,table,parent)||parent.Type is not("string?" or "long?" or "long"))
                    throw new NotSupportedException("项目内实时名称缺少当前页已证明的物理来源父键。");
            }
            result.Add(new(relation.SourceColumn.Source.Id,relation.TargetPage.Source.Id,parent?.Source.Id));
        }
        return result;
    }

    /// <summary>生成验证与导出窄投影使用完全相同的各页固定目标。</summary>
    internal static NonRedundantReadPlan CompilePage(GenerationModel model,TableModel table,IReadOnlyList<string> outputs,
        IReadOnlyList<string> dependencies,IReadOnlyList<NonRedundantCrossTableScope> crosses,
        IReadOnlyList<NonRedundantSingleTargetScope> singles)=>
        singles.Count==0?NonRedundantReads.CompileWithCrossTables(model.Catalog,table.Source.Id,outputs,crosses,dependencies,deferCascadeNames:true):
            NonRedundantReads.CompileProjectPage(model.Catalog,table.Source.Id,outputs,dependencies,crosses,singles);

    /// <summary>列表、详情与导出下发同一固定页绑定，旧的无条件生成保持原调用。</summary>
    internal static string PagePlanCall(GenerationModel model,TableModel table,PageModel page,
        NonRedundantPagePlan plan,string outputs,string dependencies)
    {
        var arguments=JsonSerializer.Serialize(table.Source.Id)+","+outputs+","+dependencies;
        var cross=NonRedundantCrossTableCompilation.BindingArgument(model,table,page);
        if(plan.SingleTargets.Count==0)
            return (cross.Length==0?"Compile":"CompileCross")+"("+arguments+cross+")";
        return "CompileProjectPage("+arguments+(cross.Length==0?",Array.Empty<NonRedundantCrossTableScope>()":cross)+
            SingleTargetArgument(plan.SingleTargets)+")";
    }

    /// <summary>业务页和记录规则的生成代码复用同一目标页与物理父键字面量。</summary>
    internal static string SingleTargetArgument(IReadOnlyList<NonRedundantSingleTargetScope> scopes)
    {
        if(scopes.Count==0)return "";
        var targets=string.Join(",",scopes.Select(scope=>"new("+JsonSerializer.Serialize(scope.ColumnId)+","+
            JsonSerializer.Serialize(scope.PageId)+","+JsonSerializer.Serialize(scope.SourceParentId)+")"));
        return ",new NonRedundantSingleTargetScope[]{"+targets+"}";
    }

    /// <summary>读写共用同一关联目标；相互冲突的有效声明不能按不同优先级串表。</summary>
    private static void ValidateTargetDeclarations(GenerationModel model,ColumnModel relation,TableModel target)
    {
        if(string.IsNullOrWhiteSpace(relation.Source.ControlDetail))return;
        using var document=JsonDocument.Parse(relation.Source.ControlDetail);var definition=document.RootElement;
        if(definition.TryGetProperty("tableId",out var id)&&model.Tables.SingleOrDefault(table=>table.Source.Id==id.ToString()) is {} byId&&byId.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联读取和写入目标声明不一致。");
        if(definition.TryGetProperty("table",out var name)&&model.Tables.SingleOrDefault(table=>table.Source.TableName==name.ToString()) is {} byName&&byName.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联表名和ID声明不一致。");
        if(definition.TryGetProperty("pageId",out var page)&&model.Tables.SingleOrDefault(table=>table.Pages.Any(item=>item.Source.Id==page.ToString())) is {} byPage&&byPage.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联页面和表声明不一致。");
    }
    /// <summary>沿原逐键覆盖语义比较有效JSON；空覆盖、属性顺序及格式差异不改变读取计划。</summary>
    private static bool HasEffectiveOverride(string source,string overlay)
    {
        try
        {
            var original=JsonNode.Parse(string.IsNullOrWhiteSpace(source)?"{}":source) as JsonObject??throw new InvalidOperationException("关系定义必须是JSON对象。");
            var changes=JsonNode.Parse(overlay) as JsonObject??throw new InvalidOperationException("页级关系定义必须是JSON对象。");var effective=original.DeepClone().AsObject();
            foreach(var pair in changes)effective[pair.Key]=pair.Value?.DeepClone();return !JsonNode.DeepEquals(original,effective);
        }
        catch(Exception error)when(error is JsonException or ArgumentException){throw new InvalidOperationException("关系覆盖必须是有效无重复键的JSON对象。",error);}
    }
    /// <summary>只接受已编译的候选条件和排序覆盖；目标与显示名称仍沿字段定义。</summary>
    private static bool ProvenOptionsOverride(GenerationModel model, PageModel page, ColumnModel source, string overlay)
    {
        var original = JsonNode.Parse(string.IsNullOrWhiteSpace(source.Source.ControlDetail) ? "{}" : source.Source.ControlDetail) as JsonObject
            ?? throw new InvalidOperationException("关系定义必须是JSON对象。");
        var changes = JsonNode.Parse(overlay) as JsonObject
            ?? throw new InvalidOperationException("页级关系定义必须是JSON对象。");
        var effective = original.DeepClone().AsObject();
        foreach (var pair in changes)
            effective[pair.Key] = pair.Value?.DeepClone();

        var conditionsChanged = !JsonNode.DeepEquals(original["whereSql"], effective["whereSql"]);
        var orderChanged = !JsonNode.DeepEquals(original["orderBy"], effective["orderBy"]);
        original.Remove("whereSql");
        effective.Remove("whereSql");
        original.Remove("orderBy");
        effective.Remove("orderBy");
        if (!JsonNode.DeepEquals(original, effective))
            return false;

        var compiled = RelationCompilation.ForPage(model, page)
            .SingleOrDefault(relation => relation.SourceColumn.Source.Id == source.Source.Id);
        return compiled is not null &&
            compiled.Target.Source.Id == ForeignValidationRenderer.Target(model, source)?.Source.Id &&
            (!conditionsChanged || compiled.Conditions is not null) &&
            (!orderChanged || compiled.OptionsOrder is not null);
    }
    /// <summary>没有页面的依赖表仍必须先验证物理结构；本入口不写任何生成文件。</summary>
    internal static void Validate(GenerationModel model,TableModel table)
    {try{foreach(var page in table.Pages)ForPage(model,table,page);}catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException){throw new InvalidOperationException("非冗余派生字段读取批次未完成："+error.Message,error);}}
}
