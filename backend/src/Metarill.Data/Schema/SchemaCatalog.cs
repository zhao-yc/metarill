using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Data.Schema;

/// <summary>物理目录只包含名称、对象类型及引擎，不读取业务行或执行结构计划。</summary>
public sealed record SchemaCatalogObject(string Name, string ObjectType, string? Engine, bool SupportedIdentifier);
public sealed record SchemaCatalogSnapshot(string Provider, string Database, IReadOnlyList<SchemaCatalogObject> Objects, string CatalogFingerprint, bool CaseSensitiveNames);

public sealed partial class SchemaService
{
    /// <summary>当前服务端连接的完整有界目录；超过预算明确拒绝，不能把截断结果当完整目录。</summary>
    public async Task<SchemaCatalogSnapshot> InspectCatalogAsync(CancellationToken token = default)
    {
        EnsureMySql();
        await using var connection = session.Provider.CreateConnection(session.Options);
        await connection.OpenAsync(token);
        await ValidateTargetAsync(connection, token);
        var caseMode = Convert.ToInt32(await ScalarAsync(connection, "SELECT @@lower_case_table_names", token), System.Globalization.CultureInfo.InvariantCulture);
        if (caseMode is < 0 or > 2) throw new InvalidOperationException("无法确认数据库对象名称比较规则。");
        var caseSensitiveNames = caseMode == 0;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TABLE_NAME,TABLE_TYPE,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() ORDER BY BINARY TABLE_NAME LIMIT 4097";
        command.CommandTimeout = 30;
        var objects = new List<SchemaCatalogObject>();
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                if (objects.Count == 4096) throw new InvalidOperationException("物理目录超过4096个对象，未返回截断目录；请先缩小服务端数据库范围。");
                var name = reader.GetString(0);
                var supported = true;
                try { MetadataValidation.RequireIdentifier(name); }
                catch (InvalidOperationException) { supported = false; }
                objects.Add(new(name, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), supported));
            }
        }
        // 指纹只用于判断目录变化，不代表列、索引或DDL结构快照一致。
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { database = session.Options.ExpectedDatabase, caseSensitiveNames, objects });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new(session.Provider.Name, session.Options.ExpectedDatabase, objects, fingerprint, caseSensitiveNames);
    }
}
