
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Generation.Tests;

[Collection(GenerationMySqlCollection.Name)]
public sealed class GeneratedParentLockMySqlTests(ITestOutputHelper output)
{
    private const long Parent = 9007199254740993L;
    private const string Route = "/api/admin/auto/customer/101";

    /// <summary>实际生成的新增及导入只锁读父主键，原拒绝和跨连接互斥均保持。</summary>
    [FreshInstallMySqlFact]
    public async Task Generated_add_and_import_project_parent_id_and_keep_the_transaction_lock()
    {
        using var session = new DatabaseSession(FreshInstallConfiguration.Read("PackagedDatabase", "package"));
        await FreshInstallConfiguration.VerifyEmptyLeaseAsync(session);
        var catalog = GeneratedMutationGuardScopeTests.Catalog("owner_id", "int64");
        catalog.ConfigPages.Single().IsNew = 1;
        catalog.ConfigColumns.Add(new() { Id = "23", ConfigTableId = "2", ColumnName = "delete_no", LogicalType = "datetime" });
        catalog.ConfigColumns.Add(new() { Id = "24", ConfigTableId = "2", ColumnName = "hidden_payload", LogicalType = "string", ColumnLength = "max" });
        var schema = new SchemaService(session);
        var plan = await schema.PreviewAsync(catalog);
        Assert.Equal(new[] { "customer", "owner" }, plan.Changes.Select(change => change.Table).Order());
        Assert.All(plan.Changes, change => Assert.Equal("createTable", change.Kind));
        Assert.True((await schema.ApplyAsync(plan)).Succeeded);
        var store = new SqlRowStore(session, catalog);
        foreach (var (id, project, deleted) in new[] { (Parent, 100L, false), (Parent + 1, 101L, false), (Parent + 2, 100L, true) })
            await store.InsertAsync("owner", new Dictionary<string, object?>
            {
                ["id"] = id, ["yz_project_id"] = project, ["delete_no"] = deleted ? DateTime.UtcNow : null,
                ["hidden_payload"] = new string('x', 16384)
            });

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-parent-lock");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            const string ns = "Example.ParentLock";
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var guard = new LockProbe(session);
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(session);
            builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(896));
            builder.Services.AddSingleton<IRecordMutationGuard>(guard);
            builder.Services.AddSingleton<IRuntimeAuthorization>(new WriteAuthorization());
            assembly.GetType(ns + ".Generated.GeneratedRegistration")!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly)
                .AddJsonOptions(value => value.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            await using var app = builder.Build();
            app.Use(async (context, next) =>
            {
                try { await next(); }
                catch (InvalidOperationException error)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message));
                }
            });
            app.MapControllers();
            await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)), Timeout = TimeSpan.FromSeconds(20) };
            var commands = new ConcurrentQueue<string>();
            session.Orm.Aop.CommandBefore += (_, args) =>
            {
                if (!args.Command.CommandText.Contains("\u0060owner\u0060", StringComparison.Ordinal)) return;
                Assert.Same(session.CurrentTransaction, args.Command.Transaction);
                Assert.NotNull(args.Command.Transaction);
                commands.Enqueue(args.Command.CommandText);
            };
            foreach (var id in new[] { Parent + 1, Parent + 2, Parent + 3 })
            {
                using var rejected = await client.PostAsJsonAsync(Route + "/add?parentId=" + id, new { name = "应拒绝" });
                Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
                var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("父记录不存在或不属于当前项目。", error.GetProperty("msg").GetString());
            }
            Assert.Equal(3, commands.Count);
            Assert.Equal(0, guard.LockedWrites);
            Assert.Empty(await store.ReadAsync("customer"));

            using var added = await client.PostAsJsonAsync(Route + "/add?parentId=" + Parent, new { name = "新增明细" });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var addBody = await added.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(200, addBody.GetProperty("code").GetInt32());
            Assert.Equal(JsonValueKind.String, addBody.GetProperty("data").GetProperty("id").ValueKind);
            Assert.Equal("新增明细", addBody.GetProperty("data").GetProperty("name").GetString());

            using var imported = await client.PostAsJsonAsync(Route + "/importDatas101",
                new { parentId = Parent.ToString(), requestJson = new[] { new Dictionary<string, string> { ["名称"] = "导入明细" } } });
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
            var importBody = await imported.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(200, importBody.GetProperty("code").GetInt32());
            Assert.Equal("导入成功1条数据", importBody.GetProperty("data").GetString());
            Assert.Equal(2, guard.LockedWrites);
            var parentLocks = commands.Where(sql => sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)).ToArray();
            var foreignChecks = commands.Where(sql => !sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.Equal(6, parentLocks.Length);
            Assert.Equal(2, foreignChecks.Length);
            Assert.All(foreignChecks, sql => Assert.Contains("SELECT count(1)", sql, StringComparison.OrdinalIgnoreCase));
            Assert.All(parentLocks, sql =>
            {
                var from = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
                Assert.True(from > 0);
                Assert.Equal("id", Assert.Single(Regex.Matches(sql[..from], "\u0060([a-z_]+)\u0060")).Groups[1].Value);
                Assert.Contains("\u0060yz_project_id\u0060", sql[from..]);
                Assert.Contains("\u0060delete_no\u0060", sql[from..]);
                Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
            });
            Assert.Null(session.CurrentTransaction);
            Assert.Equal(Parent, await guard.ReadParentNowaitAsync(default));
            var rows = await store.ReadAsync("customer");
            Assert.Equal(new[] { "导入明细", "新增明细" }.Order(StringComparer.Ordinal),
                rows.Select(row => (string)row["name"]!).Order(StringComparer.Ordinal));
            Assert.All(rows, row => Assert.Equal(Parent, Convert.ToInt64(row["owner_id"])));
            output.WriteLine($"原 HTTP 拒绝 3 次、成功 2 次。父主键锁读 {parentLocks.Length} 次、外键计数 {foreignChecks.Length} 次、独立 NOWAIT 锁冲突 {guard.LockedWrites} 次。");

        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>独立连接即时失败才证明生成服务已经持有父行锁。</summary>
    private sealed class LockProbe(DatabaseSession session) : IRecordMutationGuard
    {
        public int LockedWrites { get; private set; }

        public Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken cancellationToken = default)
        {
            Assert.Same(session.CurrentTransaction, preparation.Transaction);
            return Task.CompletedTask;
        }

        public async Task BeforeWriteAsync(RecordMutationValidation validation, CancellationToken cancellationToken = default)
        {
            Assert.Same(session.CurrentTransaction, validation.Transaction);
            Assert.Equal(3572, (await Assert.ThrowsAsync<MySqlException>(() => ReadParentNowaitAsync(cancellationToken))).Number);
            LockedWrites++;
        }

        public async Task<long> ReadParentNowaitAsync(CancellationToken cancellationToken)
        {
            await using var connection = new MySqlConnection(session.Options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM owner WHERE id=@id FOR UPDATE NOWAIT";
            command.Parameters.AddWithValue("id", Parent);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
    }

    private sealed class WriteAuthorization : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => new(896, "父锁专项普通账号", false, "100", "1");
        public void RequirePagePermission(string pageId, string operation)
        {
            if (pageId != "101" || operation is not ("create" or "import"))
                throw new UnauthorizedAccessException("当前专项只授予新增与导入。");
        }
    }
}
