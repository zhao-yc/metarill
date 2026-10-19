using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>证明与生成SQL使用同一固定范围/排序计划；所有SQL解析均离线，不连接数据库。</summary>
public sealed class FixedPageQueryCompilationTests
{
    /// <summary>实际自动项目与显式父绑定分别比较，不能以相同参数值混同两种范围。</summary>
    [Theory]
    [InlineData(false, "", "yz_project_id=@current_yz_project_id", true)]
    [InlineData(true, "", "yz_project_id=@current_yz_project_id", false)]
    [InlineData(true, "yz_project_id=@current_yz_project_id", "yz_project_id=@current_yz_project_id", true)]
    [InlineData(false, "parent_id=@parent_id", "parent_id=@parentId", true)]
    [InlineData(false, "parent_id=@parent_id", "other_parent_id=@parent_id", false)]
    [InlineData(false, "parent_id=@parent_id", "parent_id=@current_yz_project_id", false)]
    [InlineData(false, "parent_id=@parent_id", "parent_id=@parentId AND yz_project_id=@current_yz_project_id", true)]
    [InlineData(true, "parent_id=@parent_id", "parent_id=@parentId AND yz_project_id=@current_yz_project_id", false)]
    [InlineData(false, "", "parent_id=@parent_id", false)]
    [InlineData(false, "", "yz_project_id=@current_yz_project_id AND yz_project_id=@current_yz_project_id", true)]
    [InlineData(false, "", "yz_project_id=@current_yz_project_id OR parent_id=@parent_id", false)]
    [InlineData(false, "", "id=coalesce(@parent_id,id)", false)]
    [InlineData(false, "", "yz_project_id=@project_id", false)]
    [InlineData(false, "", "delete_no is null", false)]
    public void Scope_proof_matches_effective_identity_bindings(bool global, string pageWhere, string extraWhere, bool expected)
    {
        var catalog = Catalog(); catalog.ConfigPages[0].WhereSql = pageWhere;
        if (global) catalog.ConfigTables[0].Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        var before = JsonSerializer.Serialize(catalog); var (table, page) = Model(catalog); var plan = FixedPageQueryCompilation.For(table, page);
        Assert.Equal("14", plan.SoftDeleteColumn!.Source.Id);
        Assert.Equal(!global || pageWhere.Contains("@current_yz_project_id", StringComparison.Ordinal), plan.IdentityScopes.Any(scope => scope.Source == "project"));
        Assert.Equal(expected, FixedPageQueryCompilation.TryProveSelection(plan, new("101", "name", "选择", extraWhere, ""), out var reason));
        Assert.Equal(expected, reason is null); Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>控件排序与真实默认序列一起补稳定主键；未知SQL和不同序列仍拒绝。</summary>
    [Theory]
    [InlineData("", "id desc", true)] [InlineData("", "id", false)] [InlineData("", "name asc", false)]
    [InlineData("name asc", "name asc,id desc", true)] [InlineData("name asc,id desc", "name asc", true)]
    [InlineData("id asc", "id asc", true)] [InlineData("id asc,name desc", "id asc", false)]
    [InlineData("name asc", "name desc", false)] [InlineData("name asc", "name asc,id asc", false)]
    [InlineData("parent_id asc,name desc", "parent_id asc,name desc,id desc", true)]
    [InlineData("parent_id asc,name desc", "name desc,parent_id asc", false)]
    [InlineData("", "rand()", false)] [InlineData("", "unknown desc", false)]
    [InlineData("name asc", "", true)]
    public void Default_order_proof_includes_the_same_id_tiebreaker(string pageOrder, string extraOrder, bool expected)
    {
        var catalog = Catalog(); catalog.ConfigPages[0].OrderBy = pageOrder; var (table, page) = Model(catalog);
        var plan = FixedPageQueryCompilation.For(table, page);
        Assert.Equal(expected, FixedPageQueryCompilation.TryProveSelection(plan, new("101", "name", "选择", "", extraOrder), out var reason));
        Assert.Equal(expected, reason is null);
    }

    /// <summary>没有物理软删除列时计划和Mapper均保持原有行为，不凭用途读取依赖添加条件。</summary>
    [Fact]
    public void Missing_soft_delete_column_does_not_create_a_predicate()
    {
        var catalog = Catalog(); catalog.ConfigColumns.RemoveAll(column => column.Id == "14"); var (table, page) = Model(catalog);
        Assert.Null(FixedPageQueryCompilation.SoftDeleteColumn(table)); Assert.Null(FixedPageQueryCompilation.For(table, page).SoftDeleteColumn);
    }

    /// <summary>有效单选证明不能越过IR原有联接、非冗余和未知目标父门禁。</summary>
    [Theory]
    [InlineData("valid")] [InlineData("join")] [InlineData("targetJoin")] [InlineData("nonredundant")] [InlineData("targetNonredundant")] [InlineData("unknownParent")]
    public void Single_projection_keeps_existing_structural_gaps(string scenario)
    {
        var catalog = GeneratedMultiSelectContractTests.Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == "25");
        source.ColumnName = "target_id"; source.LogicalType = "int64"; source.ControlKind = "SelectPage";
        source.ControlDetail = "{\"pageId\":\"301\",\"whereSql\":\"yz_project_id=@current_yz_project_id\",\"orderBy\":\"id desc\"}";
        catalog.ConfigColumns.Single(column => column.Id == "27").ColumnName = "target_name";
        catalog.ConfigPages.Single(page => page.Id == "301").Extra["type"] = JsonSerializer.SerializeToElement(1);
        if (scenario == "join") source.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        if (scenario == "targetJoin") catalog.ConfigColumns.Single(column => column.Id == "32").Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        if (scenario == "nonredundant") catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 0;
        if (scenario == "targetNonredundant") catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 0;
        if (scenario == "unknownParent") catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = "yz_project_id=@current_yz_project_id";
        var model = GenerationModel.Create(catalog, "Example.KeepProjectionGaps"); var table = model.Tables.Single(table => table.Source.Id == "2"); var page = table.Pages.Single(page => page.Source.Id == "201");
        var plan = ReadProjectionCompilation.ForPage(model, table, page).Detail;
        Assert.Equal(scenario == "valid", plan.CanCompile);
        if (scenario == "valid")
        {
            var target = model.Tables.Single(table => table.Source.Id == "3");
            Assert.DoesNotContain(ReadProjectionCompilation.ForPage(model, target, target.Pages.Single()).Selection.Gaps, gap => gap.ColumnId == "25");
        }
    }

