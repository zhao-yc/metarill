using System.Globalization;
using System.Text.Json;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>真实MySQL验证配置归属不一致时连同前面的写入一起回滚，不修改内置或用户项目。</summary>
public sealed class ConfigurationOwnershipIntegrationTests
{
    [MetadataIntegrationFact]
    public async Task Page_table_change_with_existing_fields_rolls_back_all_changes()
    {
        await using var f=await OwnershipFixture.CreateAsync();var before=await f.Row("config_page",f.Page);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Mutate("config_page",f.Page,new(){["config_table_id"]=f.OtherTable,["name"]="不能保留的改名"}));
        Assert.Equal(Snapshot(before),Snapshot(await f.Row("config_page",f.Page)));
        var fields=await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",f.Page}});
        Assert.Single(fields);Assert.Equal(f.Column,SqlRowStore.Id(fields[0],"config_column_id"));
    }
    [MetadataIntegrationFact]
    public async Task Page_and_custom_page_backend_change_cannot_leave_invalid_menu_references()
    {
        await using var f=await OwnershipFixture.CreateAsync();
        foreach(var(table,id)in new[]{("config_page",f.Page),("config_custom_page",f.CustomPage)})
        {
            var before=await f.Row(table,id);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Mutate(table,id,new(){["yz_admin_type_id"]=f.OtherAdmin,["name"]="不得部分提交"}));
            Assert.Equal(Snapshot(before),Snapshot(await f.Row(table,id)));
        }
        Assert.Equal(f.Admin,SqlRowStore.Id(await f.Row("config_menu",f.PageMenu),"yz_admin_type_id"));
        Assert.Equal(f.Admin,SqlRowStore.Id(await f.Row("config_menu",f.CustomMenu),"yz_admin_type_id"));
    }
    [MetadataIntegrationFact]
    public async Task Referenced_column_cannot_leave_its_page_or_index_table()
    {
        await using var f=await OwnershipFixture.CreateAsync();
        // 第一个字段只被页面引用；第二个字段只被索引引用，分别覆盖两条独立约束。
        foreach(var id in new[]{f.Column,f.IndexedColumn})
        {
            var before=await f.Row("config_column",id);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Mutate("config_column",id,new(){["config_table_id"]=f.OtherTable,["name"]="不得部分提交"}));
            Assert.Equal(Snapshot(before),Snapshot(await f.Row("config_column",id)));
        }
        Assert.Equal(f.Index,SqlRowStore.Id(await f.Row("config_index_column",f.IndexField),"config_index_id"));
    }
    [MetadataIntegrationFact]
    public async Task Backend_target_must_belong_to_current_project_even_without_a_menu()
    {
        await using var f=await OwnershipFixture.CreateAsync();
        foreach(var(table,id)in new[]{("config_page",f.Page),("config_custom_page",f.CustomPage),("config_table",f.Table)})
        {
            var before=await f.Row(table,id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Mutate(table,id,new(){["yz_admin_type_id"]=f.ForeignAdmin,["name"]="跨项目失败"}));
            Assert.Equal(Snapshot(before),Snapshot(await f.Row(table,id)));
        }
    }
    [MetadataIntegrationFact]
    public async Task Existing_cross_project_child_references_are_rejected_instead_of_filtered_away()
    {
        await using var f=await OwnershipFixture.CreateAsync();
        var field=(await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",f.Page}})).Single();
        var fieldId=SqlRowStore.Id(field);
        await f.Store.UpdateAsync("config_page_column",fieldId,new Dictionary<string,object?>{{"yz_project_id",f.ForeignProject}});
        var before=await f.Row("config_page",f.Page);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Mutate("config_page",f.Page,new(){["name"]="不能隐藏跨项目子项"}));
        Assert.Equal(Snapshot(before),Snapshot(await f.Row("config_page",f.Page)));
        await f.Store.UpdateAsync("config_page_column",fieldId,new Dictionary<string,object?>{{"yz_project_id",f.Project}});
        await f.Store.UpdateAsync("config_index_column",f.IndexField,new Dictionary<string,object?>{{"yz_project_id",f.ForeignProject}});
        var column=await f.Row("config_column",f.IndexedColumn);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Mutate("config_column",f.IndexedColumn,new(){["name"]="不能隐藏跨项目索引子项"}));
        Assert.Equal(Snapshot(column),Snapshot(await f.Row("config_column",f.IndexedColumn)));
    }
    [MetadataIntegrationFact]
    public async Task Same_table_same_backend_harmless_edits_commit_normally()
    {
        await using var f=await OwnershipFixture.CreateAsync();
        await f.Mutate("config_page",f.Page,new(){["config_table_id"]=f.Table,["yz_admin_type_id"]=f.Admin,["name"]="同表页面改名"});
        await f.Mutate("config_column",f.Column,new(){["config_table_id"]=f.Table,["name"]="同表字段改名"});
        await f.Mutate("config_custom_page",f.CustomPage,new(){["yz_admin_type_id"]=f.Admin,["name"]="同后台自定义改名"});
        Assert.Equal("同表页面改名",SqlRowStore.Text(await f.Row("config_page",f.Page),"name"));
        Assert.Equal("同表字段改名",SqlRowStore.Text(await f.Row("config_column",f.Column),"name"));
        Assert.Equal("同后台自定义改名",SqlRowStore.Text(await f.Row("config_custom_page",f.CustomPage),"name"));
        foreach(var field in await f.Store.ReadAsync("config_page_column",new Dictionary<string,object?>{{"config_page_id",f.Page}}))
            Assert.Equal(f.Table,SqlRowStore.Id(await f.Row("config_column",SqlRowStore.Id(field,"config_column_id")),"config_table_id"));
    }
    /// <summary>对比真实数据库完整原行，确保名称和归属等多个字段没有部分提交。</summary>
    private static string Snapshot(Dictionary<string,object?> row)=>JsonSerializer.Serialize(row.OrderBy(pair=>pair.Key,StringComparer.Ordinal),JsonDefaults.Options);
}

