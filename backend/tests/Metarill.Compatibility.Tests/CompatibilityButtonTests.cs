using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Compatibility.Tests;

public sealed class CompatibilityButtonTests
{
    private static string MetadataPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "backend", "metadata", "platform.json");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("找不到测试元数据。");
    }
    internal static MetadataRegistry Registry() { var path=MetadataPath(); return new(path,Path.Combine(Path.GetDirectoryName(path)!,"demo.json")); }
    private static RuntimeActor Actor(bool administrator=true,string project="777")=>new(8001,"按钮测试",administrator,project,"");
    private static Dictionary<string,object?> Button(MetadataRegistry registry,string route,string table="config_table")=>new()
    {
        ["id"]=987L,["yz_project_id"]=registry.Platform.YzProjectId,["action_type"]=1,
        ["config_page_id"]=registry.Platform.ConfigPages.First(p=>p.ConfigTableId==registry.Platform.ConfigTables.Single(t=>t.TableName==table).Id).Id,["sql"]=route
    };
    private static Dictionary<string,object?> Entity(string id="888",string project="777",string table="999")=>new(){["id"]=id,["yz_project_id"]=project,["config_table_id"]=table};

    [Theory]
    [InlineData("/admin/custom/config_table/config_columns_sort","/pages/custom/config_columns_sort?config_table_id=888")]
    [InlineData("/admin/custom/config_table/batch_create_columns","/pages/custom/batch_create_columns?config_table_id=888")]
    [InlineData("/admin/custom/config_table/create_table","/pages/custom/schema_sync?projectId=777&tableId=888")]
    public void Registered_navigation_uses_database_entity(string route,string expected)
    {
        var registry=Registry(); var action=CompatibilityButtonPolicy.RequireAction(Actor(),registry,Button(registry,route));
        Assert.Equal(new CompatibilityButtonResult("path",expected),CompatibilityButtonPolicy.Resolve(Actor(),action,Entity()));
    }
    [Fact]
    public void Parent_relation_is_verified_and_layout_uses_page_id()
    {
        var registry=Registry();var action=CompatibilityButtonPolicy.RequireAction(Actor(),registry,Button(registry,"/admin/custom/config_page/detail_layout","config_page"));
        Assert.Equal("/pages/custom/page_detail_layout?pageId=888",CompatibilityButtonPolicy.Resolve(Actor(),action,Entity(),Entity("999")).Path);
        Assert.Throws<UnauthorizedAccessException>(()=>CompatibilityButtonPolicy.Resolve(Actor(),action,Entity(),Entity("999","778")));
        Assert.Throws<UnauthorizedAccessException>(()=>CompatibilityButtonPolicy.Resolve(Actor(),action,Entity(),Entity("998")));
    }
    [Fact]
    public void Generation_returns_download_command_and_project_schema_does_not_execute()
    {
        var registry=Registry();var generate=CompatibilityButtonPolicy.RequireAction(Actor(),registry,Button(registry,"/admin/custom/config_table/generate_all"));
        Assert.Equal(new CompatibilityButtonResult("metarillGenerate",ProjectId:"777"),CompatibilityButtonPolicy.Resolve(Actor(),generate,Entity()));
        var schema=CompatibilityButtonPolicy.RequireAction(Actor(),registry,Button(registry,"/admin/custom/index/init_database","yz_project"));
        Assert.Equal("/pages/custom/schema_sync?projectId=777",CompatibilityButtonPolicy.Resolve(Actor(),schema,Entity("777","untrusted")).Path);
        Assert.Throws<UnauthorizedAccessException>(()=>CompatibilityButtonPolicy.Resolve(Actor(),schema,Entity("778")));
    }
    [Theory]
    [InlineData("SELECT * FROM yz_manager")]
    [InlineData("/admin/custom/config_table/create_table?other=1")]
    [InlineData("https://example.test/execute")]
    [InlineData("/admin/custom/config_table/copy_page")]
    [InlineData("/admin/custom/config_table/delete_table")]
    [InlineData("/admin/custom/index/init_admin")]
    public void Sql_urls_unknown_and_destructive_actions_are_refused(string route)
    {
        var registry=Registry();Assert.Throws<InvalidOperationException>(()=>CompatibilityButtonPolicy.RequireAction(Actor(),registry,Button(registry,route)));
    }
    [Fact]
    public void Identity_project_button_ownership_and_source_page_are_all_required()
    {
        var registry=Registry();var button=Button(registry,"/admin/custom/config_table/create_table");
        Assert.Throws<UnauthorizedAccessException>(()=>CompatibilityButtonPolicy.RequireAction(Actor(false),registry,button));
        Assert.Throws<InvalidOperationException>(()=>CompatibilityButtonPolicy.RequireAction(Actor(project:""),registry,button));
        button["yz_project_id"]="777";Assert.Throws<InvalidOperationException>(()=>CompatibilityButtonPolicy.RequireAction(Actor(),registry,button));
        button=Button(registry,"/admin/custom/config_table/create_table","config_page");Assert.Throws<InvalidOperationException>(()=>CompatibilityButtonPolicy.RequireAction(Actor(),registry,button));
        button=Button(registry,"/admin/custom/config_table/create_table");button["action_type"]=2;Assert.Throws<InvalidOperationException>(()=>CompatibilityButtonPolicy.RequireAction(Actor(),registry,button));
        var action=new CompatibilityButtonAction("","config_table","schema");
        Assert.Throws<UnauthorizedAccessException>(()=>CompatibilityButtonPolicy.Resolve(Actor(),action,Entity(project:"778")));
    }
    [Fact]
    public void List_button_display_hints_are_accepted_but_client_routes_are_not()
    {
        var request=JsonSerializer.Deserialize<Metarill.Api.Controllers.ExecuteCompatibilityButtonRequest>("{\"buttonId\":\"7\",\"entityId\":\"8\",\"visibleScheme\":\"Normal\",\"visibleRuleJson\":\"\"}",JsonDefaults.Options);
        Assert.Equal("7",request!.ButtonId);
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<Metarill.Api.Controllers.ExecuteCompatibilityButtonRequest>("{\"buttonId\":\"7\",\"entityId\":\"8\",\"route\":\"https://evil.test\"}",JsonDefaults.Options));
    }
    [Theory]
    [InlineData("")][InlineData("0")][InlineData("-1")][InlineData("1 OR 1=1")][InlineData("../x")]
    public void Entity_identifiers_are_positive_database_keys(string id)=>Assert.Throws<InvalidOperationException>(()=>CompatibilityButtonPolicy.RequireId(id));
}

