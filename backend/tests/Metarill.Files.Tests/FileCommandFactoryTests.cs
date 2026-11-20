using System.Data;
using System.Data.Common;
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

    /// <summary>普通附件沿用账号目标范围，并让引用与业务行位于同一快照或写事务。</summary>
    [Theory]
    [InlineData("snapshot")]
    [InlineData("current")]
    [InlineData("missing-store")]
    [InlineData("unrestricted-update")]
    public async Task Ordinary_file_uses_published_join_scope_for_reference_access(string mode)
    {
        const long recordId=9007199254740993;
        const long fileId=9007199254740994;
        var catalog=FileSharedCrossParentScopeTests.JoinedExactCatalog(true,"unparented");
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

    /// <summary>固定测试身份只获一张来源页的精确数据范围，其他页拒绝。</summary>
    private sealed class JoinedFileAuthorization(RuntimeActor actor,bool unrestricted):IRuntimeAuthorization
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
                new(true,System.Text.Json.JsonSerializer.SerializeToElement("分组甲"));
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
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}
        public IFreeSql CreateOrm(DatabaseOptions options)=>new MySqlDatabaseProvider().CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options){Connections++;return Last=new(this);}
        public DbCommand CreateCommand(DbConnection connection){Commands++;return connection.CreateCommand();}
        public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;
        public string BuildSelectForUpdate(string sql)=>new MySqlDatabaseProvider().BuildSelectForUpdate(sql);
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
        protected override DbCommand CreateDbCommand()=>Command=new(this,owner.Failure,owner.Read,owner.Write);
        protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this,level);
        protected override void Dispose(bool disposing){IsDisposed=true;base.Dispose(disposing);}
    }
    private sealed class Transaction(Connection connection,IsolationLevel level):DbTransaction
    {
        public override IsolationLevel IsolationLevel=>level;protected override DbConnection DbConnection=>connection;
        public override void Commit()=>connection.Commits++;public override void Rollback()=>connection.Rollbacks++;
    }
    /// <summary>仅复用MySQL参数集合，实际执行返回空reader或原异常；不解释SQL、不读写文件。</summary>
    private sealed class Command(Connection connection,Exception? failure,Func<DbCommand,DbDataReader>? read,Func<DbCommand,int>? write):DbCommand
    {
        private readonly MySqlCommand parameters=new();internal int Executions;internal bool IsDisposed;internal CancellationToken Token;
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string CommandText{get;set;}="";public override int CommandTimeout{get;set;}public override CommandType CommandType{get;set;}public override bool DesignTimeVisible{get;set;}public override UpdateRowSource UpdatedRowSource{get;set;}
        protected override DbConnection? DbConnection{get;set;}=connection;protected override DbTransaction? DbTransaction{get;set;}protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
        public override void Cancel(){}public override void Prepare(){}protected override DbParameter CreateDbParameter()=>new MySqlParameter();public override int ExecuteNonQuery()=>write?.Invoke(this)??throw new NotSupportedException();public override object? ExecuteScalar()=>throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)=>throw new NotSupportedException();
        /// <summary>异步执行保留传入取消标记及异常对象，供真实调用链断言。</summary>
        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,CancellationToken token){Executions++;Token=token;return failure is null?Task.FromResult(read?.Invoke(this)??new DataTable().CreateDataReader()):Task.FromException<DbDataReader>(failure);}
        protected override void Dispose(bool disposing){IsDisposed=true;if(disposing)parameters.Dispose();base.Dispose(disposing);}
    }
}
