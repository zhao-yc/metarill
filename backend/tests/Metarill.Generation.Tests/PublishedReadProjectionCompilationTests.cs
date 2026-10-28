using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>兼容原单VO的内部窄读，不把用途IR或离线SQL证明当成数据库性能验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class PublishedReadProjectionCompilationTests
{
    /// <summary>实际生成表达式交给FreeSql编译SQL并执行内存投影，原JSON逐字保持，写锁继续完整实体。</summary>
    [Fact]
    public async Task Real_generated_projection_keeps_published_json_and_excludes_hidden_payload_from_select()
    {
        var catalog = GeneratorTests.Catalog();
        var pageSource = Assert.Single(catalog.ConfigPages);
        pageSource.IsSave = 1; pageSource.IsCopy = 1; pageSource.IsDelete = 1;
        pageSource.Extra["type"] = JsonSerializer.SerializeToElement(2);
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "12").IsEdit = 0;
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13").IsShow = 0;
        catalog.ConfigColumns.Add(new() { Id = "16", ConfigTableId = "1", ColumnName = "hidden_payload", LogicalType = "string", ColumnLength = "max" });
        var before = JsonSerializer.Serialize(catalog);
        var model = GenerationModel.Create(catalog, "Example.PublishedRead"); var table = Assert.Single(model.Tables); var page = Assert.Single(table.Pages);
        Assert.Equal(new[] { "id", "name", "remark" }, PublishedReadProjectionCompilation.Columns(model, table, page).Select(column => column.Source.ColumnName));
        var expression = PublishedReadProjectionCompilation.Expression(model, table, page);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-published-read"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var generated = Path.Combine(directory, "generated"); new CSharpGenerator().Generate(catalog, generated, model.Namespace);
            var service = await File.ReadAllTextAsync(Path.Combine(generated, "Services/CustomerService.g.cs"));
            Assert.Contains(".ToOneAsync(" + expression + ", cancellationToken)", service);
            Assert.Contains(".ToListAsync(" + expression + ", cancellationToken)", service);
            Assert.Contains("query.Limit(200).ToListAsync(" + expression + ", cancellationToken)", service);
            Assert.Equal(3, service.Split(".ForUpdate().FirstAsync(cancellationToken)").Length - 1);
            Assert.DoesNotContain(".ForUpdate().ToOneAsync(", service);
            await File.WriteAllTextAsync(Path.Combine(directory, "ProjectionProbe.cs"), $$"""
using System.Text.Json;
using Metarill.Data;
using Example.PublishedRead.Generated.Entities;
using Example.PublishedRead.Generated.VOs;
public static class ProjectionProbe
{
    // 仅编译SQL，不打开连接；表达式直接取自实际生成器，并已核对三个读取入口。
    public static string Sql()
    {
        using var session = new DatabaseSession(new() { ExpectedDatabase="offline", ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1" });
        return session.Select<Customer>().Where(entity => entity.YzProjectId == 9007199254740993L && entity.DeleteNo == null)
            .OrderBy(entity => entity.HiddenPayload).Page(2,3).ToSql({{expression}});
    }
    // 模拟同一物理行完整实体与窄实体，使用真实生成VO序列化；ID超过JS安全整数。
    public static string Json(bool narrow)
    {
        var row = new Customer { Id=9007199254740993L, Name="客户甲", Remark="详情专用备注", HiddenPayload=new string('x',8192), YzProjectId=100 };
        Func<Customer,Customer> project = {{expression}};
        return JsonSerializer.Serialize(CustomerPage101Vo.FromEntity(narrow ? project(row) : row));
    }
}
""");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory); var probe = assembly.GetType("ProjectionProbe")!;
            var sql = (string)probe.GetMethod("Sql")!.Invoke(null, null)!;
            var selected = sql[..sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase)];
            foreach (var column in new[] { "id", "name", "remark" }) Assert.Contains("`" + column + "`", selected);
            foreach (var column in new[] { "hidden_payload", "yz_project_id", "delete_no" }) Assert.DoesNotContain("`" + column + "`", selected);
            Assert.Contains("`yz_project_id`", sql); Assert.Contains("`delete_no`", sql); Assert.Contains("`hidden_payload`", sql);
            Assert.Contains("LIMIT 3,3", sql, StringComparison.OrdinalIgnoreCase);
            var full = (string)probe.GetMethod("Json")!.Invoke(null, [false])!;
            Assert.Equal(full, (string)probe.GetMethod("Json")!.Invoke(null, [true])!);
            Assert.Contains("详情专用备注", JsonSerializer.Deserialize<JsonElement>(full).GetProperty("remark").GetString());
            var first = Directory.GetFiles(generated, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(generated,path), File.ReadAllBytes);
            catalog.ConfigColumns.Reverse(); new CSharpGenerator().Generate(catalog, generated, model.Namespace);
            foreach (var (path, bytes) in first) Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(generated,path)));
            catalog.ConfigColumns.Reverse(); Assert.Equal(before, JsonSerializer.Serialize(catalog));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>混合键跨表保留所有已存层级名；不能用隐藏无关大字段换取展示缺失。</summary>
    [Theory]
    [InlineData("string", "string")][InlineData("string", "int64")][InlineData("int64", "string")][InlineData("int64", "int64")]
    public void Cross_table_display_dependencies_are_stable_and_complete(string sourceType, string parentType)
    {
        var catalog = CrossTableReadTests.Catalog("projected_cross", parentType, parentType);
        catalog.ConfigColumns.Single(column => column.Id == "25").LogicalType = sourceType;
        catalog.ConfigColumns.Add(new() { Id = "26", ConfigTableId = "2", ColumnName = "country_name", LogicalType = "string" });
        catalog.ConfigPageColumns.Add(new() { Id = "20126", ConfigPageId = "201", ConfigColumnId = "26", IsShow = 0, IsEdit = 0 });
        var model = GenerationModel.Create(catalog, "Example.ProjectedCross"); var table = model.Tables.Single(table => table.Source.Id == "2"); var page = Assert.Single(table.Pages);
        var columns = PublishedReadProjectionCompilation.Columns(model, table, page);
        var plan = Assert.Single(CrossTableDisplayRenderer.ForPage(model, table, page));
        foreach (var level in plan.Levels) Assert.Contains(level.Name, columns);
        // 原编译器会为跨表展示隐式开放名称；布局关闭不等于从既有VO中删除。
        Assert.Contains(page.Readable, column => column.Source.Id == "26" && column.IsManagedCascadeName);
        Assert.Contains(columns, column => column.Source.Id == "26");
        foreach (var column in page.Readable) Assert.Contains(column, columns);
        Assert.Single(columns, column => column.IsId);
        Assert.Equal(columns.Count, columns.Select(column => column.Source.Id).Distinct().Count());
        var expression = PublishedReadProjectionCompilation.Expression(model, table, page);
        catalog.ConfigColumns.Reverse(); model = GenerationModel.Create(catalog, "Example.ProjectedCross"); table = model.Tables.Single(table => table.Source.Id == "2");
        Assert.Equal(expression, PublishedReadProjectionCompilation.Expression(model, table, Assert.Single(table.Pages)));
    }

    /// <summary>隐式多选名称不在原Readable时仍被读取，元数据及写DTO权限不扩张。</summary>
    [Fact]
    public void Implicit_multi_name_cache_remains_in_physical_projection()
    {
        var catalog = GeneratedMultiSelectContractTests.Catalog();
        catalog.ConfigColumns.RemoveAll(column => column.Id == "27"); catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId == "27");
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 1;
        var before = JsonSerializer.Serialize(catalog); var model = GenerationModel.Create(catalog, "Example.ProjectedMulti");
        var table = model.Tables.Single(table => table.Source.Id == "2"); var page = Assert.Single(table.Pages);
        var cache = Assert.Single(MultiNameStorageCompilation.ImplicitNames(model, table, page));
        Assert.Contains(cache, PublishedReadProjectionCompilation.Columns(model, table, page));
        Assert.DoesNotContain(cache, page.Writable); Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>非冗余表必须走自身固定物理/派生计划，不能被此兼容投影代替。</summary>
    [Fact]
    public void Nonredundant_read_is_not_replaced_by_legacy_entity_projection()
    {
        var catalog = NonRedundantGeneratedFixture.Catalog("projected_nonredundant");
        var model = GenerationModel.Create(catalog, "Example.Nonredundant"); var table = model.Tables.Single(table => table.Source.Id == "1");
        Assert.True(NonRedundantReadCompilation.Requires(model,table));
        Assert.Throws<InvalidOperationException>(() => PublishedReadProjectionCompilation.Columns(model, table, Assert.Single(table.Pages)));
    }
}
