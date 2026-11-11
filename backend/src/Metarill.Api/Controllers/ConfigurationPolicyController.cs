using Metarill.Core.Contracts;
using Metarill.Data.Queries;
using Metarill.Runtime;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc;

namespace Metarill.Api.Controllers;

/// <summary>保留原配置控件的能力查询协议，按当前C#实现明确声明首期边界。</summary>
[ApiController,Route("api/admin/custom/config_table")]
public sealed class ConfigurationPolicyController(HttpRuntimeAuthorization authorization, TargetDatabaseSessions databases) : ControllerBase
{
    /// <summary>原表单按当前授权项目的数据库能力展示首次选项，已保存的表继续冻结模式。</summary>
    [HttpPost("storage_mode_policy")]
    public R<object> StorageModePolicy()
    {
        var actor = authorization.RequireAdministrator();
        var provider = databases.GetConfiguredProvider(actor.ProjectId);
        return R<object>.Success(new
        {
            projectId = actor.ProjectId,
            backendTarget = "csharp",
            isV8 = true,
            supportsNonRedundant = NonRedundantReads.SupportsProvider(provider)
        });
    }
}
