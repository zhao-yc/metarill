using Metarill.Core.Security;
using Metarill.Core.Contracts;
using Metarill.Platform.Generated.Entities;
using Metarill.Runtime.Security;
namespace Metarill.Platform.Generated.Services;

/// <summary>手写安全对象扩展：普通页权、后台归属及引用由统一协调器证明，不追加超级身份门槛。</summary>
public partial class YzManagerService
{
    /// <summary>手写对象规则执行前，必须证明当前操作已沿同事务安全准备入口完成。</summary>
    partial void BeforeSave(YzManager entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        SecurityRealmMutationGuard.RequirePrepared(guards,session.CurrentTransaction,"yz_manager",entity.Id,actor,creating,false);
        // 原页面提交的新密码只在同一安全事务内生成摘要；省略保持已有摘要。
        if (creating || submittedFields.Contains("password")) entity.Password=PasswordHasher.Hash(ManagerCredentialValue.Input(entity.Password));
    }
    /// <summary>删除复用真实目标的delete准备，引用和归属由最终协调器统一核验。</summary>
    partial void BeforeDelete(YzManager entity,RuntimeActor actor)
    {
        SecurityRealmMutationGuard.RequirePrepared(guards,session.CurrentTransaction,"yz_manager",entity.Id,actor,false,true);
    }
}
