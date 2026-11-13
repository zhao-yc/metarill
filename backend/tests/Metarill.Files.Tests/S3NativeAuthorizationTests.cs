using System.Text;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>全部批准形状为内存合成值；只验证拒绝顺序，不构成真实授权，也不连接SSH/S3。</summary>
public sealed class S3NativeAuthorizationTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-30T00:00:00+00:00",System.Globalization.CultureInfo.InvariantCulture);
    internal static S3NativeScope Scope()=>new(S3NativeScope.PurposeValue,new string('c',32),S3NativeScope.EndpointValue,"us-east-1","metarill-native-"+new string('c',32),"objects",new string('d',64),S3NativeScope.ImageValue,"yinzon-seaweedfs:4.48-object-acl-20261004",3,4,1048576);
    private static S3NativeLease Approved()=>new(new string('a',64),new string('b',64),"approved","ready","仅离线合成，不代表真实批准",Now.AddMinutes(-1),Now.AddHours(1),0,null);

    /// <summary>固定资源与有效批准才允许准备；第三次结束后不能继续使用同一租约。</summary>
    [Fact]
    public void Exact_scope_is_accepted_only_within_the_approved_attempt_budget()
    {
        var scope=Scope();var lease=Approved();S3NativeAuthorization.ValidateGrant(scope,lease,lease.ScopeSha256,lease.ArtifactSha256,Now);
        S3NativeAuthorization.ValidateGrant(scope,lease with{Attempts=2},lease.ScopeSha256,lease.ArtifactSha256,Now);
        Assert.Throws<InvalidOperationException>(()=>S3NativeAuthorization.ValidateGrant(scope,lease with{Attempts=3},lease.ScopeSha256,lease.ArtifactSha256,Now));
    }

    /// <summary>未批/已消费/过期/来源漂移/改名/扩预算均必须在取得网络依赖前拒绝。</summary>
    [Theory]
    [InlineData("pending")][InlineData("ended")][InlineData("running")][InlineData("expired")][InlineData("scopeHash")][InlineData("artifactHash")]
    [InlineData("bucket")][InlineData("endpoint")][InlineData("container")][InlineData("image")][InlineData("budget")][InlineData("attempts")]
    [InlineData("active")][InlineData("approval")][InlineData("permanent")][InlineData("future")][InlineData("negative")]
    public void Changed_or_unapproved_input_is_rejected_before_network_dependencies(string kind)
    {
        var scope=Scope();var lease=Approved();var originalScope=lease.ScopeSha256;var originalArtifact=lease.ArtifactSha256;var networkConstructed=false;
        switch(kind)
        {
            case "pending":lease=lease with{AuthorizationStatus="pending"};break;
            case "ended":lease=lease with{LeaseStatus="ended"};break;
            case "running":lease=lease with{LeaseStatus="running"};break;
            case "expired":lease=lease with{ApprovedUntilUtc=Now.AddSeconds(-1)};break;
            case "scopeHash":lease=lease with{ScopeSha256=new string('e',64)};break;
            case "artifactHash":lease=lease with{ArtifactSha256=new string('e',64)};break;
            case "bucket":scope=scope with{Bucket="existing-bucket"};break;
            case "endpoint":scope=scope with{Endpoint="http://127.0.0.1:8333"};break;
            case "container":scope=scope with{ContainerId="unknown"};break;
            case "image":scope=scope with{ImageId="sha256:"+new string('e',64)};break;
            case "budget":scope=scope with{MaxObjects=5};break;
            case "attempts":lease=lease with{Attempts=3};break;
            case "active":lease=lease with{ActiveAttemptId="another-run"};break;
            case "approval":lease=lease with{ApprovalReference=""};break;
            case "permanent":lease=lease with{ApprovedUntilUtc=Now.AddDays(8)};break;
            case "future":lease=lease with{ApprovedAtUtc=Now.AddSeconds(1)};break;
            case "negative":lease=lease with{Attempts=-1};break;
        }
        Assert.Throws<InvalidOperationException>(()=>
        {
            S3NativeAuthorization.ValidateGrant(scope,lease,originalScope,originalArtifact,Now);
            networkConstructed=true;
        });
        Assert.False(networkConstructed);
    }

    /// <summary>重复状态不能借最后值伪装批准，未知字段和原始异常文本也不得穿透。</summary>
    [Theory]
    [InlineData("{\"authorizationStatus\":\"pending\",\"authorizationStatus\":\"approved\"}")]
    [InlineData("{\"sql\":\"synthetic-secret-do-not-print\"}")]
    [InlineData("{\"AuthorizationStatus\":\"approved\"}")]
    [InlineData("{\"authorizationStatus\":\"pending\",\"AuthorizationStatus\":\"approved\"}")]
    public void Duplicate_or_unknown_json_is_rejected_without_echoing_input(string json)
    {
        var error=Assert.Throws<InvalidOperationException>(()=>S3NativeAuthorization.Read<S3NativeLease>(Encoding.UTF8.GetBytes(json)));
        Assert.DoesNotContain("synthetic-secret-do-not-print",error.ToString(),StringComparison.Ordinal);
    }
}
