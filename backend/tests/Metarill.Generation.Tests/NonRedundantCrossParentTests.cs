using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>固定逻辑父范围从元数据生成，原请求、隐式层位及物理守卫边界一起编译。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossParentTests
{
    /// <summary>四种ID／父键组合的根、中、末级及双控件父范围均实际编译生成服务。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public async Task Logical_parents_compile_original_mutations_import_and_private_storage_proofs(string type,string parentType)
    {
        var catalog=Catalog("nr_parent",type,parentType);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossParent");Assert.NotEmpty(GenerationCapabilityAudit.Collect(model));foreach(var table in model.Tables)foreach(var page in table.Pages)if(table.Source.IsRongyu==0)_=NonRedundantReadCompilation.ForPage(model,table,page);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-parent-compile");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(output,"generated"),model.Namespace);var service=File.ReadAllText(Path.Combine(output,"generated/Services/NrParentSourceService.g.cs"));
            Assert.Contains("LogicalParentScope202",service);Assert.Contains("includeManaged:false",service);Assert.Contains("locking:true",service);Assert.Contains("VerifyLogicalParent202Async",service);
            var preparation=service[service.IndexOf("private IReadOnlyDictionary<string, object?> MutationScopeValues(",StringComparison.Ordinal)..service.IndexOf("private static readonly",service.IndexOf("private IReadOnlyDictionary<string, object?> MutationScopeValues(",StringComparison.Ordinal),StringComparison.Ordinal)];
            Assert.DoesNotContain("[\"country_id\"]",preparation);Assert.DoesNotContain("[\"area_id\"]",preparation);Assert.DoesNotContain("[\"place_id\"]",preparation);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(output);var generated=assembly.GetType(model.Namespace+".Generated.Services.NrParentSourceService")!;
            foreach(var page in new[]{"202","203","204","205"})foreach(var action in new[]{"Create","Update","Copy","Delete","Import"})Assert.NotNull(generated.GetMethods().SingleOrDefault(method=>method.Name==action+page+"Async"));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>BackendOnly包携带真实运行时，Full仍按未完成的前端／非空路径边界拒绝。</summary>
    [Fact]
    public async Task Backend_package_builds_parent_mutations_with_its_own_runtime()
    {
        var catalog=Catalog("nr_parent_pkg","int64","string",true);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-parent-package");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new ApplicationPackager().Create(catalog,output,"Example.CrossParentPackage",NonRedundantApplicationFixture.Root(),ApplicationPackageMode.BackendOnly);foreach(var action in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,action);Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output+"-full","Example.CrossParentFull",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output+"-full"));}
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>原201页之外增加原主子页面父条件，页面字段与交互元数据保持原副本。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parentType,bool explicitAncestor=false,bool physicalDelete=false)
    {
        var catalog=NonRedundantCrossTableExcelTests.Catalog(prefix,type,parentType,explicitAncestor);var original=catalog.ConfigPages.Single(page=>page.Id=="201");original.IsNew=1;original.IsSave=1;original.IsCopy=1;original.IsDelete=1;
        foreach(var (id,condition) in new[]{("202","country_id=@parent_id"),("203","area_id=@parent_id"),("204","place_id=@parent_id"),("205","country_id=@parent_id AND secondary_country_id=@parent_id")})
        {
            var copy=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;copy.Id=id;copy.WhereSql+=" AND "+condition;catalog.ConfigPages.Add(copy);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id=id+field.Id;clone.ConfigPageId=id;catalog.ConfigPageColumns.Add(clone);}
        }
        if(physicalDelete){var deleted=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="delete_no");catalog.ConfigColumns.Remove(deleted);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==deleted.Id);}return catalog;
    }
}
