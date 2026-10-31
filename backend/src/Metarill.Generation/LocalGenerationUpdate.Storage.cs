using System.Text.RegularExpressions;

namespace Metarill.Generation;

public sealed partial class LocalGenerationUpdater
{
    private const string Control = ".local/metarill-updates";
    private sealed record ActiveUpdate(int SchemaVersion, string TransactionId, string PlanSha256);
    private sealed record JournalFile(string Path, string Action, string Scope, string? BeforeSha256, string? AfterSha256);
    private sealed record UpdateJournal(int SchemaVersion, string TransactionId, string TargetDirectory, string ProjectId, string RootNamespace, string PlanSha256,
        string[] Actions, string Status, JournalFile[] Files, string BeforePackageSha256, string BeforeGenerationSha256, string? Failure, int[]? ConfirmedFrontendCreates = null);

    /// <summary>仅回滚本机已登记的未完成事务；恢复不读取候选，也不猜测或覆盖用户后来修改的内容。</summary>
    public GenerationUpdateReceipt Recover(string targetDirectory, string transactionId, string confirmation)
    {
        var target = Root(targetDirectory);
        using var updateLock = AcquireLock(target, ".metarill-update.lock"); using var generationLock = AcquireLock(target, "backend/generated/.metarill-generation.lock");
        var active = Decode<ActiveUpdate>(ReadBytes(target, Control + "/active.json"));
        if (active.SchemaVersion != 1 || active.TransactionId != transactionId || active.PlanSha256 != confirmation) throw new InvalidOperationException("恢复事务或确认摘要与本机待恢复日志不一致。");
        var journal = ReadJournal(target, transactionId);
        if (journal.PlanSha256 != confirmation) throw new InvalidOperationException("恢复日志摘要不一致。");
        if (journal.Status == "committed")
        {
            foreach (var file in journal.Files) if (ExistingHash(target, file.Path) != file.AfterSha256) throw new InvalidOperationException("已提交事务的文件又被修改，不能自动清理恢复状态。");
            File.Delete(Safe(target, Control + "/active.json")); return Receipt(journal, "committed");
        }
        if (journal.Status is not ("prepared" or "applying" or "manual-recovery" or "rolled-back")) throw new InvalidOperationException("未知恢复阶段，未执行恢复。");
        try { Rollback(target, journal); journal = journal with { Status = "rolled-back", Failure = null }; SaveJournal(target, journal); File.Delete(Safe(target, Control + "/active.json")); return Receipt(journal, "rolled-back"); }
        catch (Exception error) { SaveJournal(target, journal with { Status = "manual-recovery", Failure = error.GetType().Name }); throw new InvalidOperationException("仍有用户改动或备份冲突；现场已保留，请人工核对后重新恢复。", error); }
    }

