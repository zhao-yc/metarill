using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

/// <summary>只有专属空库验收脚本提供的随机库配置才执行，普通 CI 不假称首次安装成功。</summary>
public sealed class FreshInstallMySqlFactAttribute : FactAttribute
{
    public FreshInstallMySqlFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")))
            Skip = "未通过专属脚本创建全新 Server-200 验收库，首次安装验收未执行。";
    }
}

/// <summary>两个场景使用各自独占的随机新库，不把已有基础表复用计作首次安装。</summary>
public sealed partial class FreshInstallMySqlTests(ITestOutputHelper output)
{
    /// <summary>普通启动和预览都保持0表，包内显式 apply 从零安装文件基础设施及业务表。</summary>
    [FreshInstallMySqlFact]
    public async Task Standalone_cli_installs_files_and_business_from_an_empty_database()
    {
        var options = FreshInstallConfiguration.Read("PackagedDatabase", "package");
        using (var initial = new DatabaseSession(options)) Assert.Equal(0L, await CountTablesAsync(initial));
        await using var fixture = await PackagedFilesFixture.CreateAsync(options);
        Assert.Empty(fixture.ExistingFileTables);
        await fixture.BuildAsync();
        var physical = new Metarill.Runtime.ProjectPhysicalModels(fixture.FileCatalog, fixture.SecurityCatalog).Compose(fixture.Catalog);
        Assert.Contains(physical.ConfigTables, table => table.TableName == "yz_manager_log");
        await fixture.StartAsync(); Assert.Equal(0L, await CountTablesAsync(fixture.Session)); await fixture.StopAsync();
        await fixture.SchemaAsync("--schema-preview"); Assert.Equal(0L, await CountTablesAsync(fixture.Session));
        var filesBefore = await new SchemaService(fixture.Session).PreviewAsync(fixture.FileCatalog);
        Assert.Equal(fixture.FileCatalog.ConfigTables.Count, filesBefore.Changes.Count(change => change.Kind == "createTable"));
        var securityBefore=await new SchemaService(fixture.Session).PreviewAsync(fixture.SecurityCatalog);
        Assert.Equal(fixture.SecurityCatalog.ConfigTables.Count, securityBefore.Changes.Count(change=>change.Kind=="createTable"));
        await fixture.SchemaAsync("--schema-apply");
        await AssertInstalledTablesAsync(fixture.Session, physical);
        Assert.Empty((await new SchemaService(fixture.Session).PreviewAsync(physical)).Changes);
        await fixture.SchemaAsync("--schema-apply"); await AssertInstalledTablesAsync(fixture.Session, physical);
        await fixture.InitializeWebSecurityAsync();
        var credentials=await ScalarAsync(fixture.Session,"SELECT password FROM yz_manager WHERE name='files-probe'");
        await fixture.InitializeWebSecurityAsync();Assert.Equal(credentials,await ScalarAsync(fixture.Session,"SELECT password FROM yz_manager WHERE name='files-probe'"));
        await fixture.StartAsync(); await fixture.LoginAsync();
        var permissions=await fixture.SuccessAsync(HttpMethod.Get,"/api/admin/sys/yz_manager/getCurrentPermissions");Assert.True(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());
        var menu=await fixture.SuccessAsync(HttpMethod.Get,"/api/admin/sys/yz_manager/getMenu");Assert.NotEmpty(menu.GetProperty("menu").EnumerateArray());
        var bytes = Encoding.UTF8.GetBytes("全新随机库首次安装后的文件与业务验收");
        var file = await fixture.UploadAsync("fresh-install.txt", bytes);
        var added = await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/add", new { name = "首次安装业务", attachments = new[] { file.ToString() } });
        var record = long.Parse(added.GetProperty("data").GetProperty("id").ToString()); fixture.RecordIds.Add(record);
        await fixture.AssertReferenceAsync(record, file); await fixture.AssertContentAsync(file, bytes, publicly: false);
        await AssertFileCleanupRetryAsync(fixture, bytes);
        await AssertWebAuthenticationAsync(fixture, record);
        output.WriteLine($"独立应用：0表启动/预览均无DDL，显式CLI安装{physical.ConfigTables.Count}张模型声明的基础表，二次无差异；真实Web普通账号授权/撤权、改密撤会话、重启持久身份及文件引用/下载与对象删除失败重试成功，非RR或并发验收。");
    }

