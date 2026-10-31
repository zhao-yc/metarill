using Xunit;

namespace Metarill.Files.Tests;

/// <summary>这些夹具共用测试库及项目，独占集合避免物理清理SQL与其它夹具事务交错；用例内部仍可显式使用多连接。</summary>
[CollectionDefinition(FilesIntegrationCollection.Name,DisableParallelization=true)]
public sealed class FilesIntegrationCollection
{
    public const string Name="共享文件库隔离夹具";
}
