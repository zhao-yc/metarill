using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

public sealed record FrontendPackageResult(IReadOnlyList<string> Files);

/// <summary>独立uni-app宿主只读取受控公共资产，不复制平台配置页、环境、AppID或用户文件。</summary>
public sealed class FrontendPackager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };
    private static readonly string[] RuntimeModules = ["buttonVisibleRule", "detailLayoutRuntime", "exportDownload", "fileLibraryFolderTree", "fileLibraryThumbnailPan", "generationDownload", "h5DocumentTitleSync", "imageUploadCropper", "listPageRuntime", "menuRuntime", "menuRuntimeBridge", "pageWritePayload", "relationReadValues", "tinyCloudEditor", "yzp-runtime"];
    private static readonly string[] Components = ["yzp-auto-pan-image", "yzp-cascade-tree", "yzp-file-library", "yzp-popup", "zxz-uni-data-select"];
    private static readonly string[] UniModules = ["uni-badge", "uni-calendar", "uni-card", "uni-collapse", "uni-combox", "uni-countdown", "uni-data-checkbox", "uni-data-picker", "uni-data-select", "uni-dateformat", "uni-datetime-picker", "uni-drawer", "uni-easyinput", "uni-fab", "uni-fav", "uni-file-picker", "uni-forms", "uni-goods-nav", "uni-grid", "uni-group", "uni-icons", "uni-indexed-list", "uni-link", "uni-list", "uni-load-more", "uni-nav-bar", "uni-notice-bar", "uni-number-box", "uni-pagination", "uni-popup", "uni-rate", "uni-row", "uni-scss", "uni-search-bar", "uni-section", "uni-segmented-control", "uni-steps", "uni-swipe-action", "uni-swiper-dot", "uni-table", "uni-tag", "uni-title", "uni-tooltip", "uni-transition", "uni-ui"];


    /// <summary>完整包在任何stage写入前检查缺口，不能用平台专用组件或缺失控件静默代替业务能力。</summary>
    public void Validate(MetadataCatalog catalog)
    {
        MetadataValidation.Validate(catalog);
        ValidateMenus(catalog);
        var model = GenerationModel.Create(catalog, "Metarill.Frontend");
        var gaps = model.Tables.SelectMany(table => table.Pages.SelectMany(page => page.DetailColumns.Where(column => page.Writable.Contains(column) && column.Source.ControlKind == "CustomControl")
            .Select(column => $"页面{page.Source.Name}({page.Source.Id})：{column.Source.ControlDetail}"))).Distinct().ToArray();
        if (gaps.Length != 0) throw new InvalidOperationException("独立前端缺少已接线的自定义控件：" + string.Join("；", gaps) + "。平台请显式使用仅后端打包（CLI --backend-only）。");
        var unsupportedMenus = catalog.ConfigMenus.Where(menu => menu.IsHidden != 1 && menu.Kind is not ("DIRECTORY" or "GENERATED_PAGE")).ToArray();
        if (unsupportedMenus.Length != 0) throw new InvalidOperationException("独立前端未提供自定义或外部菜单源：" + string.Join("；", unsupportedMenus.Select(menu => menu.Name + "(" + menu.Id + ")")) + "。请移除入口或显式使用仅后端打包。");
    }

    /// <summary>先读取并验证完整闭包再写同级stage；非空目录一律保护，重复输出由新空目录承接。</summary>
    public FrontendPackageResult Create(MetadataCatalog catalog, string generatedFrontend, string outputDirectory, string sourceRoot)
    {
        Validate(catalog);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)); CSharpGenerator.SafePath(output, ".metarill-frontend.json");
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidOperationException("独立前端输出目录必须为空，不能覆盖手写宿主或配置。");
        var source = CSharpGenerator.SafePath(Path.GetFullPath(sourceRoot), "frontend/metarill_uni_admin");
        var generated = Path.GetFullPath(generatedFrontend); CSharpGenerator.SafePath(generated, "pages.generated.json");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, byte[] bytes) { CSharpGenerator.SafePath(output, path); if (!files.TryAdd(path, bytes)) throw new InvalidOperationException("独立前端资源路径冲突：" + path); }
        void Text(string path, string value) => Add(path, Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n").Replace('\r', '\n')));
        void Copy(string relative) => Add(relative, File.ReadAllBytes(CSharpGenerator.SafePath(source, relative)));
        foreach (var path in new[] { "main.js", "uni.scss", "common/utils.js", "common/yzp.scss", "pages/main/rtf/rtf.vue", "THIRD-PARTY-NOTICES.md", "uni_modules/LICENSE.uni-ui", "licenses/sheetjs.LICENSE", "licenses/sheetjs-dist.LICENSE", "licenses/tinymce-vue.LICENSE.txt", "vendor/xlsx-0.20.3.tgz", "vendor/sheetjs-source.json", "common/yzp_runtime/vendor/cropperjs/cropper.esm.js", "common/yzp_runtime/vendor/cropperjs/cropper.css", "common/yzp_runtime/vendor/cropperjs/LICENSE" }) Copy(path);
        foreach (var module in RuntimeModules) Copy("common/yzp_runtime/" + module + ".js");
        foreach (var path in JsonSerializer.Deserialize<string[]>(Template("frontend-assets.allowlist.json.tpl"))!)
        {
            var allowed = Components.Any(component => path.StartsWith("components/" + component + "/", StringComparison.Ordinal)) || UniModules.Any(module => path.StartsWith("uni_modules/" + module + "/", StringComparison.Ordinal));
            if (!allowed) throw new InvalidOperationException("前端资源清单越过受控组件范围。");
            Copy(path);
        }
        var generatedRoutes = JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(generated, "pages.generated.json")))!["pages"]!.AsArray();
        var expected = catalog.ConfigPages.SelectMany(page => new[] { "list", "detail" }.Select(kind => "pages/yzp/" + catalog.ConfigTables.Single(table => table.Id == page.ConfigTableId).TableName + "-" + kind + "-" + page.Id)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in generatedRoutes)
        {
            var path = route?["path"]?.GetValue<string>() ?? "";
            if (!expected.Contains(path) || !seen.Add(path)) throw new InvalidOperationException("生成前端路由不属于当前元数据或有重复：" + path);
            Add(path + ".vue", File.ReadAllBytes(CSharpGenerator.SafePath(generated, path + ".vue")));
        }
        if (!expected.SetEquals(seen)) throw new InvalidOperationException("生成前端缺少当前项目的list/detail路由。");
        Add("pages.generated.json", File.ReadAllBytes(CSharpGenerator.SafePath(generated, "pages.generated.json")));
        Add("common/yzp_runtime/page-write-schema.generated.json", File.ReadAllBytes(CSharpGenerator.SafePath(generated, "common/yzp_runtime/page-write-schema.generated.json")));
        var pages = JsonNode.Parse(Template("frontend-pages.json.tpl"))!;
        foreach (var route in generatedRoutes) pages["pages"]!.AsArray().Add(route!.DeepClone());
        Text("pages.json", pages.ToJsonString(JsonOptions) + "\n");
        Text("common/application.generated.json", JsonSerializer.Serialize(new { projectId = catalog.YzProjectId, name = catalog.YzProjectName, adminTypeId = catalog.YzAdminTypes.FirstOrDefault()?.Id ?? "" }, JsonOptions) + "\n");
        foreach (var (template, destination) in new[] { ("frontend-App.vue.tpl", "App.vue"), ("frontend-environment.js.tpl", "common/environment.js"), ("frontend-config.js.tpl", "common/application.config.js"), ("frontend-yzp.js.tpl", "common/yzp.js"), ("frontend-login.vue.tpl", "pages/login.vue"), ("frontend-index.vue.tpl", "pages/main/index.vue"), ("frontend-menu.vue.tpl", "pages/main/menu.vue"), ("frontend-left.vue.tpl", "pages/main/left.vue"), ("frontend-top.vue.tpl", "pages/main/top.vue"), ("frontend-my.vue.tpl", "pages/main/my.vue"), ("frontend-manifest.json.tpl", "manifest.json"), ("frontend-index.html.tpl", "index.html"), ("frontend-verify.mjs.tpl", "scripts/verify-frontend.mjs"), ("frontend-README.md.tpl", "README.md") }) Text(destination, Template(template));
        var package = JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(source, "package.json")))!;
        var dependencies = package["dependencies"]!.DeepClone();
        var required = new Dictionary<string, string> { ["@tinymce/tinymce-vue"] = "6.3.0", ["cap-widget"] = "0.1.50", ["crypto-js"] = "4.2.0", ["xlsx"] = "file:vendor/xlsx-0.20.3.tgz" };
        if (dependencies.AsObject().Count != required.Count || required.Any(item => dependencies[item.Key]?.GetValue<string>() != item.Value)) throw new InvalidOperationException("独立前端依赖与已核对许可版本不一致，请先更新受控清单。");
        const string packageName = "metarill-generated-application";
        var lockfile = JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(source, "package-lock.json")))!;
        foreach (var item in lockfile["packages"]!.AsObject())
        {
            if (item.Value?["resolved"] is not JsonValue resolved) continue;
            var address = resolved.GetValue<string>();
            if (address == required["xlsx"]) continue;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "registry.npmjs.org" || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "") throw new InvalidOperationException("前端锁文件引用了受控registry之外的依赖。");
        }
        var integrity = lockfile["packages"]!["node_modules/xlsx"]?["integrity"]?.GetValue<string>();
        if (integrity != "sha512-" + Convert.ToBase64String(SHA512.HashData(files["vendor/xlsx-0.20.3.tgz"]))) throw new InvalidOperationException("离线SheetJS归档与锁文件完整性不一致。");
        lockfile["name"] = packageName; lockfile["packages"]![""]!["name"] = packageName;
        Text("package-lock.json", lockfile.ToJsonString(JsonOptions) + "\n");
        Text("package.json", JsonSerializer.Serialize(new { name = packageName, version = "0.1.0", @private = true, type = "module", license = "Apache-2.0", scripts = new Dictionary<string, string> { ["test"] = "node --experimental-vm-modules scripts/verify-frontend.mjs" }, dependencies }, JsonOptions) + "\n");
        Text(".gitignore", "node_modules/\nunpackage/\n.hbuilderx/\n*.local.*\n");
        var manifest = files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new { path = file.Key, sha256 = Convert.ToHexStringLower(SHA256.HashData(file.Value)) }).ToArray();
        Text(".metarill-frontend.json", JsonSerializer.Serialize(new { schemaVersion = 1, projectId = catalog.YzProjectId, authentication = "development-only", files = manifest }, JsonOptions) + "\n");
        var stage = output + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            foreach (var (relative, bytes) in files.OrderBy(file => file.Key, StringComparer.Ordinal)) { var path = CSharpGenerator.SafePath(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
            if (Directory.Exists(output)) Directory.Delete(output, false); Directory.Move(stage, output);
        }
        finally { if (Directory.Exists(stage) && Path.GetDirectoryName(stage) == Path.GetDirectoryName(output)) Directory.Delete(stage, true); }
        return new(files.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>离线包不依赖平台菜单Business，仍须提前拒绝孤儿、跨后台目标、环与过深树。</summary>
    private static void ValidateMenus(MetadataCatalog catalog)
    {
        if (catalog.ConfigMenus.Count > 5000) throw new InvalidOperationException("独立前端菜单数量不能超过5000。");
        var nodes = new Dictionary<string, MenuMetadata>(StringComparer.Ordinal);
        foreach (var menu in catalog.ConfigMenus)
            if (string.IsNullOrWhiteSpace(menu.Id) || !nodes.TryAdd(menu.Id, menu)) throw new InvalidOperationException("独立前端菜单标识为空或重复。");
        foreach (var menu in nodes.Values)
        {
            if (menu.YzProjectId != catalog.YzProjectId || !catalog.YzAdminTypes.Any(admin => admin.Id == menu.YzAdminTypeId)) throw new InvalidOperationException("独立前端菜单后台归属无效：" + menu.Name);
            if (menu.Kind == "GENERATED_PAGE" && !catalog.ConfigPages.Any(page => page.Id == menu.ConfigPageId && page.YzAdminTypeId == menu.YzAdminTypeId)) throw new InvalidOperationException("独立前端菜单目标页面不存在或跨后台：" + menu.Name);
            var ancestors = new HashSet<string>(StringComparer.Ordinal) { menu.Id }; var parent = menu.ParentMenuId;
            while (!string.IsNullOrEmpty(parent))
            {
                if (ancestors.Count > 32 || !ancestors.Add(parent)) throw new InvalidOperationException("独立前端菜单存在循环或层级超过32：" + menu.Name);
                if (!nodes.TryGetValue(parent, out var node) || node.Kind != "DIRECTORY" || node.YzAdminTypeId != menu.YzAdminTypeId) throw new InvalidOperationException("独立前端菜单父级不存在、类型或后台无效：" + menu.Name);
                parent = node.ParentMenuId;
            }
        }
    }

    /// <summary>只读取程序集内固定模板，元数据不能指定模板或磁盘路径。</summary>
    private static string Template(string name)
    {
        var assembly = typeof(FrontendPackager).Assembly; var resource = assembly.GetManifestResourceNames().Single(value => value.EndsWith("." + name, StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!); return reader.ReadToEnd();
    }
}
