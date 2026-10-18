using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Runtime.Metadata;

/// <summary>发布包内不可变的物理模型；项目可编辑元数据由数据库仓储读取。</summary>
public sealed class MetadataRegistry
{
    public MetadataCatalog Platform { get; }
    public MetadataCatalog Demo { get; }
    public string PlatformPath { get; }
    public MetadataRegistry(string platformPath, string demoPath)
    {
        PlatformPath = Path.GetFullPath(platformPath);
        Platform = Load(platformPath); Demo = Load(demoPath);
    }
    public static MetadataCatalog Load(string path)
    {
        var metadata = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(path), JsonDefaults.Options) ?? throw new InvalidOperationException("元数据文件为空。");
        MetadataValidation.Validate(metadata);
        return metadata;
    }
    public PageMetadata? FindPage(string id) => Platform.ConfigPages.Concat(Demo.ConfigPages).FirstOrDefault(p => p.Id == id);
    public static string Camel(string snake) => System.Text.RegularExpressions.Regex.Replace(snake, "_([a-z])", m => m.Groups[1].Value.ToUpperInvariant());
    public static string Snake(string camel) => System.Text.RegularExpressions.Regex.Replace(camel, "[A-Z]", m => "_" + m.Value.ToLowerInvariant());
}
