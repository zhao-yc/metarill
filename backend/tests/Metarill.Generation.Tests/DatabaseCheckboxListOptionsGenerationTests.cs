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
    public async Task Original_checkbox_only_loads_candidates_with_its_published_scope(string scenario)
    {
        var stored = scenario.StartsWith("stored", StringComparison.Ordinal);
        var catalog = RedundantMultiNameCacheTests.Published(DatabaseCheckboxGenerationTests.Catalog(scenario != "redundant" && !stored));
        var checkbox = catalog.ConfigColumns.Single(column => column.Id == "25");
        var dropdown = scenario.EndsWith("_dropdown", StringComparison.Ordinal);
        if (!dropdown) checkbox.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
        if (stored)
        {
            ConfigureStoredCheckbox(catalog, scenario.StartsWith("stored_global", StringComparison.Ordinal));
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
                Assert.Contains("CheckboxGlobalCondition201_25.BindAccount(actor)", service);
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
    public void Stored_checkbox_form_terms_reach_the_published_maintenance(bool globalSource,bool alternate)
    {
        var catalog = RedundantMultiNameCacheTests.Published(DatabaseCheckboxGenerationTests.Catalog(false));
        ConfigureStoredCheckbox(catalog, globalSource);
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
        });

        if(alternate)
        {
            Assert.Contains("target_short_names",maintenance.MixedSourceColumns["relation_source"]);
            var shortName=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["target_short_names"]));
            Assert.Contains("multi_value."+(char)96+"short_name"+(char)96,shortName);
            Assert.Contains("b."+(char)96+"category_id"+(char)96,shortName);
        }

        var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25");
        settings.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"category_id=1"}""");
        var rejected=GenerationModel.Create(catalog,"Example.ConflictingCheckboxCondition");
        Assert.Contains(GenerationCapabilityAudit.Collect(rejected),gap=>gap.Reason.Contains("沿用表级复选候选条件",StringComparison.Ordinal));
    }

    /// <summary>列表与生成查询使用同一合成门店字典，项目和门店分别存储。</summary>
    internal static void ConfigureStoredCheckbox(Metarill.Core.Metadata.MetadataCatalog catalog, bool globalSource)
    {
        var checkbox = catalog.ConfigColumns.Single(column => column.Id == "25");
        var target = catalog.ConfigTables.Single(table => table.Id == "3");
        target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        if (globalSource)
            catalog.ConfigTables.Single(table => table.Id == "2").Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        // 全局字典候选按账号门店；记录仍以实际项目和门店存储及维护。
        foreach (var definition in new[] { (Table: "2", Column: "61", Page: "201"), (Table: "3", Column: "62", Page: "301") })
        {
            catalog.ConfigColumns.Add(new() { Id = definition.Column, ConfigTableId = definition.Table,
                YzProjectId = catalog.YzProjectId, ColumnName = "store_id", LogicalType = "int64" });
            catalog.ConfigPages.Single(page => page.Id == definition.Page).WhereSql = definition.Table == "2"
                ? "yz_project_id=@current_yz_project_id AND store_id=@current_store_id"
                : "store_id=@current_store_id";
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
