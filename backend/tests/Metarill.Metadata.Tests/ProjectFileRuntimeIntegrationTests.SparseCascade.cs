using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed partial class ProjectFileRuntimeIntegrationTests
{
    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Split_database_rolls_back_sparse_cascade_and_protected_file_together() =>
        VerifySparseCascadeAndFilesAsync(shareDatabase: false);

    [ProjectFileIntegrationFact(freshOnly: true)]
    public Task Shared_database_rolls_back_sparse_cascade_and_protected_file_together() =>
        VerifySparseCascadeAndFilesAsync(shareDatabase: true);

    /// <summary>真实业务槽内改祖先名称、替换稀疏节点和附件。独立连接核对事务可见性、回滚及提交。</summary>
    private static async Task VerifySparseCascadeAndFilesAsync(bool shareDatabase)
    {
        await using var f = await ProjectFileDatabaseFixture.CreateAsync(shareDatabase: shareDatabase, sparseCascade: true);
        AssertDatabaseTopology(f, shareDatabase);
        var catalog = f.Registry.Demo;
        var actor = new RuntimeActor(8801, "级联及文件事务验收", true, catalog.YzProjectId, "");
        var project = long.Parse(actor.ProjectId);
        var authorization = new TestFileAuthorization(actor);
        var runtime = f.Runtimes.Resolve(actor.ProjectId);
        var files = new ProjectFileLibraryResolver(f.Runtimes, authorization, f.Ids).RequireCurrent();
        var observer = new ProjectFileReferenceMutationObserver(f.Runtimes, authorization, f.Ids);
        var oldFile = await f.ReadyAsync(files, "sparse-old.txt", f.ProtectedColumn);
        var newFile = await f.ReadyAsync(files, "sparse-new.txt", f.ProtectedColumn);
        var source = f.Ids.NextId();
        var sourceKey = Key("id", source);
        var refKey = Key("record_id", source);
        var session = f.Sessions.Business;
        var original = Enumerable.Range(0, 3).Select(_ => f.Ids.NextId()).ToArray();
        var replacement = Enumerable.Range(0, 2).Select(_ => f.Ids.NextId()).ToArray();

        var sourceTable = catalog.ConfigTables.Single(table => table.TableName == f.TableName);
        var selection = CrossTableSparseSelection.Compile(sourceTable, f.CascadeColumnId,
            catalog.ConfigColumns.Where(column => column.ConfigTableId == sourceTable.Id));
        var readPlan = NonRedundantReads.CompileWithCrossTables(catalog, sourceTable.Id,
            ["id", "country_id", "country_name", "area_id", "area_name", "place_id", "place_name", "attachments"],
            [new NonRedundantCrossTableScope(f.CascadeColumnId, f.CascadePageIds.Select(id => new NonRedundantCrossTableTargetScope(id)).ToArray())]);
        var schema = await new SchemaService(session).InspectAsync(catalog);
        var physicalColumns = schema.Tables.Single(table => table.Name == f.TableName).Columns.Select(column => column.Name).ToArray();
        Assert.All(selection.Slots, slot => Assert.Contains(slot.ColumnName, physicalColumns));
        Assert.DoesNotContain(physicalColumns, name => name is "country_name" or "area_name" or "place_name");

        await session.InTransactionAsync(async () =>
        {
            await SeedBranchAsync(original, ["原国家", "原地区", "原城市"]);
            await SeedBranchAsync(replacement, ["新国家旧名", "新地区"]);
            await f.BusinessStore.InsertAsync(f.TableName, new Dictionary<string, object?>(selection.PackValidatedPath(original))
            {
                ["id"] = source, ["yz_project_id"] = project, ["attachments"] = Attachment(oldFile.FileId)
            });
            await BindAsync(oldFile.FileId, inserting: true);
            return true;
        });

        using var witnessSession = new DatabaseSession(session.Options);
        var witness = new SqlRowStore(witnessSession, f.Runtimes.ComposePhysical(catalog));
        await AssertStateAsync(witness, witnessSession, original, "原国家", "原地区", "原城市", oldFile.FileId);
        var before = await SnapshotAsync();
        var tail = new InvalidOperationException("级联与附件写入后故障");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.InTransactionAsync<bool>(async () =>
        {
            await ChangeAsync();
            await AssertStateAsync(witness, witnessSession, original, "原国家", "原地区", "原城市", oldFile.FileId);
            throw tail;
        }));
        Assert.Same(tail, failure);
        Assert.Equal(before, await SnapshotAsync());
        await AssertStateAsync(witness, witnessSession, original, "原国家", "原地区", "原城市", oldFile.FileId);

        await session.InTransactionAsync(async () => { await ChangeAsync(); return true; });
        await AssertStateAsync(witness, witnessSession, replacement, "新国家已改名", "新地区", null, newFile.FileId);
        var platformFiles = new SqlRowStore(f.Sessions.Platform, f.Runtimes.ComposePhysical(catalog));
        Assert.Equal(shareDatabase ? 1 : 0, (await platformFiles.ReadAsync("yz_file_ref", refKey)).Count);
        Assert.True((await files.DeleteAsync(oldFile.FileId)).StorageRemoved);
        await Assert.ThrowsAsync<BusinessRuleException>(() => files.DeleteAsync(newFile.FileId));

        async Task SeedBranchAsync(IReadOnlyList<long> ids, IReadOnlyList<string> names)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                var row = new Dictionary<string, object?> { ["id"] = ids[index], ["yz_project_id"] = project, ["name"] = names[index] };
                if (index > 0) row.Add(f.CascadeTables[index - 1] + "_id", ids[index - 1]);
                await f.BusinessStore.InsertAsync(f.CascadeTables[index], row);
            }
        }

        async Task ChangeAsync()
        {
            Assert.Equal(1, await f.BusinessStore.UpdateAsync(f.CascadeTables[0], replacement[0],
                new Dictionary<string, object?> { ["name"] = "新国家已改名" }));
            var patch = new Dictionary<string, object?>(selection.PackValidatedPath(replacement)) { ["attachments"] = Attachment(newFile.FileId) };
            Assert.Equal(1, await f.BusinessStore.UpdateAsync(f.TableName, source, patch));
            await BindAsync(newFile.FileId);
            await AssertStateAsync(f.BusinessStore, session, replacement, "新国家已改名", "新地区", null, newFile.FileId, runtime.Store);
        }

        Task BindAsync(long id, bool inserting = false) => observer.BeforeCommitAsync(new(f.PageId, f.TableName, source, actor.ProjectId,
            new Dictionary<string, object?> { ["attachments"] = Attachment(id) }, false, session.CurrentTransaction!, inserting));

        async Task AssertStateAsync(SqlRowStore store, DatabaseSession readSession, IReadOnlyList<long> path,
            string country, string area, string? place, long file, SqlRowStore? references = null)
        {
            var raw = Assert.Single(await store.ReadAsync(f.TableName, sourceKey));
            var slots = selection.Slots.ToDictionary(slot => slot.ColumnName, slot => raw[slot.ColumnName]);
            Assert.Equal(new CrossTableSelectedNode(path.Count - 1, path[^1]), selection.ReadStored(slots));
            Assert.Equal(Attachment(file), SqlRowStore.Text(raw, "attachments"));
            var row = Assert.Single(await new NonRedundantReader(readSession).RowsAsync(readPlan,
                [new("id", FixedReadComparison.Equal, source)], [], 1, projectId: actor.ProjectId));
            Assert.Equal(path[0], NonRedundantReads.Value<long>(row, "country_id"));
            Assert.Equal(path[1], NonRedundantReads.Value<long>(row, "area_id"));
            Assert.Equal(path.Count == 3 ? path[2] : (long?)null, NonRedundantReads.Value<long?>(row, "place_id"));
            Assert.Equal(country, NonRedundantReads.Value<string>(row, "country_name"));
            Assert.Equal(area, NonRedundantReads.Value<string>(row, "area_name"));
            Assert.Equal(place, NonRedundantReads.Value<string>(row, "place_name"));
            Assert.Equal(Attachment(file), NonRedundantReads.Value<string>(row, "attachments"));
            var reference = Assert.Single(await (references ?? store).ReadAsync("yz_file_ref", refKey));
            Assert.Equal(file, SqlRowStore.Id(reference, "file_id"));
            Assert.Equal(project, SqlRowStore.Id(reference, "yz_project_id"));
        }

        async Task<string> SnapshotAsync() => JsonSerializer.Serialize(new
        {
            target = await witness.ReadIncludingDeletedAsync(f.CascadeTables[0], Key("id", replacement[0])),
            source = await witness.ReadIncludingDeletedAsync(f.TableName, sourceKey),
            references = (await witness.ReadIncludingDeletedAsync("yz_file_ref", refKey)).OrderBy(row => SqlRowStore.Id(row))
        });

        static Dictionary<string, object?> Key(string name, long value) => new() { [name] = value };
        static string Attachment(long id) => JsonSerializer.Serialize(new[] { id.ToString() });
    }
}
