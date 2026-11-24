namespace Metarill.Core.Contracts;

/// <summary>生成页面自身的分页和多选能力，服务端已发布的开关不能由请求替换。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GeneratedPagePaginationAttribute(bool allowsUnpaged, bool selectionPage) : Attribute
{
    public bool AllowsUnpaged { get; } = allowsUnpaged;
    public bool SelectionPage { get; } = selectionPage;
}
