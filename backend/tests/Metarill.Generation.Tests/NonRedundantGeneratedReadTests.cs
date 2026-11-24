using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式生成入口的读写分离与手写保护；真实HTTP/SQL另测。</summary>
public sealed class NonRedundantGeneratedReadTests
{
    /// <summary>生成实际 MySQL 查询文本，无需打开数据库连接。</summary>
    private static string SelectionSql<T>(object query) where T:class=>((FreeSql.ISelect<T>)query).ToSql("1");
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
    [InlineData(0,0,true)]
    [InlineData(0,1,true,true)]
    [InlineData(1,0,true,true,true)]
    [InlineData(1,1,true)]
    [InlineData(0,0,true,false,true)]
    [InlineData(0,0,true,true,false,true)]
    [InlineData(0,1,true,true,false,true,true)]
    [InlineData(0,0,true,false,false,true,true)]
    [InlineData(0,1,true,false,false,true)]
    [InlineData(1,0,true,true,false,true)]
    [InlineData(1,1,true,true,false,true,true)]
    [InlineData(1,0,true,false,false,true,true)]
    [InlineData(1,1,true,false,false,true)]
    [InlineData(1,0,true,true,false,true,true)]
    [InlineData(1,1,true,true,false,true)]
    [InlineData(1,0,true,false,false,true)]
    [InlineData(1,1,true,false,false,true,true)]
    [InlineData(1,0,true,true,false,true,false,true)]
    [InlineData(1,0,true,true,false,true,true,true)]
    [InlineData(1,1,true,true,false,true,false,true)]
    [InlineData(1,1,true,true,false,true,true,true)]
    [InlineData(1,0,true,false,false,true,false,true)]
    [InlineData(1,0,true,false,false,true,true,true)]
    [InlineData(1,1,true,false,false,true,false,true)]
    [InlineData(1,1,true,false,false,true,true,true)]
    [InlineData(1,0,true,false,false,false,true,true)]
    [InlineData(1,0,true,true,false,false,true,true)]
    [InlineData(1,1,true,false,false,false,true,true)]
    [InlineData(1,1,true,true,false,false,true,true)]
    public async Task Single_id_display_preserves_exact_web_filter_and_excel(
        int storage,int targetStorage,bool global=false,bool fieldId=false,bool accountParent=false,
        bool globalSource=false,bool rowProject=false,bool fixedOnly=false)
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_single_id");
        catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=storage;
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=targetStorage;
        if(global)
        {
            // 全局选档使用原 type=1 页面，没有目标行项目列时仍执行发布条件。
            catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "22" or "16" or "17" || fieldId&&column.Id=="111");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "22" or "16" or "17" ||
                fieldId&&field.ConfigColumnId=="111");
            foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                sourcePage.OrderBy="id desc";
            var target=catalog.ConfigPages.Single(page=>page.Id=="201");
            catalog.ConfigColumns.Add(new(){Id="212",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                ColumnName="status",LogicalType="int32"});
            target.WhereSql="status=83";
            if(accountParent)
            {
                catalog.ConfigColumns.Add(new(){Id="213",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                    ColumnName="store_id",LogicalType="int64"});
                target.WhereSql+=" AND group_id=@parent_id AND store_id=@current_store_id";
            }
            if(fieldId)
            {
                var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
                foreign.ControlDetail="""{"pageId":"201","nameField":"id"}""";
                foreign.LinkConfigTableId="";
            }
        }
        if(globalSource)
        {
            // 全局来源组合使用独立字典，隔离本次主键显示与既有普通项目父级。
            catalog.ConfigTables.Single(table=>table.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            var omitted = rowProject ? new[] { "19", "27" } : new[] { "12", "19", "27" };
            catalog.ConfigColumns.RemoveAll(column => omitted.Contains(column.Id));
            // 这组只消费字典的 type=1 页，源表未参与 type=2 多选。
            catalog.ConfigPages.RemoveAll(page => page.Id == "102");
            catalog.ConfigPageColumns.RemoveAll(field => field.ConfigPageId == "102" || omitted.Contains(field.ConfigColumnId));
            foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                sourcePage.WhereSql=rowProject?"yz_project_id=@current_yz_project_id":"";
            if(storage==1&&!fixedOnly)
            {
                // 复用全局单选已支持的账号范围，并保留原固定状态过滤。
                catalog.ConfigColumns.Add(new(){Id="213",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                    ColumnName="store_id",LogicalType="int64"});
                catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND store_id=@current_store_id";
                if(!fieldId)
                {
                    // 冗余名称回填从实际业务行取归属，所有该表入口必须写入同一归属。
                    catalog.ConfigColumns.Add(new(){Id="113",ConfigTableId="1",YzProjectId=catalog.YzProjectId,
                        ColumnName="store_id",LogicalType="int64"});
                    foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                        sourcePage.WhereSql=rowProject?sourcePage.WhereSql+" AND store_id=@current_store_id":"store_id=@current_store_id";
                }
            }
        }

        // R1 对照保留原名称缓存，本用例只在 ID 页开启筛选。
        if(storage==1)
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="15"))
                field.Extra.Remove("isSearch");
        if(fixedOnly&&!fieldId)
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15")
                .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>(field.ConfigPageId is "104" or "105")&&field.ConfigColumnId=="15"&&!fieldId))
            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"id"}""");
        var page=catalog.ConfigPages.Single(page=>page.Id=="104");page.IsExport=1;
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(targetStorage==0?"list":"edit");
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId=="15")
            .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        const string ns="Example.SingleIdDisplay";
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(table=>table.Source.Id=="1");
        var actual=table.Pages.Single(page=>page.Source.Id=="104");
        if (globalSource)
        {
            Assert.True(table.IsGlobal);
            Assert.Equal(rowProject, FixedPageQueryCompilation.For(table,actual).IdentityScopes
                .Any(scope=>scope.Source=="project"&&scope.Column.Source.ColumnName=="yz_project_id"));
        }
        var selection=NonRedundantSelectionCompilation.Resolve(model,table,actual,table.Columns.Single(column=>column.Source.Id=="15"));
        Assert.True(selection.Name.IsId);
        Assert.Equal("201",selection.TargetPage.Source.Id);
        if (fixedOnly)
        {
            var relation=Assert.Single(RelationCompilation.ForPage(model,actual), candidate=>candidate.SourceColumn.Source.Id=="15");
            Assert.True(relation.FixedGlobalSelection);
            Assert.False(NonRedundantReadCompilation.UsesProjectSingleRead(relation));
        }
        Assert.Empty(SelectionNameFilterCompilation.ForPage(model,table,actual).Gaps);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if (storage == 1 && !global)
            Assert.Contains("customer_name", MixedRelationPropagationCompilation.ForModel(model)
                .MixedSourceColumns["mt_single_id_order"]);
        if(storage==1&&globalSource)
        {
            var binding=Assert.Single(GlobalCachedSourceCompilation.ForModel(model),
                candidate=>candidate.Source.Source.Id=="1"&&candidate.Foreign.Source.Id=="15");
            var caches=MixedRelationPropagationCompilation.ForModel(model)
                .MixedSourceColumns.GetValueOrDefault("mt_single_id_order",[]);
            if(fieldId)
            {
                Assert.Null(binding.Cache);
                Assert.Empty(caches);
                Assert.DoesNotContain(table.Columns,column=>column.Source.ColumnName=="customer_id_id");
            }
            else
            {
                Assert.Equal("customer_name",binding.Cache?.Source.ColumnName);
                Assert.Contains("customer_name",caches);
            }
        }
        if(fixedOnly&&!fieldId)
        {
            // 同一实际字典页同时决定原名称筛选和名称重算，改状态也要触发反查。
            var namedPage=table.Pages.Single(candidate=>candidate.Source.Id=="101");
            var namedFilter=SelectionNameFilterCompilation.ForPage(model,table,namedPage);
            Assert.Empty(namedFilter.Gaps);
            Assert.Contains(namedFilter.Bindings,filter=>filter.Source.Source.Id=="15"&&filter.FilterKey=="customerName");
            var cachePlan=Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                candidate=>candidate.Cache.Source.ColumnName=="customer_name");
            Assert.Equal("201",cachePlan.ScopedSinglePageId);
            var definitions=RelationPropagationCompilation.Definitions([cachePlan]);
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic;
            var refresh=typeof(Metarill.Data.Queries.MySqlNonRedundantReadCompiler)
                .GetMethod("CompileRefresh",flags|System.Reflection.BindingFlags.Static)!
                .Invoke(null,[catalog,"1",new[]{"customer_name"},definitions,null])!;
            var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                refresh.GetType().GetProperty("Constants",flags|System.Reflection.BindingFlags.Instance)!.GetValue(refresh));
            Assert.Contains(83,constants.Values);
            var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                refresh.GetType().GetProperty("Reverse",flags|System.Reflection.BindingFlags.Instance)!.GetValue(refresh));
            Assert.Contains(reverse.Cast<object>(),reference=>
                reference.GetType().GetProperty("Outputs")!.GetValue(reference) is
                    IReadOnlyDictionary<string,IReadOnlySet<string>> dependencies &&
                dependencies.TryGetValue("customer_name",out var fields)&&fields.Contains("status"));
        }
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
            if(global&&!fieldId)
            {
                var original=await File.ReadAllTextAsync(Path.Combine(generated,"frontend/pages/yzp/mt_single_id_order-detail-101.vue"));
                Assert.Contains("selected-name-field=\"name\"",original);
            }
            Assert.Contains("case \"customerId\":",service);
            Assert.Contains("ImportRelation105_15Async",service);
            Assert.Contains("namesCustomerId",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType=assembly.GetType(ns+".Generated.Services.MtSingleIdOrderService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            if(global)
            {
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic;
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    serviceType.GetField("DerivedOptionsPlan104_15",hidden|System.Reflection.BindingFlags.Static)!.GetValue(null));
                var requiresAccount=accountParent||globalSource&&storage==1&&!fixedOnly;
                Assert.Equal(requiresAccount,plan.RequiresAccount);
                if(requiresAccount)plan=plan.BindAccount(new Metarill.Core.Security.RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5")
                    {StoreId=9007199254740993L});
                var sql=plan.GetType().GetProperty("MySql",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(plan)!;
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    sql.GetType().GetProperty("Constants",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(sql));
                Assert.Contains(83,constants.Values);
                if(requiresAccount)Assert.Contains(9007199254740993L,constants.Values);
                var option=serviceType.GetMethod("ReadDerivedOption104_15",hidden|System.Reflection.BindingFlags.Static)!
                    .Invoke(null,[new Dictionary<string,object?>{["id"]=9007199254740993L}]);
                var jsonOptions=new JsonSerializerOptions(JsonSerializerDefaults.Web);
                jsonOptions.Converters.Add(new Metarill.Core.Contracts.StringLongJsonConverter());
                var json=JsonSerializer.SerializeToElement(option,jsonOptions);
                foreach(var key in new[]{"id","name","value","text"})
                    Assert.Equal("9007199254740993",json.GetProperty(key).GetString());
            }
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
                if (rowProject)
                {
                    var project = Assert.Single(filters, item => item.Field == "yz_project_id");
                    Assert.Equal(54L, Assert.IsType<long>(project.Value));
                }
            }
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),
                ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    /// <summary>同一行项目字典的原单选、导出及普通业务各取自己的范围，显式项目条件继续生效。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    public Task Stored_global_single_with_constants_keeps_candidate_and_business_scopes(int storage,bool idLabel,bool textProject)=>
        VerifyStoredGlobalSelectionScopes(storage,idLabel,textProject,unfiltered:false);

    /// <summary>原单选实际引用仅按当前项目限定的字典页时，保留原候选、名称和工作簿入口。</summary>
    [Theory]
    [InlineData(0,0,false,false)]
    [InlineData(0,1,true,true)]
    [InlineData(1,0,false,true)]
    [InlineData(1,1,true,false)]
    public async Task Global_single_can_use_actually_published_project_dictionary(
        int sourceStorage,int targetStorage,bool textProject,bool literal)
    {
        const string ns="Example.GlobalSingleProjectDictionary";
        const string prefix="mt_single_project_dictionary";
        var catalog=NonRedundantApplicationFixture.Catalog(prefix);
        var owner=catalog.ConfigTables.Single(item=>item.Id=="1");
        owner.IsRongyu=sourceStorage;
        var target=catalog.ConfigTables.Single(item=>item.Id=="2");
        target.IsRongyu=targetStorage;
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var excluded=new HashSet<string>(["16","17","27"],StringComparer.Ordinal);
        catalog.ConfigColumns.RemoveAll(item=>excluded.Contains(item.Id));
        catalog.ConfigPages.RemoveAll(item=>item.Id=="102");
        catalog.ConfigPageColumns.RemoveAll(item=>item.ConfigPageId=="102"||excluded.Contains(item.ConfigColumnId));
        foreach(var page in catalog.ConfigPages.Where(item=>item.ConfigTableId=="1"))page.OrderBy="id desc";
        var published=catalog.ConfigPages.Single(item=>item.Id=="201");
        published.WhereSql="yz_project_id=@current_yz_project_id"+(literal?" AND amount>=1":"");
        published.IsExport=1;
        var detailPage=catalog.ConfigPages.Single(item=>item.Id=="104");
        detailPage.IsExport=1;
        detailPage.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        if(textProject)catalog.ConfigColumns.Single(item=>item.Id=="22").LogicalType="string";
        var foreign=catalog.ConfigColumns.Single(item=>item.Id=="15");
        var idDisplay=sourceStorage==0&&targetStorage==1;
        if(idDisplay)
        {
            catalog.ConfigColumns.RemoveAll(item=>item.Id=="111");
            catalog.ConfigPageColumns.RemoveAll(item=>item.ConfigColumnId=="111");
        }
        foreign.ControlDetail=JsonSerializer.Serialize(new{pageId="201",tableId="2",nameField=idDisplay?"id":"name",whereSql=published.WhereSql});
        var proof=sourceStorage==0?GlobalSingleRelationTarget.Require(catalog,owner,foreign,target):
            GlobalSingleRelationTarget.RequireCached(catalog,owner,foreign,target);
        Assert.Equal(published.Id,proof.Page.Id);
        Assert.True(proof.ProjectSelectionPage);
        Assert.Null(proof.Parent);
        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var selector=new SelectionControlDefinition("201",idDisplay?"id":"name","选择",published.WhereSql,"");
        var dictionary=model.Tables.Single(item=>item.Source.Id=="2");
        var selectedPage=dictionary.Pages.Single(item=>item.Source.Id=="201");
        var query=FixedPageQueryCompilation.For(dictionary,selectedPage);
        Assert.True(FixedPageQueryCompilation.TryProveSelection(model.Catalog,query,selector,out var reason),reason);
        Assert.False(FixedPageQueryCompilation.TryProveSelection(model.Catalog,query,
            selector with{WhereSql=selector.WhereSql+" AND amount = 999"},out _));

        var projectMarker=textProject?"@project_scope_text":"@project_scope_number";
        const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        var candidate=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.CompileGlobalCandidate(model.Catalog,"2","201",["id","name"]);
        var sqlScope=Assert.IsType<string>(candidate.GetType().GetProperty("Scope",hidden)!.GetValue(candidate));
        Assert.Contains("yz_project_id",sqlScope);
        Assert.Contains(projectMarker,sqlScope);
        Assert.DoesNotContain("group_id",sqlScope);
        Assert.Equal(literal,sqlScope.Contains("amount",StringComparison.Ordinal));
        if(sourceStorage==0&&!idDisplay)
        {
            // 业务详情解析名称时也把项目约束放到目标联接中，不能只缩小字典列表。
            var sourceRead=Metarill.Data.Queries.MySqlNonRedundantReadCompiler.Compile(model.Catalog,"1",["customer_name"]);
            var sourceSql=Assert.IsType<string>(sourceRead.GetType().GetProperty("From",hidden)!.GetValue(sourceRead));
            Assert.Contains("yz_project_id",sourceSql);
            Assert.Contains(projectMarker,sourceSql);
            Assert.True(Assert.IsType<bool>(sourceRead.GetType()
                .GetProperty("RequiresPublishedProjectIdentity",hidden)!.GetValue(sourceRead)));
        }

        if(sourceStorage==1)
        {
            // 回填依据真实来源外键，目标项目列和固定条件同样进入副本表达式。
            var cache=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("customer_name",cache.MixedSourceColumns[owner.TableName]);
            Assert.Contains(target.TableName,cache.MixedTargets);
            var plans=cache.GetType().GetProperty("Plans",hidden)!.GetValue(cache)!;
            var entry=Assert.IsAssignableFrom<System.Collections.IEnumerable>(plans).Cast<object>()
                .Single(item=>Equals(item.GetType().GetProperty("Key")!.GetValue(item),"1"));
            var tablePlan=entry.GetType().GetProperty("Value")!.GetValue(entry)!;
            var read=tablePlan.GetType().GetProperty("Read")!.GetValue(tablePlan)!;
            var expressions=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                read.GetType().GetProperty("Expressions",hidden)!.GetValue(read));
            Assert.Contains("yz_project_id",expressions["customer_name"]);
            Assert.Contains(projectMarker,expressions["customer_name"]);
            Assert.Equal(literal,expressions["customer_name"].Contains("amount",StringComparison.Ordinal));
        }

        var root=Path.Combine(Path.GetTempPath(),"metarill-single-project-dictionary",Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(root,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var sourceService=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtSingleProjectDictionaryOrderService.g.cs"));
            foreach(var method in new[]{"ImportRelation103_15Async","ImportRelation105_15Async","Export101Async","Export104Async"})
                Assert.Contains(method,sourceService);
            var detail=await File.ReadAllTextAsync(Path.Combine(generated,"frontend/pages/yzp/mt_single_project_dictionary_order-detail-104.vue"));
            Assert.Contains(idDisplay?"selected-name-field=\"id\"":"selected-name-field=\"name\"",detail);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            foreach(var serviceName in new[]{"MtSingleProjectDictionaryOrderService","MtSingleProjectDictionaryPartyService"})
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    assembly.GetType(ns+".Generated.Services."+serviceName,true)!.TypeHandle);
            if(targetStorage==1)
            {
                using var session=new DatabaseSession(new()
                {
                    Provider="mysql",ExpectedDatabase="offline",
                    ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
                });
                var generatedSql=CreateDictionarySql(assembly,ns,"MtSingleProjectDictionaryParty",session);
                var actor=new RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5");
                var actual=generatedSql("Query201",[new PageRequest(),actor]);
                Assert.Contains("yz_project_id",actual);
                if(literal)Assert.Contains("amount",actual);
            }
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    /// <summary>普通冗余来源的固定字典同时验证整数和字符串项目列、名称及主键显示。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public Task Project_cache_selects_from_managed_fixed_global_dictionary(bool idLabel,bool textProject)=>
        VerifyStoredGlobalSelectionScopes(1,idLabel,textProject,unfiltered:false,projectSource:true);
    /// <summary>无条件字典只在实际发布的详情单选入口被引用时作为全局候选读取。</summary>
    [Theory]
    [InlineData(0,false,false)]
    [InlineData(0,true,true)]
    [InlineData(1,false,true)]
    [InlineData(1,true,false)]
    public Task Stored_global_single_with_unfiltered_page_keeps_candidate_and_business_scopes(int storage,bool idLabel,bool textProject)=>
        VerifyStoredGlobalSelectionScopes(storage,idLabel,textProject,unfiltered:true);

    /// <summary>共用生成、工作簿入口和业务项目隔离检查。</summary>
    private static async Task VerifyStoredGlobalSelectionScopes(int storage,bool idLabel,bool textProject,bool unfiltered,bool projectSource=false)
    {
        const string ns="Example.GlobalFixedDictionary", prefix="mt_fixed_dictionary";
        const string fixedRange="amount >= 1 AND name <> '停用客户'";
        var catalog=NonRedundantApplicationFixture.Catalog(prefix);
        var owner=catalog.ConfigTables.Single(table=>table.Id=="1");
        owner.IsRongyu=storage;
        var dictionary=catalog.ConfigTables.Single(table=>table.Id=="2");
        dictionary.IsRongyu=1;
        dictionary.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var removed=new HashSet<string>(["16","17","27"],StringComparer.Ordinal);
        if(idLabel)removed.Add("111");
        if(storage==1&&!unfiltered&&!projectSource)
        {
            owner.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            removed.Add("19");
        }
        catalog.ConfigColumns.RemoveAll(column=>removed.Contains(column.Id));
        catalog.ConfigPages.RemoveAll(page=>page.Id=="102");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="102"||removed.Contains(field.ConfigColumnId));
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
        {
            page.OrderBy="id desc";
            if(storage==1&&!unfiltered&&!projectSource)page.WhereSql="yz_project_id=@current_yz_project_id";
        }
        if(idLabel)catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail=
            """{"pageId":"201","nameField":"id"}""";
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        targetPage.WhereSql=unfiltered?"":fixedRange;
        targetPage.IsExport=1;
        var sourceDetail=catalog.ConfigPages.Single(page=>page.Id=="104");
        sourceDetail.IsExport=1;
        sourceDetail.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        AddManagedPartyDictionary(catalog,prefix,textProject);
        // 同表显式项目页和无条件页用于核对各自的候选范围。
        foreach(var (id,type,where) in new[]
        {
            ("203",1,fixedRange+" AND yz_project_id=@current_yz_project_id"),
            ("204",1,"")
        })
        {
            catalog.ConfigPages.Add(new()
            {
                Id=id,ConfigTableId="2",YzAdminTypeId="5",YzProjectId=catalog.YzProjectId,WhereSql=where,
                Extra=new(){{"type",JsonSerializer.SerializeToElement(type)}}
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id=id+"24",ConfigPageId=id,ConfigColumnId="24",YzProjectId=catalog.YzProjectId,IsShow=1
            });
        }
        var model=GenerationModel.Create(catalog,ns);
        var target=model.Tables.Single(table=>table.Source.Id=="2");
        Assert.NotNull(FixedPageQueryCompilation.ManagedGlobalProject(target));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if((unfiltered||projectSource)&&storage==1&&!idLabel)
        {
            var cache=Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                binding=>binding.Source.Source.Id=="1"&&binding.Cache.Source.ColumnName=="customer_name");
            Assert.Equal("201",cache.ScopedSinglePageId);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-fixed-global-dictionary");
        var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(root,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var targetService=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtFixedDictionaryPartyService.g.cs"));
            var ownerService=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtFixedDictionaryOrderService.g.cs"));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            using var session=new DatabaseSession(new()
            {
                Provider="mysql",ExpectedDatabase="offline",
                ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
            });
            var sql=CreateDictionarySql(assembly,ns,"MtFixedDictionaryParty",session);
            var actor=new RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5");
            var request=new PageRequest();
            var candidate=sql("Query201",[request,actor]);
            Assert.DoesNotContain("yz_project_id",candidate);
            Assert.Contains("delete_no",candidate);
            Assert.Equal(!unfiltered,candidate.Contains("amount",StringComparison.Ordinal));
            Assert.Equal(!unfiltered,candidate.Contains("name",StringComparison.Ordinal));
            Assert.Contains("yz_project_id",sql("Scope201",[actor,null]));
            Assert.Contains("yz_project_id",sql("Query201",[request,actor,false]));
            foreach(var suffix in new[]{"202","203","204"})
                Assert.Contains("yz_project_id",sql("Query"+suffix,[request,actor]));
            Assert.Contains("Query201(request, actor, candidateRows: false)",targetService);
            Assert.Contains("Scope202(actor, parentId)",targetService);
            Assert.Contains("Query201(request, actor).CountAsync",targetService);

            var definition=new SelectionControlDefinition("201",idLabel?"id":"name","选择",targetPage.WhereSql,"");
            var plan=FixedPageQueryCompilation.For(target,target.Pages.Single(page=>page.Source.Id=="201"));
            Assert.True(FixedPageQueryCompilation.TryProveSelection(model.Catalog,plan,definition,out _));
            var projectOnly=definition with{WhereSql="yz_project_id=@current_yz_project_id"};
            Assert.False(FixedPageQueryCompilation.TryProveSelection(model.Catalog,plan,projectOnly,out _));
            var explicitPage=FixedPageQueryCompilation.For(target,target.Pages.Single(page=>page.Source.Id=="203"));
            Assert.True(FixedPageQueryCompilation.TryProveSelection(model.Catalog,explicitPage,projectOnly with{PageId="203"},out _));

            Assert.Contains("ImportRelation103_15Async",ownerService);
            Assert.Contains("ImportRelation105_15Async",ownerService);
            Assert.Contains("Export101Async",ownerService);
            Assert.Contains("Export104Async",ownerService);
            var ownerType=assembly.GetType(ns+".Generated.Services.MtFixedDictionaryOrderService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(ownerType.TypeHandle);
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
            if(unfiltered&&storage==0&&!idLabel)VerifyUnfilteredCandidatePublication(catalog,ns);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>验证同一候选页的真实引用、项目条件优先及功能表的固定引用。</summary>
    private static void VerifyUnfilteredCandidatePublication(MetadataCatalog catalog,string ns)
    {
        var setting=catalog.ConfigPageColumns.Single(item=>item.ConfigPageId=="101"&&item.ConfigColumnId=="15");
        setting.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"yz_project_id=@current_yz_project_id"}""");
        Assert.False(Published());
        setting.Extra.Remove("controlDetail");
        foreach(var field in catalog.ConfigPageColumns.Where(item=>item.ConfigColumnId=="15"))field.IsEdit=0;
        Assert.False(Published());

        catalog.ConfigTables.Add(new()
        {
            Id="5",TableName="mt_fixed_dictionary_form",YzProjectId=catalog.YzProjectId,IsFunctionTable=1
        });
        catalog.ConfigPages.Add(new(){Id="501",ConfigTableId="5",YzAdminTypeId="5",YzProjectId=catalog.YzProjectId});
        var selector=new ColumnMetadata
        {
            Id="51",ConfigTableId="5",YzProjectId=catalog.YzProjectId,ColumnName="review_id",LogicalType="int64",
            IsVirtualColumn=1,ControlKind="SelectPage",ControlDetail="""{"pageId":"201","nameField":"id"}"""
        };
        catalog.ConfigColumns.Add(selector);
        catalog.ConfigPageColumns.Add(new()
        {
            Id="50151",ConfigPageId="501",ConfigColumnId="51",YzProjectId=catalog.YzProjectId,IsEdit=1
        });
        Assert.True(Published());
        selector.LinkConfigTableId="3";
        Assert.False(Published());

        bool Published()=>GenerationModel.Create(catalog,ns).Tables.Single(item=>item.Source.Id=="2")
            .Pages.Single(item=>item.Source.Id=="201").PublishedUnfilteredSelectionCandidate;
    }

    /// <summary>增加一层已发布字典，让原字典同时承担有真实项目归属的业务读取和写入。</summary>
    private static void AddManagedPartyDictionary(MetadataCatalog catalog,string prefix,bool textProject)
    {
        if(textProject)catalog.ConfigColumns.Single(column=>column.Id=="22").LogicalType="string";
        catalog.ConfigTables.Add(new()
        {
            Id="4",TableName=prefix+"_category",YzProjectId=catalog.YzProjectId,IsRongyu=1,
            Extra=new(){{"scopeKind",JsonSerializer.SerializeToElement("global")}}
        });
        catalog.ConfigColumns.AddRange([
            new(){Id="41",ConfigTableId="4",YzProjectId=catalog.YzProjectId,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="42",ConfigTableId="4",YzProjectId=catalog.YzProjectId,ColumnName="status",LogicalType="int32"},
            new(){Id="28",ConfigTableId="2",YzProjectId=catalog.YzProjectId,ColumnName="category_id",LogicalType="int64",
                LinkConfigTableId="4",ControlKind="SelectPage",ControlDetail="""{"pageId":"401","nameField":"id"}"""}
        ]);
        catalog.ConfigPages.Add(new()
        {
            Id="401",ConfigTableId="4",YzAdminTypeId="5",YzProjectId=catalog.YzProjectId,WhereSql="status=1",
            Extra=new(){{"type",JsonSerializer.SerializeToElement(1)}}
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id="40141",ConfigPageId="401",ConfigColumnId="41",YzProjectId=catalog.YzProjectId,IsShow=1
        });
        catalog.ConfigPages.Add(new()
        {
            Id="202",ConfigTableId="2",YzAdminTypeId="5",YzProjectId=catalog.YzProjectId,IsSave=1,IsExport=1
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id="20224",ConfigPageId="202",ConfigColumnId="24",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1
        });
    }

    /// <summary>调用已生成的查询方法，并使用 FreeSql 构造 SQL，无需数据库连接。</summary>
    internal static Func<string,object?[],string> CreateDictionarySql(Assembly assembly,string ns,string entityName,DatabaseSession session)
    {
        var mapper=Activator.CreateInstance(assembly.GetType(ns+".Generated.Mappers."+entityName+"Mapper",true)!,session);
        var type=assembly.GetType(ns+".Generated.Services."+entityName+"Service",true)!;
        var constructor=type.GetConstructors().Single();
        var service=constructor.Invoke(WithDefaults(constructor,
            [session,mapper,new SnowflakeIdGenerator(),Array.Empty<IRecordMutationObserver>()]));
        return (name,values)=>
        {
            var method=type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!;
            var query=method.Invoke(service,WithDefaults(method,values));
            var toSql=typeof(NonRedundantGeneratedReadTests).GetMethod(nameof(SelectionSql),BindingFlags.Static|BindingFlags.NonPublic)!
                .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]);
            return Assert.IsType<string>(toSql.Invoke(null,[query]));
        };

        static object?[] WithDefaults(MethodBase method,object?[] values)=>
            values.Concat(method.GetParameters().Skip(values.Length).Select(parameter=>parameter.DefaultValue)).ToArray();
    }

    /// <summary>不配置账号条件时，原单选仍统一按已发布的物理父范围处理所有入口。</summary>
    [Theory]
    [InlineData(0,false,false,false)]
    [InlineData(0,true,false,true)]
    [InlineData(1,false,false,true)]
    [InlineData(1,true,false,false)]
    [InlineData(1,true,true,false)]
    [InlineData(1,true,true,true)]
    [InlineData(0,false,false,true,true)]
    [InlineData(0,true,false,true,true)]
    [InlineData(1,false,false,true,true)]
    [InlineData(1,true,true,true,true)]
    [InlineData(0,false,false,false,false,true)]
    [InlineData(0,true,false,true,false,true)]
    [InlineData(1,false,false,true,false,true)]
    [InlineData(1,true,false,false,false,true)]
    [InlineData(1,true,true,false,false,true)]
    [InlineData(1,true,true,true,true,true)]
    [InlineData(0,false,false,false,false,true,true)]
    [InlineData(1,true,true,true,true,true,true)]
    [InlineData(0,false,false,false,false,true,false,true)]
    [InlineData(0,true,false,true,false,true,true,true,true)]
    [InlineData(1,false,true,false,true,true,true,true,true)]
    [InlineData(1,true,true,true,true,true,false,true)]
    public Task Global_single_with_only_physical_parent_keeps_selection_workbook_and_cache(
        int storage,bool globalParent,bool globalSource,bool idLabel,bool excelOverride=false,bool storedTarget=false,bool textParent=false,
        bool managedTarget=false,bool textProject=false)=>
        CheckGlobalSingleWithPhysicalParent(storage,globalParent,globalSource,idLabel,excelOverride,storedTarget,textParent,managedTarget,textProject);

    /// <summary>已发布的原单选同时绑定当前项目和真实物理父键。</summary>
    [Theory]
    [InlineData(0,false,false,false,false,false)]
    [InlineData(0,true,true,true,false,true)]
    [InlineData(1,false,true,false,false,true)]
    [InlineData(1,true,false,false,false,false)]
    [InlineData(0,false,true,true,true,true)]
    [InlineData(1,true,true,false,true,true)]
    public Task Global_single_with_project_and_physical_parent_keeps_selection_workbook_and_cache(
        int storage,bool globalParent,bool globalSource,bool idLabel,bool storedTarget,bool textProject)=>
        CheckGlobalSingleWithPhysicalParent(storage,globalParent,globalSource,idLabel,excelOverride:false,storedTarget,
            textParent:textProject,managedTarget:false,textProject,projectScope:true);

    private static async Task CheckGlobalSingleWithPhysicalParent(
        int storage,bool globalParent,bool globalSource,bool idLabel,bool excelOverride,bool storedTarget,bool textParent,
        bool managedTarget,bool textProject,bool projectScope=false)
    {
        const string ns="Example.GlobalPhysicalParent";
        var catalog=NonRedundantApplicationFixture.Catalog("mt_parent_only");
        var owner=catalog.ConfigTables.Single(table=>table.Id=="1");
        var target=catalog.ConfigTables.Single(table=>table.Id=="2");
        owner.IsRongyu=storage;
        target.IsRongyu=storedTarget?1:0;
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(globalSource)owner.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var removed=new HashSet<string>(["16","17"],StringComparer.Ordinal);
        if(!managedTarget&&!projectScope)removed.Add("22");
        if(idLabel)removed.Add("111");
        if(globalParent)
        {
            var parent=catalog.ConfigTables.Single(table=>table.Id=="3");
            parent.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            removed.Add("32");
            catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="name <> '停用父记录'";
        }
        if(globalParent||projectScope&&globalSource)
        {
            // 源父字段只提供真实物理 ID，避免在单选用例里再引入另一个名称下拉。
            var sourceKey=catalog.ConfigColumns.Single(column=>column.Id=="19");
            sourceKey.ControlKind="";
            sourceKey.ControlDetail="";
        }
        catalog.ConfigColumns.RemoveAll(column=>removed.Contains(column.Id));
        catalog.ConfigPages.RemoveAll(page=>page.Id=="102");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="102"||removed.Contains(field.ConfigColumnId));
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))page.OrderBy="id desc";
        if(idLabel)catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail=
            """{"pageId":"201","tableId":"2","nameField":"id"}""";
        if(excelOverride)
            foreach(var field in catalog.ConfigPageColumns.Where(field=>(field.ConfigPageId is "104" or "105")&&field.ConfigColumnId=="15"))
                field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"amount >= 12.34"}""");
        if(textParent)catalog.ConfigColumns.Single(column=>column.Id=="27").LogicalType="string";
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        if(storedTarget||projectScope)targetPage.WhereSql+=" AND amount >= 1";
        if(projectScope)
        {
            targetPage.WhereSql+=" AND yz_project_id = @current_yz_project_id";
            if(textProject)catalog.ConfigColumns.Single(column=>column.Id=="22").LogicalType="string";
            var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
            var label=idLabel?"id":"name";
            foreign.ControlDetail=JsonSerializer.Serialize(new{pageId="201",tableId="2",nameField=label,whereSql=targetPage.WhereSql});
        }
        targetPage.IsExport=1;
        if(managedTarget)AddManagedPartyDictionary(catalog,"mt_parent_only",textProject);
        var independent=catalog.ConfigPages.Single(page=>page.Id=="104");
        independent.IsExport=1;
        independent.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(table=>table.Source.Id=="1");
        var source=model.Catalog.ConfigColumns.Single(column=>column.Id=="15");
        var binding=GlobalSingleRelationTargetForStorage();
        Assert.Equal("group_id",binding.Parent?.Target.ColumnName);
        Assert.Equal("19",binding.SourceParent?.Column.Id);
        Assert.False(binding.ScopedAccount);

        var root=Path.Combine(Path.GetTempPath(),"metarill-parent-only",Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(root,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtParentOnlyOrderService.g.cs"));
            var targetService=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MtParentOnlyPartyService.g.cs"));
            if(managedTarget)
            {
                var dictionary=model.Tables.Single(item=>item.Source.Id=="2");
                Assert.NotNull(FixedPageQueryCompilation.ManagedGlobalProject(dictionary));
                var candidatePage=dictionary.Pages.Single(item=>item.Source.Id=="201");
                var plan=FixedPageQueryCompilation.For(dictionary,candidatePage);
                var definition=new SelectionControlDefinition("201",idLabel?"id":"name","选择","group_id=@parentId AND amount>=1","");
                Assert.True(FixedPageQueryCompilation.TryProveSelection(model.Catalog,plan,definition,out _));
                Assert.False(FixedPageQueryCompilation.TryProveSelection(model.Catalog,plan,
                    definition with{WhereSql="yz_project_id=@current_yz_project_id"},out _));
                Assert.Contains("Scope201(RuntimeActor actor, string? parentId, bool candidateRows=false)",targetService);
                Assert.Contains("Query201(PageRequest request, RuntimeActor actor, bool candidateRows=true)",targetService);
                Assert.Contains("if (!candidateRows) query = query.Where(entity => entity.YzProjectId",targetService);
                Assert.Contains("Query201(request, actor, candidateRows: false)",targetService);
                Assert.Contains("Scope201(actor, parentId)",targetService);
                Assert.Contains("Scope202(RuntimeActor actor, string? parentId)",targetService);
                Assert.Contains("var query = Scope202(actor, request.ParentId)",targetService);
                Assert.Contains("Scope202(actor, parentId)",targetService);
            }
            if(storedTarget)
            {
                Assert.Contains("ValidateParents201Async(request.ParentId, actor, cancellationToken, locking:false)",targetService);
                Assert.Contains("ValidateParents201Async(parentId, actor, cancellationToken, locking:false)",targetService);
                Assert.Contains("ValidateParents201Async(request.ParentId, actor, cancellationToken, locking: false)",targetService);
                Assert.Contains("PageCondition201",targetService);
                Assert.Contains("if(targetParentId is not null&&targetParentId!=parentId)",service);
                if(textParent)
                {
                    Assert.Contains("ApplyTextParent(query,session,\"group_id\",parentId!)",targetService);
                    Assert.Contains("ApplyTextParent(query,session,\"group_id\",targetParentId!)",service);
                }
            }
            else Assert.Contains("CompileGlobalSingleParentPage",targetService);
            Assert.Contains("ImportRelation103_15Async",service);
            Assert.Contains("ImportRelation105_15Async",service);
            Assert.Contains("Export101Async",service);
            Assert.Contains("Export104Async",service);
            if(globalParent)Assert.Contains("OptionsGlobalParent104_15.BindAccount(actor)",service);
            var detail=await File.ReadAllTextAsync(Path.Combine(generated,"frontend/pages/yzp/mt_parent_only_order-detail-104.vue"));
            Assert.Contains(":parent_id=\"entity.groupId\"",detail);
            Assert.Contains(idLabel?"selected-name-field=\"id\"":"selected-name-field=\"name\"",detail);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var candidateType=assembly.GetType(ns+".Generated.Services.MtParentOnlyPartyService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(candidateType.TypeHandle);
            if(managedTarget)
            {
                // 用已生成的查询表达式生成 MySQL SQL，区分原弹窗、详情及写入、导出和独立业务页。
                using var session=new DatabaseSession(new()
                {
                    Provider="mysql",ExpectedDatabase="offline",
                    ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
                });
                var sql=CreateDictionarySql(assembly,ns,"MtParentOnlyParty",session);
                var actor=new RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5");
                var parentId="9007199254740995";
                var request=new PageRequest{ParentId=parentId};
                var candidateSql=sql("Query201",[request,actor]);
                Assert.DoesNotContain("yz_project_id",candidateSql);
                Assert.Contains("group_id",candidateSql);
                Assert.Contains("amount",candidateSql);
                Assert.Contains("delete_no",candidateSql);
                Assert.Contains("yz_project_id",sql("Scope201",[actor,parentId]));
                Assert.Contains("yz_project_id",sql("Query201",[request,actor,false]));
                Assert.Contains("yz_project_id",sql("Query202",[new PageRequest(),actor]));
            }
            var serviceType=assembly.GetType(ns+".Generated.Services.MtParentOnlyOrderService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic;
            var candidate=storedTarget
                ?Metarill.Data.Queries.NonRedundantReads.CompileGlobalCandidate(catalog,"2","201",idLabel?["id"]:["id","name"])
                :Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    candidateType.GetField("CandidateReadPlan201",hidden|System.Reflection.BindingFlags.Static)!.GetValue(null));
            Assert.False(candidate.RequiresAccount);
            var mysql=candidate.GetType().GetProperty("MySql",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(candidate)!;
            Assert.Equal("group_id",mysql.GetType().GetProperty("RequiredMultiParent",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(mysql));
            if(projectScope)
            {
                var scope=Assert.IsType<string>(mysql.GetType().GetProperty("Scope",hidden|System.Reflection.BindingFlags.Instance)!.GetValue(mysql));
                Assert.Contains("yz_project_id",scope);
                Assert.Contains(textProject?"@project_scope_text":"@project_scope_number",scope);
                Assert.Contains("group_id",scope);
                Assert.Contains("amount",scope);
                var dictionary=model.Tables.Single(item=>item.Source.Id=="2");
                var published=dictionary.Pages.Single(item=>item.Source.Id=="201");
                var query=FixedPageQueryCompilation.For(dictionary,published);
                var selector=new SelectionControlDefinition("201",idLabel?"id":"name","选择",targetPage.WhereSql,"");
                Assert.True(FixedPageQueryCompilation.TryProveSelection(model.Catalog,query,selector,out var reason),reason);
                Assert.False(FixedPageQueryCompilation.TryProveSelection(model.Catalog,query,
                    selector with{WhereSql=selector.WhereSql+" AND amount > 9"},out _));
                var withoutProject=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(published.Source))!;
                withoutProject.WhereSql=withoutProject.WhereSql.Replace(" AND yz_project_id = @current_yz_project_id","",StringComparison.Ordinal);
                var otherPage=published with{Source=withoutProject,Scopes=published.Scopes.Where(item=>item.Source!="project").ToArray()};
                var otherDictionary=dictionary with{Pages=[otherPage]};
                Assert.False(FixedPageQueryCompilation.TryProveSelection(model.Catalog,
                    FixedPageQueryCompilation.For(otherDictionary,otherPage),selector,out _));
                if(storedTarget)
                {
                    using var session=new DatabaseSession(new()
                    {
                        Provider="mysql",ExpectedDatabase="offline",
                        ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
                    });
                    var sql=CreateDictionarySql(assembly,ns,"MtParentOnlyParty",session);
                    var actor=new RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5");
                    var candidateSql=sql("Query201",[new PageRequest{ParentId="9007199254740995"},actor]);
                    Assert.Contains("yz_project_id",candidateSql);
                    Assert.Contains("group_id",candidateSql);
                    Assert.Contains("amount",candidateSql);
                }
            }
            if(excelOverride&&storedTarget)
            {
                foreach(var pageId in new[]{"104","105"})
                {
                    Assert.Contains("SingleImportCondition"+pageId+"_15.BindVariables",service);
                    Assert.Contains("importCondition.ApplyConstants(query,session,\"single_control_import_"+pageId+"_15\")",service);
                }
            }
            if(excelOverride&&!storedTarget)
            {
                const System.Reflection.BindingFlags instance=hidden|System.Reflection.BindingFlags.Instance;
                const System.Reflection.BindingFlags members=hidden|System.Reflection.BindingFlags.Static;
                var options=serviceType.GetField("DerivedOptionsPlan104_15",members)!.GetValue(null)!;
                // 原交互仍只使用目标页；附加金额字段进入两个 Excel 匹配计划。
                var optionsSql=options.GetType().GetProperty("MySql",instance)!.GetValue(options)!;
                var optionFields=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                    optionsSql.GetType().GetProperty("Expressions",instance)!.GetValue(optionsSql));
                Assert.DoesNotContain("amount",optionFields.Keys);
                foreach(var pageId in new[]{"104","105"})
                {
                    var read=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                        serviceType.GetField("SingleImportReadPlan"+pageId+"_15",members)!.GetValue(null));
                    Assert.Equal("int64",read.OutputTypes["id"]);
                    var match=Assert.IsType<Metarill.Data.Queries.MySqlNonRedundantReadPlan>(
                        read.GetType().GetProperty("MySql",instance)!.GetValue(read));
                    Assert.Equal("id",Assert.Single(match.Outputs));
                    var expressions=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                        match.GetType().GetProperty("Expressions",instance)!.GetValue(match));
                    Assert.Contains("amount",expressions.Keys);
                    Assert.Equal("group_id",match.GetType().GetProperty("RequiredMultiParent",instance)!.GetValue(match));
                    var condition=Assert.IsType<Metarill.Data.Queries.FixedConditionSqlPlan>(
                        serviceType.GetField("SingleImportCondition"+pageId+"_15",members)!.GetValue(null));
                    var clause=condition.GetType().GetMethod("RenderConstants",instance)!.Invoke(condition,
                        [new Metarill.Data.MySqlDatabaseProvider(),"excel",new Func<string,string>(name=>expressions[name])])!;
                    var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                        clause.GetType().GetProperty("Parameters")!.GetValue(clause));
                    Assert.Equal(12.34m,Assert.Single(parameters.Values));
                    Assert.Contains("SingleImportCondition"+pageId+"_15.BindVariables",service);
                }
            }
            if(storage==1&&!idLabel)
            {
                var cache=MixedRelationPropagationCompilation.ForModel(model);
                Assert.Contains("customer_name",cache.MixedSourceColumns[table.Source.TableName]);
                Assert.Contains("mt_parent_only_group",cache.MixedTargets);
                if(projectScope)
                {
                    const System.Reflection.BindingFlags members=hidden|System.Reflection.BindingFlags.Instance;
                    var plans=cache.GetType().GetProperty("Plans",members)!.GetValue(cache)!;
                    var entries=Assert.IsAssignableFrom<System.Collections.IEnumerable>(plans).Cast<object>();
                    var sourcePlan=entries.Single(item=>Equals(item.GetType().GetProperty("Key")!.GetValue(item),"1"));
                    var tablePlan=sourcePlan.GetType().GetProperty("Value")!.GetValue(sourcePlan)!;
                    var read=tablePlan.GetType().GetProperty("Read")!.GetValue(tablePlan)!;
                    var expressions=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                        read.GetType().GetProperty("Expressions",members)!.GetValue(read));
                    Assert.Contains("yz_project_id",expressions["customer_name"]);
                    Assert.Contains(textProject?"@project_scope_text":"@project_scope_number",expressions["customer_name"]);
                    Assert.Contains("group_id",expressions["customer_name"]);
                    Assert.Contains("amount",expressions["customer_name"]);
                }
            }
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}

        GlobalSingleRelationPlan GlobalSingleRelationTargetForStorage()=>storage==0
            ?GlobalSingleRelationTarget.Require(catalog,owner,source,target)
            :GlobalSingleRelationTarget.RequireCached(catalog,owner,source,target);
    }

    /// <summary>原普通下拉按真实主键显示，候选、回显、筛选及两种工作簿入口继续走本项目同父范围。</summary>
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 0, true)]
    [InlineData(0, 0, false, true)]
    [InlineData(0, 1, true, true)]
    [InlineData(1, 0, true, true)]
    [InlineData(1, 1, false, true)]
    [InlineData(0, 0, true, true, true)]
    [InlineData(1, 0, false, true, true)]
    public async Task List_id_display_preserves_scoped_options_and_workbook(
        int sourceStorage, int targetStorage, bool pageOverride, bool global=false, bool accountParent=false)
    {
        var catalog = NonRedundantGeneratedFixture.Catalog("mt_list_id");
        NonRedundantGeneratedFixture.EnableSingleSelection(catalog, targetParent: !global || accountParent);
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
        var targetPage=catalog.ConfigPages.Single(page => page.Id == "201");
        var alternatePage=global&&accountParent&&pageOverride&&sourceStorage==0;
        targetPage.Extra.Remove("type");
        if (global)
        {
            catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            // 字典无行项目列，目标页条件仍须进入固定候选。保留名称页的原模式以核对页级显示覆盖。
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "22" or "16" or "17" || !pageOverride&&column.Id=="111");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "22" or "16" or "17" ||
                !pageOverride&&field.ConfigColumnId=="111");
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                page.OrderBy="id desc";
            catalog.ConfigColumns.Add(new(){Id="212",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                ColumnName="status",LogicalType="int32"});
            targetPage.WhereSql="status=83";
            if(accountParent)
            {
                catalog.ConfigColumns.Add(new(){Id="213",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                    ColumnName="store_id",LogicalType="int64"});
                targetPage.WhereSql+=" AND group_id=@parent_id AND store_id=@current_store_id";
            }
        }
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "101");
        sourcePage.IsExport = 1;
        sourcePage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(targetStorage == 0 ? "list" : "edit");
        var field = catalog.ConfigPageColumns.Single(item => item.ConfigPageId == sourcePage.Id && item.ConfigColumnId == source.Id);
        field.Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        if (pageOverride)
        {
            if(alternatePage)
            {
                // 同一字典的一张页按主键显示，另一张仍按名称显示并使用独立发布范围。
                var alternative=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(targetPage))!;
                alternative.Id="202";
                alternative.WhereSql=targetPage.WhereSql.Replace("status=83","status=97",StringComparison.Ordinal);
                catalog.ConfigPages.Add(alternative);
                foreach(var fieldSetting in catalog.ConfigPageColumns.Where(item=>item.ConfigPageId==targetPage.Id).ToArray())
                {
                    var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(fieldSetting))!;
                    clone.Id="202"+clone.ConfigColumnId;
                    clone.ConfigPageId="202";
                    catalog.ConfigPageColumns.Add(clone);
                }
            }
            foreach (var setting in catalog.ConfigPageColumns.Where(item => item.ConfigColumnId == source.Id))
                setting.Extra["controlDetail"] = JsonSerializer.SerializeToElement(alternatePage
                    ? """{"nameField":"id","pageId":"202"}""" : """{"nameField":"id"}""");
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
        Assert.Equal(alternatePage?"202":"201",relation.TargetPage.Source.Id);
        Assert.Equal(!global||accountParent,relation.TargetPage.Scopes.Any(scope=>scope.Source=="parent"));
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
            if (!global || accountParent) Assert.Contains("originalCandidateValues.CustomerId", service);
            Assert.Contains("IntegerLabelContains",service);
            Assert.Contains("new(\"id\", FixedReadComparison.Equal, key)",service);
            if (!global || accountParent) Assert.Contains("filters.Add(new(\"group_id\",FixedReadComparison.Equal", service);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType = assembly.GetType(ns + ".Generated.Services.MtListIdOrderService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            if (global)
            {
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.NonPublic;
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    serviceType.GetField("DerivedOptionsPlan101_15",hidden | System.Reflection.BindingFlags.Static)!.GetValue(null));
                Assert.Equal(accountParent,plan.RequiresAccount);
                if(accountParent)
                    plan=plan.BindAccount(new Metarill.Core.Security.RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5")
                        {StoreId=9007199254740993L});
                var sql=plan.GetType().GetProperty("MySql",hidden | System.Reflection.BindingFlags.Instance)!.GetValue(plan)!;
                var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    sql.GetType().GetProperty("Constants",hidden | System.Reflection.BindingFlags.Instance)!.GetValue(sql));
                Assert.Contains(alternatePage?97:83,parameters.Values);
                if(alternatePage)Assert.DoesNotContain(83,parameters.Values);
                if(accountParent)Assert.Contains(9007199254740993L,parameters.Values);
            }
            if(sourceStorage==0)
            {
                var request=new Metarill.Core.Contracts.PageRequest
                {
                    ParentId="123",
                    Filters=new(){{"customerId",JsonSerializer.SerializeToElement("9007199254740993")}}
                };
                var actor=new Metarill.Core.Security.RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5");
                var filters=Assert.IsAssignableFrom<IReadOnlyList<Metarill.Data.Queries.FixedReadFilter>>(serviceType
                    .GetMethod("ReadFilters101",System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null,[request,actor]));
                var filter=Assert.Single(filters,item=>item.Field=="customer_id");
                Assert.Equal(Metarill.Data.Queries.FixedReadComparison.Equal,filter.Comparison);
                Assert.Equal(9007199254740993L,Assert.IsType<long>(filter.Value));
            }
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
    [InlineData("customImport")][InlineData("global")][InlineData("select")][InlineData("derivedJsonFilter")][InlineData("conflictingTarget")][InlineData("effectiveOverride")]
    public void Unsupported_usages_still_reject_before_files(string usage)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrg_offline");var page=catalog.ConfigPages[0];if(usage=="customImport"){page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("custom");}else if(usage=="global")catalog.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");else if(usage=="select")catalog.ConfigColumns.Single(column=>column.ColumnName=="customer_id").ControlKind="SelectPage";else if(usage=="derivedJsonFilter")
        {
            // B394 支持物理 JSON。派生 JSON 仍须由编译器拒绝。
            var filter=catalog.ConfigColumns.Single(column=>column.Id=="14");
            filter.LogicalType="json";
            filter.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            filter.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer.notes");
        }else if(usage=="conflictingTarget")catalog.ConfigColumns.Single(column=>column.ColumnName=="customer_id").LinkConfigTableId="3";else catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"unpublished_label\"}");
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
