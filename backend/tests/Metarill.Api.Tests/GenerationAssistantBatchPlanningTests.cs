using Metarill.Api.GenerationAssistant;
using Metarill.Core.Security;
using Metarill.Generation;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>批量只读计划只分类发现结果，不能把配置可用误报为已授权或已应用。</summary>
public sealed class GenerationAssistantBatchPlanningTests
{
    private static AssistantDeviceScope Scope(string project)=>new(1,"11",project,"21",H('2'),H('3'),new(true,false),H('4'),null);
    private static string H(char value)=>new(value,64);
    [Fact]
    public void Plan_preserves_each_project_result_and_marks_configured_as_authorization_pending()
    {
        var discovery=new AssistantDeviceProjectDiscovery("11",[
            new("1","项目甲","21","configured",null,Scope("1")),
            new("2","项目乙","22","skipped","generation_disabled",null),
            new("3","项目丙","23","failed","configuration_invalid",null),
            new(null,"",null,"skipped","project_not_configured",null)]);
        var plan=GenerationAssistantBatchPlanning.Create(discovery);
        Assert.False(plan.CanCreateTasks);Assert.Equal("11",plan.DeviceId);Assert.Equal(4,plan.Items.Length);
        var configured=Assert.Single(plan.Items,item=>item.ProjectId=="1");Assert.Equal("needs-project-authorization",configured.Status);Assert.True(configured.RequiresProjectAuthorization);Assert.Null(configured.ErrorCode);
        Assert.Equal("skipped",Assert.Single(plan.Items,item=>item.ProjectId=="2").Status);Assert.Equal("failed",Assert.Single(plan.Items,item=>item.ProjectId=="3").Status);Assert.Equal("project_not_configured",Assert.Single(plan.Items,item=>item.ProjectId is null).ErrorCode);
    }
    [Theory]
    [InlineData("configured")][InlineData("unknown")]
    public void Plan_never_creates_task_when_configured_scope_is_missing(string status)
    {
        var plan=GenerationAssistantBatchPlanning.Create(new("11",[new("1","项目","21",status,null,null)]));
        Assert.False(plan.CanCreateTasks);var item=Assert.Single(plan.Items);Assert.Equal("failed",item.Status);Assert.Equal(status=="configured"?"scope_missing":"unknown_discovery_status",item.ErrorCode);Assert.False(item.RequiresProjectAuthorization);
    }
    [Fact]
    public void Plan_does_not_mutate_discovery_scope_or_accept_client_actor()
    {
        var scope=Scope("1");var discovery=new AssistantDeviceProjectDiscovery("11",[new("1","项目","21","configured",null,scope)]);var plan=GenerationAssistantBatchPlanning.Create(discovery);
        Assert.Equal(scope,discovery.Projects[0].Scope);Assert.DoesNotContain("token",System.Text.Json.JsonSerializer.Serialize(plan),StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("path",System.Text.Json.JsonSerializer.Serialize(plan),StringComparison.OrdinalIgnoreCase);
    }
}
