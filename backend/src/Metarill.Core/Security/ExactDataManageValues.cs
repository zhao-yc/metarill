using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Metarill.Core.Security;

/// <summary>仅供服务端固定范围计划使用；缺值表示无匹配，不向HTTP输出账号匹配值。</summary>
public sealed record ExactDataManageValues([property:JsonIgnore] bool IsRequired,[property:JsonIgnore] JsonElement? MatchValue)
{
    public static ExactDataManageValues Unrestricted {get;}=new(false,null);
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    { "connstring", "connectionstring", "password", "pwd", "dbpassword", "accesskey", "accesskeyid", "accesskeysecret", "secret", "secretkey", "privatekey", "clientsecret", "apitoken", "accesstoken", "refreshtoken", "token", "apikey", "credentials" };
    /// <summary>发布脱敏和固定匹配计划共用凭据字段判定，间接联接不能绕过相同规则。</summary>
    public static bool IsSensitiveField(string value)=>SensitiveKeys.Contains(new string(value.Where(char.IsLetterOrDigit).ToArray()));
    /// <summary>单值或原JSON数组按目标列类型转换；空值/空数组不匹配，错误不退化为无限制。</summary>
    public T[] Read<T>()
    {
        if(!IsRequired)return [];
        var raw=MatchValue;if(raw is null||raw.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)return [];
        var value=raw.Value;
        if(value.ValueKind==JsonValueKind.String)
        {
            var text=value.GetString()!;if(string.IsNullOrWhiteSpace(text))return [];
            if(text.TrimStart().StartsWith('['))
            {
                try{using var document=JsonDocument.Parse(text);return Array<T>(document.RootElement);}
                catch(JsonException){throw Invalid();}
            }
        }
        return value.ValueKind==JsonValueKind.Array?Array<T>(value):[Scalar<T>(value)];
    }
    /// <summary>固定SQL和文件读取使用同一逻辑类型转换，不依赖数据库隐式数值转换。</summary>
    public IReadOnlyList<object?> ReadLogical(string logicalType)=>logicalType.ToLowerInvariant() switch
    {
        "id" or "long" or "bigint" or "int64"=>Read<long>().Cast<object?>().ToArray(),
        "int" or "integer" or "int32"=>Read<int>().Cast<object?>().ToArray(),
        "bool" or "boolean"=>Read<bool>().Cast<object?>().ToArray(),
        "decimal" or "money" or "number"=>Read<decimal>().Cast<object?>().ToArray(),
        "double"=>Read<double>().Cast<object?>().ToArray(),
        "date" or "datetime" or "timestamp"=>Read<DateTime>().Cast<object?>().ToArray(),
        "string" or "varchar" or "text"=>Read<string>().Cast<object?>().ToArray(),
        _=>throw new NotSupportedException("精确管理匹配字段的逻辑类型尚未支持。")
    };
    /// <summary>只接受字段标识，camel/Pascal与snake规范化为同一键，绝不解析SQL或属性路径。</summary>
    public static string Field(string? configured)
    {
        var value=configured?.Trim()??"";if(value.Length==0)return "";
        if(!Regex.IsMatch(value,@"^[A-Za-z_][A-Za-z0-9_]*$",RegexOptions.CultureInvariant))throw new ArgumentException("精确管理匹配字段声明无效。");
        return Regex.Replace(Regex.Replace(value,@"([A-Z]+)([A-Z][a-z])","$1_$2"),@"([a-z0-9])([A-Z])","$1_$2").ToLowerInvariant();
    }
    /// <summary>IN中的null沿原SQL语义不匹配null列，嵌套数组/对象不能成为参数。</summary>
    private static T[] Array<T>(JsonElement array)
    {
        if(array.ValueKind!=JsonValueKind.Array)throw Invalid();
        return array.EnumerateArray().Where(value=>value.ValueKind!=JsonValueKind.Null).Select(Scalar<T>).Distinct().ToArray();
    }
    /// <summary>长整数不经double，类型失败只输出固定错误，不包含账号原值。</summary>
    private static T Scalar<T>(JsonElement value)
    {
        if(value.ValueKind is not(JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))throw Invalid();
        var text=value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText();var type=Nullable.GetUnderlyingType(typeof(T))??typeof(T);object result;
        if(type==typeof(string))result=text;
        else if(type==typeof(long)&&long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var wide))result=wide;
        else if(type==typeof(int)&&int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var integer))result=integer;
        else if(type==typeof(decimal)&&decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number))result=number;
        else if(type==typeof(double)&&double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var real)&&double.IsFinite(real))result=real;
        else if(type==typeof(bool)&&(text is "1" or "0"||bool.TryParse(text,out _)))result=text=="1"||text!="0"&&bool.Parse(text);
        else if(type==typeof(DateTime)&&DateTime.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var date))result=date;
        else throw Invalid();
        return (T)result;
    }
    /// <summary>固定拒绝信息不能泄露账号匹配值或驱动参数。</summary>
    private static ArgumentException Invalid()=>new("精确数据管理匹配值不符合目标字段定义。");
}
