using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Platform.Generated.Entities;

namespace Metarill.Platform.Generated.Services;

/// <summary>平台字段自己的保存规则，重新生成保留此手写扩展。</summary>
public partial class ConfigColumnService
{
    /// <summary>在映射保存前同步原长度和精度，让回执与实际持久化定义一致。</summary>
    partial void BeforeSave(ConfigColumn entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    {
        var shape = DecimalColumnShape.FromSubmission(entity.ColumnType, entity.ColumnLength, entity.Precision, entity.Scale,
            creating, submittedFields.Contains("column_length"), submittedFields.Contains("precision"), submittedFields.Contains("scale"));
        if (shape is not { } decimalShape) return;

        if (submittedFields.Contains("logical_type") && !MetadataColumnTypePolicy.IsSupportedLogicalType(entity.LogicalType))
            throw new ArgumentException("字段逻辑类型无效。");
        entity.LogicalType = MetadataColumnTypePolicy.Resolve(entity.ColumnName ?? "", "decimal", entity.ColumnType, creating, true, false);
        entity.ColumnLength = decimalShape.ColumnLength;
        entity.Precision = decimalShape.Precision;
        entity.Scale = decimalShape.Scale;
        submittedFields.UnionWith(["logical_type", "column_length", "precision", "scale"]);
    }
}
