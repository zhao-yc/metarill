using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>完整路径Excel协议与实际生成编译，不使用ID或同名叶子捷径。</summary>
public sealed class CascadeTreeExcelTests
{
    /// <summary>多字符分隔符和数字名称按原格式反查，重复层级明确拒绝。</summary>
    [Fact]
    public async Task Full_path_resolves_unique_levels_and_numeric_names_without_id_fallback()
    {
        var nodes=new[]{new CascadeTreeNode(9007199254740993,"1234",null),new(2,"叶/区","9007199254740993"),new(3,"另一根",null),new(4,"叶/区","3")};
        Task<IReadOnlyList<CascadeTreeNode>> Find(string name,long? parent,CancellationToken ct)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(nodes.Where(node=>node.Name==name&&node.ParentId==parent?.ToString()).ToArray());
        Assert.Equal(2,await CascadeTreeExcelPath.ResolveAsync("1234 > 叶/区"," > ",Find));Assert.Equal(4,await CascadeTreeExcelPath.ResolveAsync("另一根 > 叶/区"," > ",Find));
        await Assert.ThrowsAsync<PageImportException>(()=>CascadeTreeExcelPath.ResolveAsync("叶/区"," > ",Find));
        await Assert.ThrowsAsync<PageImportException>(()=>CascadeTreeExcelPath.ResolveAsync("1234"," > ",(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([nodes[0],nodes[2]])));
        var path=new CascadeTreePath([new("9007199254740993","1234"),new("2","叶/区")],"不可信预计算值");Assert.Equal("1234 > 叶/区",CascadeTreeExcelPath.Format(path," > "));
        Assert.Throws<InvalidOperationException>(()=>CascadeTreeExcelPath.Format(path,"/"));Assert.Throws<InvalidOperationException>(()=>CascadeTreeExcelPath.Format(null,"/"));
        Assert.Throws<InvalidOperationException>(()=>CascadeTreeExcelPath.Format(new([new("1"," 空白 ")],""),"/"));
    }
    /// <summary>追加/覆盖均编译内部导入动作，普通写入继续检查目标list，不接受客户端动作选择。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Actual_generated_import_and_export_compile_for_all_id_types(string sourceType,string parentType)
    {
        var catalog=Catalog("excel",sourceType,parentType);var saved=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeExcel");var source=File.ReadAllText(Path.Combine(directory,"generated/Services/ExcelSourceService.g.cs"));Assert.Contains("ImportCascade201_25Async",source);Assert.Contains("ExcelCascadeTreeId(entity)",source);Assert.Contains("cascadeOperation != RecordMutationOperation.Import",source);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.CascadeExcel.Generated.Controllers.ExcelSourcePage201Controller"));Assert.Equal(saved,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>导入页无普通新增/编辑入口；覆盖按明确第一物理业务字段，不改变原页面协议。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string prefix="excel",string sourceType="string",string parentType="int64")
    {
        var catalog=CascadeTreeViewCompilationTests.Catalog(prefix,sourceType,parentType);
        foreach(var column in catalog.ConfigColumns){column.Name=column.ColumnName;if(column.ConfigTableId=="2")column.SortNo=column.Id switch{"22"=>1,"25"=>2,"27"=>3,_=>0};}
        catalog.ConfigColumns.Single(column=>column.Id=="22").Name="记录名";catalog.ConfigColumns.Single(column=>column.Id=="25").Name="树路径";catalog.ConfigColumns.Single(column=>column.Id=="27").Name="金额";
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsNew=0;page.IsSave=0;
        var overwrite=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(page))!;overwrite.Id="202";overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigPages.Add(overwrite);
        foreach(var old in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray()){var field=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(old))!;field.Id="202"+old.Id;field.ConfigPageId="202";catalog.ConfigPageColumns.Add(field);}return catalog;
    }
}
