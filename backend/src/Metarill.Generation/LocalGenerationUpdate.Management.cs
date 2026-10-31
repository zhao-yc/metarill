using System.Text.Json;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

public sealed partial class LocalGenerationUpdater
{
    private const string ManagementMode="FullManagementApplication";
    private static readonly HashSet<string> ManagementFrontend=new(StringComparer.Ordinal)
    {"frontend/pages/main/set_role_permission.vue","frontend/components/yzp-permission-workbench/yzp-permission-workbench.vue","frontend/components/yzp-permission-legacy/yzp-permission-legacy.vue","frontend/common/yzp_runtime/permissionWorkbench.js"};
    private sealed record ManagementSourceManifest(int SchemaVersion,string Profile,string RootNamespace,string ProjectId,string ManagementAdminTypeId,string ModuleSha256,string ApplicationMetadataSha256,string MetadataSha256,string SecurityCatalogSha256,UpdateEntry[] Files);

    /// <summary>完整管理模式必须同时核验外层绑定、真实持久来源与当前固定工厂；普通模式不接收管理声明。</summary>
    private static PreparedSecurityManagementSource? ReadManagement(string root,PackageManifest package,GeneratedManifest generated,Dictionary<string,UpdateEntry> entries)
    {
        if(package.PackageMode!=ManagementMode)
        {
            if(package.ManagementModule is not null||package.RedactedPaths is not null||entries.ContainsKey(PreparedSecurityManagementSource.SourceMarker)||entries.ContainsKey(PreparedSecurityManagementSource.ApplicationPath))throw new InvalidOperationException("普通完整应用不能夹带管理绑定或来源归属。");
            return null;
        }
        if(package.ManagementModule is null||package.AuthenticationProfile!="web-database-v1"||package.RedactedPaths is null||package.RedactedPaths.Any(path=>path is null))throw new InvalidOperationException("管理包缺少固定绑定或正式Web配置。");
        foreach(var path in PreparedSecurityManagementSource.OwnedMetadataPaths.Append(PreparedSecurityManagementSource.SourceMarker))
            if(!entries.ContainsKey(path))throw new InvalidOperationException("管理包缺少来源元数据完整归属："+path);
        _=Decode<ManagementSourceManifest>(ReadBytes(root,PreparedSecurityManagementSource.SourceMarker));
        var source=PreparedSecurityManagementSource.Read(root);
        var binding=new ManagementBinding(source.Module.Binding.ModuleId,source.Module.Binding.ModuleSha256,source.Module.Binding.ManagementAdminTypeId,source.ApplicationMetadataSha256);
        if(package.ManagementModule!=binding||source.RootNamespace!=package.RootNamespace||source.Module.Binding.ProjectId!=generated.ProjectId)throw new InvalidOperationException("管理包模块、后台或业务来源绑定不一致。");
        return source;
    }

    /// <summary>来源声明使用最终将写入目标的五份元数据摘要，不假定合并后的生成清单等于候选原字节。</summary>
    private static void AddManagementSource(PackageSnapshot before,PackageSnapshot after,Dictionary<string,UpdateEntry> package,Dictionary<string,byte[]> bytes)
    {
        if(after.Management is null)return;
        var source=Decode<ManagementSourceManifest>(ReadBytes(after.Root,PreparedSecurityManagementSource.SourceMarker));
        var files=PreparedSecurityManagementSource.OwnedMetadataPaths.Select(path=>new UpdateEntry(path,Hash(bytes.TryGetValue(path,out var content)?content:ReadBytes(before.Root,path)))).OrderBy(entry=>entry.Path,StringComparer.Ordinal).ToArray();
        bytes[PreparedSecurityManagementSource.SourceMarker]=Encode(source with{Files=files});
        package[PreparedSecurityManagementSource.SourceMarker]=new(PreparedSecurityManagementSource.SourceMarker,Hash(bytes[PreparedSecurityManagementSource.SourceMarker]));
    }
    /// <summary>管理来源作为第四份事务清单提交；普通模式保持原三清单与协议字节。</summary>
    private static IEnumerable<string> ManifestPaths(PackageSnapshot snapshot)=>snapshot.Management is null?[GenerationMarker,FrontendMarker,PackageMarker]:[GenerationMarker,FrontendMarker,PreparedSecurityManagementSource.SourceMarker,PackageMarker];
}
