namespace Metarill.Core.Contracts;

/// <summary>生成器发布页与真实实体读取入口的固定绑定；不接受请求指定类型或方法。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GeneratedPageAccessAttribute(string pageId, string tableId, Type entityType, Type serviceType, string readMethod) : Attribute
{
    public string PageId { get; } = pageId;
    public string TableId { get; } = tableId;
    public Type EntityType { get; } = entityType;
    public Type ServiceType { get; } = serviceType;
    public string ReadMethod { get; } = readMethod;
}
