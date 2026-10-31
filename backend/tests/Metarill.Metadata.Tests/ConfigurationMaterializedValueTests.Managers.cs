using System.Text.Json;
using Metarill.Api.Controllers;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>只替换ADO传输；调用真实原Workspace边界和当前页权限，不把候选读取当作角色赋权。</summary>
public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>普通查询权即可读本项目账号；不强加超级身份门槛，不读取口令等认证材料。</summary>
    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task Original_manager_picker_returns_only_project_names_and_string_ids(bool administrator)
    {
        using var f=new Fixture();var page=f.Registry.FindPage("6031906822946816")!;
        f.Add("yz_manager",Row(("id",9007199254740993L),("yz_project_id",1L),("name","user"),("real_name","姓名"),("password","PRIVATE_PASSWORD_SENTINEL")));
        f.Add("yz_manager",Row(("id",11L),("yz_project_id",1L),("name","other"),("real_name",null)));
        f.Add("yz_manager",Row(("id",12L),("yz_project_id",2L),("name","foreign"),("real_name","外项目")));
        f.Add("yz_manager",Row(("id",13L),("yz_project_id",1L),("name","deleted"),("delete_no",DateTime.UtcNow)));
        var http=new DefaultHttpContext();
        http.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(new RuntimeActor(8,"实际页权限",administrator,"1",page.YzAdminTypeId),
            new HashSet<string>{"6031906822946816:is_query"},1,null){PrincipalProjectId=page.YzProjectId,PrincipalAdminTypeId=page.YzAdminTypeId};
        var authorization=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Registry);
        var response=JsonSerializer.SerializeToElement(await new WorkspaceController(f.Store,authorization).Managers(default),JsonDefaults.Options);
        var values=response.GetProperty("data").EnumerateArray().ToDictionary(item=>item.GetProperty("value").GetString()!,item=>item.GetProperty("text").GetString());
        Assert.Equal(2,values.Count);Assert.Equal("姓名—user",values["9007199254740993"]);Assert.Equal("null—other",values["11"]);
        var read=Assert.Single(f.Provider.Reads);
        Assert.StartsWith("SELECT "+string.Join(",",new[]{"id","real_name","name"}.Select(QuoteCandidate))+" FROM "+QuoteCandidate("yz_manager"),read.Sql);
        Assert.Contains(QuoteCandidate("delete_no")+" IS NULL",read.Sql);Assert.Contains(QuoteCandidate("yz_project_id")+"=@p0",read.Sql);Assert.Equal(1L,read.Values["p0"]);
        Assert.DoesNotContain("PRIVATE_PASSWORD_SENTINEL",response.GetRawText(),StringComparison.Ordinal);Assert.Null(read.Transaction);
    }

    /// <summary>匿名、无查询权和错误真实后台均须在账号SQL之前拒绝，工作区参数不能替代权限。</summary>
    [Theory]
    [InlineData("anonymous")][InlineData("noPermission")][InlineData("wrongRealm")]
    public async Task Original_manager_picker_rejects_unavailable_identity_or_page_permission_before_sql(string scenario)
    {
        using var f=new Fixture();var page=f.Registry.FindPage("6031906822946816")!;var http=new DefaultHttpContext();
        if(scenario!="anonymous")http.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(new RuntimeActor(8,"普通身份",false,"1",page.YzAdminTypeId),
            scenario=="noPermission"?new HashSet<string>():new HashSet<string>{"6031906822946816:is_query"},1,null)
            {PrincipalProjectId=page.YzProjectId,PrincipalAdminTypeId=scenario=="wrongRealm"?"999":page.YzAdminTypeId};
        var authorization=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=http},f.Registry);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new WorkspaceController(f.Store,authorization).Managers(default));Assert.Empty(f.Provider.Reads);
    }
}
