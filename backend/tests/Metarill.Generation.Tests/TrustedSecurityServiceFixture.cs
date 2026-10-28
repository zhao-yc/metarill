using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际编译可信生成物；仅替代ADO/ORM数据传输，不替代认证、协调器或归属决策。</summary>
public sealed class TrustedSecurityServiceFixture:IAsyncLifetime
{
    internal const string Namespace="Example.TrustedSecurity";
    internal readonly string DirectoryPath=Path.Combine(Path.GetTempPath(),"metarill-trusted-security",Guid.NewGuid().ToString("N"));
    internal SecurityManagementModelSet Models=null!;internal MetadataCatalog Catalog=null!;internal Assembly Assembly=null!;internal byte[] CatalogBytes=[];
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    internal static string SourceRoot=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
    /// <summary>仅从本clone已提交公开模型和内部factory建立模块，整个过程不连接数据库。</summary>
    public async Task InitializeAsync()
    {
        var app=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot,"metadata/demo.json")),Json)!;
        Models=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id);Catalog=SecurityManagementModuleFactory.RequirePublishedForGeneration(Models);
        new CSharpGenerator().Generate(Models,Path.Combine(DirectoryPath,"generated"),Namespace);
        CatalogBytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Catalog,Json)+"\n");
        System.IO.Directory.CreateDirectory(Path.Combine(DirectoryPath,"host"));
        foreach(var name in new[]{"PackageSecurityCatalog.cs","WebAuthentication.cs","SessionPolicy.cs"})
            File.WriteAllText(Path.Combine(DirectoryPath,"host",name),File.ReadAllText(Path.Combine(SourceRoot,"templates/standalone",name+".tpl")).Replace("{{NAMESPACE}}",Namespace));
        File.WriteAllText(Path.Combine(DirectoryPath,"host/PackageSecurityCatalogBinding.g.cs"),ApplicationPackager.SecurityCatalogBindingSource(Namespace,Convert.ToHexStringLower(SHA256.HashData(CatalogBytes))));
        var result=await Build(DirectoryPath);Assert.True(result.ExitCode==0,result.Output);Assembly=AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(result.AssemblyPath)));
    }
    /// <summary>构建只引用本cloneCore/Data/Security，不启动Main，门闩覆盖真实子进程及输出排空。</summary>
    internal static async Task<(int ExitCode,string Output,string AssemblyPath)> Build(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);var name="TrustedSecurity"+Guid.NewGuid().ToString("N");
        var references=string.Join("",new[]{"Core","Data","Security"}.Select(project=>"<ProjectReference Include=\""+SecurityElement.Escape(Path.Combine(SourceRoot,"src/Metarill."+project,"Metarill."+project+".csproj"))+"\"/>"));
        File.WriteAllText(Path.Combine(directory,"Trusted.csproj"),"<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>"+name+"</AssemblyName></PropertyGroup><ItemGroup>"+references+"</ItemGroup></Project>");
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"build","Trusted.csproj","--nologo","--verbosity","quiet","--disable-build-servers","-warnaserror"})start.ArgumentList.Add(arg);
        using(await SharedSourceBuildGate.Shared.EnterAsync())
        {
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try{await process.WaitForExitAsync(timeout.Token);}catch(Exception failure){await SharedSourceBuildGate.StopOwnProcessAsync(process,output,error,failure);throw;}
            return(process.ExitCode,await output+await error,Path.Combine(directory,"bin/Debug/net10.0",name+".dll"));
        }
    }
    internal Type Type(string name)=>Assembly.GetType(Namespace+"."+name,true)!;
    /// <summary>只解开反射边界，保留实际产品异常类型与栈。</summary>
    internal static object? Invoke(object target,string method,params object?[] args)
    {
        var type=target as Type??target.GetType();try{return type.GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)!.Invoke(target is Type?null:target,args);}
        catch(TargetInvocationException failure)when(failure.InnerException is not null){ExceptionDispatchInfo.Capture(failure.InnerException).Throw();throw;}
    }
    public Task DisposeAsync()=>TestDirectoryCleanup.DeleteAsync(DirectoryPath,Path.Combine(Path.GetTempPath(),"metarill-trusted-security"));
}

