using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Cli;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>未登记表/视图只读目录、原会话CLI及MCP共用正式服务与真实MySQL，清理仅限本轮独占随机对象。</summary>
    [MySqlApiFact]
    public async Task Physical_catalog_and_platform_cli_preserve_project_and_readonly_contracts()
    {
        var (factory, client) = await Login(); await using var host = factory; using var http = client;
        var sessions = factory.Services.GetRequiredService<TargetDatabaseSessions>();
        var metadata = factory.Services.GetRequiredService<MetadataRepository>();
        var schema = factory.Services.GetRequiredService<ProjectSchemaService>().Resolve(Project);
        var history = schema.History.Count;
        var before = await metadata.LoadProjectAsync(Project);
        var name = "mt_catalog_" + Guid.NewGuid().ToString("N");
        var view = name + "_view";
        var unusual = name + "-legacy";
        var created = new List<string>();
        var evidenceRoot = Environment.GetEnvironmentVariable("METARILL_CATALOG_EVIDENCE");
        var evidence = evidenceRoot is null ? null : Path.Combine(evidenceRoot, name);
        if (evidence is not null)
        {
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "intent.json"), JsonSerializer.Serialize(new { database = sessions.Business.Options.ExpectedDatabase, objects = new[] { name, view, unusual } }));
        }
        await using var connection = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await connection.OpenAsync();
        async Task Execute(string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        try
        {
            await Execute($"CREATE TABLE `{name}` (`id` bigint NOT NULL PRIMARY KEY) ENGINE=InnoDB"); created.Add(name);
            await Execute($"CREATE TABLE `{unusual}` (`id` bigint NOT NULL PRIMARY KEY) ENGINE=InnoDB"); created.Add(unusual);
            await Execute($"CREATE VIEW `{view}` AS SELECT id FROM `{name}`"); created.Add(view);
            const string path = "/admin/sys/database/catalog";
            var catalog = Data(await Post(client, path, new { }));
            Assert.False(catalog.GetProperty("executable").GetBoolean()); Assert.Equal(Project, catalog.GetProperty("projectId").GetString());
            Assert.Equal("metarill_business_test", catalog.GetProperty("database").GetString());
            Assert.Matches("^[a-f0-9]{64}$", catalog.GetProperty("catalogFingerprint").GetString());
            JsonElement Item(string objectName) => Assert.Single(catalog.GetProperty("objects").EnumerateArray(), item => item.GetProperty("physical").GetProperty("name").GetString() == objectName);
            Assert.False(Item(name).GetProperty("registeredInCurrentProject").GetBoolean());
            Assert.True(Item(name).GetProperty("physical").GetProperty("supportedIdentifier").GetBoolean());
            Assert.Equal("BASE TABLE", Item(name).GetProperty("physical").GetProperty("objectType").GetString());
            Assert.Equal("VIEW", Item(view).GetProperty("physical").GetProperty("objectType").GetString());
            Assert.False(Item(unusual).GetProperty("physical").GetProperty("supportedIdentifier").GetBoolean());
            Assert.True(Item("demo_customer").GetProperty("registeredInCurrentProject").GetBoolean());
            Assert.True(Item("yz_file_library").GetProperty("registeredInCurrentProject").GetBoolean());
            // 未登记物理表不混入原受管反查，更不会自动新增config_table。
            var managed = Data(await Post(client, "/admin/sys/database/inspect", new { }));
            Assert.DoesNotContain(managed.GetProperty("snapshot").GetProperty("schema").GetProperty("tables").EnumerateArray(), item => item.GetProperty("name").GetString() == name);
            foreach (var invalid in new object[] { new { tableName = name }, new { tableId = "2200000000000100010" }, new { projectId = "1" }, new { database = "mysql" }, new { sql = "SELECT 1" } })
                Assert.Equal(500, (await Post(client, path, invalid)).GetProperty("code").GetInt32());
            using var duplicate = new StringContent("{\"sql\":\"SELECT 1\",\"sql\":\"SELECT 2\"}", Encoding.UTF8, "application/json");
            Assert.Equal(500, (await Body(await client.PostAsync("/api" + path, duplicate))).GetProperty("code").GetInt32());
            using var anonymous = factory.CreateClient();
            Assert.Equal(500, (await Body(await anonymous.PostAsJsonAsync("/api" + path, new { }))).GetProperty("code").GetInt32());
            var session = Assert.Single(client.DefaultRequestHeaders.GetValues("loginStateId"));
            foreach (var command in new[] { "catalog", "inspect" })
            {
                using var output = new StringWriter();
                string[] args = ["schema", command, "--server", "http://localhost/api", "--project-id", Project];
                if (command == "inspect") args = args.Concat(["--table-id", "2200000000000100010"]).ToArray();
                await PlatformSchemaCommand.ExecuteAsync(args, client, _ => session, output);
                var result = Json(output.ToString()); Assert.Equal(Project, result.GetProperty("projectId").GetString()); Assert.False(result.GetProperty("executable").GetBoolean());
                if (command == "catalog") Assert.Equal(catalog.GetProperty("catalogFingerprint").GetString(), result.GetProperty("catalogFingerprint").GetString());
                else Assert.Single(result.GetProperty("snapshot").GetProperty("schema").GetProperty("tables").EnumerateArray());
            }
            await using var transport = new ModelContextProtocol.Client.HttpClientTransport(new() { Endpoint = new Uri(client.BaseAddress!, "api/mcp"), TransportMode = ModelContextProtocol.Client.HttpTransportMode.StreamableHttp }, client, ownsHttpClient: false);
            await using var mcp = await ModelContextProtocol.Client.McpClient.CreateAsync(transport);
            var resultMcp = await mcp.CallToolAsync("inspect_database_catalog", new Dictionary<string, object?> { ["data"] = new { } });
            Assert.NotEqual(true, resultMcp.IsError);
            var directory = Json(Assert.Single(resultMcp.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text);
            Assert.Equal(catalog.GetProperty("catalogFingerprint").GetString(), directory.GetProperty("catalogFingerprint").GetString());
            var after = await metadata.LoadProjectAsync(Project);
            Assert.Equal(before.ConfigTables.Select(table => table.Id).Order(), after.ConfigTables.Select(table => table.Id).Order());
            Assert.Equal(history, schema.History.Count);
        }
        finally
        {
            foreach (var objectName in created.AsEnumerable().Reverse()) await Execute($"DROP {(objectName == view ? "VIEW" : "TABLE")} `{objectName}`");
            // 使用独立连接后验，避免原连接或事务可见性造成清理误判。
            await using var check = sessions.Business.Provider.CreateConnection(sessions.Business.Options); await check.OpenAsync();
            await using var command = check.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN (@table,@view,@unusual)";
            foreach (var (key, value) in new[] { ("@table", name), ("@view", view), ("@unusual", unusual) }) { var parameter = command.CreateParameter(); parameter.ParameterName = key; parameter.Value = value; command.Parameters.Add(parameter); }
            var remaining = Convert.ToInt64(await command.ExecuteScalarAsync());
            if (evidence is not null) await File.WriteAllTextAsync(Path.Combine(evidence, "cleanup.json"), JsonSerializer.Serialize(new { objects = new[] { name, view, unusual }, remaining, independentConnection = true, observedAt = DateTimeOffset.UtcNow }));
            Assert.Equal(0L, remaining);
        }
    }
}
