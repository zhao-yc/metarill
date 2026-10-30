using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Api.Controllers;
using Metarill.Api.Mcp;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>真实SDK/Streamable HTTP与原Controller共用真实业务；认证状态和ADO传输是本地夹具。</summary>
public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>工具发现、原JSON envelope、原HTTP快照和MCP迁组必须对应同一数据库事务及版本。</summary>
    [Fact]
    public async Task Layout_mcp_and_original_http_share_snapshot_save_and_conflict_contracts()
    {
        await using var run=await LayoutMcpRun.Start();
        var tools=await run.Mcp.ListToolsAsync();
        Assert.Equal(new[]{"getPageDetailLayout","savePageDetailLayout"},tools.Select(tool=>tool.Name).Order(StringComparer.Ordinal));
        var get=Assert.Single(tools,tool=>tool.Name=="getPageDetailLayout");
        Assert.Equal(new[]{"pageId"},get.JsonSchema.GetProperty("properties").EnumerateObject().Select(item=>item.Name));
        var save=Assert.Single(tools,tool=>tool.Name=="savePageDetailLayout");
        Assert.Equal(new[]{"request"},save.JsonSchema.GetProperty("properties").EnumerateObject().Select(item=>item.Name));
        var request=save.JsonSchema.GetProperty("properties").GetProperty("request");
        Assert.Equal(new[]{"detailLayout","fields","pageId","revision"},request.GetProperty("required").EnumerateArray().Select(item=>item.GetString()).Order(StringComparer.Ordinal));
        Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[]{"groups","mode","version"},request.GetProperty("properties").GetProperty("detailLayout").GetProperty("required").EnumerateArray().Select(item=>item.GetString()).Order(StringComparer.Ordinal));
        Assert.False(request.GetProperty("properties").GetProperty("fields").GetProperty("items").GetProperty("additionalProperties").GetBoolean());

        var read=await run.Mcp.CallToolAsync("getPageDetailLayout",new Dictionary<string,object?>{{"pageId","30"}});
        Assert.NotEqual(true,read.IsError);var original=ToolJson(read);Assert.True(original.GetProperty("ok").GetBoolean());
        Assert.Equal("详情布局获取成功",original.GetProperty("message").GetString());var snapshot=original.GetProperty("data");
        var http=await run.HttpSnapshot();Assert.True(JsonNode.DeepEquals(JsonNode.Parse(http.GetRawText()),JsonNode.Parse(snapshot.GetRawText())));
        Assert.Equal("20",Assert.Single(snapshot.GetProperty("fields").EnumerateArray()).GetProperty("columnId").GetString());
        var revision=snapshot.GetProperty("revision").GetString()!;var input=LayoutMcpInput(revision);
        var written=await run.Mcp.CallToolAsync("savePageDetailLayout",new Dictionary<string,object?>{{"request",input}});
        Assert.NotEqual(true,written.IsError);var saved=ToolJson(written);Assert.True(saved.GetProperty("ok").GetBoolean());
        Assert.Equal("new_group",Assert.Single(saved.GetProperty("data").GetProperty("fields").EnumerateArray()).GetProperty("detailGroupKey").GetString());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(saved.GetProperty("data").GetRawText()),JsonNode.Parse((await run.HttpSnapshot()).GetRawText())));
        var before=run.Fixture.Snapshot();var conflict=await run.Mcp.CallToolAsync("savePageDetailLayout",new Dictionary<string,object?>{{"request",input}});
        Assert.True(conflict.IsError);var error=ToolJson(conflict).GetProperty("error");
        Assert.Equal("YZP_CONFLICT",error.GetProperty("code").GetString());Assert.Equal("not_applied",error.GetProperty("outcome").GetString());
        Assert.Equal("refresh_and_replan",error.GetProperty("recovery").GetString());Assert.False(error.GetProperty("retryable").GetBoolean());
        Assert.True(Guid.TryParse(error.GetProperty("traceId").GetString(),out _));Assert.Equal(before,run.Fixture.Snapshot());
        using var httpConflict=await run.Http.PostAsJsonAsync("/api/admin/custom/page_detail_layout/save",input);
        var body=await httpConflict.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(409,body.GetProperty("code").GetInt32());Assert.Equal(before,run.Fixture.Snapshot());
    }

    /// <summary>未知/缺失/非法嵌套字段在数据库前拒绝，SDK绑定不能把额外输入吞掉后交给聚合。</summary>
    [Theory]
    [InlineData("unknown")][InlineData("layoutUnknown")][InlineData("fieldUnknown")][InlineData("groupUnknown")]
    [InlineData("missingRevision")][InlineData("missingGroupKey")][InlineData("nullFields")][InlineData("longGroupKey")][InlineData("invalidPageId")][InlineData("invalidFieldId")][InlineData("invalidRevision")][InlineData("invalidRevisionNewline")]
    public async Task Layout_mcp_rejects_invalid_arguments_before_any_database_access(string defect)
    {
        await using var run=await LayoutMcpRun.Start();var snapshot=await run.HttpSnapshot();var input=JsonSerializer.SerializeToNode(LayoutMcpInput(snapshot.GetProperty("revision").GetString()!))!;
        if(defect=="unknown")input["projectId"]="another-project";
        if(defect=="layoutUnknown")input["detailLayout"]!["sql"]="SELECT secret";
        if(defect=="fieldUnknown")input["fields"]![0]!["isInEdit"]=0;
        if(defect=="groupUnknown")input["detailLayout"]!["groups"]![0]!["secret"]="不能回显的内部字段";
        if(defect=="missingRevision")input.AsObject().Remove("revision");
        if(defect=="missingGroupKey")input["detailLayout"]!["groups"]![0]!.AsObject().Remove("key");
        if(defect=="invalidPageId")input["pageId"]="0";
        if(defect=="invalidFieldId")input["fields"]![0]!["pageColumnId"]="invalid-id";
        if(defect=="invalidRevision")input["revision"]="invalid-revision";
        if(defect=="invalidRevisionNewline")input["revision"]=new string('a',64)+"\n";
        if(defect=="nullFields")input["fields"]=null;
        if(defect=="longGroupKey")input["detailLayout"]!["groups"]![0]!["key"]=new string('a',65);
        var before=run.Fixture.Snapshot();var commands=run.Fixture.Provider.Commands.Count;
        var result=await run.Mcp.CallToolAsync("savePageDetailLayout",new Dictionary<string,object?>{{"request",JsonSerializer.SerializeToElement(input)}});
        Assert.True(result.IsError);Assert.Equal(before,run.Fixture.Snapshot());Assert.Equal(commands,run.Fixture.Provider.Commands.Count);
        if(defect is "nullFields" or "longGroupKey" or "invalidPageId" or "invalidFieldId" or "invalidRevision" or "invalidRevisionNewline")
        {
            var error=ToolJson(result).GetProperty("error");Assert.Equal("YZP_INVALID_ARGUMENT",error.GetProperty("code").GetString());
            Assert.Equal("not_applied",error.GetProperty("outcome").GetString());Assert.Equal("correct_arguments",error.GetProperty("recovery").GetString());
            Assert.False(error.GetProperty("retryable").GetBoolean());
        }
    }

    /// <summary>快照ID参数也必须在事务前分类，不能返回宽范围或触发数据库读取。</summary>
    [Theory][InlineData("0")][InlineData("invalid-id")]
    public async Task Layout_mcp_snapshot_invalid_id_is_rejected_before_database_access(string pageId)
    {
        await using var run=await LayoutMcpRun.Start();var before=run.Fixture.Provider.Commands.Count;
        var result=await run.Mcp.CallToolAsync("getPageDetailLayout",new Dictionary<string,object?>{{"pageId",pageId}});
        Assert.True(result.IsError);var error=ToolJson(result).GetProperty("error");
        Assert.Equal("YZP_INVALID_ARGUMENT",error.GetProperty("code").GetString());Assert.Equal("not_applied",error.GetProperty("outcome").GetString());
        Assert.Equal(before,run.Fixture.Provider.Commands.Count);
    }

    /// <summary>工具不接收身份/项目选择；缺少身份及非管理员在SQL前拒绝，换项目也不读取原布局。</summary>
    [Theory][InlineData("anonymous")][InlineData("ordinary")][InlineData("project")]
    public async Task Layout_mcp_keeps_current_identity_and_project_guards(string defect)
    {
        await using var run=await LayoutMcpRun.Start();var before=run.Fixture.Snapshot();var commands=run.Fixture.Provider.Commands.Count;
        if(defect=="anonymous")run.Identity=null;
        if(defect=="ordinary")run.Identity=run.Identity! with{Actor=run.Identity.Actor with{IsAdministrator=false}};
        if(defect=="project")run.Identity=run.Identity! with{Actor=run.Identity.Actor with{ProjectId="2"}};
        foreach(var tool in new[]{"getPageDetailLayout","savePageDetailLayout"})
        {
            Dictionary<string,object?> arguments=tool=="getPageDetailLayout"?new(){{"pageId","30"}}:new(){{"request",LayoutMcpInput(new string('a',64))}};
            var result=await run.Mcp.CallToolAsync(tool,arguments);Assert.True(result.IsError);var response=ToolJson(result);Assert.False(response.GetProperty("ok").GetBoolean());Assert.False(response.GetProperty("error").GetProperty("retryable").GetBoolean());
            if(defect!="project")
            {
                Assert.Equal(commands,run.Fixture.Provider.Commands.Count);
                Assert.Equal(defect=="anonymous"?"YZP_UNAUTHENTICATED":"YZP_FORBIDDEN",response.GetProperty("error").GetProperty("code").GetString());
            }
            Assert.Equal(before,run.Fixture.Snapshot());
        }
        using var anonymous=run.App.GetTestClient();using var denied=await anonymous.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=3,method="tools/list",@params=new{}});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
    }

    /// <summary>执行后异常不能被当作安全重试，原内部异常和连接正文不进入工具输出。</summary>
    [Fact]
    public async Task Layout_mcp_unknown_write_result_is_redacted_and_requires_read_back()
    {
        await using var run=await LayoutMcpRun.Start();var snapshot=await run.HttpSnapshot();
        const string secret="不应输出的数据库连接-secret-60928";
        run.Fixture.Provider.SkipWrite=(_,_)=>throw new InvalidOperationException(secret);
        var before=run.Fixture.Snapshot();var result=await run.Mcp.CallToolAsync("savePageDetailLayout",new Dictionary<string,object?>{{"request",LayoutMcpInput(snapshot.GetProperty("revision").GetString()!)}});
        Assert.True(result.IsError);var raw=Assert.Single(result.Content.OfType<TextContentBlock>()).Text;Assert.DoesNotContain(secret,raw);
        var error=ToolJson(result).GetProperty("error");Assert.Equal("unknown",error.GetProperty("outcome").GetString());Assert.Equal("read_back_before_retry",error.GetProperty("recovery").GetString());Assert.False(error.GetProperty("retryable").GetBoolean());
        Assert.Equal(before,run.Fixture.Snapshot());
    }

    /// <summary>使用原请求字段，不放入服务、身份或SQL参数。</summary>
    private static object LayoutMcpInput(string revision)=>new{pageId="30",revision,detailLayout=new{version=1,mode="tabs",groups=new[]{new{key="new_group",name="新组"}}},fields=new[]{new{pageColumnId="40",detailGroupKey="new_group"}}};
    /// <summary>工具结果必须是实际传输的原JSON文本，不读取服务端中间对象作证。</summary>
    private static JsonElement ToolJson(CallToolResult result)=>JsonSerializer.Deserialize<JsonElement>(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);

    /// <summary>本地测试宿主只替代认证状态与ADO，工具、Controller、授权器、聚合及事务均为实际源码。</summary>
    private sealed class LayoutMcpRun:IAsyncDisposable
    {
        internal readonly Fixture Fixture=new();internal WebApplication App=null!;internal HttpClient Http=null!;internal McpClient Mcp=null!;
        internal AuthenticatedIdentity? Identity=new(new(8,"布局MCP离线验收",true,"1",""),new HashSet<string>(),1,null);
        /// <summary>TestServer没有外部监听，不访问私有配置或真实数据库。</summary>
        internal static async Task<LayoutMcpRun> Start()
        {
            var run=new LayoutMcpRun();
            try
            {
                run.Fixture.Get("config_page",30)["detail_layout"]=Layout("base");run.Fixture.Get("config_page_column",40)["detail_group_key"]="base";
                var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.Logging.ClearProviders();builder.WebHost.UseTestServer();
                builder.Services.AddSingleton(run.Fixture.Registry);builder.Services.AddSingleton(run.Fixture.Store);builder.Services.AddSingleton(run.Fixture.Observer);builder.Services.AddSingleton<IIdGenerator>(run.Fixture.Ids);
                builder.Services.AddHttpContextAccessor();builder.Services.AddScoped<HttpRuntimeAuthorization>();builder.Services.AddScoped<MetadataWorkbenchBusiness>();
                builder.Services.AddMcpServer().WithHttpTransport(options=>options.SessionMode=HttpServerSessionMode.Stateless).WithTools<MetadataWorkbenchMcpTools>();
                builder.Services.AddControllers().AddApplicationPart(typeof(MetadataWorkbenchController).Assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));
                run.App=builder.Build();
                run.App.Use(async(context,next)=>
                {
                    // 此固定头只属于测试夹具，不是产品认证或部署凭据。
                    if(context.Request.Headers["loginStateId"]!="layout-offline-session"){context.Response.StatusCode=401;return;}
                    if(run.Identity is {} identity)context.Items[typeof(AuthenticatedIdentity)]=identity;
                    await next(context);
                });
                run.App.MapControllers();run.App.MapMcp("/api/mcp");await run.App.StartAsync();
                run.Http=run.App.GetTestClient();run.Http.DefaultRequestHeaders.Add("loginStateId","layout-offline-session");
                var transport=new HttpClientTransport(new(){Endpoint=new Uri(run.Http.BaseAddress!,"api/mcp"),TransportMode=HttpTransportMode.StreamableHttp},run.Http,ownsHttpClient:false);
                run.Mcp=await McpClient.CreateAsync(transport);return run;
            }
            catch{await run.DisposeAsync();throw;}
        }
        /// <summary>HTTP快照走原Controller/R协议，不能直接调用聚合冒充HTTP往返。</summary>
        internal async Task<JsonElement> HttpSnapshot()
        {
            using var result=await Http.PostAsJsonAsync("/api/admin/custom/page_detail_layout/snapshot",new{pageId="30"});
            var body=await result.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(200,body.GetProperty("code").GetInt32());return body.GetProperty("data");
        }
        /// <summary>释放本轮SDK/宿主及内存连接，不终止用户服务。</summary>
        public async ValueTask DisposeAsync()
        {
            if(Mcp is not null)await Mcp.DisposeAsync();Http?.Dispose();
            if(App is not null){await App.StopAsync();await App.DisposeAsync();}
            Fixture.Dispose();
        }
    }
}
