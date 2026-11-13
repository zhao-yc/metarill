using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>对照原级联隐式名称合同，验证真实VO/严格DTO与用途边界，不扩大隐藏业务字段。</summary>
public sealed class CascadeTreeImplicitNameTests
{
    /// <summary>名称未单独配置为页面字段时，生成程序集仍按原协议输出名称，内部请求继续拒绝。</summary>
    [Theory]
    [InlineData("string")]
    [InlineData("int64")]
    public async Task Original_implicit_name_is_projected_but_never_becomes_writable_or_a_general_hidden_field(string type)
    {
        var catalog=Catalog(type);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.ImplicitCascade");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="201");
        Assert.DoesNotContain(page.Readable,column=>column.JsonName=="treeName");Assert.DoesNotContain(page.Writable,column=>column.JsonName=="treeName");
        Assert.Equal("treeName",Assert.Single(CascadeTreeViewRenderer.ImplicitNames(model,table,page)).JsonName);
        var plan=ReadProjectionCompilation.ForPage(model,table,page);
        foreach(var purpose in new[]{plan.List,plan.Detail,plan.Selection})
        {
            Assert.True(purpose.CanCompile,string.Join(";",purpose.Gaps.Select(gap=>gap.Reason)));
            Assert.Contains(purpose.OutputFields,field=>field.JsonName=="treeName"&&field.Kind==ReadProjectionOutputKind.Physical);
            Assert.Contains(purpose.OutputFields,field=>field.JsonName=="treeIdDisplayText"&&field.Kind==ReadProjectionOutputKind.CascadeDisplayText);
            Assert.DoesNotContain(purpose.PhysicalColumns,column=>column.JsonName=="privatePayload");
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-implicit");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ImplicitCascade");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entityType=assembly.GetType("Example.ImplicitCascade.Generated.Entities.ImplicitSource",true)!;var entity=Activator.CreateInstance(entityType)!;entityType.GetProperty("TreeName")!.SetValue(entity,"存储叶子名称");entityType.GetProperty("PrivatePayload")!.SetValue(entity,"隐藏业务值");
            var vo=assembly.GetType("Example.ImplicitCascade.Generated.VOs.ImplicitSourcePage201Vo",true)!;var data=JsonSerializer.SerializeToElement(vo.GetMethod("FromEntity")!.Invoke(null,[entity]));Assert.Equal("存储叶子名称",data.GetProperty("treeName").GetString());Assert.False(data.TryGetProperty("privatePayload",out _));
            Assert.Null(assembly.GetType("Example.ImplicitCascade.Generated.VOs.ImplicitSourcePage202Vo",true)!.GetProperty("TreeName"));
            var request=assembly.GetType("Example.ImplicitCascade.Generated.DTOs.ImplicitSourcePage201CreateRequest",true)!;Assert.Null(request.GetProperty("TreeName"));Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"treeName\":\"伪造名称\"}",request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var controller=File.ReadAllText(Path.Combine(directory,"generated/Controllers/ImplicitSourcePage201Controller.g.cs"));Assert.Contains("treeName",controller);
            var hand=Path.Combine(directory,"generated/Services/ImplicitSourceService.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ImplicitCascade");Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>用途依赖只跟随原用途的级联ID；详情专用字段不能扩大列表/选择的读取。</summary>
    [Fact]
    public void Purpose_plan_does_not_add_detail_only_cascade_to_list_or_selection()
    {
        var catalog=Catalog();var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25");settings.IsShow=0;settings.IsEdit=1;settings.Extra.Remove("isInGrid");
        var model=GenerationModel.Create(catalog,"Example.ImplicitCascade");var table=model.Tables.Single(table=>table.Source.Id=="2");var plan=ReadProjectionCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="201"));
        Assert.Contains(plan.Detail.OutputFields,field=>field.JsonName=="treeName");
        foreach(var purpose in new[]{plan.List,plan.Selection})Assert.DoesNotContain(purpose.OutputFields,field=>field.JsonName is "treeId" or "treeName" or "treeIdDisplayText");
    }
    /// <summary>未知模式和错误名称类型不能借隐式依赖放宽成任意物理列。</summary>
    [Fact]
    public void Unproven_definition_or_missing_name_keeps_projection_gap()
    {
        foreach(var mutate in new Action<MetadataCatalog>[]
        {
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="{}",
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="26").LogicalType="int64"
        })
        {
            var catalog=Catalog();mutate(catalog);var model=GenerationModel.Create(catalog,"Example.ImplicitCascade");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="201");
            Assert.False(ReadProjectionCompilation.ForPage(model,table,page).Detail.CanCompile);Assert.Empty(CascadeTreeViewRenderer.ImplicitNames(model,table,page));
        }
    }
    /// <summary>即使冲突物理列未在页上显示，固定用途计划也不能把同名计算别名当可执行。</summary>
    [Fact]
    public void Hidden_physical_display_alias_conflict_is_rejected_by_both_ir_and_actual_generation()
    {
        var catalog=Catalog();catalog.ConfigColumns.Single(column=>column.Id=="28").ColumnName="tree_id_display_text";
        var model=GenerationModel.Create(catalog,"Example.ImplicitCascade");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="201");
        Assert.False(ReadProjectionCompilation.ForPage(model,table,page).Detail.CanCompile);
        var output=Path.Combine(Path.GetTempPath(),"metarill-cascade-alias",Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,output,"Example.ImplicitCascade"));
        Assert.False(Directory.Exists(output));
    }
    /// <summary>保留物理受管名称但移除该页面字段；另页不含级联，验证隐式授权不跨页传播。</summary>
    private static MetadataCatalog Catalog(string type="string")
    {
        var catalog=CascadeTreeWriteTests.Catalog("implicit",type,type);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="26");
        catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="private_payload",LogicalType="string"});
        var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;page.Id="202";catalog.ConfigPages.Add(page);catalog.ConfigPageColumns.Add(new(){Id="2022",ConfigPageId="202",ConfigColumnId="22"});return catalog;
    }
}
