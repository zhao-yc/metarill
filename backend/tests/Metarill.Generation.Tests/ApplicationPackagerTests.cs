using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>独立包必须包含真实基础工程、可编译宿主和脱敏后的单一元数据来源。</summary>
public sealed class ApplicationPackagerTests : IAsyncLifetime
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "metarill-package-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Standalone_package_builds_from_its_own_sources_and_redacts_values_without_losing_definitions()
    {
        var source = SourceRoot(); var catalog = GeneratorTests.Catalog();
        catalog.ConfigTables[0].Extra["connString"] = JsonSerializer.SerializeToElement("never-ship-connection");
        catalog.ConfigTables[0].Extra["settings"] = JsonSerializer.SerializeToElement(new { accessKey = "never-ship-access", region = "test-region", nested = new { privateKey = "never-ship-private" } });
        catalog.ConfigColumns.Add(new() { Id = "19", ConfigTableId = "1", ColumnName = "password", Name = "密码配置定义", DefaultValue = "never-ship-password", LogicalType = "string", ControlDetail = "{\"accessKey\":\"never-ship-control\",\"title\":\"配置说明\"}" });
        var output = Path.Combine(temporary, "app");
        var result = new ApplicationPackager().Create(catalog, output, "Example.Standalone", source);
        Assert.True(result.RedactedPaths.Count >= 5);
        var metadata = File.ReadAllText(Path.Combine(output, "backend/metadata/project.json"));
        Assert.DoesNotContain("never-ship", metadata);
        var sanitized = JsonSerializer.Deserialize<MetadataCatalog>(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("password", sanitized.ConfigColumns.Single(column => column.Id == "19").ColumnName);
        Assert.Equal("", sanitized.ConfigColumns.Single(column => column.Id == "19").DefaultValue);
        Assert.Equal("never-ship-connection", catalog.ConfigTables[0].Extra["connString"].GetString());
        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        Assert.DoesNotContain(files, file => file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        Assert.DoesNotContain(files, file => file.EndsWith("settings.json"));
        Assert.All(files, file => Assert.DoesNotContain("\r", File.ReadAllText(file)));
        var host = File.ReadAllText(Path.Combine(output, "backend/Application.Host/Application.Host.csproj"));
        Assert.DoesNotContain(source, host);
        Assert.DoesNotContain("Metarill.Runtime", host);
        Assert.Contains("../../src/Metarill.Data", host);
        Assert.Contains("../../src/Metarill.Exports", host);
        Assert.Contains("../../src/Metarill.Files", host);
        var filesMetadata = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(output, "backend/metadata/files.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(["yz_file_library", "yz_file_library_folder", "yz_file_ref"], filesMetadata.ConfigTables.Select(table => table.TableName).Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(filesMetadata.ConfigPages);
        Assert.NotEmpty(filesMetadata.ConfigIndexes);
        Assert.NotEmpty(filesMetadata.ConfigIndexColumns);
        Assert.Contains("ClosedXML", File.ReadAllText(Path.Combine(output, "Directory.Packages.props")));
        StandaloneExportHttpProbe.WriteFixture(output);
        Assert.Contains("environment.IsDevelopment()", File.ReadAllText(Path.Combine(output, "backend/Application.Host/DevelopmentAuthentication.cs")));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { WorkingDirectory = output, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "build", "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "--verbosity", "quiet" }) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stdout.WaitAsync(timeout.Token) + await stderr.WaitAsync(timeout.Token));
        Assert.True(File.Exists(Path.Combine(output, "backend/Application.Host/packages.lock.json")));
        await StandaloneExportHttpProbe.VerifyAsync(output);
    }
    [Fact]
    public void Failed_package_is_atomic_and_existing_output_is_never_overwritten()
    {
        Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(GeneratorTests.Catalog(), Path.Combine(SourceRoot(), "backend/src/Metarill.Core/unsafe-output"), "Example.Standalone", SourceRoot()));
        Directory.CreateDirectory(temporary); var output = Path.Combine(temporary, "app");
        Assert.ThrowsAny<Exception>(() => new ApplicationPackager().Create(GeneratorTests.Catalog(), output, "class", SourceRoot()));
        Assert.False(Directory.Exists(output));
        Assert.Empty(Directory.GetDirectories(temporary, "*.staging-*"));
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "保留用户内容");
        Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(GeneratorTests.Catalog(), output, "Example.Standalone", SourceRoot()));
        Assert.Equal("保留用户内容", File.ReadAllText(Path.Combine(output, "keep.txt")));
    }
    [Fact]
    public void Reserved_file_name_conflict_is_rejected_before_output_is_created()
    {
        var catalog = GeneratorTests.Catalog(); catalog.ConfigTables[0].TableName = "yz_file_ref";
        var output = Path.Combine(temporary, "conflict");
        Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(catalog, output, "Example.Conflict", SourceRoot()));
        Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(temporary));
    }
    [Fact]
    public void Platform_physical_definitions_are_deduplicated_without_exposing_file_pages()
    {
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "backend/metadata/platform.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var output = Path.Combine(temporary, "platform");
        var result = new ApplicationPackager().Create(catalog, output, "Example.Platform", SourceRoot());
        Assert.Contains("backend/metadata/files.json", result.Files);
        Assert.Contains("src/Metarill.Files/licenses/LICENSE.txt", result.Files);
        Assert.Contains("src/Metarill.Files/licenses/THIRD-PARTY-NOTICES.txt", result.Files);
        Assert.Contains("SkiaSharp.NativeAssets.Linux.NoDependencies", File.ReadAllText(Path.Combine(output, "Directory.Packages.props")));
    }
    [Theory]
    [InlineData("../Metarill.Runtime/Metarill.Runtime.csproj")]
    [InlineData("../../outside/Metarill.Core.csproj")]
    [InlineData("../Metarill.Data/../../outside.csproj")]
    [InlineData("$(MSBuildProjectDirectory)/../Metarill.Core/Metarill.Core.csproj")]
    public void Foundation_project_reference_must_belong_to_explicit_package_set(string reference)
    {
        var source = Path.Combine(temporary, "unsafe-source");
        var core = Path.Combine(source, "backend/src/Metarill.Core"); Directory.CreateDirectory(core);
        var metadata = Path.Combine(source, "backend/metadata"); Directory.CreateDirectory(metadata);
        File.Copy(Path.Combine(SourceRoot(), "backend/metadata/files.json"), Path.Combine(metadata, "files.json"));
        File.WriteAllText(Path.Combine(core, "Metarill.Core.csproj"), new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("ProjectReference", new System.Xml.Linq.XAttribute("Include", reference)))).ToString());
        var output = Path.Combine(temporary, "unsafe-app");
        Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(GeneratorTests.Catalog(), output, "Example.Unsafe", source));
        Assert.False(Directory.Exists(output));
        Assert.Empty(Directory.GetDirectories(temporary, "*.staging-*"));
    }
    [Fact]
    public void Same_metadata_and_foundation_produce_identical_package_bytes()
    {
        var packager = new ApplicationPackager(); var first = Path.Combine(temporary, "first"); var second = Path.Combine(temporary, "second");
        packager.Create(GeneratorTests.Catalog(), first, "Example.Stable", SourceRoot());
        packager.Create(GeneratorTests.Catalog(), second, "Example.Stable", SourceRoot());
        foreach (var file in Directory.GetFiles(first, "*", SearchOption.AllDirectories)) Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(second, Path.GetRelativePath(first, file))));
    }
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    /// <summary>异步收尾避免 Windows 文件占用让有效业务验收偶发失败。</summary>
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => TestDirectoryCleanup.DeleteAsync(temporary, Path.Combine(Path.GetTempPath(), "metarill-package-tests"));
}
