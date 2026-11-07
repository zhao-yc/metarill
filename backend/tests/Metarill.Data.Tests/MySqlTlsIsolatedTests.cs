using System.ComponentModel;
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

/// <summary>只接收独占容器配置；普通 CI 不从开发库配置推导本轮租约。</summary>
public sealed class MySqlTlsIsolatedFactAttribute : FactAttribute
{
    public MySqlTlsIsolatedFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_MYSQL_TLS_ISOLATED_SETTINGS")))
            Skip = "缺独占 MySQL TLS 容器及固定证书配置。";
    }
}

/// <summary>真实驱动的域名、CA、协议、客户端证书及平台事务接线。</summary>
public sealed class MySqlTlsIsolatedTests
{
    /// <summary>每个候选重新连接；不安装系统 CA，不以网络或密码错误替代证书拒绝。</summary>
    [MySqlTlsIsolatedFact]
    public async Task Strict_tls_domain_and_mutual_authentication_use_actual_driver()
    {
        var settings = await ReadSettings();
        var results = new List<VectorResult>();
        foreach (var vector in new[]
        {
            new Vector("full-pinned-tls13", true, "TLSv1.3"),
            new Vector("full-pinned-tls12", true, "TLSv1.2"),
            new Vector("ca-pinned-ip", true, "TLSv1.3", Host: settings.IpAddress, Mode: MySqlSslMode.VerifyCA),
            new Vector("full-pinned-ip-mismatch", false, "TLSv1.3", Host: settings.IpAddress, Rejection: "certificate"),
            new Vector("full-wrong-ca", false, "TLSv1.3", Ca: "wrong-ca.pem", Rejection: "certificate"),
            new Vector("full-system-untrusted", false, "TLSv1.3", Ca: "", Rejection: "certificate"),
            new Vector("mutual-missing-client", false, "TLSv1.3", Mutual: true, Rejection: "access-denied"),
            new Vector("mutual-valid-client", true, "TLSv1.3", Mutual: true, Client: "client"),
            new Vector("mutual-untrusted-client", false, "TLSv1.3", Mutual: true, Client: "wrong-client", Rejection: "client-certificate"),
            new Vector("full-wrong-password", false, "TLSv1.3", Password: new string('0', 64), Rejection: "access-denied"),
            new Vector("transport-account-tls", true, "TLSv1.3", UserSuffix: "_plain"),
            new Vector("plaintext-transport-rejected", false, "", Mode: MySqlSslMode.Disabled, UserSuffix: "_plain", Rejection: "secure-transport")
        })
        {
            var builder = Builder(settings, vector);
            var connected = false;
            try
            {
                await using var connection = new MySqlConnection(builder.ConnectionString);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await connection.OpenAsync(timeout.Token);
                connected = true;
                await Pin(connection, settings, timeout.Token);
                var (version, cipher) = await TlsStatus(connection, null, timeout.Token);
                results.Add(new(vector.Name, vector.Expected && version == vector.Protocol && cipher.Length > 0,
                    true, version, cipher, null, [], []));
            }
            catch (Exception error)
            {
                var chain = ErrorChain(error);
                var certificate = chain.Any(item => item is AuthenticationException) &&
                    chain.Any(item => item.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                                      item.Message.Contains("证书", StringComparison.Ordinal));
                var number = chain.OfType<MySqlException>().Select(item => (int?)item.Number).FirstOrDefault();
                var rejected = vector.Rejection switch
                {
                    "certificate" => certificate,
                    "access-denied" => number == 1045,
                    "secure-transport" => number == 3159,
                    "client-certificate" => certificate || number == 1045 || IsWindowsCertificateAlert(chain),
                    _ => false
                };
                // 仅记录类型、错误编号及证书分类，不保存驱动异常正文或连接对象。
                results.Add(new(vector.Name, !vector.Expected && !connected && rejected, connected,
                    null, null, number, chain.Select(item => item.GetType().Name).ToArray(),
                    chain.OfType<Win32Exception>().Select(item => item.NativeErrorCode).ToArray()));
            }
        }

        var driver = typeof(MySqlConnection).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        await VerifyFiles(settings);
        var passed = driver == "2.6.1" && results.All(item => item.Passed);
        await WriteEvidence(settings, "driver.json", new
        {
            passed, driver, vectors = results, systemTrustModified = false,
            certificateValidationCallback = false, existingDatabaseUsed = false, manInTheMiddleTest = false
        });
        Assert.True(passed, "独占 TLS 候选未通过；仅保留脱敏结果，不降低证书验证。");
    }

