using Metarill.Core.Security;
using Metarill.Platform.Generated.Entities;

namespace Metarill.Platform.Generated.Services;

/// <summary>配置表的保存规则沿用原表配置详情和 Java 服务合同。</summary>
public partial class ConfigTableService
{
    /// <summary>首次保存的空值采用原冗余默认值，后续明确清空仍需拒绝。</summary>
    partial void BeforeSave(ConfigTable entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    {
        if (creating && (entity.IsRongyu is null || !submittedFields.Contains("is_rongyu")))
            entity.IsRongyu = 1;
        if (entity.IsRongyu is not (0 or 1))
            throw new ArgumentException("冗余模式仅支持 0 或 1");
        if (entity.IsRongyu == 0 && (entity.IsView == 1 || entity.IsFunctionTable == 1))
            throw new ArgumentException("V8 非冗余模式仅支持实体表，视图和函数表不能选择此模式");
    }
}
