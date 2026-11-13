using Metarill.Data;
using Metarill.Data.Relations;
using Metarill.Runtime.Security;
namespace Metarill.Runtime.Maintenance;

/// <summary>原维护操作沿真实Web身份与精确按钮授权，只处理当前宿主发布项目，不从工作区选择目标库。</summary>
public sealed class SystemMaintenanceBusiness(DatabaseSession session,RelationMaintenanceCatalog catalog,ISecurityIdentityAccessor identities)
{
    /// <summary>下拉字段与原页面一致，不把权限不足或未发布字段包装成可维护表。</summary>
    public IReadOnlyList<MaintenanceTableOption> Tables(){Require("tables");return catalog.Tables;}
    /// <summary>先证明身份和原按钮授权，再进入唯一配置数据库的整批回填事务。</summary>
    public Task<MaintenanceResult> BackfillAsync(string? tableId,CancellationToken token=default)
    {
        Require("backfill_join_columns");if(string.IsNullOrWhiteSpace(tableId)||tableId.Length>80)throw new ArgumentException("配置表不能为空或标识无效。");return new RelationMaintenanceExecutor(session).BackfillAsync(catalog,tableId,token);
    }
    /// <summary>没有绑定按钮时沿原sys登录后可用规则；有绑定时只认当前真实身份的按钮ID，Agent不能借Web入口扩大能力。</summary>
    private void Require(string action)
    {
        var identity=identities.RequireIdentity();var actor=identity.Actor;
        if(identity.SessionId<=0||actor.AccountId<=0||identity.GrantId is not null||identity.PrincipalProjectId!=catalog.ProjectId||!catalog.AdminTypeIds.Contains(identity.PrincipalAdminTypeId)||actor.AdminTypeId!=identity.PrincipalAdminTypeId||!actor.IsAdministrator&&actor.ProjectId!=catalog.ProjectId)throw new UnauthorizedAccessException("维护入口不属于当前真实Web身份的宿主项目。");
        if(catalog.Buttons[action].Count>0&&!actor.IsAdministrator&&!identity.IsRealmSuper&&!catalog.Buttons[action].Any(identity.ButtonIds.Contains))throw new UnauthorizedAccessException("没有当前维护按钮权限。");
    }
}
