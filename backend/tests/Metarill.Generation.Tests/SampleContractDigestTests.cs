using System.Globalization;
using System.Text.Json;
using Metarill.GlobalSample.Business;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>直接执行公开样板源文件；固定向量另由真实Java 21对照，不用另一份C#算法生成期望值。</summary>
public sealed class SampleContractDigestTests
{
    /// <summary>中文、非BMP、组合字符、空值、不同scale及大ID在各种文化下保持原摘要。</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void Fixed_java_vectors_preserve_utf16_length_decimal_scale_and_invariant_format(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(NonRedundantApplicationFixture.TestDirectory(), "Fixtures/sample-contract-digest.json")));
            foreach (var vector in vectors.RootElement.EnumerateArray())
            {
                var id = long.Parse(vector.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
                var amount = vector.GetProperty("amount").GetString();
                var actual = SampleContractDigest.Calculate(id, vector.GetProperty("name").GetString(), amount is null ? null : decimal.Parse(amount, CultureInfo.InvariantCulture), vector.GetProperty("remarks").GetString());
                Assert.Equal(vector.GetProperty("expectedDigest").GetString(), actual); Assert.True(SampleContractDigest.Matches(actual, actual));
                Assert.False(SampleContractDigest.Matches(actual, actual.ToUpperInvariant())); Assert.False(SampleContractDigest.Matches(actual, null)); Assert.False(SampleContractDigest.Matches(actual, actual + " "));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
