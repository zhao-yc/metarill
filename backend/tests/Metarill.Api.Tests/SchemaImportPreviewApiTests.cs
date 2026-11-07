using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>真实源库→逻辑草案、Kestrel/CLI/MCP及MDL锁验证；仅本轮随机物理对象有DDL，不保存任何配置。</summary>
    [MySqlApiFact]
    public async Task Reverse_schema_preview_is_lossless_readonly_and_holds_real_metadata_locks()
    {
        var (factory, client) = await Login(factory => { factory.UseKestrel(0); return factory; }); await using var host = factory; using var http = client;
        var sessions = factory.Services.GetRequiredService<TargetDatabaseSessions>(); var schema = factory.Services.GetRequiredService<ProjectSchemaService>().Resolve(Project);
        var metadata = factory.Services.GetRequiredService<MetadataRepository>(); var beforeMetadata = JsonSerializer.Serialize(await metadata.LoadProjectAsync(Project)); var history = schema.History.Count;
        var name = "mt_imp_" + Guid.NewGuid().ToString("N"); var bad = name + "_bad"; var view = name + "_view"; var names = new[] { name, bad, view }; var created = new List<string>();
        var evidenceRoot = Environment.GetEnvironmentVariable("METARILL_IMPORT_DRAFT_EVIDENCE"); var evidence = evidenceRoot is null ? null : Path.Combine(evidenceRoot, name);
        if (evidence is not null) { Directory.CreateDirectory(evidence); await File.WriteAllTextAsync(Path.Combine(evidence, "intent.json"), JsonSerializer.Serialize(new { database = sessions.Business.Options.ExpectedDatabase, names })); }
        await using var connection = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await connection.OpenAsync();
        // DDL只使用本方法生成的独占名称，不接收用户SQL，不创建新库或账号。
        async Task Execute(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        try
        {
            await Execute($"CREATE TABLE `{name}` (`id` bigint NOT NULL PRIMARY KEY,`name` varchar(64) NOT NULL DEFAULT 'O''Brien',`amount` decimal(18,2) NULL DEFAULT 12.50,`active` tinyint(1) NULL DEFAULT 1,`payload` json NULL,`content` text NULL,`attachment` varbinary(16) NULL,`audit_time` datetime(6) NULL,`clock` time(6) NULL,`date_only` date NULL,`counter` int NULL DEFAULT 0,`delete_no` datetime(6) NULL,UNIQUE INDEX yzp_uk_name (`name`),INDEX yzp_idx_amount (`amount`,`counter`)) ENGINE=InnoDB"); created.Add(name);
            await Execute($"INSERT INTO `{name}` (id,name) VALUES (1,'private-business-row-never-in-preview')");
            await Execute($"CREATE TABLE `{bad}` (`id` bigint NOT NULL AUTO_INCREMENT PRIMARY KEY,`label` varchar(64) NULL DEFAULT '',`value` float NULL,`quantity` int NULL,`parent_id` bigint NULL,`calculated` int GENERATED ALWAYS AS (`quantity`+1) STORED,INDEX idx_prefix (`label`(5)),INDEX idx_desc (`quantity` DESC),INDEX idx_hidden (`quantity`) INVISIBLE,CONSTRAINT `fk_{name}` FOREIGN KEY (`parent_id`) REFERENCES `{name}` (`id`),CONSTRAINT `ck_{name}` CHECK (`quantity`>=0)) ENGINE=InnoDB"); created.Add(bad);
            await Execute($"CREATE VIEW `{view}` AS SELECT id FROM `{name}`"); created.Add(view);
            const string route = "/admin/sys/database/import/preview";
            var preview = Data(await Post(client, route, new { tableNames = new[] { name } }));
            Assert.False(preview.GetProperty("executable").GetBoolean()); Assert.False(preview.GetProperty("canImport").GetBoolean()); Assert.True(preview.GetProperty("pendingChecks").GetArrayLength() >= 4);
            var draft = preview.GetProperty("draft"); Assert.True(draft.GetProperty("columnsAndIndexesRepresentable").GetBoolean(), draft.ToString()); Assert.True(draft.GetProperty("usesDraftIdentifiers").GetBoolean());
            var model = draft.GetProperty("logicalMetadata"); Assert.Equal(12, model.GetProperty("configColumns").GetArrayLength()); Assert.Equal(2, model.GetProperty("configIndexes").GetArrayLength()); Assert.Empty(model.GetProperty("configPages").EnumerateArray());
            Assert.DoesNotContain(model.GetProperty("configColumns").EnumerateArray(), column => column.GetProperty("columnName").GetString() == "yz_project_id");
            Assert.DoesNotContain("private-business-row", preview.GetRawText()); Assert.False(preview.TryGetProperty("planId", out _));
            var rejected = Data(await Post(client, route, new { tableNames = new[] { bad } })); var issues = rejected.GetProperty("draft").GetProperty("issues").EnumerateArray().ToArray();
            Assert.False(rejected.GetProperty("draft").GetProperty("columnsAndIndexesRepresentable").GetBoolean()); Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "COLUMN_UNREPRESENTABLE"); Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "INDEX_FEATURE_UNSUPPORTED"); Assert.Contains(issues, issue => issue.GetProperty("code").GetString() == "CONSTRAINT_UNSUPPORTED");
            foreach (var invalid in new object[] { new { tableNames = new[] { view } }, new { tableNames = new[] { "demo_customer" } }, new { tableNames = new[] { name }, confirmed = true }, new { tableNames = new[] { name }, projectId = Project }, new { tableNames = new[] { "other_db.table" } }, new { tableNames = (string[]?)null } }) Assert.Equal(500, (await Post(client, route, invalid)).GetProperty("code").GetInt32());
            using var anonymous = factory.CreateClient(); Assert.Equal(500, (await Body(await anonymous.PostAsJsonAsync("/api" + route, new { tableNames = new[] { name } }))).GetProperty("code").GetInt32());
            var address = Assert.Single(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses); var session = Assert.Single(client.DefaultRequestHeaders.GetValues("loginStateId"));
            var cli = await RunPlatformSchemaCli(address, "import-preview", Project, session, "--tables", name); Assert.True(cli.Code == 0, cli.Error); Assert.Equal(draft.GetProperty("source").GetProperty("snapshot").GetProperty("fingerprint").GetString(), Json(cli.Output).GetProperty("draft").GetProperty("source").GetProperty("snapshot").GetProperty("fingerprint").GetString());
            await using var transport = new ModelContextProtocol.Client.HttpClientTransport(new() { Endpoint = new Uri(client.BaseAddress!, "api/mcp"), TransportMode = ModelContextProtocol.Client.HttpTransportMode.StreamableHttp }, client, ownsHttpClient: false); await using var mcp = await ModelContextProtocol.Client.McpClient.CreateAsync(transport);
            var mcpResult = await mcp.CallToolAsync("preview_database_metadata_import", new Dictionary<string, object?> { ["data"] = new { tableNames = new[] { name } } }); Assert.NotEqual(true, mcpResult.IsError);
            var mcpData = Json(Assert.Single(mcpResult.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text); Assert.Equal(preview.GetProperty("metadataFingerprint").GetString(), mcpData.GetProperty("metadataFingerprint").GetString());
            var previousFingerprint = draft.GetProperty("source").GetProperty("snapshot").GetProperty("fingerprint").GetString();
            await schema.WithImportInspectionAsync([name], async (inspection, token) =>
            {
                Assert.Equal(previousFingerprint, inspection.Snapshot.Fingerprint);
                await using var ddl = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await ddl.OpenAsync(token);
                await using var command = ddl.CreateCommand(); command.CommandText = "SET SESSION lock_wait_timeout=1"; await command.ExecuteNonQueryAsync(token);
                command.CommandText = $"ALTER TABLE `{name}` ADD COLUMN after_lock int NULL";
                var error = await Assert.ThrowsAsync<MySqlConnector.MySqlException>(async () => { await command.ExecuteNonQueryAsync(token); }); Assert.Equal(1205, error.Number);
                return true;
            });
            await Execute($"ALTER TABLE `{name}` ADD COLUMN after_lock int NULL");
            Assert.NotEqual(previousFingerprint, (await schema.InspectForImportAsync([name])).Snapshot.Fingerprint);
            await Assert.ThrowsAsync<InvalidOperationException>(() => schema.WithImportInspectionAsync<bool>([name], (_, _) => throw new InvalidOperationException("回调故障")));
            await Execute($"ALTER TABLE `{name}` ADD COLUMN after_rollback int NULL");
            Assert.Equal(beforeMetadata, JsonSerializer.Serialize(await metadata.LoadProjectAsync(Project))); Assert.Equal(history, schema.History.Count);
            await using var rows = connection.CreateCommand(); rows.CommandText = $"SELECT COUNT(*) FROM `{name}` WHERE id=1 AND name='private-business-row-never-in-preview'"; Assert.Equal(1L, Convert.ToInt64(await rows.ExecuteScalarAsync()));
        }
        finally
        {
            foreach (var objectName in created.AsEnumerable().Reverse()) await Execute($"DROP {(objectName == view ? "VIEW" : "TABLE")} `{objectName}`");
            await using var check = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await check.OpenAsync(); await using var command = check.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN (@first,@second,@third)";
            foreach (var (parameterName, value) in new[] { ("@first", name), ("@second", bad), ("@third", view) }) { var parameter = command.CreateParameter(); parameter.ParameterName = parameterName; parameter.Value = value; command.Parameters.Add(parameter); }
            var remaining = Convert.ToInt64(await command.ExecuteScalarAsync()); if (evidence is not null) await File.WriteAllTextAsync(Path.Combine(evidence, "cleanup.json"), JsonSerializer.Serialize(new { names, remaining, independentConnection = true })); Assert.Equal(0L, remaining);
        }
    }
}
