using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>实际守卫和SqlRowStore的离线事务合同；不将内存传输冒充实库锁等待或生成Service验收。</summary>
public sealed class SecurityRealmMutationGuardTests
{
    /// <summary>普通账号按账号页动作管理别的后台账号，只要求最终角色同目标后台；超级角色不额外隐藏或禁止。</summary>
    [Fact]
    public async Task Ordinary_manager_editor_can_assign_super_role_in_merged_target_admin()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        await f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30,new(){["yz_admin_type_id"]=91L}));
            var original=Scope(f.Row("yz_manager",30));await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,original,["yz_role_ids"]));return true;
        });
        AssertNoWrites(f);
    }
    /// <summary>后台已变但角色省略时仍校验原角色，不能把历史角色带入另一后台。</summary>
    [Fact]
    public async Task Manager_admin_change_revalidates_omitted_old_roles()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30,new(){["yz_admin_type_id"]=long.Parse(f.PlatformAdmin)}));
            var original=Scope(f.Row("yz_manager",30));var final=new Dictionary<string,object?>(original){["yz_admin_type_id"]=long.Parse(f.PlatformAdmin)};
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,final,["yz_admin_type_id"]));return true;
        }));Assert.Equal("[\"21\"]",f.Row("yz_manager",30)["yz_role_ids"]);AssertNoWrites(f);
    }
    /// <summary>显式null和空数组清空均合法，省略仍保原值；最终检查不会规范化或改写存储编码。</summary>
    [Theory]
    [InlineData(null)][InlineData("[]")][InlineData("[\"21\"]")]
    public async Task Manager_null_empty_and_unchanged_roles_follow_original_storage(string? roles)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        await f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));
            var original=Scope(f.Row("yz_manager",30));var final=new Dictionary<string,object?>(original){["yz_role_ids"]=roles};
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,final,roles=="[\"21\"]"?[]:["yz_role_ids"]));return true;
        });Assert.Equal("[\"21\"]",f.Row("yz_manager",30)["yz_role_ids"]);AssertNoWrites(f);
    }
    /// <summary>原角色页修改权可改超级属性，不额外增加平台超级身份门槛。</summary>
    [Fact]
    public async Task Ordinary_role_editor_can_change_super_flag_with_original_page_permission()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);TargetRole(f);Grant(f,"yz_role","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        await f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_role",RecordMutationOperation.Update,30));
            var original=Scope(f.Row("yz_role",30));var final=new Dictionary<string,object?>(original){["is_super"]=1};await guard.BeforeWriteAsync(Validation(f,identity,"yz_role",RecordMutationOperation.Update,30,original,final,["is_super"]));return true;
        });AssertNoWrites(f);
    }
    /// <summary>角色原专用Access依然要求同登录后台，账号路径的跨目标后台能力不能扩展到角色。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Role_move_or_other_identity_admin_is_rejected(bool existingOther)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);TargetRole(f);Grant(f,"yz_role","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        if(existingOther)f.Row("yz_role",30)["yz_admin_type_id"]=91L;
        await Assert.ThrowsAnyAsync<Exception>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_role",RecordMutationOperation.Update,30,new(){["yz_admin_type_id"]=91L}));
            var original=Scope(f.Row("yz_role",30));var final=new Dictionary<string,object?>(original){["yz_admin_type_id"]=91L};await guard.BeforeWriteAsync(Validation(f,identity,"yz_role",RecordMutationOperation.Update,30,original,final,["yz_admin_type_id"]));return true;
        }));AssertNoWrites(f);
    }
    /// <summary>角色仍有资源授权或有效账号引用则拒绝删除，不静默清理引用。</summary>
    [Theory]
    [InlineData("grant")][InlineData("manager")]
    public async Task Role_delete_keeps_existing_grant_and_manager_references(string reference)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);TargetRole(f);Grant(f,"yz_role","is_delete");var identity=await f.Identity();var guard=Guard(f,identity);
        if(reference=="grant")f.Add("yz_role_permission",("id",81L),("yz_role_id",30L),("permission_values","[]"));
        else f.Add("yz_manager",("id",40L),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_role_ids","[30]"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_role",RecordMutationOperation.Delete,30));var original=Scope(f.Row("yz_role",30));
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_role",RecordMutationOperation.Delete,30,original,original,[]));return true;
        }));AssertNoWrites(f);
    }
    /// <summary>后台原规则只保护菜单、角色和账号引用；不增加操作者必须属于目标后台的限制。</summary>
    [Theory]
    [InlineData("yz_role")][InlineData("yz_manager")][InlineData("config_menu")]
    public async Task Admin_delete_with_original_references_is_rejected(string table)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);f.Add("yz_admin_type",("id",91L),("yz_project_id",long.Parse(f.PlatformProject)));Grant(f,"yz_admin_type","is_delete");
        f.Add(table,("id",40L),("yz_admin_type_id",91L));var identity=await f.Identity();var guard=Guard(f,identity);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_admin_type",RecordMutationOperation.Delete,91));var original=Scope(f.Row("yz_admin_type",91));
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_admin_type",RecordMutationOperation.Delete,91,original,original,[]));return true;
        }));AssertNoWrites(f);
    }
    /// <summary>最终范围超出首次预锁集合明确冲突，不在对象锁之后补取未准备后台。</summary>
    [Fact]
    public async Task Unexpected_final_admin_is_conflict_and_never_appended_to_lock_set()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);f.Add("yz_admin_type",("id",92L),("yz_project_id",long.Parse(f.PlatformProject)));Grant(f,"yz_manager","is_update");
        var identity=await f.Identity();var guard=Guard(f,identity);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));var original=Scope(f.Row("yz_manager",30));var final=new Dictionary<string,object?>(original){["yz_admin_type_id"]=92L,["yz_role_ids"]="[]"};
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,final,["yz_admin_type_id","yz_role_ids"]));return true;
        }));Assert.Contains("YZP_CONFLICT",error.Message);AssertNoWrites(f);
    }
    /// <summary>准备或最终前撤权都依据当前记录拒绝；旧HTTP快照不能授权同事务后续写。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Permission_revocation_is_rechecked_at_both_boundaries(bool afterPreparation)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
        {
            if(afterPreparation)await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));
            f.Row("yz_role_permission",71)["permission_values"]="[]";
            if(!afterPreparation)await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));
            else{var original=Scope(f.Row("yz_manager",30));await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,original,[]));}
            return true;
        }));AssertNoWrites(f);
    }
    /// <summary>工作区项目与旧新后台项目先锁齐，再进入会话/操作者当前读。</summary>
    [Fact]
    public async Task Projects_and_admins_are_locked_before_session_and_operator()
    {
        using var f=new PrincipalRealmFixture();await Seed(f);OtherAdmin(f);var identity=await f.Identity(f.DemoProject);var guard=Guard(f,identity);f.Provider.Commands.Clear();
        await f.Session.InTransactionAsync(async()=>{await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));return true;});
        var commands=f.Provider.Commands;var session=commands.FindIndex(sql=>sql.Contains(" FROM `sys_session`",StringComparison.Ordinal));
        Assert.True(session>0);Assert.Contains(commands.Take(session),sql=>sql.Contains(" FROM `yz_project`",StringComparison.Ordinal)&&sql.EndsWith(" FOR UPDATE",StringComparison.Ordinal));
        Assert.Contains(commands.Take(session),sql=>sql.Contains(" FROM `yz_admin_type`",StringComparison.Ordinal)&&sql.EndsWith(" FOR UPDATE",StringComparison.Ordinal));AssertNoWrites(f);
    }
    /// <summary>普通角色授权源四写旗标关闭，守卫也不接受手工Service准备旁路。</summary>
    [Theory]
    [InlineData(RecordMutationOperation.Create)][InlineData(RecordMutationOperation.Update)][InlineData(RecordMutationOperation.Delete)]
    [InlineData(RecordMutationOperation.Copy)][InlineData(RecordMutationOperation.Import)]
    public async Task Raw_role_permission_writes_remain_closed(RecordMutationOperation operation)
    {
        using var f=new PrincipalRealmFixture();await Seed(f);var identity=await f.Identity();var guard=Guard(f,identity);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {await guard.BeforeMutationAsync(Preparation(f,identity,"yz_role_permission",operation,30));return true;}));AssertNoWrites(f);
    }

    /// <summary>新增在没有目标记录锁时也先准备所有后台，三个安全对象各走自己的最终规则。</summary>
    [Theory]
    [InlineData("yz_manager")][InlineData("yz_role")][InlineData("yz_admin_type")]
    public async Task Create_uses_prepared_targets_without_requiring_a_nonexistent_record(string table)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,table,"is_new");var identity=await f.Identity();var guard=Guard(f,identity);
        await f.Session.InTransactionAsync(async()=>
        {
            var hints=new Dictionary<string,object?>();var final=new Dictionary<string,object?>{{"yz_project_id",long.Parse(f.PlatformProject)}};
            if(table=="yz_manager"){hints["yz_admin_type_id"]=91L;final["yz_admin_type_id"]=91L;final["yz_role_ids"]="[\"21\"]";}
            if(table=="yz_role"){hints["yz_admin_type_id"]=long.Parse(f.PlatformAdmin);final["yz_admin_type_id"]=long.Parse(f.PlatformAdmin);final["is_super"]=1;}
            await guard.BeforeMutationAsync(Preparation(f,identity,table,RecordMutationOperation.Create,null,hints));
            await guard.BeforeWriteAsync(Validation(f,identity,table,RecordMutationOperation.Create,99,new Dictionary<string,object?>(),final,final.Keys.ToArray()));return true;
        });AssertNoWrites(f);
    }
    /// <summary>字段未进入页级表示时，普通保存依然校验锁行中的旧角色，不读取或公开隐藏值。</summary>
    [Fact]
    public async Task Hidden_role_values_are_retained_and_validated_from_locked_original()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        await f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));var original=Scope(f.Row("yz_manager",30));var final=new Dictionary<string,object?>(original);final.Remove("yz_role_ids");
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,final,["name"]));return true;
        });Assert.Equal("[\"21\"]",f.Row("yz_manager",30)["yz_role_ids"]);AssertNoWrites(f);
    }
    /// <summary>页面后台配置在任一边界关闭动作，即使旧快照有is_all也不能继续写。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Current_page_action_disable_is_checked_at_both_boundaries(bool afterPreparation)
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_all");var identity=await f.Identity();var guard=Guard(f,identity);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
        {
            if(afterPreparation)await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));
            f.Row("config_page",long.Parse(Page(f,"yz_manager")))["is_update"]=0;
            if(!afterPreparation)await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));
            else{var original=Scope(f.Row("yz_manager",30));await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,original,[]));}return true;
        }));AssertNoWrites(f);
    }
    /// <summary>后台被同事务前序步骤搬入未准备项目时也冲突，不能凭旧admin行快照继续。</summary>
    [Fact]
    public async Task Changed_admin_project_cannot_expand_existing_project_locks()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));f.Row("yz_admin_type",91)["yz_project_id"]=long.Parse(f.DemoProject);
            var original=Scope(f.Row("yz_manager",30));await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,original,original,[]));return true;
        }));Assert.Contains("YZP_CONFLICT",error.Message);AssertNoWrites(f);
    }
    /// <summary>传给最终钩子的原范围不能与准备时真正锁定的记录不符。</summary>
    [Fact]
    public async Task Forged_original_scope_does_not_pass_final_validation()
    {
        using var f=new PrincipalRealmFixture(super:false);await Seed(f);OtherAdmin(f);Grant(f,"yz_manager","is_update");var identity=await f.Identity();var guard=Guard(f,identity);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await guard.BeforeMutationAsync(Preparation(f,identity,"yz_manager",RecordMutationOperation.Update,30));var original=Scope(f.Row("yz_manager",30));var forged=new Dictionary<string,object?>(original){["yz_admin_type_id"]=long.Parse(f.PlatformAdmin)};
            await guard.BeforeWriteAsync(Validation(f,identity,"yz_manager",RecordMutationOperation.Update,30,forged,original,[]));return true;
        }));Assert.Contains("YZP_CONFLICT",error.Message);AssertNoWrites(f);
    }

    private static SecurityRealmMutationGuard Guard(PrincipalRealmFixture f,AuthenticatedIdentity identity)
    {
        var context=new DefaultHttpContext();context.Items[typeof(AuthenticatedIdentity)]=identity;var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},f.Registry);
        return new(f.Store,f.Registry,auth,new(f.Store,f.Registry,new(f.Store,f.Registry)));
    }
    private static RecordMutationPreparation Preparation(PrincipalRealmFixture f,AuthenticatedIdentity identity,string table,RecordMutationOperation operation,long? id,Dictionary<string,object?>? hints=null)=>
        new(Page(f,table),table,operation,identity.Actor,f.Session.CurrentTransaction!){TargetId=id,ScopeValues=hints??new()};
    private static RecordMutationValidation Validation(PrincipalRealmFixture f,AuthenticatedIdentity identity,string table,RecordMutationOperation operation,long id,IReadOnlyDictionary<string,object?> original,IReadOnlyDictionary<string,object?> final,string[] submitted)=>
        new(Page(f,table),table,operation,id,identity.Actor,original,final,submitted.ToHashSet(StringComparer.Ordinal),f.Session.CurrentTransaction!);
    private static Dictionary<string,object?> Scope(Dictionary<string,object?> row)=>row.Where(pair=>new[]{"yz_admin_type_id","yz_project_id","yz_role_ids","is_super","is_scope","is_built_in"}.Contains(pair.Key)).ToDictionary();
    private static string Page(PrincipalRealmFixture f,string table)=>f.Registry.Platform.ConfigPages.Single(page=>f.Registry.Platform.ConfigTables.Single(owner=>owner.Id==page.ConfigTableId).TableName==table).Id;
    private static void Grant(PrincipalRealmFixture f,string table,params string[] actions)=>f.Add("yz_role_permission",("id",71L),("yz_role_id",10L),("config_page_id",long.Parse(Page(f,table),CultureInfo.InvariantCulture)),("permission_values",JsonSerializer.Serialize(actions)));
    private static void OtherAdmin(PrincipalRealmFixture f)
    {
        f.Add("yz_admin_type",("id",91L),("yz_project_id",long.Parse(f.PlatformProject)));f.Add("yz_role",("id",21L),("yz_admin_type_id",91L),("is_super",1));
        f.Add("yz_manager",("id",30L),("yz_admin_type_id",91L),("yz_project_id",long.Parse(f.PlatformProject)),("yz_role_ids","[\"21\"]"));
    }
    private static void TargetRole(PrincipalRealmFixture f)=>f.Add("yz_role",("id",30L),("yz_admin_type_id",long.Parse(f.PlatformAdmin)),("yz_project_id",long.Parse(f.PlatformProject)),("is_super",0));
    private static async Task Seed(PrincipalRealmFixture f){var metadata=new MetadataRepository(f.Store,f.Registry);await metadata.SeedAsync(f.Registry.Platform);await metadata.SeedAsync(f.Registry.Demo);f.Provider.Commands.Clear();}
    private static void AssertNoWrites(PrincipalRealmFixture f)=>Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE ",StringComparison.Ordinal)||sql.StartsWith("INSERT ",StringComparison.Ordinal)||sql.StartsWith("DELETE ",StringComparison.Ordinal));
}
