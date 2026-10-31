using System.Data.Common;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Files.Tests;

public sealed class FilesIntegrationFactAttribute : FactAttribute
{
    public FilesIntegrationFactAttribute()
    {
        var settings = Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS");
        var metadata = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA");
        if (string.IsNullOrWhiteSpace(settings) || !File.Exists(settings) || string.IsNullOrWhiteSpace(metadata) || !File.Exists(metadata))
            Skip = "未提供隔离 MySQL 配置及平台元数据，真实文件库验收未执行。";
    }
}

internal sealed class FilesFixture : IAsyncDisposable
{
    private static readonly SnowflakeIdGenerator SharedIds = new(851);
    private readonly HashSet<string> createdTables = new(StringComparer.Ordinal);
    public DatabaseSession Session { get; private set; } = null!;
    public MetadataRegistry Registry { get; private set; } = null!;
    public SqlRowStore Store { get; private set; } = null!;
    public FileLibraryOptions Options { get; private set; } = null!;
    public string DirectoryPath { get; private set; } = "";
    public string TableName { get; private set; } = "";
    public string PageId { get; private set; } = "";
    public string ParentTableName { get; private set; } = "";
    public IReadOnlyList<string> CrossTables{get;private set;}=[];
    public string CrossType{get;private set;}="";
    public string CrossEvidence{get;private set;}="";
    public bool CrossVerified{get;set;}
    private MetadataCatalog? crossManaged;
    public string ProtectedColumnId { get; private set; } = "";
    public string PublicColumnId { get; private set; } = "";
    public List<long> FileIds { get; } = [];
    public List<long> FolderIds { get; } = [];
    public List<long> RecordIds { get; } = [];
    public IIdGenerator Ids => SharedIds;
    public RuntimeActor Actor(long id = 8001, bool administrator = false, string? project = null) => new(id, "文件测试账号", administrator, project ?? Registry.Platform.YzProjectId, Registry.Platform.YzAdminTypes[0].Id);
    public FileLibraryService Service(RuntimeActor? actor = null) => new(Store, new([Registry.Platform,Registry.Demo]), Ids, new TestAuthorization(actor ?? Actor(), Registry), Options);
    public FileReferenceMutationObserver Observer(RuntimeActor? actor = null) => new(Store, new([Registry.Platform,Registry.Demo]), new TestAuthorization(actor ?? Actor(), Registry), Ids, Options);

