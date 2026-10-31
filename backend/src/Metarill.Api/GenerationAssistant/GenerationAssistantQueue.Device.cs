using System.Globalization;
using Metarill.Generation;
namespace Metarill.Api.GenerationAssistant;

public sealed partial class GenerationAssistantQueue
{
    /// <summary>关联单独持久化，不改变已有v1核心绑定摘要；真实拥有权/项目授权必须由调用方在同请求复核。</summary>
    public AssistantDeviceBindingView AssociateDevice(AssistantOwner owner,AssistantDeviceBindRequest request,AssistantDeviceScope current)=>Locked(()=>
    {
        RequireOwner(owner,true);DeviceScope(owner,current);Uuid(request.BindingId);
        if(request.DeviceId!=current.DeviceId)throw new UnauthorizedAccessException("所选设备不属于当前服务端配置。");
        if(request.RuntimePartsProtocol!=2)throw new InvalidOperationException("批量同步要求支持分端范围的固定CLI。");
        RequireHash(request.ExpectedDeviceSha256);RequireHash(request.ExpectedConfigurationSha256);
        if(request.ExpectedDeviceSha256!=current.DeviceSha256||request.ExpectedConfigurationSha256!=current.ConfigurationSha256)throw new InvalidOperationException("设备配置已变化，请重新核对并确认关联。");
        RequireHash(request.BackendPathSha256);RequireHash(request.FrontendPathSha256);
        if(current.Parts.Backend&&request.BackendPathSha256!=current.BackendPathSha256||current.Parts.Frontend&&request.FrontendPathSha256!=current.FrontendPathSha256)throw new UnauthorizedAccessException("本机绑定目录与原设备配置不一致。");
        var binding=ReadBinding(request.BindingId);RequireScope(binding,owner);
        if(binding.Mode!="apply"||!binding.AllowedActions.Contains("sync-runtime",StringComparer.Ordinal))throw new UnauthorizedAccessException("本机绑定未允许写入运行时。");
        var name=DeviceName(owner.AccountId,current.DeviceId,current.ProjectId);var exists=File.Exists(Safe(name));
        var value=new AssistantDeviceAssociation(1,current,binding.BindingId,binding.GrantId,binding.LocalBindingSha256,"");value=value with{IntegritySha256=Hash(value)};
        if(exists)
        {
            var old=ReadDeviceAssociation(owner.AccountId,current.DeviceId,current.ProjectId);
            if(old.Scope.AccountId!=owner.AccountId)throw new UnauthorizedAccessException("设备关联不属于当前账号。");
            if(old.IntegritySha256==value.IntegritySha256)return DeviceView(old);
            if(request.ExpectedAssociationSha256!=old.IntegritySha256)throw new InvalidOperationException("设备项目已有其他关联，请先回读并明确提交旧摘要。");
        }
        else
        {
            if(request.ExpectedAssociationSha256 is not null)throw new InvalidOperationException("旧设备关联已不存在，不能按过时摘要替换。");
            if(Directory.GetFiles(options.RootDirectory,"device-*.json").Length>=options.MaxBindings)throw new InvalidOperationException("设备关联达到保留上限。");
        }
        Save(name,value);return DeviceView(value);
    });

