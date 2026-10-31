using System.ComponentModel;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using ModelContextProtocol.Server;

namespace Metarill.Runtime.Mcp;

/// <summary>独立应用只发布实际强类型页面工具；平台设计器工具继续由平台宿主提供。</summary>
[McpServerToolType]
public sealed class GeneratedPageMcpTools
{
    /// <summary>返回当前账号真正可调用的固定页面及动作，不把菜单可见性当作写权。</summary>
    [McpServerTool(Name="list_generated_pages",ReadOnly=true),Description("列出当前已认证身份可调用的已发布页面及动作；不包含数据库连接或任意SQL。Agent仅列出MCP只读动作。")]
    public static JsonElement List(PublishedPageInvoker invoker,IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities)=>
        JsonSerializer.SerializeToElement(invoker.List(authorization,identities),JsonDefaults.Options);

    /// <summary>按当前动作权限查询真实请求结构，不解析Service或返回默认配置。</summary>
    [McpServerTool(Name="describe_generated_page",ReadOnly=true),Description("描述已发布页面某动作的data字段、ID、parent位置及确认要求。operation为list/detail/create/update/delete/copy；Agent只允许读取动作。业务校验仍以原Service为准。")]
    public static JsonElement Describe(string pageId,string operation,PublishedPageInvoker invoker,IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities)=>
        JsonSerializer.SerializeToElement(invoker.Describe(pageId,operation,authorization,identities),JsonDefaults.Options);

    /// <summary>保留原工具名及参数；非字符串结果先应用字符串ID/日期/可空值的公共JSON合同。</summary>
    [McpServerTool(Name="call_generated_page",ReadOnly=false),Description("调用已发布页的强类型服务。operation 为 list/detail/create/update/delete/copy；写操作必须明确 confirmed=true。字段遵循生成 DTO，省略保持，null 清空。Agent仅允许读取，写入须使用带任务保护的能力入口。")]
    public static async Task<object?> Page(string pageId,string operation,JsonElement data,bool confirmed,PublishedPageInvoker invoker,IRuntimeAuthorization authorization,ISecurityIdentityAccessor identities,IServiceProvider services,CancellationToken token,string? parentId=null)
    {
        var result=await invoker.InvokeAsync(pageId,operation,data,confirmed,authorization,identities,services,token,parentId);
        return result is string?result:JsonSerializer.SerializeToElement(result,JsonDefaults.Options);
    }
}
