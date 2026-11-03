using System.Net;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原失败审计是认证失败后的持久副作用，不可随密码校验回滚或泄露凭据。</summary>
public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>共享认证与实际包模板分别覆盖，失败后账号／会话不变，仅增加原字段的审计。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Wrong_password_persists_original_audit_after_authentication_rollback(bool direct)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());f.Accessor.HttpContext!.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.10");
        var account=JsonSerializer.Serialize(f.Provider.Rows["yz_manager"]);var commits=f.Provider.Commits;var rollbacks=f.Provider.Rollbacks;const string wrong="仅供离线校验的错误密码";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>{if(direct)await f.Authentication.LoginAsync(Username,wrong);else await f.Login(wrong);});
        Assert.Equal(account,JsonSerializer.Serialize(f.Provider.Rows["yz_manager"]));Assert.Empty(f.Provider.Rows.GetValueOrDefault("sys_session")??[]);Assert.Null(f.Session.CurrentTransaction);
        var row=Assert.Single(f.Provider.Rows.GetValueOrDefault("yz_manager_log")??[]);Assert.True(SqlRowStore.Id(row)>9007199254740991);Assert.Equal(SqlRowStore.Id(f.Provider.Rows["yz_manager"][0]),SqlRowStore.Id(row,"yz_manager_id"));
        // 字段集合来自V56原Java真实INSERT，包含原自动填充的两个时间字段。
        Assert.Equal(new[]{"action_type","detail","execute_result","id","insert_time","ip","name","update_time","yz_manager_id","yz_manager_name"},row.Keys.OrderBy(name=>name,StringComparer.Ordinal));Assert.Equal(row["insert_time"],row["update_time"]);
        Assert.Equal(Username,SqlRowStore.Text(row,"yz_manager_name"));Assert.Equal("后台管理员登录",SqlRowStore.Text(row,"name"));Assert.Equal("8",SqlRowStore.Text(row,"action_type"));Assert.Equal("2",SqlRowStore.Text(row,"execute_result"));
        Assert.Contains("密码",SqlRowStore.Text(row,"detail"));Assert.DoesNotContain(wrong,JsonSerializer.Serialize(row));Assert.DoesNotContain(SqlRowStore.Text(f.Provider.Rows["yz_manager"][0],"password"),JsonSerializer.Serialize(row));
        Assert.Equal(direct?"未知":"192.0.2.10",SqlRowStore.Text(row,"ip"));Assert.Equal(commits+1,f.Provider.Commits);Assert.Equal(rollbacks+1,f.Provider.Rollbacks);
    }
    /// <summary>双栈HTTP映射IPv4按原IPv4显示，IPv6本身不改变。</summary>
    [Theory][InlineData("::ffff:192.0.2.10","192.0.2.10")][InlineData("2001:db8::1","2001:db8::1")]
    public async Task Trusted_remote_address_preserves_original_ip_identity(string address,string expected)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());f.Accessor.HttpContext!.Connection.RemoteIpAddress=IPAddress.Parse(address);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Login("错误的离线密码"));Assert.Equal(expected,SqlRowStore.Text(Assert.Single(f.Provider.Rows["yz_manager_log"]),"ip"));
    }
    /// <summary>不存在的账号仍记录失败名与原事件类型，不伪造账号ID或创建会话。</summary>
    [Fact]
    public async Task Unknown_account_audit_has_no_fabricated_identity()
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>package.Call(f.Web,"LoginAsync","offline-missing-name","错误的离线密码",CancellationToken.None));
        var row=Assert.Single(f.Provider.Rows.GetValueOrDefault("yz_manager_log")??[]);Assert.Null(row.GetValueOrDefault("yz_manager_id"));Assert.Equal("offline-missing-name",SqlRowStore.Text(row,"yz_manager_name"));Assert.Equal("8",SqlRowStore.Text(row,"action_type"));Assert.Empty(f.Provider.Rows.GetValueOrDefault("sys_session")??[]);
    }
    /// <summary>异常正文即使带有秘密，也不能被当作原审计detail保存。</summary>
    [Fact]
    public async Task Audit_does_not_serialize_exception_message_or_untrusted_context()
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());const string secret="unit-only-sensitive-payload";
        var failure=new UnauthorizedAccessException(secret);failure.Data["Metarill.LoginFailure"]=secret;
        await f.Authentication.RecordLoginFailureAsync(failure,Username,"192.0.2.10");var row=Assert.Single(f.Provider.Rows["yz_manager_log"]);Assert.DoesNotContain(secret,JsonSerializer.Serialize(row));Assert.Null(row.GetValueOrDefault("yz_manager_id"));
    }
    /// <summary>调用者不能把审计加入尚未结束的认证事务，否则回滚会吞掉原持久副作用。</summary>
    [Fact]
    public async Task Audit_rejects_recording_inside_an_active_authentication_transaction()
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var before=f.Snapshot();
        await f.Session.InTransactionAsync(async()=>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Authentication.RecordLoginFailureAsync(new UnauthorizedAccessException(),Username,null));return true;
        });
        Assert.Equal(before,f.Snapshot());Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_manager_log")??[]);
    }
    /// <summary>嵌套共享认证只抛出原失败；拥有外层事务的宿主在回滚后恰好写一笔。</summary>
    [Fact]
    public async Task Nested_authentication_is_audited_once_by_outer_rollback_owner()
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var before=f.IdentitySnapshot();
        var failure=await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(()=>f.Authentication.LoginAsync(Username,"错误的离线密码")));
        Assert.Null(f.Session.CurrentTransaction);Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_manager_log")??[]);Assert.Equal(before,f.IdentitySnapshot());
        await f.Authentication.RecordLoginFailureAsync(failure,Username,"192.0.2.10");Assert.Single(f.Provider.Rows["yz_manager_log"]);Assert.Equal(before,f.IdentitySnapshot());
    }
    /// <summary>审计存储故障与原实现一样不把认证失败改成成功或内部错误，失败的审计事务不得泄漏。</summary>
    [Fact]
    public async Task Audit_failure_keeps_original_authentication_rejection_and_identity_state()
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var before=f.Snapshot();f.Provider.FailOnInsert="yz_manager_log";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Login("错误的离线密码"));Assert.Equal(before,f.Snapshot());Assert.Null(f.Session.CurrentTransaction);Assert.Empty(f.Provider.Rows.GetValueOrDefault("sys_session")??[]);
    }
}
