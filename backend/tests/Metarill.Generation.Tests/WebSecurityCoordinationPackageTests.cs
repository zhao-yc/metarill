using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式协调属于Runtime源码更新，旧Full显式补齐而不碰手写Host、Service、私有配置或AppID。</summary>
public sealed partial class ApplicationAuthenticationPackageTests
{
    /// <summary>旧Web包没有协调源码时只需明确sync-runtime，应用后配置按钮仍不额外依赖Runtime。</summary>
    [Fact]
    public void WebCoordination_old_full_explicit_runtime_upgrade_preserves_developer_files()
    {
        var(target,candidate)=Pair();OldCoordination(target);
        var custom=Path.Combine(target,"backend/Application.Host/HostCustomization.cs");File.WriteAllText(custom,"namespace Example.AuthUpgrade.Host; public static partial class HostCustomization { internal const string Marker=\"手写保留\"; }\n");
        var service=Directory.GetFiles(Path.Combine(target,"backend/generated/Services"),"*Service.cs").Single();File.AppendAllText(service,"\n// 保留手写业务。\n");
        var privateConfig=Path.Combine(target,"backend/Application.Host/appsettings.local.json");File.WriteAllText(privateConfig,"{\"privateMarker\":\"只保留，不读取凭据\"}");
        var appidPath=Path.Combine(target,"frontend/manifest.json");var appid=JsonNode.Parse(File.ReadAllText(appidPath))!;appid["appid"]="__UNI__COORDINATION_OWNED";File.WriteAllText(appidPath,appid.ToJsonString(Json));
        var expected=new[]{custom,service,privateConfig,appidPath}.ToDictionary(path=>path,File.ReadAllBytes);var updater=new LocalGenerationUpdater();var config=updater.Preview(target,candidate,["generate-project-config"]);Assert.False(config.CanApply);Assert.Contains("sync-runtime",config.RequiredActions);
        var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.True(plan.CanApply);Assert.Empty(plan.RequiredActions);foreach(var path in CoordinationFiles)Assert.Contains(plan.Files,file=>file.Path==path&&file.Action=="create"&&file.Scope=="sync-runtime");Assert.Contains(plan.Files,file=>file.Path=="backend/Application.Host/Program.cs"&&file.Action=="replace");
        Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);foreach(var pair in expected)Assert.Equal(pair.Value,File.ReadAllBytes(pair.Key));Assert.DoesNotContain(updater.Preview(target,candidate,["generate-project-config"]).Files,file=>file.Action!="preserve");
    }

    /// <summary>新候选不能同时删除源码与清单，伪称仍有完整Web协调闭包。</summary>
    [Theory]
    [InlineData("backend/Application.Host/SessionPolicy.cs")][InlineData("backend/Application.Host/WebSecurityCoordination.cs")]
    public void WebCoordination_candidate_requires_both_fixed_runtime_files(string missing)
    {
        var(target,candidate)=Pair();var marker=Marker(candidate);File.Delete(Path.Combine(candidate,missing));var files=marker["files"]!.AsArray();files.Remove(files.Single(file=>file!["path"]!.GetValue<string>()==missing));File.WriteAllText(Path.Combine(candidate,".metarill-package.json"),marker.ToJsonString(Json));
        var failure=Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Preview(target,candidate,["sync-runtime"]));Assert.Contains("必要启动归属",failure.Message);Assert.Contains(missing,failure.Message);
    }

    /// <summary>新增协调文件写入后发生故障仍走原journal严格回滚，不遗留半注册宿主。</summary>
    [Fact]
    public void WebCoordination_runtime_failure_rolls_back_new_files_and_original_program()
    {
        var(target,candidate)=Pair();OldCoordination(target);var paths=new[]{".metarill-package.json","backend/Application.Host/Program.cs","backend/metadata/project.json","backend/Application.Host/PackageSecurityCatalogBinding.g.cs"};var original=paths.ToDictionary(path=>path,path=>File.ReadAllBytes(Path.Combine(target,path)));
        var updater=new LocalGenerationUpdater{AfterWrite=path=>{if(path==CoordinationFiles[1])throw new IOException("模拟协调源码写入后故障");}};var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.True(plan.CanApply);Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));
        foreach(var file in CoordinationFiles)Assert.False(File.Exists(Path.Combine(target,file)));foreach(var item in original)Assert.Equal(item.Value,File.ReadAllBytes(Path.Combine(target,item.Key)));Assert.True(new LocalGenerationUpdater().Preview(target,candidate,["sync-runtime"]).CanApply);
    }

    private static readonly string[] CoordinationFiles=["backend/Application.Host/SessionPolicy.cs","backend/Application.Host/WebSecurityCoordination.cs"];
    /// <summary>以刚发布的旧Web宿主构造精确前版本：仅移除两新增文件，并还原原授权注册语句及归属摘要。</summary>
    private static void OldCoordination(string directory)
    {
        var program=Path.Combine(directory,"backend/Application.Host/Program.cs");var text=File.ReadAllText(program);Assert.Contains("else ApplicationWebSecurityCoordination.Add(builder.Services,authenticationMode);",text);File.WriteAllText(program,text.Replace("else ApplicationWebSecurityCoordination.Add(builder.Services,authenticationMode);","else builder.Services.AddScoped<IRuntimeAuthorization,ApplicationWebAuthorization>();"));
        foreach(var path in CoordinationFiles)File.Delete(Path.Combine(directory,path));var marker=Marker(directory);var files=marker["files"]!.AsArray();foreach(var row in files.ToArray()){var path=row!["path"]!.GetValue<string>();if(!File.Exists(Path.Combine(directory,path)))files.Remove(row);else row["sha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,path))));}File.WriteAllText(Path.Combine(directory,".metarill-package.json"),marker.ToJsonString(Json));
    }
}
