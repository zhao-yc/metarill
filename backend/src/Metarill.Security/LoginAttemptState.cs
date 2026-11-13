namespace Metarill.Runtime.Security;

/// <summary>只携带服务端账号／IP锁标志，不包含口令、令牌或连接配置。</summary>
public readonly record struct LoginLockState(bool AccountLocked,bool IpLocked)
{
    public bool IsLocked=>AccountLocked||IpLocked;
    public string Message=>AccountLocked?"该账号登录失败次数过多，请15分钟后再试":"当前登录请求过于频繁，请30分钟后再试";
}

/// <summary>共享存储负责原固定计数窗口和锁期限；业务数据库品牌不进入登录规则。</summary>
public interface ILoginAttemptStore
{
    Task<LoginLockState> CheckAsync(string username,string? clientIp,CancellationToken token=default);
    Task<LoginLockState> FailAsync(string username,string? clientIp,CancellationToken token=default);
    Task ClearFailuresAsync(string username,string? clientIp,CancellationToken token=default);
}
