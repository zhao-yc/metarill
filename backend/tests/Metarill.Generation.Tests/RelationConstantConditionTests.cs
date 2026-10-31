using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>生成批次只放开常量条件；未完成联动上下文、虚拟字段和SQL语句仍明确拒绝。</summary>
public sealed class RelationConstantConditionTests
{
    /// <summary>包含AND与逗号的字面量不能被拆为独立条件；页面覆盖在所有候选及写入入口保持同源。</summary>
    [Fact]
    public async Task Published_constant_conditions_generate_and_compile_without_changing_original_metadata()
    {
        var catalog=RelationOptionsContractTests.Catalog();var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25");
        var definition=JsonSerializer.Serialize(new{whereSql="parent_id=@parentId AND alias='A AND B, C' AND name LIKE '允许%' AND id IN (9007199254750000,9007199254750001) AND delete_no IS NULL"});
        field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(definition);
        var original=JsonSerializer.Serialize(catalog);var directory=Path.Combine(Path.GetTempPath(),"metarill-relation-constant",Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ConstantCompile");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.ConstantCompile.Generated.Services.RelationSourceService",true));
            Assert.Equal(original,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,Path.Combine(Path.GetTempPath(),"metarill-relation-constant"));}
    }

    /// <summary>拒绝发生在原子批次写盘之前；不能因为解析器认识某变量就开放未接线的表单或账号权限。</summary>
    [Theory]
    [InlineData("name LIKE @formParameters.allowed")]
    [InlineData("id=@current_id")]
    [InlineData("id IN (@parentId, 1)")]
    [InlineData("name=concat('a','b')")]
    [InlineData("name='a' OR id=1")]
    [InlineData("id=9223372036854775808")]
    [InlineData("id=1e-100")]
    [InlineData("name='a'; SELECT 1")]
    [InlineData("name='a' -- comment")]
    public void Unproven_definitions_reject_without_partial_generation(string definition)
    {
        var catalog=RelationOptionsContractTests.Catalog();catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",whereSql=definition});
        var directory=Path.Combine(Path.GetTempPath(),"metarill-relation-constant-rejected",Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.ConstantRejected"));Assert.False(Directory.Exists(directory));
    }

    /// <summary>页级覆盖不能把候选换成另一张表，而写入及派生显示仍解释为原外键目标。</summary>
    [Fact]
    public void Constant_overlay_cannot_change_the_foreign_key_target_contract()
    {
        var catalog=RelationOptionsContractTests.Catalog();
        catalog.ConfigColumns.Single(column=>column.Id=="25").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("101");
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{tableId="1",whereSql="name='a'"}));
        var directory=Path.Combine(Path.GetTempPath(),"metarill-relation-constant-mismatch",Guid.NewGuid().ToString("N"));
        Assert.Contains("固定目标一致",Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.ConstantMismatch")).Message);Assert.False(Directory.Exists(directory));
    }

    /// <summary>逻辑派生名称不能当物理SQL列使用。</summary>
    [Fact]
    public void Derived_fields_remain_outside_the_physical_constant_condition_protocol()
    {
        var catalog=RelationOptionsContractTests.Catalog();catalog.ConfigColumns.Single(column=>column.Id=="35").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Single(column=>column.Id=="35").Extra["columnFullName"]=JsonSerializer.SerializeToElement("parent.name");
        catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",whereSql="alias='a'"});
        catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
        var model=GenerationModel.Create(catalog,"Example.DerivedRejected");
        Assert.Throws<InvalidOperationException>(()=>RelationCompilation.ForPage(model,model.Tables.Single(table=>table.Source.Id=="2").Pages.Single()));
    }
}
