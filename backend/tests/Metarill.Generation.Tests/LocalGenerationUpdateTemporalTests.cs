using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>普通与显式时间精度模型都依赖同一结构源码；删除文件及清单不能伪造可应用的当前候选。</summary>
    [Theory]
    [InlineData("src/Metarill.Core/Contracts/FileUploadLifetime.cs",true)]
    [InlineData("src/Metarill.Core/Contracts/BusinessCalendar.cs",true)]
    [InlineData("src/Metarill.Core/Contracts/BusinessCalendar.cs",false)]
    [InlineData("src/Metarill.Core/Metadata/MetadataColumnTypePolicy.cs",true)]
    [InlineData("src/Metarill.Core/Metadata/MetadataColumnTypePolicy.cs",false)]
    [InlineData("src/Metarill.Core/Metadata/MetadataCatalogComposition.cs",true)]
    [InlineData("src/Metarill.Data/Schema/MySqlSchemaPlanner.cs",true)]
    [InlineData("src/Metarill.Data/Schema/SchemaImportDraft.cs",true)]
    [InlineData("src/Metarill.Data/Schema/SchemaService.cs",true)]
    public void Candidate_requires_temporal_schema_runtime_before_touching_target(string path,bool explicitPrecision)
    {
        var next=Catalog();if(explicitPrecision)foreach(var column in next.ConfigColumns.Where(column=>column.LogicalType=="datetime")){column.ColumnType="datetime(0)";if(column.ColumnName is "insert_time" or "update_time")column.DefaultValue="now()";}
        var(target,candidate)=Packages(Catalog(),next);var updater=new LocalGenerationUpdater();Assert.True(updater.Preview(target,candidate,All).CanApply);var before=SourceSnapshot(target);
        var marker=Path.Combine(candidate,".metarill-package.json");var manifest=JsonNode.Parse(File.ReadAllText(marker))!;var entries=manifest["files"]!.AsArray();entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(candidate,path));File.WriteAllText(marker,manifest.ToJsonString(Json));
        var error=Assert.Throws<InvalidOperationException>(()=>updater.Preview(target,candidate,All));Assert.Contains(path,error.Message);Assert.Equal(before,SourceSnapshot(target));
    }
}
