using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>平台角色权限仅修正陈旧目标引用；原权限开关、关联协议及其余缺口必须保持。</summary>
public sealed class PlatformPermissionRelationTests
{
    private const string SourcePage = "2063264600601714690";
    private static readonly (string ColumnId, string Column, string TableId, string Table, string PageId)[] Targets =
    [
        ("2063264600681406465", "config_menu_id", "2200000000000000281", "config_menu", "2200000000000099000"),
        ("2063264600941453314", "yz_role_id", "1692532858499969026", "yz_role", "1692532858529329154")
    ];
    private static readonly string[] RemainingGaps =
    [
        "1679390851298631682:child_config_page_id:filter",
        "1679390851298631682:parent_config_page_id:filter",
        "2076977554350190593:config_column_id:detail",
        "6031906822946816:yz_role_ids:detail",
        "6186303424888832:yz_yzp_version_id:detail",
        "6218036784398336:config_column_id:detail"
    ];

    /// <summary>直接验证首次安装源的固定表页关系，不通过放宽编译器或移除原控件消除缺口。</summary>
    [Fact]
    public void Published_platform_references_resolve_both_targets_and_keep_six_other_gaps()
    {
        var catalog = Catalog(); MetadataValidation.Validate(catalog);
        var model = GenerationModel.Create(catalog, "Example.PlatformPermissions");
        var page = model.Tables.Single(table => table.Source.TableName == "yz_role_permission").Pages.Single(page => page.Source.Id == SourcePage);
        Assert.Equal(0, page.Source.IsNew); Assert.Equal(0, page.Source.IsSave); Assert.Equal(0, page.Source.IsDelete);
        foreach (var expected in Targets)
        {
            var column = catalog.ConfigColumns.Single(column => column.Id == expected.ColumnId);
            Assert.Equal(expected.Column, column.ColumnName); Assert.Equal("List", column.ControlKind);
            Assert.Equal(expected.TableId, column.LinkConfigTableId); Assert.Equal(expected.PageId, column.Extra["linkConfigPageId"].GetString());
            using var definition = JsonDocument.Parse(column.ControlDetail);
            Assert.Equal(expected.Table, definition.RootElement.GetProperty("table").GetString());
            Assert.Equal(expected.TableId, definition.RootElement.GetProperty("tableId").GetString());
            Assert.Equal("name", definition.RootElement.GetProperty("nameField").GetString());
            Assert.Equal("", definition.RootElement.GetProperty("whereSql").GetString()); Assert.Equal("", definition.RootElement.GetProperty("orderBy").GetString());
            var field = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == SourcePage && field.ConfigColumnId == expected.ColumnId);
            Assert.False(field.Extra.ContainsKey("controlDetail"));
            var relation = Assert.Single(RelationCompilation.ForPage(model, page), relation => relation.SourceColumn.Source.Id == expected.ColumnId);
            Assert.Equal(expected.TableId, relation.Target.Source.Id); Assert.Equal(expected.PageId, relation.TargetPage.Source.Id);
            Assert.False(relation.Target.IsGlobal); Assert.Contains(relation.Target.Columns, column => column.Source.ColumnName == "yz_project_id");
            Assert.Equal("name", relation.NameColumn.Source.ColumnName); Assert.Empty(relation.TargetParents);
        }
        Assert.Equal(RemainingGaps, GenerationCapabilityAudit.Collect(model).Select(Key).Order(StringComparer.Ordinal));
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
    }

    /// <summary>真实平台整批生成及编译后，原空请求和分页/选中回读仍在服务读取前检查双方页面权限。</summary>
    [Fact]
    public async Task Generated_platform_options_compile_with_original_routes_and_dual_permission_gates()
    {
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-platform-permission-references"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "generated");
        try
        {
            new CSharpGenerator().Generate(Catalog(), output, "Example.PlatformPermissions");
            using (var capabilities = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "generation-capabilities.json"))))
                Assert.Equal(RemainingGaps, capabilities.RootElement.GetProperty("unsupportedControls").EnumerateArray().Select(gap => gap.GetProperty("pageId").GetString() + ":" + gap.GetProperty("column").GetString() + ":" + gap.GetProperty("usage").GetString()).Order(StringComparer.Ordinal));
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var controllerType = assembly.GetType("Example.PlatformPermissions.Generated.Controllers.YzRolePermissionPage" + SourcePage + "Controller", true)!;
            Assert.Null(controllerType.GetMethod("Add")); Assert.Null(controllerType.GetMethod("Update")); Assert.Null(controllerType.GetMethod("Delete"));
            foreach (var expected in Targets)
            {
                var endpoint = "findNameId" + (expected.Column == "config_menu_id" ? "ConfigMenu" : "YzRole") + expected.ColumnId;
                foreach (var (suffix, route, request) in new (string, string, object?)[]
                {
                    ("", endpoint, null),
                    ("Page", endpoint + "/page", new RelationOptionsPageRequest()),
                    ("Selected", endpoint + "/selected", new RelationOptionsSelectedRequest { SelectedIds = ["9007199254740993"] })
                })
                {
                    var method = controllerType.GetMethod(endpoint + suffix)!;
                    Assert.Contains(method.GetCustomAttributes<HttpPostAttribute>(), attribute => attribute.Template == route);
                    if (suffix == "")
                    {
                        Assert.Equal(typeof(RelationOptionsLegacyRequest), method.GetParameters()[0].ParameterType);
                        Assert.Equal(EmptyBodyBehavior.Allow, method.GetParameters()[0].GetCustomAttribute<FromBodyAttribute>()!.EmptyBodyBehavior);
                    }
                    foreach (var deniedPage in new[] { SourcePage, expected.PageId })
                    {
                        var authorization = new DeniedPageAuthorization(deniedPage);
                        var controller = Activator.CreateInstance(controllerType, [null, authorization])!;
                        var task = (Task)method.Invoke(controller, [request, CancellationToken.None])!;
                        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => task);
                        Assert.Equal(deniedPage == SourcePage ? [SourcePage + ":list"] : new[] { SourcePage + ":list", expected.PageId + ":list" }, authorization.Requested);
                    }
                }
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>缺口键只包含页面、列和用途，避免把提示措辞变化误当成能力变化。</summary>
    private static string Key(GenerationCapabilityGap gap) => gap.PageId + ":" + gap.Column + ":" + gap.Usage;
    /// <summary>从本任务源码定位平台元数据，不混用主仓库或进程工作目录。</summary>
    private static MetadataCatalog Catalog([CallerFilePath] string file = "") => JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../metadata/platform.json"))), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed class DeniedPageAuthorization(string deniedPage) : IRuntimeAuthorization
    {
        internal List<string> Requested { get; } = [];
        /// <summary>测试身份仅用于验证权限顺序，不打开数据库或伪造跨项目选择。</summary>
        public RuntimeActor RequireActor() => new(1, "测试", false, "1608097412978577409", "1");
        /// <summary>来源或目标任一拒绝时必须终止，空服务保证没有提前查询数据。</summary>
        public void RequirePagePermission(string pageId, string operation)
        {
            Requested.Add(pageId + ":" + operation);
            if (pageId == deniedPage) throw new UnauthorizedAccessException("没有页面权限。");
        }
    }
}
