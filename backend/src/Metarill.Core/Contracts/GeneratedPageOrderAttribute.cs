namespace Metarill.Core.Contracts;

/// <summary>生成的列表与导出公开同一份显式排序字段，沿用各自原请求和默认排序。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedPageOrderAttribute(params string[] columns) : Attribute
{
    public IReadOnlyList<string> Columns { get; } = Array.AsReadOnly(columns);
}
