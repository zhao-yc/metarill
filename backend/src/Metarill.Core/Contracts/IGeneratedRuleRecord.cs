namespace Metarill.Core.Contracts;

/// <summary>实际生成记录的派生规则装载证明；客户端正文无法写入，缺字段与已装载null明确区分。</summary>
public interface IGeneratedRuleRecord
{
    /// <summary>此字段的规则值必须由固定逻辑读取证明；缺失时禁止回退到物理实体属性。</summary>
    bool RequiresLoadedRuleValue(string physicalColumn);
    /// <summary>仅返回固定数据库读取实际装载的派生值，不使用未装载属性的默认值。</summary>
    bool TryGetLoadedRuleValue(string physicalColumn,out object? value);
}
