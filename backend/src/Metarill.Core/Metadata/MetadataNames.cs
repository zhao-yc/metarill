namespace Metarill.Core.Metadata;

/// <summary>生成契约及持久表单绑定共用数据库名称到 JSON 名称的转换。</summary>
public static class MetadataNames
{
    /// <summary>使用原生成规则转换类型成员名。</summary>
    public static string Pascal(string value)=>string.Concat(value.Split('_',StringSplitOptions.RemoveEmptyEntries)
        .Select(part=>char.ToUpperInvariant(part[0])+part[1..]));

    /// <summary>返回原表单使用的驼峰名。</summary>
    public static string Camel(string value)
    {
        var name=Pascal(value);
        return char.ToLowerInvariant(name[0])+name[1..];
    }
}
