using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using MySqlConnector;
namespace Metarill.Tools.PlatformFoundationUpgrade;

internal interface IUpgradeDatabase:IDisposable
{
    Task<UpgradePlan> PreviewAsync(string uuid,CancellationToken token);
    Task<SchemaApplyResult> ApplySchemaAsync(CancellationToken token);
    Task SeedAsync(CancellationToken token);
    Task<UpgradeVerification> VerifyAsync(string uuid,CancellationToken token);
}

/// <summary>实际数据库适配；DDL沿SchemaService，配置只沿已发布元数据的窄种子，不能执行审查文件自带SQL。</summary>
internal sealed class UpgradeDatabase: IUpgradeDatabase
{
    private readonly DatabaseSession session;private readonly SqlRowStore store;private readonly MetadataRepository repository;private SchemaService? schema;private readonly UpgradeDefinition definition;
    private SchemaChangePlan? prepared;private string? serverUuid;
    internal UpgradeDatabase(DatabaseOptions options,UpgradeDefinition definition)
    {this.definition=definition;session=new(options);store=new(session,definition.Registry.Platform);repository=new(store,definition.Registry);}
    /// <summary>私有设置只允许Server-200的metarill_dev；连接验证在ORM/网络创建前完成，不回显凭据。</summary>
    internal static DatabaseOptions ReadOptions(string settings)
    {
        using var data=JsonDocument.Parse(File.ReadAllBytes(settings));
        static JsonElement Property(JsonElement row,string name)=>row.EnumerateObject().Single(property=>property.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
        var options=JsonSerializer.Deserialize<DatabaseOptions>(Property(Property(data.RootElement,"Metarill"),"Database"),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("缺少数据库配置。");
        var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_dev"||parsed.Database!="metarill_dev"||parsed.Server is not("192.168.31.200" or "deve-server.yinzon.com"))throw new InvalidOperationException("维护工具仅允许指定Server-200开发平台库。");
        parsed.ConnectionTimeout=Math.Clamp(parsed.ConnectionTimeout,1u,10u);parsed.Pooling=false;var result=options with{ConnectionString=parsed.ConnectionString};new MySqlDatabaseProvider().Validate(result);return result;
    }
    /// <summary>同一实际快照核实例/项目及全部全局ID；已有记录（含墓碑）一律不覆盖。</summary>
    public async Task<UpgradePlan> PreviewAsync(string uuid,CancellationToken token)
    {
        if(!Guid.TryParseExact(uuid,"D",out var parsed)||parsed.ToString("D")!=uuid)throw new InvalidOperationException("实例UUID无效。");
        serverUuid=uuid;
        await session.InReadSnapshotAsync(async()=>{await RequireIdentityAsync(uuid,token);await RequireProjectAsync(false,token);foreach(var key in UpgradeDefinition.Keys)if((await store.ReadIncludingDeletedAsync(key.Table,Id(key.Id),token)).Count!=0)throw new InvalidOperationException("固定升级标识已经被占用，停止补缺。");return true;},token);
        schema=new(session,expectedServerUuid:uuid);prepared=await schema.PreviewAsync(definition.Desired,token);
        var plan=new UpgradePlan(UpgradePlanCodec.Profile,uuid,session.Options.ExpectedDatabase,definition.SourceSha256,prepared.BeforeFingerprint,prepared.DesiredFingerprint,definition.Rows,prepared.Changes);
        UpgradePlanCodec.Validate(plan);return plan;
    }
    /// <summary>仅执行本实例新预览且随后核过审查摘要的内部计划，不接收外部SQL。</summary>
    public Task<SchemaApplyResult> ApplySchemaAsync(CancellationToken token)=>(schema??throw new InvalidOperationException("尚未绑定结构服务实例。 ")).ApplyAsync(prepared??throw new InvalidOperationException("尚未重新预览升级结构。"),token);
    /// <summary>结构先行后，在配置事务内重新锁项目及十三个全局ID；窄种子整笔提交或回滚，墓碑也不能复活。</summary>
    public Task SeedAsync(CancellationToken token)=>UpgradeSeed.ApplyAsync(session,definition,serverUuid??throw new InvalidOperationException("尚未重新预览升级目标。"),token);
    /// <summary>新会话可独立调用此只读核验；不补数据、不执行SQL计划，必须十三条逐字段一致且结构零差异。</summary>
    public async Task<UpgradeVerification> VerifyAsync(string uuid,CancellationToken token)
    {
        var count=await session.InReadSnapshotAsync(async()=>{await RequireIdentityAsync(uuid,token);await RequireProjectAsync(false,token);return await RequireRowsAsync(token);},token);
        var verificationSchema=new SchemaService(session,expectedServerUuid:uuid);var plan=await verificationSchema.PreviewAsync(definition.Desired,token);if(plan.Changes.Count!=0)throw new InvalidOperationException("升级后的基础结构尚有差异。");
        return new(count,plan.Changes.Count,plan.DesiredFingerprint);
    }
    /// <summary>项目行必须是原有效平台；配置写入前按现有项目首锁顺序取当前行。</summary>
    private Task RequireProjectAsync(bool locked,CancellationToken token)=>UpgradeSeed.RequireProjectAsync(store,definition,locked,token);
    /// <summary>真实物理身份在每次目标事务重新核对，不能沿用源别名推测。</summary>
    private Task RequireIdentityAsync(string uuid,CancellationToken token)=>UpgradeSeed.RequireIdentityAsync(session,uuid,token);
    /// <summary>验证使用与窄种子写后相同的逐字段规则。</summary>
    private Task<int> RequireRowsAsync(CancellationToken token)=>UpgradeSeed.RequireRowsAsync(store,definition,token);
    private static Dictionary<string,object?> Id(string id)=>new(){{"id",long.Parse(id,CultureInfo.InvariantCulture)}};
    public void Dispose()=>session.Dispose();
}
