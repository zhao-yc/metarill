using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Metarill.Testing;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>同一元数据证明级联根/中/末全局缓存名称的候选、读写、维护和完整包没有分叉。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCrossCachedTargetGenerationTests
{
    /// <summary>用生成的真实筛选和排序方法验证独立／共享路径及全局目标账号范围。</summary>
    [Theory]
    [InlineData(false, "list")]
    [InlineData(true, "edit")]
    public async Task R0_cross_level_names_filter_and_order_by_the_published_path(bool shared, string exportMode)
    {
        const string application = "Example.CrossNameQueries";
        var catalog = shared
            ? GlobalCrossCachedTargetFixture.CreateShared(0, "string", "int64", shorter: true)
            : GlobalCrossCachedTargetFixture.Create(0, 2, "int64", "string");
        var configuredPage = catalog.ConfigPages.Single(page => page.Id == "30");
        configuredPage.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        var orderedName = shared ? "area_name" : "place_name";
        var orderedKey = GenerationModel.Camel(orderedName);
        configuredPage.OrderBy = orderedName + " desc";
        catalog.ConfigPages.Single(page => page.Id == "10").WhereSql += " AND store_id=@current_store_id";
        catalog.ConfigColumns.Add(new()
        {
            Id = "166", ConfigTableId = "1", YzProjectId = "100", ColumnName = "store_id",
            Name = "门店", LogicalType = "int64"
        });
        foreach (var (id, field, label, match) in new[]
        {
            ("371", "country_name", "国家名称", "like"),
            ("372", orderedName, "末级名称", "exact")
        })
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = id, ConfigTableId = "3", YzProjectId = "100", ColumnName = field,
                Name = label, LogicalType = "string", ControlKind = "Text"
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "30_" + id, ConfigPageId = "30", ConfigColumnId = id, YzProjectId = "100",
                IsShow = 1, IsEdit = 1, Extra = new()
                {
                    ["isSearch"] = JsonSerializer.SerializeToElement(1),
                    ["isSort"] = JsonSerializer.SerializeToElement(1),
                    ["searchType"] = JsonSerializer.SerializeToElement(match)
                }
            });
        }
        var model = GenerationModel.Create(catalog, application);
        var table = model.Tables.Single(table => table.Source.Id == "3");
        var page = table.Pages.Single(item => item.Source.Id == "30");
        var plan = NonRedundantReadCompilation.ForPage(model, table, page);
        Assert.Contains("country_name", plan.Dependencies);
        Assert.Contains(orderedName, plan.Dependencies);
        Assert.DoesNotContain(page.Writable, column => column.Source.ColumnName == orderedName);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-r0-cross-name-queries");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var generated = Path.Combine(directory, "generated");
        try
        {
            new CSharpGenerator().Generate(catalog, generated, application);
            var list = File.ReadAllText(Path.Combine(generated, "frontend/pages/yzp/cross_consumer-list-30.vue"));
            var source = File.ReadAllText(Path.Combine(generated, "Services/CrossConsumerService.g.cs"));
            Assert.Contains("countryName", list);
            Assert.Contains(orderedKey, list);
            Assert.Contains("yzp-cascade-tree", list);
            Assert.Contains("StatisticsAsync(ReadPlan30.BindAccount(actor)", source);
            Assert.Contains("RowsAsync(ExportReadPlan30.BindAccount(actor)", source);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(application + ".Generated.Services.CrossConsumerService", throwOnError: true)!;
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var lookup = service.GetMethod("ReadFilters30", flags)!;
            var order = service.GetMethod("ReadOrder30", flags)!;
            var actor = new RuntimeActor(57, "测试账号", false, "100", "9") { StoreId = 31 };
            var request = new PageRequest
            {
                Filters = new()
                {
                    ["countryName"] = JsonSerializer.SerializeToElement("国%甲"),
                    [orderedKey] = JsonSerializer.SerializeToElement("末级乙")
                }
            };
            var filters = Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(lookup.Invoke(null, [request, actor]));
            var ancestor = Assert.Single(filters, filter => filter.Field == "country_name");
            Assert.Equal(FixedReadComparison.LikePattern, ancestor.Comparison);
            Assert.Equal("国%甲", ancestor.Value);
            var leaf = Assert.Single(filters, filter => filter.Field == orderedName);
            Assert.Equal(FixedReadComparison.Equal, leaf.Comparison);
            Assert.Equal("末级乙", leaf.Value);
            Assert.Equal(new[] { new FixedReadOrder(orderedName, true), new FixedReadOrder("id", true) },
                Assert.IsAssignableFrom<IReadOnlyList<FixedReadOrder>>(order.Invoke(null, [request])));
            request.OrderBy = new() { Column = "countryName", Order = "ascending" };
            Assert.Equal(new FixedReadOrder("country_name"),
                Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FixedReadOrder>>(order.Invoke(null, [request]))));

            foreach (var field in new[] { "ReadPlan30", "ExportReadPlan30" })
            {
                var runtimePlan = Assert.IsType<NonRedundantReadPlan>(service.GetField(field, flags)!.GetValue(null));
                Assert.NotSame(runtimePlan, runtimePlan.BindAccount(actor));
                Assert.Throws<UnauthorizedAccessException>(() => runtimePlan.BindAccount(actor with { StoreId = null }));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>同一固定条件生成到读取、写入锁、业务按钮、覆盖导入和带附件的完整应用。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fixed_cross_page_conditions_generate_for_reads_mutations_import_and_files(bool shared)
    {
        const string application="Example.LogicalPageCondition";
        var catalog=shared?GlobalCrossCachedTargetFixture.CreateShared(0,"string","int64",shorter:true)
            :GlobalCrossCachedTargetFixture.Create(0,2,"int64","string");
        var source=catalog.ConfigPages.Single(page=>page.Id=="30");
        var name=shared?"warehouse_name":"place_name";
        var country=shared?"'9007199254740993'":"9007199254740993";
        source.WhereSql+=$" AND country_id={country} AND {name} LIKE '甲%乙' AND name <> '隐藏'";
        source.IsDelete=1;
        catalog.ConfigColumns.Single(column=>column.ConfigTableId=="3"&&column.ColumnName=="name").SortNo=-10;
        source.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        source.Extra["exportMethod"]=JsonSerializer.SerializeToElement(shared?"edit":"list");
        var target=catalog.ConfigPages.Single(page=>page.Id=="10");
        target.WhereSql+=" AND store_id=@current_store_id";
        catalog.ConfigColumns.Add(new(){Id="166",ConfigTableId="1",ColumnName="store_id",YzProjectId="100",LogicalType="int64"});
        var model=GenerationModel.Create(catalog,application);
        var table=model.Tables.Single(table=>table.Source.Id=="3");
        var page=Assert.Single(table.Pages);
        Assert.Equal(new[]{"country_id",name},page.Condition!.CrossFields.Order(StringComparer.Ordinal));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-r0-cross-page-condition");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        var generated=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,generated,application);
            var code=File.ReadAllText(Path.Combine(generated,"Services/CrossConsumerService.g.cs"));
            Assert.Contains("CrossConditionPlan30.BindAccount(actor),PageCondition30",code);
            Assert.Contains("ApplyCondition(query,session",code);
            Assert.Contains("locking:true).Where",code);
            Assert.Contains("ReadAgentRecord30Async",code);
            Assert.Contains("ButtonRecordQuery30",code);
            Assert.Contains("Import30Async",code);
            Assert.Contains("Export30Async",code);
            Assert.Contains("RowsAsync(ExportReadPlan30.BindAccount(actor)",code);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(application+".Generated.Services.CrossConsumerService",true)!;
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;
            var plan=Assert.IsType<NonRedundantReadPlan>(service.GetField("CrossConditionPlan30",flags)!.GetValue(null));
            Assert.Equal(new[]{"id"},plan.OutputTypes.Keys);
            var actor=new RuntimeActor(29,"测试用户",false,"100","9");
            Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor));
            Assert.NotSame(plan,plan.BindAccount(actor with{StoreId=38}));
        }
        finally {await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>混合字符串与整数的跨表链沿原候选、路径、Excel 和维护执行。</summary>
    [Theory]
    [InlineData(0,"physical","int32")]
    [InlineData(1,"physical","int64")]
    [InlineData(0,"derived","int64")]
    [InlineData(1,"derived","int32")]
    [InlineData(0,"cached","int32")]
    [InlineData(1,"cached","int64")]
    [InlineData(0,"shared","int64")]
    [InlineData(1,"shared","int32")]
    [InlineData(0,"own","int64")]
    [InlineData(1,"repeated","int32")]
    public async Task Integer_cross_level_drives_original_paths_and_workbook(int mode,string shape,string type)
    {
        const string application = "Example.IntegerCrossLevel";
        const string prefix = "integer_cross";
        var catalog = shape switch
        {
            "cached" => GlobalCrossCachedTargetFixture.Create(mode,1,"string","int64",true),
            "shared" => SharedCrossParentGenerationTests.Catalog(prefix,"string","int64",shorter:true),
            "derived" => NonRedundantCrossTargetsTests.Catalog(prefix,"string","int64","mixed"),
            "own" => NonRedundantCrossSourceTests.Catalog(prefix,1,"string","int64",false),
            "repeated" => RedundantRepeatedCrossSourceTests.Catalog(prefix,5,"string","int64",custom:true),
            _ => RedundantMultiNameCacheTests.Published(CrossTableNameCacheGenerationTests.Catalog(prefix,"string","int64",false))
        };
        var ownerId = shape == "cached" ? "3" : "2";
        var pageId = shape == "cached" ? "30" : "201";
        catalog.ConfigTables.Single(table => table.Id == ownerId).IsRongyu = mode;
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == pageId);
        sourcePage.IsImport = sourcePage.IsExport = 1;

        var names = shape switch
        {
            "cached" => catalog.ConfigColumns.Where(column => column.Id == "114" || column.ConfigTableId == "2" && column.ColumnName == "name"),
            "own" => catalog.ConfigColumns.Where(column => column.ConfigTableId == ownerId && column.ColumnName == "name"),
            "repeated" => catalog.ConfigColumns.Where(column => column.Id is "280" or "282"),
            _ => catalog.ConfigColumns.Where(column => column.ColumnName == "name" &&
                (column.ConfigTableId == "4" || shape == "derived" && column.ConfigTableId == "6"))
        };
        Assert.NotEmpty(names);
        foreach (var name in names) name.LogicalType = type;

        var model = GenerationModel.Create(catalog,application);
        var owner = model.Tables.Single(table => table.Source.Id == ownerId);
        var page = owner.Pages.Single(candidate => candidate.Source.Id == pageId);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var plans = CrossTableWriteCompilation.ForService(model,owner,page);
        Assert.NotEmpty(plans);
        Assert.All(plans,plan => Assert.Contains(plan.Levels,level => level.Read.Name.Source.LogicalType == type && level.Name.Type == "string?"));
        if (mode == 1)
        {
            var maintenance = MixedRelationPropagationCompilation.ForModel(model);
            var tables = (System.Collections.IDictionary)typeof(Metarill.Data.Relations.RelationMaintenanceCatalog)
                .GetProperty("Plans",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(maintenance)!;
            var result = tables[ownerId]!;
            Assert.Null(result.GetType().GetProperty("Error")!.GetValue(result));
        }

        var boundary = Path.Combine(Path.GetTempPath(),"metarill-integer-cross-level");
        var directory = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,application);
            var service = await File.ReadAllTextAsync(Path.Combine(output,"Services",owner.Name+"Service.g.cs"));
            Assert.Contains((type == "int32" ? "int" : "long")+".TryParse(text,NumberStyles.AllowLeadingSign",service);
            Assert.Contains("!=text)return [];",service);
            Assert.Contains("CrossExcelResolve",service);
            Assert.Contains("?.ToString(CultureInfo.InvariantCulture)",service);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var generated = assembly.GetType(application+".Generated.Services."+owner.Name+"Service",throwOnError:true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(generated.TypeHandle);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>八个模式保持物理父键、各级固定页、三个缓存及原文件/维护/导入导出入口。</summary>
    [Theory][InlineData(0,0,"string","string",false)][InlineData(1,0,"string","string",false)][InlineData(0,1,"string","int64",false)][InlineData(1,1,"string","int64",false)]
    [InlineData(0,2,"int64","string",false)][InlineData(1,2,"int64","string",false)][InlineData(0,0,"int64","int64",true)][InlineData(1,0,"int64","int64",true)]
    public void Cached_roles_have_complete_generation_and_maintenance(int mode,int role,string key,string parent,bool allGlobal)
    {
        var c=GlobalCrossCachedTargetFixture.Create(mode,role,key,parent,allGlobal);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalCrossCachedTarget");var gaps=GenerationCapabilityAudit.Collect(model);Assert.Empty(gaps);CrossTableExcelApplicationPreparer.RequireCurrent(c,model.Namespace);var table=model.Tables.Single(x=>x.Source.Id=="3");var page=table.Pages.Single(x=>x.Source.Id=="30");var chain=Assert.Single(CrossTableReadCompilation.ForPage(model,table,page));Assert.Equal(3,chain.Levels.Count);Assert.Equal("114",chain.Levels[role].Name.Source.Id);Assert.Equal("10",chain.Levels[role].Page.Source.Id);
        var bindings=RelationPropagationCompilation.Bindings(model).Where(x=>x.Source.Source.Id=="3").ToArray();Assert.Equal(mode==1?3:0,bindings.Length);if(mode==1)Assert.Contains(bindings,x=>x.Value.Source.Id=="114"&&x.CrossTablePageId=="10");var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.Contains("lookup_name",maintenance.MixedSourceColumns["cached_global"]);Assert.Contains(page.Writable,x=>x.Source.Id=="339");Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>保留原导入导出，内部包继续拒绝更新；公开 R0／R1 均经原三动作升级。</summary>
    [Theory][InlineData(0,0,"string","string",false)][InlineData(1,1,"string","int64",false)][InlineData(0,2,"int64","string",false)][InlineData(1,0,"int64","int64",true)]
    public async Task Unpublished_prepared_application_builds_and_runs_original_cascade_with_cached_names(int mode,int role,string key,string parent,bool allGlobal)
    {
        var c=GlobalCrossCachedTargetFixture.Create(mode,role,key,parent,allGlobal);var repository=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cross-cached-target");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var publication=Path.Combine(directory,"published");new ApplicationPackager().Create(c,publication,"Example.GlobalCrossCachedTarget",repository);
            var prepared=new CrossTableExcelApplicationPreparer().Prepare(c,output,"Example.GlobalCrossCachedTarget",repository);Assert.Empty(prepared.BackendGaps);Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));using(var marker=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,CrossTableExcelApplicationPreparer.Marker)))){Assert.False(marker.RootElement.GetProperty("readyForPublication").GetBoolean());Assert.Equal("web-database-v1",marker.RootElement.GetProperty("authenticationProfile").GetString());}
            var actual=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(output,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal(1,actual.ConfigPages.Single(x=>x.Id=="30").IsImport);Assert.Equal(1,actual.ConfigPages.Single(x=>x.Id=="30").IsExport);
            var hand=Path.Combine(output,"backend/generated/Services/CrossConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalCrossCachedTarget.Generated.Services;\npublic partial class CrossConsumerService { /// <summary>手写规则必须受重复生成保护。</summary>\npublic string PreservedBusinessRule()=>\"原手写规则\"; }\n");var bytes=await File.ReadAllBytesAsync(hand);for(var round=0;round<2;round++){new CSharpGenerator().Generate(c,Path.Combine(output,"backend/generated"),"Example.GlobalCrossCachedTarget");Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));}
            var ordinary=Path.Combine(directory,"ordinary");var baseline=CrossTableExcelApplicationPreparer.RequireCurrent(c,"Example.GlobalCrossCachedTarget").Baseline;new ApplicationPackager().Create(baseline,ordinary,"Example.GlobalCrossCachedTarget",repository);Assert.ThrowsAny<Exception>(()=>new LocalGenerationUpdater().Preview(ordinary,output,["generate-project-config","sync-runtime","generate-all-page"]));
            {
                // 历史内部准备包仍不能交给更新器；公开Full候选走原三动作并保留本机手写扩展。
                var targetHand=Path.Combine(ordinary,"backend/generated/Services/CrossConsumerService.cs");await File.AppendAllTextAsync(targetHand,"\n// 更新公开R0路径Excel时保留旧工程业务。\n");var retained=await File.ReadAllBytesAsync(targetHand);
                var updater=new LocalGenerationUpdater();var pages=updater.Preview(ordinary,publication,["generate-all-page"]);Assert.False(pages.CanApply);Assert.Contains("generate-project-config",pages.RequiredActions);
                var plan=updater.Preview(ordinary,publication,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(retained,await File.ReadAllBytesAsync(targetHand));
                var updated=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(ordinary,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal(1,updated.ConfigPages.Single(page=>page.Id=="30").IsImport);Assert.Equal(1,updated.ConfigPages.Single(page=>page.Id=="30").IsExport);
            }
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalCrossCachedTargetVueProbe.mjs"));probe.ArgumentList.Add(output);await NonRedundantApplicationFixture.RunAsync(probe);
            // 正式Production/Web匿名请求应先在身份边界401，不访问不可达离线库；这不是正式登录或业务验收。
            var hostDirectory=Path.Combine(output,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"uploads");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");
            var hostLogs=Path.Combine(repository,".local/global-cross-cached-target-hosts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(hostLogs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=hostLogs;
            await using(var running=await FreshHost.StartAsync(host,"/health")){foreach(var action in new[]{"page","importDatas30","initExport","cascadeTreeOptions334"})await FreshWebHttp.SendAsync(running.Client,HttpMethod.Post,"/api/admin/auto/cross_consumer/30/"+action,new{},System.Net.HttpStatusCode.Unauthorized);}
            foreach(var file in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(repository,"frontend/metarill_uni_admin",file)),File.ReadAllBytes(Path.Combine(output,"frontend",file)));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>缓存字段不能伪造上游发布、页归属、标量父键或原物理条件，完整包在落盘前拒绝。</summary>
    [Theory][InlineData("producerProject")][InlineData("privateTerminal")][InlineData("pageForeign")][InlineData("pageVariable")][InlineData("cacheCondition")][InlineData("path")][InlineData("parentJoin")][InlineData("fakeOther")]
    public void Invalid_cached_role_rejects_complete_package_before_staging(string kind)
    {
        var c=GlobalCrossCachedTargetFixture.Create(1,1,"string","int64",true);if(kind=="producerProject")c.ConfigColumns.Add(new(){Id="199",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64"});if(kind=="privateTerminal")c.ConfigTables.Single(x=>x.Id=="2").Extra.Clear();if(kind=="pageForeign")c.ConfigPages.Single(x=>x.Id=="10").YzProjectId="101";if(kind=="pageVariable")c.ConfigPages.Single(x=>x.Id=="10").WhereSql="id=@parent_id";if(kind=="cacheCondition")c.ConfigPages.Single(x=>x.Id=="10").WhereSql="lookup_name='甲'";if(kind=="path")c.ConfigColumns.Single(x=>x.Id=="114").Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");if(kind=="parentJoin")c.ConfigColumns.Single(x=>x.Id=="p1").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="fakeOther")c.ConfigColumns.Single(x=>x.Id=="405").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        var output=Path.Combine(Path.GetTempPath(),"metarill-global-cross-cached-invalid",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidGlobalCrossCachedTarget",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
}
