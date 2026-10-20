using System.Text.Json;
using ModelContextProtocol.AspNetCore;
using Metarill.Api.Mcp;
using Metarill.Api.GenerationAssistant;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var initialize=args.Contains("--initialize");
var repairSeed=args.Contains("--synchronize-development-seed");
var builder=WebApplication.CreateBuilder(args.Where(a=>a!="--initialize"&&a!="--synchronize-development-seed").ToArray());
var root=FindRoot(builder.Environment.ContentRootPath);
var settings=Environment.GetEnvironmentVariable("METARILL_SETTINGS")??Path.Combine(root,".local","development.settings.json");
builder.Configuration.AddJsonFile(settings,optional:true,reloadOnChange:false).AddEnvironmentVariables();
if(string.IsNullOrEmpty(builder.Configuration["urls"]))builder.WebHost.UseUrls("http://127.0.0.1:8088");
var registry=new MetadataRegistry(Path.Combine(root,"backend","metadata","platform.json"),Path.Combine(root,"backend","metadata","demo.json"));
var platformOptions=builder.Configuration.GetSection("Metarill:Database").Get<DatabaseOptions>()??throw new InvalidOperationException("请配置 Metarill:Database。");
var businessOptions=builder.Configuration.GetSection("Metarill:BusinessDatabase").Get<DatabaseOptions>()??throw new InvalidOperationException("请配置 Metarill:BusinessDatabase。");
builder.Services.AddSingleton(registry);
builder.Services.AddSingleton<ISecurityResourceRegistry>(sp=>sp.GetRequiredService<MetadataRegistry>());
builder.Services.AddSingleton<ISecuritySessionPolicy,PlatformSecuritySessionPolicy>();
builder.Services.AddSingleton(_=>new TargetDatabaseSessions(platformOptions,businessOptions,registry,builder.Configuration.GetSection("Metarill:ProjectDatabases").Get<Dictionary<string,DatabaseOptions>>()));
builder.Services.AddSingleton<DatabaseSession>(sp=>sp.GetRequiredService<TargetDatabaseSessions>().Platform);
builder.Services.AddSingleton(sp=>new SqlRowStore(sp.GetRequiredService<DatabaseSession>(),registry.Platform));
builder.Services.AddSingleton(sp=>new ProjectFileRuntimes(sp.GetRequiredService<TargetDatabaseSessions>(),registry,
    MetadataRegistry.Load(Path.Combine(root,"backend","metadata","files.json")),sp.GetRequiredService<FileLibraryOptions>()));
