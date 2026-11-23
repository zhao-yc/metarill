using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class DatabaseCheckboxListOptionsGenerationTests
{
    /// <summary>生成完整包，执行原列表如何载入候选、冻结范围和丢弃迟到回执。</summary>
    [Theory]
    [InlineData("parent")]
    [InlineData("root")]
    [InlineData("form")]
    [InlineData("unbound")]
    [InlineData("redundant")]
    [InlineData("stored")]
    [InlineData("stored_global")]
    [InlineData("stored_dropdown")]
    [InlineData("stored_global_dropdown")]
    [InlineData("stored_project")]
    [InlineData("stored_project_dropdown")]
    [InlineData("stored_project_parent")]
    [InlineData("stored_project_parent_dropdown")]
    public async Task Original_checkbox_only_loads_candidates_with_its_published_scope(string scenario)
    {
        var stored = scenario.StartsWith("stored", StringComparison.Ordinal);
        var catalog = RedundantMultiNameCacheTests.Published(DatabaseCheckboxGenerationTests.Catalog(scenario != "redundant" && !stored));
        var checkbox = catalog.ConfigColumns.Single(column => column.Id == "25");
        var dropdown = scenario.EndsWith("_dropdown", StringComparison.Ordinal);
        var projectScoped = scenario.StartsWith("stored_project", StringComparison.Ordinal);
        var parentScoped = scenario.StartsWith("stored_project_parent", StringComparison.Ordinal);
        if (!dropdown) checkbox.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
        if (stored)
        {
            ConfigureStoredCheckbox(catalog, scenario.StartsWith("stored_global", StringComparison.Ordinal), projectScoped, parentScoped);
            catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["queryDefaultValue"] =
                JsonSerializer.SerializeToElement(dropdown ? "9007199254740993" : """["9007199254740993"]""");
        }
        if (scenario == "root")
        {
            foreach (var page in catalog.ConfigPages)
                page.WhereSql = "yz_project_id=@current_yz_project_id";
            checkbox.ControlDetail = "原参数一;原参数二;;name desc";
        }

        if (scenario is "form" or "unbound")
        {
            checkbox.ControlDetail = "原参数一;原参数二;parent_id=@parentId AND category_id=@formParameters.relationSource.categoryId;name desc";
            catalog.ConfigColumns.AddRange([
                new() { Id = "61", ConfigTableId = "2", YzProjectId = catalog.YzProjectId, ColumnName = "category_id", LogicalType = "int64" },
                new() { Id = "62", ConfigTableId = "3", YzProjectId = catalog.YzProjectId, ColumnName = "category_id", LogicalType = "int64" }
            ]);
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "20161", ConfigPageId = "201", ConfigColumnId = "61", YzProjectId = catalog.YzProjectId, IsShow = 1,
                Extra = new()
                {
                    ["isSearch"] = JsonSerializer.SerializeToElement(scenario == "form" ? 1 : 0),
                    ["queryDefaultValue"] = JsonSerializer.SerializeToElement("9007199254740993")
                }
            });
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-checkbox-list-options");
        var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog, output, "Example.CheckboxListOptions",
                NonRedundantApplicationFixture.Root()).FrontendIncluded);
            var controller = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Controllers/RelationSourcePage201Controller.g.cs"));
            Assert.Contains("[HttpPost(\"findNameIdTarget25\")]", controller);
            Assert.Contains("RelationOptionAuthorization.Require(authorization,\"201\"", controller);
            await RunVue(output, scenario);
            if (stored)
            {
                var service = await File.ReadAllTextAsync(Path.Combine(output, "backend/generated/Services/RelationSourceService.g.cs"));
                Assert.Contains(projectScoped ? "CheckboxTargetCondition201_25.BindAccount(actor)" :
                    "CheckboxGlobalCondition201_25.BindAccount(actor)", service);
                Assert.Contains(dropdown ? "DatabaseCheckboxValue.ReadId(filter.Value)" : "DatabaseCheckboxValue.ReadSelected(filter.Value)", service);
                Assert.Contains(dropdown ? "ApplyJsonArrayHasId" : "ApplyJsonArrayHasAnyId", service);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>门店内的分类变更必须参与持久名称回填，而非只在原候选和写入时生效。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(true,true)]
    [InlineData(false,false,true)]
    [InlineData(true,false,true)]
    [InlineData(false,true,true)]
    [InlineData(true,true,true)]
    [InlineData(false,false,false,true)]
    [InlineData(false,true,false,true)]
    [InlineData(false,false,true,true,true)]
    [InlineData(false,true,true,true,true)]
    public async Task Stored_checkbox_form_terms_reach_the_published_maintenance(bool globalSource,bool alternate,
        bool projectBound=false,bool projectScoped=false,bool parentScoped=false)
    {
        var catalog = RedundantMultiNameCacheTests.Published(DatabaseCheckboxGenerationTests.Catalog(false));
        ConfigureStoredCheckbox(catalog, globalSource, projectScoped, parentScoped);
        foreach (var (table,id,name) in new[]{("2","71","category_id"),("3","72","category_id"),("2","73","merchant_id"),("3","74","merchant_id")})
            catalog.ConfigColumns.Add(new() { Id=id,ConfigTableId=table,YzProjectId=catalog.YzProjectId,
                ColumnName=name,LogicalType="int64" });
        var sourcePage = catalog.ConfigPages.Single(page => page.Id=="201");
        sourcePage.IsImport=1;
        sourcePage.WhereSql+=" AND merchant_id=@current_merchant_id";
        catalog.ConfigPageColumns.Add(new(){Id="20171",ConfigPageId="201",ConfigColumnId="71",
            YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
        var relation = catalog.ConfigColumns.Single(column => column.Id == "25");
        relation.ControlDetail = """{"tableId":"3","pageId":"301","nameField":"name","whereSql":"category_id=@formParameters.relationSource.categoryId AND merchant_id=@current_merchant_id AND name<>'停用'"}""";
        if(projectBound)
            relation.ControlDetail=relation.ControlDetail.Replace("category_id=@formParameters","yz_project_id=@current_yz_project_id AND category_id=@formParameters",StringComparison.Ordinal);
        if(alternate)
        {
            var second=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(sourcePage))!;
            second.Id="202";
            catalog.ConfigPages.Add(second);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"&&field.ConfigColumnId!="27").ToArray())
            {
                var copy=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                copy.Id="202"+field.ConfigColumnId;
                copy.ConfigPageId="202";
                if(copy.ConfigColumnId=="25")
                    copy.Extra["controlDetail"]=JsonSerializer.SerializeToElement(
                        """{"nameField":"short_name","whereSql":"merchant_id=@current_merchant_id AND name <> '停用' AND category_id = @formParameters.relationSource.categoryId"}""");
                if(copy.ConfigColumnId=="25"&&projectBound&&!projectScoped)
                    copy.Extra["controlDetail"]=JsonSerializer.SerializeToElement(copy.Extra["controlDetail"].GetString()!
                        .Replace("category_id =","yz_project_id = @current_yz_project_id AND category_id =",StringComparison.Ordinal));
                catalog.ConfigPageColumns.Add(copy);
            }
            catalog.ConfigColumns.Add(new(){Id="75",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
                ColumnName="short_name",LogicalType="string",ColumnLength="100"});
            catalog.ConfigPageColumns.Add(new(){Id="30175",ConfigPageId="301",ConfigColumnId="75",
                YzProjectId=catalog.YzProjectId,IsShow=1});
            catalog.ConfigColumns.Add(new(){Id="76",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                ColumnName="target_short_names",LogicalType="string",ColumnLength="max",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("target.short_name")}});
            catalog.ConfigPageColumns.Add(new(){Id="20276",ConfigPageId="202",ConfigColumnId="76",YzProjectId=catalog.YzProjectId,IsShow=1});
        }

        var directory = GenerationModel.Create(catalog,"Example.StoredCheckboxForm");
        Assert.Empty(GenerationCapabilityAudit.Collect(directory));
        var service = BackendRenderer.Render(directory).Single(file=>file.Path=="Services/RelationSourceService.g.cs").Content;
        if(alternate)
        {
            Assert.Contains("CheckboxCondition202_25.Apply",service);
            Assert.Contains("entity.TargetShortNames=",service);
        }
        Assert.Contains("CheckboxCondition201_25.Apply",service);
        Assert.Contains("entity.CategoryId",service);
        Assert.Contains("ImportCheckbox201_25Async",service);
        if(projectScoped)
        {
            Assert.Contains(".Where(row=>row.YzProjectId==long.Parse(actor.ProjectId,CultureInfo.InvariantCulture))",service);
            Assert.Contains("CheckboxTargetCondition201_25.BindAccount(actor)",service);
        }
        else if(projectBound)
        {
            Assert.Contains("query=query.Where(row=>row.YzProjectId==",service);
            Assert.Contains("""["current_yz_project_id"] = actor.ProjectId""",service);
        }

        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var maintenance=MixedRelationPropagationCompilation.ForModel(directory);
        Assert.Contains("target_names",maintenance.MixedSourceColumns["relation_source"]);
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
            maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
        var plan=plans["2"]!;
        Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
        var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
        var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["target_names"]));
        Assert.Contains("multi_value."+(char)96+"category_id"+(char)96,expression);
        Assert.Contains("b."+(char)96+"category_id"+(char)96,expression);
        Assert.Contains("multi_value."+(char)96+"store_id"+(char)96,expression);
        Assert.Contains("multi_value."+(char)96+"merchant_id"+(char)96,expression);
        Assert.Contains("b."+(char)96+"merchant_id"+(char)96,expression);
        var projectScope="multi_value."+(char)96+"yz_project_id"+(char)96+"=@project_scope_";
        if(projectScoped)Assert.Contains("multi_value."+(char)96+"yz_project_id"+(char)96,expression);
        else Assert.Equal(projectBound,expression.Contains(projectScope,StringComparison.Ordinal));
        if(parentScoped)Assert.Contains("b."+(char)96+"parent_id"+(char)96,expression);
        var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
            read.GetType().GetProperty("Constants",hidden)!.GetValue(read));
        Assert.Contains("停用",parameters.Values);
        // 改分类和停用都需要按存量选择反查，不能只在目标改名时更新。
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",hidden)!.GetValue(read));
        var dependencies=reverse.Cast<object>().Where(item=>Equals("relation_target",item.GetType().GetProperty("TargetTable")!.GetValue(item)))
            .Select(item=>Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item)))
            .SelectMany(outputs=>outputs).ToDictionary(output=>output.Key,output=>output.Value,StringComparer.Ordinal);
        Assert.Equal(alternate?2:1,dependencies.Count);
        Assert.All(dependencies.Values,fields=>
        {
            Assert.Contains("category_id",fields);
            Assert.Contains("merchant_id",fields);
            Assert.Equal(projectBound||projectScoped,fields.Contains("yz_project_id"));
        });

        if(alternate)
        {
            Assert.Contains("target_short_names",maintenance.MixedSourceColumns["relation_source"]);
            var shortName=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["target_short_names"]));
            Assert.Contains("multi_value."+(char)96+"short_name"+(char)96,shortName);
            Assert.Contains("b."+(char)96+"category_id"+(char)96,shortName);
            if(projectScoped)Assert.Contains("multi_value."+(char)96+"yz_project_id"+(char)96,shortName);
            else Assert.Equal(projectBound,shortName.Contains(projectScope,StringComparison.Ordinal));
        }

        if(projectScoped&&parentScoped&&alternate)
        {
            var boundary=Path.Combine(Path.GetTempPath(),"metarill-project-checkbox-form-scope");
            var generatedRoot=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
            try
            {
                new CSharpGenerator().Generate(catalog,Path.Combine(generatedRoot,"generated"),directory.Namespace);
                var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(generatedRoot);
                var generatedService=assembly.GetType(directory.Namespace+".Generated.Services.RelationSourceService",true)!;
                RuntimeHelpers.RunClassConstructor(generatedService.TypeHandle);
                Assert.NotNull(generatedService.GetMethod("Import201Async"));
                var compiled=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(directory.Namespace+".Generated.GeneratedRelationMaintenance",true)!
                        .GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("target_short_names",compiled.MixedSourceColumns["relation_source"]);
            }
            finally{await TestDirectoryCleanup.DeleteAsync(generatedRoot,boundary);}
        }
        if(projectScoped)
        {
            var sourceDependencies=Assert.IsAssignableFrom<IReadOnlyList<string>>(read.GetType().GetProperty("SourceDependencies",hidden)!.GetValue(read));
            Assert.Contains("category_id",sourceDependencies);
            Assert.Contains("merchant_id",sourceDependencies);
            var originalScope=sourcePage.WhereSql;
            sourcePage.WhereSql=originalScope.Replace(" AND merchant_id=@current_merchant_id","",StringComparison.Ordinal);
            var missingOwner=GenerationModel.Create(catalog,"Example.MissingCheckboxFormOwner");
            var missingError=Assert.Throws<InvalidOperationException>(()=>RelationPropagationCompilation.DerivedBindings(missingOwner));
            Assert.Contains("物理账号归属",missingError.Message);
            sourcePage.WhereSql=originalScope;
        }

        var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25");
        settings.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"category_id=1"}""");
        var rejected=GenerationModel.Create(catalog,"Example.ConflictingCheckboxCondition");
        Assert.Contains(GenerationCapabilityAudit.Collect(rejected),gap=>gap.Reason.Contains("沿用表级复选候选条件",StringComparison.Ordinal));
    }

    /// <summary>列表与生成查询使用同一合成门店字典，项目和门店分别存储。</summary>
    internal static void ConfigureStoredCheckbox(Metarill.Core.Metadata.MetadataCatalog catalog, bool globalSource,
        bool projectScoped = false, bool parentScoped = false)
    {
        var checkbox = catalog.ConfigColumns.Single(column => column.Id == "25");
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        if (!projectScoped) target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        if (globalSource)
            catalog.ConfigTables.Single(table => table.Id == "2").Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        // 合成字典候选按账号门店，项目内字典还需验证真实项目和可选父记录。
        foreach (var definition in new[] { (Table: "2", Column: "61", Page: "201"), (Table: "3", Column: "62", Page: "301") })
        {
            catalog.ConfigColumns.Add(new() { Id = definition.Column, ConfigTableId = definition.Table,
                YzProjectId = catalog.YzProjectId, ColumnName = "store_id", LogicalType = "int64" });
            var scope = definition.Table == "2" || projectScoped
                ? "yz_project_id=@current_yz_project_id AND store_id=@current_store_id"
                : "store_id=@current_store_id";
            catalog.ConfigPages.Single(page => page.Id == definition.Page).WhereSql =
                parentScoped ? scope + " AND parent_id=@parentId" : scope;
        }
        checkbox.ControlDetail = """{"tableId":"3","pageId":"301","nameField":"name"}""";
    }

    /// <summary>真实挂载生成页面，捕获公共请求接口接收的参数。</summary>
    private static async Task RunVue(string output, string scenario, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "DatabaseCheckboxListOptionsProbe.mjs"));
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(scenario);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (Exception error) { await SharedSourceBuildGate.StopOwnProcessAsync(process, stdout, stderr, error); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
