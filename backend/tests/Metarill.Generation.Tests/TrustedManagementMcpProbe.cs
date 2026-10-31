using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>当前生产Program的真实Web/MCP/MySQL探针；只操作原夹具的独占项目，令牌只在内存请求中使用。</summary>
internal sealed class TrustedManagementMcpProbe:IAsyncDisposable
{
    private const string RolePage="1692532858529329154";
    private readonly HttpClient http;private readonly HttpClientTransport transport;private readonly McpClient client;
    private readonly SqlRowStore store;private readonly long project,admin;private readonly string name,evidence;
    private long? roleId;private string stage="connected";
    /// <summary>持有本探针唯一HTTP/协议客户端；不自建账号、连接设置或持久令牌文件。</summary>
    private TrustedManagementMcpProbe(HttpClient http,HttpClientTransport transport,McpClient client,SqlRowStore store,long project,long admin,string name,string evidence)
    {this.http=http;this.transport=transport;this.client=client;this.store=store;this.project=project;this.admin=admin;this.name=name;this.evidence=evidence;}

    /// <summary>匿名请求先拒绝，再用原登录令牌初始化实际SDK Streamable HTTP客户端。</summary>
    internal static async Task<TrustedManagementMcpProbe> StartAsync(string address,string token,SqlRowStore store,long project,long admin,string name,string evidence)
    {
        var http=new HttpClient{BaseAddress=new(address),Timeout=TimeSpan.FromSeconds(20)};
        HttpClientTransport? transport=null;McpClient? client=null;
        try
        {
            using(var response=await http.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=1,method="tools/list",@params=new{}}))Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
            http.DefaultRequestHeaders.Add("loginStateId",token);
            transport=new(new(){Endpoint=new Uri(http.BaseAddress!,"api/mcp"),TransportMode=HttpTransportMode.StreamableHttp},http,ownsHttpClient:false);
            client=await McpClient.CreateAsync(transport);
            var probe=new TrustedManagementMcpProbe(http,transport,client,store,project,admin,name,evidence);await probe.ReceiptAsync();return probe;
        }
        catch{if(client is not null)await client.DisposeAsync();if(transport is not null)await transport.DisposeAsync();http.Dispose();throw;}
    }

    /// <summary>页权下描述/读写都走真实生成Service；不确认写入无新增，成功结果用原库读回，删除检查墓碑。</summary>
    internal async Task VerifyOperationsAsync()
    {
        var tools=await client.ListToolsAsync();Assert.Equal(new[]{"call_generated_page","describe_generated_page","list_generated_pages"},tools.Select(tool=>tool.Name).Order(StringComparer.Ordinal));
        var listed=Text(await Tool("list_generated_pages",new()));Assert.Contains(listed.EnumerateArray(),page=>page.GetProperty("id").GetString()==RolePage);
        var described=Text(await Tool("describe_generated_page",new(){["pageId"]=RolePage,["operation"]="update"}));var schema=described.GetProperty("dataSchema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());Assert.Contains("id",schema.GetProperty("required").EnumerateArray().Select(item=>item.GetString()));
        Assert.True(schema.GetProperty("properties").TryGetProperty("name",out _));Assert.False(schema.GetProperty("properties").TryGetProperty("connectionString",out _));
        stage="discovery";await ReceiptAsync();
        var before=(await Roles()).Count;var request=new{name=name+"_mcprole",sortNo=11,isSuper=0,yzAdminTypeId=admin.ToString(CultureInfo.InvariantCulture)};
        var unconfirmed=await Tool("call_generated_page",Arguments("create",request,false));Assert.True(unconfirmed.IsError);Assert.Equal(before,(await Roles()).Count);
        var created=Text(await Tool("call_generated_page",Arguments("create",request,true)));roleId=long.Parse(created.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);
        var row=await Role();Assert.Equal(name+"_mcprole",SqlRowStore.Text(row,"name"));Assert.Equal(project,SqlRowStore.Id(row,"yz_project_id"));Assert.Equal(before+1,(await Roles()).Count);
        await Success("update",new{id=roleId.ToString(),name=name+"_mcpupdated",sortNo=12});row=await Role();Assert.Equal(name+"_mcpupdated",SqlRowStore.Text(row,"name"));Assert.Equal("12",SqlRowStore.Text(row,"sort_no"));
        var detail=await Success("detail",new{id=roleId.ToString()});Assert.Equal(roleId.ToString(),detail.GetProperty("id").GetString());
        var page=await Success("list",new{pageNo=1,pageSize=20});Assert.Contains(page.GetProperty("records").EnumerateArray(),record=>record.GetProperty("id").GetString()==roleId.ToString());
        // 未知字段不能进入写入；错误后完整目标行保持原摘要，不能只看工具错误标记。
        var fingerprint=Fingerprint(await Role());var invalid=await Tool("call_generated_page",Arguments("update",new{id=roleId.ToString(),sql="SELECT 1"},true));Assert.True(invalid.IsError);Assert.Equal(fingerprint,Fingerprint(await Role()));
        await Success("delete",new{id=roleId.ToString()});row=await Role();Assert.False(string.IsNullOrWhiteSpace(SqlRowStore.Text(row,"delete_no")));
        stage="crud-verified";await ReceiptAsync();
    }

    /// <summary>原工作台撤销动作后复用同一客户端，列表/结构/读取立即重新检查页权；不写新授权。</summary>
    internal async Task VerifyRevocationAsync()
    {
        var fingerprint=Fingerprint(await Role());
        Assert.True((await Tool("describe_generated_page",new(){["pageId"]=RolePage,["operation"]="list"})).IsError);
        Assert.True((await Tool("call_generated_page",Arguments("list",new{},false))).IsError);
        var listed=Text(await Tool("list_generated_pages",new()));Assert.Empty(listed.EnumerateArray());Assert.Equal(fingerprint,Fingerprint(await Role()));
        stage="revocation-verified";await ReceiptAsync();
    }

    /// <summary>原HTTP注销后的同一真实令牌在MCP协议前返回401。</summary>
    internal async Task VerifyLogoutAsync()
    {
        using var response=await http.PostAsJsonAsync("/api/mcp",new{jsonrpc="2.0",id=2,method="tools/list",@params=new{}});Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
        stage="logout-verified";await ReceiptAsync();
    }
    /// <summary>协议SDK实际发送工具请求，不借测试端点或反射直接调用工具。</summary>
    private Task<CallToolResult> Tool(string tool,Dictionary<string,object?> arguments)=>client.CallToolAsync(tool,arguments).AsTask();
    /// <summary>只构造原固定六操作参数，不把实例、项目或actor当作客户端输入。</summary>
    private static Dictionary<string,object?> Arguments(string operation,object data,bool confirmed)=>new(){["pageId"]=RolePage,["operation"]=operation,["data"]=data,["confirmed"]=confirmed};
    /// <summary>校验实际工具结果，写入确认对读操作也不会额外授予页权。</summary>
    private async Task<JsonElement> Success(string operation,object data)=>Text(await Tool("call_generated_page",Arguments(operation,data,true)));
    /// <summary>只反序列化实际TextContent；错误不输出正文、口令或令牌。</summary>
    private static JsonElement Text(CallToolResult result){Assert.NotEqual(true,result.IsError);return JsonSerializer.Deserialize<JsonElement>(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);}
    /// <summary>独占项目的原持久化行包含墓碑；不得读取其它项目来证明结果。</summary>
    private Task<List<Dictionary<string,object?>>> Roles()=>store.ReadIncludingDeletedAsync("yz_role",new Dictionary<string,object?>{{"yz_project_id",project}});
    /// <summary>原库读回同时检查主键与项目，不以客户端成功响应代替持久化。</summary>
    private async Task<Dictionary<string,object?>> Role()=>Assert.Single(await store.ReadIncludingDeletedAsync("yz_role",new Dictionary<string,object?>{{"id",roleId},{"yz_project_id",project}}));
    /// <summary>只记录摘要，异常断言不打印业务行。</summary>
    private static string Fingerprint(object value)=>Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    /// <summary>持久回执只含阶段和自有标识，凭据与请求正文不落盘。</summary>
    private Task ReceiptAsync()=>File.WriteAllTextAsync(Path.Combine(evidence,"mcp-probe.json"),JsonSerializer.Serialize(new{stage,project,admin,roleId,persistedWebIdentity=true,actualSdkTransport=true,actualGeneratedServices=true,ddlExecuted=false,initializationExecuted=false})+"\n");
    /// <summary>只释放本探针的协议/HTTP客户端；数据由原夹具所有权清理finally处理。</summary>
    public async ValueTask DisposeAsync(){try{await client.DisposeAsync();}finally{try{await transport.DisposeAsync();}finally{http.Dispose();}}}
}
