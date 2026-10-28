using Metarill.Runtime.Mcp;
using System.ComponentModel;
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

    [McpServerTool(Name="preview_database_metadata_import", ReadOnly=true), Description("只读检查当前项目未登记表并生成逻辑元数据草案与缺口；符号ID不是配置ID，canImport=false；不保存元数据、不补字段或页面、不执行DDL。")]
    public static Task<ProjectSchemaImportPreview> PreviewImport(JsonElement data, ProjectSchemaInspectionBusiness inspection, CancellationToken token) =>
        inspection.PreviewImportAsync(data, token);

    [McpServerTool(Name="prepare_database_metadata_import", ReadOnly=true), Description("建立当前项目表/字段/索引配置的审核计划，不保存、不DDL、不建页面；后续必须明确确认摘要。仅真实Web平台管理员。")]
    public static Task<SchemaMetadataImportPlan> PrepareMetadataImport(JsonElement data, SchemaMetadataImportBusiness import, CancellationToken token) => import.PrepareAsync(data, token);
    [McpServerTool(Name="apply_database_metadata_import", ReadOnly=false), Description("明确确认服务端计划，只在原配置事务保存表/字段/索引；confirmed必须true，不DDL、不写源业务数据、不建页面。")]
    public static Task<SchemaMetadataImportResult> ApplyMetadataImport(JsonElement data, bool confirmed, SchemaMetadataImportBusiness import, CancellationToken token)
    { if (!confirmed) throw new InvalidOperationException("配置导入必须明确确认已审核的计划。"); return import.ApplyAsync(data, token); }
    [McpServerTool(Name="get_database_metadata_import_status", ReadOnly=true), Description("只读查询同身份同摘要的有限配置回执；unknown不能表示未写入，不得自动重复应用。")]
    public static Task<SchemaMetadataImportStatus> MetadataImportStatus(JsonElement data, SchemaMetadataImportBusiness import, CancellationToken token) => import.StatusAsync(data, token);

    /// <summary>平台与完整应用使用相同的请求结构及逐次页权检查。</summary>
    [McpServerTool(Name="describe_generated_page",ReadOnly=true),Description("按当前页面动作权限描述真实生成DTO字段及parent/确认合同，不返回默认值或连接。")]
    public static JsonElement DescribePage(string pageId,string operation,PublishedPageInvoker invoker,HttpRuntimeAuthorization authorization)=>
        GeneratedPageMcpTools.Describe(pageId,operation,invoker,authorization,authorization);

    /// <summary>平台保持原工具及权限合同，六种强类型业务操作复用公共执行器。</summary>
    [McpServerTool(Name="call_generated_page",ReadOnly=false),Description("调用已发布页的强类型服务。operation 为 list/detail/create/update/delete/copy；写操作必须明确 confirmed=true。字段遵循生成 DTO，省略保持，null 清空。")]
    public static Task<object?> Page(string pageId,string operation,JsonElement data,bool confirmed,PublishedPageInvoker invoker,HttpRuntimeAuthorization authorization,IServiceProvider services,CancellationToken token,string? parentId=null)=>
        GeneratedPageMcpTools.Page(pageId,operation,data,confirmed,invoker,authorization,authorization,services,token,parentId);
}
