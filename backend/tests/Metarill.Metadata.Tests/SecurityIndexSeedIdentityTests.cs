using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>实际调用MetadataRepository.SeedAsync的离线索引ID幂等合同；不连接实库、不更改种子语义。</summary>
public sealed class SecurityIndexSeedIdentityTests
{
    /// <summary>模拟工作台已经生成的真实9ID与既有用户内容，连续Seed不得再次插入/复写或复活墓碑。</summary>
    [Fact]
    public async Task Published_workbench_ids_skip_existing_records_including_tombstones()
    {
        var (registry,catalog)=Load();var provider=new MemoryProvider();var tables=new[]{("config_index",catalog.ConfigIndexes),("config_index_column",catalog.ConfigIndexColumns)};
        foreach(var (table,records) in tables)provider.Rows[table]=records.Select((record,index)=>new Dictionary<string,object?>{{"id",long.Parse(record.GetProperty("id").GetString()!,CultureInfo.InvariantCulture)},{"delete_no",index==0?new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc):null},{"index_name","既有用户内容不被seed改写"}}).ToList();
        var before=JsonSerializer.Serialize(provider.Rows);using var session=Session(provider);var repository=new MetadataRepository(new SqlRowStore(session,registry.Platform),registry);
        await repository.SeedAsync(catalog);await repository.SeedAsync(catalog);
        Assert.Equal(before,JsonSerializer.Serialize(provider.Rows));Assert.DoesNotContain(provider.Commands,sql=>sql.StartsWith("INSERT",StringComparison.Ordinal)||sql.StartsWith("UPDATE",StringComparison.Ordinal)||sql.StartsWith("DELETE",StringComparison.Ordinal));
        Assert.Equal(4,provider.Commands.Count(sql=>sql.StartsWith("SELECT",StringComparison.Ordinal)&&(sql.Contains(" FROM `config_index`",StringComparison.Ordinal)||sql.Contains(" FROM `config_index_column`",StringComparison.Ordinal))));
        Assert.All(provider.Commands,sql=>Assert.DoesNotContain(" IS NULL",sql,StringComparison.Ordinal));
    }
    /// <summary>同名但不同ID不构成幂等证明：实际Seed仍尝试插入缺失的9ID；次轮不写，本测试不模拟实库唯一约束。</summary>
    [Fact]
    public async Task Same_names_with_other_ids_still_attempt_inserts_then_matching_ids_are_write_free()
    {
        var (registry,catalog)=Load();var provider=new MemoryProvider();provider.Rows["config_index"]=catalog.ConfigIndexes.Select((record,index)=>new Dictionary<string,object?>{{"id",2200000000000101012L+index*3},{"index_name",record.GetProperty("indexName").GetString()}}).ToList();using var session=Session(provider);var repository=new MetadataRepository(new SqlRowStore(session,registry.Platform),registry);
        await repository.SeedAsync(catalog);Assert.Equal(9,provider.Commands.Count(sql=>sql.StartsWith("INSERT INTO",StringComparison.Ordinal)));var inserted=JsonSerializer.Serialize(provider.Rows);provider.Commands.Clear();await repository.SeedAsync(catalog);
        Assert.Equal(inserted,JsonSerializer.Serialize(provider.Rows));Assert.DoesNotContain(provider.Commands,sql=>!sql.StartsWith("SELECT",StringComparison.Ordinal));Assert.Equal(6,provider.Rows["config_index"].Count);Assert.Equal(6,provider.Rows["config_index_column"].Count);
    }
    /// <summary>只提取本次三索引/六引用，仍使用实际平台物理定义构造SqlRowStore，所有ID读取精确字符串。</summary>
    private static (MetadataRegistry Registry,MetadataCatalog Catalog) Load()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)
        {
            var path=Path.Combine(dir.FullName,"backend/metadata/platform.json");if(!File.Exists(path))continue;var registry=new MetadataRegistry(path,Path.Combine(dir.FullName,"backend/metadata/demo.json"));
            var expected=new[]{"yzp_idx_role_permission_role_active","yzp_idx_role_admin_active","yzp_idx_manager_admin_active"};var indexes=registry.Platform.ConfigIndexes.Where(value=>expected.Contains(value.GetProperty("indexName").GetString(),StringComparer.Ordinal)).ToList();Assert.Equal(3,indexes.Count);var ids=indexes.Select(value=>value.GetProperty("id").GetString()).ToHashSet();var fields=registry.Platform.ConfigIndexColumns.Where(value=>ids.Contains(value.GetProperty("configIndexId").GetString())).ToList();Assert.Equal(6,fields.Count);
            return(registry,new MetadataCatalog{ConfigIndexes=indexes,ConfigIndexColumns=fields});
        }
        throw new InvalidOperationException("未找到固定平台目录。");
    }
    /// <summary>使用内存ADO驱动和不自动同步的ORM实例；地址仅本机不可用端口，不执行网络命令。</summary>
    private static DatabaseSession Session(MemoryProvider provider)=>new(new DatabaseOptions{Provider="mysql",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=offline;Password=offline",ExpectedDatabase="offline"},null,provider);
    /// <summary>仅解释SqlRowStore固定生成的参数化单表命令；不支持任意SQL，也不建立网络连接。</summary>
    private sealed class MemoryProvider:IDatabaseProvider
    {
        internal Dictionary<string,List<Dictionary<string,object?>>> Rows{get;set;}=new(StringComparer.Ordinal);internal List<string> Commands{get;}=[];internal int Rollbacks;
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);public DbConnection CreateConnection(DatabaseOptions options)=>new MemoryConnection(this);public string QuoteIdentifier(string id)=>new MySqlDatabaseProvider().QuoteIdentifier(id);public string ParameterPlaceholder(string name)=>"@"+name;public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);
        internal DbDataReader Read(string sql,DbParameterCollection parameters)
        {
            Commands.Add(sql);var match=Regex.Match(sql,@"^SELECT (.+) FROM `([^`]+)`(?: WHERE (.*?))?(?: FOR UPDATE)?$");if(!match.Success)throw new NotSupportedException(sql);
            var names=match.Groups[1].Value.Split(',').Select(value=>value.Trim('`')).ToArray();var data=new DataTable();foreach(var name in names)data.Columns.Add(name,typeof(object));
            foreach(var row in Rows.GetValueOrDefault(match.Groups[2].Value)??[])
            {
                var conditions=match.Groups[3].Value;var accepted=true;
                foreach(Match item in Regex.Matches(conditions,@"`([^`]+)`( IS NULL|=@([a-zA-Z0-9_]+))"))
                {var actual=row.GetValueOrDefault(item.Groups[1].Value);var expected=item.Groups[2].Value==" IS NULL"?null:parameters[item.Groups[3].Value].Value;accepted&=Equal(actual,expected);}
                if(accepted)data.Rows.Add(names.Select(name=>row.GetValueOrDefault(name)??DBNull.Value).ToArray());
            }
            return data.CreateDataReader();
        }
        internal int Write(string sql,DbParameterCollection parameters)
        {
            Commands.Add(sql);var update=Regex.Match(sql,@"^UPDATE `([^`]+)` SET (.+) WHERE `id`=@record_id$");
            if(update.Success)
            {
                var row=(Rows.GetValueOrDefault(update.Groups[1].Value)??[]).SingleOrDefault(row=>Equal(row.GetValueOrDefault("id"),parameters["record_id"].Value));if(row is null)return 0;
                foreach(Match field in Regex.Matches(update.Groups[2].Value,@"`([^`]+)`=@([a-zA-Z0-9_]+)"))row[field.Groups[1].Value]=Unwrap(parameters[field.Groups[2].Value].Value);return 1;
            }
            var insert=Regex.Match(sql,@"^INSERT INTO `([^`]+)` \((.+)\) VALUES \((.+)\)$");if(!insert.Success)throw new NotSupportedException(sql);
            var names=insert.Groups[2].Value.Split(',').Select(name=>name.Trim('`')).ToArray();var values=insert.Groups[3].Value.Split(',').Select(name=>Unwrap(parameters[name.TrimStart('@')].Value)).ToArray();var target=insert.Groups[1].Value;if(!Rows.TryGetValue(target,out var list))Rows[target]=list=[];list.Add(names.Select((name,index)=>(name,index)).ToDictionary(item=>item.name,item=>values[item.index],StringComparer.Ordinal));return 1;
        }
        private static object? Unwrap(object? value)=>value is DBNull?null:value;
        private static bool Equal(object? first,object? second)=>string.Equals(Convert.ToString(Unwrap(first),CultureInfo.InvariantCulture),Convert.ToString(Unwrap(second),CultureInfo.InvariantCulture),StringComparison.Ordinal)&&((Unwrap(first)is null)==(Unwrap(second)is null));
    }
    private sealed class MemoryConnection(MemoryProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new MemoryTransaction(this,provider);protected override DbCommand CreateDbCommand()=>new MemoryCommand(provider,this);
    }
    private sealed class MemoryTransaction(MemoryConnection connection,MemoryProvider provider):DbTransaction
    {
        private readonly Dictionary<string,List<Dictionary<string,object?>>> before=provider.Rows.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(row=>new Dictionary<string,object?>(row,StringComparer.Ordinal)).ToList(),StringComparer.Ordinal);
        public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback(){provider.Rollbacks++;provider.Rows=before;}
    }
    private sealed class MemoryCommand(MemoryProvider provider,MemoryConnection connection):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override object? ExecuteScalar()=>throw new NotSupportedException();public override int ExecuteNonQuery()=>provider.Write(CommandText,Parameters);protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Read(CommandText,Parameters);
    }
}