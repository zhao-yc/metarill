using System.Globalization;
using Metarill.Core.Security;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Agent;

public sealed record AgentOAuthOptions
{
    public string VerificationUri { get; init; } = "http://127.0.0.1:8088/#/pages/custom/agent_authorize";
    public int DeviceCodeSeconds { get; init; } = 600;
    public int PollingSeconds { get; init; } = 5;
    public int AccessTokenSeconds { get; init; } = 900;
    public int GrantSeconds { get; init; } = 30 * 24 * 3600;
}
public sealed class AgentOAuthException(string error, string description, int statusCode = 400) : Exception(description)
{
    public string Error { get; } = error;
    public int StatusCode { get; } = statusCode;
}
public sealed record AgentRequestIdentity(AuthenticatedIdentity Identity, string Kind, long? GrantId);
public sealed record AgentDeviceView(string UserCode, string Status, string ClientName, string ProjectId, string ProjectName, long ExpiresAt, string PermissionNotice);
public sealed record AgentApproveRequest(string UserCode, bool Approve);
public sealed record AgentRevokeRequest(long? GrantId = null);

/// <summary>公开端点限频只以实际连接来源分桶，缓存有界且不记录原始IP或凭据。</summary>
public sealed class AgentPublicRateLimiter(TimeProvider? timeProvider = null)
{
    private sealed record Window(DateTimeOffset StartedAt, int Count);
    private readonly Dictionary<string, Window> _windows = new();
    private readonly object _sync = new();
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public void Require(string category, string source, int maximum)
    {
        var now = _clock.GetUtcNow(); var key = category + ":" + PasswordHasher.Digest(source);
        lock (_sync)
        {
            foreach (var expired in _windows.Where(p => p.Value.StartedAt.AddMinutes(1) <= now).Select(p => p.Key).ToArray()) _windows.Remove(expired);
            if (!_windows.TryGetValue(key, out var window))
            {
                if (_windows.Count >= 5000) throw new AgentOAuthException("slow_down", "授权服务繁忙，请稍后重试。", 429);
                window = new(now, 0);
            }
            if (window.Count >= maximum) throw new AgentOAuthException("slow_down", "授权请求过于频繁，请稍后重试。", 429);
            _windows[key] = window with { Count = window.Count + 1 };
        }
    }
}
