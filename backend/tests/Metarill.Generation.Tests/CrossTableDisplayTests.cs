using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>跨表原展示使用已存名称，严格DTO与用途依赖不扩大到无关隐藏字段。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableDisplayTests
{
    /// <summary>四种来源/父键真实生成编译，存储名称跳空白而不需要当前目标记录。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Stored_names_decorate_original_alias_without_changing_DTO_or_handwritten_files(string sourceType,string parentType)
    {
        var catalog=CrossTableReadTests.Catalog("crossdisplay",parentType,parentType);catalog.ConfigColumns.Single(column=>column.Id=="25").LogicalType=sourceType;catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(2);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.CrossDisplay");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);Assert.Single(CrossTableDisplayRenderer.ForPage(model,table,page));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-display-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossDisplay");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var entityType=assembly.GetType("Example.CrossDisplay.Generated.Entities."+table.Name,true)!;var voType=assembly.GetType("Example.CrossDisplay.Generated.VOs."+page.Name+"Vo",true)!;var serviceType=assembly.GetType("Example.CrossDisplay.Generated.Services."+table.Name+"Service",true)!;var request=assembly.GetType("Example.CrossDisplay.Generated.DTOs."+page.Name+"CreateRequest",true)!;Assert.Null(request.GetProperty("PlaceIdDisplayText"));
            var entities=Array.CreateInstance(entityType,2);var values=Array.CreateInstance(voType,2);for(var index=0;index<2;index++){entities.SetValue(Activator.CreateInstance(entityType),index);values.SetValue(Activator.CreateInstance(voType),index);}entityType.GetProperty("CountryName")!.SetValue(entities.GetValue(0),"旧国");entityType.GetProperty("AreaName")!.SetValue(entities.GetValue(0),"  ");entityType.GetProperty("PlaceName")!.SetValue(entities.GetValue(0),"旧城");entityType.GetProperty("CountryName")!.SetValue(entities.GetValue(1)," 旧根 ");
            var method=serviceType.GetMethod("DecorateCross201",BindingFlags.Static|BindingFlags.NonPublic)!;Assert.Same(values,method.Invoke(null,[values,entities]));Assert.Equal("旧国/旧城",voType.GetProperty("PlaceIdDisplayText")!.GetValue(values.GetValue(0)));Assert.Equal(" 旧根 ",voType.GetProperty("PlaceIdDisplayText")!.GetValue(values.GetValue(1)));var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,[Array.CreateInstance(voType,0),entities]));Assert.IsType<InvalidOperationException>(error.InnerException);
            var hand=Path.Combine(directory,"generated/Services/"+table.Name+"Service.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.CrossDisplay");Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>列表只依赖已存名称，详情/确认再补原控件级ID，不读取无关隐藏业务列。</summary>
    [Fact]
    public void Purpose_IR_registers_stored_labels_and_original_level_ids()
    {
        var catalog=CrossTableReadTests.Catalog();catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="private_note",LogicalType="string"});var model=GenerationModel.Create(catalog,"Example.CrossDisplay");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var plans=ReadProjectionCompilation.ForPage(model,table,page);
        foreach(var plan in new[]{plans.List,plans.Detail,plans.Selection}){Assert.True(plan.CanCompile,string.Join(";",plan.Gaps.Select(gap=>gap.Reason)));Assert.Contains(plan.OutputFields,field=>field.JsonName=="placeIdDisplayText"&&field.Kind==ReadProjectionOutputKind.CascadeDisplayText);Assert.DoesNotContain(plan.PhysicalColumns,column=>column.Source.ColumnName=="private_note");foreach(var name in new[]{"country_name","area_name","place_name"})Assert.Contains(plan.PhysicalColumns,column=>column.Source.ColumnName==name);}
        foreach(var plan in new[]{plans.Detail,plans.Selection})foreach(var id in new[]{"country_id","area_id","place_id"})Assert.Contains(plan.PhysicalColumns,column=>column.Source.ColumnName==id);Assert.DoesNotContain(plans.List.PhysicalColumns,column=>column.Source.ColumnName=="country_id");
    }
    /// <summary>显示别名不能覆盖已有物理字段，正式生成在输出之前原子拒绝。</summary>
    [Fact]
    public void Display_alias_collision_preserves_existing_output()
    {
        var catalog=CrossTableReadTests.Catalog();catalog.ConfigColumns.Add(new(){Id="26",ConfigTableId="2",ColumnName="place_id_display_text",LogicalType="string"});var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-display-collision");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var marker=Path.Combine(directory,"handwritten.txt");File.WriteAllText(marker,"保持手写内容");try{Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.CrossDisplay"));Assert.Equal("保持手写内容",File.ReadAllText(marker));Assert.Single(Directory.GetFiles(directory,"*",SearchOption.AllDirectories));}finally{Directory.Delete(directory,true);}
    }
}
