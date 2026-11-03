using Metarill.Runtime.Persistence;
using Microsoft.Extensions.Logging;

namespace Metarill.Runtime.Security;

/// <summary>失败原因只取服务端枚举，不能把请求密码、验证码或异常正文写入日志。</summary>
public enum LoginFailureReason { UnknownAccount, InvalidPassword, UnavailableAccount, UnavailableRealm }

public sealed partial class AuthenticationService
{
    private const string FailureContextKey="Metarill.LoginFailure";
    // 沿用原SYS_LOGIN_FAIL事件8及执行失败2，不随后端语言改变。
    private const int LoginFailureAction=8,FailedExecution=2;
    private sealed record LoginFailureContext(long? AccountId,LoginFailureReason Reason);

    /// <summary>保留既有异常类型和公开提示，仅在进程内携带无秘密的可信失败上下文。</summary>
    public static UnauthorizedAccessException RejectLogin(long? accountId,LoginFailureReason reason,string message="用户名或密码不正确。")
    {
        var failure=new UnauthorizedAccessException(message);failure.Data[FailureContextKey]=new LoginFailureContext(accountId,reason);return failure;
    }
    /// <summary>拥有最外层锁的宿主须在认证回滚后调用；日志故障仅告警，不能覆盖原认证拒绝。</summary>
    public async Task RecordLoginFailureAsync(UnauthorizedAccessException failure,string username,string? clientIp,CancellationToken token=default)
    {
        if(store.Session.CurrentTransaction is not null)throw new InvalidOperationException("登录失败审计必须在认证事务回滚后记录。");
        var context=failure.Data[FailureContextKey] as LoginFailureContext??new(null,LoginFailureReason.UnknownAccount);
        if(System.Net.IPAddress.TryParse(clientIp,out var address)&&address.IsIPv4MappedToIPv6)clientIp=address.MapToIPv4().ToString();
        var name=AuditText(username,"未提供用户名");var ip=AuditText(clientIp,"未知");
        var reason=context.Reason switch{LoginFailureReason.InvalidPassword=>"密码错误",LoginFailureReason.UnavailableAccount=>"账号状态无效",LoginFailureReason.UnavailableRealm=>"账号或后台归属无效",_=>"用户名不存在"};
        var now=DateTime.Now;
        try
        {
            await store.Session.InTransactionAsync(async()=>
            {
                // 原日志字段由security元数据及同一适配器管理；不依赖HTTP输入或自动DDL。
                await store.InsertAsync("yz_manager_log",new Dictionary<string,object?>
                {
                    ["id"]=ids.NextId(),["name"]="后台管理员登录",["insert_time"]=now,["update_time"]=now,
                    ["yz_manager_id"]=context.AccountId,["yz_manager_name"]=name,["ip"]=ip,
                    ["action_type"]=LoginFailureAction,["execute_result"]=FailedExecution,
                    ["detail"]="后台管理员登录失败，原因："+reason+"；用户名："+name+"；IP："+ip+"；触发账号锁：否；触发IP锁：否"
                },token);return true;
            },token);
        }
        catch(Exception error)
        {
            // 不回显ADO异常、SQL、参数或用户秘密；原拒绝由调用者继续抛出。
            logger?.LogWarning("登录失败审计未能持久化，错误类型={ErrorType}",error.GetType().Name);
        }
    }
    /// <summary>审计显示值按原列预算截取，保留空值含义，不截出孤立代理字符。</summary>
    private static string AuditText(string? value,string fallback)
    {
        if(string.IsNullOrWhiteSpace(value))return fallback;
        var text=value[..Math.Min(value.Length,50)];if(char.IsHighSurrogate(text[^1]))text=text[..^1];return text.Length==0?fallback:text;
    }
}
