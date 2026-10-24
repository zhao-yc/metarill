using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using MySqlConnector;

namespace Metarill.Agent.Tests;

/// <summary>仅解释真实SqlRowStore的固定参数化单表命令；不联网、不代替认证和授权判断。</summary>
internal sealed class PrincipalRealmFixture : IDisposable
{
    internal RealmMemoryProvider Provider { get; } = new();
    internal MetadataRegistry Registry { get; }
    internal DatabaseSession Session { get; }
    internal SqlRowStore Store { get; }
    internal AuthenticationService Authentication { get; }
    internal AgentOAuthService OAuth { get; }
    internal IIdGenerator Ids { get; } = new SnowflakeIdGenerator(879);
    internal string PlatformProject => Registry.Platform.YzProjectId;
    internal string DemoProject => Registry.Demo.YzProjectId;
    internal string PlatformAdmin => Registry.Platform.YzAdminTypes[0].Id;
    internal string DemoAdmin => Registry.Demo.YzAdminTypes[0].Id;
    internal string DemoPage => Registry.Demo.ConfigPages[0].Id;
    internal string Token { get; } = new('a', 64);
    internal Dictionary<string,object?> Account => Provider.Rows["yz_manager"][0];
    internal Dictionary<string,object?> Role => Provider.Rows["yz_role"][0];
    internal Dictionary<string,object?> WebSession => Provider.Rows["sys_session"][0];

    internal PrincipalRealmFixture(bool business=false, bool super=true, [CallerFilePath]string file="")
    {
        var backend=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../.."));
        Registry=new(Path.Combine(backend,"metadata/platform.json"),Path.Combine(backend,"metadata/demo.json"));
        Session=new(new(){ExpectedDatabase="offline",ConnectionString="offline"},Provider.Orm,Provider);
        Store=new(Session,Registry.Platform); Authentication=new(Store,Ids,Registry,new PlatformSecuritySessionPolicy(Store)); OAuth=new(Store,Registry,Ids,new());
        Add("yz_project",("id",long.Parse(PlatformProject)),("name","平台")); Add("yz_project",("id",long.Parse(DemoProject)),("name","示例"));
        Add("yz_admin_type",("id",long.Parse(PlatformAdmin)),("yz_project_id",long.Parse(PlatformProject)));
        Add("yz_admin_type",("id",long.Parse(DemoAdmin)),("yz_project_id",long.Parse(DemoProject)));
        var admin=business?DemoAdmin:PlatformAdmin; var project=business?DemoProject:PlatformProject;
        Add("yz_role",("id",10L),("name","真实后台角色"),("yz_admin_type_id",long.Parse(admin)),("yz_project_id",long.Parse(project)),("is_super",super?1:0));
        Add("yz_manager",("id",8L),("name","realm-test"),("password",PasswordHasher.Hash("离线Realm-Password879")),("is_disabled",0),("yz_admin_type_id",long.Parse(admin)),("yz_project_id",long.Parse(project)),("yz_role_ids","[\"10\"]"));
        Add("sys_session",("id",20L),("account_id",8L),("token_hash",PasswordHasher.Digest(Token)),("kind","web"),("expires_at",DateTime.UtcNow.AddHours(1)),("project_id",long.Parse(project)));
    }
    internal void Add(string table,params(string Key,object? Value)[] values)
    { if(!Provider.Rows.TryGetValue(table,out var rows))Provider.Rows[table]=rows=[]; rows.Add(values.ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal)); }
    internal Dictionary<string,object?> Row(string table,long id)=>Provider.Rows[table].Single(row=>SqlRowStore.Id(row)==id);
    internal async Task<AuthenticatedIdentity> Identity(string? project=null)=>await Authentication.AuthenticateAsync(Token,project)??throw new InvalidOperationException("离线身份未建立。");
    /// <summary>离线测试仍用平台真实同事务适配，不以发布目录或空proof代替当前读取。</summary>
    internal SecurityRealmCurrentReader CurrentReader()=>new(Store,Registry,new PlatformSecurityCurrentResources(Store,new(Store,Registry)),new PlatformSecurityCurrentSessionPolicy(Store));
    public void Dispose()=>Session.Dispose();
}

