using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>不依赖数据库或真实 Cloud key，验证媒体编辑配置的身份和最小披露边界。</summary>
public sealed class TinyCloudConfigurationTests
{
    /// <summary>只有显式认证身份才能读取部署配置，客户端选择参数不能成为身份。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("示例测试key")]
    public void Anonymous_configuration_is_rejected_before_reading_settings(string? key)
    {
        var controller = Create(new Dictionary<string, string?> { ["Metarill:TinyCloud:ApiKey"] = key }, false);
        Assert.Throws<UnauthorizedAccessException>(() => controller.Configuration());
    }

    /// <summary>默认空配置明确不可用，既不加载 no-api-key，也不披露旁路秘密。</summary>
    [Fact]
    public void Empty_configuration_returns_disabled_and_private_no_store()
    {
        var controller = Create(new Dictionary<string, string?> { ["Metarill:TinyCloud:ApiKey"] = " ", ["Metarill:TinyCloud:Secret"] = "不应返回" });
        var result = controller.Configuration();
        Assert.Equal(200, result.Code); Assert.False(result.Data!.Enabled); Assert.Null(result.Data.ApiKey); Assert.Null(result.Data.ScriptSrc);
        Assert.Contains("尚未配置", result.Data.Message); Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.DoesNotContain("不应返回", JsonSerializer.Serialize(result));
    }

    /// <summary>认证普通账号可以加载同一部署的 Cloud，响应严格限定官方脚本及原有付费入口。</summary>
    [Fact]
    public void Authenticated_configuration_contains_only_explicit_client_fields()
    {
        var controller = Create(new Dictionary<string, string?> { ["Metarill:TinyCloud:ApiKey"] = "mock-key_123", ["Metarill:TinyCloud:CloudChannel"] = "8.3", ["Metarill:TinyCloud:PremiumPlugins:0"] = "checklist", ["Metarill:TinyCloud:PremiumPlugins:1"] = "checklist", ["Metarill:TinyCloud:PrivateKey"] = "不应返回", ["Metarill:Database:ConnectionString"] = "另一个秘密" });
        var result = controller.Configuration().Data!;
        Assert.True(result.Enabled); Assert.Equal("https://cdn.tiny.cloud/1/mock-key_123/tinymce/8.3/tinymce.min.js", result.ScriptSrc);
        Assert.Equal("zh-CN", result.Language); Assert.Equal(["checklist"], result.PremiumPlugins); Assert.Null(result.Message);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("不应返回", serialized); Assert.DoesNotContain("另一个秘密", serialized);
    }

    /// <summary>部署配置格式错误不回显原值，不允许改变脚本主机或加载任意插件。</summary>
    [Theory]
    [InlineData("ApiKey", "no-api-key")]
    [InlineData("ApiKey", "secret/key?token=hidden")]
    [InlineData("CloudChannel", "8-dev")]
    [InlineData("CloudChannel", "8/../../evil")]
    [InlineData("PremiumPlugins:0", "https://example.invalid/plugin.js")]
    public void Invalid_configuration_is_rejected_without_echoing_value(string field, string value)
    {
        var settings = new Dictionary<string, string?> { ["Metarill:TinyCloud:ApiKey"] = "mock-key_123", ["Metarill:TinyCloud:" + field] = value };
        var error = Assert.Throws<InvalidOperationException>(() => Create(settings).Configuration());
        Assert.DoesNotContain(value, error.Message);
    }

    /// <summary>用明确的认证测试替身隔离配置逻辑；生产仍调用真实 IRuntimeAuthorization。</summary>
    private static TinyCloudController Create(Dictionary<string, string?> settings, bool authenticated = true)
        => new(new Authorization(authenticated), new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private sealed class Authorization(bool authenticated) : IRuntimeAuthorization
    {
        /// <summary>缺身份直接失败，认证普通账号不附加管理员权利。</summary>
        public RuntimeActor RequireActor() => authenticated ? new RuntimeActor(123, "测试普通账号", false, "456", "789") : throw new UnauthorizedAccessException("登录超时");
        public void RequirePagePermission(string pageId, string operation) => throw new NotSupportedException("此接口不读取页面选择作为授权。");
    }
}
