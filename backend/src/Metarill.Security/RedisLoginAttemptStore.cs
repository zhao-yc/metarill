using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using StackExchange.Redis;

namespace Metarill.Runtime.Security;

/// <summary>登录计数使用独占应用前缀和Redis DB0；连接串只从私有宿主配置绑定。</summary>
public sealed class LoginFailureRedisOptions
{
    [JsonIgnore] public string ConnectionString {get;init;}="";
}

/// <summary>原5／20次失败规则；一条Lua同时操作四键，避免计数成功但期限未设置的并发窗口。</summary>
public sealed class RedisLoginAttemptStore:ILoginAttemptStore,IDisposable
{
    // 同一固定应用的键使用同一hash tag；不同项目、部署库不共享计数，也不在键名泄露用户名或IP。
    private readonly string prefix;private readonly Lazy<Task<ConnectionMultiplexer>> connection;
    internal const string Script="""
        local mode=ARGV[1]
        local account=ARGV[2]=='1'
        local ip=ARGV[3]=='1'
        if mode=='check' then
            if account and redis.call('EXISTS',KEYS[2])==1 then return {1,0} end
            if ip and redis.call('EXISTS',KEYS[4])==1 then return {0,1} end
            return {0,0}
        end
        if mode=='clear' then
            if account then redis.call('DEL',KEYS[1]) end
            if ip then redis.call('DEL',KEYS[3]) end
            return {0,0}
        end
        if mode~='fail' then return redis.error_reply('invalid login operation') end
        local function increase(enabled,failKey,lockKey,limit,duration)
            if not enabled then return 0 end
            local count=redis.call('INCR',failKey)
            if redis.call('PTTL',failKey)<0 then redis.call('PEXPIRE',failKey,900000) end
            if count>=limit then redis.call('SET',lockKey,'1','PX',duration) return 1 end
            return 0
        end
        return {increase(account,KEYS[1],KEYS[2],5,900000),increase(ip,KEYS[3],KEYS[4],20,1800000)}
        """;
    /// <summary>延迟共享连接，不在匿名查询或普通宿主启动时连接Redis；缺配置不得静默关闭保护。</summary>
    public RedisLoginAttemptStore(LoginFailureRedisOptions options,string applicationScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationScope);
        prefix="metarill:login:{"+Digest(applicationScope)+"}:";
        connection=new(async()=>
        {
            try
            {
                if(string.IsNullOrWhiteSpace(options.ConnectionString))throw new BusinessRuleException(Unavailable);
                var config=ConfigurationOptions.Parse(options.ConnectionString);
                if(config.DefaultDatabase is not(null or 0)||config.EndPoints.Count==0)throw new BusinessRuleException(Unavailable);
                config.DefaultDatabase=0;config.AbortOnConnectFail=false;config.ConnectTimeout=3000;config.AsyncTimeout=3000;config.SyncTimeout=3000;
                // 断线时不排队重放已过期的计数／清理操作，避免恢复后误清除新的失败状态。
                config.BacklogPolicy=BacklogPolicy.FailFast;return await ConnectionMultiplexer.ConnectAsync(config);
            }
            catch(Exception error)when(error is not BusinessRuleException){throw new BusinessRuleException(Unavailable);}
        });
    }
    internal const string Unavailable="登录安全校验暂不可用，请稍后再试";
    /// <summary>账号锁优先于IP锁，与原请求入口次序一致。</summary>
    public Task<LoginLockState> CheckAsync(string username,string? clientIp,CancellationToken token=default)=>ExecuteAsync("check",username,clientIp,token);
    /// <summary>仅用户名／密码失败调用；Cap拒绝和已锁定请求不得增加或延长计数。</summary>
    public Task<LoginLockState> FailAsync(string username,string? clientIp,CancellationToken token=default)=>ExecuteAsync("fail",username,clientIp,token);
    /// <summary>成功只清除失败计数，不删除可能由并发请求触发的锁。</summary>
    public async Task ClearFailuresAsync(string username,string? clientIp,CancellationToken token=default)=>await ExecuteAsync("clear",username,clientIp,token);
    /// <summary>所有Redis异常转为固定安全提示，不把地址、密码、键或内部异常链交给客户端。</summary>
    private async Task<LoginLockState> ExecuteAsync(string mode,string username,string? clientIp,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var account=AuthenticationService.NormalizeLoginUsername(username);var ip=NormalizeIp(clientIp);
        var keys=Keys(account,ip);
        try
        {
            var multiplexer=await connection.Value;token.ThrowIfCancellationRequested();
            var result=await multiplexer.GetDatabase(0).ScriptEvaluateAsync(Script,keys,new RedisValue[]{mode,string.IsNullOrWhiteSpace(account)?"0":"1",string.IsNullOrWhiteSpace(ip)?"0":"1"});
            var flags=(RedisResult[]?)result;if(flags is not{Length:2})throw new BusinessRuleException(Unavailable);
            var a=(int)flags[0];var i=(int)flags[1];if(a is not(0 or 1)||i is not(0 or 1))throw new BusinessRuleException(Unavailable);
            return new(a==1,i==1);
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
        catch(Exception error)when(error is not BusinessRuleException){throw new BusinessRuleException(Unavailable);}
    }
    /// <summary>可信连接地址沿原审计归一化，空值不生成一个所有请求共用的未知IP计数。</summary>
    internal static string NormalizeIp(string? value)=>System.Net.IPAddress.TryParse(value,out var ip)?(ip.IsIPv4MappedToIPv6?ip.MapToIPv4():ip).ToString():value??"";
    /// <summary>四键仅包含固定用途、应用摘要和身份摘要，空身份仍保持同槽但不会被读写。</summary>
    internal RedisKey[] Keys(string account,string ip)=>new RedisKey[]{prefix+"account:"+Digest(account)+":fail",prefix+"account:"+Digest(account)+":lock",prefix+"ip:"+Digest(ip)+":fail",prefix+"ip:"+Digest(ip)+":lock"};
    /// <summary>摘要只用于稳定隔离命名，不作为密码哈希或认证凭据。</summary>
    private static string Digest(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    /// <summary>宿主拥有共享连接；没有发起连接时不因释放而创建连接。</summary>
    public void Dispose()
    {
        if(!connection.IsValueCreated)return;
        _=connection.Value.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion)task.Result.Dispose();},TaskScheduler.Default);
    }
}
