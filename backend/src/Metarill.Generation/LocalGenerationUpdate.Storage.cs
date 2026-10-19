using System.Text.RegularExpressions;

namespace Metarill.Generation;

public sealed partial class LocalGenerationUpdater
{
    private const string Control = ".local/metarill-updates";
    private sealed record ActiveUpdate(int SchemaVersion, string TransactionId, string PlanSha256);
    private sealed record JournalFile(string Path, string? BeforeSha256, string? AfterSha256);
    private sealed record UpdateJournal(int SchemaVersion, string TransactionId, string TargetDirectory, string ProjectId, string RootNamespace, string PlanSha256,
        string[] Actions, string Status, JournalFile[] Files, string BeforePackageSha256, string BeforeGenerationSha256, string? Failure);

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
        var changes = plan.Files.Where(file => file.Action != "preserve").OrderBy(file => file.Scope == "manifest" ? 1 : 0).ThenBy(file => file.Path == PackageMarker ? 1 : 0).ThenBy(file => file.Path, StringComparer.Ordinal).ToArray();
        if (changes.Length == 0) return new("", plan.PlanSha256, "unchanged", 0, 0, plan.Files.Count(file => file.Action == "preserve"));
        var id = Guid.NewGuid().ToString("N"); var target = plan.TargetDirectory;
        var oldPackage = ReadBytes(target, PackageMarker); var oldGeneration = ReadBytes(target, GenerationMarker);
        var journal = new UpdateJournal(1, id, target, plan.ProjectId, plan.RootNamespace, plan.PlanSha256, plan.Actions.ToArray(), "prepared", changes.Select(file => new JournalFile(file.Path, file.BeforeSha256, file.AfterSha256)).ToArray(), Hash(oldPackage), Hash(oldGeneration), null);
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
                if (file.AfterSha256 is null) File.Delete(Safe(target, file.Path));
                else DurableWrite(target, file.Path, ReadBytes(target, Transaction(id) + $"/staged/{index:D5}.bin"));
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
            if (current != file.BeforeSha256 && current != file.AfterSha256) throw new InvalidOperationException("恢复路径出现用户后来修改的内容：" + file.Path);
        }
        for (var index = journal.Files.Length - 1; index >= 0; index--)
        {
            var file = journal.Files[index]; BeforeRestore?.Invoke(file.Path); var current = ExistingHash(target, file.Path);
            if (current == file.BeforeSha256) continue;
            if (current != file.AfterSha256) throw new InvalidOperationException("恢复写入前出现用户后来修改的内容：" + file.Path);
            if (file.BeforeSha256 is null) File.Delete(Safe(target, file.Path));
            else DurableWrite(target, file.Path, ReadBytes(target, Transaction(journal.TransactionId) + $"/backups/{index:D5}.bin"));
        }
        foreach (var file in journal.Files) if (ExistingHash(target, file.Path) != file.BeforeSha256) throw new InvalidOperationException("恢复后的文件摘要不一致。");
    }

    /// <summary>事务日志严格限定当前本机目录、GUID、动作和路径，不能成为任意文件恢复器。</summary>
    private static UpdateJournal ReadJournal(string target, string id)
    {
        var journal = Decode<UpdateJournal>(ReadBytes(target, Transaction(id) + "/journal.json"));
        if (journal.SchemaVersion != 1 || journal.TransactionId != id || journal.TargetDirectory != target || !ValidHash(journal.PlanSha256)
            || journal.Actions is null || journal.Actions.Length == 0 || journal.Actions.Any(action => !Scopes.Contains(action, StringComparer.Ordinal)) || journal.Actions.Distinct(StringComparer.Ordinal).Count() != journal.Actions.Length
            || journal.Files is null || journal.Files.Length is 0 or > 20000 || !ValidHash(journal.BeforePackageSha256) || !ValidHash(journal.BeforeGenerationSha256)) throw new InvalidOperationException("事务日志身份、版本或范围无效。");
        var oldPackageBytes = ReadBytes(target, Transaction(id) + "/identity/package.json"); var oldGenerationBytes = ReadBytes(target, Transaction(id) + "/identity/generation.json");
        if (Hash(oldPackageBytes) != journal.BeforePackageSha256 || Hash(oldGenerationBytes) != journal.BeforeGenerationSha256) throw new InvalidOperationException("旧身份备份摘要不一致。");
        var oldPackage = Decode<PackageManifest>(oldPackageBytes); var oldGeneration = Decode<GeneratedManifest>(oldGenerationBytes); var oldEntries = Entries(oldPackage.Files);
        if (oldPackage.SchemaVersion != 1 || oldGeneration.Version != 1 || oldPackage.PackageMode != "FullApplication" || !oldPackage.FrontendIncluded
            || oldPackage.RootNamespace != journal.RootNamespace || oldGeneration.RootNamespace != journal.RootNamespace || oldGeneration.ProjectId != journal.ProjectId
            || !long.TryParse(journal.ProjectId, out var project) || project <= 0 || project.ToString(System.Globalization.CultureInfo.InvariantCulture) != journal.ProjectId
            || !Regex.IsMatch(journal.RootNamespace ?? "", @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$") || oldPackage.MetadataSha256 != oldGeneration.MetadataSha256 || !ValidHash(oldPackage.MetadataSha256)
            || !oldEntries.TryGetValue(GenerationMarker, out var oldGenerationEntry) || oldGenerationEntry.Sha256 != journal.BeforeGenerationSha256) throw new InvalidOperationException("恢复日志与旧清单项目身份不一致。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in journal.Files)
        {
            if (file is null) throw new InvalidOperationException("事务文件记录不能为空。");
            ValidateRelative(file.Path);
            var baseline = file.Path == PackageMarker ? journal.BeforePackageSha256 : oldEntries.GetValueOrDefault(file.Path)?.Sha256;
            var missingShell = file.BeforeSha256 is null && Regex.IsMatch(file.Path, @"^backend/generated/Services/[A-Za-z0-9_]+Service\.cs$");
            if (baseline != file.BeforeSha256 && !missingShell) throw new InvalidOperationException("恢复文件旧摘要不属于本次旧清单。");
            if (missingShell && file.AfterSha256 is null) throw new InvalidOperationException("手写壳只能首次创建。");
            if (!seen.Add(file.Path) || file.BeforeSha256 is not null && !ValidHash(file.BeforeSha256) || file.AfterSha256 is not null && !ValidHash(file.AfterSha256) || file.BeforeSha256 is null && file.AfterSha256 is null || Protected.Contains(file.Path)) throw new InvalidOperationException("事务路径或摘要无效。");
            if (file.Path is PackageMarker or GenerationMarker or FrontendMarker || Configuration.Contains(file.Path) || RuntimeFiles.Contains(file.Path) || FrontendAssets.Value.Contains(file.Path)
                || Regex.IsMatch(file.Path, @"^(?:src/Metarill\.(?:Core|Data|Exports|Files)/[A-Za-z0-9_./-]+|frontend/common/yzp_runtime/[A-Za-z0-9_-]+\.js|backend/generated/(?:[A-Za-z0-9_/-]+\.(?:g\.cs|vue|json)|Services/[A-Za-z0-9_]+Service\.cs)|frontend/pages/yzp/[A-Za-z0-9_-]+\.vue|frontend/common/yzp_runtime/page-write-schema\.generated\.json)$")) continue;
            throw new InvalidOperationException("恢复日志包含未知写入路径。");
        }
        return journal;
    }

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
    private static void DurableWrite(string target, string relative, byte[] bytes)
    {
        var path = Safe(target, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); Safe(target, relative);
        var temp = path + ".metarill-update-" + Guid.NewGuid().ToString("N");
        try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); } Safe(target, relative); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    /// <summary>事务目录只接受本工具生成的GUID，不允许日志参数变成路径。</summary>
    private static string Transaction(string id) => Regex.IsMatch(id, "^[a-f0-9]{32}$") ? Control + "/transactions/" + id : throw new InvalidOperationException("事务标识无效。");
    /// <summary>每次阶段转换先持久化日志，恢复不依赖进程内状态。</summary>
    private static void SaveJournal(string target, UpdateJournal journal) => DurableWrite(target, Transaction(journal.TransactionId) + "/journal.json", Encode(journal));
    /// <summary>回执只报告本次源码文件数量与最终状态，不混入数据库验收结论。</summary>
    private static GenerationUpdateReceipt Receipt(UpdateJournal journal, string status) => new(journal.TransactionId, journal.PlanSha256, status, journal.Files.Count(file => file.AfterSha256 is not null), journal.Files.Count(file => file.AfterSha256 is null), 0);
}
