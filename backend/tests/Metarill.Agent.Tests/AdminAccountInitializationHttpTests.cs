using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>原HTTP边界接真实初始化业务/协调器，数据库传输仍为明确离线替身。</summary>
public sealed partial class AdminAccountInitializationTests
{
    /// <summary>原中央按钮无页编辑权仍按已保存配置执行，客户端entity不能改口令、项目或目标ID。</summary>
    [Fact]
    public async Task Central_initialization_uses_saved_values_and_preserves_account_profile()
    {
        using var f=new Fixture();f.ExistingAccount();f.Memory.Add("sys_session",("id",50L),("account_id",30L),("kind","web"),("expires_at",DateTime.UtcNow.AddHours(1)));
        var business=await f.Business();using var services=Services(f,business);var context=new DefaultHttpContext{RequestServices=services};f.Memory.Provider.Commands.Clear();
        var entity=JsonSerializer.SerializeToElement(new{id="91",password="客户端未保存口令",yzProjectId=f.Memory.PlatformProject});
        var result=Assert.IsType<OkObjectResult>(await AdminAccountInitializationHttp.TryExecuteAsync(new(Button,"91",entity),context));
        var response=JsonSerializer.SerializeToElement(result.Value,JsonDefaults.Options);Assert.Equal(200,response.GetProperty("code").GetInt32());Assert.Equal(JsonValueKind.Null,response.GetProperty("data").ValueKind);
        var account=Assert.Single(f.TargetAccounts);Assert.Equal("30",SqlRowStore.Text(account,"id"));Assert.Equal("保留姓名",account["real_name"]);Assert.Equal("保留业务档案",account["notes"]);
        Assert.True(PasswordHasher.Verify("原初始化密码",SqlRowStore.Text(account,"password")));Assert.False(PasswordHasher.Verify("客户端未保存口令",SqlRowStore.Text(account,"password")));
        Assert.NotNull(f.Memory.Row("sys_session",50)["delete_no"]);Assert.DoesNotContain(f.Memory.Provider.Commands,sql=>sql.StartsWith("DELETE ",StringComparison.Ordinal));
    }

    /// <summary>其它按钮或普通宿主没有管理初始化注册时保留原分发，零数据库调用。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Unregistered_or_unrelated_button_never_initializes(bool unrelated)
    {
        using var f=new Fixture();var business=await f.Business();using var services=Services(f,unrelated?business:null);
        f.Memory.Provider.Commands.Clear();var context=new DefaultHttpContext{RequestServices=services};
        Assert.Null(await AdminAccountInitializationHttp.TryExecuteAsync(new(unrelated?"other-button":Button,"91"),context));Assert.Empty(f.Memory.Provider.Commands);
    }

