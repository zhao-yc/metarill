using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行真实生成方法；明确离线ORM和事务代理仅验证错误分类，不冒充MySQL回滚验收。</summary>
public sealed class PageImportErrorTests
{
    [Fact]
    public void Fixed_messages_preserve_location_and_never_copy_inner_messages()
    {
        var inner = new JsonException("虚构内部SQL/路径/连接信息");
        var input = PageImportException.Input(PageImportError.Number, columnName: "金额", inner: inner).AtRow(2);
        Assert.Equal("第2行，列「金额」：应填写有效数字。", input.Message); Assert.Same(inner, input.InnerException);
        Assert.Same(input, input.AtRow(3)); Assert.Equal(2, input.RowNumber);
        Assert.Throws<ArgumentOutOfRangeException>(() => input.AtRow(0)); Assert.Throws<ArgumentOutOfRangeException>(() => input.AtRow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageImportException.Input(PageImportError.Number, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PageImportException.Input((PageImportError)999));
        foreach (var code in new[] { PageImportError.UnknownHeader, PageImportError.DuplicateHeader })
        { var error = PageImportException.Input(code, 2, "原始未知标题不准回显"); Assert.Null(error.ColumnName); Assert.DoesNotContain("原始未知", error.Message); }
        var execution = PageImportException.Execution(2, inner); Assert.False(execution.IsInputError); Assert.Same(inner, execution.InnerException);
        Assert.Equal("导入第2行执行失败。", execution.Message); Assert.Throws<InvalidOperationException>(() => execution.AtRow(3));
    }

    [Fact]
    public async Task Actual_generated_import_identifies_second_data_row_and_fixed_chinese_column_without_a_database()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-import-errors"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(), Path.Combine(directory, "generated"), "Example.ImportErrors");
            // 手写partial仅在本临时夹具制造规则/权限/取消失败；不改生成标准产物。
            await File.WriteAllTextAsync(Path.Combine(directory, "generated/Services/CustomerService.cs"), """
using Metarill.Core.Security;
namespace Example.ImportErrors.Generated.Services;
public partial class CustomerService
{
    partial void BeforeSave(Example.ImportErrors.Generated.Entities.Customer entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    {
        if (entity.Name == "内部失败") throw new InvalidOperationException("虚构SQL SELECT private; E:/fixture/private; Server=fixture;Password=synthetic;");
        if (entity.Name == "身份失败") throw new UnauthorizedAccessException("身份夹具");
        if (entity.Name == "权限失败") throw new System.Security.SecurityException("权限夹具");
        if (entity.Name == "取消失败") throw new OperationCanceledException("取消夹具");
    }
}
""");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var probe = new OfflineOrm(); using var session = new DatabaseSession(new(), probe.Orm, probe);
            var serviceType = assembly.GetType("Example.ImportErrors.Generated.Services.CustomerService", true)!;
            var mapper = Activator.CreateInstance(assembly.GetType("Example.ImportErrors.Generated.Mappers.CustomerMapper", true)!, session);
            var service = Activator.CreateInstance(serviceType, session, mapper, new SnowflakeIdGenerator(866), Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>())!;
            var requestType = assembly.GetType("Example.ImportErrors.Generated.DTOs.CustomerPage101ImportRequest", true)!;
            var method = serviceType.GetMethod("Import101Async")!; var actor = new RuntimeActor(1, "离线导入", true, "100", "1");
            Task Call(string json) => (Task)method.Invoke(service, [JsonSerializer.Deserialize(json, requestType, new JsonSerializerOptions(JsonSerializerDefaults.Web)), actor, CancellationToken.None])!;
            foreach (var (cell, column, code) in new[]
            {
                ("\"金额\":\"不是数字\"", "金额", PageImportError.Number), ("\"金额\":1e1000", "金额", PageImportError.Number),
                ("\"数量\":2147483648", "数量", PageImportError.Number), ("\"长编号\":\"9223372036854775808\"", "长编号", PageImportError.Number),
                ("\"启用\":2", "启用", PageImportError.Boolean), ("\"日期\":\"不是日期\"", "日期", PageImportError.DateTime),
                ("\"附件\":{}", "附件", PageImportError.Array), ("\"状态\":\"不存在的选项\"", "状态", PageImportError.Constant)
            })
            {
                var inserts = probe.Inserts; var rolls = probe.Rollbacks;
                var error = await Assert.ThrowsAsync<PageImportException>(() => Call("{\"requestJson\":[{\"名称\":\"合法首行\",\"金额\":12.34},{\"名称\":\"坏次行\"," + cell + "}]}"));
                Assert.True(error.IsInputError); Assert.Equal(2, error.RowNumber); Assert.Equal(column, error.ColumnName); Assert.Equal(code, error.Code);
                Assert.Equal(inserts + 1, probe.Inserts); Assert.Equal(rolls + 1, probe.Rollbacks); Assert.DoesNotContain("不是数字", error.Message);
            }
            foreach (var (row, code) in new[] { ("{\"名称\":\"x\",\"未知原值SQL\":123}", PageImportError.UnknownHeader), ("{\"名称\":\"x\",\"名称\":\"y\"}", PageImportError.DuplicateHeader), ("[]", PageImportError.RowObject) })
            {
                var error = await Assert.ThrowsAsync<PageImportException>(() => Call("{\"requestJson\":[{\"名称\":\"首行\"}," + row + "]}"));
                Assert.True(error.IsInputError); Assert.Equal(2, error.RowNumber); Assert.Equal(code, error.Code); Assert.Null(error.ColumnName); Assert.DoesNotContain("SQL", error.Message);
            }
            var shape = await Assert.ThrowsAsync<PageImportException>(() => Call("{\"requestJson\":{}}")); Assert.Equal(PageImportError.RowsArray, shape.Code); Assert.Null(shape.RowNumber);
            await Call("{\"requestJson\":[{\"名称\":\"合法字符串\",\"金额\":\"12.30\",\"数量\":\"7\",\"长编号\":\"9007199254740993\",\"启用\":1,\"附件\":[],\"状态\":\"启用\"},{\"名称\":\"清空\",\"金额\":null,\"数量\":\"\",\"长编号\":null}]}" );
            var execution = await Assert.ThrowsAsync<PageImportException>(() => Call("{\"requestJson\":[{\"名称\":\"首行\"},{\"名称\":\"内部失败\"}]}"));
            Assert.False(execution.IsInputError); Assert.Equal(2, execution.RowNumber); Assert.IsType<InvalidOperationException>(execution.InnerException); Assert.DoesNotContain("SELECT", execution.Message);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Call("{\"requestJson\":[{\"名称\":\"身份失败\"}]}"));
            await Assert.ThrowsAsync<System.Security.SecurityException>(() => Call("{\"requestJson\":[{\"名称\":\"权限失败\"}]}"));
            await Assert.ThrowsAsync<OperationCanceledException>(() => Call("{\"requestJson\":[{\"名称\":\"取消失败\"}]}"));
            foreach (var matches in new[] { 0, 2 })
            {
                probe.Matches = matches;
                var relation = await Assert.ThrowsAsync<PageImportException>(() => Call("{\"requestJson\":[{\"名称\":\"首行\"},{\"名称\":\"关系失败\",\"类别\":\"无匹配或重名\"}]}"));
                Assert.True(relation.IsInputError); Assert.Equal(PageImportError.Relation, relation.Code); Assert.Equal(2, relation.RowNumber); Assert.Equal("类别", relation.ColumnName);
            }
            foreach (var failure in new Exception[] { new FormatException("SYNTHETIC-DRIVER-CONVERSION"), new JsonException("SYNTHETIC-DRIVER-JSON"), new IOException("SYNTHETIC-DRIVER-PATH") })
            {
                probe.ReadFailure = failure;
                var relation = await Assert.ThrowsAsync<PageImportException>(() => Call("{\"requestJson\":[{\"名称\":\"首行\"},{\"名称\":\"内部关系错误\",\"类别\":\"固定名称\"}]}"));
                Assert.False(relation.IsInputError); Assert.Equal(2, relation.RowNumber); Assert.Same(failure, relation.InnerException); Assert.DoesNotContain("SYNTHETIC", relation.Message);
            }
            probe.ReadFailure = new UnauthorizedAccessException("目标授权夹具");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Call("{\"requestJson\":[{\"名称\":\"关系身份\",\"类别\":\"固定名称\"}]}"));
            probe.ReadFailure = null;
            Assert.Equal(0, probe.Commands); Assert.True(probe.Commits > 0);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>固定元数据不包含数据库、任意SQL或客户端可选择目标；长数字按原字符串协议解析。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog(); catalog.ConfigPages[0].IsImport = 1;
        foreach (var (id, name, label, type, kind, detail) in new[] { ("21", "amount", "金额", "decimal", "", ""), ("22", "quantity", "数量", "int32", "", ""), ("23", "sequence", "长编号", "int64", "", ""), ("24", "enabled", "启用", "bool", "CheckBox", ""), ("25", "business_date", "日期", "datetime", "date", ""), ("26", "attachments", "附件", "string", "FileLibrary", "{}"), ("27", "status", "状态", "int32", "ConstList", "启用|1,停用|0") })
        { catalog.ConfigColumns.Add(new() { Id = id, ConfigTableId = "1", ColumnName = name, Name = label, LogicalType = type, ControlKind = kind, ControlDetail = detail }); catalog.ConfigPageColumns.Add(new() { Id = "1" + id, ConfigPageId = "101", ConfigColumnId = id, IsEdit = 1, IsShow = 1 }); }
        catalog.ConfigTables.Add(new() { Id = "2", TableName = "category", Name = "类别" });
        catalog.ConfigColumns.AddRange([new() { Id = "31", ConfigTableId = "2", ColumnName = "id", LogicalType = "int64" }, new() { Id = "32", ConfigTableId = "2", ColumnName = "name", LogicalType = "string", Name = "类别名称" }, new() { Id = "33", ConfigTableId = "2", ColumnName = "yz_project_id", LogicalType = "int64" }, new() { Id = "34", ConfigTableId = "2", ColumnName = "delete_no", LogicalType = "datetime" }, new() { Id = "28", ConfigTableId = "1", ColumnName = "category_id", LogicalType = "int64", Name = "类别", ControlKind = "List", LinkConfigTableId = "2", ControlDetail = "{\"tableId\":\"2\"}" }]);
        catalog.ConfigPages.Add(new() { Id = "201", ConfigTableId = "2", WhereSql = "yz_project_id=@current_yz_project_id" });
        catalog.ConfigPageColumns.AddRange([new() { Id = "128", ConfigPageId = "101", ConfigColumnId = "28", IsEdit = 1 }, new() { Id = "232", ConfigPageId = "201", ConfigColumnId = "32", IsShow = 1 }]);
        return catalog;
    }

    /// <summary>只返回固定写入计数，不执行SQL；事务/命令计数明确区分模拟与真实数据库行为。</summary>
    private sealed class OfflineOrm : IDatabaseProvider
    {
        internal int Inserts, Commits, Rollbacks, Commands, Matches; internal Exception? ReadFailure;
        internal IFreeSql Orm => ProxyFor<IFreeSql>((method, _) => method.Name switch { "Insert" => Insert(method.ReturnType), "Select" => Select(method.ReturnType, method.GetGenericArguments()[0]), _ => throw new NotSupportedException("离线ORM未开放" + method.Name) });
        private object Select(Type type, Type entity)
        {
            object? query = null;
            query = ProxyFor(type, (method, _) =>
            {
                if (method.Name == "ToListAsync")
                {
                    if (ReadFailure is not null) throw ReadFailure;
                    var listType = typeof(List<>).MakeGenericType(entity); var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
                    for (var index = 0; index < Matches; index++) { var row = Activator.CreateInstance(entity)!; entity.GetProperty("Id")!.SetValue(row, (long)index + 1); list.Add(row); }
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(listType).Invoke(null, [list]);
                }
                return query;
            });
            return query;
        }
        private object Insert(Type type)
        { object? command = null; command = ProxyFor(type, (method, _) => { if (method.Name == "ExecuteAffrowsAsync") { Inserts++; return Task.FromResult(1); } return command; }); return command; }
        public string Name => "offline"; public DatabaseCapabilities Capabilities => new("offline", false, false); public void Validate(DatabaseOptions _) { }
        public IFreeSql CreateOrm(DatabaseOptions _) => throw new NotSupportedException(); public DbConnection CreateConnection(DatabaseOptions _) => new Connection(this);
        public string QuoteIdentifier(string _) => throw new NotSupportedException(); public string ParameterPlaceholder(string _) => throw new NotSupportedException();
        private static T ProxyFor<T>(Func<MethodInfo, object?[]?, object?> action) where T : class => (T)ProxyFor(typeof(T), action);
        private static object ProxyFor(Type type, Func<MethodInfo, object?[]?, object?> action) { var proxy = DispatchProxy.Create(type, typeof(CallProxy)); ((CallProxy)proxy).Action = action; return proxy; }
        private sealed class Connection(OfflineOrm owner) : DbConnection
        {
            private ConnectionState state; [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = ""; public override string Database => "offline"; public override string DataSource => "offline"; public override string ServerVersion => "offline"; public override ConnectionState State => state;
            public override void ChangeDatabase(string _) => throw new NotSupportedException(); public override void Open() => state = ConnectionState.Open; public override void Close() => state = ConnectionState.Closed;
            protected override DbTransaction BeginDbTransaction(IsolationLevel _) => new Transaction(this, owner);
            protected override DbCommand CreateDbCommand() { owner.Commands++; throw new InvalidOperationException("离线测试不允许数据库命令。"); }
        }
        private sealed class Transaction(Connection connection, OfflineOrm owner) : DbTransaction
        { public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted; protected override DbConnection DbConnection => connection; public override void Commit() => owner.Commits++; public override void Rollback() => owner.Rollbacks++; }
    }
    public class CallProxy : DispatchProxy { internal Func<MethodInfo, object?[]?, object?> Action = null!; protected override object? Invoke(MethodInfo? method, object?[]? args) => Action(method!, args); }
}
