using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实生成HTTP事务与并发读取／SQL写竞争；固定授权器不冒充正式账号登录。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class SharedCrossFilesHttpConcurrencyTests
{
    /// <summary>同源普通回归：String_string，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_String_string()=>SharedCrossFilesMySqlTests.RunHttp("string","string",concurrency:false);
    /// <summary>同源普通回归：String_long，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_String_long()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",physicalDelete:true,concurrency:false);
    /// <summary>同源普通回归：Long_string，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_Long_string()=>SharedCrossFilesMySqlTests.RunHttp("int64","string",concurrency:false);
    /// <summary>同源普通回归：Long_long，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_Long_long()=>SharedCrossFilesMySqlTests.RunHttp("int64","int64",physicalDelete:true,concurrency:false);
    /// <summary>同源普通回归：Short_string，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_Short_string()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",shorter:true,concurrency:false);
    /// <summary>同源普通回归：Short_long，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_Short_long()=>SharedCrossFilesMySqlTests.RunHttp("int64","string",shorter:true,physicalDelete:true,concurrency:false);
    /// <summary>同源普通回归：Derived_targets，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Baseline_Derived_targets()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",derived:true,concurrency:false);
    /// <summary>并发：String_string，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_String_string()=>SharedCrossFilesMySqlTests.RunHttp("string","string",concurrency:true);
    /// <summary>并发：String_long，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_String_long()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",physicalDelete:true,concurrency:true);
    /// <summary>并发：Long_string，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_Long_string()=>SharedCrossFilesMySqlTests.RunHttp("int64","string",concurrency:true);
    /// <summary>并发：Long_long，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_Long_long()=>SharedCrossFilesMySqlTests.RunHttp("int64","int64",physicalDelete:true,concurrency:true);
    /// <summary>并发：Short_string，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_Short_string()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",shorter:true,concurrency:true);
    /// <summary>并发：Short_long，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_Short_long()=>SharedCrossFilesMySqlTests.RunHttp("int64","string",shorter:true,physicalDelete:true,concurrency:true);
    /// <summary>并发：Derived_targets，包含五个真实父页面。</summary>
    [StandaloneExportMySqlFact] public Task Concurrent_Derived_targets()=>SharedCrossFilesMySqlTests.RunHttp("string","int64",derived:true,concurrency:true);
}
