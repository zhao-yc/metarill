using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Metarill.Core.Contracts;

/// <summary>逻辑double保留浮点语义；拒绝非有限值及非零下溢，避免入库／筛选前被悄悄变为其它数。</summary>
public sealed class FiniteDoubleJsonConverter:JsonConverter<double>
{
    /// <summary>沿原选项接受数值字符串；有界读取跨片段数字，不经decimal缩窄double范围。</summary>
    public override double Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        string? value;
        if(reader.TokenType==JsonTokenType.String&&(options.NumberHandling&JsonNumberHandling.AllowReadingFromString)!=0)value=reader.GetString();
        else if(reader.TokenType==JsonTokenType.Number)
        {
            if((reader.HasValueSequence?reader.ValueSequence.Length:reader.ValueSpan.Length)>512)throw new JsonException("浮点数文本超过允许长度。");
            value=reader.HasValueSequence?Encoding.UTF8.GetString(reader.ValueSequence.ToArray()):Encoding.UTF8.GetString(reader.ValueSpan);
        }
        else throw new JsonException("浮点数只接受数字或允许的数值字符串。");
        if(!ExactNumericValue.TryDouble(value,out var number))throw new JsonException("浮点数必须是有效有限值，不能溢出或下溢为零。");
        return number;
    }
    /// <summary>保持原数字输出，尊重显式字符串输出选项；手写业务产生的NaN／无穷大也不进入响应。</summary>
    public override void Write(Utf8JsonWriter writer,double value,JsonSerializerOptions options)
    {
        if(!double.IsFinite(value))throw new JsonException("浮点数不能是NaN或无穷大。");
        if((options.NumberHandling&JsonNumberHandling.WriteAsString)!=0)writer.WriteStringValue(value.ToString("R",CultureInfo.InvariantCulture));else writer.WriteNumberValue(value);
    }
}
