using System.Globalization;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Compatibility;

public sealed record CompatibilityButtonResult(string Type, string? Path = null, string? ProjectId = null);
public sealed record CompatibilityButtonAction(string Route, string EntityTable, string Operation);

/// <summary>旧按钮仅解释已登记的路由白名单；sql 字段永远不作为 SQL、脚本或可执行地址使用。</summary>
public static class CompatibilityButtonPolicy
{
    private static readonly IReadOnlyDictionary<string, CompatibilityButtonAction> Actions = new Dictionary<string, CompatibilityButtonAction>(StringComparer.Ordinal)
    {
        ["/admin/custom/config_table/config_columns_sort"] = new("", "config_table", "sort"),
        ["/admin/custom/config_table/config_indexes"] = new("", "config_table", "indexes"),
        ["/admin/custom/config_table/add_join_columns"] = new("", "config_table", "joins"),
        ["/admin/custom/config_table/batch_create_columns"] = new("", "config_table", "batch"),
        ["/admin/custom/config_table/create_table"] = new("", "config_table", "schema"),
        ["/admin/custom/config_table/generate_all"] = new("", "config_table", "generate"),
        ["/admin/custom/config_page/config_page_columns"] = new("", "config_page", "fields"),
        ["/admin/custom/config_page/batch_create_columns"] = new("", "config_page", "batch"),
        ["/admin/custom/config_page/config_columns_sort"] = new("", "config_page", "sort"),
        ["/admin/custom/config_page/to_config_table"] = new("", "config_page", "table"),
        ["/admin/custom/config_page/detail_layout"] = new("", "config_page", "layout"),
        ["/admin/custom/config_page/generate_all"] = new("", "config_page", "generate"),
        ["/admin/custom/config_column/add_column_to_db"] = new("", "config_column", "schema"),
        ["/admin/custom/index/init_database"] = new("", "yz_project", "schema")
    };

    /// <summary>管理员、真实按钮、真实配置页面三者同时匹配后才解释路由；未知动作一律拒绝。</summary>
    public static CompatibilityButtonAction RequireAction(RuntimeActor actor, MetadataRegistry registry, IReadOnlyDictionary<string, object?> button)
    {
        if (!actor.IsAdministrator) throw new UnauthorizedAccessException("需要平台管理员权限。");
        if (!long.TryParse(actor.ProjectId, out var project) || project <= 0) throw new InvalidOperationException("缺少当前项目上下文。");
        if (SqlRowStore.Text(button, "yz_project_id") != registry.Platform.YzProjectId || SqlRowStore.Text(button, "action_type") != "1")
            throw new InvalidOperationException("按钮不属于已发布的平台接口按钮。");
        var page = registry.Platform.ConfigPages.SingleOrDefault(p => p.Id == SqlRowStore.Text(button, "config_page_id"))
            ?? throw new InvalidOperationException("按钮所属配置页面未登记。");
        var table = registry.Platform.ConfigTables.Single(t => t.Id == page.ConfigTableId);
        var route = SqlRowStore.Text(button, "sql");
        if (route is "/admin/custom/config_table/delete_table" or "/admin/custom/config_column/drop_column_to_db")
            throw new InvalidOperationException("暂不支持删除数据库表或字段；该按钮不会执行任何结构操作。");
        if (route == "/admin/custom/index/init_admin")
            throw new InvalidOperationException("禁止通过兼容按钮初始化账号或重建平台元数据。");
        if (!Actions.TryGetValue(route, out var action)) throw new InvalidOperationException("该按钮动作尚未移植或未登记在安全白名单中，未执行任何操作。");
        if (action.EntityTable != table.TableName) throw new InvalidOperationException("按钮动作与所属配置页面不匹配。");
        return action with { Route = route };
    }

