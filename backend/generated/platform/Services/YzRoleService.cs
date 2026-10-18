using Metarill.Core.Security;
using Metarill.Platform.Generated.Entities;
namespace Metarill.Platform.Generated.Services;

/// <summary>角色属于平台安全配置，普通业务角色不能通过表级CRUD提升为超级管理员。</summary>
public partial class YzRoleService
{
    partial void BeforeSave(YzRole entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(!actor.IsAdministrator)throw new UnauthorizedAccessException("角色管理需要平台管理员权限。");
        if(entity.IsSuper is not (null or 0 or 1))throw new ArgumentException("超级管理员标志必须为0或1。");
    }
    partial void BeforeDelete(YzRole entity,RuntimeActor actor)
    {
        if(!actor.IsAdministrator)throw new UnauthorizedAccessException("角色管理需要平台管理员权限。");
        if(entity.IsBuiltIn==1)throw new InvalidOperationException("内置角色不能删除。");
    }
}
