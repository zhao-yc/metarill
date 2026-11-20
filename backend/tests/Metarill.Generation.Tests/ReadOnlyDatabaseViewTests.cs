using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class ReadOnlyDatabaseViewTests : IDisposable
{
    private readonly string output = Path.Combine(Path.GetTempPath(), "metarill-read-only-view", Guid.NewGuid().ToString("N"));

    /// <summary>真实 Full 工厂与原列表、详情、授权和导出共用编译结果。独立构建验证引用与包闭包。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Read_only_view_packages_original_pages_and_only_generates_allowed_database_reads(bool withProject)
    {
        var catalog = Catalog();
        var page = catalog.ConfigPages.Single();
        page.WhereSql = "";
        if (!withProject)
        {
            var excluded = catalog.ConfigColumns.Where(column => column.ConfigTableId == page.ConfigTableId &&
                column.ColumnName is "yz_project_id" or "delete_no").Select(column => column.Id).ToHashSet(StringComparer.Ordinal);
            catalog.ConfigPageColumns.RemoveAll(field => excluded.Contains(field.ConfigColumnId));
            catalog.ConfigColumns.RemoveAll(column => excluded.Contains(column.Id));
        }
        page.IsSave = 1;
        page.Extra["isUpdate"] = JsonSerializer.SerializeToElement(0);
        var source = SourceRoot();
        new ApplicationPackager().Create(catalog, output, "Example.ReadOnlyView", source);

        var frontend = Path.Combine(output, "frontend");
        var detail = File.ReadAllText(Path.Combine(frontend, "pages/yzp/customer_summary-detail-101.vue"));
        Assert.Contains("page_is_new:false", detail, StringComparison.Ordinal);
        Assert.Contains("page_is_save:false", detail, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(frontend, "pages/yzp/customer_summary-list-101.vue")));
        Assert.DoesNotContain("\"101\"", File.ReadAllText(Path.Combine(frontend, "common/yzp_runtime/page-write-schema.generated.json")));

        var backend = Path.Combine(output, "backend/generated");
        var controller = File.ReadAllText(Path.Combine(backend, "Controllers/CustomerSummaryPage101Controller.g.cs"));
        Assert.Contains("[HttpPost(\"page\")]", controller);
        Assert.Contains("[HttpPost(\"count\")]", controller);
        Assert.Contains("[HttpGet(\"detail/{id}\")]", controller);
        Assert.Contains("[HttpPost(\"initExport\")]", controller);
        Assert.Contains("RequirePagePermission(\"101\", operation)", controller);
        Assert.DoesNotContain("[HttpPost(\"add\")]", controller);
        Assert.DoesNotContain("[HttpPost(\"update\")]", controller);
        Assert.DoesNotContain("[HttpDelete]", controller);
        Assert.DoesNotContain("[HttpPost(\"copy/{id}\")]", controller);
        Assert.DoesNotContain("[HttpPost(\"import\")]", controller);

        var mapper = File.ReadAllText(Path.Combine(backend, "Mappers/CustomerSummaryMapper.g.cs"));
        Assert.Contains("session.Select<", mapper);
        Assert.Equal(withProject, mapper.Contains("entity.DeleteNo == null", StringComparison.Ordinal));
        Assert.DoesNotContain("InsertAsync", mapper);
        Assert.DoesNotContain("UpdateAsync", mapper);
        Assert.DoesNotContain("DeleteAsync", mapper);
        var service = File.ReadAllText(Path.Combine(backend, "Services/CustomerSummaryService.g.cs"));
        Assert.Equal(withProject, service.Contains("entity.YzProjectId == long.Parse(actor.ProjectId", StringComparison.Ordinal));
        Assert.DoesNotContain("Create101Async(", service);
        Assert.DoesNotContain("Update101Async(", service);
        var desired = MySqlSchemaPlanner.Compile(catalog, "isolated_view_test");
        Assert.Equal("customer_log", Assert.Single(desired.Tables).Name);

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

    /// <summary>显式开通写入须在输出前报告，包括旧更新标志及独立导入模式。</summary>
    [Theory]
    [InlineData("create")]
    [InlineData("save")]
    [InlineData("legacy-update")]
    [InlineData("delete")]
    [InlineData("copy")]
    [InlineData("import")]
    [InlineData("import-method")]
    public void Configured_view_write_actions_are_rejected_before_publishing_any_files(string operation)
    {
        var catalog = Catalog();
        var page = catalog.ConfigPages.Single();
        switch (operation)
        {
            case "create": page.IsNew = 1; break;
            case "save": page.IsSave = 1; break;
            case "legacy-update": page.Extra["isUpdate"] = JsonSerializer.SerializeToElement(1); break;
            case "delete": page.IsDelete = 1; break;
            case "copy": page.IsCopy = 1; break;
            case "import": page.IsImport = 1; break;
            case "import-method": page.Extra["importMethod"] = JsonSerializer.SerializeToElement("Append"); break;
        }
        var error = Assert.Throws<InvalidOperationException>(() =>
            new CSharpGenerator().Generate(catalog, output, "Example.ReadOnlyView"));
        Assert.Contains("customer_summary/101", error.Message, StringComparison.Ordinal);
        Assert.Contains("只读数据库视图", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>保留标准列表的认证项目条件，独立实体只用于对照物理同步范围。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        var view = catalog.ConfigTables.Single();
        view.TableName = "customer_summary";
        view.IsView = 1;
        var page = catalog.ConfigPages.Single();
        page.IsNew = page.IsSave = page.IsDelete = 0;
        page.IsExport = 1;
        catalog.ConfigTables.Add(new() { Id = "2", TableName = "customer_log", Name = "客户记录" });
        catalog.ConfigColumns.Add(new() { Id = "21", ConfigTableId = "2", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 });
        return catalog;
    }

    private static string SourceRoot([CallerFilePath] string file = "") =>
        Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ??
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));

    /// <summary>只删除本测试创建的唯一合成应用目录。</summary>
    public void Dispose() { if (Directory.Exists(output)) Directory.Delete(output, true); }
}
