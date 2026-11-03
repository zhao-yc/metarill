using System.Text.Json;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>需要本轮专用租约及实例配置。默认 CI 只执行离线门禁。</summary>
public sealed class CrossTableImportLockMySqlFactAttribute : FactAttribute
{
    /// <summary>只核对本地配置文件是否存在，不读取凭据或连接数据库。</summary>
    public CrossTableImportLockMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable(CrossTableImportLockLease.EnvironmentKey)) ||
            !File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")) ||
            !File.Exists(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")))
            Skip = "需要本轮精确表租约、隔离业务库配置与实例 pin。";
    }
}

/// <summary>将真实测试限制为同一轮四种模型的精确十六表。</summary>
internal sealed record CrossTableImportLockLease(string RunId, string Profile, string Prefix, string[] Tables, string Hostname, Guid ServerUuid)
{
    internal const string EnvironmentKey = "METARILL_CROSS_TABLE_IMPORT_LOCK_LEASE";
    internal const string Purpose = "cross-table-import-lock-mysql-v1";
    private static readonly string[] Profiles = ["ss", "sl", "ls", "ll"];
    private static readonly string[] Suffixes = ["source", "country", "area", "place"];

    /// <summary>读取租约后先原子消费本模型，再由用例读取连接配置并核对实例。</summary>
    internal static CrossTableImportLockLease Begin(string sourceType, string parentType)
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentKey);
        if (string.IsNullOrWhiteSpace(path)) throw new UnauthorizedAccessException("缺少覆盖匹配实库租约。");
        var leasePath = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(leasePath));
        var lease = Validate(document.RootElement, sourceType, parentType);
        MarkStarted(leasePath, lease.Profile);
        return lease;
    }

    /// <summary>每个模型最多启动一次，即使通过或失败后已经清理也不能自动重放。</summary>
    internal static void MarkStarted(string leasePath, string profile)
    {
        if (!Profiles.Contains(profile, StringComparer.Ordinal)) throw new InvalidOperationException("未知的覆盖匹配实库模型。");
        using var marker = new FileStream(leasePath + "." + profile + ".started", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        marker.Flush(flushToDisk: true);
    }

    /// <summary>拒绝待批准、已结束、其它目标与扩张或替换的表清单。</summary>
    internal static CrossTableImportLockLease Validate(JsonElement scope, string sourceType, string parentType)
    {
        if (Read("authorizationStatus") != "approved" || Read("leaseStatus") != "running")
            throw new UnauthorizedAccessException("覆盖匹配实库租约未批准或已结束。");
        var run = Read("runId");
        if (Read("purpose") != Purpose || Read("server") != "deve-server.yinzon.com" ||
            Read("database") != "metarill_business_test" || run.Length != 16 ||
            run.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            throw new InvalidOperationException("覆盖匹配实库租约来源或目标不匹配。");

        var profile = Code(sourceType) + Code(parentType);
        var expected = Profiles.SelectMany(value => TablesFor(run, value)).ToHashSet(StringComparer.Ordinal);
        var declared = scope.GetProperty("tables").Deserialize<string[]>() ?? [];
        if (declared.Length != expected.Count || !expected.SetEquals(declared))
            throw new InvalidOperationException("覆盖匹配实库精确表名单不匹配。");
        var pin = scope.GetProperty("instancePin");
        var hostname = pin.GetProperty("expectedHostname").GetString();
        if (string.IsNullOrWhiteSpace(hostname) || !Guid.TryParseExact(pin.GetProperty("expectedServerUuid").GetString(), "D", out var serverUuid) || serverUuid == Guid.Empty)
            throw new InvalidOperationException("覆盖匹配实库实例 pin 不完整。");
        return new(run, profile, PrefixFor(run, profile), TablesFor(run, profile), hostname, serverUuid);

        // 缺失的批准字段视为未批准，避免回退到普通连接配置。
        string Read(string name) => scope.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : "";
    }

    /// <summary>用固定字符编码标记字符串与整数，模型表名与执行顺序无关。</summary>
    private static string Code(string type) => type switch
    {
        "string" => "s",
        "int64" => "l",
        _ => throw new InvalidOperationException("未知的覆盖匹配实库键类型。")
    };

    /// <summary>所有表名来自固定格式和已验证的轮次编号。</summary>
    private static string PrefixFor(string run, string profile) => "mt_nrlock_" + run + "_" + profile;

    /// <summary>顺序与元数据夹具一致，清理仍按原表结构及逐行摘要护栏执行。</summary>
    private static string[] TablesFor(string run, string profile) =>
        Suffixes.Select(suffix => PrefixFor(run, profile) + "_" + suffix).ToArray();
}
