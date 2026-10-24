using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>页级只覆盖nameField仍继承稳定目标，不修改合法历史配置来掩盖生成缺口。</summary>
public sealed class SelectionControlOverlayTests
{
    /// <summary>复现真实203页的最小覆盖，实际生成全批Vue并编译挂载已选ID/回执。</summary>
    [Fact]
    public async Task Name_only_overlay_inherits_source_page_id_and_generates_original_selection()
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog("overlay");var original=catalog.ConfigPages.Single(page=>page.Id=="201");var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;page.Id="203";catalog.ConfigPages.Add(page);
        foreach(var id in new[]{"22","25","26"})catalog.ConfigPageColumns.Add(new(){Id="overlay_"+id,ConfigPageId=page.Id,ConfigColumnId=id,IsShow=1,IsEdit=1});
        var field=catalog.ConfigPageColumns.Single(item=>item.ConfigPageId==page.Id&&item.ConfigColumnId=="25");field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"id\"}");
        var model=GenerationModel.Create(catalog,"Example.Overlay");var table=model.Tables.Single(table=>table.Source.Id=="2");var source=table.Columns.Single(column=>column.Source.Id=="25");var effective=SelectionControlDefinition.Resolve(source,field);
        Assert.Equal("301",effective.PageId);Assert.Equal("id",effective.NameField);Assert.Equal("{\"nameField\":\"id\"}",field.Extra["controlDetail"].GetString());Assert.NotNull(MultiSelectCompilation.Resolve(model,table,table.Pages.Single(page=>page.Source.Id=="203"),source).Definition);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-selection-overlay");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.Overlay");var detail=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/overlay_source-detail-203.vue"));Assert.Contains("overlay_target-list-301.vue",detail);Assert.Contains("selected-name-field=\"id\"",detail);Assert.Contains(":selectedNames=\"entity.targetIds\"",detail);
            var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add(Path.Combine(TestDirectory(),"SelectionControlOverlayProbe.mjs"));start.ArgumentList.Add(output);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>重复键和已知复杂键不能在前后端得到不同解释；未知合法嵌套字段仍留存。</summary>
    [Theory]
    [InlineData("{\"nameField\":\"id\",\"nameField\":\"name\"}")]
    [InlineData("{\"pageId\":{\"id\":\"301\"}}")]
    public void Invalid_known_overlay_is_rejected(string text)
    {
        var model=GenerationModel.Create(GeneratedMultiSelectContractTests.Catalog(),"Example.RejectOverlay");var column=model.Tables.Single(table=>table.Source.Id=="2").Columns.Single(column=>column.Source.Id=="25");var field=new PageColumnMetadata();field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(text);Assert.Throws<InvalidOperationException>(()=>SelectionControlDefinition.Resolve(column,field));
    }
    /// <summary>测试脚本相对本源码固定定位，不依赖旧工程绝对路径。</summary>
    private static string TestDirectory([CallerFilePath]string path="")=>Path.GetDirectoryName(path)!;
}
