using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局R0逻辑名称必须闭合原候选、生成、维护与完整包，而不是只在数据层放宽目标。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalDerivedTargetGenerationTests
{
    /// <summary>两种消费者与内外两种外键共用原完整元数据，保留原导入导出及文件控件。</summary>
    [Theory]
    [InlineData(0,"int64","int64")][InlineData(0,"string","int64")]
    [InlineData(0,"int64","string")][InlineData(0,"string","string")]
    [InlineData(1,"int64","int64")][InlineData(1,"string","int64")]
    [InlineData(1,"int64","string")][InlineData(1,"string","string")]
    public void Global_r0_name_forms_a_complete_generation_model(int mode,string outer,string inner)
    {
        var c=Catalog(mode,outer,inner);var before=JsonSerializer.Serialize(c);
        var model=GenerationModel.Create(c,"Example.GlobalDerivedTarget");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.DoesNotContain("global_source",maintenance.MixedSourceColumns.Keys);
        if(mode==1){Assert.Contains("global_source",maintenance.MixedTargets);Assert.Contains("global_target",maintenance.MixedTargets);Assert.Equal(new[]{"global_source_global_target_name","selected_global_source_global_target_name"},maintenance.MixedSourceColumns["cached_consumer"].Order(StringComparer.Ordinal));}
        else Assert.DoesNotContain("cached_consumer",maintenance.MixedSourceColumns.Keys);
        var producer=model.Tables.Single(t=>t.Source.Id=="1");Assert.Contains(producer.Pages[0].Readable,f=>f.Source.Id=="14");Assert.DoesNotContain(producer.Pages[0].Writable,f=>f.Source.Id=="14");
        Assert.All(c.ConfigPages,p=>{Assert.Equal(1,p.IsImport);Assert.Equal(1,p.IsExport);});
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>四个交叉模型实际生成完整包、严格构建并挂载原Vue，两次更新保持手写业务和全部受管产物。</summary>
    [Theory][InlineData(0,"int64","int64")][InlineData(0,"string","string")][InlineData(1,"int64","string")][InlineData(1,"string","int64")]
    public async Task Global_r0_full_application_builds_updates_and_runs_original_controls(int mode,string outer,string inner)
    {
        var c=Catalog(mode,outer,inner);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var repository=NonRedundantApplicationFixture.Root();
        try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalDerivedTarget",repository);
            var hand=Path.Combine(output,"backend/generated/Services/CachedConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalDerivedTarget.Generated.Services;\npublic partial class CachedConsumerService { /// <summary>验收手写扩展，必须跨重复生成保持。</summary>\npublic string PreservedBusinessRule()=>\"全局R0原业务\"; }\n");
            var saved=await File.ReadAllBytesAsync(hand);var generated=Directory.GetFiles(Path.Combine(output,"backend/generated"),"*.g.cs",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(output,path),File.ReadAllBytes);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalDerivedTarget",repository);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);
            Assert.Equal(saved,await File.ReadAllBytesAsync(hand));foreach(var file in generated)Assert.Equal(file.Value,await File.ReadAllBytesAsync(Path.Combine(output,file.Key)));
            var repeat=updater.Preview(output,candidate,operations);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Metadata/GlobalDerivedRelationSource.cs")));
            Assert.Contains("Name = \"global_target_name\", IsIgnore = true",await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/Entities/GlobalSource.g.cs")));
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(build);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalCachedTargetVueProbe.mjs"));probe.ArgumentList.Add(output);await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var file in new[]{"components/yzp-relation-select/yzp-relation-select.vue","components/yzp-file-library/yzp-file-library.vue","common/yzp_runtime/detailLayoutRuntime.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(repository,"frontend/metarill_uni_admin",file)),File.ReadAllBytes(Path.Combine(output,"frontend",file)));
            // Production真实HTTP只证明匿名认证边界，不把离线端口当作有效登录或业务执行。
            var hostDirectory=Path.Combine(output,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"uploads");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");
            var hostLogs=Path.Combine(repository,".local/global-derived-target-hosts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(hostLogs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=hostLogs;
            await using(var running=await FreshHost.StartAsync(host,"/health")){foreach(var action in new[]{"page","importDatas30","initExport","findNameIdGlobalSource34/page"})await FreshWebHttp.SendAsync(running.Client,HttpMethod.Post,"/api/admin/auto/cached_consumer/30/"+action,new{},System.Net.HttpStatusCode.Unauthorized);}
            Assert.Equal(before,JsonSerializer.Serialize(c));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>无行项目的全局R0服务仍固定发布项目，普通和超级身份切错项目都在连接前拒绝。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Actual_generated_services_reject_foreign_project_before_connecting(int mode)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-actor");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(mode,"int64","int64"),Path.Combine(directory,"generated"),"Example.GlobalDerivedActor");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            foreach(var administrator in new[]{false,true})foreach(var (name,page) in new[]{("GlobalSource","10"),("GlobalTarget","20"),("CachedConsumer","30")})
            {
                var actor=new RuntimeActor(8,"发布范围观测",administrator,"101","");var mapper=Activator.CreateInstance(assembly.GetType("Example.GlobalDerivedActor.Generated.Mappers."+name+"Mapper",true)!,session)!;var constructor=assembly.GetType("Example.GlobalDerivedActor.Generated.Services."+name+"Service",true)!.GetConstructors().Single();
                var service=constructor.Invoke(constructor.GetParameters().Select(parameter=>parameter.ParameterType==typeof(DatabaseSession)?(object)session:parameter.ParameterType.IsInstanceOfType(mapper)?mapper:parameter.ParameterType==typeof(IIdGenerator)?new SnowflakeIdGenerator(909):parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>)?Array.Empty<IRecordMutationObserver>():parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>)?Array.Empty<IRecordMutationGuard>():null).ToArray());
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await(Task)service.GetType().GetMethod("Page"+page+"Async")!.Invoke(service,[new PageRequest(),actor,CancellationToken.None])!);
            }
            Assert.Null(session.CurrentTransaction);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原公开完整包保持严格门槛，不以隐藏名称、动态目标或伪造全局声明绕开生成校验。</summary>
    [Theory][InlineData("privateProducer")][InlineData("privateTerminal")][InlineData("hiddenName")][InlineData("dynamicPage")][InlineData("credential")][InlineData("fakePath")][InlineData("wrongProject")][InlineData("foreignName")][InlineData("virtualProducerProject")]
    public void Invalid_global_r0_application_is_rejected_before_staging(string kind)
    {
        var c=Catalog(0,"int64","int64");switch(kind)
        {
            case "virtualProducerProject":c.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64",IsVirtualColumn=1});break;
            case "privateProducer":c.ConfigTables.Single(t=>t.Id=="1").Extra.Clear();break;
            case "privateTerminal":c.ConfigTables.Single(t=>t.Id=="2").Extra.Clear();break;
            case "hiddenName":c.ConfigPageColumns.RemoveAll(f=>f.ConfigColumnId=="14");break;
            case "dynamicPage":c.ConfigPages.Single(p=>p.Id=="10").WhereSql="id=@parent_id";break;
            case "credential":c.ConfigColumns.Single(f=>f.Id=="22").ColumnName="password";break;
            case "fakePath":c.ConfigColumns.Single(f=>f.Id=="14").Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");break;
            case "wrongProject":c.ConfigTables.Single(t=>t.Id=="1").YzProjectId="101";break;
            case "foreignName":c.ConfigColumns.Single(f=>f.Id=="34").ControlDetail=c.ConfigColumns.Single(f=>f.Id=="34").ControlDetail.Replace("global_target_name","status",StringComparison.Ordinal);break;
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.GlobalDerivedReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>复用已经公开的三表完整目录，只替换中间存储模式与其物理外键类型。</summary>
    internal static MetadataCatalog Catalog(int mode,string outer,string inner)
    {
        var c=GlobalCachedTargetGenerationTests.Catalog(mode,outer);
        c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=0;
        c.ConfigColumns.Single(f=>f.Id=="13").LogicalType=inner;
        // 原R0目标页显式开放逻辑名称；页面布局和选择控件仍由原模板决定。
        c.ConfigPageColumns.Add(new(){Id="1014",ConfigPageId="10",ConfigColumnId="14",YzProjectId="100",IsShow=1,IsEdit=0});
        return c;
    }
}
