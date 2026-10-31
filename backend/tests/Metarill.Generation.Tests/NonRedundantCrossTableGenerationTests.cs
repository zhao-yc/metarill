using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原DTO与页面不改写，真实编译由元数据生成的逻辑VO和稀疏写入。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossTableGenerationTests
{
    /// <summary>四种ID／父键组合生成并编译；名称无物理列，条件在初始化表达式中完整冻结。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public async Task Generated_cross_CRUD_compiles_and_keeps_original_protocol(string type,string parentType)
    {
        var catalog=Catalog("nr_cross",type,parentType);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.NonRedundantCross");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));var read=NonRedundantReadCompilation.ForPage(model,table,page);Assert.Contains(read.Outputs,output=>output.Storage=="country_name");Assert.DoesNotContain(MetadataStoragePolicy.ForTable(catalog,table.Source).PhysicalColumns,column=>column.ColumnName.EndsWith("_name",StringComparison.Ordinal));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-generated");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var service=File.ReadAllText(Path.Combine(directory,"generated/Services/NrCrossSourceService.g.cs"));Assert.Contains("CompileCross(",service);Assert.Contains("PackCross201_25(entity,submitted)",service);Assert.True(service.IndexOf("PackCross201_25(entity,submitted)",StringComparison.Ordinal)<service.IndexOf("await ValidateMutationAsync",StringComparison.Ordinal));var init=service.Split('\n').Single(line=>line.Contains("ReadPlan201="));Assert.Contains("status",init);Assert.DoesNotContain("CrossCondition",init);var hand=Path.Combine(directory,"generated/Services/NrCrossSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 生成后必须保留的手写扩展。\n");var handBytes=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);Assert.Equal(handBytes,await File.ReadAllBytesAsync(hand));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType(model.Namespace+".Generated.VOs.NrCrossSourcePage201Vo")!.GetProperty("PlaceIdDisplayText"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>用户已确认无损路径规则，但完整往返及正式事务验收未完成前仍关闭该导入完整包。</summary>
    [Fact]
    public void Full_package_remains_closed_until_lossless_path_import_acceptance()
    {
        var catalog=Catalog("nr_cross","string","int64");catalog.ConfigPages.Single(page=>page.Id=="201").IsImport=1;RedundantMultiNameCacheTests.Published(catalog);var model=GenerationModel.Create(catalog,"Example.NonRedundantCross");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);
        Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));
        var gap=Assert.Single(GenerationCapabilityAudit.Collect(model));Assert.Equal("201",gap.PageId);Assert.Equal("place_id",gap.Column);Assert.Equal("detail",gap.Usage);
        Assert.Contains("无损路径已接生成源头",gap.Reason);Assert.Contains("正式事务验收尚待完成",gap.Reason);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-full-gate");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,model.Namespace,NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>隐藏控件的固定链仍完整证明，Full可装配但不能扩大只读投影。</summary>
    [Fact]
    public async Task Hidden_proven_cross_keeps_Full_package_and_generation_without_exposing_ids()
    {
        var catalog=Catalog("hidden_nr_cross","string","int64");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");RedundantMultiNameCacheTests.Published(catalog);var model=GenerationModel.Create(catalog,"Example.HiddenCross");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));Assert.DoesNotContain(page.Readable,column=>column.Source.Id=="25");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-hidden-cross-gate");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{var full=Path.Combine(directory,"full");Assert.True(new ApplicationPackager().Create(catalog,full,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);var backend=Path.Combine(directory,"backend-only");var result=new CSharpGenerator().Generate(catalog,Path.Combine(backend,"generated"),model.Namespace);Assert.DoesNotContain(result.Warnings,warning=>warning.Contains("隐藏控件"));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(backend);var vo=assembly.GetType(model.Namespace+".Generated.VOs.HiddenNrCrossSourcePage201Vo")!;Assert.Null(vo.GetProperty("PlaceId"));Assert.Null(vo.GetProperty("CountryName"));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>新增跨表门禁不扩大到既有selfTree，隐藏的已支持控件仍可正常发布完整包。</summary>
    [Fact]
    public async Task Existing_hidden_self_tree_keeps_its_supported_full_application()
    {
        var catalog=NonRedundantSelfTreeTests.Catalog("hidden_self",automatic:true);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");RedundantMultiNameCacheTests.Published(catalog);var model=GenerationModel.Create(catalog,"Example.HiddenSelf");Assert.Empty(GenerationCapabilityAudit.Collect(model));var boundary=Path.Combine(Path.GetTempPath(),"metarill-hidden-self-full");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{var result=new ApplicationPackager().Create(catalog,output,model.Namespace,NonRedundantApplicationFixture.Root());Assert.True(result.FrontendIncluded);}finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>导出包只用自己的Core／Data源码编译，不能借工作区DLL掩盖依赖遗漏。</summary>
    [Fact]
    public async Task Backend_only_package_builds_with_its_own_runtime_sources()
    {
        var catalog=Catalog("nr_cross","int64","string");var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-backend-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new ApplicationPackager().Create(catalog,directory,"Example.NonRedundantCrossPackage",NonRedundantApplicationFixture.Root(),ApplicationPackageMode.BackendOnly);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);Assert.True(File.Exists(Path.Combine(directory,"src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.CrossTable.cs")));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>生成与真实MySQL使用同一四表目录，原选择页及字符串ID响应不变。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parentType)
    {
        var catalog=CrossTableReadTests.Catalog(prefix,parentType,parentType);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;var column=catalog.ConfigColumns.Single(column=>column.Id=="25");column.LogicalType=type;column.ColumnLength=type=="string"?"64":"";var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsCopy=1;page.Extra["type"]=JsonSerializer.SerializeToElement(2);catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);return catalog;
    }
}
