using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;

namespace Metarill.Runtime.Security;

/// <summary>只从宿主私有配置绑定；站点公钥可公开，密钥不参与JSON或record的自动文本输出。</summary>
public sealed class LoginCapOptions
{
    public bool Enabled {get;init;}
    public string ServerUrl {get;init;}="";
    public string SiteKey {get;init;}="";
    [JsonIgnore] public string SecretKey {get;init;}="";
    /// <summary>启用配置不完整或地址非法时保持拒绝，不能静默关闭验证码。</summary>
    internal Uri? Endpoint()
    {
        if(string.IsNullOrWhiteSpace(ServerUrl)||string.IsNullOrWhiteSpace(SiteKey)||string.IsNullOrWhiteSpace(SecretKey)||SiteKey is "." or ".."||ServerUrl.Contains('\\'))return null;
        if(!Uri.TryCreate(ServerUrl,UriKind.Absolute,out var server)||server.Scheme is not("http" or "https")||server.UserInfo.Length!=0||server.Query.Length!=0||server.Fragment.Length!=0)return null;
        return Uri.TryCreate(ServerUrl.TrimEnd('/')+"/"+Uri.EscapeDataString(SiteKey)+"/",UriKind.Absolute,out var endpoint)?endpoint:null;
    }
}

/// <summary>原Cap siteverify协议：启用后先校验token再查询账号；任何传输或响应异常都不得放行。</summary>
public sealed class LoginCapVerifier(HttpClient client,LoginCapOptions options)
{
    private const string Missing="请先完成安全验证",Invalid="安全验证失败，请重试",Unavailable="安全验证暂不可用，请稍后再试";
    /// <summary>原公开接口只返回enabled及apiEndpoint，不输出任何私钥或原始配置异常。</summary>
    public R<object> PublicConfiguration()
    {
        if(!options.Enabled)return R<object>.Success(new{enabled=false,apiEndpoint=""});
        var endpoint=options.Endpoint();return endpoint is null?R<object>.Error(Unavailable):R<object>.Success(new{enabled=true,apiEndpoint=endpoint.AbsoluteUri});
    }
    /// <summary>缺token及缺配置均在发送前拒绝；请求体只含原secret/response两字段。</summary>
    public async Task VerifyAsync(string? value,CancellationToken cancellationToken=default)
    {
        if(!options.Enabled)return;
        cancellationToken.ThrowIfCancellationRequested();
        var token=TrimToken(value);if(token.Length==0)throw new BusinessRuleException(Missing);
        if(token.Length>4096)throw new BusinessRuleException(Invalid);
        var endpoint=options.Endpoint();if(endpoint is null)throw new BusinessRuleException(Unavailable);
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(endpoint,"siteverify"))
            {Content=JsonContent.Create(new Dictionary<string,string>{{"secret",options.SecretKey},{"response",token}})};
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);response.EnsureSuccessStatusCode();
            // 响应只接受小型JSON，不把远端正文、调试信息或私钥回显给用户或日志。
            await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);var bytes=new byte[4097];var length=0;
            while(length<bytes.Length){var count=await stream.ReadAsync(bytes.AsMemory(length),cancellationToken);if(count==0)break;length+=count;}
            if(length>4096)throw new BusinessRuleException(Unavailable);
            using var json=JsonDocument.Parse(bytes.AsMemory(0,length));var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Count(field=>field.Name=="success")!=1||!root.TryGetProperty("success",out var success)||success.ValueKind!=JsonValueKind.True)
                throw new BusinessRuleException(Invalid);
        }
        catch(OperationCanceledException)when(!cancellationToken.IsCancellationRequested){throw new BusinessRuleException(Unavailable);}
        catch(Exception error)when(error is HttpRequestException or IOException or JsonException){throw new BusinessRuleException(Unavailable);}
    }
    /// <summary>沿用原Java trimToEmpty，不把其它身份输入或密码一并归一化。</summary>
    private static string TrimToken(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return "";var start=0;var end=value.Length;
        while(start<end&&value[start]<=' ')start++;while(end>start&&value[end-1]<=' ')end--;return value[start..end];
    }
}