    /// <summary>Web或同项目Agent回读脱敏关联供核查；不在这里伪造有效grant，仍需调用方逐请求验证。</summary>
    public AssistantDeviceBindingView DeviceBinding(AssistantOwner owner,AssistantDeviceScope current)=>Locked(()=>
    {
        RequireOwner(owner,owner.GrantId is not null);DeviceScope(owner,current);var value=ReadDeviceAssociation(owner.AccountId,current.DeviceId,current.ProjectId);
        if(value.Scope!=current)throw new UnauthorizedAccessException("设备或项目配置已变化，请重新核对本机绑定。");
        return DeviceView(value);
    });
    /// <summary>当前拥有者可回读旧摘要供明确CAS重绑；不匹配当前配置的关联仍不能用于运行时任务。</summary>
    public AssistantDeviceBindingView? FindDeviceBinding(AssistantOwner owner,AssistantDeviceScope current)=>Locked(()=>
    {
        RequireOwner(owner,owner.GrantId is not null);DeviceScope(owner,current);
        if(!File.Exists(Safe(DeviceName(owner.AccountId,current.DeviceId,current.ProjectId))))return null;
        var value=ReadDeviceAssociation(owner.AccountId,current.DeviceId,current.ProjectId);
        if(value.Scope.AccountId!=owner.AccountId)throw new UnauthorizedAccessException("设备关联不属于当前账号。");
        return DeviceView(value) with{MatchesCurrentConfiguration=value.Scope==current};
    });
    /// <summary>规范正整数限制文件名，不能从设备ID或项目ID拼出任意路径。</summary>
    private static void DeviceId(string value)
    {
        if(!long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||id.ToString(CultureInfo.InvariantCulture)!=value)throw new InvalidOperationException("设备关联标识无效。");
    }
    /// <summary>只验证已由服务端构造的当前归属/范围；该方法不把摘要当作认证或数据库证明。</summary>
    private static void DeviceScope(AssistantOwner owner,AssistantDeviceScope current)
    {
        ArgumentNullException.ThrowIfNull(current);DeviceId(current.DeviceId);DeviceId(current.ProjectId);DeviceId(current.ConfigurationId);
        if(current.AccountId!=owner.AccountId||current.ProjectId!=owner.ProjectId)throw new UnauthorizedAccessException("设备关联跨账号或项目。");
        RequireHash(current.DeviceSha256);RequireHash(current.ConfigurationSha256);
        if(current.Parts is null||!current.Parts.Backend&&!current.Parts.Frontend)throw new InvalidOperationException("设备关联没有启用生成范围。");
        if(current.Parts.Backend)RequireHash(current.BackendPathSha256);else if(current.BackendPathSha256 is not null)throw new InvalidOperationException("未启用后端不能声明配置路径摘要。");
        if(current.Parts.Frontend)RequireHash(current.FrontendPathSha256);else if(current.FrontendPathSha256 is not null)throw new InvalidOperationException("未启用前端不能声明配置路径摘要。");
    }
    /// <summary>读取时验证完整持久摘要及既有绑定；损坏记录不自动重建或换绑。</summary>
    private AssistantDeviceAssociation ReadDeviceAssociation(long account,string device,string project)
    {
        RequireOwner(new(account,project),false);DeviceId(device);DeviceId(project);var value=Read<AssistantDeviceAssociation>(DeviceName(account,device,project));
        if(value.Scope is null||value.SchemaVersion!=1||value.Scope.AccountId!=account||value.Scope.DeviceId!=device||value.Scope.ProjectId!=project||Hash(value with{IntegritySha256=""})!=value.IntegritySha256)throw new InvalidOperationException("设备关联持久身份或摘要无效。");
        DeviceScope(new(value.Scope.AccountId,project),value.Scope);var binding=ReadBinding(value.BindingId);
        if(binding.AccountId!=value.Scope.AccountId||binding.ProjectId!=project||binding.GrantId!=value.GrantId||binding.LocalBindingSha256!=value.LocalBindingSha256)throw new InvalidOperationException("设备关联与原绑定不一致。");
        return value;
    }
    /// <summary>固定状态文件名不包含本机路径。</summary>
    private static string DeviceName(long account,string device,string project)=>"device-"+account.ToString(CultureInfo.InvariantCulture)+"-"+device+"-"+project+".json";
    /// <summary>公开值只含标识和摘要，不含本机目录、令牌或服务器状态位置。</summary>
    private static AssistantDeviceBindingView DeviceView(AssistantDeviceAssociation value)=>new(value.Scope.DeviceId,value.Scope.ProjectId,value.BindingId,value.IntegritySha256);
}
