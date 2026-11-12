using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>用真实发布目录、普通账号及新库验证共享级联和逐动作精确范围。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossExactScopeMySqlTests
{
    /// <summary>同首列的两个来源只有一个可覆盖，同时核对导出及写前拒绝。</summary>
    [FreshInstallMySqlFact]
    public async Task Production_shared_cascade_limits_reads_and_overwrite_by_exact_role()
    {
        var loginRedis = FreshInstallConfiguration.ReadLoginRedis();
        var options = FreshInstallConfiguration.Read("AgentDatabase", "agent");
        using var session = new DatabaseSession(options);
        await FreshInstallConfiguration.VerifyEmptyLeaseAsync(session);
        var catalog = SharedCrossExactScopeGenerationTests.Catalog();
        var repository = NonRedundantApplicationFixture.Root();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-shared-exact-mysql");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var password = Guid.NewGuid().ToString("N");
        var project = long.Parse(catalog.YzProjectId, CultureInfo.InvariantCulture);
        const long country = 9007199254742001, area = country + 1, place = country + 2;
        const long firstWarehouse = country + 3, secondWarehouse = country + 4;
        const long allowed = country + 5, denied = country + 6;
        const string source = "shared_cross_source";
        const string route = "/api/admin/auto/" + source + "/201";
        const string username = "shared-exact-user";

        try
        {
            new ApplicationPackager().Create(catalog, directory, "Example.SharedExactScope", repository);
            await NonRedundantApplicationFixture.BuildAsync(directory, "restore");
            await NonRedundantApplicationFixture.BuildAsync(directory, "build");
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply"));
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security"));
            var securityPath = Path.Combine(directory, "backend/metadata/security.json");
            var securityCatalog = JsonSerializer.Deserialize<MetadataCatalog>(
                await File.ReadAllTextAsync(securityPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var security = new SqlRowStore(session, securityCatalog);
            var ids = new SnowflakeIdGenerator(933);
            var role = ids.NextId();
            await security.InsertAsync("yz_role", new Dictionary<string, object?>
            {
                ["id"] = role, ["yz_project_id"] = project, ["yz_admin_type_id"] = 5L,
                ["name"] = username, ["is_scope"] = 1, ["is_super"] = 0, ["is_built_in"] = 0
            });
            await security.InsertAsync("yz_manager", new Dictionary<string, object?>
            {
                ["id"] = ids.NextId(), ["yz_project_id"] = project, ["yz_admin_type_id"] = 5L,
                ["name"] = username, ["real_name"] = "scope-a", ["password"] = PasswordHasher.Hash(password),
                ["is_disabled"] = 0, ["yz_role_ids"] = JsonSerializer.Serialize(new[] { Id(role) })
            });
            foreach (var page in catalog.ConfigPages)
                await security.InsertAsync("yz_role_permission", new Dictionary<string, object?>
                {
                    ["id"] = ids.NextId(), ["yz_project_id"] = project, ["yz_role_id"] = role,
                    ["config_page_id"] = long.Parse(page.Id, CultureInfo.InvariantCulture),
                    ["permission_values"] = "[\"is_all\"]"
                });

            var business = new SqlRowStore(session, catalog);
            await business.InsertAsync("shared_cross_country", Target(country, "country"));
            await business.InsertAsync("shared_cross_area", Target(area, "area", "shared_cross_country_id", country));
            await business.InsertAsync("shared_cross_place", Target(place, "place", "shared_cross_area_id", area));
            await business.InsertAsync("shared_cross_warehouse", Target(firstWarehouse, "first", "shared_cross_area_id", area));
            await business.InsertAsync("shared_cross_warehouse", Target(secondWarehouse, "second", "shared_cross_area_id", area));
            await business.InsertAsync(source, Source(allowed, "row-allowed", "scope-a"));
            await business.InsertAsync(source, Source(denied, "row-denied", "scope-b"));

            await using var host = await FreshHost.StartAsync(HostInfo(), "/health");
            var client = host.Client;
            await FreshWebHttp.LoginAsync(client, username, password, form: true);
            var permissions = await FreshWebHttp.SendAsync(client, HttpMethod.Get, "/api/admin/sys/yz_manager/getCurrentPermissions");
            Assert.False(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());
            var pageData = (await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/page", new PageRequest()))
                .GetProperty("data");
            Assert.Equal(1, pageData.GetProperty("total").GetInt64());
            var visible = Assert.Single(pageData.GetProperty("records").EnumerateArray());
            Assert.Equal(Id(allowed), visible.GetProperty("id").GetString());
            Assert.False(visible.TryGetProperty("scopeKey", out _));
            var count = await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/count", new PageRequest());
            Assert.Equal(1, count.GetProperty("data").GetInt64());
            var detail = await FreshWebHttp.SendAsync(client, HttpMethod.Get, route + "/detail/" + Id(allowed));
            Assert.Equal("country", detail.GetProperty("data").GetProperty("countryName").GetString());
            using var forbiddenDetail = await client.GetAsync(route + "/detail/" + Id(denied));
            Assert.Equal(HttpStatusCode.OK, forbiddenDetail.StatusCode);
            Assert.Equal(500, (await forbiddenDetail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetInt32());

            var ticket = (await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/initExport", new PageRequest()))
                .GetProperty("data").GetString();
            using var export = await client.GetAsync(ExportDownloadAccess.DownloadPath + "?exportId=" + ticket);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync()));
            var text = workbook.Worksheet(1).RowsUsed().SelectMany(row => row.CellsUsed()).Select(cell => cell.GetString()).ToArray();
            Assert.Contains("row-allowed", text);
            Assert.DoesNotContain("row-denied", text);

            await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/update",
                new { id = Id(denied), warehouseId = Id(secondWarehouse) }, HttpStatusCode.InternalServerError);
            await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/copy/" + Id(denied),
                new { }, HttpStatusCode.InternalServerError);
            await FreshWebHttp.SendAsync(client, HttpMethod.Delete, route,
                new { id = Id(denied) }, HttpStatusCode.InternalServerError);
            await FreshWebHttp.SendAsync(client, HttpMethod.Post, route + "/importDatas201", new
            {
                requestJson = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["placeId"] = "country/area/place", ["warehouseId"] = "country/area/second"
                    }
                }
            });
            var rows = await business.ReadAsync(source, new Dictionary<string, object?> { ["yz_project_id"] = project });
            Assert.Equal(2, rows.Count);
            Assert.Equal(Id(secondWarehouse), SqlRowStore.Text(Assert.Single(rows, row => SqlRowStore.Id(row) == allowed), "warehouse_id"));
            Assert.Equal(Id(firstWarehouse), SqlRowStore.Text(Assert.Single(rows, row => SqlRowStore.Id(row) == denied), "warehouse_id"));
            Assert.All(rows, row => Assert.Null(row["country_id"]));
            await FreshWebHttp.SendAsync(client, HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login", new { });
        }
        finally
        {
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }

        /// <summary>私有连接参数只传环境，宿主按真实 Production Web 认证启动。</summary>
        ProcessStartInfo HostInfo(string? operation = null)
        {
            var hostDirectory = Path.Combine(directory, "backend/Application.Host");
            var info = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory);
            info.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Debug/net10.0/Application.Host.dll"));
            if (operation is not null) info.ArgumentList.Add(operation);
            info.Environment["Authentication__LoginFailures__ConnectionString"] = loginRedis;
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            info.Environment["Database__Provider"] = "mysql";
            info.Environment["Database__ExpectedDatabase"] = options.ExpectedDatabase;
            info.Environment["Database__ConnectionString"] = options.ConnectionString;
            info.Environment["Authentication__Mode"] = "Web";
            info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
            info.Environment["Authentication__Bootstrap__Username"] = "shared-exact-owner";
            info.Environment["Authentication__Bootstrap__Password"] = password;
            info.Environment["Ids__WorkerId"] = "932";
            info.Environment["Files__RootDirectory"] = Path.Combine(directory, "files");
            info.Environment["Exports__RootDirectory"] = Path.Combine(directory, "exports");
            return info;
        }

        /// <summary>两条来源共享同一个原导入键，仅物理精确范围列取不同值。</summary>
        Dictionary<string, object?> Source(long id, string name, string scope) => new()
        {
            ["id"] = id, ["name"] = name, ["yz_project_id"] = project, ["scope_key"] = scope,
            ["place_id"] = Id(place), ["warehouse_id"] = Id(firstWarehouse)
        };

        /// <summary>所有目标由本轮独占库插入，父外键保持真实 bigint。</summary>
        Dictionary<string, object?> Target(long id, string name, string? parentField = null, long? parent = null)
        {
            var row = new Dictionary<string, object?> { ["id"] = id, ["name"] = name, ["yz_project_id"] = project, ["status"] = 1 };
            if (parentField is not null) row.Add(parentField, parent);
            return row;
        }
    }

    /// <summary>超过 JavaScript 安全整数的业务 ID 保持字符串边界。</summary>
    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
}