public sealed class GatewayIntegrationFactAttribute:FactAttribute
{
    public GatewayIntegrationFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")))Skip="未提供隔离 MySQL 配置，数据库按钮网关测试未执行。";
    }
}

public sealed class CompatibilityGatewayIntegrationTests
{
    /// <summary>仅只读已初始化的隔离库，验证真实按钮及实体查询；不修改共享种子或执行 DDL。</summary>
    private static CompatibilityButtonBusiness Business(bool administrator=true,string? project=null)
    {
        var registry=CompatibilityButtonTests.Registry();
        using var json=JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=json.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        if(options.ExpectedDatabase!="metarill_test")throw new InvalidOperationException("测试仅允许隔离 metarill_test 库。");
        var context=new DefaultHttpContext();var actor=new RuntimeActor(8001,"测试管理员",administrator,project??registry.Platform.YzProjectId,registry.Platform.YzAdminTypes[0].Id);
        context.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(actor,new HashSet<string>(),1,null);
        var store=new SqlRowStore(new DatabaseSession(options),registry.Platform);var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},registry);var ids=new SnowflakeIdGenerator(855);
        return new(store,auth,registry,new ConfigurationActionsBusiness(store,registry,new MetadataRepository(store,registry),auth,ids,new ConfigurationMutationObserver(store,registry,ids)));
    }
    [GatewayIntegrationFact]
    public async Task Registered_button_and_legacy_alias_resolve_same_database_object()
    {
        var business=Business();var gateway=await business.ExecuteAsync("1676828142707372033","6191080026669056");
        Assert.Equal("/pages/custom/config_columns_sort?config_table_id=6191080026669056",gateway.Path);
        Assert.Equal(gateway,await business.ExecuteLegacyAsync("/admin/custom/config_table/config_columns_sort","6191080026669056"));
    }
    [GatewayIntegrationFact]
    public async Task Cross_project_entity_non_admin_and_unknown_button_are_rejected()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Business(project:"2200000000000100000").ExecuteAsync("1676828142707372033","6191080026669056"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Business(false).ExecuteAsync("1676828142707372033","6191080026669056"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Business().ExecuteAsync("1","6191080026669056"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Business().ExecuteLegacyAsync("SELECT 1","6191080026669056"));
    }
    [GatewayIntegrationFact]
    public async Task Legacy_controller_accepts_query_json_and_form_without_trusting_entity_body()
    {
        async Task<Metarill.Core.Contracts.R<CompatibilityButtonResult>> Legacy(string contentType,string body)
        {
            var context=new DefaultHttpContext();context.Request.Path="/api/admin/custom/config_table/generate_all";
            context.Request.ContentType=contentType;context.Request.Body=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
            context.Request.QueryString=new QueryString("?id=6191080026669056");
            var controller=new Metarill.Api.Controllers.CompatibilityButtonController(Business()){ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext{HttpContext=context}};
            return await controller.Legacy(default);
        }
        Assert.Equal("metarillGenerate",(await Legacy("application/json","{}")).Data!.Type);
        Assert.Equal("metarillGenerate",(await Legacy("application/json","{\"id\":\"6191080026669056\"}")).Data!.Type);
        Assert.Equal("metarillGenerate",(await Legacy("application/x-www-form-urlencoded","id=6191080026669056")).Data!.Type);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Legacy("application/json","{\"id\":\"999\"}"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Legacy("application/json","{\"route\":\"/sql\"}"));
        var clientEntity=JsonSerializer.SerializeToElement(new {yzProjectId="foreign",configTableId="foreign",sql="DROP TABLE x"});
        var gateway=new Metarill.Api.Controllers.CompatibilityButtonController(Business());
        Assert.Equal("/pages/custom/config_columns_sort?config_table_id=6191080026669056",(await gateway.Execute(new("1676828142707372033","6191080026669056",clientEntity),default)).Data!.Path);
    }

}
