using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Data.Schema;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Tools.PlatformFoundationUpgrade;
using UpgradeProgram=Metarill.Tools.PlatformFoundationUpgrade.Program;
using Xunit;
namespace Metarill.Agent.Tests;

/// <summary>固定升级计划/阶段门禁及真实SqlRowStore窄种子事务；内存ADO不代替MySQL实际DDL。</summary>
public sealed class PlatformFoundationUpgradeTests
{
    private const string Uuid="11111111-2222-3333-4444-555555555555";
    /// <summary>实际发布源只能形成十三条固定对象及五张基础表，不包含页面/账号种子。</summary>
    [Fact]
    public void Published_definition_is_exact_and_contains_no_application_configuration_seed()
    {
        var source=new UpgradeDefinition(SourceRoot());Assert.Equal(13,source.Rows.Count);Assert.Equal(5,source.Desired.ConfigTables.Count);
        Assert.Single(source.Patch.ConfigColumns);Assert.Equal(4,source.Patch.ConfigIndexes.Count);Assert.Equal(8,source.Patch.ConfigIndexColumns.Count);
        Assert.Empty(source.Patch.ConfigTables);Assert.Empty(source.Patch.ConfigPages);Assert.Empty(source.Patch.YzAdminTypes);Assert.Empty(source.Patch.ConfigMenus);
        Assert.Equal(13,source.Records.Count);Assert.All(source.Rows,row=>Assert.Equal(64,row.SourceSha256.Length));
        Assert.All(source.Rows,row=>Assert.Equal("1608097412978577409",UpgradeDefinition.Text(source.Records[(row.Table,row.Id)],"yzProjectId")));
    }
    /// <summary>缺少人工确认摘要时，数据库工厂连一次都不能创建。</summary>
    [Fact]
    public async Task Confirmation_mismatch_never_creates_a_database()
    {
        var opens=0;await Assert.ThrowsAsync<InvalidOperationException>(()=>UpgradeCoordinator.ApplyAsync(Plan(),new string('f',64),()=>{opens++;throw new InvalidOperationException();},(_,_)=>{},CancellationToken.None));Assert.Equal(0,opens);
    }
    /// <summary>实时源/目标前指纹变化必须在任何结构/配置写入之前停止。</summary>
    [Fact]
    public async Task Live_preview_change_never_reaches_writes()
    {
        var plan=Plan();var fake=new Fake(plan with{BeforeFingerprint=new string('c',64)});var events=new List<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>fake,(phase,_)=>events.Add(phase),CancellationToken.None));
        Assert.Equal(new[]{"preview"},fake.Calls);Assert.Contains("stopped",events);
    }
    /// <summary>配置只能在DDL成功后开始，最终核验必须重新打开已关闭首会话之外的新会话。</summary>
    [Fact]
    public async Task Stage_order_and_independent_verification_use_two_database_instances()
    {
        var plan=Plan();var first=new Fake(plan);var second=new Fake(plan);var opened=0;var events=new List<string>();
        await UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>{opened++;if(opened==2){Assert.True(first.Disposed);return second;}return first;},(phase,_)=>events.Add(phase),CancellationToken.None);
        Assert.Equal(2,opened);Assert.Equal(new[]{"preview","ddl","metadata"},first.Calls);Assert.Equal(new[]{"verify"},second.Calls);
        Assert.True(events.IndexOf("ddl-committed")<events.IndexOf("metadata-started"));Assert.Equal("verified",events.Last());
    }
    /// <summary>DDL失败保留已确认数量与未确认的在途结果，不能继续配置或自行补偿。</summary>
    [Fact]
    public async Task Ddl_failure_reports_partial_result_and_never_seeds_metadata()
    {
        var plan=Plan();var fake=new Fake(plan){FailAt="ddl"};JsonElement stopped=default;
        await Assert.ThrowsAsync<SchemaApplyException>(()=>UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>fake,(phase,data)=>{if(phase=="stopped")stopped=JsonSerializer.SerializeToElement(data);},CancellationToken.None));
        Assert.Equal(new[]{"preview","ddl"},fake.Calls);Assert.Equal(2,stopped.GetProperty("knownCompletedStatements").GetInt32());Assert.True(stopped.GetProperty("ddlInFlightOutcomeUnknown").GetBoolean());Assert.Equal("not-started",stopped.GetProperty("metadataState").GetString());
    }
    /// <summary>元数据提交故障不声称DDL回滚，结果未知时要求独立检查而不是自动重试。</summary>
    [Fact]
    public async Task Metadata_failure_keeps_committed_schema_and_reports_unknown_metadata_outcome()
    {
        var plan=Plan();var fake=new Fake(plan){FailAt="metadata"};JsonElement stopped=default;
        await Assert.ThrowsAsync<IOException>(()=>UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>fake,(phase,data)=>{if(phase=="stopped")stopped=JsonSerializer.SerializeToElement(data);},CancellationToken.None));
        Assert.Equal(5,stopped.GetProperty("knownCompletedStatements").GetInt32());Assert.Equal("outcome-unknown",stopped.GetProperty("metadataState").GetString());Assert.DoesNotContain("verify",fake.Calls);
    }
    /// <summary>回执落盘故障应停止后续写入，错误记录再次失败不能掩盖原始异常。</summary>
    [Fact]
    public async Task Receipt_failure_after_ddl_stops_metadata_without_hiding_original_error()
    {
        var plan=Plan();var fake=new Fake(plan);
        var error=await Assert.ThrowsAsync<IOException>(()=>UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>fake,(phase,_)=>{if(phase is "ddl-committed" or "stopped")throw new IOException(phase);},CancellationToken.None));
        Assert.Equal("ddl-committed",error.Message);Assert.DoesNotContain("metadata",fake.Calls);Assert.Equal("IOException",error.Data["ReceiptFailureType"]);
    }
    /// <summary>结构执行回执数量不完整时，即使声称Succeeded也不能继续配置。</summary>
    [Fact]
    public async Task Incomplete_ddl_receipt_does_not_reach_metadata()
    {
        var plan=Plan();var fake=new Fake(plan){AppliedStatements=4};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>fake,(_,_)=>{},CancellationToken.None));Assert.DoesNotContain("metadata",fake.Calls);
    }
    /// <summary>新连接的行数、差异或指纹任一不符，最终状态都不能记为verified。</summary>
    [Theory]
    [InlineData("rows")][InlineData("changes")][InlineData("fingerprint")]
    public async Task Independent_verification_failure_is_never_success(string kind)
    {
        var plan=Plan();var first=new Fake(plan);var second=new Fake(plan){Verification=new(kind=="rows"?12:13,kind=="changes"?1:0,kind=="fingerprint"?new string('e',64):plan.DesiredFingerprint)};var opens=0;var phases=new List<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>UpgradeCoordinator.ApplyAsync(plan,UpgradePlanCodec.Digest(plan),()=>++opens==1?first:second,(phase,_)=>phases.Add(phase),CancellationToken.None));
        Assert.Equal(2,opens);Assert.Contains("metadata-committed",phases);Assert.Contains("stopped",phases);Assert.DoesNotContain("verified",phases);
    }
    /// <summary>私有配置先限主机/库名/驱动，错误目标在创建ORM前拒绝；不改变已有TLS设置。</summary>
    [Theory]
    [InlineData("valid")][InlineData("host")][InlineData("database")][InlineData("connection-database")][InlineData("provider")]
    public void Settings_are_limited_to_reviewed_development_target(string kind)
    {
        var path=Path.Combine(Path.GetTempPath(),"metarill-foundation-settings-"+Guid.NewGuid().ToString("N")+".json");
        var settings=new{Metarill=new{Database=new{Provider=kind=="provider"?"sqlite":"mysql",ExpectedDatabase=kind=="database"?"yzp":"metarill_dev",ConnectionString="Server="+(kind=="host"?"127.0.0.1":"192.168.31.200")+";Database="+(kind=="connection-database"?"yzp":"metarill_dev")+";User ID=unused;Password=unused;SSL Mode=Required;Connection Timeout=0;"}}};
        try{File.WriteAllText(path,JsonSerializer.Serialize(settings));if(kind=="valid"){var options=UpgradeDatabase.ReadOptions(path);var parsed=new MySqlConnector.MySqlConnectionStringBuilder(options.ConnectionString);Assert.Equal((uint)1,parsed.ConnectionTimeout);Assert.Equal(MySqlConnector.MySqlSslMode.Required,parsed.SslMode);Assert.False(parsed.Pooling);}else Assert.Throws<InvalidOperationException>(()=>UpgradeDatabase.ReadOptions(path));}
        finally{File.Delete(path);}
    }
    /// <summary>真实窄种子写后逐字段回读，只有三个配置表十三个对象新增，其它已存内容保持。</summary>
    [Fact]
    public async Task Real_seed_inserts_exact_thirteen_rows_without_reading_unrelated_configuration_tables()
    {
        using var f=new PrincipalRealmFixture();var source=new UpgradeDefinition(SourceRoot());await Prepare(f,source);f.Provider.Commands.Clear();
        await UpgradeSeed.ApplyAsync(f.Session,source,Uuid,CancellationToken.None);
        Assert.Equal(13,await UpgradeSeed.RequireRowsAsync(f.Store,source,CancellationToken.None));
        Assert.Equal(13,f.Provider.Commands.Count(sql=>sql.StartsWith("INSERT INTO ",StringComparison.Ordinal)));
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.Contains("FROM `config_page`",StringComparison.Ordinal)||sql.Contains("FROM `yz_admin_type`",StringComparison.Ordinal));
        Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("UPDATE ",StringComparison.Ordinal)||sql.StartsWith("DELETE ",StringComparison.Ordinal));
    }
    /// <summary>全局已有/墓碑标识或平台被删除均阻止插入，不能把窄补缺变成恢复或覆盖。</summary>
    [Theory]
    [InlineData("existing")][InlineData("tombstone")][InlineData("deleted-project")][InlineData("wrong-server")]
    public async Task Real_seed_rejects_conflicts_without_any_mutation(string kind)
    {
        using var f=new PrincipalRealmFixture();var source=new UpgradeDefinition(SourceRoot());await Prepare(f,source);
        if(kind is "existing" or "tombstone")f.Add("config_column",("id",2300000000000000401L),("yz_project_id",999L),("delete_no",kind=="tombstone"?DateTime.UtcNow:null));
        if(kind=="deleted-project")f.Row("yz_project",long.Parse(f.PlatformProject,CultureInfo.InvariantCulture))["delete_no"]=DateTime.UtcNow;
        if(kind=="wrong-server")f.Provider.PhysicalIdentity=f.Provider.PhysicalIdentity with{ServerId="other"};
        var before=JsonSerializer.Serialize(f.Provider.Rows);f.Provider.Commands.Clear();await Assert.ThrowsAsync<InvalidOperationException>(()=>UpgradeSeed.ApplyAsync(f.Session,source,Uuid,CancellationToken.None));
        Assert.Equal(before,JsonSerializer.Serialize(f.Provider.Rows));Assert.DoesNotContain(f.Provider.Commands,sql=>sql.StartsWith("INSERT ",StringComparison.Ordinal));
    }
    /// <summary>真实事务在索引字段阶段失败，应回滚已插入的字段/索引和索引字段，不能留下半批配置。</summary>
    [Fact]
    public async Task Real_seed_failure_rolls_back_all_thirteen_metadata_objects()
    {
        using var f=new PrincipalRealmFixture();var source=new UpgradeDefinition(SourceRoot());await Prepare(f,source);var before=JsonSerializer.Serialize(f.Provider.Rows);var written=0;
        f.Provider.BeforeWrite=sql=>{if(sql.StartsWith("INSERT INTO `config_index_column`",StringComparison.Ordinal)&&++written==3)throw new IOException("注入索引字段故障");};
        await Assert.ThrowsAsync<IOException>(()=>UpgradeSeed.ApplyAsync(f.Session,source,Uuid,CancellationToken.None));Assert.Equal(before,JsonSerializer.Serialize(f.Provider.Rows));
    }
    /// <summary>空种子不再扫描任意配置表，新锁入口拒绝事务外调用。</summary>
    [Fact]
    public async Task Empty_seed_and_unscoped_lock_have_no_configuration_queries()
    {
        using var f=new PrincipalRealmFixture();await new MetadataRepository(f.Store,f.Registry).SeedAsync(new());Assert.Empty(f.Provider.Commands);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Store.ReadConfigurationSeedForUpdateAsync("config_column",1));Assert.Empty(f.Provider.Commands);
        await f.Session.InTransactionAsync(async()=>{await Assert.ThrowsAsync<ArgumentException>(()=>f.Store.ReadConfigurationSeedForUpdateAsync("yz_manager",1));await Assert.ThrowsAsync<ArgumentException>(()=>f.Store.ReadConfigurationSeedForUpdateAsync("config_index",0));return true;});
    }
    /// <summary>计划文件不能夹带未知属性、重复属性、额外对象或任意SQL，合法文件可稳定往返。</summary>
    [Theory]
    [InlineData("valid")][InlineData("unknown")][InlineData("duplicate")][InlineData("duplicate-case")][InlineData("sql")][InlineData("row")][InlineData("database")]
    public void Plan_file_validation_is_bound_to_fixed_scope(string kind)
    {
        var path=Path.Combine(Path.GetTempPath(),"metarill-foundation-plan-"+Guid.NewGuid().ToString("N")+".json");var plan=Plan();var text=JsonSerializer.Serialize(plan,UpgradePlanCodec.Json);var data=JsonNode.Parse(text)!;
        try
        {
            if(kind=="unknown")data["ignoreGuards"]=true;
            if(kind=="sql")data["changes"]![0]!["sql"]="DROP TABLE yz_manager";
            if(kind=="row")data["rows"]![0]!["id"]="1";
            if(kind=="database")data["database"]="yzp";
            text=kind is "duplicate" or "duplicate-case"?"{\""+(kind=="duplicate"?"profile":"Profile")+"\":\"ignored\","+text[1..]:data.ToJsonString();File.WriteAllText(path,text);
            if(kind=="valid")Assert.Equal(UpgradePlanCodec.Digest(plan),UpgradePlanCodec.Digest(UpgradePlanCodec.Read(path)));
            else Assert.ThrowsAny<Exception>(()=>UpgradePlanCodec.Read(path));
        }
        finally{File.Delete(path);}
    }
    /// <summary>维护命令不允许遗失确认/回执、旧宽同步开关或参数重复。</summary>
    [Fact]
    public void Command_parser_rejects_incomplete_and_unknown_actions()
    {
        foreach(var args in new[]{Array.Empty<string>(),new[]{"apply"},new[]{"--synchronize-development-seed"},new[]{"preview","--settings","x","--settings","y"}})Assert.Throws<ArgumentException>(()=>UpgradeProgram.Parse(args));
    }
    /// <summary>回执只新建且逐行可读，不覆盖已有文件。</summary>
    [Fact]
    public void Receipt_creation_never_overwrites_existing_audit_file()
    {
        var path=Path.Combine(Path.GetTempPath(),"metarill-foundation-receipt-"+Guid.NewGuid().ToString("N")+".jsonl");
        try{using(var receipt=new UpgradeReceipt(path))receipt.Record("test",new{readOnly=true});var before=File.ReadAllBytes(path);Assert.Throws<IOException>(()=>new UpgradeReceipt(path));Assert.Equal(before,File.ReadAllBytes(path));}
        finally{File.Delete(path);}
    }
    private static UpgradePlan Plan()=>new(UpgradePlanCodec.Profile,Uuid,"metarill_dev",new string('a',64),new string('b',64),new string('d',64),UpgradeDefinition.Keys.Select(key=>new UpgradeRow(key.Table,key.Id,new string('a',64))).ToArray(),UpgradeDefinition.AllowedChanges);
    /// <summary>从实际平台先形成完整元数据，再仅移除本次十三个对象，模拟当前已核缺口。</summary>
    private static async Task Prepare(PrincipalRealmFixture f,UpgradeDefinition source)
    {
        await new MetadataRepository(f.Store,f.Registry).SeedAsync(f.Registry.Platform);
        foreach(var key in UpgradeDefinition.Keys)f.Provider.Rows[key.Table].RemoveAll(row=>SqlRowStore.Text(row,"id")==key.Id);
        f.Provider.PhysicalIdentity=f.Provider.PhysicalIdentity with{ServerId=Uuid,Database="metarill_dev"};
    }
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    /// <summary>阶段故障替身仅验证编排，不伪装MySQL或源数据写入；真实窄种子另有上述ADO事务用例。</summary>
    private sealed class Fake(UpgradePlan plan):IUpgradeDatabase
    {
        internal string? FailAt {get;init;}internal int AppliedStatements {get;init;}=5;internal UpgradeVerification? Verification {get;init;}internal List<string> Calls {get;}=[];internal bool Disposed {get;private set;}
        public Task<UpgradePlan> PreviewAsync(string uuid,CancellationToken token){Calls.Add("preview");return Task.FromResult(plan);}
        public Task<SchemaApplyResult> ApplySchemaAsync(CancellationToken token){Calls.Add("ddl");if(FailAt=="ddl")throw new SchemaApplyException(new("offline","metarill_dev",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,false,2,"Injected"),new IOException());return Task.FromResult(new SchemaApplyResult("offline","metarill_dev",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,true,AppliedStatements,null));}
        public Task SeedAsync(CancellationToken token){Calls.Add("metadata");if(FailAt=="metadata")throw new IOException();return Task.CompletedTask;}
        public Task<UpgradeVerification> VerifyAsync(string uuid,CancellationToken token){Calls.Add("verify");return Task.FromResult(Verification??new UpgradeVerification(13,0,plan.DesiredFingerprint));}
        public void Dispose()=>Disposed=true;
    }
}
