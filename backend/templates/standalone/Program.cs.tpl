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
using Metarill.Runtime.Security;
using {{NAMESPACE}}.Generated;
using {{NAMESPACE}}.Host;

// JSON与中文拒绝消息固定UTF8，重定向输出不依赖Windows当前控制台代码页。
Console.OutputEncoding = System.Text.Encoding.UTF8;
// 在构造Web宿主前拒绝混合、重复或未知结构命令，避免误启动服务或误执行写入。
string[] schemaCommands = ["--schema-inspect", "--schema-preview", "--schema-apply"];
var initializeSecurity=args.Contains("--initialize-security",StringComparer.Ordinal);
if(InvalidSecurityArguments(args))
{
    Console.Error.WriteLine("安全初始化必须单独使用--initialize-security，认证凭据只从私有配置或环境读取，不接受命令行字段。");
    Environment.ExitCode=2;return;
}
var requestedSchemaCommands = args.Where(value => value.StartsWith("--schema-", StringComparison.Ordinal)).ToArray();
if (requestedSchemaCommands.Length > 1 || requestedSchemaCommands.Any(value => !schemaCommands.Contains(value, StringComparer.Ordinal)))
{
    Console.Error.WriteLine("结构命令仅支持 --schema-inspect、--schema-preview 或 --schema-apply，必须单独使用且不能重复或带赋值。");
    Environment.ExitCode = 2; return;
}
var schemaCommand = requestedSchemaCommands.SingleOrDefault();
var builder = WebApplication.CreateBuilder(args.Where(value => !schemaCommands.Contains(value, StringComparer.Ordinal) && value!="--initialize-security").ToArray());
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true).AddEnvironmentVariables();
// 仅显式开发认证启用时允许本机HBuilderX不同端口；不默认开放正式部署来源。
var authenticationMode=ApplicationAuthenticationMode.Read(builder.Configuration,builder.Environment);
var developmentCors = authenticationMode.Development;
if (developmentCors) builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(origin =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.IsLoopback
    && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0).AllowAnyHeader().AllowAnyMethod()));
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
// 固定目录先核编译摘要并私有冻结；可变DI副本不能改变安全目录权威。
var verifiedCatalog=VerifiedPackageSecurityCatalog.Load(Path.Combine(AppContext.BaseDirectory,"metadata/project.json"));
var metadata=verifiedCatalog.Clone();
var infrastructure = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "metadata/files.json")), jsonOptions)
    ?? throw new InvalidOperationException("缺少文件基础元数据。");
var security = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "metadata/security.json")), jsonOptions)
    ?? throw new InvalidOperationException("缺少固定安全基础元数据。");
