using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

/// <summary>只有显式常量SELECT确认及私有连接配置时运行；普通CI据实跳过。</summary>
public sealed class SqlObservationReadOnlyFactAttribute:FactAttribute
{
    public SqlObservationReadOnlyFactAttribute()
    {
        if(Environment.GetEnvironmentVariable("METARILL_SQL_OBSERVATION_READONLY_ACK")!="CONSTANT_SELECT_ONLY"||!File.Exists(Environment.GetEnvironmentVariable("METARILL_SQL_OBSERVATION_READONLY_SETTINGS")))Skip="未提供常量SELECT只读确认及专用私有设置。";
    }
}

/// <summary>真实MySQL只读验证：无表／业务数据读取，无DDL／DML／账号／权限／GLOBAL修改。</summary>
public sealed class DatabaseCommandObservationReadOnlyTests
{
    /// <summary>原生／FreeSql命令及原事务API均实际执行，观测前后响应相同，准备及绑定类型可回读。</summary>
    [SqlObservationReadOnlyFact]
    public async Task Actual_constant_selects_keep_values_and_capture_bound_parameters_without_writes()
    {
        var evidence=Path.Combine(Root(),".local/sql-command-observation-20261102/readonly",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(evidence);var stage="private-options";
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var token=timeout.Token;
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_SQL_OBSERVATION_READONLY_SETTINGS")!,token));
            var options=JsonSerializer.Deserialize<DatabaseOptions>(Property(Property(settings.RootElement,"Metarill"),"BusinessDatabase"),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var builder=new MySqlConnectionStringBuilder(options.ConnectionString);Assert.False(builder.UserID.Equals("root",StringComparison.OrdinalIgnoreCase));Assert.False(builder.IgnorePrepare);
            Assert.Contains(builder.Server,new[]{"192.168.31.200","deve-server.yinzon.com"});Assert.Equal(3306u,builder.Port);Assert.Equal("metarill_business_dev",options.ExpectedDatabase);
            var provider=new MySqlDatabaseProvider();provider.Validate(options);stage="read-only-instance-pin";
            await using(var connection=provider.CreateConnection(options))
            {
                await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT @@hostname,@@server_uuid,DATABASE(),CURRENT_USER()";await using var reader=await command.ExecuteReaderAsync(token);Assert.True(await reader.ReadAsync(token));
                Assert.Equal("3107e214a8a3",reader.GetString(0));Assert.Equal("6d3d024a-9d73-11f1-9939-ce69308a21dc",reader.GetString(1));Assert.Equal(options.ExpectedDatabase,reader.GetString(2));Assert.False(reader.GetString(3).StartsWith("root@",StringComparison.OrdinalIgnoreCase));Assert.False(await reader.ReadAsync(token));
            }
            using var session=new DatabaseSession(options);stage="observation-disabled-baseline";var baseline=await Constants(session,token);
            stage="observation-enabled-constants";var trace=DatabaseCommandObservation.Begin(Enumerable.Repeat((byte)11,32).ToArray());
            string observed;try{observed=await Constants(session,token);var value=await session.Orm.Ado.ExecuteScalarAsync("SELECT CAST(@value AS DOUBLE)",new{value=Math.ScaleB(1d,-958)},token);Assert.Equal(BitConverter.DoubleToInt64Bits(Math.ScaleB(1d,-958)),BitConverter.DoubleToInt64Bits(Assert.IsType<double>(value)));}finally{trace.Dispose();}
            Assert.Equal(baseline,observed);var snapshot=trace.Snapshot();Assert.True(snapshot.InternallyConsistent);Assert.Equal(3,snapshot.Commands.Count);Assert.Equal(2,snapshot.Transactions.Count);
            Assert.Equal(new[]{DatabaseTransactionOperation.Begin,DatabaseTransactionOperation.Commit},snapshot.Transactions.Select(entry=>entry.Operation));Assert.All(snapshot.Transactions,entry=>Assert.True(entry.Succeeded));
            Assert.All(snapshot.Commands,entry=>{Assert.True(entry.Succeeded);Assert.True(entry.DriverCalled);Assert.StartsWith("SELECT",entry.Sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain(" FROM ",entry.Sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain(';',entry.Sql);Assert.All(entry.Parameters,parameter=>Assert.NotNull(parameter.ValueHash));});
            var native=Assert.Single(snapshot.Commands,entry=>entry.Parameters.Count==7);Assert.True(native.Prepared);Assert.NotNull(native.Transaction);Assert.Contains(native.Parameters,p=>p.ValueKind=="double-bits"&&p.ProviderType=="Double");Assert.Contains(native.Parameters,p=>p.ValueKind=="integer"&&p.ProviderType=="Int64");Assert.Contains(native.Parameters,p=>p.ValueKind=="decimal");
            var orm=Assert.Single(snapshot.Commands,entry=>entry.Parameters.Count==1);Assert.True(orm.Prepared);Assert.Null(orm.Transaction);Assert.Equal(native.Parameters.Single(p=>p.Name=="value").ValueHash,Assert.Single(orm.Parameters).ValueHash);
            await File.WriteAllTextAsync(Path.Combine(evidence,"trace.json"),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}),token);
            await File.WriteAllTextAsync(Path.Combine(evidence,"result.json"),JsonSerializer.Serialize(new{passed=true,constantSelectOnly=true,businessTablesRead=false,persistentObjectsChanged=false,globalOrSqlModeChanged=false,nonRoot=true,instancePinned=true,responseEqual=true,commands=snapshot.Commands.Count,transactions=snapshot.Transactions.Count,rawAndOrmPrepared=true,rawParameterValuesWritten=false,javaObserved=false,performanceAccepted=false}),token);
        }
        catch(Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(evidence,"failure.json"),JsonSerializer.Serialize(new{stage,type=error.GetType().FullName,mysqlErrorNumber=error is MySqlException mysql?(int?)mysql.Number:null}));
            throw new InvalidOperationException("常量只读SQL观测未通过；私有证据已保留，不回显连接或异常正文。");
        }
    }
    /// <summary>仅原事务内的身份SELECT与七参数常量SELECT，无任何表名或写入语句。</summary>
    private static Task<string> Constants(DatabaseSession session,CancellationToken token)=>session.InReadSnapshotAsync(async()=>
    {
        var transaction=session.CurrentTransaction!;var identity=await session.Provider.ReadPhysicalDatabaseIdentityAsync(session.Options,transaction,token);Assert.Equal("6d3d024a-9d73-11f1-9939-ce69308a21dc",identity.ServerId);Assert.Equal(session.Options.ExpectedDatabase,identity.Database);
        using var original=new MySqlCommand("SELECT @id,@amount,CAST(@value AS DOUBLE),@name,@bytes,CAST(@moment AS DATETIME(6)),CAST(@clock AS TIME(6))",(MySqlConnection)transaction.Connection!,(MySqlTransaction)transaction);
        var number=Math.ScaleB(1d,-958);var text="中文'\\常量";var bytes=new byte[]{0,1,255};var moment=new DateTime(2026,11,2,3,4,5,DateTimeKind.Unspecified).AddTicks(1234560);var clock=TimeSpan.FromHours(26)+TimeSpan.FromTicks(1234560);
        original.Parameters.AddWithValue("id",9007199254740993L);original.Parameters.AddWithValue("amount",0.1234567890123456789012345678m);original.Parameters.AddWithValue("value",number);original.Parameters.AddWithValue("name",text);original.Parameters.AddWithValue("bytes",bytes);original.Parameters.AddWithValue("moment",moment);original.Parameters.AddWithValue("clock",clock);
        await using var command=MySqlFloatingCommand.Wrap(original);await using var reader=await command.ExecuteReaderAsync(token);Assert.True(await reader.ReadAsync(token));Assert.Equal(9007199254740993L,reader.GetInt64(0));Assert.Equal(0.1234567890123456789012345678m,reader.GetDecimal(1));Assert.Equal(BitConverter.DoubleToInt64Bits(number),BitConverter.DoubleToInt64Bits(reader.GetDouble(2)));Assert.Equal(text,reader.GetString(3));Assert.Equal(bytes,reader.GetFieldValue<byte[]>(4));Assert.Equal(moment,reader.GetDateTime(5));Assert.Equal(clock,reader.GetFieldValue<TimeSpan>(6));var actual=string.Join("|",reader.GetInt64(0).ToString(System.Globalization.CultureInfo.InvariantCulture),reader.GetDecimal(1).ToString("G29",System.Globalization.CultureInfo.InvariantCulture),BitConverter.DoubleToInt64Bits(reader.GetDouble(2)).ToString("x16",System.Globalization.CultureInfo.InvariantCulture),reader.GetString(3),Convert.ToHexString(reader.GetFieldValue<byte[]>(4)),reader.GetDateTime(5).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),reader.GetFieldValue<TimeSpan>(6).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));Assert.False(await reader.ReadAsync(token));Assert.True(original.IsPrepared);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    },token);
    /// <summary>只在已读取的私有内存JSON中做不区分大小写的节定位。</summary>
    private static JsonElement Property(JsonElement value,string key)=>value.EnumerateObject().Single(p=>p.Name.Equals(key,StringComparison.OrdinalIgnoreCase)).Value;
    /// <summary>使用测试源路径定位证据根，避免借当前shell目录选择配置。</summary>
    private static string Root([System.Runtime.CompilerServices.CallerFilePath]string path="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,"../../.."));
}
