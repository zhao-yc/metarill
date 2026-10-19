using System.Security;
using System.Globalization;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Metarill.Api.Controllers;
using System.Text.Json;
using System.Threading.RateLimiting;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Runtime.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using {{NAMESPACE}}.Generated;
using {{NAMESPACE}}.Host;

var builder = WebApplication.CreateBuilder(args.Where(value => value is not "--schema-preview" and not "--schema-apply").ToArray());
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true).AddEnvironmentVariables();
// 仅显式开发认证启用时允许本机HBuilderX不同端口；不默认开放正式部署来源。
var developmentCors = builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Authentication:DevelopmentBootstrap:Enabled");
if (developmentCors) builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsLoopback
    && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0).AllowAnyHeader().AllowAnyMethod()));
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var metadata = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "metadata/project.json")), jsonOptions)
    ?? throw new InvalidOperationException("缺少应用元数据。");
MetadataValidation.Validate(metadata);
var infrastructure = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "metadata/files.json")), jsonOptions)
    ?? throw new InvalidOperationException("缺少文件基础元数据。");
var physicalMetadata = MetadataCatalogComposition.MergePhysical(metadata, infrastructure);
builder.Services.AddSingleton(metadata);
builder.Services.AddSingleton<DevelopmentNavigation>();
builder.Services.AddSingleton(provider => new DatabaseSession(builder.Configuration.GetSection("Database").Get<DatabaseOptions>() ?? new()));
builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(builder.Configuration.GetValue<int?>("Ids:WorkerId") ?? 1));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DevelopmentSessions>();
builder.Services.AddScoped<IRuntimeAuthorization, DevelopmentAuthorization>();
// 生成和下载共享同一个票据仓库；先取得资源额度，再执行生成页的有界查询。
builder.Services.AddSingleton(builder.Configuration.GetSection("Exports").Get<ExportOptions>() ?? new());
builder.Services.AddSingleton<ExcelPageExportService>();
builder.Services.AddSingleton<IPageExportService>(provider => provider.GetRequiredService<ExcelPageExportService>());
builder.Services.AddHostedService<ExportCleanupWorker>();
// 文件基础设施与生成 CRUD 共享同一个 DatabaseSession，引用在原业务事务内同步。
builder.Services.AddSingleton(provider => new SqlRowStore(provider.GetRequiredService<DatabaseSession>(), infrastructure));
builder.Services.AddSingleton(new FileMetadataContext([metadata]));
builder.Services.AddSingleton(builder.Configuration.GetSection("Files").Get<FileLibraryOptions>() ?? new());
builder.Services.AddScoped<FileLibraryService>();
builder.Services.AddScoped<IFileLibraryResolver, SingleDatabaseFileLibraryResolver>();
builder.Services.AddScoped<IRecordMutationObserver, FileReferenceMutationObserver>();
builder.Services.AddGeneratedServices();
builder.Services.AddControllers().AddApplicationPart(typeof(FileLibraryController).Assembly).AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
// 正式宿主在手写 partial 中替换认证、文件或按需扩展导出实现。
HostCustomization.Configure(builder.Services, builder.Configuration, builder.Environment);
var app = builder.Build();
if (args.Contains("--schema-preview", StringComparer.Ordinal) || args.Contains("--schema-apply", StringComparer.Ordinal))
{
    // 结构修改只能由显式命令触发，正常 Web 启动永远不自动建表。
    using var scope = app.Services.CreateScope();
    var schema = new SchemaService(scope.ServiceProvider.GetRequiredService<DatabaseSession>(), new FileSchemaChangeJournal(Path.Combine(app.Environment.ContentRootPath, ".local/schema-history.jsonl")));
    var plan = await schema.PreviewAsync(physicalMetadata);
    Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions(jsonOptions) { WriteIndented = true, NewLine = "\n" }));
    if (args.Contains("--schema-apply", StringComparer.Ordinal)) Console.WriteLine(JsonSerializer.Serialize(await schema.ApplyAsync(plan), jsonOptions));
    return;
}
// CORS必须在认证前处理OPTIONS预检；允许来源只解决浏览器访问，绝不赋予API身份。
app.UseRouting();
if (developmentCors) app.UseCors();
// 在控制器创建数据库服务前认证，因此无数据库配置时默认也能明确返回 401。
app.Use(async (context, next) =>
{
    try
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") && path != "/api/admin/sys/yz_manager/login" && !IsPublicFileContent(context.Request)) context.RequestServices.GetRequiredService<IRuntimeAuthorization>().RequireActor();
        await next(context);
    }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        var (status, message) = error switch
        {
            UnauthorizedAccessException => (401, "请先登录；开发认证仅在 Development 且显式配置后可用。"),
            SecurityException => (403, "没有当前操作权限。"),
            ArgumentException or JsonException => (400, "请求参数不符合页面定义。"),
            NotSupportedException => (501, "此能力需要宿主实现，目前不可用。"),
            _ => (500, "操作失败，请由应用管理员核查服务端状态。")
        };
        context.Response.Clear(); context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(R<object>.Error(message, status));
    }
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "UP", application = metadata.YzProjectName }));
app.MapPost("/api/admin/sys/yz_manager/login", async (HttpContext context, DevelopmentSessions sessions) =>
{
    string? username; string? password;
    if (context.Request.HasFormContentType)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted); username = form["username"]; password = form["password"];
    }
    else
    {
        var request = await context.Request.ReadFromJsonAsync<DevelopmentLogin>(cancellationToken: context.RequestAborted); username = request?.Username; password = request?.Password;
    }
    var token = sessions.Login(username, password);
    if (token is null) return Results.Json(R<object>.Error("账号、密码或开发认证配置无效。", 401), statusCode: 401);
    var result = R<object>.Success(new { id = "1", username, name = username }); result.Map["loginStateId"] = token; return Results.Json(result);
}).RequireRateLimiting("login");
app.MapPost("/api/admin/sys/yz_manager/exit_login", (HttpContext context, DevelopmentSessions sessions) => { sessions.Logout(DevelopmentSessions.ReadToken(context)); return R<string>.Success("退出成功"); });
// 保留已有下载协议，票据仍须绑定当前真实账号和项目，不能公开私有导出目录。
app.MapGet("/api/admin/sys/export/export", async (string exportId, HttpContext context, ExcelPageExportService exports, IRuntimeAuthorization authorization, CancellationToken token) =>
{
    var content = await exports.OpenAsync(exportId, authorization.RequireActor(), token);
    context.Response.Headers.CacheControl = "private, no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(content.Stream, content.ContentType, content.FileName, enableRangeProcessing: true);
});
// 导航和权限来自固定发布元数据，不能由客户端项目选择或私有平台配置改变。
app.MapGet("/api/admin/sys/yz_manager/getMenu", (DevelopmentNavigation navigation, IRuntimeAuthorization authorization) => navigation.Menu(authorization));
app.MapGet("/api/admin/sys/yz_manager/getCurrentPermissions", (DevelopmentNavigation navigation, IRuntimeAuthorization authorization) => R<object>.Success(navigation.Permissions(authorization)));
app.MapGet("/api/admin/sys/yz_manager/cap-config", () => R<object>.Success(new { enabled = false, apiEndpoint = "" }));
app.MapControllers();
await app.RunAsync();

// 仅正整数文件标识的 GET/HEAD 公开流可匿名；写入、解析、私有流和相似路径仍须认证。
static bool IsPublicFileContent(HttpRequest request)
{
    const string prefix = "/api/admin/sys/file-library/public-content/";
    var path = request.Path.Value ?? "";
    return (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) && path.StartsWith(prefix, StringComparison.Ordinal)
        && long.TryParse(path.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;
}

namespace {{NAMESPACE}}.Host
{
    /// <summary>正式认证和基础设施可通过同程序集手写 partial 接入。</summary>
    public static partial class HostCustomization
    {
        /// <summary>调用宿主手写服务注册扩展。</summary>
        public static void Configure(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) => ConfigureServices(services, configuration, environment);
        /// <summary>正式认证等实现只写在配套手写 partial 中。</summary>
        static partial void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);
    }
    public sealed record DevelopmentLogin(string? Username, string? Password);
}
