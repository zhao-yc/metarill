using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>级联Excel新增依赖必须随候选完整下发，双删文件及归属条目也不能绕过。</summary>
    [Theory]
    [InlineData("src/Metarill.Core/Cascades/CrossTableExcelPath.cs")]
    [InlineData("src/Metarill.Core/Cascades/CrossTableExcelRow.cs")]
    public void Candidate_requires_lossless_cross_excel_without_touching_target(string path)
    {
        var(target,candidate)=Packages(Catalog(),Catalog());var updater=new LocalGenerationUpdater();
        Assert.True(updater.Preview(target,candidate,All).CanApply);var before=SourceSnapshot(target);
        var marker=Path.Combine(candidate,".metarill-package.json");var data=JsonNode.Parse(File.ReadAllText(marker))!;
        var entries=data["files"]!.AsArray();entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));
        File.Delete(Path.Combine(candidate,path));File.WriteAllText(marker,data.ToJsonString(Json));
        Assert.Contains(path,Assert.Throws<InvalidOperationException>(()=>updater.Preview(target,candidate,All)).Message);
        Assert.Equal(before,SourceSnapshot(target));
    }

    /// <summary>旧宿主升级补齐源头依赖，不能只更页面；手写partial保持原字节。</summary>
    [Fact]
    public void Old_target_lossless_cross_excel_upgrade_requires_runtime_and_preserves_partial()
    {
        var(target,candidate)=Packages(Catalog(),Catalog());
        var paths=new[]{"src/Metarill.Core/Cascades/CrossTableExcelPath.cs","src/Metarill.Core/Cascades/CrossTableExcelRow.cs"};
        var marker=Path.Combine(target,".metarill-package.json");var data=JsonNode.Parse(File.ReadAllText(marker))!;var entries=data["files"]!.AsArray();
        foreach(var path in paths){entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(target,path));}
        File.WriteAllText(marker,data.ToJsonString(Json));
        var hand=Path.Combine(target,"backend/generated/Services/DemoCustomerService.cs");
        File.AppendAllText(hand,Environment.NewLine+"// 级联Excel升级保留手写业务。"+Environment.NewLine);var before=File.ReadAllBytes(hand);
        var updater=new LocalGenerationUpdater();var pages=updater.Preview(target,candidate,["generate-all-page"]);
        Assert.False(pages.CanApply);Assert.Contains("sync-runtime",pages.RequiredActions);
        var plan=updater.Preview(target,candidate,All);Assert.True(plan.CanApply);Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);
        Assert.Equal(before,File.ReadAllBytes(hand));foreach(var path in paths)Assert.True(File.Exists(Path.Combine(target,path)));
    }
}