    /// <summary>抽取计划不能顺带放宽Multi的既有额外排序能力边界。</summary>
    [Fact]
    public void Multi_keeps_its_existing_order_gap()
    {
        var catalog = GeneratedMultiSelectContractTests.Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == "25");
        var definition = System.Text.Json.Nodes.JsonNode.Parse(source.ControlDetail)!; definition["orderBy"] = "id desc"; source.ControlDetail = definition.ToJsonString();
        var model = GenerationModel.Create(catalog, "Example.KeepMultiGap"); var table = model.Tables.Single(table => table.Source.Id == "2"); var page = table.Pages.Single(page => page.Source.Id == "201");
        var result = MultiSelectCompilation.Resolve(model, table, page, table.Columns.Single(column => column.Source.Id == "25"));
        Assert.Null(result.Definition); Assert.Contains("排序", result.Reason);
    }

    /// <summary>真实生成并编译后解析SQL，检查普通/global/显式项目/父范围以及默认和取消排序；不执行命令。</summary>
    [Fact]
    public async Task Generated_query_sql_preserves_mapper_scope_parameters_and_default_order()
    {
        var catalog = Catalog(); catalog.ConfigPages[0].WhereSql = "parent_id=@parentId"; catalog.ConfigPages[0].OrderBy = "name asc";
        catalog.ConfigTables.Add(new() { Id = "2", TableName = "global_row", Extra = new() { ["scopeKind"] = JsonSerializer.SerializeToElement("global") } });
        catalog.ConfigColumns.AddRange([new() { Id = "21", ConfigTableId = "2", ColumnName = "id", LogicalType = "id" }, new() { Id = "22", ConfigTableId = "2", ColumnName = "name", LogicalType = "string" }, new() { Id = "23", ConfigTableId = "2", ColumnName = "yz_project_id", LogicalType = "id" }, new() { Id = "24", ConfigTableId = "2", ColumnName = "delete_no", LogicalType = "datetime" }]);
        foreach (var (id, where) in new[] { ("201", ""), ("202", "yz_project_id=@current_yz_project_id") })
        {
            catalog.ConfigPages.Add(new() { Id = id, ConfigTableId = "2", WhereSql = where });
            catalog.ConfigPageColumns.Add(new() { Id = id + "22", ConfigPageId = id, ConfigColumnId = "22", IsShow = 1 });
        }
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-query"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.FixedQuery");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session = new DatabaseSession(new() { Provider = "mysql", ExpectedDatabase = "metarill_business_test", ConnectionString = "Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;" });
            var actor = new RuntimeActor(1, "离线固定查询", false, "9007199254740993", "1");
            foreach (var (table, page, scoped, parent) in new[] { ("Customer", "101", true, true), ("GlobalRow", "201", false, false), ("GlobalRow", "202", true, false) })
            {
                var entity = assembly.GetType("Example.FixedQuery.Generated.Entities." + table, true)!;
                var serviceType = assembly.GetType("Example.FixedQuery.Generated.Services." + table + "Service", true)!;
                var mapper = Activator.CreateInstance(assembly.GetType("Example.FixedQuery.Generated.Mappers." + table + "Mapper", true)!, [session]);
                var arguments = new object?[serviceType.GetConstructors().Single().GetParameters().Length]; arguments[0] = session; arguments[1] = mapper; arguments[2] = new SnowflakeIdGenerator(864); arguments[3] = Array.Empty<IRecordMutationObserver>();
                var service = Activator.CreateInstance(serviceType, arguments)!;
                var request = new PageRequest { ParentId = "9007199254742000" };
                object Query() => serviceType.GetMethod("Query" + page, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [request, actor])!;
                object Order(object query) => serviceType.GetMethod("Order" + page, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [query, request])!;
                var sql = System.Text.RegularExpressions.Regex.Replace(Sql(entity, Order(Query())), @"\s+", " "); var where = sql[sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase)..];
                Assert.Contains("`delete_no` IS NULL", where, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(scoped, where.Contains("`yz_project_id` =", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(parent, where.Contains("`parent_id` =", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(actor.ProjectId, sql); Assert.DoesNotContain(request.ParentId, sql);
                Assert.Contains(parent ? "ORDER BY a.`name`, a.`id` DESC" : "ORDER BY a.`id` DESC", sql, StringComparison.OrdinalIgnoreCase);
                request.OrderBy = new() { Column = "name", Order = null }; Assert.Equal(sql, System.Text.RegularExpressions.Regex.Replace(Sql(entity, Order(Query())), @"\s+", " "));
                if (parent)
                {
                    request.ParentId = null;
                    Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => Query()).InnerException);
                }
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>原平台两字段的配置完整保留，实际原Vue发送名称键；只解除相同固定范围/排序的两个Gap。</summary>
    [Fact]
    public async Task Platform_two_name_filters_use_redundant_constraints_without_mutating_metadata()
    {
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(BackendRoot(), "metadata/platform.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var before = JsonSerializer.Serialize(catalog); var model = GenerationModel.Create(catalog, "Example.FixedPlatform"); var table = model.Tables.Single(table => table.Source.TableName == "config_child"); var page = table.Pages.Single();
        var plan = SelectionNameFilterCompilation.ForPage(model, table, page); Assert.Empty(plan.Gaps);
        Assert.Equal(new[] { "childConfigPageName", "parentConfigPageName" }, plan.Bindings.Select(binding => binding.FilterKey).Order(StringComparer.Ordinal));
        var read = ReadProjectionCompilation.ForPage(model, table, page);
        foreach (var id in new[] { "1679391145852018689", "1679391146044956674" }) Assert.DoesNotContain(read.List.Gaps.Concat(read.Detail.Gaps), gap => gap.ColumnId == id);
        var target = model.Tables.Single(table => table.Source.TableName == "config_page"); var targetPage = target.Pages.Single(page => page.Source.Id == "1869016706289577986");
        Assert.DoesNotContain(ReadProjectionCompilation.ForPage(model, target, targetPage).Selection.Gaps, gap => gap.ColumnId is "1679391145852018689" or "1679391146044956674");
        Assert.Equal(2, GenerationCapabilityAudit.Collect(model).Count); Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-platform"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, output, "Example.FixedPlatform"); await Probe(output);
            Assert.Equal(before, JsonSerializer.Serialize(catalog));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    /// <summary>把实际泛型ISelect交给FreeSql ToSql，避免以生成字符串断言代替ORM解析。</summary>
    private static string Sql(Type entity, object query) => (string)typeof(FixedPageQueryCompilationTests).GetMethod(nameof(TypedSql), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(entity).Invoke(null, [query])!;
    private static string TypedSql<T>(object query) where T : class => ((ISelect<T>)query).ToSql();
    /// <summary>普通行模型含两个不同父键；同值也不能互换已固定绑定列。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        catalog.ConfigColumns.AddRange([new() { Id = "16", ConfigTableId = "1", ColumnName = "parent_id", LogicalType = "id", LinkConfigTableId = "1" }, new() { Id = "17", ConfigTableId = "1", ColumnName = "other_parent_id", LogicalType = "id", LinkConfigTableId = "1" }]);
        return catalog;
    }
    /// <summary>使用真实生成模型创建计划，不由测试手造查询约束。</summary>
    private static (TableModel Table, PageModel Page) Model(MetadataCatalog catalog) { var model = GenerationModel.Create(catalog, "Example.FixedQuery"); var table = model.Tables[0]; return (table, table.Pages[0]); }
    private static string BackendRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../.."));
    /// <summary>挂载原平台列表模板，只观察公开请求，不连接后端或替换原UI。</summary>
    private static async Task Probe(string output, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "FixedPageQueryProofProbe.mjs")); start.ArgumentList.Add(output);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
