using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace Metarill.Data.Tests;

/// <summary>只启用本组件的显式环境，不能顺带执行现有建表/数据写入套件。</summary>
public sealed class ExecutionLeaseMySqlFactAttribute:FactAttribute
{
    public ExecutionLeaseMySqlFactAttribute()
    {
        var settings=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_SETTINGS");
        if(string.IsNullOrWhiteSpace(settings)||!File.Exists(settings)||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_SERVER_UUID")))Skip="未提供命名锁专用测试配置及实例固定标识。";
    }
}

/// <summary>真实MySQL跨进程及服务端到期，仅使用独占随机锁和连接会话变量，不建表/账号或读写业务记录。</summary>
public sealed class MySqlExecutionLeaseIntegrationTests
{
    [ExecutionLeaseMySqlFact]
    public async Task Separate_processes_busy_release_crash_server_expiry_and_late_owner_are_real()
    {
        try{await RunAsync();}
        catch(XunitException){throw;}
        catch(Exception error){throw new InvalidOperationException("命名锁组件验收失败，内部类型："+error.GetType().Name);}
    }
    /// <summary>实例固定核验完成后才创建随机锁；每一条锁用独立连接核零。</summary>
    private static async Task RunAsync([CallerFilePath]string source="")
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        if(options.ExpectedDatabase!="metarill_business_test")throw new InvalidOperationException("命名锁验收只允许既有隔离业务测试库。");
        var provider=new MySqlDatabaseProvider();provider.Validate(options);
        var pin=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_SERVER_UUID")!;
        await using var observer=MySqlExecutionLocks.CreateConnection(options);await observer.OpenAsync();
        await using(var command=observer.CreateCommand())
        {
            command.CommandText="SELECT @@server_uuid, DATABASE()";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());
            Assert.Equal(pin,reader.GetString(0),ignoreCase:true);Assert.Equal("metarill_business_test",reader.GetString(1));
        }
        var predecessor=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_VERIFY_NONCE");
        if(!string.IsNullOrEmpty(predecessor))
        {
            if(!Guid.TryParseExact(predecessor,"N",out _))throw new InvalidOperationException("前轮核零标识无效。");
            var previousKeys=new[]{"same-button","other-button","expiry"}.Select(suffix=>MySqlExecutionLocks.Key(options.ExpectedDatabase,"execution-acceptance:"+predecessor+":"+suffix)).ToArray();
            foreach(var key in previousKeys)await Free(observer,key);
            var evidence=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_EVIDENCE");if(!string.IsNullOrWhiteSpace(evidence)){Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"predecessor-cleanup-"+predecessor+".json"),JsonSerializer.Serialize(new{runId=predecessor,serverUuid=pin,keys=previousKeys,allFreed=true}));}
        }
        var nonce=Guid.NewGuid().ToString("N");var scope="execution-acceptance:"+nonce+":same-button";var other="execution-acceptance:"+nonce+":other-button";var expiry="execution-acceptance:"+nonce+":expiry";
        var keys=new[]{scope,other,expiry}.Select(value=>MySqlExecutionLocks.Key(options.ExpectedDatabase,value)).ToArray();
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-execution-worker");var directory=Path.Combine(boundary,nonce);Directory.CreateDirectory(directory);
        Worker? holder=null;IDatabaseExecutionLease? current=null,old=null;var stages=new List<string>();var passed=false;
        try
        {
            await BuildWorker(directory,source);
            holder=await Worker.Start(directory,scope,"hold");
            Assert.Null(await provider.TryAcquireExecutionLeaseAsync(options,scope,TimeSpan.FromSeconds(30),TimeProvider.System));stages.Add("cross-process-busy");
            await using(var independent=await provider.TryAcquireExecutionLeaseAsync(options,other,TimeSpan.FromSeconds(30),TimeProvider.System)){Assert.NotNull(independent);stages.Add("independent-button");}
            await holder.Release();await holder.DisposeAsync();holder=null;await Free(observer,keys[0]);stages.Add("owner-released");
            current=await provider.TryAcquireExecutionLeaseAsync(options,scope,TimeSpan.FromSeconds(30),TimeProvider.System);Assert.NotNull(current);await current.DisposeAsync();current=null;
            // 杀死确切的本轮子进程，连接级所有权应由服务器立即收回，而不是等待本机GC或静态信号量。
            holder=await Worker.Start(directory,scope,"hold");await holder.Crash();await holder.DisposeAsync();holder=null;await Free(observer,keys[0]);stages.Add("process-crash-released");
            // 关闭本地计时回调；只能由真实服务端wait_timeout释放这个2秒租约。
            var oldConnection=MySqlExecutionLocks.CreateConnection(options);old=await MySqlExecutionLocks.AcquireAsync(oldConnection,keys[2],2,new DormantClock(),CancellationToken.None);Assert.NotNull(old);
            Assert.Null(await provider.TryAcquireExecutionLeaseAsync(options,expiry,TimeSpan.FromSeconds(30),TimeProvider.System));await Free(observer,keys[2]);Assert.True(old.IsExpired);stages.Add("server-expiry-without-local-timer");
            current=await provider.TryAcquireExecutionLeaseAsync(options,expiry,TimeSpan.FromSeconds(30),TimeProvider.System);Assert.NotNull(current);
            await old.DisposeAsync();old=null;Assert.Equal(0,await IsFree(observer,keys[2]));stages.Add("late-owner-did-not-release-successor");await current.DisposeAsync();current=null;passed=true;
        }
        finally
        {
            if(holder is not null)await holder.DisposeAsync();if(old is not null)await old.DisposeAsync();if(current is not null)await current.DisposeAsync();
            var clean=false;
            try
            {
                await using var fresh=MySqlExecutionLocks.CreateConnection(options);await fresh.OpenAsync();
                await using(var identity=fresh.CreateCommand()){identity.CommandText="SELECT @@server_uuid";Assert.Equal(pin,Convert.ToString(await identity.ExecuteScalarAsync()),ignoreCase:true);}
                foreach(var key in keys)await Free(fresh,key);clean=true;
            }
            finally
            {
                var evidence=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_EVIDENCE");
                if(!string.IsNullOrWhiteSpace(evidence)){Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"native-proof-"+nonce+".json"),JsonSerializer.Serialize(new{runId=nonce,serverUuid=pin,database=options.ExpectedDatabase,keys,completedStages=stages,cleanupPassed=clean,passed=passed&&clean},new JsonSerializerOptions{WriteIndented=true}));}
            }
            var absolute=Path.GetFullPath(directory);if(Path.GetDirectoryName(absolute)!=Path.GetFullPath(boundary)||Path.GetFileName(absolute)!=nonce)throw new InvalidOperationException("子进程清理范围异常。");
            if(Directory.Exists(absolute))Directory.Delete(absolute,true);
        }
    }
    /// <summary>只读自己已知的随机锁状态，不读取全库锁、SQL正文或其它会话信息。</summary>
    private static async Task<int> IsFree(System.Data.Common.DbConnection connection,string key)
    {
        await using var command=connection.CreateCommand();command.CommandText="SELECT IS_FREE_LOCK(@lease_key)";var parameter=command.CreateParameter();parameter.ParameterName="lease_key";parameter.Value=key;command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture);
    }
    /// <summary>最多等待10秒，用新语句验证服务端释放；不依据客户端对象状态宣称清理。</summary>
    private static async Task Free(System.Data.Common.DbConnection connection,string key)
    {
        var timeout=Stopwatch.StartNew();while(await IsFree(connection,key)!=1){if(timeout.Elapsed>TimeSpan.FromSeconds(10))throw new XunitException("本轮独占命名锁尚未核零。");await Task.Delay(100);}
    }
    /// <summary>独立可执行程序只引用本轮真实Data工程；凭据仅由原私有配置读取，不进入参数或日志。</summary>
    private static async Task BuildWorker(string directory,string source)
    {
        var project=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../src/Metarill.Data/Metarill.Data.csproj"));
        var xml="<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><UseAppHost>false</UseAppHost></PropertyGroup><ItemGroup><ProjectReference Include=\""+System.Security.SecurityElement.Escape(project)+"\" /></ItemGroup></Project>";
        await File.WriteAllTextAsync(Path.Combine(directory,"Worker.csproj"),xml);await File.WriteAllTextAsync(Path.Combine(directory,"Program.cs"),WorkerSource);
        var start=StartInfo(directory);foreach(var value in new[]{"build","Worker.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror","-nodeReuse:false"})start.ArgumentList.Add(value);
        start.Environment["MSBUILDDISABLENODEREUSE"]="1";start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"]="0";
        using var process=Process.Start(start)!;using var drain=new CancellationTokenSource();var output=process.StandardOutput.ReadToEndAsync(drain.Token);var error=process.StandardError.ReadToEndAsync(drain.Token);
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));}
        catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}throw;}
        finally
        {
            var evidence=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_EVIDENCE");
            string captured;
            try{await Task.WhenAll(output,error).WaitAsync(TimeSpan.FromSeconds(5));captured=output.Result+"\n"+error.Result;}
            catch(TimeoutException){drain.Cancel();captured="编译进程已退出，输出管道未及时关闭，已取消输出回收；退出码："+process.ExitCode;}
            if(!string.IsNullOrWhiteSpace(evidence)){Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"native-worker-build-"+Path.GetFileName(directory)+".log"),captured);}
        }
        Assert.Equal(0,process.ExitCode);
    }
    private static ProcessStartInfo StartInfo(string directory)=>new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
    private sealed class Worker(Process process):IAsyncDisposable
    {
        private readonly Task<string> errors=process.StandardError.ReadToEndAsync();
        internal static async Task<Worker> Start(string directory,string scope,string mode)
        {
            var start=StartInfo(directory);foreach(var value in new[]{Path.Combine(directory,"bin/Debug/net10.0/Worker.dll"),scope,mode})start.ArgumentList.Add(value);
            var worker=new Worker(Process.Start(start)!);
            try{Assert.Equal("已取得",await worker.processOutput().WaitAsync(TimeSpan.FromSeconds(15)));return worker;}
            catch{await worker.DisposeAsync();throw;}
        }
        private Task<string?> processOutput()=>process.StandardOutput.ReadLineAsync();
        internal async Task Release(){await process.StandardInput.WriteLineAsync("释放");await process.StandardInput.FlushAsync();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(0,process.ExitCode);}
        internal async Task Crash(){process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}
        public async ValueTask DisposeAsync(){if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));}await errors;process.Dispose();}
    }
    private sealed class DormantClock:TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)=>new Timer();
        private sealed class Timer:ITimer{public bool Change(TimeSpan due,TimeSpan period)=>false;public void Dispose(){}public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
    }
    private const string WorkerSource="""
using System.Text.Json;
using Metarill.Data;
Console.OutputEncoding=new System.Text.UTF8Encoding(false);
try
{
    using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_SETTINGS")!));
    var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    if(options.ExpectedDatabase!="metarill_business_test")return 1;
    var provider=new MySqlDatabaseProvider();provider.Validate(options);
    await using(var observer=provider.CreateConnection(options))
    {
        await observer.OpenAsync();await using var pin=observer.CreateCommand();pin.CommandText="SELECT @@server_uuid";
        if(Convert.ToString(await pin.ExecuteScalarAsync())!=Environment.GetEnvironmentVariable("METARILL_EXECUTION_LEASE_SERVER_UUID"))return 1;
    }
    await using var lease=await provider.TryAcquireExecutionLeaseAsync(options,args[0],TimeSpan.FromSeconds(30),TimeProvider.System);
    if(lease is null){Console.WriteLine("占用");return 2;}
    Console.WriteLine("已取得");await Console.Out.FlushAsync();
    if(args[1]=="hold")await Console.In.ReadLineAsync();return 0;
}
catch{Console.Error.WriteLine("独占命名锁子进程失败。");return 1;}
""";
}
