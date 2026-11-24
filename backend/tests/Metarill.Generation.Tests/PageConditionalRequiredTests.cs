using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
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

    /// <summary>实际生成的新增、更新、复制和导入使用最终可见的原附件数组。</summary>
    [Theory]
    [InlineData(true, "galleryImages")]
    [InlineData(false, "gallery_images")]
    public async Task Array_blank_rules_follow_original_form_semantics(bool whenBlank, string field)
    {
        const string photos = """[{"url":"/api/admin/sys/file-library/public-content/101","name":"样本.png"}]""";
        var catalog = GeneratorTests.Catalog();
        var page = catalog.ConfigPages[0];
        page.IsNew = page.IsCopy = page.IsImport = 1;
        page.Extra["isUpdate"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Add(new()
        {
            Id="17", ConfigTableId="1", ColumnName="gallery_images", Name="附件", LogicalType="string", ControlKind="MultiImage"
        });
        catalog.ConfigPageColumns.Add(new() { Id="117", ConfigPageId=page.Id, ConfigColumnId="17", IsEdit=1, IsShow=1 });
        catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId=="13").IsRequired=1;
        catalog.ConfigColumns.Single(item=>item.Id=="13").Extra["visibleCondition"]=JsonSerializer.SerializeToElement(new
        {
            groups = new[] { new { conditions = new[] { new { field, @operator = whenBlank ? "isBlank" : "isNotBlank" } } } }
        });

        var required = whenBlank ? "[]" : photos;
        var optional = whenBlank ? photos : "[]";
        var boundary = Path.Combine(Path.GetTempPath(),"metarill-required-array");
        var directory = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.GuardContract");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var run = new GeneratedMutationGuardRun(assembly);
            var service = run.Service([]);

            await Assert.ThrowsAsync<BusinessRuleException>(()=>Run(RecordMutationOperation.Create,
                """{"name":"附件记录","galleryImages":VALUE}""".Replace("VALUE",required,StringComparison.Ordinal)));
            Assert.DoesNotContain("write:Insert",run.Trace);
            await Run(RecordMutationOperation.Create,"""{"name":"附件记录","galleryImages":VALUE}""".Replace("VALUE",optional,StringComparison.Ordinal));
            await Run(RecordMutationOperation.Create,"""{"name":"附件记录","galleryImages":VALUE,"remark":"已填"}""".Replace("VALUE",required,StringComparison.Ordinal));

            // 请求省略附件时从原记录读取；显式清空或更换时按提交后的值判断。
            run.SeedRow = row=>row.GetType().GetProperty("GalleryImages")!.SetValue(row,required);
            await Assert.ThrowsAsync<BusinessRuleException>(()=>Run(RecordMutationOperation.Update,"""{"id":"1"}"""));
            Assert.DoesNotContain("write:Update",run.Trace);
            await Assert.ThrowsAsync<BusinessRuleException>(()=>Run(RecordMutationOperation.Copy,""));
            await Run(RecordMutationOperation.Update,"""{"id":"1","galleryImages":VALUE}""".Replace("VALUE",optional,StringComparison.Ordinal));
            Assert.Contains("write:Update",run.Trace);

            var import = await Assert.ThrowsAsync<PageImportException>(()=>Run(RecordMutationOperation.Import,
                """{"requestJson":[{"名称":"导入附件","附件":VALUE}]}""".Replace("VALUE",required,StringComparison.Ordinal)));
            Assert.Equal(PageImportError.Required,import.Code);
            await Run(RecordMutationOperation.Import,"""{"requestJson":[{"名称":"导入附件","附件":VALUE}]}""".Replace("VALUE",optional,StringComparison.Ordinal));

            var current = run.Trace.Count(item=>item=="write:Insert");
            if (whenBlank)
                await Assert.ThrowsAsync<BusinessRuleException>(()=>Run(RecordMutationOperation.Create,"""{"name":"空值","galleryImages":null}"""));
            else
                await Run(RecordMutationOperation.Create,"""{"name":"空值","galleryImages":null}""");
            Assert.Equal(current+(whenBlank?0:1),run.Trace.Count(item=>item=="write:Insert"));

            var rejectedOutput=Path.Combine(directory,"bad-array-comparison");
            catalog.ConfigColumns.Single(item=>item.Id=="13").Extra["visibleCondition"]=JsonSerializer.SerializeToElement(new
            {
                conditions = new[] { new { field, @operator = "eq", value = "旧附件" } }
            });
            var error=Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,rejectedOutput,"Example.GuardContract"));
            Assert.Contains("数组字段只允许直接判空或判非空",error.Message);
            Assert.False(Directory.Exists(rejectedOutput));

            Task Run(RecordMutationOperation operation,string body)=>run.Invoke(service,operation,run.Request(operation,body));
        }
        finally { if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
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
