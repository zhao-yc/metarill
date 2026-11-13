using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Security.Tests;

/// <summary>实际共享策略对照原资源模式，窄目录测试不代替正式HTTP、当前读或实库验收。</summary>
public sealed class InheritedPermissionDecisionTests
{
    /// <summary>子页按同类动作继承；选择页只继承查询，is_all来源也不能扩成选择页维护权限。</summary>
    [Theory]
    [InlineData(PagePermissionInheritanceKind.Child,"is_query",true)]
    [InlineData(PagePermissionInheritanceKind.Child,"is_update",true)]
    [InlineData(PagePermissionInheritanceKind.Child,"is_export",true)]
    [InlineData(PagePermissionInheritanceKind.Child,"is_delete",true)]
    [InlineData(PagePermissionInheritanceKind.Selection,"is_query",true)]
    [InlineData(PagePermissionInheritanceKind.Selection,"is_update",false)]
    [InlineData(PagePermissionInheritanceKind.Selection,"is_export",false)]
    [InlineData(PagePermissionInheritanceKind.Selection,"is_delete",false)]
    public void Source_only_grants_keep_the_original_kind_boundary(PagePermissionInheritanceKind kind,string action,bool allowed)
    {
        var catalog=Catalog(new PagePermissionInheritanceLink("100","200",kind));var realm=Evaluate(catalog,[Role(5,1)],[Grant(5,"100",action)]);
        Assert.Equal(allowed,realm.Permissions.Contains("200:"+action));Assert.Equal(allowed,realm.ExactDataManagePermissions.Contains("200:"+action));
        Assert.Equal(allowed&&kind==PagePermissionInheritanceKind.Child&&action=="is_query",realm.FileQueryPermissions.Contains("200:is_query"));
    }
    /// <summary>资源模式合并真正命中来源；普通选择页来源只解除普通查询，不能解除文件精确范围。</summary>
    [Theory]
    [InlineData(PagePermissionInheritanceKind.Child,false)]
    [InlineData(PagePermissionInheritanceKind.Selection,true)]
    public void Mixed_sources_keep_file_scope_separate(PagePermissionInheritanceKind kind,bool fileExact)
    {
        var catalog=Catalog(new PagePermissionInheritanceLink("100","200",kind));var realm=Evaluate(catalog,[Role(5,1),Role(6,0)],[Grant(5,"200","is_query"),Grant(6,"100","is_query")]);
        Assert.Contains("200:is_query",realm.Permissions);Assert.DoesNotContain("200:is_query",realm.ExactDataManagePermissions);
        Assert.Contains("200:is_query",realm.FileQueryPermissions);Assert.Equal(fileExact,realm.FileExactDataManagePermissions.Contains("200:is_query"));
        var identity=Identity(realm);Assert.False(ExactDataManageIdentity.Resolve(identity,"200","detail","realName").IsRequired);
        var file=ExactDataManageIdentity.ResolveFileSource(identity,"200","realName");Assert.Equal(fileExact,file.IsRequired);
        if(fileExact){Assert.Equal("real_name",identity.ExactDataManageMatchField);Assert.Equal(new[]{"范围甲"},file.Read<string>());}
    }
    /// <summary>继承只读取直接授权，不做传递闭包，双向或环形引用不能扩大第三页权限。</summary>
    [Fact]
    public void Inheritance_is_not_transitive()
    {
        var catalog=Catalog(new PagePermissionInheritanceLink("100","200",PagePermissionInheritanceKind.Child),new("200","300",PagePermissionInheritanceKind.Child),new("200","100",PagePermissionInheritanceKind.Selection));
        var realm=Evaluate(catalog,[Role(5,1)],[Grant(5,"100","is_all")]);Assert.Contains("200:is_query",realm.Permissions);Assert.DoesNotContain(realm.Permissions,key=>key.StartsWith("300:",StringComparison.Ordinal));Assert.DoesNotContain("300:is_query",realm.FileQueryPermissions);
    }
    /// <summary>父页关闭修改时，继承is_all仅供复制/导入，不能作为目标标准修改的通用通行条件。</summary>
    [Fact]
    public void Inherited_all_cannot_bypass_the_source_action_switch()
    {
        var catalog=Catalog(new PagePermissionInheritanceLink("100","200",PagePermissionInheritanceKind.Child));catalog=catalog with{Pages=catalog.Pages.Select(page=>page.Id=="100"?page with{CanUpdate=false}:page).ToArray()};
        var realm=Evaluate(catalog,[Role(5,1)],[Grant(5,"100","is_all")]);Assert.Contains("200:is_query",realm.Permissions);Assert.DoesNotContain("200:is_update",realm.Permissions);Assert.DoesNotContain("200:is_all",realm.Permissions);Assert.Contains("200:is_all",realm.InheritedAllPermissions);Assert.Contains("200:is_all",realm.ExactDataManagePermissions);
    }
    /// <summary>当前目录只能收紧已发布的边/归属/开关；新增、重绑、跨后台均不能获得继承。</summary>
    [Theory]
    [InlineData("deleted")][InlineData("new")][InlineData("rebound")][InlineData("foreign")][InlineData("disabled")][InlineData("unpublished-action")]
    public void Current_catalog_requires_the_published_relation_and_capability(string scenario)
    {
        var catalog=Catalog(new PagePermissionInheritanceLink("100","200",PagePermissionInheritanceKind.Child));var current=catalog;
        switch(scenario)
        {
            case "deleted":current=catalog with{Inheritance=[]};break;
            case "new":catalog=catalog with{Inheritance=[]};break;
            case "rebound":current=catalog with{Inheritance=[new("300","200",PagePermissionInheritanceKind.Child)]};break;
            case "foreign":current=catalog with{Pages=catalog.Pages.Select(page=>page.Id=="200"?page with{AdminTypeId="20"}:page).ToArray()};break;
            case "disabled":current=catalog with{Pages=catalog.Pages.Select(page=>page.Id=="100"?page with{CanUpdate=false}:page).ToArray()};break;
            case "unpublished-action":catalog=catalog with{Pages=catalog.Pages.Select(page=>page.Id=="100"?page with{CanUpdate=false}:page).ToArray()};break;
        }
        var realm=Evaluate(catalog,[Role(5,1)],[Grant(5,"100","is_update")],current);Assert.DoesNotContain("200:is_update",realm.Permissions);Assert.DoesNotContain("200:is_update",realm.ExactDataManagePermissions);
    }
    /// <summary>选择来源无独立文件权限；改为真实子页来源后，文件及其范围按下一次决策生效。</summary>
    [Fact]
    public void Selection_only_file_access_is_denied_and_current_changes_are_recomputed()
    {
        var selection=Catalog(new PagePermissionInheritanceLink("100","200",PagePermissionInheritanceKind.Selection));var first=Evaluate(selection,[Role(5,1)],[Grant(5,"100","is_query")]);
        Assert.Contains("200:is_query",first.Permissions);Assert.DoesNotContain("200:is_query",first.FileQueryPermissions);
        var child=Catalog(new PagePermissionInheritanceLink("100","200",PagePermissionInheritanceKind.Child));var next=Evaluate(child,[Role(5,1)],[Grant(5,"100","is_query")]);Assert.Contains("200:is_query",next.FileQueryPermissions);Assert.Contains("200:is_query",next.FileExactDataManagePermissions);Assert.DoesNotContain("200:is_query",first.FileQueryPermissions);
        var revoked=Evaluate(child,[Role(5,1)],[]);Assert.Empty(revoked.FileQueryPermissions);Assert.Empty(revoked.ExactDataManagePermissions);
    }
    /// <summary>新增内部来源集合不改变原身份构造或HTTP序列化。</summary>
    [Fact]
    public void Internal_source_sets_do_not_appear_on_the_wire()
    {
        var realm=Evaluate(Catalog(new PagePermissionInheritanceLink("100","200",PagePermissionInheritanceKind.Child)),[Role(5,1)],[Grant(5,"100","is_all")]);var value=JsonSerializer.Serialize(Identity(realm),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("fileQuery",value,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("fileExact",value,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("inheritedAll",value,StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>直接调用生产策略，不在测试中复制聚合算法。</summary>
    private static PrincipalRoleRealm Evaluate(SecurityResourceCatalog catalog,IReadOnlyList<Dictionary<string,object?>> roles,IReadOnlyList<Dictionary<string,object?>> grants,SecurityResourceCatalog? current=null)=>
        PrincipalRolePolicy.Evaluate(new Registry(catalog),Row(("yz_role_ids","[\"5\",\"6\"]"),("real_name","范围甲")),Row(("id",10L)),1,roles,grants,current);
    /// <summary>身份保持与真实认证相同的来源复制，声明只来自当前后台。</summary>
    private static AuthenticatedIdentity Identity(PrincipalRoleRealm realm)=>new(new RuntimeActor(1,"账号",false,"1","10"),realm.Permissions,2,null)
    {PrincipalProjectId="1",PrincipalAdminTypeId="10",ExactDataManagePermissions=realm.ExactDataManagePermissions,ExactDataManageMatchField=realm.ExactDataManageMatchField,ExactDataManageMatchValue=realm.ExactDataManageMatchValue,FileQueryPermissions=realm.FileQueryPermissions,FileExactDataManagePermissions=realm.FileExactDataManagePermissions,InheritedAllPermissions=realm.InheritedAllPermissions};
    /// <summary>三页同后台且没有菜单，权限只来自真实资源和传入的可信关系。</summary>
    private static SecurityResourceCatalog Catalog(params PagePermissionInheritanceLink[] links)=>new("1",[new("1","records")],[new("100","1","1","10",true,true,true,true,true,true),new("200","1","1","10",true,true,true,true,true,true),new("300","1","1","10",true,true,true,true,true,true)],[],[]){Inheritance=links};
    /// <summary>范围与普通角色属于同一真实后台。</summary>
    private static Dictionary<string,object?> Role(long id,int scope)=>Row(("id",id),("yz_admin_type_id",10L),("is_scope",scope));
    /// <summary>授权保留真实动作编码，页面ID不从控件请求接受。</summary>
    private static Dictionary<string,object?> Grant(long role,string page,string action)=>Row(("yz_role_id",role),("config_page_id",page),("permission_values",JsonSerializer.Serialize(new[]{action})));
    /// <summary>只构造策略读取的原列字典。</summary>
    private static Dictionary<string,object?> Row(params (string Key,object? Value)[] values)=>values.ToDictionary(value=>value.Key,value=>value.Value,StringComparer.Ordinal);
    private sealed class Registry(SecurityResourceCatalog catalog):ISecurityResourceRegistry
    {
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[catalog];
        /// <summary>普通业务目录不声明平台管理员身份。</summary>
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
        /// <summary>匹配字段由可信发布夹具固定，不从请求或角色内容选择。</summary>
        public string? ExactDataManageManagerField(string projectId,string adminTypeId)=>"realName";
    }
}
