using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Data;
using Metarill.Data.Schema;
using MySqlConnector;
namespace Metarill.Paired.Indexes;

/// <summary>空合成文件表的独立显式迁移；不向平台SchemaService增加自动DROP能力。</summary>
public static class FileAccessIndexMigration
{
    private const string ServerUuid="6d3d024a-9d73-11f1-9939-ce69308a21dc";
    /// <summary>只有固定离线动作或两端预览／确认应用／核验；未知动作在口令和数据库之前拒绝。</summary>
    public static (string Operation,string? Target) ParseMode(string[] args)
    {
        if(args.Length!=1)throw new ArgumentException("必须指定唯一文件索引动作。");if(args[0]=="file-offline")return("offline",null);var parts=args[0].Split('-');
        if(parts.Length!=3||parts[0]!="file"||parts[1] is not("preview" or "apply" or "verify")||parts[2] is not("java" or "csharp"))throw new ArgumentException("未知文件索引动作。");return(parts[1],parts[2]);
    }
    /// <summary>明确的原生文件用途和profile必须与协调器一致，不借用普通只读或旧功能认证。</summary>
    public static string RequireScope(string? run,string? authorization,string? scope,string? profile)
    {
        var result=Program.RequireRun(run,authorization);if(scope!=FileAccessIndexProfile.Scope||profile!=FileAccessIndexProfile.Name)throw new UnauthorizedAccessException("缺少当前共同文件索引范围。");return result;
    }
    /// <summary>执行前先核授权和固定原元数据；Apply单独取得原结构锁，CAS后仅执行编译的固定DDL。</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var mode=ParseMode(args);string? run=null;
        if(mode.Operation!="offline")run=RequireScope(Environment.GetEnvironmentVariable("METARILL_INDEX_RUN_ID"),Environment.GetEnvironmentVariable("METARILL_INDEX_AUTHORIZATION"),Environment.GetEnvironmentVariable("METARILL_FILE_INDEX_SCOPE"),Environment.GetEnvironmentVariable("METARILL_FILE_INDEX_PROFILE"));
        var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"metadata/project.json"));if(RelationIndexProfile.Hash(bytes)!=RelationIndexProfile.ExtendedWorkloadMetadataSha256)throw new InvalidOperationException("文件验收元数据不是原扩展包。");
        _=RelationIndexProfile.ReadBaseline(bytes);
        if(mode.Operation=="offline")
        {
            Console.WriteLine(JsonSerializer.Serialize(new{format=FileAccessIndexProfile.Name,offline=true,ddlExecuted=false,java=FileAccessIndexProfile.ExpectedChanges("java"),csharp=FileAccessIndexProfile.ExpectedChanges("csharp")},RelationIndexProfile.Json));return 0;
        }
        var target=mode.Target!;var options=Program.Options(run!,target,Environment.GetEnvironmentVariable("Database__ExpectedDatabase"),Environment.GetEnvironmentVariable("Database__ConnectionString"));
        var confirmation=Environment.GetEnvironmentVariable("METARILL_INDEX_EXPECTED_PLAN");if(mode.Operation=="apply"&&!System.Text.RegularExpressions.Regex.IsMatch(confirmation??"",@"\A[a-f0-9]{64}\z"))throw new UnauthorizedAccessException("缺少共同文件索引确认摘要。");
        using var session=new DatabaseSession(options);var service=new SchemaService(session,expectedServerUuid:ServerUuid);await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await ValidateConnection(connection,options.ExpectedDatabase,run!);var locked=false;var applied=0;
        try
        {
            if(mode.Operation=="apply")
            {
                var lockName="metarill:schema:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.ExpectedDatabase)))[..32];
                await using var take=new MySqlCommand("SELECT GET_LOCK(@name,10)",connection);take.Parameters.AddWithValue("@name",lockName);locked=Convert.ToInt32(await take.ExecuteScalarAsync(),CultureInfo.InvariantCulture)==1;if(!locked)throw new InvalidOperationException("文件索引未取得结构锁。");
            }
            await RequireEmpty(connection);var before=await service.InspectAsync(FileAccessIndexProfile.InspectionScope());FileAccessIndexProfile.RequireState(before,target,mode.Operation=="verify");var beforeStats=await ReadStatistics(connection);RequireStatistics(beforeStats,before);
            var protectedBefore=FileAccessIndexProfile.ProtectedFingerprint(before);var hash=mode.Operation=="verify"?RelationIndexProfile.Hash(JsonSerializer.SerializeToUtf8Bytes(new{run,target,before},RelationIndexProfile.Json)):FileAccessIndexProfile.PlanSha256(run!,target,before);
            var changes=mode.Operation=="verify"?Array.Empty<SchemaChange>():FileAccessIndexProfile.ExpectedChanges(target);
            DatabaseSchema current=before;IReadOnlyList<FileIndexStatistic> statistics=beforeStats;
            if(mode.Operation=="apply")
            {
                if(hash!=confirmation)throw new InvalidOperationException("文件索引预览后结构或目标变化。");
                await ValidateConnection(connection,options.ExpectedDatabase,run!);await RequireEmpty(connection);
                foreach(var change in changes){await using var command=new MySqlCommand(change.Sql,connection){CommandTimeout=15};await command.ExecuteNonQueryAsync();applied++;}
                current=await service.InspectAsync(FileAccessIndexProfile.InspectionScope());FileAccessIndexProfile.RequireState(current,target,true);statistics=await ReadStatistics(connection);RequireStatistics(statistics,current);await RequireEmpty(connection);
                if(FileAccessIndexProfile.ProtectedFingerprint(current)!=protectedBefore)throw new InvalidOperationException("文件索引迁移改变了原字段或其它DDL。");
            }
            Console.WriteLine(JsonSerializer.Serialize(new{format=FileAccessIndexProfile.Name,runId=run,target,operation=mode.Operation,database=options.ExpectedDatabase,planSha256=hash,changes,succeeded=true,appliedStatements=applied,protectedFingerprint=protectedBefore,beforeSchema=before,schema=current,indexSnapshot=statistics,emptyTablesVerified=true,columnsAndNonIndexDdlPreserved=true},RelationIndexProfile.Json));return 0;
        }
        catch(Exception error)
        {
            // MySQL DDL不能整体回滚；准确保留已成功条数，只交给外层独占库清理，不补跑或伪称恢复。
            Console.WriteLine(JsonSerializer.Serialize(new{format=FileAccessIndexProfile.Name,runId=run,target,operation=mode.Operation,database=options.ExpectedDatabase,succeeded=false,appliedStatements=applied,errorType=error.GetType().Name},RelationIndexProfile.Json));throw;
        }
        finally
        {
            if(locked){try{await using var release=new MySqlCommand("SELECT RELEASE_LOCK(@name)",connection);release.Parameters.AddWithValue("@name","metarill:schema:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.ExpectedDatabase)))[..32]);await release.ExecuteScalarAsync();}catch{/* 关闭连接也释放会话锁；不覆盖已记录的DDL首因和部分执行数量。 */}}
        }
    }
    /// <summary>每个实际连接复核本轮库／实例／临时账号，不能仅信连接字符串或另一条连接。</summary>
    private static async Task ValidateConnection(MySqlConnection connection,string database,string run)
    {
        await using var command=new MySqlCommand("SELECT DATABASE(),@@hostname,@@server_uuid,CURRENT_USER()",connection);await using var reader=await command.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader.GetString(0)!=database||reader.GetString(1)!="3107e214a8a3"||reader.GetString(2)!=ServerUuid||reader.GetString(3).Split('@')[0]!="mtf_"+run[..16])throw new UnauthorizedAccessException("文件索引实际连接身份不符。");
    }
    /// <summary>只允许初始化后仍为空的两文件表，不触碰历史业务文件或已有引用。</summary>
    private static async Task RequireEmpty(MySqlConnection connection)
    {
        foreach(var table in FileAccessIndexProfile.Tables){await using var command=new MySqlCommand("SELECT COUNT(*) FROM "+MySqlSchemaPlanner.Quote(table),connection);if(Convert.ToInt64(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture)!=0)throw new InvalidOperationException("文件索引迁移仅允许本轮空表。");}
    }
    /// <summary>除结构反查外独立读取真实STATISTICS，方向、可见性、前缀及存储类型都不能省略。</summary>
    private static async Task<IReadOnlyList<FileIndexStatistic>> ReadStatistics(MySqlConnection connection)
    {
        await using var command=new MySqlCommand("SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,SEQ_IN_INDEX,COALESCE(COLUMN_NAME,''),COALESCE(COLLATION,''),COALESCE(CAST(SUB_PART AS CHAR),''),INDEX_TYPE,IS_VISIBLE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME IN ('yz_file_library','yz_file_ref') ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX LIMIT 65",connection);var result=new List<FileIndexStatistic>();await using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync())result.Add(new(reader.GetString(0),reader.GetString(1),Convert.ToInt32(reader.GetValue(2),CultureInfo.InvariantCulture),Convert.ToInt32(reader.GetValue(3),CultureInfo.InvariantCulture),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8)));return result;
    }
    /// <summary>拒绝额外索引、表达式、前缀、不可见、唯一性或方向漂移，包括不能误删的主键。</summary>
    public static void RequireStatistics(IReadOnlyList<FileIndexStatistic> statistics,DatabaseSchema schema)
    {
        var expected=new List<FileIndexStatistic>();foreach(var table in schema.Tables){expected.Add(new(table.Name,"PRIMARY",0,1,"id","A","","BTREE","YES"));foreach(var index in table.Indexes??[])for(var i=0;i<index.Columns.Count;i++)expected.Add(new(table.Name,index.Name,index.Unique?0:1,i+1,index.Columns[i],(index.Directions?[i]??"ASC")=="ASC"?"A":"D","","BTREE","YES"));}
        if(statistics.Count>64||!statistics.OrderBy(x=>x.Table,StringComparer.Ordinal).ThenBy(x=>x.Index,StringComparer.Ordinal).ThenBy(x=>x.Sequence).SequenceEqual(expected.OrderBy(x=>x.Table,StringComparer.Ordinal).ThenBy(x=>x.Index,StringComparer.Ordinal).ThenBy(x=>x.Sequence)))throw new InvalidOperationException("文件表实际索引属性不匹配。");
    }
}
/// <summary>有界只读索引快照，不包含数据行、连接、口令或SQL正文。</summary>
public sealed record FileIndexStatistic(string Table,string Index,int NonUnique,int Sequence,string Column,string Direction,string Prefix,string Kind,string Visible);
