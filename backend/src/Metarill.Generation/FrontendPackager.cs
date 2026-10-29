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
    private readonly Action<string, string> publish;
    /// <summary>默认单次原子目录发布；内部注入用于验证真实内外层失败保护。</summary>
    public FrontendPackager() : this(PackagePublication.Publish) { }
    /// <summary>内部逐实例注入发布动作，故障测试不使用全局可变hook。</summary>
    internal FrontendPackager(Action<string, string> publish) => this.publish = publish;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, NewLine = "\n" };
    private static readonly string[] RuntimeModules = ["buttonVisibleRule", "detailLayoutRuntime", "exportDownload", "fileLibraryFolderTree", "fileLibraryThumbnailPan", "generationDownload", "h5DocumentTitleSync", "imageUploadCropper", "listPageRuntime", "menuRuntime", "menuRuntimeBridge", "multiSelectFieldMixin", "originalListOptionsMixin", "originalSelectionPageMixin", "pageWritePayload", "relationOptionsMixin", "relationReadValues", "remoteRelationOptions", "selectionPageMixin", "tinyCloudEditor", "yzp-runtime"];
    // 仅原工程明确核对的logo/六张tab素材与用户授权新生成的默认头像；来源不等于额外商标或第三方分发授权。
    private static readonly string[] StaticAssets = ["static/logo.png", "static/images/home.png", "static/images/home-selected.png", "static/images/menu.png", "static/images/menu-selected.png", "static/images/my.png", "static/images/my-selected.png", "static/images/avatar.png"];
    private static readonly string[] Components = ["yzp-auto-pan-image", "yzp-cascade-tree", "yzp-file-library", "yzp-menu", "yzp-popup", "yzp-relation-select", "zxz-uni-data-select"];
    private static readonly string[] UniModules = ["uni-badge", "uni-calendar", "uni-card", "uni-collapse", "uni-combox", "uni-countdown", "uni-data-checkbox", "uni-data-picker", "uni-data-select", "uni-dateformat", "uni-datetime-picker", "uni-drawer", "uni-easyinput", "uni-fab", "uni-fav", "uni-file-picker", "uni-forms", "uni-goods-nav", "uni-grid", "uni-group", "uni-icons", "uni-indexed-list", "uni-link", "uni-list", "uni-load-more", "uni-nav-bar", "uni-notice-bar", "uni-number-box", "uni-pagination", "uni-popup", "uni-rate", "uni-row", "uni-scss", "uni-search-bar", "uni-section", "uni-segmented-control", "uni-steps", "uni-swipe-action", "uni-swiper-dot", "uni-table", "uni-tag", "uni-title", "uni-tooltip", "uni-transition", "uni-ui"];


    /// <summary>完整包在任何stage写入前检查缺口，不能用平台专用组件或缺失控件静默代替业务能力。</summary>
    public void Validate(MetadataCatalog catalog)
    {
        MetadataValidation.Validate(catalog);
        ValidateMenus(catalog);
        var model = GenerationModel.Create(catalog, "Metarill.Frontend");
        // 只读详情也可能渲染完整自定义组件；是否可写不能作为组件依赖已接线的依据。
        var gaps = model.Tables.SelectMany(table => table.Pages.SelectMany(page => page.DetailColumns.Where(column => column.Source.ControlKind == "CustomControl")
            .Select(column => $"页面{page.Source.Name}({page.Source.Id})：{column.Source.ControlDetail}"))).Distinct().ToArray();
        if (gaps.Length != 0) throw new InvalidOperationException("独立前端缺少已接线的自定义控件：" + string.Join("；", gaps) + "。平台请显式使用仅后端打包（CLI --backend-only）。");
        // 原控件布局不得被替换成只读占位；后端尚未接线时拒绝完整应用，不能导出可点击但失效的页面。
        var backendGaps = GenerationCapabilityAudit.Collect(model);
        if (backendGaps.Count != 0) throw new InvalidOperationException("原页面仍有未完成的后端能力：" + string.Join("；", backendGaps.Select(gap => $"页面{gap.PageId}/{gap.Column}/{gap.Usage}：{gap.Reason}")) + "。请完成适配后打包，或明确选择BackendOnly。");
        var unsupportedMenus = catalog.ConfigMenus.Where(menu => menu.IsHidden != 1 && menu.Kind is not ("DIRECTORY" or "GENERATED_PAGE")).ToArray();
        if (unsupportedMenus.Length != 0) throw new InvalidOperationException("独立前端未提供自定义或外部菜单源：" + string.Join("；", unsupportedMenus.Select(menu => menu.Name + "(" + menu.Id + ")")) + "。请移除入口或显式使用仅后端打包。");
    }

    /// <summary>先读取并验证完整闭包再写同级stage；非空目录一律保护，重复输出由新空目录承接。</summary>
    public FrontendPackageResult Create(MetadataCatalog catalog, string generatedFrontend, string outputDirectory, string sourceRoot, bool includeAgent = false)
    {
        Validate(catalog);
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)); CSharpGenerator.SafePath(output, ".metarill-frontend.json");
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidOperationException("独立前端输出目录必须为空，不能覆盖手写宿主或配置。");
        var files=BuildFiles(catalog,generatedFrontend,output,sourceRoot,null,includeAgent);
        var stage = output + ".staging-" + Guid.NewGuid().ToString("N");
        var preserveStage = false;
        try
        {
            foreach (var (relative, bytes) in files.OrderBy(file => file.Key, StringComparer.Ordinal)) { var path = CSharpGenerator.SafePath(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
            publish(stage, output);
        }
        catch (PackagePublicationException) { preserveStage = true; throw; }
        finally { if (!preserveStage && Directory.Exists(stage) && Path.GetDirectoryName(stage) == Path.GetDirectoryName(output)) Directory.Delete(stage, true); }
        return new(files.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>只准备工厂签发管理模块的前端资源，不写目录、不发布应用、不解除后端缺口。</summary>
    internal FrontendAssetPlan PrepareSecurityManagement(SecurityManagementModelSet module,string generatedFrontend,string sourceRoot,bool includeAgent=false)
    {
        var catalog=SecurityManagementModuleFactory.RequirePublishedForGeneration(module);
        MetadataValidation.Validate(catalog);ValidateMenus(catalog);
        var generated=Path.GetFullPath(generatedFrontend);
        using var manifest=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(Path.GetDirectoryName(generated)!,".metarill-generation.json")));
        var declaration=manifest.RootElement;var rootNamespace=declaration.GetProperty("rootNamespace").GetString()!;
        CSharpGenerator.ValidateNamespace(rootNamespace);
        var expected=Convert.ToHexStringLower(SHA256.HashData(CSharpGenerator.CanonicalMetadata(catalog)));
        if(declaration.GetProperty("version").GetInt32()!=1||declaration.GetProperty("projectId").GetString()!=catalog.YzProjectId||declaration.GetProperty("metadataSha256").GetString()!=expected)throw new InvalidOperationException("管理前端生成归属不匹配，未准备资源。");
        var model=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(catalog,rootNamespace),module);
        var custom=catalog.ConfigCustomPages.Single(page=>page.Id=="2062363000000000002");
        if(catalog.ConfigCustomPages.Any(page=>page.Id!=custom.Id)||catalog.ConfigMenus.Any(menu=>menu.Kind is not("DIRECTORY" or "GENERATED_PAGE")&&!(menu.Kind=="CUSTOM_PAGE"&&menu.ConfigCustomPageId==custom.Id&&menu.YzAdminTypeId==custom.YzAdminTypeId)))throw new InvalidOperationException("管理前端存在未接线的自定义或外部入口。");
        if(model.Tables.SelectMany(table=>table.Pages).SelectMany(page=>page.DetailColumns).Any(column=>column.Source.ControlKind=="CustomControl"))throw new InvalidOperationException("管理前端存在未接线的自定义控件。");
        var boundary=Path.Combine(generated,".frontend-assets-plan");
        return new(BuildFiles(catalog,generated,boundary,sourceRoot,model,includeAgent),module.PendingAdapters,GenerationCapabilityAudit.Collect(model));
    }

    /// <summary>内部Excel验收资源仍记录缺口；逐项核对生成归属和基底，不接受JSON豁免标记。</summary>
    internal FrontendAssetPlan PrepareCrossTableExcel(MetadataCatalog catalog,string generatedFrontend,string sourceRoot,bool includeAgent=false)
    {
        MetadataValidation.Validate(catalog);ValidateMenus(catalog);
        var generated=Path.GetFullPath(generatedFrontend);
        using var document=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(Path.GetDirectoryName(generated)!,".metarill-generation.json")));
        var marker=document.RootElement;var rootNamespace=marker.GetProperty("rootNamespace").GetString()!;CSharpGenerator.ValidateNamespace(rootNamespace);
        var expected=Convert.ToHexStringLower(SHA256.HashData(CSharpGenerator.CanonicalMetadata(catalog)));
        if(marker.GetProperty("version").GetInt32()!=1||marker.GetProperty("projectId").GetString()!=catalog.YzProjectId||marker.GetProperty("metadataSha256").GetString()!=expected)
            throw new InvalidOperationException("跨表Excel前端生成归属不匹配。");
        var proof=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,rootNamespace);
        return new(BuildFiles(catalog,generated,Path.Combine(generated,".frontend-assets-plan"),sourceRoot,null,includeAgent),[],proof.Gaps);
    }

    /// <summary>先建立完整资源图；普通发布和内部管理准备共用复制、依赖、路由与摘要规则。</summary>
    private static Dictionary<string,byte[]> BuildFiles(MetadataCatalog catalog,string generatedFrontend,string output,string sourceRoot,GenerationModel? trustedModel,bool includeAgent=false)
    {
        var source = CSharpGenerator.SafePath(Path.GetFullPath(sourceRoot), "frontend/metarill_uni_admin");
        var generated = Path.GetFullPath(generatedFrontend); CSharpGenerator.SafePath(generated, "pages.generated.json");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string path, byte[] bytes) { CSharpGenerator.SafePath(output, path); if (!files.TryAdd(path, bytes)) throw new InvalidOperationException("独立前端资源路径冲突：" + path); }
        void Text(string path, string value) => Add(path, Encoding.UTF8.GetBytes(value.Replace("\r\n", "\n").Replace('\r', '\n')));
        // 开发者工作副本可能保留CRLF/BOM；只规范受控文本，字体与离线归档必须保持原字节。
        void Copy(string relative)
        {
            var bytes = File.ReadAllBytes(CSharpGenerator.SafePath(source, relative));
            if (Path.GetExtension(relative) is ".tgz" or ".ttf" or ".woff" or ".woff2" or ".png") { Add(relative, bytes); return; }
            string content;
            try { content = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("前端受控文本不是UTF8：" + relative, error); }
            if (content.StartsWith('\uFEFF')) content = content[1..];
            if (relative == "uni.scss")
            {
                // 仅替换原项目主题变量，布局/样式规则保持原样；主色已由元数据入口验证为颜色字面值。
                var theme = new System.Text.RegularExpressions.Regex(@"(?m)^(\$uni-primary\s*:\s*)[^;\r\n]+(;)");
                if (theme.Matches(content).Count != 1) throw new InvalidOperationException("原主题变量结构已变化，请先核对主色下发。");
                content = theme.Replace(content, match => match.Groups[1].Value + catalog.PrimaryColor + match.Groups[2].Value);
            }
            Text(relative, content);
        }
        foreach (var path in new[] { "main.js", "uni.scss", "common/utils.js", "common/yzp.scss", "pages/main/rtf/rtf.vue", "THIRD-PARTY-NOTICES.md", "uni_modules/LICENSE.uni-ui", "licenses/sheetjs.LICENSE", "licenses/sheetjs-dist.LICENSE", "licenses/tinymce-vue.LICENSE.txt", "vendor/xlsx-0.20.3.tgz", "vendor/sheetjs-source.json", "common/yzp_runtime/vendor/cropperjs/cropper.esm.js", "common/yzp_runtime/vendor/cropperjs/cropper.css", "common/yzp_runtime/vendor/cropperjs/LICENSE" }) Copy(path);
        foreach (var module in RuntimeModules) Copy("common/yzp_runtime/" + module + ".js");
        foreach (var path in JsonSerializer.Deserialize<string[]>(Template("frontend-assets.allowlist.json.tpl"))!)
        {
            var allowed = StaticAssets.Contains(path, StringComparer.Ordinal) || Components.Any(component => path.StartsWith("components/" + component + "/", StringComparison.Ordinal)) || UniModules.Any(module => path.StartsWith("uni_modules/" + module + "/", StringComparison.Ordinal));
            if (!allowed) throw new InvalidOperationException("前端资源清单越过受控组件范围。");
            Copy(path);
        }
        var generatedRoutes = JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(generated, "pages.generated.json")))!["pages"]!.AsArray();
        var expected = catalog.ConfigPages.SelectMany(page => new[] { "list", "detail" }.Select(kind => "pages/yzp/" + catalog.ConfigTables.Single(table => table.Id == page.ConfigTableId).TableName + "-" + kind + "-" + page.Id)).ToHashSet(StringComparer.Ordinal);
        var trustedRoutes=trustedModel is null?new Dictionary<string,JsonNode>(StringComparer.Ordinal):TrustedSecurityWorkbenchRenderer.Routes(trustedModel).Select(route=>JsonSerializer.SerializeToNode(route)!).ToDictionary(route=>route["path"]!.GetValue<string>(),StringComparer.Ordinal);
        foreach(var path in trustedRoutes.Keys)if(!expected.Add(path))throw new InvalidOperationException("管理工作台路由与业务页面冲突。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in generatedRoutes)
        {
            var path = route?["path"]?.GetValue<string>() ?? "";
            if (!expected.Contains(path) || !seen.Add(path)) throw new InvalidOperationException("生成前端路由不属于当前元数据或有重复：" + path);
            if(trustedRoutes.TryGetValue(path,out var expectedRoute)&&!JsonNode.DeepEquals(route,expectedRoute))throw new InvalidOperationException("管理工作台的原路由样式已变化。");
            Add(path + ".vue", File.ReadAllBytes(CSharpGenerator.SafePath(generated, path + ".vue")));
        }
        if (!expected.SetEquals(seen)) throw new InvalidOperationException("生成前端缺少当前项目的list/detail路由。");
        if(trustedModel is not null)
        {
            foreach(var file in TrustedSecurityWorkbenchRenderer.Frontend(trustedModel))
            {
                var relative=file.Path["frontend/".Length..];var actual=File.ReadAllText(CSharpGenerator.SafePath(generated,relative));var content=file.Content.Replace("\r\n","\n").Replace('\r','\n');
                if(actual!=content)throw new InvalidOperationException("管理工作台原资源与生成快照不符："+relative);
                if(relative=="pages/main/set_role_permission.vue")continue;
                Text(relative,content);
            }
        }
        Add("pages.generated.json", File.ReadAllBytes(CSharpGenerator.SafePath(generated, "pages.generated.json")));
        Add("common/yzp_runtime/page-write-schema.generated.json", File.ReadAllBytes(CSharpGenerator.SafePath(generated, "common/yzp_runtime/page-write-schema.generated.json")));
        var pages = JsonNode.Parse(Template("frontend-pages.json.tpl"))!;
        foreach (var route in generatedRoutes) pages["pages"]!.AsArray().Add(route!.DeepClone());
        if(includeAgent)
        {
            // 原授权页面、路由和样式逐字复用，页面参数不能启用服务器能力。
            var original=JsonNode.Parse(File.ReadAllText(CSharpGenerator.SafePath(source,"pages.json")))!["pages"]!.AsArray();
            foreach(var path in new[]{"pages/custom/agent_authorize","pages/custom/agent_grants"})
            {
                Copy(path+".vue");
                pages["pages"]!.AsArray().Add(original.Single(route=>route?["path"]?.GetValue<string>()==path)!.DeepClone());
            }
        }
        pages["tabBar"]!["selectedColor"] = catalog.PrimaryColor;
        Text("pages.json", pages.ToJsonString(JsonOptions) + "\n");
        Text("common/application.generated.json", JsonSerializer.Serialize(new { projectId = catalog.YzProjectId, name = catalog.YzProjectName, adminTypeId = catalog.YzAdminTypes.FirstOrDefault()?.Id ?? "", capabilities = new { platformManagement = false, agentGrants = includeAgent } }, JsonOptions) + "\n");
        foreach (var (template, destination) in new[] { ("frontend-App.vue.tpl", "App.vue"), ("frontend-environment.js.tpl", "common/environment.js"), ("frontend-config.js.tpl", "common/application.config.js"), ("frontend-yzp.js.tpl", "common/yzp.js"), ("frontend-project.js.tpl", "common/project.js"), ("frontend-login.vue.tpl", "pages/login.vue"), ("frontend-index.vue.tpl", "pages/main/index.vue"), ("frontend-main.vue.tpl", "pages/main/main.vue"), ("frontend-change_password.vue.tpl", "pages/main/change_password.vue"), ("frontend-menu.vue.tpl", "pages/main/menu.vue"), ("frontend-left.vue.tpl", "pages/main/left.vue"), ("frontend-top.vue.tpl", "pages/main/top.vue"), ("frontend-my.vue.tpl", "pages/main/my.vue"), ("frontend-manifest.json.tpl", "manifest.json"), ("frontend-index.html.tpl", "index.html"), ("frontend-verify.mjs.tpl", "scripts/verify-frontend.mjs"), ("frontend-README.md.tpl", "README.md") }) Text(destination, Template(template));
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
        Text(".metarill-frontend.json", JsonSerializer.Serialize(new { schemaVersion = 1, projectId = catalog.YzProjectId, authentication = "web-database-v1", files = manifest }, JsonOptions) + "\n");
        return files;
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
