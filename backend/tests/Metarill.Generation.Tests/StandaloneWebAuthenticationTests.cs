using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实Full包编译后直接执行其认证模板；数据库命令由本地ADO替身处理，不启动HTTP或数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed partial class StandaloneWebAuthenticationTests(WebAuthenticationPackageFixture package):IClassFixture<WebAuthenticationPackageFixture>
{
    private const string Username="web-offline-owner",Password="离线初始Password2026!",NextPassword="离线更新Password2026!";
    /// <summary>默认/显式Web不使用开发凭据，无配置和畸形令牌在解析数据库服务前拒绝。</summary>
    [Theory]
    [InlineData("Production",null,false,false)] [InlineData("Development",null,false,false)]
    [InlineData("Development","Web",true,false)] [InlineData("Production","Development",true,false)]
    [InlineData("Production",null,true,false)] [InlineData("Development","Development",true,true)]
    [InlineData("Development",null,true,true)]
    public async Task Modes_and_anonymous_tokens_fail_closed_before_database_resolution(string environment,string? mode,bool development,bool expectedDevelopment)
    {
        var settings=Settings();settings["Authentication:Mode"]=mode;settings["Authentication:DevelopmentBootstrap:Enabled"]=development.ToString();
        var selected=package.Mode(settings,environment);Assert.Equal(expectedDevelopment,package.Property<bool>(selected,"Development"));Assert.False(package.Property<bool>(selected,"DatabaseConfigured"));
        var services=new ServiceCollection();var resolutions=0;services.AddSingleton<AuthenticationService>(_=>{resolutions++;throw new InvalidOperationException("不应解析数据库认证");});using var provider=services.BuildServiceProvider();
        var registry=package.Registry();var web=package.New("ApplicationWebAuthentication",provider,selected,registry);
        foreach(var token in new[]{"", "short",new string('x',201),new string('a',64)})Assert.Null(await package.Call(web,"ResolveAsync",token,CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>package.Call(web,"LoginAsync",Username,Password,CancellationToken.None));Assert.Equal(0,resolutions);
        var context=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};var authorization=(IRuntimeAuthorization)package.New("ApplicationWebAuthorization",context,package.Authority());Assert.Throws<UnauthorizedAccessException>(()=>authorization.RequireActor());
        settings["Authentication:Mode"]="unknown";Assert.Throws<InvalidOperationException>(()=>package.Mode(settings,environment));
    }

    /// <summary>本宿主只签发64位小写hex，配置了数据库的畸形令牌也必须在解析数据库依赖前拒绝。</summary>
    [Theory]
    [InlineData("empty")] [InlineData("short")] [InlineData("nothex")] [InlineData("uppercase")] [InlineData("length63")] [InlineData("length65")]
    public async Task Malformed_web_tokens_with_database_configuration_never_resolve_database(string kind)
    {
        var configured=package.Mode(new(){["Database:ConnectionString"]="offline",["Database:ExpectedDatabase"]="offline"},"Production");var resolutions=0;var services=new ServiceCollection();services.AddSingleton<AuthenticationService>(_=>{resolutions++;throw new InvalidOperationException("不应解析数据库");});using var provider=services.BuildServiceProvider();var web=package.New("ApplicationWebAuthentication",provider,configured,package.Registry());
        var token=kind switch{"empty"=>"","short"=>"short","nothex"=>new string('g',64),"uppercase"=>new string('A',64),"length63"=>new string('a',63),_=>new string('a',65)};Assert.Null(await package.Call(web,"ResolveAsync",token,CancellationToken.None));Assert.Equal(0,resolutions);
    }

    /// <summary>显式Web配置错误直接失败，不能调用Development会话或退回固定id1。</summary>
    [Fact]
    public async Task Explicit_web_failure_does_not_fall_back_to_configured_development_credentials()
    {
        var settings=Settings();settings["Authentication:Mode"]="Web";settings["Authentication:DevelopmentBootstrap:Enabled"]="true";settings["Database:ConnectionString"]="invalid-private-test";settings["Database:ExpectedDatabase"]="offline";
        var selected=package.Mode(settings,"Development");Assert.False(package.Property<bool>(selected,"Development"));Assert.True(package.Property<bool>(selected,"DatabaseConfigured"));
        var resolutions=0;var services=new ServiceCollection();services.AddSingleton<SqlRowStore>(_=>{resolutions++;throw new InvalidOperationException("预设配置失败");});using var provider=services.BuildServiceProvider();
        var web=package.New("ApplicationWebAuthentication",provider,selected,package.Registry());await Assert.ThrowsAsync<InvalidOperationException>(()=>package.Call(web,"LoginAsync",Username,Password,CancellationToken.None));Assert.Equal(1,resolutions);
    }

    /// <summary>实际模板初始化与共享认证执行持久会话协议；业务super不产生平台身份，撤权下一次解析生效。</summary>
    [Fact]
    public async Task Initialization_web_login_menu_roles_and_logout_use_real_persisted_identity()
    {
        using var f=new OfflineApplication(package);Assert.Equal("initialized",await f.Initialize(Settings()));
        var account=Assert.Single(f.Provider.Rows["yz_manager"]);var role=Assert.Single(f.Provider.Rows["yz_role"]);Assert.True(SqlRowStore.Id(account)>1&&SqlRowStore.Id(role)>1);Assert.NotEqual(SqlRowStore.Id(account),SqlRowStore.Id(role));
        Assert.StartsWith("pbkdf2-sha256$",SqlRowStore.Text(account,"password"));Assert.True(PasswordHasher.Verify(Password,SqlRowStore.Text(account,"password")));Assert.DoesNotContain(Password,SqlRowStore.Text(account,"password"));
        Assert.EndsWith("FOR UPDATE",f.Provider.Commands[0]);Assert.Contains("yz_project",f.Provider.Commands[0]);Assert.All(f.Provider.WriteTransactions,Assert.True);
        var first=await f.Login(Password);Assert.Equal(SqlRowStore.Text(account,"id"),first.User["id"]);Assert.False(first.User.ContainsKey("password"));Assert.False(first.User.ContainsKey("salt"));Assert.False(first.User.ContainsKey("oldPassword"));
        var stored=Assert.Single(f.Provider.Rows["sys_session"]);Assert.Equal(PasswordHasher.Digest(first.Token),SqlRowStore.Text(stored,"token_hash"));Assert.DoesNotContain(first.Token,JsonSerializer.Serialize(f.Provider.Rows));
        var identity=await f.Resolve(first.Token);Assert.NotNull(identity);Assert.True(identity.IsRealmSuper);Assert.False(identity.Actor.IsAdministrator);Assert.Equal(package.Metadata.YzProjectId,identity.Actor.ProjectId);Assert.Equal(SqlRowStore.Id(account),identity.Actor.AccountId);
        var authorization=f.Authorize(identity);foreach(var page in package.Metadata.ConfigPages)authorization.RequirePagePermission(page.Id,"list");
        var navigation=package.New("DevelopmentNavigation",package.Metadata,f.Accessor,f.Mode);var menu=JsonSerializer.SerializeToElement(package.Invoke(navigation,"Menu",authorization));Assert.Equal(200,menu.GetProperty("code").GetInt32());Assert.True(menu.GetProperty("permissions").GetProperty("isSuper").GetBoolean());Assert.NotEqual(0,menu.GetProperty("menu").GetArrayLength());
        role["is_super"]=0;var restricted=await f.Resolve(first.Token);Assert.NotNull(restricted);Assert.False(restricted.IsRealmSuper);Assert.Empty(restricted.Permissions);Assert.Throws<System.Security.SecurityException>(()=>f.Authorize(restricted).RequirePagePermission(package.Metadata.ConfigPages[0].Id,"list"));
        var limited=JsonSerializer.SerializeToElement(package.Invoke(navigation,"Menu",f.Authorize(restricted)));Assert.DoesNotContain("GENERATED_PAGE",limited.GetRawText());
        var pageId=long.Parse(package.Metadata.ConfigPages[0].Id,CultureInfo.InvariantCulture);f.Add("yz_role_permission",("id",12345L),("yz_role_id",SqlRowStore.Id(role)),("config_page_id",pageId),("permission_values","[\"is_query\"]"));
        var granted=await f.Resolve(first.Token);Assert.NotNull(granted);f.Authorize(granted).RequirePagePermission(package.Metadata.ConfigPages[0].Id,"list");Assert.Throws<System.Security.SecurityException>(()=>f.Authorize(granted).RequirePagePermission(package.Metadata.ConfigPages[0].Id,"create"));
        role["delete_no"]=DateTime.UtcNow;var revoked=await f.Resolve(first.Token);Assert.NotNull(revoked);Assert.Empty(revoked.Permissions);role["delete_no"]=null;
        await package.Call(f.Web,"LogoutAsync",granted,CancellationToken.None);Assert.Null(await f.Resolve(first.Token));
    }

    /// <summary>Agent、包外真实realm、停用/过期/删除或错误密码不能成为本应用Web身份。</summary>
    [Theory]
    [InlineData("agent")] [InlineData("outside-project")] [InlineData("outside-admin")] [InlineData("disabled")] [InlineData("expired")] [InlineData("deleted")]
    public async Task Session_and_realm_rejection_preserve_all_original_data(string kind)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var login=await f.Login(Password);var account=f.Provider.Rows["yz_manager"][0];var session=f.Provider.Rows["sys_session"][0];var admin=f.Provider.Rows["yz_admin_type"][0];
        if(kind=="agent"){session["kind"]="agent";session["grant_id"]=8L;}
        if(kind=="outside-project"){f.Add("yz_project",("id",9988L),("name","包外项目"));admin["yz_project_id"]=9988L;}
        if(kind=="outside-admin"){f.Add("yz_admin_type",("id",9999L),("yz_project_id",long.Parse(package.Metadata.YzProjectId,CultureInfo.InvariantCulture)));account["yz_admin_type_id"]=9999L;}
        if(kind=="disabled")account["is_disabled"]=1;if(kind=="expired")session["expires_at"]=DateTime.UtcNow.AddMinutes(-1);if(kind=="deleted")account["delete_no"]=DateTime.UtcNow;
        var before=f.Snapshot();Assert.Null(await f.Resolve(login.Token));Assert.Equal(before,f.Snapshot());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Login("错误的离线密码"));Assert.Equal(before,f.Snapshot());
    }

    /// <summary>重复初始化不恢复配置密码、不挂新角色；墓碑/归属冲突和插入冲突均完整回滚。</summary>
    [Theory]
    [InlineData("repeat")] [InlineData("account-tombstone")] [InlineData("project-tombstone")] [InlineData("admin-tombstone")] [InlineData("wrong-admin-project")] [InlineData("existing-plain-account")] [InlineData("role-tombstone")]
    public async Task Initialization_never_resets_rebinds_or_revives_existing_records(string kind)
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var account=f.Provider.Rows["yz_manager"][0];var role=f.Provider.Rows["yz_role"][0];account["password"]=PasswordHasher.Hash(NextPassword);
        if(kind=="account-tombstone")account["delete_no"]=DateTime.UtcNow;if(kind=="project-tombstone")f.Provider.Rows["yz_project"][0]["delete_no"]=DateTime.UtcNow;if(kind=="admin-tombstone")f.Provider.Rows["yz_admin_type"][0]["delete_no"]=DateTime.UtcNow;
        if(kind=="wrong-admin-project")f.Provider.Rows["yz_admin_type"][0]["yz_project_id"]=7777L;if(kind=="existing-plain-account")account["yz_role_ids"]="[]";if(kind=="role-tombstone")role["delete_no"]=DateTime.UtcNow;
        var before=f.Snapshot();if(kind=="repeat"){Assert.Equal("unchanged",await f.Initialize(Settings()));Assert.True(PasswordHasher.Verify(NextPassword,SqlRowStore.Text(account,"password")));}else{await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Initialize(Settings()));Assert.True(f.Provider.Rollbacks>0);}Assert.Equal(before,f.Snapshot());
    }

    /// <summary>本地ADO替身注入唯一键冲突/尾部失败，只证明真实事务入口回滚，不冒称MySQL并发验收。</summary>
    [Theory]
    [InlineData("yz_project")] [InlineData("yz_admin_type")] [InlineData("yz_role")] [InlineData("yz_manager")]
    public async Task Initialization_transaction_rolls_back_unique_conflicts_without_partial_accounts(string failedTable)
    {
        using var f=new OfflineApplication(package);f.Provider.FailOnInsert=failedTable;var before=f.Snapshot();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Initialize(Settings()));Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);Assert.Equal(0,f.Provider.Commits);
    }

    /// <summary>密码仅自助核验；摘要更新和全部会话撤销处于同一事务，错误和尾部异常不留半次改密。</summary>
    [Fact]
    public async Task Password_change_checks_current_session_and_atomically_revokes_all_account_sessions()
    {
        using var f=new OfflineApplication(package);await f.Initialize(Settings());var first=await f.Login(Password);var second=await f.Login(Password);var identity=(await f.Resolve(first.Token))!;var before=f.Snapshot();
        await Assert.ThrowsAsync<System.Security.SecurityException>(()=>package.Call(f.Web,"ChangePasswordAsync",identity,"错误密码",NextPassword,CancellationToken.None));Assert.Equal(before,f.Snapshot());
        f.Provider.FailOnUpdate="sys_session";await Assert.ThrowsAsync<InvalidOperationException>(()=>package.Call(f.Web,"ChangePasswordAsync",identity,Password,NextPassword,CancellationToken.None));Assert.Equal(before,f.Snapshot());f.Provider.FailOnUpdate=null;
        await package.Call(f.Web,"ChangePasswordAsync",identity,Password,NextPassword,CancellationToken.None);Assert.True(PasswordHasher.Verify(NextPassword,SqlRowStore.Text(f.Provider.Rows["yz_manager"][0],"password")));Assert.Null(await f.Resolve(first.Token));Assert.Null(await f.Resolve(second.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Login(Password));Assert.NotNull(await f.Resolve((await f.Login(NextPassword)).Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>package.Call(f.Web,"ChangePasswordAsync",identity,NextPassword,Password,CancellationToken.None));Assert.All(f.Provider.WriteTransactions,Assert.True);
    }

    /// <summary>多后台必须选包内ID，配置验证发生在任何ADO连接前；不接受命令选择的目录或项目。</summary>
    [Fact]
    public async Task Bootstrap_admin_is_trusted_and_multi_realm_requires_explicit_selection()
    {
        var metadata=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(package.Metadata))!;metadata.YzAdminTypes.Add(new(){Id="2300000000000000877",Name="第二后台",YzProjectId=metadata.YzProjectId});
        using var f=new OfflineApplication(package,metadata);var settings=Settings();await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Initialize(settings));Assert.Equal(0,f.Provider.Opens);
        settings["Authentication:Bootstrap:AdminTypeId"]="outside";await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Initialize(settings));Assert.Equal(0,f.Provider.Opens);
        settings["Authentication:Bootstrap:AdminTypeId"]=metadata.YzAdminTypes[1].Id;Assert.Equal("initialized",await f.Initialize(settings));Assert.Equal(metadata.YzAdminTypes[1].Id,SqlRowStore.Text(f.Provider.Rows["yz_manager"][0],"yz_admin_type_id"));
    }

    /// <summary>即使后台ID来自声明，错误项目归属也不能在初始化时被覆盖为本项目。</summary>
    [Fact]
    public async Task Declared_admin_from_another_project_is_rejected_without_opening_database()
    {
        var metadata=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(package.Metadata))!;metadata.YzAdminTypes[0].YzProjectId="2300000000000000999";using var f=new OfflineApplication(package,metadata);await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Initialize(Settings()));Assert.Equal(0,f.Provider.Opens);Assert.Empty(f.Provider.Rows);
    }

    /// <summary>原登录URL编码表单和JSON按真实编译函数解析；重复/混合/过量字段在解析身份前拒绝。</summary>
    [Theory]
    [InlineData("json")] [InlineData("form")] [InlineData("duplicate")] [InlineData("extra")] [InlineData("query")] [InlineData("oversized")]
    public async Task Login_parser_preserves_original_form_json_and_rejects_unknown_or_url_secrets(string kind)
    {
        var context=new DefaultHttpContext();var good=kind is "json" or "form";context.Request.ContentType=kind=="form"?"application/x-www-form-urlencoded":"application/json";
        var body=kind switch{"form"=>"username=web-offline-owner&password="+Uri.EscapeDataString(Password),"duplicate"=>"{\"username\":\"a\",\"username\":\"b\",\"password\":\"c\"}","extra"=>JsonSerializer.Serialize(new{username=Username,password=Password,projectId="1"}),"oversized"=>new string('a',4097),_=>JsonSerializer.Serialize(new{username=Username,password=Password})};
        context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(body));if(kind=="query")context.Request.QueryString=new("?password=not-accepted");
        if(good){var parsed=await package.TopLevel("ReadLogin",context.Request);Assert.Equal(Username,package.Property<string>(parsed!,"Username"));Assert.Equal(Password,package.Property<string>(parsed!,"Password"));}else await Assert.ThrowsAsync<ArgumentException>(()=>package.TopLevel("ReadLogin",context.Request));
    }
    /// <summary>真实编译的命令边界在创建宿主前拒绝CLI秘密与混合初始化；不启动入口Main。</summary>
    [Theory]
    [InlineData("--initialize-security",false)] [InlineData("--schema-preview",false)] [InlineData("--urls=http://127.0.0.1:5080",false)]
    [InlineData("--Authentication:Bootstrap:Password=离线秘密",true)] [InlineData("Authentication:Bootstrap:Password=离线秘密",true)]
    [InlineData("/Authentication:Bootstrap:Password=离线秘密",true)] [InlineData("/Database:ConnectionString=离线秘密",true)]
    [InlineData("/Authentication:Bootstrap:Password 离线秘密",true)] [InlineData("/Database:ConnectionString 离线秘密",true)]
    [InlineData("--database:ConnectionString=离线秘密",true)] [InlineData("Database:ConnectionString=离线秘密",true)]
    [InlineData("--initialize-security=value",true)] [InlineData("--initialize-unknown",true)]
    [InlineData("--initialize-security --schema-preview",true)] [InlineData("--initialize-security --initialize-security",true)]
    [InlineData("--initialize-security username=value",true)]
    public async Task Initialization_arguments_are_exact_and_command_line_credentials_are_rejected(string command,bool rejected)
    {
        // 实际框架提供器接受斜杠形式；不能仅检验我们自己枚举的双横线参数。
        if(command.StartsWith("/",StringComparison.Ordinal))
        {
            var arguments=command.Split(' ');var parsed=new ConfigurationBuilder().AddCommandLine(arguments).Build();var key=arguments[0].TrimStart('/').Split('=')[0];Assert.Equal("离线秘密",parsed[key]);
        }
        Assert.Equal(rejected,await package.TopLevel("InvalidSecurityArguments",(object)command.Split(' ')));
    }

    private static Dictionary<string,string?> Settings()=>new(){["Authentication:Bootstrap:Username"]=Username,["Authentication:Bootstrap:Password"]=Password};

    /// <summary>每个场景独立内存表，调用实际包模板和共享AuthenticationService，外部连接永不建立。</summary>
    private sealed class OfflineApplication:IDisposable
    {
        internal readonly StandaloneMemoryProvider Provider=new();internal readonly HttpContextAccessor Accessor=new(){HttpContext=new DefaultHttpContext()};internal readonly object Web,Mode;private readonly WebAuthenticationPackageFixture package;private readonly ServiceProvider services;private readonly MetadataCatalog metadata;
        internal OfflineApplication(WebAuthenticationPackageFixture fixture,MetadataCatalog? catalog=null)
        {
            package=fixture;metadata=catalog??fixture.Metadata;var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="offline"},Provider.Orm,Provider);var store=new SqlRowStore(session,fixture.Security);
            var registry=fixture.Registry();Mode=fixture.Mode(new(){["Database:ConnectionString"]="offline",["Database:ExpectedDatabase"]="offline"},"Production");
            var registrations=new ServiceCollection();registrations.AddSingleton(session);registrations.AddSingleton(store);registrations.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(877));registrations.AddSingleton<ISecurityResourceRegistry>((ISecurityResourceRegistry)registry);registrations.AddSingleton<ISecuritySessionPolicy>((ISecuritySessionPolicy)registry);registrations.AddScoped<AuthenticationService>();services=registrations.BuildServiceProvider();Web=fixture.New("ApplicationWebAuthentication",services,Mode,registry);
        }
        internal async Task<string> Initialize(Dictionary<string,string?> settings)=>(string)(await package.Call(package.New("ApplicationSecurityInitialization",services.GetRequiredService<SqlRowStore>(),services.GetRequiredService<IIdGenerator>(),metadata),"InitializeAsync",new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),CancellationToken.None))!;
        internal async Task<(Dictionary<string,object?> User,string Token)> Login(string password){var result=(await package.Call(Web,"LoginAsync",Username,password,CancellationToken.None))!;return((Dictionary<string,object?>)result.GetType().GetField("Item1")!.GetValue(result)!,(string)result.GetType().GetField("Item2")!.GetValue(result)!);}
        internal async Task<AuthenticatedIdentity?> Resolve(string token)=>(AuthenticatedIdentity?)await package.Call(Web,"ResolveAsync",token,CancellationToken.None);
        internal IRuntimeAuthorization Authorize(AuthenticatedIdentity identity){Accessor.HttpContext!.Items[typeof(AuthenticatedIdentity)]=identity;return(IRuntimeAuthorization)package.New("ApplicationWebAuthorization",Accessor,package.Authority());}
        internal string Snapshot()=>JsonSerializer.Serialize(Provider.Rows);
        internal void Add(string table,params(string Name,object? Value)[] values){if(!Provider.Rows.TryGetValue(table,out var rows))Provider.Rows[table]=rows=[];rows.Add(values.ToDictionary(pair=>pair.Name,pair=>pair.Value));}
        public void Dispose()=>services.Dispose();
    }
}

