using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Metarill.Generation.Tests;

/// <summary>显式人工浏览器验收的有界等待；只生成私有资源，不发布管理Full或替代浏览器证据。</summary>
internal static class TrustedManagementBrowserLease
{
    /// <summary>普通测试默认不等待；误配置必须失败，不能悄悄把人工验收当成已执行。</summary>
    internal static int? ReadSeconds(string? value)
    {
        if(value is null)return null;
        if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var seconds)||seconds is <60 or >1800)throw new ArgumentException("浏览器验收等待必须显式为60至1800秒。");
        return seconds;
    }
    /// <summary>同批工厂目录、生成归属和前端资源保持一致；只在私有副本填写已授权AppID与同源API配置。</summary>
    internal static async Task<string> WaitAsync(TrustedSecurityServiceFixture fixture,TrustedManagementProgram program,string username,string password,string evidence,int seconds)
    {
        _=ReadSeconds(seconds.ToString(CultureInfo.InvariantCulture));
        var appid=Environment.GetEnvironmentVariable("METARILL_MANAGEMENT_BROWSER_APPID");
        if(string.IsNullOrWhiteSpace(appid)||appid.Length>64||appid.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))throw new ArgumentException("浏览器构建需要显式配置已有DCloud AppID。");
        var root=Path.GetFullPath(Path.Combine(evidence,"browser"));var frontend=Path.Combine(root,"frontend");Directory.CreateDirectory(frontend);
        var resources=new FrontendPackager().PrepareSecurityManagement(fixture.Models,Path.Combine(fixture.DirectoryPath,"generated/frontend"),Path.Combine(TrustedSecurityServiceFixture.SourceRoot,".."));
        foreach(var pair in resources.Files){var path=CSharpGenerator.SafePath(frontend,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,pair.Value);}
        var manifest=Path.Combine(frontend,"manifest.json");var json=JsonNode.Parse(await File.ReadAllTextAsync(manifest))!;json["appid"]=appid;await File.WriteAllTextAsync(manifest,json.ToJsonString(new JsonSerializerOptions{WriteIndented=true})+"\n");
        using var reservation=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);reservation.Start();var frontendAddress="http://127.0.0.1:"+((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;reservation.Stop();
        await File.WriteAllTextAsync(Path.Combine(frontend,"common/application.config.js"),"/** 私有验收使用同源代理；正式模板和用户配置保持原样。 */\nexport default { apiBaseUrl: "+JsonSerializer.Serialize(frontendAddress+"/api")+" }\n");
        var leaseId=Guid.NewGuid().ToString("N");var ready=Path.Combine(root,"ready.json");var release=Path.Combine(root,"release.json");var expires=DateTimeOffset.UtcNow.AddSeconds(seconds);
        // 临时登录口令只保存在忽略目录，绝不写控制台；无论正常结束、超时还是错误都移除。
        await File.WriteAllTextAsync(Path.Combine(root,"preview.json"),JsonSerializer.Serialize(new{leaseId,backendAddress=program.Address,frontendAddress,frontend,expiresAt=expires})+"\n");
        try
        {
            await File.WriteAllTextAsync(ready,JsonSerializer.Serialize(new{leaseId,backendAddress=program.Address,frontendAddress,frontend,projectId=fixture.Catalog.YzProjectId,adminTypeId=fixture.Catalog.YzAdminTypes[0].Id,username,password,expiresAt=expires,requiredBackendAdapters=resources.RequiredBackendAdapters.Select(adapter=>adapter.Code),backendGapCount=resources.BackendGaps.Count},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})+"\n");
            while(DateTimeOffset.UtcNow<expires)
            {
                if(File.Exists(release))
                {
                    if(new FileInfo(release).Length>2048)throw new InvalidOperationException("浏览器验收释放信号过大。");
                    var status=ReadRelease(await File.ReadAllTextAsync(release),leaseId);
                    File.Delete(ready);if(File.Exists(ready))throw new IOException("浏览器临时口令文件未能移除。");
                    await File.WriteAllTextAsync(Path.Combine(root,"completed.json"),JsonSerializer.Serialize(new{leaseId,status,completedAt=DateTimeOffset.UtcNow,credentialsRemoved=true})+"\n");return status;
                }
                await Task.Delay(250);
            }
            throw new TimeoutException("浏览器验收等待已到期，宿主和自有样本将按原finally清理。");
        }
        finally{if(File.Exists(ready))File.Delete(ready);}
    }
    /// <summary>释放只接受本轮精确标识；完成构建和完成浏览器必须分别记录，不从信号推导UI已通过。</summary>
    internal static string ReadRelease(string json,string leaseId)
    {
        using var document=JsonDocument.Parse(json);var value=document.RootElement;
        if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Count()!=2||value.EnumerateObject().Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=2||!value.TryGetProperty("leaseId",out var id)||id.ValueKind!=JsonValueKind.String||id.GetString()!=leaseId||!value.TryGetProperty("status",out var status)||status.ValueKind!=JsonValueKind.String||status.GetString() is not("completed" or "build-only"))throw new InvalidOperationException("浏览器验收释放信号与当前租约不匹配。");
        return status.GetString()!;
    }
}
