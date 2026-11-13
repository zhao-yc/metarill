using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>真实双库HTTP验证旧上传公开协议及认证项目；只清本测试捕获的ID和独占目录。</summary>
    [MySqlApiFact]
    public async Task Legacy_upload_is_public_but_always_uses_real_authenticated_project_and_bounded_content()
    {
        var settings = Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!;
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(settings));
        foreach (var (key, name) in new[] { ("Database", "metarill_test"), ("BusinessDatabase", "metarill_business_test") }) Assert.Equal(name, config.RootElement.GetProperty("Metarill").GetProperty(key).GetProperty("ExpectedDatabase").GetString());
        var previous = Environment.GetEnvironmentVariable("METARILL_SETTINGS"); Environment.SetEnvironmentVariable("METARILL_SETTINGS", settings);
        var parent = Path.Combine(Path.GetTempPath(), "metarill-legacy-http"); var temporary = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        var saved = new List<(string Project, long Id)>();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.ClearProviders()).ConfigureServices(services =>
        {
            services.RemoveAll<FileLibraryOptions>(); services.AddSingleton(new FileLibraryOptions { RootDirectory = Path.Combine(temporary, "objects"), MaxFileBytes = 4096, LegacyPublicBaseUrl = "https://public.example.test/mounted" });
            services.RemoveAll<IIdGenerator>(); services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(857));
        }));
        using var client = factory.CreateClient();
        var bootstrap = config.RootElement.GetProperty("Metarill").GetProperty("Bootstrap");
        try
        {
            var login = await Body(await client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = bootstrap.GetProperty("Username").GetString()!, ["password"] = bootstrap.GetProperty("Password").GetString()! }))); Data(login);
            var token = login.GetProperty("map").GetProperty("loginStateId").GetString(); client.DefaultRequestHeaders.Add("loginStateId", token);
            client.DefaultRequestHeaders.Host = "untrusted-host.example";
            var registry = factory.Services.GetRequiredService<MetadataRegistry>(); var runtimes = factory.Services.GetRequiredService<ProjectFileRuntimes>();
            using var anonymous = factory.CreateClient();
            using var bitmap = new SKBitmap(2, 3); bitmap.Erase(SKColors.Blue); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
            var raw = encoded.ToArray().Concat("removed-payload"u8.ToArray()).ToArray();
            foreach (var project in new[] { registry.Platform.YzProjectId, registry.Demo.YzProjectId })
            {
                client.DefaultRequestHeaders.Remove("sysParams"); client.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = project })));
                var unique = "legacy-" + Guid.NewGuid().ToString("N");
                foreach (var (name, bytes, mime) in new[] { (unique + ".png", raw, "image/png"), (unique + ".pdf", "%PDF-1.4\n测试附件\n%%EOF"u8.ToArray(), "application/pdf") })
                {
                    using var form = Form(name, bytes); var result = await Body(await client.PostAsync("/api/admin/sys/upload/uploadImage?projectId=999999", form));
                    var data = Data(result); var url = data.GetProperty("url").GetString()!; Assert.Equal(url, result.GetProperty("location").GetString()); Assert.Equal(name, data.GetProperty("name").GetString());
                    Assert.EndsWith("?projectId=" + project, url); Assert.StartsWith("https://public.example.test/mounted/api/admin/sys/file-library/public-content/", url);
                    var id = long.Parse(Regex.Match(url, "public-content/([0-9]+)").Groups[1].Value); saved.Add((project, id));
                    var runtime = runtimes.Resolve(project); var row = Assert.Single(await runtime.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = id }));
                    Assert.Equal(project, row["yz_project_id"]!.ToString()); Assert.Equal("PUBLIC", row["access_mode"]); Assert.Equal("ready", row["status"]);
                    var other = runtimes.Resolve(project == registry.Platform.YzProjectId ? registry.Demo.YzProjectId : registry.Platform.YzProjectId);
                    Assert.Empty(await other.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = id }));
                    // TestServer只验证宿主地址输出与后端内容；示例公开域名的实际DNS/网关不属于此测试。
                    var downloadPath = new Uri(url).PathAndQuery["/mounted".Length..];
                    using var download = await anonymous.GetAsync(downloadPath); download.EnsureSuccessStatusCode(); Assert.Equal(mime, download.Content.Headers.ContentType?.MediaType); Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
                    var content = await download.Content.ReadAsByteArrayAsync();
                    if (name.EndsWith(".png", StringComparison.Ordinal)) { Assert.False(content.AsSpan().IndexOf("removed-payload"u8) >= 0); using var actual = SKBitmap.Decode(content); Assert.Equal(2, actual.Width); Assert.Equal(3, actual.Height); }
                    else Assert.Equal(bytes, content);
                }
                foreach (var (name, bytes) in new[] { (unique + "-fake.png", "html"u8.ToArray()), (unique + "-large.txt", new byte[4097]), (unique + ".exe", new byte[1]) })
                {
                    using var form = Form(name, bytes); var result = await Body(await client.PostAsync("/api/admin/sys/upload/uploadImage", form)); Assert.Equal(500, result.GetProperty("code").GetInt32()); Assert.False(result.TryGetProperty("location", out _));
                    Assert.Empty(await runtimes.Resolve(project).Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["name"] = name }));
                }
            }
            using (var wrong = Form("wrong.png", raw, "not-file")) Assert.Equal(500, (await Body(await client.PostAsync("/api/admin/sys/upload/uploadImage", wrong))).GetProperty("code").GetInt32());
            using (var multiple = Form("one.png", raw)) { multiple.Add(new ByteArrayContent(raw), "file", "two.png"); Assert.Equal(500, (await Body(await client.PostAsync("/api/admin/sys/upload/uploadImage", multiple))).GetProperty("code").GetInt32()); }
            // 清空请求头后，即使query携带有效令牌与项目，也不能伪造登录；测试日志已关闭避免令牌落盘。
            using (var form = Form("anonymous.png", raw))
            {
                var denied = await Body(await anonymous.PostAsync("/api/admin/sys/upload/uploadImage?loginStateId=" + Uri.EscapeDataString(token!) + "&sysParams=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = Project })), form));
                Assert.Equal(500, denied.GetProperty("code").GetInt32()); Assert.False(denied.TryGetProperty("location", out _));
            }
        }
        finally
        {
            var runtimes = factory.Services.GetRequiredService<ProjectFileRuntimes>();
            foreach (var (project, id) in saved)
            {
                var runtime = runtimes.Resolve(project); await using var connection = runtime.Session.Provider.CreateConnection(runtime.Session.Options); await connection.OpenAsync();
                await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM `yz_file_library` WHERE `id`=@id AND `yz_project_id`=@project";
                var p = command.CreateParameter(); p.ParameterName = "id"; p.Value = id; command.Parameters.Add(p); var q = command.CreateParameter(); q.ParameterName = "project"; q.Value = long.Parse(project); command.Parameters.Add(q); await command.ExecuteNonQueryAsync();
            }
            Environment.SetEnvironmentVariable("METARILL_SETTINGS", previous);
            var target = Path.GetFullPath(temporary); var boundary = Path.GetFullPath(parent) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(boundary, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(target), "N", out _)) throw new InvalidOperationException("拒绝清理上传验收目录之外的路径。");
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    /// <summary>客户端MIME刻意声明HTML，响应必须使用服务端真实处理类型。</summary>
    private static MultipartFormDataContent Form(string name, byte[] bytes, string field = "file")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(""), "modelName"); form.Add(new StringContent(""), "column"); form.Add(new StringContent("/untrusted/client/path"), "file");
        var body = new ByteArrayContent(bytes); body.Headers.ContentType = new MediaTypeHeaderValue("text/html"); form.Add(body, field, name); return form;
    }
}
