using System.Reflection;
using System.Text.Json;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成并编译VO，证明文件值与普通派生名称分别保持原JSON及标量协议。</summary>
public sealed class NonRedundantFileValueTests
{
    /// <summary>文件ID数组不二次编码，空数组/null/空存储沿原VO；派生字段仍不变成物理文件列。</summary>
    [Fact]
    public async Task Compiled_vo_preserves_file_array_shapes_without_changing_other_outputs()
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_nrf_offline",true);var model=GenerationModel.Create(catalog,"Example.NonRedFiles");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single(page=>page.Source.Id=="101");var plan=NonRedundantReadCompilation.ForPage(model,table,page);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nonred-file-values");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var type=assembly.GetType("Example.NonRedFiles.Generated.VOs.MtNrfOfflineOrderPage101Vo",true)!;Assert.Equal(typeof(JsonElement?),type.GetProperty("Attachments")!.PropertyType);
            var row=plan.Outputs.ToDictionary(output=>output.Storage,output=>output.Type switch{"long"=>(object?)9007199254740993L,"long?"=>101L,"int?"=>2,"decimal?"=>10m,"string?"=>"原名称",_=>null},StringComparer.Ordinal);
            var method=type.GetMethod("FromReadRow",BindingFlags.Public|BindingFlags.Static)!;
            foreach(var (raw,kind,count) in new (string?,JsonValueKind,int)[]{("[\"9007199254740993\",\"9007199254740994\"]",JsonValueKind.Array,2),("[]",JsonValueKind.Array,0),(null,JsonValueKind.Null,0),("",JsonValueKind.Null,0),("null",JsonValueKind.Null,0)})
            {
                row["attachments"]=raw;var vo=method.Invoke(null,[row])!;var json=JsonSerializer.SerializeToElement(vo);var value=json.GetProperty("attachments");Assert.Equal(kind,value.ValueKind);if(kind==JsonValueKind.Array){Assert.Equal(count,value.GetArrayLength());if(count>0)Assert.Equal("9007199254740993",value[0].GetString());}Assert.Equal("原名称",json.GetProperty("customerNotes").GetString());
            }
            row["attachments"]="非法JSON";Assert.IsType<JsonException>(Assert.Throws<TargetInvocationException>(()=>method.Invoke(null,[row])).InnerException);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>关系数组与派生文件仍保留独立门槛，物理文件开放不能被用于绕开未实现的多值读取。</summary>
    [Theory]
    [InlineData("multi")][InlineData("derived")]
    public void Unproved_array_storage_is_not_admitted_as_physical_files(string scenario)
    {
        var catalog=NonRedundantApplicationFixture.Catalog("mt_nrf_offline",true);var column=catalog.ConfigColumns.Single(column=>column.Id=="112");
        if(scenario=="multi")column.ControlKind="MultiCheckBoxDatabase";
        else{column.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);column.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer.attachments");}
        var model=GenerationModel.Create(catalog,"Example.NonRedFiles");var table=model.Tables.Single(table=>table.Source.Id=="1");Assert.Throws<InvalidOperationException>(()=>NonRedundantReadCompilation.ForPage(model,table,table.Pages[0]));
    }
}
