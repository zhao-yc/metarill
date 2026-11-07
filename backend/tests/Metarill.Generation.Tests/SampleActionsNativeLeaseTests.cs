using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>专属租约先于凭据配置解析；合成输入没有连接串或副作用，仅验证明确拒绝边界。</summary>
public sealed class SampleActionsNativeLeaseTests
{
    /// <summary>完整专属声明才通过；缺连接配置不影响此纯门禁，且不创建源或包目录。</summary>
    [Theory][InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")]
    [InlineData("run")][InlineData("requested")][InlineData("source")][InlineData("source-relative")]
    [InlineData("package")][InlineData("package-relative")][InlineData("missing")][InlineData("type")]
    public void Unapproved_ended_or_foreign_lease_rejects_before_connection_configuration(string kind)
    {
        var source=Path.Combine(Path.GetTempPath(),"metarill-sample-actions-lease",Guid.NewGuid().ToString("N"));const string run="0123456789abcdef";var requested=run;
        var input=new JsonObject{{"Purpose",SampleActionsNativeLease.Purpose},{"AuthorizationStatus","approved"},{"LeaseStatus","running"},{"SampleActionsProductCommit",SampleActionsNativePreparation.ProductCommit},{"RunId",run},{"SampleActionsSourceRoot",source},{"SampleActionsPreparation",new JsonObject{{"PackageRoot",Path.GetFullPath(Path.Combine(source,".local/sample-actions-native/prepared"))}}}};
        Assert.Equal(run,SampleActionsNativeLease.Validate(JsonSerializer.SerializeToElement(input),requested,source));
        switch(kind)
        {
            case "purpose":input["Purpose"]="global-cached-target-v1";break;
            case "authorization":input["AuthorizationStatus"]="pending";break;
            case "ended":input["LeaseStatus"]="ended";break;
            case "product":input["SampleActionsProductCommit"]="e051297";break;
            case "run":input["RunId"]="ABCDEF0123456789";break;
            case "requested":requested="different";break;
            case "source":input["SampleActionsSourceRoot"]=Path.Combine(source,"other");break;
            case "source-relative":input["SampleActionsSourceRoot"]="relative";break;
            case "package":input["SampleActionsPreparation"]!["PackageRoot"]=Path.Combine(source,"other");break;
            case "package-relative":input["SampleActionsPreparation"]!["PackageRoot"]="prepared";break;
            case "missing":input.Remove("SampleActionsPreparation");break;
            case "type":input["AuthorizationStatus"]=true;break;
        }
        Assert.Throws<InvalidOperationException>(()=>SampleActionsNativeLease.Validate(JsonSerializer.SerializeToElement(input),requested,source));Assert.False(Directory.Exists(source));
    }
}
