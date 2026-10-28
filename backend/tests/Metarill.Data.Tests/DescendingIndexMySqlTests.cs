using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using MySqlConnector;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>正式用例只消费本轮租约；不借通用MySQL变量执行新DDL。</summary>
public sealed class DescendingIndexMySqlFactAttribute:FactAttribute
{
    /// <summary>只检查本地配置存在，不连接数据库或取得授权。</summary>
    public DescendingIndexMySqlFactAttribute()
    {if(new[]{"METARILL_TEST_SETTINGS","METARILL_ROLE_READ_INSTANCE_PIN","METARILL_DESC_INDEX_LEASE"}.Any(key=>!File.Exists(Environment.GetEnvironmentVariable(key))))Skip="需要本轮明确授权的单表租约、业务库配置和实例pin。";}
}

/// <summary>单表实库索引验收；源码准备和离线门禁不代表正式用例已获授权或执行。</summary>
public sealed class DescendingIndexMySqlTests
{
    private const string Database="metarill_business_test",Scenario="descending-index-v1";
    private static readonly (long Id,int Bucket,decimal Score,string Name)[] Rows=[(9007199254740993,1,10.50m,"样本甲"),(9007199254740994,1,30.25m,"样本乙"),(9007199254740995,2,5.00m,"样本丙")];
    /// <summary>第一条连接前核对批准、单次状态、精确对象、结构和样本预算。</summary>
    private static string RequireLease(JsonElement lease)
    {
        if(lease.GetProperty("authorizationStatus").GetString()!="approved"||lease.GetProperty("leaseStatus").GetString()!="not-started")throw new UnauthorizedAccessException("租约未批准或已使用，未连接数据库。");
        var run=lease.GetProperty("runId").GetString()??"";
        if(run.Length!=32||run.Any(c=>!char.IsAsciiHexDigit(c)||char.IsUpper(c))||lease.GetProperty("scenario").GetString()!=Scenario||lease.GetProperty("database").GetString()!=Database)throw new InvalidOperationException("来源或目标不匹配。");
        if(!lease.GetProperty("tables").EnumerateArray().Select(item=>item.GetString()).SequenceEqual(new[]{Name(run)})||lease.GetProperty("maximumRows").GetInt32()!=3||lease.GetProperty("columns").GetInt32()!=5||lease.GetProperty("initialIndexes").GetInt32()!=2||lease.GetProperty("finalIndexes").GetInt32()!=3||!lease.GetProperty("allowDropOwnedTable").GetBoolean())throw new InvalidOperationException("单表结构、样本或清理范围不匹配。");
        if(lease.GetProperty("metadataSha256").GetString()!=Hash(run))throw new InvalidOperationException("物理计划已变化，不能扩用旧授权。");
        return run;
    }
    /// <summary>纯离线门禁；批准形状正例不是对远端操作的授权，不读取任何私有文件。</summary>
    [Fact]
    public void Exact_gate_rejects_pending_consumed_changed_and_expanded_scopes()
    {
        var run=new string('a',32);var node=Plan(run);Assert.Equal(run,RequireLease(JsonSerializer.SerializeToElement(node)));
        foreach(var (key,value) in new[]{("authorizationStatus","pending"),("leaseStatus","running"),("leaseStatus","ended"),("leaseStatus","cleanup-unconfirmed"),("scenario","other"),("database","production"),("metadataSha256","changed"),("runId",new string('A',32))})
        {var changed=node.DeepClone();changed[key]=value;Assert.ThrowsAny<Exception>(()=>RequireLease(JsonSerializer.SerializeToElement(changed)));}
        foreach(var key in new[]{"maximumRows","columns","initialIndexes","finalIndexes"}){var changed=node.DeepClone();changed[key]=99;Assert.Throws<InvalidOperationException>(()=>RequireLease(JsonSerializer.SerializeToElement(changed)));}
        var expanded=node.DeepClone();expanded["tables"]!.AsArray().Add("foreign_table");Assert.Throws<InvalidOperationException>(()=>RequireLease(JsonSerializer.SerializeToElement(expanded)));
        var renamed=node.DeepClone();renamed["tables"]![0]="foreign_table";Assert.Throws<InvalidOperationException>(()=>RequireLease(JsonSerializer.SerializeToElement(renamed)));
        var invalid=node.DeepClone();invalid["runId"]=run+"\n";Assert.Throws<InvalidOperationException>(()=>RequireLease(JsonSerializer.SerializeToElement(invalid)));
        var cleanup=node.DeepClone();cleanup["allowDropOwnedTable"]=false;Assert.Throws<InvalidOperationException>(()=>RequireLease(JsonSerializer.SerializeToElement(cleanup)));
    }
    /// <summary>批准后才可执行真实预览／安装／反查／草案、唯一性及精确清理。</summary>
    [DescendingIndexMySqlFact]
    public async Task Approved_single_table_desc_indexes_roundtrip_on_real_mysql()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));var leasePath=Path.GetFullPath(Environment.GetEnvironmentVariable("METARILL_DESC_INDEX_LEASE")!);
        if(leasePath!=Path.GetFullPath(Path.Combine(root,".local/descending-index/mysql-lease.json")))throw new InvalidOperationException("租约仅接受当前冻结源码的独占私有路径。");
        using var ownership=new FileStream(leasePath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var node=JsonNode.Parse(await File.ReadAllTextAsync(leasePath))!;using var lease=JsonDocument.Parse(node.ToJsonString());var run=RequireLease(lease.RootElement);
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var address=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!=Database||address.Database!=Database||address.Server!="deve-server.yinzon.com"||address.Port!=3306)throw new InvalidOperationException("只允许固定Server-200隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));
        foreach(var key in new[]{"expectedHostname","expectedServerUuid"})Assert.Equal(lease.RootElement.GetProperty("instancePin").GetProperty(key).GetString(),pin.RootElement.GetProperty(key).GetString());
        var table=Name(run);var initial=Catalog(run,false);var final=Catalog(run,true);var evidence=Path.Combine(root,".local/descending-index/mysql-"+run);Directory.CreateDirectory(evidence);
        var owned=false;var cleaned=false;var passed=false;var known=new HashSet<string>(StringComparer.Ordinal);
        // ORM构造也置于单次租约消费之后，不能依赖连接池保持惰性。
        State("running");using var session=new DatabaseSession(options);var schema=new SchemaService(session);
        try
        {
            await using(var connection=await OpenPinned()){await using var q=connection.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table";q.Parameters.AddWithValue("@table",table);Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}
            Assert.Empty((await schema.InspectAsync(initial)).Tables);var plan=await schema.PreviewAsync(initial);Assert.Equal("createTable",Assert.Single(plan.Changes).Kind);Assert.Contains("`score` DESC",plan.Changes[0].Sql);Assert.Contains("`name` DESC",plan.Changes[0].Sql);await Receipt("intent",new{run,table,metadataSha256=Hash(run),plannedStatements=plan.Changes,ddlNotExecutedYet=true});
            try{var result=await schema.ApplyAsync(plan);owned=result.AppliedStatements==1;Assert.True(result.Succeeded);}catch(SchemaApplyException error){owned=error.Result.AppliedStatements==1;throw;}
            var created=Assert.Single((await schema.InspectAsync(initial)).Tables);known.Add(created.SnapshotDefinition!);await Receipt("created",new{run,owned,snapshotSha256=TextHash(created.SnapshotDefinition!)});Assert.Empty((await schema.PreviewAsync(initial)).Changes);
            Assert.Equal(new[]{"ASC","DESC","ASC"},created.Indexes!.Single(index=>index.Name=="yzp_idx_mixed").Directions);Assert.Equal(new[]{"DESC"},created.Indexes!.Single(index=>index.Name=="yzp_uk_name").Directions);
            await using(var connection=await OpenPinned())
            {
                await using(var transaction=await connection.BeginTransactionAsync())
                {
                    foreach(var row in Rows){await using var q=connection.CreateCommand();q.Transaction=transaction;q.CommandText="INSERT INTO "+MySqlSchemaPlanner.Quote(table)+" (id,bucket,score,name) VALUES (@id,@bucket,@score,@name)";q.Parameters.AddWithValue("@id",row.Id);q.Parameters.AddWithValue("@bucket",row.Bucket);q.Parameters.AddWithValue("@score",row.Score);q.Parameters.AddWithValue("@name",row.Name);Assert.Equal(1,await q.ExecuteNonQueryAsync());}await transaction.CommitAsync();
                }
                await using(var query=connection.CreateCommand()){query.CommandText="SELECT id FROM "+MySqlSchemaPlanner.Quote(table)+" FORCE INDEX (`yzp_idx_mixed`) ORDER BY bucket ASC,score DESC,id ASC";await using var reader=await query.ExecuteReaderAsync();var ids=new List<long>();while(await reader.ReadAsync())ids.Add(reader.GetInt64(0));Assert.Equal(new[]{Rows[1].Id,Rows[0].Id,Rows[2].Id},ids);}
                await using(var duplicate=connection.CreateCommand()){duplicate.CommandText="INSERT INTO "+MySqlSchemaPlanner.Quote(table)+" (id,bucket,score,name) VALUES (@id,1,1,@name)";duplicate.Parameters.AddWithValue("@id",Rows[2].Id+1);duplicate.Parameters.AddWithValue("@name",Rows[0].Name);var error=await Assert.ThrowsAsync<MySqlException>(async()=>await duplicate.ExecuteNonQueryAsync());Assert.Equal(1062,error.Number);}
            }
            var add=await schema.PreviewAsync(final);var change=Assert.Single(add.Changes);Assert.Equal("addIndex",change.Kind);Assert.Contains("`score` DESC",change.Sql);Assert.DoesNotContain("DROP",change.Sql);Assert.True((await schema.ApplyAsync(add)).Succeeded);
            var actual=Assert.Single((await schema.InspectAsync(final)).Tables);known.Add(actual.SnapshotDefinition!);Assert.Equal(new[]{"DESC"},actual.Indexes!.Single(index=>index.Name=="yzp_idx_score").Directions);Assert.Empty((await schema.PreviewAsync(final)).Changes);
            var imported=await schema.InspectForImportAsync([table]);Assert.Empty(imported.Issues);var draft=SchemaImportDraftCompiler.Compile(imported,"123");Assert.True(draft.ColumnsAndIndexesRepresentable,string.Join(";",draft.Issues));var roundtrip=MySqlSchemaPlanner.Compile(draft.LogicalMetadata,Database);Assert.Empty(MySqlSchemaPlanner.Compare(imported.Snapshot.Schema,roundtrip));Assert.Empty(MySqlSchemaPlanner.Compare(roundtrip,imported.Snapshot.Schema));Assert.Equal(3,draft.LogicalMetadata.ConfigIndexColumns.Count(column=>column.GetProperty("sortDirection").GetString()=="DESC"));
            var conflict=Catalog(run,true);var columns=conflict.ConfigIndexColumns;var position=columns.FindIndex(column=>column.GetProperty("id").GetString()=="mixed_score");columns[position]=JsonSerializer.SerializeToElement(new{id="mixed_score",configIndexId="mixed",configColumnId="score",sortNo=20,sortDirection="ASC"});var before=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(final));await Assert.ThrowsAsync<InvalidOperationException>(()=>schema.PreviewAsync(conflict));Assert.Equal(before,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(final)));
            passed=true;await Receipt("verified",new{run,passed,rows=3,columns=5,initialIndexes=2,finalIndexes=3,physicalDescendingReadback=true,uniqueDescending=true,mixedOrder=true,reverseDraftRoundtrip=true,directionConflictBeforeDdl=true,platformConfigurationSaved=false,performanceMeasured=false});
        }
        catch(Exception error){await Receipt("failure",new{run,passed=false,errorType=error.GetType().Name});throw;}
        finally
        {
            try
            {
                if(owned)
                {
                    var actual=Assert.Single((await schema.InspectAsync(final)).Tables);Assert.Equal(table,actual.Name);Assert.Contains(actual.SnapshotDefinition!,known);await CheckRows();
                    await using var connection=await OpenPinned();await using var q=connection.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() AND EVENT_OBJECT_TABLE=@table";q.Parameters.AddWithValue("@table",table);Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));q.Parameters.Clear();q.CommandText="DROP TABLE "+MySqlSchemaPlanner.Quote(table);await q.ExecuteNonQueryAsync();
                }
                await using(var connection=await OpenPinned()){await using var q=connection.CreateCommand();q.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table";q.Parameters.AddWithValue("@table",table);Assert.Equal(0,Convert.ToInt32(await q.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}cleaned=true;await Receipt("cleanup",new{run,owned,cleaned,passed,independentConnectionAbsenceVerified=true});
            }
            catch(Exception error){await Receipt("cleanup-failure",new{run,cleaned=false,errorType=error.GetType().Name});throw;}
            finally{State(cleaned?"ended":"cleanup-unconfirmed");}
        }
        // 每个直接SQL连接验证真实库、主机及UUID，不把连接串当作实例证明。
        async Task<MySqlConnection> OpenPinned()
        {
            var c=new MySqlConnection(new MySqlConnectionStringBuilder(options.ConnectionString){Pooling=false}.ConnectionString);
            try{await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await q.ExecuteReaderAsync();Assert.True(await reader.ReadAsync());Assert.Equal(Database,reader.GetString(0));Assert.Equal(pin.RootElement.GetProperty("expectedHostname").GetString(),reader.GetString(1));Assert.Equal(pin.RootElement.GetProperty("expectedServerUuid").GetString(),reader.GetString(2));return c;}catch{await c.DisposeAsync();throw;}
        }
        // 仅允许本轮固定三行的任意子集；未知结构、触发器或外部行一律不硬删。
        async Task CheckRows()
        {
            await using var c=await OpenPinned();await using var q=c.CreateCommand();q.CommandText="SELECT id,bucket,score,name,delete_no FROM "+MySqlSchemaPlanner.Quote(table)+" ORDER BY id";await using var reader=await q.ExecuteReaderAsync();var count=0;
            while(await reader.ReadAsync()){count++;Assert.InRange(count,1,3);var expected=Assert.Single(Rows,row=>row.Id==reader.GetInt64(0));Assert.Equal(expected.Bucket,reader.GetInt32(1));Assert.Equal(expected.Score,reader.GetDecimal(2));Assert.Equal(expected.Name,reader.GetString(3));Assert.True(reader.IsDBNull(4));}
        }
        // 阶段回执不包含连接、密码或其它库记录。
        Task Receipt(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
        // 从首次连接前消费租约；失败不重试，核零未完成则保持清理未确认状态。
        void State(string value){node["leaseStatus"]=value;File.WriteAllText(leasePath,node.ToJsonString(new JsonSerializerOptions{WriteIndented=true})+"\n");}
    }
    /// <summary>物理模型交给原规划器，不手写建表SQL、不新增实体或Mapper。</summary>
    private static MetadataCatalog Catalog(string run,bool includeAdded)
    {
        var catalog=new MetadataCatalog{YzProjectId="123",ConfigTables=[new(){Id="table",TableName=Name(run),IsRongyu=1}],ConfigColumns=[new(){Id="id",ConfigTableId="table",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="bucket",ConfigTableId="table",ColumnName="bucket",LogicalType="int32"},new(){Id="score",ConfigTableId="table",ColumnName="score",LogicalType="decimal",Precision=18,Scale=2},new(){Id="name",ConfigTableId="table",ColumnName="name",LogicalType="string",ColumnLength="64"},new(){Id="delete",ConfigTableId="table",ColumnName="delete_no",LogicalType="datetime"}]};
        Add("mixed","yzp_idx_mixed",false,[("bucket","ASC"),("score","DESC"),("id","ASC")]);Add("unique","yzp_uk_name",true,[("name","DESC")]);if(includeAdded)Add("extra","yzp_idx_score",false,[("score","DESC")]);return catalog;
        // 显式字段／顺序／方向随同元数据进入编译器，不允许客户端SQL覆盖。
        void Add(string id,string name,bool unique,(string Column,string Direction)[] columns){catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id,configTableId="table",indexName=name,isUnique=unique?1:0,isEnabled=1}));foreach(var (column,position) in columns.Select((column,position)=>(column,position)))catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id=id+"_"+column.Column,configIndexId=id,configColumnId=column.Column,sortNo=(position+1)*10,sortDirection=column.Direction}));}
    }
    /// <summary>批准形状只作为纯离线测试数据，不能据此产生实际授权。</summary>
    private static JsonNode Plan(string run)=>JsonSerializer.SerializeToNode(new{runId=run,scenario=Scenario,database=Database,tables=new[]{Name(run)},maximumRows=3,columns=5,initialIndexes=2,finalIndexes=3,allowDropOwnedTable=true,metadataSha256=Hash(run),authorizationStatus="approved",leaseStatus="not-started"})!;
    /// <summary>表名由固定前缀和单次标识派生，不接受任意SQL标识。</summary>
    private static string Name(string run)=>"mt_dix_"+run[..12];
    /// <summary>租约绑定原方言编译后的全部最终物理结构。</summary>
    private static string Hash(string run)=>MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(Catalog(run,true),Database));
    /// <summary>只存本轮DDL摘要，不输出连接或凭据。</summary>
    private static string TextHash(string value)=>Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}
