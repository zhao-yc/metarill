using System.Text.Json;
using Metarill.Runtime.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>平台管理员在业务工作区仍读取认证后台的导航，并提供原菜单组件所需字段。</summary>
    [MySqlApiFact]
    public async Task Menu_wire_contract_supplies_visible_labels_and_refresh_context()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        var registry=factory.Services.GetRequiredService<MetadataRegistry>();
        var response=await Body(await client.GetAsync("/api/admin/sys/yz_manager/getMenu"));
        Assert.Equal(200,response.GetProperty("code").GetInt32());
        var adminTypeId = registry.Platform.YzAdminTypes.Single().Id;
        var source = registry.Platform.ConfigMenus
            .Where(menu => menu.YzAdminTypeId == adminTypeId && menu.IsHidden != 1)
            .ToDictionary(menu => menu.Id);
        var seen=new HashSet<string>();
        void Verify(JsonElement node)
        {
            var id=node.GetProperty("menu_id").GetString()!;
            var expected=source[id];Assert.True(seen.Add(id));
            Assert.False(string.IsNullOrWhiteSpace(node.GetProperty("text").GetString()));
            Assert.Equal(expected.Name,node.GetProperty("text").GetString());
            Assert.Equal(expected.IconName,node.GetProperty("icon").GetString());
            Assert.Equal(registry.Platform.YzProjectId, node.GetProperty("projectId").GetString());
            Assert.Equal(expected.YzAdminTypeId,node.GetProperty("adminTypeId").GetString());
            foreach(var child in node.GetProperty("children").EnumerateArray())Verify(child);
        }
        foreach(var root in response.GetProperty("menu").EnumerateArray())Verify(root);
        Assert.Equal(source.Count,seen.Count);
    }
    /// <summary>原控件按真实 MySQL 项目展示存储选项，匿名请求继续拒绝。</summary>
    [MySqlApiFact]
    public async Task Storage_mode_policy_is_authenticated_and_reports_current_capabilities()
    {
        var(factory,client)=await Login();await using var disposable=factory;using var http=client;
        var policy=Data(await Post(client,"/admin/custom/config_table/storage_mode_policy",new{}));
        Assert.Equal(Project,policy.GetProperty("projectId").GetString());
        Assert.Equal("csharp",policy.GetProperty("backendTarget").GetString());
        Assert.True(policy.GetProperty("isV8").GetBoolean());
        Assert.True(policy.GetProperty("supportsNonRedundant").GetBoolean());
        using var anonymous=factory.CreateClient();
        Assert.Equal(500,(await Post(anonymous,"/admin/custom/config_table/storage_mode_policy",new{})).GetProperty("code").GetInt32());
    }

}
