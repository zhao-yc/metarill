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
    /// <summary>MySQL 的 VARBINARY 默认快照可能在零字节处截断，改从同一次反查的建表文本核对明确常量。</summary>
    internal static bool TryReadCreateDefault(string ddl, string column, string storeType, out string? sql)
    {
        sql = null;
        if (!Supports(storeType)) return false;
        var name = "\x60" + column.Replace("\x60", "\x60\x60") + "\x60";
        var hex = "(?:0x[0-9a-f]*|x'[0-9a-f]*')";
        // MySQL 的 SHOW CREATE 将 VARBINARY 的 X'' 输出为字符形式的空字面量。
        // 只在实际列定义末尾或已知的列注释前接受该拼写，避免把其它表达式截断成零字节。
        var emptyVarbinary = storeType.StartsWith("varbinary(", StringComparison.Ordinal)
            ? @"|''(?=[ \t]*(?:,|\r?$|COMMENT[ \t]))"
            : "";
        var pattern = @"^[ \t]*" + System.Text.RegularExpressions.Regex.Escape(name)
            + @"[ \t]+" + System.Text.RegularExpressions.Regex.Escape(storeType)
            + @"[ \t]+(?:(?:NOT[ \t]+)?NULL[ \t]+)?DEFAULT[ \t]+(?<value>"
            + hex + @"|\(" + hex + @"\)" + emptyVarbinary + @")(?=[ \t,\r\n]|$)";
        var matches = System.Text.RegularExpressions.Regex.Matches(ddl, pattern,
            System.Text.RegularExpressions.RegexOptions.Multiline
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (matches.Count != 1) return false;
        var value = matches[0].Groups["value"].Value;
        return TryNormalize(value == "''" ? "X''" : value, out sql);
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
