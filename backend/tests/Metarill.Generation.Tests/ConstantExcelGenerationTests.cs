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
    public async Task Scalar_constants_use_typed_labels_defaults_and_workbook_values(int storage)
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
        Add("75", "active_flag", "bool", "启用状态", "ConstList", "启用|1,停用|false", "0");
        Add("76", "verified_flag", "bool", "审核状态", "ConstDownList", "通过|true,驳回|0", "false");
        Add("77", "floating_code", "double", "双精度值", "ConstDownList", "常规|1.250e-12,最大|1.7976931348623157e308", "1.250e-12");

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
            Assert.Contains($"true => {JsonSerializer.Serialize("启用")}", export);
            Assert.Contains($"false => {JsonSerializer.Serialize("驳回")}", export);
            Assert.Contains($"1.25E-12D => {JsonSerializer.Serialize("常规")}", export, StringComparison.OrdinalIgnoreCase);
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-constant-excel");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), model.Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var generated = Path.Combine(directory, "generated");
            var listFile = Path.Combine(generated, $"frontend/pages/yzp/{table.TableName}-list-{page.Id}.vue");
            var detailFile = Path.Combine(generated, $"frontend/pages/yzp/{table.TableName}-detail-{page.Id}.vue");
            var list = await File.ReadAllTextAsync(listFile);
            var detail = await File.ReadAllTextAsync(detailFile);
            Assert.Contains("v-model='filters.activeFlag' :localdata='activeFlagRange'", list);
            Assert.Contains("this.filters.activeFlag=0;", list);
            Assert.Contains("this.filters.verifiedFlag=0;", list);
            Assert.Contains("this.filters.floatingCode=1.25E-12;", list, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("v-model='entity.verifiedFlag' :localdata='verifiedFlagRange'", detail);
            Assert.Contains("activeFlag:0", detail);
            Assert.Contains("verifiedFlag:0", detail);

            var dto = assembly.GetType($"{model.Namespace}.Generated.DTOs.{entity.Name}Page101UpdateRequest", true)!;
            var dtoOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            dtoOptions.Converters.Add(new StringLongJsonConverter());
            var update = JsonSerializer.Deserialize("""{"id":"1","activeFlag":1,"verifiedFlag":null}""", dto, dtoOptions)!;
            Assert.Equal(new Optional<bool?>(true), (Optional<bool?>)dto.GetProperty("ActiveFlag")!.GetValue(update)!);
            Assert.Equal(new Optional<bool?>(null), (Optional<bool?>)dto.GetProperty("VerifiedFlag")!.GetValue(update)!);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"id":"1","activeFlag":2}""", dto, dtoOptions));

            var vo = assembly.GetType($"{model.Namespace}.Generated.VOs.{entity.Name}Page101Vo", true)!;
            var readRow = new Dictionary<string, object?> { ["id"] = 1L, ["active_flag"] = false, ["verified_flag"] = true, ["floating_code"] = 1.25e-12d };
            object displayed;
            if (storage == 0)
            {
                // 固定读取必须返回全部声明列。空值模拟实际读取到的 SQL NULL。
                var fullRow = NonRedundantReadCompilation.ForPage(model, entity, compiledPage).Outputs
                    .ToDictionary(output => output.Storage, output => readRow.GetValueOrDefault(output.Storage));
                displayed = vo.GetMethod("FromReadRow")!.Invoke(null, [fullRow])!;
            }
            else
            {
                var entityType = assembly.GetType($"{model.Namespace}.Generated.Entities.{entity.Name}", true)!;
                var record = Activator.CreateInstance(entityType)!;
                foreach (var pair in readRow) entityType.GetProperty(GenerationModel.Pascal(pair.Key))!.SetValue(record, pair.Value);
                displayed = vo.GetMethod("FromEntity")!.Invoke(null, [record])!;
            }
            var projected = JsonSerializer.SerializeToElement(displayed, vo, dtoOptions);
            Assert.Equal(0, projected.GetProperty("activeFlag").GetInt32());
            Assert.Equal(1, projected.GetProperty("verifiedFlag").GetInt32());
            Assert.Equal("停用", projected.GetProperty("activeFlagShow").GetString());
            Assert.Equal("通过", projected.GetProperty("verifiedFlagShow").GetString());
            Assert.Equal("常规", projected.GetProperty("floatingCodeShow").GetString());
            var service = assembly.GetType($"{model.Namespace}.Generated.Services.{entity.Name}Service", true)!;
            var instance = RuntimeHelpers.GetUninitializedObject(service);
            var actor = new RuntimeActor(1, "常量导入", false, catalog.YzProjectId, "5");
            var method = service.GetMethod("ParseImport101Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var floatingCell = JsonDocument.Parse("0.00000000000125");
            foreach (var row in new object[]
            {
                new Dictionary<string, object> { ["优先级"] = "高", ["手续费"] = "标准", ["等级"] = "大整数", ["序列码"] = "保留原编码", ["启用状态"] = "停用", ["审核状态"] = "通过", ["双精度值"] = "常规" },
                new Dictionary<string, object> { ["优先级"] = 2, ["手续费"] = 1.5m, ["等级"] = "9007199254740993", ["序列码"] = "9007199254740994", ["启用状态"] = 0, ["审核状态"] = true, ["双精度值"] = floatingCell.RootElement }
            })
            {
                var task = (Task<Dictionary<string, JsonElement>>)method.Invoke(instance,
                    [JsonSerializer.SerializeToElement(row), "1", actor, CancellationToken.None])!;
                var values = await task;
                Assert.Equal(2, values["priority"].GetInt32());
                Assert.Equal(1.5m, values["fee"].GetDecimal());
                Assert.Equal(9007199254740993L, values["rank"].GetInt64());
                Assert.Equal(9007199254740994L, values["serialCode"].GetInt64());
                Assert.False(values["activeFlag"].GetBoolean());
                Assert.True(values["verifiedFlag"].GetBoolean());
                Assert.Equal(1.25e-12d, values["floatingCode"].GetDouble());
            }

            var unmatched = JsonSerializer.SerializeToElement(new Dictionary<string, double> { ["双精度值"] = 1.25e-11 });
            var error = await Assert.ThrowsAsync<PageImportException>(() =>
                (Task)method.Invoke(instance, [unmatched, "1", actor, CancellationToken.None])!);
            Assert.Equal(PageImportError.Constant, error.Code);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }

        void Add(string id, string name, string type, string heading, string kind, string options, string initial = "")
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = id, ConfigTableId = table.Id, YzProjectId = catalog.YzProjectId,
                ColumnName = name, LogicalType = type, Name = heading, ControlKind = kind, ControlDetail = options, DefaultValue = initial
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = page.Id + id, ConfigPageId = page.Id, ConfigColumnId = id, YzProjectId = catalog.YzProjectId,
                IsShow = 1, IsEdit = 1,
                Extra = initial.Length == 0 ? new() : new()
                {
                    ["isInFilter"] = JsonSerializer.SerializeToElement(1),
                    ["queryDefaultValue"] = JsonSerializer.SerializeToElement(initial)
                }
            });
        }
    }
}
