using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Metarill.Cli;

/// <summary>在线结构和明确确认的配置导入复用平台身份；不会数据库直连或自动应用未经确认的模型。</summary>
internal static class PlatformSchemaCommand
{
    private const int ResponseLimit = 8 * 1024 * 1024;

    /// <summary>禁止重定向转发凭据，远程只允许标准TLS；会话仅从指定环境变量读取。</summary>
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, MaxResponseHeadersLength = 16 };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            // ResponseHeadersRead之后HttpClient超时不再覆盖响应流，因此整次命令另设同一截止时间。
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await ExecuteAsync(args, client, Environment.GetEnvironmentVariable, Console.Out, deadline.Token);
            return 0;
        }
        catch (ArgumentException exception) { Console.Error.WriteLine(exception.Message); return 1; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or IOException)
        {
            // 不回显服务器正文、URL或异常链，以免将会话、连接配置写入终端日志。
            Console.Error.WriteLine(args.Length > 1 && args[1] is "import-apply" or "import-status"
                ? "导入未获得可信回执：配置可能已保存，请查询同一计划状态并核对实际配置；不能据此认定未写入或自动重试。"
                : "平台结构查询失败：请检查会话、项目权限、服务地址及服务端日志；本次未执行结构或元数据写入。");
            return 1;
        }
    }

    /// <summary>参数先完整校验再发请求；项目选择必须由服务器再次认证，不能等价于授权。</summary>
    internal static async Task ExecuteAsync(string[] args, HttpClient client, Func<string, string?> environment, TextWriter output, CancellationToken token = default)
    {
        if (args.Length < 2 || args[0] != "schema" || args[1] is not ("inspect" or "catalog" or "import-preview" or "import-prepare" or "import-apply" or "import-status"))
            throw new ArgumentException("未知结构命令，请查看schema相关帮助。");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var special = args[1] switch { "inspect" => new[] { "--table-id" }, "catalog" => Array.Empty<string>(), "import-preview" or "import-prepare" => ["--tables"], _ => ["--plan-id", "--confirm"] };
        for (var index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                args[index] is not ("--server" or "--project-id" or "--token-env") && !special.Contains(args[index], StringComparer.Ordinal) || !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("结构命令参数未知、重复或缺值；会话不能直接放入命令行。");
        }
        if (!options.TryGetValue("--server", out var server) || !options.TryGetValue("--project-id", out var project) ||
            args[1] is "import-preview" or "import-prepare" && !options.ContainsKey("--tables") ||
            args[1] is "import-apply" or "import-status" && (!options.ContainsKey("--plan-id") || !options.ContainsKey("--confirm")))
            throw new ArgumentException("结构命令缺少必需参数，请查看帮助。");
        if (options.TryGetValue("--plan-id", out var planId) && (!Regex.IsMatch(planId, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(options["--confirm"], "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))) throw new ArgumentException("计划ID或确认摘要格式无效。");
        RequireId(project);
        if (options.TryGetValue("--table-id", out var table)) RequireId(table);
        string[]? names = null;
        if (options.TryGetValue("--tables", out var supplied))
        {
            names = supplied.Split(',');
            if (names.Length is < 1 or > 16 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length || names.Any(name => !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]{0,63}$", RegexOptions.CultureInvariant)))
                throw new ArgumentException("--tables要求1至16个逗号分隔、不重复的规范物理表名。");
        }
        var endpoint = Endpoint(server, args[1]);
        var variable = options.GetValueOrDefault("--token-env", "METARILL_LOGIN_STATE_ID");
        if (!Regex.IsMatch(variable, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant)) throw new ArgumentException("会话环境变量名无效。");
        var session = environment(variable);
        if (session is null || session.Length is < 32 or > 200 || session.Any(character => character < '!' || character > '~'))
            throw new ArgumentException("会话环境变量未配置或格式无效，请使用现有网页登录会话。");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("loginStateId", session);
        request.Headers.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = project })));
        request.Content = JsonContent.Create(planId is not null ? new Dictionary<string, object> { ["planId"] = planId, ["confirmSha256"] = options["--confirm"] } : names is not null ? new Dictionary<string, object> { ["tableNames"] = names } : table is null ? new Dictionary<string, object>() : new() { ["tableId"] = table });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > ResponseLimit) throw new InvalidOperationException("平台查询未成功。");
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var block = new byte[8192];
        int read;
        while ((read = await body.ReadAsync(block, token)) > 0)
        {
            if (buffer.Length + read > ResponseLimit) throw new InvalidOperationException("平台查询响应超出预算。");
            await buffer.WriteAsync(block.AsMemory(0, read), token);
        }
        using var json = JsonDocument.Parse(buffer.ToArray());
        var root = json.RootElement;
        // HTTP 200也可能是原R错误；拒绝错误正文及跨项目响应，不把服务器错误反射到输出。
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("code", out var code) || !code.TryGetInt32(out var number) || number != 200 ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("projectId", out var actual) || actual.ValueKind != JsonValueKind.String || actual.GetString() != project ||
            !data.TryGetProperty("executable", out var executable) || executable.ValueKind != JsonValueKind.False)
            throw new InvalidOperationException("平台查询响应与只读项目协议不符。");
        if (args[1] == "import-preview" && (!data.TryGetProperty("canImport", out var canImport) || canImport.ValueKind != JsonValueKind.False ||
            !data.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.Object || !draft.TryGetProperty("usesDraftIdentifiers", out var symbolic) || symbolic.ValueKind != JsonValueKind.True))
            throw new InvalidOperationException("逻辑草案不能授予导入权限或冒充真实配置ID。");
        if (args[1] is "import-prepare" or "import-apply" or "import-status")
        {
            if (!data.TryGetProperty("executesDdl", out var ddl) || ddl.ValueKind != JsonValueKind.False) throw new InvalidOperationException("配置导入不允许执行DDL。");
            if (args[1] == "import-prepare" && (!data.TryGetProperty("canSaveMetadata", out var canSave) || canSave.ValueKind != JsonValueKind.True ||
                !data.TryGetProperty("planId", out var returnedPlan) || returnedPlan.ValueKind != JsonValueKind.String ||
                !data.TryGetProperty("confirmSha256", out var confirmation) || confirmation.ValueKind != JsonValueKind.String)) throw new InvalidOperationException("缺少可审核计划。");
            if (args[1] is "import-apply" or "import-status" && (!data.TryGetProperty("planId", out var receiptPlan) || receiptPlan.ValueKind != JsonValueKind.String || receiptPlan.GetString() != planId))
                throw new InvalidOperationException("回执计划与本次确认不符。");
            if (args[1] == "import-apply" && (!data.TryGetProperty("configurationSaved", out var saved) || saved.ValueKind != JsonValueKind.True)) throw new InvalidOperationException("没有可信保存回执。");
            if (args[1] == "import-status")
            {
                if (!data.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String || state.GetString() is not ("prepared" or "processing" or "saved" or "unconfirmed" or "unknown") ||
                    !data.TryGetProperty("configurationSaved", out var known) || (state.GetString() == "saved" ? known.ValueKind != JsonValueKind.True : known.ValueKind != JsonValueKind.Null))
                    throw new InvalidOperationException("回执状态不可信，未知结果不能解释为未保存。");
            }
        }
        await output.WriteLineAsync(data.GetRawText().AsMemory(), token);
    }

    /// <summary>限制规范正整数，避免路径、科学计数、前导符号等进入现有标识协议。</summary>
    private static void RequireId(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || id.ToString(CultureInfo.InvariantCulture) != value)
            throw new ArgumentException("项目或受管表ID必须是规范正整数。");
    }

    /// <summary>接口路径固定；不接受URL内凭据、查询串、片段或任意代理路径。</summary>
    private static Uri Endpoint(string value, string command)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath is not ("/" or "/api" or "/api/") ||
            uri.Scheme != "https" && (uri.Scheme != "http" || !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address))))
            throw new ArgumentException("--server须为HTTPS服务地址；仅回环地址允许HTTP，路径只接受/或/api。");
        return new Uri(uri, "/api/admin/sys/database/" + (command.StartsWith("import-", StringComparison.Ordinal) ? "import/" + command[7..] : command));
    }
}
