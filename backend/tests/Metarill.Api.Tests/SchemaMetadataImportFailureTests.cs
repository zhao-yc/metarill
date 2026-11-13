using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    private const string ImportRoute = "/admin/sys/database/import/";

    /// <summary>只替换导入器的ID源，第一表含索引全部写入后第二表失败，五类配置均不得留下半批记录。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_late_failure_rolls_back_every_configuration_reference()
    {
        var fault = new ImportIdFault();
        await using var f = await ImportCase.Create(2, factory => factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped(provider => ActivatorUtilities.CreateInstance<SchemaMetadataImportBusiness>(provider, new ImportFailingIds(provider.GetRequiredService<IIdGenerator>(), fault)));
        })));
        var before = await f.Fingerprint(); var proof = await f.Prepare(); fault.Enabled = true;
        var response = await Post(f.Client, ImportRoute + "apply", proof); Assert.Equal(500, response.GetProperty("code").GetInt32()); Assert.Equal(9, fault.Calls);
        var status = Data(await Post(f.Client, ImportRoute + "status", proof)); Assert.Equal("unconfirmed", status.GetProperty("state").GetString()); Assert.Equal(JsonValueKind.Null, status.GetProperty("configurationSaved").ValueKind);
        foreach (var table in new[] { "config_table", "config_column", "config_index", "config_index_column", "config_page" })
            foreach (var id in fault.Generated) Assert.Empty(await f.Store.ReadIncludingDeletedAsync(table, new Dictionary<string,object?> { ["id"] = id }));
        await f.AssertUnregistered(); Assert.Equal(before, await f.Fingerprint()); await f.AssertSourceRows();
        f.Record(new { result = "late-batch-rollback", generated = fault.Generated.ToArray(), calls = fault.Calls, sourceUnchanged = true });
    }

    /// <summary>结构漂移、越权参数、错误会话及MCP未确认均不能把审核结果转成写入；错误确认不消耗计划。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_rechecks_schema_and_web_session_and_mcp_confirmation()
    {
        await using var f = await ImportCase.Create(1); var proof = await f.Prepare();
        using var anonymous = f.Factory.CreateClient();
        Assert.Equal(500, (await Body(await anonymous.PostAsJsonAsync("/api" + ImportRoute + "apply", proof))).GetProperty("code").GetInt32());
        using var wrong = f.Factory.CreateClient(); wrong.DefaultRequestHeaders.Add("loginStateId", new string('x', 64)); wrong.DefaultRequestHeaders.Add("sysParams", Assert.Single(f.Client.DefaultRequestHeaders.GetValues("sysParams")));
        Assert.Equal(500, (await Post(wrong, ImportRoute + "apply", proof)).GetProperty("code").GetInt32());
        Assert.Equal(500, (await Post(f.Client, ImportRoute + "apply", new { planId = proof.PlanId, confirmSha256 = proof.ConfirmSha256, logicalMetadata = new { } })).GetProperty("code").GetInt32());
        await using var transport = new ModelContextProtocol.Client.HttpClientTransport(new() { Endpoint = new Uri(f.Client.BaseAddress!, "api/mcp"), TransportMode = ModelContextProtocol.Client.HttpTransportMode.StreamableHttp }, f.Client, ownsHttpClient: false);
        await using var mcp = await ModelContextProtocol.Client.McpClient.CreateAsync(transport);
        var notConfirmed = await mcp.CallToolAsync("apply_database_metadata_import", new Dictionary<string,object?> { ["data"] = new { planId = proof.PlanId, confirmSha256 = proof.ConfirmSha256 }, ["confirmed"] = false }); Assert.True(notConfirmed.IsError);
        Assert.Equal("prepared", Data(await Post(f.Client, ImportRoute + "status", proof)).GetProperty("state").GetString());
        await f.Execute($"ALTER TABLE `{f.Names[0]}` ADD COLUMN changed_after_preview int NULL");
        Assert.Equal(500, (await Post(f.Client, ImportRoute + "apply", proof)).GetProperty("code").GetInt32()); await f.AssertUnregistered(); await f.AssertSourceRows();
        var next = await mcp.CallToolAsync("prepare_database_metadata_import", new Dictionary<string,object?> { ["data"] = new { tableNames = f.Names } }); Assert.NotEqual(true, next.IsError);
        var plan = Json(Assert.Single(next.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text);
        var saved = await mcp.CallToolAsync("apply_database_metadata_import", new Dictionary<string,object?> { ["data"] = new { planId = plan.GetProperty("planId").GetString(), confirmSha256 = plan.GetProperty("confirmSha256").GetString() }, ["confirmed"] = true }); Assert.NotEqual(true, saved.IsError);
        Assert.True(Json(Assert.Single(saved.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()).Text).GetProperty("configurationSaved").GetBoolean());
        f.Record(new { result = "source-drift-and-mcp", sourceDdlOnlyChangedByFixture = true });
    }

    /// <summary>真实旧RR快照在等待项目锁时形成，另一事务只改本轮配置；锁后必须当前读发现版本冲突。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_current_metadata_read_rejects_stale_repeatable_read_plan()
    {
        await using var f = await ImportCase.Create(1); var dummy = "mt_version_" + Guid.NewGuid().ToString("N"); f.MetadataOnlyNames.Add(dummy);
        var added = Data(await Post(f.Client, "/admin/auto/config_table/6191083005411328/add", new { name = dummy, tableName = dummy, yzAdminTypeId = "2200000000000100001", isRongyu = 1 })); var dummyId = long.Parse(added.GetProperty("id").GetString()!);
        var proof = await f.Prepare(); var registry = f.Factory.Services.GetRequiredService<MetadataRegistry>(); var platform = f.Sessions.Platform;
        using var writer = new DatabaseSession(platform.Options); var writerStore = new SqlRowStore(writer, registry.Platform);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<JsonElement>? apply = null;
        var writing = writer.InTransactionAsync(async () =>
        {
            Assert.Single(await writerStore.ReadForUpdateAsync("yz_project", new Dictionary<string,object?> { ["id"] = long.Parse(Project) }));
            await writerStore.UpdateAsync("config_table", dummyId, new Dictionary<string,object?> { ["name"] = dummy + "-新版本" }); ready.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(40)); return true;
        });
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var probe = platform.Provider.CreateConnection(platform.Options); await probe.OpenAsync(); await using var command = probe.CreateCommand(); command.CommandText = "SET SESSION innodb_lock_wait_timeout=1"; await command.ExecuteNonQueryAsync();
            // 先证明本测试启动前主身份项目没有被其他任务锁住，再用真实1205证明导入已进入预锁阶段。
            async Task<bool> Probe()
            {
                await using var transaction = await probe.BeginTransactionAsync(); command.Transaction = transaction; command.CommandText = "SELECT id FROM yz_project WHERE id=@id FOR UPDATE"; command.Parameters.Clear(); var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = long.Parse(registry.Platform.YzProjectId); command.Parameters.Add(parameter);
                try { await command.ExecuteScalarAsync(); return false; }
                catch (MySqlConnector.MySqlException error) when (error.Number == 1205) { return true; }
                finally { await transaction.RollbackAsync(); command.Transaction = null; }
            }
            Assert.False(await Probe()); apply = Post(f.Client, ImportRoute + "apply", proof);
            var waited = false; for (var i = 0; i < 30 && !apply.IsCompleted; i++) { if (await Probe()) { waited = true; break; } await Task.Delay(30); }
            Assert.True(waited, "导入没有取得主身份项目锁，不能声称已证明旧RR排队"); Assert.False(apply.IsCompleted);
            release.TrySetResult(); await writing.WaitAsync(TimeSpan.FromSeconds(10)); var rejected = await apply.WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(500, rejected.GetProperty("code").GetInt32()); Assert.Contains("版本冲突", rejected.ToString());
            await f.AssertUnregistered(); await f.AssertSourceRows(); f.Record(new { result = "old-rr-current-read", principalLock1205 = true, ownedMetadataCommitted = true });
        }
        finally { release.TrySetResult(); await writing; if (apply is not null) await apply; }
    }

    /// <summary>同名只有真实实例和数据库相同才构成冲突；其他平台库中的自有配置必须保留。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_allows_same_name_in_different_bound_database()
    {
        await using var f = await ImportCase.Create(1); var registry = f.Factory.Services.GetRequiredService<MetadataRegistry>();
        var schemas = f.Factory.Services.GetRequiredService<ProjectSchemaService>();
        Assert.NotEqual((await schemas.Resolve(Project).InspectTargetIdentityAsync()).Fingerprint, (await schemas.Resolve(registry.Platform.YzProjectId).InspectTargetIdentityAsync()).Fingerprint);
        using var other = f.Factory.CreateClient(); other.DefaultRequestHeaders.Add("loginStateId", Assert.Single(f.Client.DefaultRequestHeaders.GetValues("loginStateId"))); other.DefaultRequestHeaders.Add("sysParams", Uri.EscapeDataString(JsonSerializer.Serialize(new { yzProjectId = registry.Platform.YzProjectId })));
        string? id = null;
        try
        {
            id = Data(await Post(other, "/admin/auto/config_table/6191083005411328/add", new { name = "同名不同库验收", tableName = f.Names[0], yzAdminTypeId = registry.Platform.YzAdminTypes.First().Id, isRongyu = 1 })).GetProperty("id").GetString();
            var proof = await f.Prepare(); var saved = Data(await Post(f.Client, ImportRoute + "apply", proof)); Assert.True(saved.GetProperty("configurationSaved").GetBoolean());
            Assert.Equal(registry.Platform.YzProjectId, SqlRowStore.Text(Assert.Single(await f.Store.ReadAsync("config_table", new Dictionary<string,object?> { ["id"] = long.Parse(id!) })), "yz_project_id"));
            f.Record(new { result = "same-name-different-target", otherProjectPreserved = true });
        }
        finally
        {
            // 响应丢失也只发现本轮随机名称在明确平台项目中的配置，不误删同名业务配置。
            foreach (var row in await f.Store.ReadAsync("config_table", new Dictionary<string,object?> { ["table_name"] = f.Names[0], ["yz_project_id"] = long.Parse(registry.Platform.YzProjectId) }))
                Data(await Body(await other.SendAsync(new(HttpMethod.Delete, "/api/admin/auto/config_table/6191083005411328") { Content = JsonContent.Create(new { id = SqlRowStore.Text(row, "id") }) })));
        }
    }

    /// <summary>完整范围锁以实库1205证明能拦截不遵守项目预锁的新增；释放后新增可执行并由测试回滚。</summary>
    [MySqlApiFact]
    public async Task Metadata_import_reservation_range_blocks_noncooperating_insert_phantom()
    {
        await using var f = await ImportCase.Create(1); var registry = f.Factory.Services.GetRequiredService<MetadataRegistry>();
        var id = f.Factory.Services.GetRequiredService<IIdGenerator>().NextId(); var values = (await f.Store.ReadAsync("config_table")).First().ToDictionary(pair => pair.Key, pair => pair.Value);
        values["id"] = id; values["name"] = values["table_name"] = f.Names[0]; values["yz_project_id"] = long.Parse(Project); values["delete_no"] = null;
        using var contender = new DatabaseSession(f.Sessions.Platform.Options); var contenderStore = new SqlRowStore(contender, registry.Platform);
        // 此测试刻意使用基础设施写入模拟未遵守锁协议的旧入口；不把该旁路变成产品写入口。
        async Task Insert(bool rollback)
        {
            await contender.InTransactionAsync(async () =>
            {
                var transaction = contender.CurrentTransaction!; await using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction; command.CommandText = "SET SESSION innodb_lock_wait_timeout=1"; await command.ExecuteNonQueryAsync();
                await contenderStore.InsertAsync("config_table", values); if (rollback) throw new InvalidOperationException("本轮测试主动回滚"); return true;
            });
        }
        await f.Store.Session.InTransactionAsync(async () =>
        {
            await f.Store.ReadTableReservationsForImportAsync(); var error = await Assert.ThrowsAsync<MySqlConnector.MySqlException>(() => Insert(false)); Assert.Equal(1205, error.Number); return true;
        });
        var rolledBack = await Assert.ThrowsAsync<InvalidOperationException>(() => Insert(true)); Assert.Equal("本轮测试主动回滚", rolledBack.Message);
        Assert.Empty(await f.Store.ReadIncludingDeletedAsync("config_table", new Dictionary<string,object?> { ["id"] = id }));
        f.Record(new { result = "range-lock-phantom", insert1205 = true, released = true, testInsertRolledBack = true });
    }

    /// <summary>计划失败注入只作用于导入器；不影响登录、原配置CRUD或其他任务的ID生成。</summary>
    private sealed class ImportIdFault { internal bool Enabled; internal int Calls; internal List<long> Generated { get; } = []; }
    private sealed class ImportFailingIds(IIdGenerator inner, ImportIdFault state) : IIdGenerator
    {
        /// <summary>第二表中途出错，第一表已写入真实表/列/索引/索引列，事务应整批撤销。</summary>
        public long NextId() { if (state.Enabled && ++state.Calls == 9) throw new InvalidOperationException("本轮导入晚批故障"); var id = inner.NextId(); if (state.Enabled) state.Generated.Add(id); return id; }
    }
    private sealed record ImportProof(string PlanId, string ConfirmSha256);

    /// <summary>精确拥有随机表及元数据，失败也通过原删除入口清理配置，独立连接核零并写无秘密证据。</summary>
    private sealed class ImportCase : IAsyncDisposable
    {
        internal WebApplicationFactory<Program> Factory { get; }
        internal HttpClient Client { get; }
        internal TargetDatabaseSessions Sessions { get; }
        internal SqlRowStore Store { get; }
        internal string[] Names { get; }
        internal List<string> MetadataOnlyNames { get; } = [];
        private readonly List<string> created = [];
        private readonly string? evidence;
        private ImportCase(WebApplicationFactory<Program> factory, HttpClient client, int count)
        {
            Factory = factory; Client = client; Sessions = factory.Services.GetRequiredService<TargetDatabaseSessions>(); Store = factory.Services.GetRequiredService<SqlRowStore>();
            var suffix = Guid.NewGuid().ToString("N"); Names = Enumerable.Range(0, count).Select(i => "mt_save_" + suffix + "_" + i).ToArray();
            if (Environment.GetEnvironmentVariable("METARILL_IMPORT_SAVE_EVIDENCE") is { Length: > 0 } root) { evidence = Path.Combine(root, suffix); Directory.CreateDirectory(evidence); File.WriteAllText(Path.Combine(evidence, "intent.json"), JsonSerializer.Serialize(new { names = Names, metadataProject = Project, independentCleanupRequired = true })); }
        }
        /// <summary>只能连接既有测试库，不建库、不建账号、不增加权限。</summary>
        internal static async Task<ImportCase> Create(int count, Func<WebApplicationFactory<Program>,WebApplicationFactory<Program>>? customize = null)
        {
            var (factory, client) = await Login(customize); var fixture = new ImportCase(factory, client, count);
            try { foreach (var name in fixture.Names) { await fixture.Execute($"CREATE TABLE `{name}` (`id` bigint NOT NULL PRIMARY KEY,`name` varchar(64) NULL,`delete_no` datetime(6) NULL,INDEX yzp_idx_name (`name`)) ENGINE=InnoDB"); fixture.created.Add(name); await fixture.Execute($"INSERT INTO `{name}` (id,name) VALUES (1,'本轮业务行')"); } return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }
        /// <summary>源语句仅含本轮服务端随机名称及固定样本常量。</summary>
        internal async Task Execute(string sql) { await using var connection = Sessions.Business.Provider.CreateConnection(Sessions.Business.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        /// <summary>始终走真实HTTP审核计划，不直接构造内部缓存。</summary>
        internal async Task<ImportProof> Prepare() { var plan = Data(await Post(Client, ImportRoute + "prepare", new { tableNames = Names })); return new(plan.GetProperty("planId").GetString()!, plan.GetProperty("confirmSha256").GetString()!); }
        /// <summary>真实源快照用于证明类型/默认/索引与DDL没有被保存过程改写。</summary>
        internal async Task<string> Fingerprint() => (await Factory.Services.GetRequiredService<ProjectSchemaService>().Resolve(Project).InspectForImportAsync(Names)).Snapshot.Fingerprint;
        /// <summary>含墓碑查询证明失败事务没有留下配置记录。</summary>
        internal async Task AssertUnregistered() { foreach (var name in Names) Assert.Empty(await Store.ReadIncludingDeletedAsync("config_table", new Dictionary<string,object?> { ["table_name"] = name })); }
        /// <summary>业务行在源库独立读取，不能用元数据保存响应代替。</summary>
        internal async Task AssertSourceRows() { await using var connection = Sessions.Business.Provider.CreateConnection(Sessions.Business.Options); await connection.OpenAsync(); foreach (var name in Names) { await using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM `{name}` WHERE id=1 AND name='本轮业务行'"; Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync())); } }
        /// <summary>只记录本轮对象和结果，不保存会话或连接字符串。</summary>
        internal void Record(object value) { if (evidence is not null) File.WriteAllText(Path.Combine(evidence, "proof.json"), JsonSerializer.Serialize(value)); }
        /// <summary>逻辑删除自有配置及子项后精确删随机源表，使用新连接核验不存在。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var name in Names.Concat(MetadataOnlyNames)) foreach (var row in await Store.ReadAsync("config_table", new Dictionary<string,object?> { ["table_name"] = name }))
                { Assert.Equal(Project, SqlRowStore.Text(row, "yz_project_id")); Data(await Body(await Client.SendAsync(new(HttpMethod.Delete, "/api/admin/auto/config_table/6191083005411328") { Content = JsonContent.Create(new { id = SqlRowStore.Text(row, "id") }) }))); }
                foreach (var name in created.AsEnumerable().Reverse()) await Execute($"DROP TABLE `{name}`");
                await using var connection = Sessions.Business.Provider.CreateConnection(Sessions.Business.Options); await connection.OpenAsync(); var remaining = 0L;
                foreach (var name in created) { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@name"; var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = name; command.Parameters.Add(parameter); remaining += Convert.ToInt64(await command.ExecuteScalarAsync()); }
                foreach (var name in Names.Concat(MetadataOnlyNames)) Assert.Empty(await Store.ReadAsync("config_table", new Dictionary<string,object?> { ["table_name"] = name }));
                if (evidence is not null) File.WriteAllText(Path.Combine(evidence, "cleanup.json"), JsonSerializer.Serialize(new { names = created, remaining, activeMetadataRemaining = 0, tombstonesPreserved = true, independentConnection = true })); Assert.Equal(0L, remaining);
            }
            finally { Client.Dispose(); await Factory.DisposeAsync(); }
        }
    }
}