    /// <summary>先持久化全部备份、候选和日志，再替换文件；清单最后提交，失败只恢复仍属于本次事务的字节。</summary>
    private GenerationUpdateReceipt Commit(GenerationUpdatePlan plan, Dictionary<string, byte[]> payload)
    {
        var changes = PlannedChanges(plan);
        if (changes.Length == 0) return new("", plan.PlanSha256, "unchanged", 0, 0, plan.Files.Count(file => file.Action == "preserve"));
        var id = Guid.NewGuid().ToString("N"); var target = plan.TargetDirectory;
        var oldPackage = ReadBytes(target, PackageMarker); var oldGeneration = ReadBytes(target, GenerationMarker);
        var journal = new UpdateJournal(3, id, target, plan.ProjectId, plan.RootNamespace, plan.PlanSha256, plan.Actions.ToArray(), "prepared", changes.Select(file => new JournalFile(file.Path, file.Action, file.Scope, file.BeforeSha256, file.AfterSha256)).ToArray(), Hash(oldPackage), Hash(oldGeneration), null, []);
        // 确认计划先于active持久化；恢复必须重算摘要，不能相信journal自称的PlanSha256。
        DurableWrite(target, Transaction(id) + "/plan.json", Encode(plan));
        // 两份旧身份清单始终留存，运行时更新未改generation marker时也不能依赖当前混合状态。
        DurableWrite(target, Transaction(id) + "/identity/package.json", oldPackage); DurableWrite(target, Transaction(id) + "/identity/generation.json", oldGeneration);
        for (var index = 0; index < changes.Length; index++)
        {
            var file = changes[index];
            if (ExistingHash(target, file.Path) != file.BeforeSha256) throw new InvalidOperationException("写入前文件发生变化：" + file.Path);
            if (file.BeforeSha256 is not null) DurableWrite(target, Transaction(id) + $"/backups/{index:D5}.bin", ReadBytes(target, file.Path));
            if (file.AfterSha256 is not null)
            {
                if (!payload.TryGetValue(file.Path, out var bytes) || Hash(bytes) != file.AfterSha256) throw new InvalidOperationException("候选内容与已确认计划不符。");
                DurableWrite(target, Transaction(id) + $"/staged/{index:D5}.bin", bytes);
            }
        }
        // 备份阶段也可能有外部编辑，开始任何源码写入前重新验证完整快照。
        if (Fingerprint(ReadPackage(target, false)) != plan.TargetFingerprint) throw new InvalidOperationException("准备备份期间目标发生变化，未执行源码更新。");
        SaveJournal(target, journal); DurableWrite(target, Control + "/active.json", Encode(new ActiveUpdate(1, id, plan.PlanSha256)));
        try
        {
            journal = journal with { Status = "applying" }; SaveJournal(target, journal);
            for (var index = 0; index < changes.Length; index++)
            {
                var file = changes[index];
                if (ExistingHash(target, file.Path) != file.BeforeSha256) throw new InvalidOperationException("应用期间文件发生变化：" + file.Path);
                BeforeWrite?.Invoke(file.Path);
                if (file.AfterSha256 is null) File.Delete(Safe(target, file.Path));
                else
                {
                    var bytes = ReadBytes(target, Transaction(id) + $"/staged/{index:D5}.bin");
                    if (Hash(bytes) != file.AfterSha256) throw new InvalidOperationException("实际写入前候选暂存摘要不一致。");
                    // create在最终原子发布也拒绝覆盖，保护最后预检后才出现的开发者文件。
                    DurableWrite(target, file.Path, bytes, overwrite: file.Action != "create");
                    if (IsFrontendCreate(journal.Files[index]))
                    {
                        // 确认先刷盘再赋值；发布后、确认前中断时只能保留未知归属路径，不能凭AfterHash认领。
                        AfterCreatePublication?.Invoke(file.Path);
                        var confirmed = journal with { ConfirmedFrontendCreates = [.. journal.ConfirmedFrontendCreates!, index] };
                        SaveJournal(target, confirmed); journal = confirmed;
                    }
                }
                AfterWrite?.Invoke(file.Path);
            }
            foreach (var file in plan.Files) if (ExistingHash(target, file.Path) != (file.Action == "preserve" ? file.BeforeSha256 : file.AfterSha256)) throw new InvalidOperationException("应用后的文件校验不一致：" + file.Path);
            ReadPackage(target, false);
            journal = journal with { Status = "committed" }; SaveJournal(target, journal); File.Delete(Safe(target, Control + "/active.json"));
            return Receipt(journal, "committed") with { PreservedFiles = plan.Files.Count(file => file.Action == "preserve") };
        }
        catch (Exception error)
        {
            try { Rollback(target, journal); SaveJournal(target, journal with { Status = "rolled-back", Failure = error.GetType().Name }); File.Delete(Safe(target, Control + "/active.json")); }
            catch (Exception recovery) { SaveJournal(target, journal with { Status = "manual-recovery", Failure = recovery.GetType().Name }); throw new InvalidOperationException($"更新失败且恢复未完成；保留事务 {id}，请使用recover与确认摘要 {plan.PlanSha256} 核对恢复。", new AggregateException(error, recovery)); }
            throw new InvalidOperationException("更新失败，已恢复本次修改，备份及回执保留在.local/metarill-updates。", error);
        }
    }

