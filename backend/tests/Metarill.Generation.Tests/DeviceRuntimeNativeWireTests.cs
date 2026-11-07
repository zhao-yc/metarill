using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际Kestrel线上传输合同；无数据库、身份替身或业务成功声明。</summary>
public sealed class DeviceRuntimeNativeWireTests
{
    /// <summary>先证明旧JsonContent在真实网络上为chunked，再验证正式夹具与原页面一样发送长度2的空对象。</summary>
    [Fact]
    public async Task Original_empty_json_request_keeps_known_length_on_the_wire()
    {
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost("/wire", async (HttpContext context) => { using var reader = new StreamReader(context.Request.Body); return Results.Json(new { length = context.Request.ContentLength, chunked = context.Request.Headers.TransferEncoding.Count != 0, body = await reader.ReadToEndAsync() }); });
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
            using var legacyFixture = await client.PostAsJsonAsync("/wire", new { }); var negative = await legacyFixture.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(negative.GetProperty("chunked").GetBoolean()); Assert.Equal(JsonValueKind.Null, negative.GetProperty("length").ValueKind);
            using var request = DeviceRuntimeNativeHttp.Create("/wire", new { }, Guid.NewGuid().ToString("D")); using var response = await client.SendAsync(request); var positive = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, positive.GetProperty("length").GetInt64()); Assert.False(positive.GetProperty("chunked").GetBoolean()); Assert.Equal("{}", positive.GetProperty("body").GetString()); Assert.Equal("true", Assert.Single(request.Headers.GetValues("X-Yzp-Agent-Confirm")));
        }
        finally { await app.StopAsync(); }
    }
}
