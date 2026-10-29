using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Cascades;
using Metarill.Core.Security;
using Metarill.Runtime.Exports;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实XLSX写入与原chooseImportFile函数的SheetJS读取；uni/FileReader仅为明确受控传输，不代表浏览器或HTTP。</summary>
public sealed class CrossTableExcelWorkbookTests
{
    /// <summary>生产XLSX与原选择函数完成文本往返，保留分隔符和精确大ID。</summary>
    [Fact]
    public async Task Original_excel_handler_preserves_partial_paths_whitespace_and_literal_text()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-workbook");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string Format(string[] names,string joiner="/")=>CrossTableExcelPath.Format(new(names.Select((name,index)=>new CrossTablePathNode((index+1).ToString(System.Globalization.CultureInfo.InvariantCulture),name,"level"+index)).ToArray(),""),3,joiner)!;
            var values=new[]{Format(["国甲"]),Format(["国甲","区甲"]),Format(["国甲","区甲","城市甲"]),Format(["国甲"]," "),Format(["国甲","区甲"],"->"),Format(["=SUM(1,2)"]),Format(["'原名"]),Format(["9007199254740993"])};
            var actor=new RuntimeActor(948,"工作簿合同",false,"9007199254740993","1");
            var service=new ExcelPageExportService(new(){RootDirectory=Path.Combine(directory,"storage")});
            var rows=values.Select((value,index)=>(IReadOnlyList<object?>)new object?[]{index+1,value,"9007199254740993"}).ToArray();
            var ticket=await service.CreateAsync(new("201","级联路径",["序号","级联","选择ID"],rows),actor);
            var content=await service.OpenAsync(ticket,actor);var book=Path.Combine(directory,"paths.xlsx");
            await using(content.Stream)await using(var output=File.Create(book))await content.Stream.CopyToAsync(output);
            using(var workbook=new XLWorkbook(book))
            {
                var sheet=workbook.Worksheet(1);
                for(var index=0;index<values.Length;index++)
                {Assert.Equal(values[index],sheet.Cell(index+2,2).GetString());Assert.Equal(XLDataType.Text,sheet.Cell(index+2,2).DataType);Assert.False(sheet.Cell(index+2,2).HasFormula);}
            }
            var expected=values.Select((value,index)=>new Dictionary<string,object?>{{"序号",index+1},{"级联",value},{"选择ID","9007199254740993"}}).ToArray();
            var expectedPath=Path.Combine(directory,"expected.json");await File.WriteAllTextAsync(expectedPath,JsonSerializer.Serialize(expected));
            var root=NonRedundantApplicationFixture.Root();var start=NonRedundantApplicationFixture.Start("node",root);
            foreach(var argument in new[]{Path.Combine(root,"backend/tests/Metarill.Generation.Tests/OriginalExcelImportProbe.mjs"),root,book,expectedPath})start.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(start);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
