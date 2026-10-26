using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享R0整组写入必须实际编译原生成Service，不能以成员打包冒充接线。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedCrossTableSparseGenerationTests
{
    /// <summary>四种键、短主叶与固定派生目标实际生成编译，手写partial在重复生成后保持。</summary>
    [Theory][InlineData("string","string",false,false)][InlineData("string","int64",false,false)][InlineData("int64","string",false,false)][InlineData("int64","int64",false,false)][InlineData("string","int64",true,false)][InlineData("int64","string",true,false)][InlineData("string","int64",false,true)]
    public async Task Generated_shared_R0_services_compile_with_one_group_state(string type,string parent,bool shorter,bool derived)
    {
        var c=Catalog(type,parent,shorter,derived);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.SharedSparse");var table=model.Tables.Single(t=>t.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.Single(SharedCrossTableCompilation.ForPage(model,table,page).Groups);Assert.Equal(2,CrossTableWriteCompilation.ForService(model,table,page).Count);Assert.Empty(GenerationCapabilityAudit.Collect(model));var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-sparse-generation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var source=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/SharedCrossSourceService.g.cs"));Assert.Contains("PackSharedCross201_25(entity,submitted,sharedCrossMutations",source);Assert.Contains("VerifySharedCross201_25Async",source);Assert.Contains("state.VerifyStoredAsync",source);Assert.DoesNotContain("PackCross201_25(entity,submitted)",source);Assert.DoesNotContain("PackCross201_80(entity,submitted)",source);var hand=Path.Combine(directory,"generated/Services/SharedCrossSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 共享R0手写扩展不得覆盖。\n");var bytes=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));Assert.Equal(before,JsonSerializer.Serialize(c));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>新共享R0完整包及更新必须使用原组件和页面事件，实际Vue挂载不代替浏览器。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task Full_shared_R0_builds_updates_and_mounts_original_vue(bool shorter,bool derived)
    {
        var c=Catalog("string","int64",shorter,derived);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-sparse-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try{new ApplicationPackager().Create(c,output,"Example.SharedSparseFull",root);var hand=Path.Combine(output,"backend/generated/Services/SharedCrossSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 共享R0完整包手写扩展。\n");var saved=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.SharedSparseFull",root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(saved,await File.ReadAllBytesAsync(hand));Assert.Equal("unchanged",updater.Apply(updater.Preview(output,candidate,actions),updater.Preview(output,candidate,actions).PlanSha256).Status);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossTableVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>歧义存储、页覆盖、来源父／精确范围及隐藏坏同伴不能提前放开。</summary>
    [Theory][InlineData("default")][InlineData("overlay")][InlineData("parent")][InlineData("exact")][InlineData("badPeer")]
    public void Invalid_shared_R0_keeps_all_members_closed(string kind)
    {
        var c=Catalog("string","int64",false,false);if(kind=="default")c.ConfigColumns.Single(x=>x.Id=="80").DefaultValue="1";if(kind=="overlay"){var column=c.ConfigColumns.Single(x=>x.Id=="80");var json=JsonNode.Parse(column.ControlDetail)!;json["displayJoiner"]=" + ";c.ConfigPageColumns.Single(x=>x.ConfigPageId=="201"&&x.ConfigColumnId=="80").Extra["controlDetail"]=JsonSerializer.SerializeToElement(json.ToJsonString());}if(kind=="parent")c.ConfigPages.Single(x=>x.Id=="201").WhereSql+=" AND country_id=@parentId";if(kind=="exact")c.ConfigPages.Single(x=>x.Id=="201").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");if(kind=="badPeer"){var col=c.ConfigColumns.Single(x=>x.Id=="80");var json=JsonNode.Parse(col.ControlDetail)!;json["levels"]![2]!["nameField"]="missing";col.ControlDetail=json.ToJsonString();}var model=GenerationModel.Create(c,"Example.InvalidSharedSparse");var table=model.Tables.Single(x=>x.Source.Id=="2");Assert.Empty(CrossTableWriteCompilation.ForService(model,table,Assert.Single(table.Pages)));var boundary=Path.Combine(Path.GetTempPath(),"metarill-invalid-shared-sparse");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,model.Namespace,NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>沿用同一个原共享控件模型，仅将来源改为非冗余，不重写原前端。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string type,string parent,bool shorter=false,bool derived=false,string prefix="shared_cross")
    {
        var c=SharedCrossTableGenerationTests.Catalog(prefix,type,parent,shorter,derived);c.ConfigTables.Single(t=>t.Id=="2").IsRongyu=0;return c;
    }
}
