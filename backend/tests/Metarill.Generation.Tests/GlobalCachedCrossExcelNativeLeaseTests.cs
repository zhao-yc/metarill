using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>离线形状校验不是人类批准，不读取数据库配置或创建任何测试对象。</summary>
public sealed class GlobalCachedCrossExcelNativeLeaseTests
{
    /// <summary>旧B119用途、失效批准、不同产品/路径及少一个模型均在首个副作用之前拒绝。</summary>
    [Theory]
    [InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("previous-product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("missing")][InlineData("profile")][InlineData("duplicate")]
    public void Invalid_or_foreign_lease_does_not_create_directory(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-global-cross-excel-gate",Guid.NewGuid().ToString("N"));
        var profiles=GlobalCachedCrossExcelMySqlLease.Profiles.Select(p=>GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter)).ToArray();
        var value=JsonSerializer.SerializeToNode(new{Purpose=GlobalCachedCrossExcelNativeLease.Purpose,AuthorizationStatus="approved",LeaseStatus="running",ProductCommit=GlobalCachedCrossExcelNativeLease.ProductCommit,RunId="0123456789abcdef",GlobalCrossExcelPreparation=new{PackageRoot=root},Profiles=profiles})!;
        Assert.Equal("0123456789abcdef",GlobalCachedCrossExcelNativeLease.Validate(JsonSerializer.SerializeToElement(value),root));
        if(invalid=="purpose")value["Purpose"]=GlobalCachedCrossExcelMySqlLease.Purpose;if(invalid=="authorization")value["AuthorizationStatus"]="pending";if(invalid=="ended")value["LeaseStatus"]="ended";if(invalid=="product")value["ProductCommit"]=new string('0',40);if(invalid=="previous-product")value["ProductCommit"]="6427285dc8768c1d1f0a831aa92083e57909c5c4";if(invalid=="run")value["RunId"]="../old";if(invalid=="path")value["GlobalCrossExcelPreparation"]!["PackageRoot"]=Path.Combine(root,"foreign");if(invalid=="relative")value["GlobalCrossExcelPreparation"]!["PackageRoot"]="prepared";if(invalid=="missing")value.AsObject().Remove("GlobalCrossExcelPreparation");if(invalid=="profile")value["Profiles"]!.AsArray().RemoveAt(0);if(invalid=="duplicate")value["Profiles"]![0]=profiles[1];
        Assert.Throws<InvalidOperationException>(()=>GlobalCachedCrossExcelNativeLease.Validate(JsonSerializer.SerializeToElement(value),root));Assert.False(Directory.Exists(root));
    }
}