    /// <summary>用本轮文件实际触发存储失败，验证完整包原删除路由的业务响应、权限和恢复。</summary>
    private static async Task AssertFileCleanupRetryAsync(PackagedFilesFixture fixture, byte[] bytes)
    {
        var file = await fixture.UploadAsync("fresh-delete-retry.txt", bytes);
        var storageKey = Assert.IsType<string>(await fixture.ScalarAsync(
            "SELECT `storage_key` FROM `yz_file_library` WHERE `id`=@id", file));
        Assert.Matches("^[a-f0-9]{32}\\.blob$", storageKey);
        var path = Path.Combine(fixture.StorageRoot, storageKey);
        var retained = path + ".retained";
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var route = fixture.FileRoute + "/" + file;

        // 用同名目录触发本地存储删除失败，恢复后原 ID 继续走已部署的 HTTP 删除入口。
        File.Move(path, retained);
        Directory.CreateDirectory(path);
        using (var failed = await fixture.Client.DeleteAsync(route))
        {
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
            var result = await failed.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(500, result.GetProperty("code").GetInt32());
            Assert.Equal("文件记录已失效，物理对象清理失败。", result.GetProperty("msg").GetString());
        }
        Directory.Delete(path);
        File.Move(retained, path);

        const string tombstoneSql = "SELECT `delete_no` FROM `yz_file_library` WHERE `id`=@id AND `status`='deleted'";
        var tombstone = Assert.IsType<DateTime>(await fixture.ScalarAsync(tombstoneSql, file));
        using (var protectedContent = await fixture.Client.GetAsync(fixture.ContentPath(file, publicly: false)))
            Assert.Equal(HttpStatusCode.Unauthorized, protectedContent.StatusCode);
        using (var anonymousDelete = await fixture.Anonymous.DeleteAsync(route))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousDelete.StatusCode);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));

        var retry = await fixture.SuccessAsync(HttpMethod.Delete, route);
        Assert.Equal("删除成功", retry.GetProperty("data").GetString());
        Assert.False(File.Exists(path));
        Assert.Equal(tombstone, await fixture.ScalarAsync(tombstoneSql, file));
    }

    /// <summary>平台首次安装必须创建完整元数据表、内置元数据和管理员，重复初始化不重置密码。</summary>
    [FreshInstallMySqlFact]
    public async Task Platform_initialize_bootstraps_an_empty_database_and_is_idempotent()
    {
        var root = SourceRoot();
        var native = PlatformBootstrapNativePreparation.ReadIfRequested(root);
        var platform = FreshInstallConfiguration.Read("Database", "platform");
        var sameDatabase = native?.SharePlatformDatabase == true;
        var business = sameDatabase ? platform : FreshInstallConfiguration.Read("BusinessDatabase", "business");
        var loginRedis = FreshInstallConfiguration.ReadLoginRedis();
        using var platformSession = new DatabaseSession(platform); using var businessSession = new DatabaseSession(business);
        Assert.Equal(0L, await CountTablesAsync(platformSession)); Assert.Equal(0L, await CountTablesAsync(businessSession));
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "metarill-fresh-bootstrap");
        var directory = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var password = Guid.NewGuid().ToString("N"); var alternatePassword = Guid.NewGuid().ToString("N");
        var settings = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new { Metarill = new { Database = platform, BusinessDatabase = business, WorkerId = 854, Bootstrap = new { Username = "fresh-admin", Password = password } } }));
        var assembly = native?.AssemblyPath ?? Path.Combine(root, "backend/src/Metarill.Api/bin/Debug/net10.0/Metarill.Api.dll");
        Assert.True(File.Exists(assembly), "请使用 fresh-mysql.ps1 先构建当前平台宿主。");
        ProcessStartInfo StartInfo(string? operation = null)
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = root, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(assembly); if (operation is not null) start.ArgumentList.Add(operation);
            start.Environment["METARILL_SETTINGS"] = settings; start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            // 只供既有脱敏日志器收集秘密；连接及身份仍由同一正式配置建立。
            start.Environment["Metarill__Bootstrap__Password"] = password;
            start.Environment["Metarill__Database__ConnectionString"] = platform.ConnectionString;
            start.Environment["Metarill__BusinessDatabase__ConnectionString"] = business.ConnectionString;
            // Redis 只供 Web 登录，初始化命令及其日志不接触该凭据。
            if (operation is null) start.Environment["Metarill__Authentication__LoginFailures__ConnectionString"] = loginRedis;
            if (native is not null) start.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = native.EvidenceDirectory;
            return start;
        }
        try
        {
            await using (var ordinary = await FreshHost.StartAsync(StartInfo(), "/api/runtime/health/check"))
            {
                Assert.Equal(0L, await CountTablesAsync(platformSession)); Assert.Equal(0L, await CountTablesAsync(businessSession));
            }
            var catalog = await CatalogAsync(Path.Combine(root, "backend/metadata/platform.json"));
            var demo = await CatalogAsync(Path.Combine(root, "backend/metadata/demo.json"));
            // 与BootstrapService共用完整业务物理图，不能漏掉安全及Agent基础表。
            var files = await CatalogAsync(Path.Combine(root, "backend/metadata/files.json"));
            var security = await CatalogAsync(Path.Combine(root, "backend/metadata/security.json"));
            var registry = new Metarill.Runtime.Metadata.MetadataRegistry(Path.Combine(root, "backend/metadata/platform.json"), Path.Combine(root, "backend/metadata/demo.json"));
            var models = new Metarill.Runtime.ProjectPhysicalModels(files, security, registry);
            var platformPhysical = models.Compose(catalog);
            var businessCatalog = models.Compose(demo);
            var schema = new SchemaService(platformSession); var initial = await schema.PreviewAsync(platformPhysical);
            Assert.Equal(platformPhysical.ConfigTables.Count, initial.Changes.Count(change => change.Kind == "createTable"));
            Assert.Equal(0L, await CountTablesAsync(platformSession));
            await RunAsync(StartInfo("--initialize"));
            var platformTables = platformPhysical.ConfigTables.Select(table => table.TableName);
            var businessTables = businessCatalog.ConfigTables.Select(table => table.TableName);
            // 同库需安装两份目录的物理表并集，共享基础表只计一次。
            var installedPlatform = (sameDatabase ? platformTables.Union(businessTables, StringComparer.Ordinal) : platformTables).Order(StringComparer.Ordinal).ToArray();
            var installedBusiness = sameDatabase ? installedPlatform : businessTables.Order(StringComparer.Ordinal).ToArray();
            await AssertInstalledTablesAsync(platformSession, installedPlatform);
            Assert.Equal(0L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM yz_manager_log"), CultureInfo.InvariantCulture));
            await AssertInstalledTablesAsync(businessSession, installedBusiness);
            Assert.Empty((await schema.PreviewAsync(platformPhysical)).Changes);
            Assert.Empty((await new SchemaService(businessSession).PreviewAsync(businessCatalog)).Changes);
            // 覆盖SeedAsync写入的全部11类目录，而非仅前四张配置表。
            var expected = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var source in new[] { catalog, demo })
                foreach (var (table, count) in new[] { ("config_table", source.ConfigTables.Count), ("config_column", source.ConfigColumns.Count), ("config_page", source.ConfigPages.Count), ("config_page_column", source.ConfigPageColumns.Count), ("yz_admin_type", source.YzAdminTypes.Count), ("config_menu", source.ConfigMenus.Count), ("config_custom_page", source.ConfigCustomPages.Count), ("config_button", source.ConfigButtons.Count), ("config_child", source.ConfigChildren.Count), ("config_index", source.ConfigIndexes.Count), ("config_index_column", source.ConfigIndexColumns.Count) })
                    expected[table] = expected.GetValueOrDefault(table) + count;
            foreach (var (table, count) in expected) Assert.Equal(count, Convert.ToInt32(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `" + table + "` WHERE `delete_no` IS NULL")));
            Assert.Equal(2L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `yz_project` WHERE `delete_no` IS NULL")));
            Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `yz_manager` WHERE `delete_no` IS NULL")));
            // 在本轮独占库内制造用户编辑和墓碑，再逐行证明重复播种不会覆盖或复活。
            var seedStore = new SqlRowStore(platformSession, catalog);
            var projects = await seedStore.ReadAsync("yz_project");
            AssertProjectDefaults(projects, catalog, platform.ExpectedDatabase, "metarill", "metarill.platform");
            AssertProjectDefaults(projects, demo, business.ExpectedDatabase, "demo", "metarill.demo");
            Assert.Equal(1, await seedStore.UpdateAsync("yz_project", long.Parse(demo.YzProjectId, CultureInfo.InvariantCulture),
                new Dictionary<string, object?> { ["primary_color"] = "#112233" }));
            var editedId = long.Parse(catalog.ConfigTables[0].Id, CultureInfo.InvariantCulture);
            // 墓碑验证使用另一按钮，保留本轮后续要实际调用的初始化按钮。
            var deletedId = long.Parse(catalog.ConfigButtons.Select(button => button.GetProperty("id").ToString())
                .First(id => id != AdminAccountInitializationBusiness.ButtonId), CultureInfo.InvariantCulture);
            Assert.Equal(1, await seedStore.UpdateAsync("config_table", editedId, new Dictionary<string, object?> { ["name"] = "本轮空库验收保留的用户编辑" }));
            Assert.Equal(1, await seedStore.UpdateAsync("config_button", deletedId, new Dictionary<string, object?> { ["delete_no"] = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified) }));
            var snapshotTables = expected.Keys.Append("yz_project").ToArray();
            var seedBefore = await SeedSnapshotAsync(seedStore, snapshotTables);
            var hash = await ScalarAsync(platformSession, "SELECT `password` FROM `yz_manager` WHERE `id`=1");
            var repeated = StartInfo("--initialize"); repeated.Environment["Metarill__Bootstrap__Password"] = alternatePassword;
            await RunAsync(repeated);
            Assert.Equal(hash, await ScalarAsync(platformSession, "SELECT `password` FROM `yz_manager` WHERE `id`=1"));
            foreach (var (table, count) in expected) Assert.Equal(count - (table == "config_button" ? 1 : 0), Convert.ToInt32(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM `" + table + "` WHERE `delete_no` IS NULL")));
            Assert.Equal(seedBefore, await SeedSnapshotAsync(seedStore, snapshotTables));
            Assert.Empty((await schema.PreviewAsync(platformPhysical)).Changes);
            Assert.Empty((await new SchemaService(businessSession).PreviewAsync(businessCatalog)).Changes);
            await AssertInstalledTablesAsync(platformSession, installedPlatform);
            await AssertInstalledTablesAsync(businessSession, installedBusiness);
            string loginToken;
            await using (var host = await FreshHost.StartAsync(StartInfo(), "/api/runtime/health/check"))
            {
                using var login = await host.Client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["username"] = "fresh-admin", ["password"] = password }));
                var identity = await login.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(identity.GetProperty("code").GetInt32() == 200, (identity.TryGetProperty("msg", out var message) && message.GetString() == "登录安全校验暂不可用，请稍后再试")
                    ? "正式宿主登录安全校验不可用，请核对本轮隔离 Redis。"
                    : "正式宿主未通过正确密码登录，响应正文不回显。");
                host.Client.DefaultRequestHeaders.Add("loginStateId", identity.GetProperty("map").GetProperty("loginStateId").GetString());
                var permissions = await host.Client.GetFromJsonAsync<JsonElement>("/api/admin/sys/yz_manager/getCurrentPermissions");
                Assert.Equal(200, permissions.GetProperty("code").GetInt32()); Assert.True(permissions.GetProperty("data").GetProperty("isSuper").GetBoolean());
                var menu = await host.Client.GetFromJsonAsync<JsonElement>("/api/admin/sys/yz_manager/getMenu"); Assert.Equal(200, menu.GetProperty("code").GetInt32()); Assert.NotEmpty(menu.GetProperty("menu").EnumerateArray());
                loginToken = identity.GetProperty("map").GetProperty("loginStateId").GetString()!;
                using (var wrongPassword = await host.Client.PostAsync("/api/admin/sys/yz_manager/login", new FormUrlEncodedContent(new Dictionary<string,string> { ["username"] = "fresh-admin", ["password"] = alternatePassword })))
                    Assert.NotEqual(200, (await wrongPassword.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetInt32());
                Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(platformSession, "SELECT COUNT(*) FROM yz_manager_log WHERE yz_manager_id=1 AND action_type=8 AND execute_result=2"), CultureInfo.InvariantCulture));
            }
            await using (var restarted = await FreshHost.StartAsync(StartInfo(), "/api/runtime/health/check"))
            {
                restarted.Client.DefaultRequestHeaders.Add("loginStateId", loginToken);
                var persisted = await restarted.Client.GetFromJsonAsync<JsonElement>("/api/admin/sys/yz_manager/getCurrentPermissions");
                Assert.Equal(200, persisted.GetProperty("code").GetInt32()); Assert.True(persisted.GetProperty("data").GetProperty("isSuper").GetBoolean());
                Assert.Equal(seedBefore, await SeedSnapshotAsync(seedStore, snapshotTables));
                await VerifyPlatformOptionAliasesAsync(restarted.Client, seedStore, catalog, demo);
                if (native?.VerifyPublishedBusinessPermissions == true)
                {
                    var businessStore = new SqlRowStore(businessSession, businessCatalog);
                    await VerifyAdminInitializationAsync(restarted.Client, seedStore, businessStore, demo.YzProjectId, demo.YzAdminTypes.Single().Id, catalog.YzAdminTypes.Single().Id, sameDatabase);
                    await VerifyPublishedBusinessPermissionsAsync(restarted.Client.BaseAddress!, seedStore, businessStore, catalog.YzProjectId, demo.YzProjectId);
                }
            }
            if (native is not null)
            {
                native.Verify(root);
                await File.WriteAllTextAsync(Path.Combine(native.EvidenceDirectory, "verified.json"), JsonSerializer.Serialize(new { passed = true, environment = "Production", platformTables = platformPhysical.ConfigTables.Count, businessTables = businessCatalog.ConfigTables.Count, seedCategories = expected.Count, seedRows = expected.Values.Sum(), distinctDatabases = platform.ExpectedDatabase != business.ExpectedDatabase, installedPlatformTables = installedPlatform.Length, installedBusinessTables = installedBusiness.Length, ordinaryStartupNoDdl = true, repeatedSeedUnchanged = true, userEditPreserved = true, tombstonePreserved = true, passwordPreserved = true, loginFailureAuditPersisted = true, realWeb = true, restartSession = true, publishedBusinessPermissions = native.VerifyPublishedBusinessPermissions, publishedBusinessTransactions = native.VerifyPublishedBusinessPermissions, publishedAdminInitialization = native.VerifyPublishedBusinessPermissions, publishedAdminInitializationRollback = native.VerifyPublishedBusinessPermissions, sharedTargetWebLogin = native.VerifyPublishedBusinessPermissions && sameDatabase, browserExecuted = false, performanceExecuted = false }));
            }
            output.WriteLine($"平台：Production普通启动保持0表，显式初始化后平台连接有{installedPlatform.Length}表、业务连接有{installedBusiness.Length}表；{expected.Count}类{expected.Values.Sum()}条种子，重复保留编辑、墓碑及密码，真实登录/权限/菜单及重启会话通过。");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, temporaryRoot); }
    }

    /// <summary>物理回读核对原详情必填项，失败时不把连接字段或凭据写入日志。</summary>
    private static void AssertProjectDefaults(List<Dictionary<string,object?>> projects,MetadataCatalog catalog,string database,string shortName,string packageName)
    {
        var row=Assert.Single(projects,project=>SqlRowStore.Text(project,"id")==catalog.YzProjectId);
        var reference=new System.Data.Common.DbConnectionStringBuilder();
        try{reference.ConnectionString=SqlRowStore.Text(row,"mysql_conn_str");}
        catch(ArgumentException){throw new InvalidOperationException("原项目连接栏不是合法数据库引用。");}
        Assert.True(reference.Count==1&&reference.TryGetValue("Database",out var catalogName)&&Equals(catalogName,database),
            "原项目连接栏应只包含本项目库名，不能复制服务端凭据。");
        Assert.Equal(database,SqlRowStore.Text(row,"db_name"));
        Assert.Equal(shortName,SqlRowStore.Text(row,"short_name"));
        Assert.Equal("/api",SqlRowStore.Text(row,"context_root"));
        Assert.Equal(packageName,SqlRowStore.Text(row,"eclipse_base_name"));
        Assert.Equal(string.IsNullOrWhiteSpace(catalog.PrimaryColor)?"#0ab3ad":catalog.PrimaryColor,SqlRowStore.Text(row,"primary_color"));
        Assert.True(string.IsNullOrEmpty(SqlRowStore.Text(row,"db_username"))&&string.IsNullOrEmpty(SqlRowStore.Text(row,"db_password")),
            "空库播种不能把部署凭据保存到项目详情。");
    }

    /// <summary>按固定顺序比较项目及11类种子的完整行，包含墓碑，日志只保留摘要。</summary>
    private static async Task<string> SeedSnapshotAsync(SqlRowStore store, IEnumerable<string> tables)
    {
        var snapshot = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var table in tables)
            snapshot[table] = (await store.ReadIncludingDeletedAsync(table)).OrderBy(row => Convert.ToString(row["id"], CultureInfo.InvariantCulture), StringComparer.Ordinal).Select(row => new SortedDictionary<string, object?>(row, StringComparer.Ordinal)).ToArray();
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
    }

    /// <summary>仅在专属随机package库、文件CRUD完成后验证正式Web顺序合同；安全表种子不是已下发安全CRUD能力。</summary>
    private static async Task AssertWebAuthenticationAsync(PackagedFilesFixture fixture, long record)
    {
        FreshInstallConfiguration.Validate(fixture.Session.Options, "package");
        var store = new SqlRowStore(fixture.Session, fixture.SecurityCatalog);
        var project = long.Parse(fixture.Catalog.YzProjectId); var page = fixture.Catalog.ConfigPages.Single();
        var admin = long.Parse(page.YzAdminTypeId); var bootstrap = await ReadOneAsync(store, "yz_manager", "name", "files-probe");
        Assert.True(SqlRowStore.Id(bootstrap, "yz_admin_type_id") == admin, "初始账号不属于本包后台。");
        var nonce = Guid.NewGuid().ToString("N")[..16]; var username = "fresh_web_" + nonce;
        var password = Guid.NewGuid().ToString("N"); var nextPassword = Guid.NewGuid().ToString("N"); var bootstrapPassword = Guid.NewGuid().ToString("N");
        var ids = new SnowflakeIdGenerator(887); var roleId = ids.NextId(); var accountId = ids.NextId(); var grantId = ids.NextId(); var menuGrantId = ids.NextId();
        var menu = fixture.Catalog.ConfigMenus.Single(item => item.Kind == "GENERATED_PAGE" && item.ConfigPageId == page.Id);
        // 固定模型的明确测试数据只写本轮新空库；预查含墓碑不存在，同事务按项目先锁，再插入有审计归属的唯一角色/账号/授权。
        await fixture.Session.InTransactionAsync(async () =>
        {
            Assert.True((await store.ReadForUpdateAsync("yz_project", new Dictionary<string, object?> { ["id"] = project })).Count == 1, "本包项目必须有效且唯一。");
            foreach (var (table, id) in new[] { ("yz_role", roleId), ("yz_manager", accountId), ("yz_role_permission", grantId), ("yz_role_permission", menuGrantId) })
                Assert.True((await store.ReadIncludingDeletedAsync(table, new Dictionary<string, object?> { ["id"] = id })).Count == 0, "新样本主键含墓碑已存在。");
            foreach (var table in new[] { "yz_role", "yz_manager" })
                Assert.True((await store.ReadIncludingDeletedAsync(table, new Dictionary<string, object?> { ["name"] = username })).Count == 0, "新样本名称含墓碑已存在。");
            Dictionary<string, object?> Values(long id) => new() { ["id"] = id, ["yz_project_id"] = project, ["insert_manager_id"] = SqlRowStore.Id(bootstrap), ["insert_time"] = DateTime.UtcNow };
            var role = Values(roleId); role["name"] = username; role["yz_admin_type_id"] = admin; role["is_super"] = 0; role["is_built_in"] = 0; await store.InsertAsync("yz_role", role);
            var account = Values(accountId); account["name"] = username; account["password"] = PasswordHasher.Hash(password); account["is_disabled"] = 0; account["yz_admin_type_id"] = admin; account["yz_role_ids"] = JsonSerializer.Serialize(new[] { roleId.ToString() }); await store.InsertAsync("yz_manager", account);
            var grant = Values(grantId); grant["yz_role_id"] = roleId; grant["config_page_id"] = long.Parse(page.Id); grant["permission_values"] = "[\"is_query\"]"; await store.InsertAsync("yz_role_permission", grant);
            var navigation = Values(menuGrantId); navigation["yz_role_id"] = roleId; navigation["config_menu_id"] = long.Parse(menu.Id); navigation["permission_values"] = "[]"; await store.InsertAsync("yz_role_permission", navigation);
            return true;
        });
        using var first = fixture.CreateClient(); using var second = fixture.CreateClient();
        var firstToken = await FreshWebHttp.LoginAsync(first, username, password, form: true);
        var secondToken = await FreshWebHttp.LoginAsync(second, username, password);
        Assert.True(firstToken != secondToken, "两次真实登录必须创建独立会话。");
        await AssertQueryOnlyAsync(first, page.Id);
        var navigationResult = await FreshWebHttp.SendAsync(first, HttpMethod.Get, "/api/admin/sys/yz_manager/getMenu");
        Assert.True(HasPage(navigationResult.GetProperty("menu"), page.Id, fixture.Catalog.YzProjectId, page.YzAdminTypeId), "普通角色只能回读本包已授权菜单。");
        var detailRoute = fixture.RecordRoute + "/detail/" + record;
        Assert.Equal(record.ToString(), (await FreshWebHttp.SendAsync(first, HttpMethod.Get, detailRoute)).GetProperty("data").GetProperty("id").GetString());
        var before = await fixture.RowSnapshotAsync(record); var rows = await fixture.ScalarAsync("SELECT COUNT(*) FROM `" + fixture.Table + "`", 0);
        await FreshWebHttp.SendAsync(first, HttpMethod.Post, fixture.RecordRoute + "/add", new { name = "无权新增不得落库" }, HttpStatusCode.Forbidden);
        // 同一POST/URL先由真实bootstrap成功签发票据，证明随后403来自页导出授权而非不存在的路由。
        var export = await fixture.SuccessAsync(HttpMethod.Post, fixture.RecordRoute + "/initExport", new { selectedIds = new[] { record.ToString() } });
        Assert.True(export.GetProperty("data").ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(export.GetProperty("data").GetString()), "真实导出端点必须签发非空票据。");
        await FreshWebHttp.SendAsync(first, HttpMethod.Post, fixture.RecordRoute + "/initExport", new { selectedIds = new[] { record.ToString() } }, HttpStatusCode.Forbidden);
        Assert.Equal(rows, await fixture.ScalarAsync("SELECT COUNT(*) FROM `" + fixture.Table + "`", 0)); Assert.Equal(before, await fixture.RowSnapshotAsync(record));
        // 客户端选择参数不参与身份；登录额外上下文拒绝，认证请求的伪项目/后台/超级提示不能扩权。
        await FreshWebHttp.SendAsync(first, HttpMethod.Post, "/api/admin/sys/yz_manager/login", new { username, password, projectId = "1", adminTypeId = "1", isSuper = true }, HttpStatusCode.BadRequest);
        await AssertQueryOnlyAsync(first, page.Id, "?projectId=1&adminTypeId=1&isSuper=true");
        var secondSession = await ReadOneAsync(store, "sys_session", "token_hash", PasswordHasher.Digest(secondToken));
        await store.UpdateAsync("sys_session", SqlRowStore.Id(secondSession), new Dictionary<string, object?> { ["kind"] = "agent", ["grant_id"] = roleId });
        await FreshWebHttp.SendAsync(second, HttpMethod.Get, detailRoute, expected: HttpStatusCode.Unauthorized);
        await store.UpdateAsync("sys_session", SqlRowStore.Id(secondSession), new Dictionary<string, object?> { ["kind"] = "web", ["grant_id"] = null });
        await FreshWebHttp.SendAsync(second, HttpMethod.Get, detailRoute); // 改密前两个旧token都必须真正有效。
        await store.UpdateAsync("yz_manager", accountId, new Dictionary<string, object?> { ["is_disabled"] = 1 });
        await FreshWebHttp.SendAsync(first, HttpMethod.Get, detailRoute, expected: HttpStatusCode.Unauthorized);
        await store.UpdateAsync("yz_manager", accountId, new Dictionary<string, object?> { ["is_disabled"] = 0 });
        await store.UpdateAsync("yz_role_permission", grantId, new Dictionary<string, object?> { ["permission_values"] = "[]" });
        await FreshWebHttp.SendAsync(first, HttpMethod.Get, detailRoute, expected: HttpStatusCode.Forbidden);
        await store.UpdateAsync("yz_role_permission", grantId, new Dictionary<string, object?> { ["permission_values"] = "[\"is_query\"]" });
        await FreshWebHttp.SendAsync(first, HttpMethod.Get, detailRoute);
        var oldHash = SqlRowStore.Text(await ReadOneAsync(store, "yz_manager", "id", accountId), "password");
        await FreshWebHttp.SendAsync(first, HttpMethod.Post, "/api/admin/sys/yz_manager/edit_password", new { oldPwd = "无效旧密码", pwd = nextPassword }, HttpStatusCode.Forbidden);
        Assert.True(oldHash == SqlRowStore.Text(await ReadOneAsync(store, "yz_manager", "id", accountId), "password"), "错误旧密码不能改变摘要。");
        Assert.True((await store.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = accountId })).Count == 2, "改密前必须有两个真实持久会话。");
        await FreshWebHttp.SendAsync(first, HttpMethod.Post, "/api/admin/sys/yz_manager/edit_password", new { oldPwd = password, pwd = nextPassword });
        foreach (var client in new[] { first, second }) await FreshWebHttp.SendAsync(client, HttpMethod.Get, detailRoute, expected: HttpStatusCode.Unauthorized);
        Assert.True((await store.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = accountId })).Count == 0, "改密必须撤销该账号全部旧会话。");
        using var relogin = fixture.CreateClient();
        await FreshWebHttp.SendAsync(relogin, HttpMethod.Post, "/api/admin/sys/yz_manager/login", new { username, password }, HttpStatusCode.Unauthorized);
        var persistedToken = await FreshWebHttp.LoginAsync(relogin, username, nextPassword);
        var hash = SqlRowStore.Text(await ReadOneAsync(store, "yz_manager", "id", accountId), "password");
        Assert.True(PasswordHasher.Verify(nextPassword, hash), "真实改密必须保存新PBKDF2摘要。");
        // 对bootstrap本人改密后再运行原初始化配置，证明不会恢复启动密码；该段在原CRUD之后，不影响原文件检查。
        await fixture.ChangeBootstrapPasswordAsync(bootstrapPassword);
        var bootstrapHash = SqlRowStore.Text(await ReadOneAsync(store, "yz_manager", "name", "files-probe"), "password");
        await fixture.StopAsync(); await fixture.InitializeWebSecurityAsync();
        Assert.True(bootstrapHash == SqlRowStore.Text(await ReadOneAsync(store, "yz_manager", "name", "files-probe"), "password"), "重复初始化不能重置bootstrap新摘要。");
        await fixture.StartAsync();
        using var restarted = fixture.CreateClient(persistedToken);
        await FreshWebHttp.SendAsync(restarted, HttpMethod.Get, detailRoute); // 原令牌跨真实进程重启仍从数据库认证。
        await AssertQueryOnlyAsync(restarted, page.Id);
        await FreshWebHttp.SendAsync(restarted, HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login", new { });
        await FreshWebHttp.SendAsync(restarted, HttpMethod.Get, detailRoute, expected: HttpStatusCode.Unauthorized);
        Assert.True((await store.ReadAsync("sys_session", new Dictionary<string, object?> { ["account_id"] = accountId })).Count == 0, "注销后不留下该测试账号活动会话。");
        await fixture.LoginWithBootstrapPasswordAsync(bootstrapPassword); // 默认客户端恢复有效身份，供原fixture收口使用。
    }

    /// <summary>所有读取只定位新库内明确名称/主键，失败不打印认证行或摘要。</summary>
    private static async Task<Dictionary<string, object?>> ReadOneAsync(SqlRowStore store, string table, string column, object value)
    {
        var rows = await store.ReadAsync(table, new Dictionary<string, object?> { [column] = value });
        Assert.True(rows.Count == 1, "专属样本必须存在且唯一。"); return rows[0];
    }
    /// <summary>普通账号动作只允许本包本页query，不能借is_all或后台super扩大。</summary>
    private static async Task AssertQueryOnlyAsync(HttpClient client, string page, string suffix = "")
    {
        var response = await FreshWebHttp.SendAsync(client, HttpMethod.Get, "/api/admin/sys/yz_manager/getCurrentPermissions" + suffix);
        var permissions = response.GetProperty("data"); Assert.False(permissions.GetProperty("isSuper").GetBoolean());
        var pages = permissions.GetProperty("pagePermissions"); Assert.Equal(new[] { page }, pages.EnumerateObject().Select(item => item.Name));
        Assert.Equal(new[] { "is_query" }, pages.GetProperty(page).EnumerateArray().Select(item => item.GetString()));
    }
    /// <summary>递归核对原菜单协议，只接受真实包项目/后台的目标页。</summary>
    private static bool HasPage(JsonElement nodes, string page, string project, string admin) => nodes.EnumerateArray().Any(node =>
        node.GetProperty("projectId").GetString() == project && node.GetProperty("adminTypeId").GetString() == admin &&
        (node.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object && target.GetProperty("pageId").GetString() == page || HasPage(node.GetProperty("children"), page, project, admin)));

    /// <summary>逐名核对独占库对象，拒绝同名视图、缺表和额外表。</summary>
    private static Task AssertInstalledTablesAsync(DatabaseSession session, MetadataCatalog physical) =>
        AssertInstalledTablesAsync(session, physical.ConfigTables.Select(table => table.TableName));

    /// <summary>平台与业务共库时也逐名核对同一个物理表集合。</summary>
    private static async Task AssertInstalledTablesAsync(DatabaseSession session, IEnumerable<string> expectedNames)
    {
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME, TABLE_TYPE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            Assert.Equal("BASE TABLE", reader.GetString(1));
            names.Add(reader.GetString(0));
        }
        Assert.Equal(expectedNames.Order(StringComparer.Ordinal),
            names.Order(StringComparer.Ordinal));
    }

    /// <summary>只读完整数据库表数，证明不存在隐藏复用的基础表。</summary>
    private static async Task<long> CountTablesAsync(DatabaseSession session) => Convert.ToInt64(await ScalarAsync(session, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"));
    /// <summary>只接受测试内固定SQL；查询前由DatabaseSession核验目标库。</summary>
    private static async Task<object?> ScalarAsync(DatabaseSession session, string sql)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
    /// <summary>验收结构读取当前提交元数据，不手写任何产品表DDL。</summary>
    private static async Task<MetadataCatalog> CatalogAsync(string path) => JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>只消费子进程日志，避免错误信息携带环境配置。</summary>
    private static async Task RunAsync(ProcessStartInfo start)
    {
        var result = await RunInitializationAsync(start);
        Assert.True(result.ExitCode == 0, "首次初始化命令失败，请核对本轮脱敏私有诊断；配置不回显。");
    }
    /// <summary>成功和预期故障共用真实进程、超时及脱敏日志，不用退出失败代替明确数据库故障证明。</summary>
    private static async Task<(int ExitCode, string Output, string Error)> RunInitializationAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var outText=await stdout;var errText=await stderr;
        // 初始化失败必须留下脱敏首因；仅允许本仓库现有私有验收目录，不能丢弃子进程诊断。
        if(start.Environment.TryGetValue("METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY",out var evidence)&&!string.IsNullOrWhiteSpace(evidence))
        {
            var boundary=Path.GetFullPath(Path.Combine(SourceRoot(),".local"))+Path.DirectorySeparatorChar;var resolved=Path.GetFullPath(evidence);if(!resolved.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(resolved))throw new InvalidOperationException("初始化诊断只允许现有私有验收目录。");
            var secrets=new HashSet<string>(StringComparer.Ordinal);
            void Collect(string key,string? value)
            {
                if(string.IsNullOrWhiteSpace(value)||!Regex.IsMatch(key,"password|pwd|token|secret|connectionstring|api.?key",RegexOptions.IgnoreCase))return;secrets.Add(value);
                if(key.Contains("ConnectionString",StringComparison.OrdinalIgnoreCase)){var connection=new DbConnectionStringBuilder{ConnectionString=value};foreach(string name in connection.Keys)if(Regex.IsMatch(name,"password|pwd|token|secret",RegexOptions.IgnoreCase))secrets.Add(Convert.ToString(connection[name],CultureInfo.InvariantCulture)!);}
            }
            void Scan(JsonElement value){if(value.ValueKind==JsonValueKind.Object)foreach(var item in value.EnumerateObject()){if(item.Value.ValueKind==JsonValueKind.String)Collect(item.Name,item.Value.GetString());else Scan(item.Value);}else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Scan(item);}
            foreach(var pair in start.Environment)Collect(pair.Key,pair.Value);
            if(start.Environment.TryGetValue("METARILL_SETTINGS",out var settings)&&File.Exists(settings)){using var json=JsonDocument.Parse(await File.ReadAllTextAsync(settings));Scan(json.RootElement);}
            foreach(var secret in secrets.Where(value=>!string.IsNullOrEmpty(value)).OrderByDescending(value=>value.Length)){outText=outText.Replace(secret,"[已脱敏]",StringComparison.Ordinal);errText=errText.Replace(secret,"[已脱敏]",StringComparison.Ordinal);}
            var id=Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(Path.Combine(resolved,"initialize-"+id+".stdout.log"),outText);await File.WriteAllTextAsync(Path.Combine(resolved,"initialize-"+id+".stderr.log"),errText);
        }
        return (process.ExitCode, outText, errText);
    }
    /// <summary>默认使用本普通克隆作为源码根。</summary>
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}

