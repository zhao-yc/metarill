using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
namespace Metarill.Files.Tests;

internal sealed record SharedFileTarget(string Table,long Id);

/// <summary>普通共享R0文件并发夹具只拥有精确随机表和已登记文件ID，所有DDL之前核对Server-200实例pin。</summary>
internal sealed class SharedFileConcurrencyFixture:IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids=new(857);
    private const long Owner=9521,Reader=9522;
    private string nonce="",prefix="",evidence="",objects="",type="",parent="";
    private bool created;
    private readonly List<long> files=[];
    internal DatabaseSession Session{get;private set;}=null!;
    internal MetadataCatalog Catalog{get;private set;}=null!;
    internal MetadataCatalog Combined{get;private set;}=null!;
    internal SqlRowStore Store{get;private set;}=null!;
    private FileMetadataContext context=null!;
    private FileLibraryOptions options=null!;
    internal string Source=>prefix+"_source";
    internal string Warehouse=>prefix+"_warehouse";
    internal long Record{get;private set;}
    internal long OtherRecord{get;private set;}
    internal long WarehouseId{get;private set;}
    internal long OtherWarehouseId{get;private set;}
    internal long OtherAreaId{get;private set;}
    internal IReadOnlyList<SharedFileTarget> SelectedTargets{get;private set;}=[];
    internal bool Verified{get;set;}
    /// <summary>普通身份固定属于当前合成模型，不接受外部上下文。</summary>
    private RuntimeActor Actor(long account=Owner)=>new(account,"共享文件并发测试",false,"1","5");
    /// <summary>使用真实文件服务并显式注入普通授权器。</summary>
    private FileLibraryService Service(long account=Owner)=>new(Store,context,Ids,new Auth(Actor(account),Catalog),options);
    /// <summary>每次取得真实观察器，删除前后由调用者保留同一实例。</summary>
    internal FileReferenceMutationObserver Observer()=>new(Store,context,new Auth(Actor(),Catalog),Ids,options);
    /// <summary>物理父外键按当前模型类型生成规范值。</summary>
    internal object ParentValue(long id)=>parent=="string"?id.ToString(CultureInfo.InvariantCulture):id;
    /// <summary>稀疏末端按当前所选ID类型写入。</summary>
    private object SelectedValue(long id)=>type=="string"?id.ToString(CultureInfo.InvariantCulture):id;

    /// <summary>复用已证明的共享文件模型，仅改随机表名与物理删除元数据，不修改平台真实配置。</summary>
    internal static async Task<SharedFileConcurrencyFixture> CreateAsync(string type,string parent,bool shorter,bool derived)
    {
        var f=new SharedFileConcurrencyFixture{nonce=Guid.NewGuid().ToString("N"),type=type,parent=parent};
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            var database=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var connection=new MySqlConnectionStringBuilder(database.ConnectionString);
            if(database.Provider!="mysql"||database.ExpectedDatabase!="metarill_business_test"||connection.Database!=database.ExpectedDatabase||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("共享文件并发只允许Server-200隔离业务库。");
            f.Session=new DatabaseSession(database);await f.PinAsync();
            f.prefix="mt_sfc_"+f.nonce[..12];var repository=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??Directory.GetCurrentDirectory();
            f.evidence=Path.Combine(repository,".local/shared-cross-file-concurrency",f.nonce);Directory.CreateDirectory(f.evidence);f.objects=Path.Combine(f.evidence,"objects");
            var model=FileSharedCrossParentScopeTests.Catalog(type,parent,shorter,derived);
            f.Catalog=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(model).Replace("file_shared",f.prefix,StringComparison.Ordinal))!;
            var removed=f.Catalog.ConfigColumns.Single(c=>c.ConfigTableId=="1"&&c.ColumnName=="delete_no");f.Catalog.ConfigColumns.Remove(removed);f.Catalog.ConfigPageColumns.RemoveAll(c=>c.ConfigColumnId==removed.Id);
            f.Catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId="1"});foreach(var page in f.Catalog.ConfigPages)page.YzAdminTypeId="5";
            f.Catalog.ConfigColumns.Single(c=>c.Id=="17").ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}";
            f.context=new FileMetadataContext([f.Catalog]);Assert.True(f.context.HasLogicalFileParents);
            var path=Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA"))!,"files.json");
            var metadata=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            f.Combined=MetadataCatalogComposition.MergePhysical(f.Catalog,metadata);f.Store=new SqlRowStore(f.Session,f.Combined);
            await f.Store.ReadAsync("yz_file_library",D("id",-1L)); // 公用文件表必须已经初始化，不在夹具内隐式建表。
            f.options=new(){RootDirectory=f.objects,MaxFileBytes=4096};var schema=new SchemaService(f.Session);var plan=await schema.PreviewAsync(f.Catalog);
            Assert.Equal(f.Catalog.ConfigTables.Count,plan.Changes.Count);Assert.All(plan.Changes,c=>Assert.Equal("createTable",c.Kind));
            await f.ReceiptAsync("intent",new{f.nonce,f.prefix,type,parent,shorter,derived,names=f.Catalog.ConfigTables.Select(t=>t.TableName).Order().ToArray(),database=database.ExpectedDatabase,instanceVerified=true});
            f.created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await f.SeedAsync(shorter,derived);return f;
        }
        catch(Exception error)
        {
            if(f.evidence.Length>0)await f.ReceiptAsync("creation-failure",new{errorType=error.GetType().Name});
            await f.DisposeAsync();throw;
        }
    }
    /// <summary>两组真实目标分别用于已选路径和未选对照，名称派生不改变父键必须物理的约束。</summary>
    private async Task SeedAsync(bool shorter,bool derived)
    {
        var roles=new[]{"country","area","place","warehouse"};var nodes=new long[2,4];
        for(var batch=0;batch<2;batch++)for(var role=0;role<4;role++)nodes[batch,role]=Ids.NextId();
        for(var batch=0;batch<2;batch++)for(var role=0;role<4;role++)
        {
            var table=Catalog.ConfigTables.Single(t=>t.TableName==prefix+"_"+roles[role]);var project=Catalog.ConfigColumns.Single(c=>c.ConfigTableId==table.Id&&c.ColumnName=="yz_project_id");
            var row=D("id",nodes[batch,role],"yz_project_id",project.LogicalType=="string"?(object)"1":1L,"enabled",true);
            if(derived&&role<3){var company=Ids.NextId();await Store.InsertAsync(prefix+"_company",D("id",company,"name","派生节点"+batch+role,"yz_project_id",1L));row["company_id"]=company;}
            else row["name"]="实际节点"+batch+role;
            if(role>0)row[role==1?"country_id":"area_id"]=ParentValue(nodes[batch,role==1?0:1]);
            await Store.InsertAsync(table.TableName,row);
        }
        WarehouseId=nodes[0,3];OtherWarehouseId=nodes[1,3];OtherAreaId=nodes[1,1];
        SelectedTargets=Enumerable.Range(0,4).Where(i=>!shorter||i!=2).Select(i=>new SharedFileTarget(prefix+"_"+roles[i],nodes[0,i])).ToArray();
        Record=Ids.NextId();OtherRecord=Ids.NextId();
        foreach(var id in new[]{Record,OtherRecord})
        {
            var row=D("id",id,"name","原记录","yz_project_id",1L,"country_id",null,"area_id",null,"warehouse_id",SelectedValue(WarehouseId),"owner_country_id",SelectedValue(nodes[0,0]));
            if(!shorter)row["place_id"]=SelectedValue(nodes[0,2]);await Store.InsertAsync(Source,row);
        }
    }
    /// <summary>固定账号上传真实字节，并在计划返回时立即登记精确文件ID。</summary>
    internal async Task<long> UploadAsync(string label)
    {
        var bytes=Encoding.UTF8.GetBytes("共享父范围并发"+nonce+label);var service=Service();var plan=await service.PlanUploadAsync(new(nonce+"-"+label+".txt","text/plain",bytes.Length,"17"));files.Add(plan.FileId);
        await service.UploadAsync(plan.FileId,new MemoryStream(bytes));await service.CompleteAsync(plan.FileId);return plan.FileId;
    }
    /// <summary>实际业务字段和引用观察器同事务；不在夹具中模仿观察器内部引用写入。</summary>
    internal Task BindAsync(long file)=>Session.InTransactionAsync(async()=>{await Store.UpdateAsync(Source,Record,D("attachments",JsonSerializer.Serialize(new[]{file.ToString(CultureInfo.InvariantCulture)})));await Observer().BeforeCommitAsync(Context(file));return true;});
    /// <summary>来源页固定为共同国家父页，物理删除后的父证明变化可独立于混合物理外键检测。</summary>
    internal RecordMutationContext Context(long file,bool deleting=false)=>new("101",Source,Record,"1",D("attachments",JsonSerializer.Serialize(new[]{file.ToString(CultureInfo.InvariantCulture)})),deleting,Session.CurrentTransaction!);
    /// <summary>使用不同普通账号读取，避免上传者例外掩盖保护引用授权。</summary>
    internal async Task ReadableAsync(long file,bool allowed)
    {
        if(allowed)Assert.Equal(file,(await Service(Reader).ResolveAsync(file)).FileId);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Service(Reader).ResolveAsync(file));
    }
    /// <summary>物理DELETE使用原连接／事务，真实删除行数必须为一。</summary>
    internal async Task DeleteRecordAsync()
    {
        await using var command=Session.CurrentTransaction!.Connection!.CreateCommand();command.Transaction=Session.CurrentTransaction;command.CommandText="DELETE FROM "+Session.Provider.QuoteIdentifier(Source)+" WHERE id=@id";var p=command.CreateParameter();p.ParameterName="id";p.Value=Record;command.Parameters.Add(p);Assert.Equal(1,await command.ExecuteNonQueryAsync());
    }
    /// <summary>只读取本夹具来源记录的当前引用。</summary>
    internal Task<List<Dictionary<string,object?>>> ReferencesAsync()=>Store.ReadAsync("yz_file_ref",D("record_id",Record));
    /// <summary>全部自有目标／来源、上传文件和含墓碑引用均参与整笔回滚比较。</summary>
    internal async Task<string> StateAsync()
    {
        var state=new Dictionary<string,object>();foreach(var table in Catalog.ConfigTables)state[table.TableName]=(await Store.ReadIncludingDeletedAsync(table.TableName)).OrderBy(r=>SqlRowStore.Id(r)).ToArray();
        foreach(var table in new[]{"yz_file_library","yz_file_ref"})
        {
            var rows=new List<Dictionary<string,object?>>();foreach(var file in files)rows.AddRange(await Store.ReadIncludingDeletedAsync(table,D(table=="yz_file_library"?"id":"file_id",file)));
            state[table]=rows.OrderBy(r=>SqlRowStore.Id(r)).ToArray();
        }
        return JsonSerializer.Serialize(state);
    }
    /// <summary>私有证据不写连接串、凭据或业务数据正文。</summary>
    internal Task ReceiptAsync(string name,object value)=>File.WriteAllTextAsync(Path.Combine(evidence,name+".json"),JsonSerializer.Serialize(value)+"\n");
    /// <summary>每次DDL与清理前核对数据库、服务器UUID及主机名，不能仅凭域名认定实例。</summary>
    private async Task PinAsync()
    {
        using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var connection=Session.Provider.CreateConnection(Session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()&&reader.GetString(0)==Session.Options.ExpectedDatabase&&reader.GetString(1)==pin.RootElement.GetProperty("expectedHostname").GetString()&&reader.GetString(2)==pin.RootElement.GetProperty("expectedServerUuid").GetString(),"共享文件并发实例pin不符。");
    }
    /// <summary>只有本轮已证明不存在的随机表及登记文件可删除；结构／内容两次核对后独立核零。</summary>
    public async ValueTask DisposeAsync()
    {
        if(Session is null)return;
        try
        {
            if(!created)return;await PinAsync();var names=Catalog.ConfigTables.Select(t=>t.TableName).Order().ToArray();
            Assert.All(names,n=>Assert.StartsWith("mt_sfc_"+nonce[..12]+"_",n));var schema=new SchemaService(Session);var actual=await schema.InspectAsync(Catalog);var present=actual.Tables.Select(t=>t.Name).ToHashSet(StringComparer.Ordinal);
            Assert.All((await schema.PreviewAsync(Catalog)).Changes,c=>{Assert.Equal("createTable",c.Kind);Assert.Contains(c.Table,names);Assert.DoesNotContain(c.Table,present);});
            var hashes=new Dictionary<string,string>();foreach(var name in names.Where(present.Contains))hashes[name]=await TableHashAsync(Store,name);
            using var independent=new DatabaseSession(Session.Options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(independent).InspectAsync(Catalog)));var other=new SqlRowStore(independent,Combined);
            foreach(var name in hashes.Keys)Assert.Equal(hashes[name],await TableHashAsync(other,name));await ReceiptAsync("cleanup-before",new{names,present=present.Order().ToArray(),files,Verified,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=hashes});
            await using var connection=Session.Provider.CreateConnection(Session.Options);await connection.OpenAsync();
            foreach(var file in files)
            {
                var rows=await Store.ReadIncludingDeletedAsync("yz_file_library",D("id",file));if(rows.Count>0){Assert.Equal(Owner,SqlRowStore.Id(rows[0],"insert_manager_id"));Assert.Contains(nonce,SqlRowStore.Text(rows[0],"name"));}
                foreach(var table in new[]{"yz_file_ref","yz_file_library"}){await using var command=connection.CreateCommand();command.CommandText="DELETE FROM "+Session.Provider.QuoteIdentifier(table)+" WHERE "+(table=="yz_file_ref"?"file_id":"id")+"=@id";var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=file;command.Parameters.Add(parameter);await command.ExecuteNonQueryAsync();}
            }
            foreach(var name in names.Where(present.Contains)){await using var command=connection.CreateCommand();command.CommandText="DROP TABLE "+Session.Provider.QuoteIdentifier(name);await command.ExecuteNonQueryAsync();}
            await using var verify=Session.Provider.CreateConnection(Session.Options);await verify.OpenAsync();
            foreach(var name in names){await using var command=verify.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var p=command.CreateParameter();p.ParameterName="name";p.Value=name;command.Parameters.Add(p);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync(),CultureInfo.InvariantCulture));}
            foreach(var file in files){Assert.Empty(await other.ReadIncludingDeletedAsync("yz_file_library",D("id",file)));Assert.Empty(await other.ReadIncludingDeletedAsync("yz_file_ref",D("file_id",file)));}
            await ReceiptAsync("cleanup",new{nonce,names,files,Verified,independentTableAbsenceChecked=true,independentFileAbsenceChecked=true,remaining=0});
            var expected=Path.GetFullPath(Path.Combine(evidence,"objects"));Assert.Equal(expected,Path.GetFullPath(objects));Assert.StartsWith(Path.GetFullPath(evidence)+Path.DirectorySeparatorChar,expected);if(Directory.Exists(expected))Directory.Delete(expected,true);
        }
        catch(Exception error){if(evidence.Length>0)await ReceiptAsync("cleanup-failure",new{errorType=error.GetType().Name,files,Verified});throw;}
        finally{Session.Dispose();}
    }
    /// <summary>快照只保留摘要，独立会话逐表复算后才允许清理。</summary>
    private static async Task<string> TableHashAsync(SqlRowStore store,string table)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize((await store.ReadIncludingDeletedAsync(table)).OrderBy(r=>SqlRowStore.Id(r))))));
    private static Dictionary<string,object?> D(params object?[] values)=>Enumerable.Range(0,values.Length/2).ToDictionary(i=>(string)values[i*2]!,i=>values[i*2+1]);
    private sealed class Auth(RuntimeActor actor,MetadataCatalog catalog):IRuntimeAuthorization
    {
        /// <summary>服务端测试身份不由请求参数建立。</summary>
        public RuntimeActor RequireActor()=>actor;
        /// <summary>固定普通账号仅取得本模型已发布页面动作，不接受客户端项目或后台身份。</summary>
        public void RequirePagePermission(string page,string operation){if(!catalog.ConfigPages.Any(p=>p.Id==page&&p.YzProjectId==actor.ProjectId)||operation is not("create" or "update" or "delete" or "detail" or "list"))throw new UnauthorizedAccessException("本夹具身份无此动作。");}
    }
}