/// <summary>一个真实包只构建一次；程序集以字节载入，不运行其Main，不占端口或数据库。</summary>
public sealed class WebAuthenticationPackageFixture:IAsyncLifetime
{
    internal const string Namespace="Example.PackagedWebAuthentication";private readonly string root=Path.Combine(Path.GetTempPath(),"metarill-web-auth-source",Guid.NewGuid().ToString("N"));private Assembly assembly=null!;
    internal MetadataCatalog Metadata{get;private set;}=null!;internal MetadataCatalog Security{get;private set;}=null!;internal byte[] CatalogBytes{get;private set;}=[];
    public async Task InitializeAsync()
    {
        var source=SourceRoot();var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);Metadata=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),json)!;Security=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/security.json")),json)!;
        new ApplicationPackager().Create(Metadata,root,Namespace,source);CatalogBytes=await File.ReadAllBytesAsync(Path.Combine(root,"backend/metadata/project.json"));var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})start.ArgumentList.Add(arg);
        using(await SharedSourceBuildGate.Shared.EnterAsync()){using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));try{await process.WaitForExitAsync(deadline.Token);}catch(Exception interrupted){await SharedSourceBuildGate.StopOwnProcessAsync(process,output,error,interrupted);throw;}Assert.True(process.ExitCode==0,await output+await error);}
        assembly=AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(root,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll"))));
    }
    /// <summary>反射只用于调用真实包类型，构造异常保留原始产品异常而不让包装影响合同断言。</summary>
    internal object New(string type,params object?[] arguments)
    {
        try{return Activator.CreateInstance(assembly.GetType(Namespace+".Host."+type,true)!,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,arguments,CultureInfo.InvariantCulture)!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    /// <summary>认证始终使用真实编译包字节；替代目录仅用于既有Initializer输入反例，不冒充编译权威。</summary>
    internal object Authority()=>New("VerifiedPackageSecurityCatalog",CatalogBytes.ToArray());
    internal object Registry()=>New("ApplicationSecurityRegistry",Authority());
    internal void RegisterCatalog(IServiceCollection services,object authority)=>Invoke(assembly.GetType(Namespace+".Host.ApplicationSecurityCatalogRegistration",true)!,"Add",services,authority);
    /// <summary>执行实际完整包的正式Web注册，不能由测试重建一份近似DI替代。</summary>
    internal void RegisterCoordination(IServiceCollection services,object mode)=>Invoke(HostType("ApplicationWebSecurityCoordination"),"Add",services,mode);
    /// <summary>从实际编译的宿主程序集读取固定类型，测试不替代宿主实现。</summary>
    internal Type HostType(string name)=>assembly.GetType(Namespace+".Host."+name,true)!;
    internal object Mode(Dictionary<string,string?> values,string environment)=>Invoke(assembly.GetType(Namespace+".Host.ApplicationAuthenticationMode",true)!,"Read",new ConfigurationBuilder().AddInMemoryCollection(values).Build(),new TestEnvironment{EnvironmentName=environment})!;
    internal T Property<T>(object instance,string name)=>(T)instance.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(instance)!;
    internal object? Invoke(object instance,string name,params object?[] arguments){var type=instance as Type??instance.GetType();var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)!;try{return method.Invoke(instance is Type?null:instance,arguments);}catch(TargetInvocationException error)when(error.InnerException is not null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    internal async Task<object?> Call(object instance,string name,params object?[] arguments){var task=(Task)Invoke(instance,name,arguments)!;await task;return task.GetType().GetProperty("Result")?.GetValue(task);}
    internal async Task<object?> TopLevel(string name,params object?[] arguments){var type=assembly.GetType("Program",true)!;var method=type.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(item=>item.Name.Contains("g__"+name+"|",StringComparison.Ordinal));var value=method.Invoke(null,arguments);if(value is not Task task)return value;await task;return task.GetType().GetProperty("Result")?.GetValue(task);}
    private static string SourceRoot([CallerFilePath]string path="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
    public Task DisposeAsync()=>TestDirectoryCleanup.DeleteAsync(root,Path.Combine(Path.GetTempPath(),"metarill-web-auth-source"));
    private sealed class TestEnvironment:IHostEnvironment{public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="offline";public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}

/// <summary>只解释SqlRowStore固定单表SQL，所有连接和事务都在内存；不替代真实MySQL锁观测。</summary>
internal sealed class StandaloneMemoryProvider:IDatabaseProvider
{
    internal Dictionary<string,List<Dictionary<string,object?>>> Rows{get;set;}=new(StringComparer.Ordinal);internal readonly List<string> Commands=[];internal readonly List<bool> WriteTransactions=[];internal int Opens,Commits,Rollbacks;internal string? FailOnInsert,FailOnUpdate;
    internal IFreeSql Orm{get;}=DispatchProxy.Create<IFreeSql,StandaloneForbiddenOrm>();
    public string Name=>"offline";public DatabaseCapabilities Capabilities=>new("offline",true,false);public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>Orm;public DbConnection CreateConnection(DatabaseOptions options)=>new MemoryConnection(this);public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);
    private DbDataReader Read(string sql,DbParameterCollection parameters)
    {
        Commands.Add(sql);var match=Regex.Match(sql,@"^SELECT (.+) FROM `([^`]+)`(?: WHERE (.*?))?(?: FOR UPDATE)?$");if(!match.Success)throw new NotSupportedException("测试替身拒绝未知SQL");var columns=match.Groups[1].Value.Split(',').Select(name=>name.Trim('`')).ToArray();var table=new DataTable();foreach(var column in columns)table.Columns.Add(column,typeof(object));
        foreach(var row in Rows.GetValueOrDefault(match.Groups[2].Value)??[]){var accepted=true;foreach(Match filter in Regex.Matches(match.Groups[3].Value,@"`([^`]+)`( IS NULL|=@([A-Za-z0-9_]+))"))accepted&=Equal(row.GetValueOrDefault(filter.Groups[1].Value),filter.Groups[2].Value==" IS NULL"?null:parameters[filter.Groups[3].Value].Value);if(accepted)table.Rows.Add(columns.Select(column=>row.GetValueOrDefault(column)??DBNull.Value).ToArray());}return table.CreateDataReader();
    }
    private int Write(string sql,DbParameterCollection parameters,bool transaction)
    {
        Commands.Add(sql);WriteTransactions.Add(transaction);if(!transaction)throw new InvalidOperationException("测试发现事务外写入");var update=Regex.Match(sql,@"^UPDATE `([^`]+)` SET (.+) WHERE `id`=@record_id$");
        if(update.Success){if(FailOnUpdate==update.Groups[1].Value)throw new InvalidOperationException("模拟更新尾部失败");var row=(Rows.GetValueOrDefault(update.Groups[1].Value)??[]).SingleOrDefault(row=>Equal(row.GetValueOrDefault("id"),parameters["record_id"].Value));if(row is null)return 0;foreach(Match field in Regex.Matches(update.Groups[2].Value,@"`([^`]+)`=@([A-Za-z0-9_]+)"))row[field.Groups[1].Value]=Unwrap(parameters[field.Groups[2].Value].Value);return 1;}
        var insert=Regex.Match(sql,@"^INSERT INTO `([^`]+)` \((.+)\) VALUES \((.+)\)$");if(!insert.Success)throw new NotSupportedException("测试替身拒绝未知写入");var target=insert.Groups[1].Value;if(FailOnInsert==target)throw new InvalidOperationException("模拟并发唯一主键冲突");var names=insert.Groups[2].Value.Split(',').Select(name=>name.Trim('`')).ToArray();var values=insert.Groups[3].Value.Split(',').Select(name=>Unwrap(parameters[name.TrimStart('@')].Value)).ToArray();if(!Rows.TryGetValue(target,out var rows))Rows[target]=rows=[];var next=names.Select((name,index)=>(name,index)).ToDictionary(pair=>pair.name,pair=>values[pair.index]);if(rows.Any(row=>Equal(row.GetValueOrDefault("id"),next["id"])))throw new InvalidOperationException("模拟主键重复");rows.Add(next);return 1;
    }
    private static object? Unwrap(object? value)=>value is DBNull?null:value;private static bool Equal(object? left,object? right)=>string.Equals(Convert.ToString(Unwrap(left),CultureInfo.InvariantCulture),Convert.ToString(Unwrap(right),CultureInfo.InvariantCulture),StringComparison.Ordinal)&&((Unwrap(left)is null)==(Unwrap(right)is null));
    private sealed class MemoryConnection(StandaloneMemoryProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open(){provider.Opens++;state=ConnectionState.Open;}public override void Close()=>state=ConnectionState.Closed;protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new MemoryTransaction(this,provider);protected override DbCommand CreateDbCommand()=>new MemoryCommand(provider,this);
    }
    private sealed class MemoryTransaction(MemoryConnection connection,StandaloneMemoryProvider provider):DbTransaction
    {
        private readonly Dictionary<string,List<Dictionary<string,object?>>> before=provider.Rows.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(row=>new Dictionary<string,object?>(row)).ToList());public override IsolationLevel IsolationLevel=>IsolationLevel.Unspecified;protected override DbConnection DbConnection=>connection;public override void Commit()=>provider.Commits++;public override void Rollback(){provider.Rollbacks++;provider.Rows=before;}
    }
    private sealed class MemoryCommand(StandaloneMemoryProvider provider,MemoryConnection connection):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override object? ExecuteScalar()=>throw new NotSupportedException();public override int ExecuteNonQuery()=>provider.Write(CommandText,Parameters,DbTransaction is not null);protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Read(CommandText,Parameters);
    }
}
public class StandaloneForbiddenOrm:DispatchProxy{protected override object? Invoke(MethodInfo? method,object?[]? arguments)=>method!.Name=="Dispose"?null:throw new InvalidOperationException("认证模板离线测试禁止ORM或联网");}
