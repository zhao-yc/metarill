using System.Text.Json;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>原List保留zxz组件和数组协议；额外目标父必须来自唯一可读声明外键，不得猜测或退全量。</summary>
public sealed class GeneratedRelationUiTests
{
    [Fact]
    public async Task Same_parent_list_keeps_original_component_and_fixed_parent_request()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-ui"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = RelationOptionsContractTests.Catalog(); catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
            new CSharpGenerator().Generate(catalog, output, "Example.RelationUi");
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            var list = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-list-201.vue"));
            Assert.Contains("<zxz-uni-data-select",detail);Assert.DoesNotContain("<yzp-relation-select",detail+list);Assert.Contains("findNameIdTarget25",detail);Assert.Contains("formParameters:uni.getStorageSync(that.formParametersName)",detail);
            Assert.Contains("dataKey='name' dataValue='id'",detail);Assert.Contains("targetParentId:optionParent",detail);Assert.Contains("filters.targetId",list);
            await RunProbe(output,"same");
            catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
            new CSharpGenerator().Generate(catalog, output, "Example.RelationUi"); detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("targetId:true",detail);Assert.Contains(":disabled='targetIdReadOnly'",detail);Assert.DoesNotContain("@update:modelValue=\"change('targetId'",detail);await RunProbe(output,"readonly");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }

    [Fact]
    public async Task Independent_target_parent_requires_a_unique_readable_declared_foreign_key()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-ui"); var output = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = RelationOptionsContractTests.Catalog();
            catalog.ConfigColumns.Add(new() { Id = "28", ConfigTableId = "2", ColumnName = "source_owner_id", LogicalType = "int64", LinkConfigTableId = "2" });
            catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = "source_owner_id=@parentId AND yz_project_id=@current_yz_project_id";
            new CSharpGenerator().Generate(catalog, output, "Example.IndependentUi");
            var detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("关联目标父范围尚未明确",detail);Assert.Contains("<zxz-uni-data-select",detail);Assert.DoesNotContain("<yzp-relation-select",detail);await RunProbe(output,"unresolved");
            using (var capabilities = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "generation-capabilities.json"))))
                Assert.Contains(capabilities.RootElement.GetProperty("unsupportedControls").EnumerateArray(), item => item.GetProperty("reason").GetString()!.Contains("targetParentId", StringComparison.Ordinal));
            // 真实当前记录开放了唯一目标父外键之后，才能明确绑定 entity.parentId，不经过索引等跨层推测。
            catalog.ConfigPageColumns.Add(new() { Id = "2026", ConfigPageId = "201", ConfigColumnId = "26", IsShow = 1, IsEdit = 1 });
            new CSharpGenerator().Generate(catalog, output, "Example.IndependentUi"); detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("String(that.entity.parentId??'')",detail);Assert.Contains("targetParentId:optionParent",detail);await RunProbe(output,"independent");await RunProbe(output,"independentReadonly");
            catalog.ConfigColumns.Add(new() { Id = "29", ConfigTableId = "2", ColumnName = "other_parent_id", LogicalType = "int64", LinkConfigTableId = "1" });
            catalog.ConfigPageColumns.Add(new() { Id = "2029", ConfigPageId = "201", ConfigColumnId = "29", IsShow = 1 });
            new CSharpGenerator().Generate(catalog, output, "Example.IndependentUi"); detail = File.ReadAllText(Path.Combine(output, "frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("关联目标父范围尚未明确",detail);await RunProbe(output,"unresolved");
        }
        finally { await TestDirectoryCleanup.DeleteAsync(output, boundary); }
    }
    /// <summary>真实编译挂载生成详情，网络宿主记录固定父范围和原数组回填。</summary>
    private static async Task RunProbe(string output,string scenario,[CallerFilePath]string file="")
    {
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"GeneratedRelationUiProbe.mjs"));start.ArgumentList.Add(output);start.ArgumentList.Add(scenario);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
