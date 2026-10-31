using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>安全物理模型只参与显式结构组合；不连接数据库、不开放页面或准备账号。</summary>
public sealed class SecurityMetadataCompositionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SecurityTables = ["yz_project", "yz_admin_type", "yz_manager", "yz_role", "yz_role_permission", "sys_session"];

    [Fact]
    public void Derived_security_model_has_complete_platform_physical_shape_without_pages_or_configuration()
    {
        var security = Read("security"); var platform = Read("platform");
        MetadataValidation.Validate(security);
        Assert.Equal(SecurityTables.Order(StringComparer.Ordinal), security.ConfigTables.Select(table => table.TableName).Order(StringComparer.Ordinal));
        Assert.Equal(128, security.ConfigColumns.Count);
        Assert.Equal(6, security.ConfigIndexes.Count);
        Assert.Equal(10, security.ConfigIndexColumns.Count);
        Assert.Empty(security.ConfigPages); Assert.Empty(security.ConfigPageColumns);
        Assert.Empty(security.YzAdminTypes); Assert.Empty(security.ConfigMenus); Assert.Empty(security.ConfigCustomPages);
        Assert.Empty(security.ConfigButtons); Assert.Empty(security.ConfigChildren);
        Assert.All(security.ConfigTables, table => Assert.Empty(table.Extra));
        Assert.All(security.ConfigColumns, column =>
        {
            Assert.Equal(0, column.IsVirtualColumn); Assert.Empty(column.Extra);
            Assert.Empty(column.ControlKind); Assert.Empty(column.ControlDetail); Assert.Empty(column.LinkConfigTableId);
            Assert.Empty(column.DefaultValue);
        });
        // 编译真实结构计划逐表比较：列类型、主键、可空性、默认值及索引与平台物理源完全相同。
        var expected = MySqlSchemaPlanner.Compile(platform, "security_fixture").Tables.Where(table => SecurityTables.Contains(table.Name)).ToArray();
        var actual = MySqlSchemaPlanner.Compile(security, "security_fixture");
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(new DatabaseSchema("security_fixture", expected)), MySqlSchemaPlanner.Fingerprint(actual));
    }

    [Fact]
    public void Business_files_and_security_merge_without_mutating_any_input_or_exposing_security_pages()
    {
        var business = Read("demo"); var files = Read("files"); var security = Read("security");
        var before = new[] { Serialize(business), Serialize(files), Serialize(security) };
        var combined = MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(business, files), security);
        Assert.Equal(business.ConfigTables.Count + files.ConfigTables.Count + security.ConfigTables.Count, combined.ConfigTables.Count);
        Assert.Equal(business.ConfigColumns.Count + files.ConfigColumns.Count + security.ConfigColumns.Count, combined.ConfigColumns.Count);
        Assert.Equal(business.ConfigPages.Select(page => page.Id), combined.ConfigPages.Select(page => page.Id));
        Assert.Equal(10, MySqlSchemaPlanner.Compile(combined, "combined_fixture").Tables.Count);
        Assert.Equal(before, new[] { Serialize(business), Serialize(files), Serialize(security) });
    }

    [Fact]
    public void Same_name_id_and_complete_platform_physical_definitions_deduplicate_without_loosening_page_boundary()
    {
        var security = Read("security"); var platform = Read("platform");
        var tableIds = security.ConfigTables.Select(table => table.Id).ToHashSet(StringComparer.Ordinal);
        var indexIds = security.ConfigIndexes.Select(index => index.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
        var application = new MetadataCatalog
        {
            ConfigTables = platform.ConfigTables.Where(table => tableIds.Contains(table.Id)).ToList(),
            ConfigColumns = platform.ConfigColumns.Where(column => tableIds.Contains(column.ConfigTableId) && column.IsVirtualColumn == 0).ToList(),
            ConfigIndexes = platform.ConfigIndexes.Where(index => tableIds.Contains(index.GetProperty("configTableId").GetString()!)).ToList(),
            ConfigIndexColumns = platform.ConfigIndexColumns.Where(column => indexIds.Contains(column.GetProperty("configIndexId").GetString())).ToList(),
        };
        var before = Serialize(application);
        var merged = MetadataCatalogComposition.MergePhysical(application, security);
        Assert.Equal(6, merged.ConfigTables.Count); Assert.Equal(128, merged.ConfigColumns.Count);
        Assert.Equal(6, merged.ConfigIndexes.Count); Assert.Equal(10, merged.ConfigIndexColumns.Count);
        Assert.Equal(before, Serialize(application));
        application.ConfigPages.Add(new() { Id = "42", ConfigTableId = security.ConfigTables[0].Id });
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(application, security));
    }

    [Theory]
    [InlineData("table-id")]
    [InlineData("column-id")]
    [InlineData("type")]
    [InlineData("length")]
    [InlineData("nullability")]
    [InlineData("default")]
    [InlineData("missing-column")]
    [InlineData("index-id")]
    [InlineData("index-name")]
    [InlineData("index-unique")]
    [InlineData("index-column")]
    [InlineData("index-order")]
    [InlineData("index-direction")]
    public void Same_name_security_table_cannot_hide_physical_or_index_drift(string change)
    {
        var security = Read("security"); var application = Read("security");
        var manager = application.ConfigTables.Single(table => table.TableName == "yz_manager");
        var password = application.ConfigColumns.Single(column => column.ConfigTableId == manager.Id && column.ColumnName == "password");
        switch (change)
        {
            case "table-id":
                foreach (var column in application.ConfigColumns.Where(column => column.ConfigTableId == manager.Id)) column.ConfigTableId = "42";
                manager.Id = "42"; break;
            case "column-id": password.Id = "42"; break;
            case "type": password.LogicalType = "json"; break;
            case "length": password.ColumnLength = "50"; break;
            case "nullability": password.IsDbNotNull = 1 - password.IsDbNotNull; break;
            case "default": password.DefaultValue = "仅测试结构冲突"; break;
            case "missing-column": application.ConfigColumns.Remove(password); break;
            case "index-id": Replace(application.ConfigIndexes, "2200000000000000734", "id", "42"); break;
            case "index-name": Replace(application.ConfigIndexes, "2200000000000000734", "indexName", "wrong_name"); break;
            case "index-unique": Replace(application.ConfigIndexes, "2200000000000000734", "isUnique", 0); break;
            case "index-column": Replace(application.ConfigIndexColumns, "2200000000000000735", "configColumnId", password.Id); break;
            case "index-order": Replace(application.ConfigIndexColumns, "900235310800048128", "sortNo", 20); break;
            case "index-direction": Replace(application.ConfigIndexColumns, "900235310800048128", "sortDirection", "DESC"); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(application, security));
    }

    [Fact]
    public void Unrelated_business_table_cannot_borrow_security_ids_even_across_object_kinds()
    {
        var business = Read("demo"); var security = Read("security");
        business.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new
        {
            id = security.ConfigColumns[0].Id, configTableId = business.ConfigTables[0].Id,
            indexName = "collision_fixture", isUnique = 0, isEnabled = 1, indexType = "INDEX",
        }));
        Assert.Throws<InvalidOperationException>(() => MetadataCatalogComposition.MergePhysical(business, security));
    }

    /// <summary>只替换公开物理元数据中的一个属性，真实调用现有合成规则。</summary>
    private static void Replace(List<JsonElement> records, string id, string key, object value)
    {
        var index = records.FindIndex(record => record.GetProperty("id").GetString() == id);
        var fields = records[index].EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        fields[key] = JsonSerializer.SerializeToElement(value); records[index] = JsonSerializer.SerializeToElement(fields);
    }

    /// <summary>测试路径由源码位置定位，只读当前检出目录中的公开模型，不使用运行环境连接配置。</summary>
    private static MetadataCatalog Read(string name, [CallerFilePath] string source = "") => JsonSerializer.Deserialize<MetadataCatalog>(
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "metadata", name + ".json"))), JsonOptions)!;
    private static string Serialize(MetadataCatalog catalog) => JsonSerializer.Serialize(catalog, JsonOptions);
}
