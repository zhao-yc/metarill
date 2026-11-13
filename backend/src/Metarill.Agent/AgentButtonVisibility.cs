using Metarill.Core.Contracts;
namespace Metarill.Runtime.Agent;

/// <summary>Web中央入口与Agent共同消费发布规则，只读取范围证明后的实际生成记录。</summary>
internal static class AgentButtonVisibility
{
    /// <summary>客户端实体和规则不参与；普通按钮不额外执行页面只读条件。</summary>
    internal static void Require(AgentPublishedButton button,object record,Type entityType)
    {
        if(button.Rule is not{} rule)return;
        var values=AgentRecordValues.Read(record,entityType,button.RecordNames,rule.Fields.Concat(button.ReadOnlyRule?.Fields??[]));
        if(button.ReadOnlyRule is{} readOnly&&readOnly.IsReadOnly(values))throw new BusinessRuleException("当前记录为只读，不能执行可写按钮");
        if(!rule.Matches(values))throw new BusinessRuleException("当前记录不允许执行该按钮");
    }
}
