using System.Globalization;
using System.Net;
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

public sealed partial class DoublePersistenceNativeMySqlTests
{
    /// <summary>DB默认必须来自缺列INSERT及实际反查，不能只由生成实体默认值证明。</summary>
    private static async Task VerifyPhysicalDefaults(DatabaseSession session, MetadataCatalog catalog, SqlRowStore store)
    {
        var upper = BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.UpperBits); var lower = BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.LowerBits);
        var schema = new SchemaService(session); var inspected = await schema.InspectForImportAsync(catalog.ConfigTables.Select(table => table.TableName).ToArray()); Assert.Empty(inspected.Issues);
        var draft = SchemaImportDraftCompiler.Compile(inspected, "54"); Assert.True(draft.ColumnsAndIndexesRepresentable, string.Join(";", draft.Issues));
        var compiled = MySqlSchemaPlanner.Compile(draft.LogicalMetadata, session.Options.ExpectedDatabase); Assert.Empty(MySqlSchemaPlanner.Compare(inspected.Snapshot.Schema, compiled)); Assert.Empty(MySqlSchemaPlanner.Compare(compiled, inspected.Snapshot.Schema));
        foreach (var (table, field, expected) in new[] { (Source, "measurement", upper), (Source, "measurement_previous", lower), (Party, "amount", upper), (Party, "previous_amount", lower) })
        {
            var physical = inspected.Snapshot.Schema.Tables.Single(value => value.Name == table).Columns.Single(value => value.Name == field);
            Assert.Equal("double", physical.StoreType); Assert.Equal(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(double.Parse(physical.DefaultSql!, CultureInfo.InvariantCulture)));
            var logicalTable = draft.LogicalMetadata.ConfigTables.Single(value => value.TableName == table);
            var logical = draft.LogicalMetadata.ConfigColumns.Single(value => value.ConfigTableId == logicalTable.Id && value.ColumnName == field); Assert.Equal("double", logical.LogicalType); Assert.Equal(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(double.Parse(logical.DefaultValue!, CultureInfo.InvariantCulture)));
        }
        await session.InTransactionAsync(async () =>
        {
            await store.InsertAsync(Group, Row("id", 71L, "yz_project_id", Project, "name", "父甲")); await store.InsertAsync(Group, Row("id", 72L, "yz_project_id", Project, "name", "父乙"));
            await store.InsertAsync(Party, Row("id", First - 2, "yz_project_id", Project, "name", "raw-default"));
            await store.InsertAsync(Source, Row("id", First - 1, "yz_project_id", Project, "group_id", 71L, "name", "raw-default")); return true;
        });
        var party = Assert.Single(await store.ReadAsync(Party, Row("id", First - 2))); Bits(upper, party["amount"]); Bits(lower, party["previous_amount"]);
        var source = Assert.Single(await store.ReadAsync(Source, Row("id", First - 1))); Bits(upper, source["measurement"]); Bits(lower, source["measurement_previous"]); Assert.False(source.ContainsKey("customer_amount"));
    }

    /// <summary>普通及非冗余实体、原HTTP、真实SQL、原导入导出和事务故障共用一套有限位型参照。</summary>
    private static async Task Exercise(HttpClient client, DatabaseSession session, SqlRowStore store, MetadataCatalog catalog, SqlRowStore security, IReadOnlyDictionary<string, long> pageGrants, string hooks, string evidence)
    {
        var upper = BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.UpperBits); var lower = BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.LowerBits);
        var created = await Post(client, PartyRoute + "add", new { name = "ordinary-default" }); var createdId = created.GetProperty("id").GetString()!;
        Bits(upper, created.GetProperty("amount").GetDouble()); Bits(lower, created.GetProperty("previousAmount").GetDouble());
        var changed = await Post(client, PartyRoute + "update", new { id = createdId, amount = lower }); Bits(lower, changed.GetProperty("amount").GetDouble());
        var kept = await Post(client, PartyRoute + "update", new { id = createdId, notes = "省略测量字段" }); Bits(lower, kept.GetProperty("amount").GetDouble());
        Assert.Equal(JsonValueKind.Null, (await Post(client, PartyRoute + "update", new { id = createdId, amount = (double?)null })).GetProperty("amount").ValueKind);
        await Post(client, PartyRoute + "update", new { id = createdId, amount = upper });
        var vectors = Vectors();
        await session.InTransactionAsync(async () =>
        {
            for (var index = 0; index < vectors.Count; index++) await store.InsertAsync(Party, Row("id", First + index, "yz_project_id", Project, "name", "matrix-" + index, "notes", "数值矩阵", "amount", vectors[index].Input, "exact_amount", 9007199254740993.123456789m));
            await store.InsertAsync(Party, Row("id", First + 200, "yz_project_id", 55L, "name", "matrix-foreign", "amount", upper));
            await store.InsertAsync(Party, Row("id", First + 201, "yz_project_id", Project, "name", "matrix-deleted", "amount", upper, "delete_no", new DateTime(2026, 1, 1))); return true;
        });
        foreach (var (vector, index) in vectors.Select((value, index) => (value, index))) { var row = Assert.Single(await store.ReadAsync(Party, Row("id", First + index))); Bits(vector.Expected, row["amount"]); Assert.Equal(9007199254740993.123456789m, Assert.IsType<decimal>(row["exact_amount"])); }
        var page = await Send(client, HttpMethod.Post, PartyRoute + "page", Request("matrix-")); var records = page.GetProperty("data").GetProperty("records").EnumerateArray().ToArray(); Assert.Equal(vectors.Count, Count(page.GetProperty("data").GetProperty("total")));
        for (var index = 0; index < vectors.Count; index++) { var row = Assert.Single(records, row => row.GetProperty("name").GetString() == "matrix-" + index); Bits(vectors[index].Expected, row.GetProperty("amount").GetDouble()); Assert.Equal(Id(First + index), row.GetProperty("id").GetString()); Assert.Equal(9007199254740993.123456789m, row.GetProperty("exactAmount").GetDecimal()); }
        foreach (var value in new[] { upper, lower, double.Epsilon, double.MaxValue })
        {
            var filter = Request("matrix-"); filter.Filters["amount"] = JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
            var found = await Send(client, HttpMethod.Post, PartyRoute + "page", filter); var expected = vectors.Count(vector => BitConverter.DoubleToUInt64Bits(vector.Expected) == BitConverter.DoubleToUInt64Bits(value)); Assert.Equal(expected, Count(found.GetProperty("data").GetProperty("total"))); Assert.All(found.GetProperty("data").GetProperty("records").EnumerateArray(), row => Bits(value, row.GetProperty("amount").GetDouble()));
        }
        var source = await Post(client, SourceRoute + "add?parentId=71", new { name = "nonred-default", customerId = createdId, status = 1 }); var sourceId = source.GetProperty("id").GetString()!;
        Bits(upper, source.GetProperty("measurement").GetDouble()); Bits(lower, source.GetProperty("measurementPrevious").GetDouble()); Bits(upper, source.GetProperty("customerAmount").GetDouble());
        await Post(client, PartyRoute + "update", new { id = createdId, amount = lower }); var detail = (await Send(client, HttpMethod.Get, SourceRoute + "detail/" + sourceId + "?parentId=71")).GetProperty("data"); Bits(lower, detail.GetProperty("customerAmount").GetDouble()); Bits(upper, detail.GetProperty("measurement").GetDouble());
        var scoped = Request("nonred-default", "71"); scoped.Filters["customerAmount"] = JsonSerializer.SerializeToElement(lower, JsonDefaults.Options); var selected = await Send(client, HttpMethod.Post, SourceRoute + "page", scoped); Assert.Equal(1, Count(selected.GetProperty("data").GetProperty("total")));
        scoped.Filters["customerAmount"] = JsonSerializer.SerializeToElement(upper, JsonDefaults.Options); Assert.Equal(0, Count((await Send(client, HttpMethod.Post, SourceRoute + "page", scoped)).GetProperty("data").GetProperty("total")));
        Assert.Equal(0, Count((await Send(client, HttpMethod.Post, SourceRoute + "page", Request("nonred-default", "72"))).GetProperty("data").GetProperty("total")));
        var sourcePhysical = Assert.Single(await store.ReadAsync(Source, Row("id", long.Parse(sourceId, CultureInfo.InvariantCulture)))); Assert.False(sourcePhysical.ContainsKey("customer_amount"));
        await Post(client, SourceRoute + "update?parentId=71", new { id = sourceId, measurement = lower }); Bits(lower, (await Send(client, HttpMethod.Get, SourceRoute + "detail/" + sourceId + "?parentId=71")).GetProperty("data").GetProperty("measurement").GetDouble());
        await Post(client, SourceRoute + "update?parentId=71", new { id = sourceId, measurement = upper });
        var copy = await Post(client, SourceRoute + "copy/" + sourceId + "?parentId=71", new { }); Bits(upper, copy.GetProperty("measurement").GetDouble()); await Send(client, HttpMethod.Delete, SourceRoute.TrimEnd('/') + "?parentId=71", new { id = copy.GetProperty("id").GetString() });
        foreach (var invalid in new[] { "1e309", "1e-999", "NaN" }) { var before = await Snapshot(); await Send(client, HttpMethod.Post, PartyRoute + "add", new { name = "不得落库", amount = invalid }, HttpStatusCode.BadRequest); Assert.Equal(before, await Snapshot()); }
        await VerifySums();
        foreach (var (route, field, parent) in new[] { (PartyRoute, "amount", (string?)null), (SourceRoute, "测量值", "71") })
        {
            var before = await Snapshot(); var pageId = parent is null ? "201" : "101";
            await Send(client, HttpMethod.Post, route + "importDatas" + pageId + (parent is null ? "" : "?parentId=71"), new { parentId = parent, requestJson = new[] { Row(parent is null ? "name" : "记录名", "数值回滚首行", field, upper), Row(parent is null ? "name" : "记录名", "数值后置拒绝", field, lower) } }, HttpStatusCode.InternalServerError);
            Assert.Equal(before, await Snapshot());
        }
        var observed = File.ReadLines(hooks).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray(); Assert.Equal(2, observed.Length); Assert.Equal(new[] { Party, Source }.Order(), observed.Select(value => value.GetProperty("table").GetString()).Order()); Assert.All(observed, row => { Assert.True(row.GetProperty("insideTransaction").GetBoolean()); Assert.Equal(DoublePersistenceNativePreparation.UpperBits.ToString("X16", CultureInfo.InvariantCulture), row.GetProperty("bits").GetString()); });
        await VerifyExport(client, PartyRoute, Request("ordinary-default"), "amount", "相邻金额", upper, lower, evidence);
        await VerifyExport(client, SourceRoute, Request("nonred-default", "71"), "测量值", "相邻值", upper, lower, evidence);
        foreach (var (pageId, route, parent) in new[] { ("201", PartyRoute, (string?)null), ("101", SourceRoute, "71") })
        {
            var before = await Snapshot(); await security.UpdateAsync("yz_role_permission", pageGrants[pageId], Row("permission_values", "[]"));
            try { await Send(client, HttpMethod.Post, route + "importDatas" + pageId, new { parentId = parent, requestJson = Array.Empty<object>() }, HttpStatusCode.Forbidden); await Send(client, HttpMethod.Post, route + "initExport", Request("", parent), HttpStatusCode.Forbidden); Assert.Equal(before, await Snapshot()); }
            finally { await security.UpdateAsync("yz_role_permission", pageGrants[pageId], Row("permission_values", "[\"is_all\"]")); }
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "numeric-verified.json"), JsonSerializer.Serialize(new { matrixRows = vectors.Count, missingColumnDatabaseDefaults = true, reverseDraftRoundtrip = true, typedStoreBits = true, generatedCrud = true, nonredundantDerivedFilter = true, exactDecimalAndId = true, maxOverflowRejected = true, cancellationSum = true, subnormalSum = true, realFirstDmlRollback = true, originalXlsx = true, permissionRevocation = true, databaseCleanupByLease = true }));

        async Task<string> Snapshot()
        {
            var rows = new List<object>(); foreach (var table in catalog.ConfigTables) rows.Add(new { table.TableName, Rows = (await store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row => SqlRowStore.Id(row)) });
            return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows, JsonDefaults.Options)));
        }
        async Task VerifySums()
        {
            var targets = new List<(string Label, double[] Values, double? Sum)> { ("sum-cancel", [double.MaxValue, -double.MaxValue, 1d], 1d), ("sum-subnormal", [double.Epsilon, double.Epsilon], 2 * double.Epsilon), ("sum-overflow", [double.MaxValue, double.MaxValue], null) };
            var id = First + 300;
            foreach (var (label, values, expected) in targets)
            {
                foreach (var value in values) { id++; await store.InsertAsync(Party, Row("id", id, "yz_project_id", Project, "name", label, "amount", value)); await store.InsertAsync(Source, Row("id", id + 100, "yz_project_id", Project, "group_id", 71L, "name", "r0-" + label, "customer_id", id, "measurement", value)); }
                foreach (var (route, name, parent, field) in new[] { (PartyRoute, label, (string?)null, "amount"), (SourceRoute, "r0-" + label, "71", "customerAmount") })
                {
                    var request = Request(name, parent); request.PageSize = 1;
                    if (expected is null) await Send(client, HttpMethod.Post, route + "page", request, HttpStatusCode.InternalServerError);
                    else { var result = await Send(client, HttpMethod.Post, route + "page", request); Bits(expected.Value, result.GetProperty("map").GetProperty("statistics").GetProperty(field).GetProperty("total").GetDouble()); Assert.Equal(values.Length, Count(result.GetProperty("data").GetProperty("total"))); }
                }
            }
        }
    }

    /// <summary>持久矩阵包含极值和固定随机有限值；正常实库零按数值比较，不要求存储负零符号。</summary>
    private static IReadOnlyList<(object Input, double Expected)> Vectors()
    {
        var values = new List<double> { 0d, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue, BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.UpperBits), BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.LowerBits), -BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.UpperBits), -BitConverter.UInt64BitsToDouble(DoublePersistenceNativePreparation.LowerBits), BitConverter.UInt64BitsToDouble(0x0010000000000000), BitConverter.UInt64BitsToDouble(0x000fffffffffffff), 0.1d };
        var random = new Random(20261104); for (var index = 0; index < 64; index++) values.Add(Math.ScaleB(random.NextDouble() * (index % 2 == 0 ? 1 : -1), random.Next(-900, 901)));
        var result = values.DistinctBy(BitConverter.DoubleToUInt64Bits).Select(value => ((object)value, value)).ToList(); result.Add((0.1f, (double)0.1f)); return result;
    }
    /// <summary>正式宿主沿原StringLong转换器输出分页总数；测试不能误按数字Token读取。</summary>
    private static long Count(JsonElement value) { Assert.Equal(JsonValueKind.String, value.ValueKind); return long.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture); }
    /// <summary>只构造原PageRequest，筛选与父范围仍由实际生成Service处理。</summary>
    private static PageRequest Request(string name, string? parent = null) => new() { ParentId = parent, PageSize = 100, Filters = string.IsNullOrEmpty(name) ? [] : new() { ["name"] = JsonSerializer.SerializeToElement(name) } };
}