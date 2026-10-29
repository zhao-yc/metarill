using System.Data;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>没有专属只读配置便跳过真实TLS连接，不复用建库/写入租约。</summary>
public sealed class MySqlTlsReadOnlyFactAttribute:FactAttribute
{
    public MySqlTlsReadOnlyFactAttribute()
    {
        if(!File.Exists(Environment.GetEnvironmentVariable("METARILL_MYSQL_TLS_READONLY_SETTINGS")))
            Skip="缺专属只读TLS配置；证书/主机名实连未执行。";
    }
}

/// <summary>用既有受限账号验证五种TLS组合；SQL只有实例指纹和本连接加密状态。</summary>
public sealed class MySqlTlsReadOnlyTests
{
    /// <summary>坏目标、路径、实例或摘要在任何DNS/连接之前拒绝；不把错误配置变成默认放行。</summary>
    [Theory]
    [InlineData("host")][InlineData("database")][InlineData("root")][InlineData("ip")]
    [InlineData("uuid")][InlineData("hash")][InlineData("ca-path")][InlineData("config-path")][InlineData("output-path")]
    public void Invalid_readonly_tls_target_is_rejected_before_network(string invalid)
    {
        var directory=Path.GetFullPath(Path.GetTempPath());
        var settings=new TlsReadOnlySettings
        {
            ConfigurationPath=Path.Combine(directory,"settings.json"),CaPath=Path.Combine(directory,"ca.pem"),OutputPath=Path.Combine(directory,"result.json"),
            ExpectedConnectionHost="example.invalid",ExpectedDatabase="synthetic_readonly",ExpectedIpAddress="192.0.2.1",
            ExpectedHostname="synthetic-host",ExpectedServerUuid="11111111-1111-1111-1111-111111111111",CaSha256=new string('a',64),
            SystemTrustExpected=false,HostnameMatchExpected=false
        };
        var builder=new MySqlConnectionStringBuilder{Server=settings.ExpectedConnectionHost,Database=settings.ExpectedDatabase,UserID="synthetic_readonly"};
        switch(invalid)
        {
            case "host":builder.Server="foreign.invalid";break;
            case "database":builder.Database="foreign";break;
            case "root":builder.UserID="root";break;
            case "ip":settings=settings with{ExpectedIpAddress="not-ip"};break;
            case "uuid":settings=settings with{ExpectedServerUuid="bad"};break;
            case "hash":settings=settings with{CaSha256="bad"};break;
            case "ca-path":settings=settings with{CaPath="relative.pem"};break;
            case "config-path":settings=settings with{ConfigurationPath="relative.json"};break;
            case "output-path":settings=settings with{OutputPath="relative.json"};break;
        }
        Assert.Throws<InvalidOperationException>(()=>Validate(settings,builder));
    }

