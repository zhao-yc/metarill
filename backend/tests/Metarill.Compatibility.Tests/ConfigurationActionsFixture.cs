using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;

namespace Metarill.Compatibility.Tests;

/// <summary>只写随机ID独占的隔离元数据项目，不创建实际业务表或修改平台种子。</summary>
internal sealed class ConfigurationActionsFixture:IAsyncDisposable
{
    internal static readonly SnowflakeIdGenerator Ids=new(857);
    private readonly List<(string Table,long Id)> owned=[];
    internal DatabaseOptions Options {get;private set;}=null!;
    internal DatabaseSession Session {get;private set;}=null!;
    internal SqlRowStore Store {get;private set;}=null!;
    internal MetadataRegistry Registry {get;private set;}=null!;
    internal long Project {get;private set;} internal long ForeignProject {get;private set;}
    internal long Admin {get;private set;} internal long ForeignAdmin {get;private set;}
    internal long Table {get;private set;} internal long OtherTable {get;private set;} internal long ForeignTable {get;private set;}
    internal long Page {get;private set;} internal long ChildPage {get;private set;} internal long ParentPage {get;private set;}
    internal long Title {get;private set;} internal long Sort {get;private set;} internal long Derived {get;private set;}
    internal long[] Fields {get;private set;}=[];
    internal ConfigurationActionsBusiness Business(DatabaseSession? session=null,bool administrator=true,string? project=null,IIdGenerator? generator=null)
    {
        var store=new SqlRowStore(session??Session,Registry);var actor=new RuntimeActor(8026,"配置动作测试",administrator,project??Project.ToString(),Admin.ToString());
        var context=new DefaultHttpContext();context.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(actor,new HashSet<string>(),1,null);
        var auth=new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},Registry);
        var currentIds=generator??Ids;return new(store,Registry,new MetadataRepository(store,Registry),auth,currentIds,new ConfigurationMutationObserver(store,Registry,currentIds));
    }
    internal CompatibilityButtonBusiness Gateway()
    {
        var actor=new RuntimeActor(8026,"配置动作测试",true,Project.ToString(),Admin.ToString());var context=new DefaultHttpContext();context.Items[typeof(AuthenticatedIdentity)]=new AuthenticatedIdentity(actor,new HashSet<string>(),1,null);
        return new(Store,new HttpRuntimeAuthorization(new HttpContextAccessor{HttpContext=context},Registry),Registry,Business());
    }
    /// <summary>强制使用明确的metarill_test测试库，凭据不打印或落入提交。</summary>
    internal static async Task<ConfigurationActionsFixture> CreateAsync()
    {
        var f=new ConfigurationActionsFixture();
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            f.Options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            if(f.Options.ExpectedDatabase!="metarill_test"||f.Options.Provider!="mysql")throw new InvalidOperationException("配置动作测试只允许隔离MySQL的metarill_test库。");
            f.Registry=CompatibilityButtonTests.Registry();f.Session=new(f.Options);f.Store=new(f.Session,f.Registry);
            f.Project=Ids.NextId();f.ForeignProject=Ids.NextId();f.Admin=Ids.NextId();f.ForeignAdmin=Ids.NextId();
            f.Table=Ids.NextId();f.OtherTable=Ids.NextId();f.ForeignTable=Ids.NextId();f.Page=Ids.NextId();f.ChildPage=Ids.NextId();f.ParentPage=Ids.NextId();
            f.Title=Ids.NextId();f.Sort=Ids.NextId();f.Derived=Ids.NextId();f.Fields=[Ids.NextId(),Ids.NextId(),Ids.NextId()];
            await f.Session.InTransactionAsync(async()=>
            {
                await f.Add("yz_project",f.Project,new(){["name"]="配置动作随机测试"});await f.Add("yz_project",f.ForeignProject,new(){["name"]="跨项目拒绝测试"});
                await f.Add("yz_admin_type",f.Admin,new(){["name"]="测试后台",["yz_project_id"]=f.Project});await f.Add("yz_admin_type",f.ForeignAdmin,new(){["name"]="另一项目后台",["yz_project_id"]=f.ForeignProject});
                foreach(var (id,project,admin) in new[]{(f.Table,f.Project,f.Admin),(f.OtherTable,f.Project,f.Admin),(f.ForeignTable,f.ForeignProject,f.ForeignAdmin)})
                    await f.Add("config_table",id,new(){["name"]="测试配置表",["table_name"]="mt_actions_"+id,["yz_project_id"]=project,["yz_admin_type_id"]=admin});
                foreach(var (id,name,type,join) in new[]{(f.Title,"title","string",0),(f.Sort,"sort_no","int32",0),(f.Derived,"derived","string",1)})
                    await f.Add("config_column",id,new(){["name"]=name,["column_name"]=name,["logical_type"]=type,["column_type"]=type,["column_length"]=type=="string"?"255":"",["config_table_id"]=f.Table,["yz_project_id"]=f.Project,["is_join_column"]=join});
                foreach(var (id,table,name) in new[]{(f.Page,f.Table,"测试页面"),(f.ChildPage,f.OtherTable,"子页"),(f.ParentPage,f.OtherTable,"上级页面")})
                    await f.Add("config_page",id,new(){["name"]=name,["config_table_id"]=table,["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin,["detail_layout"]="{\"version\":1,\"mode\":\"tabs\",\"groups\":[{\"key\":\"base\",\"name\":\"基础\"}]}"});
                foreach(var (id,column) in f.Fields.Zip(new[]{f.Title,f.Sort,f.Derived}))
                    await f.Add("config_page_column",id,new(){["name"]="字段",["config_column_id"]=column,["config_page_id"]=f.Page,["config_page_name"]="测试页面",["yz_project_id"]=f.Project,["is_in_edit"]=1,["is_in_grid"]=1,["detail_group_key"]="base",["default_value"]="原配置"});
                await f.Add("config_button",Ids.NextId(),new(){["name"]="被复制按钮",["config_page_id"]=f.Page,["config_table_id"]=f.Table,["yz_project_id"]=f.Project,["action_type"]=2,["sql"]="SELECT 1"});
                foreach(var (parent,child) in new[]{(f.Page,f.ChildPage),(f.Page,f.Page),(f.ParentPage,f.Page)})
                    await f.Add("config_child",Ids.NextId(),new(){["parent_config_page_id"]=parent,["child_config_page_id"]=child,["yz_project_id"]=f.Project});
                var role=Ids.NextId();await f.Add("yz_role",role,new(){["name"]="原角色",["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin});
                await f.Add("yz_role_permission",Ids.NextId(),new(){["yz_role_id"]=role,["config_page_id"]=f.Page,["yz_project_id"]=f.Project,["permission_values"]="is_all"});
                return true;
            });return f;
        }
        catch{await f.DisposeAsync();throw;}
    }
    /// <summary>跟踪fixture记录；其他新记录仅在唯一项目范围内补充清理。</summary>
    internal async Task<long> Add(string table,long id,Dictionary<string,object?> values)
    {
        values["id"]=id;values["insert_time"]=new DateTime(2010,1,1);values["insert_manager_id"]=123L;
        var definition=Registry.Platform.ConfigTables.Single(t=>t.TableName==table);
        foreach(var column in Registry.Platform.ConfigColumns.Where(c=>c.ConfigTableId==definition.Id&&c.IsDbNotNull==1&&!values.ContainsKey(c.ColumnName)))values[column.ColumnName]=column.LogicalType is "int32" or "int64" or "decimal"?0:"";
        owned.Add((table,id));await Store.InsertAsync(table,values);return id;
    }
    internal Task<List<Dictionary<string,object?>>> Rows(string table)=>Store.ReadAsync(table,new Dictionary<string,object?>{{"yz_project_id",Project}});
    public async ValueTask DisposeAsync()
    {
        if(Session is null)return;
        try
        {
            var tables=new[]{"config_menu","config_child","config_button","config_page_column","config_page","config_column","config_table","yz_role_permission","yz_role","yz_admin_type"};
            foreach(var project in new[]{Project,ForeignProject})foreach(var table in tables)
                foreach(var row in await Store.ReadAsync(table,new Dictionary<string,object?>{{"yz_project_id",project}}))if(!owned.Contains((table,SqlRowStore.Id(row))))owned.Add((table,SqlRowStore.Id(row)));
            await using var connection=Session.Provider.CreateConnection(Session.Options);await connection.OpenAsync();
            foreach(var (table,id) in owned.AsEnumerable().Reverse())
            {
                if(table!="yz_project"&&!tables.Contains(table))throw new InvalidOperationException("清理表不在测试白名单。");
                await using var command=connection.CreateCommand();command.CommandText="DELETE FROM "+Session.Provider.QuoteIdentifier(table)+" WHERE id=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=id;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();
            }
        }finally{Session.Dispose();}
    }
}
