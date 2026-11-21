using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>独立R0自树的节点名称来自固定单值联接，原路径和叶子ID合同保持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedSelfTreeTests
{
    /// <summary>自树整数名称沿原路径与回填保存文本，查找按真实整数和父节点执行。</summary>
    [Theory]
    [InlineData(0, "physical", "int32")]
    [InlineData(0, "derived", "int64")]
    [InlineData(1, "physical", "int64")]
    [InlineData(1, "derived", "int32")]
    [InlineData(1, "globalPhysical", "int32")]
    [InlineData(0, "globalDerived", "int64")]
    [InlineData(1, "globalDerived", "int32")]
    [InlineData(1, "globalCache", "int64")]
    public async Task Self_tree_integer_name_reaches_existing_path_and_export(int sourceStorage,string kind,string type)
    {
        const string application = "Example.IntegerTree";
        var derived = kind is "derived" or "globalDerived";
        var joined = derived || kind == "globalCache";
        var catalog = joined ? Catalog("number_tree","string","int64",true) :
            RedundantMultiNameCacheTests.Published(NonRedundantSelfTreeTests.Catalog("number_tree",automatic:true));
        var source = catalog.ConfigTables.Single(table => table.Id == "2");
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        var name = catalog.ConfigColumns.Single(column => column.Id == "32");
        source.IsRongyu = sourceStorage;
        name.LogicalType = type;
        if (joined)
            catalog.ConfigColumns.Single(column => column.Id == "42").LogicalType = type;
        if (kind.StartsWith("global",StringComparison.Ordinal))
        {
            target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
            catalog.ConfigPages.Single(page => page.Id == "301").WhereSql = "";
            if (joined)
            {
                catalog.ConfigTables.Single(table => table.Id == "4").Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
                catalog.ConfigPages.Single(page => page.Id == "401").WhereSql = "";
            }
        }
        if (kind == "globalDerived")
        {
            // 与已有全局树夹具一致，树页面使用显式的父 List 和对应的辅助投影。
            var targetPage = catalog.ConfigPages.Single(page => page.Id == "301");
            targetPage.WhereSql = "status=1";
            var parent = catalog.ConfigColumns.Single(column => column.Id == "35");
            parent.ControlKind = "List";
            parent.ControlDetail = JsonSerializer.Serialize(new { tableId=target.Id, pageId=targetPage.Id, nameField="name" });
            foreach (var (field,path) in new[] { ("company_name","company.name"), (parent.ColumnName[..^3]+"_name",parent.ColumnName[..^3]+".name") })
                catalog.ConfigColumns.Add(new() { Id=field, ConfigTableId=target.Id, YzProjectId=catalog.YzProjectId,
                    ColumnName=field, LogicalType=type, Extra=new()
                    {
                        ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                        ["columnFullName"]=JsonSerializer.SerializeToElement(path)
                    } });
        }
        if (kind == "globalCache")
        {
            target.IsRongyu = 1;
            catalog.ConfigColumns.RemoveAll(column => column.ConfigTableId == target.Id && column.ColumnName == "yz_project_id");
            name.ColumnName = "company_name";
            var foreign = catalog.ConfigColumns.Single(column => column.Id == "25");
            var control = System.Text.Json.Nodes.JsonNode.Parse(foreign.ControlDetail)!;
            control["selfTree"]!["nameField"] = name.ColumnName;
            foreign.ControlDetail = control.ToJsonString();
        }

        var model = GenerationModel.Create(catalog,application);
        var owner = model.Tables.Single(table => table.Source.Id == "2");
        var pageModel = Assert.Single(owner.Pages);
        var plan = Assert.Single(CascadeTreeReadCompilation.ForPage(model,owner,pageModel));
        Assert.Equal(type,plan.Name.Source.LogicalType);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if (sourceStorage == 1)
        {
            var relations = derived ? RelationPropagationCompilation.DerivedBindings(model) : RelationPropagationCompilation.Bindings(model);
            Assert.Contains(relations,binding => binding.Source.Source.Id == "2" && binding.Cache.Type == "string?");
            var maint = MixedRelationPropagationCompilation.ForModel(model);
            var plans = (System.Collections.IDictionary)typeof(Metarill.Data.Relations.RelationMaintenanceCatalog)
                .GetProperty("Plans",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(maint)!;
            var table = plans["2"]!;
            Assert.Null(table.GetType().GetProperty("Error")!.GetValue(table));
        }

        var boundary = Path.Combine(Path.GetTempPath(),"metarill-self-tree-number");
        var directory = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,application);
            var service = await File.ReadAllTextAsync(Path.Combine(output,"Services",owner.Name+"Service.g.cs"));
            Assert.Contains((type=="int32"?"int":"long")+".TryParse(label",service);
            Assert.Contains("Format(excelPaths",service);
            Assert.Contains("?.ToString(CultureInfo.InvariantCulture)",service);
            Assert.Contains("CascadePath201_25Async",service);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var generated = assembly.GetType(application+".Generated.Services."+owner.Name+"Service",throwOnError:true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(generated.TypeHandle);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>显式和自动叶子名称从真实元数据生成编译，名称不落物理列。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public async Task Derived_tree_names_generate_and_compile(string sourceType,string parentType,bool automatic)
    {
        var c=Catalog("derived_tree",sourceType,parentType,automatic);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-self-tree");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedTree");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.DerivedTree")));Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(table=>table.Id=="3")).PhysicalColumns.Select(column=>column.ColumnName));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>同时开放的独立R0物理名称目标仍生成编译完整三列路径，不能只验证派生分支。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Independent_physical_R0_target_keeps_the_original_reader(bool automatic)
    {
        var c=NonRedundantSelfTreeTests.Catalog("physical_tree",automatic:automatic);c.ConfigTables.Single(x=>x.Id=="3").IsRongyu=0;RedundantMultiNameCacheTests.Published(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-physical-r0-tree");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.PhysicalR0Tree");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.PhysicalR0Tree")));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包仍沿原Vue、原接口和partial更新保护，不能以编译替代页面合同。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_and_original_vue_preserve_the_existing_interaction(bool automatic)
    {await NonRedundantSelfTreeTests.ExerciseFull(automatic,true);await NonRedundantSelfTreeTests.ExerciseOriginalVue(automatic,true);}
    /// <summary>独立排序联接进入候选 SQL 依赖，原路径、名称和 Excel 不返回排序字段。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    public async Task Self_tree_uses_a_published_scalar_join_for_candidate_order(int storage, bool derivedName, bool scopedDictionary = false)
    {
        const string ns = "Example.SortedSelfTree";
        var catalog = Catalog("sort_tree", "string", "int64", true);
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = storage;
        if (!derivedName)
        {
            var name = catalog.ConfigColumns.Single(column => column.Id == "32");
            name.Extra.Remove("isJoinColumn");
            name.Extra.Remove("columnFullName");
        }
        catalog.ConfigColumns.Add(new() { Id = "45", ConfigTableId = "4", ColumnName = "priority", LogicalType = "int32" });
        catalog.ConfigPageColumns.Add(new() { Id = "40145", ConfigPageId = "401", ConfigColumnId = "45", IsShow = 1 });
        if (scopedDictionary)
            catalog.ConfigPages.Single(item => item.Id == "401").WhereSql = "yz_project_id=@current_yz_project_id AND priority>=10";
        catalog.ConfigColumns.Add(new()
        {
            Id = "39", ConfigTableId = "3", ColumnName = "other_label", LogicalType = "int32",
            Extra = new()
            {
                ["isJoinColumn"] = JsonSerializer.SerializeToElement(1),
                ["columnFullName"] = JsonSerializer.SerializeToElement("company.priority")
            }
        });
        catalog.ConfigPageColumns.Add(new() { Id = "30139", ConfigPageId = "301", ConfigColumnId = "39", IsShow = 1 });
        var control = catalog.ConfigColumns.Single(column => column.Id == "25");
        var settings = System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
        settings["selfTree"]!["orderBy"] = "other_label desc, id asc";
        control.ControlDetail = settings.ToJsonString();
        catalog = RedundantMultiNameCacheTests.Published(catalog);
        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = Assert.Single(source.Pages);
        var result = CascadeTreeReadCompilation.Resolve(model, source, page, source.Columns.Single(column => column.Source.Id == "25"));

        Assert.True(result.Definition is not null, result.Reason);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        if (storage == 1)
            Assert.Contains(SelfTreePropagationCompilation.ForTable(model, source), binding => binding.Foreign.Source.Id == "25");

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-self-tree-order");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.SortTreeSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags hiddenStatic = BindingFlags.NonPublic | BindingFlags.Static;
            var order = Assert.IsType<Metarill.Data.Queries.FixedReadOrder[]>(
                service.GetField("DerivedCascadeOrder201_25", hiddenStatic)!.GetValue(null));
            Assert.Equal(new[] { new Metarill.Data.Queries.FixedReadOrder("other_label", true), new("id", false) }, order);
            var labels = Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("DerivedCascadePlan201_25", hiddenStatic)!.GetValue(null));
            var separateOrder = derivedName && scopedDictionary;
            var plan = separateOrder ? Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("DerivedCascadeSortPlan201_25", hiddenStatic)!.GetValue(null)) : labels;
            Assert.DoesNotContain("other_label", plan.OutputTypes.Keys);
            const BindingFlags hiddenInstance = BindingFlags.NonPublic | BindingFlags.Instance;
            var sql = plan.GetType().GetProperty("MySql", hiddenInstance)!.GetValue(plan)!;
            var fields = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                sql.GetType().GetProperty("Expressions", hiddenInstance)!.GetValue(sql));
            Assert.Contains(".`priority`", fields["other_label"]);
            if (scopedDictionary)
            {
                var from = Assert.IsType<string>(sql.GetType().GetProperty("From", hiddenInstance)!.GetValue(sql));
                Assert.Matches(@"priority\x60?\s*>=\s*@", from);
            }
            if (separateOrder)
            {
                var labelSql = labels.GetType().GetProperty("MySql", hiddenInstance)!.GetValue(labels)!;
                var labelFields = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
                    labelSql.GetType().GetProperty("Expressions", hiddenInstance)!.GetValue(labelSql));
                Assert.DoesNotContain("other_label", labelFields.Keys);
                var labelFrom = Assert.IsType<string>(labelSql.GetType().GetProperty("From", hiddenInstance)!.GetValue(labelSql));
                Assert.DoesNotMatch(@"priority\x60?\s*>=\s*@", labelFrom);
                Assert.Equal("string", labels.OutputTypes["name"]);
                Assert.DoesNotContain("name", plan.OutputTypes.Keys);
                var code = await File.ReadAllTextAsync(Path.Combine(output, "Services/SortTreeSourceService.g.cs"));
                Assert.Contains("SelfTreeReader.OrderCandidates", code);
                var actor = new Metarill.Core.Security.RuntimeActor(9, "自树排序", false, catalog.YzProjectId, page.Source.YzAdminTypeId);
                await AccountParentCandidateProbe.VerifySelfTreeOrderAsync(service,
                    assembly.GetType(ns + ".Generated.Mappers.SortTreeSourceMapper", true)!, actor);
            }
            Assert.NotNull(service.GetMethod("CascadeOptions201_25Async"));
            Assert.NotNull(service.GetMethod("CascadePath201_25Async"));
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>未证明的终点、拓扑或范围均拒绝；名称联接不能放开数组、凭据和缓存。</summary>
    [Theory][InlineData("sourceR1BadCache")][InlineData("global")][InlineData("array")][InlineData("credential")][InlineData("wrongPath")][InlineData("derivedParent")][InlineData("derivedCondition")][InlineData("derivedProject")][InlineData("derivedSort")]
    public void Unproven_derived_tree_is_rejected(string scenario)
    {
        var c=Catalog("nr_tree","string","int64",false);var name=c.ConfigColumns.Single(x=>x.Id=="32");
        if(scenario=="sourceR1BadCache"){c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;c.ConfigColumns.Single(x=>x.Id=="26").IsVirtualColumn=1;}
        if(scenario=="global")c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(scenario=="array")c.ConfigColumns.Single(x=>x.Id=="42").ControlKind="FileLibrary";
        if(scenario=="credential"){c.ConfigColumns.Single(x=>x.Id=="42").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(scenario=="wrongPath")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(scenario is "derivedParent" or "derivedCondition" or "derivedProject"){var field=c.ConfigColumns.Single(x=>x.Id==(scenario=="derivedParent"?"35":scenario=="derivedProject"?"33":"37"));field.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);field.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(scenario=="derivedSort"){c.ConfigColumns.Add(new(){Id="39",ConfigTableId="3",ColumnName="other_label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}});var source=c.ConfigColumns.Single(x=>x.Id=="25");source.ControlDetail=source.ControlDetail.Replace("\"rootMode\":\"blank\"","\"rootMode\":\"blank\",\"orderBy\":\"other_label asc\"",StringComparison.Ordinal);Assert.Contains("other_label asc",source.ControlDetail);}
        GenerationModel model;try{model=GenerationModel.Create(c,"Example.RejectDerivedTree");}catch(InvalidOperationException)when(scenario is "derivedCondition" or "derivedProject"){return;}var table=model.Tables.Single(x=>x.Source.Id=="2");var plan=CascadeTreeReadCompilation.Resolve(model,table,table.Pages.Single(),table.Columns.Single(x=>x.Source.Id=="25"));Assert.Null(plan.Definition);Assert.NotEmpty(plan.Reason!);
    }
    /// <summary>显式全局R0树的派生名称按固定候选页读取，带行归属的目标也不会借普通列表扩大或缩小范围。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, "owned")]
    [InlineData(false, "shared")]
    [InlineData(true, "shared")]
    [InlineData(true,"cachedGlobal")]
    public async Task Global_derived_self_tree_builds_fixed_name_and_scope(bool rowProject,string sourceScope="ordinary")
    {
        const string ns = "Example.GlobalDerivedTree";
        var catalog = Catalog("nr_tree", "string", "int64", true);
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        var upstream = catalog.ConfigTables.Single(table => table.Id == "4");
        foreach (var table in new[] { target, upstream })
            table.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        var targetPage = catalog.ConfigPages.Single(page => page.ConfigTableId == target.Id);
        targetPage.WhereSql = "status=1";
        // 树自身的编辑页用原 List 声明父关系，不能用只有表 ID 的普通外键假装全局候选。
        var parent = catalog.ConfigColumns.Single(field => field.Id == "35");
        parent.ControlKind = "List";
        parent.ControlDetail = JsonSerializer.Serialize(new { tableId = target.Id, pageId = targetPage.Id, nameField = "name" });
        AddName("company_name", "company.name");
        AddName("parent_nr_tree_tree_name", "parent_nr_tree_tree.name");
        void AddName(string field, string path) => catalog.ConfigColumns.Add(new()
        {
            Id = field, ConfigTableId = target.Id, YzProjectId = catalog.YzProjectId, ColumnName = field, LogicalType = "string",
            Extra = new() { ["isJoinColumn"] = JsonSerializer.SerializeToElement(1), ["columnFullName"] = JsonSerializer.SerializeToElement(path) }
        });
        catalog.ConfigPages.Single(page => page.ConfigTableId == upstream.Id).WhereSql = "";
        if (!rowProject)
        {
            var field = catalog.ConfigColumns.Single(column => column.ConfigTableId == target.Id && column.ColumnName == "yz_project_id");
            catalog.ConfigColumns.Remove(field);
            catalog.ConfigPageColumns.RemoveAll(pageField => pageField.ConfigColumnId == field.Id);
        }
        if(sourceScope!="ordinary")
        {
            var sourceTable=catalog.ConfigTables.Single(table=>table.Id=="2");
            sourceTable.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            if(sourceScope=="cachedGlobal")sourceTable.IsRongyu=1;
            var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
            sourcePage.WhereSql="";
            sourcePage.Extra["type"]=JsonSerializer.SerializeToElement(sourceScope=="owned"?2:0);
            sourcePage.OrderBy="tree_name asc";
            catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="tree_name",LogicalType="string"});
            catalog.ConfigPageColumns.Add(new(){Id="20126",ConfigPageId="201",ConfigColumnId="26",IsShow=1,IsEdit=0,
                Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
            if(sourceScope is "shared" or "cachedGlobal")catalog.ConfigColumns.RemoveAll(field=>field.ConfigTableId=="2"&&field.ColumnName=="yz_project_id");
        }
        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = Assert.Single(source.Pages);
        var plan = Assert.Single(CascadeTreeReadCompilation.ForPage(model, source, page));
        Assert.True(plan.DerivedName);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        Assert.Contains(rowProject ? "CompileGlobalCandidate" : "CompileRelation", DerivedCascadeNamesRenderer.Reader(model, source, page, plan));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-derived-tree");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.NrTreeSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var read = Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField("DerivedCascadePlan201_25", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null));
            Assert.Equal("string", read.OutputTypes["name"]);
            if(sourceScope=="cachedGlobal")
            {
                var maintenance=MixedRelationPropagationCompilation.ForModel(model);
                Assert.Contains("tree_name",maintenance.MixedSourceColumns["nr_tree_source"]);
                Assert.Contains("nr_tree_company",maintenance.MixedTargets);
            }
            else if(sourceScope!="ordinary")
            {
                var definition=BindingFlags.NonPublic|BindingFlags.Static;
                var readPlan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField("ReadPlan201",definition)!.GetValue(null));
                Assert.Equal("string",readPlan.OutputTypes["tree_name"]);
                Assert.NotNull(service.GetMethod("Import201Async"));
                Assert.NotNull(service.GetMethod("Export201Async"));
                if(sourceScope=="owned")Assert.NotNull(service.GetField("CandidateReadPlan201",definition)!.GetValue(null));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>非冗余树的叶子名称使用已声明的单值联接；夹具保留宿主页面和物理父键。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType,string parentType,bool automatic)
    {
        var c=NonRedundantSelfTreeTests.Catalog(prefix,sourceType,parentType,automatic);c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;var name=c.ConfigColumns.Single(column=>column.Id=="32");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="name",LogicalType="string"},new(){Id="43",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="44",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="38",ConfigTableId="3",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="40142",ConfigPageId="401",ConfigColumnId="42"});c.ConfigPageColumns.Add(new(){Id="30138",ConfigPageId="301",ConfigColumnId="38"});return RedundantMultiNameCacheTests.Published(c);
    }
}
