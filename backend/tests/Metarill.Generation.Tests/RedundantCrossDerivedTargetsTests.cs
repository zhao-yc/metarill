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
    /// <summary>显式来源R1只改变自身存储，R0目标仍不产生物理名称副本。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,string scenario)
    {
        var c=NonRedundantCrossTargetsTests.Catalog(prefix,type,parent,scenario);c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=1;return c;
    }
}
