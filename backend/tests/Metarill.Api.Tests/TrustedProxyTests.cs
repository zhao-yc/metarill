using System.Net;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Metarill.Api.Tests;

public sealed class TrustedProxyTests
{
    /// <summary>用真实转发中间件核对直接对端、最后一跳及其余转发字段的边界。</summary>
    [Theory]
    [InlineData(null, "127.0.0.1", "203.0.113.10", "127.0.0.1")]
    [InlineData("127.0.0.1", "127.0.0.1", "198.51.100.5, 203.0.113.10", "203.0.113.10")]
    [InlineData("127.0.0.1", "::ffff:127.0.0.1", "203.0.113.10", "203.0.113.10")]
    [InlineData("127.0.0.1", "127.0.0.2", "203.0.113.10", "127.0.0.2")]
    [InlineData("127.0.0.1", "127.0.0.1", "198.51.100.5, invalid", "127.0.0.1")]
    [InlineData("127.0.0.1", null, "203.0.113.10", null)]
    public async Task Only_configured_tcp_proxy_can_supply_one_client_ip(string? trusted, string? peer, string header, string? expected)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (trusted is not null) builder.Configuration["Metarill:ReverseProxy:TrustedIps:0"] = trusted;

        await using var app = builder.Build();
        app.UseTrustedClientIp();
        app.MapGet("/", (HttpContext context) => Results.Text($"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}|{context.Request.Host}"));
        await app.StartAsync();

        var response = await app.GetTestServer().SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
            context.Request.Headers["X-Forwarded-For"] = header;
            context.Request.Headers["X-Forwarded-Host"] = "foreign.example";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        using var body = new StreamReader(response.Response.Body);
        Assert.Equal($"{expected}|http|localhost", await body.ReadToEndAsync());
    }

    /// <summary>配置错误直接拒绝启动，避免误把所有地址或网段当成受信代理。</summary>
    [Theory]
    [InlineData("proxy.example")]
    [InlineData("127.0.0.0/8")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public async Task Invalid_proxy_addresses_fail_at_startup(string address)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["Metarill:ReverseProxy:TrustedIps:0"] = address;
        await using var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.UseTrustedClientIp());
    }
}
