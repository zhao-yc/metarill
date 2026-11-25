using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
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
    [InlineData(1, "globalDerived", "int32", true)]
    public async Task Self_tree_integer_name_reaches_existing_path_and_export(int sourceStorage,string kind,string type,bool businessKey=false)
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
        if(businessKey)
        {
            NonRedundantSelfTreeTests.AddBusinessTreeKey(catalog,target.Id,"25","int64");
            var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
            targetPage.IsSave=1;
            catalog.ConfigPageColumns.Add(new(){Id="301business",ConfigPageId="301",ConfigColumnId="alternate_tree_key",IsEdit=1});
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
            if(businessKey)
                AssertBusinessKeyMaintenance(assembly,application,source.Id,target.TableName,"number_tree_company",expectedProjectType:"");
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

    /// <summary>固定联接名称与节点业务键共用实际生成计划，原查询、Excel 及叶名筛选均保留原协议。</summary>
    [Theory]
    [InlineData("int64","string",false)]
    [InlineData("string","string",false)]
    [InlineData("int64","int64",true)]
    [InlineData("string","string",true)]
    public async Task Derived_tree_names_keep_the_business_key(string keyType,string parentType,bool redundant)
    {
        const string ns="Example.DerivedTreeBusinessKey";
        var catalog=Catalog("derived_key","string",parentType,automatic:false);
        NonRedundantSelfTreeTests.AddBusinessTreeKey(catalog,"3","25",keyType);
        if(redundant)catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        var target=catalog.ConfigPages.Single(page=>page.Id=="301");
        target.IsSave=1;
        target.IsDelete=1;
        catalog.ConfigPageColumns.Add(new(){Id="301business",ConfigPageId="301",ConfigColumnId="alternate_tree_key",IsEdit=1});
        var source=catalog.ConfigPages.Single(page=>page.Id=="201");
        source.IsExport=1;
        source.IsImport=1;
        var name=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="26");
        name.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-tree-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,ns)));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.DerivedKeySourceService",true)!;
            const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
            var candidate=Assert.IsType<NonRedundantReadPlan>(service.GetField("DerivedCascadePlan201_25",hidden)!.GetValue(null));
            Assert.Equal(keyType,candidate.OutputTypes["node_business_key"]);
            object mysql;
            if(redundant)
            {
                mysql=AssertBusinessKeyMaintenance(assembly,ns,"2","derived_key_tree","derived_key_company");
                var targetSource=await File.ReadAllTextAsync(Path.Combine(generated,"Services/DerivedKeyTreeService.g.cs"));
                Assert.Contains("[\"node_business_key\"] = entity.NodeBusinessKey",targetSource);
                Assert.Contains("previousRelationKeys: previousRelationKeys",targetSource);
            }
            else
            {
                var read=Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan201",hidden)!.GetValue(null));
                mysql=read.GetType().GetProperty("MySql",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(read)!;
            }
            var leaf=Assert.IsType<string>(mysql.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(mysql,["tree_name"]));
            Assert.Contains("`node_business_key`",leaf);
            Assert.Contains("`derived_key_company`",leaf);
            var generatedSource=await File.ReadAllTextAsync(Path.Combine(generated,"Services/DerivedKeySourceService.g.cs"));
            Assert.Contains("ImportCascade201_25Async",generatedSource);
            Assert.Contains("ExcelCascadeTreeId(entity)",generatedSource);
            if(keyType=="string")
                Assert.Contains("FixedReadFilter.ForTextTree(\"node_business_key\",ids)",generatedSource);
            else
                Assert.Contains("new(\"node_business_key\",FixedReadComparison.Equal,id)",generatedSource);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>验证生成包初始化的回填及反查计划，旧键、现键和真实记录游标各自使用独立参数。</summary>
    internal static object AssertBusinessKeyMaintenance(Assembly assembly,string ns,string owner,string tree,string? nameTable,
        string? expectedProjectType=null)
    {
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var declaration=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
        var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(declaration.GetProperty("Catalog")!.GetValue(null));
        Assert.Equal(new[]{"node_business_key"},maintenance.MixedTargetKeys(tree));
        if(nameTable is not null)Assert.Contains(nameTable,maintenance.MixedTargets);
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
        var plan=plans[owner]!;
        Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
        var mysql=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(mysql.GetType().GetProperty("Reverse",hidden)!.GetValue(mysql));
        var entry=Assert.Single(reverse.Cast<object>(),item=>
            item.GetType().GetProperty("TargetTable")!.GetValue(item) is string table&&table==tree&&
            item.GetType().GetProperty("NodeKey",hidden)!.GetValue(item) is not null);
        var predicate=Assert.IsType<string>(entry.GetType().GetProperty("TargetPredicate",hidden)!.GetValue(entry));
        Assert.Contains("BINARY @target_node_current,BINARY @target_node_previous",predicate);
        Assert.Contains(".node_id=@target_id",predicate);
        Assert.Contains("SELECT",predicate);
        Assert.Contains(".`id`,",predicate);
        if(expectedProjectType is not null)
        {
            var nodeKey=entry.GetType().GetProperty("NodeKey",hidden)!.GetValue(entry)!;
            Assert.Equal(expectedProjectType,nodeKey.GetType().GetProperty("ProjectType")!.GetValue(nodeKey));
        }
        return mysql;
    }

    /// <summary>同表书签的显示联接和真实拓扑不互相覆盖，写后跟进业务键而非物理主键。</summary>
    [Theory]
    [InlineData("int64")]
    [InlineData("string")]
    public async Task Same_table_derived_names_keep_business_key_topology(string keyType)
    {
        const string ns="Example.DerivedSameKey";
        var catalog=CascadeTreeSameRowTests.Catalog("derived_same_key","string",keyType);
        catalog.ConfigTables.Single().IsRongyu=0;
        catalog.ConfigColumns.Single(column=>column.Id=="1").Id="10001";
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="1"))field.ConfigColumnId="10001";
        var bookmark=catalog.ConfigColumns.Single(column=>column.Id=="6");
        bookmark.ControlDetail=bookmark.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"joined_label\"",StringComparison.Ordinal);
        NonRedundantSelfTreeTests.AddBusinessTreeKey(catalog,"1","6",keyType);
        var parent=catalog.ConfigColumns.Single(column=>column.Id=="5");
        parent.ControlKind="CascadeTree";
        parent.ControlDetail=bookmark.ControlDetail;
        catalog.ConfigTables.Add(new(){Id="400",TableName="derived_same_key_dictionary"});
        catalog.ConfigPages.Add(new(){Id="401",ConfigTableId="400",WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigColumns.AddRange([
            new(){Id="410",ConfigTableId="400",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="411",ConfigTableId="400",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="412",ConfigTableId="400",ColumnName="name",LogicalType="string"},
            new(){Id="413",ConfigTableId="1",ColumnName="dictionary_id",LogicalType="int64",ControlKind="List",
                LinkConfigTableId="400",ControlDetail="""{"tableId":"400","pageId":"401","nameField":"name"}"""},
            new(){Id="414",ConfigTableId="1",ColumnName="joined_label",LogicalType="string",Extra=new()
                {["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("dictionary.name")}}
        ]);
        catalog.ConfigPageColumns.AddRange([
            new(){Id="401412",ConfigPageId="401",ConfigColumnId="412",IsShow=1},
            new(){Id="202414",ConfigPageId="202",ConfigColumnId="414",IsShow=1}
        ]);
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
        {
            page.IsSave=1;
            page.IsExport=1;
        }
        RedundantMultiNameCacheTests.Published(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-same-key");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,ns);
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,ns)));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.DerivedSameKeyNodeService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var source=await File.ReadAllTextAsync(Path.Combine(output,"Services/DerivedSameKeyNodeService.g.cs"));
            Assert.Contains("CascadeTopology202_5(actor)",source);
            Assert.Contains("CascadeScope202_5(actor,false,false,true)",source);
            Assert.Contains("ValidateStoredCascade201_6Async",source);
            Assert.Contains(keyType=="string"?"FixedReadFilter.ForTextTree(\"node_business_key\",[id])":
                "new(\"node_business_key\",FixedReadComparison.Equal,id)",source);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
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
    [InlineData(false,"ordinary",true)]
    [InlineData(true,"ordinary",true,true)]
    [InlineData(true,"cachedGlobal",true)]
    public async Task Global_derived_self_tree_builds_fixed_name_and_scope(bool rowProject,string sourceScope="ordinary",bool alternateDictionary=false,bool dictionaryOrder=false)
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
        var dictionaryPage=catalog.ConfigPages.Single(page => page.ConfigTableId == upstream.Id);
        dictionaryPage.WhereSql="";
        if(alternateDictionary)
        {
            catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="enabled_flag",LogicalType="int32",YzProjectId=catalog.YzProjectId});
            dictionaryPage.WhereSql="enabled_flag=17";
            catalog.ConfigPages.Add(new(){Id="402",ConfigTableId="4",YzProjectId=catalog.YzProjectId,YzAdminTypeId=dictionaryPage.YzAdminTypeId,WhereSql="enabled_flag=7"});
            catalog.ConfigPageColumns.Add(new(){Id="40242",ConfigPageId="402",ConfigColumnId="42",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
            catalog.ConfigColumns.Single(field=>field.Id=="38").ControlDetail="""{"tableId":"4","pageId":"401","nameField":"name"}""";
            catalog.ConfigPageColumns.Single(column=>column.Id=="30138").Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"402"}""");
            catalog.ConfigPages.Add(new(){Id="302",ConfigTableId=target.Id,YzProjectId=catalog.YzProjectId,YzAdminTypeId=targetPage.YzAdminTypeId,WhereSql="status=1"});
            catalog.ConfigPageColumns.Add(new(){Id="30238",ConfigPageId="302",ConfigColumnId="38",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
            if(dictionaryOrder)
            {
                catalog.ConfigPageColumns.Add(new(){Id="301company_name",ConfigPageId="301",ConfigColumnId="company_name",
                    YzProjectId=catalog.YzProjectId,IsShow=1});
                var field=catalog.ConfigColumns.Single(item=>item.Id=="25");
                var settings=System.Text.Json.Nodes.JsonNode.Parse(field.ControlDetail)!;
                settings["selfTree"]!["orderBy"]="company_name desc, id asc";
                field.ControlDetail=settings.ToJsonString();
            }
        }
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
        var resolved=CascadeTreeReadCompilation.Resolve(model,source,page,source.Columns.Single(column=>column.Source.Id=="25"));
        Assert.True(resolved.Definition is not null,resolved.Reason);
        var plan = Assert.Single(CascadeTreeReadCompilation.ForPage(model, source, page));
        Assert.True(plan.DerivedName);
        if(alternateDictionary)
        {
            var targetModel=model.Tables.Single(table=>table.Source.Id==target.Id);
            _=NonRedundantReadCompilation.ForPage(model,targetModel,targetModel.Pages.Single(candidate=>candidate.Source.Id==targetPage.Id));
        }
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
            if(alternateDictionary)
            {
                const BindingFlags staticPlan=BindingFlags.NonPublic|BindingFlags.Static;
                const BindingFlags sqlPlan=BindingFlags.NonPublic|BindingFlags.Instance;
                var actor=new Metarill.Core.Security.RuntimeActor(5,"自树字典页",false,catalog.YzProjectId,page.Source.YzAdminTypeId);
                AssertDictionaryScope(read);
                var targetService=assembly.GetType(ns+".Generated.Services.NrTreeTreeService",true)!;
                var targetRead=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    targetService.GetField("ReadPlan301",staticPlan)!.GetValue(null));
                AssertDictionaryScope(targetRead);
                var sort=service.GetField("DerivedCascadeSortPlan201_25",staticPlan);
                Assert.Equal(dictionaryOrder,sort is not null);
                if(sort is not null)
                {
                    var sortPlan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(sort.GetValue(null));
                    AssertDictionaryScope(sortPlan);
                    Assert.DoesNotContain("name",sortPlan.OutputTypes.Keys);
                }
                Assert.NotNull(service.GetMethod("CascadeOptions201_25Async"));
                Assert.NotNull(service.GetMethod("CascadePath201_25Async"));
                Assert.NotNull(service.GetMethod("Import201Async"));
                Assert.NotNull(service.GetMethod("Export201Async"));

                void AssertDictionaryScope(Metarill.Data.Queries.NonRedundantReadPlan scoped)
                {
                    var selected=scoped.RequiresAccount?scoped.BindAccount(actor):scoped;
                    var sql=selected.GetType().GetProperty("MySql",sqlPlan)!.GetValue(selected)!;
                    var scope=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                        sql.GetType().GetProperty("Constants",sqlPlan)!.GetValue(sql));
                    Assert.Contains(7,scope.Values);
                    Assert.DoesNotContain(17,scope.Values);
                }
            }
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

    /// <summary>项目内派生叶名的账号和下拉附加条件贯穿实际生成的候选、查询和维护。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    [InlineData(false,false,true)]
    [InlineData(true,false,true)]
    [InlineData(false,true,true)]
    [InlineData(true,true,true)]
    public async Task Project_tree_name_uses_published_dictionary_in_queries_and_backfill(bool redundant,bool logicalScope,
        bool globalStoredDictionary=false)
    {
        const string ns="Example.ProjectTreeName";
        var catalog=Catalog("scope_tree","string","int64",true);
        var dictionary=catalog.ConfigTables.Single(table=>table.Id=="4");
        dictionary.IsRongyu=globalStoredDictionary?1:0;
        if(globalStoredDictionary)
        {
            dictionary.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            var dictionaryProject=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="4"&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(dictionaryProject);
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==dictionaryProject.Id);
            catalog.ConfigPages.Single(item=>item.Id=="401").WhereSql="status>=1";
            catalog.ConfigColumns.Add(new(){Id="48",ConfigTableId="4",ColumnName="status",LogicalType="int32",YzProjectId=catalog.YzProjectId});
        }
        var source=catalog.ConfigTables.Single(table=>table.Id=="2");
        var page=catalog.ConfigPages.Single(item=>item.Id=="201");
        catalog.ConfigPages.Single(item=>item.Id=="401").WhereSql+=" AND store_id=@current_store_id";
        catalog.ConfigColumns.Single(item=>item.Id=="38").ControlDetail=
            """{"tableId":"4","pageId":"401","nameField":"name","whereSql":"segment=7"}""";
        catalog.ConfigPageColumns.Single(item=>item.Id=="30138").Extra["controlDetail"]=
            JsonSerializer.SerializeToElement("""{"whereSql":"segment=17"}""");
        catalog.ConfigColumns.AddRange([
            new(){Id="45",ConfigTableId="4",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId},
            new(){Id="46",ConfigTableId="4",ColumnName="segment",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="26",ConfigTableId="2",ColumnName="tree_name",LogicalType="string",YzProjectId=catalog.YzProjectId}
        ]);
        catalog.ConfigPageColumns.Single(item=>item.Id=="40142").IsShow=1;
        catalog.ConfigPageColumns.Single(item=>item.Id=="30138").IsEdit=1;
        catalog.ConfigPageColumns.Add(new(){Id="20126",ConfigPageId="201",ConfigColumnId="26",IsShow=1,
            YzProjectId=catalog.YzProjectId});
        if(redundant)
        {
            source.IsRongyu=1;
            catalog.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId});
            page.WhereSql+=" AND store_id=@current_store_id";
        }
        else page.OrderBy="tree_name asc";

        if(logicalScope)
        {
            // 树本身的叶名条件与名称字典必须同时入查询。下层树仍保持真实项目和状态范围。
            var nodePage=catalog.ConfigPages.Single(item=>item.Id=="301");
            catalog.ConfigTables.Add(new(){Id="5",TableName="scope_tree_visibility",IsRongyu=1,YzProjectId=catalog.YzProjectId});
            catalog.ConfigColumns.AddRange([
                new(){Id="510",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="511",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="512",ConfigTableId="5",ColumnName="name",LogicalType="string"},
                new(){Id="513",ConfigTableId="5",ColumnName="parent_id",LogicalType="int64",LinkConfigTableId="5"},
                new(){Id="514",ConfigTableId="5",ColumnName="status",LogicalType="int32"},
                new(){Id="350",ConfigTableId="3",ColumnName="visibility_id",LogicalType="int64",
                    LinkConfigTableId="5",ControlKind="CascadeTree",
                    ControlDetail="""{"sourceMode":"selfTree","selfTree":{"table":"scope_tree_visibility","parentField":"parent_id"}}"""}
            ]);
            catalog.ConfigPages.Add(new(){Id="501",ConfigTableId="5",YzProjectId=catalog.YzProjectId,
                YzAdminTypeId=nodePage.YzAdminTypeId,WhereSql="yz_project_id=@current_yz_project_id AND status=1"});
            foreach(var field in new[]{"512","513"})
                catalog.ConfigPageColumns.Add(new(){Id="501"+field,ConfigPageId="501",ConfigColumnId=field,IsShow=1,IsEdit=1});
            catalog.ConfigPageColumns.Add(new(){Id="301350",ConfigPageId="301",ConfigColumnId="350",IsShow=1,IsEdit=1});
            nodePage.WhereSql+=" AND visibility_name <> '停用范围'";
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-project-tree-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ScopeTreeSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags staticPlans=BindingFlags.Static|BindingFlags.NonPublic;
            const BindingFlags sqlPlan=BindingFlags.Instance|BindingFlags.NonPublic;
            var actor=new RuntimeActor(12,"实际门店",false,catalog.YzProjectId,page.YzAdminTypeId){StoreId=9007199254740993L};
            var candidate=Assert.IsType<NonRedundantReadPlan>(service.GetField("DerivedCascadePlan201_25",staticPlans)!.GetValue(null));
            AssertReaderScope(candidate);
            Assert.NotNull(service.GetMethod("CascadePath201_25Async"));
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            if(redundant)
            {
                var metadata=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(metadata.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("scope_tree_company",maintenance.MixedTargets);
                Assert.Contains("tree_name",maintenance.MixedSourceColumns[source.TableName]);
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans",sqlPlan)!.GetValue(maintenance));
                var published=plans["2"]!;
                Assert.Null(published.GetType().GetProperty("Error")!.GetValue(published));
                var read=Assert.IsType<MySqlNonRedundantReadPlan>(published.GetType().GetProperty("Read")!.GetValue(published));
                Assert.False(Assert.IsType<bool>(read.GetType().GetProperty("RequiresAccount",sqlPlan)!.GetValue(read)));
                var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",sqlPlan)!.Invoke(read,["tree_name"]));
                Assert.Contains("=BINARY CAST(b.`store_id` AS CHAR)",expression);
                AssertDictionaryConstants(read);
            }
            else
            {
                var query=Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan201",staticPlans)!.GetValue(null));
                AssertReaderScope(query);
                var code=File.ReadAllText(Path.Combine(directory,"generated/Services/ScopeTreeSourceService.g.cs"));
                Assert.Contains("ReadPlan201.BindAccount(actor)",code);
            }

            void AssertReaderScope(NonRedundantReadPlan plan)
            {
                Assert.True(plan.RequiresAccount);
                Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor with{StoreId=null}));
                var read=Assert.IsType<MySqlNonRedundantReadPlan>(
                    typeof(NonRedundantReadPlan).GetProperty("MySql",sqlPlan)!.GetValue(plan.BindAccount(actor)));
                Assert.Contains(actor.StoreId,AssertDictionaryConstants(read).Values);
            }

            IReadOnlyDictionary<string,object?> AssertDictionaryConstants(MySqlNonRedundantReadPlan read)
            {
                var values=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    read.GetType().GetProperty("Constants",sqlPlan)!.GetValue(read));
                Assert.Contains(17,values.Values);
                Assert.DoesNotContain(7,values.Values);
                if(logicalScope)Assert.Contains("停用范围",values.Values);
                return values;
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>非冗余树的叶子名称使用已声明的单值联接；夹具保留宿主页面和物理父键。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType,string parentType,bool automatic)
    {
        var c=NonRedundantSelfTreeTests.Catalog(prefix,sourceType,parentType,automatic);c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;var name=c.ConfigColumns.Single(column=>column.Id=="32");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="name",LogicalType="string"},new(){Id="43",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="44",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="38",ConfigTableId="3",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="40142",ConfigPageId="401",ConfigColumnId="42"});c.ConfigPageColumns.Add(new(){Id="30138",ConfigPageId="301",ConfigColumnId="38"});return RedundantMultiNameCacheTests.Published(c);
    }
}
