using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Metadata;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Metarill.Generation.Tests")]
namespace Metarill.Generation;

/// <summary>本地预览只描述源码文件；确认摘要不授予数据库、部署或命令执行权限。</summary>
public sealed record GenerationUpdateFile(string Path, string Action, string Scope, string? BeforeSha256, string? AfterSha256);
public sealed record GenerationUpdatePlan(int SchemaVersion, string TargetDirectory, string CandidateDirectory, string ProjectId, string RootNamespace,
    IReadOnlyList<string> Actions, IReadOnlyList<string> RequiredActions, bool CanApply, string TargetFingerprint, string CandidateFingerprint,
    IReadOnlyList<GenerationUpdateFile> Files, string PlanSha256);
public sealed record GenerationUpdateReceipt(string TransactionId, string PlanSha256, string Status, int WrittenFiles, int DeletedFiles, int PreservedFiles);

/// <summary>既有独立FullApplication源码更新器；所有写入经过预览、旧摘要、路径策略和持久化恢复日志。</summary>
public sealed partial class LocalGenerationUpdater
{
    internal const string Config = "generate-project-config", Runtime = "sync-runtime", Pages = "generate-all-page";
    internal const string PackageMarker = ".metarill-package.json", GenerationMarker = "backend/generated/.metarill-generation.json", FrontendMarker = "frontend/.metarill-frontend.json";
    private static readonly string[] Scopes = [Config, Runtime, Pages];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true, NewLine = "\n" };
    // 测试只在真实文件替换后注入异常，生产CLI不提供故障或命令执行入口。
    internal Action<string>? AfterWrite { get; init; }
    // 仅测试在最后旧摘要检查后模拟第三方创建目标；生产CLI不暴露。
    internal Action<string>? BeforeWrite { get; init; }
    // 仅测试原壳发布已成功、归属确认尚未刷盘的中断窗口，CLI不提供故障入口。
    internal Action<string>? AfterCreatePublication { get; init; }
    // 恢复并发探针只供测试在首轮检查后模拟外部编辑，CLI不暴露。
    internal Action<string>? BeforeRestore { get; init; }

    /// <summary>只读目标并输出确定性差异；未选择的其他受管差异一律作为显式依赖拒绝，不猜框架ABI。</summary>
    public GenerationUpdatePlan Preview(string targetDirectory, string candidateDirectory, IReadOnlyList<string> actions)
    {
        var target = Root(targetDirectory); var candidate = Root(candidateDirectory);
        if (IsWithin(target, candidate) || IsWithin(candidate, target)) throw new InvalidOperationException("目标与候选目录不能相同或互相包含。");
        var chosen = actions.Order(StringComparer.Ordinal).ToArray();
        if (chosen.Length == 0 || chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Length || chosen.Any(action => !Scopes.Contains(action, StringComparer.Ordinal))) throw new InvalidOperationException("必须明确选择唯一且受支持的更新动作。");
        RejectPending(target);
        var before = ReadPackage(target, false); var after = ReadPackage(candidate, true);
        if (before.ProjectId != after.ProjectId || before.Package.RootNamespace != after.Package.RootNamespace) throw new InvalidOperationException("候选项目或namespace与目标不匹配。");
        if (before.Metadata.DatabaseProvider != after.Metadata.DatabaseProvider) throw new InvalidOperationException("源码更新不负责数据库品牌或存量数据转换。");
        var files = new List<GenerationUpdateFile>(); var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in before.Entries.Keys.Concat(after.Entries.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            var oldEntry = before.Entries.GetValueOrDefault(path); var newEntry = after.Entries.GetValueOrDefault(path);
            var role = Role(path, oldEntry is not null ? before : after);
            if (role == "manifest") continue;
            var actual = ExistingHash(target, path);
            if (role is "preserve" or "handwritten")
            {
                // 原壳仅允许固定清单路径缺失时首次补建；不把同名已有文件纳入受管替换。
                var createScope = role == "handwritten" ? Pages : FirstCreateFrontend.Contains(path) ? Runtime : null;
                if (createScope is not null && actual is null && newEntry is not null)
                {
                    if (chosen.Contains(createScope, StringComparer.Ordinal)) files.Add(new(path, "create", createScope, null, newEntry.Sha256)); else required.Add(createScope);
                }
                else if (actual is not null) files.Add(new(path, "preserve", role, actual, actual));
                continue;
            }
            if (oldEntry?.Sha256 == newEntry?.Sha256) continue;
            if (!chosen.Contains(role, StringComparer.Ordinal)) { required.Add(role); continue; }
            // 删除表级基类会让仍保留的手写partial失去对应类型，首期必须人工处理该依赖。
            if (newEntry is null && path.StartsWith("backend/generated/Services/", StringComparison.Ordinal) && path.EndsWith("Service.g.cs", StringComparison.Ordinal)) throw new InvalidOperationException("首期不支持删除表级类型，请先单独迁移手写依赖：" + path);
            if (oldEntry is null && actual is not null) throw new InvalidOperationException("新增路径已有非归属文件：" + path);
            files.Add(new(path, newEntry is null ? "delete" : oldEntry is null ? "create" : "replace", role, actual, newEntry?.Sha256));
        }
        if (required.Count == 0) AddManifestChanges(before, after, files);
        var plan = new GenerationUpdatePlan(1, target, candidate, before.ProjectId, before.Package.RootNamespace, chosen,
            required.Order(StringComparer.Ordinal).ToArray(), required.Count == 0, Fingerprint(before), Fingerprint(after), files.OrderBy(file => file.Action == "preserve" ? 1 : 0).ThenBy(file => file.Path, StringComparer.Ordinal).ToArray(), "");
        return plan with { PlanSha256 = Hash(Encode(plan)) };
    }

    /// <summary>只在显式确认且当前重新预览完全相同时写入；不执行SQL、构建、安装或服务启动。</summary>
    public GenerationUpdateReceipt Apply(GenerationUpdatePlan plan, string confirmation)
    {
        if (plan.SchemaVersion != 1 || !plan.CanApply || plan.RequiredActions.Count != 0 || confirmation != plan.PlanSha256 || Hash(Encode(plan with { PlanSha256 = "" })) != plan.PlanSha256) throw new InvalidOperationException("更新计划无效、未满足依赖或确认摘要不匹配。");
        var target = Root(plan.TargetDirectory); var candidate = Root(plan.CandidateDirectory);
        using var updateLock = AcquireLock(target, ".metarill-update.lock");
        using var generationLock = AcquireLock(target, "backend/generated/.metarill-generation.lock");
        var current = Preview(target, candidate, plan.Actions);
        if (current.PlanSha256 != plan.PlanSha256) throw new InvalidOperationException("预览后候选、目标或受保护文件发生变化，请重新预览。");
        var source = ReadPackage(candidate, true); var baseline = ReadPackage(target, false); var payload = Payload(baseline, source, plan.Files);
        return Commit(plan, payload);
    }

    /// <summary>读取由本工具明确输出的计划，未知字段、重复字段或超限内容都拒绝。</summary>
    public static GenerationUpdatePlan ReadPlan(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 4 * 1024 * 1024) throw new InvalidOperationException("预览计划不存在或超过4MiB限制。");
        return Decode<GenerationUpdatePlan>(File.ReadAllBytes(path));
    }
    /// <summary>计划只能写到目标与候选之外；预览绝不借输出参数修改项目。</summary>
    public static void WritePlan(GenerationUpdatePlan plan, string output)
    {
        var path = Path.GetFullPath(output);
        if (IsWithin(plan.TargetDirectory, path) || IsWithin(plan.CandidateDirectory, path)) throw new InvalidOperationException("计划输出文件必须位于目标和候选之外。");
        CSharpGenerator.SafePath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); var bytes = Encode(plan); stream.Write(bytes); stream.Flush(true);
    }

    /// <summary>按选定payload合并清单，范围外及保留项继续使用旧基线，不用新整包覆盖现有清单。</summary>
    private static Dictionary<string, byte[]> Payload(PackageSnapshot before, PackageSnapshot after, IReadOnlyList<GenerationUpdateFile> files)
    {
        var bytes = files.Where(file => file.Action is "create" or "replace" && file.Scope != "manifest").ToDictionary(file => file.Path, file => ReadBytes(after.Root, file.Path), StringComparer.OrdinalIgnoreCase);
        var package = before.Entries.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        var generated = before.Generated.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var frontend = before.Frontend.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => file.Action != "preserve" && file.Scope != "manifest"))
        {
            if (file.AfterSha256 is null) package.Remove(file.Path); else package[file.Path] = new(file.Path, file.AfterSha256);
            if (file.Path.StartsWith("backend/generated/", StringComparison.Ordinal))
            {
                var relative = file.Path["backend/generated/".Length..];
                if (after.Generated.Files.Any(entry => entry.Path == relative)) generated[relative] = new(relative, file.AfterSha256!);
                else if (file.AfterSha256 is null) generated.Remove(relative);
            }
            if (file.Path.StartsWith("frontend/", StringComparison.Ordinal))
            {
                var relative = file.Path["frontend/".Length..];
                if (file.AfterSha256 is null) frontend.Remove(relative); else frontend[relative] = new(relative, file.AfterSha256);
            }
        }
        if (!SameEntries(generated.Values, after.Generated.Files)) throw new InvalidOperationException("生成代码与配置依赖未完整选择，不能更新全局元数据摘要。");
        bytes[GenerationMarker] = Encode(before.Generated with { MetadataSha256 = after.Generated.MetadataSha256, Files = generated.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray() });
        bytes[FrontendMarker] = Encode(before.Frontend with { Authentication = after.Frontend.Authentication, Files = frontend.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray() });
        foreach (var marker in new[] { GenerationMarker, FrontendMarker }) package[marker] = new(marker, Hash(bytes[marker]));
        bytes[PackageMarker] = Encode(before.Package with { AuthenticationProfile = after.Package.AuthenticationProfile, SecurityCatalogSha256=after.Package.SecurityCatalogSha256, MetadataSha256 = after.Package.MetadataSha256, Warnings = after.Package.Warnings, Files = package.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray() });
        return bytes;
    }

    /// <summary>清单最后写入且只有字节改变时才列入计划，重复预览或应用产生可验证的空操作。</summary>
    private static void AddManifestChanges(PackageSnapshot before, PackageSnapshot after, List<GenerationUpdateFile> files)
    {
        var payload = Payload(before, after, files);
        foreach (var marker in new[] { GenerationMarker, FrontendMarker, PackageMarker })
        {
            var oldHash = ExistingHash(before.Root, marker); var hash = Hash(payload[marker]);
            if (oldHash != hash) files.Add(new(marker, "replace", "manifest", oldHash, hash));
        }
    }

    /// <summary>指纹绑定全部基线和当前受保护文件；正常手写修改不冲突，但预览后的修改必须重审。</summary>
    private static string Fingerprint(PackageSnapshot package) => Hash(Encode(new { package = Hash(ReadBytes(package.Root, PackageMarker)),
        files = package.Entries.Values.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => new { file.Path, expected = file.Sha256, actual = ExistingHash(package.Root, file.Path) }).ToArray() }));
    /// <summary>清单按路径排序比较，不能依赖导出时的枚举顺序。</summary>
    private static bool SameEntries(IEnumerable<UpdateEntry> left, IEnumerable<UpdateEntry> right) => left.OrderBy(file => file.Path, StringComparer.Ordinal).SequenceEqual(right.OrderBy(file => file.Path, StringComparer.Ordinal));
    /// <summary>协议统一采用UTF8、LF和固定属性顺序，确认摘要不受主机换行影响。</summary>
    private static byte[] Encode<T>(T value) => System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json) + "\n");
    /// <summary>将字节计算为规范小写SHA-256。</summary>
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    /// <summary>先校验路径再读取摘要；缺失文件以null表示，不能与空文件混淆。</summary>
    private static string? ExistingHash(string root, string path) => File.Exists(Safe(root, path)) ? Hash(ReadBytes(root, path)) : null;
}
