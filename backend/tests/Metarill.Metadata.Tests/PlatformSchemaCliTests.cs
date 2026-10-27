using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Cli;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>通过实际HTTP消息核对CLI会话、项目、只读响应与拒绝边界，不需要数据库。</summary>
public sealed class PlatformSchemaCliTests
{
    private const string Session = "test-session-012345678901234567890123456789";
    /// <summary>统一构造原协议的规范参数，单独测试附加或替换的输入。</summary>
    private static string[] Args(params string[] tail) => new[] { "schema", "inspect", "--server", "https://platform.example/api", "--project-id", "123" }.Concat(tail).ToArray();
    /// <summary>只读成功封套固定项目和执行标记，不包含会话或连接配置。</summary>
    private static string Envelope(string project = "123", bool executable = false) => JsonSerializer.Serialize(new { code = 200, data = new { projectId = project, executable } });

    /// <summary>项目仅进入原sysParams头，受管表仅进入原请求体，凭据不进入URL或JSON输出。</summary>
    [Theory]
    [InlineData("inspect", true)]
    [InlineData("inspect", false)]
    [InlineData("catalog", false)]
    public async Task Commands_use_existing_web_session_and_fixed_readonly_endpoints(string command, bool single)
    {
        var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://platform.example/api/admin/sys/database/" + command, request.RequestUri!.AbsoluteUri);
            Assert.Equal(Session, Assert.Single(request.Headers.GetValues("loginStateId")));
            Assert.Null(request.Headers.Authorization);
            using var selection = JsonDocument.Parse(Uri.UnescapeDataString(Assert.Single(request.Headers.GetValues("sysParams"))));
            Assert.Equal("123", selection.RootElement.GetProperty("yzProjectId").GetString());
            Assert.Equal(single ? "{\"tableId\":\"456\"}" : "{}", await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent(Envelope(), Encoding.UTF8, "application/json") };
        });
        using var client = new HttpClient(handler); using var output = new StringWriter();
        var args = Args(single ? ["--table-id", "456", "--token-env", "PRIVATE_SESSION"] : []); args[1] = command;
        await PlatformSchemaCommand.ExecuteAsync(args, client, variable => { Assert.Equal(single ? "PRIVATE_SESSION" : "METARILL_LOGIN_STATE_ID", variable); return Session; }, output);
        Assert.Equal(1, handler.Count); Assert.DoesNotContain(Session, output.ToString()); Assert.DoesNotContain("code", output.ToString());
    }

    /// <summary>危险地址、直接凭据、未知/重复参数和非法标识必须在任何网络调用前拒绝。</summary>
    [Theory]
    [InlineData("server", "http://platform.example")]
    [InlineData("server", "https://user:password@platform.example")]
    [InlineData("server", "https://platform.example/api?token=secret")]
    [InlineData("server", "https://platform.example/#secret")]
    [InlineData("server", "https://platform.example/custom")]
    [InlineData("server", "file:///api")]
    [InlineData("project-id", "00123")]
    [InlineData("project-id", "0")]
    [InlineData("project-id", "123/other")]
    [InlineData("project-id", "9223372036854775808")]
    [InlineData("token-env", "INVALID-NAME")]
    [InlineData("table-id", "1;DROP TABLE users")]
    [InlineData("token", "secret")]
    [InlineData("sql", "SELECT 1")]
    [InlineData("connection", "secret")]
    public async Task Invalid_options_never_send_credentials(string option, string value)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("不应发出请求")); using var client = new HttpClient(handler);
        var args = Args(); var index = Array.IndexOf(args, "--" + option);
        if (index >= 0) args[index + 1] = value; else args = args.Concat(["--" + option, value]).ToArray();
        await Assert.ThrowsAsync<ArgumentException>(() => PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, TextWriter.Null));
        Assert.Equal(0, handler.Count);
    }

    /// <summary>参数错误或空会话不能靠一次失败请求代替本地校验。</summary>
    [Fact]
    public async Task Duplicate_missing_catalog_table_and_invalid_sessions_are_rejected_locally()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("不应发出请求")); using var client = new HttpClient(handler);
        foreach (var args in new[] { Args("--server", "https://other.example"), Args("--table-id"), Args("--project-id", "123"), new[] { "schema", "catalog", "--server", "http://localhost", "--project-id", "123", "--table-id", "456" }, new[] { "schema", "apply" } })
            await Assert.ThrowsAsync<ArgumentException>(() => PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, TextWriter.Null));
        foreach (var session in new[] { (string?)null, "short", new string('a', 201), new string('a', 32) + "\r\nX-Forged:true", new string('a', 32) + " " })
            await Assert.ThrowsAsync<ArgumentException>(() => PlatformSchemaCommand.ExecuteAsync(Args(), client, _ => session, TextWriter.Null));
        Assert.Equal(0, handler.Count);
    }

    /// <summary>原HTTP200错误、跨项目、可执行计划与重定向都不能输出为成功结果。</summary>
    [Theory]
    [InlineData(200, "{\"code\":500,\"msg\":\"机密错误正文\"}")]
    [InlineData(200, "{\"code\":200,\"data\":{\"projectId\":\"999\",\"executable\":false}}")]
    [InlineData(200, "{\"code\":200,\"data\":{\"projectId\":\"123\",\"executable\":true}}")]
    [InlineData(200, "{\"code\":200,\"data\":null}")]
    [InlineData(302, "机密错误正文")]
    [InlineData(401, "机密错误正文")]
    [InlineData(500, "机密错误正文")]
    public async Task Refused_responses_emit_no_server_content(int status, string body)
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        using var output = new StringWriter();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSchemaCommand.ExecuteAsync(Args(), client, _ => Session, output));
        Assert.DoesNotContain("机密", exception.Message); Assert.Equal("", output.ToString());
    }

    /// <summary>即使服务端不提供长度头，流式响应也必须受总字节预算约束。</summary>
    [Fact]
    public async Task Chunked_oversized_response_is_rejected_without_output()
    {
        using var client = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new byte[8 * 1024 * 1024 + 1]) };
            Assert.Null(response.Content.Headers.ContentLength);
            return Task.FromResult(response);
        }));
        using var output = new StringWriter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSchemaCommand.ExecuteAsync(Args(), client, _ => Session, output));
        Assert.Equal("", output.ToString());
    }

    /// <summary>本机开发地址可用HTTP，仍使用固定接口而不传数据库连接信息。</summary>
    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080/api/")]
    [InlineData("http://[::1]:8080/api")]
    public async Task Loopback_development_addresses_are_supported(string server)
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope()) })));
        var args = Args(); args[3] = server;
        await PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, TextWriter.Null);
    }

    /// <summary>草案命令只提交明确表名列表，且成功响应必须保留不可导入和符号ID标记。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_preview_uses_fixed_route_and_rejects_import_authority(bool forged)
    {
        var handler = new Handler(async request =>
        {
            Assert.EndsWith("/api/admin/sys/database/import/preview", request.RequestUri!.AbsoluteUri);
            Assert.Equal("{\"tableNames\":[\"first_table\",\"second_table\"]}", await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { code = 200, data = new { projectId = "123", executable = false, canImport = forged, draft = new { usesDraftIdentifiers = true } } })) };
        });
        using var client = new HttpClient(handler); using var output = new StringWriter(); var args = Args("--tables", "first_table,second_table"); args[1] = "import-preview";
        if (forged) { await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, output)); Assert.Equal("", output.ToString()); }
        else await PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, output);
        Assert.Equal(1, handler.Count);
    }

    /// <summary>草案参数不能与旧受管表选择互换，SQL/跨库名称及重复表名先于请求拒绝。</summary>
    [Theory]
    [InlineData("first,first")]
    [InlineData("first,FIRST")]
    [InlineData("other_database.table")]
    [InlineData("table;DROP TABLE other")]
    [InlineData("")]
    public async Task Invalid_import_preview_selections_are_rejected_locally(string tables)
    {
        var handler = new Handler(_ => throw new InvalidOperationException("不应发请求")); using var client = new HttpClient(handler);
        var args = Args("--tables", tables); args[1] = "import-preview";
        await Assert.ThrowsAsync<ArgumentException>(() => PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, TextWriter.Null)); Assert.Equal(0, handler.Count);
    }

    /// <summary>准备、确认与查询分别使用固定入口；确认只能提交计划和摘要，不能夹带任意模型或SQL。</summary>
    [Theory]
    [InlineData("import-prepare")]
    [InlineData("import-apply")]
    [InlineData("import-status")]
    public async Task Metadata_import_commands_use_explicit_plan_protocol(string command)
    {
        var plan = new string('a', 32); var confirm = new string('b', 64);
        var handler = new Handler(async request =>
        {
            Assert.EndsWith("/api/admin/sys/database/import/" + command[7..], request.RequestUri!.AbsoluteUri);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Equal(command == "import-prepare" ? "{\"tableNames\":[\"first_table\"]}" : "{\"planId\":\"" + plan + "\",\"confirmSha256\":\"" + confirm + "\"}", body);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { code = 200, data = new { projectId = "123", executable = false, executesDdl = false, canSaveMetadata = true, planId = plan, confirmSha256 = confirm, configurationSaved = true, state = "saved" } })) };
        });
        using var client = new HttpClient(handler); var args = Args(command == "import-prepare" ? ["--tables", "first_table"] : ["--plan-id", plan, "--confirm", confirm]); args[1] = command;
        await PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, TextWriter.Null); Assert.Equal(1, handler.Count);
    }

    /// <summary>错误摘要/错误范围在发请求前拒绝，不允许直接提供连接、SQL或待写元数据。</summary>
    [Fact]
    public async Task Metadata_apply_rejects_missing_or_mixed_scope_arguments()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("不应发请求")); using var client = new HttpClient(handler);
        foreach (var tail in new[] { new[] { "--plan-id", new string('a', 32) }, new[] { "--plan-id", "bad", "--confirm", new string('b', 64) }, new[] { "--plan-id", new string('a', 32), "--confirm", new string('b', 64), "--tables", "other_table" } })
        { var args = Args(tail); args[1] = "import-apply"; await Assert.ThrowsAsync<ArgumentException>(() => PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, TextWriter.Null)); }
        Assert.Equal(0, handler.Count);
    }

    /// <summary>跨计划回执或把未知写成未保存必须拒绝，不能给重试提供错误证据。</summary>
    [Theory]
    [InlineData("import-apply", "other-plan", "saved", true)]
    [InlineData("import-status", "other-plan", "unknown", null)]
    [InlineData("import-status", "same-plan", "unknown", false)]
    [InlineData("import-status", "same-plan", "saved", null)]
    public async Task Metadata_receipt_cannot_change_plan_or_claim_unknown_as_not_saved(string command, string planKind, string state, bool? configurationSaved)
    {
        var plan = new string('a', 32); var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { code = 200, data = new { projectId = "123", executable = false, executesDdl = false, planId = planKind == "same-plan" ? plan : new string('b', 32), state, configurationSaved } })) }));
        using var client = new HttpClient(handler); using var output = new StringWriter(); var args = Args("--plan-id", plan, "--confirm", new string('b', 64)); args[1] = command;
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlatformSchemaCommand.ExecuteAsync(args, client, _ => Session, output)); Assert.Equal("", output.ToString()); Assert.Equal(1, handler.Count);
    }

    /// <summary>显式不提供长度头，避免MemoryStream的可计算长度使测试只覆盖预检查。</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        /// <summary>真实响应流没有Content-Length时，仍必须进入产品的逐块读取预算。</summary>
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        /// <summary>直接提供流，避免测试内容在HttpContent层预缓冲而改变读取路径。</summary>
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        /// <summary>保留HttpContent的写入合同，网络模拟无需拼装HTTP正文。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Count { get; private set; }
        /// <summary>记录真实发出的HTTP消息，确保拒绝检查发生于发送之前。</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Count++; return send(request); }
    }
}
