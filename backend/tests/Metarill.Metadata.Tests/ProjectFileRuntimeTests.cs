using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime;
using Metarill.Runtime.Files;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>不访问数据库，核验发布项目、认证和文件目录的宿主接线。</summary>
public sealed class ProjectFileRuntimeTests
{
    [Fact]
    public void Published_projects_reuse_generated_sessions_and_have_distinct_physical_roots()
    {
        using var fixture = new PublishedFileFixture();
        var platform = fixture.Runtimes.Resolve("1"); var business = fixture.Runtimes.Resolve("2");
        Assert.Same(fixture.Sessions.Platform, platform.Session);
        Assert.Same(fixture.Sessions.Business, business.Session);
        Assert.Same(platform.Session, platform.Store.Session); Assert.Same(business.Session, business.Store.Session);
        Assert.Equal(Path.GetFullPath(fixture.Root), platform.Options.RootDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(fixture.Root), "projects", "2"), business.Options.RootDirectory);
        Assert.Equal("1", platform.Options.PublicContentProjectId); Assert.Equal("2", business.Options.PublicContentProjectId);
        // 额外服务端连接尚无发布模型，不能擅自取得平台文件槽。
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Runtimes.Resolve("3"));
        foreach (var invalid in new[] { "02", " 2", "2 ", "0", "-1", "../2", "" })
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Runtimes.Resolve(invalid));
        fixture.Registry.Demo.ConfigTables.Clear();
        Assert.Single(business.Metadata.Catalogs.Single().ConfigTables);
    }

    [Fact]
    public async Task Public_routing_is_anonymous_but_never_falls_back_from_an_invalid_project_hint()
    {
        using var fixture = new PublishedFileFixture();
        var authorization = new TestFileAuthorization(null);
        var resolver = new ProjectFileLibraryResolver(fixture.Runtimes, authorization, new SnowflakeIdGenerator(854));
        Assert.Throws<UnauthorizedAccessException>(() => resolver.RequireCurrent());
        // 非法 ID 在访问数据库前拒绝；匿名正确定位不应要求身份。
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.OpenPublicAsync(0, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.OpenPublicAsync(0, "2"));
        foreach (var hint in new[] { "", "02", "3", "-1" })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.OpenPublicAsync(1, hint));
        Assert.Equal(1, authorization.ActorCalls);
    }

    [Fact]
    public async Task File_observer_requires_matching_actual_project_even_for_administrators()
    {
        using var fixture = new PublishedFileFixture();
        var actor = new RuntimeActor(1, "跨库测试", true, "1", "");
        var observer = new ProjectFileReferenceMutationObserver(fixture.Runtimes, new TestFileAuthorization(actor), new SnowflakeIdGenerator(854));
        var mutation = new RecordMutationContext("200", "business_record", 5, "2", new Dictionary<string, object?> { ["attachments"] = null }, false, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => observer.BeforeCommitAsync(mutation));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => observer.BeforeCommitAsync(mutation with { ProjectId = "1" }));
    }

    [Fact]
    public async Task Configuration_without_submitted_file_fields_does_not_acquire_a_project_or_transaction()
    {
        using var fixture = new PublishedFileFixture();
        var authorization = new TestFileAuthorization(null);
        var observer = new ProjectFileReferenceMutationObserver(fixture.Runtimes, authorization, new SnowflakeIdGenerator(854));
        // 平台配置页面可编辑其他项目配置；没有文件字段时保持原配置观察器的处理边界。
        await observer.BeforeCommitAsync(new("100", "platform_record", 5, "2", new Dictionary<string, object?> { ["name"] = "配置" }, false, null!));
        await observer.BeforeCommitAsync(new("200", "business_record", 5, "2", new Dictionary<string, object?> { ["name"] = "省略文件字段" }, false, null!));
        Assert.Equal(0, authorization.ActorCalls);
    }

    [Fact]
    public void Full_physical_model_adds_only_file_infrastructure_and_protects_source_metadata()
    {
        using var fixture = new PublishedFileFixture();
        var source = fixture.Registry.Demo;
        var physical = fixture.Runtimes.ComposePhysical(source);
        Assert.Equal(4, physical.ConfigTables.Count); Assert.Single(source.ConfigTables);
        Assert.Single(physical.ConfigPages); Assert.Equal("business_record", physical.ConfigTables.Single(table => table.Id == "20").TableName);
        source.ConfigTables[0].TableName = "yz_file_library";
        Assert.Throws<InvalidOperationException>(() => fixture.Runtimes.ComposePhysical(source));
    }
}

