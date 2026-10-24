using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>执行原表单整entity回传，实际生成HTTP必须剥离只读回显并拒绝私有写标志。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class RedundantCrossOriginalPayloadMySqlTests
{
    /// <summary>独立跨表链使用真实服务返回的完整VO回传，不添加不存在的字段。</summary>
    [StandaloneExportMySqlFact] public Task Independent_chain_keeps_original_full_entity_contract()=>RedundantRepeatedCrossSourceMySqlTests.OriginalPayloadAsync(0);
    /// <summary>重复来源默认名称使用真实MySQL，客户端辅助名称不能冒充可信缓存。</summary>
    [StandaloneExportMySqlFact] public Task Default_names_keep_original_full_entity_contract()=>RedundantRepeatedCrossSourceMySqlTests.OriginalPayloadAsync(3);
    /// <summary>三层同来源的独立名称字段仍走同一原HTTP兼容入口。</summary>
    [StandaloneExportMySqlFact] public Task Role_specific_names_keep_original_full_entity_contract()=>RedundantRepeatedCrossSourceMySqlTests.OriginalPayloadAsync(7);
}
