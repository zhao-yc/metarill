using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Testing;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>同一元数据证明级联根/中/末全局缓存名称的候选、读写、维护和完整包没有分叉。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCrossCachedTargetGenerationTests
{
    /// <summary>八个模式保持物理父键、各级固定页、三个缓存及原文件/维护/导入导出入口。</summary>
    [Theory][InlineData(0,0,"string","string",false)][InlineData(1,0,"string","string",false)][InlineData(0,1,"string","int64",false)][InlineData(1,1,"string","int64",false)]
    [InlineData(0,2,"int64","string",false)][InlineData(1,2,"int64","string",false)][InlineData(0,0,"int64","int64",true)][InlineData(1,0,"int64","int64",true)]
    public void Cached_roles_have_complete_generation_and_maintenance(int mode,int role,string key,string parent,bool allGlobal)
    {
        var c=GlobalCrossCachedTargetFixture.Create(mode,role,key,parent,allGlobal);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalCrossCachedTarget");var gaps=GenerationCapabilityAudit.Collect(model);Assert.NotEmpty(gaps);Assert.All(gaps,gap=>Assert.Contains(gap.Reason,new[]{CrossTableExcelRenderer.ImportAcceptancePending,CrossTableExcelRenderer.ExportAcceptancePending}));CrossTableExcelApplicationPreparer.RequireCurrent(c,model.Namespace);var table=model.Tables.Single(x=>x.Source.Id=="3");var page=table.Pages.Single(x=>x.Source.Id=="30");var chain=Assert.Single(CrossTableReadCompilation.ForPage(model,table,page));Assert.Equal(3,chain.Levels.Count);Assert.Equal("114",chain.Levels[role].Name.Source.Id);Assert.Equal("10",chain.Levels[role].Page.Source.Id);
        var bindings=RelationPropagationCompilation.Bindings(model).Where(x=>x.Source.Source.Id=="3").ToArray();Assert.Equal(mode==1?3:0,bindings.Length);if(mode==1)Assert.Contains(bindings,x=>x.Value.Source.Id=="114"&&x.CrossTablePageId=="10");var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.Contains("lookup_name",maintenance.MixedSourceColumns["cached_global"]);Assert.Contains(page.Writable,x=>x.Source.Id=="339");Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>保留原导入导出，以既有不可发布准备通道编译正式宿主/原组件；公开Full和更新器继续拒绝。</summary>
    [Theory][InlineData(0,0,"string","string",false)][InlineData(1,1,"string","int64",false)][InlineData(0,2,"int64","string",false)][InlineData(1,0,"int64","int64",true)]
    public async Task Unpublished_prepared_application_builds_and_runs_original_cascade_with_cached_names(int mode,int role,string key,string parent,bool allGlobal)
    {
        var c=GlobalCrossCachedTargetFixture.Create(mode,role,key,parent,allGlobal);var repository=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cross-cached-target");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var rejected=Path.Combine(directory,"not-published");Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(c,rejected,"Example.GlobalCrossCachedTarget",repository));Assert.False(Directory.Exists(rejected));
            var prepared=new CrossTableExcelApplicationPreparer().Prepare(c,output,"Example.GlobalCrossCachedTarget",repository);Assert.NotEmpty(prepared.BackendGaps);Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));using(var marker=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,CrossTableExcelApplicationPreparer.Marker)))){Assert.False(marker.RootElement.GetProperty("readyForPublication").GetBoolean());Assert.Equal("web-database-v1",marker.RootElement.GetProperty("authenticationProfile").GetString());}
            var actual=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(output,"backend/metadata/project.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal(1,actual.ConfigPages.Single(x=>x.Id=="30").IsImport);Assert.Equal(1,actual.ConfigPages.Single(x=>x.Id=="30").IsExport);
            var hand=Path.Combine(output,"backend/generated/Services/CrossConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalCrossCachedTarget.Generated.Services;\npublic partial class CrossConsumerService { /// <summary>手写规则必须受重复生成保护。</summary>\npublic string PreservedBusinessRule()=>\"原手写规则\"; }\n");var bytes=await File.ReadAllBytesAsync(hand);for(var round=0;round<2;round++){new CSharpGenerator().Generate(c,Path.Combine(output,"backend/generated"),"Example.GlobalCrossCachedTarget");Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));}
            var ordinary=Path.Combine(directory,"ordinary");var baseline=CrossTableExcelApplicationPreparer.RequireCurrent(c,"Example.GlobalCrossCachedTarget").Baseline;new ApplicationPackager().Create(baseline,ordinary,"Example.GlobalCrossCachedTarget",repository);Assert.ThrowsAny<Exception>(()=>new LocalGenerationUpdater().Preview(ordinary,output,["generate-project-config","sync-runtime","generate-all-page"]));
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalCrossCachedTargetVueProbe.mjs"));probe.ArgumentList.Add(output);await NonRedundantApplicationFixture.RunAsync(probe);
            // 正式Production/Web匿名请求应先在身份边界401，不访问不可达离线库；这不是正式登录或业务验收。
            var hostDirectory=Path.Combine(output,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"uploads");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");
            var hostLogs=Path.Combine(repository,".local/global-cross-cached-target-hosts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(hostLogs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=hostLogs;
            await using(var running=await FreshHost.StartAsync(host,"/health")){foreach(var action in new[]{"page","importDatas30","initExport","cascadeTreeOptions334"})await FreshWebHttp.SendAsync(running.Client,HttpMethod.Post,"/api/admin/auto/cross_consumer/30/"+action,new{},System.Net.HttpStatusCode.Unauthorized);}
            foreach(var file in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(repository,"frontend/metarill_uni_admin",file)),File.ReadAllBytes(Path.Combine(output,"frontend",file)));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>缓存字段不能伪造上游发布、页归属、标量父键或原物理条件，完整包在落盘前拒绝。</summary>
    [Theory][InlineData("producerProject")][InlineData("privateTerminal")][InlineData("pageForeign")][InlineData("pageVariable")][InlineData("cacheCondition")][InlineData("path")][InlineData("parentJoin")][InlineData("fakeOther")]
    public void Invalid_cached_role_rejects_complete_package_before_staging(string kind)
    {
        var c=GlobalCrossCachedTargetFixture.Create(1,1,"string","int64",true);if(kind=="producerProject")c.ConfigColumns.Add(new(){Id="199",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64"});if(kind=="privateTerminal")c.ConfigTables.Single(x=>x.Id=="2").Extra.Clear();if(kind=="pageForeign")c.ConfigPages.Single(x=>x.Id=="10").YzProjectId="101";if(kind=="pageVariable")c.ConfigPages.Single(x=>x.Id=="10").WhereSql="id=@parent_id";if(kind=="cacheCondition")c.ConfigPages.Single(x=>x.Id=="10").WhereSql="lookup_name='甲'";if(kind=="path")c.ConfigColumns.Single(x=>x.Id=="114").Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");if(kind=="parentJoin")c.ConfigColumns.Single(x=>x.Id=="p1").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="fakeOther")c.ConfigColumns.Single(x=>x.Id=="405").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        var output=Path.Combine(Path.GetTempPath(),"metarill-global-cross-cached-invalid",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidGlobalCrossCachedTarget",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
}
