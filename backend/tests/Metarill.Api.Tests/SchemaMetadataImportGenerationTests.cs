using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Generation;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Api.Tests;

public sealed partial class ApiAcceptanceTests
{
    /// <summary>实际导入→原按钮建页/菜单→原字段工作台→回读元数据→Full包构建；不手写或补丁生成业务代码。</summary>
    [MySqlApiFact]
    public Task Metadata_import_original_page_configuration_without_project_column_builds_full_application() => BuildImportedFullApplication(false);
    /// <summary>有真实项目列时保留原建页默认项目条件，完整包不能把它降为无范围查询。</summary>
    [MySqlApiFact]
    public Task Metadata_import_original_page_configuration_with_project_column_builds_full_application() => BuildImportedFullApplication(true);
    /// <summary>两种物理源各自经过真实元数据接口和完整包，不以手工目录或固定模型代替平台配置。</summary>
    private async Task BuildImportedFullApplication(bool hasProject)
    {
        await using var source = await ImportCase.Create(1, factory => { factory.UseKestrel(0); return factory; }); await using var project = await ForeignImportCase.Create(source);
        var repository = project.Demo.BaseAddress!; Assert.NotEqual(source.Client.BaseAddress, repository);
        var root = Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")!; var evidenceRoot = Environment.GetEnvironmentVariable("METARILL_IMPORT_GENERATION_EVIDENCE");
        var directory = Path.Combine(evidenceRoot ?? Path.Combine(Path.GetTempPath(), "metarill-import-generation"), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); var output = Path.Combine(directory, "application");
        var succeeded = false;
        try
        {
            if (hasProject)
            {
                // 本轮夹具在导入前模拟已有的租户源结构和数据；后续导入/配置/生成均不得增删该列。
                await source.Execute($"ALTER TABLE `{source.Names[0]}` ADD COLUMN yz_project_id bigint NULL");
                await using var connection = source.Sessions.Business.Provider.CreateConnection(source.Sessions.Business.Options); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = $"UPDATE `{source.Names[0]}` SET yz_project_id=@project WHERE id=1"; var parameter = command.CreateParameter(); parameter.ParameterName = "@project"; parameter.Value = long.Parse(project.ProjectId); command.Parameters.Add(parameter); Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }
            var schema = source.Factory.Services.GetRequiredService<ProjectSchemaService>().Resolve(Project); var inspection = await schema.InspectForImportAsync(source.Names); var original = inspection.Snapshot.Fingerprint;
            var plan = Data(await Post(project.Other, ImportRoute + "prepare", new { tableNames = source.Names })); var saved = Data(await Post(project.Other, ImportRoute + "apply", new { planId = plan.GetProperty("planId").GetString(), confirmSha256 = plan.GetProperty("confirmSha256").GetString() }));
            var tableId = Assert.Single(saved.GetProperty("tables").EnumerateArray()).GetProperty("tableId").GetString()!;
            Assert.Empty(await source.Store.ReadAsync("config_page", new Dictionary<string,object?> { ["config_table_id"] = long.Parse(tableId) }));
            // 后台和控件是用户在原配置页面的显式选择；导入不猜显示用途，不改源列的逻辑/物理类型。
            Data(await Post(project.Other, ForeignImportCase.TableRoute + "/update", new { id = tableId, name = "导入记录", yzAdminTypeId = project.AdminId }));
            var columns = await source.Store.ReadAsync("config_column", new Dictionary<string,object?> { ["config_table_id"] = long.Parse(tableId) });
            foreach (var column in columns) Data(await Post(project.Other, "/admin/auto/config_column/6217715532627968/update?parentId=" + tableId, new { id = SqlRowStore.Text(column, "id"), controlKind = "" }));
            var created = Data(await Post(project.Other, "/admin/custom/config_table/create_page", new { id = tableId })); var pageId = created.GetProperty("configuration").GetProperty("pageId").GetString()!;
            Assert.Equal(hasProject ? 4 : 3, (await source.Store.ReadAsync("config_page_column", new Dictionary<string,object?> { ["config_page_id"] = long.Parse(pageId) })).Count);
            // 源没有yz_project_id，沿原编辑接口明确设置范围；不让新页默认项目条件引用不存在的源列。
            Data(await Post(project.Other, "/admin/auto/config_page/6191175925956608/update?parentId=" + tableId, new { id = pageId, whereSql = hasProject ? "yz_project_id=@current_yz_project_id" : "id>0", isCopy = 1, isExport = 1, isImport = 1 }));
            var fields = await Body(await project.Other.GetAsync("/api/admin/custom/index/config_page_columns?configPageId=" + pageId)); var revision = fields.GetProperty("map").GetProperty("revision").GetString();
            var patches = Data(fields).EnumerateArray().Select(field => new { id = field.GetProperty("id").GetString(), isInGrid = field.GetProperty("configColumnColumnName").GetString() == "name" ? 1 : 0, isInEdit = field.GetProperty("configColumnColumnName").GetString() == "name" ? 1 : 0, isInFilter = field.GetProperty("configColumnColumnName").GetString() == "name" ? 1 : 0, isMust = field.GetProperty("configColumnColumnName").GetString() == "name" ? 1 : 0 }).ToArray();
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/custom/index/save_config_page_columns") { Content = JsonContent.Create(patches) }) { request.Headers.Add("X-Metarill-Metadata-Revision", revision); Data(await Body(await project.Other.SendAsync(request))); }
            Data(await Post(project.Other, "/admin/custom/config_page/create_menu", new { id = pageId }));
            var model = await source.Factory.Services.GetRequiredService<MetadataRepository>().LoadProjectAsync(project.ProjectId); Assert.Single(model.ConfigTables); Assert.Single(model.ConfigPages); Assert.Equal(hasProject ? 4 : 3, model.ConfigColumns.Count); Assert.Equal(2, model.ConfigMenus.Count); Assert.Equal(hasProject ? "yz_project_id=@current_yz_project_id" : "id>0", model.ConfigPages[0].WhereSql);
            Assert.Equal(hasProject, model.ConfigColumns.Any(column => column.ColumnName == "yz_project_id")); Assert.Equal(original, (await schema.InspectForImportAsync(source.Names)).Snapshot.Fingerprint); await source.AssertSourceRows();
            await File.WriteAllTextAsync(Path.Combine(directory, "metadata.json"), JsonSerializer.Serialize(model, JsonDefaults.Options));
            await File.WriteAllTextAsync(Path.Combine(directory, "source.json"), JsonSerializer.Serialize(new { projectId = project.ProjectId, tableName = source.Names[0], tableId, pageId, adminTypeId = project.AdminId, hasProject, snapshot = inspection.Snapshot, knownSeed = new { id = "1", name = "本轮业务行", projectId = hasProject ? project.ProjectId : null }, scopeConfiguredThroughOriginalApi = model.ConfigPages[0].WhereSql }, JsonDefaults.Options));
            const string ns = "Example.ImportedSource"; var package = new ApplicationPackager().Create(model, output, ns, root); Assert.True(package.FrontendIncluded); Assert.Equal(ApplicationPackageMode.FullApplication, package.Mode);
            var hand = Directory.EnumerateFiles(Path.Combine(output, "backend/generated/Services"), "*.cs").Single(path => !path.EndsWith(".g.cs", StringComparison.Ordinal)); await File.AppendAllTextAsync(hand, "\n// 本轮重复生成保护标记。\n"); var handBytes = await File.ReadAllBytesAsync(hand);
            var published = JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(output, "backend/metadata/project.json")), JsonDefaults.Options)!;
            var before = ImportGeneratedHashes(output); for (var iteration = 0; iteration < 2; iteration++) { new CSharpGenerator().Generate(published, Path.Combine(output, "backend/generated"), ns); Assert.Equal(before, ImportGeneratedHashes(output)); Assert.Equal(handBytes, await File.ReadAllBytesAsync(hand)); }
            foreach (var command in new[] { "restore", "build" })
            {
                var args = new List<string> { command, "backend/Application.Host/Application.Host.csproj", "--nologo", "--disable-build-servers", "-v:q" }; if (command == "build") args.AddRange(["-c", "Release", "--no-restore", "-warnaserror"]);
                await RunImportedPackageProcess(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")!, output, directory, command, args.ToArray());
            }
            await RunImportedPackageProcess("node", output, directory, "vue", Path.Combine(root, "backend/tests/Metarill.Api.Tests/SchemaMetadataImportVueProbe.mjs"), root, output, source.Names[0], pageId);
            Assert.Equal(original, (await schema.InspectForImportAsync(source.Names)).Snapshot.Fingerprint); await source.AssertSourceRows();
            await File.WriteAllTextAsync(Path.Combine(directory, "receipt.json"), JsonSerializer.Serialize(new { projectId = project.ProjectId, pageId, hasProject, originalImport = true, originalPageAndMenuButtons = true, originalFieldWorkbench = true, sourceFingerprint = original, sourceUnchanged = true, fullPackage = true, productionWebAcceptance = false, buildPassed = true, originalVueCompiled = true, repeatGenerationRounds = 2, handwrittenProtected = true, metadataSha256 = package.MetadataSha256, formalRequiresSeparateOwnedDatabase = true }, JsonDefaults.Options));
            source.Record(new { result = "original-page-full-package-build", directory = Path.GetFileName(directory), sourceUnchanged = true }); succeeded = true;
        }
        finally
        {
            // 原菜单工作台逐叶删除，避免后台删除被本轮菜单引用阻挡；随后源/项目夹具负责配置与物理源清理。
            while (true)
            {
                var menus = await source.Store.ReadAsync("config_menu", new Dictionary<string,object?> { ["yz_project_id"] = long.Parse(project.ProjectId) }); if (menus.Count == 0) break;
                var node = menus.First(row => !menus.Any(child => SqlRowStore.Text(child, "parent_menu_id") == SqlRowStore.Text(row, "id"))); var snapshot = Data(await Post(project.Other, "/admin/custom/menu_workbench/snapshot", new { projectId = project.ProjectId, adminTypeId = project.AdminId }));
                Data(await Post(project.Other, "/admin/custom/menu_workbench/delete", new { projectId = project.ProjectId, adminTypeId = project.AdminId, id = SqlRowStore.Text(node, "id"), revision = snapshot.GetProperty("revision").GetString() }));
            }
            if (evidenceRoot is null && succeeded) Directory.Delete(directory, true);
        }
    }

    /// <summary>生成目录逐文件摘要包含手写扩展，不把修改后的产物补丁冒充生成器输出。</summary>
    private static string ImportGeneratedHashes(string output) => string.Join("\n", Directory.EnumerateFiles(Path.Combine(output, "backend/generated"), "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => Path.GetRelativePath(output, path) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));

    /// <summary>独立包只编译包内项目，日志位于私有证据目录，超时只终止本测试自己的进程。</summary>
    private static async Task RunImportedPackageProcess(string executable, string working, string evidence, string name, params string[] args)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = working, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 }; foreach (var argument in args) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(deadline.Token); } catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); await Task.WhenAll(output, error); throw; }
        await File.WriteAllTextAsync(Path.Combine(evidence, name + ".stdout.log"), await output); await File.WriteAllTextAsync(Path.Combine(evidence, name + ".stderr.log"), await error); Assert.True(process.ExitCode == 0, "独立包子进程失败，首因见私有证据目录。" );
    }
}
