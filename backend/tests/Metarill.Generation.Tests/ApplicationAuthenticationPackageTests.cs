using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式Full与既有BackendOnly显式分流；验证物理合成、受管profile和旧项目更新，不启动任何宿主。</summary>
public sealed class ApplicationAuthenticationPackageTests:IAsyncLifetime
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"metarill-auth-package-contract",Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    /// <summary>新Full真实装配10表/安全源码，旧BackendOnly仍4表/原开发宿主；无认证静默降级。</summary>
    [Fact]
    public void Full_web_profile_and_backend_only_original_development_contract_are_distinct()
    {
        var full=Path.Combine(root,"full");var backend=Path.Combine(root,"backend");var catalog=Catalog();var generator=new ApplicationPackager();
        generator.Create(catalog,full,"Example.ProfileContract",SourceRoot());generator.Create(catalog,backend,"Example.ProfileContract",SourceRoot(),ApplicationPackageMode.BackendOnly);
        var fullPhysical=MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(Read(full,"project"),Read(full,"files")),Read(full,"security"));Assert.Equal(10,fullPhysical.ConfigTables.Count);Assert.Equal(128,Read(full,"security").ConfigColumns.Count);Assert.Equal(5,Read(full,"security").ConfigIndexes.Count);Assert.Empty(Read(full,"security").ConfigPages);
        Assert.Equal(4,MetadataCatalogComposition.MergePhysical(Read(backend,"project"),Read(backend,"files")).ConfigTables.Count);Assert.False(File.Exists(Path.Combine(backend,"backend/metadata/security.json")));Assert.False(Directory.Exists(Path.Combine(backend,"src/Metarill.Security")));
        Assert.Equal("web-database-v1",Marker(full)["authenticationProfile"]!.GetValue<string>());Assert.Equal("development-only",Marker(backend)["authenticationProfile"]!.GetValue<string>());
        Assert.Contains("Metarill.Security",File.ReadAllText(Path.Combine(full,"backend/Application.Host/Application.Host.csproj")));Assert.DoesNotContain("Metarill.Runtime",File.ReadAllText(Path.Combine(full,"backend/Application.Host/Application.Host.csproj")));
        Assert.Equal(Normalized(File.ReadAllText(Path.Combine(SourceRoot(),"backend/templates/standalone/DevelopmentProgram.cs.tpl")).Replace("{{NAMESPACE}}","Example.ProfileContract")),File.ReadAllText(Path.Combine(backend,"backend/Application.Host/Program.cs")));
        Assert.Equal(Normalized(File.ReadAllText(Path.Combine(SourceRoot(),"backend/templates/standalone/DevelopmentSettings.json.tpl"))),File.ReadAllText(Path.Combine(backend,"backend/Application.Host/appsettings.example.json")));
        foreach(var path in new[]{"WebAuthentication.cs","SecurityInitialization.cs","PackageSecurityCatalog.cs","PackageSecurityCatalogBinding.g.cs"}){Assert.True(File.Exists(Path.Combine(full,"backend/Application.Host",path)));Assert.False(File.Exists(Path.Combine(backend,"backend/Application.Host",path)));}
        Assert.False(Directory.Exists(Path.Combine(full,".local")));Assert.DoesNotContain(Directory.EnumerateFiles(full,"*",SearchOption.AllDirectories),path=>path.EndsWith("appsettings.local.json",StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>平台BackendOnly导出继续可用；安全页面不借新宿主解除Full物理/功能门禁。</summary>
    [Fact]
    public void Platform_backend_only_still_exports_while_full_security_page_models_fail_before_stage()
    {
        var platform=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(),"backend/metadata/platform.json")),Json)!;var backend=Path.Combine(root,"platform-backend");new ApplicationPackager().Create(platform,backend,"Example.PlatformContract",SourceRoot(),ApplicationPackageMode.BackendOnly);Assert.False(File.Exists(Path.Combine(backend,"backend/metadata/security.json")));
        var full=Path.Combine(root,"platform-full");var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(platform,full,"Example.PlatformContract",SourceRoot()));Assert.Contains("独立前端缺少已接线的自定义控件",error.Message);Assert.False(Directory.Exists(full));Assert.Empty(Directory.GetDirectories(root,"platform-full.staging-*"));
        var collision=Catalog();collision.ConfigTables[0].TableName="yz_manager";var rejected=Path.Combine(root,"collision");Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(collision,rejected,"Example.BadProfile",SourceRoot()));Assert.False(Directory.Exists(rejected));
    }

    /// <summary>具体页面门禁提前不豁免物理归属；无其它缺口的原项目页仍禁止通过Full公开安全基础表。</summary>
    [Fact]
    public void Full_without_frontend_gaps_still_rejects_security_table_pages_before_stage()
    {
        var catalog=TemplateVersionCatalogTests.Catalog();new FrontendPackager().Validate(catalog);
        var output=Path.Combine(root,"security-page");var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.SecurityPage",SourceRoot()));
        Assert.Contains("基础设施表不能通过业务页面开放",error.Message);Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(root)&&Directory.EnumerateDirectories(root,"security-page.staging-*").Any());
    }

    /// <summary>新Full的两层清单与生成归属第一次预览即逐字空操作，不能把profile重排计作提交。</summary>
    [Fact]
    public void Factory_full_profile_preview_and_apply_preserve_all_marker_bytes_without_normalization()
    {
        var (target,candidate)=Pair();var markers=new[]{".metarill-package.json","frontend/.metarill-frontend.json","backend/generated/.metarill-generation.json"};
        var before=markers.ToDictionary(path=>path,path=>File.ReadAllBytes(Path.Combine(target,path)),StringComparer.Ordinal);
        Assert.Equal("web-database-v1",Marker(target)["authenticationProfile"]!.GetValue<string>());
        var updater=new LocalGenerationUpdater();
        for(var round=0;round<2;round++)
        {
            var plan=updater.Preview(target,candidate,["generate-project-config","sync-runtime","generate-all-page"]);
            Assert.True(plan.CanApply);Assert.Empty(plan.RequiredActions);Assert.DoesNotContain(plan.Files,file=>file.Action!="preserve");
            var receipt=updater.Apply(plan,plan.PlanSha256);Assert.Equal("unchanged",receipt.Status);Assert.Equal(0,receipt.WrittenFiles);
            foreach(var path in markers)Assert.Equal(before[path],File.ReadAllBytes(Path.Combine(target,path)));
        }
        Assert.False(Directory.Exists(Path.Combine(target,".local/metarill-updates/transactions")));
    }

    /// <summary>旧Full归属读取仍支持，只有显式sync-runtime完整依赖才原子升级profile，AppID/私有配置不动。</summary>
    [Fact]
    public void Legacy_full_target_requires_runtime_scope_and_preserves_private_configuration_during_profile_upgrade()
    {
        var (target,candidate)=Pair();Legacy(target);var privatePath=Path.Combine(target,"backend/Application.Host/appsettings.local.json");File.WriteAllText(privatePath,"{\"privateMarker\":\"must-preserve\"}");var manifest=Path.Combine(target,"frontend/manifest.json");var appid=JsonNode.Parse(File.ReadAllText(manifest))!;appid["appid"]="__UNI__LOCAL_OWNED_AUTH";File.WriteAllText(manifest,appid.ToJsonString(Json));var bytes=File.ReadAllBytes(manifest);
        var updater=new LocalGenerationUpdater();Assert.Equal(Catalog().YzProjectId,updater.InspectTarget(target).ProjectId);var runtimeOnly=updater.Preview(target,candidate,["sync-runtime"]);Assert.False(runtimeOnly.CanApply);Assert.Contains("generate-project-config",runtimeOnly.RequiredActions);var pending=updater.Preview(target,candidate,["generate-project-config"]);Assert.False(pending.CanApply);Assert.Contains("sync-runtime",pending.RequiredActions);
        var plan=updater.Preview(target,candidate,["sync-runtime","generate-project-config"]);Assert.True(plan.CanApply);Assert.Contains(plan.Files,file=>file.Path=="backend/metadata/security.json"&&file.Action=="create");Assert.Contains(plan.Files,file=>file.Path=="src/Metarill.Security/Metarill.Security.csproj"&&file.Scope=="sync-runtime");Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);
        Assert.Equal("web-database-v1",Marker(target)["authenticationProfile"]!.GetValue<string>());Assert.Equal("web-database-v1",JsonNode.Parse(File.ReadAllText(Path.Combine(target,"frontend/.metarill-frontend.json")))!["authentication"]!.GetValue<string>());Assert.Equal(bytes,File.ReadAllBytes(manifest));Assert.Contains("must-preserve",File.ReadAllText(privatePath));Assert.DoesNotContain(updater.Preview(target,candidate,["sync-runtime"]).Files,file=>file.Action!="preserve");
    }

    /// <summary>新增安全闭包不能通过同时删文件和删归属规避，失配profile不能伪装旧包。</summary>
    [Theory]
    [InlineData("metadata")] [InlineData("host")] [InlineData("library")] [InlineData("profile")] [InlineData("catalog")]
    public void Web_profile_requires_all_bootstrap_ownership_and_consistent_markers(string kind)
    {
        var (target,candidate)=Pair();var marker=Marker(candidate);var path=kind switch{"metadata"=>"backend/metadata/security.json","host"=>"backend/Application.Host/WebAuthentication.cs","catalog"=>"backend/Application.Host/PackageSecurityCatalogBinding.g.cs",_=>"src/Metarill.Security/Metarill.Security.csproj"};
        if(kind=="profile")marker["authenticationProfile"]="development-only";else{File.Delete(Path.Combine(candidate,path));var rows=marker["files"]!.AsArray();rows.Remove(rows.Single(row=>row!["path"]!.GetValue<string>()==path));}File.WriteAllText(Path.Combine(candidate,".metarill-package.json"),marker.ToJsonString(Json));Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Preview(target,candidate,["sync-runtime"]));
    }

    /// <summary>运行时新安全文件的真实失败回滚路径也经严格journal识别，不能变成不可恢复的未知文件。</summary>
    [Fact]
    public void Profile_upgrade_failure_restores_legacy_files_and_security_payload_is_recoverable()
    {
        var (target,candidate)=Pair();Legacy(target);var marker=File.ReadAllBytes(Path.Combine(target,".metarill-package.json"));var updater=new LocalGenerationUpdater{AfterWrite=path=>{if(path.Contains("src/Metarill.Security/",StringComparison.Ordinal))throw new IOException("模拟安全源码发布后故障");}};var plan=updater.Preview(target,candidate,["sync-runtime","generate-project-config"]);Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.Equal(marker,File.ReadAllBytes(Path.Combine(target,".metarill-package.json")));Assert.False(Directory.Exists(Path.Combine(target,"src/Metarill.Security"))&&Directory.EnumerateFiles(Path.Combine(target,"src/Metarill.Security"),"*",SearchOption.AllDirectories).Any());Assert.True(new LocalGenerationUpdater().Preview(target,candidate,["sync-runtime","generate-project-config"]).CanApply);
    }
    /// <summary>目录绑定属于配置生成，不让正常配置按钮新增运行时动作；两文件必须共同原子更新。</summary>
    [Fact]
    public void ImmutableCatalog_single_config_action_updates_metadata_and_compiled_binding_together()
    {
        var target=Path.Combine(root,"config-target");var candidate=Path.Combine(root,"config-candidate");var next=Catalog();next.YzProjectName+="配置新版";
        var packager=new ApplicationPackager();packager.Create(Catalog(),target,"Example.ConfigAuthority",SourceRoot());packager.Create(next,candidate,"Example.ConfigAuthority",SourceRoot());
        const string binding="backend/Application.Host/PackageSecurityCatalogBinding.g.cs",runtime="backend/Application.Host/PackageSecurityCatalog.cs";
        var previousRuntime=File.ReadAllBytes(Path.Combine(target,runtime));var previousBinding=File.ReadAllBytes(Path.Combine(target,binding));
        // 手写Host/Service及AppID属于开发者，配置更新只替换两项固定权威文件。
        var custom=Path.Combine(target,"backend/Application.Host/HostCustomization.cs");File.WriteAllText(custom,"namespace Example.ConfigAuthority.Host; public static partial class HostCustomization { internal const string PreservedMarker=\"手写\"; }\n");
        var service=Directory.GetFiles(Path.Combine(target,"backend/generated/Services"),"*Service.cs").Single();File.AppendAllText(service,"\n// 保留业务手写扩展。\n");
        var appidPath=Path.Combine(target,"frontend/manifest.json");var appid=JsonNode.Parse(File.ReadAllText(appidPath))!;appid["appid"]="__UNI__CONFIG_OWNED_AUTH";File.WriteAllText(appidPath,appid.ToJsonString(Json));
        var protectedBytes=new[]{custom,service,appidPath}.ToDictionary(path=>path,File.ReadAllBytes);
        var updater=new LocalGenerationUpdater();var plan=updater.Preview(target,candidate,["generate-project-config"]);Assert.True(plan.CanApply);Assert.Empty(plan.RequiredActions);
        Assert.Contains(plan.Files,file=>file.Path==binding&&file.Action=="replace"&&file.Scope=="generate-project-config");Assert.Contains(plan.Files,file=>file.Path=="backend/metadata/project.json"&&file.Action=="replace");
        Assert.DoesNotContain(plan.Files,file=>file.Scope=="sync-runtime");Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);
        Assert.Equal(previousRuntime,File.ReadAllBytes(Path.Combine(target,runtime)));Assert.NotEqual(previousBinding,File.ReadAllBytes(Path.Combine(target,binding)));
        foreach(var item in protectedBytes)Assert.Equal(item.Value,File.ReadAllBytes(item.Key));
        Assert.DoesNotContain(updater.Preview(target,candidate,["generate-project-config"]).Files,file=>file.Action!="preserve");
    }

    /// <summary>已发布Web目标尚无固定目录实现/绑定时，显式补齐两项动作；不把普通Config按钮永久依赖Runtime。</summary>
    [Fact]
    public void ImmutableCatalog_old_web_target_requires_one_explicit_runtime_and_config_upgrade()
    {
        var(target,candidate)=Pair();var marker=Marker(target);marker.AsObject().Remove("securityCatalogSha256");
        foreach(var path in new[]{"backend/Application.Host/PackageSecurityCatalog.cs","backend/Application.Host/PackageSecurityCatalogBinding.g.cs"})
        {
            File.Delete(Path.Combine(target,path));var files=marker["files"]!.AsArray();files.Remove(files.Single(row=>row!["path"]!.GetValue<string>()==path));
        }
        File.WriteAllText(Path.Combine(target,".metarill-package.json"),marker.ToJsonString(Json));
        var updater=new LocalGenerationUpdater();Assert.Equal("web-database-v1",Marker(target)["authenticationProfile"]!.GetValue<string>());
        var config=updater.Preview(target,candidate,["generate-project-config"]);Assert.False(config.CanApply);Assert.Contains("sync-runtime",config.RequiredActions);
        var runtime=updater.Preview(target,candidate,["sync-runtime"]);Assert.False(runtime.CanApply);Assert.Contains("generate-project-config",runtime.RequiredActions);
        var complete=updater.Preview(target,candidate,["generate-project-config","sync-runtime"]);Assert.True(complete.CanApply);Assert.Equal("committed",updater.Apply(complete,complete.PlanSha256).Status);
        Assert.DoesNotContain(updater.Preview(target,candidate,["generate-project-config"]).Files,file=>file.Action!="preserve");
    }

    /// <summary>重算包manifest不能代替编译绑定；缺文件、改常量或仅改完整JSON都在预览拒绝。</summary>
    [Theory]
    [InlineData("metadata")][InlineData("binding")][InlineData("manifest")][InlineData("missing")]
    public void ImmutableCatalog_candidate_requires_exact_compiled_binding_not_self_reported_manifest(string kind)
    {
        var(target,candidate)=Pair();var marker=Marker(candidate);const string binding="backend/Application.Host/PackageSecurityCatalogBinding.g.cs";
        if(kind=="manifest")marker["securityCatalogSha256"]=new string('0',64);
        else if(kind=="metadata")File.AppendAllText(Path.Combine(candidate,"backend/metadata/project.json"),"\n");
        else if(kind=="binding")File.WriteAllText(Path.Combine(candidate,binding),File.ReadAllText(Path.Combine(candidate,binding)).Replace(marker["securityCatalogSha256"]!.GetValue<string>(),new string('0',64)));
        else File.Delete(Path.Combine(candidate,binding));
        var files=marker["files"]!.AsArray();foreach(var row in files.ToArray())
        {
            var path=row!["path"]!.GetValue<string>();if(!File.Exists(Path.Combine(candidate,path)))files.Remove(row);else row["sha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(candidate,path))));
        }
        File.WriteAllText(Path.Combine(candidate,".metarill-package.json"),marker.ToJsonString(Json));
        var error=Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Preview(target,candidate,["generate-project-config","sync-runtime"]));Assert.Contains("固定目录",error.Message);
    }

    private (string Target,string Candidate) Pair(){var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");new ApplicationPackager().Create(Catalog(),target,"Example.AuthUpgrade",SourceRoot());new ApplicationPackager().Create(Catalog(),candidate,"Example.AuthUpgrade",SourceRoot());return(target,candidate);}
    /// <summary>从受控旧模板构造已交付开发profile夹具，不读取旧私有输出，不修改业务页或已有元数据。</summary>
    private static void Legacy(string directory)
    {
        var marker=Marker(directory);var ns=marker["rootNamespace"]!.GetValue<string>();foreach(var pair in new[]{("DevelopmentProgram.cs.tpl","Program.cs"),("DevelopmentNavigation.cs.tpl","DevelopmentNavigation.cs")})File.WriteAllText(Path.Combine(directory,"backend/Application.Host",pair.Item2),Normalized(File.ReadAllText(Path.Combine(SourceRoot(),"backend/templates/standalone",pair.Item1)).Replace("{{NAMESPACE}}",ns)));
        var host=Path.Combine(directory,"backend/Application.Host/Application.Host.csproj");File.WriteAllText(host,File.ReadAllText(host).Replace("<ProjectReference Include=\"../../src/Metarill.Security/Metarill.Security.csproj\" />","").Replace("<ItemGroup><Content Include=\"../metadata/security.json\" Link=\"metadata/security.json\" CopyToOutputDirectory=\"PreserveNewest\" /></ItemGroup>",""));
        foreach(var path in new[]{"backend/metadata/security.json","backend/Application.Host/WebAuthentication.cs","backend/Application.Host/SecurityInitialization.cs","backend/Application.Host/PackageSecurityCatalog.cs","backend/Application.Host/PackageSecurityCatalogBinding.g.cs"})File.Delete(Path.Combine(directory,path));Directory.Delete(Path.Combine(directory,"src/Metarill.Security"),true);
        var frontendPath=Path.Combine(directory,"frontend/.metarill-frontend.json");var frontend=JsonNode.Parse(File.ReadAllText(frontendPath))!;frontend["authentication"]="development-only";File.WriteAllText(frontendPath,frontend.ToJsonString(Json)+"\n");marker.AsObject().Remove("authenticationProfile");marker.AsObject().Remove("securityCatalogSha256");var files=marker["files"]!.AsArray();foreach(var row in files.ToArray()){var path=row!["path"]!.GetValue<string>();if(!File.Exists(Path.Combine(directory,path)))files.Remove(row);else row["sha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,path))));}File.WriteAllText(Path.Combine(directory,".metarill-package.json"),marker.ToJsonString(Json)+"\n");
    }
    private static JsonNode Marker(string path)=>JsonNode.Parse(File.ReadAllText(Path.Combine(path,".metarill-package.json")))!;
    private static MetadataCatalog Read(string root,string name)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(root,"backend/metadata",name+".json")),Json)!;
    private static MetadataCatalog Catalog()=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(),"backend/metadata/demo.json")),Json)!;
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    private static string Normalized(string text)=>text.Replace("\r\n","\n").Replace('\r','\n');
    public Task InitializeAsync()=>Task.CompletedTask;
    public Task DisposeAsync()=>TestDirectoryCleanup.DeleteAsync(root,Path.Combine(Path.GetTempPath(),"metarill-auth-package-contract"));
}
