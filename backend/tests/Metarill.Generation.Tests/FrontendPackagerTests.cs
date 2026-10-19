using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>独立宿主必须具有闭合的路由/组件及明确来源，不能挟带平台配置或覆盖手写内容。</summary>
public sealed class FrontendPackagerTests : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "metarill-frontend-package", Guid.NewGuid().ToString("N"));
    private static string SourceRoot([CallerFilePath] string file = "") => Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
    private static MetadataCatalog Demo() => JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "backend/metadata/demo.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>真实Node语法/导入检查覆盖普通模块、CommonJS、动态导入、Vue多脚本及组件闭包。</summary>
    [Fact]
    public async Task Full_demo_has_runnable_host_without_platform_identity_or_private_configuration()
    {
        var output = Path.Combine(root, "full"); var result = new ApplicationPackager().Create(Demo(), output, "Example.Frontend", SourceRoot());
        Assert.True(result.FrontendIncluded); Assert.Equal(ApplicationPackageMode.FullApplication, result.Mode);
        foreach (var file in new[] { "frontend/main.js", "frontend/App.vue", "frontend/manifest.json", "frontend/pages/login.vue", "frontend/pages/main/menu.vue", "frontend/common/yzp_runtime/page-write-schema.generated.json", "frontend/components/yzp-file-library/yzp-file-library.vue", "frontend/components/yzp-relation-select/yzp-relation-select.vue", "frontend/common/yzp_runtime/relationOptionsMixin.js", "frontend/common/yzp_runtime/remoteRelationOptions.js", "frontend/common/yzp_runtime/selectionPageMixin.js", "frontend/uni_modules/uni-datetime-picker/components/uni-datetime-picker/uni-datetime-picker.vue", "frontend/pages/main/rtf/rtf.vue", "frontend/vendor/xlsx-0.20.3.tgz" }) Assert.Contains(file, result.Files);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "frontend/manifest.json"))); Assert.Equal("", manifest.RootElement.GetProperty("appid").GetString());
        Assert.DoesNotContain(result.Files, file => file.Contains("pages/custom/", StringComparison.Ordinal) || file.Contains("yzp-control-detail-editor", StringComparison.Ordinal) || file.Contains(".local/", StringComparison.Ordinal) || file.Contains("development.settings", StringComparison.Ordinal));
        var sourceAppId = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceRoot(), "frontend/metarill_uni_admin/manifest.json"))).RootElement.GetProperty("appid").GetString()!;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(output, "frontend"), "*", SearchOption.AllDirectories).Where(path => Path.GetExtension(path) is ".js" or ".json" or ".vue")) Assert.DoesNotContain(sourceAppId, File.ReadAllText(path));
        var verified = await VerifyFrontendAsync(Path.Combine(output, "frontend"));
        Assert.True(verified.Code == 0, verified.Error); Assert.Contains("闭包检查通过", verified.Output);
    }

    /// <summary>导出初始AppID仍为空，开发者填写自己的字符串AppID后真实Node闭包检查必须继续可用。</summary>
    [Fact]
    public async Task Developer_customized_appid_passes_real_node_verification()
    {
        var output = Path.Combine(root, "custom-appid"); new ApplicationPackager().Create(Demo(), output, "Example.CustomAppId", SourceRoot());
        var manifestPath = Path.Combine(output, "frontend/manifest.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath))!;
        Assert.Equal("", manifest["appid"]!.GetValue<string>());
        manifest["appid"] = "__UNI__0000000"; // 本测试的虚构标识，不复用平台或用户AppID，也不执行发行。
        File.WriteAllText(manifestPath, manifest.ToJsonString());
        var verified = await VerifyFrontendAsync(Path.Combine(output, "frontend"));
        Assert.True(verified.Code == 0, verified.Error); Assert.Contains("闭包检查通过", verified.Output);
        manifest["appid"] = 123; File.WriteAllText(manifestPath, manifest.ToJsonString());
        var invalid = await VerifyFrontendAsync(Path.Combine(output, "frontend"));
        Assert.NotEqual(0, invalid.Code); Assert.Contains("manifest.appid必须为字符串", invalid.Error);
    }

    /// <summary>真实CRLF/BOM工作副本产生相同LF包，二进制归档不改；开发者转行尾后Node条件编译仍可验证。</summary>
    [Fact]
    public async Task CrLf_bom_source_assets_produce_identical_package_and_remain_verifiable_after_customization()
    {
        var generated = Path.Combine(root, "newline-generated"); new CSharpGenerator().Generate(Demo(), generated, "Example.Newlines");
        var first = Path.Combine(root, "newline-first"); var packager = new FrontendPackager();
        var result = packager.Create(Demo(), Path.Combine(generated, "frontend"), first, SourceRoot());
        var source = Path.Combine(root, "newline-source");
        foreach (var relative in result.Files)
        {
            var original = Path.Combine(SourceRoot(), "frontend/metarill_uni_admin", relative);
            if (!File.Exists(original)) continue;
            var destination = Path.Combine(source, "frontend/metarill_uni_admin", relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (Path.GetExtension(relative) is ".tgz" or ".ttf" or ".woff" or ".woff2") File.Copy(original, destination);
            else File.WriteAllText(destination, File.ReadAllText(original).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"), new UTF8Encoding(true));
        }
        var second = Path.Combine(root, "newline-second"); packager.Create(Demo(), Path.Combine(generated, "frontend"), second, source);
        foreach (var relative in result.Files) Assert.Equal(File.ReadAllBytes(Path.Combine(first, relative)), File.ReadAllBytes(Path.Combine(second, relative)));
        foreach (var relative in new[] { "main.js", "uni_modules/uni-icons/components/uni-icons/uni-icons.vue" })
        {
            var path = Path.Combine(second, relative); File.WriteAllText(path, File.ReadAllText(path).Replace("\n", "\r\n"), new UTF8Encoding(false));
        }
        var verified = await VerifyFrontendAsync(second);
        Assert.True(verified.Code == 0, verified.Error); Assert.Contains("闭包检查通过", verified.Output);
        File.AppendAllText(Path.Combine(second, "main.js"), "\r\n// #ifdef H5\r\n");
        var unbalanced = await VerifyFrontendAsync(second);
        Assert.NotEqual(0, unbalanced.Code); Assert.Contains("条件编译指令不配对", unbalanced.Error);
    }

    /// <summary>未知与平台专用控件必须在stage写入前拒绝，错误准确指出页面和控件。</summary>
    [Theory]
    [InlineData("yzp-unknown-control")]
    [InlineData("yzp-control-detail-editor")]
    public void Missing_custom_control_is_rejected_without_partial_package(string control)
    {
        var catalog = Demo(); var column = catalog.ConfigColumns.Single(column => column.ColumnName == "name"); column.ControlKind = "CustomControl"; column.ControlDetail = control;
        var output = Path.Combine(root, "blocked"); var error = Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(catalog, output, "Example.Blocked", SourceRoot()));
        Assert.Contains(control, error.Message); Assert.Contains(catalog.ConfigPages[0].Id, error.Message); Assert.Contains("--backend-only", error.Message); Assert.False(Directory.Exists(output));
        Assert.False(Directory.Exists(root));
    }

    /// <summary>只读详情仍有组件合同，未接线的CustomControl必须在完整包stage创建前拒绝。</summary>
    [Fact]
    public void Readonly_custom_detail_control_is_rejected_without_creating_output()
    {
        var catalog = Demo(); var column = catalog.ConfigColumns.Single(column => column.ColumnName == "name");
        column.ControlKind = "CustomControl"; column.ControlDetail = "yzp-readonly-custom";
        var field = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == column.Id);
        field.Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
        var output = Path.Combine(root, "readonly-blocked");
        var error = Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(catalog, output, "Example.Readonly", SourceRoot()));
        Assert.Contains(column.ControlDetail, error.Message); Assert.Contains(catalog.ConfigPages[0].Id, error.Message);
        Assert.Contains("--backend-only", error.Message); Assert.False(Directory.Exists(output)); Assert.False(Directory.Exists(root));
        // 仅后端是显式能力，不能因完整前端合同拒绝而一并删除。
        var backend = new ApplicationPackager().Create(catalog, Path.Combine(root, "readonly-backend"), "Example.Readonly", SourceRoot(), ApplicationPackageMode.BackendOnly);
        Assert.Equal(ApplicationPackageMode.BackendOnly, backend.Mode); Assert.False(backend.FrontendIncluded);
    }

    /// <summary>无根循环和孤儿不能在菜单组装时静默消失，跨后台目标不能误导导航。</summary>
    [Theory]
    [InlineData("orphan")]
    [InlineData("cycle")]
    [InlineData("target")]
    [InlineData("duplicate")]
    public void Invalid_menu_graph_is_rejected_before_writing_stage(string scenario)
    {
        var catalog = Demo();
        if (scenario == "orphan") catalog.ConfigMenus[1].ParentMenuId = "999";
        if (scenario == "cycle") catalog.ConfigMenus[0].ParentMenuId = catalog.ConfigMenus[0].Id;
        if (scenario == "target") catalog.ConfigMenus[1].ConfigPageId = "999";
        if (scenario == "duplicate") catalog.ConfigMenus.Add(catalog.ConfigMenus[0]);
        Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(catalog, Path.Combine(root, "blocked-menu"), "Example.Menus", SourceRoot()));
        Assert.False(Directory.Exists(root));
    }

    /// <summary>显式仅后端保留旧能力，manifest和README都不能声称存在可运行前端。</summary>
    [Fact]
    public void Explicit_backend_only_is_auditable_and_platform_default_full_fails()
    {
        var catalog = JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(), "backend/metadata/platform.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Throws<InvalidOperationException>(() => new ApplicationPackager().Create(catalog, Path.Combine(root, "bad-platform"), "Example.Platform", SourceRoot()));
        var result = new ApplicationPackager().Create(catalog, Path.Combine(root, "backend"), "Example.Platform", SourceRoot(), ApplicationPackageMode.BackendOnly);
        Assert.False(result.FrontendIncluded); Assert.NotEmpty(result.Warnings); Assert.False(Directory.Exists(Path.Combine(root, "backend/frontend")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "backend/.metarill-package.json"))); Assert.False(manifest.RootElement.GetProperty("frontendIncluded").GetBoolean()); Assert.Equal("BackendOnly", manifest.RootElement.GetProperty("packageMode").GetString());
        Assert.Contains("BackendOnly", File.ReadAllText(Path.Combine(root, "backend/README.md")));
    }

    /// <summary>相同输入得到相同字节；已有宿主配置及用户内容绝不覆盖。</summary>
    [Fact]
    public void Full_packages_are_deterministic_and_existing_frontend_is_protected()
    {
        var catalog = Demo(); var first = Path.Combine(root, "first"); var second = Path.Combine(root, "second");
        new ApplicationPackager().Create(catalog, first, "Example.Frontend", SourceRoot()); new ApplicationPackager().Create(catalog, second, "Example.Frontend", SourceRoot());
        foreach (var file in Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories)) Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(second, Path.GetRelativePath(first, file))));
        var config = Path.Combine(first, "frontend/common/application.config.js"); File.AppendAllText(config, "\n// 用户手写保留标记\n"); var hash = File.ReadAllBytes(config);
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Create(catalog, Path.Combine(first, "backend/generated/frontend"), Path.Combine(first, "frontend"), SourceRoot())); Assert.Equal(hash, File.ReadAllBytes(config));
    }

    /// <summary>前端输入的任意祖先链接也必须拒绝，不能沿链接读取文件。</summary>
    [Fact]
    public void Nested_source_links_are_rejected_before_reading_their_contents()
    {
        var source = Path.Combine(root, "source"); var frontend = Path.Combine(source, "frontend/metarill_uni_admin"); Directory.CreateDirectory(frontend);
        File.Copy(Path.Combine(SourceRoot(), "frontend/metarill_uni_admin/main.js"), Path.Combine(frontend, "main.js"));
        File.Copy(Path.Combine(SourceRoot(), "frontend/metarill_uni_admin/uni.scss"), Path.Combine(frontend, "uni.scss"));
        var link = Path.Combine(frontend, "common"); var target = Path.Combine(SourceRoot(), "frontend/metarill_uni_admin/common");
        if (OperatingSystem.IsWindows())
        {
            // 只创建本测试GUID目录中的junction，无需提升符号链接特权；禁止shell元字符进入命令。
            if ((link + target).IndexOfAny(['&', '|', '<', '>', '^', '%', '!', '\"', '\r', '\n']) >= 0) throw new InvalidOperationException("测试路径包含shell特殊字符。");
            var start = new ProcessStartInfo("cmd.exe") { Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(start)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        var generated = Path.Combine(root, "generated"); new CSharpGenerator().Generate(Demo(), generated, "Example.Frontend");
        var error = Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Create(Demo(), Path.Combine(generated, "frontend"), Path.Combine(root, "out"), source)); Assert.Contains("符号链接", error.Message); Assert.False(Directory.Exists(Path.Combine(root, "out")));
    }

    /// <summary>固定资产清单不存在时明确失败，不能偷偷引用平台环境作为回退。</summary>
    [Fact]
    public void Missing_allowlisted_source_has_no_platform_or_environment_fallback()
    {
        var source = Path.Combine(root, "empty"); Directory.CreateDirectory(source); var generated = Path.Combine(root, "generated"); new CSharpGenerator().Generate(Demo(), generated, "Example.Frontend");
        Assert.ThrowsAny<Exception>(() => new FrontendPackager().Create(Demo(), Path.Combine(generated, "frontend"), Path.Combine(root, "out"), source)); Assert.False(Directory.Exists(Path.Combine(root, "out")));
    }

    /// <summary>组件目录中新出现的未列名文件不会因目录复制混入，即使扩展名是JS或JSON。</summary>
    [Fact]
    public void Unlisted_component_files_and_private_metadata_values_never_enter_frontend()
    {
        var source = SourceRoot(); var relative = "frontend/metarill_uni_admin/components/yzp-file-library/private-" + Guid.NewGuid().ToString("N") + ".js";
        var marker = "不应复制的私有标记-" + Guid.NewGuid().ToString("N"); var target = Path.Combine(source, relative);
        using (var stream = new FileStream(target, FileMode.CreateNew)) { var bytes = Encoding.UTF8.GetBytes(marker); stream.Write(bytes); }
        try
        {
            var catalog = Demo(); catalog.ConfigTables[0].Extra["accessKey"] = JsonSerializer.SerializeToElement(marker);
            var output = Path.Combine(root, "clean"); var result = new ApplicationPackager().Create(catalog, output, "Example.Clean", source);
            Assert.DoesNotContain(result.Files, file => file.EndsWith(Path.GetFileName(target), StringComparison.Ordinal));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(output, "frontend"), "*", SearchOption.AllDirectories).Where(file => Path.GetExtension(file) is ".js" or ".json" or ".vue")) Assert.DoesNotContain(marker, File.ReadAllText(file));
        }
        finally { File.Delete(target); }
    }

    /// <summary>在实际工程目录执行随包Node校验，不用源码断言代替运行结果。</summary>
    private static async Task<(int Code, string Output, string Error)> VerifyFrontendAsync(string directory)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH") ?? "node") { WorkingDirectory = directory, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        info.ArgumentList.Add("--experimental-vm-modules"); info.ArgumentList.Add("scripts/verify-frontend.mjs");
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        return (process.ExitCode, await stdout.WaitAsync(timeout.Token), await stderr.WaitAsync(timeout.Token));
    }

    public async ValueTask DisposeAsync() => await TestDirectoryCleanup.DeleteAsync(root, Path.Combine(Path.GetTempPath(), "metarill-frontend-package"));
}
