using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>固定逻辑父范围不能冒充来源逻辑条件及完整前端；生成器和完整包须继续原子拒绝。</summary>
public sealed class NonRedundantCrossTableSparseGateTests
{
    /// <summary>来源逻辑ID常量条件尚未接通，不能退回稀疏物理比较或生成半个工程。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Sparse_storage_does_not_open_unimplemented_source_conditions_or_full_packages(string type)
    {
        var catalog=CrossTableReadTests.Catalog("sparse_gate",type,type);var source=catalog.ConfigTables.Single(table=>table.Id=="2");source.IsRongyu=0;catalog.ConfigColumns.Single(column=>column.Id=="25").LogicalType=type;
        catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND country_id="+(type=="string"?"'9007199254742001'":"9007199254742001");var before=JsonSerializer.Serialize(catalog);var plan=MetadataStoragePolicy.ForTable(catalog,source);Assert.Equal(new[]{"area_id","country_id","place_id"},plan.PhysicalColumns.Where(column=>column.ColumnName is "country_id" or "area_id" or "place_id").Select(column=>column.ColumnName).Order(StringComparer.Ordinal));
        var model=GenerationModel.Create(catalog,"Example.SparseGate");Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId=="201"&&gap.Reason.Contains("非冗余"));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-sparse-gate");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,output,"Example.SparseGate"));Assert.False(Directory.Exists(output));
        Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,output,"Example.SparseGate",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));Assert.Equal(before,JsonSerializer.Serialize(catalog));
    }
}
