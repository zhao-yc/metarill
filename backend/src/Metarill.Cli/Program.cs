using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Generation;

/// <summary>离线生成入口只读元数据及写入归属目录，不执行 SQL。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h") { PrintUsage(); return 0; }
            if (args.Length != 7 || args[0] != "generate") { PrintUsage(); return 2; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i += 2)
                if (!options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("命令参数重复。");
            if (options.Keys.Any(x => x is not "--metadata" and not "--output" and not "--namespace")) throw new ArgumentException("命令参数无效。");
            var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(options["--metadata"]), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("元数据不能为空。");
            var result = new CSharpGenerator().Generate(catalog, options["--output"], options["--namespace"]);
            Console.WriteLine($"生成完成：{result.GeneratedFiles.Count} 个归属文件，{result.CreatedHandwrittenFiles.Count} 个首次创建的手写文件；元数据摘要 {result.MetadataSha256}。");
            if (result.Warnings.Count > 0) Console.WriteLine($"另有 {result.Warnings.Count} 项控件或关系尚未完成移植，当前按只读保护；详见 generation-capabilities.json。");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"生成失败：{exception.Message}");
            return 1;
        }
    }
    /// <summary>显示稳定的脚本调用协议。</summary>
    private static void PrintUsage() => Console.WriteLine("用法：metarill generate --metadata 元数据.json --output 输出目录 --namespace 根命名空间");
}
