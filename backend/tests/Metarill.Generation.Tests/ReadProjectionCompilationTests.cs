using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实元数据和原规则消费者验证用途边界；本slice尚不接SQL/VO生成入口。</summary>
public sealed class ReadProjectionCompilationTests
{
    /// <summary>保留原页标志优先级，查询/排序/项目列不因范围需要而自动输出。</summary>
    [Fact]
    public void Purposes_follow_original_flags_without_expanding_all_readable()
    {
        var catalog = Catalog(); var before = JsonSerializer.Serialize(catalog); var (model, table, page) = Model(catalog);
        var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.True(plan.Detail.CanCompile); Assert.True(plan.Selection.CanCompile);
        Assert.Equal(new[] { "id", "name", "status" }, Names(plan.List));
        Assert.Equal(new[] { "flag", "id", "remark", "secret" }, Names(plan.Detail));
        Assert.Equal(Names(plan.List), Names(plan.Selection));
        Assert.Contains(plan.List.OutputFields, field => field.JsonName == "statusShow" && field.Kind == ReadProjectionOutputKind.ConstantShow && field.Source.Source.ColumnName == "status");
        Assert.DoesNotContain(plan.Detail.OutputFields, field => field.JsonName == "statusShow");
        Assert.DoesNotContain(plan.List.PhysicalColumns.Concat(plan.Detail.PhysicalColumns), column => column.Source.ColumnName is "query_key" or "order_key" or "yz_project_id" or "delete_no");
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
        Assert.Equal(6, page.Readable.Count);
        Assert.Equal(new[] { "flag", "remark", "secret" }, page.Writable.Select(column => column.Source.ColumnName).Order(StringComparer.Ordinal));
    }