builder.Services.AddSingleton<MetadataRepository>();
builder.Services.AddSingleton<DevelopmentSeedSynchronizer>();
builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(builder.Configuration.GetValue("Metarill:WorkerId",1)));
builder.Services.AddSingleton<ISchemaChangeJournal>(new FileSchemaChangeJournal(Path.Combine(root,".local","schema-history.jsonl")));
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddSingleton<ProjectSchemaService>();
builder.Services.AddSingleton<BootstrapService>();
builder.Services.AddSingleton<AuthenticationService>();
builder.Services.AddSingleton<CSharpGenerator>();
builder.Services.AddSingleton<ApplicationPackager>();
builder.Services.AddSingleton(new GenerationAssistantOptions(Path.Combine(root, ".local", "generation-assistant")));
builder.Services.AddSingleton<GenerationAssistantQueue>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Metarill:Agent").Get<AgentOAuthOptions>()??new AgentOAuthOptions{VerificationUri="http://127.0.0.1:5188/#/pages/custom/agent_authorize"});
builder.Services.AddSingleton<AgentOAuthService>();
builder.Services.AddSingleton<AgentAuthenticationService>();
builder.Services.AddSingleton<AgentPublicRateLimiter>();
builder.Services.AddSingleton<GeneratedCapabilityIndex>();
builder.Services.AddScoped<AgentCapabilityCatalog>();
builder.Services.AddSingleton(new AgentTaskOptions());
builder.Services.AddSingleton<AgentTaskStore>();
builder.Services.AddScoped<CompatibilityButtonBusiness>();
builder.Services.AddScoped<ConfigurationActionsBusiness>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithHttpTransport(options=>options.SessionMode=HttpServerSessionMode.Stateless).WithTools<PlatformTools>();
builder.Services.AddScoped<HttpRuntimeAuthorization>();
builder.Services.AddScoped<IRuntimeAuthorization>(sp=>sp.GetRequiredService<HttpRuntimeAuthorization>());
builder.Services.AddScoped<MenuWorkbenchBusiness>();
builder.Services.AddScoped<RolePermissionBusiness>();
builder.Services.AddRealmWriteCoordination();
builder.Services.AddScoped<ConfigurationCandidatesBusiness>();
builder.Services.AddScoped<ProjectSchemaInspectionBusiness>();
builder.Services.AddScoped<IRecordMutationObserver,ConfigurationMutationObserver>();
builder.Services.AddScoped<ConfigurationMutationObserver>();
builder.Services.AddScoped<MetadataWorkbenchBusiness>();
builder.Services.AddScoped<IndexWorkbenchBusiness>();
builder.Services.AddSingleton(new FileLibraryOptions{RootDirectory=Path.Combine(root,".local","files"),LegacyPublicBaseUrl=builder.Configuration["Metarill:Files:LegacyPublicBaseUrl"]});
builder.Services.AddScoped<IFileLibraryResolver,ProjectFileLibraryResolver>();
builder.Services.AddSingleton(new ExportOptions{RootDirectory=Path.Combine(root,".local","exports")});
builder.Services.AddSingleton<ExcelPageExportService>();
builder.Services.AddSingleton<IPageExportService>(sp=>sp.GetRequiredService<ExcelPageExportService>());
builder.Services.AddHostedService<ExportCleanupWorker>();
builder.Services.AddScoped<IRecordMutationObserver,ProjectFileReferenceMutationObserver>();
Metarill.Platform.Generated.GeneratedRegistration.AddGeneratedServices(builder.Services);
Metarill.Demo.Generated.GeneratedRegistration.AddGeneratedServices(builder.Services,sp=>sp.GetRequiredService<TargetDatabaseSessions>().Business);
builder.Services.AddControllers().AddApplicationPart(typeof(Metarill.Api.Controllers.FileLibraryController).Assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options=>options.InvalidModelStateResponseFactory=_=>new Microsoft.AspNetCore.Mvc.OkObjectResult(R<object>.Error("请求字段类型、字段名或必填项不正确。")));
builder.Services.ConfigureHttpJsonOptions(options=>JsonDefaults.CopyTo(options.SerializerOptions));
builder.Services.AddCors(options=>options.AddDefaultPolicy(policy=>policy.SetIsOriginAllowed(origin=>Uri.TryCreate(origin,UriKind.Absolute,out var uri)&&uri.IsLoopback).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options=>options.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"local",_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0})));
var app=builder.Build();
if(repairSeed)
{
    var backup=await app.Services.GetRequiredService<DevelopmentSeedSynchronizer>().SynchronizeAsync(Path.Combine(root,".local","seed-backups"));
    Console.WriteLine("开发内置元数据已校正，备份："+backup);return;
}
if(initialize)
{
    await app.Services.GetRequiredService<BootstrapService>().InitializeAsync(builder.Configuration["Metarill:Bootstrap:Username"]??"",builder.Configuration["Metarill:Bootstrap:Password"]??"",businessOptions);
    Console.WriteLine("平台与独立业务库初始化完成；重复执行不会重置管理员密码。");return;
}
app.UseRouting();app.UseCors();app.UseRateLimiter();
app.Use(async(context,next)=>
{
    try
    {
        await next(context);
    }
    catch(SchemaApplyException exception)
    {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=200;var response=R<object>.Error(exception.Message);response.Map["result"]=exception.Result;
        await context.Response.WriteAsJsonAsync(response,context.RequestAborted);
    }
    catch(UnauthorizedAccessException exception)
    {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=context.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)||context.Request.Path.StartsWithSegments("/api/mcp")?401:200;
        await context.Response.WriteAsJsonAsync(R<object>.Error(exception.Message=="登录超时"?"登录超时":"无权执行此操作。"),context.RequestAborted);
    }
    catch(PageImportException exception)
    {
        if(context.Response.HasStarted)throw;
        // 仅固定导入输入分类可展示；内部执行失败保留异常链及追踪标识，不输出原异常正文。
        var message=exception.Message;
        if(!exception.IsInputError)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Metarill.Api").LogError(exception,"导入执行失败，追踪标识 {TraceId}",context.TraceIdentifier);
            message="操作失败，请查看服务端日志。";
        }
        context.Response.StatusCode=200;await context.Response.WriteAsJsonAsync(R<object>.Error(message),context.RequestAborted);
    }
    catch(Exception exception)when(exception is InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
    {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=200;await context.Response.WriteAsJsonAsync(R<object>.Error(exception.Message),context.RequestAborted);
    }
    catch(Exception exception)when(exception is not OperationCanceledException)
    {
        if(context.Response.HasStarted)throw;
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Metarill.Api").LogError(exception,"操作失败，追踪标识 {TraceId}",context.TraceIdentifier);
        context.Response.StatusCode=200;await context.Response.WriteAsJsonAsync(R<object>.Error("操作失败，请查看服务端日志。"),context.RequestAborted);
    }
});
app.UseMiddleware<AgentAuthenticationMiddleware>();
app.UseMiddleware<AgentTaskMiddleware>();
app.Use(async(context,next)=>
{
    if(context.Request.Path.StartsWithSegments("/api/mcp"))context.RequestServices.GetRequiredService<HttpRuntimeAuthorization>().RequireActor();
    await next(context);
});
app.MapGet("/api/runtime/health/check",()=>R<object>.Success(new{application="Metarill",backend="csharp",databaseProviders=DatabaseProviders.Capabilities}));
app.MapControllers();
app.MapMcp("/api/mcp");
await app.RunAsync();

static string FindRoot(string start)
{
    for(var current=new DirectoryInfo(start);current is not null;current=current.Parent)
        if(File.Exists(Path.Combine(current.FullName,"global.json")))return current.FullName;
    throw new InvalidOperationException("无法定位 Metarill 根目录。");
}
public partial class Program;
