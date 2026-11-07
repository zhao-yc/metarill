using System.Net;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>正式R0用例使用原登录页的三个表单字段，不注入身份或替换认证处理器。</summary>
internal static class GlobalDerivedTargetNativeHttp
{
    /// <summary>保留原空验证码兼容字段；验证码为空不作为认证证明。</summary>
    internal static FormUrlEncodedContent LoginForm(string username,string password)=>new(new Dictionary<string,string>{{"username",username},{"password",password},{"captchaVerification",""}});
    /// <summary>令牌只从真实登录回执读取并替换旧header，不写日志或命令行。</summary>
    internal static async Task<string> LoginAsync(HttpClient client,string username,string password)
    {
        using var body=LoginForm(username,password);var response=await FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/sys/yz_manager/login",body);
        var token=response.GetProperty("map").GetProperty("loginStateId").GetString();Assert.True(token is{Length:64}&&token.All(value=>value is >= '0' and <= '9' or >= 'a' and <= 'f'),"正式Web令牌格式不符，正文不回显。");
        client.DefaultRequestHeaders.Remove("loginStateId");client.DefaultRequestHeaders.Add("loginStateId",token);return token!;
    }
}
