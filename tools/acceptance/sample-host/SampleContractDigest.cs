using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Metarill.GlobalSample.Business;

/// <summary>与固定Java样板的BigDecimal.toPlainString、UTF-16长度前缀及UTF-8 SHA-256保持一致。</summary>
public static class SampleContractDigest
{
    /// <summary>摘要绑定真实记录、名称、金额和备注；省略与null均使用原空串规则。</summary>
    public static string Calculate(long id, string? name, decimal? amount, string? remarks)
    {
        var source = new StringBuilder();
        foreach (var field in new[] { id.ToString(CultureInfo.InvariantCulture), name, amount?.ToString(CultureInfo.InvariantCulture), remarks })
        {
            var text = field ?? "";
            // string.Length与Java String.length均按UTF-16代码单元计数，不能换成UTF-8字节数或Unicode字符数。
            source.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToString())));
    }
    /// <summary>预览摘要逐字比较，保留原大小写语义；调用者不能通过文化设置改变结果。</summary>
    public static bool Matches(string actual, string? expected) => expected is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