/// <summary>凭据只存在于脚本的临时私有文件，名称、账号和目标服务器必须严格匹配本轮编号。</summary>
internal static class FreshInstallConfiguration
{
    /// <summary>当前完整包增加固定Agent表；历史冻结包继续使用其原固定程序集，不冒称已重新验收。</summary>
    internal static int ExpectedFullTableCount(MetadataCatalog catalog)=>catalog.ConfigTables.Count+10+Metarill.Runtime.Agent.AgentFoundationCatalog.ForApplication(catalog).ConfigTables.Count;

    /// <summary>从专属验收配置读取并验证一个随机库。</summary>
    public static DatabaseOptions Read(string property, string suffix)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var options = config.RootElement.GetProperty(property).Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Validate(options, suffix); return options;
    }
    /// <summary>只读取本轮私有配置的隔离 Redis，缺失时在任何 MySQL 连接前停止。</summary>
    internal static string ReadLoginRedis()
    {
        var freshSettings = Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS");
        var path = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_PLATFORM_BOOTSTRAP_NATIVE_SETTINGS"))
            ? Environment.GetEnvironmentVariable("METARILL_FRESH_LOGIN_REDIS_SETTINGS") ?? freshSettings
            : freshSettings;
        return ReadLoginRedisFile(path);
    }

    /// <summary>协调器与验收宿主共用私有文件验证，错误不附带文件内容。</summary>
    internal static string ReadLoginRedisFile(string? path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("平台正式空库验收需要本轮隔离 Redis DB0 配置。");
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(path));
            return ParseLoginRedis(config.RootElement);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("隔离 Redis 验收配置不是有效 JSON。");
        }
    }

    /// <summary>按真实 Redis 格式解析，只接受 DB0，错误不携带原配置。</summary>
    internal static string ParseLoginRedis(JsonElement settings)
    {
        if (settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("LoginFailureRedisConnectionString", out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() is { } connection && !string.IsNullOrWhiteSpace(connection))
        {
            try
            {
                var parsed = StackExchange.Redis.ConfigurationOptions.Parse(connection);
                if (parsed.EndPoints.Count > 0 && parsed.DefaultDatabase is null or 0) return connection;
            }
            catch (ArgumentException) { /* 不回显包含凭据的解析错误。 */ }
        }
        throw new InvalidOperationException("平台正式空库验收需要本轮隔离 Redis DB0 配置。");
    }

    /// <summary>核对实例与空库，保证仅在本轮独占租约创建合成表。</summary>
    internal static async Task VerifyEmptyLeaseAsync(DatabaseSession session)
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var instance = settings.RootElement.GetProperty("InstancePin");
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@hostname,@@server_uuid,DATABASE(),(SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE())";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(instance.GetProperty("hostname").GetString(), reader.GetString(0));
        Assert.Equal(instance.GetProperty("serverUuid").GetString(), reader.GetString(1));
        Assert.Equal(session.Options.ExpectedDatabase, reader.GetString(2));
        Assert.Equal(0L, reader.GetInt64(3));
    }

    /// <summary>禁止误连固定业务库、生产服务器或高权限账号。</summary>
    public static void Validate(DatabaseOptions options, string suffix)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!));
        var run = config.RootElement.GetProperty("RunId").GetString()!;
        if (!Regex.IsMatch(run, "^[a-f0-9]{16}$") || options.Provider != "mysql" || options.ExpectedDatabase != "metarill_fresh_" + run + "_" + suffix) throw new InvalidOperationException("首次安装只允许本轮随机新库。");
        var builder = new DbConnectionStringBuilder { ConnectionString = options.ConnectionString };
        if (builder["server"].ToString() is not ("deve-server.yinzon.com" or "192.168.31.200") || builder["user id"].ToString() != "mtf_" + run) throw new InvalidOperationException("首次安装只允许Server-200临时受限账号。");
        DatabaseProviders.Resolve(options.Provider).Validate(options);
    }
}

