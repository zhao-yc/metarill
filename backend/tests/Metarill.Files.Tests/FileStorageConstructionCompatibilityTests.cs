using Metarill.Core.Security;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>文件运行时被已有手写宿主和反射工厂调用，新增供应商不得移除原公开构造签名。</summary>
public sealed class FileStorageConstructionCompatibilityTests
{
    /// <summary>保留五参文件Service签名，不能仅用六参可选参数冒充反射/二进制兼容。</summary>
    [Fact]
    public void Original_file_service_constructor_is_still_available_to_external_factories()
    {
        Assert.NotNull(typeof(FileLibraryService).GetConstructor([typeof(SqlRowStore), typeof(FileMetadataContext),
            typeof(IIdGenerator), typeof(IRuntimeAuthorization), typeof(FileLibraryOptions)]));
        Assert.IsType<FileLibraryService>(Activator.CreateInstance(typeof(FileLibraryService), [null, null, null, null, new FileLibraryOptions()]));
    }

    /// <summary>平台项目解析器原三参签名同样保留，旧本地测试/工厂无须先认识对象层。</summary>
    [Fact]
    public void Original_project_file_resolver_constructor_is_still_available()
        => Assert.NotNull(typeof(ProjectFileLibraryResolver).GetConstructor([typeof(ProjectFileRuntimes),
            typeof(IRuntimeAuthorization), typeof(IIdGenerator)]));
}