    /// <summary>平台会话和 FreeSql 使用同一严格连接配置，真实写入回滚后由新连接回读。</summary>
    [MySqlTlsIsolatedFact]
    public async Task Platform_provider_and_freesql_keep_strict_tls_and_transaction_rollback()
    {
        var settings = await ReadSettings();
        var connectionString = Builder(settings, new("platform", true, "TLSv1.3")).ConnectionString;
        var options = new DatabaseOptions { ExpectedDatabase = settings.Database, ConnectionString = connectionString };
        var stage = "initial-pin";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var token = timeout.Token;
            await using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync(token);
                await Pin(connection, settings, token);
                Assert.Equal("before", await ProbeValue(connection, null, token));
            }

            using var session = new DatabaseSession(options);
            stage = "platform-transaction";
            await Assert.ThrowsAsync<RollbackProbe>(() => session.InTransactionAsync<int>(async () =>
            {
                var transaction = session.CurrentTransaction!;
                var (version, cipher) = await TlsStatus((MySqlConnection)transaction.Connection!, transaction, token);
                Assert.Equal("TLSv1.3", version);
                Assert.NotEmpty(cipher);
                await using var command = session.Provider.CreateCommand(transaction.Connection!);
                command.Transaction = transaction;
                command.CommandText = "UPDATE tls_probe SET value=@value WHERE id=1";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "value";
                parameter.Value = "inside-transaction";
                command.Parameters.Add(parameter);
                Assert.Equal(1, await command.ExecuteNonQueryAsync(token));
                Assert.Equal("inside-transaction", await ProbeValue((MySqlConnection)transaction.Connection!, transaction, token));
                throw new RollbackProbe();
            }, token));
            Assert.Null(session.CurrentTransaction);

            stage = "independent-rollback-readback";
            await using (var connection = new MySqlConnection(connectionString))
            {
                await connection.OpenAsync(token);
                await Pin(connection, settings, token);
                Assert.Equal("before", await ProbeValue(connection, null, token));
            }