    /// <summary>恢复先验证所有备份；文件只可处于原始或本次结果状态，第三种内容一律保留。</summary>
    private void Rollback(string target, UpdateJournal journal)
    {
        for (var index = 0; index < journal.Files.Length; index++)
        {
            var file = journal.Files[index];
            if (file.BeforeSha256 is not null && Hash(ReadBytes(target, Transaction(journal.TransactionId) + $"/backups/{index:D5}.bin")) != file.BeforeSha256) throw new InvalidOperationException("恢复备份摘要不一致。");
            var current = ExistingHash(target, file.Path);
            RequireConfirmedFrontendCreate(target, journal, index, current);
            if (current != file.BeforeSha256 && current != file.AfterSha256) throw new InvalidOperationException("恢复路径出现用户后来修改的内容：" + file.Path);
        }
        for (var index = journal.Files.Length - 1; index >= 0; index--)
        {
            var file = journal.Files[index]; BeforeRestore?.Invoke(file.Path); var current = ExistingHash(target, file.Path);
            RequireConfirmedFrontendCreate(target, journal, index, current);
            if (current == file.BeforeSha256) continue;
            if (current != file.AfterSha256) throw new InvalidOperationException("恢复写入前出现用户后来修改的内容：" + file.Path);
            if (file.BeforeSha256 is null) File.Delete(Safe(target, file.Path));
            else
            {
                var bytes = ReadBytes(target, Transaction(journal.TransactionId) + $"/backups/{index:D5}.bin");
                if (Hash(bytes) != file.BeforeSha256) throw new InvalidOperationException("实际恢复前备份摘要不一致。");
                DurableWrite(target, file.Path, bytes);
            }
        }
        foreach (var file in journal.Files) if (ExistingHash(target, file.Path) != file.BeforeSha256) throw new InvalidOperationException("恢复后的文件摘要不一致。");
    }

