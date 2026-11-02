using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Generation.Tests;

/// <summary>数值正式持久化须有专属新库范围，旧六模型租约或通用MySQL设置不能启用。</summary>
internal static class DoublePersistenceNativeLease
{
    internal const string EnvironmentKey = "METARILL_DOUBLE_PERSISTENCE_NATIVE_SETTINGS";
    internal const string Purpose = "double-persistence-native-v1";
    internal const int MaximumBusinessRows = 512;

    /// <summary>任何复制工程、业务进程或数据库连接之前，先验证本轮用途、来源、对象及明确批准。</summary>
    internal static string Validate(JsonElement settings, string preparedRoot)
    {
        if (Text(settings, "Purpose") != Purpose || Text(settings, "AuthorizationStatus") != "approved" || Text(settings, "LeaseStatus") != "running" || Text(settings, "ProductCommit") != DoublePersistenceNativePreparation.ProductCommit)
            throw new InvalidOperationException("数值持久化尚无本轮独占新库授权。");
        var run = Text(settings, "RunId");
        if (!Regex.IsMatch(run, "\\A[a-f0-9]{16}\\z")) throw new InvalidOperationException("数值运行标识不规范。");
        if (!settings.TryGetProperty("DoublePersistencePreparation", out var proof) || proof.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("数值固定完整包证明缺失。");
        var root = Text(proof, "PackageRoot");
        if (!Path.IsPathFullyQualified(root) || Path.GetFullPath(root) != Path.GetFullPath(preparedRoot)) throw new InvalidOperationException("数值固定包根不符。");
        if (!settings.TryGetProperty("BusinessTables", out var tables) || tables.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("数值业务表范围缺失。");
        var names = tables.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : "").ToArray();
        var expected = DoublePersistenceNativePreparation.Catalog().ConfigTables.Select(table => table.TableName).ToHashSet(StringComparer.Ordinal);
        if (names.Length != 3 || names.Distinct(StringComparer.Ordinal).Count() != 3 || !expected.SetEquals(names)) throw new InvalidOperationException("数值业务表范围不完整或混入其它对象。");
        if (!settings.TryGetProperty("MaximumBusinessRows", out var maximum) || !maximum.TryGetInt32(out var value) || value != MaximumBusinessRows) throw new InvalidOperationException("数值样本预算不符。");
        return run;
    }

    /// <summary>缺字段或坏类型均不降级成默许。</summary>
    private static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
}