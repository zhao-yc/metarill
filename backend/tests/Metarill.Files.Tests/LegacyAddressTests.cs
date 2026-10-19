using Metarill.Runtime.Files;
using Xunit;

namespace Metarill.Files.Tests;

public sealed class LegacyAddressTests
{
    /// <summary>公开配置不得包含凭据、协议混淆或影响固定路由的query/fragment。</summary>
    [Theory]
    [InlineData("file:///tmp/object")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test/path?projectId=999")]
    [InlineData("https://example.test/path#fragment")]
    [InlineData(" https://example.test")]
    [InlineData("https://example.test\n")]
    public void Untrusted_or_ambiguous_base_configuration_is_rejected(string value) => Assert.Throws<InvalidOperationException>(() => LegacyUploadAddress.NormalizeBase(value));

    /// <summary>拼接保留配置前缀，配置存在时不重复添加请求PathBase；空配置维持同源语义。</summary>
    [Fact]
    public void Trusted_prefix_and_same_origin_fallback_are_preserved()
    {
        const string route = "/admin/sys/file-library/public-content/123?projectId=456";
        var configured = LegacyUploadAddress.NormalizeBase("https://public.example.test/prefix/nested/");
        Assert.Equal("https://public.example.test/prefix/nested/api" + route, LegacyUploadAddress.Build(configured, route, "/untrusted-duplicate"));
        Assert.Equal("/mounted/api" + route, LegacyUploadAddress.Build(null, route, "/mounted"));
        Assert.Equal("/api" + route, LegacyUploadAddress.Build(LegacyUploadAddress.NormalizeBase(""), route, null));
        Assert.Throws<InvalidOperationException>(() => LegacyUploadAddress.Build(configured, "https://other.example/anything", null));
    }
}
