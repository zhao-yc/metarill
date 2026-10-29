using System.Globalization;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Security;

public sealed record AuthenticatedIdentity(RuntimeActor Actor,IReadOnlySet<string> Permissions,long SessionId,long? GrantId)
{
    /// <summary>真实身份后台不随开发工作区切换；超级角色仅在自己的后台有效。</summary>
    public string PrincipalAdminTypeId {get;init;}="";
    public string PrincipalProjectId {get;init;}="";
    public bool IsRealmSuper {get;init;}
    /// <summary>本次真实认证的逐动作范围决策，不输出到HTTP或会话持久化，后续记录计划必须显式消费。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> ExactDataManagePermissions {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>仅实际授予该按钮的范围角色参与决策，不能由页面或其它按钮权限取消。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> ExactDataManageButtonIds {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>继承的全功能来源只供复制/导入使用，不能绕过来源页标准动作开关。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string> InheritedAllPermissions {get;init;}=new HashSet<string>(StringComparer.Ordinal);
    /// <summary>文件来源仅聚合直接/真实父子查询；null兼容旧直接页身份，正式认证始终提供独立集合。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string>? FileQueryPermissions {get;init;}
    /// <summary>独立保留文件来源查询的范围决策，不能借选择页普通角色取消限制。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlySet<string>? FileExactDataManagePermissions {get;init;}
    /// <summary>真实后台仅声明一项账号匹配值，永不参与HTTP或会话序列化。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string ExactDataManageMatchField {get;init;}="";
    [System.Text.Json.Serialization.JsonIgnore] public System.Text.Json.JsonElement? ExactDataManageMatchValue {get;init;}
    public IReadOnlySet<string> MenuIds {get;init;}=new HashSet<string>();
    public IReadOnlySet<string> ButtonIds {get;init;}=new HashSet<string>();
    /// <summary>认证与锁后复核共用同一权限投影；保留原会话/Actor来源，拒绝身份语义被替换。</summary>
    internal AuthenticatedIdentity WithPermissionContext(PrincipalRoleRealm realm)
    {
        if(PrincipalProjectId!=realm.ProjectId||PrincipalAdminTypeId!=realm.AdminTypeId||IsRealmSuper!=realm.IsSuper||Actor.IsAdministrator!=realm.IsPlatformAdministrator)throw new UnauthorizedAccessException("权限上下文与真实主体不符。");
        return this with{Permissions=realm.Permissions,ExactDataManagePermissions=realm.ExactDataManagePermissions,ExactDataManageButtonIds=realm.ExactDataManageButtonIds,InheritedAllPermissions=realm.InheritedAllPermissions,FileQueryPermissions=realm.FileQueryPermissions,FileExactDataManagePermissions=realm.FileExactDataManagePermissions,ExactDataManageMatchField=realm.ExactDataManageMatchField,ExactDataManageMatchValue=realm.ExactDataManageMatchValue,MenuIds=realm.MenuIds,ButtonIds=realm.ButtonIds};
    }
}

