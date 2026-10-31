using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留原配置控件的能力查询协议，按当前C#实现明确声明首期边界。</summary>
[ApiController,Route("api/admin/custom/config_table")]
public sealed class ConfigurationPolicyController(HttpRuntimeAuthorization authorization):ControllerBase
{
    /// <summary>isV8沿用客户端的保存后冻结协议；C#首期尚不开放非冗余模式切换。</summary>
    [HttpPost("storage_mode_policy")]
    public R<object> StorageModePolicy()
    {
        var actor=authorization.RequireAdministrator();
        return R<object>.Success(new{projectId=actor.ProjectId,backendTarget="csharp",isV8=true,supportsNonRedundant=false});
    }
}
