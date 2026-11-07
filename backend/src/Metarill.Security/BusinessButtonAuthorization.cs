using System.Security;

namespace Metarill.Runtime.Security;

/// <summary>平台与独立应用共享实际按钮的发布归属和身份边界；不附加页面动作或超级身份门槛。</summary>
public static class BusinessButtonAuthorization
{
    /// <summary>宿主固定选择平台身份支持；页面和按钮必须唯一绑定，普通身份只使用本请求已计算的实际按钮授权。</summary>
    public static void Require(AuthenticatedIdentity identity,IReadOnlyList<SecurityResourceCatalog> catalogs,string pageId,string buttonId,bool platformHost)
    {
        var pages=catalogs.SelectMany(catalog=>catalog.Pages.Where(page=>page.ProjectId==catalog.ProjectId&&page.Id==pageId)).ToArray();
        var buttons=catalogs.SelectMany(catalog=>catalog.Buttons.Where(button=>button.Id==buttonId).Select(button=>(Catalog:catalog,Button:button))).ToArray();
        if(pages.Length!=1||buttons.Length!=1||buttons[0].Button.PageId!=pageId||buttons[0].Catalog.ProjectId!=pages[0].ProjectId)throw Denied();
        // 平台管理员保持原宿主特权；独立应用即使收到此标志也拒绝，不能把业务super变成平台身份。
        if(identity.Actor.IsAdministrator){if(platformHost)return;throw Denied();}
        var page=pages[0];
        if(identity.Actor.ProjectId!=identity.PrincipalProjectId||identity.Actor.AdminTypeId!=identity.PrincipalAdminTypeId
            ||page.ProjectId!=identity.PrincipalProjectId||page.AdminTypeId!=identity.PrincipalAdminTypeId||!identity.ButtonIds.Contains(buttonId))throw Denied();
    }
    /// <summary>固定提示不回显目录、账号或匹配值，由宿主保持既有无权状态协议。</summary>
    private static SecurityException Denied()=>new("无此按钮访问权限。");
}
