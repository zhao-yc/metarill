using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>跨表写契约由元数据编译，隐式字段只扩张该控件的负载，不改布局或其它隐藏字段。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableWriteTests
{
    /// <summary>自动级ID进入原DTO和VO，名称只回显，生成代码实际编译四种父键组合。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Implicit_level_fields_and_single_DML_verification_compile(string sourceType,string parentType)
    {
        var catalog=CrossTableReadTests.Catalog("crosswrite",parentType,parentType);catalog.ConfigColumns.Single(column=>column.Id=="25").LogicalType=sourceType;var saved=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossWrite");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));Assert.Equal(3,plan.Levels.Count);Assert.Contains(page.Writable,column=>column.Source.ColumnName=="country_id");Assert.Contains(page.Readable,column=>column.Source.ColumnName=="country_name");Assert.DoesNotContain(page.Writable,column=>column.Source.ColumnName.EndsWith("_name"));Assert.DoesNotContain(page.DetailColumns,column=>column.Source.ColumnName=="country_id");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-write-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossWrite");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CrossWrite.Generated.DTOs.CrosswriteSourcePage201CreateRequest"));Assert.Equal(saved,JsonSerializer.Serialize(catalog));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>显式字段类型错误、同表图和系统前缀继续保留写门禁。</summary>
    [Fact]
    public void Conflicting_bindings_keep_write_closed()
    {
        var catalog=CrossTableReadTests.Catalog();catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="country_name",LogicalType="int64"});var model=GenerationModel.Create(catalog,"Example.Conflict");var table=model.Tables.Single(table=>table.Source.Id=="2");var gap=CrossTableWriteCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(column=>column.Source.Id=="25"));Assert.Null(gap.Definition);Assert.Contains("物理标量",gap.Reason);
    }
}
