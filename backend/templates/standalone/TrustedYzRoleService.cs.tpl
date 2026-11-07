using Metarill.Core.Security;
using {{NAMESPACE}}.Generated.Entities;
using Metarill.Runtime.Security;
namespace {{NAMESPACE}}.Generated.Services;

/// <summary>可信安全模块扩展只首次创建：普通页权、后台归属及引用由统一协调器证明，不追加超级身份门槛。</summary>
public partial class YzRoleService
{
    /// <summary>手写对象规则执行前，必须证明当前操作已沿同事务安全准备入口完成。</summary>
    private partial void BeforeSave(global::{{NAMESPACE}}.Generated.Entities.YzRole entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        SecurityRealmMutationGuard.RequirePrepared(guards,session.CurrentTransaction,"yz_role",entity.Id,actor,creating,false);
        if(entity.IsSuper is not (null or 0 or 1))throw new ArgumentException("超级管理员标志必须为0或1。");
    }
    /// <summary>删除复用真实目标的delete准备，引用和归属由最终协调器统一核验。</summary>
    private partial void BeforeDelete(global::{{NAMESPACE}}.Generated.Entities.YzRole entity,RuntimeActor actor)
    {
        SecurityRealmMutationGuard.RequirePrepared(guards,session.CurrentTransaction,"yz_role",entity.Id,actor,false,true);
    }
}
