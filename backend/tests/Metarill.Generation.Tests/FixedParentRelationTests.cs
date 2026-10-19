using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>两个平台固定单跳关系离线验收；真实数据库范围及并发由独占阶段另验。</summary>
public sealed class FixedParentRelationTests
{
    private const string TargetPage = "6217715532627968";
    private static readonly (string Table, string Page, string Column, string Owner)[] Sources =
    [("ConfigIndexColumn", "2076977554350190593", "2076977805534564352", "2076977821456142336"), ("ConfigPageColumn", "6218036784398336", "1676901024774926338", "1671100304320462849")];

    /// <summary>声明按稳定字段ID闭合全部边，客户端上下文不能替换目标父。</summary>
    [Fact]
    public void Both_published_bindings_close_all_explicit_edges_and_keep_only_two_other_gaps()
    {
        var catalog = Catalog(); var before = JsonSerializer.Serialize(catalog); var model = GenerationModel.Create(catalog, "Example.FixedParent");
        foreach (var source in Sources)
        {
            var page = model.Tables.SelectMany(table => table.Pages).Single(page => page.Source.Id == source.Page);
            var relation = RelationCompilation.ForPage(model, page).Single(relation => relation.SourceColumn.Source.Id == source.Column);
            var binding = relation.ParentBinding!.Binding!;
            Assert.Null(relation.ParentBinding.Reason); Assert.Equal(source.Owner, binding.OwnerColumn.Source.Id);
            Assert.Equal(binding.SourceParent.Source.Id, binding.SourceParentColumn.Source.LinkConfigTableId);
            Assert.Equal(binding.TargetParent.Source.Id, binding.OwnerColumn.Source.LinkConfigTableId);
            Assert.Equal(binding.TargetParent.Source.Id, binding.TargetParentColumn.Source.LinkConfigTableId);
            var context = ControlRenderer.RelationContext(page, relation, false);
            Assert.True(context.ServerDerivedTargetParent); Assert.False(context.UnresolvedTarget); Assert.Null(context.TargetParentField); Assert.False(context.SameParent);
            Assert.Equal(TargetPage, relation.TargetPage.Source.Id);
        }
        Assert.Equal(2, GenerationCapabilityAudit.Collect(model).Count); Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>声明缺边或错边保留Gap，不能回退客户端任意targetParentId或递归猜归属。</summary>
    [Theory]
    [InlineData("ownerMissing")] [InlineData("ownerWrongTable")] [InlineData("ownerLink")] [InlineData("sourceLink")] [InlineData("targetLink")]
    [InlineData("ownerType")] [InlineData("ownerJoin")] [InlineData("overlayOverride")] [InlineData("objectDeclaration")]
    public void Unknown_or_inconsistent_binding_remains_a_gap(string scenario)
    {
        var catalog = Catalog(); var source = catalog.ConfigColumns.Single(column => column.Id == Sources[1].Column); var definition = JsonNode.Parse(source.ControlDetail)!;
        if (scenario == "ownerMissing") definition[FixedParentRelationCompilation.Declaration] = "unknown";
        if (scenario == "ownerWrongTable") definition[FixedParentRelationCompilation.Declaration] = Sources[0].Owner;
        if (scenario == "objectDeclaration") definition[FixedParentRelationCompilation.Declaration] = new JsonObject { ["path"] = "anything" };
        source.ControlDetail = definition.ToJsonString();
        var owner = catalog.ConfigColumns.Single(column => column.Id == Sources[1].Owner);
        if (scenario == "ownerLink") owner.LinkConfigTableId = "";
        if (scenario == "sourceLink") catalog.ConfigColumns.Single(column => column.Id == "1676901024972058625").LinkConfigTableId = "";
        if (scenario == "targetLink") catalog.ConfigColumns.Single(column => column.Id == "1670724765850193921").LinkConfigTableId = "";
        if (scenario == "ownerType") owner.LogicalType = "string";
        if (scenario == "ownerJoin") owner.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        if (scenario == "overlayOverride") catalog.ConfigPageColumns.Single(field => field.ConfigPageId == Sources[1].Page && field.ConfigColumnId == source.Id).Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"sourceParentOwnerColumnId\":\"unknown\"}");
        var model = GenerationModel.Create(catalog, "Example.BadFixedParent"); var page = model.Tables.SelectMany(table => table.Pages).Single(page => page.Source.Id == Sources[1].Page);
        var relation = RelationCompilation.ForPage(model, page).Single(relation => relation.SourceColumn.Source.Id == source.Id);
        Assert.True(relation.ParentBinding!.Declared); Assert.Null(relation.ParentBinding.Binding); Assert.NotNull(relation.ParentBinding.Reason);
        Assert.True(ControlRenderer.RelationContext(page, relation, false).UnresolvedTarget);
        Assert.Contains(GenerationCapabilityAudit.Collect(model), gap => gap.PageId == page.Source.Id && gap.Column == "config_column_id");
    }

