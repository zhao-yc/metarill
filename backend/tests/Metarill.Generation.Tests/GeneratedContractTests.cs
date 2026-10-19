using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成、引用真实 Data 编译，再验证生成 DTO 协议与授权顺序。</summary>
public sealed class GeneratedContractTests
{
    [Fact]
    public async Task Generated_project_compiles_and_preserves_presence_and_permission_contracts()
    {
        var root = Path.Combine(Path.GetTempPath(), "metarill-generated-contract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = GeneratorTests.Catalog(); catalog.ConfigPages[0].IsCopy = 1; catalog.ConfigPages[0].IsImport = 1; catalog.ConfigPages[0].IsExport = 1; catalog.ConfigPages[0].Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            catalog.ConfigColumns.Add(new() { Id = "16", ConfigTableId = "1", ColumnName = "status", LogicalType = "int32", Name = "状态", ControlKind = "ConstList", ControlDetail = "启用|1,停用|0" });
            catalog.ConfigColumns.Add(new() { Id = "17", ConfigTableId = "1", ColumnName = "sign_date", LogicalType = "datetime", Name = "签署日期", ControlKind = "date" });
            catalog.ConfigPageColumns.Add(new() { Id = "116", ConfigPageId = "101", ConfigColumnId = "16", IsEdit = 1, IsShow = 1 });
            catalog.ConfigPageColumns.Add(new() { Id = "117", ConfigPageId = "101", ConfigColumnId = "17", IsEdit = 1, IsShow = 1 }); catalog.ConfigColumns.Single(column => column.ColumnName == "remark").ControlKind = "FileLibrary";
            catalog.ConfigColumns.Add(new() { Id = "18", ConfigTableId = "1", ColumnName = "tier", LogicalType = "string", ControlKind = "ConstDownList", ControlDetail = "高\"级|vip,basic" });
            catalog.ConfigColumns.Add(new() { Id = "19", ConfigTableId = "1", ColumnName = "fee", LogicalType = "decimal", ControlKind = "ConstList", ControlDetail = "一个单位|1.00,同值重复|1" });
            catalog.ConfigColumns.Add(new() { Id = "20", ConfigTableId = "1", ColumnName = "rank", LogicalType = "int64", ControlKind = "ConstList", ControlDetail = "大整数|9007199254740993" });
            foreach (var id in new[] { "18", "19", "20" }) catalog.ConfigPageColumns.Add(new() { Id = "1" + id, ConfigPageId = "101", ConfigColumnId = id, IsEdit = 1, IsShow = 1 });
            new CSharpGenerator().Generate(catalog, Path.Combine(root, "generated"), "Example.Contract");
            var repository = FindRepository();
            var dataProject = Environment.GetEnvironmentVariable("METARILL_DATA_PROJECT") ?? Path.Combine(repository, "backend/src/Metarill.Data/Metarill.Data.csproj");
            if (!File.Exists(dataProject)) throw new InvalidOperationException("真实 Metarill.Data 项目尚未集成，不能用替身冒充生成产物编译验收。");
            var coreProject = Path.Combine(repository, "backend/src/Metarill.Core/Metarill.Core.csproj");
            File.WriteAllText(Path.Combine(root, "Contract.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><LangVersion>latest</LangVersion><AssemblyName>Metarill.Generated.Contract</AssemblyName></PropertyGroup>
  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><ProjectReference Include="{{SecurityElement.Escape(coreProject)}}" /><ProjectReference Include="{{SecurityElement.Escape(dataProject)}}" /></ItemGroup>
</Project>
""");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            start.ArgumentList.Add("build"); start.ArgumentList.Add("Contract.csproj"); start.ArgumentList.Add("--nologo"); start.ArgumentList.Add("--verbosity"); start.ArgumentList.Add("quiet"); start.ArgumentList.Add("--disable-build-servers"); start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(true); throw; }
            Assert.True(process.ExitCode == 0, await output.WaitAsync(timeout.Token) + await error.WaitAsync(timeout.Token));
            var assemblyPath = Path.Combine(root, "bin/Debug/net10.0/Metarill.Generated.Contract.dll");
            using var image = new MemoryStream(File.ReadAllBytes(assemblyPath));
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var updateType = assembly.GetType("Example.Contract.Generated.DTOs.CustomerPage101UpdateRequest", true)!;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new StringLongJsonConverter());
            var omitted = JsonSerializer.Deserialize("{\"id\":\"9007199254740993\"}", updateType, options)!;
            var cleared = JsonSerializer.Deserialize("{\"id\":\"9007199254740993\",\"remark\":null}", updateType, options)!;
            Assert.Equal(9007199254740993L, updateType.GetProperty("Id")!.GetValue(cleared));
            Assert.False(ReadSpecified(updateType.GetProperty("Remark")!.GetValue(omitted)!));
            Assert.True(ReadSpecified(updateType.GetProperty("Remark")!.GetValue(cleared)!));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"id\":\"1\",\"yzProjectId\":\"999\"}", updateType, options));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"id\":\"1\",\"unknownField\":\"x\"}", updateType, options));
            var attached = JsonSerializer.Deserialize("{\"id\":\"1\",\"remark\":[{\"fileId\":\"7\"}]}", updateType, options)!;
            Assert.True(ReadSpecified(updateType.GetProperty("Remark")!.GetValue(attached)!));
            var entityType = assembly.GetType("Example.Contract.Generated.Entities.Customer", true)!;
            var entity = Activator.CreateInstance(entityType)!;
            entityType.GetProperty("Id")!.SetValue(entity, 1L);
            entityType.GetProperty("Remark")!.SetValue(entity, "[{\"fileId\":\"7\"}]");
            var voType = assembly.GetType("Example.Contract.Generated.VOs.CustomerPage101Vo", true)!;
            var vo = voType.GetMethod("FromEntity")!.Invoke(null, [entity])!;
            using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(vo, voType, options));
            Assert.Equal(JsonValueKind.Array, serialized.RootElement.GetProperty("remark").ValueKind);
            Assert.Equal("7", serialized.RootElement.GetProperty("remark")[0].GetProperty("fileId").GetString());
            // 展示字段是只读派生值，原值、未知枚举与空值必须保持独立。
            entityType.GetProperty("Status")!.SetValue(entity, 1);
            var display = voType.GetMethod("FromEntity")!.Invoke(null, [entity])!;
            Assert.Equal("启用", voType.GetProperty("StatusShow")!.GetValue(display));
            Assert.Equal(1, voType.GetProperty("Status")!.GetValue(display));
            entityType.GetProperty("Status")!.SetValue(entity, 9);
            Assert.Equal("9", voType.GetProperty("StatusShow")!.GetValue(voType.GetMethod("FromEntity")!.Invoke(null, [entity])));
            entityType.GetProperty("Status")!.SetValue(entity, null);
            Assert.Null(voType.GetProperty("StatusShow")!.GetValue(voType.GetMethod("FromEntity")!.Invoke(null, [entity])));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"id\":\"1\",\"statusShow\":\"停用\"}", updateType, options));
            entityType.GetProperty("Tier")!.SetValue(entity, "vip");
            entityType.GetProperty("Fee")!.SetValue(entity, 1m);
            entityType.GetProperty("Rank")!.SetValue(entity, 9007199254740993L);
            display = voType.GetMethod("FromEntity")!.Invoke(null, [entity])!;
            Assert.Equal("高\"级", voType.GetProperty("TierShow")!.GetValue(display));
            Assert.Equal("一个单位", voType.GetProperty("FeeShow")!.GetValue(display));
            Assert.Equal("大整数", voType.GetProperty("RankShow")!.GetValue(display));
            entityType.GetProperty("Tier")!.SetValue(entity, "basic");
            Assert.Equal("basic", voType.GetProperty("TierShow")!.GetValue(voType.GetMethod("FromEntity")!.Invoke(null, [entity])));

            var controllerType = assembly.GetType("Example.Contract.Generated.Controllers.CustomerPage101Controller", true)!;
            var authorization = new DeniedAuthorization();
            var controller = Activator.CreateInstance(controllerType, [null, authorization])!;
            var task = (Task)controllerType.GetMethod("Page")!.Invoke(controller, [new PageRequest(), CancellationToken.None])!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
            Assert.Equal("101:list", authorization.Requested);
            var copyMethod = controllerType.GetMethod("Copy")!;
            Assert.Equal("copy/{id}", copyMethod.GetCustomAttributes().Single(attribute => attribute.GetType().Name == "HttpPostAttribute").GetType().GetProperty("Template")!.GetValue(copyMethod.GetCustomAttributes().Single(attribute => attribute.GetType().Name == "HttpPostAttribute")));
            var copyTask = (Task)copyMethod.Invoke(controller, [1L, null, CancellationToken.None])!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => copyTask);
            Assert.Equal("101:copy", authorization.Requested);
            var importType = assembly.GetType("Example.Contract.Generated.DTOs.CustomerPage101ImportRequest", true)!;
            var importRequest = JsonSerializer.Deserialize("{\"requestJson\":[{\"名称\":\"甲\"}],\"parentId\":null}", importType, options)!;
            var importMethod = controllerType.GetMethod("Import")!;
            var importTask = (Task)importMethod.Invoke(controller, [importRequest, CancellationToken.None])!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => importTask);
            Assert.Equal("101:import", authorization.Requested);
            var exportMethod = controllerType.GetMethod("InitExport")!;
            var exportSink = new DeniedExportSink();
            var exportTask = (Task)exportMethod.Invoke(controller, [new PageRequest(), exportSink, CancellationToken.None])!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exportTask);
            Assert.Equal("101:export", authorization.Requested); Assert.False(exportSink.Called);
            // 即使拥有导出权限，控制器也必须先交给门禁，不能提前访问空服务。
            var permitted = Activator.CreateInstance(controllerType, [null, new AllowedAuthorization()])!;
            var deferred = new DeferredExportSink();
            await (Task)exportMethod.Invoke(permitted, [new PageRequest(), deferred, CancellationToken.None])!;
            Assert.True(deferred.Called);
            var legacy = (Task)exportMethod.Invoke(permitted, [new PageRequest(), exportSink, CancellationToken.None])!;
            await Assert.ThrowsAsync<NotSupportedException>(() => legacy); Assert.False(exportSink.Called);
            var serviceType = assembly.GetType("Example.Contract.Generated.Services.CustomerService", true)!;
            var parser = serviceType.GetMethod("ParseImport101Async", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var parserInstance = RuntimeHelpers.GetUninitializedObject(serviceType);
            var actor = new RuntimeActor(1, "测试", true, "100", "1");
            using var input = JsonDocument.Parse("{\"名称\":\"甲\",\"备注\":[{\"fileId\":\"7\"}],\"状态\":\"启用\",\"签署日期\":\"2026-10-18 09:10:11\"}");
            var converted = await (Task<Dictionary<string, JsonElement>>)parser.Invoke(parserInstance, [input.RootElement, null, actor, CancellationToken.None])!;
            Assert.Equal("甲", converted["name"].GetString()); Assert.Equal(1, converted["status"].GetInt32());
            Assert.Equal(JsonValueKind.Array, converted["remark"].ValueKind); Assert.Equal(new DateTime(2026, 10, 18, 9, 10, 11), converted["signDate"].GetDateTime());
            using var forbidden = JsonDocument.Parse("{\"insertManagerId\":\"99\"}");
            await Assert.ThrowsAsync<ArgumentException>(() => (Task)parser.Invoke(parserInstance, [forbidden.RootElement, null, actor, CancellationToken.None])!);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static bool ReadSpecified(object value) => (bool)value.GetType().GetProperty("IsSpecified")!.GetValue(value)!;
    private static string FindRepository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    private sealed class AllowedAuthorization : IRuntimeAuthorization
    {
        /// <summary>此测试仅验证门禁先于服务读取，身份使用固定虚构账号。</summary>
        public RuntimeActor RequireActor() => new(1, "测试", true, "100", "1");
        public void RequirePagePermission(string pageId, string operation) { }
    }
    private sealed class DeferredExportSink : Metarill.Core.Exports.IPageExportService
    {
        public bool Called { get; private set; }
        public Task<string> CreateAsync(Metarill.Core.Exports.PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default) => throw new InvalidOperationException("不得提前读取。");
        /// <summary>不调用准备委托即成功，空服务不应被提前求值。</summary>
        public Task<string> CreateAsync(RuntimeActor actor, Func<int, CancellationToken, Task<Metarill.Core.Exports.PageExportRequest>> prepare, CancellationToken cancellationToken = default) { Called = true; return Task.FromResult("测试票据"); }
    }
    private sealed class DeniedExportSink : Metarill.Core.Exports.IPageExportService
    {
        public bool Called { get; private set; }
        /// <summary>未通过导出权限时不能创建任何文件或票据。</summary>
        public Task<string> CreateAsync(Metarill.Core.Exports.PageExportRequest request, RuntimeActor actor, CancellationToken cancellationToken = default) { Called = true; throw new InvalidOperationException("无权限时不应创建导出。"); }
    }
    private sealed class DeniedAuthorization : IRuntimeAuthorization
    {
        public string Requested { get; private set; } = "";
        public RuntimeActor RequireActor() => throw new InvalidOperationException("拒绝权限后不应读取身份。");
        public void RequirePagePermission(string pageId, string operation) { Requested = pageId + ":" + operation; throw new UnauthorizedAccessException("没有页面权限。"); }
    }
}
