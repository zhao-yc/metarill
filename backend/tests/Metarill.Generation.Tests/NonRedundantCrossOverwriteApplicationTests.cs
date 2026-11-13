using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>覆盖导入的内部完整源码与真实Web装载验证；不消费实库租约或打开公开Full门槛。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossOverwriteApplicationTests
{
    private const string RootNamespace=GlobalCachedCrossExcelNativePreparation.RootNamespace;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    /// <summary>沿原六模型，仅明确选择覆盖方式与原级联首列，不改页面列、表结构或关系语义。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog=GlobalCachedCrossExcelNativePreparation.Catalog();
        foreach(var table in catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)))
        {
            var page=catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id);
            page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            catalog.ConfigColumns.First(column=>column.ConfigTableId==table.Id&&column.ControlKind=="CascadeTree").SortNo=-10000;
        }
        return catalog;
    }

    /// <summary>真实六个覆盖页与关闭 Excel 后的基底都通过公开检查，且不改输入。</summary>
    [Fact]
    public void Six_models_have_real_logical_matches_and_pass_publication_audit()
    {
        var catalog=Catalog();var before=JsonSerializer.Serialize(catalog,Json);var model=GenerationModel.Create(catalog,RootNamespace);
        var sources=model.Tables.Where(table=>table.Source.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)).ToArray();Assert.Equal(6,sources.Length);
        foreach(var table in sources)
        {
            var page=Assert.Single(table.Pages);var match=NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page));
            Assert.NotNull(match);Assert.Contains("id",match.Outputs);Assert.DoesNotContain("attachments",match.Outputs);
            Assert.Equal("OverWriteByFirstColumn",page.Source.Extra["importMethod"].GetString());
        }
        var prepared=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,RootNamespace);
        Assert.Empty(prepared.Gaps);
        Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(prepared.Baseline,RootNamespace)));
        Assert.Equal(before,JsonSerializer.Serialize(catalog,Json));
        new FrontendPackager().Validate(catalog);
    }

    /// <summary>独立编译包内所有源码并在Production/Web装载六个私有计划；未认证原导入路由必须401且不访问数据库。</summary>
    [Fact]
    public async Task Own_source_preparation_loads_six_match_plans_and_original_import_routes()
    {
        var source=NonRedundantApplicationFixture.Root();var catalog=Catalog();var before=JsonSerializer.Serialize(catalog,Json);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-overwrite-full");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(root,"prepared");
        var evidence=Path.Combine(source,".local/cross-overwrite-full-20261102",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);
        try
        {
            var prepared=new CrossTableExcelApplicationPreparer().Prepare(catalog,output,RootNamespace,source);
            Assert.Equal(before,JsonSerializer.Serialize(catalog,Json));Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));
            using(var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,CrossTableExcelApplicationPreparer.Marker))))
            {
                var state=marker.RootElement;Assert.False(state.GetProperty("readyForPublication").GetBoolean());Assert.Equal("web-database-v1",state.GetProperty("authenticationProfile").GetString());
                foreach(var file in state.GetProperty("files").EnumerateArray())Assert.Equal(file.GetProperty("sha256").GetString(),Hash(File.ReadAllBytes(CSharpGenerator.SafePath(output,file.GetProperty("path").GetString()!))));
            }
            foreach(var name in ApplicationPackager.FullFoundationProjects)Assert.True(File.Exists(Path.Combine(output,"src",name,name+".csproj")));
            foreach(var project in Directory.GetFiles(output,"*.csproj",SearchOption.AllDirectories))
                foreach(var reference in XDocument.Load(project).Descendants("ProjectReference"))
                {
                    var relative=reference.Attribute("Include")!.Value;Assert.False(Path.IsPathRooted(relative));
                    var resolved=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!,relative));Assert.StartsWith(output+Path.DirectorySeparatorChar,resolved,StringComparison.OrdinalIgnoreCase);Assert.True(File.Exists(resolved));
                }
            foreach(var file in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","common/yzp_runtime/remoteRelationOptions.js","pages/custom/agent_authorize.vue"})
                Assert.Equal(Text(Path.Combine(source,"frontend/metarill_uni_admin",file)),Text(Path.Combine(output,"frontend",file)));
            var actual=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(output,"backend/metadata/project.json")),Json)!;
            Assert.Equal(6,actual.ConfigPages.Count(page=>page.IsImport==1&&page.Extra.TryGetValue("importMethod",out var mode)&&mode.ValueKind==JsonValueKind.String&&mode.GetString()=="OverWriteByFirstColumn"));
            Assert.Equal(before,JsonSerializer.Serialize(actual,Json));
            await Build(output,evidence,false);
            var host=Path.Combine(output,"backend/Application.Host");await File.WriteAllTextAsync(Path.Combine(host,"OverwriteObservation.cs"),Observation);await Build(output,evidence,true);
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",host);start.ArgumentList.Add(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"));
            start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
            start.Environment["Database__Provider"]="mysql";start.Environment["Database__ExpectedDatabase"]="overwrite_offline_probe";
            start.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=overwrite_offline_probe;User ID=offline;Password=unused-offline;Connection Timeout=1";
            start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;
            await using(var app=await FreshHost.StartAsync(start,"/health"))
            {
                var receipt=Path.Combine(host,"overwrite-observation.json");File.Copy(receipt,Path.Combine(evidence,"observation.json"));
                using var observed=JsonDocument.Parse(await File.ReadAllTextAsync(receipt));var value=observed.RootElement;
                Assert.Equal(catalog.ConfigTables.Count,value.GetProperty("services").GetInt32());Assert.Equal(catalog.ConfigPages.Count(page=>page.IsImport==1),value.GetProperty("imports").GetInt32());Assert.True(value.GetProperty("sharedAuthorization").GetBoolean());
                var plans=value.GetProperty("plans").EnumerateArray().ToArray();Assert.Equal(6,plans.Length);
                var model=GenerationModel.Create(actual,RootNamespace);
                foreach(var table in model.Tables.Where(table=>table.Source.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)))
                {
                    var page=Assert.Single(table.Pages);var plan=Assert.Single(plans,plan=>plan.GetProperty("field").GetString()=="ImportMatchPlan"+page.Source.Id);
                    Assert.Equal(RootNamespace+".Generated.Services."+table.Name+"Service",plan.GetProperty("service").GetString());
                    var outputs=plan.GetProperty("outputs").EnumerateObject().Select(property=>property.Name).Order().ToArray();
                    var expected=NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page))!;Assert.Equal(expected.Outputs.Order(),outputs);Assert.DoesNotContain("attachments",outputs);
                    var route="/api/admin/auto/"+table.Source.TableName+"/"+page.Source.Id+"/importDatas"+page.Source.Id;
                    await FreshWebHttp.SendAsync(app.Client,HttpMethod.Post,route,new{},HttpStatusCode.Unauthorized);
                }
            }
            var generated=Path.Combine(output,"backend/generated");var tableName=catalog.ConfigTables.First(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)).TableName;
            var hand=Path.Combine(generated,"Services/"+GenerationModel.Pascal(tableName)+"Service.cs");await File.AppendAllTextAsync(hand,"\n// 覆盖导入完整源码验收保留手写扩展。\n");var retained=await File.ReadAllBytesAsync(hand);
            new CSharpGenerator().Generate(actual,generated,RootNamespace);Assert.Equal(retained,await File.ReadAllBytesAsync(hand));
            var publicOutput=Path.Combine(root,"public-full");
            Assert.True(new ApplicationPackager().Create(actual,publicOutput,RootNamespace,source).FrontendIncluded);
            Assert.True(File.Exists(Path.Combine(publicOutput,".metarill-package.json")));
            Assert.False(File.Exists(Path.Combine(publicOutput,CrossTableExcelApplicationPreparer.Marker)));
            await File.WriteAllTextAsync(Path.Combine(evidence,"result.json"),JsonSerializer.Serialize(new{passed=true,internalPreparationOnly=true,ordinaryIdentityVerified=false,databaseExecuted=false,publicationOpened=false,models=6},Json));
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>在包内相对引用基础上严格编译，保留私有日志，不使用工作区DLL替代运行时。</summary>
    private static async Task Build(string output,string evidence,bool noRestore)
    {
        var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);
        foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})start.ArgumentList.Add(argument);
        if(noRestore)start.ArgumentList.Add("--no-restore");start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;await NonRedundantApplicationFixture.RunAsync(start);
    }
    /// <summary>仅观察实际生产注册和六个固定计划，不提供身份、数据库实现或替换Controller。</summary>
    private const string Observation="""
using System.Reflection;
using System.Text.Json;
using Metarill.Data.Queries;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
namespace Metarill.GlobalCrossExcelNative.Host;
public static partial class HostCustomization
{
    // 不改变原认证、权限、事务、SQL或生成Service。
    static partial void ConfigureServices(IServiceCollection services,IConfiguration configuration,IHostEnvironment environment)=>services.AddHostedService<OverwriteObservation>();
}
internal sealed class OverwriteObservation(IServiceProvider services,IHostEnvironment environment):IHostedService
{
    // Service构造和静态计划装载不应建立业务连接，全部类型来自本包的实际宿主程序集。
    public Task StartAsync(CancellationToken token)
    {
        using var scope=services.CreateScope();var provider=scope.ServiceProvider;
        var types=typeof(OverwriteObservation).Assembly.GetTypes().Where(type=>type.Namespace=="Metarill.GlobalCrossExcelNative.Generated.Services"&&type.Name.EndsWith("Service",StringComparison.Ordinal)&&!type.IsAbstract).ToArray();
        foreach(var type in types)_=provider.GetRequiredService(type);
        var plans=types.SelectMany(type=>type.GetFields(BindingFlags.Static|BindingFlags.NonPublic).Where(field=>field.Name.StartsWith("ImportMatchPlan",StringComparison.Ordinal)).Select(field=>new{service=type.FullName,field=field.Name,outputs=((NonRedundantReadPlan)field.GetValue(null)!).OutputTypes})).ToArray();
        var actions=provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        File.WriteAllText(Path.Combine(environment.ContentRootPath,"overwrite-observation.json"),JsonSerializer.Serialize(new{services=types.Length,imports=actions.Count(action=>action.ActionName=="Import"),plans,sharedAuthorization=ReferenceEquals(provider.GetRequiredService<IRuntimeAuthorization>(),provider.GetRequiredService<ISecurityIdentityAccessor>())}));return Task.CompletedTask;
    }
    // 没有后台轮询、会话或独立连接。
    public Task StopAsync(CancellationToken token)=>Task.CompletedTask;
}
""";
    /// <summary>只核对内容摘要，避免输出原页面正文。</summary>
    private static string Hash(byte[] data)=>Convert.ToHexStringLower(SHA256.HashData(data));
    /// <summary>允许Git行尾差异，原组件内容与交互必须保持。</summary>
    private static string Text(string path)=>File.ReadAllText(path).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
}
