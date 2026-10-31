using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Testing;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享/短级联使用同一全局缓存名称，不能让去重丢失页图或修改原前端清空规则。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalSharedCachedTargetGenerationTests
{
    /// <summary>R0/R1和四种键/父键构成八模型，两个控件共同拥有缓存地区和完整原Excel开关。</summary>
    [Theory][InlineData(0,"string","string",false)][InlineData(1,"string","string",false)][InlineData(0,"string","int64",true)][InlineData(1,"string","int64",true)]
    [InlineData(0,"int64","string",false)][InlineData(1,"int64","string",false)][InlineData(0,"int64","int64",true)][InlineData(1,"int64","int64",true)]
    public void Shared_cached_models_preserve_complete_group_and_deduplicated_page_proof(int mode,string key,string parent,bool shorter)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(mode,key,parent,shorter);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalSharedCachedTarget");var proof=CrossTableExcelApplicationPreparer.RequireCurrent(c,model.Namespace);Assert.NotEmpty(proof.Gaps);var table=model.Tables.Single(x=>x.Source.Id=="3");var page=table.Pages.Single(x=>x.Source.Id=="30");var group=Assert.Single(SharedCrossTableCompilation.ForPage(model,table,page).Groups);Assert.Equal(2,group.Plans.Count);Assert.Empty(SharedCrossTableCompilation.ForPage(model,table,page).Gaps);Assert.Equal(2,CrossTableWriteCompilation.ForService(model,table,page).Count);var bindings=RelationPropagationCompilation.Bindings(model).Where(x=>x.Source.Source.Id=="3").ToArray();Assert.Equal(mode==1?(shorter?3:4):0,bindings.Length);if(mode==1){var area=Assert.Single(bindings,x=>x.Value.Source.Id=="114");Assert.Equal("10",area.CrossTablePageId);Assert.Equal("10",Assert.Single(area.CrossTableGlobalPages!).Value);}Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>长R0/短R1使用不可发布原宿主准备：实际编译、原组件联动、原开关和公开拒绝均保留。</summary>
    [Theory][InlineData(0,"string","string",false)][InlineData(1,"int64","int64",true)]
    public async Task Original_shared_controls_run_without_relaxing_publication_gate(int mode,string key,string parent,bool shorter)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(mode,key,parent,shorter);var repository=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-shared-cached-target");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var rejected=Path.Combine(directory,"not-published");Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(c,rejected,"Example.GlobalSharedCachedTarget",repository));Assert.False(Directory.Exists(rejected));new CrossTableExcelApplicationPreparer().Prepare(c,output,"Example.GlobalSharedCachedTarget",repository);Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));using(var marker=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,CrossTableExcelApplicationPreparer.Marker))))Assert.False(marker.RootElement.GetProperty("readyForPublication").GetBoolean());var actual=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(output,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal(1,actual.ConfigPages.Single(x=>x.Id=="30").IsImport);Assert.Equal(1,actual.ConfigPages.Single(x=>x.Id=="30").IsExport);
            var hand=Path.Combine(output,"backend/generated/Services/CrossConsumerService.cs");await File.AppendAllTextAsync(hand,"\n// 共享缓存名称验证保留手写规则。\n");var saved=await File.ReadAllBytesAsync(hand);for(var round=0;round<2;round++){new CSharpGenerator().Generate(c,Path.Combine(output,"backend/generated"),"Example.GlobalSharedCachedTarget");Assert.Equal(saved,await File.ReadAllBytesAsync(hand));}
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var node=NonRedundantApplicationFixture.Start("node",output);node.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalSharedCachedTargetVueProbe.mjs"));node.ArgumentList.Add(Path.Combine(output,"frontend"));node.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(node);
            foreach(var file in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(repository,"frontend/metarill_uni_admin",file)),File.ReadAllBytes(Path.Combine(output,"frontend",file)));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>共同名称、父键、上级或全局页不同就拒绝整个组，不能只保留其中一个控件。</summary>
    [Theory][InlineData("name")][InlineData("parent")][InlineData("page")][InlineData("terminal")]
    public void Invalid_shared_cached_group_cannot_enter_preparation(string kind)
    {
        var c=GlobalCrossCachedTargetFixture.CreateShared(1,"string","int64",true);var second=c.ConfigColumns.Single(x=>x.Id=="335");var json=System.Text.Json.Nodes.JsonNode.Parse(second.ControlDetail)!;if(kind=="name")json["levels"]![1]!["nameField"]="status";if(kind=="parent")json["levels"]![1]!["parentField"]="lookup_id";if(kind=="page")c.ConfigPages.Single(x=>x.Id=="10").YzProjectId="101";if(kind=="terminal")c.ConfigTables.Single(x=>x.Id=="2").Extra.Clear();second.ControlDetail=json.ToJsonString();var output=Path.Combine(Path.GetTempPath(),"metarill-bad-shared-cached",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new CrossTableExcelApplicationPreparer().Prepare(c,output,"Example.BadSharedCached",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
}
