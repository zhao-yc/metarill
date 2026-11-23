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
    /// <summary>原功能按钮必须从已授权的真实 Web 进入，不取得记录读取器，并拒绝伪造业务 ID。</summary>
    [Fact]
    public async Task Function_form_web_button_uses_the_registered_handler_without_loading_a_record()
    {
        using var fixture = new Fixture("function");
        var context = fixture.Context("Preview");
        var handler = new WebHandler();
        var executor = new OriginalBusinessButtonExecutor(fixture.Resources,fixture.Authorization,fixture.Services,
            fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>(),[handler],fixture.Guard);
        var request = new OriginalBusinessButtonRequest("31","",JsonSerializer.SerializeToElement(new { amount = 10 }));
        await Assert.ThrowsAsync<ArgumentException>(()=>executor.ExecuteAsync(request with { EntityId = "7" },context));
        await Assert.ThrowsAsync<ArgumentException>(()=>executor.ExecuteAsync(request with
            { Entity = JsonSerializer.SerializeToElement(new { id = "7" }) },context));
        Assert.Equal(0,handler.Calls);
        Assert.Equal(0,fixture.Guard.Calls);
        await executor.ExecuteAsync(request,context);
        Assert.Equal(1,handler.Calls);
        Assert.Equal(1,fixture.Guard.Calls);
        Assert.Equal(0,fixture.Reader.Calls);
    }

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
    /// <summary>Web 和 Agent 共用数据库 JSON 文本路径，按实际载入的锁定标志判断。</summary>
    [Fact]
    public async Task Writable_button_reads_declared_json_object_from_server_record()
    {
        using var fixture = new Fixture("writable", readOnly: "rulePayload.flags.locked === true && amount >= 0");
        fixture.Authorization.AllowPage = true;
        var context = fixture.Context("Preview");
        var handler = new WebHandler();
        var executor = new OriginalBusinessButtonExecutor(fixture.Resources, fixture.Authorization, fixture.Services,
            fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>(), [handler], fixture.Guard);
        var request = new OriginalBusinessButtonRequest("31", "7", JsonSerializer.SerializeToElement(new { id = "7" }), "Normal", "");
        var agentBody = Encoding.UTF8.GetBytes("""{"recordKey":7}""");

        fixture.Reader.Row.RulePayload = """{"flags":{"locked":true}}""";
        await Assert.ThrowsAsync<BusinessRuleException>(() => executor.ExecuteAsync(request, context));
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", context, agentBody));

        fixture.Reader.Row.RulePayload = """{"flags":{"locked":false}}""";
        await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", context, agentBody);
        await executor.ExecuteAsync(request, context);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>Web 和 Agent 用真实记录里的已发布 JSON 子字段判定，完整文本字段仍使用原比较语义。</summary>
    [Fact]
    public async Task Button_condition_reads_published_json_paths_from_server_record()
    {
        const string rule = """
        {"conditions":[{"field":"rule_payload.flags.audit","operator":"eq","value":9007199254740993},
                       {"field":"rule_payload","operator":"eq","value":"{\"flags\":{\"audit\":9007199254740993}}"}]}
        """;
        using var fixture = new Fixture(rule: rule);
        var context = fixture.Context("Preview");
        var handler = new WebHandler();
        var executor = new OriginalBusinessButtonExecutor(fixture.Resources,fixture.Authorization,fixture.Services,
            fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>(),[handler],fixture.Guard);
        var request = new OriginalBusinessButtonRequest("31", "7", JsonSerializer.SerializeToElement(new { id = "7", rulePayload = new { flags = new { audit = 9007199254740993L } } }));
        var arguments = Encoding.UTF8.GetBytes("""{"recordKey":7}""");

        foreach (var value in new[] { """{"flags":{"audit":9007199254740992}}""", """{"flags":[9007199254740993]}""", """{"flags":{""" })
        {
            fixture.Reader.Row.RulePayload = value;
            await Assert.ThrowsAsync<BusinessRuleException>(() => executor.ExecuteAsync(request, context));
            await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", context, arguments));
        }
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, fixture.Guard.Calls);

        fixture.Reader.Row.RulePayload = """{"flags":{"audit":9007199254740993}}""";
        await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", context, arguments);
        await executor.ExecuteAsync(request, context);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, fixture.Guard.Calls);
    }

    /// <summary>只发布实际页面配置中的 JSON 对象路径，拒绝文本、隐藏字段和不安全的路径段。</summary>
    [Theory]
    [InlineData("", "realName.status")]
    [InlineData("hidden-json", "rulePayload.flags.locked")]
    [InlineData("", "rulePayload.flags.constructor")]
    [InlineData("", "rulePayload..locked")]
    public void Button_json_paths_must_use_an_exposed_object(string variant, string path)
    {
        var rule = JsonSerializer.Serialize(new { conditions = new[] { new { field = path, @operator = "isBlank" } } });
        using var fixture = new Fixture(variant, rule: rule);
        Assert.Null(fixture.Resources.AgentCatalog.FindButton("31"));
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
