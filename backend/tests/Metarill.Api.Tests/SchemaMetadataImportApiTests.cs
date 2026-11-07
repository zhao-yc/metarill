using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>正式Web身份的源表→审核→确认→真实配置保存，原表行/DDL不变，不补8列或页面，重复确认不能重放。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_plan_saves_real_configuration_without_source_ddl_or_extra_columns()
    {
        var (factory, client) = await Login(factory => { factory.UseKestrel(0); return factory; }); await using var host = factory; using var http = client;
        var sessions = factory.Services.GetRequiredService<TargetDatabaseSessions>(); var metadata = factory.Services.GetRequiredService<MetadataRepository>(); var store = factory.Services.GetRequiredService<SqlRowStore>();
        var table = "mt_save_" + Guid.NewGuid().ToString("N"); var configIds = new List<string>();
        await using var connection = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await connection.OpenAsync();
        // 测试只操作本轮随机源表，元数据通过被测正式导入入口保存。
        async Task Execute(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        await Execute($"CREATE TABLE `{table}` (`id` bigint NOT NULL PRIMARY KEY,`name` varchar(64) NULL DEFAULT '原始常量',`value` decimal(18,2) NULL,`attachment` varbinary(16) NULL,`delete_no` datetime(6) NULL,INDEX yzp_idx_name (`name`)) ENGINE=InnoDB");
        try
        {
            await Execute($"INSERT INTO `{table}` (id,name,value) VALUES (1,'源业务行',12.50)");
            var schema = factory.Services.GetRequiredService<ProjectSchemaService>().Resolve(Project); var before = (await schema.InspectForImportAsync([table])).Snapshot.Fingerprint;
            const string prefix = "/admin/sys/database/import/";
            var address = Assert.Single(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses); var session = Assert.Single(client.DefaultRequestHeaders.GetValues("loginStateId"));
            var cliPlan = await RunPlatformSchemaCli(address, "import-prepare", Project, session, "--tables", table); Assert.True(cliPlan.Code == 0, cliPlan.Error); var prepared = Json(cliPlan.Output);
            Assert.True(prepared.GetProperty("canSaveMetadata").GetBoolean()); Assert.False(prepared.GetProperty("executesDdl").GetBoolean());
            Assert.DoesNotContain((await metadata.LoadProjectAsync(Project)).ConfigTables, row => row.TableName == table);
            var proof = new { planId = prepared.GetProperty("planId").GetString(), confirmSha256 = prepared.GetProperty("confirmSha256").GetString() };
            Assert.Equal("prepared", Data(await Post(client, prefix + "status", proof)).GetProperty("state").GetString());
            Assert.Equal(500, (await Post(client, prefix + "apply", new { proof.planId, confirmSha256 = "错误确认" })).GetProperty("code").GetInt32());
            var cliSave = await RunPlatformSchemaCli(address, "import-apply", Project, session, "--plan-id", proof.planId!, "--confirm", proof.confirmSha256!); Assert.True(cliSave.Code == 0, cliSave.Error); var result = Json(cliSave.Output);
            Assert.True(result.GetProperty("configurationSaved").GetBoolean()); Assert.False(result.GetProperty("executesDdl").GetBoolean()); Assert.Equal(0, result.GetProperty("pagesCreated").GetInt32());
            var saved = Assert.Single(result.GetProperty("tables").EnumerateArray()); var id = saved.GetProperty("tableId").GetString()!; configIds.Add(id);
            var catalog = await metadata.LoadProjectAsync(Project); Assert.Contains(catalog.ConfigTables, row => row.Id == id && row.TableName == table);
            Assert.Equal(new[] { "attachment", "delete_no", "id", "name", "value" }, catalog.ConfigColumns.Where(row => row.ConfigTableId == id).Select(row => row.ColumnName).Order());
            Assert.DoesNotContain(catalog.ConfigPages, row => row.ConfigTableId == id); Assert.Single(catalog.ConfigIndexes, index => index.GetProperty("configTableId").GetString() == id);
            Assert.Equal("saved", Data(await Post(client, prefix + "status", proof)).GetProperty("state").GetString());
            Assert.Equal(500, (await Post(client, prefix + "apply", proof)).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, prefix + "prepare", new { tableNames = new[] { table } })).GetProperty("code").GetInt32());
            var cliStatus = await RunPlatformSchemaCli(address, "import-status", Project, session, "--plan-id", proof.planId!, "--confirm", proof.confirmSha256!); Assert.True(cliStatus.Code == 0, cliStatus.Error); Assert.Equal("saved", Json(cliStatus.Output).GetProperty("state").GetString());
            Assert.Equal(before, (await schema.InspectForImportAsync([table])).Snapshot.Fingerprint);
            await using var count = connection.CreateCommand(); count.CommandText = $"SELECT COUNT(*) FROM `{table}` WHERE id=1 AND name='源业务行' AND value=12.50"; Assert.Equal(1L, Convert.ToInt64(await count.ExecuteScalarAsync()));
        }
        finally
        {
            // 如保存已提交但响应丢失，也按独占源表名发现本轮配置，不能误清理其他任务记录。
            foreach (var row in (await store.ReadIncludingDeletedAsync("config_table", new Dictionary<string,object?> { ["table_name"] = table })))
            { var id = SqlRowStore.Text(row, "id"); if (!configIds.Contains(id)) configIds.Add(id); }
            foreach (var id in configIds)
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/auto/config_table/6191083005411328") { Content = JsonContent.Create(new { id }) };
                Data(await Body(await client.SendAsync(request)));
                Assert.Equal(500, (await Post(client, "/admin/sys/database/import/prepare", new { tableNames = new[] { table } })).GetProperty("code").GetInt32());
            }
            await Execute($"DROP TABLE `{table}`");
            await using var check = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await check.OpenAsync(); await using var command = check.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name"; var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = table; command.Parameters.Add(parameter); Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
    }
}