    /// <summary>非Web令牌和提交前撤销的原按钮权不能通过新增HTTP分支维持旧授权。</summary>
    [Theory][InlineData("agent")][InlineData("revoked")]
    public async Task Http_initialization_rechecks_real_identity_and_current_button(string invalid)
    {
        using var f=new Fixture();var business=await f.Business();using var services=Services(f,business);
        if(invalid=="agent")f.Accessor.Identity=f.Accessor.Identity with{GrantId=80L};else f.Memory.Row("yz_role_permission",70)["delete_no"]=DateTime.UtcNow;
        f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>AdminAccountInitializationHttp.TryExecuteAsync(new(Button,"91"),new DefaultHttpContext{RequestServices=services}));
        NoWrites(f);Assert.Empty(f.TargetAccounts);Assert.Empty(f.TargetRoles);
    }

    /// <summary>冲突/坏回显在实际业务事务前拒绝，不借实体字段覆盖已保存对象。</summary>
    [Theory]
    [InlineData("{\"id\":\"92\"}")][InlineData("{\"Id\":\"91\"}")][InlineData("{\"id\":\"91\",\"ID\":\"91\"}")][InlineData("\"91\"")][InlineData("{\"id\":9.1e1}")][InlineData("{\"id\":true}")]
    public async Task Conflicting_entity_is_rejected_before_database(string json)
    {
        using var f=new Fixture();var business=await f.Business();using var services=Services(f,business);f.Memory.Provider.Commands.Clear();
        await Assert.ThrowsAsync<ArgumentException>(()=>AdminAccountInitializationHttp.TryExecuteAsync(new(Button,"91",JsonSerializer.Deserialize<JsonElement>(json)),new DefaultHttpContext{RequestServices=services}));
        Assert.Empty(f.Memory.Provider.Commands);
    }

    /// <summary>原直调支持query、表单及JSON，长整数保持字符串，不经浮点。</summary>
    [Theory]
    [InlineData("?id=91",null,"","91")]
    [InlineData("?id=91","application/json","","91")]
    [InlineData("","application/x-www-form-urlencoded","id=91","91")]
    [InlineData("","application/json","{\"id\":\"91\"}","91")]
    [InlineData("","application/json","{\"id\":91}","91")]
    [InlineData("?id=91","application/json","{\"id\":\"91\"}","91")]
    [InlineData("?id=91","application/json","{}","91")]
    [InlineData("?id=9007199254742001",null,"","9007199254742001")]
    public async Task Legacy_id_protocol_preserves_original_sources(string query,string? contentType,string body,string expected)
        =>Assert.Equal(expected,await AdminAccountInitializationHttp.ReadTargetIdAsync(Request(query,contentType,body)));

    /// <summary>额外口令、重复ID、双来源冲突和非规范数字全部在读取业务前拒绝。</summary>
    [Theory]
    [InlineData("?id=91&id=91",null,"")][InlineData("?id=91&password=x",null,"")][InlineData("?ID=91",null,"")]
    [InlineData("","application/x-www-form-urlencoded","id=91&id=91")]
    [InlineData("","application/json","{\"id\":\"91\",\"password\":\"x\"}")]
    [InlineData("?id=91","application/json","{\"id\":\"92\"}")]
    [InlineData("","application/json","[]")][InlineData("","application/json","{\"id\":\"91\",\"id\":\"91\"}")]
    [InlineData("","application/json","{\"id\":91.0}")][InlineData("","application/json","{\"id\":null}")]
    [InlineData("?id=91","text/plain","id=92")][InlineData("?id=091",null,"")][InlineData("?id=0",null,"")]
    public async Task Legacy_ambiguous_or_overposted_request_is_rejected(string query,string? contentType,string body)
        =>await Assert.ThrowsAsync<ArgumentException>(()=>AdminAccountInitializationHttp.ReadTargetIdAsync(Request(query,contentType,body)));

    /// <summary>真实Kestrel正文标记区分无正文JSON与分块正文，不能只凭可空ContentLength判断。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Legacy_body_detection_distinguishes_empty_from_chunked(bool canHaveBody)
    {
        var request=Request("?id=91",canHaveBody?"text/plain":"application/json",canHaveBody?"id=92":"");
        request.ContentLength=null;request.HttpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(canHaveBody));
        if(canHaveBody)await Assert.ThrowsAsync<ArgumentException>(()=>AdminAccountInitializationHttp.ReadTargetIdAsync(request));
        else Assert.Equal("91",await AdminAccountInitializationHttp.ReadTargetIdAsync(request));
    }
    private sealed record BodyDetection(bool CanHaveBody):IHttpRequestBodyDetectionFeature;

    /// <summary>真实FormFeature含文件时拒绝，不能利用multipart绕过仅id协议。</summary>
    [Fact]
    public async Task Legacy_form_file_is_rejected()
    {
        var request=Request("","multipart/form-data; boundary=example","");
        request.Form=new FormCollection(new Dictionary<string,Microsoft.Extensions.Primitives.StringValues>{{"id","91"}},new FormFileCollection{new FormFile(new MemoryStream([1]),0,1,"file","test.bin")});
        await Assert.ThrowsAsync<ArgumentException>(()=>AdminAccountInitializationHttp.ReadTargetIdAsync(request));
    }

    /// <summary>仅向本次容器注册真实业务与已经认证的身份，不替换权限协调器。</summary>
    private static ServiceProvider Services(Fixture fixture,AdminAccountInitializationBusiness? business)
    {
        var services=new ServiceCollection();services.AddSingleton<ISecurityIdentityAccessor>(fixture.Accessor);
        if(business is not null)services.AddSingleton<IAdminAccountInitializer>(business);return services.BuildServiceProvider();
    }
    /// <summary>真实HttpRequest携带独立内存正文；没有网络、真实会话或数据库副作用。</summary>
    private static HttpRequest Request(string query,string? contentType,string body)
    {
        var context=new DefaultHttpContext();context.Request.QueryString=new QueryString(query);context.Request.ContentType=contentType;
        var bytes=Encoding.UTF8.GetBytes(body);context.Request.Body=new MemoryStream(bytes);context.Request.ContentLength=bytes.Length;return context.Request;
    }
}
