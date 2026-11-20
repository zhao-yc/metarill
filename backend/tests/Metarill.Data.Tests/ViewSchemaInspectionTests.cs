using System.Data;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class ViewSchemaInspectionTests
{
    [Fact]
    public async Task View_projection_preserves_column_order_and_has_no_table_ddl_or_constraints()
    {
        using var rows = Projection();
        rows.Rows.Add("VIEW", "id", "bigint(20)", "NO", "Customer_View");
        rows.Rows.Add("VIEW", "caption", "varchar(64)", "YES", "Customer_View");

        await using var reader = rows.CreateDataReader();
        var projection = await SchemaService.ReadViewProjectionAsync(reader);
        Assert.NotNull(projection);
        Assert.Equal("Customer_View", projection.Name);
        Assert.Equal(new[] { new ColumnSchema("id", "bigint", false), new ColumnSchema("caption", "varchar(64)", true) },
            projection.Columns);
        Assert.Null(projection.SnapshotDefinition);
        Assert.Null(projection.Indexes);
    }

    [Fact]
    public async Task Missing_view_has_no_projection()
    {
        using var rows = Projection();
        await using var reader = rows.CreateDataReader();
        Assert.Null(await SchemaService.ReadViewProjectionAsync(reader));
    }

    [Theory]
    [InlineData("BASE TABLE", "id")]
    [InlineData("VIEW", null)]
    public async Task A_wrong_object_kind_or_incomplete_projection_is_rejected(string kind, string? column)
    {
        using var rows = Projection();
        rows.Rows.Add(kind, column, "bigint", "NO", "customer_view");
        await using var reader = rows.CreateDataReader();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SchemaService.ReadViewProjectionAsync(reader));
    }

    [Fact]
    public async Task Different_physical_views_are_not_combined()
    {
        using var rows = Projection();
        rows.Rows.Add("VIEW", "id", "bigint", "NO", "customer_view");
        rows.Rows.Add("VIEW", "caption", "varchar(64)", "YES", "CUSTOMER_VIEW");

        await using var reader = rows.CreateDataReader();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SchemaService.ReadViewProjectionAsync(reader));
    }

    [Fact]
    public async Task Projection_budget_rejects_an_incomplete_read()
    {
        using var rows = Projection();
        for (var i = 0; i <= 512; i++)
            rows.Rows.Add("VIEW", "field_" + i, "varchar(32)", "YES", "customer_view");
        await using var reader = rows.CreateDataReader();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SchemaService.ReadViewProjectionAsync(reader));
    }

    private static DataTable Projection()
    {
        var rows = new DataTable();
        foreach (var name in new[] { "object_kind", "column_name", "column_type", "nullable", "physical_name" })
            rows.Columns.Add(name, typeof(string));
        return rows;
    }
}
