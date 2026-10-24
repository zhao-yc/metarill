namespace Metarill.Core.Metadata;

/// <summary>配置字段的逻辑类型存储规则；合法元数据不等于当前生成器已经支持该类型。</summary>
public static class MetadataColumnTypePolicy
{
    /// <summary>保持既有元数据类型，并允许生成器原已接受的double；不在此决定代码生成能力。</summary>
    public static bool IsSupportedLogicalType(string? value) => value is "int64" or "int32" or "string" or "decimal" or "double" or "date" or "time" or "datetime" or "bool" or "json" or "binary";

    /// <summary>只为未编辑类型的旧空物理值提供局部更新兼容；null、空白或钩子改值均不符合。</summary>
    public static bool CanPreserveLegacyEmptyPhysical(string? originalPhysical,string? originalLogical,bool physicalSubmitted,bool logicalSubmitted,string? currentPhysical,string? currentLogical)
        => originalPhysical==string.Empty && IsSupportedLogicalType(originalLogical) && !physicalSubmitted && !logicalSubmitted
            && string.Equals(currentPhysical,originalPhysical,StringComparison.Ordinal) && string.Equals(currentLogical,originalLogical,StringComparison.Ordinal);

    /// <summary>无关编辑保持有效逻辑类型；显式逻辑值严格校验，旧物理类型推导保留原命名规则。</summary>
    public static string Resolve(string columnName,string? logicalType,string? physicalType,bool creating,bool logicalSubmitted,bool physicalSubmitted)
    {
        // 即使同时提交逻辑类型，明确提交的未知物理值也不能借逻辑值绕过检查。
        var mapped=physicalSubmitted ? FromPhysical(physicalType) : null;
        var inferred=!logicalSubmitted && (physicalSubmitted || creating && string.IsNullOrWhiteSpace(logicalType));
        string result;
        if(logicalSubmitted) result=RequireLogical(logicalType);
        else if(physicalSubmitted) result=mapped!;
        else if(inferred) result=FromPhysical(physicalType);
        else result=RequireLogical(logicalType);
        // 原表单只用int表示整数；仅已知物理类型推导时保持原ID/排序归一，不覆盖显式逻辑值或已有错误值。
        if(inferred)
        {
            if(columnName=="id" || columnName.EndsWith("_id",StringComparison.Ordinal)) result="int64";
            if(columnName=="sort_no") result="int32";
        }
        if((columnName=="id" || columnName.EndsWith("_id",StringComparison.Ordinal)) && result!="int64")
            throw new ArgumentException("标识字段的逻辑类型必须为int64。");
        if(columnName=="sort_no" && result!="int32") throw new ArgumentException("排序字段的逻辑类型必须为int32。");
        return result;
    }

    /// <summary>显式逻辑类型使用固定元数据词表，不归一化未知、空值或大小写错误。</summary>
    private static string RequireLogical(string? value) => IsSupportedLogicalType(value) ? value! : throw new ArgumentException("字段逻辑类型无效。");

    /// <summary>只列举已有正确映射；未知、point、varbinary和空值保持明确拒绝，不能默认伪装为string。</summary>
    private static string FromPhysical(string? value) => value?.ToLowerInvariant() switch
    {
        "bigint" or "int64" => "int64",
        "int" or "integer" or "tinyint" or "smallint" or "int32" => "int32",
        "decimal" or "numeric" or "double" or "float" => "decimal",
        "nvarchar" or "varchar" or "char" or "nchar" or "text" or "tinytext" or "mediumtext" or "longtext" or "string" => "string",
        "date" => "date", "time" => "time", "datetime" or "timestamp" => "datetime",
        "bool" or "boolean" => "bool", "json" => "json", "blob" or "binary" => "binary",
        _ => throw new ArgumentException("字段物理类型无已知逻辑映射。")
    };
}
