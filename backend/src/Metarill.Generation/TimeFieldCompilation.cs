namespace Metarill.Generation;

/// <summary>时间控件与原生TIME使用显式字段元数据分流，避免仅按CLR类型误限原生时长。</summary>
internal static class TimeFieldCompilation
{
    /// <summary>原time选择器为日内时刻，普通文本/反查原生TIME保持总小时合同。</summary>
    internal static bool IsClock(ColumnModel? column)=>column?.Source.ControlKind=="time";
    /// <summary>返回明确框架合同名，实体同名时也不能遮蔽。</summary>
    internal static string Value(ColumnModel? column)=>"global::Metarill.Core.Contracts."+(IsClock(column)?"ClockTimeValue":"SqlTimeValue");
    /// <summary>字段DTO/VO使用自己的转换器，不注册到全局持续时间选项。</summary>
    internal static string Converter(ColumnModel? column)=>"global::Metarill.Core.Contracts."+(IsClock(column)?"ClockTimeJsonConverter":"SqlTimeJsonConverter");
}
