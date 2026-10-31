using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace {{NAMESPACE}}.Generated.Controllers;

/// <summary>只随工厂签发管理模块生成的原初始化直调入口，中央按钮共用同一事务边界。</summary>
[ApiController]
public sealed class AdminAccountInitializationController:ControllerBase
{
    /// <summary>只解析原id参数；真实身份、目录、权限及已保存口令由共享入口与Business核验。</summary>
    [HttpPost("api/admin/custom/index/init_admin")]
    public async Task<IActionResult> Initialize(CancellationToken token)
    {
        var id=await AdminAccountInitializationHttp.ReadTargetIdAsync(Request,token);
        return await AdminAccountInitializationHttp.TryExecuteAsync(new(AdminAccountInitializationBusiness.ButtonId,id),HttpContext,token)
            ??throw new InvalidOperationException("本应用未注册后台账号初始化。");
    }
}
