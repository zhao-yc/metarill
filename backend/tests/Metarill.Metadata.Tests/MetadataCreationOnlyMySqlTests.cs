using System.Globalization;
using System.Text;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>唯一实库用例复用随机归属fixture；无HTTP、DDL、种子或任何平台配置写入。</summary>
public sealed class MetadataCreationOnlyMySqlTests
{
    private static readonly SnowflakeIdGenerator Ids=new(887);
    private static readonly string[] OwnedTables=["config_menu","config_index_column","config_index","config_page_column","config_page","config_column","config_table","config_custom_page","yz_admin_type"];
    [MetadataIntegrationFact]
    public async Task Creation_only_observer_keeps_holes_and_soft_deletes_while_new_column_and_derivation_remain_transactional()
    {
        await PinBeforeAnyFixtureWrite();OwnershipFixture? fixture=null;
        try
        {
            fixture=await OwnershipFixture.CreateAsync();var f=fixture;
            Assert.NotEqual(1608097412978577409L,f.Project);Assert.NotEqual(1608097412978577409L,f.ForeignProject);
            var observer=new ConfigurationMutationObserver(f.Store,f.Registry,Ids);
            var field=Assert.Single(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",f.Page},{"yz_project_id",f.Project}}));var oldField=SqlRowStore.Id(field);
            // 初始indexed_value没有页映射；将title旧映射软删，构成两种真实缺口。
            await f.Session.InTransactionAsync(async()=>
            {
                await f.Store.UpdateAsync("config_page_column",oldField,new Dictionary<string,object?>{{"delete_no",DateTime.UtcNow}});
                await observer.BeforeCommitAsync(new("creation-only","config_page_column",oldField,f.Project.ToString(CultureInfo.InvariantCulture),new Dictionary<string,object?>(),true,f.Session.CurrentTransaction!));return true;
            });
            var holeGraph=await Graph(f);var deleted=Snapshot((await f.Store.ReadIncludingDeletedAsync("config_page_column",new Dictionary<string,object?>{{"id",oldField}})).Single());
            var fieldIds=holeGraph["config_page_column"].Select(row=>SqlRowStore.Id(row)).Order().ToArray();var columnIds=holeGraph["config_column"].Select(row=>SqlRowStore.Id(row)).Order().ToArray();var pageIds=holeGraph["config_page"].Select(row=>SqlRowStore.Id(row)).Order().ToArray();
            await f.Mutate("config_column",f.Column,new(){["control_detail"]="{}"});
            await f.Mutate("config_page",f.Page,new(){["name"]="仍无新增字段"});
            var renamed="mt_creation_owner_"+f.Table;await f.Mutate("config_table",f.Table,new(){["table_name"]=renamed});
            // 另一张表本无页面/系统默认列，普通保存也不能替用户补默认页或默认列。
            await f.Mutate("config_table",f.OtherTable,new(){["name"]="没有默认页仍可更新"});
            var afterUpdates=await Graph(f);
            Assert.Equal(fieldIds,afterUpdates["config_page_column"].Select(row=>SqlRowStore.Id(row)).Order().ToArray());Assert.Equal(columnIds,afterUpdates["config_column"].Select(row=>SqlRowStore.Id(row)).Order().ToArray());Assert.Equal(pageIds,afterUpdates["config_page"].Select(row=>SqlRowStore.Id(row)).Order().ToArray());
            Assert.Equal(Snapshot(holeGraph["config_page_column"]),Snapshot(afterUpdates["config_page_column"]));Assert.Equal(renamed,SqlRowStore.Text(await f.Row("config_page",f.Page),"config_table_table_name"));
            Assert.Empty(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",f.Page},{"yz_project_id",f.Project}}));
            var newPage=Ids.NextId();var newColumn=Ids.NextId();
            await f.Session.InTransactionAsync(async()=>
            {
                // 私有fixture显式制造第二页的历史洞，不把它当普通页面创建的产品验收。
                await f.Add("config_page",newPage,new(){["name"]="另一缺口页",["config_table_id"]=f.Table,["config_table_table_name"]=renamed,["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin});
                var values=new Dictionary<string,object?>{{"name","本次新增列"},{"column_name","fresh_value"},{"logical_type","string"},{"column_type","nvarchar"},{"column_length","255"},{"config_table_id",f.Table},{"yz_project_id",f.Project},{"sort_no",25}};
                await f.Add("config_column",newColumn,values);
                await observer.BeforeCommitAsync(new("creation-only","config_column",newColumn,f.Project.ToString(CultureInfo.InvariantCulture),values,false,f.Session.CurrentTransaction!,true));return true;
            });
            var created=await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",newColumn},{"yz_project_id",f.Project}});
            Assert.Equal(new[]{f.Page,newPage}.Order().ToArray(),created.Select(row=>SqlRowStore.Id(row,"config_page_id")).Order().ToArray());
            Assert.All(created,row=>{Assert.Equal("fresh_value",SqlRowStore.Text(row,"config_column_column_name"));Assert.Equal(25,Convert.ToInt32(row["config_column_sort_no"],CultureInfo.InvariantCulture));});
            Assert.Empty(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",f.IndexedColumn},{"yz_project_id",f.Project}}));Assert.Empty(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",f.Column},{"yz_project_id",f.Project}}));
            Assert.Equal(deleted,Snapshot((await f.Store.ReadIncludingDeletedAsync("config_page_column",new Dictionary<string,object?>{{"id",oldField}})).Single()));
            await f.Mutate("config_column",newColumn,new(){["column_name"]="fresh_renamed",["sort_no"]=77});
            var propagated=await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_column_id",newColumn},{"yz_project_id",f.Project}});
            Assert.Equal(2,propagated.Count);Assert.All(propagated,row=>{Assert.Equal("fresh_renamed",SqlRowStore.Text(row,"config_column_column_name"));Assert.Equal(77,Convert.ToInt32(row["config_column_sort_no"],CultureInfo.InvariantCulture));});
            var beforeTail=Snapshot(await Graph(f));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync<bool>(async()=>{await f.Mutate("config_column",newColumn,new(){["column_name"]="tail_rollback",["sort_no"]=88});throw new InvalidOperationException("模拟尾部观察器失败");}));
            Assert.Equal(beforeTail,Snapshot(await Graph(f)));
            await Evidence("result",new{actualMySql=true,projectId=f.Project.ToString(CultureInfo.InvariantCulture),updatesDoNotCreate=true,missingAndSoftDeletedMappingsPreserved=true,newColumnMappings=created.Select(row=>SqlRowStore.Id(row).ToString(CultureInfo.InvariantCulture)).ToArray(),existingNameAndSortPropagation=true,tailFailureRolledBack=true,noDdl=true,noSeed=true,platformGraphTouched=false});
        }
        finally
        {
            if(fixture is not null)
            {
                var options=fixture.Session.Options;var ids=await OwnedIds(fixture);await fixture.DisposeAsync();
                using var session=new DatabaseSession(options);await using var connection=session.Provider.CreateConnection(options);await connection.OpenAsync();
                foreach(var row in ids)
                {
                    await using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM "+session.Provider.QuoteIdentifier(row.Table)+" WHERE id=@id";var p=command.CreateParameter();p.ParameterName="id";p.Value=row.Id;command.Parameters.Add(p);Assert.Equal(0L,Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));
                }
                await Evidence("cleanup",new{exactOwnedIds=ids.Select(row=>new{table=row.Table,id=row.Id.ToString(CultureInfo.InvariantCulture)}).ToArray(),remaining=0,platformGraphTouched=false});
            }
        }
    }
    /// <summary>先钉住Server200实例，检查失败不创建fixture；凭据或底层连接异常不进入输出。</summary>
    private static async Task PinBeforeAnyFixtureWrite()
    {
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);
            if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_test"||parsed.Database!="metarill_test"||parsed.Server!="deve-server.yinzon.com"||parsed.Port!=3306)throw new InvalidOperationException();
            await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();
            if(!await reader.ReadAsync()||reader.GetString(0)!="metarill_test"||reader.GetString(1)!="3107e214a8a3"||reader.GetString(2)!="6d3d024a-9d73-11f1-9939-ce69308a21dc")throw new InvalidOperationException();
        }
        catch{throw new InvalidOperationException("Server200隔离库身份核验失败，连接详情不输出。");}
    }
    /// <summary>只读取fixture随机项目，包含软删行以证明历史洞没有复活。</summary>
    private static async Task<Dictionary<string,List<Dictionary<string,object?>>>> Graph(OwnershipFixture f)
    {
        var graph=new Dictionary<string,List<Dictionary<string,object?>>>();foreach(var table in new[]{"config_table","config_column","config_page","config_page_column"})graph[table]=(await f.Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"yz_project_id",f.Project}})).OrderBy(row=>SqlRowStore.Id(row)).ToList();return graph;
    }
    /// <summary>清理验证仅记录本fixture两个随机项目的实际ID，不按前缀或整个库删除。</summary>
    private static async Task<List<(string Table,long Id)>> OwnedIds(OwnershipFixture f)
    {
        var rows=new List<(string,long)>{("yz_project",f.Project),("yz_project",f.ForeignProject)};foreach(var table in OwnedTables)foreach(var project in new[]{f.Project,f.ForeignProject})foreach(var row in await f.Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"yz_project_id",project}}))rows.Add((table,SqlRowStore.Id(row)));return rows.Distinct().ToList();
    }
    /// <summary>完整自有行图用于比较回滚前后字节语义，不输出到终端。</summary>
    private static string Snapshot(object value)=>JsonSerializer.Serialize(value,JsonDefaults.Options);
    /// <summary>私有证据只记录自有ID与布尔结论；不输出配置值、SQL或连接信息。</summary>
    private static async Task Evidence(string name,object value)
    {
        if(Environment.GetEnvironmentVariable("METARILL_CREATION_EVIDENCE") is not{} root)return;Directory.CreateDirectory(root);await using var stream=new FileStream(Path.Combine(root,name+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true})));await stream.FlushAsync();stream.Flush(true);
    }
}
