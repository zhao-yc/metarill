using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原列表DOM、移动分支与真实Vue生命周期，不能以新页面测试代替。</summary>
public sealed class OriginalUniappListTests
{
    /// <summary>三种原移动布局与PC筛选/分页同时存在，原模板不出现简化翻页控件。</summary>
    [Theory]
    [InlineData(1,"uni-list-item")]
    [InlineData(2,"class='yz-row'")]
    [InlineData(3,"class='yz-row3'")]
    public async Task Original_layout_and_actual_vue_keep_query_sort_and_selection(int layout,string mobile)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-list");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog(layout); new CSharpGenerator().Generate(catalog,output,"Example.OriginalList");
            var text=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/customer-list-101.vue"));
            Assert.Contains("class='list-page main-page'",text);Assert.Contains("class='list-page-pc'",text);Assert.Contains("class='list-page-mobile'",text);
            Assert.Contains("class=\"pc-filters\"",text);Assert.Contains("@change=\"paginationChange\"",text);Assert.Contains("@clickLoadMore='loadNextPage()'",text);Assert.Contains(mobile,text);
            Assert.Contains("@sort-change=\"nameSortChange\"",text);Assert.Contains("v-model='filters.name'",text);Assert.Contains("list-action-link",text);
            Assert.DoesNotContain("draftFilters",text);Assert.DoesNotContain("上一页</button>",text);Assert.DoesNotContain("下一页</button>",text);
            var selected=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/customer-list-102.vue"));
            Assert.Contains("选好了（{{ selectedList.length }}）",selected);Assert.Contains("@click=\"chkChange(index)\"",selected);Assert.DoesNotContain("yzp-relation-select",selected);
            await Probe(output);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }

    /// <summary>原常量、日期、下拉框和确认按钮均保留，来源列路由不改成新远程组件。</summary>
    [Fact]
    public async Task Original_controls_and_buttons_keep_their_declared_contracts()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-list");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog(1);catalog.PrimaryColor="#123456";
            var column=catalog.ConfigColumns.Single(c=>c.ColumnName=="remark");column.ControlKind="ConstDownList";column.ControlDetail="甲|A,乙|B";
            catalog.ConfigPageColumns.Single(c=>c.ConfigPageId=="101"&&c.ConfigColumnId==column.Id).Extra["isInFilter"]=JsonSerializer.SerializeToElement(1);
            catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="btn1",configPageId="101",name="批量确认",place="List",visibleScheme="Normal",isBatchSelectButton=1,isNeedConfirm=1,confirmMsg="按原文确认？",actionType=1,sql="/admin/custom/confirm"}));
            catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new{id="btn2",configPageId="101",name="行内确认",place="EditAndGrid",visibleScheme="AlwaysVisible",type=2,isNeedConfirm=1}));
            new CSharpGenerator().Generate(catalog,output,"Example.OriginalControls");
            var text=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/customer-list-101.vue"));
            Assert.Contains("<zxz-uni-data-select v-model='filters.remark' :localdata='remarkRange' filterable />",text);
            Assert.Contains("primaryColor=\"#123456\"",text);Assert.Contains("@click='customerButtonHandlerbtn1'",text);Assert.Contains("list-action-link--warn",text);Assert.Contains("data:that.selectedList",text);
            Assert.Contains("/admin/sys/config_button/execute",text);Assert.Contains("confirmMsg",JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>同一页配置生成普通与多选两页；两页仍使用原字段与原可视操作。</summary>
    private static MetadataCatalog Catalog(int layout)
    {
        var catalog=GeneratorTests.Catalog();catalog.ConfigPages[0].Extra["isUpdate"]=JsonSerializer.SerializeToElement(0);catalog.ConfigPages[0].Extra["mobileListPageStyleType"]=JsonSerializer.SerializeToElement(layout);
        var selection=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages[0]))!;selection.Id="102";selection.IsNew=0;selection.Extra["type"]=JsonSerializer.SerializeToElement(2);
        catalog.ConfigPages.Add(selection);
        foreach(var field in catalog.ConfigPageColumns.ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id+="s";copy.ConfigPageId="102";catalog.ConfigPageColumns.Add(copy);}
        return catalog;
    }
    private static async Task Probe(string output,[CallerFilePath]string source="")
    {
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!,"OriginalListVueProbe.mjs"));start.ArgumentList.Add(output);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
