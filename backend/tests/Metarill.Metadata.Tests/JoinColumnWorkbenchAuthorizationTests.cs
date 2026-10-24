using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>配置入口的真实身份边界必须先于数据库连接，不接受设备授权或普通账号。</summary>
public sealed class JoinColumnWorkbenchAuthorizationTests
{
    /// <summary>虚构无效连接下仍须得到身份拒绝；数据库错误不能伪装成授权测试通过。</summary>
    [Theory]
    [InlineData(false,false,false)][InlineData(true,false,false)][InlineData(true,true,true)]
    public async Task Anonymous_ordinary_and_agent_identities_are_rejected_before_opening_database(bool authenticated,bool administrator,bool agent)
    {
        using var f=new PublishedFileFixture();var context=new DefaultHttpContext();if(authenticated)context.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(new RuntimeActor(11,"联接授权测试",administrator,"1",""),new HashSet<string>(),1,agent?1:null);
        var authorization=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},f.Registry);var store=new SqlRowStore(f.Sessions.Platform,f.Registry.Platform);var ids=new SnowflakeIdGenerator(854);var business=new MetadataWorkbenchBusiness(store,authorization,f.Registry,ids,new ConfigurationMutationObserver(store,f.Registry,ids));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.JoinColumnCandidatesAsync(JsonSerializer.SerializeToElement(new{configTableId="11"})));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>business.SaveJoinColumnsAsync(JsonSerializer.SerializeToElement(new{configTableId="11",selections=new[]{new{sourceColumnId="12",targetColumnId="13"}}})));
    }
}
