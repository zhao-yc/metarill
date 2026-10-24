using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>复用完整原跨表HTTP／稀疏写／事务合同，独占随机四／五表；不冒充正式登录。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantCrossTargetsMySqlTests
{
    /// <summary>三层R0物理名称目标覆盖四种源／父键组合。</summary>
    [StandaloneExportMySqlFact]
    public Task Physical_R0_targets_keep_original_cross_crud()=>NonRedundantCrossTableCrudMySqlTests.VerifyAll("physical");
    /// <summary>三层派生名称覆盖旧RR、真实名称锁、双控件及超过5000原候选。</summary>
    [StandaloneExportMySqlFact]
    public Task Derived_R0_targets_keep_original_cross_crud()=>NonRedundantCrossTableCrudMySqlTests.VerifyAll("derived");
    /// <summary>R1／R0／R1混合层位不能误读IsIgnore名称或丢失祖先范围。</summary>
    [StandaloneExportMySqlFact]
    public Task Mixed_R0_R1_targets_keep_original_cross_crud()=>NonRedundantCrossTableCrudMySqlTests.VerifyAll("mixed");
    /// <summary>独立目标表重复角色与相同数字ID保持层位隔离，不能复用错别名或错误名称。</summary>
    [StandaloneExportMySqlFact]
    public Task Repeated_R0_target_roles_keep_independent_cross_layers()=>NonRedundantCrossTableCrudMySqlTests.VerifyAll("repeated");
}
