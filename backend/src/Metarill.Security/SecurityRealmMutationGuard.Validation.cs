using System.Globalization;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed partial class SecurityRealmMutationGuard
{
    /// <summary>页面动作和当前身份同源；普通账号不是因为平台配置总门槛而被拒，仍须真实同realm源页动作。</summary>
    private static void RequireSourceAction(SecurityCurrentPrincipal current,SecurityPageResource published,RecordMutationOperation operation)
    {
        var identity=current.Identity;
        var page=current.Catalog.Pages.SingleOrDefault(page=>page.Id==published.Id&&page.TableId==published.TableId&&
            page.AdminTypeId==identity.PrincipalAdminTypeId&&page.ProjectId==identity.PrincipalProjectId);
        if(page is null||published.AdminTypeId!=identity.PrincipalAdminTypeId||published.ProjectId!=identity.PrincipalProjectId)throw new UnauthorizedAccessException("安全源页不属于当前身份后台。");
        EnsureEnabled(page,operation);
        if(identity.IsRealmSuper)return;
        var action=Action(operation);
        if(!identity.Permissions.Contains(page.Id+":"+action)&&!identity.Permissions.Contains(page.Id+":is_all"))throw new UnauthorizedAccessException("缺少安全源页的当前操作权限。");
    }
    /// <summary>角色标准写原本就调用PermissionAccess；账号和后台路径不能复用此同后台角色约束。</summary>
    private void RequireRoleAction(SecurityCurrentPrincipal current,long targetAdmin,string action)
    {
        var identity=current.Identity;
        if(targetAdmin.ToString(CultureInfo.InvariantCulture)!=identity.PrincipalAdminTypeId)throw new UnauthorizedAccessException("不能管理其他身份后台的角色授权。");
        if(identity.IsRealmSuper)return;
        var published=registry.PublishedCatalogs.Where(catalog=>catalog.ProjectId==identity.PrincipalProjectId).ToArray();
        var roleTables=current.Catalog.Tables.Where(table=>table.TableName=="yz_role").Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var page in current.Catalog.Pages.Where(page=>roleTables.Contains(page.TableId)&&EnabledAction(page,action)))
        {
            if(!published.SelectMany(catalog=>catalog.Pages).Any(fixedPage=>fixedPage.Id==page.Id&&fixedPage.TableId==page.TableId&&
                fixedPage.AdminTypeId==identity.PrincipalAdminTypeId&&fixedPage.ProjectId==identity.PrincipalProjectId))continue;
            if(identity.Permissions.Contains(page.Id+":"+action)||identity.Permissions.Contains(page.Id+":is_all"))return;
        }
        throw new UnauthorizedAccessException(action=="is_update"?"缺少当前后台角色配置页的修改权限。":"缺少当前后台角色配置页的操作权限。");
    }

    /// <summary>原值来自守卫取得的真实锁行；手写钩子不能伪装准备前已存在的后台或角色值。</summary>
    private static void ValidateOriginal(RecordMutationValidation validation,Dictionary<string,object?>? original)
    {
        if(validation.RecordId<=0)throw new ArgumentException("安全记录标识无效。");
        if(original is null)
        {
            if(validation.Operation is not RecordMutationOperation.Create)throw Changed();return;
        }
        foreach(var key in new[]{"yz_admin_type_id","yz_project_id","yz_role_ids","is_super","is_scope","is_built_in"})
            if(validation.OriginalScopeValues.TryGetValue(key,out var value)&&Text(value)!=Text(original.GetValueOrDefault(key)))throw Changed();
    }

    /// <summary>后台归属以最终合并实体为准；所有目标必须已准备，不能在首次记录锁之后追加后台锁。</summary>
    private async Task ValidateFinalAsync(RecordMutationValidation validation,Dictionary<string,object?>? original,CoordinationState state,SecurityCurrentPrincipal current,CancellationToken token)
    {
        var deleting=validation.Operation==RecordMutationOperation.Delete;
        object? Value(string key)=>deleting?original?.GetValueOrDefault(key):validation.ScopeValues.TryGetValue(key,out var value)?value:original?.GetValueOrDefault(key);
        if(validation.TableName=="yz_manager")
        {
            if(deleting)return;
            var admin=Required(Value("yz_admin_type_id"));RequirePreparedAdmin(state,admin);
            // 省略角色仍必须校验，尤其修改后台时不能保留旧后台角色；null和[]都保持原清空语义。
            var encoded=Value("yz_role_ids") is null?null:Text(Value("yz_role_ids"));
            var roleIds=RoleCheckboxValue.Parse(encoded).Distinct().Order().ToArray();
            var rows=await store.ReadForUpdateByIdsAsync("yz_role","id",roleIds,new Dictionary<string,object?>{{"yz_admin_type_id",admin}},token);
            if(rows.Count!=roleIds.Length)throw new ArgumentException("管理账号只能分配最终所属后台的有效角色。");
            return;
        }
        if(validation.TableName=="yz_role")
        {
            var admin=Required(Value("yz_admin_type_id"));RequirePreparedAdmin(state,admin);
            if(validation.Operation==RecordMutationOperation.Update&&original is not null&&SqlRowStore.Text(original,"yz_admin_type_id")!=admin.ToString(CultureInfo.InvariantCulture))throw new ArgumentException("角色不能直接更换后台，请显式处理授权和账号引用。");
            RequireRoleAction(current,admin,deleting?"is_delete":validation.Operation is RecordMutationOperation.Create or RecordMutationOperation.Copy?"is_new":"is_update");
            if(!deleting&&Value("is_super") is{} super&&Text(super) is not ("0" or "1"))throw new ArgumentException("超级管理员标志必须为0或1。");
            if(deleting)
            {
                if((await store.ReadForUpdateByIdsAsync("yz_role_permission","yz_role_id",new[]{validation.RecordId},cancellationToken:token)).Count>0)throw new InvalidOperationException("角色仍有授权引用，不能直接删除。");
                var managers=await store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"yz_admin_type_id",admin}},token);
                if(managers.Any(manager=>ReferencesRole(SqlRowStore.Text(manager,"yz_role_ids"),validation.RecordId)))throw new InvalidOperationException("角色仍有账号引用，不能直接删除。");
            }
            return;
        }
        if(validation.TableName=="yz_admin_type")
        {
            var project=Required(Value("yz_project_id"));
            if(!state.Locks.Projects.ContainsKey(project))throw Changed();
            if(original is not null&&(deleting||SqlRowStore.Text(original,"yz_project_id")!=project.ToString(CultureInfo.InvariantCulture)))
            {
                // 保留原角色→菜单→账号锁序和遇首个引用即拒绝；菜单存储由必要宿主适配提供。
                if((await store.ReadForUpdateAsync("yz_role",new Dictionary<string,object?>{{"yz_admin_type_id",validation.RecordId}},token)).Count>0||
                    await reader.HasAdminMenuReferencesForUpdateAsync(state.Locks.Transaction,validation.RecordId,token)||
                    (await store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"yz_admin_type_id",validation.RecordId}},token)).Count>0)
                    throw new InvalidOperationException("后台仍有菜单、角色或账号引用，不能直接删除或更换项目。");
            }
        }
    }
    private static void RequirePreparedAdmin(CoordinationState state,long admin)
    {
        if(!state.Locks.Admins.TryGetValue(admin,out var row)||!state.Locks.Projects.ContainsKey(Id(SqlRowStore.Text(row,"yz_project_id"))))throw Changed();
    }
    /// <summary>历史坏JSON不被解释为任何角色；新增/更新的角色值仍由严格500字符合同拒绝。</summary>
    private static bool ReferencesRole(string encoded,long roleId){try{return RoleCheckboxValue.Parse(encoded).Contains(roleId);}catch(ArgumentException){return false;}}
    private static long Required(object? value)=>value is null?throw new ArgumentException("安全对象必须关联真实后台或项目。"):Id(value);
    private static string Text(object? value)=>Convert.ToString(value,CultureInfo.InvariantCulture)??"";
    private static string Action(RecordMutationOperation operation)=>operation switch
    {
        RecordMutationOperation.Create=>"is_new",RecordMutationOperation.Update=>"is_update",RecordMutationOperation.Delete=>"is_delete",
        RecordMutationOperation.Copy or RecordMutationOperation.Import=>"is_all",_=>throw new InvalidOperationException("安全写入动作无效。")
    };
    private static bool EnabledAction(SecurityPageResource page,string action)=>action switch
    {
        "is_new"=>page.CanCreate,"is_update"=>page.CanUpdate,"is_delete"=>page.CanDelete,"is_query" or "is_all"=>true,_=>false
    };
    private static void EnsureEnabled(SecurityPageResource page,RecordMutationOperation operation)
    {
        var enabled=operation switch
        {
            RecordMutationOperation.Create=>page.CanCreate,RecordMutationOperation.Update=>page.CanUpdate,RecordMutationOperation.Delete=>page.CanDelete,
            RecordMutationOperation.Copy=>page.CanCopy,RecordMutationOperation.Import=>page.CanImport,
            _=>false
        };
        if(!enabled)throw new UnauthorizedAccessException("当前安全页面未开放此写入操作。");
    }
}
