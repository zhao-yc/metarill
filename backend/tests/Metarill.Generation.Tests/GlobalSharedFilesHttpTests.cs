using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局共享五种来源父页面通过真实生成HTTP、文件控制器及独立SQL竞争验证。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GlobalSharedFilesHttpTests
{
    /// <summary>全局共同国家，物理项目值不同，逻辑删除。</summary>
    [StandaloneExportMySqlFact]public Task Global_root()=>SharedCrossFilesMySqlTests.RunHttp("string","string",globalMask:1);
    /// <summary>全局地区无项目列，物理删除。</summary>
    [StandaloneExportMySqlFact]public Task Global_middle()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",physicalDelete:true,globalMask:2,noProject:true);
    /// <summary>全局城市及字符串父键，逻辑删除。</summary>
    [StandaloneExportMySqlFact]public Task Global_place()=>SharedCrossFilesMySqlTests.RunHttp("int64","string",globalMask:4);
    /// <summary>独立仓库全局无项目列，物理删除。</summary>
    [StandaloneExportMySqlFact]public Task Global_warehouse()=>SharedCrossFilesMySqlTests.RunHttp("int64","int64",physicalDelete:true,globalMask:8,noProject:true);
    /// <summary>整链全局无项目列，物理删除。</summary>
    [StandaloneExportMySqlFact]public Task Global_all()=>SharedCrossFilesMySqlTests.RunHttp("int64","int64",physicalDelete:true,globalMask:15,noProject:true);
    /// <summary>短控件共享叶子与全局根保持两种父身份。</summary>
    [StandaloneExportMySqlFact]public Task Global_short_root()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",shorter:true,globalMask:1);
    /// <summary>短控件全链全局，物理删除。</summary>
    [StandaloneExportMySqlFact]public Task Global_short_all()=>SharedCrossFilesMySqlTests.RunHttp("int64","string",shorter:true,physicalDelete:true,globalMask:11,noProject:true);
}
