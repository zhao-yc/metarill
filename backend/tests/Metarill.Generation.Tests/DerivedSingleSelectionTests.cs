using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
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

    /// <summary>原弹窗单选和多选直接使用全局冗余字典页，业务读取与维护另核对真实行归属。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(1, false, true)]
    [InlineData(0, false, true, true)]
    [InlineData(1, false, true, true)]
    [InlineData(0, true, true, true)]
    [InlineData(1, true, true, true)]
    [InlineData(0, false, true, true, true)]
    [InlineData(1, false, true, true, true)]
    [InlineData(0, true, true, true, true)]
    [InlineData(1, true, true, true, true)]
    [InlineData(0, false, true, true, true, true)]
    [InlineData(1, false, true, true, true, true)]
    [InlineData(0, true, true, true, true, true)]
    [InlineData(1, true, true, true, true, true)]
    public async Task Stored_global_selection_uses_published_account_dictionary(int storage, bool globalSource,
        bool managedDictionary = false, bool multiple = false, bool pageProject = false, bool repeatedScope = false)
    {
        const string ns = "Example.GlobalAccountSelection";
        var catalog = Catalog("account_selection", false);
        var source = catalog.ConfigTables.Single(table => table.Id == "1");
        source.IsRongyu = storage;
        if (globalSource) source.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        var dictionary = catalog.ConfigTables.Single(table => table.Id == "2");
        dictionary.IsRongyu = 1;
        dictionary.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column => column.Id is "16" or "17" || !managedDictionary && column.Id is "27" or "28");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "16" or "17" || !managedDictionary && field.ConfigColumnId is "27" or "28");

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
        if (multiple)
        {
            foreign.ControlKind = "MultiSelectPage";
            foreign.ColumnName = "customer_ids";
            foreign.DefaultValue = "";
            foreign.LogicalType = "string";
            foreign.ColumnLength = "max";
            foreign.ControlDetail = """{"tableId":"2","pageId":"201","nameField":"name"}""";
            display.ColumnName = "customer_names";
            display.ColumnLength = "max";
            targetPage.Extra["type"] = JsonSerializer.SerializeToElement(2);
        }
        var cacheName = display.ColumnName;
        targetPage.WhereSql = "status=1 AND store_id=@current_store_id";
        if(pageProject)targetPage.WhereSql+=" AND yz_project_id=@current_yz_project_id";
        if(repeatedScope)
        {
            // 表级重排同一组条件，另一业务页只重复其中一项。实际范围仍由同一字典页提供。
            foreign.ControlDetail = """
                {"tableId":"2","pageId":"201","nameField":"name","whereSql":"yz_project_id=@current_yz_project_id AND (store_id = @current_store_id AND status = 1)"}
                """;
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="103"&&field.ConfigColumnId==foreign.Id)
                .Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"store_id = @current_store_id"}""");
        }
        foreach (var page in catalog.ConfigPages.Where(page => page.ConfigTableId == source.Id))
        {
            page.WhereSql += " AND store_id=@current_store_id";
            page.OrderBy = "id desc";
        }

        if (globalSource) catalog.ConfigPages.Single(page => page.Id == "101").Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");

        if (managedDictionary)
        {
            // 字典的数据维护页保存公司名称副本，弹窗页继续按明确发布的门店条件提供候选。
            catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 0;
            var company = catalog.ConfigTables.Single(table => table.Id == "4");
            company.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.Add(new()
            {
                Id = "41201", ConfigTableId = "4", YzProjectId = catalog.YzProjectId,
                ColumnName = "store_id", LogicalType = "int64"
            });
            catalog.ConfigPages.Single(page => page.Id == "401").WhereSql = "store_id=@current_store_id";
            catalog.ConfigColumns.Single(column => column.Id == "27").ControlDetail =
                """{"tableId":"4","pageId":"401","nameField":"name"}""";

            targetPage.IsExport = 1;
            foreach (var id in new[] { "202", "203" })
            {
                var owned = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(targetPage))!;
                owned.Id = id;
                if(!pageProject)owned.WhereSql += " AND yz_project_id=@current_yz_project_id";
                if (id == "202") owned.Extra.Remove("type");
                catalog.ConfigPages.Add(owned);
                foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == targetPage.Id).ToArray())
                {
                    var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                    copy.Id = id + field.Id;
                    copy.ConfigPageId = owned.Id;
                    catalog.ConfigPageColumns.Add(copy);
                }
            }
        }

        var model = GenerationModel.Create(catalog, ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var owner = model.Tables.Single(table => table.Source.Id == source.Id);
        var businessPage = owner.Pages.Single(page => page.Source.Id == "101");
        var sourceColumn = owner.Columns.Single(column => column.Source.Id == "15");
        var target = model.Tables.Single(table => table.Source.Id == dictionary.Id);
        var targetModel = target.Pages.Single(page => page.Source.Id == targetPage.Id);
        if (multiple)
        {
            var resolved = MultiSelectCompilation.Resolve(model, owner, businessPage, sourceColumn);
            Assert.NotNull(resolved.Definition);
            Assert.Equal(targetPage.Id, resolved.Definition.TargetPage.Source.Id);
            if(repeatedScope)
            {
                Assert.Null(resolved.Definition.ImportCondition);
                var otherPage=owner.Pages.Single(page=>page.Source.Id=="103");
                var other=MultiSelectCompilation.Resolve(model,owner,otherPage,sourceColumn);
                Assert.NotNull(other.Definition);
                Assert.Null(other.Definition.ImportCondition);
            }
        }
        else
        {
            var selected = NonRedundantSelectionCompilation.Resolve(model, owner, businessPage, sourceColumn);
            Assert.Equal(targetPage.Id, selected.TargetPage.Source.Id);
        }
        var filter = Assert.Single(SelectionNameFilterCompilation.ForPage(model, owner, businessPage).Bindings);
        Assert.Equal(multiple ? "customerNames" : "customerName", filter.FilterKey);

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
            Assert.Contains(multiple ? "ImportMulti101_15Async" : "ImportRelation101_15Async", ownerCode);
            Assert.Contains(multiple ? "ImportMulti103_15Async" : "ImportRelation103_15Async", ownerCode);
            Assert.Contains("Export101Async", ownerCode);
            Assert.Contains(multiple ? "MultiDerivedPlan101_15.BindAccount(actor)" : "OptionsPageCondition101_15.BindAccount(actor)", ownerCode);
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "Controllers", businessPage.Name + "Controller.g.cs"));
            Assert.Contains("""RequirePagePermission("201", "list")""", multiple ? ownerCode : controller);

            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.AccountSelectionOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            if (globalSource && multiple)
            {
                var actor = new RuntimeActor(7, "合成账号", false, "999999", "5") { StoreId = 9007199254740993L };
                var validate = service.GetMethod("ValidateActor", BindingFlags.Static | BindingFlags.NonPublic)!;
                Assert.IsType<UnauthorizedAccessException>(
                    Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [actor])).InnerException);
                if (storage == 1)
                {
                    Assert.NotNull(FixedPageQueryCompilation.ManagedGlobalProject(owner));
                    Assert.Contains("entity.YzProjectId = ", ownerCode);
                    Assert.Contains("不能修改来源记录项目归属", ownerCode);
                }
                else
                {
                    // 业务数组有行项目约束，单独的全局候选入口不允许输出同名数组。
                    Assert.Throws<NotSupportedException>(() => NonRedundantReads.CompileGlobalCandidate(
                        catalog, source.Id, businessPage.Source.Id, [foreign.ColumnName]));
                }
            }
            if (managedDictionary)
            {
                // 读取实际生成查询的 SQL，仅构造查询表达式。
                using var session = new Metarill.Data.DatabaseSession(new()
                {
                    Provider = "mysql", ExpectedDatabase = "offline",
                    ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
                });
                var serviceType = assembly.GetType(ns + ".Generated.Services.AccountSelectionPartyService", true)!;
                var mapper = Activator.CreateInstance(assembly.GetType(ns + ".Generated.Mappers.AccountSelectionPartyMapper", true)!, session);
                var arguments = new object?[] { session, mapper, new SnowflakeIdGenerator(), Array.Empty<IRecordMutationObserver>() };
                var constructor = serviceType.GetConstructors().Single();
                var dictionaryService = constructor.Invoke(Arguments(constructor, arguments));
                var actor = new RuntimeActor(7, "合成账号", false, catalog.YzProjectId, "5") { StoreId = 9007199254740993L };
                var sqlType = typeof(DerivedSingleSelectionTests).GetMethod(nameof(SelectionSql), BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(assembly.GetType(ns + ".Generated.Entities.AccountSelectionParty", true)!);
                var candidate = Sql("Query201", new PageRequest(), actor);
                Assert.Equal(pageProject,candidate.Contains("`yz_project_id`",StringComparison.Ordinal));
                Assert.Contains("`store_id`", candidate);
                Assert.Contains("`status`", candidate);
                Assert.Contains("`delete_no`", candidate);
                if (multiple)
                {
                    var selectedSql = Sql("Query201", new PageRequest { SelectedIds = ["9007199254740995"] }, actor);
                    Assert.Contains("`id`", selectedSql);
                    Assert.Contains("`store_id`", selectedSql);
                    Assert.Equal(pageProject,selectedSql.Contains("`yz_project_id`",StringComparison.Ordinal));
                    var selectedCode = await File.ReadAllTextAsync(Path.Combine(output, "Controllers", targetModel.Name + "Controller.g.cs"));
                    Assert.Contains("""[HttpPost("selection/selected")]""", selectedCode);
                    Assert.Contains("""SelectionSelected201Async(request, Actor("list"), cancellationToken)""", selectedCode);
                    Assert.Contains("Query201(new PageRequest", dictionaryCode);
                }
                Assert.Contains("`yz_project_id`", Sql("Query202", new PageRequest(), actor));
                Assert.Contains("`yz_project_id`", Sql("Scope201", actor, null));
                Assert.Contains("`yz_project_id`", Sql("Scope202", actor, null));
                Assert.Contains("`yz_project_id`", Sql("Query203", new PageRequest(), actor));
                if(!pageProject)
                {
                    Assert.Contains("`yz_project_id`", Sql("Query201", new PageRequest(), actor, false));
                    Assert.Contains("Query201(request, actor, candidateRows: false)", dictionaryCode);
                }
                var error = Assert.Throws<TargetInvocationException>(
                    () => Sql("Query201", new PageRequest(), actor with { StoreId = null }));
                Assert.IsType<UnauthorizedAccessException>(error.InnerException);
                var query = FixedPageQueryCompilation.For(target, targetModel);
                Assert.Equal(pageProject,FixedPageQueryCompilation.TryProveSelection(model.Catalog,query,
                    new(targetPage.Id, "name", "选择", "yz_project_id=@current_yz_project_id", ""), out _));

                string Sql(string method, params object?[] values)
                {
                    var query = serviceType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
                    return Assert.IsType<string>(sqlType.Invoke(null, [query.Invoke(dictionaryService, Arguments(query, values))]));
                }

                static object?[] Arguments(MethodBase method, object?[] values) =>
                    values.Concat(method.GetParameters().Skip(values.Length).Select(parameter => parameter.DefaultValue)).ToArray();
            }

            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            const string dictionaryProject = "multi_value.`yz_project_id`=@project_scope_";
            if(pageProject)
            {
                // Excel 反查和写入校验复用字典计划，不能靠弹窗列表补齐项目条件。
                var labelPlan=Assert.IsType<NonRedundantReadPlan>(service.GetField(
                    "MultiDerivedPlan101_15",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                var missingStore=new RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5");
                Assert.Throws<UnauthorizedAccessException>(()=>labelPlan.BindAccount(missingStore));
                var readLabel=labelPlan.BindAccount(missingStore with {StoreId=9007199254740993L});
                var fixedLabel=readLabel.GetType().GetProperty("MySql",hidden)!.GetValue(readLabel)!;
                var scopeLabel=Assert.IsType<string>(fixedLabel.GetType().GetProperty("Scope",hidden)!.GetValue(fixedLabel));
                Assert.Contains(".`yz_project_id`=@project_scope_",scopeLabel);
                Assert.Contains(".`store_id`=",scopeLabel);
            }
            if (storage == 0)
            {
                // 原业务列表的名称联接须绑定真实认证门店，读取和筛选共用该计划。
                var plan = Assert.IsType<NonRedundantReadPlan>(
                    service.GetField("ReadPlan101", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                Assert.True(plan.RequiresAccount);
                Assert.Equal("string", plan.OutputTypes[cacheName]);
                var bound = plan.BindAccount(new RuntimeActor(7, "合成账号", false, catalog.YzProjectId, "5")
                    { StoreId = 9007199254740993L });
                var read = bound.GetType().GetProperty("MySql", hidden)!.GetValue(bound)!;
                if(pageProject)
                {
                    var label=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,[cacheName]));
                    Assert.Contains(dictionaryProject,label);
                }
                if (globalSource && multiple)
                    Assert.Contains("`yz_project_id`", Assert.IsType<string>(read.GetType().GetProperty("Scope", hidden)!.GetValue(read)));
                var constants = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                    read.GetType().GetProperty("Constants", hidden)!.GetValue(read));
                Assert.Contains(9007199254740993L, constants.Values);
            }
            else
            {
                var binding = Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                    item => item.Foreign.Source.Id == foreign.Id);
                Assert.Equal(multiple, binding.Multiple);
                if (multiple)
                    Assert.Equal(targetPage.Id, GlobalMultipleRelationTarget.RequireCached(catalog, source, foreign, dictionary).Page.Id);
                else
                    Assert.Equal(targetPage.Id, binding.ScopedSinglePageId);
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains(cacheName, maintenance.MixedSourceColumns["account_selection_order"]);
                Assert.Contains("account_selection_party", maintenance.MixedTargets);
                var plans = Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans", hidden)!.GetValue(maintenance));
                var published = plans[source.Id]!;
                Assert.Null(published.GetType().GetProperty("Error")!.GetValue(published));
                var read = published.GetType().GetProperty("Read")!.GetValue(published)!;
                if (globalSource && multiple)
                    Assert.Contains("`yz_project_id`", Assert.IsType<string>(read.GetType().GetProperty("Scope", hidden)!.GetValue(read)));
                var label = Assert.IsType<string>(read.GetType().GetMethod("Expression", hidden)!
                    .Invoke(read, [cacheName]));
                if(pageProject)Assert.Contains(dictionaryProject,label);
                Assert.Matches(@"BINARY CAST\((?!b\.)\w+\.`store_id` AS CHAR\)=BINARY CAST\(b\.`store_id` AS CHAR\)", label);


                if(repeatedScope)
                {
                    var original=foreign.ControlDetail;
                    try
                    {
                        foreign.ControlDetail=original.Replace("status = 1","status = 2",StringComparison.Ordinal);
                        var cacheBinding=new Metarill.Data.Relations.RelationCacheDefinition(source.Id,foreign.Id,
                            dictionary.Id,binding.Value.Source.Id,binding.Cache.Source.Id,true);
                        var narrower=Assert.Throws<NotSupportedException>(()=>
                            Metarill.Data.Relations.DerivedCrossTableCaches.Validate(catalog,[cacheBinding],[]));
                        Assert.Contains("未被名称回填的固定目标范围覆盖",narrower.Message);
                    }
                    finally { foreign.ControlDetail=original; }
                }

                var sourcePage = catalog.ConfigPages.Single(page => page.Id == "101");
                sourcePage.WhereSql = sourcePage.WhereSql.Replace(" AND store_id=@current_store_id", "", StringComparison.Ordinal);
                var error = Assert.Throws<InvalidOperationException>(() =>
                {
                    if (multiple)
                        new FrontendPackager().Validate(catalog);
                    else
                        RelationPropagationCompilation.DerivedBindings(GenerationModel.Create(catalog, ns));
                });
                Assert.Contains("物理账号归属", error.Message);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>本项目多选固定范围对候选、名称读取和物理缓存重算使用同一份已发布条件。</summary>
    [Theory]
    [InlineData(0, 0, false, false, "")]
    [InlineData(0, 1, true, false, "")]
    [InlineData(1, 0, true, false, "")]
    [InlineData(1, 1, false, false, "")]
    [InlineData(0, 0, true, true, "")]
    [InlineData(0, 1, false, true, "")]
    [InlineData(0, 0, false, false, "shared")]
    [InlineData(0, 1, true, false, "form")]
    [InlineData(1, 0, true, false, "form")]
    [InlineData(1, 1, false, false, "shared")]
    [InlineData(1, 1, false, false, "only")]
    [InlineData(0, 1, true, true, "shared")]
    [InlineData(1, 1, true, false, "text")]
    [InlineData(0, 0, false, false, "compound-shared")]
    [InlineData(0, 1, true, true, "compound-shared")]
    [InlineData(1, 0, true, false, "compound-form")]
    [InlineData(1, 1, false, false, "compound-form")]
    [InlineData(0, 0, true, false, "source-pair")]
    [InlineData(0, 1, false, true, "source-compound")]
    [InlineData(1, 0, false, false, "source-compound")]
    [InlineData(1, 1, true, false, "source-pair")]
    [InlineData(0, 0, false, false, "split-source-single")]
    [InlineData(0, 1, true, false, "split-source-single")]
    [InlineData(0, 1, false, true, "split-source-pair")]
    [InlineData(0, 0, true, false, "split-source-dictionary")]
    [InlineData(0, 1, true, false, "split-source-dictionary")]
    [InlineData(0, 1, false, false, "mixed-target-root")]
    [InlineData(0, 1, true, false, "mixed-target-root")]
    [InlineData(0, 1, false, false, "mixed-target-alternate")]
    [InlineData(0, 1, true, true, "mixed-target-alternate")]
    [InlineData(0, 1, true, false, "mixed-target-root-literal")]
    [InlineData(0, 1, false, false, "mixed-target-alternate-literal")]
    [InlineData(0, 1, false, false, "account")]
    [InlineData(0, 1, true, false, "account-parent")]
    [InlineData(1, 1, true, false, "account")]
    [InlineData(1, 1, false, false, "account-parent")]
    [InlineData(0, 1, true, false, "account-merchant")]
    [InlineData(1, 1, true, false, "account-merchant-parent")]
    public async Task Project_multiselect_uses_fixed_dictionary_scope(int sourceStorage, int targetStorage, bool explicitProject,
        bool nameOverride, string parentMode)
    {
        const string ns = "Example.ProjectMultiLiteral";
        const string nameField = "customer_names";
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var catalog = DerivedSingleSelectionTests.Catalog("scope_multi", false);
        var source = catalog.ConfigTables.Single(table => table.Id == "1");
        source.IsRongyu = sourceStorage;
        var target = catalog.ConfigTables.Single(table => table.Id == "2");
        target.IsRongyu = targetStorage;
        catalog.ConfigColumns.RemoveAll(column => column.Id is "16" or "17" or "27" or "28");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "16" or "17" or "27" or "28");
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "15");
        foreign.ControlKind = "MultiSelectPage";
        foreign.ColumnName = "customer_ids";
        foreign.DefaultValue = "";
        foreign.LogicalType = "string";
        foreign.ColumnLength = "max";
        var literal = parentMode != "only";
        var accountTarget = parentMode.StartsWith("account", StringComparison.Ordinal);
        var merchantAccount = parentMode.StartsWith("account-merchant", StringComparison.Ordinal);
        var ownershipField = merchantAccount ? "merchant_id" : "store_id";
        var ownershipPredicate = $"{ownershipField}=@current_{ownershipField}";
        var hasParent = accountTarget ? parentMode.EndsWith("-parent", StringComparison.Ordinal) : parentMode.Length != 0;
        var mixedTarget = parentMode.StartsWith("mixed-target-", StringComparison.Ordinal);
        var targetHasParent = !parentMode.EndsWith("-literal", StringComparison.Ordinal);
        var rootIsPlain = parentMode.StartsWith("mixed-target-root", StringComparison.Ordinal);
        var splitSource = mixedTarget || parentMode.StartsWith("split-source-", StringComparison.Ordinal);
        var splitDictionary = mixedTarget || parentMode == "split-source-dictionary";
        var sourcePair = splitSource || parentMode.StartsWith("source-", StringComparison.Ordinal);
        var compound = parentMode.StartsWith("compound-", StringComparison.Ordinal) || parentMode == "source-compound";
        var formParent = parentMode is "form" or "compound-form";
        foreign.ControlDetail = JsonSerializer.Serialize(new
        {
            tableId = target.Id, pageId = "201", nameField = "name", whereSql = literal && !mixedTarget ? "status = 17" : ""
        });
        if (nameOverride)
            catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "101" && field.ConfigColumnId == foreign.Id)
                .Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"nameField":"notes"}""");
        var display = catalog.ConfigColumns.Single(column => column.Id == "113");
        display.ColumnName = nameField;
        display.ColumnLength = "max";
        display.Extra["columnFullName"] = JsonSerializer.SerializeToElement("customer.name");
        var page = catalog.ConfigPages.Single(page => page.Id == "201");
        foreach (var item in catalog.ConfigPages) item.OrderBy = "id desc";
        page.Extra["type"] = JsonSerializer.SerializeToElement(2);
        List<string> conditions = [];
        if (explicitProject) conditions.Add("yz_project_id=@current_yz_project_id");
        if (literal) conditions.Add("status=17");
        if (accountTarget)
        {
            conditions.Add(ownershipPredicate);
            foreach (var tableId in new[] { source.Id, target.Id })
                catalog.ConfigColumns.Add(new()
                {
                    Id = tableId + "990", ConfigTableId = tableId, YzProjectId = catalog.YzProjectId,
                    ColumnName = ownershipField, LogicalType = merchantAccount ? "string" : "int64"
                });
            foreach (var item in catalog.ConfigPages.Where(item => item.ConfigTableId == source.Id))
                item.WhereSql += " AND " + ownershipPredicate;
        }
        if (hasParent && targetHasParent)
        {
            conditions.Add("group_id=@parent_id");
            catalog.ConfigColumns.Add(new()
            {
                Id = "29", ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "group_id", LogicalType = parentMode == "text" ? "string" : "int64", LinkConfigTableId = "3"
            });
            if (compound)
            {
                conditions.Add("alternate_group_id=@parent_id");
                catalog.ConfigColumns.Add(new()
                {
                    Id = "299", ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId,
                    ColumnName = "alternate_group_id", LogicalType = "string", LinkConfigTableId = "3"
                });
            }
            if (formParent)
            {
                foreach (var item in catalog.ConfigPages.Where(item => item.ConfigTableId == source.Id))
                    item.WhereSql = "yz_project_id=@current_yz_project_id";
                foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigColumnId == "19"))
                    field.IsEdit = field.IsShow = 1;
            }
        }
        page.WhereSql = string.Join(" AND ", conditions);
        if (sourcePair)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "199", ConfigTableId = source.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "secondary_group_id", LogicalType = "string", LinkConfigTableId = "3"
            });
            foreach (var item in catalog.ConfigPages.Where(item => item.ConfigTableId == source.Id))
                item.WhereSql += " AND secondary_group_id=@parent_id";
        }
        if (splitSource)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "1199", ConfigTableId = source.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = "other_group_id", LogicalType = "string", LinkConfigTableId = "3"
            });
            var alternateSource = catalog.ConfigPages.Single(item => item.Id == "104");
            alternateSource.WhereSql = "yz_project_id=@current_yz_project_id AND other_group_id=@parent_id";
            if (parentMode == "split-source-pair") alternateSource.WhereSql += " AND secondary_group_id=@parent_id";
            var original = catalog.ConfigPages.Single(item => item.Id == "101");
            alternateSource.IsImport = original.IsImport;
            alternateSource.IsExport = original.IsExport;
            if (original.Extra.TryGetValue("importMethod", out var importMethod))
                alternateSource.Extra["importMethod"] = importMethod;
            catalog.ConfigPageColumns.Single(item => item.ConfigPageId == alternateSource.Id && item.ConfigColumnId == display.Id)
                .Extra["isSearch"] = JsonSerializer.SerializeToElement(0);
            var field = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(
                catalog.ConfigPageColumns.Single(item => item.ConfigPageId == "101" && item.ConfigColumnId == foreign.Id)))!;
            field.Id = "10415";
            field.ConfigPageId = alternateSource.Id;
            field.Extra.Remove("controlDetail");
            if (splitDictionary)
            {
                var alternateTarget = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!;
                alternateTarget.Id = "202";
                if (mixedTarget)
                {
                    var unfiltered = rootIsPlain ? page : alternateTarget;
                    unfiltered.WhereSql = explicitProject ? "yz_project_id=@current_yz_project_id" : "";
                }
                else
                    alternateTarget.WhereSql += " AND notes <> '停用备用名称'";
                catalog.ConfigPages.Add(alternateTarget);
                foreach (var targetField in catalog.ConfigPageColumns.Where(item => item.ConfigPageId == page.Id).ToArray())
                {
                    var targetCopy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(targetField))!;
                    targetCopy.Id = "202" + targetField.Id;
                    targetCopy.ConfigPageId = alternateTarget.Id;
                    catalog.ConfigPageColumns.Add(targetCopy);
                }
                field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(mixedTarget
                    ? """{"pageId":"202"}"""
                    : """{"pageId":"202","nameField":"notes"}""");
            }
            catalog.ConfigPageColumns.Add(field);
        }

        var model = GenerationModel.Create(catalog, ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var owner = model.Tables.Single(table => table.Source.Id == source.Id);
        var sourcePage = owner.Pages.Single(item => item.Source.Id == "101");
        var control = owner.Columns.Single(column => column.Source.Id == foreign.Id);
        if (mixedTarget)
        {
            await VerifyMixedTargetPages(catalog, model, owner, control, rootIsPlain, targetHasParent, nameOverride);
            return;
        }
        var selection = MultiSelectCompilation.Resolve(model, owner, sourcePage, control, forService: true);
        Assert.True(selection.Definition is not null, selection.Reason);
        if (!accountTarget)
            Assert.True(FixedPageConditionCompilation.HasMultiTargetScope(model, selection.Definition.Target, selection.Definition.TargetPage));
        Assert.Equal(hasParent && !formParent, selection.Definition.SameParent);
        if (formParent) Assert.Equal("groupId", selection.Definition.TargetParentField);
        if (sourcePair)
        {
            Assert.Equal(new[] { "group_id", "secondary_group_id" },
                selection.Definition.CompoundSourceDependencies.Select(column => column.Source.ColumnName).Order());
        }
        const long accountOwnerId = 9007199254740993L;
        var accountActor = new RuntimeActor(7, "合成归属账号", false, catalog.YzProjectId, "5")
        {
            StoreId = merchantAccount ? null : accountOwnerId,
            MerchantId = merchantAccount ? accountOwnerId : null
        };
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-project-multi-literal");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if (compound) VerifyCompoundCandidate(assembly, ns, targetStorage, catalog.YzProjectId);
            if (sourcePair)
            {
                var serviceCode = File.ReadAllText(Path.Combine(directory, "generated/Services/ScopeMultiOrderService.g.cs"));
                Assert.Contains("多选来源父字段必须指向同一条记录", serviceCode);
                Assert.Contains("Convert.ToString(entity.SecondaryGroupId, CultureInfo.InvariantCulture)", serviceCode);
                Assert.Contains("已发布的来源父键", serviceCode);
                Assert.Contains("targetParentId!=parentKey.ToString(CultureInfo.InvariantCulture)", serviceCode);
                Assert.Contains(sourceStorage == 0 ? "ForTextParent(\"secondary_group_id\"" :
                    "ApplyTextParent(query,session,\"secondary_group_id\"", serviceCode);
                Assert.Contains("entity.SecondaryGroupId!=long.Parse(parentId!, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)", serviceCode);
                await VerifyCompoundSourceWrite(assembly, ns, catalog.YzProjectId);
            }
            var service = assembly.GetType(ns + ".Generated.Services.ScopeMultiOrderService", true)!;
            if (splitSource)
            {
                var alternateRead = Assert.IsType<NonRedundantReadPlan>(
                    service.GetField("ReadPlan104", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                var expression = Expression(MySql(alternateRead));
                Assert.Contains("b.`other_group_id`", expression);
                Assert.DoesNotContain("b.`group_id`", expression);
                Assert.Contains(splitDictionary ? ".`notes`" : ".`name`", expression);
                Assert.Contains(".`status`", expression);
                if (splitDictionary)
                {
                    var read = MySql(alternateRead);
                    var constants = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                        read.GetType().GetProperty("Constants", hidden)!.GetValue(read));
                    Assert.Contains("停用备用名称", constants.Values);
                }
                Assert.Equal(parentMode == "split-source-pair", expression.Contains("b.`secondary_group_id`", StringComparison.Ordinal));
                var serviceCode = File.ReadAllText(Path.Combine(directory, "generated/Services/ScopeMultiOrderService.g.cs"));
                Assert.Contains("ForTextParent(\"other_group_id\"", serviceCode);
                Assert.Contains("ImportMulti104_15Async(", serviceCode);
                Assert.Contains("Export104Async(", serviceCode);
                var exported = Assert.IsType<NonRedundantReadPlan>(
                    service.GetField("ExportReadPlan104", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                Assert.Equal(expression, Expression(MySql(exported)));
                var filter = service.GetMethod("ReadFilters104", BindingFlags.Static | BindingFlags.NonPublic)!;
                var request = new PageRequest { ParentId = "9007199254740993" };
                var actor = new RuntimeActor(7, "合成账号", false, catalog.YzProjectId, "5");
                var filters = Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(filter.Invoke(null, [request, actor]));
                Assert.Equal(request.ParentId, Assert.Single(filters, item => item.Field == "other_group_id").Value);
                Assert.DoesNotContain(filters, item => item.Field == "group_id");
                request.ParentId = "09007199254740993";
                var invalid = Assert.Throws<TargetInvocationException>(() => filter.Invoke(null, [request, actor]));
                Assert.IsType<ArgumentException>(invalid.InnerException);
            }
            var lookup = service.GetField("MultiDerivedPlan101_15", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(lookup);
            var plan = Assert.IsType<NonRedundantReadPlan>(lookup.GetValue(null));
            var matching = MySql(BindAccount(plan));
            var matchingScope = Assert.IsType<string>(matching.GetType().GetProperty("Scope", hidden)!.GetValue(matching));
            HasDictionaryRange(matchingScope, matching);
            if (hasParent) Assert.Contains("/*metarill:nested-current*/", matchingScope);
            if (parentMode == "text" || compound) Assert.Contains("BINARY CAST(", matchingScope);
            if (compound)
            {
                Assert.Contains(".`alternate_group_id`", matchingScope);
                var types = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                    matching.GetType().GetProperty("Types", hidden)!.GetValue(matching));
                Assert.Equal("string", types["alternate_group_id"]);
            }

            if (sourceStorage == 0)
            {
                var pageRead = Assert.IsType<NonRedundantReadPlan>(
                    service.GetField("ReadPlan101", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                var read = MySql(BindAccount(pageRead));
                var expression = Expression(read);
                HasDictionaryRange(expression, read);
                if (accountTarget)
                {
                    var exported = Assert.IsType<NonRedundantReadPlan>(
                        service.GetField("ExportReadPlan101", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                    Assert.Equal(expression, Expression(MySql(BindAccount(exported))));
                }
                Assert.Contains(nameOverride ? ".`notes`" : ".`name`", expression);
                if (hasParent) Assert.Contains("b.`group_id`", expression);
                if (sourcePair)
                    Assert.Contains("BINARY CAST(b.`secondary_group_id` AS CHAR)=BINARY CAST(b.`group_id` AS CHAR)", expression);
            }
            else
            {
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains(nameField, maintenance.MixedSourceColumns["scope_multi_order"]);
                var plans = Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans", hidden)!.GetValue(maintenance));
                var published = plans[source.Id]!;
                Assert.Null(published.GetType().GetProperty("Error")!.GetValue(published));
                var read = published.GetType().GetProperty("Read")!.GetValue(published)!;
                var expression = Expression(read);
                HasDictionaryRange(expression, read);
                if (accountTarget)
                {
                    Assert.Equal(false, read.GetType().GetProperty("RequiresAccount", hidden)!.GetValue(read));
                    Assert.Contains($"b.`{ownershipField}`", expression);
                }
                if (hasParent) Assert.Contains("b.`group_id`", expression);
                if (sourcePair)
                    Assert.Contains("BINARY CAST(b.`secondary_group_id` AS CHAR)=BINARY CAST(b.`group_id` AS CHAR)", expression);
                var reverse = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                    read.GetType().GetProperty("Reverse", hidden)!.GetValue(read));
                Assert.Contains(reverse.Cast<object>(), item =>
                    Equals(target.TableName, item.GetType().GetProperty("TargetTable")!.GetValue(item)) &&
                    Assert.IsAssignableFrom<IReadOnlyDictionary<string, IReadOnlySet<string>>>(
                        item.GetType().GetProperty("Outputs")!.GetValue(item)).TryGetValue(nameField, out var fields) &&
                    (!literal || fields.Contains("status")) && fields.Contains("name") &&
                    (!accountTarget || fields.Contains(ownershipField)) &&
                    (!hasParent || fields.Contains("group_id")) &&
                    (!compound || fields.Contains("alternate_group_id")));
                if (hasParent)
                    Assert.Contains(reverse.Cast<object>(), item =>
                        Equals("scope_multi_group", item.GetType().GetProperty("TargetTable")!.GetValue(item)) &&
                        Assert.IsAssignableFrom<IReadOnlyDictionary<string, IReadOnlySet<string>>>(
                            item.GetType().GetProperty("Outputs")!.GetValue(item)).TryGetValue(nameField, out var fields) &&
                        fields.Contains("delete_no") && fields.Contains("yz_project_id"));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        if (sourcePair)
        {
            // 全局字典仍沿用原发布限制，即使该字典的父记录属于当前项目。
            var fields = catalog.ConfigColumns.Where(column => column.ConfigTableId == target.Id).Select(column => column.ColumnName);
            var targetParent = MultiTargetParentScope.Compile(catalog, target, FixedConditionPlan.Compile("group_id=@parent_id", fields));
            Assert.NotNull(targetParent);
            target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
            Assert.Throws<InvalidOperationException>(() => targetParent.Source(catalog, source, foreign, sourcePage.Source));
            target.Extra.Remove("scopeKind");
        }
        if (compound)
        {
            // 同名参数只能指向一个真实父表，两个物理字段属于不同表时必须拒绝。
            var alternate = catalog.ConfigColumns.Single(column => column.Id == "299");
            alternate.LinkConfigTableId = "4";
            var fields = catalog.ConfigColumns.Where(column => column.ConfigTableId == target.Id).Select(column => column.ColumnName);
            var syntax = FixedConditionPlan.Compile(page.WhereSql, fields);
            Assert.Throws<InvalidOperationException>(() => MultiTargetParentScope.CompileSelectionPage(catalog, target, syntax,
                allowGlobalParent: false, allowStoredPhysicalParents: true));
        }

        if (accountTarget && sourceStorage == 1)
        {
            // 隐藏来源页也能触发写入，缺少同一来源归属时应在发布前拒绝。
            var hiddenOwner = catalog.ConfigPages.Single(item => item.Id == "104");
            hiddenOwner.WhereSql = hiddenOwner.WhereSql.Replace(" AND " + ownershipPredicate, "", StringComparison.Ordinal);
            var incomplete = GenerationModel.Create(catalog, ns);
            Assert.Contains(GenerationCapabilityAudit.Collect(incomplete), gap =>
                gap.Column == foreign.ColumnName && gap.Reason.Contains("物理账号归属", StringComparison.Ordinal));
        }

        NonRedundantReadPlan BindAccount(NonRedundantReadPlan plan)
        {
            if (!accountTarget) return plan;
            Assert.True(plan.RequiresAccount);
            Assert.Throws<UnauthorizedAccessException>(() => plan.BindAccount(accountActor with { StoreId = null, MerchantId = null }));
            var bound = plan.BindAccount(accountActor);
            object expected = merchantAccount ? accountOwnerId.ToString(CultureInfo.InvariantCulture) : accountOwnerId;
            Assert.Contains(expected, Constants(MySql(bound)).Values);
            return bound;
        }

        static object MySql(NonRedundantReadPlan plan) => plan.GetType().GetProperty("MySql", hidden)!.GetValue(plan)!;
        static string Expression(object read) =>
            Assert.IsType<string>(read.GetType().GetMethod("Expression", hidden)!.Invoke(read, [nameField]));
        static IReadOnlyDictionary<string, object?> Constants(object read) =>
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                read.GetType().GetProperty("Constants", hidden)!.GetValue(read));

        void HasDictionaryRange(string sql, object read)
        {
            Assert.Equal(literal, sql.Contains(".`status`", StringComparison.Ordinal));
            Assert.Contains(".`yz_project_id`", sql);
            if (hasParent)
            {
                Assert.Contains(".`group_id`", sql);
                Assert.Contains("`scope_multi_group`", sql);
                if (compound) Assert.Contains(".`alternate_group_id`", sql);
            }
            if (literal) Assert.Contains(17, Constants(read).Values);
            if (accountTarget) Assert.Contains($".`{ownershipField}`", sql);
        }
    }

    /// <summary>从实际生成的类核对项目候选与固定候选分别读各自范围，且原送入和导出入口可用。</summary>
    private static async Task VerifyMixedTargetPages(MetadataCatalog catalog, GenerationModel model, TableModel owner,
        ColumnModel control, bool rootIsPlain, bool targetHasParent, bool nameOverride)
    {
        const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const string output = "customer_names";
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-project-mixed-target-pages");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), model.Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(model.Namespace + ".Generated.Services.ScopeMultiOrderService", true)!;
            var code = File.ReadAllText(Path.Combine(directory, "generated/Services/ScopeMultiOrderService.g.cs"));
            foreach (var (sourcePageId, dictionaryPage, scoped, parentField, display) in new[]
            {
                ("101", "201", !rootIsPlain, "group_id", nameOverride ? "notes" : "name"),
                ("104", "202", rootIsPlain, "other_group_id", "name")
            })
            {
                var sourcePage = owner.Pages.Single(item => item.Source.Id == sourcePageId);
                var selection = MultiSelectCompilation.Resolve(model, owner, sourcePage, control, forService: true);
                Assert.True(selection.Definition is not null, selection.Reason);
                Assert.Equal(dictionaryPage, selection.Definition.TargetPage.Source.Id);
                Assert.Equal(scoped && targetHasParent, selection.Definition.SameParent);
                var sql = Expression("ReadPlan" + sourcePageId);
                Assert.Equal(scoped, sql.Contains(".`status`", StringComparison.Ordinal));
                Assert.Equal(scoped && targetHasParent, sql.Contains("b.`" + parentField + "`", StringComparison.Ordinal));
                Assert.Contains(".`yz_project_id`", sql);
                Assert.Contains(".`delete_no`", sql);
                Assert.Contains(".`" + display + "`", sql);
                Assert.Equal(sql, Expression("ExportReadPlan" + sourcePageId));
                Assert.Contains("ImportMulti" + sourcePageId + "_15Async(", code);
                Assert.Equal(scoped, service.GetField("MultiDerivedPlan" + sourcePageId + "_15", fields) is not null);
            }

            string Expression(string planName)
            {
                var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField(planName, fields)!.GetValue(null));
                var read = plan.GetType().GetProperty("MySql", instance)!.GetValue(plan)!;
                return Assert.IsType<string>(read.GetType().GetMethod(nameof(Expression), instance)!.Invoke(read, [output]));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>直接执行生成的写入校验，冲突和非规范的父键在连接数据库前明确拒绝。</summary>
    private static async Task VerifyCompoundSourceWrite(Assembly assembly, string ns, string project)
    {
        using var session = new DatabaseSession(new DatabaseOptions
        {
            Provider = "mysql", ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
        });
        var service = assembly.GetType(ns + ".Generated.Services.ScopeMultiOrderService", true)!;
        var mapper = Activator.CreateInstance(assembly.GetType(ns + ".Generated.Mappers.ScopeMultiOrderMapper", true)!, session);
        var actor = new RuntimeActor(7, "合成账号", false, project, "5");
        var authorization = new CompoundSourceAuthorization(actor);
        var constructor = service.GetConstructors().Single();
        var arguments = new object?[] { session, mapper, new SnowflakeIdGenerator(), Array.Empty<IRecordMutationObserver>() };
        var instance = constructor.Invoke(arguments.Concat(constructor.GetParameters().Skip(arguments.Length)
            .Select(parameter => parameter.ParameterType == typeof(IRuntimeAuthorization) ? authorization : parameter.DefaultValue)).ToArray());
        var entityType = assembly.GetType(ns + ".Generated.Entities.ScopeMultiOrder", true)!;
        var entity = Activator.CreateInstance(entityType)!;
        entityType.GetProperty("GroupId")!.SetValue(entity, 9007199254740993L);
        entityType.GetProperty("CustomerIds")!.SetValue(entity, "[\"19\"]");
        var validation = service.GetMethod("ValidateMulti101_15Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var secondary in new[] { "9007199254740994", "09007199254740993" })
        {
            entityType.GetProperty("SecondaryGroupId")!.SetValue(entity, secondary);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => (Task)validation.Invoke(instance,
                [entity, actor, new HashSet<string>(), true, CancellationToken.None, null])!);
            Assert.Equal("多选来源父字段必须指向同一条记录。", error.Message);
        }
    }

    /// <summary>提供定向用例的当前身份和实际目标页读取权限。</summary>
    private sealed class CompoundSourceAuthorization(RuntimeActor actor) : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => actor;

        public void RequirePagePermission(string pageId, string operation)
        {
            Assert.Equal("201", pageId);
            Assert.Equal("list", operation);
        }
    }

    /// <summary>从实际生成的候选作用域验证两个物理父字段，以及字符串父键的数据库比较方式。</summary>
    private static void VerifyCompoundCandidate(Assembly assembly, string ns, int targetStorage, string project)
    {
        const string parent = "9007199254740993";
        const BindingFlags hidden = BindingFlags.NonPublic;
        var service = assembly.GetType(ns + ".Generated.Services.ScopeMultiPartyService", true)!;
        var actor = new RuntimeActor(7, "合成账号", false, project, "5");
        var request = new PageRequest { ParentId = parent };

        if (targetStorage == 0)
        {
            var filters = Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(
                service.GetMethod("ReadFilters201", hidden | BindingFlags.Static)!.Invoke(null, [request, actor]));
            Assert.Equal(9007199254740993L, Assert.Single(filters, filter => filter.Field == "group_id").Value);
            var alternate = Assert.Single(filters, filter => filter.Field == "alternate_group_id");
            Assert.Equal(parent, alternate.Value);
            var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan201", hidden | BindingFlags.Static)!.GetValue(null));
            var mysql = plan.GetType().GetProperty("MySql", hidden | BindingFlags.Instance)!.GetValue(plan);
            var comparison = typeof(MySqlNonRedundantReader).GetMethod("Filter", hidden | BindingFlags.Static)!;
            Assert.Equal("BINARY (b.`alternate_group_id`)=BINARY @filter_0", comparison.Invoke(null, [mysql, alternate, 0]));
            var invalid = new PageRequest { ParentId = "0" + parent };
            var refused = Assert.Throws<TargetInvocationException>(() =>
                service.GetMethod("ReadFilters201", hidden | BindingFlags.Static)!.Invoke(null, [invalid, actor]));
            Assert.IsType<ArgumentException>(refused.InnerException);
            return;
        }

        using var session = new DatabaseSession(new DatabaseOptions
        {
            Provider = "mysql", ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
        });
        var mapper = Activator.CreateInstance(assembly.GetType(ns + ".Generated.Mappers.ScopeMultiPartyMapper", true)!, session);
        var constructor = service.GetConstructors().Single();
        var arguments = new object?[] { session, mapper, new SnowflakeIdGenerator(), Array.Empty<IRecordMutationObserver>() };
        var instance = constructor.Invoke(arguments.Concat(constructor.GetParameters().Skip(arguments.Length)
            .Select(parameter => parameter.DefaultValue)).ToArray());
        var query = service.GetMethod("Query201", hidden | BindingFlags.Instance)!;
        var queryArgs = new object?[] { request, actor };
        var result = query.Invoke(instance, queryArgs.Concat(query.GetParameters().Skip(queryArgs.Length)
            .Select(parameter => parameter.DefaultValue)).ToArray());
        var sql = Assert.IsType<string>(typeof(DerivedSingleSelectionTests).GetMethod(nameof(SelectionSql), hidden | BindingFlags.Static)!
            .MakeGenericMethod(assembly.GetType(ns + ".Generated.Entities.ScopeMultiParty", true)!).Invoke(null, [result]));
        Assert.Contains("`group_id`", sql);
        Assert.Contains("BINARY (`a`.`alternate_group_id`)=BINARY", sql);
    }

    /// <summary>从实际生成的查询上提取 WHERE，不让投影列影响范围断言。</summary>
    private static string SelectionSql<T>(object query) where T : class => ((FreeSql.ISelect<T>)query).ToSql("1");

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
