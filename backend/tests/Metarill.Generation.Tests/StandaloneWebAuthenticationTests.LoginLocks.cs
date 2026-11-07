using Metarill.Core.Contracts;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>实际认证事务回滚后再计数和记录触锁标志；阈值拒绝不得创建会话。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Credential_failure_records_real_lock_flags_after_outer_rollback(bool account,bool ip)
    {
        var attempts=new AttemptProbe{FailureState=new(account,ip)};using var f=new OfflineApplication(package,attempts:attempts);await f.Initialize(Settings());f.Accessor.HttpContext!.Connection.RemoteIpAddress=System.Net.IPAddress.Parse("192.0.2.10");
        attempts.BeforeFailure=()=>Assert.Null(f.Session.CurrentTransaction);var before=f.IdentitySnapshot();
        if(account||ip)
        {
            var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>f.Login("错误的离线密码"));Assert.Equal(new LoginLockState(account,ip).Message,error.Message);
        }
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Login("错误的离线密码"));
        Assert.Equal(before,f.IdentitySnapshot());Assert.Equal(1,attempts.Failures);Assert.Equal(0,attempts.Clears);Assert.Equal(Username,attempts.Username);Assert.Equal("192.0.2.10",attempts.Ip);
        var detail=SqlRowStore.Text(Assert.Single(f.Provider.Rows["yz_manager_log"]),"detail");Assert.EndsWith("触发账号锁："+(account?"是":"否")+"；触发IP锁："+(ip?"是":"否"),detail);
    }
    /// <summary>已锁请求不查账号、不增计数、不清锁；只写原拒绝审计且无账号关联。</summary>
    [Theory][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Existing_lock_is_rejected_before_identity_and_never_increases_the_counter(bool account,bool ip)
    {
        var attempts=new AttemptProbe{Current=new(account,ip)};using var f=new OfflineApplication(package,attempts:attempts);await f.Initialize(Settings());var before=f.IdentitySnapshot();
        var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>f.Authentication.CheckLoginLocksAsync(" "+Username+" ","192.0.2.10"));Assert.Equal(new LoginLockState(account,ip).Message,error.Message);
        Assert.Equal(1,attempts.Checks);Assert.Equal(0,attempts.Failures);Assert.Equal(0,attempts.Clears);Assert.Equal(before,f.IdentitySnapshot());
        var row=Assert.Single(f.Provider.Rows["yz_manager_log"]);Assert.Null(row.GetValueOrDefault("yz_manager_id"));Assert.Contains(account?"账号处于临时锁定状态":"IP处于临时限制状态",SqlRowStore.Text(row,"detail"));
    }
    /// <summary>平台和完整包成功都在最外层真实提交后清计数，嵌套认证不会提前清除。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Successful_login_clears_counts_once_only_after_the_owner_commits(bool direct)
    {
        var attempts=new AttemptProbe();using var f=new OfflineApplication(package,attempts:attempts);await f.Initialize(Settings());var commits=f.Provider.Commits;
        attempts.BeforeClear=()=>{Assert.Null(f.Session.CurrentTransaction);Assert.Equal(commits+1,f.Provider.Commits);Assert.Single(f.Provider.Rows["sys_session"]);};
        if(direct)await f.Authentication.LoginFromWebAsync(Username,Password,"192.0.2.10");else await f.Login(Password);
        Assert.Equal(1,attempts.Clears);Assert.Equal(0,attempts.Failures);
    }
    /// <summary>会话插入故障整笔回滚，不能把正确口令当成一次成功并清除失败计数。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Session_tail_failure_never_clears_counts(bool direct)
    {
        var attempts=new AttemptProbe();using var f=new OfflineApplication(package,attempts:attempts);await f.Initialize(Settings());f.Provider.FailOnInsert="sys_session";
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>{if(direct)await f.Authentication.LoginAsync(Username,Password);else await f.Login(Password);});
        Assert.Equal(0,attempts.Clears);Assert.Equal(0,attempts.Failures);Assert.Empty(f.Provider.Rows.GetValueOrDefault("sys_session")??[]);
    }
    /// <summary>停用或归属拒绝不冒充原口令错误，不触发未知账号的失败计数。</summary>
    [Theory][InlineData(LoginFailureReason.UnavailableAccount)][InlineData(LoginFailureReason.UnavailableRealm)]
    public async Task Non_credential_rejection_is_audited_without_changing_failure_counts(LoginFailureReason reason)
    {
        var attempts=new AttemptProbe();using var f=new OfflineApplication(package,attempts:attempts);await f.Initialize(Settings());
        await f.Authentication.RecordLoginFailureAsync(AuthenticationService.RejectLogin(null,reason),Username,"192.0.2.10");Assert.Equal(0,attempts.Failures);Assert.Single(f.Provider.Rows["yz_manager_log"]);
    }
    /// <summary>受控存储只观察跨模块次序与返回状态，不复制Redis计数／期限算法。</summary>
    private sealed class AttemptProbe:ILoginAttemptStore
    {
        internal LoginLockState Current,FailureState;internal int Checks,Failures,Clears;internal string? Username,Ip;internal Action? BeforeFailure,BeforeClear;
        public Task<LoginLockState> CheckAsync(string username,string? ip,CancellationToken token=default){Checks++;Username=username;Ip=ip;return Task.FromResult(Current);}
        public Task<LoginLockState> FailAsync(string username,string? ip,CancellationToken token=default){BeforeFailure?.Invoke();Failures++;Username=username;Ip=ip;return Task.FromResult(FailureState);}
        public Task ClearFailuresAsync(string username,string? ip,CancellationToken token=default){BeforeClear?.Invoke();Clears++;Username=username;Ip=ip;return Task.CompletedTask;}
    }
}