// 基础设施仍严格合成；生成注册类只允许编译绑定的固定管理页进入本应用物理图，不读取业务JSON特权标记。
var combinedInfrastructure=MetadataCatalogComposition.MergePhysical(infrastructure,security);
var physicalMetadata = GeneratedRegistration.ComposePhysicalMetadata(metadata, infrastructure, security);
builder.Services.AddSingleton(metadata);
ApplicationSecurityCatalogRegistration.Add(builder.Services,verifiedCatalog);
builder.Services.AddSingleton(authenticationMode);
builder.Services.AddSingleton<ApplicationSecurityRegistry>();
builder.Services.AddSingleton<ISecurityResourceRegistry>(provider=>provider.GetRequiredService<ApplicationSecurityRegistry>());
builder.Services.AddSingleton<ISecuritySessionPolicy>(provider=>provider.GetRequiredService<ApplicationSecurityRegistry>());
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<ApplicationWebAuthentication>();
builder.Services.AddScoped<ApplicationSecurityInitialization>();
builder.Services.AddSingleton<DevelopmentNavigation>();
builder.Services.AddSingleton(provider => new DatabaseSession(builder.Configuration.GetSection("Database").Get<DatabaseOptions>() ?? new()));
builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(builder.Configuration.GetValue<int?>("Ids:WorkerId") ?? 1));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DevelopmentSessions>();
if(authenticationMode.Development)builder.Services.AddScoped<IRuntimeAuthorization,DevelopmentAuthorization>();
else ApplicationWebSecurityCoordination.Add(builder.Services,authenticationMode);
// 生成和下载共享同一个票据仓库；先取得资源额度，再执行生成页的有界查询。
builder.Services.AddSingleton(builder.Configuration.GetSection("Exports").Get<ExportOptions>() ?? new());
builder.Services.AddSingleton<ExcelPageExportService>();
builder.Services.AddSingleton<IPageExportService>(provider => provider.GetRequiredService<ExcelPageExportService>());
builder.Services.AddHostedService<ExportCleanupWorker>();
// 文件基础设施与生成 CRUD 共享同一个 DatabaseSession，引用在原业务事务内同步。
builder.Services.AddSingleton(provider => new SqlRowStore(provider.GetRequiredService<DatabaseSession>(), combinedInfrastructure));
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
// 保留手写扩展；默认正式Web和显式开发模式的边界已由固定宿主实现。
HostCustomization.Configure(builder.Services, builder.Configuration, builder.Environment);
var app = builder.Build();
if(initializeSecurity)
{
    try
    {
        if(authenticationMode.Development||!authenticationMode.DatabaseConfigured)throw new InvalidOperationException("安全初始化要求有效Web数据库配置。");
        using var scope=app.Services.CreateScope();
        var status=await scope.ServiceProvider.GetRequiredService<ApplicationSecurityInitialization>().InitializeAsync(builder.Configuration,CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(new {status,credentialsChanged=status=="initialized"},jsonOptions));
    }
    catch(Exception error)
    {
        // 不把连接异常、参数值或私有Bootstrap配置写入控制台；退出失败且绝不自动重试。
        Console.Error.WriteLine("安全初始化失败，未重置既有凭据；请核查私有配置、结构及归属。异常类型："+error.GetType().Name);Environment.ExitCode=1;
    }
    return;
}
if (schemaCommand is not null)
{
    // 只有显式结构命令进入此分支；普通Web启动不读取或修改数据库结构。
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<DatabaseSession>();
    var outputOptions = new JsonSerializerOptions(jsonOptions) { WriteIndented = true, NewLine = "\n" };
    if (schemaCommand == "--schema-inspect")
    {
        // 反查仅使用包内受管物理模型，不创建计划、不注入执行journal，也不启动HTTP/后台服务。
        var snapshot = await new SchemaService(database).InspectSnapshotAsync(physicalMetadata);
        Console.WriteLine(JsonSerializer.Serialize(new { executable = false, snapshot.Provider, DatabaseSchema = snapshot.Schema, snapshot.Fingerprint }, outputOptions));
        return;
    }
    var schema = new SchemaService(database, new FileSchemaChangeJournal(Path.Combine(app.Environment.ContentRootPath, ".local/schema-history.jsonl")));
    var plan = await schema.PreviewAsync(physicalMetadata);
    Console.WriteLine(JsonSerializer.Serialize(plan, outputOptions));
    if (schemaCommand == "--schema-apply") Console.WriteLine(JsonSerializer.Serialize(await schema.ApplyAsync(plan), jsonOptions));
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
        if (path.StartsWithSegments("/api") && path != "/api/admin/sys/yz_manager/login" && !IsPublicFileContent(context.Request) && !ExportDownloadAccess.CanUseGrant(context.Request))
        {
            if(!authenticationMode.Development)
            {
                var identity=await context.RequestServices.GetRequiredService<ApplicationWebAuthentication>().ResolveAsync(DevelopmentSessions.ReadToken(context),context.RequestAborted);
                if(identity is null)throw new UnauthorizedAccessException();
                context.Items[typeof(AuthenticatedIdentity)]=identity;
            }
            context.RequestServices.GetRequiredService<IRuntimeAuthorization>().RequireActor();
        }
        await next(context);
    }
    catch (Exception error) when (!context.Response.HasStarted)
    {
        var (status, message) = error switch
        {
            UnauthorizedAccessException => (401, "请先登录，或确认应用认证配置。"),
            SecurityException => (403, "没有当前操作权限。"),
            // 已知业务版本冲突沿原工作台提示返回，其他InvalidOperationException仍作为内部错误隐藏。
            InvalidOperationException when error.Message.StartsWith("YZP_CONFLICT：",StringComparison.Ordinal) => (400,error.Message),
            PageImportException { IsInputError: true } => (400, error.Message),
            ArgumentException or JsonException => (400, "请求参数不符合页面定义。"),
            NotSupportedException => (501, "此能力需要宿主实现，目前不可用。"),
            _ => (500, "操作失败，请由应用管理员核查服务端状态。")
        };
        // 可修正的导入输入不是服务器意外；内部执行失败保留异常链与追踪标识，响应不泄露细节。
        if (status == 500) context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Metarill.Application").LogError(error, "请求处理失败，追踪标识 {TraceId}", context.TraceIdentifier);
        context.Response.Clear(); context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(R<object>.Error(message, status));
    }
});
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "UP", application = metadata.YzProjectName }));
app.MapPost("/api/admin/sys/yz_manager/login", async (HttpContext context) =>
{
    var login=await ReadLogin(context.Request);var username=login.Username;var password=login.Password;
    if(authenticationMode.Development)
    {
        var token=context.RequestServices.GetRequiredService<DevelopmentSessions>().Login(username,password);
        if(token is null)throw new UnauthorizedAccessException();
        var development=R<object>.Success(new {id="1",username,name=username,merchantName=metadata.YzProjectName});development.Map["loginStateId"]=token;return Results.Json(development);
    }
    var logged=await context.RequestServices.GetRequiredService<ApplicationWebAuthentication>().LoginAsync(username,password,context.RequestAborted);
    var result=R<object>.Success(logged.User);result.Map["loginStateId"]=logged.Token;return Results.Json(result);
}).RequireRateLimiting("login");
// 原修改密码页合同保留；POST避免新客户端把密码放进URL，GET仅供旧协议兼容。
app.MapMethods("/api/admin/sys/yz_manager/edit_password", ["GET","POST"], async(HttpContext context)=>
{
    var request=await ReadPasswordChange(context.Request);
    if(authenticationMode.Development)context.RequestServices.GetRequiredService<DevelopmentSessions>().ChangePassword(DevelopmentSessions.ReadToken(context),request.OldPwd,request.Pwd);
    else await context.RequestServices.GetRequiredService<ApplicationWebAuthentication>().ChangePasswordAsync(RequireWebIdentity(context),request.OldPwd,request.Pwd,context.RequestAborted);
    context.Response.Headers.CacheControl="private, no-store";return R<string>.Success("修改成功");
}).RequireRateLimiting("login");
app.MapPost("/api/admin/sys/yz_manager/exit_login", async(HttpContext context) =>
{
    if(authenticationMode.Development)context.RequestServices.GetRequiredService<DevelopmentSessions>().Logout(DevelopmentSessions.ReadToken(context));
    else await context.RequestServices.GetRequiredService<ApplicationWebAuthentication>().LogoutAsync(RequireWebIdentity(context),context.RequestAborted);
    return R<string>.Success("退出成功");
});
// 原 openUrl 无头下载只使用已签发的两分钟凭证；显式认证头继续检查真实账号与项目。
app.MapMethods("/api/admin/sys/export/export", new[] { "GET", "HEAD" }, async (string exportId, HttpContext context, ExcelPageExportService exports, IRuntimeAuthorization authorization, CancellationToken token) =>
{
    var content = ExportDownloadAccess.CanUseGrant(context.Request) ? await exports.OpenDownloadAsync(exportId, token) : await exports.OpenAsync(exportId, authorization.RequireActor(), token);
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
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

// 身份仅取本请求认证中间件产物，不从query或body接收账户、后台或项目。
static AuthenticatedIdentity RequireWebIdentity(HttpContext context)=>context.Items[typeof(AuthenticatedIdentity)] as AuthenticatedIdentity??throw new UnauthorizedAccessException();

// 在构造配置提供器之前拒绝横线/斜杠形式的命令行秘密；初始化仅一个固定flag，不接受裸key=value或混合参数。
static bool InvalidSecurityArguments(string[] arguments)=>
    arguments.Any(value=>value.TrimStart('-','/').StartsWith("Authentication",StringComparison.OrdinalIgnoreCase)||value.TrimStart('-','/').StartsWith("Database",StringComparison.OrdinalIgnoreCase))||
    arguments.Any(value=>value.StartsWith("--initialize",StringComparison.Ordinal)&&value!="--initialize-security")||
    arguments.Contains("--initialize-security",StringComparer.Ordinal)&&(arguments.Length!=1||arguments.Count(value=>value=="--initialize-security")!=1);

// 登录保留原URL编码表单/JSON双协议，所有字段有界且不接URL中的秘密或授权上下文。
static async Task<DevelopmentLogin> ReadLogin(HttpRequest request)
{
    if(request.Query.Count!=0)throw new ArgumentException("登录不接受query参数。");
    var bytes=new byte[4097];var length=0;while(length<bytes.Length){var count=await request.Body.ReadAsync(bytes.AsMemory(length),request.HttpContext.RequestAborted);if(count==0)break;length+=count;}
    if(length>4096)throw new ArgumentException("登录请求超过限制。");
    if(request.ContentType?.StartsWith("application/x-www-form-urlencoded",StringComparison.OrdinalIgnoreCase)==true)
    {
        var form=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(System.Text.Encoding.UTF8.GetString(bytes,0,length));
        if(form.Count!=2||form.Keys.Any(key=>key is not("username" or "password"))||form["username"].Count!=1||form["password"].Count!=1)throw new ArgumentException("登录字段无效。");
        return new(form["username"].ToString(),form["password"].ToString());
    }
    using var json=JsonDocument.Parse(bytes.AsMemory(0,length));var fields=new HashSet<string>(StringComparer.Ordinal);
    if(json.RootElement.ValueKind!=JsonValueKind.Object||json.RootElement.EnumerateObject().Any(field=>!fields.Add(field.Name)||field.Name is not("username" or "password")||field.Value.ValueKind!=JsonValueKind.String)||fields.Count!=2)throw new ArgumentException("登录字段无效。");
    return new(json.RootElement.GetProperty("username").GetString(),json.RootElement.GetProperty("password").GetString());
}

// 请求只允许原oldPwd/pwd两个有界字段；不接账户、项目、路径、配置或任意JSON键。
static async Task<DevelopmentPasswordChange> ReadPasswordChange(HttpRequest request)
{
    if(HttpMethods.IsGet(request.Method))
    {
        if(request.Query.Keys.Any(key=>key is not("oldPwd" or "pwd"))||request.Query["oldPwd"].Count!=1||request.Query["pwd"].Count!=1||request.ContentLength>0)throw new ArgumentException("改密请求参数无效。");
        return new(request.Query["oldPwd"].ToString(),request.Query["pwd"].ToString());
    }
    if(request.Query.Count!=0)throw new ArgumentException("改密请求不能混合query。");
    var bytes=new byte[4097];var length=0;while(length<bytes.Length){var count=await request.Body.ReadAsync(bytes.AsMemory(length),request.HttpContext.RequestAborted);if(count==0)break;length+=count;}
    if(length>4096)throw new ArgumentException("改密请求超过限制。");
    using var document=JsonDocument.Parse(bytes.AsMemory(0,length));var fields=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if(document.RootElement.ValueKind!=JsonValueKind.Object||document.RootElement.EnumerateObject().Any(field=>!fields.Add(field.Name)||field.Name is not("oldPwd" or "pwd")||field.Value.ValueKind!=JsonValueKind.String)||fields.Count!=2)throw new ArgumentException("改密请求参数无效。");
    return new(document.RootElement.GetProperty("oldPwd").GetString(),document.RootElement.GetProperty("pwd").GetString());
}

namespace {{NAMESPACE}}.Host
{
    /// <summary>正式认证和基础设施可通过同程序集手写 partial 接入。</summary>
    public static partial class HostCustomization
    {
        /// <summary>调用宿主手写服务注册扩展。</summary>
        public static void Configure(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment) => ConfigureServices(services, configuration, environment);
        /// <summary>宿主自定义基础设施只写在配套手写partial中，不修改受管认证实现。</summary>
        static partial void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment);
    }
    public sealed record DevelopmentLogin(string? Username, string? Password);
    public sealed record DevelopmentPasswordChange(string? OldPwd,string? Pwd);
}
