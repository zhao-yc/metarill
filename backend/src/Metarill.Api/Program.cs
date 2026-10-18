using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Menus;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var initialize=args.Contains("--initialize");
var builder=WebApplication.CreateBuilder(args.Where(a=>a!="--initialize").ToArray());
var root=FindRoot(builder.Environment.ContentRootPath);
var settings=Environment.GetEnvironmentVariable("METARILL_SETTINGS")??Path.Combine(root,".local","development.settings.json");
builder.Configuration.AddJsonFile(settings,optional:true,reloadOnChange:false).AddEnvironmentVariables();
if(string.IsNullOrEmpty(builder.Configuration["urls"]))builder.WebHost.UseUrls("http://127.0.0.1:8088");
var registry=new MetadataRegistry(Path.Combine(root,"backend","metadata","platform.json"),Path.Combine(root,"backend","metadata","demo.json"));
var platformOptions=builder.Configuration.GetSection("Metarill:Database").Get<DatabaseOptions>()??throw new InvalidOperationException("请配置 Metarill:Database。");
var businessOptions=builder.Configuration.GetSection("Metarill:BusinessDatabase").Get<DatabaseOptions>()??throw new InvalidOperationException("请配置 Metarill:BusinessDatabase。");
builder.Services.AddSingleton(registry);
builder.Services.AddSingleton(new TargetDatabaseSessions(platformOptions,businessOptions,registry));
builder.Services.AddSingleton<DatabaseSession>(sp=>sp.GetRequiredService<TargetDatabaseSessions>().Platform);
builder.Services.AddSingleton<SqlRowStore>();
builder.Services.AddSingleton<MetadataRepository>();
builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(builder.Configuration.GetValue("Metarill:WorkerId",1)));
builder.Services.AddSingleton<ISchemaChangeJournal>(new FileSchemaChangeJournal(Path.Combine(root,".local","schema-history.jsonl")));
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddSingleton<BootstrapService>();
builder.Services.AddSingleton<AuthenticationService>();
builder.Services.AddSingleton<CSharpGenerator>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpRuntimeAuthorization>();
builder.Services.AddScoped<IRuntimeAuthorization>(sp=>sp.GetRequiredService<HttpRuntimeAuthorization>());
builder.Services.AddScoped<MenuWorkbenchBusiness>();
builder.Services.AddScoped<IRecordMutationObserver,ConfigurationMutationObserver>();
Metarill.Platform.Generated.GeneratedRegistration.AddGeneratedServices(builder.Services);
Metarill.Demo.Generated.GeneratedRegistration.AddGeneratedServices(builder.Services,sp=>sp.GetRequiredService<TargetDatabaseSessions>().Business);
builder.Services.AddControllers().AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options=>options.InvalidModelStateResponseFactory=_=>new Microsoft.AspNetCore.Mvc.OkObjectResult(R<object>.Error("请求字段类型、字段名或必填项不正确。")));
builder.Services.ConfigureHttpJsonOptions(options=>JsonDefaults.CopyTo(options.SerializerOptions));
builder.Services.AddCors(options=>options.AddDefaultPolicy(policy=>policy.SetIsOriginAllowed(origin=>Uri.TryCreate(origin,UriKind.Absolute,out var uri)&&uri.IsLoopback).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options=>options.AddPolicy("login",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"local",_=>new FixedWindowRateLimiterOptions{PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0})));
var app=builder.Build();
if(initialize)
{
    await app.Services.GetRequiredService<BootstrapService>().InitializeAsync(builder.Configuration["Metarill:Bootstrap:Username"]??"",builder.Configuration["Metarill:Bootstrap:Password"]??"",businessOptions);
    Console.WriteLine("平台与独立业务库初始化完成；重复执行不会重置管理员密码。");return;
}
app.UseCors();app.UseRateLimiter();
app.Use(async(context,next)=>
{
    try
    {
        var token=context.Request.Headers["loginStateId"].ToString();
        if(context.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))token=context.Request.Headers.Authorization.ToString()[7..];
        if(!string.IsNullOrEmpty(token))
        {
            string? project=null;
            if(context.Request.Headers.TryGetValue("sysParams",out var value)&&value.ToString().Length>0)
            {
                if(value.ToString().Length>8192)throw new InvalidOperationException("上下文过长。");
                using var json=JsonDocument.Parse(Uri.UnescapeDataString(value.ToString()));
                if(json.RootElement.TryGetProperty("yzProjectId",out var selected))project=selected.ToString();
            }
            var identity=await context.RequestServices.GetRequiredService<AuthenticationService>().AuthenticateAsync(token,project,context.RequestAborted);
            if(identity is not null)context.Items[typeof(AuthenticatedIdentity)]=identity;
        }
        await next(context);
    }
    catch(UnauthorizedAccessException exception)
    {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=context.Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)?401:200;
        await context.Response.WriteAsJsonAsync(R<object>.Error(exception.Message=="登录超时"?"登录超时":"无权执行此操作。"),context.RequestAborted);
    }
    catch(Exception exception)when(exception is InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
    {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=200;await context.Response.WriteAsJsonAsync(R<object>.Error(exception.Message),context.RequestAborted);
    }
});
app.MapGet("/api/runtime/health/check",()=>R<object>.Success(new{application="Metarill",backend="csharp",databaseProviders=DatabaseProviders.Capabilities}));
app.MapControllers();
await app.RunAsync();

static string FindRoot(string start)
{
    for(var current=new DirectoryInfo(start);current is not null;current=current.Parent)
        if(File.Exists(Path.Combine(current.FullName,"global.json")))return current.FullName;
    throw new InvalidOperationException("无法定位 Metarill 根目录。");
}
public partial class Program;
