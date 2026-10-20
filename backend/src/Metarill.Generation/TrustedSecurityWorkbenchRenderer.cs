using System.Text.Json;

namespace Metarill.Generation;

/// <summary>仅工厂签发的固定管理模块下发原工作台资源和HTTP适配，不识别业务JSON标记。</summary>
internal static class TrustedSecurityWorkbenchRenderer
{
    /// <summary>先核对完整可信快照和原自定义页归属，再读取固定编译资源。</summary>
    internal static IEnumerable<PlannedFile> Render(GenerationModel model)
    {
        if(model.TrustedSecurityServices is null)yield break;
        var management=RequireManagement(model);

        var assembly=typeof(CSharpGenerator).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name=>name.EndsWith(".TrustedRolePermissionController.cs.tpl",StringComparison.Ordinal)))!;
        using var reader=new StreamReader(stream);
        var source=reader.ReadToEnd().Replace("{{NAMESPACE}}",model.Namespace,StringComparison.Ordinal)
            .Replace("{{PROJECT_ID}}",JsonSerializer.Serialize(model.Catalog.YzProjectId),StringComparison.Ordinal)
            .Replace("{{ADMIN_TYPE_ID}}",JsonSerializer.Serialize(management.YzAdminTypeId),StringComparison.Ordinal);
        yield return new("Controllers/RolePermissionWorkbenchController.g.cs",CSharpGenerator.Header+source);
    }
    /// <summary>前端资产直接来自本程序集编译时嵌入的原源码，普通模型不增加页面或依赖。</summary>
    internal static IEnumerable<PlannedFile> Frontend(GenerationModel model)
    {
        if(model.TrustedSecurityServices is null)yield break;
        _=RequireManagement(model);
        foreach(var (resource,path) in new[]{("Page.vue","pages/main/set_role_permission.vue"),("Workbench.vue","components/yzp-permission-workbench/yzp-permission-workbench.vue"),("Legacy.vue","components/yzp-permission-legacy/yzp-permission-legacy.vue"),("Runtime.js","common/yzp_runtime/permissionWorkbench.js")})
        {
            using var stream=typeof(CSharpGenerator).Assembly.GetManifestResourceStream("Metarill.Generation.SecurityWorkbench."+resource)??throw Rejected();
            using var reader=new StreamReader(stream);
            yield return new("frontend/"+path,reader.ReadToEnd());
        }
    }
    /// <summary>前后端消费同一个签发快照和固定页归属，不能分别提供可变目录。</summary>
    private static Metarill.Core.Metadata.PageMetadata RequireManagement(GenerationModel model)
    {
        var role=model.Tables.Single(table=>table.Source.Id=="1692532858499969026");
        if(!TrustedSecurityServiceCompilation.Applies(model,role))throw Rejected();
        var custom=model.Catalog.ConfigCustomPages.SingleOrDefault(page=>page.Id=="2062363000000000002");
        var management=model.Catalog.ConfigPages.Single(page=>page.Id=="1692532858529329154");
        if(custom is null||custom.PagePath!="pages/main/set_role_permission"||custom.YzProjectId!=model.Catalog.YzProjectId||custom.YzAdminTypeId!=management.YzAdminTypeId)throw Rejected();
        return management;
    }
    /// <summary>失败只说明可信固定入口不符，不回显业务目录内容。</summary>
    private static InvalidOperationException Rejected()=>new("原权限工作台缺少可信模块或固定管理后台归属，未生成接口。");
}
