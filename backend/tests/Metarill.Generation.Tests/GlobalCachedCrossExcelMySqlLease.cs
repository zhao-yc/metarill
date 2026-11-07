using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Testing;

namespace Metarill.Generation.Tests;

/// <summary>共享业务库的真实SQL验收必须先绑定六模型32张精确新表，旧实库租约和无批准随机表一律拒绝。</summary>
internal sealed record GlobalCachedCrossExcelMySqlLease(string Nonce, string Prefix, string[] Tables)
{
    internal const string EnvironmentKey = "METARILL_GLOBAL_CACHED_CROSS_EXCEL_LEASE";
    internal const string Purpose = "global-cached-cross-excel-mysql-v1";
    internal static readonly (int Role, string Key, string Parent, bool AllGlobal, bool Shared, bool Shorter)[] Profiles =
    [ (0,"string","int64",false,false,false), (1,"int64","string",false,false,false), (2,"string","string",false,false,false), (0,"int64","int64",true,false,false), (1,"string","int64",false,true,false), (1,"int64","string",false,true,true) ];

    /// <summary>第一步即验证租约，不读取连接配置、不建立数据库连接、不创建本轮目录。</summary>
    internal static GlobalCachedCrossExcelMySqlLease Read(int role, string key, string parent, bool allGlobal, bool shared, bool shorter)
    {
        var path = Environment.GetEnvironmentVariable(EnvironmentKey);
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("全局缓存级联Excel未取得本轮精确表租约。");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return Read(document.RootElement, role, key, parent, allGlobal, shared, shorter);
    }
    /// <summary>只接受本用途、明确批准及运行中状态，精确名单必须等于全部六模型32张表且无重复。</summary>
    internal static GlobalCachedCrossExcelMySqlLease Read(JsonElement settings, int role, string key, string parent, bool allGlobal, bool shared, bool shorter)
    {
        string Text(string name) => settings.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var run = Text("RunId");
        if (Text("Purpose") != Purpose || Text("AuthorizationStatus") != "approved" || Text("LeaseStatus") != "running" || !Regex.IsMatch(run, "^[a-f0-9]{16}$")) throw new InvalidOperationException("全局缓存级联Excel未取得本轮精确表租约。");
        if (Text("Server") != "deve-server.yinzon.com" || Text("Database") != "metarill_business_test" || !Profiles.Contains((role, key, parent, allGlobal, shared, shorter))) throw new InvalidOperationException("全局缓存级联Excel目标或模型超出固定范围。");
        var expected = ExpectedTables(run); var declared = settings.GetProperty("Tables").Deserialize<string[]>() ?? [];
        if (declared.Length != 32 || declared.Distinct(StringComparer.Ordinal).Count() != declared.Length || !expected.Values.SelectMany(value => value).ToHashSet(StringComparer.Ordinal).SetEquals(declared)) throw new InvalidOperationException("全局缓存级联Excel精确临时表名单不匹配。");
        var profile = Profile(role, key, parent, allGlobal, shared, shorter);
        return new(run + "-" + profile, "mt_gcxe_" + run + "_" + profile, expected[profile]);
    }
    /// <summary>模型固定输入独立于测试执行顺序，表名提前确定并可由用户逐项审查。</summary>
    internal static string Profile(int role, string key, string parent, bool allGlobal, bool shared, bool shorter)
    {
        var text = role.ToString(CultureInfo.InvariantCulture) + "|" + key + "|" + parent + "|" + (allGlobal ? "1" : "0") + "|" + (shared ? "1" : "0") + "|" + (shorter ? "1" : "0");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];
    }
    /// <summary>名单由同一元数据夹具取得，不能以共享前缀放行额外业务表或其它账号对象。</summary>
    internal static Dictionary<string, string[]> ExpectedTables(string run)
    {
        if (!Regex.IsMatch(run, "^[a-f0-9]{16}$")) throw new InvalidOperationException("本轮表编号不规范。");
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var p in Profiles)
        {
            var catalog = p.Shared ? GlobalCrossCachedTargetFixture.CreateShared(0, p.Key, p.Parent, p.Shorter) : GlobalCrossCachedTargetFixture.Create(0, p.Role, p.Key, p.Parent, p.AllGlobal);
            var profile = Profile(p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter);
            result.Add(profile, catalog.ConfigTables.Select(table => "mt_gcxe_" + run + "_" + profile + "_" + table.TableName).ToArray());
        }
        return result;
    }
}
