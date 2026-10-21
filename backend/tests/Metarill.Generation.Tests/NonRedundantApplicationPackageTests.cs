using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>完整非冗余应用使用正式打包器与独立源码构建，不以BackendOnly或生成片段代替。</summary>
public sealed class NonRedundantApplicationPackageTests
{
    /// <summary>完整包带正式Web认证、原页面和读取运行时；实际构建包内项目并执行包内原Vue协议。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_application_builds_from_its_own_foundations_and_keeps_original_pages(bool files)
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_nrs_offline",files);var model=GenerationModel.Create(catalog,"Example.NonRedPackage");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-application");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var package=new ApplicationPackager().Create(catalog,output,model.Namespace,NonRedundantApplicationFixture.Root());Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);Assert.True(package.FrontendIncluded);
            Assert.True(File.Exists(Path.Combine(output,"src/Metarill.Data/Queries/MySqlNonRedundantReadPlan.cs")));Assert.True(File.Exists(Path.Combine(output,"backend/metadata/security.json")));
            Assert.True(File.Exists(Path.Combine(output,"frontend/pages/yzp/mt_nrs_offline_order-list-101.vue")));
            var handwritten=Path.Combine(output,"backend/generated/Services/MtNrsOfflineOrderService.cs");await File.AppendAllTextAsync(handwritten,"\n// 完整包手写扩展保护标记。\n");var protectedBytes=await File.ReadAllBytesAsync(handwritten);
            var next=NonRedundantApplicationFixture.Catalog("mt_nrs_offline",files);next.ConfigColumns.Single(column=>column.Id=="14").Name="更新后的记录名";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,model.Namespace,NonRedundantApplicationFixture.Root());
            var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(protectedBytes,await File.ReadAllBytesAsync(handwritten));
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(output,"frontend/pages/yzp/mt_nrs_offline_order-list-101.vue")),await File.ReadAllBytesAsync(Path.Combine(output,"backend/generated/frontend/pages/yzp/mt_nrs_offline_order-list-101.vue")));
            var repeated=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(repeated.CanApply);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"NonRedundantSingleSelectionProbe.mjs"));probe.ArgumentList.Add(output);probe.ArgumentList.Add("parent");await NonRedundantApplicationFixture.RunAsync(probe);
            if(files){var fileProbe=NonRedundantApplicationFixture.Start("node",output);fileProbe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"NonRedundantFilesProbe.mjs"));fileProbe.ArgumentList.Add(output);await NonRedundantApplicationFixture.RunAsync(fileProbe);}
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>不支持的存储用途仍在任何完整包文件写入前拒绝，不能因标量放开而静默降级。</summary>
    [Theory]
    [InlineData("global")][InlineData("multi")][InlineData("badTarget")][InlineData("unprovedName")][InlineData("missingRealm")][InlineData("foreignRealm")]
    public void Full_application_preserves_specific_capability_gates(string scenario)
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_nrs_offline");var source=catalog.ConfigColumns.Single(column=>column.Id=="15");
        if(scenario=="global")catalog.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(scenario=="multi")source.ControlKind="MultiSelectPage";
        if(scenario=="badTarget")catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(2);
        if(scenario=="unprovedName")catalog.ConfigColumns.Single(column=>column.Id=="24").LogicalType="int64";
        if(scenario=="missingRealm")catalog.ConfigPages.Single(page=>page.Id=="101").YzProjectId="";
        if(scenario=="foreignRealm")catalog.ConfigPages.Single(page=>page.Id=="201").YzProjectId="55";
        var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-package-reject",Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,directory,"Example.NonRedPackage",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(directory));if(Directory.Exists(Path.GetDirectoryName(directory)))Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(directory)!,Path.GetFileName(directory)+".staging-*"));
    }
}

/// <summary>同一固定元数据用于完整包离线与专属空库实测，不手写业务实体或CRUD。</summary>
internal static class NonRedundantApplicationFixture
{
    /// <summary>单选、type=2回读、同父与独立目标父、追加和覆盖都由元数据生成；菜单属于包内后台。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool files=false)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog(prefix);NonRedundantGeneratedFixture.EnableSingleSelection(catalog,true);NonRedundantGeneratedFixture.AddSelectionPage(catalog);catalog.ConfigPages[0].IsExport=1;NonRedundantGeneratedFixture.EnableImports(catalog);NonRedundantGeneratedFixture.AddMappedSinglePage(catalog);NonRedundantGeneratedFixture.EnableMappedImports(catalog);
        if(files)
        {
            catalog.ConfigColumns.Add(new(){Id="112",ConfigTableId="1",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))catalog.ConfigPageColumns.Add(new(){Id=page.Id+"112",ConfigPageId=page.Id,ConfigColumnId="112",IsShow=1,IsEdit=page.Id=="102"?0:1});
        }
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes[0].Name="独立非冗余后台";
        foreach(var page in catalog.ConfigPages.Where(page=>page.Id is "101" or "104" or "201" or "301"))catalog.ConfigMenus.Add(new(){Id=(900+int.Parse(page.Id)).ToString(),YzProjectId="54",YzAdminTypeId="5",Name="包内页面"+page.Id,Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        return catalog;
    }
    /// <summary>独立restore/build只编译包内路径，关闭共享构建服务，不引用原仓库项目。</summary>
    internal static Task BuildAsync(string output,string operation)
    {
        var info=Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var value in new[]{operation,"backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet"})info.ArgumentList.Add(value);if(operation=="build")info.ArgumentList.Add("--no-restore");return RunAsync(info);
    }
    /// <summary>消费两条日志管道，超时只终止本测试启动的子进程，错误不回显私有配置。</summary>
    internal static async Task RunAsync(ProcessStartInfo info)
    {
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}var outText=await stdout;var errText=await stderr;
        if(info.Environment.TryGetValue("METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY",out var evidence)&&!string.IsNullOrWhiteSpace(evidence))
        {
            // 只在已指定的私有验收目录保留CLI日志，公共断言不回显环境配置。
            var id=Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(Path.Combine(evidence,"process-"+id+".stdout.log"),outText);await File.WriteAllTextAsync(Path.Combine(evidence,"process-"+id+".stderr.log"),errText);
        }
        Assert.True(process.ExitCode==0,"完整非冗余包子进程失败，日志不回显配置。");
    }
    /// <summary>所有工具使用隐藏窗口和UTF8管道，不拼接shell命令。</summary>
    internal static ProcessStartInfo Start(string executable,string directory)=>new(executable){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    /// <summary>可信源码根由测试文件确定。</summary>
    internal static string Root()=>Path.GetFullPath(Path.Combine(TestDirectory(),"../../.."));
    /// <summary>测试脚本来源固定于本程序集源码目录。</summary>
    internal static string TestDirectory([CallerFilePath]string file="")=>Path.GetDirectoryName(file)!;
}
