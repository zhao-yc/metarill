using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>人工验收的误配和迟到信号必须明确失败，不能被静默跳过或标成浏览器通过。</summary>
public sealed class TrustedManagementBrowserLeaseTests
{
    /// <summary>未请求租约时普通测试保持原模式，构建完成与浏览器结束使用不同状态。</summary>
    [Fact]
    public void Default_and_two_release_states_remain_distinct()
    {
        Assert.Null(TrustedManagementBrowserLease.ReadSeconds(null));Assert.Equal(1800,TrustedManagementBrowserLease.ReadSeconds("1800"));
        Assert.Equal("build-only",TrustedManagementBrowserLease.ReadRelease("{\"leaseId\":\"own\",\"status\":\"build-only\"}","own"));
        Assert.Equal("completed",TrustedManagementBrowserLease.ReadRelease("{\"leaseId\":\"own\",\"status\":\"completed\"}","own"));
    }
    /// <summary>错误等待时长直接拒绝，不扩大到无期限测试进程。</summary>
    [Theory][InlineData("")][InlineData("59")][InlineData("1801")][InlineData("-1")][InlineData("1800 ")]
    public void Invalid_waits_do_not_start_a_lease(string value)=>Assert.Throws<ArgumentException>(()=>TrustedManagementBrowserLease.ReadSeconds(value));
    /// <summary>旧轮次、重复键、未知状态和额外字段均不能释放本轮宿主。</summary>
    [Theory]
    [InlineData("{\"leaseId\":\"old\",\"status\":\"completed\"}")]
    [InlineData("{\"leaseId\":\"own\",\"status\":\"passed\"}")]
    [InlineData("{\"leaseId\":\"own\",\"status\":\"completed\",\"status\":\"completed\"}")]
    [InlineData("{\"leaseId\":\"own\",\"status\":\"completed\",\"extra\":true}")]
    [InlineData("[]")]
    public void Wrong_or_ambiguous_release_is_not_browser_completion(string json)=>Assert.Throws<InvalidOperationException>(()=>TrustedManagementBrowserLease.ReadRelease(json,"own"));
}
