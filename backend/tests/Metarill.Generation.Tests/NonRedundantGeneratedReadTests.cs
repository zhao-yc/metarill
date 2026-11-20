using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式生成入口的读写分离与手写保护；真实HTTP/SQL另测。</summary>
public sealed class NonRedundantGeneratedReadTests
{
    /// <summary>实体忽略派生写列，Service使用固定读计划，原完整表单回显继续可回传。</summary>
    [Fact]
    public async Task Official_generation_builds_nonredundant_crud_and_protects_handwritten_partial()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-generated",Guid.NewGuid().ToString("N"));var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");NonRedundantGeneratedFixture.AddSelectionPage(catalog);catalog.ConfigPages[0].IsExport=1;NonRedundantGeneratedFixture.EnableImports(catalog);
        try
        {
            var generated=Path.Combine(root,"generated");var first=new CSharpGenerator().Generate(catalog,generated,"Example.NonRedGenerated");Assert.NotEmpty(first.GeneratedFiles);Assert.DoesNotContain("GeneratedNonRedundantReadCatalog.g.cs",first.GeneratedFiles);Assert.Contains("GeneratedNonRedundantReadCatalog",await File.ReadAllTextAsync(Path.Combine(generated,"GeneratedRegistration.g.cs")));
            var entity=await File.ReadAllTextAsync(Path.Combine(generated,"Entities/MtNrgOfflineOrder.g.cs"));Assert.Contains("Name = \"customer_notes\", IsIgnore = true",entity);Assert.Contains("Name = \"customer_amount\", IsIgnore = true",entity);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtNrgOfflineOrderService.g.cs"));Assert.Contains("GeneratedNonRedundantReadCatalog.Compile",service);Assert.Contains("SelectionSelected102Async",service);Assert.Contains("selectedRows.Count!=selected.Length",service);Assert.Contains("KeyRowsAsync(ReadPlan102,filters,selected",service);Assert.Contains("KeyRowsAsync(ReadPlan101,filters,[id]",service);Assert.Contains("PageAsync(ReadPlan101",service);Assert.DoesNotContain("Query102(",service);Assert.Contains("await ReadDetail101Async(entity.Id,parentId,actor,cancellationToken,true)",service);Assert.Contains("currentRead:currentRead",service);Assert.DoesNotContain("entity.CustomerNotes = source.CustomerNotes",service);
            var vo=await File.ReadAllTextAsync(Path.Combine(generated,"VOs/MtNrgOfflineOrderPage101Vo.g.cs"));Assert.Contains("customerName",vo);Assert.DoesNotContain("YzProjectId",vo);Assert.DoesNotContain("GroupId",vo);
            var handwritten=Path.Combine(generated,"Services/MtNrgOfflineOrderService.cs");await File.AppendAllTextAsync(handwritten,"\n// 本次手写保护标记。\n");var before=await File.ReadAllBytesAsync(handwritten);new CSharpGenerator().Generate(catalog,generated,"Example.NonRedGenerated");Assert.Equal(before,await File.ReadAllBytesAsync(handwritten));
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    /// <summary>两个业务页共享原稳定名称属性，分别使用表级名称及本页覆盖的简称。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("customer.name")]
    [InlineData("customer_id.name")]
    public async Task Page_list_label_override_retains_original_auxiliary_and_excel_contract(string? displayPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "metarill-list-label-override", Guid.NewGuid().ToString("N"));
        var catalog = NonRedundantGeneratedFixture.Catalog("mt_page_label");
        if (displayPath is not null)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "118", ConfigTableId = "1", ColumnName = "customer_name", Name = "客户名称",
                LogicalType = "string", ColumnLength = "100",
                Extra = new()
                {
                    ["isJoinColumn"] = JsonSerializer.SerializeToElement(1),
                    ["columnFullName"] = JsonSerializer.SerializeToElement(displayPath)
                }
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "101118", ConfigPageId = "101", ConfigColumnId = "118", IsShow = 1,
                Extra = new() { ["isSort"] = JsonSerializer.SerializeToElement(1) }
            });
        }
        var source = catalog.ConfigPages.Single(page => page.Id == "101");
        source.IsExport = 1;
        source.IsImport = 1;
        source.Extra["importMethod"] = JsonSerializer.SerializeToElement("append");
        var copy = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        copy.Id = "104";
        copy.Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");
        copy.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigPages.Add(copy);
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == source.Id).ToArray())
        {
            var second = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            second.Id = copy.Id + field.ConfigColumnId;
            second.ConfigPageId = copy.Id;
            catalog.ConfigPageColumns.Add(second);
        }
        var target = catalog.ConfigColumns.Single(column => column.Id == "25");
        var alias = JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(target))!;
        alias.Id = "28";
        alias.ColumnName = "short_name";
        alias.Name = "简称";
        catalog.ConfigColumns.Add(alias);
        catalog.ConfigPageColumns.Add(new() { Id = "20128", ConfigPageId = "201", ConfigColumnId = "28", IsShow = 1 });
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == copy.Id && field.ConfigColumnId == "15")
            .Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"nameField":"short_name"}""");
        NonRedundantGeneratedFixture.EnableImports(catalog);
        copy.IsImport = 1;
        try
        {
            var generated = Path.Combine(root, "generated");
            var result = new CSharpGenerator().Generate(catalog, generated, "Example.PageLabel");
            Assert.NotEmpty(result.GeneratedFiles);
            var service = await File.ReadAllTextAsync(Path.Combine(generated, "Services/MtPageLabelOrderService.g.cs"));
            Assert.Contains("DisplayNameColumnId=\"28\"", service);
            Assert.Contains("entity.ShortName", service);
            foreach (var pageId in new[] { "101", "104" })
            {
                var vo = await File.ReadAllTextAsync(Path.Combine(generated, $"VOs/MtPageLabelOrderPage{pageId}Vo.g.cs"));
                Assert.Contains("customerName", vo);
                Assert.DoesNotContain("customerShortName", vo);
            }
            var originalDetail = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/mt_page_label_order-detail-104.vue"));
            Assert.Contains("this.entity.customerName=item ? item.name", originalDetail);
            Assert.Contains("readonlyDisplayText(entity.customerName)", originalDetail);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType = assembly.GetType("Example.PageLabel.Generated.Services.MtPageLabelOrderService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>原单选按实际页面名称显示、筛选并解析Excel，两个页面使用同一响应属性。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Select_page_label_override_keeps_the_filter_and_selection_receipt(bool declaredDisplay)
    {
        var catalog = NonRedundantApplicationFixture.Catalog("mt_single_page_label");
        if (!declaredDisplay)
        {
            catalog.ConfigColumns.RemoveAll(column => column.Id == "111");
            catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "111");
        }
        catalog.ConfigColumns.Add(new()
        {
            Id = "28", ConfigTableId = "2", YzProjectId = catalog.YzProjectId,
            ColumnName = "short_name", Name = "简称", LogicalType = "string", ColumnLength = "100"
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id = "20128", ConfigPageId = "201", ConfigColumnId = "28", YzProjectId = catalog.YzProjectId, IsShow = 1
        });
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId is "104" or "105" && field.ConfigColumnId == "15"))
            field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"nameField":"short_name"}""");
        catalog.ConfigPages.Single(page => page.Id == "104").Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");
        var model = GenerationModel.Create(catalog, "Example.SinglePageLabel");
        var table = model.Tables.Single(item => item.Source.Id == "1");
        var page = table.Pages.Single(item => item.Source.Id == "104");
        var filter = Assert.Single(SelectionNameFilterCompilation.ForPage(model, table, page).Bindings);
        Assert.Equal("customerName", filter.FilterKey);
        Assert.Equal(SelectionNameFilterKind.JoinedScalarLike, filter.Kind);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var projection = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(projection.Detail.CanCompile, string.Join(" / ", projection.Detail.Gaps.Select(gap => gap.Reason)));
        Assert.True(projection.List.CanCompile, string.Join(" / ", projection.List.Gaps.Select(gap => gap.Reason)));
        Assert.Contains(projection.Detail.OutputFields, field => field.JsonName == "customerName");
        Assert.Contains(projection.List.OutputFields, field => field.JsonName == "customerName");

        var root = Path.Combine(Path.GetTempPath(), "metarill-single-page-label", Guid.NewGuid().ToString("N"));
        try
        {
            var package = new ApplicationPackager().Create(catalog, Path.Combine(root, "application"),
                model.Namespace, NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication, package.Mode);
            Assert.True(package.FrontendIncluded);
            // 生成项目的编译目录独立于完整包，避免 SDK 的默认递归包含两套源码。
            var probe = Path.Combine(root, "probe");
            var generated = Path.Combine(probe, "generated");
            new CSharpGenerator().Generate(catalog, generated, model.Namespace);
            var detail = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/mt_single_page_label_order-detail-104.vue"));
            Assert.Contains("this.entity.customerName=row.shortName", detail);
            Assert.Contains("selected-name-field=\"shortName\"", detail);
            var list = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/mt_single_page_label_order-list-104.vue"));
            Assert.Contains("customerName", list);
            var service = await File.ReadAllTextAsync(Path.Combine(generated, "Services/MtSinglePageLabelOrderService.g.cs"));
            Assert.Contains("DisplayNameColumnId=\"28\"", service);
            Assert.Contains("entity.ShortName", service);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(probe);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                assembly.GetType("Example.SinglePageLabel.Generated.Services.MtSinglePageLabelOrderService", true)!.TypeHandle);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>原单选用 ID 显示时，原筛选和 Excel 使用物理外键并保留独立目标父范围。</summary>
    [Theory]
    [InlineData(0,1)]
    [InlineData(0,0)]
    [InlineData(1,1)]
    public async Task Single_id_display_preserves_exact_web_filter_and_excel(int storage,int targetStorage)
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_single_id");
        catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=storage;
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=targetStorage;
        // R1 对照保留原名称缓存，本用例只在 ID 页开启筛选。
        if(storage==1)
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="15"))
                field.Extra.Remove("isSearch");
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId is "104" or "105"&&field.ConfigColumnId=="15"))
            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"id"}""");
        var page=catalog.ConfigPages.Single(page=>page.Id=="104");page.IsExport=1;
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(targetStorage==0?"list":"edit");
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId=="15")
            .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        const string ns="Example.SingleIdDisplay";
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(table=>table.Source.Id=="1");
        var actual=table.Pages.Single(page=>page.Source.Id=="104");
        Assert.Empty(SelectionNameFilterCompilation.ForPage(model,table,actual).Gaps);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var root=Path.Combine(Path.GetTempPath(),"metarill-single-id",Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(root,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var list=await File.ReadAllTextAsync(Path.Combine(generated,"frontend/pages/yzp/mt_single_id_order-list-104.vue"));
            Assert.Contains("filters.customerId",list);
            Assert.DoesNotContain("filters.customerName",list);
            var detail=await File.ReadAllTextAsync(Path.Combine(generated,"frontend/pages/yzp/mt_single_id_order-detail-104.vue"));
            Assert.Contains("selected-name-field=\"id\"",detail);
            Assert.Contains("readonlyDisplayText(entity.customerId)",detail);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtSingleIdOrderService.g.cs"));
            Assert.Contains("case \"customerId\":",service);
            Assert.Contains("ImportRelation105_15Async",service);
            Assert.Contains("namesCustomerId",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType=assembly.GetType(ns+".Generated.Services.MtSingleIdOrderService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            if(storage==0)
            {
                var request=new Metarill.Core.Contracts.PageRequest
                {
                    Filters=new(){{"customerId",JsonSerializer.SerializeToElement("9007199254740993")}}
                };
                var actor=new Metarill.Core.Security.RuntimeActor(1,"单选 ID",false,catalog.YzProjectId,"5");
                var filters=(IReadOnlyList<Metarill.Data.Queries.FixedReadFilter>)serviceType
                    .GetMethod("ReadFilters104",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!
                    .Invoke(null,[request,actor])!;
                var filter=Assert.Single(filters,item=>item.Field=="customer_id");
                Assert.Equal(Metarill.Data.Queries.FixedReadComparison.Equal,filter.Comparison);
                Assert.Equal(9007199254740993L,Assert.IsType<long>(filter.Value));
            }
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),
                ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    /// <summary>原普通下拉按真实主键显示，候选、回显、筛选及两种工作簿入口继续走本项目同父范围。</summary>
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 0, true)]
    public async Task List_id_display_preserves_scoped_options_and_workbook(int sourceStorage, int targetStorage, bool pageOverride)
    {
        var catalog = NonRedundantGeneratedFixture.Catalog("mt_list_id");
        NonRedundantGeneratedFixture.EnableSingleSelection(catalog, targetParent: true);
        NonRedundantGeneratedFixture.EnableImports(catalog);
        foreach (var item in catalog.ConfigTables) item.YzProjectId = catalog.YzProjectId;
        foreach (var item in catalog.ConfigPages) item.YzProjectId = catalog.YzProjectId;
        foreach (var item in catalog.ConfigColumns) item.YzProjectId = catalog.YzProjectId;
        catalog.ConfigTables.Single(table => table.Id == "1").IsRongyu = sourceStorage;
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = targetStorage;
        var source = catalog.ConfigColumns.Single(column => column.Id == "15");
        source.ControlKind = "List";
        source.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("201");
        source.ControlDetail = JsonSerializer.Serialize(new
        {
            tableId = "2", table = "mt_list_id_party", nameField = pageOverride ? "name" : "id"
        });
        catalog.ConfigPages.Single(page => page.Id == "201").Extra.Remove("type");
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "101");
        sourcePage.IsExport = 1;
        sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(targetStorage == 0 ? "list" : "edit");
        var field = catalog.ConfigPageColumns.Single(item => item.ConfigPageId == sourcePage.Id && item.ConfigColumnId == source.Id);
        field.Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        if (pageOverride)
        {
            foreach (var setting in catalog.ConfigPageColumns.Where(item => item.ConfigColumnId == source.Id))
                setting.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"nameField":"id"}""");
            var namePage = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;
            namePage.Id = "104";
            namePage.IsImport = namePage.IsExport = 0;
            catalog.ConfigPages.Add(namePage);
            foreach (var setting in catalog.ConfigPageColumns.Where(item => item.ConfigPageId == sourcePage.Id).ToArray())
            {
                var clone = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(setting))!;
                clone.Id = namePage.Id + clone.ConfigColumnId;
                clone.ConfigPageId = namePage.Id;
                clone.Extra.Remove("controlDetail");
                catalog.ConfigPageColumns.Add(clone);
            }
        }

        const string ns = "Example.ListIdDisplay";
        var model = GenerationModel.Create(catalog, ns);
        var table = model.Tables.Single(item => item.Source.Id == "1");
        var pageModel = table.Pages.Single(page => page.Source.Id == "101");
        var relation = Assert.Single(RelationCompilation.ForPage(model, pageModel),
            item => item.SourceColumn.Source.Id == source.Id);
        Assert.True(relation.NameColumn.IsId);
        Assert.Contains(relation.TargetPage.Scopes, scope => scope.Source == "parent");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var root = Path.Combine(Path.GetTempPath(), "metarill-list-id", Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(root, "generated");
            new CSharpGenerator().Generate(catalog, generated, ns);
            var list = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/mt_list_id_order-list-101.vue"));
            Assert.Contains("filters.customerId", list);
            Assert.Contains("relationDisplayText(item.customerId)", list);
            var detail = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/mt_list_id_order-detail-101.vue"));
            Assert.Contains("readonlyDisplayText(entity.customerId)", detail);
            Assert.Contains("<zxz-uni-data-select", detail);
            Assert.DoesNotContain("this.entity.customerName=item", detail);
            if (pageOverride)
            {
                var nameList = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/mt_list_id_order-list-104.vue"));
                Assert.Contains("relationDisplayText(item.customerName)", nameList);
            }
            var service = await File.ReadAllTextAsync(Path.Combine(generated, "Services/MtListIdOrderService.g.cs"));
            Assert.Contains("ImportRelation101_15Async", service);
            Assert.Contains("ImportRelation103_15Async", service);
            Assert.Contains("namesCustomerId", service);
            Assert.Contains("originalCandidateValues.CustomerId", service);
            Assert.Contains("filters.Add(new(\"group_id\",FixedReadComparison.Equal", service);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType = assembly.GetType(ns + ".Generated.Services.MtListIdOrderService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            var readOption = serviceType.GetMethod("ReadDerivedOption101_15",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var option = readOption.Invoke(null, [new Dictionary<string, object?> { ["id"] = 9007199254740993L }]);
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            jsonOptions.Converters.Add(new Metarill.Core.Contracts.StringLongJsonConverter());
            var json = JsonSerializer.SerializeToElement(option, jsonOptions);
            foreach (var key in new[] { "id", "name", "value", "text" })
                Assert.Equal("9007199254740993", json.GetProperty(key).GetString());
            Assert.Equal(ApplicationPackageMode.FullApplication, new ApplicationPackager().Create(catalog,
                Path.Combine(root, "application"), ns, NonRedundantApplicationFixture.Root()).Mode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>未接线原功能不能以静默退化方式生成；公开完整包仍保留能力门槛。</summary>
    [Theory]
    [InlineData("customImport")][InlineData("global")][InlineData("select")][InlineData("filterType")][InlineData("conflictingTarget")][InlineData("effectiveOverride")]
    public void Unsupported_usages_still_reject_before_files(string usage)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");var page=catalog.ConfigPages[0];if(usage=="customImport"){page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("custom");}else if(usage=="global")catalog.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");else if(usage=="select")catalog.ConfigColumns.Single(column=>column.ColumnName=="customer_id").ControlKind="SelectPage";else if(usage=="filterType")catalog.ConfigColumns.Single(column=>column.Id=="14").LogicalType="json";else if(usage=="conflictingTarget")catalog.ConfigColumns.Single(column=>column.ColumnName=="customer_id").LinkConfigTableId="3";else catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"unpublished_label\"}");
        var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-reject",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.NonRedGenerated"));Assert.False(Directory.Exists(root));
    }
    /// <summary>自动名称即使被标为可编辑，也不能成为物理导入列。</summary>
    [Fact]
    public void Nonredundant_import_rejects_conflicting_display_columns_before_files()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");catalog.ConfigPages[0].IsImport=1;
        catalog.ConfigColumns.Add(new(){Id="111",ConfigTableId="1",ColumnName="customer_name",Name="客户名称",LogicalType="string",ColumnLength="100"});
        catalog.ConfigPageColumns.Add(new(){Id="101111",ConfigPageId="101",ConfigColumnId="111",IsEdit=1});
        var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-import-physical",Guid.NewGuid().ToString("N"));
        var error=Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.NonRedImport"));
        Assert.Contains("关联显示别名与真实业务字段冲突",error.Message);Assert.False(Directory.Exists(directory));
    }
    /// <summary>选择页不能以未声明项目归属的表形成不受限回读。</summary>
    [Fact]
    public void Nonredundant_selection_without_project_scope_rejects_before_writes()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");NonRedundantGeneratedFixture.AddSelectionPage(catalog);catalog.ConfigColumns.RemoveAll(column=>column.Id=="12");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="12");var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-selection-scope",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.NonRedSelection"));Assert.False(Directory.Exists(directory));
    }
    /// <summary>无有效变化的原页覆盖继续兼容，不能因JSON格式不同拒绝。</summary>
    [Theory]
    [InlineData("{}")][InlineData("{ \"nameField\": \"name\", \"table\": \"mt_nrg_offline_party\", \"tableId\": \"2\" }")]
    public void Equivalent_page_overrides_keep_the_same_relation(string overlay)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay);var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-overrides",Guid.NewGuid().ToString("N"));try{Assert.NotEmpty(new CSharpGenerator().Generate(catalog,root,"Example.NonRedGenerated").GeneratedFiles);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}

