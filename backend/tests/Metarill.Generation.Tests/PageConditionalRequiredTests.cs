using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed class PageConditionalRequiredTests
{
    /// <summary>实际生成的新增服务按详情可见的 TIME 值决定必填，校验必须先于写入。</summary>
    [Theory]
    [InlineData(true, "12:34:56.120000", "12:34:56.12")]
    [InlineData(false, "-30:01:02.123456", "-30:01:02.123456")]
    public async Task Time_rules_require_visible_fields_before_generated_create(bool clock, string input, string wire)
    {
        var catalog = GeneratorTests.Catalog();
        catalog.ConfigColumns.Add(new()
        {
            Id = "17", ConfigTableId = "1", ColumnName = "opens_at", LogicalType = "time",
            ColumnType = "time", ControlKind = clock ? "time" : ""
        });
        catalog.ConfigPageColumns.Add(new() { Id = "117", ConfigPageId = "101", ConfigColumnId = "17", IsEdit = 1 });
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13").IsRequired = 1;
        catalog.ConfigColumns.Single(column => column.Id == "13").Extra["visibleCondition"] =
            JsonSerializer.SerializeToElement(new { conditions = new[] { new { field = "opensAt", value = wire } } });
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-time-required");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.GuardContract");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run = new GeneratedMutationGuardRun(assembly, new Metarill.Data.MySqlDatabaseProvider().ValidateTimeValue);
            var service = run.Service([]);
            var request = run.Request(Metarill.Core.Persistence.RecordMutationOperation.Create,
                JsonSerializer.Serialize(new { name = "缺少备注", opensAt = input }));
            await Assert.ThrowsAsync<Metarill.Core.Contracts.BusinessRuleException>(() =>
                run.Invoke(service, Metarill.Core.Persistence.RecordMutationOperation.Create, request));
            Assert.DoesNotContain("write:Insert", run.Trace);

            var complete = run.Request(Metarill.Core.Persistence.RecordMutationOperation.Create,
                JsonSerializer.Serialize(new { name = "带备注", opensAt = input, remark = "已填" }));
            await run.Invoke(service, Metarill.Core.Persistence.RecordMutationOperation.Create, complete);
            Assert.Contains("write:Insert", run.Trace);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>显示条件读取最终实体的真实字段，JSON文本与对象一致，数据库非空仍独立约束。</summary>
    [Theory]
    [InlineData("approvalPayload")]
    [InlineData("approval_payload")]
    public async Task Required_field_uses_original_visibility_rule_without_discarding_column_nullability(string fieldName)
    {
        var catalog=GeneratorTests.Catalog();
        catalog.ConfigColumns.Add(new(){Id="17",ConfigTableId="1",ColumnName="approval_payload",LogicalType="json"});
        catalog.ConfigPageColumns.Add(new(){Id="117",ConfigPageId="101",ConfigColumnId="17",IsEdit=1});
        var remark=catalog.ConfigColumns.Single(column=>column.Id=="13");
        remark.IsDbNotNull=1;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="13").IsRequired=1;
        remark.Extra["visibleCondition"]=JsonSerializer.SerializeToElement("""
{"logic":"AND","conditions":[{"field":"FIELD.flags.enabled","operator":"eq","value":true}]}
""".Replace("FIELD",fieldName,StringComparison.Ordinal));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-visible-required");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.RequiredVisibility");
            var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/CustomerService.g.cs"));
            Assert.Contains("entity.Remark is null || (RequiresVisible101_13(entity)",code);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entityType=assembly.GetType("Example.RequiredVisibility.Generated.Entities.Customer",true)!;
            var serviceType=assembly.GetType("Example.RequiredVisibility.Generated.Services.CustomerService",true)!;
            var visible=serviceType.GetMethod("RequiresVisible101_13",BindingFlags.Static|BindingFlags.NonPublic)!;
            var entity=Activator.CreateInstance(entityType)!;
            var payload=entityType.GetProperty("ApprovalPayload")!;
            payload.SetValue(entity,JsonSerializer.Serialize(new{flags=new{enabled=true}}));
            Assert.True((bool)visible.Invoke(null,[entity])!);
            payload.SetValue(entity,"{\"flags\":{\"enabled\":false}}");
            Assert.False((bool)visible.Invoke(null,[entity])!);
            payload.SetValue(entity,"{\"flags\":{\"enabled\":true}}");
            Assert.True((bool)visible.Invoke(null,[entity])!);
            payload.SetValue(entity,"invalid");
            Assert.False((bool)visible.Invoke(null,[entity])!);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
