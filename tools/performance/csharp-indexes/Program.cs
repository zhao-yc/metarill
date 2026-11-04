using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using MySqlConnector;
namespace Metarill.Paired.Indexes;

/// <summary>验收专用短进程：先预览固定索引元数据，确认摘要后应用；不注册Web入口或启动自动DDL。</summary>
public static class Program
{
    private const string ServerUuid="6d3d024a-9d73-11f1-9939-ce69308a21dc";
    /// <summary>在线入口在读取口令或打开连接前核本轮精确用途；错误仅输出类型，防止驱动正文带出秘密。</summary>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding=System.Text.Encoding.UTF8;
        try
        {
            var mode=ParseMode(args);
            if(mode.Operation!="offline")RequireRun(Environment.GetEnvironmentVariable("METARILL_INDEX_RUN_ID"),Environment.GetEnvironmentVariable("METARILL_INDEX_AUTHORIZATION"));
            var baseline=RelationIndexProfile.ReadBaseline(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"metadata/project.json")));
            if(mode.Operation=="offline")
            {
                var expected=RelationIndexProfile.ExpectedChanges(baseline,"metarill_perf_fixture");
                Console.WriteLine(JsonSerializer.Serialize(new{format=RelationIndexProfile.Name,offline=true,ddlExecuted=false,changes=expected},RelationIndexProfile.Json));return 0;
            }
            var run=RequireRun(Environment.GetEnvironmentVariable("METARILL_INDEX_RUN_ID"),Environment.GetEnvironmentVariable("METARILL_INDEX_AUTHORIZATION"));
            var options=Options(run,mode.Target!,Environment.GetEnvironmentVariable("Database__ExpectedDatabase"),Environment.GetEnvironmentVariable("Database__ConnectionString"));
            var confirmation=Environment.GetEnvironmentVariable("METARILL_INDEX_EXPECTED_PLAN");
            if(mode.Operation=="apply"&&!Regex.IsMatch(confirmation??"",@"\A[a-f0-9]{64}\z"))throw new UnauthorizedAccessException("缺少索引升级的预览摘要。");
            using var session=new DatabaseSession(options);var service=new SchemaService(session,expectedServerUuid:ServerUuid);
            var metadata=RelationIndexProfile.Build(baseline);var plan=await service.PreviewAsync(metadata);
            RelationIndexProfile.RequireChanges(plan,RelationIndexProfile.ExpectedChanges(baseline,options.ExpectedDatabase),mode.Operation=="verify");
            var hash=RelationIndexProfile.PlanSha256(plan);SchemaApplyResult? result=null;
            if(mode.Operation=="apply")
            {
                if(hash!=confirmation)throw new InvalidOperationException("索引预览后结构或目标已经变化。");
                result=await service.ApplyAsync(plan);if(!result.Succeeded||result.AppliedStatements!=2)throw new InvalidOperationException("两项索引未完整应用。");
                RelationIndexProfile.RequireChanges(await service.PreviewAsync(metadata),RelationIndexProfile.ExpectedChanges(baseline,options.ExpectedDatabase),true);
            }
            Console.WriteLine(JsonSerializer.Serialize(new{format=RelationIndexProfile.Name,runId=run,target=mode.Target,operation=mode.Operation,database=plan.Database,planSha256=hash,changes=plan.Changes,succeeded=true,appliedStatements=result?.AppliedStatements??0},RelationIndexProfile.Json));return 0;
        }
        catch(Exception error){Console.Error.WriteLine("索引元数据验收未通过："+error.GetType().Name+"；不自动重试。");return 1;}
    }
    /// <summary>只允许两端的预览、应用、复核与离线编译；不接收路径、SQL、账号或任意库名。</summary>
    public static (string Operation,string? Target) ParseMode(string[] args)
    {
        if(args.Length!=1)throw new ArgumentException("必须指定唯一固定动作。");
        if(args[0]=="offline")return("offline",null);
        var parts=args[0].Split('-');if(parts.Length!=2||parts[0] is not("preview" or "apply" or "verify")||parts[1] is not("java" or "csharp"))throw new ArgumentException("未知索引验收动作。");
        return(parts[0],parts[1]);
    }
    /// <summary>当前持续测试授权由协调器落实为独占租约；本进程不接受旧或未批准范围。</summary>
    public static string RequireRun(string? run,string? authorization)
    {
        if(authorization!="approved"||!Regex.IsMatch(run??"",@"\A[a-f0-9]{32}\z"))throw new UnauthorizedAccessException("缺少当前独占范围。");return run!;
    }
    /// <summary>复用同轮临时账号，仅将Database切到本轮另一库；原凭据不能指向非本轮资源。</summary>
    public static DatabaseOptions Options(string run,string target,string? expected,string? connection)
    {
        RequireRun(run,"approved");if(target is not("java" or "csharp"))throw new ArgumentException("未知目标。");
        if(string.IsNullOrWhiteSpace(connection))throw new UnauthorizedAccessException("缺少私有连接配置。");
        var original="metarill_fresh_"+run[..16]+"_paired_csharp";var parsed=new MySqlConnectionStringBuilder(connection);
        if(parsed.Server!="192.168.31.200"||parsed.Port!=3306||expected!=original||parsed.Database!=original||parsed.UserID!="mtf_"+run[..16]||string.IsNullOrEmpty(parsed.Password))throw new UnauthorizedAccessException("连接不是本轮临时账号和批准库。");
        parsed.Database="metarill_fresh_"+run[..16]+"_paired_"+target;
        return new(){Provider="mysql",ExpectedDatabase=parsed.Database,ConnectionString=parsed.ConnectionString};
    }
}
