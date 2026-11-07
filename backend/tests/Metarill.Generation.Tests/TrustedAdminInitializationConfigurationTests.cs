using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>验证原后台初始化配置字段的实际生成合同，不执行初始化按钮、HTTP或数据库。</summary>
public sealed class TrustedAdminInitializationConfigurationTests(TrustedSecurityServiceFixture fixture):IClassFixture<TrustedSecurityServiceFixture>
{
    private const string Page="1696799309006348290";
    private const string ConfigurationPassword="仅离线配置输入-12345";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    /// <summary>原用户名/密码输入保留，密码不是登录账号表的摘要，配置省略保留、明确null清空。</summary>
    [Fact]
    public async Task Original_configuration_password_round_trips_and_obeys_field_presence()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var id=fixture.Models.Binding.ManagementAdminTypeId;var service=run.Service("YzAdminType");
        var request=run.Request("YzAdminType",Page,"Update",JsonSerializer.Serialize(new{id,userName="init-config-user",password=ConfigurationPassword}));
        var response=await run.Call(service,"Update"+Page+"Async",request,null,run.Identity.Actor,CancellationToken.None);
        var value=JsonSerializer.SerializeToElement(response,Json);Assert.Equal("init-config-user",value.GetProperty("userName").GetString());Assert.Equal(ConfigurationPassword,value.GetProperty("password").GetString());
        var password=run.Transport.Rows["yz_admin_type"].Single(row=>Convert.ToString(row["id"])==id)["password"];
        await run.Call(service,"Update"+Page+"Async",run.Request("YzAdminType",Page,"Update",JsonSerializer.Serialize(new{id,userName="只改初始化用户名"})),null,run.Identity.Actor,CancellationToken.None);
        Assert.Equal(password,run.Transport.Rows["yz_admin_type"].Single(row=>Convert.ToString(row["id"])==id)["password"]);
        await run.Call(service,"Update"+Page+"Async",run.Request("YzAdminType",Page,"Update",JsonSerializer.Serialize(new{id,password=(string?)null})),null,run.Identity.Actor,CancellationToken.None);
        Assert.Null(run.Transport.Rows["yz_admin_type"].Single(row=>Convert.ToString(row["id"])==id)["password"]);
        Assert.Equal(3,run.Writes.Count);Assert.All(run.Transport.WriteTransactions,Assert.True);
    }

    /// <summary>原50字符限制由生成验证执行，非法输入不能部分写入已保存配置。</summary>
    [Theory]
    [InlineData("password")][InlineData("userName")]
    public async Task Original_configuration_length_limit_rejects_without_partial_update(string field)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var before=run.Fingerprint();
        var body=new Dictionary<string,object?>{{"id",fixture.Models.Binding.ManagementAdminTypeId},{field,new string('x',51)}};
        var request=run.Request("YzAdminType",Page,"Update",JsonSerializer.Serialize(body));
        await Assert.ThrowsAsync<ArgumentException>(()=>run.Call(run.Service("YzAdminType"),"Update"+Page+"Async",request,null,run.Identity.Actor,CancellationToken.None));
        Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Writes);
    }

    /// <summary>只读角色不能借恢复原控件写入；最终权限来自真实协调器的当前角色行。</summary>
    [Fact]
    public async Task Restored_configuration_field_does_not_bypass_original_page_edit_permission()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);
        foreach(var row in run.Transport.Rows["yz_role_permission"].Where(row=>Convert.ToString(row.GetValueOrDefault("config_page_id"))==Page))row["permission_values"]="[\"is_query\"]";
        var before=run.Fingerprint();var request=run.Request("YzAdminType",Page,"Update",JsonSerializer.Serialize(new{id=fixture.Models.Binding.ManagementAdminTypeId,password=ConfigurationPassword}));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Call(run.Service("YzAdminType"),"Update"+Page+"Async",request,null,run.Identity.Actor,CancellationToken.None));
        Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Writes);
    }

    /// <summary>新配置输入没有预置秘密值，原控件和后端可写字段均由同一页面元数据生成。</summary>
    [Fact]
    public void Original_password_declaration_generates_input_and_optional_dto_without_a_default_secret()
    {
        var field=fixture.Catalog.ConfigPageColumns.Single(value=>value.Id=="1696799603148693507");Assert.Equal(Page,field.ConfigPageId);Assert.Equal("1696799603148693506",field.ConfigColumnId);
        Assert.True(field.Extra.TryGetValue("defaultValue",out var initial)&&initial.GetString()=="");Assert.Equal(1,field.IsEdit);
        var request=fixture.Type("Generated.DTOs.YzAdminTypePage"+Page+"UpdateRequest");Assert.Equal(typeof(Optional<string>),request.GetProperty("Password")!.PropertyType);
        var source=File.ReadAllText(Path.Combine(fixture.DirectoryPath,"generated/frontend/pages/yzp/yz_admin_type-detail-"+Page+".vue"));
        Assert.Contains("name='password'",source);Assert.Contains("v-model='entity.password'",source);Assert.Contains("maxlength='50'",source);
    }
}
