using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class ScalarCheckboxFilterGenerationTests
{
    /// <summary>原多选复选按列值读取，同页普通日期仍使用原范围。两种存储都实际生成并编译。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Published_scalar_checkboxes_match_discrete_values(int storage)
    {
        const string rootNamespace = "Example.ScalarCheckboxFilters";
        var catalog = storage == 0 ? NonRedundantGeneratedFixture.Catalog("mt_cb_filter") : GeneratorTests.Catalog();
        var tableName = catalog.ConfigTables.Single(table => table.Id == "1").TableName;
        var entityName = GenerationModel.Pascal(tableName);
        foreach (var column in catalog.ConfigColumns.Where(column => column.LogicalType == "id")) column.LogicalType = "int64";
        Add("51", "event_time", "datetime", "date_time",
            "开场|2024-02-01 09:30:00,中场|2024-02-03 10:30:00,终场|2024-02-09 14:00:00", true);
        Add("52", "event_range", "date", "date", "", false);
        Add("53", "batch_code", "string", "ConstList", "甲|a%,乙|b", true);
        Add("54", "reference_no", "int64", "ConstList", "奇数|9007199254740993,偶数|9007199254740994", true);
        Add("55", "is_active", "bool", "CheckBox", "有效|1,无效|0", true);
        if (storage == 1) RedundantMultiNameCacheTests.Published(catalog);
        else
            foreach (var record in catalog.ConfigTables.Cast<Metarill.Core.Metadata.MetadataRecord>()
                         .Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))
                record.YzProjectId = catalog.YzProjectId;

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-scalar-checkbox-filter");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var fixture = Path.Combine(directory, "fixture");
        var generated = Path.Combine(fixture, "generated");
        try
        {
            var package = new ApplicationPackager().Create(catalog, Path.Combine(directory, "application"), rootNamespace,
                NonRedundantApplicationFixture.Root());
            Assert.True(package.FrontendIncluded);
            new CSharpGenerator().Generate(catalog, generated, rootNamespace);
            var vue = await File.ReadAllTextAsync(Path.Combine(generated, $"frontend/pages/yzp/{tableName}-list-101.vue"));
            Assert.Contains("multiple v-model='filters.eventTime' :localdata='eventTimeRange'", vue);
            Assert.Contains("multiple v-model='filters.batchCode' :localdata='batchCodeRange'", vue);
            Assert.Contains("\"9007199254740993\"", vue);
            Assert.Contains("type='daterange' v-model='filters.eventRange'", vue);

            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(fixture);
            var service = assembly.GetType($"{rootNamespace}.Generated.Services.{entityName}Service", true)!;
            var actor = new RuntimeActor(1, "离线多选筛选", false, catalog.YzProjectId, "5");
            var eventTimes = new[] { "2024-02-01 09:30:00", "2024-02-03 10:30:00", "2024-02-09 14:00:00" };
            var request = new PageRequest
            {
                ParentId = "71",
                Filters = new()
                {
                    ["eventTime"] = JsonSerializer.SerializeToElement(eventTimes),
                    ["eventRange"] = JsonSerializer.SerializeToElement(new[] { "2024-02-01", "2024-02-29" }),
                    ["batchCode"] = JsonSerializer.SerializeToElement("a%"),
                    ["referenceNo"] = JsonSerializer.SerializeToElement(new[] { "9007199254740993", "9007199254740994" }),
                    ["isActive"] = JsonSerializer.SerializeToElement(new[] { "1" })
                }
            };

            if (storage == 0)
            {
                var method = service.GetMethod("ReadFilters101", BindingFlags.NonPublic | BindingFlags.Static)!;
                var filters = Assert.IsType<List<FixedReadFilter>>(method.Invoke(null, [request, actor]));
                var events = Assert.Single(filters, filter => filter.Field == "event_time");
                Assert.Equal(FixedReadComparison.In, events.Comparison);
                Assert.Equal(new DateTime(2024, 2, 3, 10, 30, 0), Assert.IsType<DateTime>(Assert.IsType<object[]>(events.Value)[1]));
                Assert.Equal(FixedReadComparison.Equal, Assert.Single(filters, filter => filter.Field == "batch_code").Comparison);
                var active = Assert.Single(filters, filter => filter.Field == "is_active");
                Assert.Equal(FixedReadComparison.In, active.Comparison);
                Assert.True(Assert.IsType<bool>(Assert.Single(Assert.IsType<object[]>(active.Value))));
                var references = Assert.Single(filters, filter => filter.Field == "reference_no");
                Assert.Equal(FixedReadComparison.In, references.Comparison);
                Assert.Equal(9007199254740993L, Assert.IsType<long>(Assert.IsType<object[]>(references.Value)[0]));
                Assert.Contains(filters, filter => filter.Field == "event_range" && filter.Comparison == FixedReadComparison.GreaterOrEqual);
                Assert.Contains(filters, filter => filter.Field == "event_range" && filter.Comparison == FixedReadComparison.Less);
                request.Filters["eventTime"] = JsonSerializer.SerializeToElement(Array.Empty<string>());
                Assert.DoesNotContain(Assert.IsType<List<FixedReadFilter>>(method.Invoke(null, [request, actor])),
                    filter => filter.Field == "event_time");
            }
            else
            {
                using var orm = new FreeSqlBuilder().UseConnectionFactory(DataType.MySql,
                    () => throw new InvalidOperationException("离线筛选验证禁止建立数据库连接。")).UseAutoSyncStructure(false).Build();
                using var session = new DatabaseSession(new()
                    { Provider = "mysql", ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Database=offline;User ID=unused" }, orm);
                var mapper = Activator.CreateInstance(assembly.GetType($"{rootNamespace}.Generated.Mappers.{entityName}Mapper", true)!, session)!;
                var instance = Activator.CreateInstance(service, session, mapper, new SnowflakeIdGenerator(841),
                    Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>())!;
                var selection = service.GetMethod("Query101", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, [request, actor]);
                var sql = (string)typeof(ScalarCheckboxFilterGenerationTests).GetMethod(nameof(Sql), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(assembly.GetType($"{rootNamespace}.Generated.Entities.{entityName}", true)!).Invoke(null, [selection])!;
                Assert.True(System.Text.RegularExpressions.Regex.IsMatch(sql, @"(?i)`event_time`\s*\)*\s*IN\s*\("), sql);
                foreach (var time in eventTimes) Assert.Contains(time, sql);
                Assert.DoesNotContain("`event_time` >=", sql);
                Assert.Contains("`event_range` >=", sql);
                Assert.Contains("`event_range` <", sql);
                Assert.Contains("`batch_code` =", sql);
                Assert.Matches(@"(?i)`reference_no`\s*\)*\s*IN\s*\(", sql);
                Assert.Contains("9007199254740993", sql);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        void Add(string id, string name, string type, string kind, string choices, bool checkbox)
        {
            var column = new Metarill.Core.Metadata.ColumnMetadata
            {
                Id = id, ConfigTableId = "1", ColumnName = name, Name = name, LogicalType = type,
                ControlKind = kind, ControlDetail = choices
            };
            if (checkbox) column.Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");
            catalog.ConfigColumns.Add(column);
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "10" + id, ConfigPageId = "101", ConfigColumnId = id, IsShow = 1, IsEdit = 1,
                Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) }
            });
        }
    }

    /// <summary>只翻译生成查询，不执行数据库命令。</summary>
    private static string Sql<TEntity>(object query) where TEntity : class => ((ISelect<TEntity>)query).ToSql();
}
