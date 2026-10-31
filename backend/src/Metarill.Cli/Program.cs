using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Generation;

namespace Metarill.Cli;

/// <summary>离线生成、独立打包及在线只读反查的命令入口；数据库操作始终由平台显式接口控制。</summary>
internal static class Program
{
    /// <summary>参数采用明确的键值对，拒绝未知参数，避免误写输出位置。</summary>
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // 重定向输出与中文错误统一使用UTF8，避免Windows代码页破坏脚本和审计日志。
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            if (args.Length == 1 && args[0] is "--help" or "-h") { PrintUsage(); return 0; }
            if (args.Length > 0 && args[0] == "schema") return await Metarill.Cli.PlatformSchemaCommand.RunAsync(args);
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
            if (options.Keys.Any(x => x is not "--metadata" and not "--output" and not "--namespace" && (args[0] != "package" || x is not "--source-root" and not "--management-admin-type"))) throw new ArgumentException("命令参数无效。");
            if (!options.ContainsKey("--metadata") || !options.ContainsKey("--output") || !options.ContainsKey("--namespace") || args[0] == "package" && !options.ContainsKey("--source-root")) { PrintUsage(); return 2; }
            if(backendOnly&&options.ContainsKey("--management-admin-type"))throw new ArgumentException("完整管理模式不能与--backend-only同时使用。");
            var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(options["--metadata"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("元数据不能为空。");
            if (args[0] == "package")
            {
                var package = options.TryGetValue("--management-admin-type",out var managementAdminType)
                    ? new ApplicationPackager().CreateManagement(catalog,options["--output"],options["--namespace"],options["--source-root"],managementAdminType)
                    : new ApplicationPackager().Create(catalog, options["--output"], options["--namespace"], options["--source-root"], backendOnly ? ApplicationPackageMode.BackendOnly : ApplicationPackageMode.FullApplication);
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
        if (args.Length < 2 || args[1] is not ("capabilities" or "target-identity" or "prepare" or "preview" or "apply" or "apply-status" or "recover")) throw new ArgumentException("更新子命令必须是capabilities、target-identity、prepare、preview、apply、apply-status或recover。");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 2; index < args.Length; index += 2)
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("更新参数缺值或重复。");
        var allowed = args[1] switch { "capabilities" => Array.Empty<string>(), "target-identity" => new[] { "--target" }, "prepare" => new[] { "--request", "--target", "--candidate", "--source-root", "--plan" }, "preview" => new[] { "--target", "--candidate", "--actions", "--plan" }, "apply" => new[] { "--target", "--candidate", "--plan", "--confirm" }, "apply-status" => new[] { "--target", "--plan", "--confirm" }, _ => new[] { "--target", "--transaction", "--confirm" } };
        if (values.Count != allowed.Length || values.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("更新参数缺失或包含未知键。");
        if(args[1]=="capabilities"){Console.WriteLine(JsonSerializer.Serialize(LocalGenerationUpdater.ProtocolCapabilities(),new JsonSerializerOptions(JsonSerializerDefaults.Web)));return 0;}
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
    private static void PrintUsage() => Console.WriteLine("在线反查：metarill schema inspect --server 服务地址 --project-id 项目ID [--table-id 受管表ID] [--token-env 环境变量名]\n逻辑草案：metarill schema import-preview --server 服务地址 --project-id 项目ID --tables 表名1,表名2（只读，不保存）\n配置计划：metarill schema import-prepare --server 服务地址 --project-id 项目ID --tables 表名1,表名2（不保存）\n确认配置：metarill schema import-apply --server 同一服务 --project-id 同一项目 --plan-id 已审核计划ID --confirm 确认摘要（只保存配置，不执行DDL）\n查询回执：metarill schema import-status --server 同一服务 --project-id 同一项目 --plan-id 原计划ID --confirm 原摘要（unknown不代表未写入）\n物理目录：metarill schema catalog --server 服务地址 --project-id 项目ID [--token-env 环境变量名]（默认从METARILL_LOGIN_STATE_ID读取网页登录会话）\n用法：metarill generate --metadata 元数据.json --output 输出目录 --namespace 根命名空间\n或：metarill package --metadata 元数据.json --output 空目录 --namespace 根命名空间 --source-root Metarill源码根目录 [--backend-only | --management-admin-type 包内后台ID]（管理模式明确绑定包内后台）\n或：metarill update capabilities（只读协议能力，不访问项目目录）\n或：metarill update target-identity --target 已有FullApplication或FullManagementApplication（只读绑定身份）\n或：metarill update prepare --request 已审核请求.json --target 已有完整应用 --candidate 新空候选目录 --source-root 可信Metarill源码 --plan 新计划.json\n或：metarill update preview --target 已有完整应用 --candidate 新包目录 --actions generate-project-config,sync-runtime,generate-all-page --plan 新计划.json\n应用：metarill update apply --target 原目录 --candidate 原候选 --plan 计划.json --confirm 计划摘要\n状态：metarill update apply-status --target 原目录 --plan 原计划.json --confirm 计划摘要（只读；unknown不代表未写入）\n恢复：metarill update recover --target 原目录 --transaction 事务ID --confirm 计划摘要");
}
