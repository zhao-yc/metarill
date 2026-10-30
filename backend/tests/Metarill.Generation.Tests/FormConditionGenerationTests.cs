using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>联动依赖在生成阶段签发，导入的首列覆盖和全部依赖拓扑保持明确。</summary>
public sealed class FormConditionGenerationTests
{
    /// <summary>首列若为关联，必须递归解析全部条件依赖；顺序不能取Excel的属性次序。</summary>
    [Fact]
    public async Task Full_form_dependency_closure_compiles_with_original_nested_and_flat_slots()
    {
        var catalog=Catalog();var model=GenerationModel.Create(catalog,"Example.FormPlan");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();
        var plan=ImportDependencyCompilation.ForPage(model,table,page);Assert.Equal(new[]{"25","27","28"},plan.MatchFields.Order());
        var order=plan.Order.Select(column=>column.Source.Id).ToList();Assert.True(order.IndexOf("27")<order.IndexOf("25"));Assert.True(order.IndexOf("28")<order.IndexOf("25"));
        var directory=Path.Combine(Path.GetTempPath(),"metarill-form-plan",Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.FormPlan.Generated.Services.RelationSourceService",true));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,Path.Combine(Path.GetTempPath(),"metarill-form-plan"));}
    }
    /// <summary>联动字段本身是另一个关系时，循环必须在原子批次写盘前拒绝。</summary>
    [Fact]
    public void Form_dependencies_cannot_cycle_through_another_relation()
    {
        var catalog=Catalog();var allowed=catalog.ConfigColumns.Single(column=>column.Id=="27");allowed.ControlKind="List";allowed.LinkConfigTableId="3";allowed.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("301");
        allowed.ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",whereSql="id=@formParameters.targetId"});
        var directory=Path.Combine(Path.GetTempPath(),"metarill-form-cycle",Guid.NewGuid().ToString("N"));Assert.Contains("循环依赖",Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.FormCycle")).Message);Assert.False(Directory.Exists(directory));
    }
    /// <summary>跨对象槽没有可信实体映射、类型不符或未开放字段不能偷偷使用请求值。</summary>
    [Theory]
    [InlineData("alias=@formParameters.otherTable.allowed")]
    [InlineData("alias=@formParameters.missing")]
    [InlineData("alias=@formParameters.minimum")]
    [InlineData("id=@formParameters.allowed")]
    [InlineData("alias=@current_password")]
    [InlineData("id=@formParameters.id")]
    public void Unproven_dependencies_reject_the_whole_batch(string where)
    {
        var catalog=Catalog();catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",whereSql=where});
        var directory=Path.Combine(Path.GetTempPath(),"metarill-form-invalid",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.FormInvalid"));Assert.False(Directory.Exists(directory));
    }
    /// <summary>页面未开放的依赖即使物理存在，也不能通过候选请求读取或获得写入上下文。</summary>
    [Fact]
    public void Unpublished_source_fields_do_not_become_form_dependencies()
    {
        var catalog=Catalog();catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");var model=GenerationModel.Create(catalog,"Example.FormHidden");Assert.Throws<InvalidOperationException>(()=>RelationCompilation.ForPage(model,model.Tables.Single(table=>table.Source.Id=="2").Pages.Single()));
    }

    /// <summary>实际生成原详情和筛选并挂载Vue，证明发布槽经过分页与已选且旧联动watch仍生效。</summary>
    [Fact]
    public async Task Original_list_form_conditions_reach_paging_and_selected_from_declared_cache_slots()
    {
        var catalog=Catalog();
        foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "27" or "28"))column.Extra["isAutoPostBack"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Single(column=>column.Id=="25").Extra["isAutoPostBackQuery"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-form-list-ui");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.FormListUi");
            foreach(var kind in new[]{"detail","list"})
            {
                var source=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/relation_source-"+kind+"-201.vue"));
                Assert.Contains(JsonSerializer.Serialize("formFields")+":"+JsonSerializer.Serialize(new[]{new{field="allowed",table="relationSource"},new{field="minimum",table="relationSource"}}),source);
                Assert.Contains("<zxz-uni-data-select",source);
            }
            await Probe(output);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }

    /// <summary>仅替换网络/uni宿主，Node从本仓库锁定依赖加载实际生成Vue和真实运行时。</summary>
    private static async Task Probe(string output,[System.Runtime.CompilerServices.CallerFilePath]string file="")
    {
        var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"GeneratedRelationFormUiProbe.mjs"));start.ArgumentList.Add(output);
        using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }

    /// <summary>只构造本轮元数据，前端复用原槽，不手写生成Service或物理建表SQL。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog=RelationOptionsContractTests.Catalog();var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigColumns.AddRange([new(){Id="27",ConfigTableId="2",ColumnName="allowed",Name="分类",LogicalType="string",DefaultValue="允许%"},new(){Id="28",ConfigTableId="2",ColumnName="minimum",Name="最小ID",LogicalType="int64",DefaultValue="1"}]);
        catalog.ConfigColumns.Single(column=>column.Id=="25").SortNo=-5;
        foreach(var id in new[]{"27","28"})catalog.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id,IsShow=0});
        catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail=JsonSerializer.Serialize(new{tableId="3",nameField="name",whereSql="parent_id=@parentId AND name LIKE @formParameters.relationSource.allowed AND id>=@formParameters.minimum"});
        return catalog;
    }
}
