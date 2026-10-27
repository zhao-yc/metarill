using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Files;
using Metarill.Runtime.Security;
using ModelContextProtocol.Server;

namespace Metarill.Api.Mcp;

/// <summary>MCP 入口复用同一身份、生成 Service 与结构预览，不另设弱校验写入通道。</summary>
[McpServerToolType]
public sealed class PlatformTools
{
    [McpServerTool(Name="get_project_metadata",ReadOnly=true),Description("读取当前已授权项目的表、字段、页面与菜单目录，不包含数据库连接等环境配置。")]
    public static async Task<object> Metadata(MetadataRepository metadata,HttpRuntimeAuthorization authorization,CancellationToken token)
    {
        var actor=authorization.RequireAdministrator();var catalog=await metadata.LoadProjectAsync(actor.ProjectId,token);
        return new{catalog.YzProjectId,catalog.YzProjectName,
            tables=catalog.ConfigTables.Select(t=>new{t.Id,t.Name,t.TableName}),
            columns=catalog.ConfigColumns.Select(c=>new{c.Id,c.ConfigTableId,c.Name,c.ColumnName,c.LogicalType,c.ColumnLength,c.IsDbNotNull}),
            pages=catalog.ConfigPages.Select(p=>new{p.Id,p.ConfigTableId,p.Name,p.YzAdminTypeId}),menus=catalog.ConfigMenus};
    }
    [McpServerTool(Name="preview_database_schema",ReadOnly=true),Description("预览当前项目的数据库结构差异和 SQL，不执行 DDL。")]
    public static async Task<object> Preview(MetadataRepository metadata,ProjectSchemaService schemas,ProjectFileRuntimes files,HttpRuntimeAuthorization authorization,CancellationToken token)
    {
        var actor=authorization.RequireAdministrator();
        // 完整项目预览与HTTP入口相同，包含源头派生的同库文件基础结构。
        var physical=files.ComposePhysical(await metadata.LoadProjectAsync(actor.ProjectId,token));
        return await schemas.Resolve(actor.ProjectId).PreviewAsync(physical,token);
    }
    [McpServerTool(Name="inspect_database_schema", ReadOnly=true), Description("只读反查当前授权项目的受管表结构与指纹，响应executable=false；不执行DDL或反向导入元数据。")]
    public static Task<ProjectSchemaInspection> Inspect(JsonElement data, ProjectSchemaInspectionBusiness inspection, CancellationToken token) =>
        inspection.InspectAsync(data, token);

    [McpServerTool(Name="inspect_database_catalog", ReadOnly=true), Description("只读列出当前项目服务端数据库的表/视图名称、引擎和当前项目登记状态；共享库未登记不表示可导入，不读取业务数据，不写元数据或DDL。")]
    public static Task<ProjectSchemaCatalog> Catalog(JsonElement data, ProjectSchemaInspectionBusiness inspection, CancellationToken token) =>
        inspection.CatalogAsync(data, token);

    [McpServerTool(Name="call_generated_page",ReadOnly=false),Description("调用已发布页的强类型服务。operation 为 list/detail/create/update/delete/copy；写操作必须明确 confirmed=true。字段遵循生成 DTO，省略保持，null 清空。")]
    public static async Task<object?> Page(string pageId,string operation,JsonElement data,bool confirmed,MetadataRegistry registry,HttpRuntimeAuthorization authorization,IServiceProvider services,CancellationToken token,string? parentId=null)
    {
        var actor=authorization.RequireActor();authorization.RequirePagePermission(pageId,operation);
        var catalog=new[]{registry.Platform,registry.Demo}.SingleOrDefault(c=>c.ConfigPages.Any(p=>p.Id==pageId))??throw new InvalidOperationException("页面尚未发布。");
        if(authorization.RequireIdentity().GrantId is not null&&catalog.YzProjectId!=actor.ProjectId)throw new UnauthorizedAccessException("MCP页面不属于已批准的Agent项目。");
        var page=catalog.ConfigPages.Single(p=>p.Id==pageId);var table=catalog.ConfigTables.Single(t=>t.Id==page.ConfigTableId);
        var writing=operation is "create" or "update" or "delete" or "copy";
        if(writing&&!confirmed)throw new InvalidOperationException("写入前必须检查请求并明确确认。");
        if(writing&&authorization.RequireIdentity().GrantId is not null)throw new UnauthorizedAccessException("Agent 写请求须通过带请求任务保护的能力入口执行。");
        var root=catalog.YzProjectId==registry.Platform.YzProjectId?"Metarill.Platform":"Metarill.Demo";
        var tableName=string.Concat(table.TableName.Split('_').Select(part=>char.ToUpperInvariant(part[0])+part[1..]));
        var assembly=typeof(Program).Assembly;var serviceType=assembly.GetType(root+".Generated.Services."+tableName+"Service")??throw new InvalidOperationException("服务尚未发布。");
        var service=services.GetRequiredService(serviceType);string name;object?[] arguments;
        if(operation is "list" or "detail" or "delete" or "copy")
        {
            if(operation=="list"){name="Page"+pageId+"Async";arguments=[data.Deserialize<PageRequest>(JsonDefaults.Options)??throw new ArgumentException("分页请求为空。"),actor,token];}
            else
            {
                if(!data.TryGetProperty("id",out var key)||!long.TryParse(key.ToString(),NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0)throw new ArgumentException("请求缺少合法标识。");
                name=(operation=="detail"?"Detail":operation=="delete"?"Delete":"Copy")+pageId+"Async";arguments=[id,parentId,actor,token];
            }
        }
        else if(operation is "create" or "update")
        {
            name=(operation=="create"?"Create":"Update")+pageId+"Async";
            var dto=assembly.GetType(root+".Generated.DTOs."+tableName+"Page"+pageId+(operation=="create"?"Create":"Update")+"Request")??throw new InvalidOperationException("页面未开放该写入。");
            arguments=[data.Deserialize(dto,JsonDefaults.Options)??throw new ArgumentException("请求为空。"),parentId,actor,token];
        }
        else throw new ArgumentException("不支持的页面操作。");
        var method=serviceType.GetMethod(name,BindingFlags.Instance|BindingFlags.Public)??throw new InvalidOperationException("页面未开放该操作。");
        // 反射仅用于 MCP 工具目录分派；实际数据查询、映射和写入仍走已生成的固定类型服务。
        var task=(Task)method.Invoke(service,arguments)!;await task;return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
