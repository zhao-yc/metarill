using System.Security.Cryptography;
namespace Metarill.Runtime.Security;

public static class PasswordHasher
{
    /// <summary>密码只保存带随机盐的 PBKDF2 摘要，不复用旧 MD5 方案。</summary>
    public static string Hash(string password)
    {
        if(password.Length<12)throw new InvalidOperationException("密码至少需要12个字符。");
        var salt=RandomNumberGenerator.GetBytes(24);
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,350000,HashAlgorithmName.SHA256,32);
        return "pbkdf2-sha256$350000$"+Convert.ToBase64String(salt)+"$"+Convert.ToBase64String(hash);
    }
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
    public static string Token()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Digest(string token)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
