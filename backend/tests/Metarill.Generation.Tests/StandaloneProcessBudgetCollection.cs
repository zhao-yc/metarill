using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>仅串行两个实际争抢子进程预算的重量类，不改变全局并行或构建超时。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StandaloneProcessBudgetCollection
{
    public const string Name = "独立包构建与宿主进程预算";
}
