using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>只建立固定三表读取样本和自有权限/会话；私有生成页面不发布到config_*，认证沿原产品链。</summary>
internal sealed class FixedRoleReadUseMySqlFixture : IAsyncDisposable
{
    internal const string ManagerPage="6031906822946816", RolePage="1692532858529329154", AdminPage="1696799309006348290";
    private const string ManagerTable="6031906809708544",RoleTable="1692532858499969026",AdminTable="1696799308939239425",Namespace="Example.FixedRoleMySql";
    private static readonly SnowflakeIdGenerator Ids=new(881);
    private readonly string nonce=Guid.NewGuid().ToString("N")[..16];
    private readonly List<Owned> owned=[];
    private sealed record Owned(string Table,long Id,string? Name,long Project,long Creator,Dictionary<string,object?> Expected){internal string? Fingerprint;}
    internal sealed record SqlTrace(string Sql,int Parameters);
    internal readonly List<SqlTrace> Commands=[];
    internal DatabaseSession Session{get;private set;}=null!;
    internal SqlRowStore Store{get;private set;}=null!;
    internal MetadataRegistry Registry{get;private set;}=null!;
    internal HttpClient Client{get;private set;}=null!;
    internal ExcelPageExportService Exports{get;private set;}=null!;
    internal long AccountId{get;private set;}
    internal long AuthRole{get;private set;}
    internal long Project{get;private set;}
    internal long OtherProject{get;private set;}
    internal long Admin{get;private set;}
    internal string Token{get;private set;}="";
    internal string Directory{get;private set;}="";
    internal string RoleJson{get;private set;}="";
    internal readonly Dictionary<string,long> Keys=new(StringComparer.Ordinal);
    private AuthenticationService authentication=null!;
    private WebApplication? app;
    private Assembly generated=null!;
    private DatabaseOptions options=null!;
    internal sealed record InstancePin(string ExpectedHostname,string ExpectedServerUuid);
    private InstancePin instancePin=null!;
    private bool completed;
    private int cleaned;
    private bool failed;

