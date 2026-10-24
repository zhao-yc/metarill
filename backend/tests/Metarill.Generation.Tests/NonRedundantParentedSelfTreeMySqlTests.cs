using System.Text.Json;
using Metarill.Data;
using MySqlConnector;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>复用同版原来源父范围HTTP夹具，再叠加真实非冗余双树、List、多选和子页组合。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class NonRedundantParentedSelfTreeMySqlTests
{
    /// <summary>字符串外键保持长ID精度，显隐名称均完成实际生成HTTP。</summary>
    [StandaloneExportMySqlFact]
    public Task String_parent_and_tree_ids_keep_mixed_graph_and_import_transaction()=>Run("string");
    /// <summary>64位整数外键与原父锁、整批导入事务采用同一合同。</summary>
    [StandaloneExportMySqlFact]
    public Task Integer_parent_and_tree_ids_keep_mixed_graph_and_import_transaction()=>Run("int64");
    /// <summary>先固定Server-200实例与隔离业务库，显式／隐式名称各运行一个独占三表样本。</summary>
    private static async Task Run(string type)
    {
        using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));var options=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var cs=new MySqlConnectionStringBuilder(options.ConnectionString);if(options.Provider!="mysql"||options.ExpectedDatabase!="metarill_business_test"||cs.Database!=options.ExpectedDatabase||cs.Server!="deve-server.yinzon.com"||cs.Port!=3306)throw new InvalidOperationException("非冗余父范围样本仅允许Server-200隔离业务库。");using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var connection=new MySqlConnection(options.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();Assert.True(await reader.ReadAsync()&&reader.GetString(0)==options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString());}foreach(var automatic in new[]{false,true})await CascadeTreeSourceParentMySqlTests.Exercise(options,type,true,automatic);
    }
}
