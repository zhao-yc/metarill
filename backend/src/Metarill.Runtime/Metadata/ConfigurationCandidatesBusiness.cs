using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

public sealed record ConfigurationCandidateOption(string Id, string Name, string Value, string Text);

/// <summary>仅供管理员控件编辑器继承字段配置；明细保持原值，不附带其它实体或页级 SQL。</summary>
public sealed record ConfigurationControlMetadata(string ControlKind, string ColumnName, string ColumnType, string Name, string ControlDetail);

/// <summary>控件配置候选和字段引用只读取真实管理员当前项目；清空明细复用平台事务准备规则。</summary>
public sealed class ConfigurationCandidatesBusiness(SqlRowStore store, MetadataRepository metadata, MetadataRegistry registry,
    HttpRuntimeAuthorization authorization, ConfigurationMutationGuard preparation)
{
    /// <summary>表候选只返回旧选择器所需四字段，项目和连接不能由请求提供。</summary>
    public async Task<IReadOnlyList<ConfigurationCandidateOption>> TablesAsync(JsonElement request, CancellationToken token = default)
    {
        MetadataWorkbenchRules.Object(request); var actor = RequireAdministrator();
        return (await store.ReadAsync("config_table", Project(actor), token)).OrderByDescending(row => SqlRowStore.Id(row)).Select(Option).ToArray();
    }

    /// <summary>选择页候选限定当前项目的有效表与 type=1 页面，保留原名称排序和四字段协议。</summary>
    public async Task<IReadOnlyList<ConfigurationCandidateOption>> PagesAsync(JsonElement request, CancellationToken token = default)
    {
        MetadataWorkbenchRules.Object(request); var actor = RequireAdministrator();
        var tables = (await store.ReadAsync("config_table", Project(actor), token)).Select(row => SqlRowStore.Id(row)).ToHashSet();
        return (await store.ReadAsync("config_page", Project(actor), token)).Where(row => MetadataWorkbenchRules.StoredFlag(row.GetValueOrDefault("type")) == 1 && tables.Contains(SqlRowStore.Id(row, "config_table_id")))
            .OrderBy(row => SqlRowStore.Text(row, "name"), StringComparer.Ordinal).ThenByDescending(row => SqlRowStore.Id(row)).Select(Option).ToArray();
    }

    /// <summary>从真实字段推导父表并验证同项目有效归属；控件明细只读返回，不解析或执行。</summary>
    public Task<ConfigurationControlMetadata> ControlMetadataAsync(JsonElement request, CancellationToken token = default)
    {
        MetadataWorkbenchRules.Object(request, "id"); var id = MetadataWorkbenchRules.Id(request, "id"); var actor = RequireAdministrator();
        return store.Session.InTransactionAsync(async () =>
        {
            var filters = Project(actor); filters["id"] = id;
            var column = (await store.ReadAsync("config_column", filters, token)).SingleOrDefault()
                ?? throw new UnauthorizedAccessException("字段或所属配置不存在或不属于当前项目。");
            filters["id"] = SqlRowStore.Id(column, "config_table_id");
            if ((await store.ReadAsync("config_table", filters, token)).Count != 1)
                throw new UnauthorizedAccessException("字段或所属配置不存在或不属于当前项目。");
            // 保持包括 whereSql/orderBy 在内的原明细，避免继承编辑后丢失已保存条件；此接口不执行任何配置。
            return new ConfigurationControlMetadata(SqlRowStore.Text(column, "control_kind"), SqlRowStore.Text(column, "column_name"),
                SqlRowStore.Text(column, "column_type"), SqlRowStore.Text(column, "name"), SqlRowStore.Text(column, "control_detail"));
        }, token);
    }

    /// <summary>只读事务固定一代项目元数据；返回精确结构映射定位，不返回原配置文本。</summary>
    public Task<ConfigurationColumnReferences> ReferencesAsync(JsonElement request, CancellationToken token = default)
    {
        MetadataWorkbenchRules.Object(request, "id"); var id = MetadataWorkbenchRules.Id(request, "id").ToString(CultureInfo.InvariantCulture);
        var actor = RequireAdministrator();
        return store.Session.InTransactionAsync(async () => ConfigurationReferenceRules.Build(await metadata.LoadProjectAsync(actor.ProjectId, token), id), token);
    }

    /// <summary>清空只改变本字段的页面覆盖；所有归属先校验，任何失败均不提交部分清空。</summary>
    public Task<string> ClearPageControlDetailAsync(JsonElement request, CancellationToken token = default)
    {
        MetadataWorkbenchRules.Object(request, "id"); var id = MetadataWorkbenchRules.Id(request, "id"); var actor = RequireAdministrator();
        return store.Session.InTransactionAsync(async () =>
        {
            var definition = registry.Platform.ConfigTables.Single(table => table.TableName == "config_page_column");
            var page = registry.Platform.ConfigPages.First(candidate => candidate.ConfigTableId == definition.Id);
            await preparation.BeforeMutationAsync(new(page.Id, definition.TableName, RecordMutationOperation.Update, actor, store.Session.CurrentTransaction!), token);
            var column = await Owned("config_column", id, actor, token);
            var tableId = SqlRowStore.Id(column, "config_table_id"); await Owned("config_table", tableId, actor, token);
            // 不在查询中隐藏跨项目脏引用；发现冲突必须整体拒绝，不跳过继续清空。
            var fields = await store.ReadForUpdateAsync("config_page_column", new Dictionary<string, object?> { ["config_column_id"] = id }, token);
            foreach (var field in fields)
            {
                if (SqlRowStore.Text(field, "yz_project_id") != actor.ProjectId) throw new UnauthorizedAccessException("页面字段项目归属不一致。");
                var owner = await Owned("config_page", SqlRowStore.Id(field, "config_page_id"), actor, token);
                if (SqlRowStore.Id(owner, "config_table_id") != tableId) throw new InvalidOperationException("页面字段与所属字段不在同一配置表。");
            }
            foreach (var field in fields.OrderBy(row => SqlRowStore.Id(row)))
                if (await store.UpdateAsync("config_page_column", SqlRowStore.Id(field), new Dictionary<string, object?>
                    { ["control_detail"] = "", ["update_time"] = DateTime.UtcNow, ["update_manager_id"] = actor.AccountId, ["update_manager_real_name"] = actor.AccountName, ["update_user_name"] = actor.AccountName }, token) != 1)
                    throw new InvalidOperationException("页面字段控件明细清空失败。");
            return "清空成功，共处理 " + fields.Count.ToString(CultureInfo.InvariantCulture) + " 条页面字段";
        }, token);
    }

    /// <summary>配置工作台仅允许真实网页登录管理员，不能将设备或项目选择当作授权。</summary>
    private RuntimeActor RequireAdministrator()
    {
        if (authorization.RequireIdentity().GrantId is not null) throw new UnauthorizedAccessException("控件配置需要网页登录管理员身份。");
        var actor = authorization.RequireAdministrator(); MetadataWorkbenchRules.ParseId(actor.ProjectId); return actor;
    }

    /// <summary>每个读取只使用认证项目；客户端请求没有项目筛选参数。</summary>
    private static Dictionary<string, object?> Project(RuntimeActor actor) => new() { ["yz_project_id"] = MetadataWorkbenchRules.ParseId(actor.ProjectId) };
    /// <summary>候选不暴露表结构、控件明细、SQL 或连接环境。</summary>
    private static ConfigurationCandidateOption Option(Dictionary<string, object?> row)
    { var id = SqlRowStore.Text(row, "id"); var name = SqlRowStore.Text(row, "name"); return new(id, name, id, name); }
    /// <summary>项目锁之后才读取对象当前行，所有父级都必须属于真实项目。</summary>
    private async Task<Dictionary<string, object?>> Owned(string table, long id, RuntimeActor actor, CancellationToken token) =>
        (await store.ReadForUpdateAsync(table, new Dictionary<string, object?> { ["id"] = id, ["yz_project_id"] = MetadataWorkbenchRules.ParseId(actor.ProjectId) }, token)).SingleOrDefault()
        ?? throw new UnauthorizedAccessException("字段或所属配置不存在或不属于当前项目。");
}
