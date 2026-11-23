using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class MultipleIdCheckboxFilterGenerationTests
{
    /// <summary>真实完整包沿用原多选复选组件。两种物理数组控件按各自存储合同生成查询。</summary>
    [Theory]
    [InlineData(false, "multi")]
    [InlineData(false, "checkbox")]
    [InlineData(false, "stored")]
    [InlineData(false, "stored_global")]
    [InlineData(true, "multi")]
    [InlineData(true, "checkbox")]
    [InlineData(true, "constant")]
    public async Task Published_multi_id_checkboxes_preserve_the_original_storage_comparisons(bool nonRedundant, string control)
    {
        const string ns = "Example.MultiIdCheckbox";
        var stored = control.StartsWith("stored", StringComparison.Ordinal);
        var draft = control == "checkbox" || stored ? DatabaseCheckboxGenerationTests.Catalog(nonRedundant) : nonRedundant
            ? NonRedundantMultiSelectionTests.Catalog() : GeneratedMultiSelectContractTests.Catalog();
        if (control == "constant")
        {
            var source = draft.ConfigColumns.Single(column => column.Id == "25");
            source.ControlKind = "ConstList";
            source.ControlDetail = "One|1,Other|9007199254740993";
            draft.ConfigColumns.RemoveAll(column => column.Id == "27");
            draft.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "27");
        }
        var catalog = RedundantMultiNameCacheTests.Published(draft);
        if (stored) DatabaseCheckboxListOptionsGenerationTests.ConfigureStoredCheckbox(catalog, control == "stored_global");
        var field = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == "25");
        field.Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        if (control == "constant") field.IsEdit = 0;
        catalog.ConfigColumns.Single(column => column.Id == "25").Extra["filterControlKind"] = JsonSerializer.SerializeToElement("CheckBox");

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-multi-id-filter");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var fixture = Path.Combine(directory, "fixture");
        var generated = Path.Combine(fixture, "generated");
        var application = Path.Combine(directory, "application");
        try
        {
            var package = new ApplicationPackager().Create(catalog, application, ns, NonRedundantApplicationFixture.Root());
            Assert.True(package.FrontendIncluded);
            var vue = await File.ReadAllTextAsync(Path.Combine(application, "frontend/pages/yzp/relation_source-list-201.vue"));
            Assert.Contains("multiple v-model='filters.targetIds' :localdata='targetIdsRange'", vue);
            Assert.DoesNotContain("v-model='filters.targetNames'", vue);
            if (control == "constant")
                Assert.Contains("""targetIdsRange:[{"text":"One","value":"1"},{"text":"Other","value":"9007199254740993"}]""", vue);
            new CSharpGenerator().Generate(catalog, generated, ns);
            var actual = await GeneratedRelationOptionsMySqlTests.BuildGenerated(fixture);
            var service = actual.GetType(ns + ".Generated.Services.RelationSourceService", true)!;
            var actor = new RuntimeActor(1, "多值筛选", false, catalog.YzProjectId, "5") { StoreId = 9007199254740995L };
            var request = new PageRequest { ParentId = stored ? null : "71", Filters = new() };
            if (nonRedundant)
            {
                var read = service.GetMethod("ReadFilters201", BindingFlags.NonPublic | BindingFlags.Static)!;
                request.Filters["targetIds"] = JsonSerializer.SerializeToElement(new[] { "1", "9007199254740993" });
                var filters = Assert.IsType<List<FixedReadFilter>>(read.Invoke(null, [request, actor]));
                var selected = Assert.Single(filters, filter => filter.Field == "target_ids");
                Assert.Equal(FixedReadComparison.JsonArrayHasAnyId, selected.Comparison);
                Assert.Equal(new[] { 1L, 9007199254740993L }, Assert.IsType<long[]>(selected.Value));
                Assert.Contains(filters, filter => filter.Field == "parent_id" && Equals(71L, filter.Value));
                Assert.Contains(filters, filter => filter.Field == "yz_project_id" && Equals(long.Parse(catalog.YzProjectId), filter.Value));

                var plan = MySqlNonRedundantReadCompiler.Compile(catalog, "2", ["id", "target_ids", "parent_id"]);
                VerifyMembers(plan, selected);
                request.Filters["targetIds"] = JsonSerializer.SerializeToElement("[]");
                Assert.DoesNotContain(Assert.IsType<List<FixedReadFilter>>(read.Invoke(null, [request, actor])),
                    filter => filter.Field == "target_ids");
                request.Filters["targetIds"] = JsonSerializer.SerializeToElement(new[] { "9223372036854775808" });
                Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => read.Invoke(null, [request, actor])).InnerException);
            }
            else
            {
                request.Filters["targetIds"] = stored
                    ? JsonSerializer.SerializeToElement(new[] { "1", "9007199254740993" })
                    : JsonSerializer.SerializeToElement(new[] { "[\"1\"]", "[\"11\",\"9007199254740993\"]" });
                using var orm = new FreeSqlBuilder().UseConnectionFactory(DataType.MySql,
                    () => throw new InvalidOperationException("离线多值验收禁止建立数据库连接。")).UseAutoSyncStructure(false).Build();
                using var session = new DatabaseSession(new()
                    { Provider = "mysql", ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Database=offline;User ID=unused" }, orm);
                var mapper = Activator.CreateInstance(actual.GetType(ns + ".Generated.Mappers.RelationSourceMapper", true)!, session)!;
                var arguments = new object?[service.GetConstructors().Single().GetParameters().Length];
                arguments[0] = session;
                arguments[1] = mapper;
                arguments[2] = new SnowflakeIdGenerator(840);
                arguments[3] = Array.Empty<IRecordMutationObserver>();
                arguments[4] = Array.Empty<IRecordMutationGuard>();
                var instance = Activator.CreateInstance(service, arguments)!;
                var query = service.GetMethod("Query201", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, [request, actor]);
                var sql = (string)typeof(MultipleIdCheckboxFilterGenerationTests).GetMethod(nameof(Sql), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(actual.GetType(ns + ".Generated.Entities.RelationSource", true)!).Invoke(null, [query])!;
                if (stored)
                {
                    Assert.Contains("JSON_TABLE", sql);
                    Assert.Contains("CAST(checkbox_ids.member_id AS BINARY) IN", sql);
                    Assert.DoesNotContain("[\"1\",\"9007199254740993\"]", sql);
                    // 高位 ID 以十进制字符串放入独立 JSON 参数，避免浮点转换。
                    var provider = Assert.IsAssignableFrom<FreeSql.Internal.CommonProvider.Select0Provider>(query);
                    var parameter = Assert.Single(provider._params, item =>
                        item.ParameterName.TrimStart('@').StartsWith("mt_json_ids_", StringComparison.Ordinal));
                    Assert.Contains("@" + parameter.ParameterName.TrimStart('@'), sql);
                    Assert.Equal("[\"1\",\"9007199254740993\"]", parameter.Value);
                    Assert.Contains("`yz_project_id`", sql);
                    Assert.Contains("`store_id`", sql);
                    Assert.DoesNotMatch(@"(?i)`target_ids`\s*\)*\s*IN\s*\(", sql);
                }
                else
                {
                    Assert.Matches(@"(?i)`target_ids`\s*\)*\s*IN\s*\(", sql);
                    Assert.Contains("[\"1\"]", sql);
                    Assert.Contains("[\"11\",\"9007199254740993\"]", sql);
                    Assert.DoesNotContain("JSON_TABLE", sql);
                }
            }
            if (nonRedundant && control == "multi")
                foreach (var operation in new[] { "restore", "build" })
                    await NonRedundantApplicationFixture.BuildAsync(application, operation);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>用正式读取器生成 SQL 并绑定参数。只构造命令，禁止向数据库执行。</summary>
    private static void VerifyMembers(MySqlNonRedundantReadPlan plan, FixedReadFilter selected)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var reader = typeof(MySqlNonRedundantReader);
        var prepare = reader.GetMethod("Prepare", flags)!;
        var prepared = Assert.IsType<FixedReadFilter>(prepare.Invoke(null, [plan, selected]));
        var query = Assert.IsType<string>(reader.GetMethod("Filter", flags)!.Invoke(null, [plan, prepared, 0]));
        Assert.Contains("JSON_TABLE(@filter_0", query);
        Assert.Contains("CAST(checkbox_ids.member_id AS BINARY) IN", query);
        Assert.DoesNotContain("9007199254740993", query);
        using var command = new MySqlCommand { CommandText = query };
        reader.GetMethod("Bind", flags)!.Invoke(null, [command, "54", new[] { prepared }]);
        Assert.Equal("[\"1\",\"9007199254740993\"]", command.Parameters["filter_0"].Value);
        Assert.Equal(3, command.Parameters.Count);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() =>
            prepare.Invoke(null, [plan, selected with { Field = "parent_id" }])).InnerException);
    }

    /// <summary>只翻译 ORM 表达式，验证冗余整列条件。</summary>
    private static string Sql<TEntity>(object query) where TEntity : class => ((ISelect<TEntity>)query).ToSql();
}
