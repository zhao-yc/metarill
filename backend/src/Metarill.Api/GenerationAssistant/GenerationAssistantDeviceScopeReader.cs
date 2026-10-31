using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Generation;
using Metarill.Runtime.Persistence;

namespace Metarill.Api.GenerationAssistant;

/// <summary>设备摘要只能来自实际活动设备及项目配置，不把客户端设备选择、目录或摘要当作授权。</summary>
public sealed partial class GenerationAssistantDeviceScopeReader(SqlRowStore store)
{
    /// <summary>一次性设备绑定先验证真实设备拥有者，再取本项目原最新配置；读取不修改任何配置或结构。</summary>
    public async Task<AssistantDeviceScope> LoadAsync(RuntimeActor actor,string deviceId,CancellationToken token=default)
    {
        var device=Id(deviceId);var project=Id(actor.ProjectId);
        if(actor.AccountId<=0)throw new UnauthorizedAccessException("登录超时");
        var devices=await store.ReadProjectedAsync("yz_manager_devices",["id","yz_manager_id","token"],
            new Dictionary<string,object?>{{"id",device},{"yz_manager_id",actor.AccountId}},token);
        var owned=devices.SingleOrDefault()??throw new UnauthorizedAccessException("当前设备不存在或不属于当前账号。");
        // 先按设备及项目取原配置，不过滤配置账号后回退到旧记录；最新配置账号不一致时明确拒绝。
        var rows=await store.ReadProjectedAsync("yz_manager_devices_project",["id","yz_manager_id","yz_project_id","yz_manager_devices_id","is_java","java_path","is_uniapp","uniapp_path","update_time"],
            new Dictionary<string,object?>{{"yz_manager_devices_id",device},{"yz_project_id",project}},token);
        var row=rows.OrderByDescending(Updated).ThenByDescending(value=>SqlRowStore.Id(value)).FirstOrDefault()
            ??throw new InvalidOperationException("当前设备未配置该项目。");
        return ConfigurationScope(actor.AccountId,actor.ProjectId,deviceId,owned,row);
    }
    /// <summary>单项目与批量发现共用真实配置摘要；接收已查库的账号/项目值，不构造或修改认证Actor。</summary>
    private static AssistantDeviceScope ConfigurationScope(long accountId,string projectId,string deviceId,IReadOnlyDictionary<string,object?> owned,IReadOnlyDictionary<string,object?> row)
    {
        if(SqlRowStore.Id(row,"yz_manager_id")!=accountId)throw new UnauthorizedAccessException("项目配置账号与当前设备拥有者不一致。");
        var parts=new GenerationRuntimeParts(Enabled(row,"is_java"),Enabled(row,"is_uniapp"));
        if(!parts.Backend&&!parts.Frontend)throw new InvalidOperationException("当前设备项目未启用生成。");
        string? PathHash(string field,bool enabled)
        {
            if(!enabled)return null;var path=SqlRowStore.Text(row,field);
            if(string.IsNullOrWhiteSpace(path))throw new InvalidOperationException("当前设备项目缺少已启用端的路径。");
            return DeviceProjectPath.Fingerprint(path);
        }
        var backend=PathHash("java_path",parts.Backend);var frontend=PathHash("uniapp_path",parts.Frontend);
        var configurationId=SqlRowStore.Text(row,"id");_=Id(configurationId);
        var deviceHash=Hash(new{deviceId,AccountId=accountId,token=SqlRowStore.Text(owned,"token")});
        var configurationHash=Hash(new{deviceId,AccountId=accountId,projectId,configurationId,parts,backend,frontend});
        return new(accountId,deviceId,projectId,configurationId,deviceHash,configurationHash,parts,backend,frontend);
    }
    /// <summary>规范主键使用固定十进制字符串，禁止点层级、前导零或越界值进入状态文件名。</summary>
    private static long Id(string value)
    {
        if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||id.ToString(CultureInfo.InvariantCulture)!=value)throw new InvalidOperationException("设备或项目标识无效。");return id;
    }
    /// <summary>原整数开关仅解释空/0/1，损坏配置不猜测为允许写入。</summary>
    private static bool Enabled(IReadOnlyDictionary<string,object?> row,string name)
    {
        // 驱动可能将整数位开关读为CLR bool，仍仅解释等价逻辑0/1，不按品牌分支。
        if(row.GetValueOrDefault(name) is bool boolean)return boolean;
        var text=SqlRowStore.Text(row,name);if(text.Length==0)return false;
        if(!int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)||value is not(0 or 1))throw new InvalidOperationException("设备生成开关配置无效。");return value==1;
    }
    /// <summary>原配置以更新时间倒序、主键倒序选最新，空更新时间排在已有时间之后。</summary>
    private static DateTime Updated(Dictionary<string,object?> row)=>row.GetValueOrDefault("update_time") is{} value?Convert.ToDateTime(value,CultureInfo.InvariantCulture):DateTime.MinValue;
    /// <summary>只在内存计算真实配置摘要；设备凭据和目录不写HTTP响应、日志或队列状态。</summary>
    private static string Hash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}
