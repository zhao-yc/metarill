using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;
public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>公共Agent引用的新解释器不能被候选同时裁掉源码和归属；目标工程保持原样。</summary>
    [Theory]
    [InlineData("src/Metarill.Core/Metadata/ReadOnlyConditionRule.cs")]
    [InlineData("src/Metarill.Core/Metadata/JavaDecimalNumber.cs")]
    [InlineData("src/Metarill.Core/Contracts/GeneratedAgentWriteAccessAttribute.cs")]
    [InlineData("src/Metarill.Agent/AgentRecordWriteBinding.cs")]
    [InlineData("src/Metarill.Agent/AgentRecordValues.cs")]
    public void Candidate_cannot_remove_readonly_condition_runtime_and_manifest_together(string path)
    {
        var(target,candidate)=Packages(Catalog(),Catalog());var updater=new LocalGenerationUpdater();var before=SourceSnapshot(target);
        var marker=Path.Combine(candidate,".metarill-package.json");var manifest=JsonNode.Parse(File.ReadAllText(marker))!;var entries=manifest["files"]!.AsArray();entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(candidate,path));File.WriteAllText(marker,manifest.ToJsonString(Json));
        var error=Assert.Throws<InvalidOperationException>(()=>updater.Preview(target,candidate,All));Assert.Contains(path,error.Message);Assert.Equal(before,SourceSnapshot(target));
    }
}
