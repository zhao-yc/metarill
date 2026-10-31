using System.Data.Common;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

namespace Metarill.Api.Tests;

/// <summary>仅对两个已钉住的Server-200测试库执行真实HTTP；配置样本完全归属于新建随机项目，不创建业务物理表。</summary>
internal sealed class MaterializedDerivedApiFixture:IAsyncDisposable
{
    internal const string PageRoute="/admin/auto/config_page/6191175925956608";
    internal const string FieldRoute="/admin/auto/config_page_column/6218036784398336";
    internal const string ColumnRoute="/admin/auto/config_column/6217715532627968";
    internal const string TableRoute="/admin/auto/config_table/6191083005411328";
    private static readonly SnowflakeIdGenerator Ids=new(828);
    private static readonly string[] Tables=["config_index_column","config_index","config_page_column","config_page","config_column","config_table","yz_admin_type","yz_project"];
    private readonly HashSet<(string Table,long Id,long Project)> owned=[];
    private readonly HashSet<long> roots=[];
    private readonly Dictionary<long,(long Table,long Column,long Child,string Name)> registeredIndexes=[];
    private readonly Dictionary<(string Table,long Id),Dictionary<string,object?>> registeredIndexRows=[];
    private readonly HashSet<string> expectedNames=new(StringComparer.Ordinal);
    private readonly string? priorSettings=Environment.GetEnvironmentVariable("METARILL_SETTINGS");
    private string tokenHash="";private long sessionId;private long creatorId;private bool cleaned;
    internal string Evidence{get;}=Path.Combine(Repository(),".local/derived-mysql/runs",Guid.NewGuid().ToString("N"));
    internal string Marker{get;}="mt_der_"+Guid.NewGuid().ToString("N")[..12];
    internal long Project{get;}=Ids.NextId();internal long ForeignProject{get;}=Ids.NextId();
    internal long Admin{get;}=Ids.NextId();internal long ForeignAdmin{get;}=Ids.NextId();
    internal DatabaseOptions Options{get;private set;}=null!;
    internal WebApplicationFactory<Program> Factory{get;private set;}=null!;
    internal HttpClient Client{get;private set;}=null!;
    internal SqlRowStore Store{get;private set;}=null!;
    internal DatabaseSession Session{get;private set;}=null!;
    internal MetadataRegistry Registry{get;private set;}=null!;
    internal List<string> OrmCommands{get;}=[];
    internal JsonElement LastPageReceipt{get;private set;}

