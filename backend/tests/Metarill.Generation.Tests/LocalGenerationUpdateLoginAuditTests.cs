using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>当前候选删掉审计源码及归属仍须拒绝；目标文件、手写代码与配置都不能被触及。</summary>
    [Theory][InlineData("src/Metarill.Security/AuthenticationService.LoginAudit.cs")][InlineData("src/Metarill.Security/LoginCapVerifier.cs")][InlineData("src/Metarill.Security/LoginAttemptState.cs")][InlineData("src/Metarill.Security/RedisLoginAttemptStore.cs")]
    public void Candidate_requires_original_login_audit_runtime_without_touching_target(string path)
    {
        var(target,candidate)=Packages(Catalog(),Catalog());
        var marker=Path.Combine(candidate,".metarill-package.json");var manifest=JsonNode.Parse(File.ReadAllText(marker))!;var entries=manifest["files"]!.AsArray();
        entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(candidate,path));File.WriteAllText(marker,manifest.ToJsonString(Json));
        var before=SourceSnapshot(target);var error=Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Preview(target,candidate,All));Assert.Contains(path,error.Message);Assert.Equal(before,SourceSnapshot(target));
    }
}
