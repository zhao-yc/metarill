using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>可信管理前端完整资源准备；不以闭包验证代替默认Full、初始化或实际浏览器验收。</summary>
public sealed class TrustedSecurityFrontendAssetsTests
{
    /// <summary>原资源与路由完整进入只读准备，已接可信运行时不虚报缺口，后端适配要求仍显式保留。</summary>
    [Fact]
    public async Task Actual_prepared_frontend_keeps_original_assets_routes_and_passes_own_node_verifier()
    {
        var (root,models)=Setup();var generated=Path.Combine(root,"generated/frontend");
        try
        {
            var before=Snapshot(root);var plan=new FrontendPackager().PrepareSecurityManagement(models,generated,SourceRoot());Assert.Equal(before,Snapshot(root));
            Assert.Equal(4,plan.RequiredBackendAdapters.Count);Assert.Contains(plan.RequiredBackendAdapters,item=>item.Code=="initialize-account");Assert.Empty(plan.BackendGaps);
            var ordinary=GenerationModel.Create(models.Published,"Example.SecurityFrontend");
            Assert.Contains(GenerationCapabilityAudit.Collect(ordinary),gap=>gap.Usage=="runtime"&&gap.Column=="yz_role_ids");
            var files=plan.Files;foreach(var relative in new[]{"pages/main/set_role_permission.vue","components/yzp-permission-workbench/yzp-permission-workbench.vue","components/yzp-permission-legacy/yzp-permission-legacy.vue","common/yzp_runtime/permissionWorkbench.js"})
            {var original=File.ReadAllText(Path.Combine(SourceRoot(),"frontend/metarill_uni_admin",relative)).Replace("\r\n","\n").Replace('\r','\n');Assert.Equal(Encoding.UTF8.GetBytes(original),files[relative]);}
            using var pages=JsonDocument.Parse(files["pages.json"]);var route=Assert.Single(pages.RootElement.GetProperty("pages").EnumerateArray(),p=>p.GetProperty("path").GetString()=="pages/main/set_role_permission");Assert.Equal("设置角色权限",route.GetProperty("style").GetProperty("navigationBarTitleText").GetString());
            var second=new FrontendPackager().PrepareSecurityManagement(models,generated,SourceRoot()).Files;Assert.Equal(files.Keys.Order(),second.Keys.Order());foreach(var pair in files)Assert.Equal(pair.Value,second[pair.Key]);
            files["pages/main/set_role_permission.vue"][0]^=1;Assert.NotEqual(files["pages/main/set_role_permission.vue"],plan.Files["pages/main/set_role_permission.vue"]);
            // 仅测试将公开资源写到私有证据目录，供Node及HBuilderX复核；生产准备入口不发布目录或应用。
            var evidence=Path.Combine(SourceRoot(),".local/trusted-frontend-assets/process-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);var frontend=Path.Combine(evidence,"frontend");foreach(var pair in second){var path=Path.Combine(frontend,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,pair.Value);}
            var start=NonRedundantApplicationFixture.Start("node",frontend);start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=evidence;start.ArgumentList.Add("--experimental-vm-modules");start.ArgumentList.Add("scripts/verify-frontend.mjs");await NonRedundantApplicationFixture.RunAsync(start);
            Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(models.Published));Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(models.Published,Path.Combine(root,"must-not-publish"),"Example.SecurityFrontend",SourceRoot()));Assert.False(Directory.Exists(Path.Combine(root,"must-not-publish")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,Boundary());}
    }
    /// <summary>普通同形对象、未受控生成、路由/资源/归属变化都拒绝，不能借准备接口绕过发布。</summary>
    [Theory]
    [InlineData("forged")][InlineData("ordinary")][InlineData("route")][InlineData("asset")][InlineData("manifest")][InlineData("project")][InlineData("namespace")]
    public async Task Unproved_sources_reject_without_preparation_writes(string defect)
    {
        var (root,models)=Setup();var generated=Path.Combine(root,"generated/frontend");
        try
        {
            if(defect=="forged")models=new(models.Physical,models.Published,models.Binding,models.PendingAdapters);
            if(defect=="ordinary"){var regular=Path.Combine(root,"ordinary");new CSharpGenerator().Generate(models.Published,regular,"Example.SecurityFrontend");generated=Path.Combine(regular,"frontend");}
            if(defect=="route"){var path=Path.Combine(generated,"pages.generated.json");var value=JsonNode.Parse(File.ReadAllText(path))!;value["pages"]!.AsArray().Single(p=>p!["path"]!.GetValue<string>()=="pages/main/set_role_permission")!["style"]!["navigationBarTitleText"]="改写标题";File.WriteAllText(path,value.ToJsonString());}
            if(defect=="asset")File.AppendAllText(Path.Combine(generated,"common/yzp_runtime/permissionWorkbench.js"),"\n// 不属于原资源的改写。\n");
            if(defect=="manifest"){var path=Path.Combine(root,"generated/.metarill-generation.json");var value=JsonNode.Parse(File.ReadAllText(path))!;value["metadataSha256"]=new string('0',64);File.WriteAllText(path,value.ToJsonString());}
            if(defect is "project" or "namespace"){var path=Path.Combine(root,"generated/.metarill-generation.json");var value=JsonNode.Parse(File.ReadAllText(path))!;value[defect=="project"?"projectId":"rootNamespace"]=defect=="project"?"999999":"Bad;Namespace";File.WriteAllText(path,value.ToJsonString());}
            var before=Snapshot(root);
            Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().PrepareSecurityManagement(models,generated,SourceRoot()));
            Assert.Equal(before,Snapshot(root));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,Boundary());}
    }
    /// <summary>仅从当前clone公开演示模型和固定工厂真实生成，不构造可自报的trusted标记。</summary>
    private static (string Root,SecurityManagementModelSet Models) Setup()
    {
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(),"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var module=SecurityManagementModuleFactory.Compose(catalog,catalog.YzAdminTypes[0].Id);var root=Path.Combine(Boundary(),Guid.NewGuid().ToString("N"));new CSharpGenerator().Generate(module,Path.Combine(root,"generated"),"Example.SecurityFrontend");return(root,module);
    }
    /// <summary>逐字节摘要检查准备只读，不以文件数相同替代原输入未改写。</summary>
    private static string Snapshot(string root)=>string.Join("\n",Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path=>Path.GetRelativePath(root,path)+":"+Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));
    /// <summary>所有递归清理均限制在固定临时根下的本测试随机目录。</summary>
    private static string Boundary()=>Path.Combine(Path.GetTempPath(),"metarill-security-frontend");
    /// <summary>当前clone的公开资源目录，不读取私有配置。</summary>
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath]string path="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
}
