using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>执行平台真实授权类，固定目录与按钮归属来自仓库元数据，未连接数据库。</summary>
public sealed class BusinessButtonPlatformAuthorizationTests
{
    /// <summary>按钮单独授权不要求平台页面query；其范围值保持字符串ID精度，撤权与错页在解析前拒绝。</summary>
    [Fact]
    public void Real_platform_authorizer_uses_actual_button_and_own_scope()
    {
        var root=Root();var registry=new MetadataRegistry(Path.Combine(root,"backend/metadata/platform.json"),Path.Combine(root,"backend/metadata/demo.json"));
        var button=registry.Platform.ConfigButtons[0];var buttonId=button.GetProperty("id").ToString();var pageId=button.GetProperty("configPageId").ToString();var page=registry.FindPage(pageId)!;
        var identity=new AuthenticatedIdentity(new(8,"已认证按钮账号",false,page.YzProjectId,page.YzAdminTypeId),new HashSet<string>(),9,null)
        {PrincipalProjectId=page.YzProjectId,PrincipalAdminTypeId=page.YzAdminTypeId,ButtonIds=new HashSet<string>{buttonId},ExactDataManageButtonIds=new HashSet<string>{buttonId},ExactDataManageMatchField="real_name",ExactDataManageMatchValue=JsonSerializer.SerializeToElement("9007199254740993")};
        var context=new DefaultHttpContext();var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},registry);context.Items[typeof(AuthenticatedIdentity)]=identity;
        auth.RequireButtonPermission(pageId,buttonId);Assert.Equal(new long[]{9007199254740993},auth.ResolveButtonExactDataManage(pageId,buttonId,"realName").Read<long>());
        Assert.Throws<UnauthorizedAccessException>(()=>auth.RequirePagePermission(pageId,"detail"));Assert.Throws<UnauthorizedAccessException>(()=>auth.RequireButtonPermission("999",buttonId));
        context.Items[typeof(AuthenticatedIdentity)]=identity with{ButtonIds=new HashSet<string>()};Assert.Throws<UnauthorizedAccessException>(()=>auth.ResolveButtonExactDataManage(pageId,buttonId,"real_name"));
        context.Items.Clear();Assert.Throws<UnauthorizedAccessException>(()=>auth.RequireButtonPermission(pageId,buttonId));
    }
    /// <summary>用调用源定位固定仓库，测试不修改平台目录。</summary>
    private static string Root([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
