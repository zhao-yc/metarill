using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>没有末段故障专用配置时明确跳过，不把旧成功安装租约或普通CI计为实库回滚。</summary>
public sealed class PlatformSeedRollbackNativeMySqlFactAttribute : FactAttribute
{
    public PlatformSeedRollbackNativeMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable(PlatformSeedRollbackNativePreparation.EnvironmentKey))) Skip = "未取得本轮平台种子末段故障专属双库租约。";
    }
}

/// <summary>真实生产初始化在平台及示例各自最后非空种子行失败，再移除本轮约束恢复；DDL不冒充可整体回滚。</summary>
public sealed partial class FreshInstallMySqlTests
{
    /// <summary>同目录全部回滚、已提交目录保留、初始化身份尚未创建，恢复后完整种子及真实Web必须成功。</summary>
    [PlatformSeedRollbackNativeMySqlFact]
    public async Task Platform_seed_late_failures_rollback_each_catalog_and_recover_in_production_host()
    {
        var root = SourceRoot(); var native = PlatformSeedRollbackNativePreparation.Read(root);
        var platform = FreshInstallConfiguration.Read("Database", "platform"); var business = FreshInstallConfiguration.Read("BusinessDatabase", "business");
        using var platformSession = new DatabaseSession(platform); using var businessSession = new DatabaseSession(business);
        using var settingsJson = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable(PlatformSeedRollbackNativePreparation.EnvironmentKey)!));
        var run = settingsJson.RootElement.GetProperty("RunId").GetString()!;
        var pin = settingsJson.RootElement.GetProperty("InstancePin");
        Assert.Equal(pin.GetProperty("serverUuid").GetString(), Convert.ToString(await ScalarAsync(platformSession, "SELECT @@server_uuid"), CultureInfo.InvariantCulture));
        Assert.Equal(pin.GetProperty("hostname").GetString(), Convert.ToString(await ScalarAsync(platformSession, "SELECT @@hostname"), CultureInfo.InvariantCulture));
        Assert.Equal(0L, await CountTablesAsync(platformSession)); Assert.Equal(0L, await CountTablesAsync(businessSession));
        var catalog = await CatalogAsync(Path.Combine(root, "backend/metadata/platform.json")); var demo = await CatalogAsync(Path.Combine(root, "backend/metadata/demo.json"));
        var files = await CatalogAsync(Path.Combine(root, "backend/metadata/files.json")); var security = await CatalogAsync(Path.Combine(root, "backend/metadata/security.json"));
        var platformEnd = PlatformSeedRollbackCheckpoint.For(catalog); var demoEnd = PlatformSeedRollbackCheckpoint.For(demo);
        var registry = new Metarill.Runtime.Metadata.MetadataRegistry(Path.Combine(root, "backend/metadata/platform.json"), Path.Combine(root, "backend/metadata/demo.json"));
        var models = new ProjectPhysicalModels(files, security, registry);
        var platformPhysical = models.Compose(catalog);
        var schema = new SchemaService(platformSession); var plan = await schema.PreviewAsync(platformPhysical); await schema.ApplyAsync(plan);
        Assert.Equal(platformPhysical.ConfigTables.Count, await CountTablesAsync(platformSession)); Assert.Equal(0L, await CountTablesAsync(businessSession));
        var credentialsRoot = Path.Combine(native.EvidenceDirectory, "credentials"); var credentials = Path.Combine(credentialsRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(credentials);
        var password = Guid.NewGuid().ToString("N"); var settings = Path.Combine(credentials, "host.json");
        await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new { Metarill = new { Database = platform, BusinessDatabase = business, WorkerId = 855, Bootstrap = new { Username = "seed-rollback-owner", Password = password } } }));
        ProcessStartInfo Start(string? operation = null)
        {
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = root, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(native.AssemblyPath); if (operation is not null) info.ArgumentList.Add(operation);
            info.Environment["METARILL_SETTINGS"] = settings; info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            info.Environment["Metarill__Bootstrap__Password"] = password;
            info.Environment["Metarill__Database__ConnectionString"] = platform.ConnectionString; info.Environment["Metarill__BusinessDatabase__ConnectionString"] = business.ConnectionString;
            info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = native.EvidenceDirectory;
            return info;
        }
        async Task Execute(string sql)
        {
            await using var connection = platformSession.Provider.CreateConnection(platform); await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        async Task FailAt(PlatformSeedRollbackCheckpoint checkpoint, string label)
        {
            var constraint = "mt_seed_" + label + "_" + run;
            await Execute($"ALTER TABLE `{checkpoint.Table}` ADD CONSTRAINT `{constraint}` CHECK (`id` <> {checkpoint.LastId.ToString(CultureInfo.InvariantCulture)})");
            var result = await RunInitializationAsync(Start("--initialize"));
            Assert.NotEqual(0, result.ExitCode);
            Assert.True((result.Output + result.Error).Contains("Check constraint '" + constraint + "' is violated", StringComparison.Ordinal), "必须由本轮最后种子行CHECK触发，不能用连接或DDL失败替代回滚。");
            // 约束保留到所有回滚断言完成；失败时由外层同轮整库清理，不擅自重试或修改旧库。
        }
        var store = new SqlRowStore(platformSession, catalog); Exception? primary = null;
        try
        {
            await FailAt(platformEnd, "platform");
            foreach (var table in platformPhysical.ConfigTables) Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `" + table.TableName + "`"), CultureInfo.InvariantCulture));
            Assert.Equal(0L, await CountTablesAsync(businessSession));
            await File.WriteAllTextAsync(Path.Combine(native.EvidenceDirectory, "platform-rollback.json"), JsonSerializer.Serialize(new { passed = true, checkpoint = platformEnd, allPlatformTablesEmpty = true, businessTables = 0, ddlRetained = platformPhysical.ConfigTables.Count }));
            await Execute($"ALTER TABLE `{platformEnd.Table}` DROP CHECK `mt_seed_platform_{run}`");
            await FailAt(demoEnd, "demo");
            var platformBefore = await SnapshotCatalogSeedsAsync(store, catalog);
            await AssertCatalogSeedCountsAsync(platformSession, catalog, present: true); await AssertCatalogSeedCountsAsync(platformSession, demo, present: false);
            foreach (var table in new[] { "yz_project", "yz_role", "yz_manager", "yz_manager_devices" }) Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `" + table + "`"), CultureInfo.InvariantCulture));
            Assert.Equal(0L, await CountTablesAsync(businessSession));
            await File.WriteAllTextAsync(Path.Combine(native.EvidenceDirectory, "demo-rollback.json"), JsonSerializer.Serialize(new { passed = true, checkpoint = demoEnd, priorCatalogPreserved = true, failedCatalogEmpty = true, bootstrapIdentities = 0, businessTables = 0, platformBefore }));
            await Execute($"ALTER TABLE `{demoEnd.Table}` DROP CHECK `mt_seed_demo_{run}`");
            await RunAsync(Start("--initialize"));
            await AssertCatalogSeedCountsAsync(platformSession, catalog, present: true); await AssertCatalogSeedCountsAsync(platformSession, demo, present: true);
            Assert.Equal(platformBefore, await SnapshotCatalogSeedsAsync(store, catalog));
            var businessCatalog = models.Compose(demo);
            Assert.Equal(businessCatalog.ConfigTables.Count, await CountTablesAsync(businessSession));
            Assert.Empty((await schema.PreviewAsync(platformPhysical)).Changes); Assert.Empty((await new SchemaService(businessSession).PreviewAsync(businessCatalog)).Changes);
            var beforeRepeat = await SeedSnapshotAsync(store, PlatformSeedRollbackCheckpoint.Rows(catalog).Select(x => x.Table));
            await RunAsync(Start("--initialize"));
            Assert.Equal(beforeRepeat, await SeedSnapshotAsync(store, PlatformSeedRollbackCheckpoint.Rows(catalog).Select(x => x.Table)));
            await using (var host = await FreshHost.StartAsync(Start(), "/api/runtime/health/check"))
            {
                using var response = await host.Client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "seed-rollback-owner", ["password"] = password }));
                var identity = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(200, identity.GetProperty("code").GetInt32());
                host.Client.DefaultRequestHeaders.Add("loginStateId", identity.GetProperty("map").GetProperty("loginStateId").GetString());
                var permission = await host.Client.GetFromJsonAsync<JsonElement>("/api/admin/sys/yz_manager/getCurrentPermissions"); Assert.True(permission.GetProperty("data").GetProperty("isSuper").GetBoolean());
            }
            native.Verify(root);
            await File.WriteAllTextAsync(Path.Combine(native.EvidenceDirectory, "verified.json"), JsonSerializer.Serialize(new { passed = true, environment = "Production", platformRollback = true, demoRollback = true, priorCatalogPreserved = true, platformTables = platformPhysical.ConfigTables.Count, businessTables = businessCatalog.ConfigTables.Count, seedCategories = PlatformSeedRollbackCheckpoint.Rows(catalog).Length, seedRows = PlatformSeedRollbackCheckpoint.Rows(catalog).Sum(x => x.Ids.Length) + PlatformSeedRollbackCheckpoint.Rows(demo).Sum(x => x.Ids.Length), recovery = true, repeatedSeedUnchanged = true, realWeb = true, ddlRollbackClaimed = false, crossDatabaseTransactionClaimed = false, browserExecuted = false, performanceExecuted = false }));
            output.WriteLine("平台及示例最后非空种子行真实CHECK拒绝；本目录回滚、已提交平台保留，移除本轮约束后完整安装及重复种子、真实Web通过。DDL留存，不计跨库整体事务。");
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { await TestDirectoryCleanup.DeleteAsync(credentials, credentialsRoot); }
            catch (Exception error) when (primary is not null) { await File.WriteAllTextAsync(Path.Combine(native.EvidenceDirectory, "credential-cleanup-failure.json"), JsonSerializer.Serialize(new { primaryErrorType = primary.GetType().Name, cleanupErrorType = error.GetType().Name })); }
        }
    }

    /// <summary>从独立提交连接核对每类目录的实际行数，不按逻辑删除过滤或只检查最后一张表。</summary>
    private static async Task AssertCatalogSeedCountsAsync(DatabaseSession session, MetadataCatalog catalog, bool present)
    {
        var project = long.Parse(catalog.YzProjectId, CultureInfo.InvariantCulture);
        foreach (var row in PlatformSeedRollbackCheckpoint.Rows(catalog)) Assert.Equal(present ? row.Ids.Length : 0L, Convert.ToInt64(await ScalarAsync(session, $"SELECT COUNT(*) FROM `{row.Table}` WHERE `yz_project_id`={project.ToString(CultureInfo.InvariantCulture)}"), CultureInfo.InvariantCulture));
    }
    /// <summary>保留已提交目录完整行摘要，恢复阶段不得通过覆盖既有种子达到计数相同。</summary>
    private static async Task<string> SnapshotCatalogSeedsAsync(SqlRowStore store, MetadataCatalog catalog)
    {
        var snapshot = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var group in PlatformSeedRollbackCheckpoint.Rows(catalog))
            snapshot[group.Table] = (await store.ReadIncludingDeletedAsync(group.Table, new Dictionary<string, object?> { ["yz_project_id"] = long.Parse(catalog.YzProjectId, CultureInfo.InvariantCulture) })).OrderBy(row => Convert.ToString(row["id"], CultureInfo.InvariantCulture), StringComparer.Ordinal).Select(row => new SortedDictionary<string, object?>(row, StringComparer.Ordinal)).ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    }
}

