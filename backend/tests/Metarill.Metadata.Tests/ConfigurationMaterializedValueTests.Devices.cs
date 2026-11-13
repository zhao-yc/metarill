using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Api.GenerationAssistant;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>实际SQL先验证设备账号，再限定设备/项目配置；设备自身的项目标记不替代设备拥有权。</summary>
    [Fact]
    public async Task Assistant_device_scope_reads_real_owned_device_and_fixed_current_project_configuration()
    {
        using var f=new Fixture();AddDevice(f);var before=f.Snapshot();var reader=new GenerationAssistantDeviceScopeReader(f.Store);
        var result=await reader.LoadAsync(f.Authorization.RequireActor(),"11");
        Assert.Equal(8,result.AccountId);Assert.Equal("1",result.ProjectId);Assert.Equal("11",result.DeviceId);Assert.Equal("21",result.ConfigurationId);
        Assert.True(result.Parts.Backend);Assert.True(result.Parts.Frontend);Assert.Equal(DeviceProjectPath.Fingerprint("E:/Repo"),result.BackendPathSha256);
        Assert.Equal(DeviceProjectPath.Fingerprint("E:/Repo/frontend"),result.FrontendPathSha256);Assert.Equal(before,f.Snapshot());
        Assert.Equal(2,f.Provider.Reads.Count);Assert.All(f.Provider.Reads,read=>Assert.Null(read.Transaction));
        Assert.Contains("`yz_manager_id`=",f.Provider.Reads[0].Sql,StringComparison.Ordinal);Assert.DoesNotContain("`yz_project_id`=",f.Provider.Reads[0].Sql,StringComparison.Ordinal);
        Assert.Contains("`yz_manager_devices_id`=",f.Provider.Reads[1].Sql,StringComparison.Ordinal);Assert.Contains("`yz_project_id`=",f.Provider.Reads[1].Sql,StringComparison.Ordinal);
        var json=JsonSerializer.Serialize(result);Assert.DoesNotContain("E:",json,StringComparison.Ordinal);Assert.DoesNotContain("not-a-real-token",json,StringComparison.Ordinal);
    }
    /// <summary>外账号/软删/缺失设备在第一笔读后拒绝，不能再读取项目配置。</summary>
    [Theory]
    [InlineData("foreign")][InlineData("deleted")][InlineData("missing")]
    public async Task Assistant_device_scope_rejects_unowned_or_inactive_device_before_configuration(string kind)
    {
        using var f=new Fixture();AddDevice(f);
        if(kind=="foreign")f.Get("yz_manager_devices",11)["yz_manager_id"]=9L;
        if(kind=="deleted")f.Get("yz_manager_devices",11)["delete_no"]=DateTime.UtcNow;
        if(kind=="missing")f.Provider.Rows["yz_manager_devices"].Clear();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new GenerationAssistantDeviceScopeReader(f.Store).LoadAsync(f.Authorization.RequireActor(),"11"));
        Assert.Single(f.Provider.Reads);
    }
    /// <summary>最新配置先按原更新时间/主键排序，不被其他项目、设备或墓碑覆盖；不能过滤坏账号后退到旧配置。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Assistant_device_scope_uses_original_latest_configuration_and_rejects_wrong_manager(bool wrongManager)
    {
        using var f=new Fixture();AddDevice(f);var original=f.Get("yz_manager_devices_project",21);
        foreach(var(mode,id) in new[]{("old",900L),("new",30L),("foreign",901L),("device",902L),("deleted",903L)})
        {
            var row=new Dictionary<string,object?>(original,StringComparer.Ordinal){["id"]=id,["update_time"]=mode=="old"?new DateTime(2025,1,1):new DateTime(2030,1,1)};
            if(mode=="foreign")row["yz_project_id"]=2L;if(mode=="device")row["yz_manager_devices_id"]=12L;if(mode=="deleted")row["delete_no"]=DateTime.UtcNow;
            if(mode=="new"&&wrongManager)row["yz_manager_id"]=9L;f.Add("yz_manager_devices_project",row);
        }
        var reader=new GenerationAssistantDeviceScopeReader(f.Store);
        if(wrongManager)await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.LoadAsync(f.Authorization.RequireActor(),"11"));
        else Assert.Equal("30",(await reader.LoadAsync(f.Authorization.RequireActor(),"11")).ConfigurationId);
    }
    /// <summary>设备重置与路径/开关变化分别改变相应摘要；仅分隔符及Windows ASCII大小写等价不会误改目录身份。</summary>
    [Theory]
    [InlineData("reset")][InlineData("path")][InlineData("flags")][InlineData("equivalent")]
    public async Task Assistant_device_scope_fingerprints_follow_actual_authority_and_enabled_paths(string kind)
    {
        using var f=new Fixture();AddDevice(f);var reader=new GenerationAssistantDeviceScopeReader(f.Store);var first=await reader.LoadAsync(f.Authorization.RequireActor(),"11");
        var row=f.Get("yz_manager_devices_project",21);
        if(kind=="reset")f.Get("yz_manager_devices",11)["token"]="another-not-real-token";
        if(kind=="path")row["java_path"]="E:/Other";
        if(kind=="flags"){row["is_java"]=0;row["java_path"]="相对路径在禁用端不被使用";}
        if(kind=="equivalent"){row["java_path"]="e:/repo/";row["uniapp_path"]="e:/REPO//frontend/";}
        var second=await reader.LoadAsync(f.Authorization.RequireActor(),"11");
        Assert.Equal(kind!="reset",first.DeviceSha256==second.DeviceSha256);
        Assert.Equal(kind is "reset" or "equivalent",first.ConfigurationSha256==second.ConfigurationSha256);
        if(kind=="flags"){Assert.Null(second.BackendPathSha256);Assert.False(second.Parts.Backend);Assert.True(second.Parts.Frontend);}
    }
    /// <summary>未启用、已启用却缺路径、坏开关和非明确目录不能创建绑定范围。</summary>
    [Theory]
    [InlineData("disabled")][InlineData("backend-missing")][InlineData("frontend-missing")][InlineData("flag")][InlineData("relative")][InlineData("missing")]
    public async Task Assistant_device_scope_rejects_invalid_or_unconfigured_generation(string kind)
    {
        using var f=new Fixture();AddDevice(f);var row=f.Get("yz_manager_devices_project",21);
        if(kind=="disabled"){row["is_java"]=0;row["is_uniapp"]=0;}
        if(kind=="backend-missing")row["java_path"]=" ";if(kind=="frontend-missing")row["uniapp_path"]=null;
        if(kind=="flag")row["is_java"]=2;if(kind=="relative")row["java_path"]="relative/path";if(kind=="missing")f.Provider.Rows["yz_manager_devices_project"].Clear();
        var before=f.Snapshot();var error=await Record.ExceptionAsync(()=>new GenerationAssistantDeviceScopeReader(f.Store).LoadAsync(f.Authorization.RequireActor(),"11"));
        Assert.True(error is ArgumentException or InvalidOperationException);Assert.Equal(before,f.Snapshot());
    }
    /// <summary>实际驱动可返回bool，仍与原整数0/1开关一致，不使用禁用端旧路径。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Assistant_device_scope_accepts_driver_boolean_flags(bool backend,bool frontend)
    {
        using var f=new Fixture();AddDevice(f);var row=f.Get("yz_manager_devices_project",21);row["is_java"]=backend;row["is_uniapp"]=frontend;
        var result=await new GenerationAssistantDeviceScopeReader(f.Store).LoadAsync(f.Authorization.RequireActor(),"11");Assert.Equal(backend,result.Parts.Backend);Assert.Equal(frontend,result.Parts.Frontend);Assert.Equal(backend,result.BackendPathSha256 is not null);Assert.Equal(frontend,result.FrontendPathSha256 is not null);
    }

    /// <summary>状态文件用规范十进制ID，坏设备标识在任何查询前失败。</summary>
    [Theory]
    [InlineData("../11")][InlineData("011")][InlineData("0")][InlineData("9223372036854775808")]
    public async Task Assistant_device_scope_rejects_unsafe_ids_before_sql(string id)
    {
        using var f=new Fixture();await Assert.ThrowsAsync<InvalidOperationException>(()=>new GenerationAssistantDeviceScopeReader(f.Store).LoadAsync(f.Authorization.RequireActor(),id));Assert.Empty(f.Provider.Reads);
    }
    /// <summary>共享向量核对规范目录和SHA；原始JSON已拒绝的坏UTF16不被当成合法路径。</summary>
    [Fact]
    public void Assistant_device_paths_match_shared_node_vectors()
    {
        var root=DeviceSourceRoot();using var vectors=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tools/metarill-agent-cli/test/device-path-vectors.json")));
        foreach(var row in vectors.RootElement.EnumerateArray())
        {
            string? raw;
            try{raw=row.GetProperty("path").GetString();}catch(InvalidOperationException){Assert.Equal(JsonValueKind.Null,row.GetProperty("normalized").ValueKind);continue;}
            if(row.GetProperty("normalized").ValueKind==JsonValueKind.Null){Assert.ThrowsAny<ArgumentException>(()=>DeviceProjectPath.Fingerprint(raw!));continue;}
            Assert.Equal(row.GetProperty("normalized").GetString(),DeviceProjectPath.Normalize(raw!));Assert.Matches("^[a-f0-9]{64}$",DeviceProjectPath.Fingerprint(raw!));
        }
        Assert.ThrowsAny<ArgumentException>(()=>DeviceProjectPath.Fingerprint("/root/"+(char)0xd800+"next"));
        Assert.Equal(DeviceProjectPath.Fingerprint("C:/Work/Repo"),DeviceProjectPath.Fingerprint(@"c:\work\repo\"));
        Assert.NotEqual(DeviceProjectPath.Fingerprint("/Work/Repo"),DeviceProjectPath.Fingerprint("/work/Repo"));
    }
    /// <summary>只从当前源码定位共享公开向量，不读取私有设置。</summary>
    private static string DeviceSourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));

    /// <summary>本夹具全为合成配置；设备项目刻意与当前工作项目不同，证明拥有权不由所选项目代替。</summary>
    private static void AddDevice(Fixture f)
    {
        f.Add("yz_manager_devices",Row(("id",11L),("yz_manager_id",8L),("yz_project_id",999L),("token","not-a-real-token")));
        f.Add("yz_manager_devices_project",Row(("id",21L),("yz_manager_id",8L),("yz_project_id",1L),("yz_manager_devices_id",11L),("is_java",1),("java_path",@"E:\Repo"),("is_uniapp",1),("uniapp_path",@"E:\Repo\frontend"),("update_time",new DateTime(2026,1,1))));
    }
}
