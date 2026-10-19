using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原数组下拉控件按固定元数据携带两个父范围，不能空目标父退回全表。</summary>
public sealed class OriginalListTargetParentTests
{
    /// <summary>真实原Vue覆盖同父、独立目标父、未开放父筛选的明确缺口；不使用替代组件。</summary>
    [Theory]
    [InlineData("same")][InlineData("independent")][InlineData("unresolved")]
    public async Task Original_list_options_follow_declared_filter_parent_and_discard_old_scope(string scenario)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-list-parent");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=RelationOptionsContractTests.Catalog();catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            if(scenario!="same")
            {
                catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="source_owner_id",LogicalType="int64",LinkConfigTableId="2"});
                catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="source_owner_id=@parentId AND yz_project_id=@current_yz_project_id";
                catalog.ConfigPageColumns.Add(new(){Id="2026",ConfigPageId="201",ConfigColumnId="26",IsShow=1,IsEdit=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(scenario=="independent"?1:0)}});
            }
            new CSharpGenerator().Generate(catalog,output,"Example.OriginalListParent");
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(SourceFile())!,"OriginalListTargetParentProbe.mjs"));start.ArgumentList.Add(output);start.ArgumentList.Add(scenario);
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode==0,await stdout+await stderr);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    private static string SourceFile([CallerFilePath]string source="")=>source;
}
