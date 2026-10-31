using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际生成原详情并挂载真实Vue/uni-easyinput；只模拟请求及平台picker事件协议。</summary>
public sealed class OriginalTimeInputBindingTests
{
    /// <summary>清空/微秒输入及选择确认进入原整entity保存，只读迟到事件拒绝；缺绑定阴性必须重现旧值。</summary>
    [Theory]
    [InlineData("standalone")][InlineData("embedded")][InlineData("readonly")][InlineData("missing-binding")]
    public async Task Actual_original_time_input_events_reach_saved_entity_without_changing_controls(string scenario)
    {
        var catalog=TimeValueNativePreparation.Catalog();var model=GenerationModel.Create(catalog,"Example.TimeInput");
        var table=model.Tables.Single(value=>value.Source.TableName=="sample_contract");var page=table.Pages.Single(value=>value.Source.Id=="81002");
        var detail=OriginalUniappDetailRenderer.Render(model,table,page,ChildrenCompilation.Compile(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-time-input");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(output,"detail.vue"),detail);
            await Run(output,scenario);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>有限时长执行同仓库Vue探针；超时只终止本次子进程，不留下后台消费者。</summary>
    private static async Task Run(string output,string scenario,[CallerFilePath]string file="")
    {
        var info=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        info.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"OriginalTimeInputBindingProbe.mjs"));info.ArgumentList.Add(output);info.ArgumentList.Add(scenario);
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw new TimeoutException("原时间输入Vue探针超时；本次进程已结束。");}
        Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