internal sealed class RealmMemoryProvider : IDatabaseProvider
{
    internal Dictionary<string,List<Dictionary<string,object?>>> Rows {get;set;}=new(StringComparer.Ordinal);
    internal List<string> Commands {get;}=[]; internal IFreeSql Orm {get;}=DispatchProxy.Create<IFreeSql,ForbiddenRealmOrm>();
    public string Name=>"offline"; public DatabaseCapabilities Capabilities=>new("offline",true,false);
    public void Validate(DatabaseOptions options){} public IFreeSql CreateOrm(DatabaseOptions options)=>Orm;
    public DbConnection CreateConnection(DatabaseOptions options)=>new MemoryConnection(this);
    public string QuoteIdentifier(string id)=>new MySqlDatabaseProvider().QuoteIdentifier(id);
    public string ParameterPlaceholder(string name)=>"@"+name;
    public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);
    internal DbDataReader Read(string sql,DbParameterCollection parameters)
    {
        Commands.Add(sql); var match=Regex.Match(sql,@"^SELECT (.+) FROM \x60([^\x60]+)\x60(?: WHERE (.*?))?(?: FOR UPDATE)?$"); if(!match.Success)throw new NotSupportedException("离线夹具拒绝未知查询。");
        var names=match.Groups[1].Value.Split(',').Select(value=>value.Trim((char)96)).ToArray();var data=new DataTable();foreach(var name in names)data.Columns.Add(name,typeof(object));
        foreach(var row in Rows.GetValueOrDefault(match.Groups[2].Value)??[])
        {
            var accepted=true;foreach(Match condition in Regex.Matches(match.Groups[3].Value,@"\x60([^\x60]+)\x60( IS NULL|=@([a-zA-Z0-9_]+))"))
                accepted &= Equal(row.GetValueOrDefault(condition.Groups[1].Value),condition.Groups[2].Value==" IS NULL"?null:parameters[condition.Groups[3].Value].Value);
            foreach(Match set in Regex.Matches(match.Groups[3].Value,@"\x60([^\x60]+)\x60 IN \((@[a-zA-Z0-9_]+(?:,@[a-zA-Z0-9_]+)*)\)"))
                accepted &= set.Groups[2].Value.Split(',').Any(parameter => Equal(row.GetValueOrDefault(set.Groups[1].Value), parameters[parameter.TrimStart('@')].Value));
            if(accepted)data.Rows.Add(names.Select(name=>row.GetValueOrDefault(name)??DBNull.Value).ToArray());
        }
        return data.CreateDataReader();
    }
    internal int Write(string sql,DbParameterCollection parameters)
    {
        Commands.Add(sql);var update=Regex.Match(sql,@"^UPDATE \x60([^\x60]+)\x60 SET (.+) WHERE \x60id\x60=@record_id$");
        if(update.Success)
        {
            var row=(Rows.GetValueOrDefault(update.Groups[1].Value)??[]).SingleOrDefault(row=>Equal(row.GetValueOrDefault("id"),parameters["record_id"].Value)); if(row is null)return 0;
            foreach(Match field in Regex.Matches(update.Groups[2].Value,@"\x60([^\x60]+)\x60=@([a-zA-Z0-9_]+)"))row[field.Groups[1].Value]=Unwrap(parameters[field.Groups[2].Value].Value);return 1;
        }
        var insert=Regex.Match(sql,@"^INSERT INTO \x60([^\x60]+)\x60 \((.+)\) VALUES \((.+)\)$");if(!insert.Success)throw new NotSupportedException("离线夹具拒绝未知写入。");
        var names=insert.Groups[2].Value.Split(',').Select(value=>value.Trim((char)96)).ToArray();var values=insert.Groups[3].Value.Split(',').Select(value=>Unwrap(parameters[value.TrimStart('@')].Value)).ToArray();
        if(!Rows.TryGetValue(insert.Groups[1].Value,out var rows))Rows[insert.Groups[1].Value]=rows=[];
        rows.Add(names.Select((name,index)=>(name,index)).ToDictionary(pair=>pair.name,pair=>values[pair.index],StringComparer.Ordinal));return 1;
    }
    private static object? Unwrap(object? value)=>value is DBNull?null:value;
    private static bool Equal(object? left,object? right)=>string.Equals(Convert.ToString(Unwrap(left),CultureInfo.InvariantCulture),Convert.ToString(Unwrap(right),CultureInfo.InvariantCulture),StringComparison.Ordinal)&&((Unwrap(left)is null)==(Unwrap(right)is null));
    private sealed class MemoryConnection(RealmMemoryProvider provider):DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;
        public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new MemoryTransaction(this,provider);protected override DbCommand CreateDbCommand()=>new MemoryCommand(provider,this);
    }
    private sealed class MemoryTransaction(MemoryConnection connection,RealmMemoryProvider provider):DbTransaction
    {
        private readonly Dictionary<string,List<Dictionary<string,object?>>> before=provider.Rows.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(row=>new Dictionary<string,object?>(row,StringComparer.Ordinal)).ToList(),StringComparer.Ordinal);
        public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback()=>provider.Rows=before;
    }
    private sealed class MemoryCommand(RealmMemoryProvider provider,MemoryConnection connection):DbCommand
    {
        private readonly MySqlCommand parameters=new();[System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override object? ExecuteScalar()=>throw new NotSupportedException();public override int ExecuteNonQuery()=>provider.Write(CommandText,Parameters);protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>provider.Read(CommandText,Parameters);
    }
}
public class ForbiddenRealmOrm:DispatchProxy
{
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name=="Dispose"?null:throw new InvalidOperationException("身份测试禁止ORM或联网数据库。");
}
