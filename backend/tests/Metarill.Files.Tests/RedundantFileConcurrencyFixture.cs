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

/// <summary>固定R1实际父范围并发夹具只拥有精确随机表和已登记文件ID，所有DDL之前核对Server-200实例pin。</summary>
internal sealed class RedundantFileConcurrencyFixture:IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator Ids=new(858);
    private const long Owner=9521,Reader=9522;
    private string nonce="",prefix="",evidence="",objects="",type="",parent="";
    private bool created,shorter,derived;
    private readonly long[,] nodes=new long[2,4];
    private static readonly string[] Roles=["country","area","place","warehouse"];
    internal string PageId{get;private set;}="101";
    internal int Role{get;private set;}
    internal string ScopeField=>Roles[Role]+"_id";
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
    internal long ForeignRecord{get;private set;}
    internal long WarehouseId{get;private set;}
    internal long OtherWarehouseId{get;private set;}
    internal long OtherAreaId{get;private set;}
    internal IReadOnlyList<SharedFileTarget> AllTargets=>Enumerable.Range(0,4).Select(i=>new SharedFileTarget(prefix+"_"+Roles[i],nodes[0,i])).ToArray();
    internal IReadOnlyList<SharedFileTarget> OtherTargets=>Enumerable.Range(0,4).Select(i=>new SharedFileTarget(prefix+"_"+Roles[i],nodes[1,i])).ToArray();
    internal IReadOnlyList<SharedFileTarget> SelectedTargets=>AllTargets.Where((_,i)=>i==0||Role>=1&&i==1||i==Role).ToArray();
    internal IReadOnlyList<SharedFileTarget> UnselectedTargets=>AllTargets.Except(SelectedTargets).ToArray();
    internal SharedFileTarget ScopeTarget=>AllTargets[Role];
    internal bool Verified{get;set;}
    /// <summary>普通身份固定属于当前合成模型，不接受外部上下文。</summary>
    private RuntimeActor Actor(long account=Owner)=>new(account,"共享文件并发测试",false,"1","5");
    /// <summary>使用真实文件服务并显式注入普通授权器。</summary>
    private FileLibraryService Service(long account=Owner)=>new(Store,context,Ids,new Auth(Actor(account),Catalog),options);
    /// <summary>每次取得真实观察器，删除前后由调用者保留同一实例。</summary>
    internal FileReferenceMutationObserver Observer()=>new(Store,context,new Auth(Actor(),Catalog),Ids,options);
    /// <summary>物理父外键按当前模型类型生成规范值。</summary>
    internal object ParentValue(long id)=>parent=="string"?id.ToString(CultureInfo.InvariantCulture):id;
    /// <summary>来源实际物理父键按声明类型写入，不压缩祖先。</summary>
    internal object SelectedValue(long id)=>type=="string"?id.ToString(CultureInfo.InvariantCulture):id;

    /// <summary>复用已证明的共享文件模型，仅改随机表名与物理删除元数据，不修改平台真实配置。</summary>
    internal static async Task<RedundantFileConcurrencyFixture> CreateAsync(string type,string parent,bool shorter,bool derived,int globalMask=0,bool noProject=false)
    {
        var f=new RedundantFileConcurrencyFixture{nonce=Guid.NewGuid().ToString("N"),type=type,parent=parent,shorter=shorter,derived=derived};
        try
        {
            using var settings=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            var database=settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var connection=new MySqlConnectionStringBuilder(database.ConnectionString);
            if(database.Provider!="mysql"||database.ExpectedDatabase!="metarill_business_test"||connection.Database!=database.ExpectedDatabase||connection.Server!="deve-server.yinzon.com"||connection.Port!=3306)throw new InvalidOperationException("共享文件并发只允许Server-200隔离业务库。");
            f.Session=new DatabaseSession(database);await f.PinAsync();
            f.prefix="mt_rfc_"+f.nonce[..12];var repository=Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??Directory.GetCurrentDirectory();
            f.evidence=Path.Combine(repository,".local/shared-r1-file-concurrency",f.nonce);Directory.CreateDirectory(f.evidence);f.objects=Path.Combine(f.evidence,"objects");
            var model=FileRedundantSharedParentScopeTests.Catalog(type,parent,shorter,derived);
            // 只有明确全局目标取消项目范围，来源仍保留物理项目列及原文件身份。
            for(var role=0;role<4;role++)if((globalMask&(1<<role))!=0){var id=(role+2).ToString(CultureInfo.InvariantCulture);model.ConfigTables.Single(table=>table.Id==id).Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");model.ConfigPages.Single(page=>page.ConfigTableId==id).WhereSql="enabled=1";if(noProject){var removedProjects=model.ConfigColumns.Where(column=>column.ConfigTableId==id&&column.ColumnName=="yz_project_id").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);model.ConfigColumns.RemoveAll(column=>removedProjects.Contains(column.Id));model.ConfigPageColumns.RemoveAll(field=>removedProjects.Contains(field.ConfigColumnId));}}
            f.Catalog=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(model).Replace("file_shared",f.prefix,StringComparison.Ordinal))!;
            var removed=f.Catalog.ConfigColumns.Single(c=>c.ConfigTableId=="1"&&c.ColumnName=="delete_no");f.Catalog.ConfigColumns.Remove(removed);f.Catalog.ConfigPageColumns.RemoveAll(c=>c.ConfigColumnId==removed.Id);
            f.Catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId="1"});foreach(var page in f.Catalog.ConfigPages)page.YzAdminTypeId="5";
            f.Catalog.ConfigColumns.Single(c=>c.Id=="17").ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}";
            f.context=new FileMetadataContext([f.Catalog]);Assert.True(f.context.HasLogicalFileParents);foreach(var page in f.context.Catalogs.Single().ConfigPages.Where(page=>page.ConfigTableId=="1"))Assert.All(f.context.PageScope(f.context.Catalogs.Single(),page).Crosses,cross=>Assert.StartsWith("r1:",cross.SourceId));
            var path=Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA"))!,"files.json");
            var metadata=JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(path),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            f.Combined=MetadataCatalogComposition.MergePhysical(f.Catalog,metadata);f.Store=new SqlRowStore(f.Session,f.Combined);
            await f.Store.ReadAsync("yz_file_library",D("id",-1L)); // 公用文件表必须已经初始化，不在夹具内隐式建表。
            f.options=new(){RootDirectory=f.objects,MaxFileBytes=4096};var schema=new SchemaService(f.Session);var plan=await schema.PreviewAsync(f.Catalog);
            Assert.Equal(f.Catalog.ConfigTables.Count,plan.Changes.Count);Assert.All(plan.Changes,c=>Assert.Equal("createTable",c.Kind));
            await f.ReceiptAsync("intent",new{f.nonce,f.prefix,type,parent,shorter,derived,globalMask,noProject,names=f.Catalog.ConfigTables.Select(t=>t.TableName).Order().ToArray(),database=database.ExpectedDatabase,instanceVerified=true});
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
        var roles=Roles;
        for(var batch=0;batch<2;batch++)for(var role=0;role<4;role++)nodes[batch,role]=Ids.NextId();
        for(var batch=0;batch<2;batch++)for(var role=0;role<4;role++)
        {
            var table=Catalog.ConfigTables.Single(t=>t.TableName==prefix+"_"+roles[role]);var project=Catalog.ConfigColumns.SingleOrDefault(c=>c.ConfigTableId==table.Id&&c.ColumnName=="yz_project_id");
            var row=D("id",nodes[batch,role],"enabled",true);if(project is not null){var owner=GlobalCrossTableTarget.Declared(table)?2L:1L;row["yz_project_id"]=project.LogicalType=="string"?owner.ToString(CultureInfo.InvariantCulture):(object)owner;}
            if(derived&&role<3){var company=Ids.NextId();await Store.InsertAsync(prefix+"_company",D("id",company,"name","派生节点"+batch+role,"yz_project_id",1L));row["company_id"]=company;}
            else row["name"]="实际节点"+batch+role;
            if(role>0)row[role==1?"country_id":"area_id"]=ParentValue(nodes[batch,role==1?0:1]);
            await Store.InsertAsync(table.TableName,row);
        }
        WarehouseId=nodes[0,3];OtherWarehouseId=nodes[1,3];OtherAreaId=nodes[1,1];
        Record=Ids.NextId();OtherRecord=Ids.NextId();ForeignRecord=Ids.NextId();
        foreach(var id in new[]{Record,OtherRecord,ForeignRecord})await Store.InsertAsync(Source,RecordValues(id));
    }
    /// <summary>真实R1祖先及缓存均写入自有样本，不将R0稀疏空值伪作物理父。</summary>
    private Dictionary<string,object?> RecordValues(long id)
    {
        var row=D("id",id,"name","原记录","yz_project_id",id==ForeignRecord?2L:1L,"country_id",SelectedValue(nodes[0,0]),"area_id",SelectedValue(nodes[0,1]),"warehouse_id",SelectedValue(nodes[0,3]),"owner_country_id",SelectedValue(nodes[0,0]));
        if(!shorter)row["place_id"]=SelectedValue(nodes[0,2]);var physical=MetadataStoragePolicy.ForTable(Catalog,Catalog.ConfigTables.Single(t=>t.Id=="1")).PhysicalColumns.Select(c=>c.ColumnName).ToHashSet(StringComparer.Ordinal);
        for(var role=0;role<4;role++)if(physical.Contains(Roles[role]+"_name"))row[Roles[role]+"_name"]=(derived&&role<3?"派生节点":"实际节点")+"0"+role;
        if(physical.Contains("owner_country_name"))row["owner_country_name"]=(derived?"派生节点":"实际节点")+"00";return row;
    }
    /// <summary>上页真实删除后为下一父页建立独立来源；全部页面由同一发布目录编译。</summary>
    internal async Task NextPageAsync(string page)
    {
        if(page is not("101" or "102" or "103" or "104" or "105"))throw new ArgumentException("未知自有父页。");
        if(page!="101"){Assert.Empty(await Store.ReadIncludingDeletedAsync(Source,D("id",Record)));Record=Ids.NextId();await Store.InsertAsync(Source,RecordValues(Record));}
        PageId=page;Role=page=="102"?1:page=="103"?(shorter?1:2):page=="104"?3:0;
    }
    /// <summary>固定账号上传真实字节，并在计划返回时立即登记精确文件ID。</summary>
    internal async Task<long> UploadAsync(string label)
    {
        var bytes=Encoding.UTF8.GetBytes("共享父范围并发"+nonce+label);var service=Service();var plan=await service.PlanUploadAsync(new(nonce+"-"+label+".txt","text/plain",bytes.Length,"17"));files.Add(plan.FileId);
        await service.UploadAsync(plan.FileId,new MemoryStream(bytes));await service.CompleteAsync(plan.FileId);return plan.FileId;
    }
    /// <summary>实际业务字段和引用观察器同事务；不在夹具中模仿观察器内部引用写入。</summary>
    internal Task BindAsync(long file)=>Session.InTransactionAsync(async()=>{await Store.UpdateAsync(Source,Record,D("attachments",JsonSerializer.Serialize(new[]{file.ToString(CultureInfo.InvariantCulture)})));await Observer().BeforeCommitAsync(Context(file));return true;});
    /// <summary>父页由固定五页轮次选择，观察器只能读取实际来源父键。</summary>
    internal RecordMutationContext Context(long file,bool deleting=false)=>new(PageId,Source,Record,"1",D("attachments",JsonSerializer.Serialize(new[]{file.ToString(CultureInfo.InvariantCulture)})),deleting,Session.CurrentTransaction!);
    /// <summary>使用不同普通账号读取，避免上传者例外掩盖保护引用授权。</summary>
    internal async Task ReadableAsync(long file,bool allowed)
    {
        if(allowed)Assert.Equal(file,(await Service(Reader).ResolveAsync(file)).FileId);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Service(Reader).ResolveAsync(file));
    }
    /// <summary>即便使用相同全局祖先，外项目来源也不能在本项目挂文件，拒绝后不留引用。</summary>
    internal async Task AssertForeignSourceDeniedAsync(long file)
    {
        var before=await StateAsync();await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Session.InTransactionAsync(async()=>{var values=D("attachments",JsonSerializer.Serialize(new[]{file.ToString(CultureInfo.InvariantCulture)}));await Store.UpdateAsync(Source,ForeignRecord,values);await Observer().BeforeCommitAsync(new(PageId,Source,ForeignRecord,"1",values,false,Session.CurrentTransaction!));return true;}));Assert.Equal(before,await StateAsync());Assert.Empty(await Store.ReadIncludingDeletedAsync("yz_file_ref",D("record_id",ForeignRecord)));
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
            Assert.All(names,n=>Assert.StartsWith("mt_rfc_"+nonce[..12]+"_",n));var schema=new SchemaService(Session);var actual=await schema.InspectAsync(Catalog);var present=actual.Tables.Select(t=>t.Name).ToHashSet(StringComparer.Ordinal);
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
    /// <summary>参数值保留明确类型与null，不拼接到SQL正文。</summary>
    private static Dictionary<string,object?> D(params object?[] values)=>Enumerable.Range(0,values.Length/2).ToDictionary(i=>(string)values[i*2]!,i=>values[i*2+1]);
    private sealed class Auth(RuntimeActor actor,MetadataCatalog catalog):IRuntimeAuthorization
    {
        /// <summary>服务端测试身份不由请求参数建立。</summary>
        public RuntimeActor RequireActor()=>actor;
        /// <summary>固定普通账号仅取得本模型已发布页面动作，不接受客户端项目或后台身份。</summary>
        public void RequirePagePermission(string page,string operation){if(!catalog.ConfigPages.Any(p=>p.Id==page&&p.YzProjectId==actor.ProjectId)||operation is not("create" or "update" or "delete" or "detail" or "list"))throw new UnauthorizedAccessException("本夹具身份无此动作。");}
    }
}
