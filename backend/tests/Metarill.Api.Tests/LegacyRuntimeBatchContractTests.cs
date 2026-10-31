using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Api.GenerationAssistant;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>原页面共享请求会携带编码sysParams；parentId为独立列表上下文，不能替代设备选择或成为成功回执。</summary>
public sealed class LegacyRuntimeBatchContractTests
{
    /// <summary>直接列表parentId常为undefined，子列表则可能是别的实体ID；两者都沿真实原头选择设备。</summary>
    [Theory]
    [InlineData("")][InlineData("?parentId=")][InlineData("?parentId=undefined")][InlineData("?parentId=999")]
    public void Legacy_device_selection_comes_from_sysparams_not_parent(string query)
    {
        var request=new DefaultHttpContext().Request;request.QueryString=new(query);request.Headers["sysParams"]=Uri.EscapeDataString("{\"yzProjectId\":\"1\",\"yzManagerDevicesId\":\"9007199254740993\"}");
        Assert.Equal("9007199254740993",LegacyRuntimeBatchController.ReadDeviceId(request));
    }
    /// <summary>缺头、坏设备/重复键/重复头、坏正文与附加设备query都在查库前拒绝，不能回退parentId。</summary>
    [Theory]
    [InlineData("missing")][InlineData("numeric")][InlineData("empty")][InlineData("leading-zero")][InlineData("overflow")][InlineData("duplicate-key")][InlineData("case-key")][InlineData("duplicate-header")][InlineData("body")][InlineData("chunked")][InlineData("query")][InlineData("parent-repeat")]
    public void Legacy_invalid_device_context_is_rejected_without_parent_fallback(string kind)
    {
        var request=new DefaultHttpContext().Request;request.QueryString=new("?parentId=11");var text="{\"yzManagerDevicesId\":\"11\"}";
        if(kind=="numeric")text="{\"yzManagerDevicesId\":11}";if(kind=="empty")text="{}";if(kind=="leading-zero")text="{\"yzManagerDevicesId\":\"011\"}";if(kind=="overflow")text="{\"yzManagerDevicesId\":\"9223372036854775808\"}";
        if(kind=="duplicate-key")text="{\"yzManagerDevicesId\":\"11\",\"yzManagerDevicesId\":\"12\"}";if(kind=="case-key")text="{\"yzManagerDevicesId\":\"11\",\"YZMANAGERDEVICESID\":\"12\"}";
        request.Headers["sysParams"]=Uri.EscapeDataString(text);if(kind=="missing")request.Headers.Remove("sysParams");if(kind=="duplicate-header")request.Headers["sysParams"]=new Microsoft.Extensions.Primitives.StringValues([text,text]);if(kind=="body")request.ContentLength=2;if(kind=="chunked")request.Headers.TransferEncoding="chunked";if(kind=="query")request.QueryString=new("?parentId=11&deviceId=12");if(kind=="parent-repeat")request.QueryString=new("?parentId=11&parentId=12");
        Assert.Throws<InvalidOperationException>(()=>LegacyRuntimeBatchController.ReadDeviceId(request));
    }
    /// <summary>原UI按R200直接成功，所有未应用及空/仅跳过结果必须返回R500且保留逐项数据。</summary>
    [Theory]
    [InlineData("queued")][InlineData("leased")][InlineData("preview_ready")][InlineData("failed")][InlineData("expired")][InlineData("awaiting_application")][InlineData("manual_recovery")][InlineData("rolled_back")][InlineData("skipped")]
    public void Legacy_non_applied_outcomes_never_trigger_original_success_handler(string status)
    {
        var result=new AssistantRuntimeBatchEnqueueResult("11",[new("1","样例",status,null,"11111111-1111-4111-8111-111111111111")],status=="queued");var receipt=LegacyRuntimeBatchController.LegacyReceipt(result);
        Assert.Equal(500,receipt.Code);Assert.Same(result,receipt.Data);Assert.Contains("样例：",receipt.Msg!,StringComparison.Ordinal);Assert.Contains(result.Items[0].RequestId!,receipt.Msg!,StringComparison.Ordinal);Assert.DoesNotContain(status,receipt.Msg!,StringComparison.Ordinal);
    }
    /// <summary>真正applied加跳过可返回旧成功码；部分失败与空集合绝不成功。</summary>
    [Fact]
    public void Legacy_completion_requires_at_least_one_applied_and_no_pending_or_failed_projects()
    {
        var applied=new AssistantRuntimeBatchEnqueueItem("1","完成","applied",null,"11111111-1111-4111-8111-111111111111");
        Assert.Equal(200,LegacyRuntimeBatchController.LegacyReceipt(new("11",[applied,new("2","跳过","skipped",null,null)],false)).Code);
        Assert.Equal(500,LegacyRuntimeBatchController.LegacyReceipt(new("11",[applied,new("2","失败","failed",null,null)],false)).Code);Assert.Equal(500,LegacyRuntimeBatchController.LegacyReceipt(new("11",[],false)).Code);
    }
}
