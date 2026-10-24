namespace Metarill.Core.Contracts;

/// <summary>HTTP 旧实体兼容入口仍公开原严格 DTO 的机器可读合同，不把 JsonElement 作为任意对象能力。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class LegacyPageWriteRequestAttribute(Type requestType) : Attribute
{
    public Type RequestType { get; } = requestType;
}
