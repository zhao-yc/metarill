using System.Text.Json;
using System.Text.Json.Serialization;
namespace Metarill.Core.Contracts;

/// <summary>仅标注生成time字段及该Service局部选项，不改变全局TimeSpan持续时间合同。</summary>
public sealed class ClockTimeJsonConverter:JsonConverterFactory
{
    /// <summary>支持持久化/VO、筛选及原可选请求包装，不通过反射选择任意类型。</summary>
    public override bool CanConvert(Type type)=>type==typeof(TimeSpan)||type==typeof(TimeSpan?)||type==typeof(Optional<TimeSpan?>);
    /// <summary>字段转换保留省略与null，非可空筛选值不能清空。</summary>
    public override JsonConverter CreateConverter(Type type,JsonSerializerOptions options)=>type==typeof(TimeSpan)?new ValueConverter():type==typeof(TimeSpan?)?new NullableConverter():type==typeof(Optional<TimeSpan?>)?new OptionalConverter():throw new NotSupportedException("时间字段转换类型不受支持。");
    /// <summary>读取固定形状；不把非空无效文本悄悄变成null。</summary>
    private static TimeSpan? ReadValue(ref Utf8JsonReader reader)=>reader.TokenType switch{JsonTokenType.Null=>null,JsonTokenType.String=>ClockTimeValue.ParseText(reader.GetString()),_=>throw new JsonException("时间只接受文本或null。")};
    /// <summary>原字段明确空值保留为空，实际时刻由同一格式器输出。</summary>
    private static void WriteValue(Utf8JsonWriter writer,TimeSpan? value){if(value is null)writer.WriteNullValue();else writer.WriteStringValue(ClockTimeValue.Format(value.Value));}
    private sealed class ValueConverter:JsonConverter<TimeSpan>
    {
        /// <summary>非可空调用拒绝明确清空。</summary>
        public override TimeSpan Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>ReadValue(ref reader)??throw new JsonException("非可空时刻不能清空。");
        /// <summary>仅此字段使用无日期/时区的日内字符串。</summary>
        public override void Write(Utf8JsonWriter writer,TimeSpan value,JsonSerializerOptions options)=>WriteValue(writer,value);
    }
    private sealed class NullableConverter:JsonConverter<TimeSpan?>
    {
        /// <summary>可空字段按相同规则读取，错误不降级为空。</summary>
        public override TimeSpan? Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>ReadValue(ref reader);
        /// <summary>仅此字段使用无日期/时区的日内字符串。</summary>
        public override void Write(Utf8JsonWriter writer,TimeSpan? value,JsonSerializerOptions options)=>WriteValue(writer,value);
    }
    private sealed class OptionalConverter:JsonConverter<Optional<TimeSpan?>>
    {
        public override bool HandleNull=>true;
        /// <summary>显式null/空串仍记录提交，缺省由Optional默认值保留。</summary>
        public override Optional<TimeSpan?> Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>new(ReadValue(ref reader));
        /// <summary>仅此字段使用无日期/时区的日内字符串。</summary>
        public override void Write(Utf8JsonWriter writer,Optional<TimeSpan?> value,JsonSerializerOptions options)=>WriteValue(writer,value.Value);
    }
}