    /// <summary>只从数据库实体计算跳转参数，不使用客户端 entity 中的项目、主键、路由或表归属。</summary>
    public static CompatibilityButtonResult Resolve(RuntimeActor actor, CompatibilityButtonAction action, IReadOnlyDictionary<string, object?> entity, IReadOnlyDictionary<string, object?>? parentTable = null)
    {
        if (!actor.IsAdministrator) throw new UnauthorizedAccessException("需要平台管理员权限。");
        var entityId = RequireId(SqlRowStore.Text(entity, "id"));
        var owner = action.EntityTable == "yz_project" ? entityId : SqlRowStore.Text(entity, "yz_project_id");
        if (owner != actor.ProjectId) throw new UnauthorizedAccessException("配置对象不属于当前项目。");
        var tableId = action.EntityTable == "config_table" ? entityId : action.EntityTable == "yz_project" ? "" : RequireId(SqlRowStore.Text(entity, "config_table_id"));
        if (action.EntityTable is "config_page" or "config_column")
        {
            if (parentTable is null || SqlRowStore.Text(parentTable, "id") != tableId || SqlRowStore.Text(parentTable, "yz_project_id") != actor.ProjectId)
                throw new UnauthorizedAccessException("配置对象关联表不属于当前项目。");
        }
        string Query(string key, string value) => key + "=" + Uri.EscapeDataString(value);
        var tableQuery = Query("config_table_id", tableId);
        return action.Operation switch
        {
            "sort" => new("path", "/pages/custom/config_columns_sort?" + tableQuery),
            "indexes" => new("path", "/pages/custom/config_indexes?" + tableQuery),
            "joins" => new("path", "/pages/custom/add_join_columns?" + tableQuery),
            "batch" => new("path", "/pages/custom/batch_create_columns?" + tableQuery),
            "fields" => new("path", "/pages/custom/config_page_columns?" + Query("config_page_id", entityId)),
            "layout" => new("path", "/pages/custom/page_detail_layout?" + Query("pageId", entityId)),
            "table" => new("path", "/pages/yzp/config_table-detail-6191083005411328?" + Query("id", tableId)),
            "schema" => new("path", "/pages/custom/schema_sync?" + Query("projectId", actor.ProjectId) + (tableId.Length == 0 ? "" : "&" + Query("tableId", tableId))),
            "generate" => new("metarillGenerate", ProjectId: actor.ProjectId),
            _ => throw new InvalidOperationException("该按钮动作尚未移植，未执行任何操作。")
        };
    }

    /// <summary>只接受正整数主键，不把路径、表达式或空主键传播到数据库查询。</summary>
    public static string RequireId(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id.ToString(CultureInfo.InvariantCulture) : throw new InvalidOperationException("请先保存对象，标识不能为空或无效。");
}

/// <summary>兼容旧按钮网关；数据库按钮和关联实体均在服务端重新读取，不执行客户端提交的实体。</summary>
public sealed class CompatibilityButtonBusiness(SqlRowStore store, HttpRuntimeAuthorization authorization, MetadataRegistry registry)
{
    /// <summary>按钮 ID 是唯一动作入口，客户端只允许提供待核验的实体 ID。</summary>
    public async Task<CompatibilityButtonResult> ExecuteAsync(string buttonId, string? entityId, CancellationToken cancellationToken = default)
    {
        var actor = authorization.RequireAdministrator();
        var button = await RequireRowAsync("config_button", CompatibilityButtonPolicy.RequireId(buttonId), cancellationToken);
        return await ExecuteRegisteredAsync(actor, button, entityId, cancellationToken);
    }

    /// <summary>首页旧接口别名同样必须对应数据库已登记按钮，不能借接口别名执行任意路由。</summary>
    public async Task<CompatibilityButtonResult> ExecuteLegacyAsync(string registeredRoute, string? entityId, CancellationToken cancellationToken = default)
    {
        var actor = authorization.RequireAdministrator();
        var buttons = await store.ReadAsync("config_button", new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(registry.Platform.YzProjectId), ["sql"] = registeredRoute }, cancellationToken);
        if (buttons.Count == 0) throw new InvalidOperationException("该按钮动作未登记或尚未移植，未执行任何操作。");
        var button = buttons.OrderBy(b => SqlRowStore.Id(b)).First();
        return await ExecuteRegisteredAsync(actor, button, entityId, cancellationToken);
    }

    /// <summary>加载白名单要求的实体与关联表后生成无副作用指令。</summary>
    private async Task<CompatibilityButtonResult> ExecuteRegisteredAsync(RuntimeActor actor, IReadOnlyDictionary<string, object?> button, string? entityId, CancellationToken cancellationToken)
    {
        var action = CompatibilityButtonPolicy.RequireAction(actor, registry, button);
        var entity = await RequireRowAsync(action.EntityTable, CompatibilityButtonPolicy.RequireId(entityId ?? ""), cancellationToken);
        // 关联配置表也重新读库；实体自身项目正确不代表其关联主键可信。
        Dictionary<string, object?>? parent = null;
        if (action.EntityTable is "config_page" or "config_column") parent = await RequireRowAsync("config_table", CompatibilityButtonPolicy.RequireId(SqlRowStore.Text(entity, "config_table_id")), cancellationToken);
        return CompatibilityButtonPolicy.Resolve(actor, action, entity, parent);
    }

    /// <summary>基础设施查询只使用明确主键，逻辑删除记录由仓储自动排除。</summary>
    private async Task<Dictionary<string, object?>> RequireRowAsync(string table, string id, CancellationToken cancellationToken) =>
        (await store.ReadAsync(table, new Dictionary<string, object?> { ["id"] = long.Parse(id) }, cancellationToken)).SingleOrDefault() ?? throw new InvalidOperationException("按钮或配置对象不存在，可能已被删除。");
}
