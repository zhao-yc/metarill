using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>沿原Excel中文表头及空值规则，来源稀疏位不能扩大模板或破坏标量导出筛选。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossTableExcelTests
{
    /// <summary>原四列表单、标量列表导出和两棵树的逻辑筛选由实际生成代码编译四种类型。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public async Task Original_import_columns_and_narrow_scalar_export_compile(string sourceType,string parentType)
    {
        var catalog=Catalog("nr_cross_excel",sourceType,parentType);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.NonRedundantCrossExcel");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.Equal(new[]{"name","place_id","secondary_place_id","amount"},ImportRenderer.Columns(page).Select(column=>column.Source.ColumnName));Assert.DoesNotContain(page.ListColumns,column=>column.Source.ControlKind=="CascadeTree");Assert.Contains(page.Writable,column=>column.Source.ColumnName=="country_id");Assert.Contains(page.Writable,column=>column.Source.ColumnName=="secondary_area_id");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-excel-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType(model.Namespace+".Generated.Services.NrCrossExcelSourceService")!.GetMethod("Import201Async"));Assert.NotNull(assembly.GetType(model.Namespace+".Generated.Services.NrCrossExcelSourceService")!.GetMethod("Export201Async"));Assert.Equal(before,JsonSerializer.Serialize(catalog));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>显式祖先保留原表头；只读字段即使普通表单扩张也不能被Excel重新开放。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void Explicit_ancestor_and_readonly_headers_keep_original_boundary(bool readOnly)
    {
        var catalog=Catalog("nr_cross_excel","int64","string",true);if(readOnly)catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="28").Extra["isReadOnly"]=JsonSerializer.SerializeToElement(1);var page=Assert.Single(GenerationModel.Create(catalog,"Example.NonRedundantCrossExcel").Tables.Single(table=>table.Source.Id=="2").Pages);Assert.Equal(!readOnly,ImportRenderer.Columns(page).Any(column=>column.Source.ColumnName=="country_id"));Assert.DoesNotContain(ImportRenderer.Columns(page),column=>column.Source.ColumnName.EndsWith("_name",StringComparison.Ordinal)||column.IsId);
    }
    /// <summary>导出包用自身运行时源码构建，Full继续保留尚未完成验收的路径用途门槛。</summary>
    [Fact]
    public async Task Backend_package_builds_with_original_import_and_scalar_export_while_Full_remains_closed()
    {
        var catalog=Catalog("nr_cross_excel","string","int64");var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-excel-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{new ApplicationPackager().Create(catalog,directory,"Example.CrossExcelPackage",NonRedundantApplicationFixture.Root(),ApplicationPackageMode.BackendOnly);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(directory,operation);Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,directory+"-full","Example.CrossExcelFull",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(directory+"-full"));}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>双控件沿相同固定目标，隐式各级ID只参与普通表单；列表导出只有名称／金额／字符串ID。</summary>
    internal static MetadataCatalog Catalog(string prefix,string sourceType,string parentType,bool explicitAncestor=false)
    {
        var catalog=CrossTableExcelImportColumnsTests.Catalog(prefix,sourceType);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "45" or "55"))column.LogicalType=parentType;var source=catalog.ConfigColumns.Single(column=>column.Id=="25");var secondary=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(source))!;secondary.Id="80";secondary.ColumnName="secondary_place_id";secondary.Name="另一层级";secondary.SortNo=15;secondary.ControlDetail=secondary.ControlDetail.Replace("\"columnPrefix\":\"","\"columnPrefix\":\"secondary_");catalog.ConfigColumns.Add(secondary);catalog.ConfigPageColumns.Add(new(){Id="20180",ConfigPageId="201",ConfigColumnId="80",IsShow=0,SortNo=15,Extra=new(){{"isSearch",JsonSerializer.SerializeToElement(1)}}});catalog.ConfigPageColumns.Add(new(){Id="20121",ConfigPageId="201",ConfigColumnId="21",SortNo=99,Extra=new(){{"isReadOnly",JsonSerializer.SerializeToElement(1)}}});
        if(explicitAncestor){catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="country_id",Name="国家ID",LogicalType="string",LinkConfigTableId="3",SortNo=30});catalog.ConfigPageColumns.Add(new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=0,SortNo=30});}return catalog;
    }
}