/// <summary>真实认证与当前写协调使用同一Session；内存层仅执行固定参数化SQL和强类型表达式。</summary>
internal sealed class TrustedSecurityServiceRun:IAsyncDisposable
{
    internal readonly TrustedSecurityMemoryProvider Transport=new();internal readonly DatabaseSession Session;internal readonly ServiceProvider Services;internal readonly IServiceScope Scope;
    internal readonly SqlRowStore Store;internal AuthenticatedIdentity Identity {get;private set;}=null!;internal readonly TrustedSecurityServiceFixture Fixture;internal readonly List<string> Writes=[];
    private readonly FixedHttpContextAccessor accessor=new(){HttpContext=new DefaultHttpContext()};
    internal const string Password="可信模块测试密码-12345";
    internal static async Task<TrustedSecurityServiceRun> Create(TrustedSecurityServiceFixture fixture)=>await Initialize(new(fixture));
    private TrustedSecurityServiceRun(TrustedSecurityServiceFixture fixture)
    {
        Fixture=fixture;var orm=Proxy<IFreeSql>((method,args)=>method.Name switch{"Select"=>Select(method.GetGenericArguments()[0]),"Insert" or "Update" or "Delete"=>Mutation(method.ReturnType,method.GetGenericArguments()[0],method.Name),"Dispose"=>null,_=>throw new NotSupportedException(method.Name)});
        Session=new(new(){ExpectedDatabase="offline",ConnectionString="offline"},orm,Transport);Store=new(Session,fixture.Models.Physical);
        var project=long.Parse(fixture.Catalog.YzProjectId);var admin=long.Parse(fixture.Models.Binding.ManagementAdminTypeId);
        Add("yz_project",("id",project),("name","离线固定项目"));Add("yz_admin_type",("id",admin),("yz_project_id",project),("name","离线管理后台"));
        Add("yz_role",("id",10L),("name","普通编辑角色"),("yz_admin_type_id",admin),("yz_project_id",project),("is_super",0),("permission_mode","RESOURCE"));
        Add("yz_manager",("id",8L),("name","trusted-editor"),("yz_admin_type_id",admin),("yz_project_id",project),("is_disabled",0),("yz_role_ids","[\"10\"]"));
        Add("sys_session",("id",20L),("account_id",8L),("token_hash",PasswordHasher.Digest(new string('a',64))),("kind","web"),("project_id",project),("expires_at",DateTime.UtcNow.AddHours(1)));
        var id=30L;foreach(var page in fixture.Catalog.ConfigPages.Where(page=>page.Id is "6031906822946816" or "1692532858529329154" or "1696799309006348290"))Add("yz_role_permission",("id",id++),("yz_role_id",10L),("config_page_id",page.Id),("permission_values","[\"is_all\"]"));
        var authority=Activator.CreateInstance(fixture.Type("Host.VerifiedPackageSecurityCatalog"),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[fixture.CatalogBytes.ToArray()],null)!;
        var registry=Activator.CreateInstance(fixture.Type("Host.ApplicationSecurityRegistry"),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[authority],null)!;
        var registrations=new ServiceCollection();registrations.AddSingleton(Session);registrations.AddSingleton(Store);registrations.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(881));registrations.AddSingleton<IHttpContextAccessor>(accessor);registrations.AddSingleton((ISecurityResourceRegistry)registry);
        TrustedSecurityServiceFixture.Invoke(fixture.Type("Host.ApplicationSecurityCatalogRegistration"),"Add",registrations,authority);
        registrations.AddScoped<IRuntimeAuthorization>(_=> (IRuntimeAuthorization)Activator.CreateInstance(fixture.Type("Host.ApplicationWebAuthorization"),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[accessor,authority],null)!);
        registrations.AddScoped<ISecurityIdentityAccessor>(provider=>(ISecurityIdentityAccessor)provider.GetRequiredService<IRuntimeAuthorization>());
        registrations.AddScoped<ISecurityCurrentSessionPolicy>(_ =>(ISecurityCurrentSessionPolicy)Activator.CreateInstance(fixture.Type("Host.ApplicationSecurityCurrentSessionPolicy"),Store)!);
        registrations.AddScoped<SecurityRealmCurrentReader>();registrations.AddScoped<SecurityRealmMutationGuard>();registrations.AddScoped<IRecordMutationGuard>(provider=>provider.GetRequiredService<SecurityRealmMutationGuard>());
        TrustedSecurityServiceFixture.Invoke(fixture.Type("Generated.GeneratedRegistration"),"AddGeneratedServices",registrations,null);
        Services=registrations.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true});Scope=Services.CreateScope();
        // 身份由真实AuthenticationService从受控会话行取得；不在此模拟授权结论。
    }
    private static async Task<TrustedSecurityServiceRun> Initialize(TrustedSecurityServiceRun run)
    {
        var registry=(ISecurityResourceRegistry)run.Services.GetRequiredService<ISecurityResourceRegistry>();var auth=new AuthenticationService(run.Store,new SnowflakeIdGenerator(881),registry,(ISecuritySessionPolicy)registry);
        var identity=await auth.AuthenticateAsync(new string('a',64),run.Fixture.Catalog.YzProjectId)??throw new InvalidOperationException("离线真实认证未建立。");
        run.accessor.HttpContext!.Items[typeof(AuthenticatedIdentity)]=identity;run.Identity=identity;return run;
    }
    internal void Add(string table,params(string Name,object? Value)[] values){if(!Transport.Rows.TryGetValue(table,out var rows))Transport.Rows[table]=rows=[];rows.Add(values.ToDictionary(item=>item.Name,item=>item.Value));}
    internal object Service(string name)=>Scope.ServiceProvider.GetRequiredService(Fixture.Type("Generated.Services."+name+"Service"));
    /// <summary>只供错误DI及快照探针，mapper/Session/授权仍来自相同实际生成程序集和scope。</summary>
    internal object Construct(string name,IEnumerable<IRecordMutationGuard> guards)
    {
        var type=Fixture.Type("Generated.Services."+name+"Service");var mapper=Activator.CreateInstance(Fixture.Type("Generated.Mappers."+name+"Mapper"),Session)!;
        var args=new List<object?>{Session,mapper,new SnowflakeIdGenerator(881),Array.Empty<IRecordMutationObserver>(),guards};if(type.GetConstructors().Single().GetParameters().Length==6)args.Add(Scope.ServiceProvider.GetRequiredService<IRuntimeAuthorization>());
        try{return Activator.CreateInstance(type,args.ToArray())!;}catch(TargetInvocationException failure)when(failure.InnerException is not null){ExceptionDispatchInfo.Capture(failure.InnerException).Throw();throw;}
    }

    /// <summary>按物理投影完整还原已有行，数据库null不能保留Entity默认初始化值。</summary>
    internal object Existing(string name,long id)=>Rows(Fixture.Type("Generated.Entities."+name)).Single(row=>(long)row.GetType().GetProperty("Id")!.GetValue(row)! == id);
    internal object Entity(string name,long id)=>Activator.CreateInstance(Fixture.Type("Generated.Entities."+name))! is{} entity?WithId(entity,id):throw new InvalidOperationException();
    private static object WithId(object entity,long id){entity.GetType().GetProperty("Id")!.SetValue(entity,id);return entity;}
    internal object Request(string name,string page,string operation,string json){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new StringLongJsonConverter());return JsonSerializer.Deserialize(json,Fixture.Type("Generated.DTOs."+name+"Page"+page+operation+"Request"),options)!;}
    internal async Task<object?> Call(object service,string method,params object?[] args){var task=(Task)TrustedSecurityServiceFixture.Invoke(service,method,args)!;await task;return task.GetType().GetProperty("Result")?.GetValue(task);}
    internal string Fingerprint()=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Transport.Rows)));
    public ValueTask DisposeAsync(){Scope.Dispose();Services.Dispose();Session.Dispose();return ValueTask.CompletedTask;}

    /// <summary>表达式按真实行执行；不把关联Count一律返回成功。</summary>
    private object Select(Type entityType)
    {
        var predicates=new List<LambdaExpression>();object query=null!;query=Proxy(typeof(ISelect<>).MakeGenericType(entityType),(method,args)=>
        {
            if(method.Name=="Where"||method.Name=="WhereIf"){if(args!.FirstOrDefault() is not bool enabled||enabled)predicates.AddRange(args!.OfType<LambdaExpression>());return query;}
            var rows=Rows(entityType).Where(row=>predicates.All(predicate=>(bool)predicate.Compile().DynamicInvoke(row)!)).ToArray();
            if(method.Name=="Any")return rows.Length>0;if(method.Name=="CountAsync")return Task.FromResult((long)rows.Length);
            if(method.Name is "FirstAsync" or "ToOneAsync")
            {
                // 原范围过滤后再投影，空结果保持null；保存锁读没有投影时仍返回完整实体。
                var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var row=rows.FirstOrDefault();
                return TaskValue(projection?.ReturnType??entityType,row is null||projection is null?row:projection.Compile().DynamicInvoke(row));
            }
            if(method.Name=="ToListAsync")
            {
                var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var type=projection?.ReturnType??entityType;var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type))!;foreach(var row in rows)list.Add(projection is null?row:projection.Compile().DynamicInvoke(row));return TaskValue(list.GetType(),list);
            }
            return query;
        });return query;
    }
    private object Mutation(Type commandType,Type entityType,string kind)
    {
        object? entity=null;string[]? columns=null;object command=null!;command=Proxy(commandType,(method,args)=>
        {
            if(method.Name is "AppendData" or "SetSource"){entity=args![0] is IEnumerable values && args[0] is not string?values.Cast<object>().First():args[0];return command;}
            if(method.Name=="UpdateColumns"){columns=(string[])args![0]!;return command;}
            if(method.Name=="ExecuteAffrowsAsync")
            {
                if(Session.CurrentTransaction is null)throw new InvalidOperationException("测试发现Mapper事务外写入");var table=Fixture.Catalog.ConfigTables.Single(item=>GenerationModel.Pascal(item.TableName)==entityType.Name);var all=Fixture.Catalog.ConfigColumns.Where(item=>item.ConfigTableId==table.Id).ToArray();
                if(entity is null)throw new InvalidOperationException("Mapper缺少实体");var id=(long)entityType.GetProperty("Id")!.GetValue(entity)!;if(!Transport.Rows.TryGetValue(table.TableName,out var data))Transport.Rows[table.TableName]=data=[];
                var row=kind=="Insert"?new Dictionary<string,object?>():data.Single(item=>Convert.ToInt64(item["id"])==id);foreach(var column in all.Where(column=>kind=="Insert"||columns!.Contains(column.ColumnName)))row[column.ColumnName]=entityType.GetProperty(GenerationModel.Pascal(column.ColumnName))!.GetValue(entity);
                if(kind=="Insert")data.Add(row);Writes.Add(kind+":"+table.TableName);return Task.FromResult(1);
            }
            return command;
        });return command;
    }
    private IEnumerable<object> Rows(Type type)
    {
        var table=Fixture.Catalog.ConfigTables.Single(item=>GenerationModel.Pascal(item.TableName)==type.Name);foreach(var row in Transport.Rows.GetValueOrDefault(table.TableName)??[])
        {
            var entity=Activator.CreateInstance(type)!;foreach(var column in Fixture.Catalog.ConfigColumns.Where(item=>item.ConfigTableId==table.Id))type.GetProperty(GenerationModel.Pascal(column.ColumnName))!.SetValue(entity,row.GetValueOrDefault(column.ColumnName));yield return entity;
        }
    }
    private static object TaskValue(Type type,object? value)=>typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value])!;
    private static T Proxy<T>(Func<MethodInfo,object?[]?,object?> action)where T:class=>(T)Proxy(typeof(T),action);
    private static object Proxy(Type type,Func<MethodInfo,object?[]?,object?> action){var proxy=DispatchProxy.Create(type,typeof(OrmProxy));((OrmProxy)proxy).Action=action;return proxy;}
    /// <summary>直接Service夹具使用实例身份上下文，避免AsyncLocal在异步建立身份后回到调用者时丢失。</summary>
    private sealed class FixedHttpContextAccessor:IHttpContextAccessor{public HttpContext? HttpContext {get;set;}}
    public class OrmProxy:DispatchProxy{internal Func<MethodInfo,object?[]?,object?> Action=null!;protected override object? Invoke(MethodInfo? method,object?[]? args)=>Action(method!,args);}
}
