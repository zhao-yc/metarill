using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成编译、真实 HTTP 控制器与隔离 MySQL，验证超过 5000 项时完整范围没有旁路。</summary>
[Collection(GenerationMySqlCollection.Name)]
public sealed class GeneratedRelationOptionsMySqlTests
{
    private const long Project = 9007199254740993L;
    private const long Parent = 9007199254742001L;
    private const long First = 9007199254750000L;

    [StandaloneExportMySqlFact]
    public async Task Generated_list_options_page_and_selected_share_permissions_project_parent_and_literal_keyword_boundaries()
    {
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
        var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("关联分页仅允许明确的 MySQL 业务隔离测试库。");
        using var session = new DatabaseSession(options);
        var prefix = "mt_rel_" + Guid.NewGuid().ToString("N")[..12]; var catalog = RelationOptionsContractTests.Catalog(prefix);
        catalog.ConfigPages.Single(page => page.Id == "301").Extra["type"] = JsonSerializer.SerializeToElement(2);
        // 第二来源页使用本表记录作父级，目标仍使用独立父表，不能把两个长 ID 混为一个。
        catalog.ConfigColumns.Add(new() { Id = "28", ConfigTableId = "2", ColumnName = "source_owner_id", LogicalType = "int64", LinkConfigTableId = "2" });
        catalog.ConfigPages.Add(new() { Id = "202", ConfigTableId = "2", WhereSql = "yz_project_id=@current_yz_project_id AND source_owner_id=@parentId" });
        catalog.ConfigPageColumns.AddRange([new() { Id = "20201", ConfigPageId = "202", ConfigColumnId = "22" }, new() { Id = "20202", ConfigPageId = "202", ConfigColumnId = "25" }]);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-relation-mysql"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        WebApplication? app = null; var cleanup = false;
        try
        {
            var schema = new SchemaService(session); Assert.Empty((await schema.InspectAsync(catalog)).Tables);
            var plan = await schema.PreviewAsync(catalog); Assert.Equal(3, plan.Changes.Count); cleanup = true;
            Assert.True((await schema.ApplyAsync(plan)).Succeeded);
            await InsertParents(session, prefix + "_parent"); await InsertTargets(session, prefix + "_target");
            await Execute(session, "INSERT INTO " + session.Provider.QuoteIdentifier(prefix + "_source") + " (id,name,yz_project_id,delete_no) VALUES (@id,'来源页父记录',@project,NULL),(@foreignId,'外项目来源父记录',@foreignProject,NULL),(@deletedId,'已删来源父记录',@project,@deleted)", ("id", First + 8000), ("project", Project), ("foreignId", First + 8001), ("foreignProject", Project + 1), ("deletedId", First + 8002), ("deleted", DateTime.UtcNow));
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.RelationProbe");
            var assembly = await BuildGenerated(directory);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:" + port);
            builder.Services.AddHttpContextAccessor(); builder.Services.AddSingleton(session); builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(857));
            builder.Services.AddScoped<IRuntimeAuthorization>(provider => new ProbeAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
            assembly.GetType("Example.RelationProbe.Generated.GeneratedRegistration", true)!.GetMethod("AddGeneratedServices")!.Invoke(null, [builder.Services, null]);
            builder.Services.AddControllers().AddApplicationPart(assembly).AddJsonOptions(configuration => configuration.JsonSerializerOptions.Converters.Add(new StringLongJsonConverter()));
            app = builder.Build();
            app.Use(async (context, next) =>
            {
                try { await next(); }
                catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
                { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(R<string>.Error(error.Message)); }
            });
            app.MapControllers(); await app.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(15) };
            var endpoint = "/api/admin/auto/" + prefix + "_source/201/findNameId" + Pascal(prefix + "_target") + "25";
            var page = Data(await Post(client, endpoint + "/page", new { parentId = Parent.ToString(), pageNo = 1, pageSize = 100 }));
            Assert.Equal(5014, long.Parse(page.GetProperty("total").ToString())); Assert.Equal(51, long.Parse(page.GetProperty("pages").ToString()));
            var firstPage = page.GetProperty("records").EnumerateArray().ToArray(); Assert.Equal(100, firstPage.Length);
            Assert.All(firstPage, AssertOption);
            Assert.Equal(100, firstPage.Select(row => row.GetProperty("id").GetString()).Distinct().Count());
            var second = Data(await Post(client, endpoint + "/page", new { parentId = Parent.ToString(), pageNo = 2, pageSize = 100 })).GetProperty("records").EnumerateArray().ToArray();
            Assert.Empty(firstPage.Select(Id).Intersect(second.Select(Id)));
            var selectedId = (First + 5007).ToString(); Assert.DoesNotContain(firstPage, row => Id(row) == selectedId);
            var selected = Data(await Post(client, endpoint + "/selected", new { parentId = Parent.ToString(), selectedIds = new[] { selectedId, selectedId } })).EnumerateArray().ToArray();
            Assert.Equal(selectedId, Id(Assert.Single(selected))); AssertOption(selected[0]);
            var overflow = await Post(client, endpoint, new { parentId = Parent.ToString(), formParameters = new { } });
            Assert.Equal(500, overflow.GetProperty("code").GetInt32()); Assert.Contains("/page", overflow.GetProperty("msg").GetString()); Assert.Equal(JsonValueKind.Null, overflow.GetProperty("data").ValueKind);
            // SQL 关键字符都是名称中的普通数据；LIKE 不能将 %/_ 当作未声明的通配符。
            var commands = new List<(string Sql, int Parameters)>();
            session.Orm.Aop.CommandBefore += (_, args) => { if (args.Command.CommandText.Contains(prefix + "_target", StringComparison.Ordinal)) commands.Add((args.Command.CommandText, args.Command.Parameters.Count)); };
            foreach (var (keyword, expected) in new[] { ("special%tag", "special%tag"), ("special_tag", "special_tag"), ("special'tag", "special'tag") })
            {
                commands.Clear(); var found = Data(await Post(client, endpoint + "/page", new { parentId = Parent.ToString(), keyword, pageSize = 10 }));
                Assert.Equal(1, long.Parse(found.GetProperty("total").ToString())); Assert.Equal(expected, Assert.Single(found.GetProperty("records").EnumerateArray()).GetProperty("name").GetString());
                Assert.NotEmpty(commands); Assert.All(commands, command => { Assert.True(command.Parameters > 0); Assert.DoesNotContain(keyword, command.Sql); Assert.DoesNotContain("alias", command.Sql, StringComparison.OrdinalIgnoreCase); });
            }
            Assert.Equal(0, long.Parse(Data(await Post(client, endpoint + "/page", new { parentId = Parent.ToString(), keyword = "' OR 1=1 --" })).GetProperty("total").ToString()));
            var ties = Data(await Post(client, endpoint + "/page", new { parentId = Parent.ToString(), keyword = "同名候选" })).GetProperty("records").EnumerateArray().ToArray();
            Assert.Equal(new[] { First.ToString(), (First + 1).ToString() }, ties.Select(Id));
            // 外项目、软删、其它父范围、未知记录任何一个混入，不能返回合法部分以掩盖状态变化。
            foreach (var invalid in new[] { First + 6000, First + 6001, First + 6002, First + 9999 })
                Assert.Equal(500, (await Post(client, endpoint + "/selected", new { parentId = Parent.ToString(), selectedIds = new[] { selectedId, invalid.ToString() } })).GetProperty("code").GetInt32());
            foreach (var parent in new string?[] { null, "1 OR 1=1", (Parent + 2).ToString(), (Parent + 3).ToString(), (Parent + 99).ToString() })
            {
                Assert.Equal(500, (await Post(client, endpoint + "/page", new { parentId = parent })).GetProperty("code").GetInt32());
                Assert.Equal(500, (await Post(client, endpoint + "/selected", new { parentId = parent, selectedIds = new[] { selectedId } })).GetProperty("code").GetInt32());
            }
            var otherParent = Data(await Post(client, endpoint, new { parentId = (Parent + 1).ToString() })).EnumerateArray().ToArray(); Assert.Equal((First + 6002).ToString(), Id(Assert.Single(otherParent)));
            var independent = endpoint.Replace("/201/", "/202/", StringComparison.Ordinal); var sourceParent = (First + 8000).ToString();
            Assert.Equal(5014, long.Parse(Data(await Post(client, independent + "/page", new { parentId = sourceParent, targetParentId = Parent.ToString() })).GetProperty("total").ToString()));
            Assert.Equal(selectedId, Id(Assert.Single(Data(await Post(client, independent + "/selected", new { parentId = sourceParent, targetParentId = Parent.ToString(), selectedIds = new[] { selectedId } })).EnumerateArray())));
            // 原数组及只读 id 也必须分别验证来源父和目标父，不能借 formParameters 选择目标。
            var legacyIndependent = independent.Replace("findNameId" + Pascal(prefix + "_target"), "findNameIdTarget", StringComparison.Ordinal);
            foreach (var legacyEndpoint in new[] { independent, legacyIndependent })
            {
                var bounded = Data(await Post(client, legacyEndpoint, new { parentId = sourceParent, targetParentId = (Parent + 1).ToString(), formParameters = new { } })).EnumerateArray().ToArray();
                Assert.Equal((First + 6002).ToString(), Id(Assert.Single(bounded)));
                Assert.Equal(selectedId, Id(Assert.Single(Data(await Post(client, legacyEndpoint, new { parentId = sourceParent, targetParentId = Parent.ToString(), id = selectedId })).EnumerateArray())));
                Assert.Empty(Data(await Post(client, legacyEndpoint, new { parentId = sourceParent, targetParentId = Parent.ToString(), id = "" })).EnumerateArray());
                foreach (var invalidTarget in new[] { (Parent + 2).ToString(), (Parent + 3).ToString(), sourceParent, "1 OR 1=1" })
                    foreach (var id in new string?[] { null, selectedId })
                        Assert.Equal(500, (await Post(client, legacyEndpoint, new { parentId = sourceParent, targetParentId = invalidTarget, id })).GetProperty("code").GetInt32());
                foreach (var invalidSource in new[] { (First + 8001).ToString(), (First + 8002).ToString(), Parent.ToString() })
                    Assert.Equal(500, (await Post(client, legacyEndpoint, new { parentId = invalidSource, targetParentId = Parent.ToString(), id = selectedId })).GetProperty("code").GetInt32());
                Assert.Equal(500, (await Post(client, legacyEndpoint, new { parentId = sourceParent, targetParentId = (Parent + 1).ToString(), id = selectedId })).GetProperty("code").GetInt32());
                foreach (var denied in new[] { "202", "301" })
                {
                    client.DefaultRequestHeaders.Add("denyPage", denied);
                    Assert.Equal(500, (await Post(client, legacyEndpoint, new { parentId = sourceParent, targetParentId = Parent.ToString(), id = selectedId })).GetProperty("code").GetInt32());
                    client.DefaultRequestHeaders.Remove("denyPage");
                }
            }
            foreach (var action in new[] { "", "/page", "/selected" })
            {
                var missingTarget = await Post(client, independent + action, action == "/selected" ? new { parentId = sourceParent, selectedIds = new[] { selectedId } } : (object)new { parentId = sourceParent });
                Assert.Equal(500, missingTarget.GetProperty("code").GetInt32()); Assert.Contains("/page", missingTarget.GetProperty("msg").GetString());
            }
            foreach (var invalid in new[] { (Parent + 2).ToString(), (Parent + 3).ToString(), sourceParent })
                Assert.Equal(500, (await Post(client, independent + "/page", new { parentId = sourceParent, targetParentId = invalid })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, independent + "/selected", new { parentId = Parent.ToString(), targetParentId = Parent.ToString(), selectedIds = new[] { selectedId } })).GetProperty("code").GetInt32());
            foreach (var invalid in new[] { (First + 8001).ToString(), (First + 8002).ToString() })
                Assert.Equal(500, (await Post(client, independent + "/selected", new { parentId = invalid, targetParentId = Parent.ToString(), selectedIds = new[] { selectedId } })).GetProperty("code").GetInt32());
            foreach (var denied in new[] { "201", "301" })
            {
                client.DefaultRequestHeaders.Add("denyPage", denied);
                foreach (var action in new[] { "", "/page", "/selected" })
                    Assert.Equal(500, (await Post(client, endpoint + action, action == "/selected" ? new { parentId = Parent.ToString(), selectedIds = new[] { selectedId } } : (object)new { parentId = Parent.ToString() })).GetProperty("code").GetInt32());
                client.DefaultRequestHeaders.Remove("denyPage");
            }
            foreach (var request in new object[] { new { parentId = Parent.ToString(), sql = "1=1" }, new { parentId = Parent.ToString(), pageId = "101" }, new { parentId = Parent.ToString(), table = prefix + "_parent" } })
                foreach (var action in new[] { "", "/page", "/selected" })
                    using (var rejected = await client.PostAsJsonAsync(endpoint + action, request)) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            foreach (var request in new object[] { new { parentId = Parent.ToString(), pageSize = 101 }, new { parentId = Parent.ToString(), keyword = new string('字', 101) } })
                Assert.Equal(500, (await Post(client, endpoint + "/page", request)).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, endpoint + "/selected", new { parentId = Parent.ToString(), selectedIds = Enumerable.Repeat(selectedId, 201).ToArray() })).GetProperty("code").GetInt32());
            // type=2独立选择页确认前回读完整VO；目标页list权限、真实父和项目/软删范围一致。
            var selection = "/api/admin/auto/" + prefix + "_target/301";
            var initial = new[] { selectedId, First.ToString() };
            var selectedRows = Data(await Post(client, selection + "/selection/selected", new { parentId = Parent.ToString(), selectedIds = initial })).EnumerateArray().ToArray();
            Assert.Equal(initial, selectedRows.Select(Id)); Assert.All(selectedRows, row => Assert.Equal(JsonValueKind.String, row.GetProperty("id").ValueKind));
            var selectedPage = Data(await Post(client, selection + "/page", new { parentId = Parent.ToString(), selectedIds = initial, pageSize = 1 }));
            Assert.Equal(2, long.Parse(selectedPage.GetProperty("total").ToString())); Assert.Single(selectedPage.GetProperty("records").EnumerateArray());
            foreach (var invalid in new[] { First + 6000, First + 6001, First + 6002, First + 9999 })
                Assert.Equal(500, (await Post(client, selection + "/selection/selected", new { parentId = Parent.ToString(), selectedIds = new[] { selectedId, invalid.ToString() } })).GetProperty("code").GetInt32());
            foreach (var parent in new string?[] { null, "1 OR 1=1", (Parent + 2).ToString(), (Parent + 3).ToString(), (Parent + 99).ToString() })
            {
                Assert.Equal(500, (await Post(client, selection + "/selection/selected", new { parentId = parent, selectedIds = initial })).GetProperty("code").GetInt32());
                Assert.Equal(500, (await Post(client, selection + "/page", new { parentId = parent, selectedIds = initial })).GetProperty("code").GetInt32());
            }
            client.DefaultRequestHeaders.Add("denyPage", "301");
            Assert.Equal(500, (await Post(client, selection + "/selection/selected", new { parentId = Parent.ToString(), selectedIds = initial })).GetProperty("code").GetInt32());
            client.DefaultRequestHeaders.Remove("denyPage");
            Assert.Equal(500, (await Post(client, selection + "/selection/selected", new { parentId = Parent.ToString(), selectedIds = Enumerable.Repeat(selectedId, 201).ToArray() })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, selection + "/page", new { parentId = Parent.ToString(), selectedIds = Enumerable.Repeat(selectedId, 201).ToArray() })).GetProperty("code").GetInt32());
            Assert.Equal(500, (await Post(client, selection + "/page", new { parentId = Parent.ToString(), pageNo = 1000001 })).GetProperty("code").GetInt32());
            using (var rejected = await client.PostAsJsonAsync(selection + "/selection/selected", new { parentId = Parent.ToString(), selectedIds = initial, table = prefix + "_source" })) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            await Execute(session, "UPDATE " + session.Provider.QuoteIdentifier(prefix + "_target") + " SET delete_no=@deleted WHERE id=@id", ("id", long.Parse(selectedId)), ("deleted", DateTime.UtcNow));
            Assert.Equal(500, (await Post(client, selection + "/selection/selected", new { parentId = Parent.ToString(), selectedIds = initial })).GetProperty("code").GetInt32());

        }
        finally
        {
            if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); }
            // 宿主释放可能已释放注入的 session；清理使用同一经过核实的独立测试配置新会话。
            if (cleanup) using (var cleanupSession = new DatabaseSession(options))
                foreach (var table in catalog.ConfigTables) await Execute(cleanupSession, "DROP TABLE IF EXISTS " + cleanupSession.Provider.QuoteIdentifier(table.TableName));
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>控制器仍走真实权限接口；测试身份固定，不提供客户端指定项目的通用认证入口。</summary>
    private sealed class ProbeAuthorization(IHttpContextAccessor accessor) : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => new(857, "关系分页验收", false, Project.ToString(), "1");
        public void RequirePagePermission(string pageId, string operation)
        { if (operation != "list" || accessor.HttpContext!.Request.Headers["denyPage"] == pageId) throw new UnauthorizedAccessException("没有页面读取权限。"); }
    }
    /// <summary>四字段且长 ID 为字符串，不能随着数据库类型或分页端点变化。</summary>
    private static void AssertOption(JsonElement option)
    { Assert.Equal(new[] { "id", "name", "text", "value" }, option.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray()); Assert.Equal(JsonValueKind.String, option.GetProperty("id").ValueKind); Assert.Equal(Id(option), option.GetProperty("value").GetString()); }
    /// <summary>只提取声明好的字符串 ID。</summary>
    private static string Id(JsonElement option) => option.GetProperty("id").GetString()!;
    /// <summary>读取真实 HTTP 响应，不在异常里输出连接配置。</summary>
    private static async Task<JsonElement> Post(HttpClient client, string url, object request) => JsonSerializer.Deserialize<JsonElement>(await (await client.PostAsJsonAsync(url, request)).Content.ReadAsStringAsync());
    /// <summary>成功断言只输出业务回执。</summary>
    private static JsonElement Data(JsonElement response) { Assert.Equal(200, response.GetProperty("code").GetInt32()); return response.GetProperty("data"); }
    /// <summary>与生成器一致地计算测试表类型名，名称来自测试自身受控前缀。</summary>
    private static string Pascal(string value) => string.Concat(value.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>父数据包含同项目、外项目和软删除状态，不复用任何开发业务记录。</summary>
    private static async Task InsertParents(DatabaseSession session, string table)
    {
        await Execute(session, "INSERT INTO " + session.Provider.QuoteIdentifier(table) + " (id,name,yz_project_id,delete_no) VALUES (@a,'有效父级',@project,NULL),(@b,'另一有效父级',@project,NULL),(@c,'外项目父级',@foreign,NULL),(@d,'已删父级',@project,@deleted)",
            ("a", Parent), ("b", Parent + 1), ("c", Parent + 2), ("d", Parent + 3), ("project", Project), ("foreign", Project + 1), ("deleted", DateTime.UtcNow));
    }
    /// <summary>批量参数化写入随机独占目标表，数据量足以揭示旧数组截断和分页回读问题。</summary>
    private static async Task InsertTargets(DatabaseSession session, string table)
    {
        for (var offset = 0; offset < 5014; offset += 500)
        {
            var values = new List<string>(); var parameters = new List<(string, object?)>();
            for (var index = offset; index < Math.Min(offset + 500, 5014); index++)
            {
                var name = index < 2 ? "同名候选" : index < 5010 ? "候选" + index.ToString("D5") : new[] { "special%tag", "special_tag", "special'tag", "specialXtag" }[index - 5010];
                values.Add("(@i" + index + ",@n" + index + ",@project,@parent)"); parameters.Add(("i" + index, First + index)); parameters.Add(("n" + index, name));
            }
            parameters.Add(("project", Project)); parameters.Add(("parent", Parent));
            await Execute(session, "INSERT INTO " + session.Provider.QuoteIdentifier(table) + " (id,name,yz_project_id,parent_id) VALUES " + string.Join(",", values), parameters.ToArray());
        }
        await Execute(session, "INSERT INTO " + session.Provider.QuoteIdentifier(table) + " (id,name,yz_project_id,parent_id,delete_no) VALUES (@foreign,'外项目',@other,@parent,NULL),(@deleted,'已删目标',@project,@parent,@now),(@child,'另一父级目标',@project,@another,NULL)",
            ("foreign", First + 6000), ("deleted", First + 6001), ("child", First + 6002), ("other", Project + 1), ("project", Project), ("parent", Parent), ("another", Parent + 1), ("now", DateTime.UtcNow));
    }
    /// <summary>仅用于明确测试表的参数化准备/清理 SQL，不接受任何 HTTP 输入。</summary>
    private static async Task Execute(DatabaseSession session, string sql, params (string Name, object? Value)[] values)
    {
        await using var connection = session.Provider.CreateConnection(session.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in values) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter); }
        await command.ExecuteNonQueryAsync();
    }
    /// <summary>真实引用 Core/Data 源码编译生成物，不用替身声明冒充生成项目可编译。</summary>
    internal static async Task<Assembly> BuildGenerated(string directory, [CallerFilePath] string source = "")
    {
        var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../.."));
        var core = SecurityElement.Escape(Path.Combine(repository, "backend/src/Metarill.Core/Metarill.Core.csproj")); var data = SecurityElement.Escape(Path.Combine(repository, "backend/src/Metarill.Data/Metarill.Data.csproj"));
        var assemblyName = "Metarill.Generated.Relation." + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(directory, "Relation.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>{{assemblyName}}</AssemblyName></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><ProjectReference Include="{{core}}"/><ProjectReference Include="{{data}}"/></ItemGroup></Project>
""");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "build", "Relation.csproj", "--nologo", "--verbosity", "quiet", "--disable-build-servers" }) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using (await SharedSourceBuildGate.Shared.EnterAsync())
        {
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); try { await process.WaitForExitAsync(timeout.Token); } catch (Exception interrupted) { await SharedSourceBuildGate.StopOwnProcessAsync(process, output, error, interrupted); throw; }
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(directory, "bin/Debug/net10.0", assemblyName + ".dll"))));
    }
}
