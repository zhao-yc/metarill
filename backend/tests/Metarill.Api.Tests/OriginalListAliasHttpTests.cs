using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>两个旧104候选地址须复用当前105的HTTP与授权入口，全部拒绝在SQL之前发生。</summary>
public sealed class OriginalListAliasHttpTests
{
    private const string TargetPage="1696799309006348290";
    private static readonly string[] Pages=["1868973685063311361","6191175925956608"];

    /// <summary>真实Program下新旧六路由都进入原匿名拒绝，不允许404或绕过现有身份。</summary>
    [Fact]
    public async Task Old_and_current_relation_routes_share_original_anonymous_boundary()
        =>await WithOfflineSettings(async ()=>
        {
            await using var factory=new WebApplicationFactory<Program>();using var client=factory.CreateClient();
            foreach(var page in Pages)foreach(var kind in new[]{"","page","selected"})
            {
                var current=await Send(client,page,"105",kind);var legacy=await Send(client,page,"104",kind);
                Assert.Equal(current,legacy);Assert.Equal((HttpStatusCode.OK,500,"登录超时"),legacy);
            }
            // 原数组的空POST仍允许到达认证入口，不会变成MVC缺少正文错误。
            foreach(var page in Pages)
            {
                using var response=await client.PostAsync(Route(page,"104",""),null);
                using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(500,body.RootElement.GetProperty("code").GetInt32());Assert.Equal("登录超时",body.RootElement.GetProperty("msg").GetString());
            }
        });

    /// <summary>源页拒绝或源页通过后目标页拒绝，新旧URI得到相同逐次检查；替身仅观测授权，不执行数据库。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Alias_keeps_source_then_target_permission_checks_before_sql(bool targetDenied)
        =>await WithOfflineSettings(async ()=>
        {
            var authorization=new RecordingAuthorization(targetDenied);
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
            {
                services.RemoveAll<IRuntimeAuthorization>();services.AddSingleton<IRuntimeAuthorization>(authorization);
            }));
            using var client=factory.CreateClient();
            foreach(var page in Pages)foreach(var kind in new[]{"","page","selected"})
            {
                authorization.Calls.Clear();var current=await Send(client,page,"105",kind);var expected=authorization.Calls.ToArray();
                authorization.Calls.Clear();var legacy=await Send(client,page,"104",kind);
                Assert.Equal(current,legacy);Assert.Equal(expected,authorization.Calls);Assert.Equal(500,legacy.Code);
                Assert.Equal("无权执行此操作。",legacy.Message);
                Assert.Contains(page+":list",expected);
                if(targetDenied){Assert.Contains(TargetPage+":list",expected);Assert.True(Array.IndexOf(expected,page+":list")<Array.IndexOf(expected,TargetPage+":list"));}
                else Assert.DoesNotContain(TargetPage+":list",expected);
            }
        });

    /// <summary>请求固定在已发布页与旧/新字段号，测试不允许指定表或任意路由。</summary>
    private static string Route(string page,string field,string kind)=>"/api/admin/auto/config_page/"+page+"/findNameIdYzAdminType2200000000000000"+field+(kind.Length==0?"":"/"+kind);

    /// <summary>记录真实MVC响应状态和原R，不把找不到路由或绑定失败当作拒权通过。</summary>
    private static async Task<(HttpStatusCode Status,int Code,string? Message)> Send(HttpClient client,string page,string field,string kind)
    {
        object request=kind=="selected"?new{selectedIds=Array.Empty<string>()}:kind=="page"?new{pageNo=1,pageSize=20}:new{};
        using var response=await client.PostAsJsonAsync(Route(page,field,kind),request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var body=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return(response.StatusCode,body.RootElement.GetProperty("code").GetInt32(),body.RootElement.GetProperty("msg").GetString());
    }

    /// <summary>仅使用不可达回环连接；不读取私有开发配置，不初始化、登录或写库。</summary>
    private static async Task WithOfflineSettings(Func<Task> action)
    {
        var directory=Path.Combine(Path.GetTempPath(),"metarill-list-alias",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"settings.json");var previous=Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        var database=new{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1;"};
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{Metarill=new{Database=database,BusinessDatabase=database}}));
        Environment.SetEnvironmentVariable("METARILL_SETTINGS",path);
        try{await action();}
        finally{Environment.SetEnvironmentVariable("METARILL_SETTINGS",previous);File.Delete(path);Directory.Delete(directory);}
    }

    /// <summary>逐次记录来源/目标检查，明确拒绝后不得进入实际Service查询。</summary>
    private sealed class RecordingAuthorization(bool targetDenied):IRuntimeAuthorization
    {
        internal List<string> Calls{get;}=[];
        public RuntimeActor RequireActor(){Calls.Add("actor");return new(7,"授权观测替身",false,"1608097412978577409","1");}
        public void RequirePagePermission(string pageId,string operation)
        {
            Calls.Add(pageId+":"+operation);
            if(!targetDenied||pageId==TargetPage)throw new UnauthorizedAccessException(targetDenied?"旧接口目标页拒绝":"旧接口来源页拒绝");
        }
    }
}
