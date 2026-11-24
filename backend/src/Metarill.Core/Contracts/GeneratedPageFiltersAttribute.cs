namespace Metarill.Core.Contracts;

/// <summary>生成查询从实际页面规则发布的筛选字段描述。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedPageFiltersAttribute(string schemaJson) : Attribute
{
    public string SchemaJson { get; } = schemaJson;
}
