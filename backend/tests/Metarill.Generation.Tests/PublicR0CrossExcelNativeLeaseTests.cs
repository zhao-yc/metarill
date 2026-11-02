using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>只验证离线形状，不表示人类批准；用例不配置连接、运行正式场景或创建资源。</summary>
public sealed class PublicR0CrossExcelNativeLeaseTests
{
    /// <summary>其它用途、状态、包根、模型／表清单及预算任一不符，都须在首个副作用前拒绝。</summary>
    [Theory]
    [InlineData("legacy-purpose")][InlineData("double-purpose")][InlineData("pending")][InlineData("ended")]
    [InlineData("product")][InlineData("run")][InlineData("run-newline")][InlineData("proof")][InlineData("relative")][InlineData("foreign-root")]
    [InlineData("profiles")][InlineData("duplicate-profiles")][InlineData("tables")][InlineData("duplicate-tables")]
    [InlineData("rows")][InlineData("row-kind")][InlineData("table-budget")]
    public void Foreign_or_changed_lease_is_rejected_without_creating_directory(string fault)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-public-cross-excel-gate",Guid.NewGuid().ToString("N"));
        var profiles=GlobalCachedCrossExcelMySqlLease.Profiles.Select(p=>GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter)).ToArray();
        var tables=PublicR0CrossExcelNativePreparation.Catalog().ConfigTables.Select(table=>table.TableName).ToArray();
        var value=JsonSerializer.SerializeToNode(new{Purpose=PublicR0CrossExcelNativeLease.Purpose,AuthorizationStatus="approved",LeaseStatus="running",ProductCommit=PublicR0CrossExcelNativePreparation.ProductCommit,RunId="0123456789abcdef",PublicCrossExcelPreparation=new{PackageRoot=root},Profiles=profiles,BusinessTables=tables,MaximumBusinessRows=PublicR0CrossExcelNativeLease.MaximumBusinessRows,MaximumTables=PublicR0CrossExcelNativeLease.MaximumTables})!;
        Assert.Equal("0123456789abcdef",PublicR0CrossExcelNativeLease.Validate(JsonSerializer.SerializeToElement(value),root));
        if(fault=="legacy-purpose")value["Purpose"]=GlobalCachedCrossExcelNativeLease.Purpose;
        if(fault=="double-purpose")value["Purpose"]=DoublePersistenceNativeLease.Purpose;
        if(fault=="pending")value["AuthorizationStatus"]="pending";if(fault=="ended")value["LeaseStatus"]="ended";
        if(fault=="product")value["ProductCommit"]=GlobalCachedCrossExcelNativeLease.ProductCommit;
        if(fault=="run")value["RunId"]="../old";if(fault=="run-newline")value["RunId"]="0123456789abcdef\n";
        if(fault=="proof"){value["GlobalCrossExcelPreparation"]=value["PublicCrossExcelPreparation"]!.DeepClone();value.AsObject().Remove("PublicCrossExcelPreparation");}
        if(fault=="relative")value["PublicCrossExcelPreparation"]!["PackageRoot"]="prepared";
        if(fault=="foreign-root")value["PublicCrossExcelPreparation"]!["PackageRoot"]=Path.Combine(root,"other");
        if(fault=="profiles")value["Profiles"]!.AsArray().RemoveAt(0);if(fault=="duplicate-profiles")value["Profiles"]![0]=profiles[1];
        if(fault=="tables")value["BusinessTables"]!.AsArray().RemoveAt(0);if(fault=="duplicate-tables")value["BusinessTables"]![0]=tables[1];
        if(fault=="rows")value["MaximumBusinessRows"]=513;if(fault=="row-kind")value["MaximumBusinessRows"]="512";if(fault=="table-budget")value["MaximumTables"]=44;
        Assert.Throws<InvalidOperationException>(()=>PublicR0CrossExcelNativeLease.Validate(JsonSerializer.SerializeToElement(value),root));Assert.False(Directory.Exists(root));
    }
}
