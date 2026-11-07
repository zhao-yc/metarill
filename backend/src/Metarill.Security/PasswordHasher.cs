using System.Security.Cryptography;
namespace Metarill.Runtime.Security;

public static class PasswordHasher
{
    /// <summary>密码只保存带随机盐的 PBKDF2 摘要，不复用旧 MD5 方案。</summary>
    public static string Hash(string password)
    {
        if(password.Length<12)throw new InvalidOperationException("密码至少需要12个字符。");
        return Encode(password);
    }
    /// <summary>原后台初始化配置允许非空且最长50字符；专用入口保持原验证，仅替换为现有随机盐PBKDF2格式。</summary>
    internal static string HashInitializationConfiguration(string password)
    {
        if(string.IsNullOrWhiteSpace(password)||password.Length>50)throw new ArgumentException("初始化密码必须非空且不超过50字符。");
        return Encode(password);
    }
    /// <summary>编码算法统一，两个入口只区分已有的输入合同。</summary>
    private static string Encode(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(24);
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,350000,HashAlgorithmName.SHA256,32);
        return "pbkdf2-sha256$350000$"+Convert.ToBase64String(salt)+"$"+Convert.ToBase64String(hash);
    }
    /// <summary>沿用已保存摘要格式与固定时间比较，迁移工程不会重算旧密码。</summary>
    public static bool Verify(string password,string encoded)
    {
        try
        {
            var parts=encoded.Split('$');if(parts.Length!=4||parts[0]!="pbkdf2-sha256")return false;
            var expected=Convert.FromBase64String(parts[3]);
            var actual=Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(parts[2]),int.Parse(parts[1]),HashAlgorithmName.SHA256,expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        }
        catch(FormatException){return false;}
    }
    /// <summary>会话原令牌只交给调用者，数据库持久化它的摘要。</summary>
    public static string Token()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    /// <summary>复用现有SHA256令牌摘要协议，不引入新的密钥或存储副本。</summary>
    public static string Digest(string token)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