/// <summary>仅拥有本 fixture 创建的随机回环宿主，不影响用户开发服务。</summary>
internal sealed class FreshHost : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    private readonly string? evidenceDirectory;
    private readonly string[] secrets;
    public HttpClient Client { get; }
    /// <summary>限制日志落点，并提取子进程配置的秘密供停止后脱敏。</summary>
    private FreshHost(ProcessStartInfo info, Uri address)
    {
        if (info.Environment.TryGetValue("METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY", out var directory) && !string.IsNullOrWhiteSpace(directory))
        {
            var root = Path.GetFullPath(Path.Combine(NonRedundantApplicationFixture.Root(), ".local")) + Path.DirectorySeparatorChar;
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(resolved)) throw new InvalidOperationException("宿主日志只允许现有私有验收目录。");
            evidenceDirectory = resolved;
        }
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in info.Environment.Where(pair => Regex.IsMatch(pair.Key, "password|token|secret|connectionstring|api.?key", RegexOptions.IgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;
            values.Add(pair.Value);
            if (!pair.Key.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase)) continue;
            if (pair.Key.EndsWith("__LoginFailures__ConnectionString", StringComparison.OrdinalIgnoreCase))
            {
                // Redis 为逗号分隔格式，不能交给 ADO.NET 连接串解析器。
                var password = StackExchange.Redis.ConfigurationOptions.Parse(pair.Value).Password;
                if (!string.IsNullOrEmpty(password)) values.Add(password);
                continue;
            }
            var connection = new DbConnectionStringBuilder { ConnectionString = pair.Value };
            foreach (string key in connection.Keys)
                if (Regex.IsMatch(key, "password|pwd|token|secret", RegexOptions.IgnoreCase) && connection[key] is string value && value.Length > 0) values.Add(value);
        }
        secrets = values.OrderByDescending(value => value.Length).ToArray();
        // 配置校验在启动之前完成，避免无效日志目录留下孤儿宿主。
        process = Process.Start(info)!; stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
        Client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(15) };
    }
    /// <summary>取得临时端口并启动实际平台二进制，等待健康响应。</summary>
    public static async Task<FreshHost> StartAsync(ProcessStartInfo info, string health, Uri? previousAddress = null)
    {
        // 正式助手profile固定API地址，重启必须复用本fixture此前的回环端口；端口被占用时拒绝，不终止其它进程。
        if (previousAddress is not null && (previousAddress.Scheme != "http" || previousAddress.Host != "127.0.0.1" || previousAddress.Port <= 0 || previousAddress.AbsolutePath != "/" || previousAddress.Query.Length != 0 || previousAddress.Fragment.Length != 0)) throw new InvalidOperationException("验收重启只允许原回环根地址。");
        var listener = new TcpListener(IPAddress.Loopback, previousAddress?.Port ?? 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var address = new Uri($"http://127.0.0.1:{port}"); info.ArgumentList.Add("--urls"); info.ArgumentList.Add(address.ToString());
        var host = new FreshHost(info, address);
        for (var attempt = 0; attempt < 150 && !host.process.HasExited; attempt++)
        {
            try { using var response = await host.Client.GetAsync(health); if (response.IsSuccessStatusCode) return host; } catch (HttpRequestException) { }
            await Task.Delay(100);
        }
        await host.DisposeAsync(); throw new InvalidOperationException("首次安装验收宿主未就绪，日志不回显配置。");
    }
    /// <summary>退出宿主后等待日志管道结束，释放DLL占用。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(); var outText = await stdout; var errText = await stderr;
            if (evidenceDirectory is null) return;
            var id = Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "host-" + id + ".stdout.log"), Redact(outText));
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "host-" + id + ".stderr.log"), Redact(errText));
        }
        finally { process.Dispose(); Client.Dispose(); }
    }
    /// <summary>保留诊断信息，移除配置秘密和请求中的登录、下载凭证。</summary>
    private string Redact(string value)
    {
        foreach (var secret in secrets) value = value.Replace(secret, "[已脱敏]", StringComparison.Ordinal);
        return Regex.Replace(value, @"((?:loginStateId|access_token|refresh_token|exportId|Authorization)\s*[=:]\s*)[^\s&""<>]+", "$1[已脱敏]", RegexOptions.IgnoreCase);
    }
}
