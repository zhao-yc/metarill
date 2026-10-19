using System.Net.Http.Json;
using Metarill.Core.Persistence;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    [MySqlApiFact]
    public async Task Generated_metadata_update_waits_for_project_before_locking_the_column()
    {
        var guardEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, client) = await Login(current => current.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRecordMutationGuard>();
            services.AddScoped<IRecordMutationGuard>(provider => new SignallingConfigurationGuard(ActivatorUtilities.CreateInstance<ConfigurationMutationGuard>(provider), guardEntered));
        })));
        await using var disposable = factory; using var http = client;
        const string tableRoute = "/admin/auto/config_table/6191083005411328";
        string? tableId = null; DatabaseSession? ownerSession = null; Task? owner = null; Task<HttpResponseMessage>? updating = null;
        var projectLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectColumn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var columnLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var table = Data(await Post(client, tableRoute + "/add", new { name = "锁序验收", tableName = "mt_lock_order_" + Guid.NewGuid().ToString("N")[..12], yzAdminTypeId = "2200000000000100001", isRongyu = 1 }));
            tableId = table.GetProperty("id").GetString()!;
            Data(await Post(client, "/admin/custom/index/save_batch_create_columns", new { config_table_id = tableId, content = "锁序字段 lock_value" }));
            var platformStore = factory.Services.GetRequiredService<SqlRowStore>();
            var column = (await platformStore.ReadAsync("config_column", new Dictionary<string, object?> { ["config_table_id"] = long.Parse(tableId), ["column_name"] = "lock_value" })).Single();
            var columnId = SqlRowStore.Id(column);
            // 独立连接模拟索引工作台：先持项目锁，等待真实 HTTP 更新进入准备阶段。
            ownerSession = new DatabaseSession(factory.Services.GetRequiredService<TargetDatabaseSessions>().Platform.Options);
            var ownerStore = new SqlRowStore(ownerSession, factory.Services.GetRequiredService<MetadataRegistry>().Platform);
            owner = Task.Run(() => ownerSession.InTransactionAsync(async () =>
            {
                await ownerStore.ReadForUpdateAsync("yz_project", new Dictionary<string, object?> { ["id"] = long.Parse(Project) }, cancellation.Token);
                projectLocked.SetResult();
                await inspectColumn.Task.WaitAsync(cancellation.Token);
                // 更新若已经抢占字段锁，这里就会阻塞并使测试失败；正确顺序应立即取得字段锁。
                var locked = await ownerStore.ReadForUpdateAsync("config_column", new Dictionary<string, object?> { ["id"] = columnId }, cancellation.Token);
                Assert.Single(locked); columnLocked.SetResult();
                await releaseOwner.Task.WaitAsync(cancellation.Token); return true;
            }, cancellation.Token));
            await projectLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            updating = client.PostAsJsonAsync("/api/admin/auto/config_column/6217715532627968/update?parentId=" + tableId, new { id = columnId.ToString(), name = "锁序更新完成" }, cancellation.Token);
            await guardEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            inspectColumn.SetResult(); await columnLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(updating.IsCompleted);
            releaseOwner.SetResult(); await owner.WaitAsync(TimeSpan.FromSeconds(10));
            using var response = await updating.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("锁序更新完成", Data(await Body(response)).GetProperty("name").GetString());
        }
        finally
        {
            inspectColumn.TrySetResult(); releaseOwner.TrySetResult();
            // 失败也先排空本测试事务和请求，再删除本测试唯一元数据，不能遗留持锁连接。
            if (owner is not null) { try { await owner.WaitAsync(TimeSpan.FromSeconds(10)); } catch { cancellation.Cancel(); } }
            if (updating is not null) { try { using var response = await updating.WaitAsync(TimeSpan.FromSeconds(10)); } catch { cancellation.Cancel(); } }
            ownerSession?.Dispose();
            if (tableId is not null) Data(await Body(await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api" + tableRoute) { Content = JsonContent.Create(new { id = tableId }) })));
        }
    }

    /// <summary>仅记录真实更新进入准备守卫的时刻，不替换项目锁或业务校验。</summary>
    private sealed class SignallingConfigurationGuard(ConfigurationMutationGuard inner, TaskCompletionSource entered) : IRecordMutationGuard
    {
        public Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken cancellationToken = default)
        {
            if (preparation.TableName == "config_column" && preparation.Operation == RecordMutationOperation.Update) entered.TrySetResult();
            return inner.BeforeMutationAsync(preparation, cancellationToken);
        }
    }
}
