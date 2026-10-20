using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>真实重新生成并重建三层一致清单，确保拒绝来自完整前端能力而非摘要或生成产物漂移。</summary>
    [Theory]
    [InlineData("readonlyCustom", "yzp-readonly-custom")]
    [InlineData("cycle", "独立前端菜单存在循环")]
    public void Full_application_candidate_semantics_are_checked_after_consistent_regeneration(string scenario, string expected)
    {
        var (target, candidate) = Packages(Catalog(), Catalog()); var next = Catalog();
        if (scenario == "readonlyCustom")
        {
            var column = next.ConfigColumns.Single(column => column.ColumnName == "name"); column.ControlKind = "CustomControl"; column.ControlDetail = "yzp-readonly-custom";
            foreach (var field in next.ConfigPageColumns.Where(field => field.ConfigColumnId == column.Id)) field.Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
            var pages = GenerationModel.Create(next, "Example.LocalUpdate").Tables.SelectMany(table => table.Pages).ToArray();
            Assert.Contains(pages, page => page.DetailColumns.Any(item => item.Source.Id == column.Id)); Assert.DoesNotContain(pages, page => page.Writable.Any(item => item.Source.Id == column.Id));
        }
        else
        {
            var directory = next.ConfigMenus.First(menu => menu.Kind == "DIRECTORY"); directory.ParentMenuId = directory.Id;
        }
        RegenerateConsistentCandidate(candidate, next);
        var updater = new LocalGenerationUpdater(); var before = SourceSnapshot(target); var candidateBefore = SourceSnapshot(candidate);
        // 反向只读预览成功证明旧目标能通过真实hash、metadata摘要、三层归属和新IR合并检查。
        // 新门禁只放在candidate分支，不额外阻止读取旧项目的待修复定义。
        Assert.True(updater.Preview(candidate, target, All).CanApply);
        var error = Assert.Throws<InvalidOperationException>(() => updater.Preview(target, candidate, All)); Assert.Contains(expected, error.Message);
        Assert.Equal(before, SourceSnapshot(target)); Assert.Equal(candidateBefore, SourceSnapshot(candidate));
    }

    /// <summary>模拟绕过完整打包器的能力校验后仍使用当前IR导出的候选，所有生成代码、页面、路由和摘要都真实更新。</summary>
    private static void RegenerateConsistentCandidate(string candidate, MetadataCatalog catalog)
    {
        var result = new CSharpGenerator().Generate(catalog, Path.Combine(candidate, "backend/generated"), "Example.LocalUpdate");
        File.WriteAllText(Path.Combine(candidate, "backend/metadata/project.json"), JsonSerializer.Serialize(catalog, Json) + "\n");
        // 测试重建完整候选时也同步固定目录编译绑定，不能被新摘要门禁提前遮住原能力断言。
        var securityCatalogSha256=ActualHash(candidate,"backend/metadata/project.json");
        File.WriteAllText(Path.Combine(candidate,"backend/Application.Host/PackageSecurityCatalogBinding.g.cs"),ApplicationPackager.SecurityCatalogBindingSource("Example.LocalUpdate",securityCatalogSha256));
        var generated = JsonNode.Parse(File.ReadAllText(Path.Combine(candidate, "backend/generated/.metarill-generation.json")))!;
        foreach (var entry in generated["files"]!.AsArray().Where(entry => entry!["path"]!.GetValue<string>().StartsWith("frontend/", StringComparison.Ordinal)))
        {
            var path = entry!["path"]!.GetValue<string>(); File.Copy(Path.Combine(candidate, "backend/generated", path), Path.Combine(candidate, path), true);
        }
        var pagesPath = Path.Combine(candidate, "frontend/pages.json"); var pages = JsonNode.Parse(File.ReadAllText(pagesPath))!; var routes = pages["pages"]!.AsArray();
        foreach (var route in routes.Where(route => route!["path"]!.GetValue<string>().StartsWith("pages/yzp/", StringComparison.Ordinal)).ToArray()) routes.Remove(route);
        var generatedRoutes = JsonNode.Parse(File.ReadAllText(Path.Combine(candidate, "frontend/pages.generated.json")))!["pages"]!.AsArray(); foreach (var route in generatedRoutes) routes.Add(route!.DeepClone());
        File.WriteAllText(pagesPath, pages.ToJsonString(Json) + "\n");
        var frontendPath = Path.Combine(candidate, "frontend/.metarill-frontend.json"); var frontend = JsonNode.Parse(File.ReadAllText(frontendPath))!;
        foreach (var entry in frontend["files"]!.AsArray()) entry!["sha256"] = ActualHash(candidate, "frontend/" + entry["path"]!.GetValue<string>());
        File.WriteAllText(frontendPath, frontend.ToJsonString(Json) + "\n");
        var packagePath = Path.Combine(candidate, ".metarill-package.json"); var package = JsonNode.Parse(File.ReadAllText(packagePath))!; package["securityCatalogSha256"]=securityCatalogSha256; package["metadataSha256"] = result.MetadataSha256; package["warnings"] = JsonSerializer.SerializeToNode(result.Warnings, Json);
        foreach (var entry in package["files"]!.AsArray()) entry!["sha256"] = ActualHash(candidate, entry["path"]!.GetValue<string>());
        File.WriteAllText(packagePath, package.ToJsonString(Json) + "\n");
        Assert.Equal(result.MetadataSha256, generated["metadataSha256"]!.GetValue<string>());
        var outer = package["files"]!.AsArray().ToDictionary(entry => entry!["path"]!.GetValue<string>(), entry => entry!["sha256"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var (path, hash) in outer) Assert.Equal(hash, ActualHash(candidate, path));
        foreach (var entry in generated["files"]!.AsArray())
        {
            var path = entry!["path"]!.GetValue<string>(); var hash = entry["sha256"]!.GetValue<string>(); Assert.Equal(hash, outer["backend/generated/" + path]);
            if (path.StartsWith("frontend/", StringComparison.Ordinal)) Assert.Equal(hash, outer[path]);
        }
        foreach (var entry in frontend["files"]!.AsArray()) Assert.Equal(entry!["sha256"]!.GetValue<string>(), outer["frontend/" + entry["path"]!.GetValue<string>()]);
    }
    /// <summary>清单使用实际字节摘要，不以测试写入的期望值冒充一致性。</summary>
    private static string ActualHash(string directory, string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, path))));
}
