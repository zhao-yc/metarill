using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>验证托管名称的严格DTO、原输入外观及实际生成编译，不以此代替实库事务。</summary>
public sealed class CascadeTreeWriteTests
{
    /// <summary>两个物理ID模式交叉编译，原页面可回显名称，内部请求类型不开放名称。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Final_validation_and_managed_names_compile_for_all_scalar_id_combinations(string sourceType,string parentType)
    {
        var catalog=Catalog("write",sourceType,parentType);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CascadeWrite");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var name=table.Columns.Single(column=>column.Source.Id=="26");
        Assert.True(name.IsManagedCascadeName);Assert.DoesNotContain(page.Writable,column=>column.Source.Id=="26");Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));
        // 原元数据允许的名称输入仍保持原DOM，只在服务端忽略/重算值。
        Assert.False(new OriginalDetailContext(model,table,page,[]).Field(name).ReadOnly);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-write-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeWrite");
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/WriteSourceService.g.cs"));Assert.Contains("CascadeReader201_25(actor,true,false,requireTargetList).PathAsync",service);Assert.Contains("submitted.Add(\"tree_name\")",service);Assert.DoesNotContain("物理派生写入尚未证明：tree_name",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var request=assembly.GetType("Example.CascadeWrite.Generated.DTOs.WriteSourcePage201CreateRequest",true)!;Assert.Null(request.GetProperty("TreeName"));
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"treeName\":\"伪造名称\"}",request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var hand=Path.Combine(directory,"generated/Services/WriteSourceService.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CascadeWrite");Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Reason.Contains("级联导入导出"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>辅助列缺失或叶子目标冲突仍明确拒绝，不借普通外键规则写入未知级联。</summary>
    [Fact]
    public void Unproven_cascade_never_falls_through_to_unvalidated_scalar_storage()
    {
        foreach(var change in new Action<MetadataCatalog>[]
        {
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="26").IsVirtualColumn=1,
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="25").LinkConfigTableId="2",
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="26").LogicalType="int64"
        })
        {
            var catalog=Catalog();change(catalog);var model=GenerationModel.Create(catalog,"Example.CascadeWrite");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var source=table.Columns.Single(column=>column.Source.Id=="25");
            Assert.Null(CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition);Assert.Contains("throw new InvalidOperationException",CascadeTreeWriteRenderer.Validation(model,table,page,source));
        }
    }

    /// <summary>普通物理名称同样不从请求写入，但仍按原元数据保留输入外观。</summary>
    [Fact]
    public void Non_join_auxiliary_is_server_owned_without_changing_original_input_readonly_state()
    {
        var catalog=Catalog();catalog.ConfigColumns.Single(column=>column.Id=="26").Extra.Remove("isJoinColumn");
        var model=GenerationModel.Create(catalog,"Example.CascadeWrite");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var name=table.Columns.Single(column=>column.Source.Id=="26");
        Assert.DoesNotContain(page.Writable,column=>column.Source.Id=="26");Assert.False(new OriginalDetailContext(model,table,page,[]).Field(name).ReadOnly);
    }

    /// <summary>错误逻辑类型也必须走级联拒绝分支，不能逃过原字符串/long外键枚举。</summary>
    [Theory]
    [InlineData("int32")]
    [InlineData("datetime")]
    [InlineData("bool")]
    public void Unsupported_leaf_types_are_rejected_even_outside_ordinary_foreign_scalar_types(string type)
    {
        var catalog=Catalog();catalog.ConfigColumns.Single(column=>column.Id=="25").LogicalType=type;
        var model=GenerationModel.Create(catalog,"Example.CascadeWrite");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);
        var rejected=CascadeTreeWriteCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="25"));Assert.Contains("CascadeTree写入尚未支持",rejected.Reason);Assert.Contains(JsonSerializer.Serialize(rejected.Reason),ForeignValidationRenderer.Render(model,table,page));
    }

    /// <summary>同表另一个关系字段由事务内最终状态校验，不再以旧数据库快照放行。</summary>
    [Fact]
    public void Same_table_non_parent_relation_has_a_final_storage_validation_plan()
    {
        var catalog=Catalog();var source=catalog.ConfigColumns.Single(column=>column.Id=="25");source.LinkConfigTableId="2";source.ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table="write_source",parentField="other_parent_id"}});
        catalog.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="other_parent_id",LogicalType="int64",LinkConfigTableId="2"});catalog.ConfigPageColumns.Add(new(){Id="2007",ConfigPageId="201",ConfigColumnId="27"});
        var model=GenerationModel.Create(catalog,"Example.CascadeWrite");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var gap=CascadeTreeWriteCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="25"));Assert.NotNull(gap.Definition);Assert.True(CascadeTreeMergedWriteRenderer.Applies(table,gap.Definition!));Assert.Contains("ValidateStoredCascade201_25Async",CascadeTreeMergedWriteRenderer.After(model,table,page,false));
    }

    /// <summary>本夹具保留显式名称配置，自动派生另有专项；生成本身不修改数据库。</summary>
    internal static MetadataCatalog Catalog(string prefix="write",string sourceType="string",string parentType="int64")
    {
        var catalog=CascadeTreeReadTests.Catalog(prefix,parentType);var source=catalog.ConfigColumns.Single(column=>column.Id=="25");source.LogicalType=sourceType;source.LinkConfigTableId="3";
        catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="tree_name",LogicalType="string",ColumnLength="10",DefaultValue="不可信默认值",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1)}});
        catalog.ConfigPageColumns.Add(new(){Id="2006",ConfigPageId="201",ConfigColumnId="26"});catalog.ConfigPages.Single(page=>page.Id=="201").IsCopy=1;return catalog;
    }
}
