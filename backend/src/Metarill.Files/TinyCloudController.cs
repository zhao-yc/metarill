using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Metarill.Api.Controllers;

public sealed record TinyCloudClientConfiguration(bool Enabled, string? ApiKey, string CloudChannel, string? ScriptSrc, string Language, IReadOnlyList<string> PremiumPlugins, string? Message);

/// <summary>平台和独立宿主共用的媒体编辑配置；只返回当前部署明确允许客户端使用的 Cloud 参数。</summary>
[ApiController, Route("api/admin/sys/tiny-cloud")]
public sealed class TinyCloudController(IRuntimeAuthorization authorization, IConfiguration configuration) : ControllerBase
{
    /// <summary>先验证真实登录身份，再读取固定私有配置；API key 是 Cloud 浏览器标识，其他配置与秘密不下发。</summary>
    [HttpPost("config")]
    public R<TinyCloudClientConfiguration> Configuration()
    {
        authorization.RequireActor();
        Response.Headers.CacheControl = "private, no-store";
        var key = configuration["Metarill:TinyCloud:ApiKey"]?.Trim();
        if (string.IsNullOrEmpty(key))
            return R<TinyCloudClientConfiguration>.Success(new(false, null, "8", null, "zh-CN", [], "尚未配置 Tiny Cloud，请管理员设置本部署的 ApiKey。"));
        if (key.Length > 256 || key == "no-api-key" || !Regex.IsMatch(key, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Tiny Cloud ApiKey 配置格式不正确。");
        var channel = configuration["Metarill:TinyCloud:CloudChannel"]?.Trim() ?? "8";
        if (!Regex.IsMatch(channel, @"^8(?:\.[0-9]{1,3})?$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Tiny Cloud 仅允许稳定版 8 或明确的 8.x 频道。");
        // 只允许原工具栏已有的两个付费入口，开关不代表 Cloud 账户已取得相应授权。
        var premium = configuration.GetSection("Metarill:TinyCloud:PremiumPlugins").GetChildren().Select(item => item.Value ?? "").ToArray();
        if (premium.Any(value => value is not ("mergetags" or "checklist")))
            throw new InvalidOperationException("Tiny Cloud 付费插件配置不在原工具栏范围内。");
        var script = "https://cdn.tiny.cloud/1/" + Uri.EscapeDataString(key) + "/tinymce/" + channel + "/tinymce.min.js";
        return R<TinyCloudClientConfiguration>.Success(new(true, key, channel, script, "zh-CN", premium.Distinct(StringComparer.Ordinal).ToArray(), null));
    }
}
