using System.Text.Json;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Data.Tests;

public sealed class SchemaJournalTests
{
    [Fact]
    public async Task Journal_persists_complete_plan_and_result_without_connection_options()
    {
        var directory = Path.Combine(Path.GetTempPath(), "metarill-schema-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "history.jsonl");
        try
        {
            var journal = new FileSchemaChangeJournal(path);
            var plan = new SchemaChangePlan("plan", "mysql", "test_db", "before", "desired", [new("createTable", "record", null, "CREATE TABLE `record` (`id` bigint PRIMARY KEY)")]);
            var result = new SchemaApplyResult("plan", "test_db", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true, 1, null);
            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => journal.AppendAsync(new SchemaAuditEntry(plan, result))));
            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(5, lines.Length);
            Assert.All(lines, line =>
            {
                var saved = JsonSerializer.Deserialize<SchemaAuditEntry>(line)!;
                Assert.Equal(1, saved.Result.AppliedStatements);
                Assert.Equal(plan.Changes[0].Sql, saved.Plan.Changes[0].Sql);
                Assert.DoesNotContain("ConnectionString", line);
                Assert.DoesNotContain("Password", line);
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