    /// <summary>必须先检查真实编译DTO和实例指纹，再登录；宿主不带initialize或修复种子参数。</summary>
    internal static async Task<MaterializedDerivedApiFixture> CreateAsync(Action<IServiceCollection>? configure=null)
    {
        var f=new MaterializedDerivedApiFixture();Directory.CreateDirectory(f.Evidence);
        try
        {
            Assert.Null(typeof(Metarill.Platform.Generated.DTOs.ConfigPagePage6191175925956608UpdateRequest).GetProperty("ConfigTableTableName"));
            Assert.Null(typeof(Metarill.Platform.Generated.DTOs.ConfigPageColumnPage6218036784398336UpdateRequest).GetProperty("ConfigColumnColumnName"));
            Assert.Null(typeof(Metarill.Platform.Generated.DTOs.ConfigPageColumnPage6218036784398336UpdateRequest).GetProperty("ConfigColumnSortNo"));
            var settings=Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")??throw new InvalidOperationException("未提供私有测试配置。");
            using var config=JsonDocument.Parse(await File.ReadAllTextAsync(settings));
            foreach(var item in new[]{("Database","metarill_test"),("BusinessDatabase","metarill_business_test")})
            {
                var options=config.RootElement.GetProperty("Metarill").GetProperty(item.Item1).Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
                await PinAsync(options,item.Item2);if(item.Item1=="Database")f.Options=options;
            }
            Environment.SetEnvironmentVariable("METARILL_SETTINGS",settings);
            f.Factory=new WebApplicationFactory<Program>();if(configure is not null)f.Factory=f.Factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(configure));
            f.Client=f.Factory.CreateClient();f.Store=f.Factory.Services.GetRequiredService<SqlRowStore>();f.Session=f.Factory.Services.GetRequiredService<TargetDatabaseSessions>().Platform;f.Registry=f.Factory.Services.GetRequiredService<MetadataRegistry>();
            var bootstrap=config.RootElement.GetProperty("Metarill").GetProperty("Bootstrap");
            using var loginResponse=await f.Client.PostAsync("/api/admin/sys/yz_manager/login",new FormUrlEncodedContent(new Dictionary<string,string>{{"username",bootstrap.GetProperty("Username").GetString()!},{"password",bootstrap.GetProperty("Password").GetString()!}}));
            var login=await Body(loginResponse);Data(login);var token=login.GetProperty("map").GetProperty("loginStateId").GetString()!;f.tokenHash=PasswordHasher.Digest(token);
            f.Client.DefaultRequestHeaders.Add("loginStateId",token);
            var ownSession=(await f.Store.ReadAsync("sys_session",new Dictionary<string,object?>{{"token_hash",f.tokenHash}})).Single();
            f.sessionId=SqlRowStore.Id(ownSession);f.creatorId=SqlRowStore.Id(ownSession,"account_id");
            await f.AddSeed("yz_project",f.Project,f.Project,new(){["name"]=f.Marker});
            await f.AddSeed("yz_project",f.ForeignProject,f.ForeignProject,new(){["name"]=f.Marker+"_foreign"});
            await f.AddSeed("yz_admin_type",f.Admin,f.Project,new(){["name"]="派生验收后台",["yz_project_id"]=f.Project});
            await f.AddSeed("yz_admin_type",f.ForeignAdmin,f.ForeignProject,new(){["name"]="另一随机后台",["yz_project_id"]=f.ForeignProject});
            f.SetProject(f.Project);
            f.Session.Orm.Aop.CommandBefore+=(_,args)=>{lock(f.OrmCommands)f.OrmCommands.Add(args.Command.CommandText);};
            await f.FlushLedger("prepared");return f;
        }
        catch{await f.DisposeAsync();throw;}
    }

    /// <summary>身份指纹每次开测都复核，禁止仅凭库名含test就放行。</summary>
    private static async Task PinAsync(DatabaseOptions options,string database)
    {
        var builder=new MySqlConnectionStringBuilder(options.ConnectionString);
        if(options.Provider!="mysql"||options.ExpectedDatabase!=database||builder.Database!=database||builder.Server!="deve-server.yinzon.com"||builder.Port!=3306)throw new InvalidOperationException("数据库连接不匹配已授权Server-200测试库。");
        await using var connection=new MySqlConnection(options.ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";
        await using var reader=await command.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader.GetString(0)!=database||reader.GetString(1)!="3107e214a8a3"||reader.GetString(2)!="6d3d024a-9d73-11f1-9939-ce69308a21dc")throw new InvalidOperationException("Server-200实例身份不匹配。");
    }
    /// <summary>测试种子先落自有ID意图，再插入配置元数据；不安装结构、不创建账号或角色。</summary>
    private async Task AddSeed(string table,long id,long project,Dictionary<string,object?> values)
    {
        values["id"]=id;
        var definition=Registry.Platform.ConfigTables.Single(row=>row.TableName==table);
        foreach(var column in Registry.Platform.ConfigColumns.Where(column=>column.ConfigTableId==definition.Id&&column.IsDbNotNull==1&&!values.ContainsKey(column.ColumnName)))values[column.ColumnName]=column.LogicalType switch{"int32" or "int64" or "decimal"=>0,"bool"=>false,"datetime" or "date"=>DateTime.UtcNow,_=>""};
        Assert.Empty(await Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"id",id}}));
        owned.Add((table,id,project));await FlushLedger("seed-intent");await Store.InsertAsync(table,values);
    }
    internal void SetProject(long project){Client.DefaultRequestHeaders.Remove("sysParams");Client.DefaultRequestHeaders.Add("sysParams",Uri.EscapeDataString(JsonSerializer.Serialize(new{yzProjectId=project.ToString(CultureInfo.InvariantCulture)})));}
    internal async Task<JsonElement> Post(string route,object body){using var response=await Client.PostAsJsonAsync("/api"+route,body);return await Body(response);}
    internal async Task<JsonElement> Get(string route){using var response=await Client.GetAsync("/api"+route);return await Body(response);}
    internal async Task Delete(string route,long id,string? parent=null){using var response=await Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,"/api"+route+(parent is null?"":"?parentId="+parent)){Content=JsonContent.Create(new{id=id.ToString(CultureInfo.InvariantCulture)})});Data(await Body(response));}
    internal static JsonElement Data(JsonElement response){Assert.True(response.GetProperty("code").GetInt32()==200,response.ToString());return response.GetProperty("data");}
    internal static async Task<JsonElement> Body(HttpResponseMessage response){var text=await response.Content.ReadAsStringAsync();Assert.True(text.TrimStart().StartsWith('{'),"HTTP返回非JSON：状态="+(int)response.StatusCode+"，类型="+response.Content.Headers.ContentType?.MediaType);return JsonSerializer.Deserialize<JsonElement>(text);}
    internal static long Id(JsonElement row)=>long.Parse(row.GetProperty("id").GetString()!,CultureInfo.InvariantCulture);
    internal async Task<Dictionary<string,object?>> Row(string table,long id,bool deleted=false)=>(await (deleted?Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"id",id}}):Store.ReadAsync(table,new Dictionary<string,object?>{{"id",id}}))).Single();

    /// <summary>正式开放的表新增会自动补页面和系统列；只接受本次随机命名及已知返回根ID。</summary>
    internal async Task<long> CreateTable(string suffix)
    {
        var name=Marker+"_"+suffix;expectedNames.Add(name);await FlushLedger("table-intent");
        var row=Data(await Post(TableRoute+"/add",new{name="派生测试表"+suffix,tableName=name,yzAdminTypeId=Admin.ToString(CultureInfo.InvariantCulture),isRongyu=1}));var id=Id(row);roots.Add(id);owned.Add(("config_table",id,Project));await Capture();return id;
    }
    internal async Task<long> CreatePage(long table,string name="测试页面",Func<Task>? afterResponse=null)
    {
        var row=Data(await Post(PageRoute+"/add",new{name,configTableId=table.ToString(CultureInfo.InvariantCulture),yzAdminTypeId=Admin.ToString(CultureInfo.InvariantCulture),mobileListPageStyleType=2,cols=2}));if(afterResponse is not null)await afterResponse();var id=Id(row);LastPageReceipt=row;var actual=await Row("config_page",id);Assert.Equal(Project.ToString(CultureInfo.InvariantCulture),SqlRowStore.Text(actual,"yz_project_id"));owned.Add(("config_page",id,Project));await Capture();return id;
    }
    internal async Task<long[]> AddColumns(long table,string content)
    {
        var data=Data(await Post("/admin/custom/index/save_batch_create_columns",new{config_table_id=table.ToString(CultureInfo.InvariantCulture),content}));await Capture();return data.GetProperty("columnIds").EnumerateArray().Select(value=>long.Parse(value.GetString()!,CultureInfo.InvariantCulture)).ToArray();
    }
    internal async Task<long> ForeignTable()
    {
        var id=Ids.NextId();var name=Marker+"_foreign";expectedNames.Add(name);roots.Add(id);await AddSeed("config_table",id,ForeignProject,new(){["name"]="另一项目表",["table_name"]=name,["yz_project_id"]=ForeignProject,["yz_admin_type_id"]=ForeignAdmin,["is_rongyu"]=1});return id;
    }
    /// <summary>新自动记录必须能从已知根表逐边证明；不因项目随机就接受未知根或跨项目引用。</summary>
    internal async Task Capture()
    {
        foreach(var project in new[]{Project,ForeignProject})
        {
            var tables=await Rows("config_table",project);foreach(var row in tables){if(!roots.Contains(SqlRowStore.Id(row))||!expectedNames.Contains(SqlRowStore.Text(row,"table_name")))throw new InvalidOperationException("发现未登记根表，保留现场。");owned.Add(("config_table",SqlRowStore.Id(row),project));}
            var columns=await Rows("config_column",project);foreach(var row in columns){if(!tables.Any(table=>SqlRowStore.Id(table)==SqlRowStore.Id(row,"config_table_id")))throw new InvalidOperationException("字段来源不属于自有根表。");owned.Add(("config_column",SqlRowStore.Id(row),project));}
            // 索引只接受已显式登记的工作台回执，合法同表但未登记的第三方记录也必须保留现场。
            var indexes=await Rows("config_index",project);
            foreach(var row in indexes)RequireRegisteredIndexRow("config_index",row,project);
            var indexFields=await Rows("config_index_column",project);
            foreach(var row in indexFields)RequireRegisteredIndexRow("config_index_column",row,project);
            if(project==Project&&registeredIndexRows.Keys.Any(key=>!(key.Table=="config_index"?indexes:indexFields).Any(row=>SqlRowStore.Id(row)==key.Id)))throw new InvalidOperationException("已登记索引记录缺失，保留现场。");
            var pages=await Rows("config_page",project);foreach(var row in pages){if(!tables.Any(table=>SqlRowStore.Id(table)==SqlRowStore.Id(row,"config_table_id")))throw new InvalidOperationException("页面来源不属于自有根表。");owned.Add(("config_page",SqlRowStore.Id(row),project));}
            foreach(var row in await Rows("config_page_column",project))
            {
                var page=pages.SingleOrDefault(page=>SqlRowStore.Id(page)==SqlRowStore.Id(row,"config_page_id"));var column=columns.SingleOrDefault(column=>SqlRowStore.Id(column)==SqlRowStore.Id(row,"config_column_id"));
                if(page is null||column is null||SqlRowStore.Id(page,"config_table_id")!=SqlRowStore.Id(column,"config_table_id")&&(row.GetValueOrDefault("delete_no") is null||!owned.Contains(("config_page_column",SqlRowStore.Id(row),project))))throw new InvalidOperationException("页面字段来源不属于同表自有图。");owned.Add(("config_page_column",SqlRowStore.Id(row),project));
            }
        }
        await FlushLedger("captured");
    }
    /// <summary>首笔回执只登记固定自有索引；后续同一ID的已知写入必须保持原完整记录除明确变更字段外字节等价。</summary>
    internal async Task RegisterIndexSave(JsonElement receipt,long table,long column,string name,int enabled)
    {
        if(!roots.Contains(table)||receipt.GetProperty("configTableId").GetString()!=table.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException("索引回执所属表不匹配，保留现场。");
        var indexes=receipt.GetProperty("indexes").EnumerateArray().ToArray();if(indexes.Length!=1)throw new InvalidOperationException("索引回执数量异常，保留现场。");
        var index=indexes[0];var children=index.GetProperty("columns").EnumerateArray().ToArray();
        if(children.Length!=1||index.GetProperty("indexName").GetString()!=name||index.GetProperty("indexType").GetString()!="NORMAL"||index.GetProperty("isEnabled").GetInt32()!=enabled)throw new InvalidOperationException("索引回执定义不匹配，保留现场。");
        var id=Id(index);var child=Id(children[0]);
        if(id<=0||child<=0||children[0].GetProperty("configColumnId").GetString()!=column.ToString(CultureInfo.InvariantCulture)||children[0].GetProperty("columnName").GetString()!="indexed_value"||children[0].GetProperty("sortNo").GetInt32()!=10||children[0].GetProperty("sortDirection").GetString()!="ASC")throw new InvalidOperationException("索引字段回执不匹配，保留现场。");
        var proof=(Table:table,Column:column,Child:child,Name:name);
        if(registeredIndexes.TryGetValue(id,out var previous)&&previous!=proof||registeredIndexes.Any(pair=>pair.Key!=id&&(pair.Value.Table==table||pair.Value.Child==child)))throw new InvalidOperationException("索引回执身份发生变化，保留现场。");
        await SaveEvidence("index-receipt-"+id.ToString(CultureInfo.InvariantCulture),new{id,child,table,column,name,enabled,creator=creatorId,project=Project,receipt});
        var indexRow=await ExactIndexRow("config_index",id);var childRow=await ExactIndexRow("config_index_column",child);
        RequireIndexOwner(indexRow);RequireIndexOwner(childRow);
        if(SqlRowStore.Id(indexRow,"config_table_id")!=table||SqlRowStore.Text(indexRow,"index_name")!=name||SqlRowStore.Text(indexRow,"index_type")!="NORMAL"||Convert.ToInt32(indexRow["is_enabled"],CultureInfo.InvariantCulture)!=enabled||indexRow.GetValueOrDefault("delete_no") is not null
            ||SqlRowStore.Id(childRow,"config_index_id")!=id||SqlRowStore.Id(childRow,"config_column_id")!=column||SqlRowStore.Text(childRow,"column_name")!="indexed_value"||SqlRowStore.Text(childRow,"config_index_index_name")!=name||Convert.ToInt32(childRow["sort_no"],CultureInfo.InvariantCulture)!=10||SqlRowStore.Text(childRow,"sort_direction")!="ASC"||childRow.GetValueOrDefault("delete_no") is not null)throw new InvalidOperationException("索引实际定义与已知回执不一致，保留现场。");
        // 禁用只允许启用值和服务端更新时间变化；不能把其它完整字段修改当作测试自有写入。
        RequireKnownIndexChange("config_index",id,indexRow,["is_enabled","update_time"]);
        RequireKnownIndexChange("config_index_column",child,childRow,["update_time"]);
        registeredIndexes[id]=proof;RegisterIndexRow("config_index",id,indexRow);RegisterIndexRow("config_index_column",child,childRow);
        await FlushLedger("index-registered");
    }

    /// <summary>只接受已知工作台移除或整表删除的墓碑变化；调用前必须已通过Capture完整核验。</summary>
    internal async Task RegisterIndexDelete(long table,bool workbench)
    {
        foreach(var pair in registeredIndexes.Where(pair=>pair.Value.Table==table))
        {
            foreach(var item in new[]{("config_index",pair.Key),("config_index_column",pair.Value.Child)})
            {
                var row=await ExactIndexRow(item.Item1,item.Item2);RequireIndexOwner(row);
                if(row.GetValueOrDefault("delete_no") is null)throw new InvalidOperationException("自有索引删除未产生预期墓碑，保留现场。");
                RequireKnownIndexChange(item.Item1,item.Item2,row,workbench?["delete_no","update_time"]:["delete_no"]);
                RegisterIndexRow(item.Item1,item.Item2,row);
            }
        }
        await FlushLedger("index-delete-registered");
    }

    /// <summary>读取精确索引ID且只输出固定失败说明，不由断言格式化完整元数据行。</summary>
    private async Task<Dictionary<string,object?>> ExactIndexRow(string table,long id)
    {
        var rows=await Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"id",id}});
        if(rows.Count!=1)throw new InvalidOperationException("已登记索引记录数量不一致，保留现场。");return rows[0];
    }
    /// <summary>创建归属必须来自实际登录会话及固定随机项目，不能从回执或项目名猜测所有者。</summary>
    private void RequireIndexOwner(IReadOnlyDictionary<string,object?> row)
    {
        if(SqlRowStore.Id(row,"yz_project_id")!=Project||SqlRowStore.Id(row,"insert_manager_id")!=creatorId||SqlRowStore.Id(row,"update_manager_id")!=creatorId)throw new InvalidOperationException("索引创建或修改归属不匹配，保留现场。");
    }
    /// <summary>Capture不自动认领任何索引；完整字段漂移即停止后续修改与清理。</summary>
    private void RequireRegisteredIndexRow(string table,Dictionary<string,object?> row,long project)
    {
        var id=SqlRowStore.Id(row);
        if(project!=Project||!registeredIndexRows.TryGetValue((table,id),out var expected)||IndexFingerprint(expected)!=IndexFingerprint(row))throw new InvalidOperationException("发现未登记或已变化的索引记录，保留现场。");
        RequireIndexOwner(row);
        if(!owned.Contains((table,id,project)))throw new InvalidOperationException("索引尚未完成显式登记，保留现场。");
    }
    /// <summary>只有明确预期的字段可以在本次原HTTP写入后改变；其它字段维持完整基线。</summary>
    private void RequireKnownIndexChange(string table,long id,Dictionary<string,object?> row,string[] changed)
    {
        if(registeredIndexRows.TryGetValue((table,id),out var expected)&&IndexFingerprint(expected,changed)!=IndexFingerprint(row,changed))throw new InvalidOperationException("索引出现本次写入以外的变化，保留现场。");
    }
    /// <summary>只有完整验证后才将精确记录纳入清理账本，未知同表节点不会被自动接收。</summary>
    private void RegisterIndexRow(string table,long id,Dictionary<string,object?> row){registeredIndexRows[(table,id)]=new(row,StringComparer.Ordinal);owned.Add((table,id,Project));}
    /// <summary>按稳定键序比较完整字段，仅已知HTTP动作可以排除本次明确变更的键。</summary>
    private static string IndexFingerprint(IReadOnlyDictionary<string,object?> row,string[]? ignored=null)=>JsonSerializer.Serialize(row.Where(pair=>ignored is null||!ignored.Contains(pair.Key,StringComparer.Ordinal)).OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToDictionary(pair=>pair.Key,pair=>pair.Value));
    private Task<List<Dictionary<string,object?>>> Rows(string table,long project)=>Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"yz_project_id",project}});
    internal void AllowRename(string name)=>expectedNames.Add(name);
    internal async Task<string> Snapshot()
    {
        var result=new List<object>();foreach(var item in owned.Where(item=>item.Table!="yz_project").OrderBy(item=>item.Table,StringComparer.Ordinal).ThenBy(item=>item.Id))
        {var rows=await Store.ReadIncludingDeletedAsync(item.Table,new Dictionary<string,object?>{{"id",item.Id},{"yz_project_id",item.Project}});result.Add(new{item.Table,item.Id,values=rows.SingleOrDefault()?.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToDictionary(pair=>pair.Key,pair=>pair.Value)});}
        return JsonSerializer.Serialize(result);
    }
    internal async Task SaveEvidence(string name,object value)=>await Durable(Path.Combine(Evidence,name+".json"),JsonSerializer.Serialize(value));
    private async Task FlushLedger(string state)=>await SaveEvidence("ledger",new{state,marker=Marker,project=Project,foreignProject=ForeignProject,roots=roots.Order().ToArray(),owned=owned.OrderBy(item=>item.Table,StringComparer.Ordinal).ThenBy(item=>item.Id).Select(item=>new{table=item.Table,id=item.Id,project=item.Project}).ToArray(),expectedTableNames=expectedNames.Order(StringComparer.Ordinal).ToArray(),registeredIndexes=registeredIndexes.OrderBy(pair=>pair.Key).Select(pair=>new{id=pair.Key,table=pair.Value.Table,column=pair.Value.Column,child=pair.Value.Child,name=pair.Value.Name,creator=creatorId,project=Project}).ToArray(),sessionId});
    private static async Task Durable(string path,string value){await using var file=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read);await file.WriteAsync(Encoding.UTF8.GetBytes(value));await file.FlushAsync();file.Flush(true);}

    /// <summary>只有成功验收才启动精确清理；清理前重新核验全部ID及归属图，未知记录即停止并保留现场。</summary>
    internal async Task CompleteAsync()
    {
        await Capture();await SaveEvidence("final-rows",JsonSerializer.Deserialize<JsonElement>(await Snapshot()));
        await SaveEvidence("orm-sql",OrmCommands.ToArray());
        await PinAsync(Options,"metarill_test");
        foreach(var project in new[]{Project,ForeignProject})foreach(var table in Tables.Where(table=>table!="yz_project"))foreach(var row in await Rows(table,project))
            if(!owned.Contains((table,SqlRowStore.Id(row),project)))throw new InvalidOperationException("清理发现未知记录，保留现场。");
        await Session.InTransactionAsync(async()=>
        {
            foreach(var table in Tables)foreach(var item in owned.Where(item=>item.Table==table).OrderByDescending(item=>item.Id))
            {
                var rows=await Store.ReadIncludingDeletedAsync(table,new Dictionary<string,object?>{{"id",item.Id}});if(rows.Count!=1||table!="yz_project"&&SqlRowStore.Text(rows[0],"yz_project_id")!=item.Project.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException("精确清理归属不匹配。");
                await using var command=Session.CurrentTransaction!.Connection!.CreateCommand();command.Transaction=Session.CurrentTransaction;
                command.CommandText="DELETE FROM "+Session.Provider.QuoteIdentifier(table)+" WHERE id=@id"+(table=="yz_project"?"":" AND yz_project_id=@project");Bind(command,"id",item.Id);if(table!="yz_project")Bind(command,"project",item.Project);if(await command.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("精确清理记录数量不一致。");
            }
            var session=(await Store.ReadAsync("sys_session",new Dictionary<string,object?>{{"id",sessionId},{"token_hash",tokenHash}})).Single();
            if(SqlRowStore.Text(session,"kind")!="web")throw new InvalidOperationException("登录会话归属不匹配。");
            await using(var command=Session.CurrentTransaction!.Connection!.CreateCommand()){command.Transaction=Session.CurrentTransaction;command.CommandText="DELETE FROM sys_session WHERE id=@id AND token_hash=@hash";Bind(command,"id",sessionId);Bind(command,"hash",tokenHash);if(await command.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("会话清理失败。");}
            return true;
        });
        cleaned=true;await FlushLedger("cleaned");
    }
    private static void Bind(DbCommand command,string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);}
    /// <summary>失败保留自有数据及收据，不在异常退出时猜测范围清理。</summary>
    public async ValueTask DisposeAsync()
    {
        if(!cleaned&&Store is not null)
        {
            // 配置Observer可能在最后一次HTTP中补行；失败现场也先尝试证明完整自有图，不能漏收自动行。
            try{await Capture();}catch(InvalidOperationException error){await SaveEvidence("capture-rejected",new{error=error.Message});}
            await SaveEvidence("preserved-rows",JsonSerializer.Deserialize<JsonElement>(await Snapshot()));await FlushLedger("preserved");
        }
        Client?.Dispose();if(Factory is not null)await Factory.DisposeAsync();Environment.SetEnvironmentVariable("METARILL_SETTINGS",priorSettings);
    }
    internal static string Repository([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
}
