using System.Net.Http.Json;
using System.Text;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>真实业务库快照与结构指纹可回读，严格限定当前项目且没有执行计划或写入副作用。</summary>
    [MySqlApiFact]
    public async Task Managed_schema_inspection_is_readonly_and_bound_to_authenticated_project()
    {
        var (factory, client) = await Login(); await using var host = factory; using var http = client;
        const string path = "/admin/sys/database/inspect";
        var schemas = factory.Services.GetRequiredService<ProjectSchemaService>();
        var historyBefore = schemas.Resolve(Project).History.Count;
        var result = Data(await Post(client, path, new { }));
        Assert.False(result.GetProperty("executable").GetBoolean()); Assert.Equal(Project, result.GetProperty("projectId").GetString());
        var snapshot = result.GetProperty("snapshot"); Assert.Equal("mysql", snapshot.GetProperty("provider").GetString());
        Assert.Equal("metarill_business_test", snapshot.GetProperty("schema").GetProperty("database").GetString());
        Assert.Matches("^[a-f0-9]{64}$", snapshot.GetProperty("fingerprint").GetString());
        var names = snapshot.GetProperty("schema").GetProperty("tables").EnumerateArray().Select(table => table.GetProperty("name").GetString()).ToArray();
        Assert.Contains("demo_customer", names); Assert.Contains("yz_file_library", names); Assert.DoesNotContain("config_table", names);
        var single = Data(await Post(client, path, new { tableId = "2200000000000100010" }));
        var table = Assert.Single(single.GetProperty("snapshot").GetProperty("schema").GetProperty("tables").EnumerateArray());
        Assert.Equal("demo_customer", table.GetProperty("name").GetString()); Assert.StartsWith("CREATE TABLE", table.GetProperty("snapshotDefinition").GetString());
        Assert.False(single.TryGetProperty("id", out _)); Assert.False(single.TryGetProperty("changes", out _));
        var registry = factory.Services.GetRequiredService<MetadataRegistry>();
        foreach (var invalid in new object[] { new { projectId = registry.Platform.YzProjectId }, new { tableId = registry.Platform.ConfigTables.First().Id }, new { tableId = "x;DROP TABLE demo_customer" }, new { tableId = (string?)null }, new { sql = "SELECT 1" } })
            Assert.Equal(500, (await Post(client, path, invalid)).GetProperty("code").GetInt32());
        using var duplicate = new StringContent("{\"tableId\":\"2200000000000100010\",\"tableId\":\"2\"}", Encoding.UTF8, "application/json");
        Assert.Equal(500, (await Body(await client.PostAsync("/api" + path, duplicate))).GetProperty("code").GetInt32());
        using var anonymous = factory.CreateClient(); Assert.Equal(500, (await Body(await anonymous.PostAsJsonAsync("/api" + path, new { }))).GetProperty("code").GetInt32());
        Assert.Equal(historyBefore, schemas.Resolve(Project).History.Count);
        Assert.Equal(snapshot.GetProperty("fingerprint").GetString(), Data(await Post(client, path, new { })).GetProperty("snapshot").GetProperty("fingerprint").GetString());
    }
}
