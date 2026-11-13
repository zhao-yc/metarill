namespace Metarill.Core.Contracts;

/// <summary>业务代码主动声明可展示的规则提示；数据库、IO或未知异常不得自动转成此类型。</summary>
public sealed class BusinessRuleException : InvalidOperationException
{
    /// <summary>提示来自受维护的业务规则，宿主沿原HTTP200/R500回传；不接收内部异常链。</summary>
    public BusinessRuleException(string message):base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}
