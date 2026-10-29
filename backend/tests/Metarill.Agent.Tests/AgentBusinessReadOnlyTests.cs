using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Agent;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Metarill.Agent.Tests;

public sealed partial class AgentBusinessActionTests
{
    /// <summary>Web和Agent消费同一已发布条件与真正读取结果；客户端旧entity及发布后元数据不能放宽。</summary>
    [Theory]
    [InlineData("amount < 0")][InlineData("entity.amount < 0")][InlineData("this.entity.amount < 0")]
    [InlineData("realName === '锁定' || amount < 0")][InlineData("HTTP_status === '允许' && amount < 0")]
    public async Task Writable_condition_uses_server_record_for_both_entry_points(string condition)
    {
        using var fixture=new Fixture("writable",readOnly:condition);fixture.Authorization.AllowPage=true;
        fixture.Reader.Row.Amount=-1;var context=fixture.Context("Preview");var handler=new WebHandler();
        var executor=new OriginalBusinessButtonExecutor(fixture.Resources,fixture.Authorization,fixture.Services,fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>(),[handler],fixture.Guard);
        var request=new OriginalBusinessButtonRequest("31","7",JsonSerializer.SerializeToElement(new{id="7",amount=10}),"Normal","");
        var agentError=await Assert.ThrowsAsync<BusinessRuleException>(()=>fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",context,Encoding.UTF8.GetBytes("{\"recordKey\":7}")));
        var webError=await Assert.ThrowsAsync<BusinessRuleException>(()=>executor.ExecuteAsync(request,context));Assert.Equal(agentError.Message,webError.Message);Assert.Contains("只读",webError.Message);
        Assert.Equal(0,fixture.Guard.Calls);Assert.Equal(0,handler.Calls);Assert.Equal(2,fixture.Reader.Calls);
        fixture.Metadata.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("false");
        await Assert.ThrowsAsync<BusinessRuleException>(()=>executor.ExecuteAsync(request,context));Assert.Equal(0,handler.Calls);
        fixture.Reader.Row.Amount=10;
        await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",context,Encoding.UTF8.GetBytes("{\"recordKey\":7}"));
        await executor.ExecuteAsync(request with{Entity=JsonSerializer.SerializeToElement(new{id="7",amount=-1})},context);
        Assert.Equal(1,handler.Calls);Assert.Equal(1,fixture.Guard.Calls);Assert.Equal(1,fixture.Guard.Released);
    }
    /// <summary>普通按钮保持自身授权，不因页面设置只读条件就被统一增加修改权限。</summary>
    [Fact]
    public async Task Normal_button_does_not_inherit_writable_visibility_condition()
    {
        using var fixture=new Fixture(readOnly:"true");
        await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",fixture.Context("Preview"),Encoding.UTF8.GetBytes("{\"recordKey\":7}"));
        Assert.Equal(0,fixture.Authorization.PageChecks);
    }
    [Theory][InlineData("amount.toFixed(2)")][InlineData("amount = 1")][InlineData("this.entity.constructor")][InlineData("unknown < 0")]
    public void Unsupported_or_unbound_condition_is_not_published(string condition)
    {
        using var fixture=new Fixture("writable",readOnly:condition);fixture.Authorization.AllowPage=true;
        Assert.Null(fixture.Resources.AgentCatalog.FindButton("31"));Assert.Throws<UnauthorizedAccessException>(()=>fixture.Catalog.Describe("action:fixture.preview"));fixture.Catalog.List();
    }
    [Fact]
    public async Task Unloaded_condition_field_is_rejected_instead_of_becoming_writable()
    {
        using var fixture=new Fixture("writable",readOnly:"computedName === null");fixture.Authorization.AllowPage=true;
        await Assert.ThrowsAsync<NotSupportedException>(()=>fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",fixture.Context("Preview"),Encoding.UTF8.GetBytes("{\"recordKey\":7}")));Assert.Equal(0,fixture.Guard.Calls);
    }
}
