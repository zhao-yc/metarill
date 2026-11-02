using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>先验证正式元数据／生成／完整包，准备通过不代表已取得新库租约或完成持久化。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DoublePersistenceNativePreparationTests
{
    /// <summary>普通／非冗余页共享真实关联和父范围，四个物理double默认值不合并相邻位型。</summary>
    [Fact]
    public void Catalog_compiles_exact_defaults_real_derived_double_and_original_statistics()
    {
        var catalog = DoublePersistenceNativePreparation.Catalog(); var before = JsonSerializer.Serialize(catalog);
        MetadataValidation.Validate(catalog);
        Assert.Equal(3, catalog.ConfigTables.Count); Assert.Equal(3, catalog.ConfigPages.Count); Assert.Equal(3, catalog.ConfigMenus.Count);
        var model = GenerationModel.Create(catalog, DoublePersistenceNativePreparation.RootNamespace);
        var source = model.Tables.Single(table => table.Source.Id == "1"); var ordinary = model.Tables.Single(table => table.Source.Id == "2");
        Assert.Equal(0, source.Source.IsRongyu); Assert.NotEqual(0, ordinary.Source.IsRongyu);
        Assert.Equal("group_id=@parent_id", source.Pages.Single().Source.WhereSql);
        var read = NonRedundantReadCompilation.ForPage(model, source, source.Pages.Single());
        Assert.Contains(read.Outputs, field => field.Storage == "customer_amount");
        Assert.Equal("double?", source.Columns.Single(column => column.Source.ColumnName == "customer_amount").Type);
        var schema = MySqlSchemaPlanner.Compile(catalog, "offline");
        var sourceTable = schema.Tables.Single(table => table.Name.EndsWith("_order", StringComparison.Ordinal));
        var ordinaryTable = schema.Tables.Single(table => table.Name.EndsWith("_party", StringComparison.Ordinal));
        Assert.DoesNotContain(sourceTable.Columns, column => column.Name is "customer_amount" or "customer_notes");
        foreach (var (table, name, bits) in new[] { (sourceTable, "measurement", DoublePersistenceNativePreparation.UpperBits), (sourceTable, "measurement_previous", DoublePersistenceNativePreparation.LowerBits),
            (ordinaryTable, "amount", DoublePersistenceNativePreparation.UpperBits), (ordinaryTable, "previous_amount", DoublePersistenceNativePreparation.LowerBits) })
        {
            var column = table.Columns.Single(column => column.Name == name); Assert.Equal("double", column.StoreType);
            Assert.Equal(bits, BitConverter.DoubleToUInt64Bits(double.Parse(column.DefaultSql!, CultureInfo.InvariantCulture)));
        }
        foreach (var table in new[] { source, ordinary })
        {
            var sums = OriginalListStatisticsCompilation.ForPage(model, table, table.Pages.Single())!;
            Assert.Contains(sums, item => item.Kind == 1 && item.Column.Type == "double?");
            Assert.Equal(1, table.Pages.Single().Source.IsImport); Assert.Equal(1, table.Pages.Single().Source.IsExport);
        }
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>正式模型自身必须能按原反查器无损往返，避免离线只编译正向DDL却遗漏整表类型范围。</summary>
    [Fact]
    public void Entire_numeric_fixture_schema_roundtrips_through_original_import_rules()
    {
        var catalog = DoublePersistenceNativePreparation.Catalog();
        var planned = MySqlSchemaPlanner.Compile(catalog, "offline");
        var source = new SchemaImportInspection(new("mysql", planned, "numeric-fixture"), []);
        var draft = SchemaImportDraftCompiler.Compile(source, catalog.YzProjectId);
        Assert.True(draft.ColumnsAndIndexesRepresentable, string.Join(";", draft.Issues));
        var rebuilt = MySqlSchemaPlanner.Compile(draft.LogicalMetadata, "offline");
        Assert.Empty(MySqlSchemaPlanner.Compare(planned, rebuilt));
        Assert.Empty(MySqlSchemaPlanner.Compare(rebuilt, planned));
    }

    /// <summary>超出C#全部可表示范围的decimal仍原样拒绝，不能通过缩小反查定义使夹具通过。</summary>
    [Fact]
    public void Oversized_decimal_fixture_keeps_original_import_rejection()
    {
        var catalog = DoublePersistenceNativePreparation.Catalog();
        catalog.ConfigColumns.Single(column => column.ColumnName == "exact_amount").Precision = 30;
        var planned = MySqlSchemaPlanner.Compile(catalog, "offline");
        var before = JsonSerializer.Serialize(planned);
        var draft = SchemaImportDraftCompiler.Compile(new(new("mysql", planned, "oversized-numeric-fixture"), []), catalog.YzProjectId);
        Assert.False(draft.ColumnsAndIndexesRepresentable);
        Assert.Contains(draft.Issues, issue => issue.Table == "mt_double_native_party" && issue.Member == "exact_amount" && issue.Code == "COLUMN_UNREPRESENTABLE");
        Assert.Equal(before, JsonSerializer.Serialize(planned));
    }

    /// <summary>实际Full包独立严格构建，并运行原生产宿主的六个匿名拒绝入口；无数据库配置。</summary>
    [Fact]
    public async Task Public_full_package_builds_and_preserves_generated_hooks_original_assets_and_runtime()
    {
        var source = NonRedundantApplicationFixture.Root(); var boundary = Path.Combine(Path.GetTempPath(), "metarill-double-persistence-package");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var package = Path.Combine(directory, "application");
        try
        {
            DoublePersistenceNativePreparation.Create(source, package);
            var build = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", package);
            foreach (var argument in new[] { "build", "backend/Application.Host/Application.Host.csproj", "-c", "Release", "-warnaserror", "--disable-build-servers", "--verbosity", "quiet" }) build.ArgumentList.Add(argument);
            var logs = Path.Combine(source, DoublePersistenceNativePreparation.Root, "offline-builds", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(logs); build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = logs;
            await NonRedundantApplicationFixture.RunAsync(build);
            var proof = DoublePersistenceNativePreparation.Capture(package); var catalog = DoublePersistenceNativePreparation.Verify(proof);
            foreach (var relative in new[] { "Metarill.Core/Contracts/ExactNumericValue.cs", "Metarill.Data/Infrastructure/SqlRowStore.cs", "Metarill.Data/MySqlFloatingCommand.cs", "Metarill.Data/Queries/FloatingStatistics.cs", "Metarill.Exports/FloatingWorkbookWriter.cs" })
            {
                var expected = (await File.ReadAllTextAsync(Path.Combine(source, "backend/src", relative))).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                Assert.Equal(expected, await File.ReadAllTextAsync(Path.Combine(package, "src", relative)));
            }
            Assert.Equal(File.ReadAllBytes(Path.Combine(source, "frontend/metarill_uni_admin/common/yzp_runtime/yzp-runtime.js")), File.ReadAllBytes(Path.Combine(package, "frontend/common/yzp_runtime/yzp-runtime.js")));
            var hostDirectory = CSharpGenerator.SafePath(package, "backend/Application.Host");
            var host = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory);
            host.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory, "bin/Release/net10.0/Application.Host.dll"));
            host.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; host.Environment["DOTNET_ENVIRONMENT"] = "Production"; host.Environment["Authentication__Mode"] = "Web"; host.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
            host.Environment["Database__Provider"] = "mysql"; host.Environment["Database__ExpectedDatabase"] = "offline"; host.Environment["Database__ConnectionString"] = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";
            host.Environment["Files__RootDirectory"] = Path.Combine(directory, "files"); host.Environment["Exports__RootDirectory"] = Path.Combine(directory, "exports"); host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = logs;
            await using (var running = await FreshHost.StartAsync(host, "/health"))
                foreach (var page in catalog.ConfigPages.Where(page => page.Id is "101" or "201"))
                {
                    var table = catalog.ConfigTables.Single(table => table.Id == page.ConfigTableId);
                    foreach (var action in new[] { "page", "importDatas" + page.Id, "initExport" })
                        await FreshWebHttp.SendAsync(running.Client, System.Net.Http.HttpMethod.Post, "/api/admin/auto/" + table.TableName + "/" + page.Id + "/" + action, new { }, System.Net.HttpStatusCode.Unauthorized);
                }
            DoublePersistenceNativePreparation.Verify(proof);
            var file = CSharpGenerator.SafePath(package, "src/Metarill.Data/Infrastructure/SqlRowStore.cs"); var original = await File.ReadAllBytesAsync(file);
            await File.AppendAllTextAsync(file, "\n// 本轮故意制造来源漂移。\n"); Assert.Throws<InvalidOperationException>(() => DoublePersistenceNativePreparation.Verify(proof)); Assert.Throws<InvalidOperationException>(() => DoublePersistenceNativePreparation.Verify(DoublePersistenceNativePreparation.Capture(package))); await File.WriteAllBytesAsync(file, original);
            var foreign = CSharpGenerator.SafePath(package, "src/Metarill.Core/Foreign.cs"); await File.WriteAllTextAsync(foreign, "// 重新捕获不能收编额外代码。\n"); Assert.Throws<InvalidOperationException>(() => DoublePersistenceNativePreparation.Verify(DoublePersistenceNativePreparation.Capture(package))); File.Delete(foreign);
            var marker = CSharpGenerator.SafePath(package, ".metarill-package.json"); var markerBytes = await File.ReadAllBytesAsync(marker); var node = JsonNode.Parse(markerBytes)!; node["authenticationProfile"] = "development-only"; await File.WriteAllTextAsync(marker, node.ToJsonString()); Assert.Throws<InvalidOperationException>(() => DoublePersistenceNativePreparation.Verify(DoublePersistenceNativePreparation.Capture(package))); await File.WriteAllBytesAsync(marker, markerBytes);
            var copied = DoublePersistenceNativePreparation.CopyForExecution(proof, Path.Combine(directory, "execution/application")); var journal = CSharpGenerator.SafePath(copied.PackageRoot, "backend/Application.Host/.local/schema-history.jsonl"); Directory.CreateDirectory(Path.GetDirectoryName(journal)!); await File.WriteAllTextAsync(journal, "{}\n"); Assert.Throws<InvalidOperationException>(() => DoublePersistenceNativePreparation.Verify(copied)); DoublePersistenceNativePreparation.Verify(copied, true); DoublePersistenceNativePreparation.Verify(proof);
            var output = Environment.GetEnvironmentVariable("METARILL_DOUBLE_PERSISTENCE_PREPARE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Assert.Equal(CSharpGenerator.SafePath(source, DoublePersistenceNativePreparation.Root + "/prepared"), Path.GetFullPath(output));
                var exported = DoublePersistenceNativePreparation.CopyForExecution(proof, output);
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(output)!, "prepared-receipt.json"), JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }
}