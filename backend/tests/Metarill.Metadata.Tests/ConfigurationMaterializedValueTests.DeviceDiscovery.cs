using System.Globalization;
using System.Text.Json;
using Metarill.Api.GenerationAssistant;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed partial class ConfigurationMaterializedValueTests
{
    /// <summary>实际仓储枚举设备全部项目，配置SQL不按工作项目或账号过滤；项目名称由真实表定向回读。</summary>
    [Fact]
    public async Task Assistant_device_discovery_enumerates_all_projects_and_uses_actual_project_names()
    {
        using var f=new Fixture();AddDevice(f);f.Get("yz_project",1)["name"]="项目甲";AddDiscoveryProject(f,2);var before=f.Snapshot();var reader=new GenerationAssistantDeviceScopeReader(f.Store);
        var result=await reader.DiscoverAsync(f.Authorization.RequireActor(),"11");
        Assert.Equal("11",result.DeviceId);Assert.Equal(new[]{"1","2"},result.Projects.Select(item=>item.ProjectId).Order(StringComparer.Ordinal));Assert.All(result.Projects,item=>Assert.Equal("configured",item.Status));
        Assert.Equal("项目甲",result.Projects.Single(item=>item.ProjectId=="1").ProjectName);Assert.Equal("项目2",result.Projects.Single(item=>item.ProjectId=="2").ProjectName);
        Assert.Equal(await reader.LoadAsync(f.Authorization.RequireActor(),"11"),result.Projects.Single(item=>item.ProjectId=="1").Scope);
        var configRead=Assert.Single(f.Provider.Reads,read=>read.Sql.Contains("FROM `yz_manager_devices_project`",StringComparison.Ordinal)&&!read.Sql.Contains("`yz_project_id`=",StringComparison.Ordinal));
        Assert.Contains("`yz_manager_devices_id`=",configRead.Sql,StringComparison.Ordinal);Assert.DoesNotContain("`yz_manager_id`=",configRead.Sql,StringComparison.Ordinal);Assert.DoesNotContain("yz_project_name",configRead.Sql,StringComparison.Ordinal);
        var projectRead=Assert.Single(f.Provider.Reads,read=>read.Sql.Contains("FROM `yz_project`",StringComparison.Ordinal));Assert.Contains("`id` IN (",projectRead.Sql,StringComparison.Ordinal);Assert.Equal(2,projectRead.Values.Count);
        Assert.Equal(before,f.Snapshot());Assert.All(f.Provider.Reads,read=>Assert.Null(read.Transaction));
        var json=JsonSerializer.Serialize(result);Assert.DoesNotContain("E:",json,StringComparison.Ordinal);Assert.DoesNotContain("not-a-real-token",json,StringComparison.Ordinal);
    }

    /// <summary>发现本身不转移权限；非管理员及外账号/墓碑设备都在读取项目配置前拒绝。</summary>
    [Theory]
    [InlineData("ordinary")][InlineData("anonymous")][InlineData("foreign")][InlineData("deleted")][InlineData("missing")]
    public async Task Assistant_device_discovery_rejects_untrusted_actor_or_device_before_projects(string kind)
    {
        using var f=new Fixture();AddDevice(f);var actor=f.Authorization.RequireActor();
        if(kind=="ordinary")actor=actor with{IsAdministrator=false};if(kind=="anonymous")actor=actor with{AccountId=0};
        if(kind=="foreign")f.Get("yz_manager_devices",11)["yz_manager_id"]=9L;if(kind=="deleted")f.Get("yz_manager_devices",11)["delete_no"]=DateTime.UtcNow;if(kind=="missing")f.Provider.Rows["yz_manager_devices"].Clear();
        var before=f.Snapshot();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new GenerationAssistantDeviceScopeReader(f.Store).DiscoverAsync(actor,"11"));
        Assert.Equal(kind is "ordinary" or "anonymous"?0:1,f.Provider.Reads.Count);Assert.Equal(before,f.Snapshot());
    }

    /// <summary>按原开关和路径完整性跳过整个项目，禁用端旧路径不参与配置有效性或摘要。</summary>
    [Theory]
    [InlineData("disabled","generation_disabled")][InlineData("backend-missing","enabled_path_missing")][InlineData("frontend-missing","enabled_path_missing")]
    [InlineData("backend-disabled",null)][InlineData("frontend-disabled",null)]
    public async Task Assistant_device_discovery_keeps_original_skip_rules(string kind,string? code)
    {
        using var f=new Fixture();AddDevice(f);var row=f.Get("yz_manager_devices_project",21);
        if(kind=="disabled"){row["is_java"]=false;row["is_uniapp"]=0;}
        if(kind=="backend-missing")row["java_path"]=" ";if(kind=="frontend-missing")row["uniapp_path"]=null;
        if(kind=="backend-disabled"){row["is_java"]=false;row["java_path"]="坏的禁用端路径";}
        if(kind=="frontend-disabled"){row["is_uniapp"]=false;row["uniapp_path"]="坏的禁用端路径";}
        var result=Assert.Single((await new GenerationAssistantDeviceScopeReader(f.Store).DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects);
        Assert.Equal(code is null?"configured":"skipped",result.Status);Assert.Equal(code,result.ErrorCode);Assert.Equal(code is null,result.Scope is not null);
        if(kind=="backend-disabled")Assert.Null(result.Scope!.BackendPathSha256);if(kind=="frontend-disabled")Assert.Null(result.Scope!.FrontendPathSha256);
    }

    /// <summary>单项坏配置明确失败而其他项目继续；坏账号不能过滤后沿用旧配置，错误不带路径或异常正文。</summary>
    [Theory]
    [InlineData("owner","configuration_owner_mismatch")][InlineData("flag","configuration_invalid")][InlineData("path","configuration_invalid")][InlineData("time","configuration_invalid")][InlineData("id","configuration_invalid")]
    public async Task Assistant_device_discovery_isolates_invalid_configuration_without_dropping_other_projects(string kind,string code)
    {
        using var f=new Fixture();AddDevice(f);AddDiscoveryProject(f,2);var row=f.Get("yz_manager_devices_project",102);
        if(kind=="owner")row["yz_manager_id"]=9L;if(kind=="flag")row["is_java"]=2;if(kind=="path")row["java_path"]="not-an-absolute-path";if(kind=="time")row["update_time"]="坏时间";if(kind=="id")row["id"]=-102L;
        var before=f.Snapshot();var results=(await new GenerationAssistantDeviceScopeReader(f.Store).DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects;
        Assert.Equal(2,results.Length);Assert.Equal("configured",results.Single(item=>item.ProjectId=="1").Status);var failed=results.Single(item=>item.ProjectId=="2");Assert.Equal("failed",failed.Status);Assert.Equal(code,failed.ErrorCode);Assert.Null(failed.Scope);Assert.Equal(before,f.Snapshot());
    }

    /// <summary>同项目只固定最新更新时间/ID，其他设备与墓碑不参与；最新缺路径时不能拿旧路径继续更新。</summary>
    [Theory]
    [InlineData("newer")][InlineData("tie")][InlineData("owner")][InlineData("missing-path")]
    public async Task Assistant_device_discovery_selects_latest_once_and_never_falls_back(string kind)
    {
        using var f=new Fixture();AddDevice(f);var original=f.Get("yz_manager_devices_project",21);
        var latest=new Dictionary<string,object?>(original,StringComparer.Ordinal){["id"]=30L,["update_time"]=new DateTime(2030,1,1)};
        if(kind=="owner")latest["yz_manager_id"]=9L;if(kind=="missing-path")latest["uniapp_path"]=" ";f.Add("yz_manager_devices_project",latest);
        foreach(var(mode,id) in new[]{("old",900L),("foreign-device",901L),("deleted",902L)})
        {
            var row=new Dictionary<string,object?>(original,StringComparer.Ordinal){["id"]=id,["update_time"]=new DateTime(2040,1,1)};
            if(mode=="old")row["update_time"]=kind=="tie"?new DateTime(2030,1,1):new DateTime(2025,1,1);
            if(mode=="foreign-device")row["yz_manager_devices_id"]=12L;if(mode=="deleted")row["delete_no"]=DateTime.UtcNow;f.Add("yz_manager_devices_project",row);
        }
        var result=Assert.Single((await new GenerationAssistantDeviceScopeReader(f.Store).DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects);
        Assert.Equal(kind=="tie"?"900":"30",result.ConfigurationId);Assert.Equal(kind=="owner"?"failed":kind=="missing-path"?"skipped":"configured",result.Status);
        if(kind=="owner")Assert.Equal("configuration_owner_mismatch",result.ErrorCode);if(kind=="missing-path")Assert.Equal("enabled_path_missing",result.ErrorCode);
    }

    /// <summary>真实项目不存在/删除只影响对应项，重名不合并；空名称使用规范ID而非冗余名称。</summary>
    [Theory]
    [InlineData("missing")][InlineData("deleted")][InlineData("same-name")][InlineData("blank-name")]
    public async Task Assistant_device_discovery_checks_actual_project_state_and_retains_duplicate_names(string kind)
    {
        using var f=new Fixture();AddDevice(f);AddDiscoveryProject(f,2);var project=f.Get("yz_project",2);
        if(kind=="missing")f.Provider.Rows["yz_project"].Remove(project);if(kind=="deleted")project["delete_no"]=DateTime.UtcNow;
        if(kind=="same-name"){project["name"]="同名项目";f.Get("yz_project",1)["name"]="同名项目";}if(kind=="blank-name")project["name"]=" ";
        var results=(await new GenerationAssistantDeviceScopeReader(f.Store).DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects;Assert.Equal(2,results.Length);
        var second=results.Single(item=>item.ProjectId=="2");Assert.Equal(kind is "missing" or "deleted"?"failed":"configured",second.Status);
        if(kind is "missing" or "deleted"){Assert.Equal("project_not_found",second.ErrorCode);Assert.Null(second.Scope);}if(kind=="blank-name")Assert.Equal("2",second.ProjectName);
    }

    /// <summary>无项目标记按原规则跳过，损坏标识明确失败，不能把原始坏值拼入路径或伪造有效Scope。</summary>
    [Theory]
    [InlineData(null,"skipped")][InlineData(" ","skipped")][InlineData("02","failed")][InlineData("../2","failed")]
    public async Task Assistant_device_discovery_does_not_use_invalid_project_ids(string? id,string status)
    {
        using var f=new Fixture();AddDevice(f);f.Get("yz_manager_devices_project",21)["yz_project_id"]=id;
        var result=Assert.Single((await new GenerationAssistantDeviceScopeReader(f.Store).DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects);
        Assert.Equal(status,result.Status);Assert.Null(result.Scope);Assert.Null(result.ProjectId);
    }

    /// <summary>空配置可明确发现为空，超限整批拒绝而非截断成部分成功；这些入口均不创建任务或写库。</summary>
    [Theory]
    [InlineData("empty")][InlineData("projects")][InlineData("rows")]
    public async Task Assistant_device_discovery_is_complete_or_explicitly_rejects_oversized_sets(string kind)
    {
        using var f=new Fixture();AddDevice(f);var rows=f.Provider.Rows["yz_manager_devices_project"];var original=rows[0];
        if(kind=="empty")rows.Clear();
        if(kind=="projects")for(var i=2;i<=1001;i++)rows.Add(new Dictionary<string,object?>(original,StringComparer.Ordinal){["id"]=10000L+i,["yz_project_id"]=(long)i});
        if(kind=="rows")for(var i=0;i<8192;i++)rows.Add(new Dictionary<string,object?>(original,StringComparer.Ordinal){["id"]=10000L+i});
        var reader=new GenerationAssistantDeviceScopeReader(f.Store);
        if(kind=="empty")Assert.Empty((await reader.DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects);
        else{await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.DiscoverAsync(f.Authorization.RequireActor(),"11"));Assert.Equal(2,f.Provider.Reads.Count);}
    }

    /// <summary>规范设备标识及取消在任何SQL前处理，不能借批量入口注入设备或忽略取消。</summary>
    [Fact]
    public async Task Assistant_device_discovery_rejects_invalid_device_and_cancellation_before_sql()
    {
        using var f=new Fixture();var reader=new GenerationAssistantDeviceScopeReader(f.Store);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.DiscoverAsync(f.Authorization.RequireActor(),"../11"));
        using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reader.DiscoverAsync(f.Authorization.RequireActor(),"11",cts.Token));Assert.Empty(f.Provider.Reads);
    }

    /// <summary>C65既有设备关联的公开合成固定摘要不能因共享实现提取而变化；常量由旧字段顺序UTF8封套独立计算。</summary>
    [Fact]
    public async Task Assistant_device_discovery_keeps_existing_device_association_digest_bytes()
    {
        using var f=new Fixture();AddDevice(f);var reader=new GenerationAssistantDeviceScopeReader(f.Store);
        var original=await reader.LoadAsync(f.Authorization.RequireActor(),"11");var discovered=Assert.Single((await reader.DiscoverAsync(f.Authorization.RequireActor(),"11")).Projects).Scope!;
        Assert.Equal("5fb078f5b3a052c7d29a7511415a5cea370cc5f47c515b6122d017706e183dc7",original.DeviceSha256);Assert.Equal("a3b15eebecd33873ae4de79c47d2b176e2bbb38cc3c335ded404e687113d6cd4",original.ConfigurationSha256);Assert.Equal(original,discovered);
    }

    /// <summary>只建立合成第二项目配置，不克隆生产Actor或把工作区选择当作跨项目认证。</summary>
    private static void AddDiscoveryProject(Fixture f,long project)
    {
        f.Add("yz_project",Row(("id",project),("name","项目"+project.ToString(CultureInfo.InvariantCulture))));
        f.Add("yz_manager_devices_project",new Dictionary<string,object?>(f.Get("yz_manager_devices_project",21),StringComparer.Ordinal){["id"]=100+project,["yz_project_id"]=project});
    }
}
