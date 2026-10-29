using System.Data.Common;
using System.Globalization;
namespace Metarill.Data;

/// <summary>不含连接串或凭据的物理身份；数据库名已按适配器实际大小写规则归一。</summary>
public sealed record PhysicalDatabaseIdentity(string Provider,string ServerId,string Database);

public sealed partial class MySqlDatabaseProvider
{
    /// <summary>使用传入事务实际连接核验UUID、数据库和大小写规则，DNS别名或不同凭据不能绕过同库检测。</summary>
    public async Task<PhysicalDatabaseIdentity> ReadPhysicalDatabaseIdentityAsync(DatabaseOptions options,DbTransaction transaction,CancellationToken cancellationToken=default)
    {
        Validate(options);var connection=transaction.Connection??throw new InvalidOperationException("数据库身份核验必须使用活跃事务。");
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT @@server_uuid,DATABASE(),@@lower_case_table_names";
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken)||reader.IsDBNull(0)||reader.IsDBNull(1)||reader.IsDBNull(2))throw new InvalidOperationException("无法确认物理数据库身份。");
        var server=reader.GetString(0);var database=reader.GetString(1);var casing=Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture);
        if(string.IsNullOrWhiteSpace(server)||string.IsNullOrWhiteSpace(database)||casing is not(0 or 1 or 2)||await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("物理数据库身份无效。");
        var expected=options.ExpectedDatabase;
        if(casing!=0){database=database.ToLowerInvariant();expected=expected.ToLowerInvariant();}
        if(database!=expected)throw new InvalidOperationException("实际连接数据库与预期不一致。");
        return new("mysql",server.ToLowerInvariant(),database);
    }
}
