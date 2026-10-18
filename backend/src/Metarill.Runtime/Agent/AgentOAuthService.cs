using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Agent;

/// <summary>设备流绑定人工批准的真实账号、项目和固定CLI客户端，不授予客户端自报权限。</summary>
public sealed partial class AgentOAuthService(SqlRowStore store, MetadataRegistry registry, IIdGenerator ids, AgentOAuthOptions options, TimeProvider? timeProvider = null)
{
    public const string ClientId = "metarill-agent-cli";
    public const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private sealed record TokenResult(Dictionary<string, object?>? Payload, AgentOAuthException? Error);
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)] private static partial Regex TokenPattern();
    [GeneratedRegex("^[A-HJ-NP-Z2-9]{8}$", RegexOptions.CultureInvariant)] private static partial Regex UserCodePattern();

    /// <summary>返回原始OAuth字典，长码仅在签发响应出现，数据库只保存摘要。</summary>
    public async Task<Dictionary<string, object?>> StartDeviceAsync(string clientId, string? clientName, CancellationToken cancellationToken = default)
    {
        RequireClient(clientId); ValidateOptions();
        var name = string.IsNullOrWhiteSpace(clientName) ? "Metarill Agent CLI" : clientName.Trim();
        if (name.Length > 100 || name.Any(char.IsControl)) throw Error("invalid_request", "客户端名称无效。");
        var now = _clock.GetUtcNow();
        var pending = await store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["status"] = "pending" }, cancellationToken);
        if (pending.Count(r => Date(r, "expires_at") > now.UtcDateTime) >= 1000) throw Error("temporarily_unavailable", "待确认设备过多，请稍后重试。", 503);
        var deviceCode = PasswordHasher.Token();
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = string.Concat(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
        await store.InsertAsync("sys_agent_grant", new Dictionary<string, object?>
        {
            ["id"] = ids.NextId(), ["project_id"] = long.Parse(registry.Platform.YzProjectId, CultureInfo.InvariantCulture),
            ["device_code_hash"] = PasswordHasher.Digest(deviceCode), ["user_code"] = code, ["client_id"] = ClientId,
            ["client_name"] = name, ["status"] = "pending", ["expires_at"] = now.AddSeconds(options.DeviceCodeSeconds).UtcDateTime,
            ["poll_interval_seconds"] = options.PollingSeconds, ["insert_time"] = now.UtcDateTime
        }, cancellationToken);
        var uri = options.VerificationUri;
        return new() { ["device_code"] = deviceCode, ["user_code"] = code, ["verification_uri"] = uri,
            ["verification_uri_complete"] = uri + (uri.Contains('?') ? "&" : "?") + "user_code=" + code,
            ["expires_in"] = options.DeviceCodeSeconds, ["interval"] = options.PollingSeconds };
    }

    /// <summary>网页只看到人工码、客户端及当前项目，不能读取设备长码或摘要。</summary>
    public async Task<AgentDeviceView> ViewDeviceAsync(string userCode, AuthenticatedIdentity identity, CancellationToken cancellationToken = default)
    {
        await RequireWebAsync(identity, true, cancellationToken);
        var code = NormalizeCode(userCode);
        var row = (await store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["user_code"] = code }, cancellationToken)).SingleOrDefault();
        var project = identity.Actor.ProjectId;
        if (row is null || Date(row, "expires_at") <= _clock.GetUtcNow().UtcDateTime) return new(code, "EXPIRED", "", project, await ProjectNameAsync(project, cancellationToken), 0, PermissionNotice);
        if (SqlRowStore.Text(row, "status") != "pending" && (SqlRowStore.Id(row, "account_id") != identity.Actor.AccountId || SqlRowStore.Text(row, "project_id") != project)) throw new UnauthorizedAccessException("该授权不属于当前账号项目。");
        return new(code, SqlRowStore.Text(row, "status").ToUpperInvariant(), SqlRowStore.Text(row, "client_name"), project, await ProjectNameAsync(project, cancellationToken),
            new DateTimeOffset(DateTime.SpecifyKind(Date(row, "expires_at"), DateTimeKind.Utc)).ToUnixTimeMilliseconds(), PermissionNotice);
    }

    /// <summary>人工决定只接受真实web会话和当前项目管理员，批准后项目冻结。</summary>
    public Task ApproveAsync(string userCode, bool approve, AuthenticatedIdentity identity, CancellationToken cancellationToken = default) => store.Session.InTransactionAsync(async () =>
    {
        await RequireWebAsync(identity, true, cancellationToken);
        var row = (await store.ReadForUpdateAsync("sys_agent_grant", new Dictionary<string, object?> { ["user_code"] = NormalizeCode(userCode) }, cancellationToken)).SingleOrDefault();
        if (row is null || Date(row, "expires_at") <= _clock.GetUtcNow().UtcDateTime || SqlRowStore.Text(row, "status") != "pending") throw new InvalidOperationException("授权已处理或过期，请重新发起。");
        await ProjectNameAsync(identity.Actor.ProjectId, cancellationToken);
        await store.UpdateAsync("sys_agent_grant", SqlRowStore.Id(row), new Dictionary<string, object?>
        { ["status"] = approve ? "approved" : "denied", ["account_id"] = identity.Actor.AccountId,
            ["project_id"] = long.Parse(identity.Actor.ProjectId, CultureInfo.InvariantCulture), ["refresh_expires_at"] = approve ? _clock.GetUtcNow().AddSeconds(options.GrantSeconds).UtcDateTime : null,
            ["update_time"] = _clock.GetUtcNow().UtcDateTime }, cancellationToken);
        return true;
    }, cancellationToken);

    /// <summary>轮询更新须在错误返回前提交，否则pending/slow_down会被事务回滚绕过。</summary>
    public async Task<Dictionary<string, object?>> TokenAsync(string clientId, string grantType, string? deviceCode, string? refreshToken, CancellationToken cancellationToken = default)
    {
        RequireClient(clientId); ValidateOptions();
        var result = grantType switch
        {
            DeviceGrant => await ExchangeAsync(deviceCode, cancellationToken),
            "refresh_token" => await RefreshAsync(refreshToken, cancellationToken),
            _ => throw Error("unsupported_grant_type", "不支持当前授权类型。")
        };
        if (result.Error is not null) throw result.Error;
        return result.Payload!;
    }

    /// <summary>短码只能换取一次访问与刷新凭据，频率上调和消费由数据库行锁保证。</summary>
    private Task<TokenResult> ExchangeAsync(string? token, CancellationToken cancellationToken)
    {
        RequireToken(token);
        return store.Session.InTransactionAsync(async () =>
        {
            var row = (await store.ReadForUpdateAsync("sys_agent_grant", new Dictionary<string, object?> { ["device_code_hash"] = PasswordHasher.Digest(token!) }, cancellationToken)).SingleOrDefault();
            var now = _clock.GetUtcNow().UtcDateTime;
            if (row is null || Date(row, "expires_at") <= now) return Failure("expired_token", "设备授权已过期。");
            if (SqlRowStore.Text(row, "client_id") != ClientId || row.GetValueOrDefault("device_redeemed_at") is not null) return Failure("invalid_grant", "设备授权已消费或不属于当前客户端。");
            var interval = row.GetValueOrDefault("poll_interval_seconds") is null ? options.PollingSeconds : Convert.ToInt32(row["poll_interval_seconds"], CultureInfo.InvariantCulture);
            var slow = row.GetValueOrDefault("last_poll_at") is not null && Date(row, "last_poll_at").AddSeconds(interval) > now;
            await store.UpdateAsync("sys_agent_grant", SqlRowStore.Id(row), new Dictionary<string, object?> { ["last_poll_at"] = now, ["poll_interval_seconds"] = slow ? Math.Min(interval + 5, 60) : interval }, cancellationToken);
            if (slow) return Failure("slow_down", "轮询过快，请增加五秒间隔。");
            var status = SqlRowStore.Text(row, "status");
            if (status == "pending") return Failure("authorization_pending", "等待网页登录人工确认。");
            if (status != "approved") return Failure("access_denied", "用户拒绝或授权已撤销。");
            if (!await ValidAccountAsync(SqlRowStore.Id(row, "account_id"), cancellationToken)) return Failure("access_denied", "批准授权的账号已失效。");
            var access = PasswordHasher.Token(); var refresh = PasswordHasher.Token();
            await store.UpdateAsync("sys_agent_grant", SqlRowStore.Id(row), new Dictionary<string, object?> { ["device_redeemed_at"] = now, ["refresh_hash"] = PasswordHasher.Digest(refresh) }, cancellationToken);
            await InsertAccessAsync(row, access, cancellationToken);
            return Success(row, access, refresh);
        }, cancellationToken);
    }

    /// <summary>刷新轮转不延长原授权期限；消费过的刷新凭据再次使用会撤销对应授权。</summary>
    private Task<TokenResult> RefreshAsync(string? token, CancellationToken cancellationToken)
    {
        RequireToken(token);
        return store.Session.InTransactionAsync(async () =>
        {
            var hash = PasswordHasher.Digest(token!);
            var row = (await store.ReadForUpdateAsync("sys_agent_grant", new Dictionary<string, object?> { ["refresh_hash"] = hash }, cancellationToken)).SingleOrDefault();
            if (row is null)
            {
                var used = (await store.ReadForUpdateAsync("sys_session", new Dictionary<string, object?> { ["token_hash"] = hash, ["kind"] = "agent_refresh_used" }, cancellationToken)).SingleOrDefault();
                if (used is not null) await RevokeGrantAsync(SqlRowStore.Id(used, "grant_id"), cancellationToken);
                return Failure("invalid_grant", "刷新凭据已失效或被重复使用。");
            }
            if (SqlRowStore.Text(row, "client_id") != ClientId || SqlRowStore.Text(row, "status") != "approved" || Date(row, "refresh_expires_at") <= _clock.GetUtcNow().UtcDateTime || !await ValidAccountAsync(SqlRowStore.Id(row, "account_id"), cancellationToken))
                return Failure("invalid_grant", "授权已失效，请重新登录。");
            await InvalidateAccessAsync(SqlRowStore.Id(row), cancellationToken);
            await store.InsertAsync("sys_session", new Dictionary<string, object?> { ["id"] = ids.NextId(), ["token_hash"] = hash,
                ["account_id"] = SqlRowStore.Id(row, "account_id"), ["kind"] = "agent_refresh_used", ["grant_id"] = SqlRowStore.Id(row),
                ["project_id"] = SqlRowStore.Id(row, "project_id"), ["expires_at"] = Date(row, "refresh_expires_at"), ["insert_time"] = _clock.GetUtcNow().UtcDateTime }, cancellationToken);
            var access = PasswordHasher.Token(); var refresh = PasswordHasher.Token();
            await store.UpdateAsync("sys_agent_grant", SqlRowStore.Id(row), new Dictionary<string, object?> { ["refresh_hash"] = PasswordHasher.Digest(refresh), ["update_time"] = _clock.GetUtcNow().UtcDateTime }, cancellationToken);
            await InsertAccessAsync(row, access, cancellationToken);
            return Success(row, access, refresh);
        }, cancellationToken);
    }

    /// <summary>CLI只能撤销本次授权，网页登录可以撤销当前账号当前项目的授权。</summary>
    public async Task RevokeAsync(long? grantId, AgentRequestIdentity requestIdentity, CancellationToken cancellationToken = default)
    {
        var identity = requestIdentity.Identity;
        if (requestIdentity.Kind == "agent") { if (grantId is not null && grantId != requestIdentity.GrantId) throw new UnauthorizedAccessException("CLI只能撤销自身授权。"); grantId = requestIdentity.GrantId; }
        else await RequireWebAsync(identity, false, cancellationToken);
        if (grantId is null) throw new InvalidOperationException("必须指定授权标识。");
        await store.Session.InTransactionAsync(async () =>
        {
            var row = (await store.ReadForUpdateAsync("sys_agent_grant", new Dictionary<string, object?> { ["id"] = grantId }, cancellationToken)).SingleOrDefault();
            if (row is null || SqlRowStore.Id(row, "account_id") != identity.Actor.AccountId || SqlRowStore.Text(row, "project_id") != identity.Actor.ProjectId) throw new UnauthorizedAccessException("授权不属于当前账号项目。");
            await RevokeGrantAsync(grantId.Value, cancellationToken); return true;
        }, cancellationToken);
    }

    /// <summary>网页授权列表不回显令牌、摘要或设备长码。</summary>
    public async Task<IReadOnlyList<object>> GrantsAsync(AuthenticatedIdentity identity, CancellationToken cancellationToken = default)
    {
        await RequireWebAsync(identity, false, cancellationToken);
        return (await store.ReadAsync("sys_agent_grant", new Dictionary<string, object?> { ["account_id"] = identity.Actor.AccountId, ["project_id"] = long.Parse(identity.Actor.ProjectId, CultureInfo.InvariantCulture), ["status"] = "approved" }, cancellationToken))
            .Where(r => Date(r, "refresh_expires_at") > _clock.GetUtcNow().UtcDateTime).Select(r => (object)new { id = SqlRowStore.Id(r), clientName = SqlRowStore.Text(r, "client_name"), createdAt = Date(r, "insert_time"), expiresAt = Date(r, "refresh_expires_at") }).ToArray();
    }

    /// <summary>服务入口再次核对真实web会话和当前账号角色，不能只相信调用者构造的Actor。</summary>
    private async Task RequireWebAsync(AuthenticatedIdentity identity, bool administrator, CancellationToken cancellationToken)
    {
        var session = (await store.ReadAsync("sys_session", new Dictionary<string, object?> { ["id"] = identity.SessionId }, cancellationToken)).SingleOrDefault();
        if (session is null || SqlRowStore.Text(session, "kind") != "web" || SqlRowStore.Id(session, "account_id") != identity.Actor.AccountId || Date(session, "expires_at") <= _clock.GetUtcNow().UtcDateTime || identity.GrantId is not null)
            throw new UnauthorizedAccessException("设备授权管理需要真实网页登录会话。");
        var account = (await store.ReadAsync("yz_manager", new Dictionary<string, object?> { ["id"] = identity.Actor.AccountId }, cancellationToken)).SingleOrDefault();
        if (account is null || !AuthenticationService.AccountActive(account)) throw new UnauthorizedAccessException("当前账号已失效。");
        if (administrator)
        {
            var roles = ParseIds(SqlRowStore.Text(account, "yz_role_ids"));
            var current = await store.ReadAsync("yz_role", cancellationToken: cancellationToken);
            if (!current.Any(role => roles.Contains(SqlRowStore.Text(role, "id")) && SqlRowStore.Text(role, "is_super") == "1")) throw new UnauthorizedAccessException("批准Agent授权需要当前项目管理员权限。");
        }
    }

    /// <summary>签发短期访问会话，只有摘要进入现有会话表。</summary>
    private Task InsertAccessAsync(IReadOnlyDictionary<string, object?> grant, string access, CancellationToken cancellationToken) => store.InsertAsync("sys_session", new Dictionary<string, object?>
    { ["id"] = ids.NextId(), ["account_id"] = SqlRowStore.Id(grant, "account_id"), ["token_hash"] = PasswordHasher.Digest(access), ["kind"] = "agent", ["grant_id"] = SqlRowStore.Id(grant),
        ["project_id"] = SqlRowStore.Id(grant, "project_id"), ["expires_at"] = AccessExpiry(grant), ["insert_time"] = _clock.GetUtcNow().UtcDateTime }, cancellationToken);
    /// <summary>撤销授权和所有当前访问会话共享同一数据库事务。</summary>
    private async Task RevokeGrantAsync(long id, CancellationToken token) { await store.UpdateAsync("sys_agent_grant", id, new Dictionary<string, object?> { ["status"] = "revoked", ["refresh_hash"] = null }, token); await InvalidateAccessAsync(id, token); }
    /// <summary>刷新或撤权后旧访问令牌立即失效，保留已消费刷新摘要供重放检测。</summary>
    private async Task InvalidateAccessAsync(long id, CancellationToken token)
    { foreach (var session in await store.ReadForUpdateAsync("sys_session", new Dictionary<string, object?> { ["grant_id"] = id, ["kind"] = "agent" }, token)) await store.UpdateAsync("sys_session", SqlRowStore.Id(session), new Dictionary<string, object?> { ["delete_no"] = _clock.GetUtcNow().UtcDateTime }, token); }
    /// <summary>账号有效期每次签发重新读取，不复制旧Java中不同的启用字段语义。</summary>
    private async Task<bool> ValidAccountAsync(long id, CancellationToken token) { var account = (await store.ReadAsync("yz_manager", new Dictionary<string, object?> { ["id"] = id }, token)).SingleOrDefault(); return account is not null && AuthenticationService.AccountActive(account); }
    /// <summary>项目名只来自已登记的服务端记录。</summary>
    private async Task<string> ProjectNameAsync(string project, CancellationToken token) { var row = (await store.ReadAsync("yz_project", new Dictionary<string, object?> { ["id"] = long.Parse(project, CultureInfo.InvariantCulture) }, token)).SingleOrDefault() ?? throw new UnauthorizedAccessException("项目未登记。"); return SqlRowStore.Text(row, "name"); }
    /// <summary>OAuth响应使用标准snake_case键，不经过业务R封装。</summary>
    private TokenResult Success(IReadOnlyDictionary<string, object?> row, string access, string refresh) => new(new() { ["access_token"] = access, ["refresh_token"] = refresh, ["token_type"] = "Bearer", ["expires_in"] = Math.Max(1, (int)(AccessExpiry(row) - _clock.GetUtcNow().UtcDateTime).TotalSeconds), ["project_id"] = SqlRowStore.Text(row, "project_id") }, null);
    private DateTime AccessExpiry(IReadOnlyDictionary<string, object?> row) { var maximum = _clock.GetUtcNow().AddSeconds(options.AccessTokenSeconds).UtcDateTime; var end = Date(row, "refresh_expires_at"); return end < maximum ? end : maximum; }
    private static TokenResult Failure(string code, string message) => new(null, Error(code, message));
    private static AgentOAuthException Error(string code, string message, int status = 400) => new(code, message, status);
    private static DateTime Date(IReadOnlyDictionary<string, object?> row, string name) => row.GetValueOrDefault(name) is { } value ? Convert.ToDateTime(value, CultureInfo.InvariantCulture) : DateTime.MinValue;
    private static HashSet<string> ParseIds(string source) { try { return JsonSerializer.Deserialize<List<JsonElement>>(source)?.Select(x => x.ToString()).ToHashSet() ?? []; } catch (JsonException) { return []; } }
    private static void RequireClient(string client) { if (client != ClientId) throw Error("invalid_client", "未识别的Agent客户端。"); }
    private static void RequireToken(string? token) { if (token is null || !TokenPattern().IsMatch(token)) throw Error("invalid_request", "授权凭据格式无效。"); }
    private static string NormalizeCode(string value) { var code = (value ?? "").Trim().Replace("-", "").ToUpperInvariant(); if (!UserCodePattern().IsMatch(code)) throw new InvalidOperationException("用户码格式无效。"); return code; }
    private void ValidateOptions()
    {
        if (options.DeviceCodeSeconds is < 60 or > 1800 || options.PollingSeconds is < 1 or > 30 || options.AccessTokenSeconds is < 60 or > 3600 || options.GrantSeconds is < 3600 or > 90 * 24 * 3600) throw new InvalidOperationException("Agent授权有效期配置无效。");
        if (!Uri.TryCreate(options.VerificationUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback) || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("授权地址必须是服务端可信HTTPS地址，本机开发允许HTTP回环地址。");
    }
    private const string PermissionNotice = "此设备以当前账号已有权限访问所选项目；写操作仍须逐次明确确认，可随时撤销。";
}