/// <summary>按生产播种顺序定位真实发布目录的最后非空集合和最后一条记录；示例目录末段是菜单而非空索引表。</summary>
internal sealed record PlatformSeedRollbackCheckpoint(string Table, long LastId, int PriorRows)
{
    /// <summary>最后一条实际INSERT故障前，所有非空目录前缀均应已经写入同一目录事务。</summary>
    internal static PlatformSeedRollbackCheckpoint For(MetadataCatalog catalog)
    {
        var rows = Rows(catalog); var last = rows.Last(row => row.Ids.Length > 0);
        if (rows.SelectMany(x => x.Ids).Any(id => id <= 0)) throw new InvalidOperationException("种子故障点需要正整数ID。");
        return new(last.Table, last.Ids[^1], rows.Sum(x => x.Ids.Length) - 1);
    }
    /// <summary>同时驱动完整计数和最后非空故障点，保留空目录以证明没有意外新增。</summary>
    internal static (string Table, long[] Ids)[] Rows(MetadataCatalog catalog)
    {
        long[] Ids<T>(IEnumerable<T> source) => source.Select(record => JsonSerializer.SerializeToElement(record, JsonDefaults.Options).GetProperty("id")).Select(value => long.Parse(value.ToString(), CultureInfo.InvariantCulture)).ToArray();
        return [("config_table", Ids(catalog.ConfigTables)), ("config_column", Ids(catalog.ConfigColumns)), ("config_page", Ids(catalog.ConfigPages)), ("config_page_column", Ids(catalog.ConfigPageColumns)), ("yz_admin_type", Ids(catalog.YzAdminTypes)), ("config_menu", Ids(catalog.ConfigMenus)), ("config_custom_page", Ids(catalog.ConfigCustomPages)), ("config_button", Ids(catalog.ConfigButtons)), ("config_child", Ids(catalog.ConfigChildren)), ("config_index", Ids(catalog.ConfigIndexes)), ("config_index_column", Ids(catalog.ConfigIndexColumns))];
    }
}
