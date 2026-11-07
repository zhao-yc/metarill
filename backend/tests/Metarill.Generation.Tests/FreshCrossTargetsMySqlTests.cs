using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>四种新跨表目标的真实Full／Production/Web／空库与原文件权限合同。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class FreshCrossTargetsMySqlTests
{
    /// <summary>每个模型使用独占随机库，沿原Web身份而非固定授权器。</summary>
    [FreshCrossMySqlTheory]
    [InlineData("string","string","CrossStringStringDatabase","cross_ss",970,"physical")]
    [InlineData("string","int64","CrossStringLongDatabase","cross_sl",971,"derived")]
    [InlineData("int64","string","CrossLongStringDatabase","cross_ls",972,"mixed")]
    [InlineData("int64","int64","CrossLongLongDatabase","cross_ll",973,"repeated")]
    public Task Original_contract_accepts_nonredundant_targets(string type,string parentType,string property,string suffix,int worker,string profile)=>FreshCrossTableMySqlTests.Exercise(type,parentType,property,suffix,worker,profile);
}
