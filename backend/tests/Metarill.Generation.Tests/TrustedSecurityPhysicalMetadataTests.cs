using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行实际编译的可信物理方法，普通合成与完整包门槛保持独立。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class TrustedSecurityPhysicalMetadataTests(TrustedSecurityServiceFixture fixture):IClassFixture<TrustedSecurityServiceFixture>
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    /// <summary>固定三页只从结构副本移除；业务页和发布原对象不变，结构指纹与工厂相同。</summary>
    [Fact]
    public void Compiled_trusted_physical_model_keeps_original_catalog_and_factory_schema()
    {
        var catalog=fixture.Models.Published;var before=JsonSerializer.Serialize(catalog,Json);var result=Compose(catalog,Read("files"),Read("security"));
        Assert.Equal(before,JsonSerializer.Serialize(catalog,Json));Assert.Equal(fixture.Models.Physical.ConfigPages.Select(page=>page.Id),result.ConfigPages.Select(page=>page.Id));
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(fixture.Models.Physical,"physical_probe")),MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(result,"physical_probe")));
        Assert.Equal(10,result.ConfigTables.Count);Assert.Equal(catalog.ConfigPages.Count-3,result.ConfigPages.Count);
        // 原严格合成仍拒绝相同的管理JSON，不因新宿主接线全局放宽。
        Assert.Throws<InvalidOperationException>(()=>MetadataCatalogComposition.MergePhysical(catalog,Read("security")));
        Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,Path.Combine(fixture.DirectoryPath,"still-gated"),"Example.Rejected",Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"..")));
    }
    /// <summary>即使只改布局或导航也不接受旧编译证明，不能仅检查物理列来放行另一目录。</summary>
    [Theory]
    [InlineData("project")][InlineData("table")][InlineData("column")][InlineData("page")][InlineData("field")][InlineData("menu")][InlineData("custom")]
    public void Modified_published_snapshot_rejects_before_schema(string change)
    {
        var catalog=fixture.Models.Published;
        if(change=="project")catalog.YzProjectName+="changed";if(change=="table")catalog.ConfigTables[0].TableName+="changed";if(change=="column")catalog.ConfigColumns[0].ColumnName+="changed";if(change=="page")catalog.ConfigPages[0].Name+="changed";
        if(change=="field")catalog.ConfigPageColumns.Clear();if(change=="menu")catalog.ConfigMenus.Clear();if(change=="custom")catalog.ConfigCustomPages.Clear();
        var error=Assert.Throws<InvalidOperationException>(()=>Compose(catalog,Read("files"),Read("security")));Assert.Contains("编译快照",error.Message);
    }
    /// <summary>已签发页面仍不能覆盖运行时文件/安全基础物理结构。</summary>
    [Theory][InlineData("files")][InlineData("security")]
    public void Changed_infrastructure_is_still_rejected_by_strict_composition(string which)
    {var files=Read("files");var security=Read("security");(which=="files"?files:security).ConfigColumns[0].ColumnName+="_foreign";Assert.Throws<InvalidOperationException>(()=>Compose(fixture.Models.Published,files,security));}
    /// <summary>运行时基础文件不能向已签发十表图追加第十一张表；校验仅在内存，不执行DDL。</summary>
    [Theory][InlineData("files")][InlineData("security")]
    public void Extra_infrastructure_object_cannot_expand_signed_physical_scope(string which)
    {
        var files=Read("files");var security=Read("security");var target=which=="files"?files:security;
        target.ConfigTables.Add(new(){Id="9911223344556677",TableName="mt_runtime_unexpected",YzProjectId=target.YzProjectId});
        target.ConfigColumns.Add(new(){Id="9911223344556678",ConfigTableId="9911223344556677",ColumnName="id",LogicalType="int64",IsDbNotNull=1,YzProjectId=target.YzProjectId});
        var error=Assert.Throws<InvalidOperationException>(()=>Compose(fixture.Models.Published,files,security));Assert.Contains("超出可信管理编译目录",error.Message);
    }
    /// <summary>只跨程序集调用实际生成方法，不实现另一套合成逻辑。</summary>
    private MetadataCatalog Compose(MetadataCatalog catalog,MetadataCatalog files,MetadataCatalog security)=>(MetadataCatalog)TrustedSecurityServiceFixture.Invoke(fixture.Type("Generated.GeneratedRegistration"),"ComposePhysicalMetadata",catalog,files,security)!;
    /// <summary>只读当前clone的公开元数据，不接受私有配置路径。</summary>
    private static MetadataCatalog Read(string name)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"metadata",name+".json")),Json)!;
}
