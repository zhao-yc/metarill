using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>未知协议、重复路径和越界路径在只读预检失败，不能借受管声明扩展写入面。</summary>
    [Fact]
    public void Unknown_duplicate_missing_and_unsafe_manifests_are_rejected_without_source_writes()
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); var updater = new LocalGenerationUpdater(); var before = SourceSnapshot(target);
        var marker = Path.Combine(candidate, ".metarill-package.json"); var original = File.ReadAllText(marker);
        foreach (var scenario in new[] { "version", "backendOnly", "unknown", "duplicate", "traversal", "absolute", "backslash", "device", "ads", "trailing", "caseDuplicate", "missing", "nullEntry" })
        {
            var data = JsonNode.Parse(original)!; var files = data["files"]!.AsArray();
            switch (scenario)
            {
                case "version": data["schemaVersion"] = 2; break;
                case "backendOnly": data["packageMode"] = "BackendOnly"; data["frontendIncluded"] = false; break;
                case "unknown": data["force"] = true; break;
                case "duplicate": files.Add(files[0]!.DeepClone()); break;
                case "caseDuplicate": var duplicate = files[0]!.DeepClone(); duplicate["path"] = duplicate["path"]!.GetValue<string>().ToUpperInvariant(); files.Add(duplicate); break;
                case "traversal": files[0]!["path"] = "../outside.cs"; break;
                case "absolute": files[0]!["path"] = "/outside.cs"; break;
                case "backslash": files[0]!["path"] = "frontend\\outside.js"; break;
                case "device": files[0]!["path"] = "frontend/NUL.js"; break;
                case "ads": files[0]!["path"] = "frontend/main.js:stream"; break;
                case "trailing": files[0]!["path"] = "frontend/main.js."; break;
                case "missing": files.Remove(files.Single(file => file!["path"]!.GetValue<string>() == "frontend/pages.json")); break;
                case "nullEntry": files.Add(null); break;
            }
            File.WriteAllText(marker, data.ToJsonString(Json)); Assert.ThrowsAny<Exception>(() => updater.Preview(target, candidate, All)); Assert.Equal(before, SourceSnapshot(target));
        }
        File.WriteAllText(marker, original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => updater.Preview(target, candidate, All)); File.WriteAllText(marker, original);
        foreach (var actions in new[] { Array.Empty<string>(), new[] { "sync-runtime", "sync-runtime" }, new[] { "force" } }) Assert.Throws<InvalidOperationException>(() => updater.Preview(target, candidate, actions));
        Assert.Throws<InvalidOperationException>(() => updater.Preview(target, target, All)); Assert.Throws<InvalidOperationException>(() => updater.Preview(target, Path.Combine(target, "frontend"), All)); Assert.Equal(before, SourceSnapshot(target));
    }

    /// <summary>项目身份必须来自真实包三层定义，其他项目即使命名空间相同也不能更新。</summary>
    [Fact]
    public void Cross_project_candidates_are_refused_even_when_namespace_and_file_names_match()
    {
        var next = JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(Catalog(), Json).Replace(Catalog().YzProjectId, "2200000000000999999", StringComparison.Ordinal), Json)!;
        var (target, candidate) = Packages(Catalog(), next); var before = SourceSnapshot(target);
        var error = Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Preview(target, candidate, All)); Assert.Contains("候选项目", error.Message); Assert.Equal(before, SourceSnapshot(target));
    }

    /// <summary>存在祖先junction时拒绝读取，不能沿候选或目标链接触及项目外文件。</summary>
    [Fact]
    public void Linked_target_directories_are_rejected_without_following_external_source()
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); var link = Path.Combine(target, "frontend/common"); var outside = Path.Combine(root, "external-common"); Directory.Move(link, outside);
        if (OperatingSystem.IsWindows())
        {
            if ((link + outside).IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '"', '\r', '\n']) >= 0) throw new InvalidOperationException("测试链接路径包含shell字符。");
            using var process = Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = "/c mklink /J \"" + link + "\" \"" + outside + "\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside);
        try
        {
            var before = Directory.GetFiles(outside, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Preview(target, candidate, All));
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            // 只移除本测试明确创建的链接本身，不递归删除其目标。
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0); Directory.Delete(link, false); Directory.Move(outside, link);
        }
    }

    /// <summary>新生成文件不能覆盖同路径手写内容；缺失手写壳只允许页面动作首次创建。</summary>
    [Fact]
    public void New_path_collisions_are_refused_and_missing_partial_is_created_only_by_page_scope()
    {
        var next = WithExtraPage(Catalog()); var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater();
        var relative = "frontend/pages/yzp/demo_customer-detail-2200000000000999900.vue"; var path = Path.Combine(target, relative); File.WriteAllText(path, "// 用户自有页面\n"); var before = SourceSnapshot(target);
        Assert.Throws<InvalidOperationException>(() => updater.Preview(target, candidate, All)); Assert.Equal(before, SourceSnapshot(target)); File.Delete(path);
        const string shell = "backend/generated/Services/DemoCustomerService.cs"; File.Delete(Path.Combine(target, shell));
        var limited = updater.Preview(target, candidate, ["generate-project-config"]); Assert.False(limited.CanApply); Assert.Contains("generate-all-page", limited.RequiredActions);
        var ready = updater.Preview(target, candidate, All); Assert.Contains(ready.Files, file => file.Path == shell && file.Action == "create"); updater.Apply(ready, ready.PlanSha256); Assert.Equal(File.ReadAllBytes(Path.Combine(candidate, shell)), File.ReadAllBytes(Path.Combine(target, shell)));
    }

    /// <summary>移除的页面受管文件可删除，两个前端副本与三个清单不能留下孤儿归属。</summary>
    [Fact]
    public void Removed_pages_are_deleted_from_both_mirrors_and_all_owned_manifests()
    {
        var (target, candidate) = Packages(WithExtraPage(Catalog()), Catalog()); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All);
        var removals = plan.Files.Where(file => file.Action == "delete").ToArray(); Assert.Contains(removals, file => file.Path == "frontend/pages/yzp/demo_customer-detail-2200000000000999900.vue"); updater.Apply(plan, plan.PlanSha256);
        foreach (var file in removals) Assert.False(File.Exists(Path.Combine(target, file.Path))); Assert.True(updater.Preview(target, candidate, All).CanApply);
        Assert.DoesNotContain("2200000000000999900", File.ReadAllText(Path.Combine(target, ".metarill-package.json")));
    }

    /// <summary>与直接生成共享互斥锁，持锁时不执行源码写入，也不终止占用者。</summary>
    [Fact]
    public void Existing_update_and_generation_locks_block_apply_without_touching_sources()
    {
        var next = Catalog(); next.YzProjectName = "候选"; var (target, candidate) = Packages(Catalog(), next); var updater = new LocalGenerationUpdater(); var plan = updater.Preview(target, candidate, All); var before = SourceSnapshot(target);
        foreach (var relative in new[] { ".metarill-update.lock", "backend/generated/.metarill-generation.lock" })
        {
            using (var held = new FileStream(Path.Combine(target, relative), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)) Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256));
            Assert.Equal(before, SourceSnapshot(target));
        }
    }

    /// <summary>回滚首轮后发生的第三种内容必须保留，未完成事务拒绝新任务；身份备份不符也拒绝恢复。</summary>
    [Fact]
    public void Restore_rechecks_each_write_and_requires_old_identity_before_explicit_recovery()
    {
        var next = Catalog(); next.YzProjectName = "恢复候选"; var (target, candidate) = Packages(Catalog(), next); var before = SourceSnapshot(target);
        var originals = Directory.GetFiles(target, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(target, path).Replace('\\', '/'), File.ReadAllBytes); var writes = 0; string? edited = null;
        var updater = new LocalGenerationUpdater
        {
            AfterWrite = _ => { if (++writes == 2) throw new IOException("写入阶段故障"); },
            BeforeRestore = path => { if (edited is null) { edited = path; File.WriteAllText(Path.Combine(target, path), "// 回滚首轮检查之后的外部编辑\n"); } }
        };
        var plan = updater.Preview(target, candidate, All); Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256)); Assert.NotNull(edited);
        Assert.Contains("外部编辑", File.ReadAllText(Path.Combine(target, edited!))); var active = JsonNode.Parse(File.ReadAllText(Path.Combine(target, ".local/metarill-updates/active.json")))!; var id = active["transactionId"]!.GetValue<string>();
        Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Preview(target, candidate, All)); Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Recover(target, id, new string('0', 64)));
        var journalPath = Path.Combine(target, ".local/metarill-updates/transactions", id, "journal.json"); var journal = File.ReadAllText(journalPath); var corrupted = JsonNode.Parse(journal)!; corrupted["projectId"] = "2200000000000999999"; File.WriteAllText(journalPath, corrupted.ToJsonString(Json));
        Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Recover(target, id, plan.PlanSha256)); Assert.Contains("外部编辑", File.ReadAllText(Path.Combine(target, edited!))); File.WriteAllText(journalPath, journal);
        var identityPath = Path.Combine(target, ".local/metarill-updates/transactions", id, "identity/generation.json"); var identity = File.ReadAllBytes(identityPath); File.WriteAllText(identityPath, "{}");
        Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Recover(target, id, plan.PlanSha256)); File.WriteAllBytes(identityPath, identity);
        var backupPath = Directory.GetFiles(Path.Combine(target, ".local/metarill-updates/transactions", id, "backups")).First(); var backup = File.ReadAllBytes(backupPath); File.WriteAllText(backupPath, "损坏的测试备份");
        Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Recover(target, id, plan.PlanSha256)); Assert.Contains("外部编辑", File.ReadAllText(Path.Combine(target, edited!))); File.WriteAllBytes(backupPath, backup);
        File.WriteAllBytes(Path.Combine(target, edited!), originals[edited!]); var receipt = new LocalGenerationUpdater().Recover(target, id, plan.PlanSha256); Assert.Equal("rolled-back", receipt.Status); Assert.Equal(before, SourceSnapshot(target)); Assert.False(File.Exists(Path.Combine(target, ".local/metarill-updates/active.json")));
    }

    /// <summary>真实CLI进程执行preview/apply及严格参数拒绝，退出码与输出不是测试替身。</summary>
    [Fact]
    public async Task Cli_preview_apply_and_unknown_duplicate_arguments_use_real_processes()
    {
        var next = Catalog(); next.YzProjectName = "CLI候选"; next.ConfigColumns.Single(column => column.ColumnName == "name").Name = "CLI新标签"; var (target, candidate) = Packages(Catalog(), next); var source = SourceRoot(); var binaries = Path.Combine(root, "cli");
        var built = await Cli(source, "build", "backend/src/Metarill.Cli/Metarill.Cli.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet", "--output", binaries); Assert.True(built.Code == 0, built.Output); var cli = Path.Combine(binaries, "Metarill.Cli.dll"); var planPath = Path.Combine(root, "cli-plan.json"); var before = SourceSnapshot(target);
        var preview = await Cli(source, cli, "update", "preview", "--target", target, "--candidate", candidate, "--actions", "generate-project-config", "--plan", planPath); Assert.True(preview.Code == 3, preview.Output); Assert.Equal(before, SourceSnapshot(target)); var blocked = LocalGenerationUpdater.ReadPlan(planPath); Assert.False(blocked.CanApply); Assert.Contains("generate-all-page", blocked.RequiredActions);
        var cannotApply = await Cli(source, cli, "update", "apply", "--target", target, "--candidate", candidate, "--plan", planPath, "--confirm", blocked.PlanSha256); Assert.Equal(1, cannotApply.Code); Assert.Equal(before, SourceSnapshot(target));
        planPath = Path.Combine(root, "cli-complete-plan.json"); var complete = await Cli(source, cli, "update", "preview", "--target", target, "--candidate", candidate, "--actions", string.Join(',', All), "--plan", planPath); Assert.True(complete.Code == 0, complete.Output); var plan = LocalGenerationUpdater.ReadPlan(planPath);
        var unknown = await Cli(source, cli, "update", "apply", "--target", target, "--candidate", candidate, "--plan", planPath, "--confirm", plan.PlanSha256, "--force", "true"); Assert.Equal(1, unknown.Code); Assert.Equal(before, SourceSnapshot(target));
        var duplicate = await Cli(source, cli, "update", "preview", "--target", target, "--target", target, "--candidate", candidate, "--actions", "sync-runtime", "--plan", Path.Combine(root, "duplicate.json")); Assert.Equal(1, duplicate.Code); Assert.False(File.Exists(Path.Combine(root, "duplicate.json")));
        var mismatch = await Cli(source, cli, "update", "apply", "--target", candidate, "--candidate", target, "--plan", planPath, "--confirm", plan.PlanSha256); Assert.Equal(1, mismatch.Code); Assert.Equal(before, SourceSnapshot(target));
        var applied = await Cli(source, cli, "update", "apply", "--target", target, "--candidate", candidate, "--plan", planPath, "--confirm", plan.PlanSha256); Assert.True(applied.Code == 0, applied.Output); Assert.Contains("committed", applied.Output); Assert.Equal("CLI候选", JsonNode.Parse(File.ReadAllText(Path.Combine(target, "backend/metadata/project.json")))!["yzProjectName"]!.GetValue<string>());
    }

    /// <summary>恢复不相信journal自报摘要；新增路径、动作和hash的变更必须与原确认计划逐项一致。</summary>
    [Fact]
    public void Recovery_rejects_retargeted_creates_and_every_unconfirmed_journal_write_change()
    {
        var (target, candidate) = Packages(Catalog(), WithExtraPage(Catalog())); var planner = new LocalGenerationUpdater(); var plan = planner.Preview(target, candidate, All);
        var created = plan.Files.First(file => file.Action == "create"); var injected = false;
        var updater = new LocalGenerationUpdater { AfterWrite = path => { if (path == created.Path && !injected) { injected = true; File.WriteAllText(Path.Combine(target, path), "// 模拟写入后的外部改动\n"); throw new IOException("留存真实未完成事务"); } } };
        Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256)); Assert.True(injected);
        File.WriteAllBytes(Path.Combine(target, created.Path), File.ReadAllBytes(Path.Combine(candidate, created.Path)));
        var active = JsonNode.Parse(File.ReadAllText(Path.Combine(target, ".local/metarill-updates/active.json")))!; var id = active["transactionId"]!.GetValue<string>();
        var transaction = Path.Combine(target, ".local/metarill-updates/transactions", id); var journalPath = Path.Combine(transaction, "journal.json"); var original = File.ReadAllText(journalPath);
        const string userFile = "frontend/common/yzp_runtime/user-created-later.js"; var userPath = Path.Combine(target, userFile); var userBytes = Encoding.UTF8.GetBytes("// 此文件由开发者在失败后创建，禁止恢复器删除。\n"); File.WriteAllBytes(userPath, userBytes); var snapshot = SourceSnapshot(target);
        foreach (var scenario in new[] { "retarget", "after", "before", "action", "scope", "omit", "append", "reorder", "actions", "identity", "legacy" })
        {
            var journal = JsonNode.Parse(original)!; var files = journal["files"]!.AsArray(); var entry = files.Single(file => file!["path"]!.GetValue<string>() == created.Path)!;
            switch (scenario)
            {
                case "retarget": entry["path"] = userFile; entry["afterSha256"] = Convert.ToHexStringLower(SHA256.HashData(userBytes)); break;
                case "after": entry["afterSha256"] = new string('0', 64); break;
                case "before": entry["beforeSha256"] = new string('0', 64); break;
                case "action": entry["action"] = "replace"; break;
                case "scope": entry["scope"] = "sync-runtime"; break;
                case "omit": files.Remove(entry); break;
                case "append": var extra = entry.DeepClone(); extra["path"] = userFile; extra["afterSha256"] = Convert.ToHexStringLower(SHA256.HashData(userBytes)); files.Add(extra); break;
                case "reorder": var first = files[0]!.DeepClone(); files.RemoveAt(0); files.Add(first); break;
                case "actions": journal["actions"] = new JsonArray("sync-runtime"); break;
                case "identity": journal["rootNamespace"] = "Example.Other"; break;
                case "legacy": journal["schemaVersion"] = 1; break;
            }
            File.WriteAllText(journalPath, journal.ToJsonString(Json)); Assert.Throws<InvalidOperationException>(() => planner.Recover(target, id, plan.PlanSha256)); Assert.Equal(snapshot, SourceSnapshot(target)); Assert.Equal(userBytes, File.ReadAllBytes(userPath));
        }
        File.WriteAllText(journalPath, original); var planPath = Path.Combine(transaction, "plan.json"); var originalPlan = File.ReadAllText(planPath); var changedPlan = JsonNode.Parse(originalPlan)!; changedPlan["candidateDirectory"] = Path.Combine(root, "unconfirmed-candidate"); File.WriteAllText(planPath, changedPlan.ToJsonString(Json));
        Assert.Throws<InvalidOperationException>(() => planner.Recover(target, id, plan.PlanSha256)); Assert.Equal(snapshot, SourceSnapshot(target)); File.Delete(planPath);
        Assert.Throws<InvalidOperationException>(() => planner.Recover(target, id, plan.PlanSha256)); Assert.Equal(snapshot, SourceSnapshot(target)); File.WriteAllText(planPath, originalPlan);
        // 恢复只依赖目标内的完整确认计划与备份，候选退役后也应能够回滚。
        Assert.StartsWith(root + Path.DirectorySeparatorChar, Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase); Directory.Move(candidate, Path.Combine(root, "retired-candidate"));
        var receipt = planner.Recover(target, id, plan.PlanSha256); Assert.Equal("rolled-back", receipt.Status); Assert.False(File.Exists(Path.Combine(target, created.Path))); Assert.Equal(userBytes, File.ReadAllBytes(userPath)); Assert.False(File.Exists(Path.Combine(target, ".local/metarill-updates/active.json")));
    }

    /// <summary>同时删入口文件和两层归属仍不能成为合法FullApplication候选，不能删除目标启动入口。</summary>
    [Fact]
    public void Full_application_bootstrap_files_cannot_be_removed_by_pruning_manifests()
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); var marker = Path.Combine(candidate, ".metarill-package.json"); var frontendMarker = Path.Combine(candidate, "frontend/.metarill-frontend.json"); var original = File.ReadAllText(marker); var originalFrontend = File.ReadAllText(frontendMarker); var snapshot = SourceSnapshot(target);
        foreach (var path in new[] { "frontend/main.js", "frontend/App.vue", "frontend/index.html", "backend/Application.Host/Program.cs", "backend/Application.Host/Application.Host.csproj", "src/Metarill.Core/Metarill.Core.csproj" })
        {
            var bytes = File.ReadAllBytes(Path.Combine(candidate, path)); var package = JsonNode.Parse(original)!; var files = package["files"]!.AsArray(); files.Remove(files.Single(file => file!["path"]!.GetValue<string>() == path)); File.Delete(Path.Combine(candidate, path));
            if (path.StartsWith("frontend/", StringComparison.Ordinal))
            {
                var frontend = JsonNode.Parse(originalFrontend)!; var registered = frontend["files"]!.AsArray(); registered.Remove(registered.Single(file => file!["path"]!.GetValue<string>() == path["frontend/".Length..])); File.WriteAllText(frontendMarker, frontend.ToJsonString(Json));
                files.Single(file => file!["path"]!.GetValue<string>() == "frontend/.metarill-frontend.json")!["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(frontendMarker)));
            }
            File.WriteAllText(marker, package.ToJsonString(Json)); var error = Assert.Throws<InvalidOperationException>(() => new LocalGenerationUpdater().Preview(target, candidate, ["sync-runtime"])); Assert.Contains("启动归属", error.Message); Assert.Equal(snapshot, SourceSnapshot(target)); File.WriteAllBytes(Path.Combine(candidate, path), bytes); File.WriteAllText(marker, original); File.WriteAllText(frontendMarker, originalFrontend);
        }
    }

    /// <summary>仅首次补建缺失partial且清单未变时，恢复仍能通过原指纹绑定旧身份并回到缺失状态。</summary>
    [Fact]
    public void Recovery_of_only_missing_handwritten_shell_does_not_require_a_marker_rewrite()
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); var planner = new LocalGenerationUpdater(); var stable = planner.Preview(target, candidate, All); planner.Apply(stable, stable.PlanSha256);
        const string shell = "backend/generated/Services/DemoCustomerService.cs"; var shellPath = Path.Combine(target, shell); File.Delete(shellPath); var before = SourceSnapshot(target); var plan = planner.Preview(target, candidate, ["generate-all-page"]);
        Assert.Single(plan.Files, file => file.Action != "preserve"); Assert.DoesNotContain(plan.Files, file => file.Path == ".metarill-package.json");
        var updater = new LocalGenerationUpdater { AfterWrite = path => { File.WriteAllText(Path.Combine(target, path), "// 创建之后的第三种内容\n"); throw new IOException("测试partial创建中断"); } };
        Assert.Throws<InvalidOperationException>(() => updater.Apply(plan, plan.PlanSha256)); File.WriteAllBytes(shellPath, File.ReadAllBytes(Path.Combine(candidate, shell)));
        var active = JsonNode.Parse(File.ReadAllText(Path.Combine(target, ".local/metarill-updates/active.json")))!; var receipt = planner.Recover(target, active["transactionId"]!.GetValue<string>(), plan.PlanSha256); Assert.Equal("rolled-back", receipt.Status); Assert.Equal(before, SourceSnapshot(target));
    }

    /// <summary>额外详情页复用同表字段，不引入数据库或手写类型依赖，用于实际新增/删除路径验收。</summary>
    private static MetadataCatalog WithExtraPage(MetadataCatalog catalog)
    {
        var existing = catalog.ConfigPages.Single(page => page.Id == "2200000000000100020"); var page = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(existing, Json), Json)!; page.Id = "2200000000000999900"; page.Name = "额外详情"; catalog.ConfigPages.Add(page);
        var index = 0; foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == existing.Id).ToArray()) { var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field, Json), Json)!; copy.Id = (2200000000000999910L + index++).ToString(System.Globalization.CultureInfo.InvariantCulture); copy.ConfigPageId = page.Id; catalog.ConfigPageColumns.Add(copy); }
        return catalog;
    }
    /// <summary>全部参数通过ArgumentList传递，子进程只执行构建CLI或显式源码更新命令。</summary>
    private static async Task<(int Code, string Output)> Cli(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = directory, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 }; foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // 只有 CLI build 会改写同根依赖的 obj；其余命令保持原并行行为。
        using var buildLease = await SharedSourceBuildGate.Shared.EnterAsync(arguments.FirstOrDefault());
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; } return (process.ExitCode, await stdout.WaitAsync(timeout.Token) + await stderr.WaitAsync(timeout.Token));
    }
}