    /// <summary>隐藏关联ID仍可保存其他字段；未知声明明确失败且BackendOnly保持可编译，不额外开放隐藏字段候选。</summary>
    [Theory]
    [InlineData("hidden")] [InlineData("unknown")]
    public async Task Hidden_or_unknown_projection_still_compiles_without_broadening_the_api(string scenario)
    {
        var catalog = Catalog();
        if (scenario == "hidden") catalog.ConfigPageColumns.RemoveAll(field => field.ConfigPageId == Sources[1].Page && field.ConfigColumnId == Sources[1].Column);
        else
        {
            var column = catalog.ConfigColumns.Single(column => column.Id == Sources[1].Column); var json = JsonNode.Parse(column.ControlDetail)!;
            json[FixedParentRelationCompilation.Declaration] = "unpublished"; column.ControlDetail = json.ToJsonString();
        }
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-parent-projection"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.ParentProjection"); var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var controller = assembly.GetType("Example.ParentProjection.Generated.Controllers.ConfigPageColumnPage" + Sources[1].Page + "Controller", true)!;
            var service = assembly.GetType("Example.ParentProjection.Generated.Services.ConfigPageColumnService", true)!; var suffix = Sources[1].Page + "_" + Sources[1].Column;
            if (scenario == "hidden")
            {
                Assert.Null(controller.GetMethod("findNameIdConfigColumn" + Sources[1].Column)); Assert.Null(service.GetMethod("Options" + suffix + "Async"));
                Assert.NotNull(service.GetMethod("OptionsScope" + suffix + "Async", BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.NotNull(service.GetMethod("ValidateBound" + suffix + "Async", BindingFlags.NonPublic | BindingFlags.Instance));
                Assert.Null(assembly.GetType("Example.ParentProjection.Generated.VOs.ConfigPageColumnPage" + Sources[1].Page + "Vo", true)!.GetProperty("ConfigColumnId"));
            }
            else
            {
                // 故意不构造session；未知声明必须在任何数据库或授权对象读取之前作固定失败。
                var instance = RuntimeHelpers.GetUninitializedObject(service); var method = service.GetMethod("Options" + suffix + "Async")!;
                var task = (Task)method.Invoke(instance, [new PageRequest { ParentId = "9007199254740993" }, new RuntimeActor(1, "离线", false, catalog.YzProjectId, "1"), CancellationToken.None, null])!;
                Assert.Contains("固定源父", (await Assert.ThrowsAsync<InvalidOperationException>(() => task)).Message);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>实际平台整批编译，Service先目标授权，最终关系检查处于写入之前；离线连接工厂禁止任何网络连接。</summary>
    [Fact]
    public async Task Generated_platform_compiles_and_checks_target_permission_before_any_relation_read()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-fixed-parent"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "generated");
        try
        {
            var catalog = Catalog();
            // 私有编译夹具额外启用编辑导出，核验未来开关不把来源export权限误收紧为list。
            foreach (var source in Sources) catalog.ConfigPages.Single(page => page.Id == source.Page).Extra["exportMethod"] = JsonSerializer.SerializeToElement("edit");
            new CSharpGenerator().Generate(catalog, output, "Example.FixedParent"); var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var opens = 0;
            using var orm = new FreeSqlBuilder().UseConnectionFactory(DataType.MySql, () => new DeniedConnection(() => opens++)).UseAutoSyncStructure(false).UseGenerateCommandParameterWithLambda(true).Build();
            using var session = new DatabaseSession(new() { Provider = "mysql", ExpectedDatabase = "metarill_business_test", ConnectionString = "Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;" }, orm);
            var authorization = new TargetDeniedAuthorization();
            foreach (var source in Sources)
            {
                var serviceType = assembly.GetType("Example.FixedParent.Generated.Services." + source.Table + "Service", true)!;
                var mapper = Activator.CreateInstance(assembly.GetType("Example.FixedParent.Generated.Mappers." + source.Table + "Mapper", true)!, [session]);
                var parameters = serviceType.GetConstructors().Single().GetParameters(); var args = new object?[parameters.Length]; args[0] = session; args[1] = mapper; args[2] = new SnowflakeIdGenerator(865); args[3] = Array.Empty<IRecordMutationObserver>();
                for (var index = 0; index < parameters.Length; index++) if (parameters[index].ParameterType == typeof(IRuntimeAuthorization)) args[index] = authorization;
                var service = Activator.CreateInstance(serviceType, args)!; var suffix = source.Page + "_" + source.Column;
                foreach (var (method, request) in new (string, object)[] { ("Options" + suffix + "Async", new PageRequest { ParentId = "9007199254740993" }), ("OptionsPage" + suffix + "Async", new RelationOptionsPageRequest { ParentId = "9007199254740993" }), ("OptionsSelected" + suffix + "Async", new RelationOptionsSelectedRequest { ParentId = "9007199254740993", SelectedIds = ["9007199254740994"] }) })
                {
                    authorization.Requested.Clear();
                    var info = serviceType.GetMethod(method)!; var call = info.GetParameters().Length == 4 ? new object?[] { request, authorization.Actor, CancellationToken.None, null } : [request, authorization.Actor, CancellationToken.None];
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)info.Invoke(service, call)!);
                    Assert.Equal(new[] { source.Page + ":list", TargetPage + ":list" }, authorization.Requested);
                }
                var targetType = assembly.GetType("Example.FixedParent.Generated.Entities.ConfigColumn", true)!;
                var query = typeof(DatabaseSession).GetMethod(nameof(DatabaseSession.Select))!.MakeGenericMethod(targetType).Invoke(session, null)!;
                var scoped = serviceType.GetMethod("ApplyBoundScope" + suffix, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [query, 9007199254742000L, authorization.Actor])!;
                var sql = (string)typeof(FixedParentRelationTests).GetMethod(nameof(Sql), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(targetType).Invoke(null, [scoped])!;
                Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sql, @"\bEXISTS\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
                Assert.Contains(source.Table == "ConfigIndexColumn" ? "`config_index`" : "`config_page`", sql);
                Assert.Contains("`config_table`", sql); Assert.Contains("`delete_no`", sql); Assert.Contains("`yz_project_id`", sql);
                Assert.Matches(@"[A-Za-z0-9_]+\.`config_table_id`\s*=\s*[A-Za-z0-9_]+\.`config_table_id`", sql);
                Assert.DoesNotContain("9007199254742000", sql); Assert.DoesNotContain(authorization.Actor.ProjectId, sql);
                var optionsMethod = serviceType.GetMethod("Options" + suffix + "Async")!;
                authorization.Requested.Clear();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)optionsMethod.Invoke(service, [new PageRequest { ParentId = "9007199254740993" }, new RuntimeActor(1, "伪项目", false, "999", "1"), CancellationToken.None, null])!);
                Assert.Empty(authorization.Requested);
                var sourceAuthorization = new TargetDeniedAuthorization(source.Page);
                for (var index = 0; index < parameters.Length; index++) if (parameters[index].ParameterType == typeof(IRuntimeAuthorization)) args[index] = sourceAuthorization;
                var sourceDenied = Activator.CreateInstance(serviceType, args)!;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)optionsMethod.Invoke(sourceDenied, [new PageRequest { ParentId = "9007199254740993" }, authorization.Actor, CancellationToken.None, null])!);
                Assert.Equal(new[] { source.Page + ":list" }, sourceAuthorization.Requested);
                sourceAuthorization.Requested.Clear();
                serviceType.GetMethod("AuthorizeBound" + suffix, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sourceDenied, [authorization.Actor, false]);
                Assert.Equal(new[] { TargetPage + ":list" }, sourceAuthorization.Requested);
                authorization.Requested.Clear();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)serviceType.GetMethod("Export" + source.Page + "Async")!.Invoke(service, [new PageRequest { ParentId = "9007199254740993" }, authorization.Actor, 100, CancellationToken.None])!);
                Assert.Equal(source.Page + ":export", authorization.Requested[0]);
                Assert.Contains(TargetPage + ":list", authorization.Requested);
                Assert.DoesNotContain(source.Page + ":list", authorization.Requested);
                for (var index = 0; index < parameters.Length; index++) if (parameters[index].ParameterType == typeof(IRuntimeAuthorization)) args[index] = null;
                var unauthenticated = Activator.CreateInstance(serviceType, args)!;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)optionsMethod.Invoke(unauthenticated, [new PageRequest { ParentId = "9007199254740993" }, authorization.Actor, CancellationToken.None, null])!);
                var generated = await File.ReadAllTextAsync(Path.Combine(output, "Services", source.Table + "Service.g.cs"));
                Assert.Contains("targetParentId与服务器固定归属不一致", generated);
                Assert.Contains("OptionsScope" + suffix + "Async(request.ParentId, groupConfigColumnId.Key, actor, cancellationToken, false, false)", generated);
                Assert.DoesNotContain("formParameters", generated);
                Assert.Contains("if (!locking) query = ApplyBoundScope" + suffix, generated);
                Assert.Contains("if (locking) sourceQuery = sourceQuery.ForUpdate();", generated);
                Assert.Contains("if (locking) ownerQuery = ownerQuery.ForUpdate();", generated);
                if (source.Table == "ConfigIndexColumn")
                {
                    Assert.Contains("query.OrderBy(entity => entity.SortNo).OrderBy(entity => entity.Id)", generated);
                    Assert.Contains("entity.IsVirtualColumn", generated); Assert.Contains("entity.IsJoinColumn", generated);
                    Assert.Null(serviceType.GetMethod("Update" + source.Page + "Async")); Assert.Null(serviceType.GetMethod("Delete" + source.Page + "Async"));
                }
                else
                {
                    Assert.DoesNotContain("entity.IsVirtualColumn", generated);
                    var update = generated[generated.IndexOf("public Task<ConfigPageColumnPage", StringComparison.Ordinal)..];
                    Assert.True(update.IndexOf("AuthorizeBound", StringComparison.Ordinal) < update.IndexOf("await ValidateRelations", StringComparison.Ordinal));
                    Assert.True(update.IndexOf("await ValidateRelations", StringComparison.Ordinal) < update.IndexOf("await ValidateBound", StringComparison.Ordinal));
                    Assert.True(update.IndexOf("await ValidateBound", StringComparison.Ordinal) < update.IndexOf("mapper.UpdateAsync", StringComparison.Ordinal));
                    var delete = generated[generated.IndexOf("public Task<bool> Delete", StringComparison.Ordinal)..];
                    Assert.True(delete.IndexOf("await ValidateBound", StringComparison.Ordinal) < delete.IndexOf("entity.DeleteNo =", StringComparison.Ordinal));
                }
            }
            Assert.Equal(0, opens); await Probe(output);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>实际FreeSql解析生成的相关子查询，不执行SQL，也不以字符串拼接模拟翻译。</summary>
    private static string Sql<T>(object query) where T : class => ((ISelect<T>)query).ToSql();

    /// <summary>允许ORM创建连接对象检查类型，但Open/命令始终拒绝，测试不会触发网络或数据库。</summary>
    private sealed class DeniedConnection(Action opening) : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull] public override string ConnectionString { get; set; } = "Server=offline;Database=metarill_business_test;";
        public override string Database => "metarill_business_test";
        public override string DataSource => "offline";
        public override string ServerVersion => "8.4.11";
        public override ConnectionState State => ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new InvalidOperationException("离线测试禁止切换数据库。");
        public override void Open() { opening(); throw new InvalidOperationException("离线测试禁止打开连接。"); }
        public override void Close() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new InvalidOperationException("离线测试禁止开启事务。");
        protected override DbCommand CreateDbCommand() => throw new InvalidOperationException("离线测试禁止创建命令。");
    }

    /// <summary>真实项目源仅作离线编译输入，所有连接准备都不能接触共享数据库。</summary>
    private static MetadataCatalog Catalog([CallerFilePath] string file = "") => JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../metadata/platform.json"))), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private sealed class TargetDeniedAuthorization(string deniedPage = TargetPage) : IRuntimeAuthorization
    {
        internal RuntimeActor Actor { get; } = new(1, "离线固定父", false, "1608097412978577409", "1");
        internal List<string> Requested { get; } = [];
        public RuntimeActor RequireActor() => Actor;
        public void RequirePagePermission(string pageId, string operation) { Requested.Add(pageId + ":" + operation); if (pageId == deniedPage) throw new UnauthorizedAccessException("目标页禁止读取。"); }
    }
    /// <summary>实际Vue挂载验证原请求只传来源parentId，不添加父授权props或隐藏字段取值。</summary>
    private static async Task Probe(string output, [CallerFilePath] string file = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!, "FixedParentRelationProbe.mjs")); start.ArgumentList.Add(output);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
