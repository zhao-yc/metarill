using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Metarill.Data;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>复用隔离项目fixture，额外清理仅该随机项目下的索引元数据。</summary>
internal sealed class IndexWorkbenchFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids = new(854);
    internal MetadataWorkbenchFixture Metadata { get; }
    private IndexWorkbenchFixture(MetadataWorkbenchFixture metadata) => Metadata = metadata;
    internal static async Task<IndexWorkbenchFixture> CreateAsync() => new(await MetadataWorkbenchFixture.CreateAsync());
    internal IndexWorkbenchBusiness Business(DatabaseSession? session = null, bool administrator = true, string? project = null, bool agent = false, IIdGenerator? ids = null)
    {
        var actor = new RuntimeActor(8013, "索引测试", administrator, project ?? Metadata.ProjectId.ToString(), Metadata.Registry.Platform.YzAdminTypes[0].Id);
        var context = new DefaultHttpContext(); context.Items[typeof(AuthenticatedIdentity)] = new AuthenticatedIdentity(actor, new HashSet<string>(), 1, agent ? 1 : null);
        return new(new SqlRowStore(session ?? Metadata.Session, Metadata.Registry.Platform), new HttpRuntimeAuthorization(new HttpContextAccessor { HttpContext = context }, Metadata.Registry), ids ?? Ids);
    }
    internal Task<IndexWorkbenchSnapshot> Snapshot(long? table = null) => Business().SnapshotAsync(Json(new { configTableId = (table ?? Metadata.TableId).ToString() }));
    internal static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
    internal static JsonElement Payload(IndexWorkbenchSnapshot snapshot, params object[] indexes) => Json(new { configTableId = snapshot.ConfigTableId, revision = snapshot.Revision, indexes });
    internal object Index(string name = "yzp_idx_title", string id = "", string? column = null, string? columnId = null, string type = "NORMAL", string childId = "", string direction = "ASC", int enabled = 1) => new
    {
        id, indexName = name, indexType = type, isEnabled = enabled,
        columns = new[] { new { id = childId, configColumnId = columnId ?? Metadata.FirstColumnId.ToString(), columnName = column ?? "title", sortNo = 10, sortDirection = direction } }
    };
    internal async Task AddUnmanagedAsync()
    {
        var id = Ids.NextId();
        await Metadata.Store.InsertAsync("config_index", new Dictionary<string, object?> { ["id"] = id, ["config_table_id"] = Metadata.TableId, ["index_name"] = "ux_external_guard", ["index_type"] = "UNIQUE", ["is_enabled"] = 1, ["is_unique"] = 1, ["yz_project_id"] = Metadata.ProjectId });
        await Metadata.Store.InsertAsync("config_index_column", new Dictionary<string, object?> { ["id"] = Ids.NextId(), ["config_index_id"] = id, ["config_column_id"] = Metadata.FirstColumnId, ["column_name"] = "title", ["sort_no"] = 10, ["sort_direction"] = "ASC", ["yz_project_id"] = Metadata.ProjectId });
    }
    internal async Task<int> PhysicalTableCountAsync()
    {
        await using var connection = Metadata.Session.Provider.CreateConnection(Metadata.Options); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=@db AND table_name=@name";
        foreach (var (name, value) in new[] { ("db", Metadata.Options.ExpectedDatabase), ("name", "mt_metadata_" + Metadata.TableId) }) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    /// <summary>软删除记录也按独占项目清理，不触及任何既有项目或物理业务表。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var connection = Metadata.Session.Provider.CreateConnection(Metadata.Options); await connection.OpenAsync();
            foreach (var table in new[] { "config_index_column", "config_index" })
            { await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM " + Metadata.Session.Provider.QuoteIdentifier(table) + " WHERE yz_project_id=@project"; var parameter = command.CreateParameter(); parameter.ParameterName = "project"; parameter.Value = Metadata.ProjectId; command.Parameters.Add(parameter); await command.ExecuteNonQueryAsync(); }
        }
        finally { await Metadata.DisposeAsync(); }
    }
    internal sealed class FailingIds(int successfulCalls) : IIdGenerator
    { private int _count; public long NextId() => ++_count <= successfulCalls ? Ids.NextId() : throw new InvalidOperationException("测试模拟批次写入途中失败。"); }
}

