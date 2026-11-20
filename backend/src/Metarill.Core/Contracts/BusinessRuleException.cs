namespace Metarill.Core.Contracts;

/// <summary>业务代码主动声明可展示的规则提示；数据库、IO或未知异常不得自动转成此类型。</summary>
public sealed class BusinessRuleException : InvalidOperationException
{
    /// <summary>生成必填校验登记已发布字段标题，导入可据此定位数据行。</summary>
    public string? RequiredFieldName { get; private init; }

    /// <summary>普通保存显示字段提示，导入将此错误转换为相同字段的输入错误。</summary>
    public static BusinessRuleException RequiredField(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return new BusinessRuleException(fieldName+"不能为空。"){RequiredFieldName=fieldName};
    }

    /// <summary>提示来自受维护的业务规则，宿主沿原HTTP200/R500回传；不接收内部异常链。</summary>
    public BusinessRuleException(string message):base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}
