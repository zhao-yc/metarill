using System.Reflection;
using System.Security;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行实际可信生成Controller和共享Business，仅数据库传输使用受控内存，不冒称HTTP或实库。</summary>
public sealed class TrustedSecurityWorkbenchTests(TrustedSecurityServiceFixture fixture):IClassFixture<TrustedSecurityServiceFixture>
{
    private const string RolePage="1692532858529329154";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    /// <summary>普通模型和同形JSON标记均不下发管理接口，可信模块仍保留原Full门禁。</summary>
    [Fact]
    public void Only_the_factory_issued_module_emits_the_original_three_routes()
    {
        var ordinary=GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace);
        Assert.Empty(TrustedSecurityWorkbenchRenderer.Render(ordinary));
        ordinary.Catalog.ConfigTables[0].Extra["trustedModule"]=JsonSerializer.SerializeToElement("security-management-v1");
        Assert.Empty(TrustedSecurityWorkbenchRenderer.Render(ordinary));
        var trusted=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        var file=Assert.Single(TrustedSecurityWorkbenchRenderer.Render(trusted));Assert.Equal("Controllers/RolePermissionWorkbenchController.g.cs",file.Path);
        var type=fixture.Type("Generated.Controllers.RolePermissionWorkbenchController");
        Assert.Equal("api/admin/sys/yzp_permission",type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("roles",type.GetMethod("Roles")!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        foreach(var action in new[]{"Snapshot","Save"})Assert.Equal(action.ToLowerInvariant(),type.GetMethod(action)!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Single(type.GetConstructors());Assert.Equal(new[]{typeof(RolePermissionBusiness),typeof(ISecurityIdentityAccessor),typeof(IRuntimeAuthorization)},type.GetConstructors()[0].GetParameters().Select(parameter=>parameter.ParameterType));
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(fixture.Catalog));
    }

    /// <summary>签发后的页路径、后台或命名空间漂移直接拒绝，不降为普通接口。</summary>
    [Theory]
    [InlineData("path")][InlineData("admin")][InlineData("namespace")]
    public void Changed_issued_scope_is_rejected_before_rendering(string change)
    {
        var model=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        if(change=="path")model.Catalog.ConfigCustomPages[0].PagePath="pages/custom/other";
        if(change=="admin")model.Catalog.ConfigCustomPages[0].YzAdminTypeId="42";
        if(change=="namespace")model=model with{Namespace="Other.Host"};
        Assert.Throws<InvalidOperationException>(()=>TrustedSecurityWorkbenchRenderer.Render(model).ToArray());
    }

    /// <summary>真实普通编辑身份能查询并保存同后台super角色草稿，原revision及共享事务保持。</summary>
    [Fact]
    public async Task Actual_controller_uses_shared_business_and_original_editor_rule()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);
        run.Add("yz_role",("id",12L),("name","目标超级角色"),("yz_admin_type_id",long.Parse(fixture.Models.Binding.ManagementAdminTypeId)),("is_super",1),("permission_mode","RESOURCE"));
        var controller=Controller(run);
        var roles=Data(await run.Call(controller,"Roles",CancellationToken.None));Assert.Contains(roles.GetProperty("roles").EnumerateArray(),role=>role.GetProperty("id").GetString()=="12");
        var snapshot=Data(await run.Call(controller,"Snapshot",Snapshot("12"),CancellationToken.None));Assert.True(snapshot.GetProperty("isSuper").GetBoolean());
        Assert.Contains(snapshot.GetProperty("catalog").GetProperty("pages").EnumerateArray(),page=>page.GetProperty("id").GetString()==RolePage&&page.GetProperty("tableName").GetString()=="yz_role");
        var revision=snapshot.GetProperty("revision").GetString()!;
        // 保存只需原角色页修改动作；不为形成回执额外要求查询权限。
        foreach(var row in run.Transport.Rows["yz_role_permission"].Where(row=>Convert.ToString(row.GetValueOrDefault("config_page_id"))==RolePage))row["permission_values"]="[\"is_update\"]";
        SetIdentity(run,run.Identity with{Permissions=new HashSet<string>{RolePage+":is_update"}});
        var request=new RolePermissionSave("12",revision,new([],new(){{RolePage,["is_query"]}},[]));
        var saved=Data(await run.Call(controller,"Save",request,CancellationToken.None));Assert.True(saved.GetProperty("isSuper").GetBoolean());Assert.NotEqual(revision,saved.GetProperty("revision").GetString());
        Assert.True(run.Transport.Commits>0);Assert.All(run.Transport.WriteTransactions,Assert.True);
        Assert.Equal(0,Convert.ToInt32(run.Transport.Rows["yz_role"].Single(row=>Convert.ToInt64(row["id"])==10)["is_super"]));
        Assert.False(run.Identity.Actor.IsAdministrator);Assert.False(run.Identity.IsRealmSuper);
        var before=run.Fingerprint();await Assert.ThrowsAsync<InvalidOperationException>(()=>run.Call(controller,"Save",request,CancellationToken.None));Assert.Equal(before,run.Fingerprint());
        await Assert.ThrowsAsync<SecurityException>(()=>run.Call(controller,"Snapshot",Snapshot("12"),CancellationToken.None));
    }

    /// <summary>匿名、Agent、开发超级标志及包外身份均在调用Business或读取SQL之前拒绝。</summary>
    [Theory]
    [InlineData("anonymous")][InlineData("agent")][InlineData("session")][InlineData("account")][InlineData("platform")]
    [InlineData("project")][InlineData("principal-project")][InlineData("admin")][InlineData("principal-admin")]
    public async Task Invalid_web_boundary_cannot_enter_any_of_the_three_methods(string kind)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);AuthenticatedIdentity? identity=run.Identity;
        identity=kind switch
        {
            "anonymous"=>null,"agent"=>identity with{GrantId=99},"session"=>identity with{SessionId=0},
            "account"=>identity with{Actor=identity.Actor with{AccountId=0}},"platform"=>identity with{Actor=identity.Actor with{IsAdministrator=true}},
            "project"=>identity with{Actor=identity.Actor with{ProjectId="42"}},"principal-project"=>identity with{PrincipalProjectId="42"},
            "admin"=>identity with{Actor=identity.Actor with{AdminTypeId="42"}},_=>identity with{PrincipalAdminTypeId="42"}
        };
        SetIdentity(run,identity);var controller=Controller(run);run.Transport.Commands.Clear();var before=run.Fingerprint();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Call(controller,"Roles",CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Call(controller,"Snapshot",Snapshot("10"),CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Call(controller,"Save",new RolePermissionSave("10","invalid",new([],new(),[])),CancellationToken.None));
        Assert.Empty(run.Transport.Commands);Assert.Equal(before,run.Fingerprint());
    }

    /// <summary>目标角色的后台仍由共享业务从真实行读取，不能凭角色ID绕过当前后台。</summary>
    [Fact]
    public async Task Foreign_target_role_is_rejected_by_the_shared_business()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);run.Add("yz_role",("id",12L),("name","其它后台角色"),("yz_admin_type_id",42L),("is_super",1));
        var before=run.Fingerprint();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Call(Controller(run),"Snapshot",Snapshot("12"),CancellationToken.None));Assert.Equal(before,run.Fingerprint());
    }

    /// <summary>已登录但无角色页动作走正式Web授权的403语义，在读取共享业务数据之前拒绝。</summary>
    [Theory]
    [InlineData("Roles")][InlineData("Snapshot")][InlineData("Save")]
    public async Task Authenticated_page_denial_uses_the_existing_web_permission_boundary(string operation)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);SetIdentity(run,run.Identity with{Permissions=new HashSet<string>()});
        run.Transport.Commands.Clear();var before=run.Fingerprint();var controller=Controller(run);
        var args=operation switch
        {
            "Roles"=>new object?[]{CancellationToken.None},"Snapshot"=>[Snapshot("10"),CancellationToken.None],
            _=>[new RolePermissionSave("10","invalid",new([],new(),[])),CancellationToken.None]
        };
        await Assert.ThrowsAsync<SecurityException>(()=>run.Call(controller,operation,args));Assert.Empty(run.Transport.Commands);Assert.Equal(before,run.Fingerprint());
    }
    /// <summary>同后台超级身份必须从实际会话与角色行解析，不用平台管理员开关代替。</summary>
    [Fact]
    public async Task Real_same_admin_super_identity_keeps_original_read_access()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);
        run.Transport.Rows["yz_role"].Single(row=>Convert.ToInt64(row["id"])==10)["is_super"]=1;run.Transport.Rows["yz_role_permission"].Clear();
        var services=run.Scope.ServiceProvider;var registry=services.GetRequiredService<ISecurityResourceRegistry>();
        var auth=new AuthenticationService(run.Store,services.GetRequiredService<IIdGenerator>(),registry,(ISecuritySessionPolicy)registry);
        var identity=await auth.AuthenticateAsync(new string('a',64),fixture.Catalog.YzProjectId);Assert.NotNull(identity);Assert.True(identity.IsRealmSuper);Assert.False(identity.Actor.IsAdministrator);
        SetIdentity(run,identity);var before=run.Fingerprint();var controller=Controller(run);
        Assert.NotEmpty(Data(await run.Call(controller,"Roles",CancellationToken.None)).GetProperty("roles").EnumerateArray());
        Assert.True(Data(await run.Call(controller,"Snapshot",Snapshot("10"),CancellationToken.None)).GetProperty("isSuper").GetBoolean());
        Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Transport.WriteTransactions);
    }

    /// <summary>即使第二个别名转发同样方法，也不能替代正式宿主的同一Scoped授权对象。</summary>
    [Fact]
    public async Task Different_permission_alias_is_rejected_before_business_reads()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);
        var controller=Controller(run,new OtherPermissionAlias(run.Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>()));
        run.Transport.Commands.Clear();var before=run.Fingerprint();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>run.Call(controller,"Roles",CancellationToken.None));Assert.Empty(run.Transport.Commands);Assert.Equal(before,run.Fingerprint());
    }
    /// <summary>实际生成的原工作台及依赖与当前原源码逐字一致，重复生成不增加或重排页面。</summary>
    [Fact]
    public void Original_frontend_assets_are_embedded_from_existing_sources_and_regenerate_stably()
    {
        var directory=Path.Combine(fixture.DirectoryPath,"original-workbench-assets");
        var first=new CSharpGenerator().Generate(fixture.Models,directory,TrustedSecurityServiceFixture.Namespace);
        var paths=new[]{"pages/main/set_role_permission.vue","components/yzp-permission-workbench/yzp-permission-workbench.vue","components/yzp-permission-legacy/yzp-permission-legacy.vue","common/yzp_runtime/permissionWorkbench.js"};
        foreach(var path in paths)
        {
            Assert.Contains("frontend/"+path,first.GeneratedFiles);
            var original=File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"../frontend/metarill_uni_admin",path)).Replace("\r\n","\n").Replace('\r','\n');
            Assert.Equal(original,File.ReadAllText(Path.Combine(directory,"frontend",path)));
        }
        var before=Directory.GetFiles(directory,"*",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(directory,path),File.ReadAllText);
        var second=new CSharpGenerator().Generate(fixture.Models,directory,TrustedSecurityServiceFixture.Namespace);
        Assert.Equal(first.GeneratedFiles,second.GeneratedFiles);Assert.Empty(second.CreatedHandwrittenFiles);
        foreach(var file in before)Assert.Equal(file.Value,File.ReadAllText(Path.Combine(directory,file.Key)));
        var ordinary=GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace);Assert.Empty(TrustedSecurityWorkbenchRenderer.Frontend(ordinary));
    }
    /// <summary>实际可信生成包含原工作台唯一路由及完整样式，普通生成仍不发布自定义工作台。</summary>
    [Fact]
    public void Trusted_generated_routes_keep_original_workbench_registration_and_ordinary_gate()
    {
        var directory=Path.Combine(fixture.DirectoryPath,"original-workbench-routes");
        new CSharpGenerator().Generate(fixture.Models,directory,TrustedSecurityServiceFixture.Namespace);
        using var generated=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"frontend/pages.generated.json")));
        var actual=Assert.Single(generated.RootElement.GetProperty("pages").EnumerateArray(),page=>page.GetProperty("path").GetString()=="pages/main/set_role_permission");
        using var original=JsonDocument.Parse(File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"../frontend/metarill_uni_admin/pages.json")));
        var expected=Assert.Single(original.RootElement.GetProperty("pages").EnumerateArray(),page=>page.GetProperty("path").GetString()=="pages/main/set_role_permission");
        Assert.Equal(JsonSerializer.Serialize(expected),JsonSerializer.Serialize(actual));
        var ordinary=GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace);
        Assert.Empty(TrustedSecurityWorkbenchRenderer.Routes(ordinary));
        var regular=UniappRenderer.Render(ordinary).Single(file=>file.Path=="frontend/pages.generated.json");
        using var regularRoutes=JsonDocument.Parse(regular.Content);
        Assert.DoesNotContain(regularRoutes.RootElement.GetProperty("pages").EnumerateArray(),page=>page.GetProperty("path").GetString()=="pages/main/set_role_permission");
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(fixture.Models.Published));
    }

    /// <summary>签发后改变页路径或后台的模型不能只通过资源输出绕过完整快照校验。</summary>
    [Theory]
    [InlineData("path")][InlineData("admin")][InlineData("namespace")]
    public void Changed_issued_model_cannot_emit_workbench_routes(string change)
    {
        var model=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        if(change=="path")model.Catalog.ConfigCustomPages[0].PagePath="pages/custom/changed";
        if(change=="admin")model.Catalog.ConfigCustomPages[0].YzAdminTypeId="42";
        if(change=="namespace")model=model with{Namespace="Other.Host"};
        Assert.Throws<InvalidOperationException>(()=>TrustedSecurityWorkbenchRenderer.Routes(model).ToArray());
    }
    /// <summary>实例化实际生成适配与同scope共享依赖，不用假Business代替权限判断。</summary>
    private object Controller(TrustedSecurityServiceRun run,IRuntimeAuthorization? other=null)
    {
        var services=run.Scope.ServiceProvider;
        var business=new RolePermissionBusiness(run.Store,services.GetRequiredService<IRolePermissionCatalog>(),services.GetRequiredService<ISecurityIdentityAccessor>(),services.GetRequiredService<IIdGenerator>(),services.GetRequiredService<ISecurityResourceRegistry>(),services.GetRequiredService<SecurityRealmMutationGuard>());
        return Activator.CreateInstance(fixture.Type("Generated.Controllers.RolePermissionWorkbenchController"),business,services.GetRequiredService<ISecurityIdentityAccessor>(),other??services.GetRequiredService<IRuntimeAuthorization>())!;
    }
    /// <summary>实例化原快照协议DTO，只接受角色标识。</summary>
    private object Snapshot(string roleId)=>Activator.CreateInstance(fixture.Type("Generated.Controllers.RoleWorkbenchSnapshotRequest"),roleId)!;
    /// <summary>核对原R封装后读取实际生成响应。</summary>
    private static JsonElement Data(object? response){var json=JsonSerializer.SerializeToElement(response,Json);Assert.Equal(200,json.GetProperty("code").GetInt32());return json.GetProperty("data");}
    /// <summary>反例仅替换同一真实授权对象的请求身份槽，不替代业务或权限实现。</summary>
    private static void SetIdentity(TrustedSecurityServiceRun run,AuthenticatedIdentity? identity)
    {
        var context=run.Scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!;context.Items.Remove(typeof(AuthenticatedIdentity));if(identity is not null)context.Items[typeof(AuthenticatedIdentity)]=identity;
    }
    /// <summary>仅用于DI错误反例，实际授权决定仍调用原正式Web实现。</summary>
    private sealed class OtherPermissionAlias(IRuntimeAuthorization source):IRuntimeAuthorization
    {
        /// <summary>保持原身份读取，反例只改变对象引用。</summary>
        public RuntimeActor RequireActor()=>source.RequireActor();
        /// <summary>保持原权限判定，不能借委托伪装同一作用域实例。</summary>
        public void RequirePagePermission(string pageId,string operation)=>source.RequirePagePermission(pageId,operation);
    }}
