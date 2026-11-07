using System.Text.Json;
using Metarill.Data;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Compatibility.Tests;

public sealed class ConfigurationActionsIntegrationTests
{
    private static string Json(object value)=>JsonSerializer.Serialize(value,JsonDefaults.Options);
    /// <summary>复制全部配置与出向关系，字段新ID与原布局组一一对应，源记录和权限完全不变。</summary>
    [GatewayIntegrationFact]
    public async Task Copy_preserves_layout_and_outgoing_children_and_resets_audit_without_grants()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();
        var source=(await f.Rows("config_page")).Single(p=>SqlRowStore.Id(p)==f.Page);var before=Json(source);
        var sourceFields=(await f.Rows("config_page_column")).ToArray();var permissions=Json(await f.Rows("yz_role_permission"));
        var receipt=await f.Business().CopyPageAsync(f.Page.ToString());Assert.True(receipt.Created);Assert.Equal(3,receipt.FieldIds!.Count);
        var copy=(await f.Rows("config_page")).Single(p=>SqlRowStore.Text(p,"id")==receipt.PageId);
        Assert.Equal("测试页面_复制",SqlRowStore.Text(copy,"name"));Assert.Equal(source["detail_layout"],copy["detail_layout"]);
        Assert.Equal(8026,SqlRowStore.Id(copy,"insert_manager_id"));Assert.NotEqual(source["insert_time"],copy["insert_time"]);
        var copiedFields=(await f.Rows("config_page_column")).Where(row=>SqlRowStore.Text(row,"config_page_id")==receipt.PageId).ToArray();Assert.Equal(3,copiedFields.Length);
        foreach(var field in sourceFields)
        {
            var copied=copiedFields.Single(row=>SqlRowStore.Text(row,"id")==receipt.FieldIds[SqlRowStore.Text(field,"id")]);
            Assert.Equal(field["config_column_id"],copied["config_column_id"]);Assert.Equal("base",copied["detail_group_key"]);Assert.Equal("原配置",copied["default_value"]);Assert.Equal(8026,SqlRowStore.Id(copied,"insert_manager_id"));
        }
        Assert.Single(await f.Rows("config_button"),row=>SqlRowStore.Text(row,"config_page_id")==receipt.PageId);
        var children=(await f.Rows("config_child")).Where(row=>SqlRowStore.Text(row,"parent_config_page_id")==receipt.PageId).ToArray();Assert.Equal(2,children.Length);
        Assert.Contains(children,row=>SqlRowStore.Id(row,"child_config_page_id")==f.Page);Assert.Contains(children,row=>SqlRowStore.Id(row,"child_config_page_id")==f.ChildPage);
        Assert.Equal(before,Json((await f.Rows("config_page")).Single(row=>SqlRowStore.Id(row)==f.Page)));Assert.Equal(permissions,Json(await f.Rows("yz_role_permission")));
    }
    /// <summary>从表创建的是页面配置，字段完整且默认只读/排序规则一致；数据库业务表不会被创建。</summary>
    [GatewayIntegrationFact]
    public async Task Create_page_uses_current_admin_and_default_fields_without_duplicate_references()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();var receipt=await f.Business().CreatePageAsync(f.Table.ToString());
        var page=(await f.Rows("config_page")).Single(row=>SqlRowStore.Text(row,"id")==receipt.PageId);Assert.Equal(f.Admin,SqlRowStore.Id(page,"yz_admin_type_id"));Assert.Equal(1,SqlRowStore.Id(page,"is_new"));
        Assert.Equal("OneWithMany",SqlRowStore.Text(page,"child_place"));Assert.Equal(2,SqlRowStore.Id(page,"cols"));
        var fields=(await f.Rows("config_page_column")).Where(row=>SqlRowStore.Text(row,"config_page_id")==receipt.PageId).ToArray();Assert.Equal(3,fields.Length);Assert.Equal(3,fields.Select(row=>SqlRowStore.Id(row,"config_column_id")).Distinct().Count());
        var sort=fields.Single(row=>SqlRowStore.Id(row,"config_column_id")==f.Sort);Assert.Equal("99",sort["default_value"]);Assert.Equal(1,SqlRowStore.Id(sort,"is_must"));
        Assert.Equal(1,SqlRowStore.Id(fields.Single(row=>SqlRowStore.Id(row,"config_column_id")==f.Derived),"is_read_only"));Assert.All(fields,row=>Assert.Equal(8026,SqlRowStore.Id(row,"insert_manager_id")));
        var second=await f.Business().CreatePageAsync(f.Table.ToString());Assert.NotEqual(receipt.PageId,second.PageId);
    }
    /// <summary>相同快捷菜单在跨连接并发下仍只生成一个，回执版本等于实际菜单快照。</summary>
    [GatewayIntegrationFact]
    public async Task Menu_is_idempotent_under_database_project_lock_and_uses_one_explicit_root()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();using var peerSession=new DatabaseSession(f.Options);
        var results=await Task.WhenAll(f.Business().CreateMenuAsync(f.Page.ToString()),f.Business(peerSession).CreateMenuAsync(f.Page.ToString()));
        Assert.Single(results,row=>row.Created);Assert.Equal(results[0].MenuId,results[1].MenuId);
        var menus=await f.Rows("config_menu");Assert.Equal(2,menus.Count);var root=menus.Single(row=>SqlRowStore.Text(row,"kind")=="DIRECTORY");var leaf=menus.Single(row=>SqlRowStore.Text(row,"kind")=="GENERATED_PAGE");
        Assert.Equal("页面",root["name"]);Assert.Equal(0,SqlRowStore.Id(root,"parent_menu_id"));Assert.Equal(SqlRowStore.Id(root),SqlRowStore.Id(leaf,"parent_menu_id"));Assert.Equal(f.Admin,SqlRowStore.Id(leaf,"yz_admin_type_id"));
        var catalog=await new MetadataRepository(f.Store,f.Registry).LoadProjectAsync(f.Project.ToString());MenuWorkbenchBusiness.Validate(catalog);Assert.All(results,row=>Assert.Equal(MenuWorkbenchBusiness.Revision(catalog),row.Revision));
    }
    /// <summary>歧义目录或非法父链必须在整树校验时拒绝，不能留下新菜单。</summary>
    [GatewayIntegrationFact]
    public async Task Invalid_or_ambiguous_menu_tree_rejects_without_partial_creation()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();
        foreach(var _ in new[]{1,2})await f.Add("config_menu",ConfigurationActionsFixture.Ids.NextId(),new(){["name"]="页面",["kind"]="DIRECTORY",["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business().CreateMenuAsync(f.Page.ToString()));Assert.Equal(2,(await f.Rows("config_menu")).Count);
        await f.Store.UpdateAsync("config_menu",SqlRowStore.Id((await f.Rows("config_menu"))[0]),new Dictionary<string,object?>{{"parent_menu_id",987654321L}});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business().CreateMenuAsync(f.Page.ToString()));Assert.Equal(2,(await f.Rows("config_menu")).Count);
    }
    /// <summary>无效字段或子页引用导致整个复制回滚，源配置及新增记录数量不变。</summary>
    [GatewayIntegrationFact]
    public async Task Invalid_field_layout_or_child_owner_refuses_copy_atomically()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();var before=(await f.Rows("config_page")).Count;
        await f.Store.UpdateAsync("config_page_column",f.Fields[0],new Dictionary<string,object?>{{"detail_group_key","missing"}});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business().CopyPageAsync(f.Page.ToString()));Assert.Equal(before,(await f.Rows("config_page")).Count);
        await f.Store.UpdateAsync("config_page_column",f.Fields[0],new Dictionary<string,object?>{{"detail_group_key","base"},{"config_column_id",987654321L}});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business().CopyPageAsync(f.Page.ToString()));Assert.Equal(before,(await f.Rows("config_page")).Count);
        await f.Store.UpdateAsync("config_page_column",f.Fields[0],new Dictionary<string,object?>{{"config_column_id",f.Title}});
        var relation=(await f.Rows("config_child")).First(row=>SqlRowStore.Id(row,"parent_config_page_id")==f.Page);
        await f.Store.UpdateAsync("config_child",SqlRowStore.Id(relation),new Dictionary<string,object?>{{"yz_project_id",f.ForeignProject}});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business().CopyPageAsync(f.Page.ToString()));Assert.Equal(before,(await f.Rows("config_page")).Count);
        var foreignPage=ConfigurationActionsFixture.Ids.NextId();await f.Add("config_page",foreignPage,new(){["name"]="其他项目页面",["config_table_id"]=f.ForeignTable,["yz_project_id"]=f.ForeignProject,["yz_admin_type_id"]=f.ForeignAdmin});
        await f.Store.UpdateAsync("config_child",SqlRowStore.Id(relation),new Dictionary<string,object?>{{"yz_project_id",f.Project},{"child_config_page_id",foreignPage}});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business().CopyPageAsync(f.Page.ToString()));Assert.Equal(before,(await f.Rows("config_page")).Count);
    }
    /// <summary>权限、项目、后台归属及子页入口限制不能由管理员身份或旧按钮绕过。</summary>
    [GatewayIntegrationFact]
    public async Task Administrator_current_project_admin_owner_and_child_menu_rules_are_required()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business(administrator:false).CreatePageAsync(f.Table.ToString()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business(project:f.ForeignProject.ToString()).CopyPageAsync(f.Page.ToString()));
        await f.Store.UpdateAsync("config_page",f.Page,new Dictionary<string,object?>{{"yz_admin_type_id",f.ForeignAdmin}});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business().CopyPageAsync(f.Page.ToString()));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business().CreateMenuAsync(f.Page.ToString()));
        await f.Store.UpdateAsync("config_page",f.Page,new Dictionary<string,object?>{{"yz_admin_type_id",f.Admin},{"where_sql","parent_id=@parent_id"}});
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business().CreateMenuAsync(f.Page.ToString()));Assert.Empty(await f.Rows("config_menu"));
        await f.Store.UpdateAsync("config_table",f.Table,new Dictionary<string,object?>{{"yz_admin_type_id",f.ForeignAdmin}});
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Business().CreatePageAsync(f.Table.ToString()));
    }
    private sealed class FailureIds(IIdGenerator inner,int failAt):IIdGenerator
    {
        private int count;
        public long NextId()=>++count==failAt?throw new InvalidOperationException("测试：生成ID中途失败"):inner.NextId();
    }
    /// <summary>在已插入页面和部分字段后制造失败，确认新增记录在同一事务整体回滚。</summary>
    [GatewayIntegrationFact]
    public async Task Failure_after_partial_inserts_rolls_back_copy_and_default_page_creation()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();
        var beforePages=Json(await f.Rows("config_page"));var beforeFields=Json(await f.Rows("config_page_column"));var beforeButtons=Json(await f.Rows("config_button"));var beforeChildren=Json(await f.Rows("config_child"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business(generator:new FailureIds(ConfigurationActionsFixture.Ids,3)).CopyPageAsync(f.Page.ToString()));
        Assert.Equal(beforePages,Json(await f.Rows("config_page")));Assert.Equal(beforeFields,Json(await f.Rows("config_page_column")));Assert.Equal(beforeButtons,Json(await f.Rows("config_button")));Assert.Equal(beforeChildren,Json(await f.Rows("config_child")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Business(generator:new FailureIds(ConfigurationActionsFixture.Ids,2)).CreatePageAsync(f.Table.ToString()));
        Assert.Equal(beforePages,Json(await f.Rows("config_page")));Assert.Equal(beforeFields,Json(await f.Rows("config_page_column")));
    }
    /// <summary>原平台登记按钮及首页别名实际进入新领域动作，回执不会被当成业务实体。</summary>
    [GatewayIntegrationFact]
    public async Task Registered_buttons_dispatch_configuration_actions_and_unknown_actions_stay_denied()
    {
        await using var f=await ConfigurationActionsFixture.CreateAsync();
        var copied=await f.Gateway().ExecuteAsync("1761377189732839425",f.Page.ToString());Assert.Equal("metarillRefresh",copied.Type);Assert.Equal("copyPage",copied.Configuration!.Action);
        var created=await f.Gateway().ExecuteLegacyAsync("/admin/custom/config_table/create_page",f.Table.ToString());Assert.Equal("createPage",created.Configuration!.Action);
        var menu=await f.Gateway().ExecuteAsync("1696339787104047106",f.Page.ToString());Assert.Equal("createMenu",menu.Configuration!.Action);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Gateway().ExecuteLegacyAsync("/admin/custom/config_table/create_view",f.Table.ToString()));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Gateway().ExecuteLegacyAsync("/admin/custom/config_table/add_join_columns",f.Table.ToString()));
    }
}
