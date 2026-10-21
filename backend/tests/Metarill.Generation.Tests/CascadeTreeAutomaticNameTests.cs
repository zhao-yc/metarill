using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>从未显式配置名称列的原级联元数据实际生成，验证物理定义与强类型程序集一致。</summary>
public sealed class CascadeTreeAutomaticNameTests
{
    /// <summary>四种来源/父键组合均生成自动物理名称，严格DTO不接受它，元数据与手写扩展不改写。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Generated_entity_and_implicit_response_share_schema_name_without_metadata_record(string sourceType,string parentType)
    {
        var catalog=CascadeTreeWriteTests.Catalog("automatic",sourceType,parentType);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="26");catalog.ConfigColumns.RemoveAll(column=>column.Id=="26");if(sourceType=="string")catalog.ConfigColumns.Single(column=>column.Id=="25").ColumnLength="90";
        var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.AutomaticCascade");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var name=table.Columns.Single(column=>column.Source.ColumnName=="tree_name");
        Assert.True(name.IsManagedCascadeName);Assert.Equal(name.Source.Id,MetadataStoragePolicy.ForTable(catalog,table.Source).PhysicalColumns.Single(column=>column.ColumnName=="tree_name").Id);Assert.Equal(sourceType=="string"?"90":"50",name.Source.ColumnLength);Assert.DoesNotContain(page.Writable,column=>column.Source.Id==name.Source.Id);Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-automatic");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.AutomaticCascade");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var entity=assembly.GetType("Example.AutomaticCascade.Generated.Entities.AutomaticSource",true)!;Assert.Equal(typeof(string),entity.GetProperty("TreeName")!.PropertyType);
            Assert.NotNull(assembly.GetType("Example.AutomaticCascade.Generated.VOs.AutomaticSourcePage201Vo",true)!.GetProperty("TreeName"));var request=assembly.GetType("Example.AutomaticCascade.Generated.DTOs.AutomaticSourcePage201CreateRequest",true)!;Assert.Null(request.GetProperty("TreeName"));Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"treeName\":\"伪造\"}",request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var hand=Path.Combine(directory,"generated/Services/AutomaticSourceService.cs");var saved=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.AutomaticCascade");Assert.Equal(saved,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
