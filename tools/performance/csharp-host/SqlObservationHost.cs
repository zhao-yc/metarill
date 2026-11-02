using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Metarill.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Metarill.Paired.SqlObservation;

/// <summary>独占合成宿主的每请求私有采集；只读已接线Data观察，不打开连接或执行SQL。</summary>
public sealed class SqlObservationHost:IDisposable
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){Converters={new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)}};
    private readonly object gate=new();private readonly byte[] key;private readonly string output,runId,dataSha;
    private readonly int maximumRequests,maximumCommands,maximumConcurrent,maximumEntries;
    private readonly HashSet<string> expectedDatabases,labels=new(StringComparer.Ordinal),problems=new(StringComparer.Ordinal);
    private readonly List<Ticket> tickets=[];
    private int started,completed,observed,active;private long retainedEntries;private bool closed;
    private sealed class Ticket(string id,string? sample,DatabaseCommandTrace? trace,List<string> issues)
    {
        internal string Id{get;}=id;internal string? Sample{get;}=sample;internal DatabaseCommandTrace? Trace{get;}=trace;internal List<string> Issues{get;}=issues;
        internal int Status;internal bool ChainSucceeded;internal string? ErrorType,Digest;internal DatabaseCommandTraceSnapshot? Snapshot;
    }
    /// <summary>固定程序集及库名必须明确；禁用时本类型根本不构造，不读取这些设置。</summary>
    public SqlObservationHost(IConfiguration configuration)
    {
        runId=Required(configuration,"RunId");if(!Regex.IsMatch(runId,"\\A[a-f0-9]{32}\\z"))throw new InvalidOperationException("采集运行标识不合法。");
        dataSha=Required(configuration,"ExpectedDataSha256");if(!Regex.IsMatch(dataSha,"\\A[a-f0-9]{64}\\z"))throw new InvalidOperationException("数据层程序集摘要不合法。");
        var location=typeof(DatabaseCommandObservation).Assembly.Location;if(string.IsNullOrEmpty(location)||new FileInfo(location).Length>64*1024*1024||!string.Equals(HashFile(Ordinary(location,false)),dataSha,StringComparison.Ordinal))throw new InvalidOperationException("实际数据层程序集与本轮固定来源不一致。");
        var names=Required(configuration,"ExpectedDatabases").Split(',');expectedDatabases=new(names,StringComparer.Ordinal);
        if(names.Length!=expectedDatabases.Count||names.Length is <1 or >16||names.Any(name=>!Regex.IsMatch(name,"\\A[A-Za-z_][A-Za-z0-9_]{0,63}\\z")))throw new InvalidOperationException("必须明确不重复的采集目标库名。");
        maximumRequests=Budget(configuration,"MaximumRequests",64,4096);maximumCommands=Budget(configuration,"MaximumCommands",4096,65536);maximumConcurrent=Budget(configuration,"MaximumConcurrentRequests",4,64);maximumEntries=Budget(configuration,"MaximumRetainedEntries",100000,1000000);
        var root=Ordinary(Required(configuration,"OutputRoot"),true);var owner=Ordinary(Path.Combine(root,".metarill-paired-observation-owner"),false);
        if(new FileInfo(owner).Length>128||File.ReadAllText(owner)!=runId)throw new InvalidOperationException("采集目录不属于本轮运行。");
        var keyFile=Ordinary(Required(configuration,"KeyFile"),false);if(new FileInfo(keyFile).Length!=32)throw new InvalidOperationException("采集密钥必须为32字节。");key=File.ReadAllBytes(keyFile);
        if(key.Length!=32){CryptographicOperations.ZeroMemory(key);throw new InvalidOperationException("密钥读取期间发生变化。");}
        output=Path.Combine(root,Guid.NewGuid().ToString("N"));
        try{if(OperatingSystem.IsWindows())Directory.CreateDirectory(output);else Directory.CreateDirectory(output,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);WriteIndex(false);}
        catch{CryptographicOperations.ZeroMemory(key);throw;}
    }
    /// <summary>仅解析固定设置名称，不把私有路径或值放入异常正文。</summary>
    private static string Required(IConfiguration configuration,string name)=>!string.IsNullOrWhiteSpace(configuration[name])?configuration[name]!:throw new InvalidOperationException("缺少采集设置："+name);
    private static int Budget(IConfiguration configuration,string name,int fallback,int maximum){var value=configuration.GetValue(name,fallback);if(value<1||value>maximum)throw new InvalidOperationException("采集预算不合法。");return value;}
    /// <summary>逐级拒绝链接/reparse，输出名称只由内部生成，不跟随客户端路径。</summary>
    private static string Ordinary(string path,bool directory)
    {
        var absolute=Path.GetFullPath(path);if(OperatingSystem.IsWindows()&&absolute.StartsWith("\\\\",StringComparison.Ordinal))throw new IOException("采集路径必须为普通本地路径。");
        for(string? current=absolute;current is not null;current=Path.GetDirectoryName(current)){if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("采集路径不能包含链接或reparse对象。");}
        if(directory?!Directory.Exists(absolute):!File.Exists(absolute))throw new IOException("采集路径类型不匹配。");return absolute;
    }
    /// <summary>流式计算已加载程序集摘要，不读取连接配置或数据库。</summary>
    private static string HashFile(string path){using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
    /// <summary>建立scope的同步调用位于原异步链之前，AsyncLocal继续沿原RequestDelegate传播。</summary>
    private Ticket Begin(HttpContext context)
    {
        lock(gate)
        {
            started++;var issues=new List<string>();var header=context.Request.Headers["X-Metarill-Sql-Sample"];string? sample=header.Count==0?null:header[0];
            if(header.Count>1||sample is not null&&!Regex.IsMatch(sample,"\\A[a-f0-9]{32}\\z")){issues.Add("invalid-sample-label");sample=null;}
            else if(sample is not null&&labels.Contains(sample))issues.Add("duplicate-sample-label");
            var id=Guid.NewGuid().ToString("N");DatabaseCommandTrace? trace=null;var reserved=false;
            if(closed||observed>=maximumRequests||active>=maximumConcurrent||retainedEntries>=maximumEntries)problems.Add(closed?"late-request":observed>=maximumRequests?"request-limit":active>=maximumConcurrent?"concurrency-limit":"total-entry-limit");
            else
            {
                observed++;active++;reserved=true;if(sample is not null)labels.Add(sample);
                try{trace=DatabaseCommandObservation.Begin(key,maximumCommands);}catch(Exception){problems.Add("scope-setup-failure");}
            }
            var ticket=new Ticket(id,sample,trace,issues);if(trace is not null)tickets.Add(ticket);else if(reserved)active--;return ticket;
        }
    }
    /// <summary>原链与异常保持；采集失败只污染私有证据，不改成新HTTP结果、不重试。</summary>
    public async Task CaptureAsync(HttpContext context,Func<Task> next)
    {
        var ticket=Begin(context);Exception? failure=null;
        try{await next();}catch(Exception error){failure=error;throw;}
        finally
        {
            ticket.Trace?.Dispose();
            lock(gate)
            {
                ticket.Status=context.Response.StatusCode;ticket.ChainSucceeded=failure is null;ticket.ErrorType=failure?.GetType().FullName;
                if(ticket.Trace is not null){active--;ticket.Snapshot=ticket.Trace.Snapshot();retainedEntries+=ticket.Snapshot.Commands.Count+ticket.Snapshot.Transactions.Count;SaveTicket(ticket);}
                completed++;if(retainedEntries>maximumEntries)problems.Add("total-entry-limit");SafeIndex(closed);
            }
        }
    }
    /// <summary>库名及scope问题都必须通过；正常失败业务仍可拥有完整失败证据，二者不混同。</summary>
    private void SaveTicket(Ticket ticket)
    {
        var snapshot=ticket.Snapshot;if(snapshot is null)return;
        if(snapshot.Commands.Any(command=>!expectedDatabases.Contains(command.Database))&&!ticket.Issues.Contains("unexpected-database"))ticket.Issues.Add("unexpected-database");
        var consistent=snapshot.InternallyConsistent&&ticket.Issues.Count==0;if(!consistent)problems.Add("incomplete-request");
        try
        {
            var record=new{format="metarill-paired-dotnet-http-v1",runId,id=ticket.Id,sample=ticket.Sample,statusAtMiddlewareExit=ticket.Status,chainSucceeded=ticket.ChainSucceeded,errorType=ticket.ErrorType,problems=ticket.Issues.ToArray(),internallyConsistent=consistent,trace=snapshot};
            var bytes=JsonSerializer.SerializeToUtf8Bytes(record,Json);Atomic(ticket.Id+".json",bytes);ticket.Digest=Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        catch(Exception){problems.Add("trace-io-failure");}
    }
    /// <summary>同目录写入并替换；只删除本调用生成的临时文件，源和目标始终位于本实例私有目录。</summary>
    private void Atomic(string name,byte[] bytes)
    {
        Ordinary(output,true);var temporary=Path.Combine(output,"."+Guid.NewGuid().ToString("N")+".tmp");
        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}Ordinary(output,true);File.Move(temporary,Path.Combine(output,name),true);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    /// <summary>只有关闭后、固定程序集和完整计数/记录均匹配才表示采集内部完整，不代表发布或性能通过。</summary>
    private void WriteIndex(bool finalized)
    {
        var receipts=tickets.Where(ticket=>ticket.Digest is not null).Select(ticket=>new{id=ticket.Id,sha256=ticket.Digest}).ToArray();
        var index=new{format="metarill-paired-dotnet-index-v1",runId,dataAssemblySha256=dataSha,expectedDatabases=expectedDatabases.Order(StringComparer.Ordinal).ToArray(),started,completed,observed,retainedEntries,problems=problems.Order(StringComparer.Ordinal).ToArray(),requests=receipts,finalized,internallyConsistent=finalized&&started==completed&&completed==observed&&observed==receipts.Length&&active==0&&problems.Count==0};Atomic("index.json",JsonSerializer.SerializeToUtf8Bytes(index,Json));
    }
    private void SafeIndex(bool finalized){try{WriteIndex(finalized);}catch(Exception){problems.Add("index-io-failure");}}
    /// <summary>关宿主时重读已关闭scope，捕获此之前的迟到失效；进程退出后的全路径完整性仍须协调器审计。</summary>
    public void Dispose()
    {
        lock(gate)
        {
            if(closed)return;closed=true;CryptographicOperations.ZeroMemory(key);if(started!=completed||active!=0)problems.Add("pending-request");
            foreach(var ticket in tickets){ticket.Trace?.Dispose();ticket.Snapshot=ticket.Trace?.Snapshot();SaveTicket(ticket);}SafeIndex(true);
        }
    }
}
