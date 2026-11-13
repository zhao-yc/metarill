using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Paired.SqlObservation.Tests;

/// <summary>真实Kestrel和HostingStartup运输验证；SQL使用实际关闭的MySQL驱动，不连接数据库或伪装普通权限矩阵。</summary>
public sealed class SqlObservationHttpTests
{
    private const string Run="1234567890abcdef1234567890abcdef";
    /// <summary>未加载或未开启时无额外必选配置、不创建采集目录，原异步处理器照常返回。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task Default_host_does_not_read_private_settings_or_change_original_response(bool load,bool enabled)
    {
        await using var fixture=await Fixture.Start(load,enabled);var text=await fixture.Client.GetStringAsync("/original");Assert.Equal("original-response",text);await fixture.Stop();Assert.Empty(Directory.GetDirectories(fixture.Output));
    }
    /// <summary>启用后跨await捕获实际参数和原驱动失败，原R响应保持；关闭后才发布完整索引。</summary>
    [Fact]
    public async Task Real_hosting_startup_captures_the_original_async_chain_and_bound_parameter()
    {
        await using var fixture=await Fixture.Start();using var request=new HttpRequestMessage(HttpMethod.Post,"/sql"){Content=new StringContent("password=PRIVATE-REQUEST-BODY")};request.Headers.Add("X-Metarill-Sql-Sample",Run);using var response=await fixture.Client.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("original-driver-failure",await response.Content.ReadAsStringAsync());await fixture.Stop();
        using var index=fixture.Index();Assert.True(index.RootElement.GetProperty("internallyConsistent").GetBoolean());Assert.True(index.RootElement.GetProperty("finalized").GetBoolean());Assert.Equal(1,index.RootElement.GetProperty("observed").GetInt32());using var trace=fixture.Trace();var record=trace.RootElement;Assert.True(record.GetProperty("chainSucceeded").GetBoolean());Assert.Equal(Run,record.GetProperty("sample").GetString());Assert.True(record.GetProperty("internallyConsistent").GetBoolean());var command=Assert.Single(record.GetProperty("trace").GetProperty("commands").EnumerateArray());Assert.False(command.GetProperty("succeeded").GetBoolean());Assert.True(command.GetProperty("driverCalled").GetBoolean());Assert.Equal("offline",command.GetProperty("database").GetString());var parameter=Assert.Single(command.GetProperty("parameters").EnumerateArray());Assert.Equal("value",parameter.GetProperty("name").GetString());Assert.NotNull(parameter.GetProperty("valueHash").GetString());Assert.DoesNotContain("PRIVATE-",trace.RootElement.GetRawText());Assert.DoesNotContain("User ID",trace.RootElement.GetRawText());
    }
    /// <summary>并发请求各自独立采集，不能把两个请求参数或SQL混入同一scope。</summary>
    [Fact]
    public async Task Concurrent_requests_keep_their_own_async_scope()
    {
        await using var fixture=await Fixture.Start();await Task.WhenAll(fixture.Client.GetStringAsync("/sql?value=31"),fixture.Client.GetStringAsync("/sql?value=47"));await fixture.Stop();using var index=fixture.Index();Assert.True(index.RootElement.GetProperty("internallyConsistent").GetBoolean());Assert.Equal(2,index.RootElement.GetProperty("completed").GetInt32());var hashes=new HashSet<string>();foreach(var path in fixture.TraceFiles()){using var trace=JsonDocument.Parse(File.ReadAllText(path));var command=Assert.Single(trace.RootElement.GetProperty("trace").GetProperty("commands").EnumerateArray());hashes.Add(Assert.Single(command.GetProperty("parameters").EnumerateArray()).GetProperty("valueHash").GetString()!);}Assert.Equal(2,hashes.Count);
    }
    /// <summary>原处理链未处理异常仍由Kestrel返回失败，私有记录只保存类型，不保存异常正文。</summary>
    [Fact]
    public async Task Original_unhandled_exception_keeps_server_failure_and_private_message_out_of_trace()
    {
        await using var fixture=await Fixture.Start();using var response=await fixture.Client.GetAsync("/failure");Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);await fixture.Stop();using var index=fixture.Index();Assert.True(index.RootElement.GetProperty("internallyConsistent").GetBoolean());using var trace=fixture.Trace();Assert.False(trace.RootElement.GetProperty("chainSucceeded").GetBoolean());Assert.DoesNotContain("PRIVATE-ERROR",trace.RootElement.GetRawText());
    }
    /// <summary>库名超出明确范围只使采集失效，不改原业务R响应。</summary>
    [Fact]
    public async Task Unexpected_database_is_not_reported_as_complete_evidence()
    {
        await using var fixture=await Fixture.Start(settings:new(){["ExpectedDatabases"]="other"});await fixture.Client.GetStringAsync("/sql");await fixture.Stop();using var index=fixture.Index();Assert.False(index.RootElement.GetProperty("internallyConsistent").GetBoolean());using var trace=fixture.Trace();Assert.Contains("unexpected-database",trace.RootElement.GetProperty("problems").EnumerateArray().Select(item=>item.GetString()));
    }
    /// <summary>错误固定摘要、所有权、密钥和预算均在正式接请求前拒绝。</summary>
    [Theory][InlineData("hash")][InlineData("key")][InlineData("owner")][InlineData("budget")][InlineData("databases")][InlineData("missing")]
    public async Task Invalid_private_configuration_fails_before_listening(string defect)
    {
        var exception=await Record.ExceptionAsync(()=>Fixture.Start(defect:defect));Assert.NotNull(exception);
    }
    /// <summary>样本预算与无效标记不拦原请求，也不能用正常trace掩盖漏采。</summary>
    [Theory][InlineData("budget")][InlineData("duplicate")][InlineData("invalid")]
    public async Task Transport_or_sample_limits_keep_original_handler_and_reject_final_index(string kind)
    {
        await using var fixture=await Fixture.Start(settings:kind=="budget"?new(){["MaximumRequests"]="1"}:null);for(var i=0;i<2;i++){using var request=new HttpRequestMessage(HttpMethod.Get,"/original");request.Headers.TryAddWithoutValidation("X-Metarill-Sql-Sample",kind=="invalid"?"../../PRIVATE":Run);using var response=await fixture.Client.SendAsync(request);Assert.Equal("original-response",await response.Content.ReadAsStringAsync());}await fixture.Stop();using var index=fixture.Index();Assert.False(index.RootElement.GetProperty("internallyConsistent").GetBoolean());Assert.Equal(2,index.RootElement.GetProperty("completed").GetInt32());
    }
    /// <summary>并发预算只使证据失效，不能改变两个原处理器的返回。</summary>
    [Fact]
    public async Task Concurrency_limit_does_not_lose_active_request_accounting()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);await using var fixture=await Fixture.Start(settings:new(){["MaximumConcurrentRequests"]="1"},blocked:async()=>{entered.SetResult();await release.Task;});var first=fixture.Client.GetStringAsync("/blocked");await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal("original-response",await fixture.Client.GetStringAsync("/original"));release.SetResult();Assert.Equal("blocked-response",await first);await fixture.Stop();using var index=fixture.Index();Assert.False(index.RootElement.GetProperty("internallyConsistent").GetBoolean());Assert.Contains("concurrency-limit",index.RootElement.GetProperty("problems").EnumerateArray().Select(value=>value.GetString()));
    }
    /// <summary>Scope关闭后的继承任务若在宿主关闭前执行，最终索引必须重新发现迟到命令。</summary>
    [Fact]
    public async Task Late_command_before_host_disposal_invalidates_the_final_snapshot()
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);Task? late=null;await using var fixture=await Fixture.Start(late:()=>{late=Task.Run(async()=>{await release.Task;await ExecuteClosedDriver(19);});});await fixture.Client.GetStringAsync("/late");release.SetResult();await late!.WaitAsync(TimeSpan.FromSeconds(5));await fixture.Stop();using var index=fixture.Index();Assert.False(index.RootElement.GetProperty("internallyConsistent").GetBoolean());using var trace=fixture.Trace();Assert.Contains("lateCommand",trace.RootElement.GetProperty("trace").GetProperty("problems").EnumerateArray().Select(value=>value.GetString()));
    }
    /// <summary>索引不可写仍返回原业务正文，不能伪造finalized记录。</summary>
    [Fact]
    public async Task Index_io_failure_does_not_replace_original_response()
    {
        await using var fixture=await Fixture.Start();var index=Path.Combine(Assert.Single(Directory.GetDirectories(fixture.Output)),"index.json");Assert.StartsWith(Path.GetFullPath(fixture.Folder)+Path.DirectorySeparatorChar,Path.GetFullPath(index),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);File.Delete(index);Directory.CreateDirectory(index);Assert.Equal("original-response",await fixture.Client.GetStringAsync("/original"));await fixture.Stop();Assert.True(Directory.Exists(index));Assert.Equal(Enumerable.Repeat((byte)11,32).ToArray(),File.ReadAllBytes(Path.Combine(fixture.Folder,"key.bin")));
    }
    /// <summary>本轮输出目录被移走时只使证据失败，原链仍正常返回；移动严格限制在本用例目录内。</summary>
    [Fact]
    public async Task Trace_io_failure_leaves_no_complete_final_manifest()
    {
        await using var fixture=await Fixture.Start();var directory=Assert.Single(Directory.GetDirectories(fixture.Output));var destination=directory+".moved";var prefix=Path.GetFullPath(fixture.Folder)+Path.DirectorySeparatorChar;Assert.StartsWith(prefix,Path.GetFullPath(directory),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);Assert.StartsWith(prefix,Path.GetFullPath(destination),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);Assert.Equal((FileAttributes)0,File.GetAttributes(directory)&FileAttributes.ReparsePoint);Directory.Move(directory,destination);Assert.Equal("original-response",await fixture.Client.GetStringAsync("/original"));await fixture.Stop();using var index=JsonDocument.Parse(File.ReadAllText(Path.Combine(destination,"index.json")));Assert.False(index.RootElement.GetProperty("finalized").GetBoolean());Assert.False(index.RootElement.GetProperty("internallyConsistent").GetBoolean());
    }
    /// <summary>总条目预算到达后不继续保留新scope，仍执行原请求并明确漏采。</summary>
    [Fact]
    public async Task Total_entry_budget_does_not_silently_discard_samples()
    {
        await using var fixture=await Fixture.Start(settings:new(){["MaximumRetainedEntries"]="1"});await fixture.Client.GetStringAsync("/sql");await fixture.Client.GetStringAsync("/sql");await fixture.Stop();using var index=fixture.Index();Assert.Equal(2,index.RootElement.GetProperty("completed").GetInt32());Assert.False(index.RootElement.GetProperty("internallyConsistent").GetBoolean());Assert.Contains("total-entry-limit",index.RootElement.GetProperty("problems").EnumerateArray().Select(value=>value.GetString()));
    }
    /// <summary>重复运输头不能关联为合法单一样本，原请求不被拦截。</summary>
    [Fact]
    public async Task Multiple_sample_headers_are_explicitly_incomplete()
    {
        await using var fixture=await Fixture.Start();using var request=new HttpRequestMessage(HttpMethod.Get,"/original");request.Headers.TryAddWithoutValidation("X-Metarill-Sql-Sample",new[]{Run,"abcdefabcdefabcdefabcdefabcdefab"});using var response=await fixture.Client.SendAsync(request);Assert.Equal("original-response",await response.Content.ReadAsStringAsync());await fixture.Stop();using var trace=fixture.Trace();Assert.Contains("invalid-sample-label",trace.RootElement.GetProperty("problems").EnumerateArray().Select(value=>value.GetString()));
    }
    /// <summary>宿主重放HostingStartup时只能产生一层请求采集，不把嵌套失败或重复计数当通过。</summary>
    [Fact]
    public async Task Repeated_hosting_startup_registration_is_idempotent()
    {
        await using var fixture=await Fixture.Start(configureTwice:true);Assert.Equal("original-response",await fixture.Client.GetStringAsync("/original"));await fixture.Stop();using var index=fixture.Index();Assert.Equal(1,index.RootElement.GetProperty("started").GetInt32());Assert.Equal(1,index.RootElement.GetProperty("observed").GetInt32());Assert.True(index.RootElement.GetProperty("internallyConsistent").GetBoolean());
    }
    /// <summary>实际关闭的驱动仍经过原Data包装；故意不Open，既不访问数据库也不模拟SQL成功。</summary>
    private static async Task ExecuteClosedDriver(int value)
    {
        var provider=new MySqlDatabaseProvider();await using var connection=provider.CreateConnection(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});await using var command=provider.CreateCommand(connection);command.CommandText="SELECT @value";var parameter=command.CreateParameter();parameter.ParameterName="value";parameter.Value=value;command.Parameters.Add(parameter);await Assert.ThrowsAsync<InvalidOperationException>(()=>command.ExecuteScalarAsync());
    }
    /// <summary>每个用例保留本轮合成证据，使用操作系统分配端口，无本机Docker或真实数据库。</summary>
    private sealed class Fixture:IAsyncDisposable
    {
        internal string Folder{get;}=Path.Combine(Root(),".local/csharp-http-observation-20261102/cases",Guid.NewGuid().ToString("N"));internal string Output=>Path.Combine(Folder,"output");internal HttpClient Client{get;private set;}=null!;private WebApplication? app;
        /// <summary>真实HostingStartup由WebHost原加载机制发现，避免只手工调用中间件冒充接线。</summary>
        internal static async Task<Fixture> Start(bool load=true,bool enabled=true,Dictionary<string,string>? settings=null,string? defect=null,Func<Task>? blocked=null,Action? late=null,bool configureTwice=false)
        {
            var fixture=new Fixture();Directory.CreateDirectory(fixture.Output);File.WriteAllText(Path.Combine(fixture.Output,".metarill-paired-observation-owner"),defect=="owner"?"other":Run);File.WriteAllBytes(Path.Combine(fixture.Folder,"key.bin"),Enumerable.Repeat((byte)11,defect=="key"?31:32).ToArray());
            using var stream=File.OpenRead(typeof(DatabaseCommandObservation).Assembly.Location);var values=new Dictionary<string,string>{{"Enabled",enabled.ToString()},{"RunId",Run},{"ExpectedDataSha256",defect=="hash"?new string('0',64):Convert.ToHexStringLower(SHA256.HashData(stream))},{"ExpectedDatabases","offline"},{"OutputRoot",fixture.Output},{"KeyFile",Path.Combine(fixture.Folder,"key.bin")}};if(defect=="budget")values["MaximumRequests"]="0";if(defect=="databases")values["ExpectedDatabases"]="offline,offline";if(defect=="missing")values.Remove("KeyFile");if(settings is not null)foreach(var pair in settings)values[pair.Key]=pair.Value;
            var arguments=new List<string>{"--urls","http://127.0.0.1:0"};if(load){arguments.Add("--hostingStartupAssemblies");arguments.Add(typeof(SqlObservationHostingStartup).Assembly.GetName().Name!);}foreach(var pair in values){arguments.Add("--Metarill:Paired:SqlObservation:"+pair.Key);arguments.Add(pair.Value);}
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=arguments.ToArray(),ContentRootPath=fixture.Folder,EnvironmentName="Production",ApplicationName=typeof(SqlObservationHttpTests).Assembly.GetName().Name});builder.Logging.ClearProviders();if(configureTwice)new SqlObservationHostingStartup().Configure(builder.WebHost);var app=builder.Build();fixture.app=app;
            app.MapGet("/original",()=>"original-response");app.MapMethods("/sql",["GET","POST"],async(HttpContext context)=>{await Task.Yield();await ExecuteClosedDriver(int.TryParse(context.Request.Query["value"],out var value)?value:11);return Results.Json(new{code=500,message="original-driver-failure"});});
            app.MapGet("/failure",(HttpContext _)=>throw new InvalidOperationException("PRIVATE-ERROR-MESSAGE"));app.MapGet("/blocked",async()=>{if(blocked is not null)await blocked();return "blocked-response";});app.MapGet("/late",()=>{late?.Invoke();return "late-response";});
            try{await app.StartAsync();var address=Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);fixture.Client=new HttpClient{BaseAddress=new Uri(address),Timeout=TimeSpan.FromSeconds(10)};return fixture;}
            catch{await app.DisposeAsync();throw;}
        }
        internal IEnumerable<string> TraceFiles()=>Directory.GetFiles(Assert.Single(Directory.GetDirectories(Output)),"*.json").Where(path=>Path.GetFileName(path)!="index.json");
        internal JsonDocument Trace()=>JsonDocument.Parse(File.ReadAllText(Assert.Single(TraceFiles())));
        internal JsonDocument Index()=>JsonDocument.Parse(File.ReadAllText(Path.Combine(Assert.Single(Directory.GetDirectories(Output)),"index.json")));
        /// <summary>停宿主并释放DI单例后读取finalized；不以HTTP响应已到达推断采集已落盘。</summary>
        internal async Task Stop(){if(app is not null){await app.StopAsync();await app.DisposeAsync();app=null;}}
        public async ValueTask DisposeAsync(){await Stop();Client?.Dispose();}
        private static string Root([System.Runtime.CompilerServices.CallerFilePath]string path="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
    }
}
