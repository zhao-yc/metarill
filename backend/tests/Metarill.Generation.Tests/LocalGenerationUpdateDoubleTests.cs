using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>当前完整包的公共JSON数值依赖不可被删；同时验证含double和普通字符串业务模型。</summary>
    [Theory]
    [InlineData("src/Metarill.Core/Contracts/FiniteDoubleJsonConverter.cs",true)]
    [InlineData("src/Metarill.Core/Contracts/FiniteDoubleJsonConverter.cs",false)]
    [InlineData("src/Metarill.Core/Contracts/ExactNumericValue.cs",true)]
    [InlineData("src/Metarill.Core/Contracts/ExactNumericValue.cs",false)]
    public void Candidate_requires_complete_numeric_runtime_without_touching_target(string path,bool withDouble)
    {
        var next=Catalog();if(withDouble){next.ConfigColumns.Single(column=>column.ColumnName=="name").LogicalType="double";next.ConfigColumns.Single(column=>column.ColumnName=="name").ControlKind="";}var(target,candidate)=Packages(Catalog(),next);var updater=new LocalGenerationUpdater();Assert.True(updater.Preview(target,candidate,All).CanApply);var before=SourceSnapshot(target);
        var marker=Path.Combine(candidate,".metarill-package.json");var manifest=JsonNode.Parse(File.ReadAllText(marker))!;var entries=manifest["files"]!.AsArray();entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==path));File.Delete(Path.Combine(candidate,path));File.WriteAllText(marker,manifest.ToJsonString(Json));var error=Assert.Throws<InvalidOperationException>(()=>updater.Preview(target,candidate,All));Assert.Contains(path,error.Message);Assert.Equal(before,SourceSnapshot(target));
    }
}