    /// <summary>只在指定测试库建立随机临时业务表，文件基础设施表必须已由协调者初始化。</summary>
    public static async Task<FilesFixture> CreateAsync(bool childScope = false, bool parentKeysAsText = false,bool physicalDelete=false,bool crossParent=false,string crossType="string")
    {
        var fixture = new FilesFixture();
        try
        {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("Database").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_test") throw new InvalidOperationException("文件测试只允许平台隔离MySQL测试库。");
        var connection = new MySqlConnector.MySqlConnectionStringBuilder(options.ConnectionString);
        if (connection.Database != options.ExpectedDatabase || !(await System.Net.Dns.GetHostAddressesAsync(connection.Server)).Any(address => address.ToString() == "192.168.31.200"))
            throw new InvalidOperationException("文件测试仅允许已核对的Server-200隔离库。");
        fixture.Session = new DatabaseSession(options);
        if(physicalDelete||crossParent)
        {
            using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using var actual=fixture.Session.Provider.CreateConnection(options);await actual.OpenAsync();await using var identity=actual.CreateCommand();identity.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await identity.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader.GetString(0)!=options.ExpectedDatabase||reader.GetString(1)!=pin.RootElement.GetProperty("expectedHostname").GetString()||reader.GetString(2)!=pin.RootElement.GetProperty("expectedServerUuid").GetString())throw new InvalidOperationException("物理删除样本实例pin不匹配。");
        }
        var metadataPath = Environment.GetEnvironmentVariable("METARILL_PLATFORM_METADATA")!;
        var platform = MetadataRegistry.Load(metadataPath);
        var testId = Guid.NewGuid().ToString("N")[..12];
        fixture.TableName = "mt_test_files_" + testId;
        fixture.DirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), ".local", "file-test-" + testId);
        Directory.CreateDirectory(fixture.DirectoryPath);
        fixture.Options = new FileLibraryOptions { RootDirectory = Path.Combine(fixture.DirectoryPath, "objects"), MaxFileBytes = 4096 };
        var table = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.TableName, Name = "虚构文件测试记录", YzProjectId = platform.YzProjectId };
        ColumnMetadata Column(string name, string type, bool required = false, string control = "", string detail = "") => new()
        {
            Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, ColumnName = name, LogicalType = type, ColumnLength = type == "string" ? "max" : "", IsDbNotNull = required ? 1 : 0,
            ControlKind = control, ControlDetail = detail, YzProjectId = platform.YzProjectId
        };
        var columns = new List<ColumnMetadata> { Column("id", "int64", true), Column("yz_project_id", "int64", true), Column("delete_no", "datetime"),
            Column("attachments", "string", false, "FileLibrary", "{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"),
            Column("public_files", "string", false, "FileLibrary", "{\"accessMode\":\"PUBLIC\"}"), Column("notes", "string") };
        if(physicalDelete)columns.RemoveAll(column=>column.ColumnName=="delete_no");
        var managedTables = new List<TableMetadata> { table };
        var managedColumns = new List<ColumnMetadata>();
        if (childScope)
        {
            fixture.ParentTableName = fixture.TableName + "_parent";
            var parent = new TableMetadata { Id = SharedIds.NextId().ToString(), TableName = fixture.ParentTableName, Name = "本次文件测试父记录", YzProjectId = platform.YzProjectId };
            var parentColumns = new[] { Column("id", "int64", true), Column("yz_project_id", parentKeysAsText ? "string" : "int64", true), Column("delete_no", "datetime") };
            foreach (var parentColumn in parentColumns) parentColumn.ConfigTableId = parent.Id;
            var relation = Column("parent_id", parentKeysAsText ? "string" : "int64"); relation.LinkConfigTableId = parent.Id;
            columns.Add(relation); managedTables.Add(parent); managedColumns.AddRange(parentColumns);
        }
        if(crossParent)
        {
            table.IsRongyu=0;fixture.CrossType=crossType;fixture.CrossTables=new[]{"country","area","place"}.Select(name=>fixture.TableName+"_"+name).ToArray();var targets=new List<TableMetadata>();
            for(var index=0;index<3;index++)
            {
                var target=new TableMetadata{Id=SharedIds.NextId().ToString(),YzProjectId=platform.YzProjectId,TableName=fixture.CrossTables[index],IsRongyu=1};targets.Add(target);managedTables.Add(target);
                var fields=new List<ColumnMetadata>{Column("id","int64",true),Column("name","string"),Column("yz_project_id",parentKeysAsText?"string":"int64",true),Column("delete_no","datetime"),Column("enabled","bool")};
                if(index>0){var parent=Column(index==1?"country_id":"area_id",parentKeysAsText?"string":"int64");parent.LinkConfigTableId=targets[index-1].Id;fields.Add(parent);}foreach(var field in fields)field.ConfigTableId=target.Id;managedColumns.AddRange(fields);
                var view=new PageMetadata{Id=SharedIds.NextId().ToString(),YzProjectId=platform.YzProjectId,YzAdminTypeId=platform.YzAdminTypes[0].Id,ConfigTableId=target.Id,WhereSql="yz_project_id=@current_yz_project_id AND enabled=1"};platform.ConfigPages.Add(view);platform.ConfigPageColumns.AddRange(fields.Select(field=>new PageColumnMetadata{Id=SharedIds.NextId().ToString(),ConfigPageId=view.Id,ConfigColumnId=field.Id,IsShow=1}));
            }
            var cascade=Column("place_id",crossType,false,"CascadeTree",JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{new{table=fixture.CrossTables[0],columnPrefix="country",nameField="name"},new{table=fixture.CrossTables[1],columnPrefix="area",parentField="country_id",nameField="name"},new{table=fixture.CrossTables[2],columnPrefix="place",parentField="area_id",nameField="name"}}}));cascade.LinkConfigTableId=targets[2].Id;columns.Add(cascade);
            fixture.CrossEvidence=Path.Combine(Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??Directory.GetCurrentDirectory(),".local/nonredundant-cross-table-parent-files",testId);Directory.CreateDirectory(fixture.CrossEvidence);
        }
        managedColumns.AddRange(columns);
        fixture.ProtectedColumnId = columns.Single(c => c.ColumnName == "attachments").Id;
        fixture.PublicColumnId = columns.Single(c => c.ColumnName == "public_files").Id;
        var page = new PageMetadata { Id = SharedIds.NextId().ToString(), ConfigTableId = table.Id, YzProjectId = platform.YzProjectId, YzAdminTypeId = platform.YzAdminTypes[0].Id };
        fixture.PageId = page.Id;
        if(crossParent)page.WhereSql="yz_project_id=@current_yz_project_id AND country_id=@parent_id"+(childScope?" AND parent_id=@parent_id":"");
        else if (childScope) page.WhereSql = "yz_project_id=@current_yz_project_id AND parent_id=@parent_id";
        platform.ConfigTables.AddRange(managedTables); platform.ConfigColumns.AddRange(managedColumns); platform.ConfigPages.Add(page);
        platform.ConfigPageColumns.AddRange(columns.Where(c => c.ControlKind == "FileLibrary").Select(c => new PageColumnMetadata { Id = SharedIds.NextId().ToString(), ConfigPageId = page.Id, ConfigColumnId = c.Id, ColumnName = c.ColumnName, IsEdit = 1 }));
        var testPlatform = Path.Combine(fixture.DirectoryPath, "platform.json");
        await File.WriteAllTextAsync(testPlatform, JsonSerializer.Serialize(platform, JsonDefaults.Options));
        fixture.Registry = new MetadataRegistry(testPlatform, Path.Combine(Path.GetDirectoryName(metadataPath)!, "demo.json"));
        fixture.Store = new SqlRowStore(fixture.Session, fixture.Registry.Platform);
        await fixture.Store.ReadAsync("yz_file_library", new Dictionary<string, object?> { ["id"] = -1L });
        var schema = new SchemaService(fixture.Session);
        var managed = new MetadataCatalog { YzProjectId = platform.YzProjectId, ConfigTables = managedTables, ConfigColumns = managedColumns };
        if(crossParent)fixture.crossManaged=managed;
        var plan = await schema.PreviewAsync(managed);
        if (plan.Changes.Any(change => change.Kind != "createTable") || plan.Changes.Count != managedTables.Count)
            throw new InvalidOperationException("随机文件测试表必须全部不存在且仅创建本次声明的表。");
        // 只登记已证明不存在的本次创建目标，失败清理不能删除碰巧同名的既有表。
        foreach (var change in plan.Changes) fixture.createdTables.Add(change.Table);
        await schema.ApplyAsync(plan);
        return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>每个文件都记录本测试分配的ID，后续清理不会扫整个文件库。</summary>
    public async Task<FileUploadPlan> PlanAsync(string name, byte[] bytes, RuntimeActor? actor = null, bool publicly = false, long? folder = null, string? sha256 = null)
    {
        var plan = await Service(actor).PlanUploadAsync(new(name, "text/html", bytes.Length, publicly ? PublicColumnId : ProtectedColumnId, folder, sha256));
        FileIds.Add(plan.FileId); return plan;
    }
    public async Task<FileDescriptor> ReadyAsync(string name, byte[] bytes, RuntimeActor? actor = null, bool publicly = false)
    {
        var service = Service(actor); var plan = await PlanAsync(name, bytes, actor, publicly);
        await service.UploadAsync(plan.FileId, new MemoryStream(bytes)); return await service.CompleteAsync(plan.FileId);
    }
    public async Task<long> RecordAsync(string? project = null, long? parent = null)
    {
        var id = SharedIds.NextId(); RecordIds.Add(id);
        var values = new Dictionary<string, object?> { ["id"] = id, ["yz_project_id"] = long.Parse(project ?? Registry.Platform.YzProjectId) };
        if (ParentTableName.Length > 0) values["parent_id"] = parent;
        await Store.InsertAsync(TableName, values); return id;
    }
    public async Task<List<Dictionary<string, object?>>> ReferencesAsync(long record) => await Store.ReadAsync("yz_file_ref", new Dictionary<string, object?> { ["record_id"] = record });

    /// <summary>清理范围只包含当前fixture创建的唯一ID和随机临时表。</summary>
    public async ValueTask DisposeAsync()
    {
        var cleaned=false;
        try
        {
            if (Session is null) {cleaned=true;return;}
            await using var connection = Session.Provider.CreateConnection(Session.Options); await connection.OpenAsync();
            if(CrossTables.Count>0&&createdTables.Count>0)
            {
                using var pin=JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_ROLE_READ_INSTANCE_PIN")!));await using(var identity=connection.CreateCommand()){identity.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var reader=await identity.ExecuteReaderAsync();if(!await reader.ReadAsync()||reader.GetString(0)!=Session.Options.ExpectedDatabase||reader.GetString(1)!=pin.RootElement.GetProperty("expectedHostname").GetString()||reader.GetString(2)!=pin.RootElement.GetProperty("expectedServerUuid").GetString())throw new InvalidOperationException("文件跨表清理实例pin不符。");}
                if(!CrossTables.SequenceEqual(new[]{"country","area","place"}.Select(name=>TableName+"_"+name)))throw new InvalidOperationException("文件跨表清理目标不符。");var schema=new SchemaService(Session);var actual=await schema.InspectAsync(crossManaged!);if((await schema.PreviewAsync(crossManaged!)).Changes.Count!=0||actual.Tables.Count!=createdTables.Count)throw new InvalidOperationException("文件跨表样本结构有漂移，保留现场。");var state=new Dictionary<string,string>();foreach(var table in createdTables)state[table]=JsonSerializer.Serialize((await Store.ReadIncludingDeletedAsync(table)).OrderBy(row=>SqlRowStore.Id(row)));
                using var check=new DatabaseSession(Session.Options);Assert.Equal(MySqlSchemaPlanner.Fingerprint(actual),MySqlSchemaPlanner.Fingerprint(await new SchemaService(check).InspectAsync(crossManaged!)));var other=new SqlRowStore(check,Registry.Platform);foreach(var table in createdTables)Assert.Equal(state[table],JsonSerializer.Serialize((await other.ReadIncludingDeletedAsync(table)).OrderBy(row=>SqlRowStore.Id(row))));
                await File.WriteAllTextAsync(Path.Combine(CrossEvidence,"cleanup-before.json"),JsonSerializer.Serialize(new{table=TableName,names=createdTables.Order().ToArray(),fileIds=FileIds.ToArray(),recordIds=RecordIds.ToArray(),verified=CrossVerified,crossType=CrossType,instanceVerified=true,database=Session.Options.ExpectedDatabase,fingerprint=MySqlSchemaPlanner.Fingerprint(actual),rowsSha256=state.ToDictionary(pair=>pair.Key,pair=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pair.Value))))})+"\n");
            }
            foreach (var id in FileIds)
            {
                await ExecuteAsync(connection, "DELETE FROM `yz_file_ref` WHERE `file_id`=@id", id);
                await ExecuteAsync(connection, "DELETE FROM `yz_file_library` WHERE `id`=@id", id);
            }
            foreach (var id in RecordIds) await ExecuteAsync(connection, "DELETE FROM `yz_file_ref` WHERE `record_id`=@id", id);
            foreach (var id in FolderIds) await ExecuteAsync(connection, "DELETE FROM `yz_file_library_folder` WHERE `id`=@id", id);
            if (string.IsNullOrEmpty(TableName)) {cleaned=true;return;}
            if (!TableName.StartsWith("mt_test_files_", StringComparison.Ordinal)) throw new InvalidOperationException("临时表清理范围错误。");
            await using var drop = connection.CreateCommand();
            if (createdTables.Contains(TableName)) { drop.CommandText = "DROP TABLE IF EXISTS " + Session.Provider.QuoteIdentifier(TableName); await drop.ExecuteNonQueryAsync(); }
            if (ParentTableName.Length > 0 && createdTables.Contains(ParentTableName))
            {
                if (ParentTableName != TableName + "_parent") throw new InvalidOperationException("父表清理范围错误。");
                drop.CommandText = "DROP TABLE IF EXISTS " + Session.Provider.QuoteIdentifier(ParentTableName); await drop.ExecuteNonQueryAsync();
            }
            foreach(var table in CrossTables.Reverse())if(createdTables.Contains(table)){drop.CommandText="DROP TABLE "+Session.Provider.QuoteIdentifier(table);await drop.ExecuteNonQueryAsync();}
            if(CrossTables.Count>0&&createdTables.Count>0)
            {
                await using var independent=Session.Provider.CreateConnection(Session.Options);await independent.OpenAsync();
                foreach(var table in createdTables){await using var command=independent.CreateCommand();command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=@name";var name=command.CreateParameter();name.ParameterName="name";name.Value=table;command.Parameters.Add(name);Assert.Equal(0,Convert.ToInt32(await command.ExecuteScalarAsync()));}
                await File.WriteAllTextAsync(Path.Combine(CrossEvidence,"cleanup.json"),JsonSerializer.Serialize(new{table=TableName,names=createdTables.Order().ToArray(),fileIds=FileIds.ToArray(),recordIds=RecordIds.ToArray(),verified=CrossVerified,crossType=CrossType,instanceVerified=true,independentTableAbsenceChecked=true})+"\n");
            }
            cleaned=true;
        }
        catch(Exception error)
        {
            try{await PersistCleanupRecoveryAsync(error);}
            catch(Exception evidence){throw new AggregateException("测试清理与恢复清单保存同时失败，首因保留。",error,evidence);}
            throw;
        }
        finally
        {
            Session?.Dispose();
            var local = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".local")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(DirectoryPath);
            if (cleaned && target.StartsWith(local, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target)) Directory.Delete(target, true);
        }
    }
    /// <summary>清理失败保留本夹具明确拥有的ID和表名，禁止记录连接串、文件内容或原始SQL异常正文。</summary>
    private async Task PersistCleanupRecoveryAsync(Exception error)
    {
        if(string.IsNullOrEmpty(DirectoryPath))return;
        var local=Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),".local"))+Path.DirectorySeparatorChar;
        var target=Path.GetFullPath(DirectoryPath);
        if(!target.StartsWith(local,StringComparison.OrdinalIgnoreCase)||!Directory.Exists(target))throw new InvalidOperationException("测试恢复清单目录越界或不存在。");
        var recovery=new{schemaVersion=1,database=Session.Options.ExpectedDatabase,table=TableName,parentTable=ParentTableName,
            createdTables=createdTables.Order(StringComparer.Ordinal).ToArray(),fileIds=FileIds.ToArray(),folderIds=FolderIds.ToArray(),recordIds=RecordIds.ToArray(),
            errorType=error.GetType().FullName,errorNumber=(error as MySqlConnector.MySqlException)?.Number,createdAtUtc=DateTimeOffset.UtcNow};
        await File.WriteAllTextAsync(Path.Combine(target,"cleanup-recovery.json"),JsonSerializer.Serialize(recovery,new JsonSerializerOptions{WriteIndented=true})+"\n");
    }
    /// <summary>只清理列表中明确登记的单个ID，所有值仍通过驱动参数绑定。</summary>
    private static async Task ExecuteAsync(DbConnection connection, string sql, long id)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        var parameter = command.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = id; command.Parameters.Add(parameter); await command.ExecuteNonQueryAsync();
    }
}

internal sealed class TestAuthorization(RuntimeActor actor, MetadataRegistry registry, IReadOnlySet<string>? operations = null) : IRuntimeAuthorization
{
    public RuntimeActor RequireActor() => actor;
    public void RequirePagePermission(string pageId, string operation)
    {
        var page = registry.FindPage(pageId);
        if (operations is not null && !operations.Contains(operation)) throw new UnauthorizedAccessException("测试身份缺少当前操作权限。");
        if (page is null || page.YzProjectId != actor.ProjectId || operation is not ("create" or "update" or "delete" or "detail" or "list")) throw new UnauthorizedAccessException("测试身份无该页面权限。");
    }
}