    /// <summary>实际生成器及C#编译仍走现有入口；新IR不会提前改变现有VO或模板。</summary>
    [Fact]
    public async Task Real_generated_original_views_compile_while_ir_is_not_yet_wired()
    {
        var catalog = Catalog(); var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-read-projection"); var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N")); var generated = Path.Combine(directory, "generated");
        try
        {
            new CSharpGenerator().Generate(catalog, generated, "Example.Projection");
            var list = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/customer-list-101.vue"));
            var detail = await File.ReadAllTextAsync(Path.Combine(generated, "frontend/pages/yzp/customer-detail-101.vue"));
            Assert.DoesNotContain("item.remark", list); Assert.Contains("item.statusShow", list); Assert.Contains("v-html='entity.remark'", detail);
            Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName == "remark");
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            // 当前VO仍为原并集，明确证明没有把此IR误报成已完成SQL投影接线。
            Assert.NotNull(assembly.GetType("Example.Projection.Generated.VOs.CustomerPage101Vo")!.GetProperty("Remark"));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>List辅助名及Detail独立目标父均来自固定已读列，不扩成整个Readable集合。</summary>
    [Fact]
    public void Relation_name_and_independent_parent_are_explicit_dependencies()
    {
        var catalog = RelationOptionsContractTests.Catalog();
        catalog.ConfigColumns.Add(new() { Id = "27", ConfigTableId = "2", ColumnName = "target_name", LogicalType = "string" });
        catalog.ConfigPageColumns.Add(new() { Id = "2027", ConfigPageId = "201", ConfigColumnId = "27", IsShow = 0, IsEdit = 1 });
        catalog.ConfigPageColumns.Add(new() { Id = "2026", ConfigPageId = "201", ConfigColumnId = "26", IsShow = 1, IsEdit = 0 });
        catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = "yz_project_id=@current_yz_project_id";
        var (model, table, page) = Model(catalog, "2", "201"); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.True(plan.Detail.CanCompile);
        Assert.Contains(plan.List.PhysicalColumns, column => column.Source.Id == "27");
        Assert.Contains(plan.List.Dependencies, dependency => dependency.ColumnId == "27" && dependency.Reasons.Any(reason => reason.Contains("辅助名称", StringComparison.Ordinal)));
        Assert.Contains(plan.Detail.Dependencies, dependency => dependency.ColumnId == "26" && dependency.Reasons.Any(reason => reason.Contains("独立目标父", StringComparison.Ordinal)));
        Assert.Single(plan.Detail.PhysicalColumns, column => column.Source.Id == "26");
        Assert.DoesNotContain(plan.Detail.PhysicalColumns, column => column.Source.Id == "23");
        Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName.Contains("Target", StringComparison.Ordinal));
    }

    /// <summary>原UI明确的ID回退不凭空创造名称属性或SQL列。</summary>
    [Fact]
    public void Missing_relation_auxiliary_keeps_documented_id_fallback()
    {
        var (model, table, page) = Model(RelationOptionsContractTests.Catalog(), "2", "201");
        var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.Contains(plan.List.OutputFields, field => field.JsonName == "targetId");
        Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName == "targetName");
        Assert.Contains(plan.List.Dependencies, dependency => dependency.ColumnId == "25" && dependency.Reasons.Any(reason => reason.Contains("ID回退", StringComparison.Ordinal)));
    }

    /// <summary>目标name仅详情可读仍是精确入站依赖；同表另一页不能代替当前目标页授权。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Selection_plan_adds_exact_inbound_name_without_expanding_list_or_other_target_pages(bool multi)
    {
        var catalog = SelectionCatalog(multi);
        var name = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "301" && field.ConfigColumnId == "32"); name.IsShow = 0; name.IsEdit = 1;
        var (model, table, page) = Model(catalog, "3", "301"); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Selection.CanCompile); Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName == "name");
        Assert.Contains(plan.Selection.OutputFields, field => field.JsonName == "name");
        Assert.Contains(plan.Selection.Dependencies, dependency => dependency.ColumnId == "32" && dependency.Reasons.Any(reason => reason.Contains("201/25", StringComparison.Ordinal)));
        catalog.ConfigPageColumns.Remove(name);
        catalog.ConfigPages.Add(new() { Id = "303", ConfigTableId = "3" });
        catalog.ConfigPageColumns.Add(new() { Id = "3032", ConfigPageId = "303", ConfigColumnId = "32" });
        (model, table, page) = Model(catalog, "3", "301"); plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.False(plan.Selection.CanCompile); Assert.DoesNotContain(plan.Selection.OutputFields, field => field.JsonName == "name");
        Assert.Contains(plan.Selection.Gaps, gap => gap.Reason.Contains("当前目标页", StringComparison.Ordinal));
    }

    /// <summary>nameField=id覆盖继承原pageId，确认合同不生成不存在的名称列。</summary>
    [Fact]
    public void Name_id_overlay_keeps_only_real_target_id()
    {
        var catalog = SelectionCatalog(true);
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == "25").Extra["controlDetail"] = JsonSerializer.SerializeToElement("{\"nameField\":\"id\"}");
        var field = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "301" && field.ConfigColumnId == "32"); field.IsShow = 0; field.IsEdit = 1;
        var (model, table, page) = Model(catalog, "3", "301"); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Selection.CanCompile); Assert.DoesNotContain(plan.Selection.OutputFields, field => field.JsonName == "name");
        Assert.Contains(plan.Selection.Dependencies, dependency => dependency.ColumnId == "31" && dependency.Reasons.Any(reason => reason.Contains("入站选择", StringComparison.Ordinal)));
    }

    /// <summary>真正原List/Detail渲染证明：页级覆盖不创造详情弹层，反向覆盖也不消除原按钮。</summary>
    [Theory]
    [InlineData(false, "", "SelectPage")]
    [InlineData(false, "", "MultiSelectPage")]
    [InlineData(false, "SelectPage", "TextArea")]
    [InlineData(false, "SelectPage", "MultiSelectPage")]
    [InlineData(true, "", "MultiSelectPage")]
    [InlineData(true, "MultiSelectPage", "TextArea")]
    [InlineData(true, "SelectPage", "MultiSelectPage")]
    [InlineData(true, "MultiSelectPage", "SelectPage")]
    public void Effective_control_matrix_matches_actual_original_views(bool multi, string sourceKind, string pageKind)
    {
        var catalog = SelectionCatalog(multi);
        catalog.ConfigColumns.Single(column => column.Id == "25").ControlKind = sourceKind;
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "201" && field.ConfigColumnId == "25").Extra["controlKind"] = JsonSerializer.SerializeToElement(pageKind);
        var name = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "301" && field.ConfigColumnId == "32"); name.IsShow = 0; name.IsEdit = 1;
        var (model, table, page) = Model(catalog, "2", "201"); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        var list = OriginalUniappListRenderer.Render(model, table, page); var detail = OriginalUniappDetailRenderer.Render(model, table, page, []);
        var opensSelection = sourceKind is "SelectPage" or "MultiSelectPage";
        Assert.Equal(opensSelection, detail.Contains("openSelect25()", StringComparison.Ordinal)); Assert.DoesNotContain("openSelect25()", list);
        var auxiliary = multi ? "targetNames" : "targetName";
        Assert.Contains("relationDisplayText(item." + auxiliary + ")", list);
        Assert.Contains(plan.List.OutputFields, output => output.JsonName == auxiliary);
        Assert.True(plan.List.CanCompile, string.Join(";", plan.List.Gaps.Select(gap => gap.Reason)));
        Assert.True(plan.Detail.CanCompile, string.Join(";", plan.Detail.Gaps.Select(gap => gap.Reason)));
        var target = model.Tables.Single(table => table.Source.Id == "3");
        var selection = ReadProjectionCompilation.ForPage(model, target, target.Pages.Single(page => page.Source.Id == "301")).Selection;
        Assert.True(selection.CanCompile, string.Join(";", selection.Gaps.Select(gap => gap.Reason)));
        Assert.Equal(opensSelection, selection.Dependencies.Any(dependency => dependency.ColumnId == "32" && dependency.Reasons.Any(reason => reason.Contains("固定入站", StringComparison.Ordinal))));
        Assert.Equal(opensSelection, selection.OutputFields.Any(output => output.JsonName == "name"));
    }

    /// <summary>只有实际详情中出现的选择控件才建立入站确认合同；网格只显示既有辅助名。</summary>
    [Fact]
    public void Grid_only_selection_does_not_create_an_unreachable_inbound_confirmation()
    {
        var catalog = SelectionCatalog(false);
        foreach (var field in catalog.ConfigPageColumns.Where(item => item.ConfigColumnId == "25")) { field.IsEdit = 0; field.Extra["isInEdit"] = JsonSerializer.SerializeToElement(0); }
        var name = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "301" && field.ConfigColumnId == "32"); name.IsShow = 0; name.IsEdit = 1;
        var (model, table, page) = Model(catalog, "2", "201");
        Assert.DoesNotContain("openSelect25()", OriginalUniappDetailRenderer.Render(model, table, page, []));
        var target = model.Tables.Single(table => table.Source.Id == "3");
        var selection = ReadProjectionCompilation.ForPage(model, target, target.Pages.Single(page => page.Source.Id == "301")).Selection;
        Assert.True(selection.CanCompile); Assert.DoesNotContain(selection.OutputFields, output => output.JsonName == "name");
    }

    /// <summary>详情不采用页级controlKind覆盖，不透明控件缺口只按真正用途生效。</summary>
    [Fact]
    public void Detail_control_kind_uses_table_definition_while_list_uses_page_overlay()
    {
        var catalog = Catalog(); var field = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13");
        field.Extra["controlKind"] = JsonSerializer.SerializeToElement("CustomControl");
        var (model, table, page) = Model(catalog);
        Assert.Contains("v-html='entity.remark'", OriginalUniappDetailRenderer.Render(model, table, page, []));
        Assert.True(ReadProjectionCompilation.ForPage(model, table, page).Detail.CanCompile);
        field.Extra["isInGrid"] = JsonSerializer.SerializeToElement(1);
        (model, table, page) = Model(catalog); Assert.False(ReadProjectionCompilation.ForPage(model, table, page).List.CanCompile);
        catalog.ConfigColumns.Single(column => column.Id == "13").ControlKind = "CustomControl";
        field.Extra["controlKind"] = JsonSerializer.SerializeToElement("RTF");
        (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.False(plan.Detail.CanCompile);
    }

    /// <summary>来源Multi但目标不是type2没有完整合同，不能退成单选可编译计划。</summary>
    [Fact]
    public void Multi_source_with_single_target_is_an_explicit_confirmation_gap()
    {
        var catalog = SelectionCatalog(true); catalog.ConfigPages.Single(page => page.Id == "301").Extra["type"] = JsonSerializer.SerializeToElement(1);
        var (model, table, page) = Model(catalog, "2", "201"); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.False(plan.Detail.CanCompile); Assert.Contains(plan.Detail.Gaps, gap => gap.Reason.Contains("type=2", StringComparison.Ordinal));
        var target = model.Tables.Single(table => table.Source.Id == "3");
        Assert.False(ReadProjectionCompilation.ForPage(model, target, target.Pages.Single(page => page.Source.Id == "301")).Selection.CanCompile);
    }

    /// <summary>按钮嵌套条件由原runtime实际求值，常量中出现字段名不会成为读取依赖。</summary>
    [Fact]
    public async Task Nested_button_rules_and_constant_show_match_original_runtime()
    {
        var catalog = Catalog();
        var rule = JsonSerializer.SerializeToElement(new { logic = "AND", conditions = new[] { new { field = "flag", @operator = "eq", value = "queryKey" } }, groups = new[] { new { logic = "OR", conditions = new[] { new { field = "statusShow", @operator = "in", valueList = new[] { "secret", "name" } } } } } });
        catalog.ConfigButtons.Add(Button("EditAndGrid", rule));
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.Contains(plan.List.OutputFields, field => field.JsonName == "flag");
        Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName is "secret" or "queryKey");
        Assert.Single(plan.List.PhysicalColumns, column => column.Source.ColumnName == "status");
        await RuleProbe(rule, plan.List.OutputFields.Select(field => field.JsonName).ToArray());
    }

    /// <summary>详情可见条件与只读依赖单独加入，不把详情整组字段带回列表。</summary>
    [Fact]
    public void Detail_visibility_and_children_readonly_have_explicit_narrow_dependencies()
    {
        var catalog = Catalog(); catalog.ConfigPages.Single().Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("status");
        catalog.ConfigColumns.Single(column => column.Id == "13").Extra["visibleCondition"] = JsonSerializer.SerializeToElement("{\"conditions\":[{\"field\":\"statusShow\",\"value\":\"启用\"}]}");
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.Detail.CanCompile); Assert.Contains(plan.Detail.OutputFields, field => field.JsonName == "statusShow");
        Assert.Contains(plan.Detail.Dependencies, dependency => dependency.ColumnId == "16" && dependency.Reasons.Any(reason => reason.Contains("主子只读", StringComparison.Ordinal)));
        Assert.Contains(plan.Detail.Dependencies, dependency => dependency.ColumnId == "16" && dependency.Reasons.Any(reason => reason.Contains("可见条件", StringComparison.Ordinal)));
        Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName == "remark");
    }

    /// <summary>未使用的详情坏控件不污染List，未知用途仍拒绝而不靠全Readable兜底。</summary>
    [Fact]
    public void Opaque_detail_gap_does_not_poison_independent_list_plan()
    {
        var catalog = Catalog(); catalog.ConfigColumns.Single(column => column.Id == "13").ControlKind = "CustomControl";
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.False(plan.Detail.CanCompile);
        Assert.Contains(plan.Detail.Gaps, gap => gap.ColumnId == "13" && gap.Reason.Contains("CustomControl", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Detail.PhysicalColumns, column => column.Source.Id == "13");
    }

    /// <summary>未知条件、物理来源或输出冲突成为Gap，不默认为读取全部列。</summary>
    [Theory]
    [InlineData("join")] [InlineData("nonred")] [InlineData("virtual")] [InlineData("script")] [InlineData("path")]
    [InlineData("shape")] [InlineData("duplicate")] [InlineData("hidden")] [InlineData("operator")] [InlineData("collision")]
    public void Unproven_definitions_are_explicit_gaps(string kind)
    {
        var catalog = Catalog();
        if (kind == "join") catalog.ConfigColumns.Single(column => column.Id == "12").Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        if (kind == "nonred") catalog.ConfigTables.Single().IsRongyu = 0;
        if (kind == "virtual") catalog.ConfigColumns.Single(column => column.Id == "12").IsVirtualColumn = 1;
        var rule = kind switch { "script" => "return entity.flag", "path" => "{\"conditions\":[{\"field\":\"remark.length\"}]}", "shape" => "{\"groups\":{}}", "duplicate" => "{\"conditions\":[],\"conditions\":[]}", "hidden" => "{\"conditions\":[{\"field\":\"queryKey\"}]}", "operator" => "{\"conditions\":[{\"field\":\"flag\",\"operator\":\"sql\"}]}", _ => "" };
        if (rule.Length > 0) catalog.ConfigButtons.Add(Button("EditAndGrid", JsonSerializer.SerializeToElement(rule)));
        if (kind == "collision") { catalog.ConfigColumns.Add(new() { Id = "21", ConfigTableId = "1", ColumnName = "status_show", LogicalType = "string" }); catalog.ConfigPageColumns.Add(new() { Id = "121", ConfigPageId = "101", ConfigColumnId = "21", IsShow = 0, IsEdit = 1 }); }
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.False(plan.List.CanCompile); Assert.NotEmpty(plan.List.Gaps);
        Assert.DoesNotContain(plan.List.PhysicalColumns, column => column.Source.ColumnName is "query_key" or "order_key" or "yz_project_id");
        Assert.True(plan.List.PhysicalColumns.Count < table.Columns.Count);
    }

    /// <summary>列表顶部按钮不按行判断，不因此拉取隐藏列。</summary>
    [Fact]
    public void Non_row_button_parameters_do_not_become_column_dependencies()
    {
        var catalog = Catalog(); catalog.ConfigButtons.Add(Button("List", JsonSerializer.SerializeToElement("{\"conditions\":[{\"field\":\"secret\"}]}")));
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.DoesNotContain(plan.List.OutputFields, field => field.JsonName == "secret");
    }

    /// <summary>原详情未知分支和未完成数据库多选不能因普通物理列或_ids展示而误报可编译。</summary>
    [Theory]
    [InlineData("FutureControl")] [InlineData("MultiCheckBoxDatabase")]
    public void Unproven_control_cannot_escape_through_a_physical_or_suffix_display(string kind)
    {
        var catalog = SelectionCatalog(true); catalog.ConfigColumns.Single(column => column.Id == "25").ControlKind = kind;
        var (model, table, page) = Model(catalog, "2", "201"); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.False(plan.Detail.CanCompile); Assert.Contains(plan.Detail.Gaps, gap => gap.ColumnId == "25" && gap.Reason.Contains(kind, StringComparison.Ordinal));
        if (kind == "MultiCheckBoxDatabase") Assert.False(plan.List.CanCompile);
        Assert.DoesNotContain(plan.Detail.PhysicalColumns, column => column.Source.Id == "25");
    }

    /// <summary>原runtime对OnlyDeve固定返回不可见，不读取其规则字段。</summary>
    [Fact]
    public void Only_developer_buttons_do_not_require_hidden_row_values()
    {
        var catalog = Catalog(); catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new { id = "dev", configPageId = "101", place = "EditAndGrid", visibleScheme = "OnlyDeve", visibleRuleJson = "{\"conditions\":[{\"field\":\"queryKey\"}]}" }));
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.True(plan.List.CanCompile); Assert.True(plan.Detail.CanCompile);
        Assert.DoesNotContain(plan.List.OutputFields.Concat(plan.Detail.OutputFields), output => output.JsonName == "queryKey");
    }

    /// <summary>按钮固定参数损坏时返回Gap，不能抛异常绕过计划说明。</summary>
    [Fact]
    public void Malformed_fixed_button_setting_is_a_gap()
    {
        var catalog = Catalog(); catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new { configPageId = "101", place = new { unknown = true } }));
        var (model, table, page) = Model(catalog); var plan = ReadProjectionCompilation.ForPage(model, table, page);
        Assert.Contains(plan.List.Gaps, gap => gap.Reason.Contains("按钮固定", StringComparison.Ordinal));
    }

    /// <summary>原字段可见标志、详情大文本与查询专属字段组成独立夹具。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog = GeneratorTests.Catalog();
        var name = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "12"); name.IsShow = 0; name.IsEdit = 1; name.Extra["isInGrid"] = JsonSerializer.SerializeToElement(1); name.Extra["isInEdit"] = JsonSerializer.SerializeToElement(0);
        var detail = catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "13"); detail.IsShow = 1; detail.IsEdit = 0; detail.Extra["isInGrid"] = JsonSerializer.SerializeToElement(0); detail.Extra["isInEdit"] = JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Single(column => column.Id == "13").ControlKind = "RTF";
        foreach (var (id, columnName, type, show, edit) in new[] { ("16", "status", "int32", 1, 0), ("17", "flag", "string", 0, 1), ("18", "secret", "string", 0, 1), ("19", "query_key", "string", 0, 0), ("20", "order_key", "string", 0, 0) })
        {
            catalog.ConfigColumns.Add(new() { Id = id, ConfigTableId = "1", ColumnName = columnName, LogicalType = type, ControlKind = id == "16" ? "ConstList" : "", ControlDetail = id == "16" ? "启用|1,停用|0" : "" });
            catalog.ConfigPageColumns.Add(new() { Id = "1" + id, ConfigPageId = "101", ConfigColumnId = id, IsShow = show, IsEdit = edit, Extra = id == "19" ? new() { ["isSearch"] = JsonSerializer.SerializeToElement(1) } : id == "20" ? new() { ["isSort"] = JsonSerializer.SerializeToElement(1) } : [] });
        }
        return catalog;
    }

    /// <summary>复用真实多选模型，单选仅调整已知控件和存储定义。</summary>
    private static MetadataCatalog SelectionCatalog(bool multi)
    {
        var catalog = GeneratedMultiSelectContractTests.Catalog();
        if (!multi)
        {
            var source = catalog.ConfigColumns.Single(column => column.Id == "25"); source.ColumnName = "target_id"; source.LogicalType = "int64"; source.ControlKind = "SelectPage";
            catalog.ConfigColumns.Single(column => column.Id == "27").ColumnName = "target_name";
            catalog.ConfigPages.Single(page => page.Id == "301").Extra["type"] = JsonSerializer.SerializeToElement(1);
        }
        return catalog;
    }

    /// <summary>按钮只提供原显示条件，不接受执行逻辑。</summary>
    private static JsonElement Button(string place, JsonElement rule) => JsonSerializer.SerializeToElement(new { id = "b1", configPageId = "101", place, visibleScheme = "AlwaysVisible", visibleRuleJson = rule });
    /// <summary>从真实生成模型选取输入，避免镜像PageModel掩盖原标志规则。</summary>
    private static (GenerationModel Model, TableModel Table, PageModel Page) Model(MetadataCatalog catalog, string tableId = "1", string pageId = "101")
    { var model = GenerationModel.Create(catalog, "Example.Projection"); var table = model.Tables.Single(table => table.Source.Id == tableId); return (model, table, table.Pages.Single(page => page.Source.Id == pageId)); }
    /// <summary>断言物理字段集合，不把派生输出当作SQL列。</summary>
    private static string[] Names(ReadProjectionPlan plan) => plan.PhysicalColumns.Select(column => column.Source.ColumnName).Order(StringComparer.Ordinal).ToArray();

    /// <summary>执行真实原规则模块，不镜像其条件解释器。</summary>
    private static async Task RuleProbe(JsonElement rule, string[] fields, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!, "ReadProjectionRuleProbe.mjs")); start.ArgumentList.Add(rule.GetRawText()); start.ArgumentList.Add(JsonSerializer.Serialize(fields));
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token); Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }
}
