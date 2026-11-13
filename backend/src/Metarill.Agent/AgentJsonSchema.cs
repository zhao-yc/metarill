using Metarill.Core.Contracts;

namespace Metarill.Runtime.Agent;

/// <summary>保留既有Agent调用边界，结构描述与MCP共用实际DTO规则。</summary>
internal static class AgentJsonSchema
{
    /// <summary>兼容既有递归深度与可选字段描述。</summary>
    public static Dictionary<string, object?> Create(Type type,int depth=0)=>RequestJsonSchema.Create(type,depth);
}