    /// <summary>只读canonical后复制两页开关到私有文件；原平台文件、数据库元数据、按钮及开关不改。</summary>
    internal static MetadataCatalog PrivateCatalog(string source)
    {
        var all=MetadataRegistry.Load(source);var clone=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(all,JsonDefaults.Options),JsonDefaults.Options)!;
        foreach(var page in clone.ConfigPages.Where(page=>page.Id is ManagerPage or RolePage)){page.IsExport=1;page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("list");page.Extra["type"]=JsonSerializer.SerializeToElement(2);}
        return clone;
    }
    /// <summary>生成仅含固定角色/账号/后台物理读取依赖的三表私有模型，不创建物理表。</summary>
    private static MetadataCatalog Model(MetadataCatalog all)
    {
        var model=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(all,JsonDefaults.Options),JsonDefaults.Options)!;var tables=new[]{ManagerTable,RoleTable,AdminTable};
        model.ConfigTables=model.ConfigTables.Where(table=>tables.Contains(table.Id)).ToList();
        model.ConfigColumns=model.ConfigColumns.Where(column=>tables.Contains(column.ConfigTableId)&&column.ColumnName is "id" or "name" or "yz_role_ids" or "yz_admin_type_id" or "yz_project_id" or "delete_no" or "is_super" or "password" or "salt" or "old_password").ToList();
        model.ConfigPages=model.ConfigPages.Where(page=>page.Id is ManagerPage or RolePage or AdminPage).ToList();
        var columns=model.ConfigColumns.Select(column=>column.Id).ToHashSet();var pages=model.ConfigPages.Select(page=>page.Id).ToHashSet();
        model.ConfigPageColumns=model.ConfigPageColumns.Where(field=>pages.Contains(field.ConfigPageId)&&columns.Contains(field.ConfigColumnId)).ToList();
        model.ConfigMenus=[];model.ConfigButtons=[];model.ConfigChildren=[];model.ConfigCustomPages=[];model.ConfigIndexes=[];model.ConfigIndexColumns=[];return model;
    }
    /// <summary>真实生成并编译，固定程序集只引用本clone的Core/Data；超时仅结束自己创建的构建进程。</summary>
    internal static async Task<Assembly> CompileAsync(string directory,MetadataCatalog catalog,[CallerFilePath]string file="")
    {
        System.IO.Directory.CreateDirectory(directory);new CSharpGenerator().Generate(Model(catalog),Path.Combine(directory,"generated"),Namespace);
        var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));var assembly="Metarill.FixedRoleSql."+Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(directory,"Fixture.csproj"),$$"""
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>{{assembly}}</AssemblyName></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><ProjectReference Include="{{SecurityElement.Escape(Path.Combine(root,"backend/src/Metarill.Core/Metarill.Core.csproj"))}}"/><ProjectReference Include="{{SecurityElement.Escape(Path.Combine(root,"backend/src/Metarill.Data/Metarill.Data.csproj"))}}"/></ItemGroup></Project>
""");
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"build","Fixture.csproj","--nologo","--verbosity","quiet","--disable-build-servers","-warnaserror"})start.ArgumentList.Add(argument);start.Environment["MSBUILDDISABLENODEREUSE"]="1";
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw new InvalidOperationException("私有读取合同编译超时，已停止自有构建进程。");}
        var output=await stdout+await stderr;await File.WriteAllTextAsync(Path.Combine(directory,"build.log"),output);Assert.True(process.ExitCode==0,"私有读取合同编译失败，详见本机隔离证据中的build.log。");
        return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(directory,"bin/Debug/net10.0",assembly+".dll"))));
    }
    /// <summary>先钉住真实Server200实例再写任何样本；缺配置由Fact属性跳过，本方法绝不扩大目标。</summary>
    internal static async Task<FixedRoleReadUseMySqlFixture> CreateAsync(string mode,[CallerFilePath]string file="")
    {
        var f=new FixedRoleReadUseMySqlFixture();var root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));f.Directory=Path.Combine(root,".local/fixed-role-read-mysql",f.nonce);
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));f.options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            f.instancePin=ReadInstancePin();await PinAsync(f.options,f.instancePin);System.IO.Directory.CreateDirectory(f.Directory);var source=Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;var privateCatalog=PrivateCatalog(source);
            await File.WriteAllTextAsync(Path.Combine(f.Directory,"platform.json"),JsonSerializer.Serialize(privateCatalog,JsonDefaults.Options));File.Copy(Path.Combine(Path.GetDirectoryName(source)!,"demo.json"),Path.Combine(f.Directory,"demo.json"));
            f.Registry=new(Path.Combine(f.Directory,"platform.json"),Path.Combine(f.Directory,"demo.json"));f.generated=await CompileAsync(Path.Combine(f.Directory,"application"),privateCatalog);
            f.Session=new(f.options);f.Store=new(f.Session,f.Registry.Platform);f.Project=long.Parse(f.Registry.Platform.YzProjectId,CultureInfo.InvariantCulture);f.OtherProject=long.Parse(f.Registry.Demo.YzProjectId,CultureInfo.InvariantCulture);f.Admin=long.Parse(f.Registry.Platform.YzAdminTypes[0].Id,CultureInfo.InvariantCulture);
            // 两个既有项目/平台后台只读确认；不会创建、修改或删除它们。
            await f.RequireAnchor("yz_project",f.Project);await f.RequireAnchor("yz_project",f.OtherProject);await f.RequireAnchor("yz_admin_type",f.Admin);
            f.AccountId=Ids.NextId();await f.Absent("yz_manager",f.AccountId,f.Name("operator"));f.AuthRole=await f.Add("yz_role","auth-role",f.Project,new(){["yz_admin_type_id"]=f.Admin,["is_super"]=0,["is_built_in"]=0,["sort_no"]=10});
            var password="Metarill-fixture-"+Guid.NewGuid().ToString("N");
            await f.Add("yz_manager","operator",f.Project,new(){["password"]=PasswordHasher.Hash(password),["is_disabled"]=0,["yz_admin_type_id"]=f.Admin,["yz_role_ids"]=JsonSerializer.Serialize(new[]{Text(f.AuthRole)})},f.AccountId);
            if(mode is "manager-selection" or "export")await f.Grant(ManagerPage,mode=="export"?"is_export":"is_query");
            if(mode is "role-selection" or "export")await f.Grant(RolePage,mode=="export"?"is_export":"is_query");
            if(mode is not ("manager-selection" or "role-selection" or "export"))throw new InvalidOperationException("未声明的真实读取测试模式。");
            var sessionId=Ids.NextId();await f.Absent("sys_session",sessionId,null);var old=await f.Store.ReadIncludingDeletedAsync("sys_session",new Dictionary<string,object?>{{"account_id",f.AccountId}});Assert.True(old.Count==0,"新操作者不能已有会话。");
            var sessionOwned=new Owned("sys_session",sessionId,null,f.Project,0,new(){["account_id"]=f.AccountId,["project_id"]=f.Project,["kind"]="web"});f.owned.Add(sessionOwned);await f.Ledger("session-intent");
            f.authentication=new(f.Store,new OnlySessionId(sessionId),f.Registry,new PlatformSecuritySessionPolicy(f.Store));var login=await f.authentication.LoginAsync(f.Name("operator"),password);f.Token=login.Token;sessionOwned.Expected["token_hash"]=PasswordHasher.Digest(f.Token);sessionOwned.Fingerprint=await f.Prove(sessionOwned);
            var identity=await f.authentication.AuthenticateAsync(f.Token,null);Assert.True(identity is not null&&!identity.Actor.IsAdministrator&&!identity.IsRealmSuper,"必须建立真实普通账号会话，不能借超级身份验收。");
            await f.Samples();await f.StartHost();await f.Ledger("ready");return f;
        }
        catch{f.failed=true;await f.DisposeAsync();throw;}
    }
    /// <summary>实例标识只从显式私有pin文件读取；缺失、过大或非法时在任何连接和夹具写入前失败。</summary>
    private static InstancePin ReadInstancePin()
    {
        try{var path=Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN");if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)||new FileInfo(path).Length is <=0 or >2048)throw new InvalidOperationException();return ParseInstancePin(File.ReadAllText(path));}
        catch{throw new InvalidOperationException("本专项缺少有效的私有实例pin文件，尚未连接或创建夹具；不输出实例标识。");}
    }
    /// <summary>只接受两个固定字符串键，不允许重复、未知字段或用空pin降为仅核库名。</summary>
    internal static InstancePin ParseInstancePin(string text)
    {
        try
        {
            using var json=JsonDocument.Parse(text);if(json.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException();var fields=json.RootElement.EnumerateObject().ToArray();
            if(fields.Length!=2||fields.Select(field=>field.Name).Distinct(StringComparer.Ordinal).Count()!=2||fields.Any(field=>field.Value.ValueKind!=JsonValueKind.String||field.Name is not ("expectedHostname" or "expectedServerUuid")))throw new InvalidOperationException();
            var host=json.RootElement.GetProperty("expectedHostname").GetString()!;var uuid=json.RootElement.GetProperty("expectedServerUuid").GetString()!;
            if(!System.Text.RegularExpressions.Regex.IsMatch(host,"^[A-Za-z0-9][A-Za-z0-9.-]{0,62}$")||uuid.Length!=36||(!Guid.TryParseExact(uuid,"D",out var parsed)||parsed==Guid.Empty))throw new InvalidOperationException();return new(host,uuid);
        }
        catch{throw new InvalidOperationException("私有实例pin格式无效，尚未连接或创建夹具；字段内容不输出。");}
    }
    /// <summary>固定连接、库、hostname和UUID全部相同；不读账号口令、不输出连接详情。</summary>
    internal static async Task PinAsync(DatabaseOptions options,InstancePin pin)
    {
        try{var b=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_test"||b.Database!="metarill_test"||b.Server!="deve-server.yinzon.com"||b.Port!=3306)throw new InvalidOperationException();
            await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync()||r.GetString(0)!="metarill_test"||!string.Equals(r.GetString(1),pin.ExpectedHostname,StringComparison.Ordinal)||!string.Equals(r.GetString(2),pin.ExpectedServerUuid,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException();}
        catch{throw new InvalidOperationException("固定Server200/metarill_test实例核验失败，未建立写入夹具；连接详情不输出。");}
    }
    /// <summary>只读核对既有归属锚点，未命中时不补项目或后台。</summary>
    private async Task RequireAnchor(string table,long id){var rows=await Store.ReadAsync(table,new Dictionary<string,object?>{{"id",id}});Assert.True(rows.Count==1,"测试既有归属锚点必须唯一有效，失败不输出行。");}
    /// <summary>稳定前缀加本次nonce和固定tag，名称不来自现有行或用户输入。</summary>
    private string Name(string tag)=>"mt_role_read_"+nonce+"_"+tag;
    /// <summary>插入之前同时证明id及唯一name包括墓碑从未存在；不能认领同名既有记录。</summary>
    private async Task Absent(string table,long id,string? name)
    {var rows=await Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"id",id}});Assert.True(rows.Count==0,"随机主键必须从未存在。");if(name is not null){rows=await Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"name",name}});Assert.True(rows.Count==0,"随机名称必须从未存在。");}}
    /// <summary>样本写入先记录完整精确归属意图，读取后核id/name/creator/project及全部声明FK；不触config_*。</summary>
    private async Task<long> Add(string table,string tag,long project,Dictionary<string,object?> values,long? selectedId=null)
    {
        if(table is not ("yz_admin_type" or "yz_role" or "yz_manager" or "yz_role_permission"))throw new InvalidOperationException("夹具表不在固定allowlist。");
        var id=selectedId??Ids.NextId();var name=table=="yz_role_permission"?null:Name(tag);await Absent(table,id,name);values["id"]=id;values["yz_project_id"]=project;values["insert_manager_id"]=AccountId;values["insert_time"]=DateTime.UtcNow;if(name is not null)values["name"]=name;
        var item=new Owned(table,id,name,project,AccountId,new(values,StringComparer.Ordinal));owned.Add(item);Keys[tag]=id;await Ledger("insert-intent");await Store.InsertAsync(table,values);item.Fingerprint=await Prove(item);await Ledger("inserted");return id;
    }
    /// <summary>权限行也只归本次普通角色；使用真实认证器重新解释，不构造permission集合。</summary>
    private Task<long> Grant(string page,string action)=>Add("yz_role_permission","grant-"+page,Project,new(){["yz_role_id"]=AuthRole,["config_page_id"]=long.Parse(page,CultureInfo.InvariantCulture),["permission_values"]=JsonSerializer.Serialize(new[]{action})});
    /// <summary>故意损坏的范围和软删只存在私有样本，用来证明读取不会泄出其名称。</summary>
    private async Task Samples()
    {
        var active=await Add("yz_admin_type","active-admin",Project,new());var foreign=await Add("yz_admin_type","foreign-admin",OtherProject,new());var deleted=await Add("yz_admin_type","deleted-admin",Project,new(){["delete_no"]=DateTime.UtcNow});
        Keys["missing-admin"]=Ids.NextId();await Absent("yz_admin_type",Keys["missing-admin"],null);Keys["missing-role"]=Ids.NextId();await Absent("yz_role",Keys["missing-role"],null);
        foreach(var (tag,parent,deletedRole) in new[]{("active-role",active,false),("foreign-role",foreign,false),("deleted-role",active,true),("deleted-admin-role",deleted,false),("missing-admin-role",Keys["missing-admin"],false)})
        {var row=new Dictionary<string,object?>{{"yz_admin_type_id",parent},{"is_super",0},{"is_built_in",0},{"sort_no",10}};if(deletedRole)row["delete_no"]=DateTime.UtcNow;await Add("yz_role",tag,Project,row);}
        RoleJson=JsonSerializer.Serialize(new[]{Text(Keys["active-role"]),Text(Keys["foreign-role"]),Text(Keys["active-role"]),Text(Keys["deleted-role"]),Text(Keys["deleted-admin-role"]),Text(Keys["missing-admin-role"]),Text(Keys["missing-role"])});
        foreach(var (tag,project,roles,deletedAccount) in new[]{("array-account",Project,(string?)RoleJson,false),("null-account",Project,(string?)null,false),("empty-account",Project,(string?)"[]",false),("foreign-account",OtherProject,(string?)RoleJson,false),("deleted-account",Project,(string?)RoleJson,true)})
        {var row=new Dictionary<string,object?>{{"yz_admin_type_id",active},{"is_disabled",0},{"password",PasswordHasher.Hash("私有读取样本"+nonce)},{"yz_role_ids",roles}};if(deletedAccount)row["delete_no"]=DateTime.UtcNow;await Add("yz_manager",tag,project,row);}
    }
    /// <summary>真实产品身份中间件和Http授权配合私有生成Controller；无网络监听、不替换授权或ORM。</summary>
    private async Task StartHost()
    {
        Exports=new(new(){RootDirectory=Path.Combine(Directory,"exports")});var b=WebApplication.CreateBuilder();b.Logging.ClearProviders();b.WebHost.UseTestServer();b.Services.AddSingleton(Session);b.Services.AddSingleton(Store);b.Services.AddSingleton(Registry);b.Services.AddSingleton(authentication);b.Services.AddSingleton<AgentAuthenticationService>();b.Services.AddHttpContextAccessor();b.Services.AddScoped<HttpRuntimeAuthorization>();b.Services.AddScoped<IRuntimeAuthorization>(p=>p.GetRequiredService<HttpRuntimeAuthorization>());b.Services.AddSingleton<IIdGenerator>(Ids);b.Services.AddSingleton<IPageExportService>(Exports);
        generated.GetType(Namespace+".Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[b.Services,null]);var mvc=b.Services.AddControllers().AddJsonOptions(o=>JsonDefaults.CopyTo(o.JsonSerializerOptions));mvc.PartManager.ApplicationParts.Clear();mvc.AddApplicationPart(generated);
        app=b.Build();app.Use(async(context,next)=>{try{await next();}catch(UnauthorizedAccessException){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(R<object>.Error("读取权限拒绝。"));}catch(Exception error)when(error is ArgumentException or InvalidOperationException){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(R<object>.Error("读取合同拒绝。"));}});app.UseMiddleware<AgentAuthenticationMiddleware>();app.MapControllers();await app.StartAsync();Client=app.GetTestClient();
        Session.Orm.Aop.CommandBefore+=(_,args)=>{if(args.Command.CommandText.StartsWith("SELECT",StringComparison.OrdinalIgnoreCase))Commands.Add(new(args.Command.CommandText,args.Command.Parameters.Count));};
    }
    /// <summary>每次HTTP都以真实token建立身份；错误响应只断言静态状态，不输出body或token。</summary>
    internal async Task<JsonElement> Post(string table,string page,string action,object body,HttpStatusCode expected=HttpStatusCode.OK,bool authenticated=true)
    {
        if((table,page) is not ("yz_manager",ManagerPage) and not ("yz_role",RolePage)||action is not ("selection/selected" or "initExport"))throw new InvalidOperationException("测试只允许两个固定页面的读取入口。");
        var payload=JsonSerializer.SerializeToElement(body,JsonDefaults.Options);if(!payload.TryGetProperty("selectedIds",out var selected)||selected.ValueKind!=JsonValueKind.Array||action=="initExport"&&selected.GetArrayLength()==0)throw new InvalidOperationException("本测试必须显式提供所选ID，导出不能退全量。");
        foreach(var value in selected.EnumerateArray())if(value.ValueKind!=JsonValueKind.String||!long.TryParse(value.GetString(),NumberStyles.None,CultureInfo.InvariantCulture,out var key)||!Keys.Values.Contains(key))throw new InvalidOperationException("测试请求包含未登记源ID。");
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/admin/auto/"+table+"/"+page+"/"+action);if(authenticated)request.Headers.Add("loginStateId",Token);request.Content=JsonContent.Create(body,options:JsonDefaults.Options);using var response=await Client.SendAsync(request);Assert.True(response.StatusCode==expected,"固定读取HTTP状态不符，响应正文不输出。");using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.True(json.RootElement.GetProperty("code").GetInt32()==(expected==HttpStatusCode.OK?200:500),"固定读取R响应状态不符。");return json.RootElement.GetProperty("data").Clone();}
    /// <summary>选择/导出只能使用预登记样本ID，不允许以空条件扫描全部账号或角色。</summary>
    internal List<string> Selected(params string[] tags)=>tags.Select(tag=>Text(Keys[tag])).ToList();
    /// <summary>返回本次已登记随机名称作精确正例；不会查询任意现有记录。</summary>
    internal string DisplayName(string tag)=>Name(tag);
    /// <summary>测试断言可验证当前真实权限，但不构造或替换身份。</summary>
    internal async Task<AuthenticatedIdentity> Identity()=>await authentication.AuthenticateAsync(Token,null)??throw new InvalidOperationException("真实测试身份失效。");
    /// <summary>所有真实样本行快照只返回摘要，防止失败断言打印密码或会话字段。</summary>
    private async Task<string> Prove(Owned item)
    {
        var rows=await Store.ReadIncludingDeletedAsync(item.Table,new Dictionary<string,object?>{{"id",item.Id}});if(rows.Count!=1)throw new InvalidOperationException("自有样本必须恰好一行，不输出行内容。");var row=rows[0];
        if(item.Name is not null&&(SqlRowStore.Text(row,"name")!=item.Name||!item.Name.StartsWith("mt_role_read_"+nonce+"_",StringComparison.Ordinal)))throw new InvalidOperationException("自有样本名称不匹配。");
        if(SqlRowStore.Id(row,item.Table=="sys_session"?"project_id":"yz_project_id")!=item.Project||item.Table!="sys_session"&&SqlRowStore.Id(row,"insert_manager_id")!=item.Creator)throw new InvalidOperationException("自有样本创建者或项目不匹配。");
        foreach(var field in item.Expected.Where(field=>field.Key!="insert_time"&&field.Key!="delete_no")){if(JsonSerializer.Serialize(row.GetValueOrDefault(field.Key),JsonDefaults.Options)!=JsonSerializer.Serialize(field.Value,JsonDefaults.Options))throw new InvalidOperationException("自有样本声明字段已改变，停止清理。");}
        return Fingerprint(row);
    }
    /// <summary>全行摘要仅用于阴性与清理证明，永不把账号/会话字典传给断言或报告。</summary>
    internal static string Fingerprint(IReadOnlyDictionary<string,object?> row)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new SortedDictionary<string,object?>(row.ToDictionary(pair=>pair.Key,pair=>pair.Value),StringComparer.Ordinal),JsonDefaults.Options)));
    /// <summary>就绪后全图应保持不变；清理前验证所有包含墓碑的自有行，未知变化不认领或泛删。</summary>
    internal async Task AssertOwnedUnchanged()
    {foreach(var item in owned)if(item.Fingerprint is not null&&await Prove(item)!=item.Fingerprint)throw new InvalidOperationException("读取测试改变了自有样本，保留现场。");}
    /// <summary>私有清理前核依赖仅落在预登记ID；这里只选ID，不读取共享账号密码。</summary>
    private async Task RejectUnknownReferences(DbConnection connection,DbTransaction transaction)
    {
        var ids=owned.Select(item=>(item.Table,item.Id)).ToHashSet();
        foreach(var item in owned.Where(item=>item.Table is "yz_admin_type" or "yz_role" or "yz_manager"))
        {
            var refs=item.Table switch{"yz_admin_type"=>new[]{("yz_role","yz_admin_type_id"),("config_menu","yz_admin_type_id"),("yz_manager","yz_admin_type_id")},"yz_role"=>new[]{("yz_role_permission","yz_role_id")},_=>new[]{("sys_session","account_id")}};
            foreach(var(table,field)in refs){await using var q=connection.CreateCommand();q.Transaction=transaction;q.CommandText="SELECT `id` FROM `"+table+"` WHERE `"+field+"`=@id ORDER BY `id` LIMIT 1001 FOR UPDATE";Add(q,"id",item.Id);await using var reader=await q.ExecuteReaderAsync();while(await reader.ReadAsync())if(!ids.Contains((table,Convert.ToInt64(reader.GetValue(0),CultureInfo.InvariantCulture))))throw new InvalidOperationException("存在非本次登记引用，停止清理。");}
            if(item.Table!="yz_role")continue;
            // 前置只有项目/自有行/FK的current锁读；此处首次普通SELECT取得锁后新RR快照，不对共享账号做全表锁。
            // 正式同项目写入口仍被两个项目聚合锁串行；不宣称能协调未知直接SQL绕过该锁的写入。
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT `id` FROM `yz_manager` WHERE CASE WHEN JSON_VALID(`yz_role_ids`) THEN JSON_CONTAINS(`yz_role_ids`,@text) OR JSON_CONTAINS(`yz_role_ids`,@number) ELSE 0 END ORDER BY `id` LIMIT 1001";Add(command,"text",JsonSerializer.Serialize(Text(item.Id)));Add(command,"number",Text(item.Id));await using var found=await command.ExecuteReaderAsync();while(await found.ReadAsync())if(!ids.Contains(("yz_manager",Convert.ToInt64(found.GetValue(0),CultureInfo.InvariantCulture))))throw new InvalidOperationException("角色存在未登记账号引用，停止清理。");
        }
    }
    /// <summary>实际成功时显式收口；Dispose不因测试失败而把未知现场自动清掉。</summary>
    internal async Task Complete()
    {
        await AssertOwnedUnchanged();
        var login=owned.Single(item=>item.Table=="sys_session");var beforeRows=await Store.ReadIncludingDeletedAsync("sys_session",new Dictionary<string,object?>{{"id",login.Id}});if(beforeRows.Count!=1)throw new InvalidOperationException("自有会话注销前必须唯一。");var before=beforeRows[0];
        await authentication.LogoutAsync(login.Id);var afterRows=await Store.ReadIncludingDeletedAsync("sys_session",new Dictionary<string,object?>{{"id",login.Id}});if(afterRows.Count!=1)throw new InvalidOperationException("自有会话注销后必须唯一。");var after=afterRows[0];
        before.Remove("delete_no");var stable=new Dictionary<string,object?>(after,StringComparer.Ordinal);stable.Remove("delete_no");
        if(after.GetValueOrDefault("delete_no") is null||Fingerprint(before)!=Fingerprint(stable)||await authentication.AuthenticateAsync(Token,null) is not null)throw new InvalidOperationException("自有会话注销未保持声明边界，停止清理。");
        login.Fingerprint=await Prove(login);await Ledger("logged-out");await Cleanup();completed=true;await Ledger("completed");
    }
    /// <summary>仅按精确id/name/creator/project或本会话hash删除自有记录；一事务内保墓碑零残留证明。</summary>
    private async Task Cleanup()
    {
        await PinAsync(options,instancePin);await AssertOwnedUnchanged();await using var connection=Session.Provider.CreateConnection(options);await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();
        try
        {
            // 先与正式写协调器按项目串行：项目升序→自有后台升序→其它自有行→当前引用读。
            foreach(var project in new[]{Project,OtherProject}.Distinct().Order())
            {
                await using var projectLock=connection.CreateCommand();projectLock.Transaction=transaction;projectLock.CommandText="SELECT `id` FROM `yz_project` WHERE `id`=@id AND `delete_no` IS NULL FOR UPDATE";Add(projectLock,"id",project);await using var result=await projectLock.ExecuteReaderAsync();
                if(!await result.ReadAsync()||Convert.ToInt64(result.GetValue(0),CultureInfo.InvariantCulture)!=project||await result.ReadAsync())throw new InvalidOperationException("清理项目锁必须命中唯一有效既有项目。");
            }
            foreach(var item in owned.Where(item=>item.Table=="yz_admin_type").OrderBy(item=>item.Id))await RequireLockedOwned(connection,transaction,item);
            foreach(var item in owned.Where(item=>item.Table!="yz_admin_type").OrderBy(item=>item.Table,StringComparer.Ordinal).ThenBy(item=>item.Id))await RequireLockedOwned(connection,transaction,item);
            await RejectUnknownReferences(connection,transaction);
            foreach(var item in owned.OrderBy(item=>item.Table switch{"sys_session"=>0,"yz_role_permission"=>1,"yz_manager"=>2,"yz_role"=>3,_=>4}).ThenBy(item=>item.Id))
            {
                await using var command=connection.CreateCommand();command.Transaction=transaction;
                command.CommandText="DELETE FROM `"+item.Table+"` WHERE `id`=@id AND `"+(item.Table=="sys_session"?"project_id":"yz_project_id")+"`=@project";Add(command,"id",item.Id);Add(command,"project",item.Project);
                if(item.Table=="sys_session"){if(!item.Expected.TryGetValue("token_hash",out var hash))throw new InvalidOperationException("会话未知结果不能清理。");command.CommandText+=" AND `account_id`=@creator AND `kind`='web' AND `token_hash`=@hash";Add(command,"creator",AccountId);Add(command,"hash",hash!);}
                else{command.CommandText+=" AND `insert_manager_id`=@creator";Add(command,"creator",item.Creator);if(item.Name is not null){command.CommandText+=" AND `name`=@name";Add(command,"name",item.Name);}}
                if(await command.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("自有样本精确删除数量不符，事务回滚并保留现场。");
            }
            await transaction.CommitAsync();
        }
        catch{await transaction.RollbackAsync();throw;}
        foreach(var item in owned){var rows=await Store.ReadIncludingDeletedAsync(item.Table,new Dictionary<string,object?>{{"id",item.Id}});Assert.True(rows.Count==0,"自有行含墓碑必须零残留，失败不输出行。");if(item.Name is not null){rows=await Store.ReadIncludingDeletedAsync(item.Table,new Dictionary<string,object?>{{"name",item.Name}});Assert.True(rows.Count==0,"自有唯一名称清理后必须零残留。");}cleaned++;}
    }
    /// <summary>包含墓碑锁定精确自有行，锁后重验完整指纹；此时还未执行任何物理删除。</summary>
    private async Task RequireLockedOwned(DbConnection connection,DbTransaction transaction,Owned item)
    {
                // 包含软删行；锁定后复核全行指纹，禁止首次校验后外部编辑被清理覆盖。
                await using(var check=connection.CreateCommand()){check.Transaction=transaction;check.CommandText="SELECT "+string.Join(",",Registry.Platform.ConfigColumns.Where(column=>column.ConfigTableId==Registry.Platform.ConfigTables.Single(table=>table.TableName==item.Table).Id&&column.IsVirtualColumn==0).Select(column=>"`"+column.ColumnName+"`"))+" FROM `"+item.Table+"` WHERE `id`=@id FOR UPDATE";Add(check,"id",item.Id);await using var reader=await check.ExecuteReaderAsync();if(!await reader.ReadAsync())throw new InvalidOperationException("清理锁定的自有行已丢失。");var row=new Dictionary<string,object?>();for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=await reader.IsDBNullAsync(i)?null:reader.GetValue(i);if(item.Fingerprint is null||Fingerprint(row)!=item.Fingerprint||await reader.ReadAsync())throw new InvalidOperationException("清理锁后样本指纹不符，保留现场。");}
    }
    /// <summary>固定参数绑定，不允许将任何外部值拼到表或列名。</summary>
    private static void Add(DbCommand command,string name,object value){var p=command.CreateParameter();p.ParameterName=name;p.Value=value;command.Parameters.Add(p);}
    /// <summary>恢复收据只写明确的非秘密归属FK，不序列化整份Expected或permission/凭据字段。</summary>
    private static Dictionary<string,string?> SafeForeignKeys(Owned item)=>new[]{"yz_admin_type_id","yz_role_id","config_page_id","account_id","project_id"}.Where(item.Expected.ContainsKey).ToDictionary(key=>key,key=>Convert.ToString(item.Expected[key],CultureInfo.InvariantCulture),StringComparer.Ordinal);
    /// <summary>角色ID是本次公开归属事实；密码、盐、token及token_hash永远不进入收据。</summary>
    private static string[]? SafeRoleIds(Owned item)=>item.Expected.TryGetValue("yz_role_ids",out var value)&&value is string text?RoleCheckboxValue.Parse(text).Select(Text).ToArray():null;
    /// <summary>只持久化非敏感归属/数量状态；不保存连接、密码、原token、token_hash或账号字典；全行指纹另行标注。</summary>
    private async Task Ledger(string stage)
    {if(string.IsNullOrEmpty(Directory))return;System.IO.Directory.CreateDirectory(Directory);var path=Path.Combine(Directory,"receipt-"+stage+"-"+Guid.NewGuid().ToString("N")+".json");await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);await JsonSerializer.SerializeAsync(stream,new{stage,nonce,rows=owned.Select(item=>new{item.Table,item.Id,item.Name,item.Project,item.Creator,fullRowFingerprint=item.Fingerprint,foreignKeys=SafeForeignKeys(item),roleIds=SafeRoleIds(item)}),reserved=Keys.Where(key=>!owned.Any(item=>item.Id==key.Value)).ToDictionary(key=>key.Key,key=>key.Value),cleaned,completed,failed},JsonDefaults.Options);await stream.FlushAsync();stream.Flush(true);}
    /// <summary>只处理自己启动的TestServer与依赖；失败保自有图和receipt，绝不泛删已有行或重试业务。</summary>
    public async ValueTask DisposeAsync()
    {Client?.Dispose();if(app is not null)await app.DisposeAsync();if(!completed&&owned.Count>0){failed=true;await Ledger("preserved");}Session?.Dispose();}
    /// <summary>长ID按整数文化输出，不经过浮点数。</summary>
    internal static string Text(long id)=>id.ToString(CultureInfo.InvariantCulture);
    /// <summary>只有预登记的会话ID能由真实认证器分配，重复登录明确失败。</summary>
    private sealed class OnlySessionId(long id):IIdGenerator{private int used;public long NextId()=>Interlocked.Increment(ref used)==1?id:throw new InvalidOperationException("本夹具只允许一个预登记登录会话。");}
}
