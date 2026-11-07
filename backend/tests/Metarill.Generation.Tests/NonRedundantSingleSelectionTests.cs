using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定单选名称谓词、原Vue协议和候选写入共用正式生成，不以伪SQL或手写页面替代。</summary>
public sealed class NonRedundantSingleSelectionTests
{
    /// <summary>无父与同父候选均生成编译，原名称输入转成固定SQL筛选而非IsIgnore实体查询。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task Original_single_selection_compiles_and_executes_fixed_name_contract(bool parent,bool mapped)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrs_offline");NonRedundantGeneratedFixture.EnableSingleSelection(catalog,parent);NonRedundantGeneratedFixture.AddSelectionPage(catalog);NonRedundantGeneratedFixture.EnableImports(catalog);catalog.ConfigPages[0].IsExport=1;if(mapped){NonRedundantGeneratedFixture.AddMappedSinglePage(catalog);NonRedundantGeneratedFixture.EnableMappedImports(catalog);}
        var model=GenerationModel.Create(catalog,"Example.NonRedSingle");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single(page=>page.Source.Id=="101");
        var names=SelectionNameFilterCompilation.ForPage(model,table,page);Assert.Empty(names.Gaps);var binding=Assert.Single(names.Bindings);Assert.Equal("customerName",binding.FilterKey);Assert.Equal(SelectionNameFilterKind.JoinedScalarLike,binding.Kind);Assert.Throws<InvalidOperationException>(()=>SelectionNameFilterCompilation.RenderCases(names));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-single");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var generated=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,generated,model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType("Example.NonRedSingle.Generated.Services.MtNrsOfflineOrderService",true)!;
            var filters=type.GetMethod("ReadFilters101",BindingFlags.NonPublic|BindingFlags.Static)!;var actor=new RuntimeActor(1,"单选合同",false,"54","5");
            var request=new PageRequest{ParentId="71",Filters=new(){{"customerName",JsonSerializer.SerializeToElement("中文_%!引号'")}}};
            var actual=Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(filters.Invoke(null,[request,actor]));
            var name=Assert.Single(actual,filter=>filter.Field=="customer_name");Assert.Equal(FixedReadComparison.Contains,name.Comparison);Assert.Equal("中文_%!引号'",name.Value);
            foreach(var value in new[]{JsonSerializer.SerializeToElement(1),JsonSerializer.SerializeToElement(true),JsonSerializer.SerializeToElement(new[]{"A"})})
            {request.Filters["customerName"]=value;Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>filters.Invoke(null,[request,actor])).InnerException);}
            request.Filters["customerName"]=JsonSerializer.SerializeToElement<object?>(null);Assert.DoesNotContain(Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(filters.Invoke(null,[request,actor])),filter=>filter.Field=="customer_name");
            if(mapped)
            {
                Assert.NotNull(type.GetMethod("Import104Async"));Assert.NotNull(type.GetMethod("Import105Async"));
                var mappedPage=table.Pages.Single(page=>page.Source.Id=="104");var dependency=ImportDependencyCompilation.ForPage(model,table,mappedPage);
                Assert.Equal("19",dependency.Parents["15"].Source.Id);Assert.True(dependency.Order.ToList().FindIndex(column=>column.Source.Id=="19")<dependency.Order.ToList().FindIndex(column=>column.Source.Id=="15"));Assert.Equal(new[]{"14"},dependency.MatchFields);
            }
            await Probe(generated,parent);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>错误目标、辅助名、覆盖或未知父不能生成半可用单选应用。</summary>
    [Theory]
    [InlineData("pageType")][InlineData("path")][InlineData("nameType")][InlineData("targetHidden")][InlineData("scope")][InlineData("nameOverride")][InlineData("foreignTarget")][InlineData("arrayName")]
    public void Invalid_single_selection_rejects_before_writing_files(string scenario)
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrs_offline");NonRedundantGeneratedFixture.EnableSingleSelection(catalog,true);
        if(scenario=="pageType")catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(2);
        if(scenario=="path")catalog.ConfigColumns.Single(column=>column.Id=="111").Extra["columnFullName"]=JsonSerializer.SerializeToElement("group.name");
        if(scenario=="nameType")catalog.ConfigColumns.Single(column=>column.Id=="24").LogicalType="int64";
        if(scenario=="arrayName")catalog.ConfigColumns.Single(column=>column.Id=="24").ControlKind="FileLibrary";
        if(scenario=="targetHidden"){var field=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="24");field.IsEdit=0;field.IsShow=0;}
        if(scenario=="scope")catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail="{\"pageId\":\"201\",\"nameField\":\"name\",\"whereSql\":\"name=@parent_id\"}";
        if(scenario=="nameOverride")catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="101"&&field.ConfigColumnId=="15").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"notes\"}");
        if(scenario=="foreignTarget")catalog.ConfigColumns.Single(column=>column.Id=="15").LinkConfigTableId="3";
        var directory=Path.Combine(Path.GetTempPath(),"metarill-nonred-single-reject",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,directory,"Example.NonRedSingle"));Assert.False(Directory.Exists(directory));
    }
    /// <summary>开启非冗余不能意外放宽其它未定义的存储模式。</summary>
    [Fact]
    public void Unknown_storage_mode_has_no_name_binding()
    {
        var catalog=NonRedundantGeneratedFixture.Catalog("mt_nrs_offline");NonRedundantGeneratedFixture.EnableSingleSelection(catalog,false);catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=2;
        var model=GenerationModel.Create(catalog,"Example.NonRedSingle");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());
        Assert.Empty(plan.Bindings);Assert.Contains("未识别",Assert.Single(plan.Gaps).Reason);
    }
    /// <summary>运行真实原Vue生成脚本；网络及外部宿主受控，不等于浏览器或真实权限系统。</summary>
    private static async Task Probe(string generated,bool parent,[CallerFilePath]string file="")
    {
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"NonRedundantSingleSelectionProbe.mjs"));start.ArgumentList.Add(generated);start.ArgumentList.Add(parent?"parent":"plain");
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception interrupted){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,interrupted);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
