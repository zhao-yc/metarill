using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FunctionTableGenerationTests : IDisposable
{
    private readonly string output = Path.Combine(Path.GetTempPath(), "metarill-function-form", Guid.NewGuid().ToString("N"));

    /// <summary>从真实 Full 包验证原表单、候选权限、功能菜单与无持久化边界。</summary>
    [Theory]
    [InlineData(1, "physical")]
    [InlineData(0, "physical")]
    [InlineData(0, "derived")]
    [InlineData(0, "logical")]
    [InlineData(0, "ordinary")]
    [InlineData(0, "multi")]
    [InlineData(0, "shared")]
    public async Task Function_form_packages_its_original_detail_and_authorized_options_without_a_mapper(int scopedTargetStorage,string scope)
    {
        var catalog = WithScopedCheckbox(WithCascades(Catalog()), scopedTargetStorage);
        if(scope!="physical")ConfigureDerivedCheckboxScope(catalog,scope);
        var logical=scope is "logical" or "ordinary" or "multi" or "shared";
        new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot());

        var frontend = Path.Combine(output, "frontend");
        var detailPath = Path.Combine(frontend, "pages/yzp/summary_form-detail-102.vue");
        var detail = File.ReadAllText(detailPath);
        Assert.Contains("page_is_function_table:true", detail);
        Assert.Contains("page_is_delete:false", detail);
        Assert.Contains("page_is_update:true", detail);
        Assert.Contains("title", detail);
        Assert.Contains("findNameIdCustomer24", detail);
        Assert.Contains("createMultiSelectFieldMixin", detail);
        Assert.Contains("customer-list-101.vue", detail);
        Assert.Contains(":selectedIds=\"entity.customerIds\"", detail);
        Assert.Contains(":selectedNames=\"entity.customerNames\"", detail);
        Assert.Contains("receiveMultiSelection", detail);
        Assert.Contains("findNameIdApproved26", detail);
        Assert.Contains("v-model='entity.approvedIds'", detail);
        Assert.Contains("readonlyDisplayText(entity.approvedNames)", detail);
        Assert.Contains("cascadeTreePath27", detail);
        Assert.Contains("cascadeTreeOptions28", detail);
        Assert.Contains("cascadeTreeOptions29", detail);
        Assert.Contains("leaf-id-property='treeNodeId'", detail);
        Assert.Contains("leaf-id-property='placeId'", detail);
        Assert.Contains("<yzp-file-library v-model='entity.attachments' config-column-id='37' page-id='102' table-id='2'",detail);
        Assert.Contains(":record-id='id'",detail);
        Assert.Contains("/admin/sys/config_button/execute", detail);
        Assert.Contains("saveEntity", detail);
        Assert.False(File.Exists(Path.Combine(frontend, "pages/yzp/summary_form-list-102.vue")));
        var pages = File.ReadAllText(Path.Combine(frontend, "pages.json"));
        Assert.Contains("summary_form-detail-102", pages);
        Assert.DoesNotContain("summary_form-list-102", pages);
        Assert.DoesNotContain("\"102\"", File.ReadAllText(Path.Combine(frontend,"common/yzp_runtime/page-write-schema.generated.json")));

        var backend = Path.Combine(output, "backend/generated");
        Assert.False(File.Exists(Path.Combine(backend, "Entities/SummaryForm.g.cs")));
        Assert.False(File.Exists(Path.Combine(backend, "Mappers/SummaryFormMapper.g.cs")));
        var controller = File.ReadAllText(Path.Combine(backend, "Controllers/SummaryFormPage102Controller.g.cs"));
        Assert.Contains("findNameIdCustomer24/page", controller);
        Assert.Contains("authorization.RequirePagePermission(\"102\", operation)", controller);
        Assert.Contains("authorization.RequirePagePermission(\"101\", \"list\")", controller);
        Assert.DoesNotContain("[HttpPost(\"page\")]", controller);
        Assert.DoesNotContain("[HttpGet(\"detail/{id}\")]", controller);
        Assert.DoesNotContain("[HttpDelete]", controller);
        Assert.Contains("RelationOptionAuthorization.Require(authorization,\"102\",false,true)",controller);
        Assert.Contains("CheckboxOptions102_26Async(request,actor,cancellationToken)",controller);
        Assert.Contains("CascadePath102_27Async(request,Actor(\"list\")",controller);
        Assert.Contains("CrossPath102_28Async(request,Actor(\"list\")",controller);
        Assert.Contains("CrossOptions102_29Async(request,Actor(\"list\")",controller);
        var targetController = File.ReadAllText(Path.Combine(backend,"Controllers/CustomerPage101Controller.g.cs"));
        Assert.Contains("[HttpPost(\"selection/selected\")]",targetController);
        Assert.Contains("SelectionSelected101Async(request, Actor(\"list\"), cancellationToken)",targetController);
        var service = File.ReadAllText(Path.Combine(backend, "Services/SummaryFormService.g.cs"));
        Assert.Contains("Select<global::Example.FunctionForm.Generated.Entities.Customer>", service);
        Assert.Contains("entity.YzProjectId == long.Parse(actor.ProjectId", service);
        Assert.DoesNotContain("InsertAsync", service);
        Assert.DoesNotContain("MultiName",service);
        Assert.Contains("ReadCheckboxForm102_26",service);
        Assert.Contains("CheckboxTargetCondition102_26",service);
        Assert.Contains("findNameIdScoped38",detail);
        var label=logical?"scopedCountryNames":scopedTargetStorage==0?"scopedCountryLabels":"scopedNames";
        Assert.Contains($"readonlyDisplayText(entity.{label})",detail);
        Assert.Contains("Select<global::Example.FunctionForm.Generated.Entities.FunctionCountry>",service);
        Assert.Contains("parentQuery.Where(row=>row.DeleteNo==null)",service);
        if(logical)
        {
            Assert.DoesNotContain("row.CountryId==long.Parse(parentId!",service);
            Assert.Contains("filters.Add(new(\"country_id\",FixedReadComparison.Equal,",service);
        }
        else Assert.Contains("row.CountryId==long.Parse(parentId!",service);
        Assert.Contains("ResolveExactDataManage(\"103\", \"list\", \"real_name\")",service);
        Assert.Contains("CheckboxOptions102_38Async(request,actor,cancellationToken)",controller);
        if(scopedTargetStorage==0)
        {
            if(logical)
            {
                Assert.Contains("CompileExactCheckboxParentCandidate(\"1\",\"103\"",service);
                Assert.Contains(scope=="shared"?"FixedReadFilter.ForSharedExact(\"assigned_area_name\",":
                    "FixedReadFilter.ForCrossExact(\"assigned_area_name\",",service);
            }
            else
            {
                if(scope=="physical")Assert.Contains("CompileMultiLabel(\"1\",\"country_label\"",service);
                Assert.Contains("CheckboxTargetCondition102_38.BindAccount(actor)",service);
                Assert.Contains(scope=="derived"?"filters.Add(new(\"country_label\", FixedReadComparison.In,":
                    "filters.Add(new(\"owner_code\", FixedReadComparison.In,",service);
            }
            Assert.Contains("DerivedCheckboxPlan102_38.BindAccount(actor)",service);
        }
        else
        {
            Assert.Contains("ExactDataManageSql.Apply(query, session, \"owner_code\"",service);
            Assert.Contains("CheckboxTargetCondition102_38",service);
        }
        Assert.DoesNotContain("ValidateCheckbox",service);
        Assert.Contains("CrossTargetPlan102_29_1",service);
        Assert.Contains("CascadeScope102_27(RuntimeActor",service);
        Assert.Contains("relationAuthorization.RequirePagePermission(\"201\",\"list\")",service);
        Assert.Contains("relationAuthorization.RequirePagePermission(\"401\",\"list\")",service);
        Assert.DoesNotContain("ValidateCascade",service);
        var nav = File.ReadAllText(Path.Combine(output, "backend/Application.Host/DevelopmentNavigation.cs"));
        Assert.Contains("table.IsFunctionTable == 1 ? \"-detail-\" : \"-list-\"", nav);
        Assert.Contains("countEndpoint=table.IsFunctionTable==1?null", nav);
        var physical=MySqlSchemaPlanner.Compile(catalog,"form_test").Tables.Select(table=>table.Name).Order(StringComparer.Ordinal);
        Assert.Equal(new[]{"customer","function_area","function_country","function_place","function_tree_node"},physical);

        await VerifyVueAsync(output,"GeneratedFunctionFormProbe.mjs");
        await VerifyVueAsync(output,"GeneratedFunctionFormCascadeProbe.mjs");
        await VerifyVueAsync(output,"GeneratedFunctionFormFileProbe.mjs");
        await BuildFullAsync(output);

        // 严格构建后再装载类型，确认下发的精简目录可以初始化固定读取计划。
        CheckCheckboxPlan(output, scopedTargetStorage, logical);
    }

    /// <summary>功能表的业务键沿用原控件和远端只读权限，整个包不生成来源持久化。</summary>
    [Theory]
    [InlineData("int64","physical","9007199254740995")]
    [InlineData("string","physical","001")]
    [InlineData("string","derived","A-01")]
    [InlineData("string","global","Ab-01")]
    public async Task Function_self_tree_preserves_the_published_business_key(string keyType,string targetKind,string selected)
    {
        const string ns="Example.FunctionForm";
        var catalog=WithCascades(Catalog());
        var source=catalog.ConfigColumns.Single(column=>column.Id=="27");
        source.LogicalType=keyType;
        source.DefaultValue=selected;
        catalog.ConfigColumns.Single(column=>column.Id=="306").LogicalType=keyType;
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        if(targetKind=="global")
        {
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            targetPage.WhereSql="";
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="303");
        }
        if(targetKind=="derived")
        {
            target.IsRongyu=0;
            var label=catalog.ConfigColumns.Single(column=>column.Id=="302");
            label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.name");
            catalog.ConfigColumns.Add(new(){Id="309",ConfigTableId="3",YzProjectId=catalog.YzProjectId,ColumnName="dictionary_id",
                LogicalType="int64",ControlKind="List",LinkConfigTableId="4",ControlDetail="""{"tableId":"4","pageId":"301","nameField":"name"}"""});
        }
        NonRedundantSelfTreeTests.AddBusinessTreeKey(catalog,"3","27",keyType);
        Assert.Contains("alternate_tree_index",CascadeTreeStorageColumns.RequiredNodeIndexes(catalog));
        Assert.Contains(GenerationMetadataPublisher.ForRelations(catalog).ConfigIndexes,index=>
            index.GetProperty("id").GetString()=="alternate_tree_index");

        var project=GenerationModel.Create(catalog,ns);
        var owner=project.Tables.Single(table=>table.Source.Id=="2");
        var page=owner.Pages.Single(instance=>instance.Source.Id=="102");
        var plan=CascadeTreeReadCompilation.Resolve(project,owner,page,owner.Columns.Single(column=>column.Source.Id=="27"));
        Assert.True(plan.Definition is not null,plan.Reason);
        Assert.Equal(keyType,plan.Definition.Key.Source.LogicalType);
        new ApplicationPackager().Create(catalog,output,ns,SourceRoot());
        var generated=Path.Combine(output,"backend/generated");
        var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/SummaryFormService.g.cs"));
        Assert.Contains("relationAuthorization.RequirePagePermission(\"201\",\"list\")",service);
        Assert.Contains(keyType=="string"?"request.ParseTextLeaf()":"row.NodeBusinessKey==id",service);
        if(targetKind=="derived")Assert.Contains("DerivedCascadePlan102_27",service);
        Assert.DoesNotContain("ValidateCascade",service);
        Assert.DoesNotContain("InsertAsync",service);
        Assert.False(File.Exists(Path.Combine(generated,"Entities/SummaryForm.g.cs")));
        Assert.False(File.Exists(Path.Combine(generated,"Mappers/SummaryFormMapper.g.cs")));
        Assert.DoesNotContain(MySqlSchemaPlanner.Compile(catalog,"form_test").Tables,table=>table.Name=="summary_form");
        await VerifyVueAsync(output,"GeneratedFunctionFormCascadeProbe.mjs",selected);
        await BuildFullAsync(output);

        var context=new AssemblyLoadContext("FunctionSelfTree."+Guid.NewGuid().ToString("N"),isCollectible:true);
        try
        {
            using var binary=File.OpenRead(Path.Combine(output,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
            var runtime=context.LoadFromStream(binary).GetType(ns+".Generated.Services.SummaryFormService",true)!;
            RuntimeHelpers.RunClassConstructor(runtime.TypeHandle);
            Assert.NotNull(runtime.GetMethod("CascadeOptions102_27Async"));
            Assert.NotNull(runtime.GetMethod("CascadePath102_27Async"));
        }
        finally { context.Unload(); }

        var overlay=catalog.ConfigPageColumns.Single(column=>column.ConfigPageId=="102"&&column.ConfigColumnId=="27");
        var altered=System.Text.Json.Nodes.JsonNode.Parse(source.ControlDetail)!;
        altered["selfTree"]!["idField"]="id";
        overlay.Extra["controlDetail"]=JsonSerializer.SerializeToElement(altered);
        Assert.Contains("页级配置不能替换表级业务节点键",Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog)).Message);
        overlay.Extra.Remove("controlDetail");
        catalog.ConfigIndexes.Clear();
        Assert.Contains(targetKind=="global"?"全局自树业务键需要单列唯一索引":"联合唯一索引",
            Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog)).Message);
    }

    /// <summary>实际生成的 type=2 目标在列表和已选回读中使用同一精确权限和父范围。</summary>
    [Theory]
    [InlineData("physical")]
    [InlineData("nested")]
    [InlineData("logical")]
    public async Task Function_multiselect_uses_the_published_parent_and_exact_scope(string scenario)
    {
        var catalog=WithScopedCheckbox(WithCascades(Catalog()),scenario=="physical"?1:0);
        var target=catalog.ConfigPages.Single(page=>page.Id=="103");
        var name="name";
        if(scenario=="logical")ConfigureDerivedCheckboxScope(catalog,"multi");
        else if(scenario=="nested")
        {
            ConfigureDerivedCheckboxScope(catalog,"derived");
            name="country_label";
            // 中间页使用已发布的 R0 读取合同，确保账号变量确实进入联接计划。
            catalog.ConfigTables.Single(table=>table.Id=="4").IsRongyu=0;
            catalog.ConfigColumns.Add(new(){Id="407",ConfigTableId="4",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql+=" AND store_id=@current_store_id";
            catalog.ConfigPageColumns.Add(new(){Id="10319",ConfigPageId="103",ConfigColumnId="19",IsShow=1});
        }
        target.Extra["type"]=JsonSerializer.SerializeToElement(2);
        catalog.ConfigColumns.Add(new(){Id="39",ConfigTableId="2",ColumnName="review_ids",LogicalType="string",
            ControlKind="MultiSelectPage",LinkConfigTableId="1",IsVirtualColumn=1,
            ControlDetail=JsonSerializer.Serialize(new{pageId="103",nameField=name})});
        catalog.ConfigPageColumns.Add(new(){Id="10239",ConfigPageId="102",ConfigColumnId="39",IsEdit=1});
        new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot());

        var generated=Path.Combine(output,"backend/generated");
        var form=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/summary_form-detail-102.vue"));
        Assert.Contains("customer-list-103.vue",form);
        Assert.Contains("reviewCountryId",form);
        var controller=await File.ReadAllTextAsync(Path.Combine(generated,"Controllers/CustomerPage103Controller.g.cs"));
        Assert.Contains("SelectionSelected103Async(request, Actor(\"list\")",controller);
        var code=await File.ReadAllTextAsync(Path.Combine(generated,"Services/CustomerService.g.cs"));
        Assert.Contains("ResolveExactDataManage(\"103\", authorizationOperation, \"real_name\")",code);
        var index=code.IndexOf(" SelectionSelected103Async(",StringComparison.Ordinal);
        Assert.True(index>=0);
        var selected=code[index..];
        Assert.Contains("await ValidateSelectionParents103Async(request.ParentId",selected);
        Assert.Contains(scenario=="physical"?"entities.Count != selected.Length":"selectedRows.Count!=selected.Length",selected);
        if(scenario!="physical")
        {
            Assert.Contains("PageAsync(ReadPlan103.BindAccount(actor)",code);
            Assert.Contains("KeyRowsAsync(ReadPlan103.BindAccount(actor)",selected);
            Assert.Contains("ReadFilters103(new PageRequest{ParentId=request.ParentId}",selected);
        }

        await VerifyVueAsync(output,"GeneratedFunctionScopedMultiProbe.mjs");
        await BuildFullAsync(output);
        if(scenario!="nested")return;
        var context=new AssemblyLoadContext("ScopedFunctionMulti."+Guid.NewGuid().ToString("N"),isCollectible:true);
        try
        {
            using var binary=File.OpenRead(Path.Combine(output,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
            var service=context.LoadFromStream(binary).GetType("Example.FunctionForm.Generated.Services.CustomerService",true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var binding=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField("ReadPlan103",binding)!.GetValue(null));
            Assert.True(plan.RequiresAccount);
        }
        finally {context.Unload();}
    }

    /// <summary>独立表单父键验证已发布全局父页。候选与确认共用同一页和同一读快照。</summary>
    [Theory]
    [InlineData("global")]
    [InlineData("globalRowProject")]
    [InlineData("project")]
    [InlineData("redundant")]
    [InlineData("globalStored")]
    [InlineData("globalStoredCompound")]
    public async Task Function_multiselect_uses_the_published_global_parent(string scenario)
    {
        var catalog=WithGlobalParent(Catalog(),scenario);
        Assert.Equal("802",Metarill.Data.Queries.NonRedundantReads.MultiGlobalParentPageId(catalog,"7","103"));
        new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot());

        var generated=Path.Combine(output,"backend/generated");
        var controller=await File.ReadAllTextAsync(Path.Combine(generated,"Controllers/FunctionOptionPage103Controller.g.cs"));
        Assert.Contains("SelectionSelected103Async(request, Actor(\"list\")",controller);
        var code=await File.ReadAllTextAsync(Path.Combine(generated,"Services/FunctionOptionService.g.cs"));
        Assert.Contains("CompileMultiGlobalParent(\"7\",\"103\")",code);
        Assert.Contains("PageMultiGlobalParent103.BindAccount(actor)",code);
        Assert.Contains("await ValidateParents103Async(parentId,actor,cancellationToken,locking:false)",code);
        var start=code.IndexOf(" SelectionSelected103Async(",StringComparison.Ordinal);
        Assert.True(start>=0);
        var selected=code[start..];
        Assert.Contains("InReadSnapshotAsync",selected[..selected.IndexOf("ArgumentNullException",StringComparison.Ordinal)]);
        if(scenario.StartsWith("globalStored",StringComparison.Ordinal))
        {
            // 冗余实体的分页、数量和已选使用同一普通实体查询，显式项目及固定账号条件始终生效。
            Assert.Contains("query = query.Where(entity => entity.YzProjectId == long.Parse(actor.ProjectId",code);
            Assert.DoesNotContain("if (!candidateRows) query = query.Where(entity => entity.YzProjectId",code);
            Assert.Contains("PageCondition103.BindAccount(actor).ApplyConstants",code);
            Assert.Contains("var query = Query103(new PageRequest { ParentId = request.ParentId",selected);
            if(scenario=="globalStoredCompound")Assert.Contains("query = query.Where(entity => entity.OtherScopeId",code);
        }
        else if(scenario.StartsWith("global",StringComparison.Ordinal))
        {
            Assert.Contains("PageAsync(CandidateReadPlan103.BindAccount(actor)",code);
            Assert.Contains("KeyRowsAsync(CandidateReadPlan103.BindAccount(actor)",selected);
        }
        else
        {
            Assert.Contains("ResolveExactDataManage(\"103\", authorizationOperation, \"real_name\")",code);
        }

        await VerifyVueAsync(output,"GeneratedFunctionScopedMultiProbe.mjs");
        await BuildFullAsync(output);
        var context=new AssemblyLoadContext("GlobalParentFunctionMulti."+Guid.NewGuid().ToString("N"),isCollectible:true);
        try
        {
            using var binary=File.OpenRead(Path.Combine(output,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
            var service=context.LoadFromStream(binary).GetType("Example.FunctionForm.Generated.Services.FunctionOptionService",true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var binding=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("PageMultiGlobalParent103",binding)!.GetValue(null));
            Assert.True(plan.RequiresAccount);
        }
        finally {context.Unload();}
    }

    /// <summary>复选独立父级使用实际指定父页，同步读取固定条件和当前账号范围。</summary>
    [Theory]
    [InlineData("global")]
    [InlineData("globalRowProject")]
    [InlineData("project")]
    [InlineData("redundant")]
    [InlineData("ordinary")]
    [InlineData("globalStored")]
    [InlineData("globalStoredCompound")]
    public async Task Function_checkbox_uses_the_published_global_parent(string scenario)
    {
        var catalog=WithGlobalParent(Catalog(),scenario=="ordinary"?"redundant":scenario);
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="103");
        if(scenario=="ordinary")targetPage.Extra.Remove("type");
        var field=catalog.ConfigColumns.Single(column=>column.Id=="39");
        field.ControlKind="MultiCheckBoxDatabase";
        Assert.Equal("802",Metarill.Data.Queries.NonRedundantReads.MultiGlobalParentPageId(catalog,"7","103"));
        if(scenario=="globalStoredCompound")
        {
            // 同一父表的另一发布页不代表相同账号范围。
            var other=catalog.ConfigColumns.Single(column=>column.Id=="708");
            var original=other.Extra["linkConfigPageId"];
            try
            {
                other.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("801");
                var error=Assert.Throws<InvalidOperationException>(()=>
                    Metarill.Data.Queries.NonRedundantReads.MultiGlobalParentPageId(catalog,"7","103"));
                Assert.Contains("同一个实际发布父页",error.Message);
            }
            finally{other.Extra["linkConfigPageId"]=original;}
        }
        new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot());

        var generated=Path.Combine(output,"backend/generated");
        var controller=await File.ReadAllTextAsync(Path.Combine(generated,"Controllers/SummaryFormPage102Controller.g.cs"));
        Assert.Contains("CheckboxOptions102_39Async(request,actor,cancellationToken)",controller);
        var code=await File.ReadAllTextAsync(Path.Combine(generated,"Services/SummaryFormService.g.cs"));
        Assert.Contains("CheckboxGlobalParent102_39=global::Example.FunctionForm.Generated.GeneratedNonRedundantReadCatalog.CompileMultiGlobalParent(\"7\",\"103\")",code);
        Assert.Contains("CheckboxGlobalParent102_39.BindAccount(actor)",code);
        Assert.Contains("row.ScopeId==long.Parse(parentId!",code);
        if(scenario=="globalStoredCompound")Assert.Contains("row.OtherScopeId==long.Parse(parentId!",code);
        var global=scenario.StartsWith("global",StringComparison.Ordinal);
        if(global)
        {
            Assert.Contains("PageAsync(DerivedCheckboxPlan102_39.BindAccount(actor)",code);
            if(scenario=="globalRowProject")
                Assert.Contains("new(\"yz_project_id\",FixedReadComparison.Equal,long.Parse(actor.ProjectId",code);
        }
        else
        {
            Assert.Contains("CheckboxTargetCondition102_39.BindAccount(actor)",code);
            Assert.Contains("ResolveExactDataManage(\"103\", \"list\", \"real_name\")",code);
            Assert.Contains("ExactDataManageSql.Apply(query, session, \"name\"",code);
        }
        var reader=code[code.IndexOf(" CheckboxOptions102_39Async(",StringComparison.Ordinal)..];
        var snapshot=reader.IndexOf("InReadSnapshotAsync",StringComparison.Ordinal);
        var read=reader.IndexOf(global?"DerivedCheckboxRows102_39Async(request.ParentId":"CheckboxScope102_39Async(request.ParentId",StringComparison.Ordinal);
        Assert.True(snapshot>=0&&read>snapshot);

        await VerifyVueAsync(output,"GeneratedFunctionGlobalCheckboxProbe.mjs");
        await BuildFullAsync(output);
        var context=new AssemblyLoadContext("GlobalParentFunctionCheckbox."+Guid.NewGuid().ToString("N"),isCollectible:true);
        try
        {
            using var binary=File.OpenRead(Path.Combine(output,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
            var service=context.LoadFromStream(binary).GetType("Example.FunctionForm.Generated.Services.SummaryFormService",true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var binding=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("CheckboxGlobalParent102_39",binding)!.GetValue(null));
            // 另一个父页没有账号条件。冻结计划必须读取显式指定的父页。
            Assert.True(plan.RequiresAccount);
            if(global)
            {
                var options=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    service.GetField("DerivedCheckboxPlan102_39",binding)!.GetValue(null));
                Assert.True(options.RequiresAccount);
            }
        }
        finally {context.Unload();}
    }

    /// <summary>使用两个全局父页面，显式绑定第二页的物理键和账号范围。</summary>
    private static MetadataCatalog WithGlobalParent(MetadataCatalog catalog,string scenario)
    {
        var global=scenario.StartsWith("global",StringComparison.Ordinal);
        var parent=new TableMetadata {Id="8",TableName="function_scope",Name="全局范围",YzProjectId="100",IsRongyu=1,
            Extra=new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}};
        var target=new TableMetadata {Id="7",TableName="function_option",Name="范围候选",YzProjectId="100",
            IsRongyu=scenario=="redundant"||scenario.StartsWith("globalStored",StringComparison.Ordinal)?1:0};
        if(global)target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigTables.AddRange([parent,target]);
        foreach(var table in new[]{parent,target})
            catalog.ConfigColumns.AddRange([
                new(){Id=table.Id+"01",ConfigTableId=table.Id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=table.Id+"02",ConfigTableId=table.Id,ColumnName="name"},
                new(){Id=table.Id+"03",ConfigTableId=table.Id,ColumnName="status",LogicalType="int32"},
                new(){Id=table.Id+"04",ConfigTableId=table.Id,ColumnName="delete_no",LogicalType="datetime"},
                new(){Id=table.Id+"05",ConfigTableId=table.Id,ColumnName="store_id",LogicalType="int64"}
            ]);
        if(scenario!="global")
            catalog.ConfigColumns.Add(new(){Id="706",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"});
        var published=new PageMetadata {Id="103",ConfigTableId="7",YzProjectId="100",YzAdminTypeId="70",Name="范围候选",
            WhereSql=(global&&target.IsRongyu==0?"":"yz_project_id=@current_yz_project_id AND ")+"scope_id=@parent_id AND status=1 AND store_id=@current_store_id",
            Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}};
        if(scenario=="globalStoredCompound")
        {
            catalog.ConfigColumns.Add(new(){Id="708",ConfigTableId="7",ColumnName="other_scope_id",LogicalType="int64",
                LinkConfigTableId="8",Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("802")}});
            published.WhereSql+=" AND other_scope_id=@parentId";
        }
        if(!global)
        {
            published.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");
            catalog.YzAdminTypes.Single(item=>item.Id=="70").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        }
        catalog.ConfigPages.AddRange([
            new(){Id="801",ConfigTableId="8",YzProjectId="100",YzAdminTypeId="70",WhereSql="status=0"},
            new(){Id="802",ConfigTableId="8",YzProjectId="100",YzAdminTypeId="70",WhereSql="status=1 AND store_id=@current_store_id"},
            published
        ]);
        catalog.ConfigColumns.AddRange([
            new(){Id="707",ConfigTableId="7",ColumnName="scope_id",LogicalType="int64",LinkConfigTableId="8",
                Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("802")}},
            new(){Id="39",ConfigTableId="2",ColumnName="review_ids",LogicalType="string",IsVirtualColumn=1,
                ControlKind="MultiSelectPage",LinkConfigTableId="7",ControlDetail="""{"pageId":"103","nameField":"name"}"""},
            new(){Id="94",ConfigTableId="2",ColumnName="review_country_id",Name="全局父级",LogicalType="int64",
                IsVirtualColumn=1,LinkConfigTableId="8"}
        ]);
        catalog.ConfigPageColumns.AddRange([
            new(){Id="801802",ConfigPageId="801",ConfigColumnId="802",IsShow=1},
            new(){Id="802802",ConfigPageId="802",ConfigColumnId="802",IsShow=1},
            new(){Id="103702",ConfigPageId="103",ConfigColumnId="702",IsShow=1},
            new(){Id="10239",ConfigPageId="102",ConfigColumnId="39",IsEdit=1},
            new(){Id="10294",ConfigPageId="102",ConfigColumnId="94",IsEdit=1}
        ]);
        return catalog;
    }

    /// <summary>真实单选目标页承接父范围、派生名称和精确列表权限。</summary>
    [Theory]
    [InlineData("physical")]
    [InlineData("derived")]
    [InlineData("logical")]
    [InlineData("global")]
    public async Task Function_single_selection_uses_the_actual_target_page(string scenario)
    {
        var catalog=WithSingleSelection(Catalog(),scenario);
        new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot());

        var generated=Path.Combine(output,"backend/generated");
        var table=scenario=="global"?"FunctionOption":"Customer";
        var controller=await File.ReadAllTextAsync(Path.Combine(generated,$"Controllers/{table}Page103Controller.g.cs"));
        Assert.Contains("Actor(\"list\")",controller);
        var service=await File.ReadAllTextAsync(Path.Combine(generated,$"Services/{table}Service.g.cs"));
        if(scenario!="global")
        {
            Assert.Contains("ResolveExactDataManage(\"103\", authorizationOperation, \"real_name\")",service);
            if(scenario=="logical")Assert.Contains("FixedReadFilter.ForCrossExact(\"assigned_area_name\"",service);
        }
        else Assert.Contains("CandidateReadPlan103.BindAccount(actor)",service);

        var sourceService=await File.ReadAllTextAsync(Path.Combine(generated,"Services/SummaryFormService.g.cs"));
        Assert.DoesNotContain("findNameIdReview39",sourceService);
        Assert.False(File.Exists(Path.Combine(generated,"Entities/SummaryForm.g.cs")));
        await VerifyVueAsync(output,"GeneratedFunctionSingleProbe.mjs");
        await BuildFullAsync(output);
        var context=new AssemblyLoadContext("FunctionSingleSelection."+Guid.NewGuid().ToString("N"),isCollectible:true);
        try
        {
            using var binary=File.OpenRead(Path.Combine(output,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
            var runtime=context.LoadFromStream(binary).GetType($"Example.FunctionForm.Generated.Services.{table}Service",true)!;
            RuntimeHelpers.RunClassConstructor(runtime.TypeHandle);
        }
        finally {context.Unload();}
    }

    /// <summary>控件独有的条件不能在直达目标页时丢失，无法覆盖时拒绝打包。</summary>
    [Fact]
    public void Function_single_selection_rejects_an_unbound_control_condition()
    {
        var catalog=WithSingleSelection(Catalog(),"physical");
        catalog.ConfigColumns.Single(field=>field.Id=="39").ControlDetail=
            """{"pageId":"103","nameField":"name","whereSql":"name='额外限定'"}""";
        var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot()));
        Assert.Contains("附加范围未被目标固定查询约束",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>覆盖原单选的物理／逻辑独立父、页面覆盖、派生名称及全局候选。</summary>
    private static MetadataCatalog WithSingleSelection(MetadataCatalog catalog,string scenario)
    {
        if(scenario=="global")
        {
            WithGlobalParent(catalog,scenario);
            catalog.ConfigColumns.RemoveAll(field=>field.Id=="39");
            catalog.ConfigPageColumns.RemoveAll(field=>field.Id=="10239");
        }
        else
        {
            WithScopedCheckbox(WithCascades(catalog),scenario=="physical"?1:0);
            if(scenario=="derived")
            {
                ConfigureDerivedCheckboxScope(catalog,"derived");
                catalog.ConfigPageColumns.Add(new(){Id="10319",ConfigPageId="103",ConfigColumnId="19",IsShow=1});
            }
            else if(scenario=="logical")ConfigureDerivedCheckboxScope(catalog,"logical");
        }

        var target=catalog.ConfigPages.Single(page=>page.Id=="103");
        target.Extra["type"]=JsonSerializer.SerializeToElement(1);
        var name=scenario=="derived"?"country_label":"name";
        var field=new ColumnMetadata {Id="39",ConfigTableId="2",ColumnName="review_id",LogicalType="int64",
            IsVirtualColumn=1,ControlKind="SelectPage",LinkConfigTableId=scenario=="global"?"":"1"};
        var setting=new PageColumnMetadata {Id="10239",ConfigPageId="102",ConfigColumnId="39",IsEdit=1};
        if(scenario=="derived")
        {
            field.ControlDetail=JsonSerializer.Serialize(new{nameField=name});
            setting.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"103"}""");
        }
        else
        {
            field.ControlDetail=JsonSerializer.Serialize(new{pageId="103",nameField=name,
                whereSql=scenario=="global"?"status=1":"name <> '停用'"});
        }
        catalog.ConfigColumns.Add(field);
        catalog.ConfigColumns.Add(new(){Id="397",ConfigTableId="2",ColumnName="review_"+name,LogicalType="string",IsVirtualColumn=1});
        catalog.ConfigPageColumns.Add(setting);
        return catalog;
    }

    /// <summary>无来源记录的原下拉使用真实目标页的父范围、名称和精确权限。</summary>
    [Theory]
    [InlineData("physical")]
    [InlineData("derived")]
    [InlineData("logical")]
    [InlineData("global")]
    [InlineData("treeExact")]
    public async Task Function_original_list_uses_one_candidate_scope(string scenario)
    {
        var catalog = WithFunctionList(Catalog(), scenario=="treeExact"?"logical":scenario);
        if(scenario=="treeExact")PublishFunctionListTreeExact(catalog);
        new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot());

        var generated = Path.Combine(output, "backend/generated");
        var controller = await File.ReadAllTextAsync(Path.Combine(generated, "Controllers/SummaryFormPage102Controller.g.cs"));
        Assert.Contains("""[HttpPost("findNameIdReview39/page")]""", controller);
        Assert.Contains("""authorization.RequirePagePermission("103", "list");""", controller);
        var service = await File.ReadAllTextAsync(Path.Combine(generated, "Services/SummaryFormService.g.cs"));
        Assert.Contains("DerivedOptionsPlan102_39", service);
        Assert.Contains("InReadSnapshotAsync", service);
        if (scenario == "global")
        {
            Assert.Contains("CompileMultiGlobalParent(\"7\",\"103\")", service);
            Assert.Contains("DerivedOptionsPlan102_39.BindAccount(actor)", service);
        }
        else
        {
            Assert.Contains("""ResolveExactDataManage("103", "list", "real_name")""", service);
            Assert.Contains("ReadOptionsForm102_39", service);
            if (scenario == "logical") Assert.Contains("""FixedReadFilter.ForCrossExact("assigned_area_name",""", service);
            if (scenario == "treeExact") Assert.Contains("""new("approval_tree_name", FixedReadComparison.In,""", service);
        }
        Assert.False(File.Exists(Path.Combine(generated, "Entities/SummaryForm.g.cs")));
        await VerifyVueAsync(output, "GeneratedFunctionListProbe.mjs", scenario);
        await BuildFullAsync(output);
        var context = new AssemblyLoadContext("FunctionList." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var binary = File.OpenRead(Path.Combine(output, "backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
            var runtime = context.LoadFromStream(binary).GetType("Example.FunctionForm.Generated.Services.SummaryFormService", true)!;
            RuntimeHelpers.RunClassConstructor(runtime.TypeHandle);
        }
        finally { context.Unload(); }
    }

    /// <summary>独立验证功能表的 List 叠加逻辑父和自树精确匹配，仍复用原表单及路由。</summary>
    private static void PublishFunctionListTreeExact(MetadataCatalog catalog)
    {
        var project=catalog.YzProjectId;
        var target=catalog.ConfigPages.Single(page=>page.Id=="103");
        target.Extra["type"]=JsonSerializer.SerializeToElement(0);
        target.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("approval_tree_name");
        catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="38");
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="38");
        catalog.ConfigTables.Add(new(){Id="90",TableName="function_approval_nodes",YzProjectId=project,IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="9001",ConfigTableId="90",YzProjectId=project,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="9002",ConfigTableId="90",YzProjectId=project,ColumnName="name",LogicalType="string"},
            new(){Id="9003",ConfigTableId="90",YzProjectId=project,ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="9004",ConfigTableId="90",YzProjectId=project,ColumnName="parent_function_approval_nodes_id",
                LogicalType="int64",LinkConfigTableId="90"},
            new(){Id="9005",ConfigTableId="1",YzProjectId=project,ColumnName="approval_tree_id",LogicalType="int64",
                ControlKind="CascadeTree",LinkConfigTableId="90",ControlDetail=JsonSerializer.Serialize(new
                {
                    sourceMode="selfTree",selfTree=new{table="function_approval_nodes",idField="id",nameField="name",
                        parentField="parent_function_approval_nodes_id",rootMode="blank",filterMatchMode="selfAndDescendants"}
                })}
        ]);
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="90",YzProjectId=project,YzAdminTypeId=target.YzAdminTypeId,
            WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigPageColumns.AddRange([
            new(){Id="90102",ConfigPageId="901",ConfigColumnId="9002",YzProjectId=project,IsShow=1},
            new(){Id="90104",ConfigPageId="901",ConfigColumnId="9004",YzProjectId=project,IsShow=1}
        ]);
    }

    /// <summary>目标页有父参数时拒绝没有已开放父字段的发布配置。</summary>
    [Fact]
    public void Function_original_list_rejects_missing_form_parent()
    {
        var catalog = WithFunctionList(Catalog(), "physical");
        catalog.ConfigPageColumns.RemoveAll(column => column.Id == "10294");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot()));
        Assert.Contains("唯一可读的目标父字段", error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>沿原 List 绑定实际发布页，配置两类项目父级和显式全局父页。</summary>
    private static MetadataCatalog WithFunctionList(MetadataCatalog catalog, string scenario)
    {
        WithSingleSelection(catalog, scenario);
        var field = catalog.ConfigColumns.Single(column => column.Id == "39");
        field.ControlKind = "List";
        var name = scenario == "derived" ? "country_label" : "name";
        if (scenario == "global")
            field.ControlDetail = """{"pageId":"103","nameField":"name","whereSql":"status=1"}""";
        else
        {
            field.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("103");
            field.Extra["isAutoPostBackQuery"] = JsonSerializer.SerializeToElement(1);
            field.ControlDetail = JsonSerializer.Serialize(new {tableId="1",nameField=name,whereSql="remark = @formParameters.summaryForm.title"});
            catalog.ConfigPageColumns.Single(column => column.Id == "10239").Extra.Remove("controlDetail");
        }
        return catalog;
    }

    /// <summary>使用包内实际项目和依赖构建 ASP.NET Core 宿主，警告按错误处理。</summary>
    private static async Task BuildFullAsync(string output)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = output, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "build", "backend/Application.Host/Application.Host.csproj", "--nologo",
            "--disable-build-servers", "--verbosity", "quiet", "-warnaserror", "-p:NuGetAudit=false" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }

    /// <summary>装载包内复选计划，避免生成核验通过而宿主静态初始化失败。</summary>
    private static void CheckCheckboxPlan(string output,int scopedTargetStorage,bool logical)
    {
        if(scopedTargetStorage==0)
        {
            var context = new AssemblyLoadContext("FunctionForm."+Guid.NewGuid().ToString("N"), isCollectible:true);
            try
            {
                using var binary=File.OpenRead(Path.Combine(output,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"));
                var assembly=context.LoadFromStream(binary);
                var generatedService=assembly.GetType("Example.FunctionForm.Generated.Services.SummaryFormService",throwOnError:true)!;
                RuntimeHelpers.RunClassConstructor(generatedService.TypeHandle);
                var binding=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    generatedService.GetField("DerivedCheckboxPlan102_38",binding)!.GetValue(null));
                if(logical)
                {
                    Assert.True(plan.RequiresAccount);
                    const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                    var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                    Assert.Equal("country_id",sql.GetType().GetProperty("RequiredMultiParent",hidden)!.GetValue(sql));
                    var scopeSql=Assert.IsType<string>(sql.GetType().GetProperty("Scope",hidden)!.GetValue(sql));
                    Assert.Contains("store_id",scopeSql);
                    var exact=sql.GetType().GetMethod("Expression",hidden)!.Invoke(sql,["assigned_area_name"]);
                    Assert.Contains("CASE WHEN",Assert.IsType<string>(exact));

                    // 运行实际读取器的谓词生成，核对精确范围使用路径校验和参数绑定。
                    var filter=typeof(Metarill.Data.Queries.MySqlNonRedundantReader)
                        .GetMethod("Filter",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
                    var allowed=Metarill.Data.Queries.FixedReadFilter.ForCrossExact("assigned_area_name",["允许范围"]);
                    var predicate=Assert.IsType<string>(filter.Invoke(null,[sql,allowed,1]));
                    Assert.Contains("CASE WHEN",predicate);
                    Assert.Contains("IN (@filter_1_0)",predicate);
                    Assert.DoesNotContain("允许范围",predicate);
                    var empty=Metarill.Data.Queries.FixedReadFilter.ForCrossExact("assigned_area_name",[]);
                    Assert.Equal("1=0",filter.Invoke(null,[sql,empty,1]));
                }
            }
            finally { context.Unload(); }
        }
    }

    /// <summary>装载完整包内的原表单，验证原确认事件到业务按钮的实际请求参数。</summary>
    private static async Task VerifyVueAsync(string directory, string probe, string? scenario = null, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, probe));
        start.ArgumentList.Add(directory);
        if (scenario is not null) start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }

    /// <summary>功能表不能使用超出目标页已证明合同的附加条件。</summary>
    [Fact]
    public void Function_form_rejects_an_unbound_multiselect_filter()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail =
            """{"pageId":"101","nameField":"name","whereSql":"name = '限定客户'"}""";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot()));
        Assert.Contains("额外控件范围", error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>旧复选可直接声明真实候选表，沿用来源页授权。</summary>
    [Fact]
    public void Function_form_checkbox_accepts_legacy_table_only_options()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.Id == "26").ControlDetail = "customer";
        new CSharpGenerator().Generate(catalog, output, "Example.FunctionForm");
        var service = File.ReadAllText(Path.Combine(output, "Services/SummaryFormService.g.cs"));
        Assert.Contains("CheckboxOptions102_26Async", service);
        Assert.Contains("RelationOptionAuthorization.Require(relationAuthorization,\"102\",false,true)", service);
        Assert.DoesNotContain("CheckboxTargetCondition102_26", service);
    }

    /// <summary>虚拟表单值只能绑定当前来源页开放的确切字段。</summary>
    [Fact]
    public void Function_form_checkbox_rejects_an_unpublished_form_slot()
    {
        var catalog = Catalog();
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "23");
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot()));
        Assert.Contains("当前页开放的非凭据标量", error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>功能表不能作为实体候选或选择列表，拒绝生成悬空导航。</summary>
    [Fact]
    public void Function_form_cannot_be_used_as_its_own_database_options_target()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column=>column.Id=="24").ControlDetail =
            """{"tableId":"2","table":"summary_form","nameField":"title"}""";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new CSharpGenerator().Generate(catalog,output,"Example.FunctionForm"));
        Assert.Contains("功能表没有可读取的候选记录",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>复选仍使用原 parentId，无法唯一确定独立表单父字段时拒绝发布。</summary>
    [Fact]
    public void Function_form_checkbox_requires_a_published_independent_parent()
    {
        var catalog=WithScopedCheckbox(WithCascades(Catalog()), 1);
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="102"&&field.ConfigColumnId=="94");
        var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot()));
        Assert.Contains("功能表页面须开启唯一的目标父表单字段",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>同一功能表叠加独立父键、联动条件及目标页面的精确管理范围。</summary>
    private static MetadataCatalog WithScopedCheckbox(MetadataCatalog catalog,int storage)
    {
        catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=storage;
        var scopedPage=new PageMetadata
        {
            Id="103", ConfigTableId="1", Name="按国家复选客户", YzProjectId="100", YzAdminTypeId="70",
            WhereSql="yz_project_id=@current_yz_project_id AND country_id=@parent_id AND name <> '停用'"
        };
        if(storage==0)scopedPage.WhereSql+=" AND store_id=@current_store_id";
        scopedPage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("owner_code");
        catalog.ConfigPages.Add(scopedPage);
        catalog.YzAdminTypes.Single(admin=>admin.Id=="70").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        catalog.ConfigColumns.AddRange([
            new(){Id="16",ConfigTableId="1",ColumnName="country_id",LogicalType="int64",LinkConfigTableId="4"},
            new(){Id="17",ConfigTableId="1",ColumnName="owner_code"},
            new(){Id="18",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"},
            new(){Id="38",ConfigTableId="2",ColumnName="scoped_ids",Name="按国家复选客户",
                LogicalType="string",ColumnLength="max",ControlKind="MultiCheckBoxDatabase",LinkConfigTableId="1",IsVirtualColumn=1,
                ControlDetail="""{"pageId":"103","nameField":"name","whereSql":"remark = @formParameters.summaryForm.title"}""",
                Extra=new(){["isAutoPostBackQuery"]=JsonSerializer.SerializeToElement(1)}},
            new(){Id="94",ConfigTableId="2",ColumnName="review_country_id",Name="候选所属国家",
                LogicalType="int64",LinkConfigTableId="4",IsVirtualColumn=1}
        ]);
        catalog.ConfigPageColumns.AddRange([
            new(){Id="10312",ConfigPageId="103",ConfigColumnId="12",IsShow=1},
            new(){Id="10238",ConfigPageId="102",ConfigColumnId="38",IsEdit=1},
            new(){Id="10294",ConfigPageId="102",ConfigColumnId="94",IsEdit=1}
        ]);
        if(storage==0)
        {
            catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",ColumnName="country_label",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("country.name")}});
            var key=catalog.ConfigColumns.Single(column=>column.Id=="16");
            key.ControlKind="List";
            key.ControlDetail="""{"tableId":"4","pageId":"301","nameField":"name"}""";
            catalog.ConfigColumns.Single(column=>column.Id=="38").ControlDetail=
                """{"pageId":"103","nameField":"country_label","whereSql":"remark = @formParameters.summaryForm.title"}""";
        }
        return catalog;
    }

    /// <summary>复选候选的父级和精确字段可分别取自真实稀疏路径，不要求有同名数据库列。</summary>
    private static void ConfigureDerivedCheckboxScope(MetadataCatalog catalog,string scope)
    {
        var page=catalog.ConfigPages.Single(item=>item.Id=="103");
        if(scope=="derived")
        {
            page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("country_label");
            return;
        }
        if(scope!="shared")
            page.Extra["type"]=JsonSerializer.SerializeToElement(scope switch{"ordinary"=>0,"logical"=>1,_=>2});
        page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("assigned_area_name");
        page.WhereSql+=" AND country_name <> '排除范围'";
        catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="1"&&column.Id is "16" or "19");
        var control=new ColumnMetadata {Id="120",ConfigTableId="1",ColumnName="assigned_area_id",
            LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="5",
            ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{
                new{key="country",table="function_country",columnPrefix="country"},
                new{key="area",table="function_area",columnPrefix="assigned_area",parentField="country_id"}}})};
        catalog.ConfigColumns.Add(control);
        catalog.ConfigPageColumns.Add(new(){ Id="103120",ConfigPageId="103",ConfigColumnId="120",IsShow=1,IsEdit=1 });
        if(scope=="shared")
        {
            var backup=new ColumnMetadata {Id="121",ConfigTableId="1",ColumnName="assigned_place_id",
                LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="6",
                ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{
                    new{key="country",table="function_country",columnPrefix="country"},
                    new{key="area",table="function_area",columnPrefix="assigned_area",parentField="country_id"},
                    new{key="place",table="function_place",columnPrefix="assigned_place",parentField="area_id"}}})};
            catalog.ConfigColumns.Add(backup);
            catalog.ConfigPageColumns.Add(new(){Id="103121",ConfigPageId="103",ConfigColumnId="121",IsShow=1,IsEdit=1});
        }
        catalog.ConfigColumns.Single(column=>column.Id=="38").ControlDetail=
            """{"pageId":"103","nameField":"country_name","whereSql":"remark = @formParameters.summaryForm.title"}""";
    }

    /// <summary>原无主键功能表可把虚拟表单字段交给业务按钮，外表负责提供真实候选。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        catalog.ConfigColumns.Where(column=>column.LogicalType=="id").ToList().ForEach(column=>column.LogicalType="int64");
        catalog.ConfigTables[0].YzProjectId = catalog.YzProjectId;
        catalog.ConfigTables.Add(new() { Id="2", Name="汇总表单", TableName="summary_form", YzProjectId="100", IsFunctionTable=1 });
        catalog.YzAdminTypes.Add(new() { Id="70", Name="业务后台", YzProjectId="100" });
        catalog.ConfigPages[0].YzProjectId="100";
        catalog.ConfigPages[0].YzAdminTypeId="70";
        catalog.ConfigPages[0].Extra["type"]=JsonSerializer.SerializeToElement(2);
        catalog.ConfigPages[0].WhereSql+=" AND name <> '停用'";
        catalog.ConfigPages.Add(new() { Id="102", Name="汇总表单", ConfigTableId="2", YzProjectId="100", YzAdminTypeId="70", IsCopy=1, WhereSql="yz_project_id=@current_yz_project_id" });
        catalog.ConfigColumns.Add(new() { Id="23", ConfigTableId="2", ColumnName="title", Name="标题", DefaultValue="业务汇总", IsVirtualColumn=1,
            Extra=new() { ["isAutoPostBack"]=JsonSerializer.SerializeToElement(1) } });
        catalog.ConfigColumns.Add(new() { Id="24", ConfigTableId="2", ColumnName="customer_id", Name="客户", LogicalType="int64",
            ControlKind="List", ControlDetail="""{"tableId":"1","table":"customer","nameField":"name"}""" });
        catalog.ConfigColumns.Add(new() { Id="25", ConfigTableId="2", ColumnName="customer_ids", Name="多选客户",
            LogicalType="string", ColumnLength="max", ControlKind="MultiSelectPage", LinkConfigTableId="1", IsVirtualColumn=1,
            ControlDetail="""{"pageId":"101","nameField":"name","buttonText":"选择"}""" });
        catalog.ConfigColumns.Add(new() { Id="26", ConfigTableId="2", ColumnName="approved_ids", Name="复选客户",
            LogicalType="string", ColumnLength="max", ControlKind="MultiCheckBoxDatabase", LinkConfigTableId="1", IsVirtualColumn=1,
            ControlDetail="""{"pageId":"101","nameField":"name","whereSql":"remark = @formParameters.summaryForm.title"}""",
            Extra=new() { ["isAutoPostBackQuery"]=JsonSerializer.SerializeToElement(1) } });
        catalog.ConfigColumns.Add(new() { Id="37", ConfigTableId="2", ColumnName="attachments", Name="附件",
            LogicalType="string", ColumnLength="max", ControlKind="FileLibrary", IsVirtualColumn=1,
            ControlDetail="""{"accessMode":"PROTECTED","maxCount":3,"maxSizeMb":5,"extensions":["txt"]}""" });
        catalog.ConfigPageColumns.Add(new() { Id="117", ConfigPageId="102", ConfigColumnId="37", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="116", ConfigPageId="102", ConfigColumnId="26", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="115", ConfigPageId="102", ConfigColumnId="25", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="113", ConfigPageId="102", ConfigColumnId="23", IsEdit=1, IsRequired=1 });
        catalog.ConfigPageColumns.Add(new() { Id="114", ConfigPageId="102", ConfigColumnId="24", IsEdit=1 });
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new { id="63", configPageId="102", configTableId="2", yzProjectId="100",
            name="汇总", place="Edit", actionType=1, sql="/admin/custom/report/preview", visibleScheme="Normal", saveOption="Save", isNeedConfirm=1 }));
        catalog.ConfigMenus.Add(new() { Id="82", Name="汇总表单", YzProjectId="100", YzAdminTypeId="70", Kind="GENERATED_PAGE", ConfigPageId="102", IsShowCount=1 });
        return catalog;
    }

    /// <summary>共享范围冲突和错误字段映射在打包前报告。</summary>
    [Theory]
    [InlineData("shared")]
    [InlineData("alias")]
    public void Function_form_cascade_rejects_conflicting_scopes_or_aliases(string kind)
    {
        var catalog=WithCascades(Catalog());
        var branch=catalog.ConfigColumns.Single(column=>column.Id=="28");
        branch.ControlDetail=kind=="shared"
            ?branch.ControlDetail.Replace("\"parentField\":\"country_id\"","\"parentField\":\"country_id\",\"nameField\":\"alt_name\"",StringComparison.Ordinal)
            :branch.ControlDetail.Replace("\"parentField\":\"country_id\"","\"parentField\":\"country_id\",\"idPropertyName\":\"title\"",StringComparison.Ordinal);
        var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.FunctionForm",SourceRoot()));
        Assert.Contains(kind=="shared"?"共享级联的上级路径":"属性映射",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>同一原功能表包含固定自树和共享国家地区的长短跨表路径，所有槽位留在表单。</summary>
    private static MetadataCatalog WithCascades(MetadataCatalog catalog)
    {
        foreach(var (id,name,page,parent,parentTable,mode) in new (string,string,string,string?,string,int)[]
        {
            ("3","function_tree_node","201","parent_node_id","3",1),
            ("4","function_country","301",null,"",1),
            ("5","function_area","401","country_id","4",0),
            ("6","function_place","501","area_id","5",1)
        })
        {
            catalog.ConfigTables.Add(new(){Id=id,TableName=name,Name=name,YzProjectId="100",IsRongyu=mode});
            catalog.ConfigPages.Add(new(){Id=page,ConfigTableId=id,Name=name,YzProjectId="100",YzAdminTypeId="70",WhereSql="yz_project_id=@current_yz_project_id"});
            catalog.ConfigColumns.AddRange([
                new(){Id=id+"01",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=id+"02",ConfigTableId=id,ColumnName="name",YzProjectId="100"},
                new(){Id=id+"03",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=id+"04",ConfigTableId=id,ColumnName="delete_no",LogicalType="datetime"},
                new(){Id=id+"05",ConfigTableId=id,ColumnName="alt_name",YzProjectId="100"}]);
            catalog.ConfigPageColumns.AddRange([
                new(){Id=page+"02",ConfigPageId=page,ConfigColumnId=id+"02",IsShow=1},
                new(){Id=page+"05",ConfigPageId=page,ConfigColumnId=id+"05",IsShow=1}]);
            if(parent is null)continue;
            catalog.ConfigColumns.Add(new(){Id=id+"06",ConfigTableId=id,ColumnName=parent,LogicalType="int64",LinkConfigTableId=parentTable});
            catalog.ConfigPageColumns.Add(new(){Id=page+"06",ConfigPageId=page,ConfigColumnId=id+"06",IsShow=1});
        }
        var country=new{key="country",table="function_country",columnPrefix="country"};
        var area=new{key="area",table="function_area",columnPrefix="area",parentField="country_id"};
        var place=new{key="place",table="function_place",columnPrefix="place",parentField="area_id"};
        catalog.ConfigColumns.AddRange([
            new(){Id="27",ConfigTableId="2",ColumnName="tree_node_id",LogicalType="int64",LinkConfigTableId="3",IsVirtualColumn=1,
                ControlKind="CascadeTree",DefaultValue="9007199254740993",ControlDetail=
                """{"sourceMode":"selfTree","selfTree":{"table":"function_tree_node","parentField":"parent_node_id","nameField":"name"}}"""},
            new(){Id="28",ConfigTableId="2",ColumnName="area_id",LogicalType="int64",LinkConfigTableId="5",IsVirtualColumn=1,
                ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{country,area}})},
            new(){Id="29",ConfigTableId="2",ColumnName="place_id",LogicalType="string",LinkConfigTableId="6",IsVirtualColumn=1,
                ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{country,area,place}})}]);
        foreach(var id in new[]{"27","28","29"})
            catalog.ConfigPageColumns.Add(new(){Id="102"+id,ConfigPageId="102",ConfigColumnId=id,IsEdit=1});
        return catalog;
    }

    private static string SourceRoot([CallerFilePath] string file = "") =>
        Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ??
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    /// <summary>只回收本测试创建的独占工作目录。</summary>
    public void Dispose() { if (Directory.Exists(output)) Directory.Delete(output,true); }
}
