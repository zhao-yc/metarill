using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
namespace Metarill.Data.Schema;

/// <summary>逻辑Base64只编译为MySQL十六进制常量；未知表达式不解除结构保护。</summary>
internal static class MySqlBinaryDefault
{
    /// <summary>BLOB必须使用表达式括号；VARBINARY使用字面量，不依赖连接字符集或SQL转义模式。</summary>
    internal static string? Compile(ColumnMetadata column,string storeType)
    {
        var bytes=BinaryFieldValue.DefaultBytes(column.DefaultValue,column.ColumnLength);if(bytes is null)return null;
        var literal="X'"+Convert.ToHexString(bytes)+"'";return storeType=="longblob"?"("+literal+")":literal;
    }
    /// <summary>仅接受服务端明确十六进制常量及一层括号，不执行函数、字符转换或客户端SQL。</summary>
    internal static bool TryNormalize(string? raw,out string? sql)
    {
        sql=null;if(raw is null)return false;var text=raw.Trim();
        if(text.StartsWith('(')&&text.EndsWith(')'))text=text[1..^1].Trim();
        string hex;
        if(text.StartsWith("0x",StringComparison.Ordinal))hex=text[2..];
        else if(text.Length>=3&&text[0] is 'x' or 'X'&&text[1]=='\''&&text[^1]=='\'')hex=text[2..^1];
        else return false;
        if(hex.Length%2!=0||hex.Any(value=>!char.IsAsciiHexDigit(value)))return false;
        sql="X'"+hex.ToUpperInvariant()+"'";return true;
    }
    /// <summary>反查只还原已归一字节，不将SQL文字误作为Base64默认内容。</summary>
    internal static byte[] Read(string raw)
    {
        if(!TryNormalize(raw,out var sql))throw new NotSupportedException("二进制默认值不是已证明的十六进制常量。");
        return Convert.FromHexString(sql![2..^1]);
    }
    /// <summary>仅为已开放的可变长二进制物理类型归一；固定长BINARY填零语义仍不支持。</summary>
    internal static bool Supports(string type)=>type=="longblob"||type.StartsWith("varbinary(",StringComparison.Ordinal)&&type.EndsWith(')');
}
