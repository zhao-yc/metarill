using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>旧完整宿主通过原运行时升级接回R字段错误合同，生成页面单独更新不能遗漏Program。</summary>
    [Fact]
    public void Model_state_contract_upgrade_updates_owned_host_and_preserves_partial()
    {
        var(target,candidate)=Packages(Catalog(),Catalog());const string program="backend/Application.Host/Program.cs";
        var old=File.ReadAllText(Path.Combine(target,program));
        var lines=old.Split('\n').Where(line=>!line.Contains("原管理页MVC解析/绑定错误沿平台API",StringComparison.Ordinal)&&!line.Contains("options.InvalidModelStateResponseFactory",StringComparison.Ordinal));
        var historical=string.Join("\n",lines);Assert.NotEqual(old,historical);File.WriteAllText(Path.Combine(target,program),historical);
        var marker=Path.Combine(target,".metarill-package.json");var manifest=JsonNode.Parse(File.ReadAllText(marker))!;
        manifest["files"]!.AsArray().Single(entry=>entry!["path"]!.GetValue<string>()==program)!["sha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(target,program))));File.WriteAllText(marker,manifest.ToJsonString(Json));
        var handwritten=Path.Combine(target,"backend/generated/Services/DemoCustomerService.cs");File.AppendAllText(handwritten,"\n// R合同升级保留手写业务。\n");var before=File.ReadAllBytes(handwritten);
        var updater=new LocalGenerationUpdater();var pages=updater.Preview(target,candidate,["generate-all-page"]);Assert.False(pages.CanApply);Assert.Contains("sync-runtime",pages.RequiredActions);
        var plan=updater.Preview(target,candidate,All);Assert.True(plan.CanApply);Assert.Contains(plan.Files,file=>file.Path==program&&file.Action=="replace"&&file.Scope=="sync-runtime");
        Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(File.ReadAllBytes(Path.Combine(candidate,program)),File.ReadAllBytes(Path.Combine(target,program)));Assert.Equal(before,File.ReadAllBytes(handwritten));
    }
}
