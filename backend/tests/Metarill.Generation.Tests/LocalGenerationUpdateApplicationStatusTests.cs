using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>没有本机日志只表示未知，错误确认或其他目标须在读写锁之前明确拒绝。</summary>
    [Fact]
    public void Application_status_without_journal_is_unknown_and_inputs_are_strict()
    {
        var next = Catalog(); next.YzProjectName = "状态候选";
        var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All);
        var before = ApplicationStatusSnapshot(target); var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256);
        Assert.Equal("unknown", result.Status); Assert.Null(result.TransactionId); Assert.Equal(plan.PlanSha256, result.PlanSha256);
        Assert.Throws<InvalidOperationException>(() => updater.InspectApplicationStatus(target, plan, new string('0', 64)));
        Assert.Throws<InvalidOperationException>(() => updater.InspectApplicationStatus(candidate, plan, plan.PlanSha256));
        Assert.Throws<InvalidOperationException>(() => updater.InspectApplicationStatus(target, plan with { CandidateDirectory = candidate + "-changed" }, plan.PlanSha256));
        Assert.Equal(before, ApplicationStatusSnapshot(target));
    }

    /// <summary>真实提交、增删受管页或首次补壳均核对完整结果；自有partial和AppID无需依赖仍在线的候选目录。</summary>
    [Theory]
    [InlineData("normal")] [InlineData("missingShell")] [InlineData("addedPage")] [InlineData("removedPage")]
    public void Application_status_proves_real_commit_without_candidate_and_preserves_local_bytes(string scenario)
    {
        var old = scenario == "removedPage" ? WithExtraPage(Catalog()) : Catalog();
        var next = scenario == "addedPage" ? WithExtraPage(Catalog()) : Catalog(); if (scenario == "normal") next.YzProjectName = "提交后的项目名";
        var (target, candidate) = Packages(old, next); const string service = "backend/generated/Services/DemoCustomerService.cs";
        if (scenario == "missingShell") File.Delete(Path.Combine(target, service));
        else File.AppendAllText(Path.Combine(target, service), "\n// 此处属于开发者的手写partial。\n");
        var appidPath = Path.Combine(target, "frontend/manifest.json"); var appid = JsonNode.Parse(File.ReadAllText(appidPath))!;
        appid["appid"] = "__UNI__APPLICATION_STATUS_TEST"; File.WriteAllText(appidPath, appid.ToJsonString(Json));
        var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); var receipt = updater.Apply(plan, plan.PlanSha256);
        Directory.Move(candidate, Path.Combine(root, "retired-candidate")); var before = ApplicationStatusSnapshot(target);
        var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256);
        Assert.Equal("committed", result.Status); Assert.Equal(receipt.TransactionId, result.TransactionId); Assert.Equal(plan.PlanSha256, result.PlanSha256);
        Assert.Equal(before, ApplicationStatusSnapshot(target)); Assert.Contains("__UNI__APPLICATION_STATUS_TEST", File.ReadAllText(appidPath));
    }

    /// <summary>真实替换故障自动回滚后，状态查询证明所有原字节仍在，不执行第二次恢复。</summary>
    [Fact]
    public void Application_status_proves_automatic_rollback_from_real_write_failure()
    {
        var next = Catalog(); next.YzProjectName = "自动回滚候选"; var (target, candidate) = Packages(Catalog(), next); var before = SourceSnapshot(target);
        var writes = 0; var updater = new LocalGenerationUpdater { AfterWrite = _ => { if (++writes == 2) throw new IOException("真实替换之后的故障"); } };
        var plan = updater.Preview(target, candidate, All); Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256));
        Assert.Equal(before, SourceSnapshot(target)); var snapshot = ApplicationStatusSnapshot(target); var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256);
        Assert.Equal("rolled-back", result.Status); Assert.Equal(Path.GetFileName(Assert.Single(Directory.GetDirectories(Path.Combine(target, ".local/metarill-updates/transactions")))), result.TransactionId);
        Assert.Equal(snapshot, ApplicationStatusSnapshot(target));
    }

    /// <summary>第三种内容使真实事务停在待恢复；状态查询只给原本机ID，不删除active或覆盖外部编辑。</summary>
    [Fact]
    public void Application_status_reports_local_recovery_required_without_restoring_or_clearing_active()
    {
        var next = Catalog(); next.YzProjectName = "保留中断"; var (target, candidate) = Packages(Catalog(), next);
        string? edited = null; byte[]? original = null; var updater = new LocalGenerationUpdater
        {
            AfterWrite = path => { edited = path; File.WriteAllText(Path.Combine(target, path), "// 外部第三种内容\n"); throw new IOException("中断"); }
        };
        var plan = updater.Preview(target, candidate, All); var first = plan.Files.Where(file => file.Action != "preserve").OrderBy(file => file.Scope == "manifest" ? 1 : 0).ThenBy(file => file.Path == ".metarill-package.json" ? 1 : 0).ThenBy(file => file.Path, StringComparer.Ordinal).First();
        original = File.ReadAllBytes(Path.Combine(target, first.Path)); Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256)); Assert.Equal(first.Path, edited);
        var activePath = Path.Combine(target, ".local/metarill-updates/active.json"); var active = JsonNode.Parse(File.ReadAllText(activePath))!; var id = active["transactionId"]!.GetValue<string>();
        var before = ApplicationStatusSnapshot(target); var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256);
        Assert.Equal("recovery-required", result.Status); Assert.Equal(id, result.TransactionId); Assert.Equal(before, ApplicationStatusSnapshot(target));
        var originalActive = File.ReadAllText(activePath); active["planSha256"] = new string('0', 64); File.WriteAllText(activePath, active.ToJsonString(Json));
        var other = ApplicationStatusSnapshot(target); Assert.Throws<InvalidOperationException>(() => updater.InspectApplicationStatus(target, plan, plan.PlanSha256)); Assert.Equal(other, ApplicationStatusSnapshot(target)); File.WriteAllText(activePath, originalActive);
        File.WriteAllBytes(Path.Combine(target, edited!), original); Assert.Equal("rolled-back", updater.Recover(target, id, plan.PlanSha256).Status);
        Assert.Equal("rolled-back", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
    }

    /// <summary>终态active尚未清理不能被只读查询伪装为完成，只有显式原recover才能完成该步骤。</summary>
    [Fact]
    public void Application_status_keeps_terminal_active_pending_until_explicit_recover()
    {
        var next = Catalog(); next.YzProjectName = "终态active"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); var receipt = updater.Apply(plan, plan.PlanSha256);
        File.WriteAllText(Path.Combine(target, ".local/metarill-updates/active.json"), JsonSerializer.Serialize(new { schemaVersion = 1, transactionId = receipt.TransactionId, planSha256 = receipt.PlanSha256 }, Json));
        var before = ApplicationStatusSnapshot(target); var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256);
        Assert.Equal("recovery-required", result.Status); Assert.Equal(receipt.TransactionId, result.TransactionId); Assert.Equal(before, ApplicationStatusSnapshot(target));
        Assert.Equal("committed", updater.Recover(target, receipt.TransactionId, plan.PlanSha256).Status); Assert.Equal("committed", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
    }

    /// <summary>原计划、身份、日志集合、备份或暂存任一缺失或篡改都不能只凭终态字符串宣称提交。</summary>
    [Fact]
    public void Application_status_rejects_missing_and_tampered_local_evidence_without_writing_it()
    {
        var next = Catalog(); next.YzProjectName = "证据候选"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); var receipt = updater.Apply(plan, plan.PlanSha256);
        var transaction = Path.Combine(target, ".local/metarill-updates/transactions", receipt.TransactionId); var journal = Path.Combine(transaction, "journal.json"); var persistentPlan = Path.Combine(transaction, "plan.json");
        foreach (var relative in new[] { "journal.json", "plan.json", "identity/package.json", "identity/generation.json", "backups/00000.bin", "staged/00000.bin" })
        {
            var path = Path.Combine(transaction, relative); var bytes = File.ReadAllBytes(path);
            foreach (var missing in new[] { true, false })
            {
                if (missing) File.Delete(path); else File.WriteAllText(path, "{}\n");
                var before = ApplicationStatusSnapshot(target); var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256);
                Assert.Equal("unknown", result.Status); Assert.Null(result.TransactionId); Assert.Equal(before, ApplicationStatusSnapshot(target)); File.WriteAllBytes(path, bytes);
            }
        }
        var original = File.ReadAllText(journal);
        foreach (var kind in new[] { "status", "identity", "write", "unknownStatus" })
        {
            var data = JsonNode.Parse(original)!;
            if (kind == "status") data["status"] = "rolled-back";
            if (kind == "identity") data["projectId"] = "2200000000000999999";
            if (kind == "write") data["files"]![0]!["afterSha256"] = new string('0', 64);
            if (kind == "unknownStatus") data["status"] = "server-says-complete";
            File.WriteAllText(journal, data.ToJsonString(Json)); var before = ApplicationStatusSnapshot(target);
            Assert.Equal("unknown", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status); Assert.Equal(before, ApplicationStatusSnapshot(target)); File.WriteAllText(journal, original);
        }
        var originalPlan = File.ReadAllText(persistentPlan); var changedPlan = JsonNode.Parse(originalPlan)!; changedPlan["candidateDirectory"] = Path.Combine(root, "forged"); File.WriteAllText(persistentPlan, changedPlan.ToJsonString(Json));
        Assert.Equal("unknown", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status); File.WriteAllText(persistentPlan, originalPlan);
        Assert.Equal("committed", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
    }

    /// <summary>受管结果、保留partial、AppID和未改变运行时任何漂移，都不能从旧提交日志推断当前仍完成。</summary>
    [Fact]
    public void Application_status_preserves_third_state_and_checks_complete_current_package()
    {
        var next = Catalog(); next.YzProjectName = "完整包候选"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); updater.Apply(plan, plan.PlanSha256);
        foreach (var relative in new[] { "backend/metadata/project.json", "backend/generated/Services/DemoCustomerService.cs", "frontend/manifest.json", "frontend/common/utils.js" })
        {
            var path = Path.Combine(target, relative); var original = File.ReadAllBytes(path); File.AppendAllText(path, "\n// 提交之后开发者编辑\n"); var before = ApplicationStatusSnapshot(target);
            var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256); Assert.Equal("unknown", result.Status); Assert.Null(result.TransactionId); Assert.Equal(before, ApplicationStatusSnapshot(target)); File.WriteAllBytes(path, original);
        }
        Assert.Equal("committed", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
    }

    /// <summary>同一计划真实先回滚后再次提交会留下两个匹配事务，状态不得挑选一个猜测调用结果。</summary>
    [Fact]
    public void Application_status_is_unknown_for_multiple_matching_real_transactions()
    {
        var next = Catalog(); next.YzProjectName = "重复事务"; var (target, candidate) = Packages(Catalog(), next); var failed = new LocalGenerationUpdater { AfterWrite = _ => throw new IOException("自动回滚") }; var plan = failed.Preview(target, candidate, All);
        Assert.Throws<InvalidOperationException>(() => failed.Apply(plan, plan.PlanSha256)); var updater = new LocalGenerationUpdater(); Assert.Equal("committed", updater.Apply(plan, plan.PlanSha256).Status);
        var before = ApplicationStatusSnapshot(target); var result = updater.InspectApplicationStatus(target, plan, plan.PlanSha256); Assert.Equal("unknown", result.Status); Assert.Null(result.TransactionId); Assert.Equal(before, ApplicationStatusSnapshot(target));
    }

    /// <summary>状态读取复用两道锁；日志目录或计划祖先链接不能变成读取项目外证据的捷径。</summary>
    [Fact]
    public void Application_status_respects_existing_locks_and_refuses_external_links()
    {
        var next = Catalog(); next.YzProjectName = "锁与链接"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); updater.Apply(plan, plan.PlanSha256);
        foreach (var relative in new[] { ".metarill-update.lock", "backend/generated/.metarill-generation.lock" })
        {
            var before = ApplicationStatusSnapshot(target);
            using (var held = new FileStream(Path.Combine(target, relative), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) Assert.Throws<InvalidOperationException>(() => updater.InspectApplicationStatus(target, plan, plan.PlanSha256));
            Assert.Equal(before, ApplicationStatusSnapshot(target));
        }
        var link = Path.Combine(target, ".local/metarill-updates/transactions"); var outside = Path.Combine(root, "external-transactions"); Directory.Move(link, outside); ApplicationStatusLink(link, outside);
        try { var before = ApplicationStatusSnapshot(outside); Assert.Equal("unknown", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status); Assert.Equal(before, ApplicationStatusSnapshot(outside)); }
        finally { Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0); Directory.Delete(link, false); Directory.Move(outside, link); }
        var real = Path.Combine(root, "real-plans"); Directory.CreateDirectory(real); LocalGenerationUpdater.WritePlan(plan, Path.Combine(real, "plan.json")); var planLink = Path.Combine(root, "linked-plans"); ApplicationStatusLink(planLink, real);
        try { Assert.Throws<InvalidOperationException>(() => LocalGenerationUpdater.ReadApplicationStatusPlan(Path.Combine(planLink, "plan.json"))); }
        finally { Assert.True((File.GetAttributes(planLink) & FileAttributes.ReparsePoint) != 0); Directory.Delete(planLink, false); }
    }

    /// <summary>真实CLI只接受本机目标、计划与确认，输出固定三字段，不接受远端事务或候选路径。</summary>
    [Fact]
    public async Task Application_status_cli_uses_real_process_and_strict_local_arguments()
    {
        var next = Catalog(); next.YzProjectName = "CLI状态"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); var planPath = Path.Combine(root, "status-plan.json"); LocalGenerationUpdater.WritePlan(plan, planPath);
        var binaries = Path.Combine(root, "status-cli"); var built = await Cli(SourceRoot(), "build", "backend/src/Metarill.Cli/Metarill.Cli.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet", "--output", binaries); Assert.True(built.Code == 0, built.Output); var cli = Path.Combine(binaries, "Metarill.Cli.dll");
        var arguments = new[] { cli, "update", "apply-status", "--target", target, "--plan", planPath, "--confirm", plan.PlanSha256 };
        var before = ApplicationStatusSnapshot(target); var unknown = await Cli(SourceRoot(), arguments); Assert.True(unknown.Code == 0, unknown.Output); Assert.Equal("unknown", JsonNode.Parse(unknown.Output)!["status"]!.GetValue<string>()); Assert.Equal(before, ApplicationStatusSnapshot(target));
        foreach (var extra in new[] { new[] { "--transaction", Guid.NewGuid().ToString("N") }, new[] { "--candidate", candidate }, new[] { "--target", target } }) Assert.Equal(1, (await Cli(SourceRoot(), arguments.Concat(extra).ToArray())).Code);
        var badConfirm = arguments.ToArray(); badConfirm[^1] = new string('0', 64); Assert.Equal(1, (await Cli(SourceRoot(), badConfirm)).Code);
        var wrongTarget = arguments.ToArray(); wrongTarget[4] = candidate; Assert.Equal(1, (await Cli(SourceRoot(), wrongTarget)).Code); Assert.Equal(before, ApplicationStatusSnapshot(target));
        var receipt = updater.Apply(plan, plan.PlanSha256); before = ApplicationStatusSnapshot(target); var committed = await Cli(SourceRoot(), arguments); Assert.True(committed.Code == 0, committed.Output);
        using var json = JsonDocument.Parse(committed.Output); Assert.Equal(new[] { "planSha256", "status", "transactionId" }, json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("committed", json.RootElement.GetProperty("status").GetString()); Assert.Equal(receipt.TransactionId, json.RootElement.GetProperty("transactionId").GetString()); Assert.Equal(plan.PlanSha256, json.RootElement.GetProperty("planSha256").GetString()); Assert.DoesNotContain(root, committed.Output); Assert.Equal(before, ApplicationStatusSnapshot(target));
    }

    /// <summary>原壳已持久确认才可证明提交；旧v2仅无原壳create的事务兼容，不能伪造确认。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Application_status_handles_v3_shell_proof_and_legacy_v2_without_claiming_unconfirmed_create(bool frontendShell)
    {
        var next = Catalog(); if (!frontendShell) next.YzProjectName = "旧日志兼容";
        var (target, candidate) = Packages(Catalog(), next); const string relative = "frontend/common/project.js";
        if (frontendShell) File.Delete(Path.Combine(target, relative));
        var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, frontendShell ? ["sync-runtime"] : All); var receipt = updater.Apply(plan, plan.PlanSha256);
        Directory.Move(candidate, Path.Combine(root, "status-retired-candidate"));
        var journalPath = Path.Combine(target, ".local/metarill-updates/transactions", receipt.TransactionId, "journal.json"); var original = File.ReadAllText(journalPath); var journal = JsonNode.Parse(original)!;
        Assert.Equal(3, journal["schemaVersion"]!.GetValue<int>());
        Assert.Equal("committed", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
        if (frontendShell)
        {
            Assert.Single(journal["confirmedFrontendCreates"]!.AsArray()); journal["confirmedFrontendCreates"] = new JsonArray(); File.WriteAllText(journalPath, journal.ToJsonString(Json));
            var before = ApplicationStatusSnapshot(target); Assert.Equal("unknown", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status); Assert.Equal(before, ApplicationStatusSnapshot(target));
        }
        journal["schemaVersion"] = 2; journal.AsObject().Remove("confirmedFrontendCreates"); File.WriteAllText(journalPath, journal.ToJsonString(Json));
        var legacyBefore = ApplicationStatusSnapshot(target); Assert.Equal(frontendShell ? "unknown" : "committed", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status); Assert.Equal(legacyBefore, ApplicationStatusSnapshot(target));
        File.WriteAllText(journalPath, original);
        Assert.Equal("committed", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
    }

    /// <summary>最后预检后的第三方文件及发布确认间隙都需人工审查，即使同字节也不认领/删除或误报成功。</summary>
    [Theory]
    [InlineData("different-race")] [InlineData("same-race")] [InlineData("publication-gap")]
    public void Application_status_preserves_v3_unconfirmed_shell_and_only_recovers_after_manual_review(string scenario)
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); const string relative = "frontend/common/project.js"; var file = Path.Combine(target, relative); File.Delete(file);
        var expected = scenario == "different-race" ? "// 最后预检后的第三方字节\n" : File.ReadAllText(Path.Combine(candidate, relative));
        var updater = scenario == "publication-gap"
            ? new LocalGenerationUpdater { AfterCreatePublication = path => { if (path == relative) throw new IOException("真实发布后归属确认前中断"); } }
            : new LocalGenerationUpdater { BeforeWrite = path => { if (path == relative) File.WriteAllText(file, expected); } };
        var plan = updater.Preview(target, candidate, ["sync-runtime"]); Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256)); Assert.Equal(expected, File.ReadAllText(file));
        var activePath = Path.Combine(target, ".local/metarill-updates/active.json"); var active = File.ReadAllBytes(activePath); var id = JsonNode.Parse(active)!["transactionId"]!.GetValue<string>();
        var before = ApplicationStatusSnapshot(target); var status = updater.InspectApplicationStatus(target, plan, plan.PlanSha256); Assert.Equal("recovery-required", status.Status); Assert.Equal(id, status.TransactionId); Assert.Equal(before, ApplicationStatusSnapshot(target));
        Assert.Throws<InvalidOperationException>(() => updater.Recover(target, id, plan.PlanSha256)); Assert.Equal(expected, File.ReadAllText(file));
        // active丢失也只能未知，不能仅以候选同hash推断该外来文件属于本次发布。
        File.Delete(activePath); var noActive = ApplicationStatusSnapshot(target); Assert.Equal("unknown", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status); Assert.Equal(noActive, ApplicationStatusSnapshot(target)); File.WriteAllBytes(activePath, active);
        var reviewed = Path.Combine(root, "manually-reviewed-project.js"); File.Move(file, reviewed); Assert.Equal("rolled-back", updater.Recover(target, id, plan.PlanSha256).Status); Assert.Equal(expected, File.ReadAllText(reviewed)); Assert.False(File.Exists(file));
        Assert.Equal("rolled-back", updater.InspectApplicationStatus(target, plan, plan.PlanSha256).Status);
    }

    /// <summary>把控制日志一并纳入字节快照，证明状态读取没有更新阶段、active或备份。</summary>
    private static string ApplicationStatusSnapshot(string directory) => string.Join("\n", Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(directory, path) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));

    /// <summary>仅在本测试GUID目录创建链接，清理时只移除链接本身。</summary>
    private static void ApplicationStatusLink(string link, string destination)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, destination); return; }
        if ((link + destination).IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n']) >= 0) throw new InvalidOperationException("测试链接路径含shell字符。");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = "/c mklink /J \"" + link + "\" \"" + destination + "\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit(); Assert.Equal(0, process.ExitCode);
    }
}
