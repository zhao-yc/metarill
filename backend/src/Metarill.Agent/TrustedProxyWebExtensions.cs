using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Metarill.Runtime.Security;

public static class TrustedProxyWebExtensions
{
    /// <summary>登录和 Agent 限流只采信直接连接的受信代理提供的最后一跳客户端地址。</summary>
    public static void UseTrustedClientIp(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("ForwardedHeaders_Enabled")
            || string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_FORWARDEDHEADERS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment.GetEnvironmentVariable("DOTNET_FORWARDEDHEADERS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请关闭 FORWARDEDHEADERS_ENABLED，并在 Metarill:ReverseProxy:TrustedIps 中配置确切代理地址。");

        var section = app.Configuration.GetSection("Metarill:ReverseProxy:TrustedIps");
        if (section.Value is { Length: > 0 })
            throw new InvalidOperationException("反向代理地址必须配置为 IP 地址数组。");

        // .NET 10 的 JSON 提供器用空字符串表示 []。空名单只允许更高优先级的显式配置补充地址。
        string?[]? proxies;
        if (section.Value == string.Empty)
        {
            var overrides = ((IConfigurationRoot)app.Configuration).Providers.Reverse()
                .TakeWhile(provider => !provider.TryGet(section.Path, out _)).ToArray();
            proxies = section.GetChildren()
                .Where(child => overrides.Any(provider => provider.TryGet(child.Path, out _)))
                .Select(child => child.Value).ToArray();
        }
        else
        {
            proxies = section.Get<string[]>();
        }
        if (proxies is not { Length: > 0 }) return;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1
        };
        // 清除框架默认的回环信任，只使用部署者明确配置的对端 IP。
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var value in proxies)
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException("反向代理配置必须使用确切 IP 地址。");
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("反向代理配置必须使用确切 IP 地址。");
            options.KnownProxies.Add(address);
        }
        // 缺少 TCP 对端地址时无法证明请求来自受信代理。
        app.UseWhen(context => context.Connection.RemoteIpAddress is not null, branch => branch.UseForwardedHeaders(options));
    }
}
