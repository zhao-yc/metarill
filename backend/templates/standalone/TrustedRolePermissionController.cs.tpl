using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace {{NAMESPACE}}.Generated.Controllers;

public sealed record RoleWorkbenchSnapshotRequest(string RoleId);

/// <summary>沿用原权限工作台三个接口；权限、完整目录和保存事务统一进入共享Business。</summary>
[ApiController,Route("api/admin/sys/yzp_permission")]
public sealed class RolePermissionWorkbenchController(RolePermissionBusiness business,ISecurityIdentityAccessor authorization,IRuntimeAuthorization pageAuthorization):ControllerBase
{
    private const string ProjectId={{PROJECT_ID}};
    private const string ManagementAdminTypeId={{ADMIN_TYPE_ID}};

    /// <summary>只接受本包固定管理后台的持久Web身份，工作区参数不能代替真实归属。</summary>
    private void RequireWebIdentity(string operation)
    {
        var identity=authorization.RequireIdentity();var actor=identity.Actor;
        if(identity.GrantId is not null||identity.SessionId<=0||actor.AccountId<=0||actor.IsAdministrator||
            actor.ProjectId!=ProjectId||identity.PrincipalProjectId!=ProjectId||
            actor.AdminTypeId!=ManagementAdminTypeId||identity.PrincipalAdminTypeId!=ManagementAdminTypeId)
            throw new UnauthorizedAccessException("原权限工作台需要本应用管理后台的真实Web身份。");
        if(!ReferenceEquals(authorization,pageAuthorization))throw new InvalidOperationException("原权限工作台身份与页授权必须共用同一作用域实例。");
        // 正式Web页授权用SecurityException表达403；同后台super仍沿原业务规则，不额外要求普通角色动作。
        if(!identity.IsRealmSuper)pageAuthorization.RequirePagePermission("1692532858529329154",operation);
    }
    /// <summary>保留原GET协议，角色查询权限仍由共享业务依据当前后台判断。</summary>
    [HttpGet("roles")]
    public async Task<R<object>> Roles(CancellationToken token)
    {
        RequireWebIdentity("list");return R<object>.Success(await business.RolesAsync(token));
    }
    /// <summary>快照只接角色ID，不能从客户端指定目标后台或扩展资源目录。</summary>
    [HttpPost("snapshot")]
    public async Task<R<object>> Snapshot(RoleWorkbenchSnapshotRequest request,CancellationToken token)
    {
        RequireWebIdentity("list");return R<object>.Success(await business.SnapshotAsync(request.RoleId,token));
    }
    /// <summary>原revision和授权草稿原样交给共享事务，不补超级身份门槛或写Mapper。</summary>
    [HttpPost("save")]
    public async Task<R<object>> Save(RolePermissionSave request,CancellationToken token)
    {
        RequireWebIdentity("update");return R<object>.Success(await business.SaveAsync(request,token));
    }
}