    /// <summary>真实连接不设证书回调、不改全局/会话模式；拒绝结果必须来自证书验证，而非网络或密码错误。</summary>
    [MySqlTlsReadOnlyFact]
    public async Task Actual_driver_preserves_tls_modes_and_rejects_untrusted_or_mismatched_certificate()
    {
        var settings=JsonSerializer.Deserialize<TlsReadOnlySettings>(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_MYSQL_TLS_READONLY_SETTINGS")!))!;
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(settings.ConfigurationPath));
        var database=document.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase");
        var original=new MySqlConnectionStringBuilder(database.GetProperty("ConnectionString").GetString()??throw new InvalidOperationException("缺少既有私有连接配置。"));
        Validate(settings,original);
        Assert.Equal(settings.ExpectedDatabase,database.GetProperty("ExpectedDatabase").GetString());
        if(database.TryGetProperty("Provider",out var provider))Assert.Equal("mysql",provider.GetString());
        Assert.True(string.IsNullOrEmpty(original.SslCert)&&string.IsNullOrEmpty(original.SslKey),"双向TLS须独立场景，不覆盖已有客户端证书。");
        Assert.False(File.Exists(settings.OutputPath),"每轮证据独占新路径，不覆盖旧结果。");
        var addresses=await Dns.GetHostAddressesAsync(original.Server);
        Assert.NotEmpty(addresses);Assert.All(addresses,address=>Assert.Equal(settings.ExpectedIpAddress,address.ToString()));
        var ca=await File.ReadAllBytesAsync(settings.CaPath);
        Assert.Equal(settings.CaSha256,Convert.ToHexStringLower(SHA256.HashData(ca)));
        Assert.DoesNotContain("PRIVATE KEY",System.Text.Encoding.ASCII.GetString(ca),StringComparison.Ordinal);
        var vectors=new List<TlsReadOnlyVector>();
        foreach(var item in new[]
        {
            (Name:"required",Mode:MySqlSslMode.Required,Ca:false,Expected:true),
            (Name:"verify-ca-system",Mode:MySqlSslMode.VerifyCA,Ca:false,Expected:settings.SystemTrustExpected),
            (Name:"verify-full-system",Mode:MySqlSslMode.VerifyFull,Ca:false,Expected:settings.SystemTrustExpected&&settings.HostnameMatchExpected),
            (Name:"verify-ca-pinned",Mode:MySqlSslMode.VerifyCA,Ca:true,Expected:true),
            (Name:"verify-full-pinned",Mode:MySqlSslMode.VerifyFull,Ca:true,Expected:settings.HostnameMatchExpected)
        })
        {
            // 每个候选是独立客户端配置，不写回原连接、CA信任库或数据库配置。
            var builder=new MySqlConnectionStringBuilder(original.ConnectionString){SslMode=item.Mode,SslCa=item.Ca?settings.CaPath:"",Pooling=false,ConnectionTimeout=5,DefaultCommandTimeout=10};
            var opened=false;
            try
            {
                await using var connection=new MySqlConnection(builder.ConnectionString);
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));await connection.OpenAsync(timeout.Token);opened=true;
                await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,true,timeout.Token);
                await using(var pin=connection.CreateCommand())
                {
                    pin.Transaction=transaction;pin.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid,@@version";
                    await using var reader=await pin.ExecuteReaderAsync(timeout.Token);
                    if(!await reader.ReadAsync(timeout.Token)||reader.GetString(0)!=settings.ExpectedDatabase||reader.GetString(1)!=settings.ExpectedHostname||reader.GetString(2)!=settings.ExpectedServerUuid)
                        throw new InvalidOperationException("只读TLS实例指纹不符。");
                }
                var status=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                await using(var command=connection.CreateCommand())
                {
                    command.Transaction=transaction;command.CommandText="SHOW SESSION STATUS WHERE Variable_name IN ('Ssl_cipher','Ssl_version')";
                    await using var reader=await command.ExecuteReaderAsync(timeout.Token);while(await reader.ReadAsync(timeout.Token))status[reader.GetString(0)]=reader.GetString(1);
                }
                if(!status.TryGetValue("Ssl_cipher",out var cipher)||string.IsNullOrWhiteSpace(cipher)||!status.TryGetValue("Ssl_version",out var version)||version is not("TLSv1.2" or "TLSv1.3"))
                    throw new InvalidOperationException("TLS候选没有实际加密协议和密码套件。");
                await transaction.RollbackAsync(timeout.Token);
                vectors.Add(new(item.Name,item.Expected,true,false,item.Expected,version,cipher,[]));
            }
            catch(Exception error)
            {
                var chain=new List<Exception>();for(Exception? next=error;next is not null;next=next.InnerException)chain.Add(next);
                // 只保存异常类型和证书错误分类；驱动原文/栈/连接对象均不落盘或进入断言。
                var certificate=chain.Any(value=>value is AuthenticationException)&&chain.Any(value=>value.Message.Contains("certificate",StringComparison.OrdinalIgnoreCase)||value.Message.Contains("证书",StringComparison.Ordinal));
                vectors.Add(new(item.Name,item.Expected,opened,certificate,!item.Expected&&!opened&&certificate,null,null,chain.Select(value=>value.GetType().Name).ToArray()));
            }
        }
        Assert.Equal(settings.CaSha256,Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(settings.CaPath))));
        var actualDriver=typeof(MySqlConnection).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        var passed=vectors.All(value=>value.Passed)&&actualDriver=="2.6.1";
        // 证据只含模式与加密状态；不包含用户名、口令、连接串、令牌或原始驱动错误。
        using(var stream=new FileStream(settings.OutputPath,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await JsonSerializer.SerializeAsync(stream,new{passed,readOnly=true,driver=actualDriver,ddlExecuted=false,dmlExecuted=false,configurationModified=false,trustStoreModified=false,manInTheMiddleTest=false,vectors},new JsonSerializerOptions{WriteIndented=true});
        Assert.True(passed,"TLS只读连接结果不符合已核实证书预期；仅保存脱敏分类，不降低验证模式。");
    }

    /// <summary>所有目标和输出必须显式绑定，禁止使用管理员或不受控多主机连接；此方法没有网络/文件副作用。</summary>
    private static void Validate(TlsReadOnlySettings value,MySqlConnectionStringBuilder connection)
    {
        if(string.IsNullOrWhiteSpace(value.ExpectedConnectionHost)||connection.Server!=value.ExpectedConnectionHost||
           connection.Database!=value.ExpectedDatabase||!Regex.IsMatch(value.ExpectedDatabase,"^[a-zA-Z0-9_]{1,64}$")||
           string.IsNullOrEmpty(connection.UserID)||connection.UserID.Equals("root",StringComparison.OrdinalIgnoreCase)||
           !IPAddress.TryParse(value.ExpectedIpAddress,out _)||string.IsNullOrWhiteSpace(value.ExpectedHostname)||
           !Guid.TryParseExact(value.ExpectedServerUuid,"D",out _)||!Regex.IsMatch(value.CaSha256,"^[a-f0-9]{64}$")||
           !Path.IsPathFullyQualified(value.ConfigurationPath)||!Path.IsPathFullyQualified(value.CaPath)||!Path.IsPathFullyQualified(value.OutputPath))
            throw new InvalidOperationException("只读TLS目标、路径或证书固定配置无效。");
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record TlsReadOnlySettings
    {
        public required string ConfigurationPath{get;init;}
        public required string CaPath{get;init;}
        public required string OutputPath{get;init;}
        public required string ExpectedConnectionHost{get;init;}
        public required string ExpectedDatabase{get;init;}
        public required string ExpectedIpAddress{get;init;}
        public required string ExpectedHostname{get;init;}
        public required string ExpectedServerUuid{get;init;}
        public required string CaSha256{get;init;}
        public required bool SystemTrustExpected{get;init;}
        public required bool HostnameMatchExpected{get;init;}
    }
    private sealed record TlsReadOnlyVector(string Name,bool ExpectedConnected,bool Connected,bool CertificateRejected,bool Passed,string? Version,string? Cipher,string[] ErrorTypes);
}
