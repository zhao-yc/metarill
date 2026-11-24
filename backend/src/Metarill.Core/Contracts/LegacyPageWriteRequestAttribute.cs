namespace Metarill.Core.Contracts;

/// <summary>HTTP 旧实体兼容入口仍公开原严格 DTO 的机器可读合同，不把 JsonElement 作为任意对象能力。</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class LegacyPageWriteRequestAttribute(Type requestType) : Attribute
{
    public Type RequestType { get; } = requestType;

    /// <summary>新增时由已认证账号或必填查询范围提供的 DTO 字段。</summary>
    public string[] ServerSuppliedFields { get; set; } = [];
}
