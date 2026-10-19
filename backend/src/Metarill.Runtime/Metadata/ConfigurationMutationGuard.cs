using System.Globalization;
using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;

namespace Metarill.Runtime.Metadata;

/// <summary>平台配置聚合统一先锁真实项目，避免生成 CRUD 先锁对象再等待工作台项目锁。</summary>
public sealed class ConfigurationMutationGuard(SqlRowStore store, MetadataRegistry registry, HttpRuntimeAuthorization authorization) : IRecordMutationGuard
{
    private static readonly HashSet<string> ConfigurationTables = new(StringComparer.Ordinal)
    {
        "config_table", "config_column", "config_page", "config_custom_page", "config_page_column", "config_button",
        "config_child", "config_index", "config_index_column", "config_menu", "yz_admin_type"
    };

    /// <summary>只处理已发布平台配置页，按真实认证项目加锁，跨库或伪造身份在首次对象访问前拒绝。</summary>
    public async Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken cancellationToken = default)
    {
        var page = registry.Platform.ConfigPages.SingleOrDefault(candidate => candidate.Id == preparation.PageId);
        if (page is null) return;
        var table = registry.Platform.ConfigTables.Single(candidate => candidate.Id == page.ConfigTableId);
        if (table.TableName != preparation.TableName) throw new UnauthorizedAccessException("准备页面与配置表不一致。");
        if (!ConfigurationTables.Contains(table.TableName)) return;
        var actor = authorization.RequireAdministrator();
        if (actor != preparation.Actor) throw new UnauthorizedAccessException("准备身份与当前认证身份不一致。");
        authorization.RequirePagePermission(page.Id, preparation.Operation switch
        {
            RecordMutationOperation.Create => "create", RecordMutationOperation.Update => "update", RecordMutationOperation.Delete => "delete",
            RecordMutationOperation.Copy => "copy", RecordMutationOperation.Import => "import", _ => throw new InvalidOperationException("准备操作类型无效。")
        });
        if (preparation.Transaction is null || preparation.Transaction.Connection is null || !ReferenceEquals(preparation.Transaction, store.Session.CurrentTransaction))
            throw new InvalidOperationException("配置准备守卫必须使用平台写入的同一连接及事务。");
        if (!long.TryParse(actor.ProjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var project) || project <= 0)
            throw new UnauthorizedAccessException("当前项目标识无效。");
        if ((await store.ReadForUpdateAsync("yz_project", new Dictionary<string, object?> { ["id"] = project }, cancellationToken)).Count != 1)
            throw new UnauthorizedAccessException("当前项目不存在。");
    }
}
