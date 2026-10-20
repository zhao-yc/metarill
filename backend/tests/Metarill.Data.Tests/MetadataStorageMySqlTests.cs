using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>只在明确测试实例创建自有表，证明非冗余派生项不进入实际DDL、仓储读写或索引。</summary>
public sealed class MetadataStorageMySqlTests
{
    /// <summary>真实结构预览/应用→参数化基础仓储→回滚→重复零差异→精确清理，不冒充生成业务读计划。</summary>
    [MySqlIntegrationFact]
    public async Task Nonredundant_schema_and_row_store_only_use_real_physical_columns()
    {
        var source=Root();using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var builder=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||builder.Database!="metarill_business_test"||builder.Server!="deve-server.yinzon.com"||builder.Port!=3306)throw new InvalidOperationException("非冗余存储测试只允许指定隔离业务库。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("必须提供实例pin。")));
        await using(var c=new MySqlConnection(options.ConnectionString)){await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await q.ExecuteReaderAsync();Assert.True(await r.ReadAsync()&&r.GetString(0)=="metarill_business_test"&&r.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"测试实例未匹配，未创建样本。");}
        var nonce=Guid.NewGuid().ToString("N");var name="mt_storage_"+nonce[..12];var catalog=new MetadataCatalog{ConfigTables=[MetadataStoragePolicyTests.Table(0)],ConfigColumns=MetadataStoragePolicyTests.Columns().ToList()};catalog.ConfigTables[0].TableName=name;
        var evidence=Path.Combine(source,".local/storage-policy/mysql-"+nonce);Directory.CreateDirectory(evidence);using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));var store=new SqlRowStore(session,catalog);var created=false;string? fingerprint=null;string? rowsHash=null;var verified=false;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);var change=Assert.Single(plan.Changes);Assert.Equal("createTable",change.Kind);Assert.Equal(name,change.Table);Assert.DoesNotContain("customer_name",change.Sql);await Receipt("intent",false);
            created=true;var applied=await schema.ApplyAsync(plan);Assert.True(applied.Succeeded);Assert.Equal(1,applied.AppliedStatements);var actual=await schema.InspectAsync(catalog);Assert.Equal(new[]{"customer_id","id","name"},Assert.Single(actual.Tables).Columns.Select(column=>column.Name).Order(StringComparer.Ordinal));fingerprint=MySqlSchemaPlanner.Fingerprint(actual);
            const long id=9007199254740993;await session.InTransactionAsync(async()=>{await store.InsertAsync(name,new Dictionary<string,object?>{{"id",id},{"name","中文'参数;--"},{"customer_id",99L}});return true;});var rows=await store.ReadAsync(name);var row=Assert.Single(rows);Assert.Equal(3,row.Count);Assert.Equal(id,SqlRowStore.Id(row));Assert.Equal("中文'参数;--",SqlRowStore.Text(row,"name"));rowsHash=Hash(rows);
            // 真实事务内已修改后故障，不能留下半次写入，也不能借派生列触发不存在列SQL。
            await Assert.ThrowsAsync<InvalidOperationException>(()=>session.InTransactionAsync<int>(async()=>{Assert.Equal(1,await store.UpdateAsync(name,id,new Dictionary<string,object?>{{"name","必须回滚"}}));throw new InvalidOperationException("存储专项尾部故障。");}));Assert.Equal(rowsHash,Hash(await store.ReadAsync(name)));
            Assert.Equal(0,await store.UpdateAsync(name,id,new Dictionary<string,object?>{{"customer_name","只读派生"}}));await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ReadAsync(name,new Dictionary<string,object?>{{"customer_name","非法物理筛选"}}));
            Assert.Empty((await schema.PreviewAsync(catalog)).Changes);Assert.Equal(fingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog)));Assert.Equal(rowsHash,Hash(await store.ReadAsync(name)));
            var inputName=catalog.ConfigColumns.Single(column=>column.ColumnName=="name");inputName.ColumnName="not_a_real_column";
            try{Assert.Equal(rowsHash,Hash(await store.ReadAsync(name)));}finally{inputName.ColumnName="name";}
            verified=true;await Receipt("verified",false);
        }
        finally
        {
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var inspect=new SchemaService(cleanup);var actual=await inspect.InspectAsync(catalog);
                if(actual.Tables.Count!=0)
                {
                    if(fingerprint is null||MySqlSchemaPlanner.Fingerprint(actual)!=fingerprint||name!="mt_storage_"+nonce[..12])throw new InvalidOperationException("自有结构不符，保留现场。");var ownStore=new SqlRowStore(cleanup,catalog);var rows=await ownStore.ReadAsync(name);if(rowsHash is not null?Hash(rows)!=rowsHash:rows.Count!=0)throw new InvalidOperationException("自有行不符，保留现场。");
                    await using var c=cleanup.Provider.CreateConnection(options);await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="DROP TABLE "+cleanup.Provider.QuoteIdentifier(name);await q.ExecuteNonQueryAsync();Assert.Empty((await inspect.InspectAsync(catalog)).Tables);
                }
                await Receipt("cleaned",true);
            }
        }
        Task Receipt(string stage,bool cleaned)=>File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(new{stage,nonce,name,fingerprint,rowsHash,verified,cleaned,generatedBusinessReadsExecuted=false,initializationExecuted=false,grantAttempted=false},new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>只比较自有行的不可逆摘要，不输出实际业务值。</summary>
    private static string Hash(object value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    /// <summary>从当前测试源码定位所属clone，不依赖运行器工作目录。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
