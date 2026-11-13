using System.Security.Cryptography;
using System.Text;
namespace Metarill.Api.GenerationAssistant;

/// <summary>只核对原配置与本机绑定的明确目录身份，不访问文件系统或展开相对路径。</summary>
internal static class DeviceProjectPath
{
    /// <summary>固定版本前缀与UTF8字节进入摘要，线上仅传摘要，不传本机路径。</summary>
    internal static string Fingerprint(string raw)=>Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false,true).GetBytes("metarill-device-path-v1"+(char)10+Normalize(raw))));
    /// <summary>Windows仅折叠ASCII大小写与分隔符；POSIX保持大小写；点层级和特殊路径明确拒绝。</summary>
    internal static string Normalize(string raw)
    {
        if(string.IsNullOrWhiteSpace(raw)||raw.Length>4096||char.IsWhiteSpace(raw[0])||char.IsWhiteSpace(raw[^1])||raw[0]==(char)0xfeff||raw[^1]==(char)0xfeff||raw.Any(char.IsControl))throw new ArgumentException("设备路径必须是明确的有界绝对目录。");
        _=new UTF8Encoding(false,true).GetByteCount(raw);
        var drive=raw.Length>=3&&(raw[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z')&&raw[1]==':'&&(raw[2]=='/'||raw[2]==92);
        var windows=drive||raw.StartsWith(new string((char)92,2),StringComparison.Ordinal)||raw.StartsWith("//",StringComparison.Ordinal);
        var text=windows?raw.Replace((char)92,'/'):raw;string prefix;string[] parts;
        if(drive){prefix=AsciiLower(text[..2])+"/";parts=text[3..].Split('/',StringSplitOptions.RemoveEmptyEntries);}
        else if(windows)
        {
            var all=text[2..].Split('/',StringSplitOptions.RemoveEmptyEntries);if(all.Length<3)throw new ArgumentException("设备路径不能是共享根目录。");
            WindowsSegment(all[0]);WindowsSegment(all[1]);prefix="//"+AsciiLower(all[0])+"/"+AsciiLower(all[1])+"/";parts=all[2..];
        }
        else if(text.StartsWith("/",StringComparison.Ordinal)&&!text.Contains((char)92)){prefix="/";parts=text[1..].Split('/',StringSplitOptions.RemoveEmptyEntries);}
        else throw new ArgumentException("设备路径不能是相对路径或隐式盘符路径。");
        if(parts.Length==0)throw new ArgumentException("设备路径不能是根目录。");
        foreach(var part in parts){if(part is "." or "..")throw new ArgumentException("设备路径不能含点层级，请配置实际绝对目录。");if(windows)WindowsSegment(part);}
        return (windows?"windows|":"posix|")+prefix+string.Join('/',windows?parts.Select(AsciiLower):parts);
    }
    /// <summary>拒绝Windows设备名、ADS、尾点/空格及特殊设备前缀，不能将其视为普通项目目录。</summary>
    private static void WindowsSegment(string part)
    {
        var name=AsciiLower(part).Split('.')[0];
        var reserved=name is "con" or "prn" or "aux" or "nul"||name.Length==4&&(name.StartsWith("com",StringComparison.Ordinal)||name.StartsWith("lpt",StringComparison.Ordinal))&&name[3] is >= '1' and <= '9';
        if(part is "." or ".."||part.EndsWith(' ')||part.EndsWith('.')||part.Any(ch=>ch is (char)60 or (char)62 or (char)58 or (char)34 or (char)124 or (char)63 or (char)42)||reserved)throw new ArgumentException("设备路径包含特殊Windows名称。");
    }
    /// <summary>与Node逐ASCII码一致，不将不同Unicode文件名强制折叠为同一路径。</summary>
    private static string AsciiLower(string text)=>string.Concat(text.Select(value=>value is >= 'A' and <= 'Z'?(char)(value+32):value));
}
