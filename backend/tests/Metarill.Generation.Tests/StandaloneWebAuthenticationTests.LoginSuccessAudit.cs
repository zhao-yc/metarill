using System.Net;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>实际包与共享认证都在成功后写原日历时间和可信IP，原账号的其它字段保持。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Successful_login_updates_original_audit_fields_in_session_transaction(bool direct)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var row=f.Provider.Rows["yz_manager"][0];
        row["last_login_time"]=new DateTime(2000,1,1);row["login_ip"]="192.0.2.1";row["update_time"]=new DateTime(2000,1,1);
        var before=OtherFields(row);var from=DateTime.Now;f.Accessor.HttpContext!.Connection.RemoteIpAddress=IPAddress.Parse("::ffff:192.0.2.10");
        var login=direct?await f.Authentication.LoginFromWebAsync(Username,Password,"::ffff:192.0.2.10"):await f.Login(Password);
        row=f.Provider.Rows["yz_manager"][0];var at=Assert.IsType<DateTime>(row.GetValueOrDefault("last_login_time"));
        Assert.InRange(at,from,DateTime.Now);Assert.Equal(at,row["update_time"]);Assert.Equal("192.0.2.10",SqlRowStore.Text(row,"login_ip"));Assert.Equal(before,OtherFields(row));
        Assert.NotNull(await f.Resolve(login.Token));Assert.Single(f.Provider.Rows["sys_session"]);Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_manager_log")??[]);
        Assert.All(f.Provider.WriteTransactions,Assert.True);Assert.Null(f.Session.CurrentTransaction);
    }
    /// <summary>成功审计更新或其后的会话插入失败，账号与会话都恢复原值，不能留下半次登录。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Successful_login_audit_and_session_are_atomic_on_storage_failure(bool failAudit)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var row=f.Provider.Rows["yz_manager"][0];
        row["last_login_time"]=new DateTime(2000,1,1);row["login_ip"]="192.0.2.1";var before=f.Snapshot();
        if(failAudit)f.Provider.FailOnUpdate="yz_manager";else f.Provider.FailOnInsert="sys_session";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Login(Password));Assert.Equal(before,f.Snapshot());Assert.Null(f.Session.CurrentTransaction);Assert.Empty(f.Provider.Rows.GetValueOrDefault("sys_session")??[]);
        // 失败不是密码拒绝，不可被转换为密码失败审计。
        Assert.Empty(f.Provider.Rows.GetValueOrDefault("yz_manager_log")??[]);
    }
    /// <summary>原Java只去除用户名两端不大于空格的字符；密码及不间断空格不能一起被归一化。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Original_username_normalization_does_not_trim_password_or_unicode_identity(bool direct)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());
        if(direct)await f.Authentication.LoginAsync(" \t"+Username+"\r\n",Password);
        else await package.Call(f.Web,"LoginAsync"," \t"+Username+"\r\n",Password,CancellationToken.None);
        Assert.Single(f.Provider.Rows["sys_session"]);
        foreach(var (name,password) in new[]{(Username," "+Password+" "),("\u00a0"+Username+"\u00a0",Password)})
        {
            var before=f.IdentitySnapshot();await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>
            {if(direct)await f.Authentication.LoginAsync(name,password);else await package.Call(f.Web,"LoginAsync",name,password,CancellationToken.None);});
            Assert.Equal(before,f.IdentitySnapshot());Assert.Single(f.Provider.Rows["sys_session"]);
        }
    }
    /// <summary>只允许原成功审计三字段变化；不会以整行重写掩盖其它账号状态变化。</summary>
    private static string OtherFields(Dictionary<string,object?> row)=>JsonSerializer.Serialize(row.Where(pair=>pair.Key is not("last_login_time" or "login_ip" or "update_time")).ToDictionary(pair=>pair.Key,pair=>pair.Value));
}
