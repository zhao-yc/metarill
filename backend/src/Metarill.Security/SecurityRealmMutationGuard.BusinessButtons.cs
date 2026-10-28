using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed partial class SecurityRealmMutationGuard
{
    /// <summary>与安全表共用预锁和当前读，普通按钮只验证本按钮，不增加页面修改或超级身份门槛。</summary>
    public async Task RequireBusinessButtonAsync(string pageId,string buttonId,RuntimeActor actor,DbTransaction transaction,CancellationToken token=default)
    {
        // 不借另一个连接的认证快照冒充当前写事务；跨业务库需显式接入相应协调边界。
        var request=RequireRequest(actor,transaction);var published=registry.PublishedCatalogs;
        var platform=registry.IsPlatformRealm(request.PrincipalProjectId,request.PrincipalAdminTypeId);
        BusinessButtonAuthorization.Require(request,published,pageId,buttonId,platform);
        var page=published.SelectMany(catalog=>catalog.Pages).Single(value=>value.Id==pageId);
        var state=await PrepareLocksAsync(request,"",null,new Dictionary<string,object?>(),token,new HashSet<long>{Id(page.AdminTypeId)});
        var current=await ReadCurrentAsync(request,state,token);
        await RequireCurrentButtonBindingAsync(request,current,state,pageId,buttonId,platform,token);
        // SQL仍使用请求原来的窄身份，任何本按钮范围/账号匹配值变化都拒绝，不发布扩大后的权限。
        var restricted=request.ExactDataManageButtonIds.Contains(buttonId);var latest=current.Identity.ExactDataManageButtonIds.Contains(buttonId);
        if(restricted!=latest||restricted&&(request.ExactDataManageMatchField!=current.Identity.ExactDataManageMatchField
            ||!SameValue(request.ExactDataManageMatchValue,current.Identity.ExactDataManageMatchValue)))throw ButtonChanged();
        state.Buttons.Add((pageId,buttonId));
    }
    /// <summary>只在已完成按钮预锁的原事务提供文件用途身份；不把新身份发布为普通页面权限。</summary>
    public async Task<AuthenticatedIdentity> ReadBusinessButtonFileIdentityAsync(string pageId,string buttonId,RuntimeActor actor,DbTransaction transaction,CancellationToken token=default)
    {
        var request=RequireRequest(actor,transaction);
        if(!_states.TryGetValue(transaction,out var state)||!state.Buttons.Contains((pageId,buttonId)))
            throw new InvalidOperationException("文件来源当前读必须复用本事务已经完成的按钮证明。");
        RequireSameRequest(state.Request,request);await RefreshLocks(state,token);
        var current=await ReadCurrentAsync(request,state,token);
        await RequireCurrentButtonBindingAsync(request,current,state,pageId,buttonId,registry.IsPlatformRealm(request.PrincipalProjectId,request.PrincipalAdminTypeId),token);
        // 当前页号仍有授权也不能读取它发布时的旧表；文件来源必须与当前真实绑定一致。
        var published=registry.PublishedCatalogs;
        var filePages=current.Catalog.Pages.Where(live=>published.SelectMany(item=>item.Pages).Any(fixedPage=>fixedPage.Id==live.Id
            &&fixedPage.TableId==live.TableId&&fixedPage.ProjectId==live.ProjectId&&fixedPage.AdminTypeId==live.AdminTypeId
            &&published.SelectMany(item=>item.Tables).Any(fixedTable=>fixedTable.Id==live.TableId
                &&current.Catalog.Tables.Any(table=>table.Id==fixedTable.Id&&table.TableName==fixedTable.TableName))))
            .Select(page=>page.Id).ToHashSet(StringComparer.Ordinal);
        bool FilePermission(string value)=>value.LastIndexOf(':') is var separator&&separator>0&&filePages.Contains(value[..separator]);
        return current.Identity with
        {
            FileQueryPermissions=(current.Identity.FileQueryPermissions??current.Identity.Permissions).Where(FilePermission).ToHashSet(StringComparer.Ordinal),
            FileExactDataManagePermissions=(current.Identity.FileExactDataManagePermissions??current.Identity.ExactDataManagePermissions).Where(FilePermission).ToHashSet(StringComparer.Ordinal)
        };
    }
    /// <summary>按钮与真实来源表绑定保持不变；文件观察发生在写后，不能拿新记录值重做原写前范围。</summary>
    private async Task RequireCurrentButtonBindingAsync(AuthenticatedIdentity request,SecurityCurrentPrincipal current,CoordinationState state,string pageId,string buttonId,bool platform,CancellationToken token)
    {
        var published=registry.PublishedCatalogs;
        var page=published.SelectMany(catalog=>catalog.Pages).Single(value=>value.Id==pageId);
        var source=page.ProjectId==request.PrincipalProjectId&&page.AdminTypeId==request.PrincipalAdminTypeId?current.Catalog:
            await reader.ReadDeclaredResourcesAsync(state.Locks,Id(page.ProjectId),Id(page.AdminTypeId),token);
        var livePage=source.Pages.SingleOrDefault(value=>value.Id==pageId);
        var fixedTable=published.SelectMany(catalog=>catalog.Tables).SingleOrDefault(value=>value.Id==page.TableId);
        if(livePage is null||livePage.TableId!=page.TableId||livePage.ProjectId!=page.ProjectId||livePage.AdminTypeId!=page.AdminTypeId
            ||fixedTable is null||!source.Tables.Any(value=>value.Id==page.TableId&&value.TableName==fixedTable.TableName))throw ButtonChanged();
        BusinessButtonAuthorization.Require(current.Identity,[source],pageId,buttonId,platform);
    }
    /// <summary>仅比较已选定的账号匹配值，不把完整账号或权限字典输出到错误消息。</summary>
    private static bool SameValue(JsonElement? left,JsonElement? right)
    {
        if(left?.ValueKind==JsonValueKind.Undefined)left=null;if(right?.ValueKind==JsonValueKind.Undefined)right=null;
        return JsonNode.DeepEquals(left is{} l?JsonNode.Parse(l.GetRawText()):null,right is{} r?JsonNode.Parse(r.GetRawText()):null);
    }
    /// <summary>范围变化必须刷新请求，不在原记录锁后继续借用旧范围。</summary>
    private static UnauthorizedAccessException ButtonChanged()=>new("当前按钮归属、权限或匹配范围已变化，请刷新后重试。");
}
