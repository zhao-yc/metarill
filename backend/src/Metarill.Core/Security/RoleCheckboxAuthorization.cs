namespace Metarill.Core.Security;

/// <summary>原findNameId候选允许查询、新增或修改页权任一通过，不改变其他端点的动作映射。</summary>
public static class RoleCheckboxAuthorization
{
    /// <summary>先建立真实身份，再尝试元数据实际开放动作；只捕获明确无权限，不吞服务异常。</summary>
    public static RuntimeActor Require(IRuntimeAuthorization authorization, string pageId, bool canCreate, bool canUpdate)
    {
        var actor = authorization.RequireActor();
        foreach (var operation in new[] { "list", canCreate ? "create" : null, canUpdate ? "update" : null }.OfType<string>())
        {
            try { authorization.RequirePagePermission(pageId,operation); return actor; }
            catch (UnauthorizedAccessException) { }
        }
        throw new UnauthorizedAccessException("当前页面未授予查询、新增或修改权限。");
    }
}
