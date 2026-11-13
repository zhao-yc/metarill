using System.Text.Json;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实例标识及可选外层实际配置只做解析／固定包核对；不创建连接或应用进程。</summary>
public sealed class TimeValueNativeInputTests
{
    /// <summary>旧数字执行器字段、缺字段、坏类型、重复或额外字段必须明确拒绝。</summary>
    [Theory]
    [InlineData("{}")] 
    [InlineData("{\"hostname\":\"fixed-host\",\"serverUuid\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"expectedHostname\":\"fixed-host\"}")]
    [InlineData("{\"expectedHostname\":1,\"expectedServerUuid\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"expectedHostname\":\"fixed-host\",\"expectedServerUuid\":\"bad-uuid\"}")]
    [InlineData("{\"expectedHostname\":\"fixed-host\",\"expectedServerUuid\":\"11111111-1111-1111-1111-111111111111\",\"extra\":true}")]
    [InlineData("{\"expectedHostname\":\"fixed-host\",\"expectedHostname\":\"other\",\"expectedServerUuid\":\"11111111-1111-1111-1111-111111111111\"}")]
    public void Instance_pin_rejects_other_adapters_and_malformed_fields_without_connections(string input)
    {
        using var parsed=JsonDocument.Parse(input);
        Assert.Throws<InvalidOperationException>(()=>TimeValueNativeLease.ReadInstancePin(parsed.RootElement));
    }

    /// <summary>正常单测验证原字段；外层显式提供实际假口令配置时再验证完整传输，仍不连接数据库。</summary>
    [Fact]
    public void Valid_pin_and_optional_external_configuration_match_original_contract()
    {
        using var fixture=JsonDocument.Parse("{\"expectedHostname\":\"fixed-host\",\"expectedServerUuid\":\"11111111-1111-1111-1111-111111111111\"}");
        var valid=TimeValueNativeLease.ReadInstancePin(fixture.RootElement);Assert.Equal("fixed-host",valid.ExpectedHostname);Assert.Equal("11111111-1111-1111-1111-111111111111",valid.ExpectedServerUuid);
        var path=Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_INPUT_SETTINGS");
        if(string.IsNullOrEmpty(path))return;
        Assert.Equal(Path.GetFullPath(path),path);Assert.Equal(path,Environment.GetEnvironmentVariable("METARILL_FRESH_INSTALL_SETTINGS"));
        using var document=JsonDocument.Parse(File.ReadAllText(path));var settings=document.RootElement;
        var root=NonRedundantApplicationFixture.Root();var run=TimeValueNativeLease.Validate(settings,CSharpGenerator.SafePath(root,".local/time-values-native/prepared"));
        var pin=TimeValueNativeLease.ReadInstancePin(settings.GetProperty("InstancePin"));
        Assert.Equal(Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_INPUT_HOSTNAME"),pin.ExpectedHostname);
        Assert.Equal(Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_INPUT_UUID"),pin.ExpectedServerUuid);
        var options=FreshInstallConfiguration.Read("AgentDatabase","agent");var connection=new MySqlConnectionStringBuilder(options.ConnectionString);
        Assert.Equal("mtf_"+run,connection.UserID);Assert.Equal("metarill_fresh_"+run+"_agent",options.ExpectedDatabase);
        var proof=settings.GetProperty("TimeValuePreparation").Deserialize<GlobalCachedPackageProof>()!;
        var catalog=TimeValueNativePreparation.Verify(proof);Assert.Equal(4,catalog.ConfigColumns.Count(column=>column.LogicalType=="time"));Assert.Equal(15,FreshInstallConfiguration.ExpectedFullTableCount(catalog));
        var receipt=Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_INPUT_RECEIPT")??throw new InvalidOperationException("外层传输预检缺回执位置。");
        Assert.Equal(Path.Combine(Path.GetDirectoryName(path)!,"validation.json"),receipt);
        using var stream=new FileStream(receipt,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(stream,new{run,valid=true,expectedDatabase=options.ExpectedDatabase,timeColumns=4,tableCount=15,databaseOpened=false,applicationStarted=false});
    }
}
