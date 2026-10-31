namespace Metarill.Runtime.Agent;

/// <summary>显式业务版本冲突可展示稳定提示；进入业务后的结果仍须查任务，不能据异常宣称未写入。</summary>
public sealed class AgentActionConflictException : Exception
{
    /// <summary>只接受手写业务明确声明的公开提示，不自动包装未知内部异常。</summary>
    public AgentActionConflictException(string message) : base(message) => ArgumentException.ThrowIfNullOrWhiteSpace(message);
}
