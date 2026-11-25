using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;
/// <summary>R1各层物理缓存到R0单值目标由同一生成器完成，保留原前端组件。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantCrossDerivedTargetsTests
{
    /// <summary>四种ID／父键，物理、全派生、混合和重复目标形成完整包；原元数据不改。</summary>
    [Theory][InlineData("string","string","physical")][InlineData("string","int64","derived")][InlineData("int64","string","mixed")][InlineData("int64","int64","derived")][InlineData("string","int64","repeated")]
    public async Task Complete_application_builds_with_direct_and_mixed_cache_closures(string type,string parent,string scenario)
    {
        var c=Catalog("rr_derived",type,parent,scenario);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.RedundantDerived");var direct=RelationPropagationCompilation.Bindings(model);var derived=RelationPropagationCompilation.DerivedBindings(model);Assert.Equal(3,direct.Count+derived.Count);Assert.Equal(scenario=="physical"?0:scenario=="mixed"?1:3,derived.Count);var authority=MixedRelationPropagationCompilation.ForModel(model);foreach(var binding in derived)Assert.Contains(binding.Cache.Source.ColumnName,authority.MixedSourceColumns[binding.Source.Source.TableName]);Assert.Empty(GenerationCapabilityAudit.Collect(model));var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var root=NonRedundantApplicationFixture.Root();
        try{new ApplicationPackager().Create(c,directory,model.Namespace,root);foreach(var op in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,op);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(directory,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));using var caps=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/generation-capabilities.json")));var cap=Assert.Single(caps.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),x=>x.GetProperty("controlKind").GetString()=="CascadeTree");Assert.True(cap.GetProperty("renamePropagationSupported").GetBoolean());Assert.True(cap.GetProperty("fullApplicationAccepted").GetBoolean());Assert.Equal(before,JsonSerializer.Serialize(c));}finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>冗余来源按已发布文本业务键读取非冗余目标，名称缓存保留物理传播和派生依赖。</summary>
    [Theory]
    [InlineData("derived","country_id")]
    [InlineData("mixed","area_id")]
    [InlineData("physical","place_id")]
    public async Task Project_text_keys_resolve_R0_targets_and_generate_cache_consumers(string scenario,string parentColumn)
    {
        const string ns="Example.ProjectTextCache";
        var catalog=Catalog("project_text_cache","string","string",scenario);
        var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
        CrossTableExcelLosslessGenerationTests.AddBusinessKeys(catalog,control,primaryLevel:1);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.ColumnName=="business_key"))
        {
            column.LogicalType="string";
            column.ColumnLength="50";
        }
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");
        page.WhereSql+=$" AND {parentColumn}=@parent_id";
        page.IsNew=page.IsSave=page.IsImport=page.IsExport=1;
        page.Extra.Remove("type");
        var pathField=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==control.Id);
        pathField.IsShow=pathField.IsEdit=1;
        var nameField=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId=="22");
        nameField.IsShow=nameField.IsEdit=1;
        page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(parentColumn=="place_id"?"edit":"list");

        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var actualPage=Assert.Single(source.Pages);
        var resolved=CrossTableReadCompilation.Resolve(model,source,actualPage,source.Columns.Single(column=>column.Source.Id=="25"));
        Assert.True(resolved.Definition is not null,resolved.Reason);
        Assert.True(resolved.Definition!.Levels[0].TextKey);
        Assert.True(resolved.Definition.Levels[2].TextKey);
        var definitions=RelationPropagationCompilation.Bindings(model).Concat(RelationPropagationCompilation.DerivedBindings(model))
            .Where(binding=>binding.Source.Source.Id=="2").ToArray();
        Assert.Equal(3,definitions.Length);
        Assert.Equal(2,definitions.Count(binding=>binding.TargetKey=="business_key"));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        if(scenario=="derived")Assert.Contains("business_key",maintenance.MixedTargetKeys("project_text_cache_country"));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-project-text-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ProjectTextCacheSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("CrossPath201_25Async"));
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            var published=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(published.TypeHandle);
            if(scenario=="derived")
            {
                var package=Path.Combine(directory,"package");
                var root=NonRedundantApplicationFixture.Root();
                new ApplicationPackager().Create(catalog,package,ns,root);
                foreach(var operation in new[]{"restore","build"})
                    await NonRedundantApplicationFixture.BuildAsync(package,operation);
                Assert.Equal(
                    File.ReadAllBytes(Path.Combine(root,"frontend","metarill_uni_admin","components","yzp-cascade-tree","yzp-cascade-tree.vue")),
                    File.ReadAllBytes(Path.Combine(package,"frontend","components","yzp-cascade-tree","yzp-cascade-tree.vue")));
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>显式来源R1只改变自身存储，R0目标仍不产生物理名称副本。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,string scenario)
    {
        var c=NonRedundantCrossTargetsTests.Catalog(prefix,type,parent,scenario);c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;return c;
    }
}
