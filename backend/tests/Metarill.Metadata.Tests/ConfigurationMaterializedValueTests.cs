using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeSql;
using MySqlConnector;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>真实SqlRowStore/观察器/工作台的离线命令回归；内存命令适配器不证明MySQL锁或隔离行为。</summary>
public sealed class ConfigurationMaterializedValueTests
{
    [Fact]
    public void Fixed_values_overwrite_stale_copy_and_preserve_real_null()
    {
        var values=Row(("config_column_column_name","旧值"),("config_column_sort_no",99));
        ConfigurationMaterializedValues.PageColumn(values,Row(("column_name","actual"),("sort_no",null)));
        Assert.Equal("actual",values["config_column_column_name"]);Assert.Null(values["config_column_sort_no"]);
        ConfigurationMaterializedValues.RequirePageColumn(values,Row(("column_name","actual"),("sort_no",null)));
        values["config_column_sort_no"]="";Assert.Throws<InvalidOperationException>(()=>ConfigurationMaterializedValues.RequirePageColumn(values,Row(("column_name","actual"),("sort_no",null))));
    }

    [Fact]
    public async Task Column_rename_and_null_sort_update_only_active_references_and_tail_failure_rolls_back()
    {
        using var f=new Fixture();f.Add("config_page_column",Row(("id",41L),("yz_project_id",1L),("config_column_id",20L),("config_page_id",30L),("config_column_column_name","软删旧值"),("config_column_sort_no",99),("delete_no",DateTime.UtcNow)));
        await f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"column_name","new_column"},{"sort_no",null}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]="new_column",["sort_no"]=null}));return true;
        });
        Assert.Equal("new_column",f.Get("config_page_column",40)["config_column_column_name"]);Assert.Null(f.Get("config_page_column",40)["config_column_sort_no"]);Assert.Equal("软删旧值",f.Get("config_page_column",41)["config_column_column_name"]);
        var before=f.Snapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>
        {
            await f.Store.UpdateAsync("config_column",20,new Dictionary<string,object?>{{"column_name","rolled_back"}});
            await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]="rolled_back"}));
            return await Task.FromException<bool>(new InvalidOperationException("模拟同事务后续观察器失败"));
        }));Assert.Equal(before,f.Snapshot());Assert.Equal(1,f.Provider.Rollbacks);
    }

    [Fact]
    public async Task Foreign_reference_is_rejected_before_any_reference_lock_or_write()
    {
        using var f=new Fixture();f.Get("config_page_column",40)["yz_project_id"]=2L;
        var before=f.Snapshot();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Session.InTransactionAsync(async()=>
        {await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["column_name"]="source_column"}));return true;}));
        Assert.Equal(before,f.Snapshot());Assert.DoesNotContain(f.Provider.Commands,sql=>sql.Contains("FROM `config_page_column`",StringComparison.Ordinal)&&sql.EndsWith("FOR UPDATE",StringComparison.Ordinal));
    }

    [Fact]
    public async Task Page_column_omission_keeps_history_but_create_rejects_forged_cache_and_wrong_owner()
    {
        using var f=new Fixture();
        await f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,new(){["name"]="仅名称"}));return true;});
        Assert.Equal("stale_name",f.Get("config_page_column",40)["config_column_column_name"]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,new(),true));return true;}));
        f.Get("config_page",30)["config_table_id"]=999L;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_page_column",40,new()));return true;}));
    }

    [Fact]
    public async Task Real_sort_workbench_and_copy_page_use_current_target_values()
    {
        using var f=new Fixture();var workbench=new MetadataWorkbenchBusiness(f.Store,f.Authorization,f.Registry,f.Ids,f.Observer);
        var snapshot=await workbench.ColumnOrderAsync("10");
        await workbench.SaveColumnOrderAsync(JsonSerializer.SerializeToElement(new[]{new{id="20",sortNo=77}}),snapshot.Revision);
        Assert.Equal(77,Convert.ToInt32(f.Get("config_page_column",40)["config_column_sort_no"],CultureInfo.InvariantCulture));
        // 排序只传播排序，不替用户执行历史名称回填。
        Assert.Equal("stale_name",f.Get("config_page_column",40)["config_column_column_name"]);
        var business=new ConfigurationActionsBusiness(f.Store,f.Registry,new MetadataRepository(f.Store,f.Registry),f.Authorization,f.Ids,f.Observer);
        var result=await business.CopyPageAsync("30");var pageId=long.Parse(result.PageId!,CultureInfo.InvariantCulture);
        Assert.Equal("actual_table",f.Get("config_page",pageId)["config_table_table_name"]);
        var field=f.Provider.Rows["config_page_column"].Single(row=>Convert.ToInt64(row["config_page_id"],CultureInfo.InvariantCulture)==pageId);
        Assert.Equal("source_column",field["config_column_column_name"]);Assert.Equal(77,Convert.ToInt32(field["config_column_sort_no"],CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Automatic_new_page_fields_receive_both_values_without_touching_old_fields()
    {
        using var f=new Fixture();f.Provider.Rows["config_page_column"].Clear();
        await f.Session.InTransactionAsync(async()=>{await f.Observer.BeforeCommitAsync(f.Context("config_column",20,new(){["name"]="仅标签"}));return true;});
        var created=Assert.Single(f.Provider.Rows["config_page_column"]);Assert.Equal("source_column",created["config_column_column_name"]);Assert.Equal(10,Convert.ToInt32(created["config_column_sort_no"],CultureInfo.InvariantCulture));
    }

    private static Dictionary<string,object?> Row(params(string Key,object? Value)[] fields)=>fields.ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal);
    private sealed class Fixture:IDisposable
    {
        internal readonly MemoryProvider Provider=new();internal DatabaseSession Session{get;}internal MetadataRegistry Registry{get;}internal SqlRowStore Store{get;}internal ConfigurationMutationObserver Observer{get;}internal HttpRuntimeAuthorization Authorization{get;}internal IIdGenerator Ids{get;}=new SnowflakeIdGenerator(829);
        internal Fixture([CallerFilePath]string file="")
        {
            var backend=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../.."));Registry=new(Path.Combine(backend,"metadata/platform.json"),Path.Combine(backend,"metadata/demo.json"));
            var options=new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1"};
            Session=new(options,null,Provider);Store=new(Session,Registry.Platform);Observer=new(Store,Registry,Ids);
            var http=new DefaultHttpContext();http.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(new(8,"离线配置测试",true,"1",""),new HashSet<string>(),1,null);Authorization=new(new HttpContextAccessor{HttpContext=http},Registry);
            Add("yz_project",Row(("id",1L)));Add("yz_admin_type",Row(("id",90L),("yz_project_id",1L)));
            Add("config_table",Row(("id",10L),("yz_project_id",1L),("table_name","actual_table"),("name","表"),("yz_admin_type_id",90L)));
            Add("config_column",Row(("id",20L),("yz_project_id",1L),("config_table_id",10L),("column_name","source_column"),("name","源字段"),("sort_no",10),("logical_type","string"),("column_type","nvarchar")));
            Add("config_page",Row(("id",30L),("yz_project_id",1L),("config_table_id",10L),("name","原页"),("yz_admin_type_id",90L),("config_table_table_name","old_table")));
            Add("config_page_column",Row(("id",40L),("yz_project_id",1L),("config_column_id",20L),("config_page_id",30L),("config_column_column_name","stale_name"),("config_column_sort_no",99)));
        }
        internal void Add(string table,Dictionary<string,object?> row){if(!Provider.Rows.TryGetValue(table,out var rows))Provider.Rows[table]=rows=[];rows.Add(row);}
        internal Dictionary<string,object?> Get(string table,long id)=>Provider.Rows[table].Single(row=>Convert.ToInt64(row["id"],CultureInfo.InvariantCulture)==id);
        internal RecordMutationContext Context(string table,long id,Dictionary<string,object?> submitted,bool creating=false)=>new("offline",table,id,"1",submitted,false,Session.CurrentTransaction!,creating);
        internal string Snapshot()=>JsonSerializer.Serialize(Provider.Rows);
        public void Dispose()=>Session.Dispose();
    }

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
