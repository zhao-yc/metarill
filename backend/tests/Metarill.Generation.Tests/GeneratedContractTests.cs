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
            var catalog = GeneratorTests.Catalog(); catalog.ConfigColumns.Single(column => column.ColumnName == "remark").ControlKind = "FileLibrary";
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
            var controllerType = assembly.GetType("Example.Contract.Generated.Controllers.CustomerPage101Controller", true)!;
            var authorization = new DeniedAuthorization();
            var controller = Activator.CreateInstance(controllerType, [null, authorization])!;
            var task = (Task)controllerType.GetMethod("Page")!.Invoke(controller, [new PageRequest(), CancellationToken.None])!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
            Assert.Equal("101:list", authorization.Requested);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static bool ReadSpecified(object value) => (bool)value.GetType().GetProperty("IsSpecified")!.GetValue(value)!;
    private static string FindRepository([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    private sealed class DeniedAuthorization : IRuntimeAuthorization
    {
        public string Requested { get; private set; } = "";
        public RuntimeActor RequireActor() => throw new InvalidOperationException("拒绝权限后不应读取身份。");
        public void RequirePagePermission(string pageId, string operation) { Requested = pageId + ":" + operation; throw new UnauthorizedAccessException("没有页面权限。"); }
    }
}
