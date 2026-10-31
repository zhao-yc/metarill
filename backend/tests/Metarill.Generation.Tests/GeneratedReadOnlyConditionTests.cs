using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>实际生成/挂载原Vue详情，复合只读条件只增加必要读取依赖，不改变页面结构与权限门禁。</summary>
public sealed class GeneratedReadOnlyConditionTests
{
    /// <summary>字段物理名及entity前缀均绑定真实JSON属性，仅详情可读条件列不能顺便进入列表。</summary>
    [Fact]
    public async Task Composite_condition_mounts_original_page_and_tracks_only_its_detail_dependencies()
    {
        var catalog=Catalog();catalog.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("locked || (this.entity.approval_state === '已审' && entity.name != '允许')");
        var model=GenerationModel.Create(catalog,"Example.Readonly");var table=model.Tables.Single();var page=table.Pages.Single();var compiled=PageReadOnlyCompilation.Compile(page)!;
        Assert.Contains("this.entity.approvalState",compiled.JavaScript);Assert.DoesNotContain("approval_state",compiled.JavaScript);Assert.Equal(new[]{"locked","approvalState","name"},compiled.Dependencies.Select(column=>column.JsonName));
        var projection=ReadProjectionCompilation.ForPage(model,table,page);Assert.True(projection.Detail.CanCompile);Assert.True(projection.List.CanCompile);
        Assert.Equal(new[]{"12","16","17"},projection.Detail.Dependencies.Where(dependency=>dependency.Reasons.Any(reason=>reason.Contains("主子只读",StringComparison.Ordinal))).Select(dependency=>dependency.ColumnId).Order());
        Assert.DoesNotContain(projection.List.OutputFields,field=>field.JsonName is "locked" or "approvalState");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-generated-readonly");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,model.Namespace);var path=Path.Combine(output,"frontend/pages/yzp/customer-detail-101.vue");var first=await File.ReadAllTextAsync(path);
            new CSharpGenerator().Generate(catalog,output,model.Namespace);Assert.Equal(first,await File.ReadAllTextAsync(path));
            await Probe(output);
        }
        finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>坏表达式或未知字段仍在生成阶段拒绝，不能把脚本直接插入computed。</summary>
    [Theory][InlineData("locked || sideEffect()")][InlineData("locked = true")][InlineData("missing")][InlineData("this.entity.constructor")][InlineData("name.value")][InlineData("locked; globalThis.injected=true")]
    public void Unsafe_or_unbound_expression_remains_a_generation_error(string condition)
    {
        var catalog=Catalog();catalog.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement(condition);
        var model=GenerationModel.Create(catalog,"Example.Readonly");Assert.Throws<InvalidOperationException>(()=>PageReadOnlyCompilation.Compile(model.Tables.Single().Pages.Single()));
        Assert.False(ReadProjectionCompilation.ForPage(model,model.Tables.Single(),model.Tables.Single().Pages.Single()).Detail.CanCompile);
    }
    /// <summary>已有单字段生成文本保持不变，空条件不额外插入表达式；JSON路径只投影其根列。</summary>
    [Fact]
    public void Simple_field_and_declared_json_path_have_fixed_narrow_bindings()
    {
        var catalog=Catalog();var page=catalog.ConfigPages[0];Assert.Null(Compile());
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("locked");Assert.Equal("this.entity.locked",Compile()!.JavaScript);
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",ColumnName="payload",LogicalType="json"});catalog.ConfigPageColumns.Add(new(){Id="118",ConfigPageId="101",ConfigColumnId="18"});
        page.Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement("payload.locked && payload.flags.active");var condition=Compile()!;Assert.Single(condition.Dependencies);Assert.Equal("payload",condition.Dependencies[0].JsonName);Assert.Equal("(this.entity.payload[\"locked\"] && this.entity.payload[\"flags\"][\"active\"])",condition.JavaScript);
        PageReadOnlyCompilation? Compile()=>PageReadOnlyCompilation.Compile(GenerationModel.Create(catalog,"Example.Readonly").Tables.Single().Pages.Single());
    }
    /// <summary>两个条件字段只在详情可读，列表没有该两列；不扩大现有页面读取上限。</summary>
    private static Metarill.Core.Metadata.MetadataCatalog Catalog()
    {
        var catalog=GeneratorTests.Catalog();
        foreach(var(id,name,type)in new[]{("16","locked","bool"),("17","approval_state","string")})
        {catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="1",ColumnName=name,LogicalType=type});catalog.ConfigPageColumns.Add(new(){Id="1"+id,ConfigPageId="101",ConfigColumnId=id,IsShow=0,IsEdit=1,Extra=new(){["isInGrid"]=JsonSerializer.SerializeToElement(0),["isInEdit"]=JsonSerializer.SerializeToElement(1)}});}
        return catalog;
    }
    /// <summary>执行实际Vue编译器和原详情方法；网络/权限仅为明确夹具，不宣称浏览器或Production身份验收。</summary>
    private static async Task Probe(string output,[CallerFilePath]string source="")
    {
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!,"GeneratedReadOnlyConditionProbe.mjs"));start.ArgumentList.Add(output);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