            stage = "freesql-tls";
            var status = await session.Orm.Ado.QueryAsync<TlsSessionStatus>(
                "SHOW SESSION STATUS WHERE Variable_name IN ('Ssl_cipher','Ssl_version')", null, token);
            Assert.Equal("TLSv1.3", status.Single(row => row.Variable_name == "Ssl_version").Value);
            Assert.NotEmpty(status.Single(row => row.Variable_name == "Ssl_cipher").Value);
            Assert.Equal("before", await session.Orm.Ado.ExecuteScalarAsync("SELECT value FROM tls_probe WHERE id=1", null, token));
            await VerifyFiles(settings);
            await WriteEvidence(settings, "platform.json", new { passed = true, providerPinned = true,
                strictTls = true, freeSqlTls = true, rolledBack = true, independentReadback = true,
                startupDdl = false, existingDatabaseUsed = false });
        }
        catch (Exception error)
        {
            await WriteEvidence(settings, "platform.json", new { passed = false, stage,
                errorTypes = ErrorChain(error).Select(item => item.GetType().Name).ToArray() });
            throw new InvalidOperationException("独占 TLS 平台事务链未通过；错误正文不回显。");
        }
    }

    /// <summary>适配器和 ORM 都必须保留错误 CA／主机名拒绝，不能只证明连接采用了加密。</summary>
    [MySqlTlsIsolatedFact]
    public async Task Platform_provider_and_freesql_reject_wrong_ca_and_hostname()
    {
        var settings = await ReadSettings();
        var results = new List<object>();
        var passed = true;
        // 独占冷账号永远不做成功认证；普通账号先成功一次，分别证明完整认证与缓存认证两条拒绝路径。
        await using (var warm = new MySqlConnection(Builder(settings, new("warm", true, "TLSv1.3")).ConnectionString))
        {
            await warm.OpenAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await Pin(warm, settings, timeout.Token);
        }
        foreach (var cold in new[] { true, false })
        foreach (var vector in new[]
        {
            new Vector("wrong-ca", false, "TLSv1.3", Ca: "wrong-ca.pem"),
            new Vector("wrong-hostname", false, "TLSv1.3", Host: settings.IpAddress)
        })
        foreach (var orm in new[] { false, true })
        {
            var rejected = false;
            string[] errorTypes = [];
            int[] mysqlErrors = [];
            string? rejectionKind = null;
            var options = new DatabaseOptions { ExpectedDatabase = settings.Database,
                ConnectionString = Builder(settings, vector with { UserSuffix = cold ? "_cold" : "" }).ConnectionString };
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                if (orm)
                {
                    using var session = new DatabaseSession(options);
                    await session.Orm.Ado.ExecuteScalarAsync("SELECT 1", null, timeout.Token);
                }
                else
                {
                    await using var connection = new MySqlDatabaseProvider().CreateConnection(options);
                    await connection.OpenAsync(timeout.Token);
                }
            }
            catch (Exception error)
            {
                var chain = ErrorChain(error);
                var certificate = chain.Any(item => item is AuthenticationException) &&
                    chain.Any(item => item.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                                      item.Message.Contains("证书", StringComparison.Ordinal));
                var identityGuard = chain.OfType<MySqlException>().Any(item => IsIdentityPasswordGuard(item.Number, item.Message));
                rejected = cold ? identityGuard : certificate;
                rejectionKind = identityGuard ? "identity-before-password" : certificate ? "certificate" : null;
                errorTypes = chain.Select(item => item.GetType().Name).ToArray();
                mysqlErrors = chain.OfType<MySqlException>().Select(item => item.Number).ToArray();
            }
            passed &= rejected;
            results.Add(new { vector.Name, orm, coldAccount = cold, rejected, rejectionKind, errorTypes, mysqlErrors });
        }
        await VerifyFiles(settings);
        await WriteEvidence(settings, "platform-rejections.json", new { passed, vectors = results });
        Assert.True(passed, "平台适配器或 FreeSql 未保留严格证书拒绝。");
    }

    /// <summary>完整认证拒绝需匹配驱动的精确身份校验提示，普通连接失败或坏密码不能代替。</summary>
    [Fact]
    public void Identity_guard_does_not_accept_generic_connection_or_authentication_error()
    {
        var message = IdentityPasswordMessage("caching_sha2_password");
        Assert.True(IsIdentityPasswordGuard(1042, message));
        Assert.True(IsIdentityPasswordGuard(1042, IdentityPasswordMessage("sha256_password")));
        Assert.False(IsIdentityPasswordGuard(1045, message));
        Assert.False(IsIdentityPasswordGuard(1042, "Unable to connect to any MySQL hosts."));
        Assert.False(IsIdentityPasswordGuard(1042, "synthetic certificate password error"));
    }

    // 对应固定驱动 2.6.1 的 SendClearPasswordAsync；不将所有 1042 或含 certificate 的文本视作通过。
    private static bool IsIdentityPasswordGuard(int number, string message) => number == 1042 &&
        (message == IdentityPasswordMessage("caching_sha2_password") || message == IdentityPasswordMessage("sha256_password"));
    private static string IdentityPasswordMessage(string method) =>
        $"Authentication method '{method}' requires a secure connection with a verified server certificate to send the password.";

    /// <summary>仅承认明确的 Schannel 证书告警，普通 IOException 或非法消息不能冒充证书拒绝。</summary>
    [Theory]
    [InlineData(unchecked((int)0x80090325), true)]
    [InlineData(unchecked((int)0x80090327), true)]
    [InlineData(unchecked((int)0x80090326), false)]
    [InlineData(10054, false)]
    public void Client_certificate_alert_requires_specific_native_error(int code, bool expected)
        => Assert.Equal(expected, IsWindowsCertificateAlert([new IOException("synthetic"), new Win32Exception(code)]));

    // Windows 的 TLS unknown_ca / bad_certificate 对应独立证书状态；不按泛化握手故障放行。
    private static bool IsWindowsCertificateAlert(IEnumerable<Exception> errors) => errors.OfType<Win32Exception>()
        .Any(error => error.NativeErrorCode is unchecked((int)0x80090325) or unchecked((int)0x80090327));

    /// <summary>先验证本轮名称、路径、证书摘要与固定 DNS，避免错误配置连接其它数据库。</summary>
    private static async Task<Settings> ReadSettings()
    {
        var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(
            Environment.GetEnvironmentVariable("METARILL_MYSQL_TLS_ISOLATED_SETTINGS")!))!;
        if (!Regex.IsMatch(settings.RunId, "^[a-f0-9]{16}$") ||
            settings.Host != "deve-server.yinzon.com" || settings.IpAddress != "192.168.31.200" ||
            settings.Port is < 28000 or > 28999 || settings.Database != "metarill_tls_" + settings.RunId ||
            settings.User != "mtls_" + settings.RunId || !Regex.IsMatch(settings.Password, "^[a-f0-9]{64}$") ||
            !Guid.TryParseExact(settings.ServerUuid, "D", out _) ||
            settings.Hostname != "mt-tls-" + settings.RunId ||
            !Path.IsPathFullyQualified(settings.CertificateDirectory) || !Path.IsPathFullyQualified(settings.EvidenceDirectory))
            throw new InvalidOperationException("独占 TLS 固定目标或路径无效。");
        await VerifyFiles(settings);
        var addresses = await Dns.GetHostAddressesAsync(settings.Host);
        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.Equal(settings.IpAddress, address.ToString()));
        return settings;
    }

    /// <summary>证书文件以固定集合及摘要绑定，任何候选不能替换公开 CA 或客户端私钥。</summary>
    private static async Task VerifyFiles(Settings settings)
    {
        Assert.Equal(new[] { "ca.pem", "client-key.pem", "client.pem", "wrong-ca.pem", "wrong-client-key.pem", "wrong-client.pem" },
            settings.CertificateSha256.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, hash) in settings.CertificateSha256)
        {
            Assert.Matches("^[a-f0-9]{64}$", hash);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(
                Path.Combine(settings.CertificateDirectory, name)))));
        }
    }

    /// <summary>只改变本候选的明确配置；禁用池以保证每次实际完成握手。</summary>
    private static MySqlConnectionStringBuilder Builder(Settings settings, Vector vector) => new()
    {
        Server = vector.Host ?? settings.Host, Port = settings.Port, Database = settings.Database,
        UserID = settings.User + (vector.Mutual ? "_x509" : vector.UserSuffix), Password = vector.Password ?? settings.Password,
        SslMode = vector.Mode, SslCa = vector.Ca == "" ? "" : Path.Combine(settings.CertificateDirectory, vector.Ca),
        SslCert = vector.Client is null ? "" : Path.Combine(settings.CertificateDirectory, vector.Client + ".pem"),
        SslKey = vector.Client is null ? "" : Path.Combine(settings.CertificateDirectory, vector.Client + "-key.pem"),
        TlsVersion = vector.Protocol, Pooling = false, ConnectionTimeout = 5, DefaultCommandTimeout = 10
    };

    /// <summary>所有成功连接必须核对独占实例、数据库及受限账号。</summary>
    private static async Task Pin(MySqlConnection connection, Settings settings, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@hostname,@@server_uuid,DATABASE(),CURRENT_USER()";
        await using var reader = await command.ExecuteReaderAsync(token);
        Assert.True(await reader.ReadAsync(token));
        Assert.Equal(settings.Hostname, reader.GetString(0));
        Assert.Equal(settings.ServerUuid, reader.GetString(1));
        Assert.Equal(settings.Database, reader.GetString(2));
        Assert.Contains(reader.GetString(3), new[] { settings.User + "@%", settings.User + "_x509@%", settings.User + "_plain@%" });
        Assert.False(await reader.ReadAsync(token));
    }

    /// <summary>读取当前连接的实际加密状态，而不是仅核对连接字符串。</summary>
    private static async Task<(string Version, string Cipher)> TlsStatus(MySqlConnection connection,
        System.Data.Common.DbTransaction? transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)transaction;
        command.CommandText = "SHOW SESSION STATUS WHERE Variable_name IN ('Ssl_cipher','Ssl_version')";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) values.Add(reader.GetString(0), reader.GetString(1));
        return (values["Ssl_version"], values["Ssl_cipher"]);
    }

    /// <summary>测试表名称及行 ID 固定，不读取其它表。</summary>
    private static async Task<string> ProbeValue(MySqlConnection connection,
        System.Data.Common.DbTransaction? transaction, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)transaction;
        command.CommandText = "SELECT value FROM tls_probe WHERE id=1";
        return (string)(await command.ExecuteScalarAsync(token))!;
    }

    /// <summary>输出独占脱敏文件；证据已存在便拒绝覆盖。</summary>
    private static async Task WriteEvidence<T>(Settings settings, string name, T value)
    {
        await using var stream = new FileStream(Path.Combine(settings.EvidenceDirectory, name), FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<Exception> ErrorChain(Exception error)
    {
        var result = new List<Exception>();
        for (Exception? next = error; next is not null; next = next.InnerException) result.Add(next);
        return result;
    }

    private sealed class RollbackProbe : Exception;
    private sealed class TlsSessionStatus
    {
        public string Variable_name { get; set; } = "";
        public string Value { get; set; } = "";
    }
    private sealed record Vector(string Name, bool Expected, string Protocol, string? Host = null,
        MySqlSslMode Mode = MySqlSslMode.VerifyFull, string Ca = "ca.pem", bool Mutual = false,
        string? Client = null, string? Password = null, string? Rejection = null, string UserSuffix = "");
    private sealed record VectorResult(string Name, bool Passed, bool Connected, string? Protocol,
        string? Cipher, int? MySqlError, string[] ErrorTypes, int[] NativeErrors);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Settings(string RunId, string Host, string IpAddress, uint Port, string Database,
        string User, string Password, string Hostname, string ServerUuid, string CertificateDirectory,
        string EvidenceDirectory, Dictionary<string, string> CertificateSha256);
}
