using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Schema;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实编译生成服务，所有写入口在首次目标表访问之前执行同事务准备守卫。</summary>
public sealed class GeneratedMutationGuardMySqlTests
{
    [StandaloneExportMySqlFact]
    public async Task Every_generated_mutation_prepares_before_any_record_access_in_the_actual_transaction()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-generated-guard");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("METARILL_TEST_SETTINGS")!));
            var options = settings.RootElement.GetProperty("Metarill").GetProperty("BusinessDatabase").Deserialize<DatabaseOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (options.Provider != "mysql" || options.ExpectedDatabase != "metarill_business_test") throw new InvalidOperationException("生成准备守卫只允许明确的业务隔离测试库。");
            using var session = new DatabaseSession(options);
            var catalog = GeneratorTests.Catalog(); var table = catalog.ConfigTables.Single();
            table.TableName = "mt_missing_guard_" + Guid.NewGuid().ToString("N")[..12];
            foreach (var column in catalog.ConfigColumns.Where(column => column.LogicalType == "id")) column.LogicalType = "int64";
            var page = catalog.ConfigPages.Single(); page.IsCopy = 1; page.IsImport = 1;
            page.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            // 目标表必须不存在；任何入口在守卫之前读取、加锁或写入都会报 MySQL 缺表，而不会到达探针。
            Assert.Empty((await new SchemaService(session).InspectAsync(catalog)).Tables);
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.Guard");
            var repository = SourceRoot();
            var dataProject = Path.Combine(repository, "backend/src/Metarill.Data/Metarill.Data.csproj");
            var coreProject = Path.Combine(repository, "backend/src/Metarill.Core/Metarill.Core.csproj");
            var assemblyName = "Metarill.Generated.Guard." + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(directory, "Guard.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>{{assemblyName}}</AssemblyName></PropertyGroup>
<ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><ProjectReference Include="{{SecurityElement.Escape(coreProject)}}" /><ProjectReference Include="{{SecurityElement.Escape(dataProject)}}" /></ItemGroup></Project>
""");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "build", "Guard.csproj", "--nologo", "--verbosity", "quiet", "--disable-build-servers" }) start.ArgumentList.Add(argument);
            start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            using (await SharedSourceBuildGate.Shared.EnterAsync())
            {
                using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try { await process.WaitForExitAsync(timeout.Token); } catch (Exception interrupted) { await SharedSourceBuildGate.StopOwnProcessAsync(process, output, error, interrupted); throw; }
                Assert.True(process.ExitCode == 0, await output + await error);
            }
            using var image = new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(directory, "bin/Debug/net10.0", assemblyName + ".dll")));
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var name = string.Concat(table.TableName.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            var serviceType = assembly.GetType("Example.Guard.Generated.Services." + name + "Service", true)!;
            var mapperType = assembly.GetType("Example.Guard.Generated.Mappers." + name + "Mapper", true)!;
            var actor = new RuntimeActor(8015, "准备守卫测试", true, catalog.YzProjectId, "1");
            var guard = new BlockingGuard(session, actor, page.Id, table.TableName);
            var service = Activator.CreateInstance(serviceType, [session, Activator.CreateInstance(mapperType, [session]), new SnowflakeIdGenerator(856), Array.Empty<IRecordMutationObserver>(), new IRecordMutationGuard[] { guard }])!;
            object Request(string suffix) => Activator.CreateInstance(assembly.GetType("Example.Guard.Generated.DTOs." + name + "Page" + page.Id + suffix, true)!)!;
            foreach (var operation in Enum.GetValues<RecordMutationOperation>())
            {
                var method = serviceType.GetMethod(operation + page.Id + "Async")!;
                object?[] arguments = operation switch
                {
                    RecordMutationOperation.Create => [Request("CreateRequest"), null, actor, CancellationToken.None],
                    RecordMutationOperation.Update => [Request("UpdateRequest"), null, actor, CancellationToken.None],
                    RecordMutationOperation.Delete or RecordMutationOperation.Copy => [1L, null, actor, CancellationToken.None],
                    RecordMutationOperation.Import => [Request("ImportRequest"), actor, CancellationToken.None],
                    _ => throw new InvalidOperationException("测试操作类型无效。")
                };
                await Assert.ThrowsAsync<PreparationStoppedException>(() => (Task)method.Invoke(service, arguments)!);
                Assert.Equal(operation, guard.Operations[^1]); Assert.Null(session.CurrentTransaction);
            }
            Assert.Equal(Enum.GetValues<RecordMutationOperation>(), guard.Operations);
            var invalidActor = actor with { AccountId = 0 };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => (Task)serviceType.GetMethod("Create" + page.Id + "Async")!.Invoke(service, [Request("CreateRequest"), null, invalidActor, CancellationToken.None])!);
            Assert.Equal(5, guard.Operations.Count);
            Assert.Empty((await new SchemaService(session).InspectAsync(catalog)).Tables);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>寻找真实 Core/Data 源码，不用替身类型冒充生成应用编译。</summary>
    private static string SourceRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "global.json"))) return current.FullName;
        throw new InvalidOperationException("无法定位生成测试源码根。");
    }

    /// <summary>只验证事务和固定上下文后阻止记录访问，任何类型或锁顺序错误都使测试失败。</summary>
    private sealed class BlockingGuard(DatabaseSession session, RuntimeActor actor, string page, string table) : IRecordMutationGuard
    {
        public List<RecordMutationOperation> Operations { get; } = [];
        public Task BeforeMutationAsync(RecordMutationPreparation preparation, CancellationToken cancellationToken = default)
        {
            Assert.Same(session.CurrentTransaction, preparation.Transaction); Assert.NotNull(preparation.Transaction.Connection);
            Assert.Equal(actor, preparation.Actor); Assert.Equal(page, preparation.PageId); Assert.Equal(table, preparation.TableName);
            Operations.Add(preparation.Operation); throw new PreparationStoppedException();
        }
    }
    private sealed class PreparationStoppedException : Exception;
}
