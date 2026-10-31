using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>当前候选必须带齐两种时间合同，即使本轮普通模型没有time字段也不能双删绕过。</summary>
    [Theory]
    [InlineData("src/Metarill.Core/Contracts/BinaryFieldValue.cs")][InlineData("src/Metarill.Data/Schema/MySqlBinaryDefault.cs")]
    [InlineData("src/Metarill.Core/Contracts/ClockTimeValue.cs")][InlineData("src/Metarill.Core/Contracts/ClockTimeJsonConverter.cs")]
    [InlineData("src/Metarill.Core/Contracts/SqlTimeValue.cs")][InlineData("src/Metarill.Core/Contracts/SqlTimeJsonConverter.cs")][InlineData("src/Metarill.Data/MySqlPersistedTimeValues.cs")]
    public void Candidate_requires_complete_time_contract_without_touching_target(string file)
    {
        var(target,candidate)=Packages(Catalog(),Catalog());var updater=new LocalGenerationUpdater();Assert.True(updater.Preview(target,candidate,All).CanApply);var before=SourceSnapshot(target);
        var path=file;var marker=Path.Combine(candidate,".metarill-package.json");var data=JsonNode.Parse(File.ReadAllText(marker))!;
        var entries=data["files"]!.AsArray();entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(candidate,path));File.WriteAllText(marker,data.ToJsonString(Json));
        Assert.Contains(path,Assert.Throws<InvalidOperationException>(()=>updater.Preview(target,candidate,All)).Message);Assert.Equal(before,SourceSnapshot(target));
    }
    /// <summary>旧目标缺二进制合同必须同步运行时，手写partial保持。</summary>
    [Fact]
    public void Old_target_binary_upgrade_requires_runtime_and_preserves_partial()
    {
        var(target,candidate)=Packages(Catalog(),Catalog());var path="src/Metarill.Core/Contracts/BinaryFieldValue.cs";
        var marker=Path.Combine(target,".metarill-package.json");var data=JsonNode.Parse(File.ReadAllText(marker))!;var files=data["files"]!.AsArray();files.Remove(files.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(target,path));File.WriteAllText(marker,data.ToJsonString(Json));
        var binarySql="src/Metarill.Data/Schema/MySqlBinaryDefault.cs";files.Remove(files.Single(entry=>entry!["path"]!.GetValue<string>()==binarySql));File.Delete(Path.Combine(target,binarySql));File.WriteAllText(marker,data.ToJsonString(Json));
        var hand=Path.Combine(target,"backend/generated/Services/DemoCustomerService.cs");File.AppendAllText(hand,"\n// 二进制升级保留手写扩展。\n");var before=File.ReadAllBytes(hand);
        var updater=new LocalGenerationUpdater();var pages=updater.Preview(target,candidate,["generate-all-page"]);Assert.False(pages.CanApply);Assert.Contains("sync-runtime",pages.RequiredActions);
        var all=updater.Preview(target,candidate,All);Assert.True(all.CanApply);Assert.Equal("committed",updater.Apply(all,all.PlanSha256).Status);Assert.Equal(before,File.ReadAllBytes(hand));Assert.True(File.Exists(Path.Combine(target,path)));Assert.True(File.Exists(Path.Combine(target,binarySql)));
    }
    /// <summary>旧目标无时间合同仍能预览升级；仅更页面必须要求同步运行时，保护手写partial。</summary>
    [Fact]
    public void Old_target_time_upgrade_requires_runtime_and_preserves_partial()
    {
        var(target,candidate)=Packages(Catalog(),Catalog());
        var marker=Path.Combine(target,".metarill-package.json");var data=JsonNode.Parse(File.ReadAllText(marker))!;var files=data["files"]!.AsArray();
        foreach(var file in new[]{"ClockTimeValue.cs","ClockTimeJsonConverter.cs","SqlTimeValue.cs","SqlTimeJsonConverter.cs"})
        {var path="src/Metarill.Core/Contracts/"+file;files.Remove(files.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(target,path));}
        var persisted="src/Metarill.Data/MySqlPersistedTimeValues.cs";files.Remove(files.Single(entry=>entry!["path"]!.GetValue<string>()==persisted));File.Delete(Path.Combine(target,persisted));
        File.WriteAllText(marker,data.ToJsonString(Json));var hand=Path.Combine(target,"backend/generated/Services/DemoCustomerService.cs");File.AppendAllText(hand,"\n// 时间升级保留现有手写业务。\n");var before=File.ReadAllBytes(hand);
        var updater=new LocalGenerationUpdater();var pages=updater.Preview(target,candidate,["generate-all-page"]);Assert.False(pages.CanApply);Assert.Contains("sync-runtime",pages.RequiredActions);
        var all=updater.Preview(target,candidate,All);Assert.True(all.CanApply);Assert.Equal("committed",updater.Apply(all,all.PlanSha256).Status);
        Assert.Equal(before,File.ReadAllBytes(hand));Assert.True(File.Exists(Path.Combine(target,"src/Metarill.Core/Contracts/SqlTimeValue.cs")));
    }
}
