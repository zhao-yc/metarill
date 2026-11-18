using Metarill.Core.Metadata;
using Metarill.Runtime.Files;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>锁定父表之前先证明严格等值子集和发布定义，不以SQL字符串猜测任意表。</summary>
public sealed class FileReferenceScopeTests
{
    [Theory]
    [InlineData("yz_project_id=@current_yz_project_id AND parent_id=@parent_id")]
    [InlineData("`yz_project_id` = @current_yz_project_id and `parent_id` = @parentId")]
    public void Parent_scope_binds_declared_target_and_preserves_long_string_types(string sql)
    {
        var catalog = Catalog(sql);
        var scope = Assert.Single(FileReferenceScope.Compile(catalog, catalog.ConfigTables[0], catalog.ConfigPages[0]));
        Assert.Equal("parent", scope.Parent.TableName); Assert.True(scope.HasSoftDelete);
        Assert.Equal(9007199254740993L, FileReferenceScope.PositiveId("9007199254740993"));
        var project = Assert.IsType<ColumnMetadata>(scope.Project);
        project.LogicalType = "string";
        Assert.Equal("9007199254740993", FileReferenceScope.IdentityValue(project, 9007199254740993L));
    }

    [Theory]
    [InlineData("parent_id=@current_yz_project_id")]
    [InlineData("yz_project_id=@parentId")]
    [InlineData("missing_id=@parentId")]
    [InlineData("parent_id=@parentId OR 1=1")]
    [InlineData("parent_id=@parentId;DELETE FROM parent")]
    [InlineData("parent_id IN (@parentId)")]
    [InlineData("parent_id=@other")]
    public void Unknown_or_complex_scope_is_rejected_without_a_query(string sql)
    {
        var catalog = Catalog(sql);
        Assert.Throws<NotSupportedException>(() => FileReferenceScope.Compile(catalog, catalog.ConfigTables[0], catalog.ConfigPages[0]));
    }

    [Fact]
    public void Missing_parent_project_virtual_parent_and_non_identity_key_stay_unsupported()
    {
        foreach (var defect in new[] { "target", "project", "virtual", "type" })
        {
            var catalog = Catalog("parent_id=@parentId");
            if (defect == "target") catalog.ConfigColumns.Single(column => column.Id == "13").LinkConfigTableId = "missing";
            if (defect == "project") catalog.ConfigColumns.RemoveAll(column => column.Id == "22");
            if (defect == "virtual") catalog.ConfigColumns.Single(column => column.Id == "13").IsVirtualColumn = 1;
            if (defect == "type") catalog.ConfigColumns.Single(column => column.Id == "13").LogicalType = "json";
            Assert.Throws<NotSupportedException>(() => FileReferenceScope.Compile(catalog, catalog.ConfigTables[0], catalog.ConfigPages[0]));
        }
    }

    /// <summary>逻辑叶名用于附件记录范围，页面覆盖改变真实目标定义时拒绝。</summary>
    [Fact]
    public void File_scope_preserves_the_fixed_self_tree_condition()
    {
        var catalog = Catalog("yz_project_id=@current_yz_project_id AND tree_name LIKE '可见%'");
        var source = catalog.ConfigTables[0];
        var page = catalog.ConfigPages[0];
        source.IsRongyu = 0;
        catalog.ConfigTables[1].IsRongyu = 1;
        foreach (var identity in catalog.ConfigColumns.Where(column => column.ColumnName == "id"))
            identity.IsDbNotNull = 1;
        catalog.ConfigColumns.AddRange([
            new() { Id = "14", ConfigTableId = "1", ColumnName = "tree_id", LogicalType = "int64", ControlKind = "CascadeTree", LinkConfigTableId = "2",
                ControlDetail = """{"sourceMode":"selfTree","selfTree":{"table":"parent","parentField":"parent_id","nameField":"name"}}""" },
            new() { Id = "24", ConfigTableId = "2", ColumnName = "parent_id", LogicalType = "int64", LinkConfigTableId = "2" },
            new() { Id = "25", ConfigTableId = "2", ColumnName = "name", LogicalType = "string" }
        ]);
        catalog.ConfigPages.Add(new() { Id = "4", ConfigTableId = "2" });
        catalog.ConfigPageColumns.AddRange([
            new() { Id = "41", ConfigPageId = "4", ConfigColumnId = "24" },
            new() { Id = "42", ConfigPageId = "4", ConfigColumnId = "25" }
        ]);
        var scope = FileReferenceScope.CompilePage(catalog, source, page);
        Assert.Equal("tree_name", Assert.Single(scope.ConditionFields));
        Assert.NotNull(scope.Condition);
        Assert.NotNull(scope.LogicalConditionPlan);
        Assert.Equal("id", Assert.Single(scope.LogicalConditionPlan!.OutputTypes.Keys));

        var overlay = new PageColumnMetadata { Id = "43", ConfigPageId = page.Id, ConfigColumnId = "14" };
        overlay.Extra["controlDetail"] = System.Text.Json.JsonSerializer.SerializeToElement(
            """{"sourceMode":"selfTree","selfTree":{"table":"parent","parentField":"parent_id","nameField":"delete_no"}}""");
        catalog.ConfigPageColumns.Add(overlay);
        Assert.Throws<NotSupportedException>(() => FileReferenceScope.CompilePage(catalog, source, page));
    }

    /// <summary>构造只含实体身份和明确外键的发布定义，测试不连接数据库。</summary>
    private static MetadataCatalog Catalog(string sql) => new()
    {
        YzProjectId = "1",
        ConfigTables = [new() { Id = "1", TableName = "child" }, new() { Id = "2", TableName = "parent" }],
        ConfigPages = [new() { Id = "3", ConfigTableId = "1", WhereSql = sql }],
        ConfigColumns =
        [
            new() { Id = "11", ConfigTableId = "1", ColumnName = "id", LogicalType = "int64" },
            new() { Id = "12", ConfigTableId = "1", ColumnName = "yz_project_id", LogicalType = "int64" },
            new() { Id = "13", ConfigTableId = "1", ColumnName = "parent_id", LogicalType = "int64", LinkConfigTableId = "2" },
            new() { Id = "21", ConfigTableId = "2", ColumnName = "id", LogicalType = "int64" },
            new() { Id = "22", ConfigTableId = "2", ColumnName = "yz_project_id", LogicalType = "int64" },
            new() { Id = "23", ConfigTableId = "2", ColumnName = "delete_no", LogicalType = "datetime" }
        ]
    };
}
