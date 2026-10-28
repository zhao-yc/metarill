using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>存储改型不能偷偷改变原页面的控件、初值、校验、布局或样式。</summary>
public sealed class PlatformConfigurationIdPresentationTests
{
    /// <summary>两份受影响原详情逐字保持批准前快照，展示转换不得修改真实元数据或后端模型。</summary>
    [Fact]
    public async Task Original_detail_pages_are_byte_identical_while_backend_ids_are_int64()
    {
        var catalog=Catalog();var original=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Metarill.Platform");
        var expected=new Dictionary<string,string>{
            ["frontend/pages/yzp/config_child-detail-1679390851298631682.vue"]="0034524cb455b65559fbae1a585c4685379767c0eb5dbe03a8c1ae43051222e8",
            ["frontend/pages/yzp/config_page_column-detail-6218036784398336.vue"]="0fb5062723e3a19d73f10d3718566425b61f828e23ef7bedc7476a63a82827be"};
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-platform-id-presentation");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            // 比较正式生成落盘字节，不能把模板展开的混合行尾误当作最终交付文件。
            new CSharpGenerator().Generate(catalog,output,"Metarill.Platform");
            foreach(var page in expected)Assert.Equal(page.Value,Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(output,page.Key)))));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
        Assert.Equal(original,JsonSerializer.Serialize(catalog));Assert.Equal("int64",catalog.ConfigColumns.Single(column=>column.Id=="1679391146044956674").LogicalType);
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
