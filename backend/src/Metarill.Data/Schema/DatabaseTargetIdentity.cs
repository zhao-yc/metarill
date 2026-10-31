using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Metarill.Data.Schema;

/// <summary>实际实例/数据库的不可逆标识，不包含连接串、凭据或可执行SQL。</summary>
public sealed record DatabaseTargetIdentity(string Provider, string Database, string Fingerprint, bool CaseSensitiveNames);

public sealed partial class SchemaService
{
    /// <summary>归属判断使用实际实例和数据库，不用连接串文本或主机别名推断是否同库。</summary>
    public async Task<DatabaseTargetIdentity> InspectTargetIdentityAsync(CancellationToken token = default)
    {
        EnsureMySql();
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(token);
        var identity = await ValidateTargetAsync(connection, token);
        var mode = Convert.ToInt32(await ScalarAsync(connection, "SELECT @@lower_case_table_names", token), CultureInfo.InvariantCulture);
        return TargetIdentity(identity, mode);
    }

    /// <summary>名称规则由适配器读取；大小写不敏感的数据库身份按同一规则归一。</summary>
    private DatabaseTargetIdentity TargetIdentity(string identity, int mode)
    {
        if (mode is < 0 or > 2) throw new InvalidOperationException("无法确认数据库名称规则。");
        var normalized = mode == 0 ? identity : identity.ToLowerInvariant();
        return new(session.Provider.Name, session.Options.ExpectedDatabase, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(session.Provider.Name + "\0" + normalized))), mode == 0);
    }
}
