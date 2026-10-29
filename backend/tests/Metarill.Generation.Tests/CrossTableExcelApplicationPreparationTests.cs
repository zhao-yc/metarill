using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>准备检查不读取私有设置、启动数据库或创建对象；公开Full/更新器门槛保持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelApplicationPreparationTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    /// <summary>八组所有权/ID/物理结构固定且每组带实际导入导出；元数据原对象不被修改。</summary>
    [Fact]
    public void Matrix_has_one_realm_eight_sources_and_only_known_pending_excel_gaps()
    {
        var catalog=CrossTableExcelNativePreparation.Catalog();var before=JsonSerializer.Serialize(catalog,Json);
        var proof=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,CrossTableExcelNativePreparation.RootNamespace);
        Assert.Equal(32,catalog.ConfigTables.Count);Assert.Equal(32,catalog.ConfigPages.Count);Assert.Equal(8,catalog.ConfigPages.Count(page=>page.IsImport==1));Assert.Equal(8,catalog.ConfigColumns.Count(column=>column.ControlKind=="FileLibrary"));
        Assert.Equal(43,FreshInstallConfiguration.ExpectedFullTableCount(catalog));Assert.NotEmpty(proof.Gaps);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(proof.Baseline,CrossTableExcelNativePreparation.RootNamespace)));
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(catalog,"offline")),MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(proof.Baseline,"offline")));
        Assert.Equal(before,JsonSerializer.Serialize(catalog,Json));
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
    }
    /// <summary>业务JSON标记不豁免其它坏链/身份归属/自定义控件/外部菜单；失败不创建输出。</summary>
    [Theory][InlineData("bad-chain")][InlineData("target-project")][InlineData("custom")][InlineData("external-menu")]
    public void Unrelated_gaps_never_pass_preparation_or_public_Full(string fault)
    {
        var catalog=CrossTableExcelNativePreparation.Catalog();catalog.ConfigColumns[0].Extra["allowUnverifiedExcel"]=JsonSerializer.SerializeToElement(true);
        if(fault=="bad-chain")catalog.ConfigColumns.First(column=>column.ControlKind=="CascadeTree").ControlDetail="{\"sourceMode\":\"crossTable\",\"levels\":[]}";
        if(fault=="target-project")catalog.ConfigTables.First(table=>table.TableName.EndsWith("_country",StringComparison.Ordinal)).YzProjectId="2";
        if(fault=="custom")
        {
            var source=catalog.ConfigTables.First(table=>table.TableName.EndsWith("_source",StringComparison.Ordinal));
            catalog.ConfigColumns.Add(new(){Id="99000001",ConfigTableId=source.Id,YzProjectId=catalog.YzProjectId,ColumnName="custom",ControlKind="CustomControl",ControlDetail="未接线组件"});
            catalog.ConfigPageColumns.Add(new(){Id="99000002",ConfigPageId=catalog.ConfigPages.First(page=>page.ConfigTableId==source.Id).Id,ConfigColumnId="99000001",YzProjectId=catalog.YzProjectId});
        }
        if(fault=="external-menu")catalog.ConfigMenus[0].Kind="EXTERNAL_URL";
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-preparation-rejected");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        Assert.ThrowsAny<Exception>(()=>new CrossTableExcelApplicationPreparer().Prepare(catalog,output,CrossTableExcelNativePreparation.RootNamespace,NonRedundantApplicationFixture.Root()));
        Assert.False(Directory.Exists(output));if(Directory.Exists(boundary))Assert.Empty(Directory.GetDirectories(boundary,Path.GetFileName(output)+".preparing-*"));
        Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,CrossTableExcelNativePreparation.RootNamespace,NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>真实应用源码独立编译；原前端与生成归属一致，Web认证启动不连接虚构数据库。</summary>
    [Fact]
    public async Task Prepared_production_host_builds_with_original_assets_actual_metadata_and_Web_identity()
    {
        var source=NonRedundantApplicationFixture.Root();var catalog=CrossTableExcelNativePreparation.Catalog();
        const string secret="不应进入准备包的测试密钥-903581";catalog.ConfigTables[0].Extra["apiKey"]=JsonSerializer.SerializeToElement(secret);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-positive");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(root,"prepared");
        try
        {
            var result=new CrossTableExcelApplicationPreparer().Prepare(catalog,output,CrossTableExcelNativePreparation.RootNamespace,source);
            var repeated=Path.Combine(root,"repeated");new CrossTableExcelApplicationPreparer().Prepare(catalog,repeated,CrossTableExcelNativePreparation.RootNamespace,source);
            Assert.Equal(Snapshot(output),Snapshot(repeated));Assert.NotEmpty(result.RedactedPaths);Assert.NotEmpty(result.BackendGaps);
            Assert.Throws<NotSupportedException>(()=>((IList<string>)result.Files).Add("未知文件"));
            Assert.False(File.Exists(Path.Combine(output,".metarill-package.json")));
            using var marker=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,CrossTableExcelApplicationPreparer.Marker)));
            var state=marker.RootElement;Assert.False(state.GetProperty("readyForPublication").GetBoolean());Assert.Equal("web-database-v1",state.GetProperty("authenticationProfile").GetString());
            Assert.Equal(result.MetadataSha256,state.GetProperty("metadataSha256").GetString());
            foreach(var file in state.GetProperty("files").EnumerateArray())Assert.Equal(file.GetProperty("sha256").GetString(),Hash(File.ReadAllBytes(CSharpGenerator.SafePath(output,file.GetProperty("path").GetString()!))));
            var bytes=File.ReadAllBytes(Path.Combine(output,"backend/metadata/project.json"));Assert.Equal(Hash(bytes),state.GetProperty("securityCatalogSha256").GetString());
            var actual=JsonSerializer.Deserialize<MetadataCatalog>(bytes,Json)!;Assert.Equal(8,actual.ConfigPages.Count(page=>page.IsImport==1));Assert.Equal(8,actual.ConfigPages.Count(page=>page.IsExport==1));
            Assert.Equal(result.MetadataSha256,Hash(CSharpGenerator.CanonicalMetadata(actual)));
            Assert.Contains(Hash(bytes),File.ReadAllText(Path.Combine(output,"backend/Application.Host/PackageSecurityCatalogBinding.g.cs")));
            foreach(var file in Directory.GetFiles(output,"*",SearchOption.AllDirectories))Assert.DoesNotContain(secret,System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            foreach(var name in ApplicationPackager.FullFoundationProjects)Assert.True(File.Exists(Path.Combine(output,"src",name,name+".csproj")));
            foreach(var project in Directory.GetFiles(output,"*.csproj",SearchOption.AllDirectories))foreach(var reference in XDocument.Load(project).Descendants("ProjectReference"))Assert.False(Path.IsPathRooted(reference.Attribute("Include")!.Value));
            foreach(var relative in new[]{"components/yzp-cascade-tree/yzp-cascade-tree.vue","pages/custom/agent_authorize.vue","common/yzp_runtime/remoteRelationOptions.js"})
                Assert.Equal(Text(Path.Combine(source,"frontend/metarill_uni_admin",relative)),Text(Path.Combine(output,"frontend",relative)));
            var baseline=CrossTableExcelApplicationPreparer.RequireCurrent(actual,CrossTableExcelNativePreparation.RootNamespace).Baseline;
            var target=Path.Combine(root,"ordinary-target");new ApplicationPackager().Create(baseline,target,CrossTableExcelNativePreparation.RootNamespace,source);
            var targetBefore=Snapshot(target);
            Assert.ThrowsAny<Exception>(()=>new LocalGenerationUpdater().Preview(target,output,["generate-project-config","sync-runtime","generate-all-page"]));
            Assert.Equal(targetBefore,Snapshot(target));
            var generated=Path.Combine(output,"backend/generated");
            foreach(var file in Directory.GetFiles(Path.Combine(generated,"frontend/pages"),"*.vue",SearchOption.AllDirectories))
                Assert.Equal(Text(file),Text(Path.Combine(output,"frontend/pages",Path.GetRelativePath(Path.Combine(generated,"frontend/pages"),file))));
            // 首次按原生产宿主编译；之后仅加只读观察器核对实际MVC/DI，绝不替换权限或业务入口。
            await Build(output,false);var hostDirectory=Path.Combine(output,"backend/Application.Host");
            await File.WriteAllTextAsync(Path.Combine(hostDirectory,"PreparedObservation.cs"),Observation);
            await Build(output,true);
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);
            start.ArgumentList.Add(Path.Combine(hostDirectory,"bin/Debug/net10.0/Application.Host.dll"));
            start.Environment["ASPNETCORE_ENVIRONMENT"]="Production";start.Environment["DOTNET_ENVIRONMENT"]="Production";
            start.Environment["Authentication__Mode"]="Web";start.Environment["Authentication__DevelopmentBootstrap__Enabled"]="false";
            start.Environment["Database__Provider"]="mysql";start.Environment["Database__ExpectedDatabase"]="cross_excel_offline_probe";
            start.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=cross_excel_offline_probe;User ID=offline;Password=not-used-offline;Connection Timeout=1";
            var logs=Path.Combine(source,".local/cross-excel-preparation-http",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(logs);start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=logs;
            await using(var app=await FreshHost.StartAsync(start,"/health"))
            {
                var receipt=Path.Combine(hostDirectory,"prepared-observation.json");File.Copy(receipt,Path.Combine(logs,"observation.json"));
                using var observation=JsonDocument.Parse(await File.ReadAllTextAsync(receipt));var observed=observation.RootElement;
                Assert.Equal(32,observed.GetProperty("services").GetInt32());Assert.Equal(8,observed.GetProperty("importRoutes").GetInt32());Assert.Equal(8,observed.GetProperty("exportRoutes").GetInt32());Assert.True(observed.GetProperty("sharedAuthorization").GetBoolean());Assert.Equal(8,observed.GetProperty("enabledImports").GetInt32());
                var model=GenerationModel.Create(actual,CrossTableExcelNativePreparation.RootNamespace);
                foreach(var table in model.Tables.Where(table=>table.Source.TableName.EndsWith("_source",StringComparison.Ordinal)))
                {
                    var route="/api/admin/auto/"+table.Source.TableName+"/"+table.Pages.Single().Source.Id;
                    await FreshWebHttp.SendAsync(app.Client,System.Net.Http.HttpMethod.Post,route+"/importDatas"+table.Pages.Single().Source.Id,new{},System.Net.HttpStatusCode.Unauthorized);
                }
            }
            var hand=Directory.GetFiles(Path.Combine(generated,"Services"),"*Service.cs").First();
            await File.AppendAllTextAsync(hand,"\n// 验收准备必须保护的手写扩展。\n");var retained=await File.ReadAllBytesAsync(hand);
            new CSharpGenerator().Generate(actual,generated,CrossTableExcelNativePreparation.RootNamespace);Assert.Equal(retained,await File.ReadAllBytesAsync(hand));
            Assert.Throws<InvalidOperationException>(()=>new CrossTableExcelApplicationPreparer().Prepare(catalog,output,CrossTableExcelNativePreparation.RootNamespace,source));Assert.Equal(retained,await File.ReadAllBytesAsync(hand));
            Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(actual,Path.Combine(root,"not-published"),CrossTableExcelNativePreparation.RootNamespace,source));Assert.False(Directory.Exists(Path.Combine(root,"not-published")));
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>发布异常保留真实准备目录和诊断，不能把故障包当作成功发布或误删现场。</summary>
    [Fact]
    public async Task Publication_failure_preserves_owned_stage()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-failure");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(root,"prepared");
        try
        {
            var preparer=new CrossTableExcelApplicationPreparer((stage,destination)=>throw new PackagePublicationException(stage,destination,new IOException("准备发布故障")));
            var error=Assert.Throws<PackagePublicationException>(()=>preparer.Prepare(CrossTableExcelNativePreparation.Catalog(),output,CrossTableExcelNativePreparation.RootNamespace,source));
            Assert.True(Directory.Exists(error.StageDirectory));Assert.NotNull(error.ContainingStageDirectory);Assert.True(File.Exists(error.DiagnosticPath));
            Assert.True(File.Exists(Path.Combine(error.StageDirectory,CrossTableExcelApplicationPreparer.Marker)));Assert.False(Directory.Exists(output));
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>编译实际包内相对引用，不使用测试程序集代替生产宿主。</summary>
    private static async Task Build(string output,bool noRestore)
    {
        var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);
        foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})build.ArgumentList.Add(argument);
        if(noRestore)build.ArgumentList.Add("--no-restore");await NonRedundantApplicationFixture.RunAsync(build);
    }
    private const string Observation="""
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Text.Json;
namespace Metarill.CrossExcelNative.Host;
public static partial class HostCustomization
{
    // 仅观察既有生产注册，不替换Controller、认证、事务或SQL。
    static partial void ConfigureServices(IServiceCollection services,IConfiguration configuration,IHostEnvironment environment)=>services.AddHostedService<PreparedCrossExcelObservation>();
}
internal sealed class PreparedCrossExcelObservation(IServiceProvider services,IHostEnvironment environment):IHostedService
{
    // 构造全部真实生成Service；读取MVC描述和已校验目录不触发数据库调用。
    public Task StartAsync(CancellationToken token)
    {
        using var scope=services.CreateScope();var provider=scope.ServiceProvider;
        var generated=typeof(PreparedCrossExcelObservation).Assembly.GetTypes().Where(type=>type.Namespace=="Metarill.CrossExcelNative.Generated.Services"&&type.Name.EndsWith("Service",StringComparison.Ordinal)&&!type.IsAbstract).ToArray();
        foreach(var type in generated)_=provider.GetRequiredService(type);
        var actions=provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToArray();
        var catalog=provider.GetRequiredService<VerifiedPackageSecurityCatalog>().Clone();
        var value=new{services=generated.Length,importRoutes=actions.Count(action=>action.ActionName=="Import"),exportRoutes=actions.Count(action=>action.ActionName=="InitExport"),enabledImports=catalog.ConfigPages.Count(page=>page.IsImport==1),sharedAuthorization=ReferenceEquals(provider.GetRequiredService<IRuntimeAuthorization>(),provider.GetRequiredService<ISecurityIdentityAccessor>())};
        File.WriteAllText(Path.Combine(environment.ContentRootPath,"prepared-observation.json"),JsonSerializer.Serialize(value));return Task.CompletedTask;
    }
    // 没有循环或自有连接。
    public Task StopAsync(CancellationToken token)=>Task.CompletedTask;
}
""";
    /// <summary>只比较相对路径和摘要，避免输出业务内容。</summary>
    private static string Snapshot(string root)=>string.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(path=>Path.GetRelativePath(root,path).Replace('\\','/')+":"+Hash(File.ReadAllBytes(path))).Order(StringComparer.Ordinal));
    /// <summary>记录固定内容摘要。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    /// <summary>只统一源文件换行，不改页面交互。</summary>
    private static string Text(string file)=>File.ReadAllText(file).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');

}
