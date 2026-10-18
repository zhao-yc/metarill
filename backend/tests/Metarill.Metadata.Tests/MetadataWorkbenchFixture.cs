using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Metarill.Metadata.Tests;

public sealed class MetadataIntegrationFactAttribute : FactAttribute
{
    public MetadataIntegrationFactAttribute()
    { if (!File.Exists(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")) || !File.Exists(Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA"))) Skip="未提供隔离MySQL测试配置与平台物理元数据。"; }
}

/// <summary>仅创建随机ID的元数据项目，不创建实际业务表，不修改现有项目配置。</summary>
internal sealed class MetadataWorkbenchFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids = new(853);
    private readonly List<(string Table,long Id)> rows=[];
    internal DatabaseSession Session {get;private set;}=null!;
    internal MetadataRegistry Registry {get;private set;}=null!;
    internal SqlRowStore Store {get;private set;}=null!;
    internal DatabaseOptions Options {get;private set;}=null!;
    internal long ProjectId {get;private set;}
    internal long TableId {get;private set;}
    internal long OtherTableId {get;private set;}
    internal long PageId {get;private set;}
    internal long OtherPageId {get;private set;}
    internal long FirstColumnId {get;private set;}
    internal long SecondColumnId {get;private set;}
    internal long OtherColumnId {get;private set;}
    internal long FirstFieldId {get;private set;}
    internal long SecondFieldId {get;private set;}
    internal long OtherFieldId {get;private set;}
    /// <summary>每个业务实例拥有独立HTTP认证上下文，不能通过请求参数伪造管理员。</summary>
    internal MetadataWorkbenchBusiness Business(DatabaseSession? session=null,bool administrator=true,string? project=null)
    {
        var actor=new RuntimeActor(8012,"工作台测试",administrator,project??ProjectId.ToString(),Registry.Platform.YzAdminTypes[0].Id);
        var context=new DefaultHttpContext(); context.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(actor,new HashSet<string>(),1,null);
        var currentStore=new SqlRowStore(session??Session,Registry);
        return new(currentStore,new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},Registry),Registry,Ids,new ConfigurationMutationObserver(currentStore,Registry,Ids));
    }
    /// <summary>仅允许明确指定的metarill_test隔离数据库，源连接内容不写日志。</summary>
    internal static async Task<MetadataWorkbenchFixture> CreateAsync()
    {
        var fixture=new MetadataWorkbenchFixture();
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            fixture.Options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            if(fixture.Options.ExpectedDatabase!="metarill_test"||fixture.Options.Provider!="mysql")throw new InvalidOperationException("元数据集成测试只允许MySQL的metarill_test隔离库。");
            var metadata=Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
            fixture.Registry=new(metadata,Path.Combine(Path.GetDirectoryName(metadata)!,"demo.json"));
            fixture.Session=new(fixture.Options); fixture.Store=new(fixture.Session,fixture.Registry);
            fixture.ProjectId=Ids.NextId(); fixture.TableId=Ids.NextId(); fixture.OtherTableId=Ids.NextId(); fixture.PageId=Ids.NextId(); fixture.OtherPageId=Ids.NextId();
            fixture.FirstColumnId=Ids.NextId(); fixture.SecondColumnId=Ids.NextId(); fixture.OtherColumnId=Ids.NextId(); fixture.FirstFieldId=Ids.NextId(); fixture.SecondFieldId=Ids.NextId(); fixture.OtherFieldId=Ids.NextId();
            await fixture.Session.InTransactionAsync(async()=>
            {
                await fixture.Add("yz_project",fixture.ProjectId,new(){["name"]="工作台独立测试"});
                foreach(var id in new[]{fixture.TableId,fixture.OtherTableId})await fixture.Add("config_table",id,new(){["name"]="测试配置表",["table_name"]="mt_metadata_"+id,["yz_project_id"]=fixture.ProjectId,["is_rongyu"]=1});
                await fixture.Add("config_column",fixture.FirstColumnId,new(){["name"]="标题",["column_name"]="title",["logical_type"]="string",["column_type"]="nvarchar",["column_length"]="255",["config_table_id"]=fixture.TableId,["yz_project_id"]=fixture.ProjectId,["sort_no"]=10});
                await fixture.Add("config_column",fixture.SecondColumnId,new(){["name"]="备注",["column_name"]="notes",["logical_type"]="string",["column_type"]="nvarchar",["column_length"]="255",["config_table_id"]=fixture.TableId,["yz_project_id"]=fixture.ProjectId,["sort_no"]=20});
                await fixture.Add("config_column",fixture.OtherColumnId,new(){["name"]="其他表字段",["column_name"]="title",["logical_type"]="string",["column_type"]="nvarchar",["column_length"]="255",["config_table_id"]=fixture.OtherTableId,["yz_project_id"]=fixture.ProjectId,["sort_no"]=30});
                foreach(var id in new[]{fixture.PageId,fixture.OtherPageId})await fixture.Add("config_page",id,new(){["name"]="测试页面",["config_table_id"]=fixture.TableId,["yz_project_id"]=fixture.ProjectId,["yz_admin_type_id"]=long.Parse(fixture.Registry.Platform.YzAdminTypes[0].Id)});
                await fixture.Add("config_page_column",fixture.FirstFieldId,new(){["name"]="标题",["config_column_id"]=fixture.FirstColumnId,["config_page_id"]=fixture.PageId,["yz_project_id"]=fixture.ProjectId,["is_in_edit"]=1,["is_in_grid"]=1,["is_must"]=0});
                await fixture.Add("config_page_column",fixture.SecondFieldId,new(){["name"]="备注",["config_column_id"]=fixture.SecondColumnId,["config_page_id"]=fixture.PageId,["yz_project_id"]=fixture.ProjectId,["is_in_edit"]=1,["is_in_grid"]=1,["is_must"]=0});
                await fixture.Add("config_page_column",fixture.OtherFieldId,new(){["name"]="另一页标题",["config_column_id"]=fixture.FirstColumnId,["config_page_id"]=fixture.OtherPageId,["yz_project_id"]=fixture.ProjectId,["is_in_edit"]=1,["is_in_grid"]=1,["is_must"]=0});
                return true;
            });
            return fixture;
        }
        catch {await fixture.DisposeAsync();throw;}
    }
    /// <summary>跟踪当前测试的唯一ID，清理不得扩展到其他测试或真实记录。</summary>
    private async Task Add(string table,long id,Dictionary<string,object?> values)
    {
        values["id"]=id;
        var definition=Registry.Platform.ConfigTables.Single(row=>row.TableName==table);
        foreach(var column in Registry.Platform.ConfigColumns.Where(row=>row.ConfigTableId==definition.Id&&row.IsDbNotNull==1&&!values.ContainsKey(row.ColumnName)))
            values[column.ColumnName]=column.LogicalType switch {"int32" or "int64" or "decimal"=>0,"bool"=>false,"datetime" or "date"=>DateTime.UtcNow,"bytes"=>Array.Empty<byte>(),_=>""};
        rows.Add((table,id));await Store.InsertAsync(table,values);
    }
    /// <summary>逆序仅删除fixture自己创建的记录，查询值全部参数化。</summary>
    public async ValueTask DisposeAsync()
    {
        if(Session is null)return;
        try
        {
            // 当前项目ID由fixture独占，补充跟踪被afterAdd自动创建的从属记录。
            foreach(var table in new[]{"config_table","config_page","config_column","config_page_column"})
                foreach(var row in await Store.ReadAsync(table,new Dictionary<string,object?>{{"yz_project_id",ProjectId}}))
                    if(!rows.Contains((table,SqlRowStore.Id(row))))rows.Add((table,SqlRowStore.Id(row)));
            await using var connection=Session.Provider.CreateConnection(Session.Options);await connection.OpenAsync();
            foreach(var(table,id)in rows.AsEnumerable().Reverse())
            {
                if(table is not ("yz_project" or "config_table" or "config_column" or "config_page" or "config_page_column"))throw new InvalidOperationException("清理表不在白名单。");
                await using var command=connection.CreateCommand();command.CommandText="DELETE FROM "+Session.Provider.QuoteIdentifier(table)+" WHERE id=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=id;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();
            }
        }
        finally{Session.Dispose();}
    }
}
