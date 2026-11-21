using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>数值常量的原 Excel 标签与实际类型一致，导入可还原精确值。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class ConstantExcelGenerationTests
{
    /// <summary>列表及详情使用同一类型化标签。编译生成代码，并调用原导入解析方法。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Numeric_constants_use_typed_labels_and_allow_canonical_workbook_values(int storage)
    {
        var catalog = storage == 0 ? NonRedundantGeneratedFixture.Catalog("mt_constant_excel") : GeneratorTests.Catalog();
        var table = catalog.ConfigTables.Single(item => item.Id == "1");
        table.IsRongyu = storage;
        var page = catalog.ConfigPages.Single(item => item.Id == "101");
        page.IsExport = 1;
        page.IsImport = 1;
        catalog.ConfigPageColumns.RemoveAll(item => item.ConfigPageId == page.Id &&
            catalog.ConfigColumns.Single(column => column.Id == item.ConfigColumnId).ColumnName is not ("name" or "id"));
        Add("71", "priority", "int32", "优先级", "ConstList", "高|02,低|1");
        Add("72", "fee", "decimal", "手续费", "ConstDownList", "标准|1.500");
        Add("73", "rank", "int64", "等级", "ConstList", "大整数|09007199254740993");
        Add("74", "serial_code", "int64", "序列码", "ConstDownList", "保留原编码|09007199254740994");

        var model = GenerationModel.Create(catalog, "Example.ConstantExcel");
        var entity = model.Tables.Single(item => item.Source.Id == table.Id);
        var compiledPage = entity.Pages.Single();

        foreach (var mode in new[] { "list", "edit" })
        {
            page.Extra["exportMethod"] = JsonSerializer.SerializeToElement(mode);
            var export = ExportRenderer.Service(model, entity, compiledPage);
            Assert.Contains("entity.Priority switch", export);
            Assert.Contains("entity.Fee switch", export);
            Assert.Contains("entity.Rank switch", export);
            Assert.Contains($"2 => {JsonSerializer.Serialize("高")}", export);
            Assert.Contains($"1.5M => {JsonSerializer.Serialize("标准")}", export);
            Assert.Contains($"9007199254740993L => {JsonSerializer.Serialize("大整数")}", export);
            Assert.Contains("entity.SerialCode?.ToString(CultureInfo.InvariantCulture)", export);
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-constant-excel");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), model.Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType($"{model.Namespace}.Generated.Services.{entity.Name}Service", true)!;
            var instance = RuntimeHelpers.GetUninitializedObject(service);
            var actor = new RuntimeActor(1, "常量导入", false, catalog.YzProjectId, "5");
            var method = service.GetMethod("ParseImport101Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var row in new object[]
            {
                new Dictionary<string, object> { ["优先级"] = "高", ["手续费"] = "标准", ["等级"] = "大整数", ["序列码"] = "保留原编码" },
                new Dictionary<string, object> { ["优先级"] = 2, ["手续费"] = 1.5m, ["等级"] = "9007199254740993", ["序列码"] = "9007199254740994" }
            })
            {
                var task = (Task<Dictionary<string, JsonElement>>)method.Invoke(instance,
                    [JsonSerializer.SerializeToElement(row), "1", actor, CancellationToken.None])!;
                var values = await task;
                Assert.Equal(2, values["priority"].GetInt32());
                Assert.Equal(1.5m, values["fee"].GetDecimal());
                Assert.Equal(9007199254740993L, values["rank"].GetInt64());
                Assert.Equal(9007199254740994L, values["serialCode"].GetInt64());
            }
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }

        void Add(string id, string name, string type, string heading, string kind, string options)
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = id, ConfigTableId = table.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = name, LogicalType = type, Name = heading, ControlKind = kind, ControlDetail = options
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = page.Id + id, ConfigPageId = page.Id, ConfigColumnId = id, YzProjectId = catalog.YzProjectId,
                IsShow = 1, IsEdit = 1
            });
        }
    }
}
