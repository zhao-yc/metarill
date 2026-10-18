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
            if (args.Length == 0 || args[0] is not "generate" and not "package" || args.Length % 2 != 1) { PrintUsage(); return 2; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i += 2)
                if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("命令参数重复。");
            if (options.Keys.Any(x => x is not "--metadata" and not "--output" and not "--namespace" && (args[0] != "package" || x != "--source-root"))) throw new ArgumentException("命令参数无效。");
            if (!options.ContainsKey("--metadata") || !options.ContainsKey("--output") || !options.ContainsKey("--namespace") || args[0] == "package" && !options.ContainsKey("--source-root")) { PrintUsage(); return 2; }
            var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(options["--metadata"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("元数据不能为空。");
            if (args[0] == "package")
            {
                var package = new ApplicationPackager().Create(catalog, options["--output"], options["--namespace"], options["--source-root"]);
                Console.WriteLine($"独立包完成：{package.Files.Count} 个文件，已清除 {package.RedactedPaths.Count} 项敏感配置；元数据摘要 {package.MetadataSha256}。");
                if (package.Warnings.Count != 0) Console.WriteLine($"另有 {package.Warnings.Count} 项控件或关系尚未完成移植，详见 generation-capabilities.json。");
            }
            else
            {
                var result = new CSharpGenerator().Generate(catalog, options["--output"], options["--namespace"]);
                Console.WriteLine($"生成完成：{result.GeneratedFiles.Count} 个归属文件，{result.CreatedHandwrittenFiles.Count} 个首次创建的手写文件；元数据摘要 {result.MetadataSha256}。");
                if (result.Warnings.Count > 0) Console.WriteLine($"另有 {result.Warnings.Count} 项控件或关系尚未完成移植，当前按只读保护；详见 generation-capabilities.json。");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"生成失败：{exception.Message}"); return 1;
        }
    }
    /// <summary>显示稳定的脚本调用协议。</summary>
    private static void PrintUsage() => Console.WriteLine("用法：metarill generate --metadata 元数据.json --output 输出目录 --namespace 根命名空间\n或：metarill package --metadata 元数据.json --output 空目录 --namespace 根命名空间 --source-root Metarill源码根目录");
}
