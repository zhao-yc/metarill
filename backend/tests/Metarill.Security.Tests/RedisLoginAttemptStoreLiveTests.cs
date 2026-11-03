using Metarill.Runtime.Security;
using StackExchange.Redis;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>只能连接本轮独占Redis，先核服务端run_id；没有明确探针环境时列为跳过而非通过。</summary>
public sealed class RedisLoginAttemptStoreLiveTests
{
    /// <summary>真实客户端、同应用两个实例、原Lua和服务端TTL一起验证，不使用内存计数替身。</summary>
    [RedisProbeFact]
    public async Task Real_clients_share_counts_keep_original_thresholds_and_reconnect_without_losing_state()
    {
        var connection=Environment.GetEnvironmentVariable("METARILL_LOGIN_REDIS_PROBE_CONNECTION")!;
        var expected=Environment.GetEnvironmentVariable("METARILL_LOGIN_REDIS_PROBE_RUN_ID")!;
        var scope=Environment.GetEnvironmentVariable("METARILL_LOGIN_REDIS_PROBE_SCOPE")!;
        Assert.Matches("^[a-f0-9]{40}$",expected);Assert.Matches("^probe:[a-f0-9]{32}$",scope);
        var options=ConfigurationOptions.Parse(connection);Assert.Single(options.EndPoints);Assert.Null(options.Password);Assert.True(options.DefaultDatabase is null or 0);
        // 仅合成实例的身份核验客户端允许INFO；不改变生产计数客户端或任何服务端ACL。
        options.AllowAdmin=true;options.ConnectTimeout=3000;options.AsyncTimeout=3000;using var inspector=await ConnectionMultiplexer.ConnectAsync(options);
        var server=inspector.GetServer(Assert.Single(inspector.GetEndPoints()));var info=await server.InfoAsync("server");Assert.Equal(expected,info.SelectMany(group=>group).Single(pair=>pair.Key=="run_id").Value);
        var db=inspector.GetDatabase(0);var privateOptions=new LoginFailureRedisOptions{ConnectionString=connection};
        using var first=new RedisLoginAttemptStore(privateOptions,scope);using var second=new RedisLoginAttemptStore(privateOptions,scope);
        const string name="unit-only-shared-user",ip="192.0.2.10";
        Assert.False((await first.CheckAsync(name,ip)).IsLocked);
        for(var i=0;i<4;i++)Assert.False((await (i%2==0?first:second).FailAsync(name,ip)).IsLocked);
        var keys=first.Keys(name,ip);var before=await db.KeyTimeToLiveAsync(keys[0]);Assert.True(before>TimeSpan.Zero&&before<=TimeSpan.FromMinutes(15));
        Assert.Equal(new LoginLockState(true,false),await second.FailAsync(name,ip));
        var accountLock=await db.KeyTimeToLiveAsync(keys[1]);Assert.True(accountLock>TimeSpan.Zero&&accountLock<=TimeSpan.FromMinutes(15));
        Assert.True(await db.KeyTimeToLiveAsync(keys[0])<=before);
        // 重新创建真实客户端不能丢计数／锁；这里不是Redis自身重启或完整应用重启验收。
        using(var recreated=new RedisLoginAttemptStore(privateOptions,scope))Assert.True((await recreated.CheckAsync(name,"::ffff:"+ip)).AccountLocked);
        await second.ClearFailuresAsync(name,ip);Assert.False(await db.KeyExistsAsync(keys[0]));Assert.False(await db.KeyExistsAsync(keys[2]));Assert.True((await first.CheckAsync(name,ip)).AccountLocked);
        using var isolated=new RedisLoginAttemptStore(privateOptions,scope+":other");Assert.False((await isolated.CheckAsync(name,ip)).IsLocked);
        for(var i=0;i<19;i++)Assert.False((await first.FailAsync("unit-only-ip-"+i,"192.0.2.20")).IsLocked);
        Assert.Equal(new LoginLockState(false,true),await second.FailAsync("unit-only-ip-19","192.0.2.20"));
        Assert.True((await first.CheckAsync("unit-only-fresh-user","192.0.2.20")).IpLocked);
        var ipKeys=first.Keys("unit-only-fresh-user","192.0.2.20");var ipLock=await db.KeyTimeToLiveAsync(ipKeys[3]);Assert.True(ipLock>TimeSpan.Zero&&ipLock<=TimeSpan.FromMinutes(30));
        var outcomes=await Task.WhenAll(Enumerable.Range(0,40).Select(i=>(i%2==0?first:second).FailAsync("unit-only-parallel","192.0.2.30")));
        var parallel=first.Keys("unit-only-parallel","192.0.2.30");Assert.Equal("40",(string?)await db.StringGetAsync(parallel[0]));Assert.Equal("40",(string?)await db.StringGetAsync(parallel[2]));Assert.Equal(36,outcomes.Count(value=>value.AccountLocked));Assert.Equal(21,outcomes.Count(value=>value.IpLocked));
        Assert.Equal(new LoginLockState(true,false),await first.CheckAsync("unit-only-parallel","192.0.2.30"));
        await db.KeyExpireAsync(keys[1],TimeSpan.FromMilliseconds(1));await Task.Delay(20);Assert.False((await second.CheckAsync(name,ip)).IsLocked);
        await first.FailAsync("unit-only-window","192.0.2.40");var window=first.Keys("unit-only-window","192.0.2.40");await db.KeyExpireAsync(window[0],TimeSpan.FromMilliseconds(1));await db.KeyExpireAsync(window[2],TimeSpan.FromMilliseconds(1));await Task.Delay(20);
        Assert.False((await second.FailAsync("unit-only-window","192.0.2.40")).IsLocked);Assert.Equal("1",(string?)await db.StringGetAsync(window[0]));
    }
}

/// <summary>只有协调器给出独占服务端标识及本轮scope时才开启真实Redis用例。</summary>
public sealed class RedisProbeFactAttribute:FactAttribute
{
    public RedisProbeFactAttribute()
    {
        if(new[]{"CONNECTION","RUN_ID","SCOPE"}.Any(key=>string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_LOGIN_REDIS_PROBE_"+key))))Skip="需要Server-200本轮独占Redis与服务端身份，默认不连接外部服务。";
    }
}
