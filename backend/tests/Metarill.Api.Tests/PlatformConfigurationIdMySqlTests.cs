using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Platform.Generated.Entities;
using Metarill.Runtime;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Api.Tests;

/// <summary>缺少专属租约/私有配置/pin时明确跳过；存在租约也须在连接之前再次验证批准和未使用状态。</summary>
public sealed class ConfigurationIdMySqlFactAttribute:FactAttribute
{
    /// <summary>只检查本地配置是否存在，不反射运行测试体或连接数据库。</summary>
    public ConfigurationIdMySqlFactAttribute()
    {if(new[]{"METARILL_TEST_SETTINGS","METARILL_ROLE_READ_INSTANCE_PIN","METARILL_CONFIGURATION_ID_MYSQL_LEASE"}.Any(key=>!File.Exists(Environment.GetEnvironmentVariable(key))))Skip="需要隔离业务MySQL配置、实例pin和本轮明确批准的三表租约。";}
}

/// <summary>只验证真实六字段的数据库类型和实际生成实体投影，不操作旧平台表、数据库账号或权限。</summary>
public sealed class PlatformConfigurationIdMySqlTests
{
    private const long Id=9007199254740993L,Target=9007199254742001L;
    private static readonly string[] OriginalNames=["config_child","config_column","config_page_column"];
    private static readonly string[] Fields=["1679391146044956674","1670726646479638529","1670726646546747393","2200000000000000135","2200000000000000137","2200000000000000139"];
    /// <summary>授权只覆盖固定三表；pending、已用租约、元数据漂移或改名均在任何数据库连接前拒绝。</summary>
    private static string RequireLease(JsonElement lease,string metadataSha)
    {
        if(lease.GetProperty("authorizationStatus").GetString()!="approved"||lease.GetProperty("leaseStatus").GetString()!="not-started")throw new UnauthorizedAccessException("三表租约尚未批准或已使用，未连接数据库。");
        var run=lease.GetProperty("runId").GetString()!;
        if(!Regex.IsMatch(run,"^[a-f0-9]{32}$")||lease.GetProperty("database").GetString()!="metarill_business_test"||lease.GetProperty("metadataSha256").GetString()!=metadataSha)throw new InvalidOperationException("租约来源或目标不匹配。");
        if(!lease.GetProperty("tables").EnumerateArray().Select(value=>value.GetString()).SequenceEqual(OriginalNames.Select(name=>"mt_idt_"+run[..12]+"_"+name)))throw new InvalidOperationException("租约三表名称不匹配。");
        return run;
    }
    /// <summary>真实校验门禁，不读取私有文件或建立连接；正例仅验证本地计划格式，不授权执行。</summary>
    [Fact]
    public void Lease_gate_rejects_pending_used_renamed_or_changed_inputs_before_connection()
    {
        var run=new string('a',32);var hash=new string('b',64);var plan=new{runId=run,database="metarill_business_test",tables=OriginalNames.Select(name=>"mt_idt_"+run[..12]+"_"+name),authorizationStatus="approved",leaseStatus="not-started",metadataSha256=hash};
        var node=JsonSerializer.SerializeToNode(plan)!;Assert.Equal(run,RequireLease(JsonSerializer.SerializeToElement(node),hash));
        foreach(var (key,value)in new[]{("authorizationStatus","pending"),("leaseStatus","ended"),("database","production"),("metadataSha256","changed")})
        {var changed=node.DeepClone();changed[key]=value;Assert.ThrowsAny<Exception>(()=>RequireLease(JsonSerializer.SerializeToElement(changed),hash));}
        var renamed=node.DeepClone();renamed["tables"]![0]="other_table";Assert.Throws<InvalidOperationException>(()=>RequireLease(JsonSerializer.SerializeToElement(renamed),hash));
    }
    /// <summary>原SchemaService显式预览/安装，仓储真实归一、生成实体列读取、空选择清除及独占结构清理。</summary>
    [ConfigurationIdMySqlFact]
    public async Task Actual_reference_types_roundtrip_in_approved_owned_mysql_tables()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));var leasePath=Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_CONFIGURATION_ID_MYSQL_LEASE")!);
        if(leasePath!=Path.GetFullPath(Path.Combine(root,".local/platform-id-types/mysql-lease.json")))throw new InvalidOperationException("租约只允许本轮固定私有路径。");
        var metadata=await File.ReadAllBytesAsync(Path.Combine(root,"backend/metadata/platform.json"));
        // 在独占锁中重新读取租约，不能用加锁前的旧not-started快照重用已结束租约。
        using var ownershipLock=new FileStream(leasePath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var leaseNode=JsonNode.Parse(await File.ReadAllTextAsync(leasePath))!;using var lease=JsonDocument.Parse(leaseNode.ToJsonString());
        var run=RequireLease(lease.RootElement,Convert.ToHexStringLower(SHA256.HashData(metadata)));
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var address=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||address.Database!="metarill_business_test"||address.Server!="deve-server.yinzon.com"||address.Port!=3306)throw new InvalidOperationException("只允许固定Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));
        foreach(var key in new[]{"expectedHostname","expectedServerUuid"})Assert.Equal(lease.RootElement.GetProperty("instancePin").GetProperty(key).GetString(),pin.RootElement.GetProperty(key).GetString());
        var source=JsonSerializer.Deserialize<MetadataCatalog>(metadata,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var prefix="mt_idt_"+run[..12];var evidence=Path.Combine(root,".local/platform-id-types/mysql-"+run);Directory.CreateDirectory(evidence);
        var tables=source.ConfigTables.Where(table=>OriginalNames.Contains(table.TableName)).ToList();Assert.Equal(3,tables.Count);var names=tables.ToDictionary(table=>table.TableName,table=>prefix+"_"+table.TableName);var ids=tables.Select(table=>table.Id).ToHashSet();
        var columns=source.ConfigColumns.Where(column=>ids.Contains(column.ConfigTableId)&&(Fields.Contains(column.Id)||column.ColumnName is "id" or "delete_no" or "yz_project_id")).ToList();Assert.Equal(6,columns.Count(column=>Fields.Contains(column.Id)));
        foreach(var column in columns.Where(column=>Fields.Contains(column.Id)))Assert.Equal("bigint",MySqlSchemaPlanner.CompileColumn(column).StoreType);
        foreach(var table in tables)table.TableName=names[table.TableName];
        // 本专项只消费真实物理列属性；不把控件声明解释为新的关系写入或授权能力。
        foreach(var column in columns){column.ControlKind="";column.ControlDetail="";column.LinkConfigTableId="";column.Extra.Clear();}
        var catalog=new MetadataCatalog{YzProjectId=source.YzProjectId,ConfigTables=tables,ConfigColumns=columns};var owned=new HashSet<string>();var passed=false;var cleaned=false;
        using var session=new DatabaseSession(options);var schema=new SchemaService(session);var store=new SqlRowStore(session,catalog);
        await VerifyInstance(options,pin.RootElement);Assert.Empty((await schema.InspectAsync(catalog)).Tables);await Receipt("intent",new{run,names,existingTablesAbsent=true});State("running");
        try
        {
            var plan=await schema.PreviewAsync(catalog);Assert.Equal(3,plan.Changes.Count);Assert.All(plan.Changes,change=>Assert.Equal("createTable",change.Kind));
            try{var applied=await schema.ApplyAsync(plan);foreach(var change in plan.Changes.Take(applied.AppliedStatements))owned.Add(change.Table);Assert.True(applied.Succeeded);}
            catch(SchemaApplyException error){foreach(var change in plan.Changes.Take(error.Result.AppliedStatements))owned.Add(change.Table);throw;}
            await Receipt("created",new{run,owned=owned.Order().ToArray()});Assert.Empty((await schema.PreviewAsync(catalog)).Changes);
            await session.InTransactionAsync(async()=>{foreach(var table in tables){var values=new Dictionary<string,object?>{{"id",Id},{"yz_project_id",long.Parse(source.YzProjectId)}};foreach(var field in columns.Where(column=>column.ConfigTableId==table.Id&&Fields.Contains(column.Id)))values[field.ColumnName]=JsonSerializer.SerializeToElement(Target.ToString());await store.InsertAsync(table.TableName,values);}return true;});
            await Check(false);
            await session.InTransactionAsync(async()=>{foreach(var table in tables)Assert.Equal(1,await store.UpdateAsync(table.TableName,Id,columns.Where(column=>column.ConfigTableId==table.Id&&Fields.Contains(column.Id)).ToDictionary(column=>column.ColumnName,column=>(object?)"")));return true;});
            await Check(true);passed=true;await Receipt("verified",new{run,passed,fields=6,tables=3,generatedEntityProjection=true,stringIdExact=true,emptySelectionNull=true});
        }
        catch(Exception error){await Receipt("failure",new{run,passed=false,errorType=error.GetType().Name});throw;}
        finally
        {
            try
            {
                await VerifyInstance(options,pin.RootElement);var actual=await schema.InspectAsync(catalog);var desired=MySqlSchemaPlanner.Compile(catalog,options.ExpectedDatabase);
                foreach(var table in actual.Tables.Where(table=>owned.Contains(table.Name)))
                {
                    Assert.Contains(table.Name,names.Values);var expected=desired.Tables.Single(item=>item.Name==table.Name);
                    Assert.Equal(expected.Columns.OrderBy(column=>column.Name).Select(column=>(column.Name,column.StoreType,column.Nullable,column.PrimaryKey)),table.Columns.OrderBy(column=>column.Name).Select(column=>(column.Name,column.StoreType,column.Nullable,column.PrimaryKey)));
                    var rows=await store.ReadIncludingDeletedAsync(table.Name);Assert.InRange(rows.Count,passed?1:0,1);
                    foreach(var row in rows){Assert.Equal(Id,SqlRowStore.Id(row));Assert.Equal(long.Parse(source.YzProjectId),SqlRowStore.Id(row,"yz_project_id"));Assert.Null(row["delete_no"]);foreach(var field in columns.Where(column=>Fields.Contains(column.Id)&&column.ConfigTableId==tables.Single(item=>item.TableName==table.Name).Id))Assert.True(row[field.ColumnName] is null||Convert.ToInt64(row[field.ColumnName],CultureInfo.InvariantCulture)==Target);}
                    await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="DROP TABLE "+session.Provider.QuoteIdentifier(table.Name);await q.ExecuteNonQueryAsync();
                }
                await VerifyInstance(options,pin.RootElement);await using var check=new MySqlConnection(options.ConnectionString);await check.OpenAsync();
                foreach(var name in names.Values){await using var q=check.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name";q.Parameters.AddWithValue("@name",name);Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}
                cleaned=true;await Receipt("cleanup",new{run,cleaned,independentAbsenceVerified=true,owned=owned.Count,passed});
            }
            finally{State(cleaned?"ended":"cleanup-unconfirmed");}
        }
        // 使用当前生产生成实体的Column属性及真实驱动读取，不重写实体/Mapper。
        async Task Check(bool cleared)
        {
            var child=Assert.Single(await session.Select<ConfigChild>().AsTable((type,name)=>names["config_child"]).Where(row=>row.Id==Id).ToListAsync(row=>new{row.Id,row.ChildConfigPageId}));
            var column=Assert.Single(await session.Select<ConfigColumn>().AsTable((type,name)=>names["config_column"]).Where(row=>row.Id==Id).ToListAsync(row=>new{row.Id,row.LinkConfigTableId,row.LinkConfigPageId}));
            var field=Assert.Single(await session.Select<ConfigPageColumn>().AsTable((type,name)=>names["config_page_column"]).Where(row=>row.Id==Id).ToListAsync(row=>new{row.Id,row.LinkConfigTableId,row.LinkConfigPageId,row.CombineSuperColumnId}));
            foreach(var record in new object[]{child,column,field}){var json=JsonSerializer.SerializeToElement(record,JsonDefaults.Options);Assert.Equal(Id.ToString(),json.GetProperty("id").GetString());foreach(var value in json.EnumerateObject().Where(value=>value.Name!="id")){if(cleared)Assert.Equal(JsonValueKind.Null,value.Value.ValueKind);else Assert.Equal(Target.ToString(),value.Value.GetString());}}
        }
        // 只保存本轮私有阶段证明，不记录连接或凭据。
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
        // 已结束或清理未确认的租约不允许重新执行远端验收。
        void State(string state){leaseNode["leaseStatus"]=state;File.WriteAllText(leasePath,leaseNode.ToJsonString(new JsonSerializerOptions{WriteIndented=true})+"\n");}
    }
    /// <summary>不回显连接串，实际库、主机及UUID必须与本轮明确pin一致。</summary>
    private static async Task VerifyInstance(DatabaseOptions options,JsonElement pin)
    {
        await using var c=new MySqlConnection(options.ConnectionString);await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await q.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync()&&r.GetString(0)==options.ExpectedDatabase&&r.GetString(1)==pin.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.GetProperty("expectedServerUuid").GetString(),"实例不匹配，禁止操作。");
    }
}
