using System.Net;
using Metarill.Api.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>通过真实MVC路由选择核对旧URL；在动作执行前停止，不注册数据库或替身业务。</summary>
public sealed class CompatibilityAliasRoutingTests
{
    /// <summary>非保留路由参数不能覆盖MVC固定action值，旧复制/生成/结构入口应命中原Legacy方法。</summary>
    [Theory]
    [InlineData("/api/admin/custom/config_page/copy_page", "Legacy")]
    [InlineData("/api/admin/custom/config_page/generate_all", "Legacy")]
    [InlineData("/api/admin/custom/config_table/create_page", "Legacy")]
    [InlineData("/api/admin/custom/config_table/config_indexes", "Legacy")]
    [InlineData("/api/admin/custom/config_column/add_column_to_db", "Legacy")]
    [InlineData("/api/admin/custom/index/init_database", "Legacy")]
    [InlineData("/api/admin/custom/index/init_admin", "Legacy")]
    [InlineData("/api/admin/sys/config_button/execute", "Execute")]
    public async Task Original_urls_reach_the_real_controller_action_without_a_database(string path, string expectedAction)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(CompatibilityButtonController).Assembly);
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            // 只记录真实端点选择结果；原动作的身份、数据库按钮白名单和事务仍由完整HTTP验收覆盖。
            var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action?.ControllerTypeInfo.AsType() == typeof(CompatibilityButtonController))
            {
                context.Response.Headers["X-Test-Matched-Action"] = action.ActionName;
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next(context);
        });
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PostAsync(path + "?id=9007199254740993", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(expectedAction, Assert.Single(response.Headers.GetValues("X-Test-Matched-Action")));
    }
}
