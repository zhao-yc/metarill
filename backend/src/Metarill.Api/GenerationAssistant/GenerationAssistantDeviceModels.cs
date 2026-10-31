using System.Text.Json.Serialization;
using Metarill.Generation;
namespace Metarill.Api.GenerationAssistant;

/// <summary>仅由服务端真实设备/项目配置组装，客户端不能提供归属和当前配置摘要替代查库。</summary>
public sealed record AssistantDeviceScope(long AccountId,string DeviceId,string ProjectId,string ConfigurationId,string DeviceSha256,string ConfigurationSha256,GenerationRuntimeParts Parts,string? BackendPathSha256,string? FrontendPathSha256);
/// <summary>本机明确设备与目录摘要；替换旧关联必须回读并明确提交旧摘要，不自动覆盖。</summary>
public sealed record AssistantDeviceBindRequest([property:JsonRequired]string BindingId,[property:JsonRequired]string DeviceId,[property:JsonRequired]string BackendPathSha256,[property:JsonRequired]string FrontendPathSha256,[property:JsonRequired]int RuntimePartsProtocol,[property:JsonRequired]string ExpectedDeviceSha256,[property:JsonRequired]string ExpectedConfigurationSha256,string? ExpectedAssociationSha256=null);
public sealed record AssistantDeviceBindingView(string DeviceId,string ProjectId,string BindingId,string AssociationSha256,bool MatchesCurrentConfiguration=true);
internal sealed record AssistantDeviceAssociation(int SchemaVersion,AssistantDeviceScope Scope,string BindingId,long GrantId,string LocalBindingSha256,string IntegritySha256);

/// <summary>查询仅选择当前主体拥有的设备，不接收项目或配置摘要。</summary>
public sealed record AssistantDeviceLookupRequest([property:JsonRequired]string DeviceId);
/// <summary>只读预检暴露当前归属/配置摘要与启用端，不返回本机目录或设备凭据。</summary>
public sealed record AssistantDeviceConfigurationView(string DeviceId,string ProjectId,string DeviceSha256,string ConfigurationSha256,GenerationRuntimeParts Parts,string? BackendPathSha256,string? FrontendPathSha256);
