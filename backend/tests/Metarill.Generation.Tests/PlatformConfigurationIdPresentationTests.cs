using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>存储改型不能偷偷改变原页面的控件、初值、校验、布局或样式。</summary>
public sealed class PlatformConfigurationIdPresentationTests
{
    /// <summary>同一当前模板分别消费真实存储类型和独立旧字段事实，两份详情落盘字节必须相同。</summary>
    [Fact]
    public async Task Original_detail_pages_are_byte_identical_while_backend_ids_are_int64()
    {
        var catalog=Catalog();var original=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Metarill.Platform");
        // 独立事实取自A15前提交bd800bfa969775d462dada3135950118432063be，不调用待测展示转换构造期望值。
        var legacy=JsonSerializer.Deserialize<MetadataCatalog>(original,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        (string Id,string Name,string Physical,bool HasDisplay)[] fields=[
            ("1679391146044956674","child_config_page_id","nvarchar",true),
            ("1670726646479638529","link_config_table_id","nvarchar",true),
            ("1670726646546747393","link_config_page_id","nvarchar",true),
            ("2200000000000000135","link_config_table_id","varchar",false),
            ("2200000000000000137","link_config_page_id","varchar",false),
            ("2200000000000000139","combine_super_column_id","varchar",false)];
        foreach(var field in fields)
        {
            var column=legacy.ConfigColumns.Single(column=>column.Id==field.Id);Assert.Equal(field.Name,column.ColumnName);
            Assert.Equal("int64",column.LogicalType);Assert.Equal(field.HasDisplay,column.Extra.ContainsKey("columnTypeShow"));
            column.LogicalType="string";column.ColumnType=field.Physical;column.ColumnLength="50";
            if(field.HasDisplay)column.Extra["columnTypeShow"]=JsonSerializer.SerializeToElement("字符");
        }
        var legacyBefore=JsonSerializer.Serialize(legacy);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-platform-id-presentation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"current");var expected=Path.Combine(directory,"legacy");
            new CSharpGenerator().Generate(catalog,output,"Metarill.Platform");new CSharpGenerator().Generate(legacy,expected,"Metarill.Platform");
            foreach(var page in new[]{"config_child-detail-1679390851298631682.vue","config_page_column-detail-6218036784398336.vue"})
            {
                var relative=Path.Combine("frontend/pages/yzp",page);
                Assert.Equal(File.ReadAllBytes(Path.Combine(expected,relative)),File.ReadAllBytes(Path.Combine(output,relative)));
            }
            // 未受另行批准的List分页影响的原页面仍须匹配最初整文件摘要，不能只让两个新输出互相作证。
            Assert.Equal("0034524cb455b65559fbae1a585c4685379767c0eb5dbe03a8c1ae43051222e8",
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(output,"frontend/pages/yzp/config_child-detail-1679390851298631682.vue")))));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
        Assert.Equal(original,JsonSerializer.Serialize(catalog));Assert.Equal(legacyBefore,JsonSerializer.Serialize(legacy));
        Assert.Equal("long?",model.Tables.Single(table=>table.Source.TableName=="config_child").Columns.Single(column=>column.Source.ColumnName=="child_config_page_id").Type);
    }
    /// <summary>其他项目即便声明同名字段或固定ID，也不获得平台展示转换。</summary>
    [Fact]
    public void Other_projects_keep_their_own_declared_presentation()
    {
        var catalog=Catalog();catalog.YzProjectId="9007199254740993";var model=GenerationModel.Create(catalog,"Example.Other");Assert.Same(model,PlatformConfigurationIdPresentation.ForFrontend(model));
    }
    /// <summary>读取当前发布源，不依赖旧YZP路径、数据库或生成缓存。</summary>
    private static MetadataCatalog Catalog()=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../metadata/platform.json"))),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