/// <summary>只在metarill_test内建立独占项目及唯一ID元数据；不建业务表，不重置种子。</summary>
internal sealed class OwnershipFixture:IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids=new(859);
    private readonly List<(string Table,long Id)> rows=[];
    private static readonly string[] OwnedTables=["config_menu","config_index_column","config_index","config_page_column","config_page","config_column","config_table","config_custom_page","yz_admin_type"];
    public DatabaseSession Session {get;private set;}=null!;
    public MetadataRegistry Registry {get;private set;}=null!;
    public SqlRowStore Store {get;private set;}=null!;
    public long Project {get;}=Ids.NextId();public long ForeignProject {get;}=Ids.NextId();
    public long Admin {get;}=Ids.NextId();public long OtherAdmin {get;}=Ids.NextId();public long ForeignAdmin {get;}=Ids.NextId();
    public long Table {get;}=Ids.NextId();public long OtherTable {get;}=Ids.NextId();
    public long Column {get;}=Ids.NextId();public long IndexedColumn {get;}=Ids.NextId();public long OtherColumn {get;}=Ids.NextId();
    public long Page {get;}=Ids.NextId();public long CustomPage {get;}=Ids.NextId();
    public long PageMenu {get;}=Ids.NextId();public long CustomMenu {get;}=Ids.NextId();
    public long Index {get;}=Ids.NextId();public long IndexField {get;}=Ids.NextId();
    /// <summary>只读取显式隔离配置，凭据不进入测试输出或源码。</summary>
    public static async Task<OwnershipFixture> CreateAsync()
    {
        var f=new OwnershipFixture();
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            var options=settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            if(options.ExpectedDatabase!="metarill_test"||options.Provider!="mysql")throw new InvalidOperationException("归属测试只允许metarill_test隔离MySQL库。");
            var metadata=Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
            f.Registry=new(metadata,Path.Combine(Path.GetDirectoryName(metadata)!,"demo.json"));f.Session=new(options);f.Store=new(f.Session,f.Registry);
            await f.Session.InTransactionAsync(async()=>
            {
                await f.Add("yz_project",f.Project,new(){["name"]="归属独立测试"});await f.Add("yz_project",f.ForeignProject,new(){["name"]="归属对照测试"});
                foreach(var id in new[]{f.Admin,f.OtherAdmin})await f.Add("yz_admin_type",id,new(){["name"]="测试后台",["yz_project_id"]=f.Project});
                await f.Add("yz_admin_type",f.ForeignAdmin,new(){["name"]="他项目后台",["yz_project_id"]=f.ForeignProject});
                foreach(var id in new[]{f.Table,f.OtherTable})await f.Add("config_table",id,new(){["name"]="归属测试表",["table_name"]="mt_ownership_"+id,["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin});
                await f.Add("config_column",f.Column,f.ColumnValues("title",f.Table));await f.Add("config_column",f.IndexedColumn,f.ColumnValues("indexed_value",f.Table));await f.Add("config_column",f.OtherColumn,f.ColumnValues("other_value",f.OtherTable));
                await f.Add("config_page",f.Page,new(){["name"]="归属测试页",["config_table_id"]=f.Table,["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin});
                await f.Add("config_page_column",Ids.NextId(),new(){["name"]="原表页面字段",["config_page_id"]=f.Page,["config_column_id"]=f.Column,["yz_project_id"]=f.Project});
                await f.Add("config_custom_page",f.CustomPage,new(){["name"]="归属测试自定义页",["page_path"]="pages/custom/ownership_test",["yz_project_id"]=f.Project,["yz_admin_type_id"]=f.Admin});
                await f.Add("config_menu",f.PageMenu,new(){["name"]="页面菜单",["kind"]="GENERATED_PAGE",["config_page_id"]=f.Page,["yz_admin_type_id"]=f.Admin,["yz_project_id"]=f.Project});
                await f.Add("config_menu",f.CustomMenu,new(){["name"]="自定义菜单",["kind"]="CUSTOM_PAGE",["config_custom_page_id"]=f.CustomPage,["yz_admin_type_id"]=f.Admin,["yz_project_id"]=f.Project});
                await f.Add("config_index",f.Index,new(){["name"]="归属测试索引",["index_name"]="idx_ownership_"+f.Index,["config_table_id"]=f.Table,["yz_project_id"]=f.Project});
                await f.Add("config_index_column",f.IndexField,new(){["name"]="索引字段",["config_index_id"]=f.Index,["config_column_id"]=f.IndexedColumn,["column_name"]="indexed_value",["yz_project_id"]=f.Project});
                return true;
            });return f;
        }
        catch {await f.DisposeAsync();throw;}
    }
    /// <summary>每次写入都和正式生成服务一样在同一DatabaseSession事务内调用观察者。</summary>
    public Task<bool> Mutate(string table,long id,Dictionary<string,object?> patch)=>Session.InTransactionAsync(async()=>
    {
        await Store.UpdateAsync(table,id,patch);
        await new ConfigurationMutationObserver(Store,Registry,Ids).BeforeCommitAsync(new RecordMutationContext("ownership-test",table,id,Project.ToString(CultureInfo.InvariantCulture),patch,false,Session.CurrentTransaction!));
        return true;
    });
    /// <summary>按唯一ID读取断言数据，不扩大查询到其他测试记录。</summary>
    public async Task<Dictionary<string,object?>> Row(string table,long id)=>(await Store.ReadAsync(table,new Dictionary<string,object?>{{"id",id}})).Single();
    private Dictionary<string,object?> ColumnValues(string name,long table)=>new(){["name"]=name,["column_name"]=name,["logical_type"]="string",["column_type"]="varchar",["column_length"]="255",["config_table_id"]=table,["yz_project_id"]=Project};
    /// <summary>按发布物理元数据补测试基础字段，只跟踪本fixture创建的主键。</summary>
    private async Task Add(string table,long id,Dictionary<string,object?> values)
    {
        values["id"]=id;var definition=Registry.Platform.ConfigTables.Single(item=>item.TableName==table);
        foreach(var column in Registry.Platform.ConfigColumns.Where(item=>item.ConfigTableId==definition.Id&&item.IsDbNotNull==1&&!values.ContainsKey(item.ColumnName)))
            values[column.ColumnName]=column.DefaultValue.Length>0?column.DefaultValue:column.LogicalType switch{"int32" or "int64" or "decimal"=>0,"bool"=>false,"date" or "datetime"=>DateTime.UtcNow,_=>""};
        rows.Add((table,id));await Store.InsertAsync(table,values);
    }
    /// <summary>清理仅覆盖本fixture两项目、白名单表及跟踪ID，不触碰用户或内置元数据。</summary>
    public async ValueTask DisposeAsync()
    {
        if(Session is null)return;
        try
        {
            foreach(var table in OwnedTables)
                foreach(var project in new[]{Project,ForeignProject})
                    foreach(var row in await Store.ReadAsync(table,new Dictionary<string,object?>{{"yz_project_id",project}}))
                        if(!rows.Contains((table,SqlRowStore.Id(row))))rows.Add((table,SqlRowStore.Id(row)));
            await using var connection=Session.Provider.CreateConnection(Session.Options);await connection.OpenAsync();
            foreach(var(table,id)in rows.AsEnumerable().Reverse())
            {
                if(table!="yz_project"&&!OwnedTables.Contains(table))throw new InvalidOperationException("测试清理超出白名单。");
                await using var command=connection.CreateCommand();command.CommandText="DELETE FROM "+Session.Provider.QuoteIdentifier(table)+" WHERE id=@id";
                var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=id;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();
            }
        }
        finally{Session.Dispose();}
    }
}
