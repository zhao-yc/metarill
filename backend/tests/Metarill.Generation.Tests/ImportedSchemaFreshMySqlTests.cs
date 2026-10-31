using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>消费平台原接口产出的固定Full包，不用手工模型替代导入链路；只允许精确租约的新空库。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class ImportedSchemaFreshMySqlTests
{
    /// <summary>没有项目列的物理表按原显式页面范围工作，不能暗补项目字段。</summary>
    [FreshInstallMySqlFact]
    public Task Imported_source_without_project_column_executes_full_production_web_crud() => Exercise(false);
    /// <summary>真实项目列继续约束原接口，外项目行不能因反向导入而变成可读写。</summary>
    [FreshInstallMySqlFact]
    public Task Imported_source_with_project_column_executes_full_production_web_crud() => Exercise(true);

    /// <summary>包的每个固定来源/程序集在连接前核对，之后才按实例pin进入授权的新库；本测试不创建/删除库或账号。</summary>
    private static async Task Exercise(bool hasProject)
    {
        var property = hasProject ? "ImportedWithProjectDatabase" : "ImportedWithoutProjectDatabase"; var suffix = hasProject ? "import_with" : "import_without";
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS")!)); var configuration = settings.RootElement;
        var prepared = configuration.GetProperty("ImportedPreparations").GetProperty(hasProject ? "withProject" : "withoutProject"); var root = Path.GetFullPath(prepared.GetProperty("directory").GetString()!);
        var boundary = Path.GetFullPath(Path.Combine(NonRedundantApplicationFixture.Root(), ".local/schema-import-generation/final/packages")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(boundary, root, StringComparison.OrdinalIgnoreCase);
        await ValidatePrepared(root, prepared.GetProperty("manifestSha256").GetString()!);
        using var source = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "source.json"))); Assert.Equal(hasProject, source.RootElement.GetProperty("hasProject").GetBoolean());
        var application = Path.Combine(root, "application"); var catalog = await Catalog(Path.Combine(application, "backend/metadata/project.json")); var security = await Catalog(Path.Combine(application, "backend/metadata/security.json")); var files = await Catalog(Path.Combine(application, "backend/metadata/files.json"));
        var table = Assert.Single(catalog.ConfigTables); var page = Assert.Single(catalog.ConfigPages); Assert.Matches("^mt_save_[a-f0-9]{32}_0$", table.TableName); Assert.Equal(source.RootElement.GetProperty("tableName").GetString(), table.TableName); Assert.Equal(hasProject, catalog.ConfigColumns.Any(column => column.ColumnName == "yz_project_id"));
        var original = source.RootElement.GetProperty("snapshot").Deserialize<SchemaInspectionSnapshot>(Json)!; var originalTable = Assert.Single(original.Schema.Tables); Assert.Equal(table.TableName, originalTable.Name); var originalDdl = originalTable.SnapshotDefinition ?? throw new InvalidOperationException("缺少固定源表定义。");
        var physical = MetadataCatalogComposition.MergePhysical(MetadataCatalogComposition.MergePhysical(catalog, files), security);
        var options = FreshInstallConfiguration.Read(property, suffix); using var session = new DatabaseSession(options);
        await using var connection = session.Provider.CreateConnection(options); await connection.OpenAsync();
        await using (var pin = connection.CreateCommand())
        {
            pin.CommandText = "SELECT DATABASE(),@@hostname,@@server_uuid,CURRENT_USER()"; await using var reader = await pin.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); var expected = configuration.GetProperty("InstancePin");
            Assert.Equal(options.ExpectedDatabase, reader.GetString(0)); Assert.Equal(expected.GetProperty("hostname").GetString(), reader.GetString(1)); Assert.Equal(expected.GetProperty("serverUuid").GetString(), reader.GetString(2)); Assert.StartsWith("mtf_" + configuration.GetProperty("RunId").GetString() + "@", reader.GetString(3));
        }
        async Task<long> Tables() { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()"; return Convert.ToInt64(await command.ExecuteScalarAsync()); }
        Assert.Equal(0L, await Tables()); var evidence = Path.Combine(NonRedundantApplicationFixture.Root(), ".local/schema-import-generation/fresh", configuration.GetProperty("RunId").GetString()! + "-" + suffix); Directory.CreateDirectory(evidence);
        var username = "import-owner-" + suffix; var password = Guid.NewGuid().ToString("N"); var readerPassword = Guid.NewGuid().ToString("N"); var hostDirectory = Path.Combine(application, "backend/Application.Host"); FreshHost? host = null; var stage = "零库普通启动";
        ProcessStartInfo HostInfo(string? operation = null)
        {
            var info = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory); info.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Release/net10.0/Application.Host.dll")); if (operation is not null) info.ArgumentList.Add(operation);
            info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; info.Environment["Database__Provider"] = "mysql"; info.Environment["Database__ExpectedDatabase"] = options.ExpectedDatabase; info.Environment["Database__ConnectionString"] = options.ConnectionString;
            info.Environment["Authentication__Mode"] = "Web"; info.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false"; info.Environment["Authentication__Bootstrap__AdminTypeId"] = page.YzAdminTypeId; info.Environment["Authentication__Bootstrap__Username"] = username; info.Environment["Authentication__Bootstrap__Password"] = password; info.Environment["Ids__WorkerId"] = "804";
            info.Environment["Files__RootDirectory"] = Path.Combine(evidence, "files"); info.Environment["Exports__RootDirectory"] = Path.Combine(evidence, "exports"); info.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"] = evidence; return info;
        }
        async Task SourceUnchanged()
        {
            var now = Assert.Single((await new SchemaService(session).InspectSnapshotAsync(catalog)).Schema.Tables);
            Assert.Equal(MySqlSchemaPlanner.Fingerprint(new("source-only", [originalTable])), MySqlSchemaPlanner.Fingerprint(new("source-only", [now])));
        }
        try
        {
            host = await FreshHost.StartAsync(HostInfo(), "/health"); Assert.Equal(0L, await Tables()); await host.DisposeAsync(); host = null;
            stage = "原物理源和已知样本";
            // 只执行已固定摘要、由本轮平台源表快照返回的单表定义；目标账号只有本轮两库权限。
            Assert.StartsWith("CREATE TABLE `" + table.TableName + "` (", originalDdl);
            await using (var command = connection.CreateCommand()) { command.CommandText = originalDdl; await command.ExecuteNonQueryAsync(); }
            await Seed(1L, "本轮业务行", long.Parse(catalog.YzProjectId)); await Seed(2L, hasProject ? "外项目行" : "源库第二行", checked(long.Parse(catalog.YzProjectId) + 1)); await SourceUnchanged();
            stage = "显式结构与重复初始化";
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-preview")); Assert.Equal(1L, await Tables()); await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply")); Assert.Equal(physical.ConfigTables.Count, await Tables()); await SourceUnchanged();
            await NonRedundantApplicationFixture.RunAsync(HostInfo("--schema-apply")); Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes); await SourceUnchanged();
            var securityStore = new SqlRowStore(session, security); await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security")); var ownerHash = SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager", Row("name", username))), "password"); Assert.True(PasswordHasher.Verify(password, ownerHash)); await NonRedundantApplicationFixture.RunAsync(HostInfo("--initialize-security")); Assert.Equal(ownerHash, SqlRowStore.Text(Assert.Single(await securityStore.ReadAsync("yz_manager", Row("name", username))), "password"));
            stage = "正式登录和原CRUD"; host = await FreshHost.StartAsync(HostInfo(), "/health"); var route = "/api/admin/auto/" + table.TableName + "/" + page.Id; var owner = host.Client;
            await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/page", new PageRequest(), HttpStatusCode.Unauthorized); var ownerToken = await FreshWebHttp.LoginAsync(owner, username, password, form: true);
            var menu = await FreshWebHttp.SendAsync(owner, HttpMethod.Get, "/api/admin/sys/yz_manager/getMenu"); Assert.Contains(page.Id, menu.GetRawText());
            var initial = (await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/page", new PageRequest())).GetProperty("data"); Assert.Equal(hasProject ? 1 : 2, initial.GetProperty("records").GetArrayLength());
            var added = (await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/add", new { name = "正式导入新增" })).GetProperty("data"); var id = added.GetProperty("id").GetString()!; Assert.True(long.Parse(id) > 9007199254740991L);
            var updated = (await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/update", new { id, name = "正式导入修改" })).GetProperty("data"); Assert.Equal("正式导入修改", updated.GetProperty("name").GetString());
            await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/update", new { id, name = (string?)null }, HttpStatusCode.BadRequest); Assert.Equal("正式导入修改", (await FreshWebHttp.SendAsync(owner, HttpMethod.Get, route + "/detail/" + id)).GetProperty("data").GetProperty("name").GetString());
            await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/update", new { id, sql = "1=1" }, HttpStatusCode.BadRequest);
            var copied = (await FreshWebHttp.SendAsync(owner, HttpMethod.Post, route + "/copy", new { id })).GetProperty("data"); Assert.NotEqual(id, copied.GetProperty("id").GetString());
            await FreshWebHttp.SendAsync(owner, HttpMethod.Delete, route, new { id }); Assert.Equal(0L, await Active(id)); Assert.Equal(1L, await Active("1"));
            if (hasProject)
            {
                var refused = await owner.GetFromJsonAsync<JsonElement>(route + "/detail/2"); Assert.Equal(500, refused.GetProperty("code").GetInt32()); Assert.Equal(1L, await Active("2"));
                Assert.Equal(long.Parse(catalog.YzProjectId), await StoredProject(copied.GetProperty("id").GetString()!));
            }
            stage = "普通真实账号及撤权"; var ids = new SnowflakeIdGenerator(805); var role = ids.NextId(); var account = ids.NextId(); var permission = ids.NextId(); var readerName = "import-reader-" + suffix;
            await securityStore.InsertAsync("yz_role", Row("id", role, "name", readerName, "yz_project_id", long.Parse(catalog.YzProjectId), "yz_admin_type_id", long.Parse(page.YzAdminTypeId), "is_super", 0, "is_built_in", 0));
            await securityStore.InsertAsync("yz_manager", Row("id", account, "name", readerName, "password", PasswordHasher.Hash(readerPassword), "yz_role_ids", JsonSerializer.Serialize(new[] { role.ToString(CultureInfo.InvariantCulture) }), "yz_project_id", long.Parse(catalog.YzProjectId), "yz_admin_type_id", long.Parse(page.YzAdminTypeId), "is_disabled", 0));
            await securityStore.InsertAsync("yz_role_permission", Row("id", permission, "yz_role_id", role, "yz_project_id", long.Parse(catalog.YzProjectId), "config_page_id", long.Parse(page.Id), "permission_values", "[\"is_query\"]"));
            using var ordinary = new HttpClient { BaseAddress = owner.BaseAddress }; var readerToken = await FreshWebHttp.LoginAsync(ordinary, readerName, readerPassword); Assert.False((await FreshWebHttp.SendAsync(ordinary, HttpMethod.Get, "/api/admin/sys/yz_manager/getCurrentPermissions")).GetProperty("data").GetProperty("isSuper").GetBoolean());
            await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, route + "/page", new PageRequest()); await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, route + "/add", new { name = "普通账号不能写" }, HttpStatusCode.Forbidden);
            await securityStore.UpdateAsync("yz_role_permission", permission, Row("delete_no", DateTime.UtcNow)); await FreshWebHttp.SendAsync(ordinary, HttpMethod.Post, route + "/page", new PageRequest(), HttpStatusCode.Forbidden);
            stage = "实际宿主重启和持久会话"; await host.DisposeAsync(); host = null; host = await FreshHost.StartAsync(HostInfo(), "/health"); host.Client.DefaultRequestHeaders.Add("loginStateId", ownerToken); await FreshWebHttp.SendAsync(host.Client, HttpMethod.Get, route + "/detail/" + copied.GetProperty("id").GetString());
            using var persisted = new HttpClient { BaseAddress = host.Client.BaseAddress }; persisted.DefaultRequestHeaders.Add("loginStateId", readerToken); await FreshWebHttp.SendAsync(persisted, HttpMethod.Post, route + "/page", new PageRequest(), HttpStatusCode.Forbidden);
            await FreshWebHttp.SendAsync(host.Client, HttpMethod.Post, "/api/admin/sys/yz_manager/exit_login", new { }); await FreshWebHttp.SendAsync(host.Client, HttpMethod.Post, route + "/page", new PageRequest(), HttpStatusCode.Unauthorized); await SourceUnchanged(); Assert.Empty((await new SchemaService(session).PreviewAsync(physical)).Changes);
            await File.WriteAllTextAsync(Path.Combine(evidence, "receipt.json"), JsonSerializer.Serialize(new { passed = true, hasProject, formalProductionWeb = true, fixedAuthorizationInjected = false, originalImportedMetadata = true, sourceSchemaUnchanged = true, ownerAndOrdinaryAccount = true, revocation = true, persistentSession = true, browserExecuted = false, deviceExecuted = false, performanceExecuted = false, cleanupByExactLease = true }, Json));
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(evidence, "failure.json"), JsonSerializer.Serialize(new { passed = false, hasProject, stage, errorType = error.GetType().FullName, error.Message, error.StackTrace, cleanupByExactLease = true }, Json)); throw; }
        finally { if (host is not null) await host.DisposeAsync(); }

        /// <summary>所有业务值参数化，物理表名来自已固定且验证的目录，不拼客户端SQL。</summary>
        async Task Seed(long id, string name, long project)
        {
            await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO " + session.Provider.QuoteIdentifier(table.TableName) + " (id,name" + (hasProject ? ",yz_project_id" : "") + ") VALUES (@id,@name" + (hasProject ? ",@project" : "") + ")";
            foreach (var (key, value) in new[] { ("@id", (object)id), ("@name", (object)name), ("@project", (object)project) }) { var parameter = command.CreateParameter(); parameter.ParameterName = key; parameter.Value = value; command.Parameters.Add(parameter); } await command.ExecuteNonQueryAsync();
        }
        /// <summary>实际存储核验与页面返回分开，删除和租户字段必须真正落库。</summary>
        async Task<long> Active(string id) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM " + session.Provider.QuoteIdentifier(table.TableName) + " WHERE id=@id AND delete_no IS NULL"; var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = long.Parse(id); command.Parameters.Add(parameter); return Convert.ToInt64(await command.ExecuteScalarAsync()); }
        /// <summary>有项目列模型核对服务器写入的真实项目归属。</summary>
        async Task<long> StoredProject(string id) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT yz_project_id FROM " + session.Provider.QuoteIdentifier(table.TableName) + " WHERE id=@id"; var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = long.Parse(id); command.Parameters.Add(parameter); return Convert.ToInt64(await command.ExecuteScalarAsync()); }
    }
    /// <summary>未被审核的本地配置不得偷偷进入正式宿主；完整性保护先于任何数据库访问。</summary>
    [Fact]
    public async Task Prepared_package_rejects_unreviewed_configuration_and_changed_source_before_database_access()
    {
        var boundary = Path.Combine(NonRedundantApplicationFixture.Root(), ".local/schema-import-generation/final/packages"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.json"); await File.WriteAllTextAsync(source, "{}"); var manifest = Path.Combine(directory, "prepared-files.json"); await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new Dictionary<string,string> { ["source.json"] = Hash(await File.ReadAllBytesAsync(source)) })); var expected = Hash(await File.ReadAllBytesAsync(manifest)); await ValidatePrepared(directory, expected);
            var extra = Path.Combine(directory, "application/backend/Application.Host/appsettings.local.json"); Directory.CreateDirectory(Path.GetDirectoryName(extra)!); await File.WriteAllTextAsync(extra, "{}");
            await Assert.ThrowsAsync<InvalidOperationException>(() => ValidatePrepared(directory, expected)); File.Delete(extra);
            await File.WriteAllTextAsync(source, "{\"changed\":true}"); await Assert.ThrowsAsync<InvalidOperationException>(() => ValidatePrepared(directory, expected));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }
    /// <summary>全部来源、程序集和额外配置同时核验；只允许由显式结构命令生成的既有固定历史文件。</summary>
    private static async Task ValidatePrepared(string root, string manifestHash)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var manifestPath = Path.Combine(root, "prepared-files.json"); if (Hash(await File.ReadAllBytesAsync(manifestPath)) != manifestHash) throw new InvalidOperationException("固定包清单摘要不符。");
        var files = JsonSerializer.Deserialize<Dictionary<string,string>>(await File.ReadAllTextAsync(manifestPath)) ?? throw new InvalidOperationException("固定包清单无效。");
        var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Where(path => path is not ("prepared-files.json" or "application/backend/Application.Host/.local/schema-history.jsonl")).ToHashSet(StringComparer.Ordinal);
        if (files.Count == 0 || !actual.SetEquals(files.Keys)) throw new InvalidOperationException("固定包存在未审核文件或缺少原文件，拒绝读取本地认证/数据库配置。");
        foreach (var (relative, hash) in files) if (Hash(await File.ReadAllBytesAsync(CSharpGenerator.SafePath(root, relative))) != hash) throw new InvalidOperationException("固定包来源或程序集发生变化。");
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    /// <summary>这里只读固定包内元数据，不访问平台库或执行反向导入。</summary>
    private static async Task<MetadataCatalog> Catalog(string path) => JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path), Json)!;
    /// <summary>摘要不包含环境凭据，固定文件与程序集先于一切正式库访问核对。</summary>
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    /// <summary>小型基础设施夹具值，只写本轮受限新库，不创建任何数据库或账号。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs) { var row = new Dictionary<string,object?>(); for (var i = 0; i < pairs.Length; i += 2) row[(string)pairs[i]!] = pairs[i + 1]; return row; }
}
