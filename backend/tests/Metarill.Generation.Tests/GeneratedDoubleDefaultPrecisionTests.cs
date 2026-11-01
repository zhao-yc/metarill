using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Runtime;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class GeneratedDoubleValueTests
{
    /// <summary>实际生成／严格编译／创建Service，并挂载原Vue检查新增默认、响应及主动null。</summary>
    [Fact]
    public async Task Adjacent_double_defaults_remain_distinct_in_generated_service_and_original_vue()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-double-default-precision");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(root,"generated");var catalog=GeneratorTests.Catalog();
        foreach(var(id,name,value)in new[]{("900","score","4.1045368012983762E-289"),("902","score_previous","4.1045368012983758E-289")})
        {
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId="1",ColumnName=name,Name=name=="score"?"测量上值":"测量下值",LogicalType="double",DefaultValue=value});catalog.ConfigPageColumns.Add(new(){Id=id+"1",ConfigPageId="101",ConfigColumnId=id,IsEdit=1,IsShow=1});
        }
        catalog.ConfigPages.Single(page=>page.Id=="101").IsImport=1;
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.GuardContract");await File.WriteAllTextAsync(Path.Combine(root,"Directory.Build.props"),"<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);var request=JsonSerializer.Deserialize("{\"name\":\"默认精度\"}",assembly.GetType("Example.GuardContract.Generated.DTOs.CustomerPage101CreateRequest",true)!,JsonDefaults.Options)!;
            using var run=new GeneratedMutationGuardRun(assembly);var result=await Invoke(run.Service([]),"Create101Async",request,null,run.Actor,CancellationToken.None);Assert.NotNull(result);
            foreach(var(name,bits)in new[]{("Score",0x0410_0000_0000_0000L),("ScorePrevious",0x040f_ffff_ffff_ffffL)})Assert.Equal(bits,BitConverter.DoubleToInt64Bits(Assert.IsType<double>(result.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)!.GetValue(result))));
            var parse=run.Service([]).GetType().GetMethod("ParseImport101Async",BindingFlags.Instance|BindingFlags.NonPublic)!;Assert.NotNull(parse);
            foreach(var(input,bits)in new[]{("4.1045368012983762E-289",0x0410_0000_0000_0000L),("4.1045368012983758E-289",0x040f_ffff_ffff_ffffL)})foreach(var numberToken in new[]{false,true})
            {
                var row=numberToken?JsonSerializer.Deserialize<JsonElement>("{\"测量上值\":"+input+"}"):JsonSerializer.SerializeToElement(new Dictionary<string,string>{{"测量上值",input}});object?[] args=parse.GetParameters().Length==4?[row,null,run.Actor,CancellationToken.None]:[row,null,run.Actor,null,false,CancellationToken.None];
                var parsed=(Task)parse.Invoke(run.Service([]),args)!;await parsed;var values=Assert.IsAssignableFrom<IReadOnlyDictionary<string,JsonElement>>(parsed.GetType().GetProperty("Result")!.GetValue(parsed));Assert.Equal(bits,BitConverter.DoubleToInt64Bits(JsonSerializer.Deserialize<double>(values["score"].GetRawText(),JsonDefaults.Options)));
            }
            Assert.Contains("write:Insert",run.Trace);var response=Path.Combine(root,"response.json");await File.WriteAllTextAsync(response,JsonSerializer.Serialize(result,JsonDefaults.Options));
            var detail=Assert.Single(Directory.EnumerateFiles(Path.Combine(output,"frontend/pages/yzp"),"*-detail-101.vue"));await RunDefaultPrecisionVue(detail,response);
        }
        finally{if(Directory.Exists(root))await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>复用真实原Vue3挂载工具；网络及表单底层控件受控，不冒充浏览器或持久化验收。</summary>
    private static async Task RunDefaultPrecisionVue(string detail,string response,[CallerFilePath]string source="")
    {
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!,"DoubleDefaultPrecisionVueProbe.mjs"));start.ArgumentList.Add(detail);start.ArgumentList.Add(response);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