/// <summary>最小已发布双项目模型；本夹具的虚构连接不会建立数据库连接。</summary>
internal sealed class PublishedFileFixture : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "metarill-project-files", Guid.NewGuid().ToString("N"));
    public MetadataRegistry Registry { get; }
    public TargetDatabaseSessions Sessions { get; }
    public ProjectFileRuntimes Runtimes { get; }
    public string Root => Path.Combine(directory, "objects");

    /// <summary>写入唯一临时元数据，复用真实会话类型验证 DI 归属。</summary>
    public PublishedFileFixture()
    {
        Directory.CreateDirectory(directory);
        var platform = Catalog("1", "10", "100", "platform_record", false);
        var demo = Catalog("2", "20", "200", "business_record", true);
        var platformPath = Path.Combine(directory, "platform.json"); var demoPath = Path.Combine(directory, "demo.json");
        File.WriteAllText(platformPath, JsonSerializer.Serialize(platform, JsonDefaults.Options));
        File.WriteAllText(demoPath, JsonSerializer.Serialize(demo, JsonDefaults.Options));
        Registry = new(platformPath, demoPath);
        var platformOptions = Options("metarill_project_platform_test"); var businessOptions = Options("metarill_project_business_test");
        Sessions = new(platformOptions, businessOptions, Registry, new Dictionary<string, DatabaseOptions> { ["3"] = businessOptions });
        var infrastructure = new MetadataCatalog { YzProjectId = "1" };
        var index = 800;
        foreach (var name in new[] { "yz_file_library", "yz_file_library_folder", "yz_file_ref" })
        {
            var id = (++index).ToString(CultureInfo.InvariantCulture);
            infrastructure.ConfigTables.Add(new() { Id = id, TableName = name, YzProjectId = "1" });
            infrastructure.ConfigColumns.Add(new() { Id = (++index).ToString(CultureInfo.InvariantCulture), ConfigTableId = id, ColumnName = "id", LogicalType = "int64" });
        }
        Runtimes = new(Sessions, Registry, infrastructure, new() { RootDirectory = Root });
    }

    /// <summary>构造最小页面，只有业务页面携带文件字段。</summary>
    private static MetadataCatalog Catalog(string project, string table, string page, string name, bool files)
    {
        var result = new MetadataCatalog { YzProjectId = project, ConfigTables = [new() { Id = table, TableName = name, YzProjectId = project }],
            ConfigColumns = [new() { Id = table + "1", ConfigTableId = table, ColumnName = "id", LogicalType = "int64" }],
            ConfigPages = [new() { Id = page, ConfigTableId = table, YzProjectId = project }] };
        if (files)
        {
            result.ConfigColumns.Add(new() { Id = table + "2", ConfigTableId = table, ColumnName = "attachments", ControlKind = "FileLibrary" });
            result.ConfigPageColumns.Add(new() { Id = table + "3", ConfigPageId = page, ConfigColumnId = table + "2", ColumnName = "attachments" });
        }
        return result;
    }

    /// <summary>使用无效回环端口，纯接线测试一旦意外访问数据库即失败。</summary>
    private static DatabaseOptions Options(string database) => new() { ExpectedDatabase = database, ConnectionString = "Server=127.0.0.1;Port=1;Database=" + database + ";User ID=unit_test;Password=unit_test;Connection Timeout=1" };

    /// <summary>仅清理本夹具 GUID 目录，不递归删除父目录。</summary>
    public void Dispose()
    {
        Sessions.Dispose();
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "metarill-project-files"));
        var target = Path.GetFullPath(directory);
        if (Path.GetDirectoryName(target) != parent || !Guid.TryParseExact(Path.GetFileName(target), "N", out _)) throw new InvalidOperationException("测试目录清理范围无效。");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}

/// <summary>身份固定在测试上下文；测试可确认公开路由未尝试取得身份。</summary>
internal sealed class TestFileAuthorization(RuntimeActor? actor) : IRuntimeAuthorization
{
    public int ActorCalls { get; private set; }
    public RuntimeActor RequireActor() { ActorCalls++; return actor ?? throw new UnauthorizedAccessException("测试未登录。"); }
    public void RequirePagePermission(string pageId, string operation) { }
}
