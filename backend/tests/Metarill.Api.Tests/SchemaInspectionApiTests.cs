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
    /// <summary>合法功能表/视图定义明确标记排除，不能因适配器未检查而误报物理缺失。</summary>
    [MySqlApiFact]
    public async Task Non_physical_definitions_are_excluded_instead_of_reported_as_missing_tables()
    {
        var (factory, client) = await Login(); await using var host = factory; using var http = client;
        var ids = new List<string>(); const string tableRoute = "/admin/auto/config_table/6191083005411328";
        try
        {
            foreach (var (function, view) in new[] { (1, 0), (0, 1) })
            {
                var name = "mt_ix_" + Guid.NewGuid().ToString("N");
                var saved = Data(await Post(client, tableRoute + "/add", new { name = "反查排除验收", tableName = name, yzAdminTypeId = "2200000000000100001", isRongyu = 1, isFunctionTable = function, isView = view }));
                var id = saved.GetProperty("id").GetString()!; ids.Add(id);
                var full = Data(await Post(client, "/admin/sys/database/inspect", new { }));
                Assert.DoesNotContain(full.GetProperty("managedTables").EnumerateArray(), entry => entry.GetString() == name);
                Assert.DoesNotContain(full.GetProperty("missingTables").EnumerateArray(), entry => entry.GetString() == name);
                Assert.Contains(full.GetProperty("excludedTables").EnumerateArray(), entry => entry.GetProperty("tableId").GetString() == id && entry.GetProperty("tableName").GetString() == name);
                var denied = await Post(client, "/admin/sys/database/inspect", new { tableId = id });
                Assert.Equal(500, denied.GetProperty("code").GetInt32()); Assert.Contains("不支持", denied.GetProperty("msg").GetString());
            }
        }
        finally
        {
            foreach (var id in ids) Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api" + tableRoute) { Content = JsonContent.Create(new { id }) })));
        }
    }

}
