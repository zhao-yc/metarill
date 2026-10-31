using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>真实SqlRowStore执行固定参数化集合命令；离线传输仅记录SQL，不证明真实RR锁等待。</summary>
public sealed class SecurityCurrentReadSetTests
{
    [Fact]
    public async Task Parameterized_set_preserves_registered_projection_soft_delete_scope_and_actual_transaction()
    {
        using var fixture = new PrincipalRealmFixture();
        fixture.Add("yz_role",("id",11L),("yz_admin_type_id",long.Parse(fixture.DemoAdmin)),("name","另后台"));
        fixture.Add("yz_role",("id",12L),("yz_admin_type_id",long.Parse(fixture.PlatformAdmin)),("delete_no",DateTime.UtcNow));
        await fixture.Session.InTransactionAsync(async () =>
        {
            var rows = await fixture.Store.ReadForUpdateByIdsAsync("yz_role","id",new long[]{12,11,10},new Dictionary<string,object?>{{"yz_admin_type_id",long.Parse(fixture.PlatformAdmin)}});
            Assert.Equal(10L,SqlRowStore.Id(Assert.Single(rows)));return true;
        });
        var sql=Assert.Single(fixture.Provider.Commands);
        Assert.Contains("`delete_no` IS NULL",sql);Assert.Contains("`yz_admin_type_id`=@p0",sql);Assert.Contains("`id` IN (@p1,@p2,@p3)",sql);Assert.EndsWith(" ORDER BY `id` FOR UPDATE",sql);
        Assert.DoesNotContain(" IN (10",sql);Assert.Contains("`name`",sql);Assert.Null(fixture.Session.CurrentTransaction);
    }

    [Fact]
    public async Task Empty_set_is_empty_without_any_command_and_still_requires_current_transaction()
    {
        using var fixture=new PrincipalRealmFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.Store.ReadForUpdateByIdsAsync("yz_role","id",Array.Empty<long>()));
        await fixture.Session.InTransactionAsync(async()=>{Assert.Empty(await fixture.Store.ReadForUpdateByIdsAsync("yz_role","id",Array.Empty<long>()));return true;});
        Assert.Empty(fixture.Provider.Commands);
    }

    [Theory]
    [InlineData("duplicate")][InlineData("zero")][InlineData("negative")][InlineData("tooMany")][InlineData("unknownColumn")][InlineData("stringColumn")][InlineData("unknownTable")]
    public async Task Invalid_set_or_identifier_is_rejected_before_command(string scenario)
    {
        using var fixture=new PrincipalRealmFixture();var ids=scenario switch{"duplicate"=>new long[]{10,10},"zero"=>[0],"negative"=>[-1],"tooMany"=>Enumerable.Range(1,1001).Select(value=>(long)value).ToArray(),_=>[10]};
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Session.InTransactionAsync(async()=>
        {await fixture.Store.ReadForUpdateByIdsAsync(scenario=="unknownTable"?"yz_role WHERE 1=1":"yz_role",scenario=="unknownColumn"?"id) OR 1=1":"stringColumn"==scenario?"name":"id",ids);return true;}));
        Assert.Empty(fixture.Provider.Commands);
    }

    [Fact]
    public async Task Canceled_set_never_opens_an_unbounded_read()
    {
        using var fixture=new PrincipalRealmFixture();using var source=new CancellationTokenSource();source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>fixture.Session.InTransactionAsync(async()=>
        {await fixture.Store.ReadForUpdateByIdsAsync("yz_role","id",new long[]{10},cancellationToken:source.Token);return true;}));
        Assert.Empty(fixture.Provider.Commands);
    }
}
