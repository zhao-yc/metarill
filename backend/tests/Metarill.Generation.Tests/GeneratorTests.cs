using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>验证再生成保护、批次原子性和范围条件，避免模板回归造成越权或覆盖。</summary>
public sealed class GeneratorTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "metarill-generation-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void Repeated_generation_is_deterministic_and_preserves_custom_partial()
    {
        var generator = new CSharpGenerator();
        var metadata = Catalog();
        var first = generator.Generate(metadata, directory, "Example.App");
        var original = first.GeneratedFiles.ToDictionary(x => x, x => File.ReadAllText(Path.Combine(directory, x)));
        var handwritten = Path.Combine(directory, "Services/CustomerService.cs");
        File.AppendAllText(handwritten, "// 手写业务规则应被保留。\n");
        var custom = File.ReadAllText(handwritten);
        metadata.ConfigColumns.Reverse();
        var second = generator.Generate(metadata, directory, "Example.App");
        Assert.Equal(first.MetadataSha256, second.MetadataSha256);
        Assert.Empty(second.CreatedHandwrittenFiles);
        Assert.Equal(custom, File.ReadAllText(handwritten));
        foreach (var (path, content) in original) Assert.Equal(content, File.ReadAllText(Path.Combine(directory, path)));
    }

    [Fact]
    public void Unowned_late_conflict_prevents_entire_batch()
    {
        Directory.CreateDirectory(Path.Combine(directory, "Controllers"));
        var conflict = Path.Combine(directory, "Controllers/CustomerPage101Controller.g.cs");
        File.WriteAllText(conflict, "用户文件");
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(Catalog(), directory, "Example.App"));
        Assert.Equal("用户文件", File.ReadAllText(conflict));
        Assert.False(File.Exists(Path.Combine(directory, "Entities/Customer.g.cs")));
    }

    [Fact]
    public void Modified_generated_file_blocks_regeneration_and_stale_deletion()
    {
        var generator = new CSharpGenerator();
        var catalog = Catalog();
        generator.Generate(catalog, directory, "Example.App");
        var entity = Path.Combine(directory, "Entities/Customer.g.cs");
        File.AppendAllText(entity, "// 未经确认的手改\n");
        var manifest = File.ReadAllText(Path.Combine(directory, ".metarill-generation.json"));
        Assert.Throws<InvalidOperationException>(() => generator.Generate(catalog, directory, "Example.App"));
        Assert.Equal(manifest, File.ReadAllText(Path.Combine(directory, ".metarill-generation.json")));
        Assert.EndsWith("// 未经确认的手改\n", File.ReadAllText(entity));
    }

    [Fact]
    public void Traversing_manifest_path_is_rejected_before_any_write()
    {
        var generator = new CSharpGenerator();
        generator.Generate(Catalog(), directory, "Example.App");
        var manifest = Path.Combine(directory, ".metarill-generation.json");
        var json = JsonNode.Parse(File.ReadAllText(manifest))!;
        json["files"]![0]!["path"] = "../outside.cs";
        File.WriteAllText(manifest, json.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => generator.Generate(Catalog(), directory, "Example.App"));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(directory)!, "outside.cs")));
    }

    [Fact]
    public void Unknown_where_scope_never_silently_generates_unrestricted_queries()
    {
        var metadata = Catalog();
        metadata.ConfigPages[0].WhereSql = "owner_id = untrusted_function()";
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(metadata, directory, "Example.App"));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Cross_table_page_field_is_rejected()
    {
        var metadata = Catalog();
        metadata.ConfigTables.Add(new TableMetadata { Id = "2", TableName = "other" });
        metadata.ConfigColumns.Add(new ColumnMetadata { Id = "22", ConfigTableId = "2", ColumnName = "id", LogicalType = "id" });
        metadata.ConfigPageColumns[0].ConfigColumnId = "22";
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(metadata, directory, "Example.App"));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Changed_project_cannot_claim_existing_output()
    {
        var generator = new CSharpGenerator();
        generator.Generate(Catalog(), directory, "Example.App");
        var metadata = Catalog(); metadata.YzProjectId = "999";
        Assert.Throws<InvalidOperationException>(() => generator.Generate(metadata, directory, "Example.App"));
    }

    [Fact]
    public void Removing_metadata_removes_owned_generated_files_only()
    {
        var generator = new CSharpGenerator();
        var metadata = Catalog();
        generator.Generate(metadata, directory, "Example.App");
        metadata.ConfigPages.Clear(); metadata.ConfigPageColumns.Clear();
        generator.Generate(metadata, directory, "Example.App");
        Assert.False(File.Exists(Path.Combine(directory, "Controllers/CustomerPage101Controller.g.cs")));
        Assert.True(File.Exists(Path.Combine(directory, "Services/CustomerService.cs")));
    }

    [Fact]
    public void String_primary_key_is_rejected_instead_of_generating_mismatched_mapping()
    {
        var metadata = Catalog(); metadata.ConfigColumns[0].LogicalType = "string";
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(metadata, directory, "Example.App"));
        Assert.False(Directory.Exists(directory));
    }
    [Fact]
    public void Write_schema_never_allows_readonly_or_system_fields()
    {
        var metadata = Catalog(); metadata.ConfigPageColumns[0].Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
        new CSharpGenerator().Generate(metadata, directory, "Example.App");
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "frontend/common/yzp_runtime/page-write-schema.generated.json")));
        var page = schema.RootElement.GetProperty("pages").GetProperty("101");
        Assert.Equal("/admin/auto/customer/101", page.GetProperty("route").GetString());
        Assert.Equal(["remark"], page.GetProperty("createFields").EnumerateArray().Select(field => field.GetString()).ToArray());
        Assert.Equal(["id", "remark"], page.GetProperty("updateFields").EnumerateArray().Select(field => field.GetString()).ToArray());
    }
    internal static MetadataCatalog Catalog() => new()
    {
        YzProjectId = "100",
        ConfigTables = [new() { Id = "1", TableName = "customer", Name = "客户" }],
        ConfigColumns = [
            new() { Id = "11", ConfigTableId = "1", ColumnName = "id", LogicalType = "id", IsDbNotNull = 1 },
            new() { Id = "12", ConfigTableId = "1", ColumnName = "name", LogicalType = "string", Name = "名称", IsDbNotNull = 1 },
            new() { Id = "13", ConfigTableId = "1", ColumnName = "remark", LogicalType = "string", Name = "备注" },
            new() { Id = "14", ConfigTableId = "1", ColumnName = "delete_no", LogicalType = "datetime" },
            new() { Id = "15", ConfigTableId = "1", ColumnName = "yz_project_id", LogicalType = "id" }
        ],
        ConfigPages = [new() { Id = "101", ConfigTableId = "1", Name = "客户管理", WhereSql = "yz_project_id=@current_yz_project_id" }],
        ConfigPageColumns = [
            new() { Id = "111", ConfigPageId = "101", ConfigColumnId = "12", IsEdit = 1, IsShow = 1, Extra = new() { ["isSearch"] = JsonSerializer.SerializeToElement(1), ["isSort"] = JsonSerializer.SerializeToElement(1) } },
            new() { Id = "112", ConfigPageId = "101", ConfigColumnId = "13", IsEdit = 1, IsShow = 1 }
        ]
    };
}
