using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Generation;

/// <summary>只报告本机已证明的应用状态；unknown不等于未写入，也不授予重试apply的权利。</summary>
public sealed record GenerationApplicationStatus(string Status, string? TransactionId, string PlanSha256);

public sealed partial class LocalGenerationUpdater
{
    /// <summary>固定目标与确认计划，使用原两道互斥锁只读核对本机日志；不应用、恢复或清理active。</summary>
    public GenerationApplicationStatus InspectApplicationStatus(string targetDirectory, GenerationUpdatePlan plan, string confirmation)
    {
        var target = Root(targetDirectory);
        ValidateApplicationStatusPlan(target, plan, confirmation);
        using var updateLock = AcquireLock(target, ".metarill-update.lock");
        using var generationLock = AcquireLock(target, "backend/generated/.metarill-generation.lock");
        var unknown = new GenerationApplicationStatus("unknown", null, plan.PlanSha256);
        ActiveUpdate? active;
        try
        {
            var activePath = Safe(target, Control + "/active.json");
            active = File.Exists(activePath) ? Decode<ActiveUpdate>(ReadBytes(target, Control + "/active.json")) : null;
            if (active is not null && (active.SchemaVersion != 1 || !ValidHash(active.PlanSha256) || !Regex.IsMatch(active.TransactionId ?? "", "^[a-f0-9]{32}$"))) return unknown;
        }
        catch (Exception error) when (StatusEvidenceError(error)) { return unknown; }
        // 其他计划未完成不能被本次状态查询隐式忽略，更不能由调用方指定事务路径来接管。
        if (active is not null && active.PlanSha256 != confirmation) throw new InvalidOperationException("当前目标存在另一计划的待恢复事务，状态查询拒绝跨计划。");
        try
        {
            var folder = Safe(target, Control + "/transactions");
            if (!Directory.Exists(folder)) return unknown;
            var entries = Directory.EnumerateFileSystemEntries(folder).Take(2001).ToArray();
            if (entries.Length > 2000) return unknown;
            var matches = new List<UpdateJournal>();
            foreach (var path in entries)
            {
                var id = Path.GetFileName(path);
                if (!Regex.IsMatch(id, "^[a-f0-9]{32}$") || !Directory.Exists(Safe(target, Transaction(id)))) return unknown;
                var journal = ReadJournal(target, id);
                if (journal.PlanSha256 != confirmation) continue;
                // 同摘要的持久计划必须与调用方逐字规范一致，不能只相信journal自报确认摘要。
                var original = Decode<GenerationUpdatePlan>(ReadBytes(target, Transaction(id) + "/plan.json"));
                if (Hash(Encode(original)) != Hash(Encode(plan))) return unknown;
                matches.Add(journal);
            }
            if (matches.Count != 1) return unknown;
            var match = matches[0];
            if (active is not null)
            {
                if (active.TransactionId != match.TransactionId || match.Status is not ("prepared" or "applying" or "manual-recovery" or "committed" or "rolled-back")) return unknown;
                // 即使终态已落盘，只要active尚在也不修改它；调用方须显式运行原recover再核验。
                return new("recovery-required", match.TransactionId, plan.PlanSha256);
            }
            if (match.Status is not ("committed" or "rolled-back")) return unknown;
            VerifyStatusPayload(target, match);
            var after = match.Status == "committed";
            foreach (var file in plan.Files)
                if (ExistingHash(target, file.Path) != (file.Action == "preserve" ? file.BeforeSha256 : after ? file.AfterSha256 : file.BeforeSha256)) return unknown;
            var current = ReadPackage(target, false);
            if (current.ProjectId != plan.ProjectId || current.Package.RootNamespace != plan.RootNamespace) return unknown;
            var expected = after ? ApplicationResultFingerprint(target, match, plan) : plan.TargetFingerprint;
            if (Fingerprint(current) != expected) return unknown;
            return new(match.Status, match.TransactionId, plan.PlanSha256);
        }
        catch (Exception error) when (StatusEvidenceError(error)) { return unknown; }
    }

    /// <summary>状态入口只能读取本机明确的普通计划文件，链接和未知计划仍按严格原协议拒绝。</summary>
    public static GenerationUpdatePlan ReadApplicationStatusPlan(string path)
    {
        var full = Path.GetFullPath(path);
        CSharpGenerator.SafePath(Path.GetDirectoryName(full)!, Path.GetFileName(full));
        return ReadPlan(full);
    }