public sealed class AuthenticationService(SqlRowStore store,IIdGenerator ids,ISecurityResourceRegistry registry,ISecuritySessionPolicy sessionPolicy)
{
    /// <summary>网页登录校验账号状态，令牌仅保存摘要；原始密码与令牌不写日志。</summary>
    public Task<(Dictionary<string,object?> User,string Token)> LoginAsync(string username,string password,CancellationToken cancellationToken=default)=>
        store.Session.InTransactionAsync(async()=>
        {
            // 与账号重置共用用户名唯一索引锁；旧密码验证和会话创建不可跨越已提交重置。
            var account=(await store.ReadForUpdateAsync("yz_manager",new Dictionary<string,object?>{{"name",username}},cancellationToken)).SingleOrDefault();
            if(account is null||!AccountActive(account)||!PasswordHasher.Verify(password,SqlRowStore.Text(account,"password")))
                throw new UnauthorizedAccessException("用户名或密码不正确。");
            var realm=await PrincipalRolePolicy.LoadAsync(store,registry,account,cancellationToken,includePermissions:false) ?? throw new UnauthorizedAccessException("用户名或密码不正确。");
            var token=PasswordHasher.Token();
            await store.InsertAsync("sys_session",new Dictionary<string,object?>
            {{"id",ids.NextId()},{"account_id",SqlRowStore.Id(account)},{"token_hash",PasswordHasher.Digest(token)},{"expires_at",DateTime.UtcNow.AddHours(8)},{"kind","web"},{"project_id",long.Parse(realm.ProjectId,CultureInfo.InvariantCulture)},{"insert_time",DateTime.UtcNow}},cancellationToken);
            return (PublicAccount(account),token);
        },cancellationToken);
    /// <summary>每次重新验证真实会话、身份后台与角色；宿主扩展只负责非Web会话的附加证明。</summary>
    public async Task<AuthenticatedIdentity?> AuthenticateAsync(string token,string? selectedProject,CancellationToken cancellationToken=default)
    {
        if(token.Length is < 32 or > 200)return null;
        var session=(await store.ReadAsync("sys_session",new Dictionary<string,object?>{{"token_hash",PasswordHasher.Digest(token)}},cancellationToken)).SingleOrDefault();
        if(session is null||SqlRowStore.Text(session,"kind") is not ("web" or "agent")||Convert.ToDateTime(session["expires_at"],CultureInfo.InvariantCulture)<=DateTime.UtcNow)return null;
        var account=(await store.ReadAsync("yz_manager",new Dictionary<string,object?>{{"id",SqlRowStore.Id(session,"account_id")}},cancellationToken)).SingleOrDefault();
        if(account is null||!AccountActive(account))return null;
        var realm=await PrincipalRolePolicy.LoadAsync(store,registry,account,cancellationToken);if(realm is null)return null;
        var project=string.IsNullOrWhiteSpace(selectedProject)?SqlRowStore.Text(session,"kind")=="agent"?SqlRowStore.Text(session,"project_id"):realm.ProjectId:selectedProject;
        if(!long.TryParse(project,NumberStyles.None,CultureInfo.InvariantCulture,out var projectId)||projectId<=0)return null;
        var registered=(await store.ReadAsync("yz_project",new Dictionary<string,object?>{{"id",projectId}},cancellationToken)).SingleOrDefault();
        if(registered is null)return null;
        if(!realm.IsPlatformAdministrator&&project!=realm.ProjectId)return null;
        // Agent签发域由宿主固定目录证明；应用超级角色仍不会产生平台身份。
        if(SqlRowStore.Text(session,"kind")=="agent"&&(!registry.IsAgentRealm(realm.ProjectId,realm.AdminTypeId)||project!=SqlRowStore.Text(session,"project_id")))return null;
        long? grantId=session.GetValueOrDefault("grant_id") is null?null:SqlRowStore.Id(session,"grant_id");
        if(SqlRowStore.Text(session,"kind")=="agent")
        {
            if(grantId is not {} grant)return null;
            // 直接认证仍要求宿主证明grant绑定；平台保留原OAuth校验，不因共享程序集提取降级。
            if(!await sessionPolicy.ValidateAgentSessionAsync(new(grant,SqlRowStore.Id(account),SqlRowStore.Text(session,"project_id")),cancellationToken))return null;
        }
        else if(grantId is not null)return null;
        var actor=new RuntimeActor(SqlRowStore.Id(account),SqlRowStore.Text(account,"name"),realm.IsPlatformAdministrator,project,realm.AdminTypeId);
        return new AuthenticatedIdentity(actor,realm.Permissions,SqlRowStore.Id(session),grantId){PrincipalAdminTypeId=realm.AdminTypeId,PrincipalProjectId=realm.ProjectId,IsRealmSuper=realm.IsSuper}.WithPermissionContext(realm);
    }
    /// <summary>有效期与登录起止时间在每次请求重新检查，停用立即生效。</summary>
    public static bool AccountActive(IReadOnlyDictionary<string,object?> account)=>AccountActive(account,DateTimeOffset.UtcNow,TimeZoneInfo.Local);
    /// <summary>原账号字段是宿主日历时间；显式瞬时保留自身时区，绝不把这些字段和UTC会话期限混用。</summary>
    internal static bool AccountActive(IReadOnlyDictionary<string,object?> account,DateTimeOffset now,TimeZoneInfo calendarZone)
    {
        ArgumentNullException.ThrowIfNull(calendarZone);
        if(SqlRowStore.Text(account,"is_disabled")=="1")return false;
        foreach(var field in new[]{"validity_time","login_stop_time"})
            if(account.GetValueOrDefault(field) is{} end&&(!TryCalendarUtcTicks(end,calendarZone,out var until)||until<=now.UtcTicks))return false;
        return account.GetValueOrDefault("login_rise_time") is not{} start||TryCalendarUtcTicks(start,calendarZone,out var from)&&from<=now.UtcTicks;
    }
    /// <summary>未标时区的库值按原Java atZone解释：重叠取较早瞬时，跳时向前跨过缺口；坏值拒绝。</summary>
    private static bool TryCalendarUtcTicks(object value,TimeZoneInfo zone,out long ticks)
    {
        ticks=0;
        try
        {
            if(value is DateTimeOffset instant){ticks=instant.UtcTicks;return true;}
            var calendar=Convert.ToDateTime(value,CultureInfo.InvariantCulture);
            if(calendar.Kind==DateTimeKind.Utc){ticks=calendar.Ticks;return true;}
            if(calendar.Kind==DateTimeKind.Local){ticks=calendar.ToUniversalTime().Ticks;return true;}
            if(zone.IsInvalidTime(calendar))
            {
                var before=calendar;var after=calendar;
                // 时区偏移范围有界，按小时找缺口两侧；同时覆盖非整小时和跳过一日的变化。
                for(var i=0;i<48&&zone.IsInvalidTime(before);i++)before=before.AddHours(-1);
                for(var i=0;i<48&&zone.IsInvalidTime(after);i++)after=after.AddHours(1);
                if(zone.IsInvalidTime(before)||zone.IsInvalidTime(after))return false;
                var gap=zone.GetUtcOffset(after)-zone.GetUtcOffset(before);if(gap<=TimeSpan.Zero)return false;
                calendar=calendar.Add(gap);if(zone.IsInvalidTime(calendar))return false;
            }
            var offset=zone.IsAmbiguousTime(calendar)?zone.GetAmbiguousTimeOffsets(calendar).Max():zone.GetUtcOffset(calendar);
            // ticks比较避免正负时区把合法DateTime.Min/Max边界转换成不可构造的DateTimeOffset。
            ticks=calendar.Ticks-offset.Ticks;return true;
        }
        catch(Exception error)when(error is FormatException or InvalidCastException or ArgumentException or OverflowException or InvalidTimeZoneException){return false;}
    }
    /// <summary>仅将本次真实会话逻辑注销，保留审计与既有会话存储语义。</summary>
    public Task LogoutAsync(long sessionId,CancellationToken cancellationToken=default)=>store.UpdateAsync("sys_session",sessionId,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}},cancellationToken);
    public static Dictionary<string,object?> PublicAccount(IReadOnlyDictionary<string,object?> row)=>new()
    {["id"]=SqlRowStore.Id(row),["name"]=SqlRowStore.Text(row,"name"),["realName"]=SqlRowStore.Text(row,"real_name"),["avatar"]=SqlRowStore.Text(row,"avatar"),["yzAdminTypeId"]=SqlRowStore.Text(row,"yz_admin_type_id")};
}
