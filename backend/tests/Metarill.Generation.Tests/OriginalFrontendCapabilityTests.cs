using System.Runtime.CompilerServices;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>保留原UI后未实现能力必须明确阻止完整打包，不能继续声称只读占位已经可用。</summary>
public sealed class OriginalFrontendCapabilityTests
{
    [Theory]
    [InlineData("CascadeTree",false,"detail")]
    [InlineData("FileLibrary",true,"filter")]
    public async Task Unimplemented_backend_keeps_gap_evidence_and_blocks_full_application(string kind,bool search,string usage)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-capabilities");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=GeneratorTests.Catalog();var column=catalog.ConfigColumns.Single(column=>column.ColumnName=="remark");column.ControlKind=kind;
            if(kind=="CascadeTree")column.ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"customer\",\"idField\":\"id\",\"nameField\":\"name\",\"parentField\":\"parent_id\"}}";
            var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==column.Id);field.Extra["isReadOnly"]=JsonSerializer.SerializeToElement(1);
            if(search)field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            var result=new CSharpGenerator().Generate(catalog,Path.Combine(output,"fragments"),"Example.Gaps");
            using var gaps=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"fragments/generation-capabilities.json")));
            Assert.Contains(gaps.RootElement.GetProperty("unsupportedControls").EnumerateArray(),gap=>gap.GetProperty("column").GetString()=="remark"&&gap.GetProperty("usage").GetString()==usage);
            Assert.Contains(result.Warnings,warning=>warning.Contains("remark",StringComparison.Ordinal)&&warning.Contains(usage,StringComparison.Ordinal));
            var full=Path.Combine(output,"full");var error=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,full,"Example.Gaps",SourceRoot()));
            Assert.Contains("未完成的后端能力",error.Message);Assert.False(Directory.Exists(full));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    private static string SourceRoot([CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../.."));
}
