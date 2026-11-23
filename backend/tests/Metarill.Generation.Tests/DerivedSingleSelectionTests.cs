using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原type=1单选页使用R0目标的单值派生名称，不改页面、回执或辅助字段。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedSingleSelectionTests
{
    /// <summary>无目标父与同父模型均从真实元数据生成编译，名称筛选共用固定SQL键。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Derived_select_page_generates_original_name_filter_and_import_export(bool parent)
    {
        var c=Catalog("derived_single",parent);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-single");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedSingle");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));
            var model=GenerationModel.Create(c,"Example.DerivedSingle");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="101"));Assert.Empty(plan.Gaps);Assert.Equal("customerCompanyName",Assert.Single(plan.Bindings).FilterKey);Assert.Empty(GenerationCapabilityAudit.Collect(model));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>项目内单选页使用实际备用全局字典，原查询和 Excel 继续绑定门店及表单值。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false,1)]
    [InlineData(true,1)]
    public async Task Project_selection_uses_the_published_global_dictionary(bool parent,int sourceStorage=0)
    {
        const string ns="Example.ProjectSelectionDictionary";
        var catalog=Catalog("scope_select",parent);
        catalog.ConfigTables.Single(table=>table.Id=="4").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="42");
        catalog.ConfigColumns.AddRange([
            new(){Id="45",ConfigTableId="4",ColumnName="store_id",LogicalType="int64"},
            new(){Id="46",ConfigTableId="4",ColumnName="option_group",LogicalType="int32"},
            new(){Id="213",ConfigTableId="2",ColumnName="dictionary_group",LogicalType="int32"}
        ]);
        var primary=catalog.ConfigPages.Single(page=>page.Id=="401");
        primary.WhereSql="option_group=17";
        catalog.ConfigPages.Add(new(){Id="402",ConfigTableId="4",YzProjectId=catalog.YzProjectId,
            YzAdminTypeId=primary.YzAdminTypeId,WhereSql="option_group>=7 AND store_id=@current_store_id"});
        catalog.ConfigPageColumns.AddRange([
            new(){Id="40244",ConfigPageId="402",ConfigColumnId="44",IsShow=1},
            new(){Id="201213",ConfigPageId="201",ConfigColumnId="213",IsShow=1,IsEdit=1}
        ]);
        catalog.ConfigColumns.Single(column=>column.Id=="27").ControlDetail=
            """{"tableId":"4","pageId":"401","nameField":"name"}""";
        var dictionary=catalog.ConfigPageColumns.Single(field=>field.Id=="20127");
        dictionary.IsShow=1;
        dictionary.IsEdit=1;
        dictionary.Extra["controlDetail"]=JsonSerializer.SerializeToElement(
            """{"pageId":"402","whereSql":"option_group=@formParameters.dictionaryGroup"}""");

        if(sourceStorage==1)
        {
            catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;
            catalog.ConfigColumns.Add(new(){Id="114",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                page.WhereSql+=" AND store_id=@current_store_id";

            // 冗余来源按原名称键查询已登记的物理副本。
            var model=GenerationModel.Create(catalog,ns);
            var source=model.Tables.Single(table=>table.Source.Id=="1");
            var filter=SelectionNameFilterCompilation.ForPage(model,source,source.Pages.Single(page=>page.Source.Id=="101"));
            Assert.Empty(filter.Gaps);
            var binding=Assert.Single(filter.Bindings);
            Assert.Equal("customerCompanyName",binding.FilterKey);
            Assert.Equal(SelectionNameFilterKind.ScalarLike,binding.Kind);
            Assert.Equal("customer_company_name",binding.StorageColumn.Source.ColumnName);
            Assert.Empty(GenerationCapabilityAudit.Collect(model));
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-project-selection-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ScopeSelectOrderService",true)!;
            var actor=new RuntimeActor(9,"实际门店",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L};
            const BindingFlags fields=BindingFlags.Static|BindingFlags.NonPublic;
            const BindingFlags properties=BindingFlags.Instance|BindingFlags.NonPublic;

            var readPlans=sourceStorage==0
                ?new[]{"DerivedOptionsPlan101_15","ReadPlan101","ExportReadPlan101"}
                :new[]{"DerivedOptionsPlan101_15"};
            foreach(var member in readPlans)
            {
                var plan=Assert.IsType<NonRedundantReadPlan>(service.GetField(member,fields)!.GetValue(null));
                Assert.True(plan.RequiresAccount);
                Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
                var sql=typeof(NonRedundantReadPlan).GetProperty("MySql",properties)!.GetValue(plan.BindAccount(actor))!;
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    sql.GetType().GetProperty("Constants",properties)!.GetValue(sql));
                Assert.Contains(actor.StoreId,constants.Values);
                Assert.Contains(7,constants.Values);
                Assert.DoesNotContain(17,constants.Values);
                var from=Assert.IsType<string>(sql.GetType().GetProperty("From",properties)!.GetValue(sql));
                var nameField=member=="DerivedOptionsPlan101_15"?"company_name":"customer_company_name";
                var label=Assert.IsType<string>(sql.GetType().GetMethod("Expression",properties)!.Invoke(sql,[nameField]));
                Assert.Contains(".`dictionary_group`",from+label);
                Assert.Contains(".`store_id`",from+label);
            }
            if(sourceStorage==1)
            {
                var code=File.ReadAllText(Path.Combine(directory,"generated/Services/ScopeSelectOrderService.g.cs"));
                Assert.Contains("StringNameQuery.ApplyLike(session, query, entity => entity.CustomerCompanyName, value)",code);
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("scope_select_company",maintenance.MixedTargets);
                Assert.Contains("customer_company_name",maintenance.MixedSourceColumns["scope_select_order"]);
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans",properties)!.GetValue(maintenance));
                var published=plans["1"]!;
                Assert.Null(published.GetType().GetProperty("Error")!.GetValue(published));
                var maintenanceRead=published.GetType().GetProperty("Read")!.GetValue(published)!;
                var expression=Assert.IsType<string>(maintenanceRead.GetType().GetMethod("Expression",properties)!
                    .Invoke(maintenanceRead,["customer_company_name"]));
                Assert.Contains("b.`store_id`",expression);
                Assert.Contains(".`dictionary_group`",expression);
            }
            Assert.NotNull(service.GetMethod("OptionsSelected101_15Async"));
            Assert.NotNull(service.GetMethod("Import101Async"));
            Assert.NotNull(service.GetMethod("Export101Async"));
        }
        finally
        {
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>从生成服务执行两个筛选入口，验证模糊联接名称、目标精确筛选和回执的原 JSON 类型。</summary>
    [Theory]
    [InlineData("int32", "-2000000001", false)]
    [InlineData("int64", "9007199254740993", true)]
    [InlineData("bool", "true", false)]
    [InlineData("double", "0.125", true)]
    [InlineData("bool", "false", false, true)]
    [InlineData("double", "0.125", true, true)]
    public async Task Scalar_selection_keeps_name_filters_and_receipt_precision(string logicalType, string label, bool parent, bool automatic=false)
    {
        var catalog = Catalog("derived_single", parent);
        ConfigureIntegerSelection(catalog, logicalType);
        if(automatic)RemoveDisplayColumn(catalog);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-derived-integer-select");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, "Example.IntegerSelection");
            var model = GenerationModel.Create(catalog, "Example.IntegerSelection");
            Assert.Empty(GenerationCapabilityAudit.Collect(model));
            var source = model.Tables.Single(table => table.Source.Id == "1");
            var sourcePage = source.Pages.Single(page => page.Source.Id == "101");
            var nameFilter = SelectionNameFilterCompilation.ForPage(model, source, sourcePage);
            Assert.Empty(nameFilter.Gaps);
            var binding = Assert.Single(nameFilter.Bindings);
            Assert.Equal("customerCompanyName", binding.FilterKey);
            var integer=logicalType is "int32" or "int64";
            Assert.Equal(integer?SelectionNameFilterKind.JoinedIntegerLike:SelectionNameFilterKind.JoinedTypedLike, binding.Kind);
            var service = await File.ReadAllTextAsync(Path.Combine(output, "Services/DerivedSingleOrderService.g.cs"));
            Assert.Contains("""[new("company_name", true), new("id", true)]""", service);
            Assert.Contains("""scope.Filters.Add(new("company_name", FixedReadComparison.Equal, key));""", service);
            Assert.Contains("DerivedOptionsRows101_15Async", service);

            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var actor = new RuntimeActor(9, "示例账号", false, catalog.YzProjectId, "5");
            var request = new PageRequest { ParentId = "71", Filters = new() { [binding.FilterKey] = JsonSerializer.SerializeToElement(label[..^1]) } };
            var sourceService = assembly.GetType("Example.IntegerSelection.Generated.Services.DerivedSingleOrderService")!;
            var sourceFilters = (List<FixedReadFilter>)sourceService.GetMethod("ReadFilters101", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [request, actor])!;
            var filter = Assert.Single(sourceFilters, item => item.Field == "customer_company_name");
            Assert.Equal(integer?FixedReadComparison.IntegerLabelContains:FixedReadComparison.TypedLabelContains, filter.Comparison);
            Assert.Equal(label[..^1], filter.Value);

            object expected=logicalType switch
            {
                "int32" => int.Parse(label,CultureInfo.InvariantCulture),
                "int64" => long.Parse(label,CultureInfo.InvariantCulture),
                _ => MultiValueLabel.Parse(logicalType,label)
            };
            request.Filters = new() { ["companyName"] = integer?JsonSerializer.SerializeToElement(label):JsonSerializer.SerializeToElement(expected) };
            var targetService = assembly.GetType("Example.IntegerSelection.Generated.Services.DerivedSinglePartyService")!;
            var targetFilters = (List<FixedReadFilter>)targetService.GetMethod("ReadFilters201", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [request, actor])!;
            var exact = Assert.Single(targetFilters, item => item.Field == "company_name");
            Assert.Equal(FixedReadComparison.Equal, exact.Comparison);
            Assert.Equal(expected, exact.Value);

            var target = model.Tables.Single(table => table.Source.Id == "2");
            var page = target.Pages.Single(item => item.Source.Id == "201");
            var targetVo = assembly.GetType("Example.IntegerSelection.Generated.VOs." + page.Name + "Vo")!;
            var row = NonRedundantReadCompilation.ForPage(model, target, page).ReadOutputs.ToDictionary(name => name, name => (object?)null);
            row["id"] = 9007199254740995L;
            row["company_name"] = expected;
            var receipt = targetVo.GetMethod("FromReadRow")!.Invoke(null, [row]);
            var json = JsonSerializer.SerializeToElement(receipt, Metarill.Runtime.JsonDefaults.Options);
            Assert.Equal("9007199254740995", json.GetProperty("id").GetString());
            var value=json.GetProperty("companyName");
            if(logicalType=="bool")Assert.Equal((bool)expected,value.GetBoolean());
            else
            {
                Assert.Equal(logicalType=="int64"?JsonValueKind.String:JsonValueKind.Number,value.ValueKind);
                Assert.Equal(label,value.ToString());
            }
            if(automatic)
            {
                var sourcePlan=NonRedundantReadCompilation.ForPage(model,source,sourcePage);
                var sourceRow=sourcePlan.ReadOutputs.ToDictionary(name=>name,name=>(object?)null);
                sourceRow["id"]=9007199254741001L;
                sourceRow["customer_id"]=9007199254740995L;
                sourceRow["customer_company_name"]=expected;
                var sourceVo=assembly.GetType("Example.IntegerSelection.Generated.VOs."+sourcePage.Name+"Vo",true)!;
                var sourceReceipt=sourceVo.GetMethod("FromReadRow")!.Invoke(null,[sourceRow]);
                Assert.Equal(expected,sourceVo.GetProperty("CustomerCompanyName")!.GetValue(sourceReceipt));
                var dto=assembly.GetType("Example.IntegerSelection.Generated.DTOs."+sourcePage.Name+"UpdateRequest",true)!;
                Assert.Null(dto.GetProperty("CustomerCompanyName"));
                var controller=assembly.GetType("Example.IntegerSelection.Generated.Controllers."+sourcePage.Name+"Controller",true)!;
                var contract=(LegacyPageWriteContract)controller.GetField("LegacyWrite",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                var entity=JsonSerializer.SerializeToElement(new{id="9007199254741001",customerId="9007199254740995",customerCompanyName=expected});
                var normalized=contract.Normalize(entity,LegacyPageWriteOperation.Update,catalog.YzProjectId,"71");
                Assert.False(normalized.TryGetProperty("customerCompanyName",out _));
                Assert.Equal("9007199254740995",normalized.GetProperty("customerId").GetString());
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>普通冗余单选沿用原目标页，Excel 可按名称往返且不会发布新的候选路由。</summary>
    [Theory]
    [InlineData(false, "list", "OnlyNew")]
    [InlineData(true, "edit", "OverWriteByFirstColumn", true)]
    public async Task Redundant_physical_selection_uses_the_published_scope_for_excel(bool independentParent, string exportMode,
        string importMode, bool exactTarget = false)
    {
        const string generatedNamespace = "Example.RedundantSingleExcel";
        var catalog = RedundantMultiNameCacheTests.Published(RelationOptionsContractTests.Catalog());
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        sourcePage.IsImport = sourcePage.IsExport = 1;
        sourcePage.Extra["importMethod"] = JsonSerializer.SerializeToElement(importMode);
        sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "25");
        foreign.ControlKind = "SelectPage";
        foreign.ControlDetail = """{"pageId":"301","tableId":"3","nameField":"name","whereSql":"status=1"}""";
        foreign.Name = "目标名称";
        foreign.SortNo = -10;
        var targetPage = catalog.ConfigPages.Single(page => page.Id == "301");
        targetPage.WhereSql += " AND status=1 AND store_id=@current_store_id";
        targetPage.Extra["type"] = JsonSerializer.SerializeToElement(1);
        if (exactTarget) targetPage.Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement("store_id");
        catalog.ConfigColumns.AddRange([
            new() { Id = "37", YzProjectId = catalog.YzProjectId, ConfigTableId = "3", ColumnName = "status", LogicalType = "int32" },
            new() { Id = "38", YzProjectId = catalog.YzProjectId, ConfigTableId = "3", ColumnName = "store_id", LogicalType = "int64" }
        ]);
        if (independentParent)
        {
            sourcePage.WhereSql = "yz_project_id=@current_yz_project_id";
            catalog.ConfigColumns.Single(column => column.Id == "26").Name = "目标父记录";
            catalog.ConfigPageColumns.Add(new() { Id = "20126", YzProjectId = catalog.YzProjectId, ConfigPageId = "201", ConfigColumnId = "26" });
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-redundant-single-excel");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, generatedNamespace);
            var service = await File.ReadAllTextAsync(Path.Combine(output, "Services/RelationSourceService.g.cs"));
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "Controllers/RelationSourcePage201Controller.g.cs"));
            Assert.Contains("await ImportRelation201_25Async(cell.Value", service);
            if (exactTarget)
            {
                // 精确数据管理沿固定 SQL 的当前读查找唯一名称，普通页继续使用实体查询。
                Assert.Contains("""scope.Filters.Add(new("name",FixedReadComparison.Equal,cell.ToString().Trim()));""", service);
                Assert.Contains("DerivedOptionsScope201_25Async(parentId,targetParent.ToString(),actor,cancellationToken,locking:true)", service);
                Assert.Contains("""DerivedOptionsPlan201_25.BindAccount(actor),scope.Filters,[new("id")],1,2""", service);
                Assert.Contains("currentRead:true", service);
                Assert.Contains("if(rows.Records.Count!=1)", service);
            }
            else
            {
                Assert.Contains("query=query.Where(row=>row.Name==label);", service);
                Assert.Contains("query.ForUpdate().Limit(2)", service);
            }
            Assert.Contains("OptionsScope201_25Async", service);
            Assert.Contains("query = query.Where(entity => entity.ParentId", service);
            Assert.Contains("options_page_201_25", service);
            if (exactTarget)
                Assert.Contains("targetExactAuthorization.ResolveExactDataManage(\"301\", \"list\",", service);
            if (!exactTarget) Assert.Contains(independentParent ? "string? targetParentId=parentValue.ToString();" : "string? targetParentId=parentId;", service);
            Assert.Contains("importDatas201", controller);
            Assert.Contains("initExport", controller);
            Assert.DoesNotContain("findNameId", controller);
            Assert.Contains("""authorization.RequirePagePermission("301", "list")""", controller);
            new FrontendPackager().Validate(catalog);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var serviceType = assembly.GetType(generatedNamespace + ".Generated.Services.RelationSourceService", true)!;
            Assert.NotNull(serviceType.GetMethod("Import201Async"));
            Assert.NotNull(serviceType.GetMethod("Export201Async"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>原弹窗单选直接使用全局冗余字典页的账号与常量条件，冗余副本另按持久门店计算。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task Stored_global_selection_uses_published_account_dictionary(int storage, bool globalSource)
    {
        const string ns = "Example.GlobalAccountSelection";
        var catalog = Catalog("account_selection", false);
        var source = catalog.ConfigTables.Single(table => table.Id == "1");
        source.IsRongyu = storage;
        if (globalSource) source.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        var dictionary = catalog.ConfigTables.Single(table => table.Id == "2");
        dictionary.IsRongyu = 1;
        dictionary.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column => column.Id is "16" or "17" or "27" or "28");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "16" or "17" or "27" or "28");

        var foreign = catalog.ConfigColumns.Single(column => column.Id == "15");
        foreign.ControlDetail = """{"tableId":"2","pageId":"201","nameField":"name","whereSql":"status=1"}""";
        var display = catalog.ConfigColumns.Single(column => column.Id == "113");
        display.ColumnName = "customer_name";
        display.Extra["columnFullName"] = JsonSerializer.SerializeToElement("customer.name");
        catalog.ConfigColumns.AddRange([
            new() { Id = "11201", ConfigTableId = "1", YzProjectId = catalog.YzProjectId, ColumnName = "store_id", LogicalType = "int64" },
            new() { Id = "21201", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "store_id", LogicalType = "int64" }
        ]);
        var targetPage = catalog.ConfigPages.Single(page => page.Id == "201");
        targetPage.WhereSql = "status=1 AND store_id=@current_store_id";
        foreach (var page in catalog.ConfigPages.Where(page => page.ConfigTableId == source.Id))
        {
            page.WhereSql += " AND store_id=@current_store_id";
            page.OrderBy = "id desc";
        }

        if (globalSource) catalog.ConfigPages.Single(page => page.Id == "101").Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");

        var model = GenerationModel.Create(catalog, ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var owner = model.Tables.Single(table => table.Source.Id == source.Id);
        var businessPage = owner.Pages.Single(page => page.Source.Id == "101");
        var selected = NonRedundantSelectionCompilation.Resolve(model, owner, businessPage,
            owner.Columns.Single(column => column.Source.Id == "15"));
        Assert.Equal("201", selected.TargetPage.Source.Id);
        var filter = Assert.Single(SelectionNameFilterCompilation.ForPage(model, owner, businessPage).Bindings);
        Assert.Equal("customerName", filter.FilterKey);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-account-selection");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var services = Path.Combine(output, "Services");
            var ownerCode = await File.ReadAllTextAsync(Path.Combine(services, "AccountSelectionOrderService.g.cs"));
            var dictionaryCode = await File.ReadAllTextAsync(Path.Combine(services, "AccountSelectionPartyService.g.cs"));
            Assert.Contains("PageCondition201.BindAccount(actor)", dictionaryCode);
            Assert.Contains("ImportRelation101_15Async", ownerCode);
            Assert.Contains("ImportRelation103_15Async", ownerCode);
            Assert.Contains("Export101Async", ownerCode);
            Assert.Contains("OptionsPageCondition101_15.BindAccount(actor)", ownerCode);
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "Controllers", businessPage.Name + "Controller.g.cs"));
            Assert.Contains("""RequirePagePermission("201", "list")""", controller);

            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.AccountSelectionOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            if (storage == 0)
            {
                // 原业务列表的名称联接须绑定真实认证门店，读取和筛选共用该计划。
                var plan = Assert.IsType<NonRedundantReadPlan>(
                    service.GetField("ReadPlan101", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                Assert.True(plan.RequiresAccount);
                Assert.Equal("string", plan.OutputTypes["customer_name"]);
                var bound = plan.BindAccount(new RuntimeActor(7, "合成账号", false, catalog.YzProjectId, "5")
                    { StoreId = 9007199254740993L });
                var read = bound.GetType().GetProperty("MySql", hidden)!.GetValue(bound)!;
                var constants = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                    read.GetType().GetProperty("Constants", hidden)!.GetValue(read));
                Assert.Contains(9007199254740993L, constants.Values);
            }
            else
            {
                var binding = Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                    item => item.Foreign.Source.Id == foreign.Id);
                Assert.Equal("201", binding.ScopedSinglePageId);
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("customer_name", maintenance.MixedSourceColumns["account_selection_order"]);
                Assert.Contains("account_selection_party", maintenance.MixedTargets);
                var plans = Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans", hidden)!.GetValue(maintenance));
                var published = plans[source.Id]!;
                Assert.Null(published.GetType().GetProperty("Error")!.GetValue(published));
                var read = published.GetType().GetProperty("Read")!.GetValue(published)!;
                var label = Assert.IsType<string>(read.GetType().GetMethod("Expression", hidden)!
                    .Invoke(read, ["customer_name"]));
                Assert.Matches(@"BINARY CAST\((?!b\.)\w+\.`store_id` AS CHAR\)=BINARY CAST\(b\.`store_id` AS CHAR\)", label);

                var sourcePage = catalog.ConfigPages.Single(page => page.Id == "101");
                sourcePage.WhereSql = sourcePage.WhereSql.Replace(" AND store_id=@current_store_id", "", StringComparison.Ordinal);
                var missing = GenerationModel.Create(catalog, ns);
                var error = Assert.Throws<InvalidOperationException>(() => RelationPropagationCompilation.DerivedBindings(missing));
                Assert.Contains("物理账号归属", error.Message);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>普通项目的两种目标存储都能把物理编号用于候选、回显和工作簿。</summary>
    [Theory]
    [InlineData(0, "int32", false)]
    [InlineData(0, "int64", true)]
    [InlineData(1, "int32", true)]
    [InlineData(1, "int64", false)]
    [InlineData(1, "double", false, true)]
    [InlineData(0, "double", true, true)]
    public async Task Local_physical_number_reaches_both_control_contracts(int targetStorage, string logicalType, bool selectPage, bool automatic=false)
    {
        var catalog = selectPage ? Catalog("local_number", true) : DerivedRelationNamesTests.Catalog("local_number", true);
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = targetStorage;
        var number = catalog.ConfigColumns.Single(column => column.Id == "28");
        number.Extra.Remove("isJoinColumn");
        number.Extra.Remove("columnFullName");
        number.ColumnName = "company_code";
        number.LogicalType = logicalType;
        var source = catalog.ConfigColumns.Single(column => column.Id == "15");
        source.ControlDetail = source.ControlDetail.Replace("company_name", "company_code", StringComparison.Ordinal);
        var display = catalog.ConfigColumns.Single(column => column.Id == "113");
        display.ColumnName = "customer_company_code";
        display.LogicalType = logicalType;
        display.Extra["columnFullName"] = JsonSerializer.SerializeToElement("customer.company_code");
        if(automatic)RemoveDisplayColumn(catalog);
        catalog.ConfigPages.Single(page => page.Id == "201").OrderBy = "company_code desc";

        var model = GenerationModel.Create(catalog, "Example.LocalNumber");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var owner = model.Tables.Single(table => table.Source.Id == "1");
        var page = owner.Pages.Single(item => item.Source.Id == "101");
        var plan = NonRedundantReadCompilation.ForPage(model, owner, page);
        var nameOutput=Assert.Single(plan.Outputs,output=>output.Storage=="customer_company_code");
        if(automatic)
        {
            Assert.Equal("double?",nameOutput.Type);
            Assert.DoesNotContain(owner.Columns,column=>column.Source.ColumnName==nameOutput.Storage);
            Assert.Contains("relationDisplayText(item.customerCompanyCode)",OriginalUniappListRenderer.Render(model,owner,page));
        }
        var relation = Assert.Single(RelationCompilation.ForPage(model, page), candidate => candidate.SourceColumn.Source.Id == "15");
        Assert.Equal("201", relation.TargetPage.Source.Id);
        Assert.True(DerivedRelationNamesCompilation.UsesFixedRead(model, relation));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-local-physical-number");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, "Example.LocalNumber");
            var service = await File.ReadAllTextAsync(Path.Combine(output, "Services/LocalNumberOrderService.g.cs"));
            Assert.Contains((targetStorage == 1 ? "CompilePhysicalRelation" : "CompileRelation") + """("2",new string[]{"id","company_code"}""", service);
            Assert.Contains(logicalType=="double"?
                """new("company_code",FixedReadComparison.TypedLabelContains,request.Keyword)""":
                """new("company_code",FixedReadComparison.IntegerLabelContains,request.Keyword)""", service);
            Assert.Contains("""scope.Filters.Add(new("company_code", FixedReadComparison.Equal, key));""", service);
            Assert.Contains("DerivedOptionsRows101_15Async", service);
            Assert.Contains("OptionsScope104_15Async", service);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>去掉显式回显列，实体和写请求应始终只保存关系 ID。</summary>
    private static void RemoveDisplayColumn(MetadataCatalog catalog)
    {
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="113");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="113");
    }

    /// <summary>标量联接使用目标页的默认排序，选择弹层与辅助候选共用。</summary>
    private static void ConfigureIntegerSelection(MetadataCatalog catalog, string logicalType)
    {
        DerivedRelationNamesTests.UseIntegerName(catalog, logicalType);
        catalog.ConfigPages.Single(page => page.Id == "201").OrderBy = "company_name desc";
    }

    /// <summary>完整包构建、原Vue回执及更新保护共同证明，没有另写单选页面。</summary>
    [Theory][InlineData(false,"string")][InlineData(true,"string")][InlineData(true,"int64")][InlineData(false,"bool")][InlineData(false,"bool",true)]
    public async Task Full_derived_selection_keeps_original_vue_and_handwritten_partial(bool parent, string logicalType, bool automatic=false)
    {
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-single-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var c=Catalog("derived_single",parent);
        if(logicalType!="string")ConfigureIntegerSelection(c,logicalType);
        if(automatic)RemoveDisplayColumn(c);
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.DerivedSingleApplication",root).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var asset in new[]{"common/yzp_runtime/originalSelectionPageMixin.js","common/yzp_runtime/selectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",asset)),File.ReadAllBytes(Path.Combine(output,"frontend",asset)));
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};foreach(var arg in new[]{Path.Combine(root,"backend/tests/Metarill.Generation.Tests/NonRedundantSingleSelectionProbe.mjs"),output,parent?"parent":"plain","derived_single","customerCompanyName","companyName",logicalType})start.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var names=capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.GetProperty("controlKind").GetString()=="SelectPage").ToArray();Assert.Equal(parent?3:2,names.Length);Assert.All(names,item=>Assert.True(item.GetProperty("derivedNameSupported").GetBoolean()));}
            var hand=Path.Combine(output,"backend/generated/Services/DerivedSingleOrderService.cs");await File.AppendAllTextAsync(hand,"\n// 原单选派生名称手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.DerivedSingleApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明名称、目标、父／附加条件或终点在写stage之前拒绝，隐藏控制也不能绕过。</summary>
    [Theory][InlineData("global")][InlineData("type2")][InlineData("hiddenName")][InlineData("broken")][InlineData("credential")][InlineData("array")][InlineData("stringKey")][InlineData("scope")][InlineData("form")][InlineData("hiddenBad")][InlineData("nameOverride")]
    public void Invalid_derived_single_selection_is_rejected_before_staging(string kind)
    {
        var c=Catalog("derived_single",true);var name=c.ConfigColumns.Single(column=>column.Id=="28");
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="type2")c.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(2);
        if(kind=="hiddenName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28");
        if(kind is "broken" or "hiddenBad")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="hiddenBad")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");
        if(kind=="credential"){c.ConfigColumns.Single(column=>column.Id=="44").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(column=>column.Id=="44").ControlKind="FileLibrary";
        if(kind=="stringKey")c.ConfigColumns.Single(column=>column.Id=="27").LogicalType="string";
        if(kind=="scope")c.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND company_name='X'";
        if(kind=="form")c.ConfigColumns.Single(column=>column.Id=="15").ControlDetail="{\"pageId\":\"201\",\"nameField\":\"company_name\",\"whereSql\":\"segment=@formParameters.segment\"}";
        if(kind=="nameOverride")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"name\"}");
        var output=Path.Combine(Path.GetTempPath(),"metarill-derived-single-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidDerivedSingle",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>客户的company_name继续沿既有联接，单选附加条件只使用目标页已经执行的固定条件。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool parent)
    {
        var c=DerivedRelationNamesTests.Catalog(prefix,false);var source=c.ConfigColumns.Single(column=>column.Id=="15");source.ControlKind="SelectPage";source.ControlDetail=JsonSerializer.Serialize(new{pageId="201",tableId="2",table=prefix+"_party",nameField="company_name",whereSql=parent?"status=1 AND segment=1":"status=1",buttonText="选择客户"});
        var target=c.ConfigPages.Single(page=>page.Id=="201");target.Extra["type"]=JsonSerializer.SerializeToElement(1);c.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);c.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="113").Extra["isSearch"]=JsonSerializer.SerializeToElement(0);
        if(parent)
        {
            c.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"});c.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29"});target.WhereSql+=" AND group_id=@parent_id AND segment=1";
            foreach(var (id,table) in new[]{("112","1"),("211","2")}){c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName="segment",Name="分组条件",LogicalType="int32",DefaultValue="1"});c.ConfigPageColumns.Add(new(){Id=table+id,ConfigPageId=table=="1"?"101":"201",ConfigColumnId=id});}
        }
        if(parent)
        {
            var original=c.ConfigPages.Single(page=>page.Id=="101");var mapped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;mapped.Id="106";mapped.WhereSql="yz_project_id=@current_yz_project_id";c.ConfigPages.Add(mapped);
            foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id="106"+field.Id;clone.ConfigPageId="106";if(clone.ConfigColumnId=="19"){clone.IsShow=1;clone.IsEdit=1;}c.ConfigPageColumns.Add(clone);}
            c.ConfigColumns.Single(column=>column.Id=="19").Name="目标组";
        }
        c.ConfigColumns.Single(column=>column.Id=="28").Name="公司名称";
        c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
                foreach(var record in c.ConfigColumns.Cast<MetadataRecord>().Concat(c.ConfigPageColumns))if(string.IsNullOrWhiteSpace(record.YzProjectId))record.YzProjectId=c.YzProjectId;
        return c;
    }
}