/// <summary>同一元数据样本同时驱动生成、DDL及实际HTTP，隐藏项目/父键不加入VO。</summary>
internal static class NonRedundantGeneratedFixture
{
    /// <summary>所有表和页面来自同一测试元数据，不手写业务CRUD。</summary>
    internal static MetadataCatalog Catalog(string prefix)
    {
        var catalog=new MetadataCatalog{YzProjectId="54",ConfigTables=[new(){Id="1",TableName=prefix+"_order",IsRongyu=0},new(){Id="2",TableName=prefix+"_party"},new(){Id="3",TableName=prefix+"_group"}],YzAdminTypes=[new(){Id="5",YzProjectId="54"}],ConfigPages=[new(){Id="101",ConfigTableId="1",YzAdminTypeId="5",WhereSql="group_id=@parent_id",OrderBy="customer_notes asc,id desc",IsCopy=1,Extra=new(){{"isSum",JsonSerializer.SerializeToElement(1)}}},new(){Id="201",ConfigTableId="2",YzAdminTypeId="5"},new(){Id="301",ConfigTableId="3",YzAdminTypeId="5"}]};
        catalog.ConfigColumns=[C("11","1","id","int64"),C("12","1","yz_project_id","int64"),C("13","1","delete_no","datetime"),C("14","1","name"),C("15","1","customer_id","int64"),C("16","1","customer_notes"),C("17","1","customer_amount","decimal"),C("18","1","status","int32"),C("19","1","group_id","int64"),C("110","1","private_payload"),C("21","2","id","int64"),C("22","2","yz_project_id","int64"),C("23","2","delete_no","datetime"),C("24","2","name"),C("25","2","notes"),C("26","2","amount","decimal"),C("31","3","id","int64"),C("32","3","yz_project_id","int64"),C("33","3","delete_no","datetime"),C("34","3","name")];
        var customer=catalog.ConfigColumns.Single(column=>column.Id=="15");customer.ControlKind="List";customer.LinkConfigTableId="2";customer.ControlDetail=JsonSerializer.Serialize(new{tableId="2",table=prefix+"_party",nameField="name"});catalog.ConfigColumns.Single(column=>column.Id=="19").LinkConfigTableId="3";
        foreach(var (id,path) in new[]{("16","customer.notes"),("17","customer.amount")})catalog.ConfigColumns.Single(column=>column.Id==id).Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement(path)}};
        foreach(var page in catalog.ConfigPages)foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId))
        {var visible=column.ColumnName is not("yz_project_id" or "group_id" or "delete_no");catalog.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsShow=visible&&column.ColumnName!="private_payload"?1:0,IsEdit=visible?1:0,Extra=new(){{"isSearch",JsonSerializer.SerializeToElement(column.ColumnName is "name" or "customer_notes"?1:0)},{"isSort",JsonSerializer.SerializeToElement(visible?1:0)},{"sumKind",JsonSerializer.SerializeToElement(column.ColumnName=="customer_amount"?1:column.ColumnName=="status"?2:0)}}});}
        return catalog;
    }
    /// <summary>原type=2独立页只增加读取合同，不启用多值关系字段或改变前端流程。</summary>
    internal static void AddSelectionPage(MetadataCatalog catalog)
    {
        var source=catalog.ConfigPages.Single(page=>page.Id=="101");var selection=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;selection.Id="102";selection.IsNew=0;selection.IsSave=0;selection.IsDelete=0;selection.IsCopy=0;selection.Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigPages.Add(selection);
        var fields=catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray();foreach(var field in fields){field.Id="102"+field.Id;field.ConfigPageId="102";catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>原单选页与显式派生辅助名；可选同父候选验证实际父范围接线。</summary>
    internal static void EnableSingleSelection(MetadataCatalog catalog,bool targetParent)
    {
        var source=catalog.ConfigColumns.Single(column=>column.Id=="15");source.ControlKind="SelectPage";
        source.ControlDetail=JsonSerializer.Serialize(new{pageId="201",tableId="2",table=catalog.ConfigTables.Single(table=>table.Id=="2").TableName,nameField="name"});
        catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var display=C("111","1","customer_name");display.Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("customer.name")}};catalog.ConfigColumns.Add(display);
        catalog.ConfigPageColumns.Add(new(){Id="101111",ConfigPageId="101",ConfigColumnId="111",IsEdit=1,IsShow=0});
        if(targetParent)
        {
            var parent=C("27","2","group_id","int64");parent.LinkConfigTableId="3";catalog.ConfigColumns.Add(parent);
            catalog.ConfigPageColumns.Add(new(){Id="20127",ConfigPageId="201",ConfigColumnId="27"});catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="group_id=@parent_id";
        }
    }
    /// <summary>独立目标父来自本对象物理外键，不借来源parentId；整行导入通过独立开关启用。</summary>
    internal static void AddMappedSinglePage(MetadataCatalog catalog)
    {
        var source=catalog.ConfigPages.Single(page=>page.Id=="101");var mapped=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        mapped.Id="104";mapped.WhereSql="";mapped.IsImport=0;mapped.Extra.Remove("importMethod");catalog.ConfigPages.Add(mapped);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray())
        {field.Id="104"+field.Id;field.ConfigPageId="104";if(field.ConfigColumnId=="19")field.IsEdit=1;catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>跨父追加与覆盖保持同一Excel接口，目标父来自整行或锁定原对象。</summary>
    internal static void EnableMappedImports(MetadataCatalog catalog)
    {
        var group=catalog.ConfigColumns.Single(column=>column.Id=="19");group.ControlKind="List";group.DefaultValue="71";
        group.ControlDetail=JsonSerializer.Serialize(new{tableId="3",table=catalog.ConfigTables.Single(table=>table.Id=="3").TableName,nameField="name"});
        var source=catalog.ConfigPages.Single(page=>page.Id=="104");source.IsImport=1;
        var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        overwrite.Id="105";overwrite.IsNew=0;overwrite.IsSave=0;overwrite.IsDelete=0;overwrite.IsCopy=0;overwrite.IsExport=0;
        overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(overwrite);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="104").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray())
        {field.Id="105"+field.Id;field.ConfigPageId="105";catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>追加与覆盖分别沿用原导入开关；覆盖页不额外开放普通新增/编辑按钮。</summary>
    internal static void EnableImports(MetadataCatalog catalog)
    {
        var source=catalog.ConfigPages.Single(page=>page.Id=="101");source.IsImport=1;
        catalog.ConfigColumns.Single(column=>column.Id=="14").Name="记录名";
        // 原V8覆盖键按表字段排序选首个可导入列，页面排序不能另换匹配键。
        catalog.ConfigColumns.Single(column=>column.Id=="14").SortNo=-1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="14").SortNo=-1;
        var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;
        overwrite.Id="103";overwrite.IsNew=0;overwrite.IsSave=0;overwrite.IsDelete=0;overwrite.IsCopy=0;overwrite.IsExport=0;overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(overwrite);
        var fields=catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray();
        foreach(var field in fields){field.Id="103"+field.Id;field.ConfigPageId="103";catalog.ConfigPageColumns.Add(field);}
    }
    /// <summary>只声明逻辑类型和长度，具体DDL仍走正式SchemaService。</summary>
    private static ColumnMetadata C(string id,string table,string name,string type="string")=>new(){Id=id,ConfigTableId=table,ColumnName=name,Name=name,LogicalType=type,ColumnLength="100",IsDbNotNull=name=="id"?1:0};
}
