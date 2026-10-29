using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Data.Schema;
namespace Metarill.Tools.PlatformFoundationUpgrade;

internal sealed record UpgradeRow(string Table,string Id,string SourceSha256);
internal sealed record UpgradePlan(string Profile,string ServerUuid,string Database,string SourceSha256,string BeforeFingerprint,string DesiredFingerprint,IReadOnlyList<UpgradeRow> Rows,IReadOnlyList<SchemaChange> Changes);
internal sealed record UpgradeVerification(int MatchingRows,int PendingChanges,string Fingerprint);

/// <summary>审查文件只声明已固定对象的摘要和结构SQL；反序列化后仍需对实时源及目标重新预览，不能直接执行文件SQL。</summary>
internal static class UpgradePlanCodec
{
    internal const string Profile="platform-foundation-20261030-v1";
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    /// <summary>摘要按固定记录顺序规范序列化，不受缩进换行影响。</summary>
    internal static string Digest(UpgradePlan plan)=>Hash(JsonSerializer.SerializeToUtf8Bytes(plan,Json));
    /// <summary>公共源/计划摘要不包含连接串或配置口令。</summary>
    internal static string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));
    /// <summary>未知键、重复键、超大文件或无效合同在创建数据库会话之前拒绝。</summary>
    internal static UpgradePlan Read(string path)
    {
        var bytes=File.ReadAllBytes(path);if(bytes.Length>256*1024)throw new InvalidOperationException("升级计划超过大小限制。");
        using var document=JsonDocument.Parse(bytes);RequireUnique(document.RootElement);
        var plan=JsonSerializer.Deserialize<UpgradePlan>(bytes,Json)??throw new InvalidOperationException("升级计划为空。");Validate(plan);return plan;
    }
    /// <summary>计划始终只面向开发平台库，所有对象和SQL必须属于当前固定版本白名单。</summary>
    internal static void Validate(UpgradePlan plan)
    {
        if(plan.Profile!=Profile||plan.Database!="metarill_dev"||!Guid.TryParseExact(plan.ServerUuid,"D",out var uuid)||uuid.ToString("D")!=plan.ServerUuid
            ||!Hex(plan.SourceSha256)||!Hex(plan.BeforeFingerprint)||!Hex(plan.DesiredFingerprint)||plan.Rows is null||plan.Changes is null)
            throw new InvalidOperationException("升级计划目标或版本无效。");
        if(!plan.Rows.Select(row=>(row.Table,row.Id)).SequenceEqual(UpgradeDefinition.Keys)||plan.Rows.Any(row=>!Hex(row.SourceSha256)))throw new InvalidOperationException("升级计划不是固定十三条元数据。");
        if(plan.Changes.Count>5||plan.Changes.Select(change=>change.Sql).Distinct(StringComparer.Ordinal).Count()!=plan.Changes.Count
            ||plan.Changes.Any(change=>!UpgradeDefinition.AllowedChanges.Contains(change)))throw new InvalidOperationException("升级计划夹带非固定新增结构。");
    }
    /// <summary>确认摘要先于数据库打开，过期/被改动的人工审查文件不能进入执行阶段。</summary>
    internal static void Confirm(UpgradePlan plan,string hash){Validate(plan);if(!Hex(hash)||Digest(plan)!=hash)throw new InvalidOperationException("确认摘要与升级计划不一致。");}
    private static bool Hex(string? value)=>value is{Length:64}&&value.All(char.IsAsciiHexDigitLower);
    /// <summary>即使重复属性的最后一个值合法也拒绝，避免审查者和解析器读到不同计划。</summary>
    private static void RequireUnique(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object){var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var field in value.EnumerateObject()){if(!keys.Add(field.Name))throw new InvalidOperationException("升级计划属性重复。");RequireUnique(field.Value);}}
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())RequireUnique(item);
    }
}
