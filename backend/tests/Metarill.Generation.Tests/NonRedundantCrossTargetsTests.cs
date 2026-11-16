using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R0跨表目标及混合层位由真实元数据生成，名称不落库、原接口和部分选择保留。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossTargetsTests
{
    /// <summary>四种叶子／父键组合分别覆盖物理、全派生和R0／R1混合名称。</summary>
    [Theory]
    [InlineData("string","string","physical")][InlineData("string","int64","physical")][InlineData("int64","string","physical")][InlineData("int64","int64","physical")]
    [InlineData("string","string","derived")][InlineData("string","int64","derived")][InlineData("int64","string","derived")][InlineData("int64","int64","derived")]
    [InlineData("string","string","mixed")][InlineData("string","int64","mixed")][InlineData("int64","string","mixed")][InlineData("int64","int64","mixed")]
    public async Task Cross_targets_generate_and_compile_all_read_and_write_consumers(string sourceType,string parentType,string scenario)
    {
        var c=Catalog("nr_cross_target",sourceType,parentType,scenario);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-target-generate");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.CrossTarget");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.CrossTarget")));foreach(var target in c.ConfigTables.Where(x=>x.Id!="2"&&x.IsRongyu==0)){var storage=MetadataStoragePolicy.ForTable(c,target);Assert.Equal(scenario=="physical",storage.PhysicalColumns.Any(x=>x.ColumnName=="name"));}}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>账号范围的固定投影进入生成的查询、回执和导出，不能留到读取端才发现缺少账号。</summary>
    [Fact]
    public async Task Account_scoped_cross_target_generates_bound_projection_and_frozen_metadata()
    {
        const string ns="Example.CrossAccount";
        var catalog=Catalog("nr_cross_target","string","int64","derived");
        var target=catalog.ConfigPages.Single(page=>page.Id=="301");
        catalog.ConfigColumns.Add(new(){Id="country_account",ConfigTableId=target.ConfigTableId,ColumnName="owner_id",LogicalType="int64"});
        target.WhereSql+=" AND owner_id=@current_yz_manager_id";
        catalog.ConfigPages.Single(page=>page.Id=="201").IsExport=1;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-account");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var servicePath=Path.Combine(directory,"generated/Services/NrCrossTargetSourceService.g.cs");
            var code=await File.ReadAllTextAsync(servicePath);
            Assert.Contains("PageAsync(ReadPlan201.BindAccount(actor)",code);
            Assert.Contains("KeyRowsAsync(ReadPlan201.BindAccount(actor)",code);
            Assert.Contains("RowsAsync(ExportReadPlan201.BindAccount(actor)",code);
            Assert.Contains("CrossCondition201_25_0.BindAccount(actor)",code);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.NrCrossTargetSourceService",true)!.TypeHandle);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>冗余来源连接按行归属回填，真实冻结目录必须与生成服务一起初始化。</summary>
    [Fact]
    public async Task Account_scoped_cross_cache_generates_maintenance_and_scoped_paths()
    {
        const string ns="Example.CrossAccountCache";
        var catalog=Catalog("nr_cross_target","string","int64","mixed");
        var source=catalog.ConfigTables.Single(table=>table.Id=="2");
        source.IsRongyu=1;
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        catalog.ConfigColumns.Add(new(){Id="source_account",ConfigTableId=source.Id,ColumnName="record_owner_id",LogicalType="int64"});
        sourcePage.WhereSql+=" AND record_owner_id=@current_yz_manager_id";
        var target=catalog.ConfigPages.Single(page=>page.Id=="401");
        catalog.ConfigColumns.Add(new(){Id="area_account",ConfigTableId=target.ConfigTableId,ColumnName="owner_id",LogicalType="int64"});
        target.WhereSql+=" AND owner_id=@current_yz_manager_id";
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-account-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var model=GenerationModel.Create(catalog,ns);
            var compiled=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("area_name",compiled.MixedSourceColumns[source.TableName]);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var published=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
            Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(published.GetProperty("Catalog")!.GetValue(null));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>显式全局中层使用固定派生名称，来源自己的逻辑路径仍按原项目隔离。</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Independent_cross_tree_uses_published_global_derived_level(bool rowProject,bool redundantSource,bool accountScope)
    {
        const string ns = "Example.GlobalDerivedCross";
        var catalog = Catalog("nr_cross_target", "string", "int64", "mixed");
        var target = catalog.ConfigTables.Single(table => table.Id == "4");
        var producer = catalog.ConfigTables.Single(table => table.Id == "6");
        foreach (var table in new[] { target, producer })
            table.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        var targetPage = catalog.ConfigPages.Single(page => page.ConfigTableId == target.Id);
        targetPage.WhereSql = "status=1";
        catalog.ConfigPages.Single(page => page.ConfigTableId == producer.Id).WhereSql = "";
        catalog.ConfigColumns.Add(new()
        {
            Id = "global_company_name", ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId,
            ColumnName = "company_name", LogicalType = "string",
            Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement("company.name") }
        });
        if (!rowProject)
        {
            var project = catalog.ConfigColumns.Single(column => column.ConfigTableId == target.Id && column.ColumnName == "yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == project.Id);
        }
        if(redundantSource)catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        sourcePage.IsImport = sourcePage.IsExport = 1;
        if(accountScope)
        {
            catalog.ConfigColumns.Add(new(){Id="global_dealer",ConfigTableId=target.Id,ColumnName="dealer_id",LogicalType="string"});
            targetPage.WhereSql+=" AND dealer_id=@current_dealer_id";
            if(redundantSource)
            {
                catalog.ConfigColumns.Add(new(){Id="source_dealer",ConfigTableId="2",ColumnName="source_dealer_id",LogicalType="int64"});
                sourcePage.WhereSql+=" AND source_dealer_id=@current_dealer_id";
            }
        }
        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = source.Pages.Single(item => item.Source.Id == "201");
        var plan = Assert.Single(CrossTableReadCompilation.ForService(model, source, page));
        Assert.True(plan.Levels[1].DerivedName);
        if(redundantSource)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("area_name",maintenance.MixedSourceColumns[source.Source.TableName]);
            Assert.Contains(target.TableName,maintenance.MixedTargets);
            Assert.Contains(producer.TableName,maintenance.MixedTargets);
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        Assert.Contains(rowProject ? "CompileGlobalCandidate" : "CompileRelation", NonRedundantCrossTargetsRenderer.Service(model, source, page, plan, 1));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-derived-cross");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if(accountScope&&rowProject&&redundantSource)
            {
                var packaged=Path.Combine(directory,"application");
                var root=NonRedundantApplicationFixture.Root();
                Assert.True(new ApplicationPackager().Create(catalog,packaged,ns,root).FrontendIncluded);
                Assert.True(File.Exists(Path.Combine(packaged,"src/Metarill.Data/Queries/AccountScopedCrossCache.cs")));
            }
            var service = assembly.GetType(ns + ".Generated.Services.NrCrossTargetSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            if(accountScope)
            {
                var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/NrCrossTargetSourceService.g.cs"));
                Assert.Contains("CrossCondition201_25_1.BindAccount(actor)",code);
                if(!redundantSource)Assert.Contains("ReadPlan201.BindAccount(actor)",code);
                if(rowProject)Assert.Contains("CrossTargetPlan201_25_1.BindAccount(actor)",code);
                if(redundantSource)
                {
                    var maintenance=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                    Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(maintenance.GetProperty("Catalog")!.GetValue(null));
                }
            }
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>同一独立R0表可处在不同层位，计划和SQL别名按层区分，不借用来源自身覆盖。</summary>
    [Theory][InlineData("physical")][InlineData("derived")]
    public async Task Repeated_independent_target_roles_generate_without_alias_collision(string scenario)
    {
        var c=Catalog("repeat_cross","string","int64",scenario);RepeatTarget(c,"repeat_cross","int64");var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-target-repeat");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.RepeatedCrossTarget");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.RepeatedCrossTarget")));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>仅将末级目标换成首级表的另一个角色，前缀和原稀疏位保持。</summary>
    internal static void RepeatTarget(MetadataCatalog c,string prefix,string parentType)
    {
        var source=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(source.ControlDetail)!;json["levels"]![2]!["table"]=prefix+"_country";json["levels"]![2]!["parentField"]=prefix+"_area_id";source.ControlDetail=json.ToJsonString();source.LinkConfigTableId="3";c.ConfigColumns.Add(new(){Id="35",YzProjectId=c.YzProjectId,ConfigTableId="3",ColumnName=prefix+"_area_id",LogicalType=parentType,LinkConfigTableId="4"});c.ConfigPageColumns.Add(new(){Id="30135",YzProjectId=c.YzProjectId,ConfigPageId="301",ConfigColumnId="35"});
    }
    /// <summary>三个真实Full包沿原双控件／四类逻辑父页面，实际构建、更新和原Vue探针。</summary>
    [Theory][InlineData("physical")][InlineData("derived")][InlineData("mixed")]
    public async Task Full_mixed_targets_preserve_original_component_and_handwritten_extensions(string scenario)
    {
        var c=NonRedundantCrossFullTests.Catalog("string","int64",false);Apply(c,"nr_cross_full",scenario);Assert.Equal("5",Assert.Single(c.YzAdminTypes).Id);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-target-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var root=NonRedundantApplicationFixture.Root();
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.CrossTargetFull",root).FrontendIncluded);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            var hand=Path.Combine(output,"backend/generated/Services/NrCrossFullSourceService.cs");await File.AppendAllTextAsync(hand,"\n// R0目标完整包手写保护标记。\n");var bytes=await File.ReadAllBytesAsync(hand);var next=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(c))!;next.ConfigColumns.Single(x=>x.Id=="22").Name="更新标签";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,"Example.CrossTargetFull",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));foreach(var action in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,action);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"NonRedundantCrossFullVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));await NonRedundantApplicationFixture.RunAsync(probe);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>固定范围之外仍在创建stage前拒绝，不能用R0名称放开未证明拓扑或Excel。</summary>
    [Theory][InlineData("sourceR1BadPeer")][InlineData("global")][InlineData("view")][InlineData("credential")][InlineData("array")][InlineData("derivedParent")][InlineData("derivedScope")][InlineData("pathOverwrite")]
    public void Invalid_target_uses_stay_gated_before_package(string kind)
    {
        var c=Catalog("nr_cross_target","string","int64","derived");
        if(kind=="sourceR1BadPeer"){c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;c.ConfigColumns.Add(new(){Id="bad_source_cache",ConfigTableId="2",ColumnName="bad_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("missing.name")}});}
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="view")c.ConfigTables.Single(x=>x.Id=="3").IsView=1;
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="62").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="32").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(x=>x.Id=="62").ControlKind="FileLibrary";
        if(kind=="derivedParent"){var p=c.ConfigColumns.Single(x=>x.Id=="45");p.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);p.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="derivedScope")c.ConfigPages.Single(x=>x.Id=="301").WhereSql+=" AND name='未证明条件'";
        if(kind=="pathOverwrite"){var page=c.ConfigPages.Single(x=>x.Id=="201");page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-invalid-cross-target");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidCrossTarget",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>已验独立R0派生目标的普通路径导入实际创建Full包，相邻覆盖导入仍在上例拒绝。</summary>
    [Fact]
    public async Task Independent_derived_target_path_import_creates_full_package()
    {
        var catalog=Catalog("nr_cross_target","string","int64","derived");catalog.ConfigPages.Single(page=>page.Id=="201").IsImport=1;
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-valid-cross-target-import");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.ValidCrossImport")));
            Assert.True(new ApplicationPackager().Create(catalog,output,"Example.ValidCrossImport",NonRedundantApplicationFixture.Root()).FrontendIncluded);
            Assert.True(File.Exists(Path.Combine(output,".metarill-package.json")));
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>同一源模型仅改变目标存储与固定名称路径，不改源稀疏编码或原页面字段。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType,string parentType,string scenario)
    {
        var c=NonRedundantCrossTableGenerationTests.Catalog(prefix,sourceType,parentType);Apply(c,prefix,scenario=="repeated"?"derived":scenario);if(scenario=="repeated")RepeatTarget(c,prefix,parentType);return RedundantMultiNameCacheTests.Published(c);
    }
    /// <summary>完整包模型可复用本变换；已发布后台不能重复登记。</summary>
    internal static void Apply(MetadataCatalog c,string prefix,string scenario)
    {
        foreach(var table in c.ConfigTables.Where(x=>x.Id is "3" or "4" or "5"))if(scenario!="mixed"||table.Id=="4")table.IsRongyu=0;
        if(scenario!="physical")
        {
            c.ConfigTables.Add(new(){Id="6",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"}]);c.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="60162",ConfigPageId="601",ConfigColumnId="62"});
            foreach(var table in c.ConfigTables.Where(x=>x.Id is "3" or "4" or "5"&&x.IsRongyu==0))
            {
                var name=c.ConfigColumns.Single(x=>x.ConfigTableId==table.Id&&x.ColumnName=="name");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");var id=table.Id+"8";c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table.Id,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="6",ControlKind="List",ControlDetail="{\"tableId\":\"6\",\"nameField\":\"name\"}"});c.ConfigPageColumns.Add(new(){Id=table.Id+"01"+id,ConfigPageId=table.Id+"01",ConfigColumnId=id});
            }
        }
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";
    }
}
