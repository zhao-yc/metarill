using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>纯离线负向合同，批准形状只是测试数据，不是对远端资源的授权。</summary>
public sealed class DoublePersistenceNativeLeaseTests
{
    /// <summary>缺失、已用、旧用途／产品、坏路径／运行号及扩张对象／预算均在副作用之前拒绝。</summary>
    [Theory]
    [InlineData("pending")][InlineData("ended")][InlineData("purpose")][InlineData("product")][InlineData("run")][InlineData("newline")]
    [InlineData("proof")][InlineData("relative")][InlineData("foreign-root")][InlineData("missing-table")][InlineData("foreign-table")][InlineData("duplicate-table")][InlineData("budget")][InlineData("null-budget")]
    public void Invalid_or_expanded_scope_is_rejected_without_creating_execution_directory(string invalid)
    {
        var root = Path.Combine(Path.GetTempPath(), "metarill-double-native-gate", Guid.NewGuid().ToString("N"));
        var value = JsonSerializer.SerializeToNode(new { Purpose = DoublePersistenceNativeLease.Purpose, AuthorizationStatus = "approved", LeaseStatus = "running", ProductCommit = DoublePersistenceNativePreparation.ProductCommit,
            RunId = "0123456789abcdef", DoublePersistencePreparation = new { PackageRoot = root }, BusinessTables = DoublePersistenceNativePreparation.Catalog().ConfigTables.Select(table => table.TableName).ToArray(), MaximumBusinessRows = 512 })!;
        Assert.Equal("0123456789abcdef", DoublePersistenceNativeLease.Validate(JsonSerializer.SerializeToElement(value), root));
        switch (invalid)
        {
            case "pending": value["AuthorizationStatus"] = "pending"; break;
            case "ended": value["LeaseStatus"] = "ended"; break;
            case "purpose": value["Purpose"] = GlobalCachedCrossExcelNativeLease.Purpose; break;
            case "product": value["ProductCommit"] = GlobalCachedCrossExcelNativeLease.ProductCommit; break;
            case "run": value["RunId"] = "../old"; break;
            case "newline": value["RunId"] = "0123456789abcdef\n"; break;
            case "proof": value.AsObject().Remove("DoublePersistencePreparation"); break;
            case "relative": value["DoublePersistencePreparation"]!["PackageRoot"] = "prepared"; break;
            case "foreign-root": value["DoublePersistencePreparation"]!["PackageRoot"] = Path.Combine(root, "foreign"); break;
            case "missing-table": value["BusinessTables"]!.AsArray().RemoveAt(0); break;
            case "foreign-table": value["BusinessTables"]!.AsArray().Add("foreign_table"); break;
            case "duplicate-table": value["BusinessTables"]![0] = value["BusinessTables"]![1]!.GetValue<string>(); break;
            case "budget": value["MaximumBusinessRows"] = 513; break;
            default: value["MaximumBusinessRows"] = null; break;
        }
        Assert.Throws<InvalidOperationException>(() => DoublePersistenceNativeLease.Validate(JsonSerializer.SerializeToElement(value), root)); Assert.False(Directory.Exists(root));
    }
}