public sealed class IndexWorkbenchIntegrationTests
{
    [MetadataIntegrationFact]
    public async Task CompleteLifecycleMapsIdsPreservesUnmanagedAndDoesNotExecuteDdl()
    {
        await using var f = await IndexWorkbenchFixture.CreateAsync(); await f.AddUnmanagedAsync(); var snapshot = await f.Snapshot();
        Assert.Empty(snapshot.Indexes); Assert.All(snapshot.ColumnOptions, column => Assert.NotEmpty(column.ConfigColumnId)); Assert.Equal(0, await f.PhysicalTableCountAsync());
        var saved = await f.Business().SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index(type: "UNIQUE", name: "yzp_uk_title", direction: "DESC")));
        var index = Assert.Single(saved.Indexes); Assert.True(long.Parse(index.Id) > 0); Assert.Equal("UNIQUE", index.IndexType); Assert.Equal("DESC", Assert.Single(index.Columns).SortDirection);
        var raw = Assert.Single(await f.Metadata.Store.ReadAsync("config_index", new Dictionary<string, object?> { ["id"] = long.Parse(index.Id) })); Assert.Equal(1, Convert.ToInt32(raw["is_unique"]));
        var disabled = await f.Business().SaveAsync(IndexWorkbenchFixture.Payload(saved, f.Index(id: index.Id, childId: index.Columns[0].Id, type: "UNIQUE", name: "yzp_uk_title", enabled: 0)));
        Assert.Equal(0, Assert.Single(disabled.Indexes).IsEnabled);
        var empty = await f.Business().SaveAsync(IndexWorkbenchFixture.Payload(disabled)); Assert.Empty(empty.Indexes);
        raw = Assert.Single(await f.Metadata.Store.ReadAsync("config_index", new Dictionary<string, object?> { ["config_table_id"] = f.Metadata.TableId })); Assert.Equal("ux_external_guard", SqlRowStore.Text(raw, "index_name"));
        Assert.Equal(0, await f.PhysicalTableCountAsync()); Assert.NotEqual(snapshot.Revision, saved.Revision);
    }

    [MetadataIntegrationFact]
    public async Task CrossTableIdsColumnIdsAndChildMovesRejectWholeBatch()
    {
        await using var f = await IndexWorkbenchFixture.CreateAsync();
        var other = await f.Snapshot(f.Metadata.OtherTableId); other = await f.Business().SaveAsync(IndexWorkbenchFixture.Payload(other, f.Index(columnId: f.Metadata.OtherColumnId.ToString())));
        var foreign = Assert.Single(other.Indexes); var current = await f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(current, f.Index("yzp_idx_valid"), f.Index(id: foreign.Id))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(current, f.Index(columnId: f.Metadata.OtherColumnId.ToString()))));
        Assert.Equal(current.Revision, (await f.Snapshot()).Revision);
        var two = await f.Business().SaveAsync(IndexWorkbenchFixture.Payload(current, f.Index("yzp_idx_first"), f.Index("yzp_idx_second")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(two, f.Index("yzp_idx_first", two.Indexes[0].Id, childId: two.Indexes[1].Columns[0].Id), two.Indexes[1])));
        Assert.Equal(two.Revision, (await f.Snapshot()).Revision);
    }

    [MetadataIntegrationFact]
    public async Task PhysicalColumnOwnershipAndSubmittedNameAreVerified()
    {
        await using var f = await IndexWorkbenchFixture.CreateAsync();
        await f.Metadata.Store.UpdateAsync("config_column", f.Metadata.SecondColumnId, new Dictionary<string, object?> { ["is_virtual_column"] = 1 });
        var snapshot = await f.Snapshot(); Assert.DoesNotContain(snapshot.ColumnOptions, column => column.ColumnName == "notes");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index(column: "notes", columnId: f.Metadata.SecondColumnId.ToString()))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index(column: "notes"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index(column: "id", columnId: ""))));
        // 空ID只能映射已声明列，不能按旧版系统列清单凭空制造物理字段。
        var saved = await f.Business().SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index(columnId: "")));
        Assert.Equal(f.Metadata.FirstColumnId.ToString(), Assert.Single(Assert.Single(saved.Indexes).Columns).ConfigColumnId);
    }

    [MetadataIntegrationFact]
    public async Task AuthorizationRevisionAndConcurrentWritersCannotOverrideCurrentProject()
    {
        await using var f = await IndexWorkbenchFixture.CreateAsync(); var snapshot = await f.Snapshot();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Business(administrator: false).SnapshotAsync(IndexWorkbenchFixture.Json(new { configTableId = f.Metadata.TableId.ToString() })));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Business(agent: true).SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Business(project: f.Metadata.Registry.Platform.YzProjectId).SnapshotAsync(IndexWorkbenchFixture.Json(new { configTableId = f.Metadata.TableId.ToString() })));
        using var secondSession = new DatabaseSession(f.Metadata.Options); var first = f.Business(); var second = f.Business(secondSession);
        async Task<bool> Attempt(IndexWorkbenchBusiness business, string name)
        { try { await business.SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index(name))); return true; } catch (MetadataRevisionConflictException) { return false; } }
        var attempts = await Task.WhenAll(Attempt(first, "yzp_idx_one"), Attempt(second, "yzp_idx_two")); Assert.Single(attempts, success => success);
        await Assert.ThrowsAsync<MetadataRevisionConflictException>(() => f.Business().SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index())));
        Assert.Single((await f.Snapshot()).Indexes);
    }

    [MetadataIntegrationFact]
    public async Task RuntimeFailureAfterFirstInsertRollsBackEntireAggregate()
    {
        await using var f = await IndexWorkbenchFixture.CreateAsync(); var snapshot = await f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Business(ids: new IndexWorkbenchFixture.FailingIds(1)).SaveAsync(IndexWorkbenchFixture.Payload(snapshot, f.Index())));
        Assert.Equal(snapshot.Revision, (await f.Snapshot()).Revision); Assert.Empty((await f.Snapshot()).Indexes);
        Assert.Empty(await f.Metadata.Store.ReadAsync("config_index", new Dictionary<string, object?> { ["config_table_id"] = f.Metadata.TableId }));
    }
}