    /// <summary>事务日志严格限定当前本机目录、GUID、动作和路径，不能成为任意文件恢复器。</summary>
    private static UpdateJournal ReadJournal(string target, string id)
    {
        var journal = Decode<UpdateJournal>(ReadBytes(target, Transaction(id) + "/journal.json"));
        if (journal.SchemaVersion is not (2 or 3) || journal.TransactionId != id || journal.TargetDirectory != target || !ValidHash(journal.PlanSha256)
            || journal.Actions is null || journal.Actions.Length == 0 || journal.Actions.Any(action => !Scopes.Contains(action, StringComparer.Ordinal)) || journal.Actions.Distinct(StringComparer.Ordinal).Count() != journal.Actions.Length
            || journal.Files is null || journal.Files.Length is 0 or > 20000 || !ValidHash(journal.BeforePackageSha256) || !ValidHash(journal.BeforeGenerationSha256)) throw new InvalidOperationException("事务日志身份、版本或范围无效。");
        var oldPackageBytes = ReadBytes(target, Transaction(id) + "/identity/package.json"); var oldGenerationBytes = ReadBytes(target, Transaction(id) + "/identity/generation.json");
        if (Hash(oldPackageBytes) != journal.BeforePackageSha256 || Hash(oldGenerationBytes) != journal.BeforeGenerationSha256) throw new InvalidOperationException("旧身份备份摘要不一致。");
        var oldPackage = Decode<PackageManifest>(oldPackageBytes); var oldGeneration = Decode<GeneratedManifest>(oldGenerationBytes); var oldEntries = Entries(oldPackage.Files);
        if (oldPackage.SchemaVersion != 1 || oldGeneration.Version != 1 || oldPackage.PackageMode is not("FullApplication" or "FullManagementApplication") || !oldPackage.FrontendIncluded
            || (oldPackage.PackageMode==ManagementMode)!=(oldPackage.ManagementModule is not null) || oldPackage.RootNamespace != journal.RootNamespace || oldGeneration.RootNamespace != journal.RootNamespace || oldGeneration.ProjectId != journal.ProjectId
            || !long.TryParse(journal.ProjectId, out var project) || project <= 0 || project.ToString(System.Globalization.CultureInfo.InvariantCulture) != journal.ProjectId
            || !Regex.IsMatch(journal.RootNamespace ?? "", @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") || oldPackage.MetadataSha256 != oldGeneration.MetadataSha256 || !ValidHash(oldPackage.MetadataSha256)
            || !oldEntries.TryGetValue(GenerationMarker, out var oldGenerationEntry) || oldGenerationEntry.Sha256 != journal.BeforeGenerationSha256) throw new InvalidOperationException("恢复日志与旧清单项目身份不一致。");
        ValidateRecoveryPlan(target, id, journal, oldEntries);
        var confirmedCreates = journal.ConfirmedFrontendCreates;
        if (journal.SchemaVersion == 3 && confirmedCreates is null || journal.SchemaVersion == 2 && confirmedCreates is { Length: > 0 }
            || confirmedCreates is not null && (!confirmedCreates.SequenceEqual(confirmedCreates.Distinct().Order()) || confirmedCreates.Any(index => index < 0 || index >= journal.Files.Length || !IsFrontendCreate(journal.Files[index])))) throw new InvalidOperationException("原壳归属确认索引无效，未执行恢复。");
        if (journal.Status == "committed" && journal.Files.Select((file,index)=>(file,index)).Any(item=>IsFrontendCreate(item.file) && !(confirmedCreates?.Contains(item.index) ?? false))) throw new InvalidOperationException("原壳发布缺少持久归属确认，不能宣称提交成功。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in journal.Files)
        {
            if (file is null) throw new InvalidOperationException("事务文件记录不能为空。");
            ValidateRelative(file.Path);
            var baseline = file.Path == PackageMarker ? journal.BeforePackageSha256 : oldEntries.GetValueOrDefault(file.Path)?.Sha256;
            // 受保护原壳仅允许本机已确认的create/no-before日志，绝不能借恢复执行替换或删除。
            var missingFrontendShell = journal.SchemaVersion == 3 && IsFrontendCreate(file);
            var missingShell = file.BeforeSha256 is null && Regex.IsMatch(file.Path, @"^backend/generated/Services/[A-Za-z0-9_]+Service\.cs$") || missingFrontendShell;
            if (baseline != file.BeforeSha256 && !missingShell) throw new InvalidOperationException("恢复文件旧摘要不属于本次旧清单。");
            if (missingShell && file.AfterSha256 is null) throw new InvalidOperationException("手写壳只能首次创建。");
            if (!seen.Add(file.Path) || file.BeforeSha256 is not null && !ValidHash(file.BeforeSha256) || file.AfterSha256 is not null && !ValidHash(file.AfterSha256) || file.BeforeSha256 is null && file.AfterSha256 is null || Protected.Contains(file.Path) && !missingFrontendShell) throw new InvalidOperationException("事务路径或摘要无效。");
            if (file.Path is PackageMarker or GenerationMarker or FrontendMarker or "backend/generated/frontend/common/yzp_runtime/page-write-schema.generated.json" || oldPackage.PackageMode==ManagementMode&&(file.Path==PreparedSecurityManagementSource.SourceMarker||file.Path==PreparedSecurityManagementSource.ApplicationPath||ManagementFrontend.Contains(file.Path)||file.Path.StartsWith("backend/generated/",StringComparison.Ordinal)&&ManagementFrontend.Contains(file.Path["backend/generated/".Length..])) || Configuration.Contains(file.Path) || RuntimeFiles.Contains(file.Path) || FrontendAssets.Value.Contains(file.Path) || missingFrontendShell
                || Regex.IsMatch(file.Path, @"^(?:src/Metarill\.(?:Core|Data|Exports|Files|Security|Agent|Mcp)/[A-Za-z0-9_./-]+|frontend/common/yzp_runtime/[A-Za-z0-9_-]+\.js|backend/generated/(?:[A-Za-z0-9_/-]+\.(?:g\.cs|vue|json)|Services/[A-Za-z0-9_]+Service\.cs)|frontend/pages/yzp/[A-Za-z0-9_-]+\.vue|frontend/common/yzp_runtime/page-write-schema\.generated\.json)$")) continue;
            throw new InvalidOperationException("恢复日志包含未知写入路径。");
        }
        return journal;
    }

    /// <summary>只有固定原壳的Runtime create/no-before可登记发布归属；索引始终绑定完整已确认计划。</summary>
    private static bool IsFrontendCreate(JournalFile? file) => file is not null && FirstCreateFrontend.Contains(file.Path) && file.Action == "create" && file.Scope == Runtime && file.BeforeSha256 is null && file.AfterSha256 is not null;
    /// <summary>Move未成功或确认前中断时，存在路径即保留，哪怕字节恰好等于候选；人工核查移走后才可继续恢复。</summary>
    private static void RequireConfirmedFrontendCreate(string target, UpdateJournal journal, int index, string? current)
    {
        var file=journal.Files[index];
        if(IsFrontendCreate(file) && !(journal.ConfirmedFrontendCreates?.Contains(index) ?? false) && (current is not null || Directory.Exists(Safe(target,file.Path)))) throw new InvalidOperationException("原壳创建归属尚未确认，请先人工审查并移走该路径后再恢复："+file.Path);
    }

    /// <summary>完整原计划绑定确认摘要与每条journal写入，新增路径也不能靠自报hash变成恢复目标。</summary>
    private static void ValidateRecoveryPlan(string target, string id, UpdateJournal journal, Dictionary<string, UpdateEntry> oldEntries)
    {
        var plan = Decode<GenerationUpdatePlan>(ReadBytes(target, Transaction(id) + "/plan.json"));
        ValidateScopedPlan(plan);
        if (plan.SchemaVersion is not (1 or 2) || !plan.CanApply || plan.RequiredActions is null || plan.RequiredActions.Count != 0 || plan.Actions is null || plan.Files is null || plan.Files.Count is 0 or > 20000
            || plan.TargetDirectory != target || plan.ProjectId != journal.ProjectId || plan.RootNamespace != journal.RootNamespace
            || plan.PlanSha256 != journal.PlanSha256 || Hash(Encode(plan with { PlanSha256 = "" })) != journal.PlanSha256
            || !ValidHash(plan.TargetFingerprint) || !ValidHash(plan.CandidateFingerprint) || !plan.Actions.SequenceEqual(journal.Actions, StringComparer.Ordinal)) throw new InvalidOperationException("持久化计划摘要、身份或动作与恢复日志不一致。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            if (file is null || !seen.Add(file.Path) || file.Action is not ("create" or "replace" or "delete" or "preserve")) throw new InvalidOperationException("持久化计划包含未知动作或重复路径。");
            ValidateRelative(file.Path);
            if (file.Action == "preserve")
            {
                if (file.Scope is not ("preserve" or "handwritten") || !ValidHash(file.BeforeSha256) || file.BeforeSha256 != file.AfterSha256) throw new InvalidOperationException("保留项计划无效。");
                continue;
            }
            if (file.Scope != "manifest" && !plan.Actions.Contains(file.Scope, StringComparer.Ordinal)
                || file.BeforeSha256 is not null && !ValidHash(file.BeforeSha256) || file.AfterSha256 is not null && !ValidHash(file.AfterSha256)
                || file.Action == "create" && (file.BeforeSha256 is not null || file.AfterSha256 is null)
                || file.Action == "replace" && (file.BeforeSha256 is null || file.AfterSha256 is null)
                || file.Action == "delete" && (file.BeforeSha256 is null || file.AfterSha256 is not null)) throw new InvalidOperationException("持久化计划写入范围或前后摘要无效。");
        }
        var expected = PlannedChanges(plan);
        if (expected.Length != journal.Files.Length) throw new InvalidOperationException("恢复日志写入集合与已确认计划不一致。");
        for (var index = 0; index < expected.Length; index++)
        {
            var file = expected[index]; var actual = journal.Files[index];
            if (actual is null || actual.Path != file.Path || actual.Action != file.Action || actual.Scope != file.Scope || actual.BeforeSha256 != file.BeforeSha256 || actual.AfterSha256 != file.AfterSha256) throw new InvalidOperationException("恢复日志写入条目与已确认计划不一致。");
        }
        // 仅补建缺失手写壳时三个清单可能完全不变，因此不能要求计划必须改写根marker。
        // 用原计划登记的保留项/缺失壳状态和旧受管hash重建原指纹，始终绑定完整旧包身份。
        var planned = plan.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var fingerprint = Hash(Encode(new { package = journal.BeforePackageSha256, files = oldEntries.Values.OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => new { file.Path, expected = file.Sha256, actual = Protected.Contains(file.Path) || Regex.IsMatch(file.Path, @"^backend/generated/Services/[A-Za-z0-9_]+Service\.cs$") ? planned.GetValueOrDefault(file.Path)?.BeforeSha256 : file.Sha256 }).ToArray() }));
        if (fingerprint != plan.TargetFingerprint) throw new InvalidOperationException("旧包身份与文件基线不属于已确认计划。");
    }

    /// <summary>应用与恢复共用稳定的写入顺序，不能分别重排后误配备份编号。</summary>
    private static GenerationUpdateFile[] PlannedChanges(GenerationUpdatePlan plan) => plan.Files.Where(file => file.Action != "preserve")
        .OrderBy(file => file.Scope == "manifest" ? 1 : 0).ThenBy(file => file.Path == PackageMarker ? 1 : 0).ThenBy(file => file.Path, StringComparer.Ordinal).ToArray();

    /// <summary>存在未完成任务时不允许新的预览/应用，先由显式recover核对恢复。</summary>
    private static void RejectPending(string target)
    {
        var active = Safe(target, Control + "/active.json");
        if (File.Exists(active)) throw new InvalidOperationException("存在未完成的本地更新，请先核对.local/metarill-updates/active.json并执行recover。");
    }
    /// <summary>与直接生成共用第二道进程互斥锁；不终止其他进程或绕过正在占用的锁。</summary>
    private static FileStream AcquireLock(string target, string path)
    {
        try { return new FileStream(Safe(target, path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (IOException error) { throw new InvalidOperationException("当前项目已有生成或更新任务持锁，请稍后重新预览。", error); }
    }
    /// <summary>日志和源码均以同目录临时文件、强制刷盘及原子替换写入。</summary>
    private static void DurableWrite(string target, string relative, byte[] bytes, bool overwrite = true)
    {
        var path = Safe(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); Safe(target, relative);
        var temp = path + ".metarill-update-" + Guid.NewGuid().ToString("N");
        try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); } Safe(target, relative); File.Move(temp, path, overwrite); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    /// <summary>事务目录只接受本工具生成的GUID，不允许日志参数变成路径。</summary>
    private static string Transaction(string id) => Regex.IsMatch(id, "^[a-f0-9]{32}$") ? Control + "/transactions/" + id : throw new InvalidOperationException("事务标识无效。");
    /// <summary>每次阶段转换先持久化日志，恢复不依赖进程内状态。</summary>
    private static void SaveJournal(string target, UpdateJournal journal) => DurableWrite(target, Transaction(journal.TransactionId) + "/journal.json", Encode(journal));
    /// <summary>回执只报告本次源码文件数量与最终状态，不混入数据库验收结论。</summary>
    private static GenerationUpdateReceipt Receipt(UpdateJournal journal, string status) => new(journal.TransactionId, journal.PlanSha256, status, journal.Files.Count(file => file.AfterSha256 is not null), journal.Files.Count(file => file.AfterSha256 is null), 0);
}