    /// <summary>输入计划先验证确认摘要、目标、范围和每项动作；无journal不能反向推断未应用。</summary>
    private static void ValidateApplicationStatusPlan(string target, GenerationUpdatePlan plan, string confirmation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateScopedPlan(plan);
        if (plan.SchemaVersion is not (1 or 2) || !plan.CanApply || plan.TargetDirectory != target || !ValidHash(confirmation) || confirmation != plan.PlanSha256
            || Hash(Encode(plan with { PlanSha256 = "" })) != confirmation || !ValidHash(plan.TargetFingerprint) || !ValidHash(plan.CandidateFingerprint)
            || !long.TryParse(plan.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var project) || project <= 0 || project.ToString(CultureInfo.InvariantCulture) != plan.ProjectId
            || !Regex.IsMatch(plan.RootNamespace ?? "", @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")
            || plan.RequiredActions is null || plan.RequiredActions.Count != 0 || plan.Actions is null || plan.Actions.Count is 0 or > 3
            || plan.Actions.Any(action => !Scopes.Contains(action, StringComparer.Ordinal)) || plan.Actions.Distinct(StringComparer.Ordinal).Count() != plan.Actions.Count
            || plan.Files is null || plan.Files.Count > 20000) throw new InvalidOperationException("状态查询的目标、计划或确认摘要无效。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            if (file is null || !paths.Add(file.Path)) throw new InvalidOperationException("状态查询计划含空项或重复路径。");
            ValidateRelative(file.Path);
            if (file.Action == "preserve")
            {
                if (file.Scope is not ("preserve" or "handwritten") || !ValidHash(file.BeforeSha256) || file.BeforeSha256 != file.AfterSha256) throw new InvalidOperationException("状态查询计划的保留项无效。");
                continue;
            }
            if (file.Action is not ("create" or "replace" or "delete") || file.Scope != "manifest" && !plan.Actions.Contains(file.Scope, StringComparer.Ordinal)
                || file.BeforeSha256 is not null && !ValidHash(file.BeforeSha256) || file.AfterSha256 is not null && !ValidHash(file.AfterSha256)
                || file.Action == "create" && (file.BeforeSha256 is not null || file.AfterSha256 is null)
                || file.Action == "replace" && (file.BeforeSha256 is null || file.AfterSha256 is null)
                || file.Action == "delete" && (file.BeforeSha256 is null || file.AfterSha256 is not null)) throw new InvalidOperationException("状态查询计划写入范围或摘要无效。");
        }
    }

    /// <summary>完整备份与暂存都仍属于该确认计划；证据丢失或漂移不能只凭终态字符串报成功。</summary>
    private static void VerifyStatusPayload(string target, UpdateJournal journal)
    {
        for (var index = 0; index < journal.Files.Length; index++)
        {
            var file = journal.Files[index];
            if (file.BeforeSha256 is not null && Hash(ReadBytes(target, Transaction(journal.TransactionId) + $"/backups/{index:D5}.bin")) != file.BeforeSha256)
                throw new InvalidOperationException("状态查询的原字节备份缺失或不一致。");
            if (file.AfterSha256 is not null && Hash(ReadBytes(target, Transaction(journal.TransactionId) + $"/staged/{index:D5}.bin")) != file.AfterSha256)
                throw new InvalidOperationException("状态查询的结果暂存缺失或不一致。");
        }
    }

    /// <summary>从本机已确认暂存根清单重建完整结果指纹，保留原partial/AppID而不读取退役候选。</summary>
    private static string ApplicationResultFingerprint(string target, UpdateJournal journal, GenerationUpdatePlan plan)
    {
        var marker = Array.FindIndex(journal.Files, file => file.Path == PackageMarker);
        var bytes = ReadBytes(target, marker < 0 ? Transaction(journal.TransactionId) + "/identity/package.json" : Transaction(journal.TransactionId) + $"/staged/{marker:D5}.bin");
        var package = Decode<PackageManifest>(bytes); var entries = Entries(package.Files);
        var planned = plan.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        return Hash(Encode(new
        {
            package = Hash(bytes),
            files = entries.Values.OrderBy(file => file.Path, StringComparer.Ordinal).Select(file => new
            {
                file.Path,
                expected = file.Sha256,
                actual = planned.TryGetValue(file.Path, out var item) ? item.Action == "preserve" ? item.BeforeSha256 : item.AfterSha256
                    : Protected.Contains(file.Path) || Regex.IsMatch(file.Path, @"^backend/generated/Services/[A-Za-z0-9_]+Service\.cs$") ? null : file.Sha256
            }).ToArray()
        }));
    }

    /// <summary>证据无法证明时只返回unknown，不修改日志，也不把未知当成可安全重新应用。</summary>
    private static bool StatusEvidenceError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException or NotSupportedException;
}
