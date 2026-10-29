using System.Text;
using System.Text.Json;
namespace Metarill.Tools.PlatformFoundationUpgrade;

/// <summary>一次性固定开发升级维护工具；普通生成CLI、原Web页面和服务启动均不增加自动写库行为。</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding=new UTF8Encoding(false);
        if(args.Length==1&&args[0] is "--help" or "-h"){Console.WriteLine("固定开发平台升级：preview --settings 私有配置 --source-root 源根 --server-uuid 本轮UUID --plan 新计划文件；apply/verify --settings 私有配置 --source-root 源根 --plan 计划文件 --confirm-sha 审查摘要 --receipt 新回执文件。apply会新增结构与十三条元数据，必须显式获准后执行。");return 0;}
        try
        {
            var command=Parse(args);
            // apply/verify先读审查合同并核确认摘要，再读取私有设置或创建数据库服务。
            UpgradePlan? reviewed=null;
            if(command.Mode!="preview"){reviewed=UpgradePlanCodec.Read(command.Values["--plan"]);UpgradePlanCodec.Confirm(reviewed,command.Values["--confirm-sha"]);}
            var definition=new UpgradeDefinition(command.Values["--source-root"]);
            if(reviewed is not null&&definition.SourceSha256!=reviewed.SourceSha256)throw new InvalidOperationException("升级源已改变，请重新预览。");
            var options=UpgradeDatabase.ReadOptions(command.Values["--settings"]);
            IUpgradeDatabase Open()=>new UpgradeDatabase(options,definition);
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
            if(command.Mode=="preview")
            {
                using var database=Open();var plan=await database.PreviewAsync(command.Values["--server-uuid"],timeout.Token);
                WriteNew(command.Values["--plan"],JsonSerializer.SerializeToUtf8Bytes(plan,UpgradePlanCodec.Json));
                Console.WriteLine($"只读计划已保存；确认摘要 {UpgradePlanCodec.Digest(plan)}；固定元数据 {plan.Rows.Count} 条，新增结构 {plan.Changes.Count} 条。尚未执行写入。");return 0;
            }
            using var receipt=new UpgradeReceipt(command.Values["--receipt"]);
            receipt.Record("opened",new{mode=command.Mode,planSha256=command.Values["--confirm-sha"],sourceSha256=definition.SourceSha256});
            if(command.Mode=="verify")
            {
                using var database=Open();var result=await database.VerifyAsync(reviewed!.ServerUuid,timeout.Token);
                if(result.Fingerprint!=reviewed.DesiredFingerprint)throw new InvalidOperationException("独立核验目标与审查计划不一致。");
                receipt.Record("verified",result);Console.WriteLine("独立只读核验通过，结构零差异且十三条固定元数据匹配。");return 0;
            }
            await UpgradeCoordinator.ApplyAsync(reviewed!,command.Values["--confirm-sha"],Open,receipt.Record,timeout.Token);
            Console.WriteLine("固定升级及新会话核验通过；原服务未重启，未部署。");return 0;
        }
        catch(Exception error)
        {
            // 不输出连接异常正文或私有配置；阶段及已确认数量由提前创建的回执记录。
            Console.Error.WriteLine("固定升级维护已停止（"+error.GetType().Name+"）；检查回执和实际状态，禁止盲目重试或自动回退。");return 1;
        }
    }
    internal sealed record Command(string Mode,IReadOnlyDictionary<string,string> Values);
    /// <summary>拒绝混合模式、重复/未知参数与空值；预览必须明确本轮UUID，执行必须明确审查摘要和新回执。</summary>
    internal static Command Parse(string[] args)
    {
        if(args.Length<1||args[0] is not("preview" or "apply" or "verify"))throw new ArgumentException("必须明确preview、apply或verify。");
        var mode=args[0];var keys=mode=="preview"?new[]{"--settings","--source-root","--server-uuid","--plan"}:new[]{"--settings","--source-root","--plan","--confirm-sha","--receipt"};
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        for(var index=1;index<args.Length;index+=2)
        {
            if(index+1>=args.Length||!keys.Contains(args[index],StringComparer.Ordinal)||string.IsNullOrWhiteSpace(args[index+1])||args[index+1].StartsWith("--",StringComparison.Ordinal)||!values.TryAdd(args[index],args[index+1]))throw new ArgumentException("维护参数无效或重复。");
        }
        if(values.Count!=keys.Length)throw new ArgumentException("维护参数缺失。");
        if(mode=="preview"&&(!Guid.TryParseExact(values["--server-uuid"],"D",out var uuid)||uuid.ToString("D")!=values["--server-uuid"]))throw new ArgumentException("实例UUID必须来自本轮核验。");
        return new(mode,values);
    }
    /// <summary>计划输出必须为新文件，先落盘成功再报告，不覆盖既有审查材料。</summary>
    private static void WriteNew(string path,byte[] content)
    {using var stream=new FileStream(Path.GetFullPath(path),FileMode.CreateNew,FileAccess.Write,FileShare.Read);stream.Write(content);stream.Flush(true);}
}

/// <summary>回执在任何写库前创建，分阶段即时落盘；只存固定计划信息，不存私有设置或原始异常正文。</summary>
internal sealed class UpgradeReceipt:IDisposable
{
    private readonly FileStream stream;private readonly StreamWriter writer;
    internal UpgradeReceipt(string path){stream=new(Path.GetFullPath(path),FileMode.CreateNew,FileAccess.Write,FileShare.Read);writer=new(stream,new UTF8Encoding(false),leaveOpen:true);}
    /// <summary>每条阶段记录都强制落盘，磁盘写失败立即阻止后续阶段。</summary>
    internal void Record(string phase,object? data){writer.WriteLine(JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,phase,data},UpgradePlanCodec.Json));writer.Flush();stream.Flush(true);}
    public void Dispose(){writer.Dispose();stream.Dispose();}
}
