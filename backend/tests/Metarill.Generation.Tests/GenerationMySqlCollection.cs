using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>共享业务隔离库的结构验收串行执行，避免用例之间争用产品唯一结构锁；不改变产品锁和超时。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GenerationMySqlCollection
{
    public const string Name = "生成真实MySQL结构验收";
}
