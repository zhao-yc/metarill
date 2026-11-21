using System.Data;
using System.Data.Common;
using System.Reflection;
using FreeSql;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using MySqlConnector;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>使用生成服务的静态读计划，验证发送给驱动的真实命令、整型参数和事务归属。</summary>
internal static class AccountParentCandidateProbe
{
    internal static async Task VerifyAsync(NonRedundantReadPlan plan,IReadOnlyList<RedundantSharedParentPlan> paths,
        string projectId,bool global,string codeType)
    {
        const long storeId=9007199254740993L;
        const long parentId=9007199254742000L;
        const long recordId=9007199254743000L;
        var actor=new RuntimeActor(1,"候选测试",false,projectId,"5"){StoreId=storeId};
        var provider=new CaptureProvider(codeType=="int32"?(object)2000000001:storeId,recordId);
        using var session=new DatabaseSession(new(){ExpectedDatabase="offline"},null,provider);
        var reader=new NonRedundantReader(session);
        var filters=new FixedReadFilter[]
        {
            new("place_id",FixedReadComparison.Equal,parentId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("code",FixedReadComparison.IntegerLabelContains,"90%_")
        };

        // 未绑定、缺少账号归属、借用其它项目的计划都在创建驱动命令之前拒绝。
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.RowsAsync(plan,filters,[],10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.RowsAsync(plan.BindAccount(actor with{StoreId=null,IsAdministrator=true}),filters,[],10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.RowsAsync(plan.BindAccount(actor),filters,[],10,projectId:"987"));
        Assert.Equal(0,provider.Opens);

        var bound=plan.BindAccount(actor);
        await session.InReadSnapshotAsync(async()=>
        {
            await RedundantSharedParentScopeSql.RequireParentAsync(session,paths,parentId,actor,false,default);
            var page=await reader.PageAsync(bound,filters,[new("code")],1,20,projectId:projectId);
            Assert.Equal(1,page.Total);
            Assert.Equal(recordId,Assert.Single(page.Records)["id"]);
            var selected=await reader.RowsAsync(bound,[filters[0],new("id",FixedReadComparison.Equal,recordId)],[],200,
                projectId:projectId);
            Assert.Equal(recordId,Assert.Single(selected)["id"]);
            Assert.All(provider.Commands,command=>Assert.Same(session.CurrentTransaction,command.Transaction));
            return true;
        });
        Assert.Collection(provider.Commands,
            check=>
            {
                Assert.Contains("BINARY CAST(@r1_candidate_parent AS CHAR)",check.Sql);
                Assert.Equal(parentId,check.Values["r1_candidate_parent"]);
                CheckAncestors(check);
            },
            count=>
            {
                Assert.StartsWith("SELECT COUNT(*)",count.Sql);
                CheckCandidate(count);
                Assert.Equal("%90!%!_%",count.Values["filter_1"]);
            },
            page=>
            {
                CheckCandidate(page);
                Assert.Contains(" ORDER BY b.code ASC",page.Sql);
                Assert.Equal(provider.Commands[1].Sql["SELECT COUNT(*)".Length..],
                    page.Sql[page.Sql.IndexOf(" FROM ",StringComparison.Ordinal)..page.Sql.LastIndexOf(" ORDER BY ",StringComparison.Ordinal)]);
                Assert.Equal(20,page.Values["page_limit"]);
            },
            selected=>
            {
                CheckCandidate(selected);
                Assert.Equal(recordId,selected.Values["filter_1"]);
                Assert.DoesNotContain(" LIKE ",selected.Sql);
            });

        provider.Commands.Clear();
        await session.InTransactionAsync(async()=>
        {
            await RedundantSharedParentScopeSql.RequireParentAsync(session,paths,parentId,actor,true,default);
            var matches=await reader.PageAsync(bound,[filters[0],new("code",FixedReadComparison.Equal,provider.Code)],[],1,2,
                projectId:projectId,currentRead:true);
            Assert.Single(matches.Records);
            Assert.All(provider.Commands,command=>
            {
                Assert.Same(session.CurrentTransaction,command.Transaction);
                Assert.Contains(" FOR UPDATE)",command.Sql);
                CheckAncestors(command);
            });
            Assert.Equal(provider.Code,provider.Commands[^1].Values["filter_1"]);
            return true;
        });

        void CheckCandidate(Captured command)
        {
            Assert.Contains("BINARY CAST(b.place_id AS CHAR)",command.Sql);
            CheckAncestors(command);
        }
        void CheckAncestors(Captured command)
        {
            Assert.Contains("INNER JOIN shared_parent_area rp1",command.Sql);
            Assert.Contains("INNER JOIN shared_parent_country rp0",command.Sql);
            Assert.Contains("rp0.store_id=",command.Sql);
            Assert.Contains("rp1.store_id=",command.Sql);
            Assert.Equal(!global,command.Sql.Contains("rp0.yz_project_id=",StringComparison.Ordinal));
            Assert.Contains(storeId,command.Values.Values);
            Assert.Contains("9007199254740993",command.Values.Values);
        }
    }

    /// <summary>实际生成的候选方法必须在同一读快照发送两条固定查询，并按排序 ID 返回原名称。</summary>
    internal static async Task VerifySelfTreeOrderAsync(Type serviceType, Type mapperType, RuntimeActor actor)
    {
        const long first = 9007199254742001;
        const long second = 9007199254742002;
        const long parent = 9007199254740993;
        var provider = new CaptureProvider(0, first, command =>
        {
            var table = new DataTable();
            table.Columns.Add("id", typeof(long));
            if (command.CommandText.Contains(" AS \u0060name\u0060", StringComparison.Ordinal))
            {
                table.Columns.Add("name", typeof(string));
                table.Columns.Add("parent_sort_tree_tree_id", typeof(long));
                table.Rows.Add(first, "全名甲", parent);
                table.Rows.Add(second, "全名乙", parent);
            }
            else
            {
                table.Rows.Add(second);
                table.Rows.Add(first);
            }
            return table;
        });
        using var session = new DatabaseSession(new()
        {
            ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
        }, null, provider);
        // 此探针只执行实际的固定 SQL，两次 ORM 调用只允许创建过滤器，禁止执行。
        session.Orm.Aop.CurdBefore += (_, _) => throw new InvalidOperationException("自树排序探针禁止执行 ORM 命令。");
        var mapper = Activator.CreateInstance(mapperType, session)!;
        var authorization = new TreeAuthorization(actor);
        var service = Activator.CreateInstance(serviceType, [session, mapper, new SnowflakeIdGenerator(945),
            Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>(), authorization])!;
        var method = serviceType.GetMethod("OrderedCascadeChildren201_25Async", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await session.InReadSnapshotAsync(async () =>
        {
            var results = await (Task<IReadOnlyList<CascadeTreeNode>>)method.Invoke(service, [parent, actor, true, true, CancellationToken.None])!;
            Assert.Equal(new[] { second, first }, results.Select(row => row.Id));
            Assert.Equal(new[] { "全名乙", "全名甲" }, results.Select(row => row.Name));
            Assert.Equal(2, provider.Commands.Count);
            Assert.All(provider.Commands, command => Assert.Same(session.CurrentTransaction, command.Transaction));
            return true;
        });
        Assert.Equal(new[] { "201", "301" }, authorization.Pages);
        Assert.DoesNotMatch(@"priority\s*>=\s*@", provider.Commands[0].Sql);
        Assert.Contains(" AS name", provider.Commands[0].Sql);
        Assert.DoesNotContain(" AS name", provider.Commands[1].Sql);
        Assert.Matches(@"priority\s*>=\s*@", provider.Commands[1].Sql);
        Assert.Matches(@" ORDER BY [^.]+\.\s*priority DESC,\s*b\.id ASC", provider.Commands[1].Sql);
        Assert.All(provider.Commands, command => Assert.Contains(parent, command.Values.Values));
    }

    /// <summary>经过实际生成入口检查计数、分页 SQL、已选范围和原名称响应。</summary>
    internal static async Task VerifyListOrderAsync(Type serviceType, Type mapperType, string projectId, string labelField)
    {
        const long id = 9007199254742002;
        var actor = new RuntimeActor(1, "候选测试", false, projectId, "5");
        var provider = new CaptureProvider(0, id, _ =>
        {
            var rows = new DataTable();
            rows.Columns.Add("id", typeof(long));
            rows.Columns.Add(labelField, typeof(string));
            rows.Rows.Add(id, "原名称");
            return rows;
        });
        using var session = new DatabaseSession(new()
        {
            ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
        }, null, provider);
        session.Orm.Aop.CurdBefore += (_, _) => throw new InvalidOperationException("下拉排序探针禁止执行 ORM 命令。");
        var service = Activator.CreateInstance(serviceType, [session, Activator.CreateInstance(mapperType, session)!,
            new SnowflakeIdGenerator(945), Array.Empty<IRecordMutationObserver>(),
            Array.Empty<IRecordMutationGuard>(), new TreeAuthorization(actor)])!;

        var page = await Invoke("OptionsPage101_15Async", new RelationOptionsPageRequest { Keyword = "原%_", PageNo = 2, PageSize = 1 });
        Assert.Equal(1L, page.GetType().GetProperty("Total")!.GetValue(page));
        CheckName(page.GetType().GetProperty("Records")!.GetValue(page)!);
        CheckName(await Invoke("OptionsSelected101_15Async", new RelationOptionsSelectedRequest { SelectedIds = [id.ToString()] }));
        CheckName(await Invoke("Options101_15Async", new PageRequest(), true));

        Assert.Equal(4, provider.Commands.Count);
        var count = provider.Commands[0];
        var pageRead = provider.Commands[1];
        Assert.StartsWith("SELECT COUNT(*)", count.Sql);
        Assert.DoesNotContain("metarill_candidate_order", count.Sql);
        Assert.DoesNotContain("priority", count.Sql);
        Assert.Equal("%原!%!_%", count.Values["filter_1"]);
        Assert.Equal(count.Values["filter_1"], pageRead.Values["filter_1"]);
        Assert.Equal(1L, Convert.ToInt64(pageRead.Values["page_offset"]));
        Assert.Same(count.Transaction, pageRead.Transaction);
        Assert.NotNull(pageRead.Transaction);
        foreach (var read in provider.Commands.Skip(1))
        {
            Assert.Contains("LEFT JOIN (SELECT", read.Sql);
            Assert.Contains("metarill_candidate_order.sort_alias DESC", read.Sql);
            Assert.Contains("b.id ASC", read.Sql);
            Assert.Matches(@"priority\s*>=\s*@candidate_order_", read.Sql);
            Assert.Contains(read.Values, pair => pair.Key.StartsWith("candidate_order_", StringComparison.Ordinal) && Convert.ToInt64(pair.Value) == 10);
            Assert.DoesNotContain("sort_alias", read.Sql[..read.Sql.IndexOf(" FROM ", StringComparison.Ordinal)]);
        }
        Assert.DoesNotContain(" LIKE ", provider.Commands[2].Sql);
        Assert.Contains(id, provider.Commands[2].Values.Values);
        Assert.Equal(5001, provider.Commands[3].Values["page_limit"]);

        async Task<object> Invoke(string name, object request, bool legacy = false)
        {
            object?[] args = legacy ? [request, actor, CancellationToken.None, null] : [request, actor, CancellationToken.None];
            var task = (Task)serviceType.GetMethod(name)!.Invoke(service, args)!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }

        static void CheckName(object options)
        {
            var option = Assert.Single(((System.Collections.IEnumerable)options).Cast<object>());
            Assert.Equal("原名称", option.GetType().GetProperty("Name")!.GetValue(option));
        }
    }

    private sealed class TreeAuthorization(RuntimeActor actor) : IRuntimeAuthorization
    {
        internal List<string> Pages { get; } = [];
        public RuntimeActor RequireActor() => actor;
        public void RequirePagePermission(string pageId, string operation)
        {
            Assert.Equal("list", operation);
            Pages.Add(pageId);
        }
    }

    private sealed record Captured(string Sql,IReadOnlyDictionary<string,object?> Values,DbTransaction? Transaction);

    /// <summary>仅记录驱动输入并返回声明的合成行，任何数据库写入或实际联网均拒绝。</summary>
    private sealed class CaptureProvider(object code,long recordId,Func<DbCommand,DataTable>? rows=null):IDatabaseProvider
    {
        private readonly MySqlDatabaseProvider dialect=new();
        internal object Code=>code;
        internal long RecordId=>recordId;
        internal List<Captured> Commands { get; }=[];
        internal int Opens { get; private set; }
        public string Name=>"mysql";
        public DatabaseCapabilities Capabilities=>dialect.Capabilities;
        public void Validate(DatabaseOptions options){}
        public string QuoteIdentifier(string name)=>dialect.QuoteIdentifier(name);
        public string ParameterPlaceholder(string name)=>dialect.ParameterPlaceholder(name);
        public string BuildSelectForUpdate(string sql)=>dialect.BuildSelectForUpdate(sql);
        public IFreeSql CreateOrm(DatabaseOptions options)=>rows is null
            ? throw new InvalidOperationException("固定候选测试不通过 ORM 读取。")
            : dialect.CreateOrm(options);
        public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken token=default)=>
            connection.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);

        private DataTable? Rows(DbCommand command) => rows?.Invoke(command);

        private void Capture(DbCommand command)=>Commands.Add(new(command.CommandText.Replace("\u0060","",StringComparison.Ordinal),
            command.Parameters.Cast<DbParameter>().ToDictionary(parameter=>parameter.ParameterName,parameter=>(object?)parameter.Value),
            command.Transaction));

        private sealed class Connection(CaptureProvider provider):DbConnection
        {
            private ConnectionState state;
            [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString{get;set;}="";
            public override string Database=>"offline";
            public override string DataSource=>"offline";
            public override string ServerVersion=>"offline";
            public override ConnectionState State=>state;
            public override void Open(){provider.Opens++;state=ConnectionState.Open;}
            public override void Close()=>state=ConnectionState.Closed;
            public override void ChangeDatabase(string databaseName)=>throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)=>new Transaction(this,isolationLevel);
            protected override DbCommand CreateDbCommand()=>new Command(provider,this);
        }

        private sealed class Transaction(Connection connection,IsolationLevel isolation):DbTransaction
        {
            public override IsolationLevel IsolationLevel=>isolation;
            protected override DbConnection DbConnection=>connection;
            public override void Commit(){}
            public override void Rollback(){}
        }

        private sealed class Command(CaptureProvider provider,Connection connection):DbCommand
        {
            private readonly MySqlCommand parameters=new();
            [System.Diagnostics.CodeAnalysis.AllowNull] public override string CommandText{get;set;}="";
            public override int CommandTimeout{get;set;}
            public override CommandType CommandType{get;set;}
            public override bool DesignTimeVisible{get;set;}
            public override UpdateRowSource UpdatedRowSource{get;set;}
            protected override DbConnection? DbConnection{get;set;}=connection;
            protected override DbTransaction? DbTransaction{get;set;}
            protected override DbParameterCollection DbParameterCollection=>parameters.Parameters;
            protected override DbParameter CreateDbParameter()=>new MySqlParameter();
            public override void Cancel(){}
            public override void Prepare(){}
            public override int ExecuteNonQuery()=>throw new InvalidOperationException("固定候选测试禁止写入。");
            public override object ExecuteScalar(){provider.Capture(this);return 1L;}
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                provider.Capture(this);
                if (provider.Rows(this) is { } fixedRows) return fixedRows.CreateDataReader();
                var table=new DataTable();
                table.Columns.Add("id",typeof(long));
                table.Columns.Add("code",provider.Code.GetType());
                table.Rows.Add(provider.RecordId,provider.Code);
                return table.CreateDataReader();
            }
            protected override void Dispose(bool disposing)
            {
                if(disposing)parameters.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
