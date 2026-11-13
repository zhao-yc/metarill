using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;

namespace Metarill.Runtime.Security;

/// <summary>绑定既有角色revision的字节合同；与平台原JsonDefaults逐字等价，不改其它宿主JSON设置。</summary>
internal static class RolePermissionRevisionJson
{
    internal static JsonSerializerOptions Options {get;}=Create();
    /// <summary>保留原Web命名、缩进、长ID字符串及固定日期格式；修改时必须同时核对旧revision预像。</summary>
    private static JsonSerializerOptions Create()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true,NumberHandling=JsonNumberHandling.AllowReadingFromString};
        options.Converters.Add(new StringLongJsonConverter());options.Converters.Add(new RevisionDateTimeConverter());return options;
    }
    private sealed class RevisionDateTimeConverter:JsonConverter<DateTime>
    {
        /// <summary>保留平台原兼容日期解析规则；revision当前只使用写出，避免未来误用时改变既有解释。</summary>
        public override DateTime Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>DateTime.Parse(reader.GetString()!,CultureInfo.InvariantCulture,DateTimeStyles.AllowWhiteSpaces);
        /// <summary>日期精度、分隔符与原JsonDefaults一致，不能改为ISO格式使全部旧草稿无故失效。</summary>
        public override void Write(Utf8JsonWriter writer,DateTime value,JsonSerializerOptions options)=>writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture));
    }
}
