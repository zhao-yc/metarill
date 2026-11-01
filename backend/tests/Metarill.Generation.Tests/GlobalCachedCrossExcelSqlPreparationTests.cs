using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Testing;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>提前验证六份真正生成的R0源码与静态读取计划，不连接MySQL，也不把准备通过当作Excel事务通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedCrossExcelSqlPreparationTests
{
    /// <summary>与实库精确名单共用六个固定模型，公开完整包缺口继续保持。</summary>
    public static IEnumerable<object[]> Cases() => GlobalCachedCrossExcelMySqlLease.Profiles.Select(p => new object[] { p.Role, p.Key, p.Parent, p.AllGlobal, p.Shared, p.Shorter });

    /// <summary>精确表名进入控件而外键角色路径保持；原导入导出及静态SQL计划初始化先离线验证。</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Exact_scoped_R0_model_builds_real_import_export_and_static_read_plan(int role, string key, string parent, bool allGlobal, bool shared, bool shorter)
    {
        const string run = "0123456789abcdef";
        var profile = GlobalCachedCrossExcelMySqlLease.Profile(role, key, parent, allGlobal, shared, shorter);
        var prefix = "mt_gcxe_" + run + "_" + profile;
        var catalog = shared ? GlobalCrossCachedTargetFixture.CreateShared(0, key, parent, shorter) : GlobalCrossCachedTargetFixture.Create(0, role, key, parent, allGlobal);
        GlobalCachedCrossExcelMySqlTests.Prefix(catalog, prefix);
        var expected = GlobalCachedCrossExcelMySqlLease.ExpectedTables(run)[profile]; Assert.Equal(expected, catalog.ConfigTables.Select(table => table.TableName));
        var input = JsonSerializer.Serialize(catalog); var source = catalog.ConfigTables.Single(table => table.Id == "3");
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-cross-excel-sql-preparation"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var generated = Path.Combine(directory, "generated");
        try
        {
            new CSharpGenerator().Generate(catalog, generated, GlobalCachedCrossExcelMySqlTests.Namespace);
            await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), "<Project><PropertyGroup><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(GlobalCachedCrossExcelMySqlTests.Namespace + ".Generated.Services." + GenerationModel.Pascal(source.TableName) + "Service", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import30Async")); Assert.NotNull(service.GetMethod("Export30Async")); Assert.NotNull(service.GetField("ExportReadPlan30", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null));
            var hand = Path.Combine(generated, "Services", GenerationModel.Pascal(source.TableName) + "Service.cs"); await File.AppendAllTextAsync(hand, "\n// 本轮精确表模型的手写扩展必须保留。\n"); var original = await File.ReadAllBytesAsync(hand);
            new CSharpGenerator().Generate(catalog, generated, GlobalCachedCrossExcelMySqlTests.Namespace); Assert.Equal(original, await File.ReadAllBytesAsync(hand)); Assert.Equal(input, JsonSerializer.Serialize(catalog));
            Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }
}
