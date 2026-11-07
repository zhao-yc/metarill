using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.GlobalSample.Generated.Entities;

namespace Metarill.GlobalSample.Generated.Services;

/// <summary>原样板的手写对象规则，与生成Service属于同一程序集partial。</summary>
public partial class SampleContractService
{
    /// <summary>负金额在标准生成写入内拒绝，不能只在Controller校验。</summary>
    partial void BeforeSave(SampleContract entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    {
        if (entity.Amount is { } amount && amount < 0) throw new BusinessRuleException("合同金额不能小于零");
    }
}
