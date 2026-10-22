using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原Excel字段来自显式页面配置，不能被普通表单的隐式级ID扩张。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelImportColumnsTests
{
    /// <summary>原三列保持；普通表单仍接受祖先ID，元数据不回写。</summary>
    [Fact]
    public void Implicit_level_IDs_stay_in_form_DTO_but_not_original_Excel_columns()
    {
        var catalog=Catalog();var saved=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossExcelColumns");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.Equal(new[]{"amount","name","place_id"},ImportRenderer.Columns(page).Select(column=>column.Source.ColumnName).Order());Assert.Contains(page.Writable,column=>column.Source.ColumnName=="country_id");Assert.Contains(page.Writable,column=>column.Source.ColumnName=="area_id");Assert.Equal(saved,JsonSerializer.Serialize(catalog));
    }
    /// <summary>显式配置的祖先字段仍按原模板保留，不按字段后缀一律删除。</summary>
    [Fact]
    public void Explicit_level_ID_remains_an_original_Excel_column()
    {
        var catalog=Catalog();AddCountry(catalog);var page=GenerationModel.Create(catalog,"Example.CrossExcelColumns").Tables.Single(table=>table.Source.Id=="2").Pages.Single();Assert.Contains(ImportRenderer.Columns(page),column=>column.Source.ColumnName=="country_id");Assert.DoesNotContain(ImportRenderer.Columns(page),column=>column.Source.ColumnName=="area_id");
    }
    /// <summary>只读字段和当前父范围继续排除，不能借显式编辑列绕过原上下文。</summary>
    [Fact]
    public void Readonly_and_parent_scoped_explicit_IDs_remain_excluded()
    {
        foreach(var scoped in new[]{false,true}){var catalog=Catalog();AddCountry(catalog);if(scoped)catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND country_id=@parent_id";else catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="28").Extra["isReadOnly"]=JsonSerializer.SerializeToElement(1);var page=GenerationModel.Create(catalog,"Example.CrossExcelColumns").Tables.Single(table=>table.Source.Id=="2").Pages.Single();Assert.DoesNotContain(ImportRenderer.Columns(page),column=>column.Source.ColumnName=="country_id");}
    }
    /// <summary>两种来源键实际生成编译，输入模板不增列，空单元格准备可加入原事务。</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int64")]
    public async Task Generated_import_template_and_clear_preparation_compile(string type)
    {
        var catalog=Catalog("excelcolumns",type);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-columns");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ExcelColumns");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.ExcelColumns.Generated.Services.ExcelcolumnsSourceService"));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>最小原配置只有名称、金额和末级控件，启用导入不应扩大页面列。</summary>
    internal static MetadataCatalog Catalog(string prefix="crossexcel",string type="string")
    {
        var catalog=CrossTableFilterTests.Catalog(prefix,type,"int64");var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsNew=0;page.IsSave=0;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigColumns.Single(column=>column.Id=="27").DefaultValue="1";catalog.ConfigColumns.Single(column=>column.Id=="25").Name="级联";
        foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId=="2")){if(column.Id=="22")column.SortNo=0;else if(column.Id=="25")column.SortNo=10;else if(column.Id=="26")column.SortNo=20;}
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"))field.SortNo=field.ConfigColumnId=="22"?0:field.ConfigColumnId=="25"?10:20;return catalog;
    }
    /// <summary>用户明确配置的ID字段属于原Excel表单，不是自动物理列。</summary>
    private static void AddCountry(MetadataCatalog catalog){catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="country_id",LogicalType="string",LinkConfigTableId="3"});catalog.ConfigPageColumns.Add(new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=0});}
}
