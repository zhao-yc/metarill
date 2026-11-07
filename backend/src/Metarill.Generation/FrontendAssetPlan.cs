namespace Metarill.Generation;

/// <summary>内部完整资源准备结果；后端缺口只记录、不豁免，不是可发布应用或业务JSON能力标记。</summary>
internal sealed class FrontendAssetPlan
{
    private readonly Dictionary<string,byte[]> files;
    internal IReadOnlyList<SecurityModuleDependency> RequiredBackendAdapters {get;}
    internal IReadOnlyList<GenerationCapabilityGap> BackendGaps {get;}
    /// <summary>复制调用方缓冲区，读取时也返回副本，防止一次准备的资源污染后续检查。</summary>
    internal IReadOnlyDictionary<string,byte[]> Files=>new System.Collections.ObjectModel.ReadOnlyDictionary<string,byte[]>(files.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToArray(),StringComparer.Ordinal));
    /// <summary>资源与缺口一起固定；不提供将其标记为正式发布的开关。</summary>
    internal FrontendAssetPlan(IReadOnlyDictionary<string,byte[]> files,IReadOnlyList<SecurityModuleDependency> adapters,IReadOnlyList<GenerationCapabilityGap> gaps)
    {
        this.files=files.ToDictionary(pair=>pair.Key,pair=>pair.Value.ToArray(),StringComparer.Ordinal);
        RequiredBackendAdapters=Array.AsReadOnly(adapters.ToArray());BackendGaps=Array.AsReadOnly(gaps.ToArray());
    }
}
