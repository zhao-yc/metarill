using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>首次安装验收只使用原HTTP/R协议；失败提示不回显请求、响应或认证秘密。</summary>
internal static class FreshWebHttp
{
    /// <summary>同时核对HTTP状态和R业务码，错误正文不进入异常消息。</summary>
    internal static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string route, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null) request.Content = body as HttpContent ?? JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Assert.True(response.StatusCode == expected, "独立Web请求HTTP状态不符，正文与凭据不回显。");
        JsonElement json;
        try { json = await response.Content.ReadFromJsonAsync<JsonElement>(); }
        catch (JsonException) { throw new InvalidOperationException("独立Web响应不是统一JSON，正文不回显。"); }
        Assert.True(json.ValueKind == JsonValueKind.Object && json.TryGetProperty("code", out var code) && code.TryGetInt32(out var number) && number == (int)expected, "独立Web响应业务码不符，正文与凭据不回显。");
        return json;
    }
    /// <summary>原form与JSON登录只取可信回执令牌；重复登录替换header，不累计旧会话头。</summary>
    internal static async Task<string> LoginAsync(HttpClient client, string username, string password, bool form = false)
    {
        object body = form ? new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = username, ["password"] = password }) : new { username, password };
        var response = await SendAsync(client, HttpMethod.Post, "/api/admin/sys/yz_manager/login", body);
        var token = response.GetProperty("map").GetProperty("loginStateId").GetString();
        Assert.True(token is { Length: 64 } && token.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f'), "正式Web登录必须返回规范令牌。");
        client.DefaultRequestHeaders.Remove("loginStateId"); client.DefaultRequestHeaders.Add("loginStateId", token); return token!;
    }
}

/// <summary>仅替换HTTP传输的离线合同，不启动Main、端口或数据库，不计为真实Web验收。</summary>
public sealed class FreshWebHttpTests
{
    /// <summary>原form/JSON及长字符串ID保留，重复登录只留单个有效令牌头。</summary>
    [Fact]
    public async Task Original_login_shapes_replace_header_and_keep_long_string_ids()
    {
        var calls = 0; var token = new string('a', 64); const string longId = "9007199254740993";
        using var handler = new Reply(async request =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("/api/admin/sys/yz_manager/login", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            if (calls == 1) { Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType); Assert.Equal("username=only-owner&password=only-password", body); }
            else { Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType); using var parsed = JsonDocument.Parse(body); Assert.Equal(2, parsed.RootElement.EnumerateObject().Count()); Assert.Equal("only-owner", parsed.RootElement.GetProperty("username").GetString()); Assert.Equal("only-password", parsed.RootElement.GetProperty("password").GetString()); }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { code = 200, data = new { id = longId }, map = new { loginStateId = token } }) };
        });
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1") }; client.DefaultRequestHeaders.Add("loginStateId", new string('b', 64));
        Assert.Equal(token, await FreshWebHttp.LoginAsync(client, "only-owner", "only-password", form: true)); Assert.Equal(token, await FreshWebHttp.LoginAsync(client, "only-owner", "only-password"));
        Assert.Equal(new[] { token }, client.DefaultRequestHeaders.GetValues("loginStateId")); Assert.Equal(2, calls);
        var value = await FreshWebHttp.SendAsync(client, HttpMethod.Post, "/api/admin/sys/yz_manager/login", new { username = "only-owner", password = "only-password" }); Assert.Equal(longId, value.GetProperty("data").GetProperty("id").GetString());
    }
    /// <summary>HTTP/R码分别满足，损坏JSON也失败，不追加响应正文到断言。</summary>
    [Theory]
    [InlineData("http")] [InlineData("code")] [InlineData("json")]
    public async Task Unexpected_http_or_R_code_fails_without_echoing_response(string kind)
    {
        const string marker = "response-secret-must-not-appear";
        using var handler = new Reply(_ => Task.FromResult(new HttpResponseMessage(kind == "http" ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent(kind == "json" ? marker : "{\"code\":500,\"msg\":\"" + marker + "\"}", Encoding.UTF8, "application/json") }));
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1") }; var error = await Assert.ThrowsAnyAsync<Exception>(() => FreshWebHttp.SendAsync(client, HttpMethod.Get, "/api/contract")); Assert.DoesNotContain(marker, error.ToString());
    }
    /// <summary>拒绝分支也要求真实R码，不能把任意403当作业务拒绝已验证。</summary>
    [Fact]
    public async Task Expected_rejection_requires_matching_R_code()
    {
        using var handler = new Reply(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code = 403, data = (object?)null }) }));
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:1") }; Assert.Equal(403, (await FreshWebHttp.SendAsync(client, HttpMethod.Post, "/api/contract", new { }, HttpStatusCode.Forbidden)).GetProperty("code").GetInt32());
    }
    /// <summary>只接管本次HttpClient，不监听真实网络。</summary>
    private sealed class Reply(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
