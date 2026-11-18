using System.Data;
using System.Data.Common;
using FreeSql;
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

    private sealed record Captured(string Sql,IReadOnlyDictionary<string,object?> Values,DbTransaction? Transaction);

    /// <summary>仅记录驱动输入并返回声明的合成行，任何数据库写入或实际联网均拒绝。</summary>
    private sealed class CaptureProvider(object code,long recordId):IDatabaseProvider
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
        public IFreeSql CreateOrm(DatabaseOptions options)=>throw new InvalidOperationException("固定候选测试不通过 ORM 读取。");
        public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken token=default)=>
            connection.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);

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
