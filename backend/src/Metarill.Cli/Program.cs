using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Generation;

/// <summary>离线生成和独立打包只读元数据及基础源码，不执行 SQL。</summary>
internal static class Program
{
    /// <summary>参数采用明确的键值对，拒绝未知参数，避免误写输出位置。</summary>
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h") { PrintUsage(); return 0; }
            if (args.Length > 0 && args[0] == "update") return Update(args);
            if (args.Length == 0 || args[0] is not "generate" and not "package") { PrintUsage(); return 2; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var backendOnly = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--backend-only")
                {
                    if (args[0] != "package" || backendOnly) throw new ArgumentException("--backend-only仅用于package且不能重复。");
                    backendOnly = true; continue;
                }
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("命令参数缺少值。");
                if (!options.TryAdd(args[i], args[++i])) throw new ArgumentException("命令参数重复。");
            }
            if (options.Keys.Any(x => x is not "--metadata" and not "--output" and not "--namespace" && (args[0] != "package" || x != "--source-root"))) throw new ArgumentException("命令参数无效。");
            if (!options.ContainsKey("--metadata") || !options.ContainsKey("--output") || !options.ContainsKey("--namespace") || args[0] == "package" && !options.ContainsKey("--source-root")) { PrintUsage(); return 2; }
            var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(options["--metadata"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("元数据不能为空。");
            if (args[0] == "package")
            {
                var package = new ApplicationPackager().Create(catalog, options["--output"], options["--namespace"], options["--source-root"], backendOnly ? ApplicationPackageMode.BackendOnly : ApplicationPackageMode.FullApplication);
                Console.WriteLine($"独立包完成：{package.Files.Count} 个文件，模式 {package.Mode}，前端宿主 {package.FrontendIncluded}，已清除 {package.RedactedPaths.Count} 项敏感配置；元数据摘要 {package.MetadataSha256}。");
                if (package.Warnings.Count != 0) foreach (var warning in package.Warnings) Console.WriteLine("打包提示：" + warning);
            }
            else
            {
                var result = new CSharpGenerator().Generate(catalog, options["--output"], options["--namespace"]);
                Console.WriteLine($"生成完成：{result.GeneratedFiles.Count} 个归属文件，{result.CreatedHandwrittenFiles.Count} 个首次创建的手写文件；元数据摘要 {result.MetadataSha256}。");
                if (result.Warnings.Count > 0) Console.WriteLine($"另有 {result.Warnings.Count} 项控件或关系后端适配尚未完成；原UI分支保留不代表功能可用，详见 generation-capabilities.json。");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"生成失败：{exception.Message}"); return 1;
        }
    }
    /// <summary>更新仅接受本机明确目录和预览摘要，不提供force、SQL、脚本或安装选项。</summary>
    private static int Update(string[] args)
    {
        if (args.Length < 2 || args[1] is not ("target-identity" or "prepare" or "preview" or "apply" or "apply-status" or "recover")) throw new ArgumentException("更新子命令必须是target-identity、prepare、preview、apply、apply-status或recover。");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 2; index < args.Length; index += 2)
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("更新参数缺值或重复。");
        var allowed = args[1] switch { "target-identity" => new[] { "--target" }, "prepare" => new[] { "--request", "--target", "--candidate", "--source-root", "--plan" }, "preview" => new[] { "--target", "--candidate", "--actions", "--plan" }, "apply" => new[] { "--target", "--candidate", "--plan", "--confirm" }, "apply-status" => new[] { "--target", "--plan", "--confirm" }, _ => new[] { "--target", "--transaction", "--confirm" } };
        if (values.Count != allowed.Length || values.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("更新参数缺失或包含未知键。");
        var updater = new LocalGenerationUpdater();
        if (args[1] == "target-identity")
        {
            Console.WriteLine(JsonSerializer.Serialize(updater.InspectTarget(values["--target"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))); return 0;
        }
        if (args[1] == "prepare")
        {
            var result = updater.PrepareRequest(values["--request"], values["--target"], values["--candidate"], values["--source-root"], values["--plan"]);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })); return result.Plan.CanApply ? 0 : 3;
        }
        if (args[1] == "preview")
        {
            var plan = updater.Preview(values["--target"], values["--candidate"], values["--actions"].Split(',')); LocalGenerationUpdater.WritePlan(plan, values["--plan"]);
            Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })); return plan.CanApply ? 0 : 3;
        }
        if (args[1] == "apply-status")
        {
            var plan = LocalGenerationUpdater.ReadApplicationStatusPlan(values["--plan"]);
            Console.WriteLine(JsonSerializer.Serialize(updater.InspectApplicationStatus(values["--target"], plan, values["--confirm"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))); return 0;
        }
        if (args[1] == "apply")
        {
            var plan = LocalGenerationUpdater.ReadPlan(values["--plan"]);
            if (Path.GetFullPath(values["--target"]).TrimEnd(Path.DirectorySeparatorChar) != plan.TargetDirectory || Path.GetFullPath(values["--candidate"]).TrimEnd(Path.DirectorySeparatorChar) != plan.CandidateDirectory) throw new ArgumentException("命令目录与已确认计划不匹配。");
            Console.WriteLine(JsonSerializer.Serialize(updater.Apply(plan, values["--confirm"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))); return 0;
        }
        Console.WriteLine(JsonSerializer.Serialize(updater.Recover(values["--target"], values["--transaction"], values["--confirm"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))); return 0;
    }
    /// <summary>显示稳定的脚本调用协议。</summary>
    private static void PrintUsage() => Console.WriteLine("用法：metarill generate --metadata 元数据.json --output 输出目录 --namespace 根命名空间\n或：metarill package --metadata 元数据.json --output 空目录 --namespace 根命名空间 --source-root Metarill源码根目录 [--backend-only]\n或：metarill update target-identity --target 已有FullApplication（只读绑定身份）\n或：metarill update prepare --request 已审核请求.json --target 已有FullApplication --candidate 新空候选目录 --source-root 可信Metarill源码 --plan 新计划.json\n或：metarill update preview --target 已有FullApplication --candidate 新包目录 --actions generate-project-config,sync-runtime,generate-all-page --plan 新计划.json\n应用：metarill update apply --target 原目录 --candidate 原候选 --plan 计划.json --confirm 计划摘要\n状态：metarill update apply-status --target 原目录 --plan 原计划.json --confirm 计划摘要（只读；unknown不代表未写入）\n恢复：metarill update recover --target 原目录 --transaction 事务ID --confirm 计划摘要");
}
