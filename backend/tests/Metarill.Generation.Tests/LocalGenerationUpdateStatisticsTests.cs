using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>旧SDK缺统计数学依赖时必须显式同步运行时和配置，不能只下发引用新类型的页面。</summary>
    [Fact]
    public void Statistics_upgrade_requires_missing_core_runtime_and_preserves_handwritten_service()
    {
        var old=Catalog();var next=Catalog();next.ConfigPages[0].Extra["isSum"]=JsonSerializer.SerializeToElement(1);
        var amount=next.ConfigColumns.Single(column=>column.ColumnName=="amount");
        next.ConfigPageColumns.Single(field=>field.ConfigColumnId==amount.Id).Extra["sumKind"]=JsonSerializer.SerializeToElement(1);
        var(target,candidate)=Packages(old,next);const string runtime="src/Metarill.Core/Contracts/LegacyStatisticsMath.cs";
        var marker=Path.Combine(target,".metarill-package.json");var data=JsonNode.Parse(File.ReadAllText(marker))!;
        var files=data["files"]!.AsArray();files.Remove(files.Single(file=>file!["path"]!.GetValue<string>()==runtime));
        File.Delete(Path.Combine(target,runtime));File.WriteAllText(marker,data.ToJsonString(Json));
        var handwritten=Path.Combine(target,"backend/generated/Services/DemoCustomerService.cs");File.AppendAllText(handwritten,"\n// 统计升级必须保护本地业务扩展。\n");
        var before=SHA256.HashData(File.ReadAllBytes(handwritten));var updater=new LocalGenerationUpdater();
        var pages=updater.Preview(target,candidate,["generate-all-page"]);Assert.False(pages.CanApply);
        Assert.Contains("sync-runtime",pages.RequiredActions);Assert.Contains("generate-project-config",pages.RequiredActions);
        var complete=updater.Preview(target,candidate,All);Assert.True(complete.CanApply);
        Assert.Contains(complete.Files,file=>file.Path==runtime&&file.Action=="create"&&file.Scope=="sync-runtime");
        Assert.Equal("committed",updater.Apply(complete,complete.PlanSha256).Status);
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(handwritten)));Assert.True(File.Exists(Path.Combine(target,runtime)));
        Assert.DoesNotContain(updater.Preview(target,candidate,All).Files,file=>file.Action!="preserve");
    }
}
