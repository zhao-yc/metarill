using System.Data;
using System.Data.Common;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Files;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;

namespace Metarill.Files.Tests;

/// <summary>实际调用文件浏览Service，以明确替身验证适配器路由、原事务和连接所有权；不冒充实库权限验收。</summary>
public sealed class FileCommandFactoryTests
{
    /// <summary>原文件库的名称和目录拒绝必须回滚并保留可返回页面的业务提示。</summary>
    [Theory]
    [InlineData("create-missing", "文件夹名称不能为空")]
    [InlineData("create-blank", "文件夹名称不能为空")]
    [InlineData("rename-long-folder", "文件夹名称不能超过100个字符")]
    [InlineData("rename-missing-file", "文件名称不能为空")]
    [InlineData("rename-blank-file", "文件名称不能为空")]
    [InlineData("rename-long-file", "文件名称不能超过255个字符")]
    [InlineData("duplicate", "同级目录已存在相同名称")]
    [InlineData("delete-child", "文件夹下存在子文件夹，不能删除")]
    [InlineData("delete-file", "文件夹下存在文件，不能删除")]
    [InlineData("move-self", "不能移动到自身下")]
    [InlineData("move-child", "不能移动到自己的子文件夹下")]
    [InlineData("move-cycle", "目标文件夹层级异常")]
    public async Task Original_file_management_failures_are_displayable_and_roll_back(string scenario, string message)
    {
        const long folderId = 41;
        const long childId = 42;
        const long fileId = 43;
        var catalog = new MetadataCatalog { YzProjectId = "1" };
        var infrastructurePath = Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../metadata/files.json");
        var infrastructure = System.Text.Json.JsonSerializer.Deserialize<MetadataCatalog>(
            File.ReadAllText(infrastructurePath), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var provider = new Provider { Read = Read };
        using var session = new DatabaseSession(
            new() { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused" },
            null, provider);
        var files = new FileLibraryService(new SqlRowStore(session, MetadataCatalogComposition.MergePhysical(catalog, infrastructure)),
            new FileMetadataContext([catalog]), new SnowflakeIdGenerator(29), new Authorization(), new FileLibraryOptions());

        Func<Task> action = scenario switch
        {
            "create-missing" => () => files.CreateFolderAsync(null),
            "create-blank" => () => files.CreateFolderAsync(" "),
            "rename-long-folder" => () => files.RenameFolderWithResultAsync(folderId, new string('长', 101)),
            "rename-missing-file" => () => files.RenameWithResultAsync(fileId, null),
            "rename-blank-file" => () => files.RenameWithResultAsync(fileId, " "),
            "rename-long-file" => () => files.RenameWithResultAsync(fileId, new string('长', 256)),
            "duplicate" => () => files.CreateFolderAsync(" 同名 "),
            "delete-child" or "delete-file" => () => files.DeleteFolderAsync(folderId),
            "move-self" => () => files.MoveFolderWithResultAsync(folderId, folderId),
            _ => () => files.MoveFolderWithResultAsync(folderId, childId)
        };

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(action);
        Assert.Equal(message, failure.Message);
        Assert.Equal(0, provider.Last!.Commits);
        Assert.Equal(1, provider.Last.Rollbacks);

        DbDataReader Read(DbCommand command)
        {
            // 分别模拟已在原范围内读到的父目录、同级和文件，无需自造另一套事务逻辑。
            var rows = new DataTable();
            rows.Columns.Add("id", typeof(long));
            rows.Columns.Add("yz_project_id", typeof(long));
            rows.Columns.Add("name", typeof(string));
            rows.Columns.Add("parent_folder_id", typeof(long));
            rows.Columns.Add("status", typeof(string));
            if (command.CommandText.Contains("WHERE `id`=@p0", StringComparison.Ordinal)
                || command.CommandText.Contains("AND `id`=@p0", StringComparison.Ordinal))
            {
                var id = Convert.ToInt64(command.Parameters["p0"].Value);
                long? parentId = id == childId ? scenario == "move-cycle" ? childId : folderId : null;
                rows.Rows.Add(id, 1L, "目录", parentId is { } parent ? parent : DBNull.Value, DBNull.Value);
            }
            else if (scenario == "duplicate" && command.CommandText.Contains("`name`=@p1", StringComparison.Ordinal))
                rows.Rows.Add(folderId, 1L, "同名", DBNull.Value, DBNull.Value);
            else if (scenario == "delete-child" && command.CommandText.Contains("`parent_folder_id`=@p0", StringComparison.Ordinal))
                rows.Rows.Add(childId, 1L, "子文件夹", folderId, DBNull.Value);
            else if (scenario == "delete-file" && command.CommandText.Contains("FROM `yz_file_library`", StringComparison.Ordinal))
                rows.Rows.Add(fileId, 1L, "业务文件", DBNull.Value, "ready");
            return rows.CreateDataReader();
        }
    }

    /// <summary>上传计划的可纠正输入错误必须返回固定提示，并且不写入草稿。</summary>
    [Theory]
    [InlineData(null, 1, null, "文件信息不能为空")]
    [InlineData(" ", 1, null, "文件信息不能为空")]
    [InlineData("../invalid.txt", 1, null, "文件名无效，不允许路径或控制字符。")]
    [InlineData("document.txt", -1, null, "文件大小超过字段限制")]
    [InlineData("document.txt", 1_048_577, null, "文件大小超过字段限制")]
    [InlineData("document.html", 1, null, "文件后缀不在字段允许范围内")]
    [InlineData("document.txt", 1, "bad", "SHA-256 摘要格式无效。")]
    public async Task Original_upload_plan_exposes_caller_errors_without_creating_a_draft(
        string? name, long size, string? digest, string message)
    {
        var catalog = new MetadataCatalog { YzProjectId = "1" };
        var provider = new Provider();
        using var session = new DatabaseSession(
            new() { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused" },
            null, provider);
        var files = new FileLibraryService(new SqlRowStore(session, catalog), new FileMetadataContext([catalog]),
            new SnowflakeIdGenerator(29), new Authorization(), new FileLibraryOptions { MaxFileBytes = 1_048_576 });

        var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => files.PlanUploadAsync(new(name, null, size, Sha256: digest)));
        Assert.Equal(message, failure.Message);
        Assert.Equal(0, provider.Commands);
        Assert.Equal(0, provider.Last!.Commits);
        Assert.Equal(1, provider.Last.Rollbacks);
    }

    private const string SizeLimitError = "文件大小超过字段限制";
    private const string SuffixLimitError = "文件后缀不在字段允许范围内";

    /// <summary>上传计划按原文件控件的默认和显式大小、后缀校验，服务器上限仍然优先。</summary>
    [Theory]
    [InlineData("", true, 100, 50, 0, "plan.txt", null)]
    [InlineData("", true, 100, 50, 1, "plan.txt", SizeLimitError)]
    [InlineData("{}", true, 100, 50, 1, "plan.txt", SizeLimitError)]
    [InlineData("{\"maxSizeMb\":null}", true, 100, 50, 1, "plan.txt", SizeLimitError)]
    [InlineData("{\"maxSizeMb\":80}", true, 100, 80, 0, "plan.txt", null)]
    [InlineData("{\"maxSizeMb\":80}", true, 100, 80, 1, "plan.txt", SizeLimitError)]
    [InlineData("{\"maxSizeMb\":80}", true, 60, 60, 1, "plan.txt", SizeLimitError)]
    [InlineData("", true, 30, 30, 1, "plan.txt", SizeLimitError)]
    [InlineData("", false, 100, 80, 0, "plan.txt", null)]
    [InlineData("{\"extensions\":[\" TXT \",\" .PDF \"]}", true, 100, 1, 0, "plan.TXT", null)]
    [InlineData("{\"extensions\":[\" TXT \",\" .PDF \"]}", true, 100, 1, 0, "plan.pdf", null)]
    [InlineData("{\"extensions\":[\" TXT \",\" .PDF \"]}", true, 100, 1, 0, "plan.csv", SuffixLimitError)]
    [InlineData("{\"extensions\":[\"txt\",\" .EXE \"]}", true, 100, 1, 0, "plan.exe", SuffixLimitError)]
    public async Task Web_upload_plan_shares_original_file_field_limits(
        string detail, bool published, int hostLimit, int sizeMb, int extraBytes, string fileName, string? expectedError)
    {
        const long mib = 1024L * 1024L;
        var table = new TableMetadata { Id = "10", TableName = "attachment_form", IsFunctionTable = 1 };
        var page = new PageMetadata { Id = "20", ConfigTableId = table.Id, YzAdminTypeId = "3" };
        var field = new ColumnMetadata
        {
            Id = "14", ConfigTableId = table.Id, ColumnName = "attachments", ControlKind = "FileLibrary",
            LogicalType = "string", IsVirtualColumn = 1, ControlDetail = detail
        };
        var catalog = new MetadataCatalog
        {
            YzProjectId = "1", ConfigTables = [table], ConfigPages = [page], ConfigColumns = [field],
            ConfigPageColumns = [new() { Id = "21", ConfigPageId = page.Id, ConfigColumnId = field.Id, IsEdit = 1 }],
            YzAdminTypes = [new() { Id = "3" }]
        };
        var infrastructurePath = Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../metadata/files.json");
        var infrastructure = System.Text.Json.JsonSerializer.Deserialize<MetadataCatalog>(
            File.ReadAllText(infrastructurePath), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var writes = new List<string>();
        var provider = new Provider { Write = command => { writes.Add(command.CommandText); return 1; } };
        using var session = new DatabaseSession(
            new() { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused" },
            null, provider);
        var files = new FileLibraryService(new SqlRowStore(session, MetadataCatalogComposition.MergePhysical(catalog, infrastructure)),
            new FileMetadataContext([catalog]), new SnowflakeIdGenerator(29), new FunctionFileAuthorization("upload"),
            new FileLibraryOptions { MaxFileBytes = hostLimit * mib });
        var request = new FileUploadRequest(fileName, null, sizeMb * mib + extraBytes, published ? field.Id : null);

        if (expectedError is null)
        {
            Assert.True((await files.PlanUploadAsync(request)).FileId > 0);
            Assert.Contains("INSERT INTO `yz_file_library`", Assert.Single(writes));
            Assert.Equal(1, provider.Last!.Commits);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<BusinessRuleException>(() => files.PlanUploadAsync(request));
            Assert.Equal(expectedError, failure.Message);
            Assert.Empty(writes);
            Assert.Equal(1, provider.Last!.Rollbacks);
        }
    }

    /// <summary>已有附件按历史字段规则验证，显式大小继续限制引用。</summary>
    [Theory]
    [InlineData("{}", 100)]
    [InlineData("{\"maxSizeMb\":null}", 100)]
    [InlineData("{\"maxSizeMb\":60}", 60)]
    public void Existing_file_reference_keeps_the_previous_size_policy(string detail, int expectedMb)
    {
        const long mib = 1024L * 1024L;
        var column = new ColumnMetadata { ControlDetail = detail };
        var options = new FileLibraryOptions { MaxFileBytes = 100L * mib };

        Assert.Equal(expectedMb * mib, FileValidation.Policy(column, options).MaximumBytes);
    }

    /// <summary>使用真实本地流和文件事务，检查可重试上传的原提示、失败清理及权限错误分类。</summary>
    [Theory]
    [InlineData("expired", "上传计划已过期。")]
    [InlineData("expired-direct", "上传计划已过期。")]
    [InlineData("short", "实际文件大小与上传计划不一致")]
    [InlineData("long", "实际文件大小与上传计划不一致")]
    [InlineData("hash", "上传内容摘要与计划不一致。")]
    [InlineData("complete-before-upload", "文件尚未上传完成")]
    [InlineData("wrong-channel", "当前文件须通过已签发的直传表单上传。")]
    [InlineData("already-uploaded", "文件状态不允许重新上传。")]
    [InlineData("invalid-deadline", "上传计划期限无效。")]
    [InlineData("other-owner", "文件或目录不存在或不可访问。")]
    public async Task Original_upload_retries_preserve_actionable_messages_and_rollback(string scenario, string message)
    {
        var key = Guid.NewGuid().ToString("N") + ".blob";
        var root = Path.Combine(Path.GetTempPath(), "metarill-file-upload-" + Guid.NewGuid().ToString("N"));
        var catalog = new MetadataCatalog { YzProjectId = "1" };
        var infrastructurePath = Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../metadata/files.json");
        var infrastructure = System.Text.Json.JsonSerializer.Deserialize<MetadataCatalog>(
            File.ReadAllText(infrastructurePath), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var writes = new List<string>();
        var provider = new Provider
        {
            Write = command => { writes.Add(command.CommandText); return 1; },
            Read = _ =>
            {
                var row = new DataTable();
                row.Columns.Add("id", typeof(long));
                row.Columns.Add("owner_id", typeof(long));
                row.Columns.Add("yz_project_id", typeof(long));
                row.Columns.Add("status", typeof(string));
                row.Columns.Add("storage_key", typeof(string));
                row.Columns.Add("size", typeof(long));
                row.Columns.Add("sha256", typeof(string));
                row.Columns.Add(FileUploadLifetime.DeadlineColumn, typeof(object));
                row.Rows.Add(41L, scenario == "other-owner" ? 3L : 2L, 1L, scenario == "already-uploaded" ? "uploaded" : "planned",
                    scenario is "wrong-channel" or "expired-direct" ? "s3:profile:object:direct" : key, 2L, scenario == "hash" ? new string('0', 64) : "",
                    scenario == "invalid-deadline" ? "invalid" : DateTime.UtcNow.AddMinutes(scenario is "expired" or "expired-direct" ? -5 : 5));
                return row.CreateDataReader();
            }
        };

        using var session = new DatabaseSession(
            new() { ExpectedDatabase = "offline", ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused" },
            null, provider);
        var files = new FileLibraryService(new SqlRowStore(session, MetadataCatalogComposition.MergePhysical(catalog, infrastructure)),
            new FileMetadataContext([catalog]), new SnowflakeIdGenerator(29), new Authorization(),
            new FileLibraryOptions { RootDirectory = root });
        var writeAttempt = scenario is "short" or "long" or "hash";
        try
        {
            using var stream = new MemoryStream(scenario switch { "short" => [1], "long" => [1, 2, 3], _ => [1, 2] });
            Func<Task> action = scenario is "complete-before-upload" or "expired-direct" ? () => files.CompleteAsync(41) : () => files.UploadAsync(41, stream);
            var failure = scenario switch
            {
                "invalid-deadline" => (Exception)await Assert.ThrowsAsync<InvalidOperationException>(action),
                "other-owner" => await Assert.ThrowsAsync<UnauthorizedAccessException>(action),
                _ => await Assert.ThrowsAsync<BusinessRuleException>(action)
            };
            Assert.Equal(message, failure.Message);
            Assert.Equal(0, provider.Last!.Commits);
            Assert.Equal(1, provider.Last.Rollbacks);
            if (writeAttempt) Assert.Contains("UPDATE `yz_file_library`", Assert.Single(writes));
            else Assert.Empty(writes);
            if (Directory.Exists(root)) Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }

    /// <summary>无事务自管连接、有事务复用连接；成功、驱动异常和取消均只执行一次原查询。</summary>
    [Theory]
    [InlineData(false,0)][InlineData(true,0)][InlineData(false,1)][InlineData(true,1)][InlineData(false,2)][InlineData(true,2)]
    public async Task Browsing_uses_provider_factory_and_preserves_transaction_and_failure(bool transactional,int failure)
    {
        using var cancellation=new CancellationTokenSource();var expected=failure switch{1=>new InvalidOperationException("原驱动失败"),2=>(Exception)new OperationCanceledException(cancellation.Token),_=>null};
        var provider=new Provider{Failure=expected};var options=new DatabaseOptions{ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused",ExpectedDatabase="offline"};
        using var session=new DatabaseSession(options,null,provider);var catalog=new MetadataCatalog{YzProjectId="1"};
        var service=new FileLibraryService(new SqlRowStore(session,catalog),new FileMetadataContext([catalog]),new SnowflakeIdGenerator(29),new Authorization(),new FileLibraryOptions());
        async Task<int> Work()
        {
            var transaction=session.CurrentTransaction;
            if(expected is null)Assert.Empty(await service.ListAsync(42,cancellation.Token));
            else{var actual=await Record.ExceptionAsync(()=>service.ListAsync(42,cancellation.Token));Assert.Same(expected,actual);throw actual!;}
            Assert.Same(transaction,provider.Last!.Command!.Transaction);Assert.False(provider.Last.IsDisposed);return 1;
        }
        if(transactional)
        {
            if(expected is null)await session.InTransactionAsync(Work);
            else Assert.Same(expected,await Record.ExceptionAsync(()=>session.InTransactionAsync(Work)));
        }
        else if(expected is null)await service.ListAsync(42,cancellation.Token);
        else Assert.Same(expected,await Record.ExceptionAsync(()=>service.ListAsync(42,cancellation.Token)));
        var connection=Assert.IsType<Connection>(provider.Last);var command=Assert.IsType<Command>(connection.Command);
        Assert.Equal(1,provider.Commands);Assert.Equal(1,provider.Connections);Assert.Equal(1,connection.Opens);Assert.Equal(1,command.Executions);Assert.True(connection.IsDisposed);Assert.True(command.IsDisposed);
        Assert.Same(connection,command.Connection);Assert.Equal(transactional,command.Transaction is not null);
        Assert.Equal(transactional&&expected is null?1:0,connection.Commits);Assert.Equal(transactional&&expected is not null?1:0,connection.Rollbacks);
        Assert.Equal(42L,command.Parameters["browse_folder"].Value);Assert.Equal(1L,command.Parameters["browse_project"].Value);Assert.Equal("deleted",command.Parameters["deleted_status"].Value);
        Assert.Contains("`yz_file_library`",command.CommandText);Assert.Contains("`folder_id`=@browse_folder",command.CommandText);Assert.DoesNotContain("42",command.CommandText);Assert.Equal(cancellation.Token,command.Token);
    }
    /// <summary>原功能表的浏览和上传计划使用实际页面权限，无记录时不能创建文件引用。</summary>
    [Theory]
    [InlineData("upload",true,true)]
    [InlineData("readonly",true,false)]
    [InlineData("readOnlyField",true,false)]
    [InlineData("legacyDetailFlag",true,true)]
    [InlineData("createOnly",false,false)]
    [InlineData("hidden",false,false)]
    [InlineData("foreign",false,false)]
    [InlineData("ordinaryVirtual",false,true)]
    public async Task Function_form_attachment_uses_original_file_endpoints_and_no_record_binding(string kind,bool browse,bool upload)
    {
        var table=new TableMetadata { Id="10", TableName="attachment_form", IsFunctionTable=kind=="ordinaryVirtual"?0:1 };
        var page=new PageMetadata { Id="20",ConfigTableId="10",YzAdminTypeId="3" };
        var field=new ColumnMetadata { Id="14",ConfigTableId="10",ColumnName="attachments",ControlKind="FileLibrary",
            LogicalType="string",IsVirtualColumn=1,ControlDetail="""{"accessMode":"PROTECTED","extensions":["txt"],"maxCount":2}""" };
        var link=new PageColumnMetadata { Id="21",ConfigPageId="20",ConfigColumnId="14",IsEdit=1 };
        if(kind is "hidden" or "legacyDetailFlag")link.IsEdit=0;
        if(kind=="legacyDetailFlag")link.Extra["isInEdit"]=System.Text.Json.JsonSerializer.SerializeToElement(1);
        if(kind=="readOnlyField")link.Extra["isReadOnly"]=System.Text.Json.JsonSerializer.SerializeToElement(1);
        if(kind=="foreign")page.YzProjectId="2";
        var catalog=new MetadataCatalog { YzProjectId="1",ConfigTables=[table],ConfigPages=[page],
            ConfigColumns=[new(){Id="11",ConfigTableId="10",ColumnName="id",LogicalType="int64"},field],
            ConfigPageColumns=[link],YzAdminTypes=[new(){Id="3"}] };
        var registry=new FileMetadataContext([catalog]);
        var snapshot=Assert.Single(registry.Catalogs);
        if(table.IsFunctionTable==1) Assert.Empty(registry.PhysicalColumns(snapshot, Assert.Single(snapshot.ConfigTables)));

        // 计划仍使用标准文件基础设施，连接替身仅捕获真实参数，不接触数据库或存储对象。
        var source=Path.Combine(Path.GetDirectoryName(SourcePath())!,"../../metadata/files.json");
        var infrastructure=System.Text.Json.JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(source),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"},null,provider);
        var auth=new FunctionFileAuthorization(kind);
        var service=new FileLibraryService(new SqlRowStore(session,MetadataCatalogComposition.MergePhysical(catalog,infrastructure)),
            registry,new SnowflakeIdGenerator(29),auth,new FileLibraryOptions());
        if(browse)
        {
            Assert.Empty(await service.ListAccessibleAsync(field.Id,null,null));
            var command=Assert.IsType<Command>(provider.Last!.Command);
            Assert.Equal("PROTECTED",command.Parameters["browse_mode"].Value);
            Assert.Equal(1L,command.Parameters["browse_project"].Value);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.ListAccessibleAsync(field.Id,null,null));
            Assert.Equal(0,provider.Commands);
        }
        var writes=new List<string>();
        provider.Write=command=>
        {
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.Contains("INSERT INTO `yz_file_library`",command.CommandText);
            Assert.Contains(command.Parameters.Cast<DbParameter>(),item=>Equals(item.Value,"PROTECTED"));
            Assert.Contains(command.Parameters.Cast<DbParameter>(),item=>Equals(item.Value,2L));
            writes.Add(command.CommandText);
            return 1;
        };
        var request=new FileUploadRequest("合成.txt","text/plain",61,field.Id);
        if(upload)
        {
            var plan=await service.PlanUploadAsync(request);
            Assert.Equal("SERVER_MULTIPART",plan.Plan.UploadMode);
            Assert.EndsWith("/"+plan.FileId,plan.Plan.UploadUrl);
            Assert.Single(writes);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.PlanUploadAsync(request));
            Assert.Empty(writes);
        }
        if(table.IsFunctionTable==1)Assert.DoesNotContain("create",auth.Operations);
        var before=provider.Commands;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.SynchronizeReferencesAsync(
            new(page.Id,table.Id,field.Id,table.TableName,"9",null,field.ColumnName,["8"])));
        Assert.Equal(before,provider.Commands);
    }

    /// <summary>报错仍保留原页面操作权限，测试区分只读、可写及只获旧新增权。</summary>
    private sealed class FunctionFileAuthorization(string kind):IRuntimeAuthorization
    {
        internal List<string> Operations { get; } = [];
        public RuntimeActor RequireActor()=>new(2,"测试",false,"1","3");
        public void RequirePagePermission(string pageId,string operation)
        {
            Assert.Equal("20",pageId);
            Operations.Add(operation);
            if(kind=="createOnly"?operation!="create":operation==(kind=="readonly"?"update":"delete"))
                throw new UnauthorizedAccessException();
        }
    }

    /// <summary>从固定源码目录使用现有文件库基础设施模型。</summary>
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath]string path="")=>path;

    /// <summary>从已发布元数据解析原请求，在同一实际事务核对记录及修改权限后同步已保存的文件 ID。</summary>
    [Theory]
    [InlineData("allowed")]
    [InlineData("wrong-field")]
    [InlineData("legacy-disabled")]
    [InlineData("missing-scope")]
    [InlineData("wrong-values")]
    [InlineData("exact-allowed")]
    [InlineData("exact-denied")]
    public async Task Original_reference_sync_only_reconciles_the_persisted_authorized_file_field(string mode)
    {
        const long recordId = 9007199254740993;
        const long fileId = 9007199254740994;
        var catalog = new MetadataCatalog
        {
            YzProjectId = "1",
            ConfigTables =
            [
                new() { Id = "10", TableName = "sync_record" },
                new() { Id = "40", TableName = "yz_file_ref" },
                new() { Id = "50", TableName = "yz_file_library" }
            ],
            ConfigPages = [new() { Id = "20", ConfigTableId = "10", YzAdminTypeId = "3", WhereSql = "yz_project_id=@current_yz_project_id AND status=1" }],
            YzAdminTypes = [new() { Id = "3" }],
            ConfigColumns =
            [
                new() { Id = "11", ConfigTableId = "10", ColumnName = "id", LogicalType = "int64" },
                new() { Id = "12", ConfigTableId = "10", ColumnName = "yz_project_id", LogicalType = "int64" },
                new() { Id = "13", ConfigTableId = "10", ColumnName = "status", LogicalType = "int32" },
                new() { Id = "14", ConfigTableId = "10", ColumnName = "attachments", LogicalType = "string",
                    ControlKind = "FileLibrary", ControlDetail = """{"accessMode":"PUBLIC","maxCount":2,"allowedExtensions":[".txt"]}""" }
            ],
            ConfigPageColumns = [new() { Id = "21", ConfigPageId = "20", ConfigColumnId = "14" }]
        };
        static void AddColumns(MetadataCatalog catalog, string tableId, params string[] names)
        {
            for (var i = 0; i < names.Length; i++)
                catalog.ConfigColumns.Add(new() { Id = tableId + (i + 1).ToString(), ConfigTableId = tableId, ColumnName = names[i],
                    LogicalType = names[i] switch
                    {
                        "insert_time" or "delete_no" => "datetime",
                        "field_name" or "status" or "name" or "access_mode" => "string",
                        _ => "int64"
                    } });
        }
        AddColumns(catalog, "40", "id", "file_id", "page_id", "record_id", "field_name", "yz_project_id", "insert_time", "insert_manager_id", "delete_no");
        AddColumns(catalog, "50", "id", "yz_project_id", "status", "name", "size", "access_mode", "owner_id", "delete_no");
        if (mode == "legacy-disabled") catalog.ConfigPages[0].Extra["isUpdate"] = System.Text.Json.JsonSerializer.SerializeToElement(0);
        if (mode is "exact-allowed" or "exact-denied")
        {
            catalog.ConfigPages[0].Extra["exactDataManageMatchField"] = System.Text.Json.JsonSerializer.SerializeToElement("status");
            catalog.YzAdminTypes[0].Extra["exactDataManageMatchField"] = System.Text.Json.JsonSerializer.SerializeToElement("department_id");
        }

        var provider = new Provider();
        var options = new DatabaseOptions { ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused", ExpectedDatabase = "offline" };
        using var session = new DatabaseSession(options, null, provider);
        var authorization = new ReferenceSyncAuthorization(mode == "exact-allowed");
        var files = new FileLibraryService(new SqlRowStore(session, catalog), new FileMetadataContext([catalog]),
            new SnowflakeIdGenerator(29), authorization, new FileLibraryOptions());
        var mutations = new List<string>();
        var filters = new List<DbCommand>();
        var requests = new FileReferenceSyncRequest("20", "10", "14", "sync_record", recordId.ToString(), null,
            mode == "wrong-field" ? "other_field" : "attachments", [mode == "wrong-values" ? "1" : fileId.ToString()]);

        provider.Read = command =>
        {
            Assert.Same(session.CurrentTransaction, command.Transaction);
            var sql = command.CommandText;
            if (sql.Contains("FROM `sync_record`", StringComparison.Ordinal))
            {
                var table = new DataTable();
                table.Columns.Add("id", typeof(long)); table.Columns.Add("yz_project_id", typeof(long));
                table.Columns.Add("attachments", typeof(string)); table.Columns.Add("status", typeof(int));
                if (sql.Contains("file_page", StringComparison.Ordinal)) filters.Add(command);
                if (mode != "missing-scope" || !sql.Contains("file_page", StringComparison.Ordinal))
                    table.Rows.Add(recordId, 1L, "[\"" + fileId + "\"]", 1);
                return table.CreateDataReader();
            }
            if (sql.Contains("FROM `yz_file_library`", StringComparison.Ordinal))
            {
                var table = new DataTable();
                table.Columns.Add("id", typeof(long)); table.Columns.Add("yz_project_id", typeof(long));
                table.Columns.Add("status", typeof(string)); table.Columns.Add("name", typeof(string));
                table.Columns.Add("size", typeof(long)); table.Columns.Add("access_mode", typeof(string));
                table.Rows.Add(fileId, 1L, "ready", "document.txt", 3L, "PUBLIC");
                return table.CreateDataReader();
            }
            Assert.Contains("FROM `yz_file_ref`", sql);
            return new DataTable().CreateDataReader();
        };
        provider.Write = command =>
        {
            Assert.Same(session.CurrentTransaction, command.Transaction);
            mutations.Add(command.CommandText);
            Assert.Contains(command.Parameters.Cast<DbParameter>(), parameter => Equals(parameter.Value, fileId));
            Assert.Contains(command.Parameters.Cast<DbParameter>(), parameter => Equals(parameter.Value, recordId));
            return 1;
        };

        if (mode is "allowed" or "exact-allowed")
        {
            await files.SynchronizeReferencesAsync(requests);
            Assert.Contains("INSERT INTO `yz_file_ref`", Assert.Single(mutations));
            Assert.Equal(1, provider.Last!.Commits);
        }
        else
        {
            var failure = await Record.ExceptionAsync(() => files.SynchronizeReferencesAsync(requests));
            Assert.NotNull(failure);
            Assert.Empty(mutations);
        }
        if (mode is "wrong-field" or "legacy-disabled") Assert.Equal(0, provider.Connections);
        else if (mode == "exact-denied")
        {
            Assert.Empty(filters);
            Assert.Equal(1, provider.Last!.Rollbacks);
        }
        else
        {
            var scopeCommand = Assert.Single(filters);
            Assert.Contains("FOR UPDATE", scopeCommand.CommandText);
            Assert.Contains("update", authorization.Operations);
            if (mode == "exact-allowed") Assert.Contains("`status` IN (@exact_0)", scopeCommand.CommandText);
        }
        Assert.Equal(mode is "exact-allowed" or "exact-denied" ? 1 : 0, authorization.ExactCalls);
    }

    /// <summary>保护文件读取和原引用同步核对同一个实际存储父键，文本父键必须逐字对应真实父记录。</summary>
    [Theory]
    [InlineData("string", false, "identity")]
    [InlineData("text", false, "identity")]
    [InlineData("varchar", false, "identity")]
    [InlineData("string", true, "identity")]
    [InlineData("int64", false, "identity")]
    [InlineData("string", false, "current")]
    [InlineData("int64", false, "snapshot")]
    [InlineData("string", false, "nested-snapshot")]
    public async Task File_read_and_reference_sync_share_project_parent_identity(string type, bool noncanonical, string mode)
    {
        const long parentId = 9007199254740993;
        const long recordId = 71;
        const long fileId = 81;
        var parentText = parentId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var storedParent = noncanonical ? "0" + parentText : parentText;
        var catalog = new MetadataCatalog
        {
            YzProjectId = "1",
            ConfigTables =
            [
                new() { Id = "10", TableName = "child_record" },
                new() { Id = "30", TableName = "parent_record" }
            ],
            ConfigPages = [new() { Id = "20", ConfigTableId = "10", YzAdminTypeId = "3", WhereSql = "parent_id=@parentId AND yz_project_id=@current_yz_project_id" }],
            YzAdminTypes = [new() { Id = "3" }],
            ConfigColumns =
            [
                new() { Id = "11", ConfigTableId = "10", ColumnName = "id", LogicalType = "int64" },
                new() { Id = "12", ConfigTableId = "10", ColumnName = "yz_project_id", LogicalType = "int64" },
                new() { Id = "13", ConfigTableId = "10", ColumnName = "parent_id", LogicalType = type, LinkConfigTableId = "30" },
                new() { Id = "14", ConfigTableId = "10", ColumnName = "attachments", LogicalType = "string",
                    ControlKind = "FileLibrary", ControlDetail = """{"accessMode":"PROTECTED","maxCount":2,"allowedExtensions":[".txt"]}""" },
                new() { Id = "31", ConfigTableId = "30", ColumnName = "id", LogicalType = "int64" },
                new() { Id = "32", ConfigTableId = "30", ColumnName = "yz_project_id", LogicalType = "int64" }
            ],
            ConfigPageColumns = [new() { Id = "21", ConfigPageId = "20", ConfigColumnId = "14" }]
        };
        var source = Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../metadata/files.json");
        var infrastructure = System.Text.Json.JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(source),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var registry = new FileMetadataContext([catalog]);
        var provider = new Provider();
        using var session = new DatabaseSession(new()
        {
            ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused"
        }, null, provider);
        var store = new SqlRowStore(session, MetadataCatalogComposition.MergePhysical(catalog, infrastructure));
        var authorization = new ReferenceSyncAuthorization(false);
        var files = new FileLibraryService(store, registry, new SnowflakeIdGenerator(29), authorization, new FileLibraryOptions());
        var synchronizing = false;
        var writes = 0;
        var parentChecks = 0;
        var authorizationReadCount = 0;
        DbTransaction? authorizationTransaction = null;
        void AssertAuthorizationRead(DbCommand command)
        {
            if (mode == "identity" || synchronizing) return;
            Assert.Equal(mode == "current", command.CommandText.EndsWith("FOR UPDATE", StringComparison.Ordinal));
            Assert.NotNull(command.Transaction);
            authorizationTransaction ??= command.Transaction;
            Assert.Same(authorizationTransaction, command.Transaction);
            authorizationReadCount++;
        }
        provider.Scalar = command =>
        {
            Assert.Same(session.CurrentTransaction, command.Transaction);
            if (command.CommandText.Contains("FROM `yz_file_ref`", StringComparison.Ordinal))
            {
                Assert.True(synchronizing);
                Assert.Equal(fileId, command.Parameters["history_file"].Value);
                return null;
            }
            Assert.Contains("FROM `parent_record`", command.CommandText);
            Assert.Contains(command.Parameters.Cast<DbParameter>(), parameter => Equals(parameter.Value, parentId));
            Assert.Contains(command.Parameters.Cast<DbParameter>(), parameter => Equals(parameter.Value, 1L));
            AssertAuthorizationRead(command);
            if (mode == "identity" || synchronizing)
                Assert.Equal(synchronizing, command.CommandText.EndsWith("FOR UPDATE", StringComparison.Ordinal));
            parentChecks++;
            return parentId;
        };
        provider.Read = command =>
        {
            var data = new DataTable();
            var sql = command.CommandText;
            AssertAuthorizationRead(command);
            if (sql.Contains("FROM `child_record`", StringComparison.Ordinal))
            {
                data.Columns.Add("id", typeof(long));
                data.Columns.Add("yz_project_id", typeof(long));
                data.Columns.Add("parent_id", type == "int64" ? typeof(long) : typeof(string));
                data.Columns.Add("attachments", typeof(string));
                data.Rows.Add(recordId, 1L, type == "int64" ? (object)parentId : storedParent, "[\"81\"]");
            }
            else if (sql.Contains("FROM `yz_file_library`", StringComparison.Ordinal))
            {
                data.Columns.Add("id", typeof(long));
                data.Columns.Add("yz_project_id", typeof(long));
                data.Columns.Add("status", typeof(string));
                data.Columns.Add("name", typeof(string));
                data.Columns.Add("size", typeof(long));
                data.Columns.Add("access_mode", typeof(string));
                data.Columns.Add("owner_id", typeof(long));
                data.Rows.Add(fileId, 1L, "ready", "sample.txt", 3L, "PROTECTED", 2L);
            }
            else
            {
                Assert.Contains("FROM `yz_file_ref`", sql);
                data.Columns.Add("record_id", typeof(long));
                data.Columns.Add("field_name", typeof(string));
                if (!synchronizing) data.Rows.Add(recordId, "attachments");
            }
            return data.CreateDataReader();
        };
        provider.Write = command =>
        {
            Assert.Contains("INSERT INTO `yz_file_ref`", command.CommandText);
            Assert.Same(session.CurrentTransaction, command.Transaction);
            writes++;
            return 1;
        };

        Task<bool> CanRead() => FileReferenceReadAuthorization.CanReadAsync(store, registry, authorization, fileId,
            authorization.RequireActor(), default);
        var canRead = mode switch
        {
            "current" => await session.InTransactionAsync(CanRead),
            "nested-snapshot" => await session.InReadSnapshotAsync(CanRead),
            _ => await CanRead()
        };
        if (mode != "identity") Assert.Equal(3, authorizationReadCount);
        synchronizing = true;
        var request = new FileReferenceSyncRequest("20", "10", "14", "child_record", "71", parentText, "attachments", ["81"]);
        if (noncanonical)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => files.SynchronizeReferencesAsync(request));
        else
            await files.SynchronizeReferencesAsync(request);

        Assert.Equal(!noncanonical, canRead);
        Assert.Equal(noncanonical ? 0 : 2, parentChecks);
        Assert.Equal(noncanonical ? 0 : 1, writes);
        Assert.Equal(noncanonical ? 1 : 0, provider.Last!.Rollbacks);
    }

    /// <summary>普通附件沿用账号目标范围，并让引用与业务行位于同一快照或写事务。</summary>
    [Theory]
    [InlineData("snapshot",false)]
    [InlineData("current",false)]
    [InlineData("missing-store",false)]
    [InlineData("unrestricted-update",false)]
    [InlineData("snapshot",true)]
    [InlineData("current",true)]
    [InlineData("missing-store",true)]
    public async Task Ordinary_file_uses_published_join_scope_for_reference_access(string mode,bool textForeignKey)
    {
        const long recordId=9007199254740993;
        const long fileId=9007199254740994;
        var catalog=FileSharedCrossParentScopeTests.JoinedExactCatalog(true,"unparented");
        if(textForeignKey)
        {
            var foreign=catalog.ConfigColumns.Single(column=>column.Id=="19");
            foreign.LogicalType="string";
            foreign.LinkConfigTableId="";
        }
        catalog.ConfigTables.Add(new(){Id="80",TableName="yz_file_ref",YzProjectId="1"});
        var ordinal=0;
        foreach(var name in new[]{"id","yz_project_id","file_id","page_id","record_id","field_name"})
            catalog.ConfigColumns.Add(new(){Id="8_"+(++ordinal),ConfigTableId="80",YzProjectId="1",
                ColumnName=name,LogicalType=name=="field_name"?"string":"int64"});

        var registry=new FileMetadataContext([catalog]);
        var frozen=Assert.Single(registry.Catalogs);
        var page=frozen.ConfigPages.Single(item=>item.Id=="102");
        var entity=frozen.ConfigTables.Single(item=>item.Id==page.ConfigTableId);
        var physical=registry.PhysicalColumns(frozen,entity);
        var field=physical.Single(item=>item.ColumnName=="attachments");
        var actor=new RuntimeActor(12,"测试",false,"1","900"){StoreId=mode is "snapshot" or "current"?21L:null};
        var authorization=new JoinedFileAuthorization(actor,mode=="unrestricted-update");
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        var store=new SqlRowStore(session,frozen);
        var recordReads=0;
        provider.Read=command=>
        {
            Assert.NotNull(command.Transaction);
            Assert.Same(session.CurrentTransaction,command.Transaction);
            var data=new DataTable();
            var sql=command.CommandText;
            if(sql.Contains("FROM `yz_file_ref`",StringComparison.Ordinal))
            {
                data.Columns.Add("record_id",typeof(long));
                data.Columns.Add("field_name",typeof(string));
                data.Rows.Add(recordId,"attachments");
                return data.CreateDataReader();
            }
            recordReads++;
            Assert.Contains("FROM `file_shared_source`",sql);
            Assert.DoesNotContain(" AS `branch_name`",sql);
            if(mode=="unrestricted-update")
            {
                Assert.DoesNotContain("JOIN",sql);
                Assert.EndsWith("FOR UPDATE",sql);
            }
            else
            {
                Assert.Contains("EXISTS (SELECT 1",sql);
                Assert.Contains(".`store_id`",sql);
                if(textForeignKey)
                    Assert.Matches(@"BINARY CAST\(r\d+\.`id` AS CHAR\)=BINARY b\.`branch_id`",sql);
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,21L));
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,"分组甲"));
                Assert.Equal(mode=="current",sql.Contains(" FOR SHARE)",StringComparison.Ordinal));
                Assert.Equal(mode=="current",sql.EndsWith(" FOR SHARE",StringComparison.Ordinal));
            }
            data.Columns.Add("id",typeof(long));data.Columns.Add("yz_project_id",typeof(long));
            data.Columns.Add("attachments",typeof(string));
            data.Rows.Add(recordId,1L,"[\""+fileId+"\"]");
            return data.CreateDataReader();
        };

