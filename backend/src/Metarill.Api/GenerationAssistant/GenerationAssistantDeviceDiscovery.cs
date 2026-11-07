using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Api.GenerationAssistant;

/// <summary>仅为服务器批量准备发现配置，configured不表示已绑定、授权、入队或应用；不携带本机路径。</summary>
public sealed record AssistantDeviceProjectSelection(string? ProjectId,string ProjectName,string? ConfigurationId,string Status,string? ErrorCode,AssistantDeviceScope? Scope);
public sealed record AssistantDeviceProjectDiscovery(string DeviceId,AssistantDeviceProjectSelection[] Projects);

public sealed partial class GenerationAssistantDeviceScopeReader
{
    /// <summary>真实平台管理员枚举自己活动设备的全部项目；不按当前工作项目过滤，也不以发现结果替代逐项目真实认证。</summary>
    public async Task<AssistantDeviceProjectDiscovery> DiscoverAsync(RuntimeActor actor,string deviceId,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var device=Id(deviceId);
        if(actor.AccountId<=0||!actor.IsAdministrator)throw new UnauthorizedAccessException("批量运行时发现仅限当前平台管理员。");
        var devices=await store.ReadProjectedAsync("yz_manager_devices",["id","yz_manager_id","token"],new Dictionary<string,object?>{{"id",device},{"yz_manager_id",actor.AccountId}},token);
        var owned=devices.SingleOrDefault()??throw new UnauthorizedAccessException("当前设备不存在或不属于当前账号。");
        var rows=await store.ReadProjectedAsync("yz_manager_devices_project",["id","yz_manager_id","yz_project_id","yz_manager_devices_id","is_java","java_path","is_uniapp","uniapp_path","update_time"],new Dictionary<string,object?>{{"yz_manager_devices_id",device}},token);
        // 不截断或返回部分成功；超出有界队列的项目配置必须整批明确拒绝，避免漏掉后半部分项目。
        if(rows.Count>8192)throw new InvalidOperationException("设备配置过多，请先整理重复配置；未创建批量任务。");
        var groups=rows.GroupBy(row=>SqlRowStore.Text(row,"yz_project_id"),StringComparer.Ordinal).ToArray();
        if(groups.Length>1000)throw new InvalidOperationException("设备项目超过批量上限；未创建批量任务。");
        var result=new List<AssistantDeviceProjectSelection>();
        foreach(var group in groups)
        {
            token.ThrowIfCancellationRequested();string? configurationId=null;string? projectId=null;
            try
            {
                if(string.IsNullOrWhiteSpace(group.Key))
                {
                    result.Add(new(null,"",null,"skipped","project_not_configured",null));continue;
                }
                _=Id(group.Key);projectId=group.Key;
                // 同项目只固定原最新配置；坏账号、坏时间或坏路径不能回退到旧记录继续写入。
                var row=group.OrderByDescending(Updated).ThenByDescending(value=>SqlRowStore.Id(value)).First();
                configurationId=SqlRowStore.Text(row,"id");_=Id(configurationId);
                if(SqlRowStore.Id(row,"yz_manager_id")!=actor.AccountId)
                {
                    result.Add(new(projectId,projectId,configurationId,"failed","configuration_owner_mismatch",null));continue;
                }
                var backend=Enabled(row,"is_java");var frontend=Enabled(row,"is_uniapp");
                if(!backend&&!frontend)
                {
                    result.Add(new(projectId,projectId,configurationId,"skipped","generation_disabled",null));continue;
                }
                if(backend&&string.IsNullOrWhiteSpace(SqlRowStore.Text(row,"java_path"))||frontend&&string.IsNullOrWhiteSpace(SqlRowStore.Text(row,"uniapp_path")))
                {
                    result.Add(new(projectId,projectId,configurationId,"skipped","enabled_path_missing",null));continue;
                }
                result.Add(new(projectId,projectId,configurationId,"configured",null,ConfigurationScope(actor.AccountId,projectId,deviceId,owned,row)));
            }
            catch(Exception error) when(error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                // 只归类明确的配置输入损坏，取消/数据库读取故障继续向上抛出，不伪装逐项目成功。
                result.Add(new(projectId,projectId??"",configurationId,"failed","configuration_invalid",null));
            }
        }
        var ids=result.Where(item=>item.ProjectId is not null).Select(item=>Id(item.ProjectId!)).Distinct().ToArray();
        var projects=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var chunk in ids.Chunk(1000))
            foreach(var project in await store.ReadProjectedByIdsAsync("yz_project",["id","name"],chunk,token:token))
            {
                var id=SqlRowStore.Text(project,"id");var name=SqlRowStore.Text(project,"name");projects.Add(id,string.IsNullOrWhiteSpace(name)?id:name);
            }
        return new(deviceId,result.Select(item=>item.ProjectId is{} id&&projects.TryGetValue(id,out var name)?item with{ProjectName=name}:item.Status=="configured"?item with{Status="failed",ErrorCode="project_not_found",Scope=null}:item)
            .OrderBy(item=>item.ProjectName,StringComparer.Ordinal).ThenBy(item=>item.ProjectId,StringComparer.Ordinal).ToArray());
    }
}
