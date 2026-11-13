using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>共享客户端的作用域、私有配置及无配置拒绝；不连接本机或外部Redis。</summary>
public sealed class LoginAttemptStoreTests
{
    /// <summary>连接缺失或非DB0在任何套接字建立前拒绝，提示和异常链不带私有配置。</summary>
    [Theory][InlineData("")][InlineData("unit-only.invalid:6379,password=unit-only-secret,defaultDatabase=5")][InlineData("not a valid option=unit-only-secret")]
    public async Task Invalid_private_configuration_never_disables_protection_or_leaks_secret(string connection)
    {
        using var store=new RedisLoginAttemptStore(new(){ConnectionString=connection},"unit-only-scope");
        var error=await Assert.ThrowsAsync<BusinessRuleException>(()=>store.CheckAsync("unit-only-user","192.0.2.10"));
        Assert.Equal(RedisLoginAttemptStore.Unavailable,error.Message);Assert.Null(error.InnerException);Assert.DoesNotContain("unit-only-secret",error.ToString());
    }
    /// <summary>Redis配置JSON和默认文本均不输出密码。</summary>
    [Fact]
    public void Private_connection_never_appears_in_option_serialization()
    {
        var options=new LoginFailureRedisOptions{ConnectionString="unit-only-secret"};Assert.DoesNotContain("unit-only-secret",JsonSerializer.Serialize(options));Assert.DoesNotContain("unit-only-secret",options.ToString()!);
    }
    /// <summary>账号与IP域不相撞、键保持同槽、应用间隔离且不包含身份明文。</summary>
    [Fact]
    public void Keys_are_private_domain_separated_and_stable_for_the_same_application()
    {
        using var a=new RedisLoginAttemptStore(new(),"unit-only-a");using var b=new RedisLoginAttemptStore(new(),"unit-only-b");using var replica=new RedisLoginAttemptStore(new(),"unit-only-a");
        var keys=a.Keys("unit-only-user","192.0.2.10").Select(key=>key.ToString()).ToArray();Assert.Equal(4,keys.Distinct().Count());
        Assert.All(keys,key=>{Assert.DoesNotContain("unit-only-user",key);Assert.DoesNotContain("192.0.2.10",key);});Assert.Single(keys.Select(key=>key.Split('{')[1].Split('}')[0]).Distinct());
        Assert.Equal(keys,replica.Keys("unit-only-user","192.0.2.10").Select(key=>key.ToString()));Assert.Empty(keys.Intersect(b.Keys("unit-only-user","192.0.2.10").Select(key=>key.ToString())));
    }
    /// <summary>调用方取消必须在连接创建前生效，不被误报为认证失败或连接故障。</summary>
    [Fact]
    public async Task Cancellation_before_any_operation_does_not_create_a_connection()
    {
        using var store=new RedisLoginAttemptStore(new(),"unit-only-scope");using var source=new CancellationTokenSource();source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.CheckAsync("unit-only-user","192.0.2.10",source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.FailAsync("unit-only-user","192.0.2.10",source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.ClearFailuresAsync("unit-only-user","192.0.2.10",source.Token));
    }
    /// <summary>IPv4映射地址与原地址共用同一计数身份；真正IPv6保持独立。</summary>
    [Theory][InlineData("::ffff:192.0.2.10","192.0.2.10")][InlineData("2001:0db8::1","2001:db8::1")][InlineData(null,"")]
    public void Address_normalization_preserves_the_original_client_identity(string? value,string expected)=>Assert.Equal(expected,RedisLoginAttemptStore.NormalizeIp(value));
}
