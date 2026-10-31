using System.Text.Json;
using System.Reflection;
using Metarill.Core.Metadata;
using Metarill.Runtime.Security;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>以正式四模型和生产权限策略证明原type=1查询继承，避免正式用例误断言直接撤权即整体拒绝。</summary>
public sealed class GlobalCachedTargetPermissionTests
{
    /// <summary>目标直接权为空时继承只读；来源撤权后查询消失，不扩大到修改、导出或文件读取。</summary>
    [Theory][InlineData(0,"string")][InlineData(0,"int64")][InlineData(1,"string")][InlineData(1,"int64")]
    public void Original_selection_inherits_query_from_the_real_consumer_page(int mode,string type)
    {
        var metadata=GlobalCachedTargetNativePreparation.Catalog(mode,type);var before=JsonSerializer.Serialize(metadata);var links=PagePermissionInheritance.Compile(metadata);
        Assert.Contains(new("30","10",PagePermissionInheritanceKind.Selection),links);
        var catalog=new SecurityResourceCatalog("100",metadata.ConfigTables.Select(table=>new SecurityTableResource(table.Id,table.TableName)).ToArray(),metadata.ConfigPages.Select(page=>new SecurityPageResource(page.Id,page.ConfigTableId,"100","9",true,true,true,true,true,true)).ToArray(),[],[]){Inheritance=links};
        var role=new Dictionary<string,object?>{{"id",5L},{"yz_admin_type_id",9L},{"is_scope",0}};var manager=new Dictionary<string,object?>{{"yz_role_ids","[\"5\"]"}};var admin=new Dictionary<string,object?>{{"id",9L}};
        foreach(var action in new[]{"is_query","is_all"})
        {
            var grants=new[]{Grant("30",JsonSerializer.Serialize(new[]{action})),Grant("10","[]")};var realm=Evaluate(catalog,manager,admin,[role],grants);
            Assert.Contains("10:is_query",realm.Permissions);Assert.DoesNotContain("10:is_update",realm.Permissions);Assert.DoesNotContain("10:is_export",realm.Permissions);Assert.DoesNotContain("10:is_query",realm.FileQueryPermissions);
        }
        var revoked=Evaluate(catalog,manager,admin,[role],[Grant("30","[]"),Grant("10","[]")]);Assert.DoesNotContain("10:is_query",revoked.Permissions);Assert.DoesNotContain("30:is_query",revoked.Permissions);Assert.Equal(before,JsonSerializer.Serialize(metadata));
    }
    /// <summary>生产聚合器保持内部可见性；测试反射调用同一实现，不增加友元或复制决策算法。</summary>
    private static PrincipalRoleRealm Evaluate(SecurityResourceCatalog catalog,IReadOnlyDictionary<string,object?> manager,IReadOnlyDictionary<string,object?> admin,IReadOnlyList<Dictionary<string,object?>> roles,IReadOnlyList<Dictionary<string,object?>> grants)
    {
        var method=typeof(PrincipalRolePolicy).GetMethod("Evaluate",BindingFlags.Static|BindingFlags.NonPublic)??throw new InvalidOperationException("生产权限聚合器不存在。");
        return Assert.IsType<PrincipalRoleRealm>(method.Invoke(null,[new Registry(catalog),manager,admin,100L,roles,grants,null]));
    }
    /// <summary>授权行保持实际角色/页面字段与空数组，不伪造已聚合的权限集合。</summary>
    private static Dictionary<string,object?> Grant(string page,string actions)=>new(){{"yz_role_id",5L},{"config_page_id",page},{"permission_values",actions}};
    private sealed class Registry(SecurityResourceCatalog catalog):ISecurityResourceRegistry
    {
        public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[catalog];
        /// <summary>本批普通业务后台不能变成平台管理员。</summary>
        public bool IsPlatformRealm(string projectId,string adminTypeId)=>false;
    }
}
