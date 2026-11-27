using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行实际生成代码中的小数筛选及原中文表头导入入口。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GeneratedDecimalValueTests
{
    private const string Namespace = "Example.GuardContract";

    [Fact]
    public async Task Generated_filters_and_excel_reject_inexact_decimal_before_writes()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-decimal");
        var root = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var catalog = Catalog();
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(root, "generated"), Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            using var run = new GeneratedMutationGuardRun(assembly);
            var service = run.Service([]);
            var query = service.GetType().GetMethod("Query101", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var input = new PageRequest { Filters = new() { ["amount"] = JsonSerializer.Deserialize<JsonElement>("1e-29") } };
            Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(() => query.Invoke(service, [input, run.Actor])).InnerException);

            var dto = assembly.GetType(Namespace + ".Generated.DTOs.CustomerPage101ImportRequest", true)!;
            var request = JsonSerializer.Deserialize(
                """{"requestJson":[{"名称":"测试客户","金额":"12345678901234567890.123456789123456789"}]}""", dto, JsonDefaults.Options)!;
            var import = service.GetType().GetMethod("Import101Async")!;
            var failure = await Assert.ThrowsAsync<PageImportException>(async () =>
                await (Task)import.Invoke(service, [request, run.Actor, CancellationToken.None])!);
            Assert.Contains("金额", failure.Message);
            Assert.DoesNotContain("write:Insert", run.Trace);
            Assert.Contains("rollback", run.Trace);
        }
        finally
        {
            if (Directory.Exists(root)) await TestDirectoryCleanup.DeleteAsync(root, boundary);
        }
    }

    [Fact]
    public void Inexact_decimal_default_is_rejected_before_generated_output()
    {
        var catalog = Catalog();
        catalog.ConfigColumns.Single(column => column.ColumnName == "amount").DefaultValue = "1e-29";
        var output = Path.Combine(Path.GetTempPath(), "metarill-invalid-decimal-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(catalog, output, Namespace));
        Assert.False(Directory.Exists(output));
    }

    /// <summary>复用原项目及守卫模拟，仅添加普通小数字段和原 Excel 导入选择。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        catalog.ConfigPages.Single(page => page.Id == "101").IsImport = 1;
        catalog.ConfigColumns.Add(new()
        {
            Id = "906", ConfigTableId = "1", ColumnName = "amount", Name = "金额", LogicalType = "decimal"
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id = "907", ConfigPageId = "101", ConfigColumnId = "906", IsEdit = 1, IsShow = 1,
            Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) }
        });
        return catalog;
    }
}