        Task<bool> CanRead()=>FileReferenceReadAuthorization.CanReadAsync(store,registry,authorization,fileId,actor,default);
        if(mode=="unrestricted-update")
        {
            await session.InTransactionAsync(async()=>
            {
                await FileReferenceReadAuthorization.RequireCurrentUpdateScopeAsync(store,registry,authorization,
                    frozen,entity,page,field,physical,registry.PageScope(frozen,page),recordId,actor,default);
                return true;
            });
        }
        else if(mode=="current")Assert.True(await session.InTransactionAsync(CanRead));
        else Assert.Equal(mode=="snapshot",await CanRead());

        Assert.Equal(mode=="missing-store"?0:1,recordReads);
        Assert.Equal(1,provider.Connections);
    }

    /// <summary>附件范围用实际字典页和来源页联接常量，快照及当前读均保留同一组查询条件。</summary>
    [Theory]
    [InlineData(false,false,true,true)]
    [InlineData(false,true,false,true)]
    [InlineData(true,false,false,true)]
    [InlineData(true,true,true,true)]
    [InlineData(false,false,true,false)]
    [InlineData(false,true,false,false)]
    [InlineData(true,false,false,false)]
    [InlineData(true,true,true,false)]
    [InlineData(false,false,true,false,"int64")]
    [InlineData(false,true,false,false,"string")]
    [InlineData(true,false,false,false,"int64")]
    [InlineData(true,true,true,false,"string")]
    public async Task File_page_uses_joined_dictionary_conditions(bool stored,bool locking,bool visible,bool parented,string? physicalType=null)
    {
        const long recordId=9007199254740993L;
        var catalog=JoinedConditionFileCatalog(stored);
        var source=catalog.ConfigPages.Single(item=>item.Id=="101");
        if(!parented)
            source.WhereSql="yz_project_id=@current_yz_project_id AND display_region='可下载'";
        if(physicalType is not null)
        {
            catalog.ConfigColumns.Add(new(){Id="185",ConfigTableId="1",ColumnName="owner_country_id",
                LogicalType=physicalType,LinkConfigTableId="2",YzProjectId="1"});
            source.WhereSql+=" AND owner_country_id=@parent_id";
        }
        var registry=new FileMetadataContext([catalog]);
        var frozen=Assert.Single(registry.Catalogs);
        var page=frozen.ConfigPages.Single(item=>item.Id=="101");
        var scope=registry.PageScope(frozen,page);
        Assert.True(registry.NeedsFileReadSnapshot);
        Assert.Equal(parented?1:0,scope.Crosses.Count);
        if(physicalType is not null)
        {
            var parent=Assert.Single(scope.Parents);
            Assert.Equal("2",parent.Parent.Id);
            Assert.Equal("owner_country_id",parent.Source.ColumnName);
            Assert.Equal(physicalType,parent.Source.LogicalType);
        }
        var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(scope.LogicalConditionPlan);
        var actor=new RuntimeActor(12,"测试",false,"1","3");
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor));
        actor=actor with{StoreId=29};
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        provider.Read=command=>
        {
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.NotNull(command.Transaction);
            Assert.Contains("`file_joined_scope_label`",command.CommandText);
            Assert.Contains(".`store_id`",command.CommandText);
            Assert.Contains(".`name`",command.CommandText);
            Assert.DoesNotContain("b.`display_region`",command.CommandText);
            Assert.Equal(locking,command.CommandText.Contains("FOR SHARE",StringComparison.Ordinal));
            if(!stored)Assert.Equal(locking,command.CommandText.Contains(" FOR SHARE)",StringComparison.Ordinal));
            foreach(var expected in new object[]{recordId,29L,"停用","可下载"})
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,expected)&&
                    command.CommandText.Contains(parameter.ParameterName,StringComparison.Ordinal));
            var data=new DataTable();
            data.Columns.Add("id",typeof(long));
            if(visible)data.Rows.Add(recordId);
            return data.CreateDataReader();
        };
        Task<bool> Read()=>FilePageScope.MatchesAsync(session,plan.BindAccount(actor),scope.Condition!.BindAccount(actor),
            recordId,actor.ProjectId,locking,default);
        Assert.Equal(visible,locking?await session.InTransactionAsync(Read):await session.InReadSnapshotAsync(Read));
        Assert.Equal(1,provider.Last?.Command?.Executions);
    }

    /// <summary>通过中间字典解析名称时，末级字典继续按中间字典的实际页面取范围。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_page_uses_each_published_scope_of_chained_dictionary_conditions(bool terminalStored)
    {
        var catalog=JoinedConditionFileCatalog(stored:false);
        var label=catalog.ConfigColumns.Single(column=>column.Id=="19");
        label.Extra["columnFullName"]=System.Text.Json.JsonSerializer.SerializeToElement("label_item.terminal_caption");
        catalog.ConfigTables.Add(new(){Id="6",TableName="file_joined_scope_terminal",IsRongyu=terminalStored?1:0,YzProjectId="1"});
        catalog.ConfigColumns.AddRange([
            new(){Id="505",ConfigTableId="5",ColumnName="terminal_id",LogicalType="int64",YzProjectId="1",
                ControlKind="List",LinkConfigTableId="6",ControlDetail="""{"tableId":"6","pageId":"601","nameField":"name"}""",
                Extra={["linkConfigPageId"]=System.Text.Json.JsonSerializer.SerializeToElement("601")}},
            new(){Id="506",ConfigTableId="5",ColumnName="terminal_caption",LogicalType="string",YzProjectId="1",
                Extra={["isJoinColumn"]=System.Text.Json.JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=System.Text.Json.JsonSerializer.SerializeToElement("terminal.name")}},
            new(){Id="601",ConfigTableId="6",ColumnName="id",LogicalType="int64",YzProjectId="1",IsDbNotNull=1},
            new(){Id="602",ConfigTableId="6",ColumnName="name",LogicalType="string",YzProjectId="1"},
            new(){Id="603",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64",YzProjectId="1"}
        ]);
        catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6",YzProjectId="1",YzAdminTypeId="3",
            WhereSql="yz_project_id=@current_yz_project_id AND name <> '末级停用'"});
        catalog.ConfigPageColumns.Add(new(){Id="f601602",ConfigPageId="601",ConfigColumnId="602",YzProjectId="1",IsShow=1});
        var registry=new FileMetadataContext([catalog]);
        var frozen=Assert.Single(registry.Catalogs);
        var page=frozen.ConfigPages.Single(item=>item.Id=="101");
        var scope=registry.PageScope(frozen,page);
        var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(scope.LogicalConditionPlan);
        var actor=new RuntimeActor(12,"测试",false,"1","3"){StoreId=29};

        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        provider.Read=command=>
        {
            var sql=command.CommandText;
            Assert.Contains("`file_joined_scope_label`",sql);
            Assert.Contains("`file_joined_scope_terminal`",sql);
            foreach(var expected in new[]{"停用","末级停用","可下载"})
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,expected)&&
                    sql.Contains(parameter.ParameterName,StringComparison.Ordinal));
            var expectedNestedLocks=terminalStored?1:2;
            Assert.Equal(expectedNestedLocks,System.Text.RegularExpressions.Regex.Matches(sql,@" FOR SHARE\)").Count);
            return new DataTable().CreateDataReader();
        };
        Assert.False(await session.InTransactionAsync(()=>FilePageScope.MatchesAsync(
            session,plan.BindAccount(actor),scope.Condition!.BindAccount(actor),97,actor.ProjectId,true,default)));
        Assert.Equal(1,provider.Last?.Command?.Executions);
    }

    /// <summary>业务关联的目标页条件由该页实际绑定求值，字典中间层和末级范围进入同一当前读。</summary>
    [Theory]
    [InlineData(false,false,true)]
    [InlineData(true,false,true)]
    [InlineData(false,true,true)]
    [InlineData(true,true,true)]
    [InlineData(false,false,false)]
    [InlineData(true,true,false)]
    public async Task Business_lookup_locks_the_pages_used_by_dictionary_conditions(bool stored,bool explicitTarget,bool parented)
    {
        var catalog=JoinedConditionFileCatalog(stored);
        if(!parented)
            catalog.ConfigPages.Single(item=>item.Id=="101").WhereSql=
                "yz_project_id=@current_yz_project_id AND display_region='可下载'";
        catalog.ConfigTables.Add(new(){Id="7",TableName="file_joined_scope_order",IsRongyu=0,YzProjectId="1"});
        catalog.ConfigColumns.AddRange([
            new(){Id="71",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1,YzProjectId="1"},
            new(){Id="72",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64",YzProjectId="1"},
            new(){Id="73",ConfigTableId="7",ColumnName="country_id",LogicalType="string",LinkConfigTableId="2",YzProjectId="1"},
            new(){Id="74",ConfigTableId="7",ColumnName="document_id",LogicalType="int64",LinkConfigTableId="1",YzProjectId="1",
                ControlKind="List",ControlDetail="""{"tableId":"1","pageId":"101","nameField":"name"}""",
                Extra={["linkConfigPageId"]=System.Text.Json.JsonSerializer.SerializeToElement("101")}},
            new(){Id="75",ConfigTableId="7",ColumnName="document_name",LogicalType="string",YzProjectId="1",
                Extra={["isJoinColumn"]=System.Text.Json.JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=System.Text.Json.JsonSerializer.SerializeToElement("document.name")}}
        ]);
        catalog.ConfigPages.Add(new(){Id="701",ConfigTableId="7",YzAdminTypeId="3",YzProjectId="1",
            WhereSql="yz_project_id=@current_yz_project_id AND country_id=@parent_id"});
        var binding=new Metarill.Data.Queries.NonRedundantSingleTargetScope("74","101",parented?"73":null)
        {
            SourcePageId="701",
            NestedTargets=explicitTarget?[new("18","501"){SourcePageId="101"}]:[]
        };
        var plan=Metarill.Data.Queries.NonRedundantReads.CompileProjectPage(catalog,"7",["id"],["document_name"],[],[binding]);
        var actor=new RuntimeActor(12,"测试",false,"1","3");
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor));
        var bound=plan.BindAccount(actor with{StoreId=29});

        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        provider.Read=command=>
        {
            var sql=command.CommandText;
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.Contains("`file_joined_scope_label`",sql);
            Assert.Contains(".`store_id`",sql);
            Assert.DoesNotContain(" AS `document_name`",sql);
            Assert.EndsWith("FOR SHARE",sql,StringComparison.Ordinal);
            Assert.Equal(stored?1:2,System.Text.RegularExpressions.Regex.Matches(sql,@" FOR SHARE\)").Count);
            foreach(var expected in new object[]{29L,"停用","可下载"})
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,expected)&&
                    sql.Contains(parameter.ParameterName,StringComparison.Ordinal));
            return new DataTable().CreateDataReader();
        };
        var reader=new Metarill.Data.Queries.NonRedundantReader(session);
        await session.InTransactionAsync(async()=>
        {
            var rows=await reader.CurrentRowsAsync(bound,[],[],1,projectId:actor.ProjectId);
            Assert.Empty(rows);
            return true;
        });
        Assert.Equal(1,provider.Last?.Command?.Executions);
    }

    /// <summary>精确匹配的冗余字典独立于附件页面常量，仍须按实际目标页限制账号和状态。</summary>
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,true,false)]
    [InlineData(false,true,true)]
    [InlineData(true,true,true)]
    public async Task File_exact_match_uses_published_stored_dictionary_scope(bool locking,bool textForeignKey,bool selection)
    {
        const long recordId=9007199254740993L;
        var catalog=JoinedConditionFileCatalog(stored:true);
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="18");
        if(textForeignKey)foreign.LogicalType="string";
        if(selection)
        {
            foreign.ControlKind="SelectPage";
            catalog.ConfigPages.Single(item=>item.Id=="501").Extra["type"]=System.Text.Json.JsonSerializer.SerializeToElement(1);
        }
        var source=catalog.ConfigPages.Single(item=>item.Id=="101");
        source.WhereSql=source.WhereSql.Replace(" AND display_region='可下载'","",StringComparison.Ordinal);
        source.Extra["exactDataManageMatchField"]=System.Text.Json.JsonSerializer.SerializeToElement("display_region");
        var registry=new FileMetadataContext([catalog]);
        var frozen=Assert.Single(registry.Catalogs);
        var page=frozen.ConfigPages.Single(item=>item.Id==source.Id);
        var file=frozen.ConfigColumns.Single(item=>item.ConfigTableId==page.ConfigTableId&&item.ColumnName=="attachments");
        var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(registry.DerivedExactPlan(frozen,page,file));
        var actor=new RuntimeActor(12,"测试",false,"1","3");
        Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor));
        actor=actor with{StoreId=29};

        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        provider.Read=command=>
        {
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.NotNull(command.Transaction);
            var sql=command.CommandText;
            Assert.Contains("JOIN `file_joined_scope_label`",sql);
            if(textForeignKey)
                Assert.Matches(@"BINARY CAST\(r\d+\.`id` AS CHAR\)=BINARY b\.`label_item_id`",sql);
            Assert.Contains(".`store_id`",sql);
            Assert.DoesNotContain("AS `display_region`",sql);
            Assert.Equal(locking,sql.EndsWith(" FOR SHARE",StringComparison.Ordinal));
            foreach(var expected in new object[]{recordId,29L,"停用","授权名"})
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,expected)&&
                    sql.Contains(parameter.ParameterName,StringComparison.Ordinal));
            return new DataTable().CreateDataReader();
        };
        var bound=plan.BindAccount(actor);
        var reader=new Metarill.Data.Queries.NonRedundantReader(session);
        var filters=new Metarill.Data.Queries.FixedReadFilter[]{
            new("id",Metarill.Data.Queries.FixedReadComparison.Equal,recordId),
            new("display_region",Metarill.Data.Queries.FixedReadComparison.In,new object?[]{"授权名"})};
        async Task<bool> Verify()
        {
            var rows=locking?await reader.CurrentRowsAsync(bound,filters,[],1,projectId:actor.ProjectId):
                await reader.RowsAsync(bound,filters,[],1,projectId:actor.ProjectId);
            Assert.Empty(rows);
            return true;
        }
        if(locking)await session.InTransactionAsync(Verify);
        else await session.InReadSnapshotAsync(Verify);
        Assert.Equal(1,provider.Last?.Command?.Executions);
    }

    /// <summary>附件联接条件的别名必须落到非凭据终点。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void File_joined_dictionary_condition_rejects_credential_terminal(bool parented)
    {
        var catalog=JoinedConditionFileCatalog(stored:true);
        if(!parented)
            catalog.ConfigPages.Single(item=>item.Id=="101").WhereSql=
                "yz_project_id=@current_yz_project_id AND display_region='可下载'";
        catalog.ConfigColumns.Add(new(){Id="505",ConfigTableId="5",ColumnName="password",LogicalType="string",YzProjectId="1"});
        catalog.ConfigColumns.Single(column=>column.Id=="19").Extra["columnFullName"]=
            System.Text.Json.JsonSerializer.SerializeToElement("label_item.password");
        Assert.Throws<NotSupportedException>(()=>FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],catalog.ConfigPages[0]));
    }

    /// <summary>普通页列可以联接本表，只有目标页条件重新引用同一发布范围时才拒绝。</summary>
    [Fact]
    public void File_joined_dictionary_condition_rejects_recursive_page()
    {
        var catalog=JoinedConditionFileCatalog(stored:false);
        var page=catalog.ConfigPages.Single(item=>item.Id=="101");
        page.WhereSql="yz_project_id=@current_yz_project_id AND display_region='可下载'";
        var foreign=catalog.ConfigColumns.Single(item=>item.Id=="18");
        foreign.LinkConfigTableId="1";
        foreign.ControlDetail="""{"tableId":"1","pageId":"101","nameField":"name"}""";
        foreign.Extra["linkConfigPageId"]=System.Text.Json.JsonSerializer.SerializeToElement("101");
        var error=Assert.Throws<NotSupportedException>(()=>FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],page));
        Assert.Contains("循环依赖",error.Message);
    }

    /// <summary>使用已有跨表祖先模型，联接目标通过实际页面限制门店和名称。</summary>
    private static MetadataCatalog JoinedConditionFileCatalog(bool stored)
    {
        var catalog=FileCrossParentScopeTests.Catalog("file_joined_scope","string","string");
        catalog.YzAdminTypes.Add(new(){Id="3",YzProjectId="1"});
        foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="3";
        var tree=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(tree.ControlDetail)!;
        definition["levels"]![0]!["idField"]="business_key";
        tree.ControlDetail=definition.ToJsonString();
        catalog.ConfigColumns.Add(new(){Id="209",ConfigTableId="2",ColumnName="business_key",LogicalType="string",YzProjectId="1",IsDbNotNull=1,ColumnLength="50"});
        catalog.ConfigIndexes.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{id="210",configTableId="2",isUnique=1}));
        catalog.ConfigIndexColumns.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{configIndexId="210",configColumnId="209"}));
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",ColumnName="label_item_id",LogicalType="int64",YzProjectId="1",
            ControlKind="List",LinkConfigTableId="5",ControlDetail="""{"tableId":"5","pageId":"501","nameField":"name"}""",
            Extra={["linkConfigPageId"]=System.Text.Json.JsonSerializer.SerializeToElement("501")}});
        catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",ColumnName="display_region",LogicalType="string",YzProjectId="1",
            Extra={["isJoinColumn"]=System.Text.Json.JsonSerializer.SerializeToElement(1),
            ["columnFullName"]=System.Text.Json.JsonSerializer.SerializeToElement("label_item.name")}});
        catalog.ConfigTables.Add(new(){Id="5",TableName="file_joined_scope_label",IsRongyu=stored?1:0,YzProjectId="1"});
        catalog.ConfigColumns.AddRange([
            new(){Id="501",ConfigTableId="5",ColumnName="id",LogicalType="int64",YzProjectId="1",IsDbNotNull=1},
            new(){Id="502",ConfigTableId="5",ColumnName="name",LogicalType="string",YzProjectId="1"},
            new(){Id="503",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64",YzProjectId="1"},
            new(){Id="504",ConfigTableId="5",ColumnName="store_id",LogicalType="int64",YzProjectId="1"}
        ]);
        catalog.ConfigPages.Add(new(){Id="501",ConfigTableId="5",YzProjectId="1",YzAdminTypeId="3",
            WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id AND name <> '停用'"});
        catalog.ConfigPageColumns.Add(new(){Id="f501502",ConfigPageId="501",ConfigColumnId="502",YzProjectId="1",IsShow=1});
        catalog.ConfigPages[0].WhereSql+=" AND display_region='可下载'";
        return catalog;
    }

    /// <summary>原附件查询与写前校验按发布的文本级联授权，页面无需把匹配路径同时用作父级。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task File_exact_text_cascade_applies_to_lookup_and_reference_validation(bool shared,bool locking)
    {
        const long recordId=9007199254740993;
        const long fileId=9007199254740994;
        const string key="001-A";
        var catalog=FileCrossParentScopeTests.Catalog("file_exact_text","string","string");
        var source=catalog.ConfigPages[0];
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==source.Id))
            field.ConfigPageId="102";
        source.Id="102";
        source.WhereSql="yz_project_id=@current_yz_project_id";
        source.Extra["exactDataManageMatchField"]=System.Text.Json.JsonSerializer.SerializeToElement("countryId");
        catalog.YzAdminTypes.Add(new(){Id="900",YzProjectId="1"});
        foreach(var publishedPage in catalog.ConfigPages)publishedPage.YzAdminTypeId="900";

        var control=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
        definition["levels"]![0]!["idField"]="business_key";
        control.ControlDetail=definition.ToJsonString();
        catalog.ConfigColumns.AddRange([
            new(){Id="209",ConfigTableId="2",ColumnName="business_key",LogicalType="string",ColumnLength="50",IsDbNotNull=1},
            new(){Id="208",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"}
        ]);
        catalog.ConfigIndexes.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{id="210",configTableId="2",isUnique=1}));
        catalog.ConfigIndexColumns.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{id="211",configIndexId="210",configColumnId="209"}));
        catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND store_id=@current_store_id";
        if(shared)
        {
            definition["levels"]![2]!["columnPrefix"]="warehouse";
            catalog.ConfigColumns.Add(new(){Id="16",ConfigTableId="1",ColumnName="warehouse_id",LogicalType="string",
                ControlKind="CascadeTree",LinkConfigTableId="4",ControlDetail=definition.ToJsonString()});
        }

        var registry=new FileMetadataContext([catalog]);
        var frozen=Assert.Single(registry.Catalogs);
        var page=frozen.ConfigPages.Single(item=>item.Id=="102");
        var table=frozen.ConfigTables.Single(item=>item.Id=="1");
        var physical=registry.PhysicalColumns(frozen,table);
        var file=physical.Single(item=>item.ColumnName=="attachments");
        var scope=registry.PageScope(frozen,page);
        Assert.Empty(scope.Crosses);
        var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(registry.DerivedExactPlan(frozen,page,file));
        Assert.True(plan.RequiresAccount);
        Assert.DoesNotContain("country_id",plan.OutputTypes.Keys);

        var filesPath=Path.Combine(Path.GetDirectoryName(SourcePath())!,"../../metadata/files.json");
        var infrastructure=System.Text.Json.JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(filesPath),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var actor=new RuntimeActor(12,"测试",false,"1","900"){StoreId=29};
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        var store=new SqlRowStore(session,MetadataCatalogComposition.MergePhysical(frozen,infrastructure));
        var scopeReads=0;
        provider.Read=command=>
        {
            Assert.Same(session.CurrentTransaction,command.Transaction);
            var sql=command.CommandText;
            var data=new DataTable();
            if(sql.Contains(" FROM `yz_file_ref`",StringComparison.Ordinal))
            {
                Assert.False(locking);
                data.Columns.Add("record_id",typeof(long));
                data.Columns.Add("field_name",typeof(string));
                data.Rows.Add(recordId,"attachments");
                return data.CreateDataReader();
            }

            scopeReads++;
            Assert.Contains("`file_exact_text_country`",sql);
            Assert.Contains(".`business_key`",sql);
            Assert.Contains(".`store_id`",sql);
            Assert.Contains("utf8mb4_bin",sql);
            Assert.Contains(" IN (@filter_1_0)",sql);
            Assert.DoesNotContain("AS `country_id`",sql);
            Assert.Equal(locking,sql.EndsWith(" FOR SHARE",StringComparison.Ordinal));
            Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,29L));
            Assert.Equal(recordId,command.Parameters["@filter_0"].Value);

            // 模拟驱动返回满足条件的源记录，测试固定 SQL 和参数，不替代真实 MySQL 排序规则验收。
            if(Equals(command.Parameters["@filter_1_0"].Value,key))
            {
                data.Columns.Add("attachments",typeof(string));
                var values=new List<object>{System.Text.Json.JsonSerializer.Serialize(new[]{fileId.ToString()})};
                foreach(System.Text.RegularExpressions.Match alias in System.Text.RegularExpressions.Regex.Matches(sql,
                    " AS `(metarill_cross_(?:input|valid)_[0-9]+)`"))
                {
                    var name=alias.Groups[1].Value;
                    data.Columns.Add(name,typeof(long));
                    values.Add(name.StartsWith("metarill_cross_valid_",StringComparison.Ordinal)?1L:DBNull.Value);
                }
                data.Rows.Add(values.ToArray());
            }
            return data.CreateDataReader();
        };
        var allowed=new JoinedFileAuthorization(actor,false,key);
        var denied=new JoinedFileAuthorization(actor,false,"001-a");
        if(locking)
        {
            Task Check(IRuntimeAuthorization authorization)=>FileReferenceReadAuthorization.RequireCurrentUpdateScopeAsync(
                store,registry,authorization,frozen,table,page,file,physical,scope,recordId,actor,default);
            await session.InTransactionAsync(async()=>
            {
                await Check(allowed);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Check(denied));
                return true;
            });
        }
        else
        {
            Assert.True(await FileReferenceReadAuthorization.CanReadAsync(store,registry,allowed,fileId,actor,default));
            Assert.False(await FileReferenceReadAuthorization.CanReadAsync(store,registry,denied,fileId,actor,default));
        }
        Assert.Equal(2,scopeReads);
    }

    /// <summary>原附件更新校验对物理和联接TIME字段使用原写事务及有类型的微秒参数。</summary>
    [Theory]
    [InlineData(false,true)]
    [InlineData(true,true)]
    [InlineData(false,false)]
    [InlineData(true,false)]
    public async Task File_exact_time_uses_typed_values_in_current_read(bool derived,bool valid)
    {
        var catalog=FileSharedCrossParentScopeTests.JoinedExactCatalog(derived,"unparented");
        var source=catalog.ConfigPages.Single(item=>item.Id=="102");
        if(derived)
        {
            catalog.ConfigColumns.Add(new(){Id="66",ConfigTableId="6",ColumnName="shift_time",LogicalType="time",YzProjectId="1"});
            var match=catalog.ConfigColumns.Single(item=>item.Id=="20");
            match.LogicalType="time";
            match.Extra["columnFullName"]=System.Text.Json.JsonSerializer.SerializeToElement("branch.shift_time");
        }
        else
        {
            source.Extra["exactDataManageMatchField"]=System.Text.Json.JsonSerializer.SerializeToElement("shiftTime");
            catalog.ConfigColumns.Add(new(){Id="69",ConfigTableId="1",ColumnName="shift_time",LogicalType="time",YzProjectId="1"});
        }
        var registry=new FileMetadataContext([catalog]);
        var frozen=Assert.Single(registry.Catalogs);
        var page=frozen.ConfigPages.Single(item=>item.Id==source.Id);
        var table=frozen.ConfigTables.Single(item=>item.Id==page.ConfigTableId);
        var physical=registry.PhysicalColumns(frozen,table);
        var file=physical.Single(item=>item.ColumnName=="attachments");
        var actor=new RuntimeActor(12,"测试",false,"1","900"){StoreId=21};
        var text=valid?"-30:01:02.123456":"839:00:00";
        var authorization=new JoinedFileAuthorization(actor,false,text);
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        var store=new SqlRowStore(session,frozen);
        provider.Read=command=>
        {
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.NotNull(command.Transaction);
            Assert.Contains("`shift_time`",command.CommandText);
            Assert.DoesNotContain(text,command.CommandText);
            Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>
                Equals(parameter.Value,Metarill.Core.Contracts.SqlTimeValue.ParseText(text))&&
                command.CommandText.Contains(parameter.ParameterName,StringComparison.Ordinal));
            Assert.EndsWith(derived?"FOR SHARE":"FOR UPDATE",command.CommandText,StringComparison.Ordinal);
            return new DataTable().CreateDataReader();
        };
        await session.InTransactionAsync(async()=>
        {
            Task Check()=>FileReferenceReadAuthorization.RequireCurrentUpdateScopeAsync(store,registry,authorization,
                frozen,table,page,file,physical,registry.PageScope(frozen,page),97,actor,default);
            if(valid)await Assert.ThrowsAsync<UnauthorizedAccessException>(Check);
            else await Assert.ThrowsAsync<ArgumentOutOfRangeException>(Check);
            return true;
        });
        Assert.Equal(valid?1:0,provider.Last?.Command?.Executions??0);
    }

    /// <summary>固定测试身份只获一张来源页的精确数据范围，其他页拒绝。</summary>
    private sealed class JoinedFileAuthorization(RuntimeActor actor,bool unrestricted,string permittedValue="分组甲"):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string pageId,string operation)
        {
            if(pageId!="102")throw new UnauthorizedAccessException();
        }
        public ExactDataManageValues ResolveExactDataManage(string pageId,string operation,string managerField)
        {
            RequirePagePermission(pageId,operation);
            return unrestricted?ExactDataManageValues.Unrestricted:
                new(true,System.Text.Json.JsonSerializer.SerializeToElement(permittedValue));
        }
    }

    /// <summary>只放行测试项目的原修改动作。</summary>
    private sealed class ReferenceSyncAuthorization(bool match) : IRuntimeAuthorization
    {
        internal List<string> Operations { get; } = [];
        internal int ExactCalls;
        public ExactDataManageValues ResolveExactDataManage(string pageId, string operation, string managerField)
        {
            Assert.Equal(("20", "update", "department_id"), (pageId, operation, managerField));
            ExactCalls++;
            return new(true, match ? System.Text.Json.JsonSerializer.SerializeToElement(1) : null);
        }
        public RuntimeActor RequireActor() => new(2, "测试", false, "1", "3");
        public void RequireFileSourcePagePermission(string pageId) => Assert.Equal("20", pageId);
        public void RequirePagePermission(string pageId, string operation)
        {
            Assert.Equal("20", pageId);
            Assert.Equal("update", operation);
            Operations.Add(operation);
        }
    }

    /// <summary>实际独立及共享父路径逐层发送参数。账号不满足任一级条件时不能取得文件父范围。</summary>
    [Theory]
    [InlineData(false,false,false,false)]
    [InlineData(false,true,true,false)]
    [InlineData(true,false,true,false)]
    [InlineData(true,true,false,false)]
    [InlineData(false,false,true,true)]
    [InlineData(true,true,false,true)]
    public async Task Logical_file_parent_binds_real_account_on_each_target(bool shared,bool global,bool locking,bool redundant)
    {
        const long storeId=9007199254740993L;
        const long merchantId=9007199254740994L;
        var catalog=redundant?FileRedundantSharedParentScopeTests.Catalog("string","int64"):
            shared?FileSharedCrossParentScopeTests.Catalog("string","int64"):
            FileCrossParentScopeTests.Catalog("file_actor","string","int64");
        if(redundant)
        {
            // 只给本轮两条路径提供相同来源归属，移除指向同一目标的独立 List。
            catalog.ConfigPages.RemoveAll(page=>page.Id=="105"||!shared&&page.Id=="104");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="105"||field.ConfigColumnId=="18"||
                !shared&&(field.ConfigPageId=="104"||field.ConfigColumnId=="16"));
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="18"||!shared&&column.Id=="16");
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                page.WhereSql+=" AND store_id=@current_store_id AND merchant_id=@current_merchant_id";
            catalog.ConfigColumns.Add(new(){Id="190",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigColumns.Add(new(){Id="191",ConfigTableId="1",ColumnName="merchant_id",LogicalType="string"});
        }
        var country=catalog.ConfigTables.Single(table=>table.Id=="2");
        var countryPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        var areaPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        if(global)
        {
            country.Extra["scopeKind"]=System.Text.Json.JsonSerializer.SerializeToElement("global");
            countryPage.WhereSql="enabled=1";
        }
        countryPage.WhereSql+=" AND store_id=@current_store_id";
        areaPage.WhereSql+=" AND merchant_id=@current_merchant_id";
        catalog.ConfigColumns.Add(new(){Id="290",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
        catalog.ConfigColumns.Add(new(){Id="390",ConfigTableId="3",ColumnName="merchant_id",LogicalType="string"});
        var sourcePage=redundant?catalog.ConfigPages.Single(page=>page.Id==(shared?"104":"103")):catalog.ConfigPages[0];
        if(!redundant)sourcePage.WhereSql+=" AND country_name <> '排除'";
        var scope=FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],sourcePage);
        Assert.Equal(!redundant,scope.LogicalConditionPlan is not null);
        var stored=new Dictionary<string,object?>{{"country_id",redundant?"99":null},{"area_id",null},{"place_id",shared?null:"30"}};
        if(shared)stored.Add("warehouse_id","30");
        var actor=new RuntimeActor(14,"账号",false,"1","3"){ StoreId=storeId,MerchantId=merchantId };

        var executions=new List<(long Node,DbTransaction? Transaction)>();
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"},null,provider);
        var rowStore=new SqlRowStore(session,catalog);
        provider.Read=command=>
        {
            var id=Assert.IsType<long>(command.Parameters["node"].Value);
            Assert.Equal(locking,command.CommandText.EndsWith(" FOR UPDATE",StringComparison.Ordinal));
            executions.Add((id,command.Transaction));
            var data=new DataTable();
            data.Columns.Add("id",typeof(long));
            if(id!=10)data.Columns.Add("parent",typeof(long));
            if(id==10)
            {
                Assert.Contains("`store_id`=",command.CommandText);
                Assert.Equal(!global,command.CommandText.Contains("`yz_project_id`=",StringComparison.Ordinal));
                if(command.Parameters.Cast<DbParameter>().Any(parameter=>Equals(parameter.Value,storeId)))
                    data.Rows.Add(10L);
            }
            else if(id==20)
            {
                Assert.Contains("`merchant_id`=",command.CommandText);
                Assert.Contains(command.Parameters.Cast<DbParameter>(),parameter=>Equals(parameter.Value,"9007199254740994"));
                data.Rows.Add(20L,10L);
            }
            else
            {
                Assert.Equal(30L,id);
                data.Rows.Add(30L,20L);
            }
            return data.CreateDataReader();
        };

        Task<long[]> Read(RuntimeActor identity)=>FileCrossParentScope.ValuesAsync(scope.Crosses,rowStore,stored,identity,locking,default);
        // 归属在打开连接前绑定，管理员状态也不能替代真实门店。
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read(actor with { StoreId=null,IsAdministrator=true }));
        Assert.Equal(0,provider.Connections);

        async Task<bool> Check()
        {
            Assert.Equal(redundant?30L:10L,Assert.Single(await Read(actor)));
            Assert.Equal(new[]{30L,20L,10L},executions.Select(item=>item.Node));
            Assert.All(executions,item=>Assert.Same(session.CurrentTransaction,item.Transaction));
            executions.Clear();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read(actor with { StoreId=91 }));
            Assert.Equal(new[]{30L,20L,10L},executions.Select(item=>item.Node));
            return true;
        }
        if(locking)await session.InTransactionAsync(Check);
        else await session.InReadSnapshotAsync(Check,default);
    }


    /// <summary>附件父节点复用递归候选 SQL，始终在原快照或写事务内核对实际嵌套范围。</summary>
    [Theory]
    [InlineData(false,false,"ordinary",false)]
    [InlineData(true,true,"ordinary",false)]
    [InlineData(false,true,"row-project",false)]
    [InlineData(true,false,"without-project",false)]
    [InlineData(false,true,"ordinary",true)]
    [InlineData(true,false,"without-project",true)]
    public async Task Logical_file_parent_checks_recursive_candidate_condition(bool shared,bool locking,string mode,bool redundant)
    {
        const long storeId=9007199254740993L;
        var catalog=shared?FileSharedCrossParentScopeTests.Catalog("string","int64"):
            FileCrossParentScopeTests.Catalog("file_nested","string","int64");
        var country=catalog.ConfigTables.Single(item=>item.Id=="2");
        country.IsRongyu=0;
        var candidatePage=catalog.ConfigPages.Single(item=>item.Id=="201");
        if(mode!="ordinary")
        {
            country.Extra["scopeKind"]=System.Text.Json.JsonSerializer.SerializeToElement("global");
            country.YzProjectId=catalog.YzProjectId;
            candidatePage.YzProjectId=catalog.YzProjectId;
            candidatePage.YzAdminTypeId="9";
            catalog.YzAdminTypes.Add(new(){Id="9",YzProjectId=catalog.YzProjectId});
            candidatePage.WhereSql="enabled=1";
        }
        if(mode=="without-project")
        {
            var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId==country.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==project.Id);
        }
        candidatePage.WhereSql+=" AND scope_zone_name <> '排除区域' AND store_id=@current_store_id";
        catalog.ConfigColumns.Add(new(){Id="290",ConfigTableId=country.Id,ColumnName="store_id",LogicalType="int64"});
        var prefix=country.TableName+"_scope";
        foreach(var (id,role) in new[]{("91","zone"),("92","site")})
        {
            catalog.ConfigTables.Add(new(){Id=id,TableName=prefix+"_"+role,IsRongyu=1});
            catalog.ConfigColumns.AddRange([
                new(){Id=id+"01",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=id+"02",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=id+"03",ConfigTableId=id,ColumnName="name",LogicalType="string"}
            ]);
            catalog.ConfigPages.Add(new(){Id=id+"0",ConfigTableId=id,WhereSql="yz_project_id=@current_yz_project_id"});
            catalog.ConfigPageColumns.Add(new(){Id=id+"003",ConfigPageId=id+"0",ConfigColumnId=id+"03",IsShow=1});
        }
        catalog.ConfigColumns.Add(new(){Id="9204",ConfigTableId="92",ColumnName="zone_id",LogicalType="int64",LinkConfigTableId="91"});
        catalog.ConfigPageColumns.Add(new(){Id="92004",ConfigPageId="920",ConfigColumnId="9204",IsShow=1});
        catalog.ConfigColumns.Add(new(){Id="291",ConfigTableId=country.Id,ColumnName="scope_site_id",LogicalType="int64",
            ControlKind="CascadeTree",LinkConfigTableId="92",ControlDetail=System.Text.Json.JsonSerializer.Serialize(new
            {
                sourceMode="crossTable",
                levels=new object[]{
                    new{table=prefix+"_zone",nameField="name",columnPrefix="scope_zone"},
                    new{table=prefix+"_site",nameField="name",columnPrefix="scope_site",parentField="zone_id"}
                }
            })});
        var source=catalog.ConfigTables.Single(item=>item.Id=="1");
        if(redundant)
        {
            source.IsRongyu=1;
            if(!catalog.YzAdminTypes.Any(item=>item.Id=="9"))
                catalog.YzAdminTypes.Add(new(){Id="9",YzProjectId=catalog.YzProjectId});
            foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigPages))
                record.YzProjectId=catalog.YzProjectId;
            foreach(var page in catalog.ConfigPages)
            {
                page.YzAdminTypeId="9";
                if(page.ConfigTableId==source.Id)page.WhereSql+=" AND store_id=@current_store_id";
            }
            catalog.ConfigColumns.Add(new(){Id="191",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"});
        }
        var scope=FileReferenceScope.CompilePage(catalog,source,catalog.ConfigPages.Single(item=>item.Id=="101"));
        var parent=Assert.Single(scope.Crosses);
        Assert.Equal(redundant,parent.SourceId.StartsWith("r1:",StringComparison.Ordinal));
        if(redundant)Assert.Equal("country_id",Assert.Single(parent.StorageNames));
        var stored=new Dictionary<string,object?>{{"country_id","10"},{"area_id",null},{"place_id",null}};
        if(shared)stored.Add("warehouse_id",null);
        var actor=new RuntimeActor(14,"候选附件账号",false,"1","3"){StoreId=storeId};
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        var store=new SqlRowStore(session,catalog);
        var reads=0;
        provider.Read=command=>
        {
            reads++;
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.Contains(prefix+"_zone",command.CommandText);
            Assert.Contains(prefix+"_site",command.CommandText);
            Assert.Contains("@mt_candidate_",command.CommandText);
            Assert.Equal(mode=="ordinary",command.CommandText.Contains("b.`yz_project_id`=",StringComparison.Ordinal));
            Assert.Contains("cross0.`yz_project_id`=@project_scope_number",command.CommandText);
            Assert.DoesNotContain("排除区域",command.CommandText);
            Assert.Equal(locking,command.CommandText.EndsWith(" FOR SHARE",StringComparison.Ordinal));
            Assert.Equal(10L,command.Parameters["filter_0"].Value);
            var data=new DataTable();
            data.Columns.Add("id",typeof(long));
            data.Columns.Add("metarill_cross_valid_0",typeof(int));
            data.Columns.Add("metarill_cross_input_1",typeof(long));
            data.Columns.Add("metarill_cross_input_2",typeof(long));
            if(command.Parameters.Cast<DbParameter>().Any(parameter=>Equals(parameter.Value,storeId)))
                data.Rows.Add(10L,1,DBNull.Value,99L);
            return data.CreateDataReader();
        };
        Task<long[]> Read(RuntimeActor identity)=>FileCrossParentScope.ValuesAsync(scope.Crosses,store,stored,identity,locking,default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read(actor with{StoreId=null}));
        Assert.Equal(0,provider.Connections);
        async Task<bool> Verify()
        {
            Assert.Equal(10L,Assert.Single(await Read(actor)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Read(actor with{StoreId=31}));
            return true;
        }
        if(locking)await session.InTransactionAsync(Verify);
        else await session.InReadSnapshotAsync(Verify);
        Assert.Equal(2,reads);
        Assert.Equal(1,provider.Connections);
    }

    /// <summary>业务附件从实际存储恢复混合父链，文本键逐字绑定，大小写相异直接拒绝。</summary>
    [Theory]
    [InlineData(false,false,false,false,false)]
    [InlineData(true,false,false,false,false)]
    [InlineData(false,true,false,false,false)]
    [InlineData(true,true,false,false,false)]
    [InlineData(false,false,true,false,false)]
    [InlineData(true,false,true,false,false)]
    [InlineData(false,true,true,false,false)]
    [InlineData(true,true,true,false,false)]
    [InlineData(false,true,false,true,false)]
    [InlineData(true,true,true,true,false)]
    [InlineData(false,false,false,true,false)]
    [InlineData(true,false,true,true,false)]
    [InlineData(false,true,false,false,true)]
    [InlineData(true,true,false,true,true)]
    [InlineData(false,false,true,false,true)]
    [InlineData(true,true,true,true,true)]
    [InlineData(false,false,true,true,true,true)]
    [InlineData(true,true,true,false,true,true)]
    public async Task File_parent_scope_restores_actual_text_keys(
        bool locking,bool redundant,bool globalRoot,bool shared,bool derivedRoot,bool account=false)
    {
        var catalog=FileCrossParentScopeTests.Catalog("file_text","string","string");
        var control=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var definition=System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
        foreach(var (index,tableId) in new[]{(0,"2"),(2,"4")})
        {
            definition["levels"]![index]!["idField"]="business_key";
            var columnId=tableId+"09";
            var indexId=tableId+"10";
            catalog.ConfigColumns.Add(new(){Id=columnId,ConfigTableId=tableId,ColumnName="business_key",
                LogicalType="string",ColumnLength="50",IsDbNotNull=1});
            catalog.ConfigIndexes.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{id=indexId,configTableId=tableId,isUnique=1}));
            catalog.ConfigIndexColumns.Add(System.Text.Json.JsonSerializer.SerializeToElement(new{configIndexId=indexId,configColumnId=columnId}));
        }
        control.ControlDetail=definition.ToJsonString();
        if(redundant)
        {
            catalog.ConfigTables[0].IsRongyu=1;
            catalog.ConfigPages[0].WhereSql="yz_project_id=@current_yz_project_id AND place_id=@parent_id";
        }
        if(shared)
        {
            // 同一目标被两个叶子角色引用时，附件依旧从本页实际 place_id 复核共同祖先。
            var peer=System.Text.Json.Nodes.JsonNode.Parse(control.ControlDetail)!;
            peer["levels"]![2]!["columnPrefix"]="warehouse";
            peer["levels"]![2]!["key"]="warehouse";
            catalog.ConfigColumns.Add(new(){Id="16",ConfigTableId="1",ColumnName="warehouse_id",LogicalType="string",
                ControlKind="CascadeTree",LinkConfigTableId="4",ControlDetail=peer.ToJsonString()});
        }
        if(globalRoot)
        {
            catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=
                System.Text.Json.JsonSerializer.SerializeToElement("global");
            catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="enabled=1";
        }
        if(derivedRoot)
        {
            catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
            var label=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="name");
            label.Extra["isJoinColumn"]=System.Text.Json.JsonSerializer.SerializeToElement(1);
            label.Extra["columnFullName"]=System.Text.Json.JsonSerializer.SerializeToElement("company.name");
            var dictionary=new TableMetadata{Id="5",YzProjectId="1",TableName="file_text_company",IsRongyu=1};
            catalog.ConfigTables.Add(dictionary);
            catalog.ConfigColumns.AddRange([
                new(){Id="51",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="52",ConfigTableId="5",ColumnName="name",LogicalType="string"},
                new(){Id="53",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="54",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="5"}
            ]);
            if(globalRoot)
            {
                dictionary.Extra["scopeKind"]=System.Text.Json.JsonSerializer.SerializeToElement("global");
                catalog.ConfigPages.Add(new(){Id="501",ConfigTableId="5",YzProjectId="1"});
                catalog.ConfigPageColumns.Add(new(){Id="50152",ConfigPageId="501",ConfigColumnId="52",IsShow=1});
                var dictionaryId=catalog.ConfigColumns.Single(column=>column.Id=="54");
                dictionaryId.ControlKind="List";
                dictionaryId.ControlDetail="""{"tableId":"5","pageId":"501","nameField":"name"}""";
            }
        }
        if(redundant||globalRoot)
        {
            catalog.YzAdminTypes.Add(new(){Id="3",YzProjectId="1"});
            foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="3";
        }
        const long dealer=9007199254740993;
        if(account)
        {
            catalog.ConfigColumns.Add(new(){Id="country_dealer",ConfigTableId="2",ColumnName="dealer_id",LogicalType="string"});
            catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND dealer_id=@current_dealer_id";
            if(redundant)
            {
                catalog.ConfigColumns.Add(new(){Id="source_dealer",ConfigTableId="1",ColumnName="record_dealer_id",LogicalType="int64"});
                catalog.ConfigPages[0].WhereSql+=" AND record_dealer_id=@current_dealer_id";
            }
        }
        var scope=FileReferenceScope.CompilePage(catalog,catalog.ConfigTables[0],catalog.ConfigPages[0]);
        Assert.True(Assert.Single(scope.Crosses).TextKey);
        var provider=new Provider();
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        var store=new SqlRowStore(session,catalog);
        var actor=new RuntimeActor(14,"文本父范围",false,"1","3"){DealerId=account?dealer:null};
        var source=new Dictionary<string,object?>{{"country_id",null},{"area_id",null},{"place_id","Leaf-A"}};
        if(shared)source["warehouse_id"]=null;
        var visited=new List<object>();
        provider.Read=command=>
        {
            Assert.Same(session.CurrentTransaction,command.Transaction);
            Assert.Equal(locking,command.CommandText.EndsWith(" FOR UPDATE",StringComparison.Ordinal));
            if(globalRoot&&command.CommandText.Contains("`file_text_country`",StringComparison.Ordinal))
            {
                Assert.False(command.Parameters.Contains("project"));
                Assert.Contains("`enabled`=@",command.CommandText);
            }
            else
            {
                var project=command.Parameters["project"].Value;
                if(command.CommandText.Contains("`file_text_area`",StringComparison.Ordinal))
                    Assert.Equal("1",Assert.IsType<string>(project));
                else
                    Assert.Equal(1L,Assert.IsType<long>(project));
            }
            var value=command.Parameters["node"].Value!;
            visited.Add(value);
            var data=new DataTable();
            if(command.CommandText.Contains("`file_text_place`",StringComparison.Ordinal))
            {
                Assert.Contains("BINARY `business_key`=BINARY @node",command.CommandText);
                data.Columns.Add("business_key",typeof(string));
                data.Columns.Add("area_id",typeof(string));
                if(Equals(value,"Leaf-A"))data.Rows.Add("Leaf-A","21");
            }
            else if(command.CommandText.Contains("`file_text_area`",StringComparison.Ordinal))
            {
                Assert.Equal(21L,value);
                data.Columns.Add("id",typeof(long));
                data.Columns.Add("country_id",typeof(string));
                data.Rows.Add(21L,"001");
            }
            else
            {
                Assert.Contains("`file_text_country`",command.CommandText);
                Assert.Contains("BINARY `business_key`=BINARY @node",command.CommandText);
                Assert.Equal("001",value);
                if(derivedRoot)Assert.DoesNotContain("file_text_company",command.CommandText);
                data.Columns.Add("business_key",typeof(string));
                if(account)Assert.Contains("`dealer_id`=@file_cross_",command.CommandText);
                if(!account||command.Parameters.Cast<DbParameter>().Any(parameter=>
                    Equals(parameter.Value,dealer.ToString(System.Globalization.CultureInfo.InvariantCulture))))
                    data.Rows.Add("001");
            }
            return data.CreateDataReader();
        };
        async Task<bool> Verify()
        {
            Assert.Equal(redundant?"Leaf-A":"001",Assert.Single(await FileCrossParentScope.KeyValuesAsync(scope.Crosses,store,source,actor,locking,default)));
            Assert.Equal(new object[]{"Leaf-A",21L,"001"},visited);
            source["place_id"]="leaf-a";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FileCrossParentScope.KeyValuesAsync(scope.Crosses,store,source,actor,locking,default));
            Assert.Equal("leaf-a",visited[^1]);
            Assert.Equal(4,visited.Count);
            if(account)
            {
                source["place_id"]="Leaf-A";
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FileCrossParentScope.KeyValuesAsync(
                    scope.Crosses,store,source,actor with{DealerId=null},locking,default));
                Assert.Equal(4,visited.Count);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>FileCrossParentScope.KeyValuesAsync(
                    scope.Crosses,store,source,actor with{DealerId=dealer+1},locking,default));
                Assert.Equal(7,visited.Count);
            }
            return true;
        }
        if(locking)await session.InTransactionAsync(Verify);
        else await session.InReadSnapshotAsync(Verify);
        Assert.Equal(1,provider.Connections);
    }

    /// <summary>只给固定身份；本用例没有任何文件行，不触发或替代来源权限判定。</summary>
    private sealed class Authorization:IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(2,"测试",false,"1","3");public void RequirePagePermission(string page,string operation)=>throw new UnauthorizedAccessException();
    }
    /// <summary>工厂计数发生在提供程序而非连接，漏接工厂会令本用例失败。</summary>
    private sealed class Provider:IDatabaseProvider
    {
        internal int Commands,Connections;internal Exception? Failure;internal Connection? Last;
        internal Func<DbCommand,DbDataReader>? Read;
        internal Func<DbCommand,int>? Write;
        internal Func<DbCommand,object?>? Scalar;
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}
        public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options){Connections++;return Last=new(this);}
        public DbCommand CreateCommand(DbConnection connection){Commands++;return connection.CreateCommand();}
        public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;
        public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);
        public void ValidateTimeValue(TimeSpan value)=>new MySqlDatabaseProvider().ValidateTimeValue(value);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken cancellationToken=default)
            =>connection.BeginTransactionAsync(IsolationLevel.RepeatableRead,cancellationToken);
    }
    /// <summary>替身连接只记录所有权，不联网；事务结束时保留身份供断言。</summary>
    private sealed class Connection(Provider owner):DbConnection
    {
        internal int Opens,Commits,Rollbacks;internal bool IsDisposed;internal Command? Command;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";
        public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"fake";public override ConnectionState State=>ConnectionState.Open;
        public override void Open()=>Opens++;public override void Close(){}public override void ChangeDatabase(string name)=>throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()=>Command=new(this,owner.Failure,owner.Read,owner.Write,owner.Scalar);
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this,level);
        protected override void Dispose(bool disposing){IsDisposed=true;base.Dispose(disposing);}
    }
    private sealed class Transaction(Connection connection,IsolationLevel level):DbTransaction
    {
        public override IsolationLevel IsolationLevel=>level;protected override DbConnection DbConnection=>connection;
        public override void Commit()=>connection.Commits++;public override void Rollback()=>connection.Rollbacks++;
    }
    /// <summary>仅复用MySQL参数集合，实际执行返回空reader或原异常；不解释SQL、不读写文件。</summary>
    private sealed class Command(Connection connection,Exception? failure,Func<DbCommand,DbDataReader>? read,Func<DbCommand,int>? write,Func<DbCommand,object?>? scalar):DbCommand
    {
        private readonly MySqlCommand parameters=new();internal int Executions;internal bool IsDisposed;internal CancellationToken Token;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override int ExecuteNonQuery()=>write?.Invoke(this)??throw new NotSupportedException();public override object? ExecuteScalar()=>scalar is null?throw new NotSupportedException():scalar(this);
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>throw new NotSupportedException();
        /// <summary>异步执行保留传入取消标记及异常对象，供真实调用链断言。</summary>
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){Executions++;Token=token;return failure is null?Task.FromResult(read?.Invoke(this)??new DataTable().CreateDataReader()):Task.FromException<DbDataReader>(failure);}
        protected override void Dispose(bool disposing){IsDisposed=true;if(disposing)parameters.Dispose();base.Dispose(disposing);}
    }
}
