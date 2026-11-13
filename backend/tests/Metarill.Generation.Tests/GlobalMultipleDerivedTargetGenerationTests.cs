using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局R0单值名称进入两个原数组控件；候选、原名称数组及完整维护必须同时生成。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalMultipleDerivedTargetGenerationTests
{
    /// <summary>R0/R1、显式/自动名称与内层两种物理键均生成完整模型，逻辑值不交给物理传播器。</summary>
    [Theory][InlineData(0,false,"int64")][InlineData(0,true,"int64")][InlineData(1,false,"int64")][InlineData(1,true,"int64")]
    [InlineData(0,false,"string")][InlineData(0,true,"string")][InlineData(1,false,"string")][InlineData(1,true,"string")]
    public void Global_r0_arrays_generate_candidates_and_complete_mixed_maintenance(int mode,bool automatic,string inner)
    {
        var c=Catalog(mode,automatic,inner);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalMultipleDerivedTarget");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var table=model.Tables.Single(t=>t.Source.Id=="3");var page=table.Pages.Single(p=>p.Source.Id=="30");var multi=Assert.Single(MultiSelectCompilation.ForPage(model,table,page));var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page));
        Assert.Equal("14",multi.Name.Source.Id);Assert.Equal("14",checkbox.Name.Source.Id);Assert.Equal("global_source_global_target_names",multi.Names!.Source.ColumnName);Assert.Equal("selected_global_source_global_target_names",checkbox.Names.Source.ColumnName);
        Assert.DoesNotContain(page.Writable,f=>f.Source.Id==multi.Names.Source.Id||f.Source.Id==checkbox.Names.Source.Id);Assert.DoesNotContain(RelationPropagationCompilation.Bindings(model),b=>b.Source.Source.Id=="3");
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.DoesNotContain("global_source",maintenance.MixedSourceColumns.Keys);
        if(mode==1){Assert.Contains("global_source",maintenance.MixedTargets);Assert.Contains("global_target",maintenance.MixedTargets);Assert.Equal(new[]{"global_source_global_target_names","selected_global_source_global_target_names"},maintenance.MixedSourceColumns["cached_consumer"].Order(StringComparer.Ordinal));}
        else Assert.DoesNotContain("cached_consumer",maintenance.MixedSourceColumns.Keys);
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>四个交叉模型实际生成完整包、严格构建并挂载原Vue，两次更新保持手写业务和全部受管产物。</summary>
    [Theory][InlineData(0,false,"int64")][InlineData(0,true,"string")][InlineData(1,false,"string")][InlineData(1,true,"int64")]
    public async Task Global_r0_array_full_apps_preserve_original_controls_and_runtime(int mode,bool automatic,string inner)
    {
        var c=Catalog(mode,automatic,inner);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-derived-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var repository=NonRedundantApplicationFixture.Root();
        try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalMultipleDerivedTarget",repository);
            var hand=Path.Combine(output,"backend/generated/Services/CachedConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalMultipleDerivedTarget.Generated.Services;\npublic partial class CachedConsumerService { /// <summary>验收手写扩展，必须跨重复生成保持。</summary>\npublic string PreservedBusinessRule()=>\"全局R0原业务\"; }\n");
            var saved=await File.ReadAllBytesAsync(hand);var generated=Directory.GetFiles(Path.Combine(output,"backend/generated"),"*.g.cs",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(output,path),File.ReadAllBytes);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalMultipleDerivedTarget",repository);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);
            Assert.Equal(saved,await File.ReadAllBytesAsync(hand));foreach(var file in generated)Assert.Equal(file.Value,await File.ReadAllBytesAsync(Path.Combine(output,file.Key)));
            var repeat=updater.Preview(output,candidate,operations);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Core/Metadata/GlobalDerivedRelationSource.cs")));
            Assert.Contains("Name = \"global_target_name\", IsIgnore = true",await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/Entities/GlobalSource.g.cs")));
            var evidence=Path.Combine(repository,".local/global-multiple-derived-builds",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})build.ArgumentList.Add(arg);build.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;await NonRedundantApplicationFixture.RunAsync(build);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalMultipleCachedTargetVueProbe.mjs"));probe.ArgumentList.Add(output);probe.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;await NonRedundantApplicationFixture.RunAsync(probe);
            foreach(var file in new[]{"components/yzp-relation-select/yzp-relation-select.vue","components/yzp-file-library/yzp-file-library.vue","common/yzp_runtime/detailLayoutRuntime.js","uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue","common/yzp_runtime/originalSelectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(repository,"frontend/metarill_uni_admin",file)),File.ReadAllBytes(Path.Combine(output,"frontend",file)));
            // Production真实HTTP只证明匿名认证边界，不把离线端口当作有效登录或业务执行。
            var hostDirectory=Path.Combine(output,"backend/Application.Host");var host=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);host.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));host.Environment["ASPNETCORE_ENVIRONMENT"]="Production";host.Environment["DOTNET_ENVIRONMENT"]="Production";host.Environment["Authentication__Mode"]="Web";host.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";host.Environment["Database__Provider"]="mysql";host.Environment["Database__ExpectedDatabase"]="offline";host.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";host.Environment["Files__RootDirectory"]=Path.Combine(directory,"uploads");host.Environment["Exports__RootDirectory"]=Path.Combine(directory,"exports");
            var hostLogs=Path.Combine(repository,".local/global-multiple-derived-hosts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(hostLogs);host.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=hostLogs;
            await using(var running=await FreshHost.StartAsync(host,"/health")){foreach(var action in new[]{"page","importDatas30","initExport","findNameIdGlobalSource34/page"})await FreshWebHttp.SendAsync(running.Client,HttpMethod.Post,"/api/admin/auto/cached_consumer/30/"+action,new{},System.Net.HttpStatusCode.Unauthorized);}
            Assert.Equal(before,JsonSerializer.Serialize(c));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>无行项目的全局R0服务仍固定发布项目，普通和超级身份切错项目都在连接前拒绝。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Actual_array_services_reject_foreign_project_before_connecting(int mode)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-derived-actor");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(mode,false,"string"),Path.Combine(directory,"generated"),"Example.GlobalMultipleDerivedActor");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            foreach(var administrator in new[]{false,true})foreach(var (name,page) in new[]{("GlobalSource","10"),("GlobalTarget","20"),("CachedConsumer","30")})
            {
                var actor=new RuntimeActor(8,"发布范围观测",administrator,"101","");var mapper=Activator.CreateInstance(assembly.GetType("Example.GlobalMultipleDerivedActor.Generated.Mappers."+name+"Mapper",true)!,session)!;var constructor=assembly.GetType("Example.GlobalMultipleDerivedActor.Generated.Services."+name+"Service",true)!.GetConstructors().Single();
                var service=constructor.Invoke(constructor.GetParameters().Select(parameter=>parameter.ParameterType==typeof(DatabaseSession)?(object)session:parameter.ParameterType.IsInstanceOfType(mapper)?mapper:parameter.ParameterType==typeof(IIdGenerator)?new SnowflakeIdGenerator(909):parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>)?Array.Empty<IRecordMutationObserver>():parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>)?Array.Empty<IRecordMutationGuard>():null).ToArray());
                foreach(var (action,arguments) in new (string,object?[])[]{("Page",[new PageRequest(),actor,CancellationToken.None]),("Count",[new PageRequest(),actor,CancellationToken.None]),("Detail",[1L,null,actor,CancellationToken.None]),("Export",[new PageRequest(),actor,100,CancellationToken.None])})
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>InvokeAsync(service,action+page+"Async",arguments));
            }
            Assert.Null(session.CurrentTransaction);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原公开完整包保持严格门槛，不以隐藏名称、动态目标或伪造全局声明绕开生成校验。</summary>
    [Theory][InlineData("privateProducer")][InlineData("privateTerminal")][InlineData("hiddenName")][InlineData("dynamicPage")][InlineData("credential")][InlineData("fakePath")][InlineData("wrongProject")][InlineData("foreignName")][InlineData("virtualProducerProject")]
    public void Invalid_global_r0_array_application_is_rejected_before_staging(string kind)
    {
        var c=Catalog(0,false,"string");switch(kind)
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
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-derived-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.GlobalMultipleDerivedReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>全局R0的物理标签仍走原物理传播；显式／自动名称不会被派生能力混淆。</summary>
    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)]
    public void Global_r0_physical_labels_keep_original_array_ownership(int mode,bool automatic)
    {
        var c=Catalog(mode,automatic,"string");c.ConfigColumns.Add(new(){Id="16",ConfigTableId="1",YzProjectId="100",ColumnName="caption",LogicalType="string",ColumnLength="100"});c.ConfigPageColumns.Add(new(){Id="1016",ConfigPageId="10",ConfigColumnId="16",YzProjectId="100",IsShow=1,IsEdit=1});foreach(var field in c.ConfigColumns.Where(f=>f.Id is "34" or "38"))field.ControlDetail=field.ControlDetail.Replace("global_target_name","caption",StringComparison.Ordinal);foreach(var field in c.ConfigColumns.Where(f=>f.Id is "35" or "40")){field.ColumnName=field.ColumnName.Replace("global_target_names","captions",StringComparison.Ordinal);field.Extra["columnFullName"]=JsonSerializer.SerializeToElement(field.Id=="35"?"global_source.caption":"selected_global_source.caption");}
        var model=GenerationModel.Create(c,"Example.GlobalR0PhysicalArray");Assert.Empty(GenerationCapabilityAudit.Collect(model));var table=model.Tables.Single(t=>t.Source.Id=="3");var page=table.Pages.Single(p=>p.Source.Id=="30");Assert.Equal("global_source_captions",Assert.Single(MultiSelectCompilation.ForPage(model,table,page)).Names!.Source.ColumnName);Assert.Equal("selected_global_source_captions",Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page)).Names.Source.ColumnName);Assert.Equal(mode==1?2:0,RelationPropagationCompilation.Bindings(model).Count(b=>b.Source.Source.Id=="3"));
    }
    /// <summary>仅解开反射调用的同步异常外壳，不改变真实Service异步错误或数据库行为。</summary>
    private static async Task InvokeAsync(object service,string method,object?[] arguments)
    {
        Task pending;
        try{pending=(Task)service.GetType().GetMethod(method)!.Invoke(service,arguments)!;}
        catch(System.Reflection.TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        await pending;
    }
    /// <summary>只更改中间目标存储模式和真实外键类型，原页面和两控件、文件、导入导出及维护按钮保留。</summary>
    internal static MetadataCatalog Catalog(int mode,bool automatic,string inner)
    {
        var c=GlobalMultipleCachedTargetGenerationTests.Catalog(mode,automatic);c.ConfigTables.Single(t=>t.Id=="1").IsRongyu=0;c.ConfigColumns.Single(f=>f.Id=="13").LogicalType=inner;return c;
    }
}
