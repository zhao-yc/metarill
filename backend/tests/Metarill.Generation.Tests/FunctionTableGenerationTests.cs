using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FunctionTableGenerationTests : IDisposable
{
    private readonly string output = Path.Combine(Path.GetTempPath(), "metarill-function-form", Guid.NewGuid().ToString("N"));

    /// <summary>从真实 Full 包验证原表单、候选权限、功能菜单与无持久化边界。</summary>
    [Fact]
    public async Task Function_form_packages_its_original_detail_and_authorized_options_without_a_mapper()
    {
        var catalog = Catalog();
        new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot());

        var frontend = Path.Combine(output, "frontend");
        var detailPath = Path.Combine(frontend, "pages/yzp/summary_form-detail-102.vue");
        var detail = File.ReadAllText(detailPath);
        Assert.Contains("page_is_function_table:true", detail);
        Assert.Contains("page_is_delete:false", detail);
        Assert.Contains("page_is_update:true", detail);
        Assert.Contains("title", detail);
        Assert.Contains("findNameIdCustomer24", detail);
        Assert.Contains("createMultiSelectFieldMixin", detail);
        Assert.Contains("customer-list-101.vue", detail);
        Assert.Contains(":selectedIds=\"entity.customerIds\"", detail);
        Assert.Contains(":selectedNames=\"entity.customerNames\"", detail);
        Assert.Contains("receiveMultiSelection", detail);
        Assert.Contains("/admin/sys/config_button/execute", detail);
        Assert.Contains("saveEntity", detail);
        Assert.False(File.Exists(Path.Combine(frontend, "pages/yzp/summary_form-list-102.vue")));
        var pages = File.ReadAllText(Path.Combine(frontend, "pages.json"));
        Assert.Contains("summary_form-detail-102", pages);
        Assert.DoesNotContain("summary_form-list-102", pages);
        Assert.DoesNotContain("\"102\"", File.ReadAllText(Path.Combine(frontend,"common/yzp_runtime/page-write-schema.generated.json")));

        var backend = Path.Combine(output, "backend/generated");
        Assert.False(File.Exists(Path.Combine(backend, "Entities/SummaryForm.g.cs")));
        Assert.False(File.Exists(Path.Combine(backend, "Mappers/SummaryFormMapper.g.cs")));
        var controller = File.ReadAllText(Path.Combine(backend, "Controllers/SummaryFormPage102Controller.g.cs"));
        Assert.Contains("findNameIdCustomer24/page", controller);
        Assert.Contains("authorization.RequirePagePermission(\"102\", operation)", controller);
        Assert.Contains("authorization.RequirePagePermission(\"101\", \"list\")", controller);
        Assert.DoesNotContain("[HttpPost(\"page\")]", controller);
        Assert.DoesNotContain("[HttpGet(\"detail/{id}\")]", controller);
        Assert.DoesNotContain("[HttpDelete]", controller);
        var targetController = File.ReadAllText(Path.Combine(backend,"Controllers/CustomerPage101Controller.g.cs"));
        Assert.Contains("[HttpPost(\"selection/selected\")]",targetController);
        Assert.Contains("SelectionSelected101Async(request, Actor(\"list\"), cancellationToken)",targetController);
        var service = File.ReadAllText(Path.Combine(backend, "Services/SummaryFormService.g.cs"));
        Assert.Contains("Select<global::Example.FunctionForm.Generated.Entities.Customer>", service);
        Assert.Contains("entity.YzProjectId == long.Parse(actor.ProjectId", service);
        Assert.DoesNotContain("InsertAsync", service);
        Assert.DoesNotContain("MultiName",service);
        var nav = File.ReadAllText(Path.Combine(output, "backend/Application.Host/DevelopmentNavigation.cs"));
        Assert.Contains("table.IsFunctionTable == 1 ? \"-detail-\" : \"-list-\"", nav);
        Assert.Contains("countEndpoint=table.IsFunctionTable==1?null", nav);
        Assert.Equal("customer", Assert.Single(MySqlSchemaPlanner.Compile(catalog,"form_test").Tables).Name);

        await VerifyMultiSelectionAsync(output);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = output, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "build", "backend/Application.Host/Application.Host.csproj", "--nologo",
            "--disable-build-servers", "--verbosity", "quiet", "-warnaserror", "-p:NuGetAudit=false" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }

    /// <summary>装载完整包内的原表单，验证原确认事件到业务按钮的实际请求参数。</summary>
    private static async Task VerifyMultiSelectionAsync(string directory, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "GeneratedFunctionFormProbe.mjs"));
        start.ArgumentList.Add(directory);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
    }

    /// <summary>功能表不能使用超出目标页已证明合同的附加条件。</summary>
    [Fact]
    public void Function_form_rejects_an_unbound_multiselect_filter()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail =
            """{"pageId":"101","nameField":"name","whereSql":"name = '限定客户'"}""";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationPackager().Create(catalog, output, "Example.FunctionForm", SourceRoot()));
        Assert.Contains("额外控件范围", error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>功能表不能作为实体候选或选择列表，拒绝生成悬空导航。</summary>
    [Fact]
    public void Function_form_cannot_be_used_as_its_own_database_options_target()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column=>column.Id=="24").ControlDetail =
            """{"tableId":"2","table":"summary_form","nameField":"title"}""";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new CSharpGenerator().Generate(catalog,output,"Example.FunctionForm"));
        Assert.Contains("功能表没有可读取的候选记录",error.Message);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>原无主键功能表可把虚拟表单字段交给业务按钮，外表负责提供真实候选。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        catalog.ConfigColumns.Where(column=>column.LogicalType=="id").ToList().ForEach(column=>column.LogicalType="int64");
        catalog.ConfigTables[0].YzProjectId = catalog.YzProjectId;
        catalog.ConfigTables.Add(new() { Id="2", Name="汇总表单", TableName="summary_form", YzProjectId="100", IsFunctionTable=1 });
        catalog.YzAdminTypes.Add(new() { Id="70", Name="业务后台", YzProjectId="100" });
        catalog.ConfigPages[0].YzProjectId="100";
        catalog.ConfigPages[0].YzAdminTypeId="70";
        catalog.ConfigPages[0].Extra["type"]=JsonSerializer.SerializeToElement(2);
        catalog.ConfigPages.Add(new() { Id="102", Name="汇总表单", ConfigTableId="2", YzProjectId="100", YzAdminTypeId="70", IsCopy=1, WhereSql="yz_project_id=@current_yz_project_id" });
        catalog.ConfigColumns.Add(new() { Id="23", ConfigTableId="2", ColumnName="title", Name="标题", DefaultValue="业务汇总", IsVirtualColumn=1 });
        catalog.ConfigColumns.Add(new() { Id="24", ConfigTableId="2", ColumnName="customer_id", Name="客户", LogicalType="int64",
            ControlKind="List", ControlDetail="""{"tableId":"1","table":"customer","nameField":"name"}""" });
        catalog.ConfigColumns.Add(new() { Id="25", ConfigTableId="2", ColumnName="customer_ids", Name="多选客户",
            LogicalType="string", ColumnLength="max", ControlKind="MultiSelectPage", LinkConfigTableId="1", IsVirtualColumn=1,
            ControlDetail="""{"pageId":"101","nameField":"name","buttonText":"选择"}""" });
        catalog.ConfigPageColumns.Add(new() { Id="115", ConfigPageId="102", ConfigColumnId="25", IsEdit=1 });
        catalog.ConfigPageColumns.Add(new() { Id="113", ConfigPageId="102", ConfigColumnId="23", IsEdit=1, IsRequired=1 });
        catalog.ConfigPageColumns.Add(new() { Id="114", ConfigPageId="102", ConfigColumnId="24", IsEdit=1 });
        catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new { id="63", configPageId="102", configTableId="2", yzProjectId="100",
            name="汇总", place="Edit", actionType=1, sql="/admin/custom/report/preview", visibleScheme="Normal", saveOption="Save", isNeedConfirm=1 }));
        catalog.ConfigMenus.Add(new() { Id="82", Name="汇总表单", YzProjectId="100", YzAdminTypeId="70", Kind="GENERATED_PAGE", ConfigPageId="102", IsShowCount=1 });
        return catalog;
    }

    private static string SourceRoot([CallerFilePath] string file = "") =>
        Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ??
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    /// <summary>只回收本测试创建的独占工作目录。</summary>
    public void Dispose() { if (Directory.Exists(output)) Directory.Delete(output,true); }
}
