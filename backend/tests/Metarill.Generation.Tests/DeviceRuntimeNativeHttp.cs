using System.Net.Http;
using System.Text;
using System.Text.Json;
using Metarill.Runtime;
namespace Metarill.Generation.Tests;

/// <summary>正式夹具按原uni-app发送有长度的JSON正文，不能用JsonContent隐式变成chunked请求。</summary>
internal static class DeviceRuntimeNativeHttp
{
    /// <summary>保留原空对象、UTF-8及确认头；StringContent明确计算字节长度，不修改产品对chunked的原门禁。</summary>
    internal static HttpRequestMessage Create(string route, object body, string? batch = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json") };
        if (batch is not null) { request.Headers.Add("X-Yzp-Agent-Request-Id", batch); request.Headers.Add("X-Yzp-Agent-Confirm", "true"); }
        return request;
    }
}
