using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>独立自有表的真实生成HTTP/MySQL聚合；测试授权器固定身份，不冒充正式Web登录或浏览器验收。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class OriginalListStatisticsMySqlTests
{
    /// <summary>反查空表→正式创建→生成编译→HTTP统计→结构和行快照不变→精确清理，所有输入均为自有样本。</summary>
    [StandaloneExportMySqlFact]
    public async Task Real_mysql_page_statistics_keep_original_map_and_all_read_scopes()
    {
        var root=SourceRoot();
        // 使用明确私有配置并核Server200实例；不打印连接、账号或实例标识。
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var connection=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||connection.Database!="metarill_business_test"||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("统计专项只允许固定隔离MySQL业务库。");
        var pinPath=Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")??throw new InvalidOperationException("统计专项需要明确实例pin。");
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(pinPath));
        await using(var c=new MySqlConnection(options.ConnectionString))
        {
            await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";
            await using var r=await q.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync()&&r.GetString(0)=="metarill_business_test"&&r.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&r.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"统计专项实例未匹配，尚未创建夹具。");
        }
        var nonce=Guid.NewGuid().ToString("N");var tableName="mt_stats_"+nonce[..12];
        var catalog=OriginalStatisticsFixture.Catalog();catalog.ConfigTables[0].TableName=tableName;
        var entityName=GenerationModel.Pascal(tableName);var boundary=Path.Combine(Path.GetTempPath(),"metarill-statistics-mysql");var directory=Path.Combine(boundary,nonce);
        var evidence=Path.Combine(root,".local/original-list-statistics/mysql-"+nonce);Directory.CreateDirectory(evidence);
        using var session=new DatabaseSession(options);var schema=new SchemaService(session,new FileSchemaChangeJournal(Path.Combine(evidence,"schema-journal")));
        WebApplication? app=null;var created=false;string? schemaFingerprint=null;string? rowsFingerprint=null;var finished=false;
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var plan=await schema.PreviewAsync(catalog);var change=Assert.Single(plan.Changes);Assert.Equal("createTable",change.Kind);Assert.Equal(tableName,change.Table);
            await Receipt("intent",false);
            created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            schemaFingerprint=MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog));
            await Execute(session,"INSERT INTO "+session.Provider.QuoteIdentifier(tableName)+" (id,yz_project_id,name,amount,status,average_status,delete_no) VALUES (1,54,'目标甲',10,2,2,NULL),(2,54,'目标乙',NULL,1,NULL,NULL),(3,54,'其他',5,2,2,NULL),(4,56,'目标外项目',1000,2,2,NULL),(5,54,'目标已删除',999,2,2,'2020-01-01 00:00:00')");
            rowsFingerprint=await RowsFingerprint(session,tableName);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.StatisticsMySql");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
            var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:"+port);
            var authorization=new StatisticsAuthorization();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(942));builder.Services.AddSingleton<IRuntimeAuthorization>(authorization);
            assembly.GetType("Example.StatisticsMySql.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(options=>options.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app=builder.Build();
            app.Use(async(context,next)=>{try{await next();}catch(UnauthorizedAccessException){context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(R<string>.Error("统计测试拒权",403));}});
            app.MapControllers();await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(15)};var url="/api/admin/auto/"+tableName+"/301/page";
            var page=await Post(new(){PageNo=1,PageSize=1,Filters=new(){{"name",JsonSerializer.SerializeToElement("目标")}}});
            var stats=page.GetProperty("map").GetProperty("statistics");Assert.Equal(10m,stats.GetProperty("amount").GetProperty("total").GetDecimal());Assert.Equal(0m,stats.GetProperty("amount").GetProperty("currentPage").GetDecimal());
            foreach(var field in new[]{"status","averageStatus"}){Assert.Equal(50m,stats.GetProperty(field).GetProperty("total").GetDecimal());Assert.Equal(0m,stats.GetProperty(field).GetProperty("currentPage").GetDecimal());}
            Assert.Equal("2",page.GetProperty("data").GetProperty("total").ToString());Assert.Single(page.GetProperty("data").GetProperty("records").EnumerateArray());
            var selected=await Post(new(){SelectedIds=["1"]});Assert.Equal(100m,selected.GetProperty("map").GetProperty("statistics").GetProperty("status").GetProperty("total").GetDecimal());
            var nullOnly=await Post(new(){SelectedIds=["2"]});Assert.Equal(0m,nullOnly.GetProperty("map").GetProperty("statistics").GetProperty("amount").GetProperty("total").GetDecimal());
            var empty=await Post(new(){Filters=new(){{"name",JsonSerializer.SerializeToElement("没有该名称")}}});var emptyStats=empty.GetProperty("map").GetProperty("statistics");
            Assert.Equal(0m,emptyStats.GetProperty("amount").GetProperty("total").GetDecimal());Assert.Empty(emptyStats.GetProperty("status").EnumerateObject());Assert.Empty(emptyStats.GetProperty("averageStatus").EnumerateObject());
            authorization.Deny=true;using(var denied=await client.PostAsJsonAsync(url,new PageRequest()))Assert.Equal(System.Net.HttpStatusCode.Forbidden,denied.StatusCode);authorization.Deny=false;
            Assert.Equal(schemaFingerprint,MySqlSchemaPlanner.Fingerprint(await schema.InspectAsync(catalog)));
            Assert.Equal(rowsFingerprint,await RowsFingerprint(session,tableName));
            await Receipt("verified",false);finished=true;

            // 真实HTTP只读取生成Controller结果；测试授权器的身份及页面检查另与SQL范围分别验证。
            async Task<JsonElement> Post(PageRequest request)
            {
                using var response=await client.PostAsJsonAsync(url,request);Assert.True(response.IsSuccessStatusCode,"统计HTTP未成功，错误实体不输出。");
                using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.Equal(200,json.RootElement.GetProperty("code").GetInt32());return json.RootElement.Clone();
            }
        }
        finally
        {
            if(app is not null){await app.StopAsync();await app.DisposeAsync();}
            if(created)
            {
                using var cleanup=new DatabaseSession(options);var inspect=new SchemaService(cleanup);var actual=await inspect.InspectAsync(catalog);
                if(actual.Tables.Count!=0)
                {
                    if(schemaFingerprint is null||MySqlSchemaPlanner.Fingerprint(actual)!=schemaFingerprint)throw new InvalidOperationException("自有统计表结构变化，保留现场，不执行清理。");
                    if(rowsFingerprint is not null&&await RowsFingerprint(cleanup,tableName)!=rowsFingerprint)throw new InvalidOperationException("自有统计样本行发生变化，保留现场，不执行清理。");
                    if(!tableName.StartsWith("mt_stats_",StringComparison.Ordinal)||tableName!="mt_stats_"+nonce[..12])throw new InvalidOperationException("统计表归属校验失败，未清理。");
                    await Execute(cleanup,"DROP TABLE "+cleanup.Provider.QuoteIdentifier(tableName));
                    Assert.Empty((await inspect.InspectAsync(catalog)).Tables);
                }
                await Receipt("cleaned",true);
            }
            await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }

        /// <summary>证据只保存自有资源名称和摘要，不含连接、令牌或业务行。</summary>
        async Task Receipt(string stage,bool cleaned)
        {
            var value=new{stage,nonce,tableName,metadataProject=catalog.YzProjectId,schemaFingerprint,rowsFingerprint,cleaned,finished,authorization="固定测试授权器，非正式登录",accountInitializationExecuted=false};
            await File.WriteAllTextAsync(Path.Combine(evidence,stage+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true})+"\n");
        }
    }

    /// <summary>仓库路径来自当前测试源码，避免依赖测试运行器的输出层级。</summary>
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../.."));

    /// <summary>DDL只作用于先证明不存在的自有随机表；数据写入仅用于该表的样本准备。</summary>
    private static async Task Execute(DatabaseSession session,string sql)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();
    }
    /// <summary>只读取自有表的固定七列，前后摘要相同才能按精确归属清理。</summary>
    private static async Task<string> RowsFingerprint(DatabaseSession session,string table)
    {
        await using var connection=session.Provider.CreateConnection(session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,yz_project_id,name,amount,status,average_status,delete_no FROM "+session.Provider.QuoteIdentifier(table)+" ORDER BY id";
        await using var rows=await command.ExecuteReaderAsync();var values=new List<object?[]>();
        while(await rows.ReadAsync()){var row=new object?[7];for(var i=0;i<7;i++)row[i]=await rows.IsDBNullAsync(i)?null:rows.GetValue(i);values.Add(row);}
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
    }
}
