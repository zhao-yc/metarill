using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>可信生成管理页、正式Web身份与共享协调器的实库HTTP闭环；不放开默认Full门禁。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class TrustedSecurityRuntimeMySqlTests
{
    private const string ManagerPage="6031906822946816",RolePage="1692532858529329154",AdminPage="1696799309006348290";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};

    /// <summary>普通编辑身份创建和修改账号/超级角色，工作台按原revision保存，撤权下个请求生效，失败不改行。</summary>
    [StandaloneExportMySqlFact]
    public async Task Generated_management_http_uses_persisted_web_identity_and_current_realm_guards()
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var connection=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_test"||connection.Database!="metarill_test"||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("可信运行时专项只允许固定隔离MySQL测试库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("缺少明确实例pin。")));
        await using(var c=new MySqlConnection(options.ConnectionString))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await q.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync()&&r.GetString(0)=="metarill_test"&&r.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"实例不匹配，尚未建立样本。");
        }
        var nonce=Guid.NewGuid().ToString("N");var ids=new SnowflakeIdGenerator(946);var project=ids.NextId();var admin=ids.NextId();var foreignAdmin=ids.NextId();var editorRole=ids.NextId();var foreignRole=ids.NextId();var editor=ids.NextId();
        var name="mt_trusted_"+nonce[..20];var password="Metarill-"+nonce;var evidence=Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"../.local/trusted-security-runtime/mysql-"+nonce);Directory.CreateDirectory(evidence);
        var fixture=new TrustedSecurityServiceFixture();WebApplication? host=null;var seeded=false;var verified=false;long? createdManager=null,createdRole=null;
        using var session=new DatabaseSession(options);
        // 每轮发布目录使用全新项目/后台，既不复用平台身份，也不修改既有发布元数据。
        var original=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"metadata/demo.json")),Json)!;
        var node=JsonSerializer.SerializeToNode(original,Json)!;var oldProject=original.YzProjectId;var oldAdmin=original.YzAdminTypes[0].Id;
        // 仅替换示例项目/后台的完整字符串ID，不改固定模块的物理和原页面ID。
        void Bind(JsonNode? value)
        {
            if(value is JsonObject row)foreach(var key in row.Select(x=>x.Key).ToArray())
            {if(row[key] is JsonValue text&&text.TryGetValue<string>(out var s)&&s==oldProject)row[key]=project.ToString(CultureInfo.InvariantCulture);else if(row[key] is JsonValue a&&a.TryGetValue<string>(out var t)&&t==oldAdmin)row[key]=admin.ToString(CultureInfo.InvariantCulture);else Bind(row[key]);}
            else if(value is JsonArray array)foreach(var item in array)Bind(item);
        }
        Bind(node);var app=node.Deserialize<MetadataCatalog>(Json)!;app.YzProjectName=name;app.YzAdminTypes.Add(new(){Id=foreignAdmin.ToString(CultureInfo.InvariantCulture),Name="自有其他后台",YzProjectId=app.YzProjectId});
        fixture.Models=SecurityManagementModuleFactory.Compose(app,admin.ToString(CultureInfo.InvariantCulture));fixture.Catalog=fixture.Models.Published;fixture.CatalogBytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fixture.Catalog,Json)+"\n");
        var store=new SqlRowStore(session,fixture.Models.Physical);
        try
        {
            // 只核已存在的安全表与必要账号索引；本专项不执行DDL、初始化、GRANT或全表清理。
            await using(var c=new MySqlConnection(options.ConnectionString))
            {
                await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='sys_session' AND INDEX_NAME='ix_session_account_active' AND ((SEQ_IN_INDEX=1 AND COLUMN_NAME='account_id') OR (SEQ_IN_INDEX=2 AND COLUMN_NAME='delete_no'))";Assert.Equal(2,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));
            }
            foreach(var (table,id) in new[]{("yz_project",project),("yz_admin_type",admin),("yz_admin_type",foreignAdmin),("yz_role",editorRole),("yz_role",foreignRole),("yz_manager",editor)})Assert.Empty(await store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"id",id}}));
            foreach(var(table,key)in new[]{("sys_session","project_id"),("yz_role_permission","yz_project_id"),("yz_manager","yz_project_id"),("yz_role","yz_project_id"),("yz_admin_type","yz_project_id"),("yz_project","id")})Assert.Empty(await store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{key,project}}));
            Assert.Empty(await store.ReadIncludingDeletedAsync("yz_manager",new Dictionary<string,object?>{{"name",name}}));await Receipt("intent",false);
            await session.InTransactionAsync(async()=>
            {
                await Seed("yz_project",("id",project),("name",name));
                await Seed("yz_admin_type",("id",admin),("name",name),("yz_project_id",project));await Seed("yz_admin_type",("id",foreignAdmin),("name",name+"_other"),("yz_project_id",project));
                await Seed("yz_role",("id",editorRole),("name",name+"_editor"),("yz_admin_type_id",admin),("yz_project_id",project),("is_super",0),("permission_mode","RESOURCE"));
                await Seed("yz_role",("id",foreignRole),("name",name+"_foreign"),("yz_admin_type_id",foreignAdmin),("yz_project_id",project),("is_super",0),("permission_mode","RESOURCE"));
                await Seed("yz_manager",("id",editor),("name",name),("real_name","自有普通编辑者"),("password",PasswordHasher.Hash(password)),("yz_admin_type_id",admin),("yz_project_id",project),("yz_role_ids",JsonSerializer.Serialize(new[]{editorRole.ToString(CultureInfo.InvariantCulture)})),("is_disabled",0));
                foreach(var page in new[]{ManagerPage,RolePage,AdminPage})await Seed("yz_role_permission",("id",ids.NextId()),("yz_role_id",editorRole),("config_page_id",long.Parse(page,CultureInfo.InvariantCulture)),("permission_values","[\"is_all\"]"),("yz_project_id",project));return true;
            });seeded=true;
            new CSharpGenerator().Generate(fixture.Models,Path.Combine(fixture.DirectoryPath,"generated"),TrustedSecurityServiceFixture.Namespace);Directory.CreateDirectory(Path.Combine(fixture.DirectoryPath,"host"));
            foreach(var file in new[]{"PackageSecurityCatalog.cs","WebAuthentication.cs","SessionPolicy.cs","WebSecurityCoordination.cs"})await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath,"host",file),File.ReadAllText(Path.Combine(TrustedSecurityServiceFixture.SourceRoot,"templates/standalone",file+".tpl")).Replace("{{NAMESPACE}}",TrustedSecurityServiceFixture.Namespace));
            await File.WriteAllTextAsync(Path.Combine(fixture.DirectoryPath,"host/PackageSecurityCatalogBinding.g.cs"),ApplicationPackager.SecurityCatalogBindingSource(TrustedSecurityServiceFixture.Namespace,Convert.ToHexStringLower(SHA256.HashData(fixture.CatalogBytes))));
            var build=await TrustedSecurityServiceFixture.Build(fixture.DirectoryPath);Assert.True(build.ExitCode==0,build.Output);fixture.Assembly=AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(build.AssemblyPath)));
            object New(string type,params object?[] args)=>Activator.CreateInstance(fixture.Type("Host."+type),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null)!;
            var authority=New("VerifiedPackageSecurityCatalog",fixture.CatalogBytes);var registry=New("ApplicationSecurityRegistry",authority);var mode=New("ApplicationAuthenticationMode",false,true);
            var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Services.AddSingleton(session);builder.Services.AddSingleton(store);builder.Services.AddSingleton<IIdGenerator>(ids);builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton(fixture.Type("Host.ApplicationAuthenticationMode"),mode);builder.Services.AddSingleton(fixture.Type("Host.ApplicationSecurityRegistry"),registry);builder.Services.AddSingleton<ISecurityResourceRegistry>((ISecurityResourceRegistry)registry);builder.Services.AddSingleton<ISecuritySessionPolicy>((ISecuritySessionPolicy)registry);builder.Services.AddScoped<AuthenticationService>();builder.Services.AddScoped(fixture.Type("Host.ApplicationWebAuthentication"));
            TrustedSecurityServiceFixture.Invoke(fixture.Type("Host.ApplicationSecurityCatalogRegistration"),"Add",builder.Services,authority);TrustedSecurityServiceFixture.Invoke(fixture.Type("Host.ApplicationWebSecurityCoordination"),"Add",builder.Services,mode);
            TrustedSecurityServiceFixture.Invoke(fixture.Type("Generated.GeneratedRegistration"),"AddGeneratedServices",builder.Services,null);
            builder.Services.AddControllers().AddApplicationPart(fixture.Assembly).AddJsonOptions(o=>o.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));host=builder.Build();
            // 夹具仅承载回环HTTP；身份解析、登录和注销直接执行实际宿主模板，不注入身份/页权替身。
            host.Use(async(context,next)=>
            {
                try
                {
                    if(context.Request.Path!="/login")
                    {
                        var identity=(AuthenticatedIdentity?)await Call(context.RequestServices.GetRequiredService(fixture.Type("Host.ApplicationWebAuthentication")),"ResolveAsync",context.Request.Headers["loginStateId"].ToString(),context.RequestAborted);
                        if(identity is null)throw new UnauthorizedAccessException();context.Items[typeof(AuthenticatedIdentity)]=identity;
                    }
                    await next(context);
                }
                catch(UnauthorizedAccessException){context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(R<string>.Error("真实身份拒绝",401));}
                catch(SecurityException){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(R<string>.Error("原页权拒绝",403));}
                catch(Exception e)when(e is ArgumentException or InvalidOperationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<string>.Error("样本请求拒绝",400));}
            });
            host.MapPost("/login",async(HttpContext context)=>
            {
                var form=await context.Request.ReadFormAsync();var result=((Dictionary<string,object?> User,string Token))(await Call(context.RequestServices.GetRequiredService(fixture.Type("Host.ApplicationWebAuthentication")),"LoginAsync",form["username"].ToString(),form["password"].ToString(),context.RequestAborted))!;return Results.Json(new{token=result.Token});
            });
            host.MapPost("/logout",async(HttpContext context)=>{await Call(context.RequestServices.GetRequiredService(fixture.Type("Host.ApplicationWebAuthentication")),"LogoutAsync",(AuthenticatedIdentity)context.Items[typeof(AuthenticatedIdentity)]!,context.RequestAborted);return Results.Ok();});
            host.MapControllers();await host.StartAsync();var address=Assert.Single(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);using var client=new HttpClient{BaseAddress=new(address),Timeout=TimeSpan.FromSeconds(20)};
            using(var anonymous=await client.PostAsJsonAsync(Route("yz_role",RolePage)+"/page",new{}))Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
            var token=await Login(name,password);client.DefaultRequestHeaders.Add("loginStateId",token);
            var identityCheck=await new AuthenticationService(store,ids,(ISecurityResourceRegistry)registry,(ISecuritySessionPolicy)registry).AuthenticateAsync(token,project.ToString(CultureInfo.InvariantCulture));Assert.NotNull(identityCheck);Assert.False(identityCheck.IsRealmSuper);Assert.False(identityCheck.Actor.IsAdministrator);
            var role=await Post(Route("yz_role",RolePage)+"/add",new{name=name+"_createdrole",sortNo=10,isSuper=1,yzAdminTypeId=admin.ToString(CultureInfo.InvariantCulture)});createdRole=long.Parse(role.GetProperty("data").GetProperty("id").GetString()!,CultureInfo.InvariantCulture);
            var created=await Post(Route("yz_manager",ManagerPage)+"/add",new{name=name+"_new",realName="原表单创建",password,isDisabled=1,yzAdminTypeId=admin.ToString(CultureInfo.InvariantCulture),yzRoleIds=new[]{editorRole.ToString(CultureInfo.InvariantCulture)}});createdManager=long.Parse(created.GetProperty("data").GetProperty("id").GetString()!,CultureInfo.InvariantCulture);Assert.Equal("********",created.GetProperty("data").GetProperty("password").GetString());
            var row=await OwnedManager();var digest=SqlRowStore.Text(row,"password");Assert.True(PasswordHasher.Verify(password,digest));Assert.Equal("0",SqlRowStore.Text(row,"is_disabled"));
            await Post(Route("yz_manager",ManagerPage)+"/update",new{id=createdManager.ToString(),realName="省略口令保留摘要"});Assert.True(digest==SqlRowStore.Text(await OwnedManager(),"password"),"省略口令不能刷新摘要。");
            var before=Fingerprint(await OwnedManager());await Rejected(Route("yz_manager",ManagerPage)+"/update",new{id=createdManager.ToString(),password=(string?)null,realName="失败不得保存"});Assert.Equal(before,Fingerprint(await OwnedManager()));
            await Rejected(Route("yz_manager",ManagerPage)+"/update",new{id=createdManager.ToString(),yzRoleIds=new[]{foreignRole.ToString(CultureInfo.InvariantCulture)},realName="跨后台不得保存"});Assert.Equal(before,Fingerprint(await OwnedManager()));
            // 后台配置密码仍是原初始化表单配置，不进入管理账号摘要规则，也不触发初始化。
            await Post(Route("yz_admin_type",AdminPage)+"/update",new{id=admin.ToString(CultureInfo.InvariantCulture),name=name,userName=name+"_configured",password="短配置密码"});
            var configured=Assert.Single(await store.ReadIncludingDeletedAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin}}));Assert.True(SqlRowStore.Text(configured,"password")=="短配置密码","原后台初始化配置必须保持配置口径，不执行账号重置。");
            var configuredBefore=Fingerprint(configured);await Rejected(Route("yz_admin_type",AdminPage)+"/update",new{id=admin.ToString(CultureInfo.InvariantCulture),password=new string('x',51)});Assert.Equal(configuredBefore,Fingerprint(Assert.Single(await store.ReadIncludingDeletedAsync("yz_admin_type",new Dictionary<string,object?>{{"id",admin}}))));
            var workbench="/api/admin/sys/yzp_permission";var snapshot=(await Post(workbench+"/snapshot",new{roleId=createdRole.ToString()})).GetProperty("data");Assert.True(snapshot.GetProperty("isSuper").GetBoolean());
            var saved=await Post(workbench+"/save",new RolePermissionSave(createdRole.ToString()!,snapshot.GetProperty("revision").GetString()!,new([],new(){{RolePage,["is_query"]}},[])));Assert.True(saved.GetProperty("data").GetProperty("isSuper").GetBoolean());
            var grantsBefore=Fingerprint(await store.ReadIncludingDeletedAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",createdRole}}));
            await Rejected(workbench+"/save",new RolePermissionSave(createdRole.ToString()!,snapshot.GetProperty("revision").GetString()!,new([],new(),[])));Assert.Equal(grantsBefore,Fingerprint(await store.ReadIncludingDeletedAsync("yz_role_permission",new Dictionary<string,object?>{{"yz_role_id",createdRole}})));
            // 沿已确认原规则，普通编辑者可以给同后台账号绑定super角色；不增平台超级门槛。
            await Post(Route("yz_manager",ManagerPage)+"/update",new{id=createdManager.ToString(),yzRoleIds=new[]{createdRole.ToString()}});var childToken=await Login(name+"_new",password);var childIdentity=await new AuthenticationService(store,ids,(ISecurityResourceRegistry)registry,(ISecuritySessionPolicy)registry).AuthenticateAsync(childToken,project.ToString(CultureInfo.InvariantCulture));Assert.NotNull(childIdentity);Assert.True(childIdentity.IsRealmSuper);Assert.False(childIdentity.Actor.IsAdministrator);
            var self=(await Post(workbench+"/snapshot",new{roleId=editorRole.ToString(CultureInfo.InvariantCulture)})).GetProperty("data");await Post(workbench+"/save",new RolePermissionSave(editorRole.ToString(CultureInfo.InvariantCulture),self.GetProperty("revision").GetString()!,new([],new(),[])));
            using(var denied=await client.PostAsJsonAsync(Route("yz_role",RolePage)+"/page",new{}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using(var logout=await client.PostAsync("/logout",null))Assert.True(logout.IsSuccessStatusCode);using(var revoked=await client.PostAsJsonAsync(Route("yz_role",RolePage)+"/page",new{}))Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
            verified=true;await Receipt("verified",false);

            async Task<string> Login(string username,string secret){using var response=await client.PostAsync("/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"username",username},{"password",secret}}));Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty("token").GetString()!;}
            async Task<JsonElement> Post(string url,object body){using var response=await client.PostAsJsonAsync(url,body);Assert.True(response.IsSuccessStatusCode,"真实生成HTTP未成功，响应实体不输出。状态："+(int)response.StatusCode);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(200,json.RootElement.GetProperty("code").GetInt32());return json.RootElement.Clone();}
            async Task Rejected(string url,object body){using var response=await client.PostAsJsonAsync(url,body);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);}
            async Task<Dictionary<string,object?>> OwnedManager()=>Assert.Single(await store.ReadIncludingDeletedAsync("yz_manager",new Dictionary<string,object?>{{"id",createdManager}}));
        }
        finally
        {
            if(host is not null){await host.StopAsync();await host.DisposeAsync();}
            if(seeded)
            {
                // 先验证随机项目及两个后台归属，再在一个事务仅删除本轮项目行；墓碑也纳入清理和最终复核。
                var marker=Assert.Single(await store.ReadIncludingDeletedAsync("yz_project",new Dictionary<string,object?>{{"id",project}}));Assert.True(SqlRowStore.Text(marker,"name")==name,"自有项目标记变化，保留现场。");
                foreach(var id in new[]{admin,foreignAdmin}){var a=Assert.Single(await store.ReadIncludingDeletedAsync("yz_admin_type",new Dictionary<string,object?>{{"id",id}}));Assert.Equal(project,SqlRowStore.Id(a,"yz_project_id"));}
                await session.InTransactionAsync(async()=>{foreach(var(table,key)in new[]{("sys_session","project_id"),("yz_role_permission","yz_project_id"),("yz_manager","yz_project_id"),("yz_role","yz_project_id"),("yz_admin_type","yz_project_id"),("yz_project","id")})await ExecuteDelete(session,table,key,project);return true;});
                foreach(var(table,key)in new[]{("sys_session","project_id"),("yz_role_permission","yz_project_id"),("yz_manager","yz_project_id"),("yz_role","yz_project_id"),("yz_admin_type","yz_project_id"),("yz_project","id")})Assert.Empty(await store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{key,project}}));await Receipt("cleaned",true);
            }
            await fixture.DisposeAsync();
        }
        // 样本只在已有事务内写入当前随机项目，口令和连接不进入收据。
        Task Seed(string table,params(string Key,object? Value)[] values)=>store.InsertAsync(table,values.ToDictionary(x=>x.Key,x=>x.Value));
        // 私有证据只记录自有标识及验收边界，不记录会话令牌或业务行。
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,project,admin,foreignAdmin,editorRole,foreignRole,editor,createdManager,createdRole,verified,cleaned,persistedWebIdentity=true,generatedControllers=true,productionCoordinationRegistration=true,initializationExecuted=false,ddlExecuted=false,permissionsGranted=false,defaultFullGateRemoved=false},Json)+"\n");
    }
    /// <summary>反射只穿过实际生成程序集边界，保留原异常与异步结果，不实现授权替身。</summary>
    private static async Task<object?> Call(object target,string method,params object?[] args){var task=(Task)TrustedSecurityServiceFixture.Invoke(target,method,args)!;await task;return task.GetType().GetProperty("Result")?.GetValue(task);}
    /// <summary>生成接口仍沿原表名和页面ID构造，不增加产品HTTP入口。</summary>
    private static string Route(string table,string page)=>"/api/admin/auto/"+table+"/"+page;
    /// <summary>只比较自有行快照的摘要，失败不输出账号原值。</summary>
    private static string Fingerprint(object value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json)));
    /// <summary>仅固定六表和固定列允许清理；参数不拼SQL，必须已有同一真实事务。</summary>
    private static async Task ExecuteDelete(DatabaseSession session,string table,string key,long id)
    {
        if(session.CurrentTransaction is null||!(new[]{("sys_session","project_id"),("yz_role_permission","yz_project_id"),("yz_manager","yz_project_id"),("yz_role","yz_project_id"),("yz_admin_type","yz_project_id"),("yz_project","id")}).Contains((table,key)))throw new InvalidOperationException("拒绝越界清理。");
        await using var command=session.CurrentTransaction.Connection!.CreateCommand();command.Transaction=session.CurrentTransaction;command.CommandText="DELETE FROM "+session.Provider.QuoteIdentifier(table)+" WHERE "+session.Provider.QuoteIdentifier(key)+"=@owned";var p=command.CreateParameter();p.ParameterName="@owned";p.Value=id;command.Parameters.Add(p);await command.ExecuteNonQueryAsync();
    }
}
