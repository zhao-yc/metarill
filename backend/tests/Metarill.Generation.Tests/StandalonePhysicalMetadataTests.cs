using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>复用同一个真实普通完整包程序集，不能借可信模块JSON改变编译期物理合成边界。</summary>
public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>普通完整包仍得到原严格合成的物理图且不修改调用者对象。</summary>
    [Fact]
    public void Ordinary_compiled_physical_composition_keeps_original_strict_graph()
    {
        var files=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var expected=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(package.Metadata,files),package.Security);
        var result=(MetadataCatalog)package.Invoke(package.HostType("ApplicationSecurityRegistry").Assembly.GetType(WebAuthenticationPackageFixture.Namespace+".Generated.GeneratedRegistration",true)!,"ComposePhysicalMetadata",package.Metadata,files,package.Security)!;
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(expected,"ordinary_physical")),MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(result,"ordinary_physical")));
        Assert.Equal(package.Metadata.ConfigPages.Count,result.ConfigPages.Count);
    }
    /// <summary>相同管理目录经普通生成只能被拒绝；JSON中的页面及模块形状都不能充当编译证明。</summary>
    [Fact]
    public void Ordinary_compiled_composition_rejects_management_json_without_factory_generation()
    {
        var module=SecurityManagementModuleFactory.Compose(package.Metadata,package.Metadata.YzAdminTypes[0].Id);
        var files=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"metadata/files.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Throws<InvalidOperationException>(()=>package.Invoke(package.HostType("ApplicationSecurityRegistry").Assembly.GetType(WebAuthenticationPackageFixture.Namespace+".Generated.GeneratedRegistration",true)!,"ComposePhysicalMetadata",module.Published,files,package.Security));
    }
}
