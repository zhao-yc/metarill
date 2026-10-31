using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>使用真实元数据反例，不能把任意可编译页面范围当作可传递的安全归属值。</summary>
public sealed class GeneratedMutationGuardScopeTests
{
    [Theory]
    [InlineData("password", "string")]
    [InlineData("db_password", "string")]
    [InlineData("pwd", "string")]
    [InlineData("token", "string")]
    [InlineData("name", "int64")]
    [InlineData("description", "string")]
    [InlineData("owner_id", "string")]
    [InlineData("access_token", "int64")]
    public async Task Unproven_or_sensitive_parent_scope_keeps_original_query_without_exposing_values(string column, string logicalType)
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-guard-scope"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(column, logicalType), directory, "Example.Scope");
            var source = await File.ReadAllTextAsync(Path.Combine(directory, "Services/CustomerService.g.cs"));
            var property = string.Concat(column.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            Assert.Contains("query = query.Where(entity => entity." + property + " == ", source);
            Assert.DoesNotContain("[\"" + column + "\"]", source.Split("MutationScopeValues(global::Example.Scope.Generated.Entities.Customer entity)")[1].Split("private static readonly")[0]);
            Assert.DoesNotContain(source.Split('\n'), line => line.Contains("mutationScopeValues[\"" + column + "\"]", StringComparison.Ordinal));
            Assert.Contains("parent1.ForUpdate().FirstAsync", source);

        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    [Fact]
    public async Task Stable_parent_id_and_fixed_project_scope_keep_sql_and_compile_the_actual_generated_batch()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-guard-scope"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog("owner_id", "int64"), Path.Combine(directory, "generated"), "Example.Scope");
            var source = await File.ReadAllTextAsync(Path.Combine(directory, "generated/Services/CustomerService.g.cs"));
            Assert.Contains("query = query.Where(entity => entity.OwnerId == long.Parse(parentId, CultureInfo.InvariantCulture));", source);
            Assert.Contains("[\"owner_id\"] = entity.OwnerId", source);
            Assert.Contains("mutationScopeValues[\"owner_id\"] = long.Parse(parentId, CultureInfo.InvariantCulture)", source);
            Assert.Contains("[\"yz_project_id\"] = entity.YzProjectId", source);
            Assert.Contains("parent1.ForUpdate().FirstAsync", source);
            var import = source.Split("public Task<string> Import101Async")[1].Split("var rowNumber")[0];
            Assert.Contains("mutationScopeValues[\"owner_id\"] = long.Parse(request.ParentId, CultureInfo.InvariantCulture)", import);
            Assert.True(import.IndexOf("PrepareMutationAsync", StringComparison.Ordinal) < import.IndexOf("ValidateParents101Async", StringComparison.Ordinal));
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    [Fact]
    public async Task Actual_generated_service_does_not_publish_password_parent_values_to_either_guard_phase()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-guard-scope"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog("pwd", "string"), Path.Combine(directory, "generated"), "Example.GuardContract");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run = new GeneratedMutationGuardRun(assembly) { ParentId = "1" }; var guard = new ScopeGuard();
            await run.Invoke(run.Service([guard]), RecordMutationOperation.Create, run.Request(RecordMutationOperation.Create, "{\"name\":\"合法原页面\",\"pwd\":\"客户端秘密不传递\"}"));
            var preparation = Assert.Single(guard.Preparations); var validation = Assert.Single(guard.Validations);
            Assert.False(preparation.ScopeValues.ContainsKey("pwd")); Assert.False(validation.ScopeValues.ContainsKey("pwd"));
            Assert.False(validation.OriginalScopeValues.ContainsKey("pwd"));
            Assert.Equal(100L, preparation.ScopeValues["yz_project_id"]); Assert.Equal(100L, validation.ScopeValues["yz_project_id"]);
            Assert.Contains("pwd", validation.SubmittedFields); // 只传提交键名，不传其值。
            Assert.Contains("write:Insert", run.Trace); Assert.Equal("commit", run.Trace[^1]);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>只捕获真实生成服务传递的上下文，不替代任何权限决定。</summary>
    private sealed class ScopeGuard : IRecordMutationGuard
    {
        public List<RecordMutationPreparation> Preparations { get; } = []; public List<RecordMutationValidation> Validations { get; } = [];
        public Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken token = default) { Preparations.Add(preparation); return Task.CompletedTask; }
        public Task BeforeWriteAsync(RecordMutationValidation validation, CancellationToken token = default) { Validations.Add(validation); return Task.CompletedTask; }
    }

    /// <summary>显式合法外键和已发布父表使旧parent编译真实可达，反例不是缺少父定义造成的假失败。</summary>
    private static MetadataCatalog Catalog(string columnName, string logicalType)
    {
        var catalog = GeneratorTests.Catalog();
        foreach (var column in catalog.ConfigColumns.Where(column => column.LogicalType == "id")) column.LogicalType = "int64";
        catalog.ConfigTables.Add(new() { Id = "2", TableName = "owner" });
        catalog.ConfigColumns.Add(new() { Id = "21", ConfigTableId = "2", ColumnName = "id", LogicalType = "int64", IsDbNotNull = 1 });
        catalog.ConfigColumns.Add(new() { Id = "22", ConfigTableId = "2", ColumnName = "yz_project_id", LogicalType = "int64" });
        var scope = catalog.ConfigColumns.SingleOrDefault(column => column.ColumnName == columnName && column.ConfigTableId == "1");
        if (scope is null)
        {
            scope = new() { Id = "16", ConfigTableId = "1", ColumnName = columnName, LogicalType = logicalType };
            catalog.ConfigColumns.Add(scope);
            catalog.ConfigPageColumns.Add(new() { Id = "116", ConfigPageId = "101", ConfigColumnId = "16", IsEdit = 1, IsShow = 1 });
        }
        scope.LogicalType = logicalType; scope.LinkConfigTableId = "2";
        catalog.ConfigPages.Single().IsImport = 1;
        catalog.ConfigPages.Single().WhereSql += " AND " + columnName + "=@parent_id";
        return catalog;
    }
}
