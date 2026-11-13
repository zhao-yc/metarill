using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Testing;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>B121真实六模型的公开打包接线；实际Full构建与原资源核对不替代同包实库验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelPublicationTests
{
    /// <summary>独立配置编号和表名前缀也必须可发布，不能靠测试模型名称白名单放行。</summary>
    [Fact]
    public void Proven_R0_six_model_catalog_has_no_cross_excel_publication_gap()
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        var before = JsonSerializer.Serialize(catalog);
        var model = GenerationModel.Create(catalog, "Example.PublishedR0Excel");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        VerifyPathCapabilities(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content, true, true);
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>页级分隔符贯通原Vue、路径接口和Excel，级联名称传播仍使用表级结构。</summary>
    [Theory]
    [InlineData("shared-r0", " > ", " > ")]
    [InlineData("shared-r1", " > ", " > ")]
    [InlineData("global-r1", " > ", " > ")]
    [InlineData("shared-r0", "", "/")]
    [InlineData("shared-r1", null, "/")]
    public void Page_display_joiner_preserves_the_cross_table_storage_and_excel_contract(
        string profile, string? configured, string expected)
    {
        const string columnId = "25";
        var catalog = CrossTableExcelSharedGenerationTests.Catalog(profile);
        var control = catalog.ConfigColumns.Single(column => column.Id == columnId);
        var field = catalog.ConfigPageColumns.Single(column => column.ConfigPageId == "201" && column.ConfigColumnId == columnId);
        var tableDefinition = control.ControlDetail;
        var overlay = JsonNode.Parse(tableDefinition)!.AsObject();
        overlay["displayJoiner"] = configured;
        field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(overlay.ToJsonString());
        var before = JsonSerializer.Serialize(catalog);

        var model = GenerationModel.Create(catalog, "Example.PageCrossDisplay");
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = table.Pages.Single(item => item.Source.Id == "201");
        var group = Assert.Single(SharedCrossTableCompilation.ForPage(model, table, page).Groups);
        Assert.Equal(2, group.Plans.Count);
        var primary = group.Plans.Single(plan => plan.Read.Source.Source.Id == columnId);
        Assert.Equal(expected, primary.Read.Joiner);
        Assert.Equal("/", group.Plans.Single(plan => plan.Read.Source.Source.Id != columnId).Read.Joiner);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var frontend = UniappRenderer.Render(model);
        var detail = Assert.Single(frontend, file => file.Path.EndsWith("-detail-201.vue", StringComparison.Ordinal)).Content;
        Assert.Contains("displayJoiner", detail);
        var detailJoiner = System.Security.SecurityElement.Escape(JsonSerializer.Serialize(expected));
        Assert.Contains($"readonlyDisplayText([entity.countryName,entity.areaName,entity.placeName], {detailJoiner})", detail);
        var excel = CrossTableExcelRenderer.Helpers(model, table, page);
        Assert.Contains("CrossExcelResolve201_" + columnId, excel);
        Assert.Contains("ResolveAsync(text,3," + JsonSerializer.Serialize(expected), excel);
        Assert.Equal(tableDefinition, control.ControlDetail);
        Assert.Equal(before, JsonSerializer.Serialize(catalog));
    }

    /// <summary>三级逻辑父范围均可发布，末级固定父键继续从原导入列排除。</summary>
    [Theory]
    [InlineData("country_id", "OnlyNew", "list", true)]
    [InlineData("country_id", "", "", true)]
    [InlineData("area_id", "OnlyNew", "list", true)]
    [InlineData("place_id", "OnlyNew", "list", false)]
    [InlineData("country_id", "OverWriteByFirstColumn", "list", true)]
    [InlineData("area_id", "OverWriteByFirstColumn", "list", true)]
    [InlineData("place_id", "OverWriteByFirstColumn", "list", false)]
    public void Proven_R0_logical_parents_publish_import_and_list_export(string parentColumn, string importMode, string exportMode, bool pathImport)
    {
        var catalog = ParentScopedCatalog(parentColumn, importMode, exportMode);
        var model = GenerationModel.Create(catalog, "Example.ParentCrossExcel");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        using var capabilities = JsonDocument.Parse(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content);
        var path = Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),
            item => item.GetProperty("pageId").GetString() == "201" && item.GetProperty("columnId").GetString() == "25");
        Assert.True(path.GetProperty("sourceParentSupported").GetBoolean());
        Assert.Equal(pathImport, path.GetProperty("pathCellImportSupported").GetBoolean());
        Assert.True(path.GetProperty("pathCellExportSupported").GetBoolean());
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = Assert.Single(table.Pages);
        if (!pathImport)
            Assert.Equal(new[] { "name", "amount" }, ImportRenderer.Columns(page).Select(column => column.Source.ColumnName));
        if (importMode == "OverWriteByFirstColumn")
            Assert.Contains("PageImportError.AmbiguousOverwrite", ImportRenderer.Service(model, table, page));
    }

    /// <summary>详情导出复用原页父范围和权限，投影包含详情专用列而排除列表专用列。</summary>
    [Theory]
    [InlineData("country_id", "OverWriteByFirstColumn")]
    [InlineData("area_id", "OverWriteByFirstColumn")]
    [InlineData("place_id", "OverWriteByFirstColumn")]
    [InlineData("country_id", "OnlyNew")]
    [InlineData("area_id", "OnlyNew")]
    [InlineData("place_id", "OnlyNew")]
    public void Proven_R0_logical_parents_publish_detail_export(string parentColumn, string importMode)
    {
        var catalog = ParentScopedCatalog(parentColumn, importMode, "edit");
        var model = GenerationModel.Create(catalog, "Example.ParentCrossExcel");
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = Assert.Single(table.Pages);

        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        Assert.True(ExportRenderer.IncludesColumn(page, table.Columns.Single(column => column.Source.Id == "25")));
        if (parentColumn != "place_id")
        {
            var detailOnly = table.Columns.Single(column => column.Source.Id == "28");
            Assert.Contains(detailOnly, page.DetailColumns);
            Assert.DoesNotContain(detailOnly, page.ListColumns);
            Assert.True(ExportRenderer.IncludesColumn(page, detailOnly));
        }
    }

    /// <summary>共享两条路径使用同一发布证明；被固定为父键的控件保持原导入列规则。</summary>
    [Theory]
    [InlineData("shared-r0", "area_id", "OnlyNew", "edit", true)]
    [InlineData("shared-r0", "area_id", "OverWriteByFirstColumn", "list", true)]
    [InlineData("short-r0", "country_id", "OnlyNew", "list", true)]
    [InlineData("shared-r0", "place_id", "OverWriteByFirstColumn", "edit", false)]
    public void Shared_R0_parent_paths_publish_original_excel(string profile,string parent,string import,string export,bool primaryImport)
    {
        var catalog=CrossTableExcelSharedGenerationTests.Catalog(profile);
        var source=catalog.ConfigPages.Single(page=>page.Id=="201");
        source.WhereSql+=$" AND {parent}=@parent_id";
        source.Extra["importMethod"]=JsonSerializer.SerializeToElement(import);
        source.Extra["exportMethod"]=JsonSerializer.SerializeToElement(export);
        var model=GenerationModel.Create(catalog,"Example.SharedParentExcel");
        var table=model.Tables.Single(candidate=>candidate.Source.Id=="2");
        var page=Assert.Single(table.Pages);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        using var capabilities=JsonDocument.Parse(UniappRenderer.Render(model).Single(file=>file.Path=="generation-capabilities.json").Content);
        var controls=capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray()
            .Where(item=>item.GetProperty("pageId").GetString()=="201").ToArray();
        Assert.Equal(2,controls.Length);
        Assert.All(controls,control=>
        {
            Assert.True(control.GetProperty("sourceParentSupported").GetBoolean());
            Assert.True(control.GetProperty("pathCellExportSupported").GetBoolean());
            Assert.Equal(control.GetProperty("columnId").GetString()=="25"?primaryImport:true,
                control.GetProperty("pathCellImportSupported").GetBoolean());
        });
        var generatedImport=ImportRenderer.Service(model,table,page);
        if(import=="OverWriteByFirstColumn")
            Assert.Contains("PageImportError.AmbiguousOverwrite",generatedImport);
        if(primaryImport)
            Assert.DoesNotContain("ReadFixedCrossParent",generatedImport);
        else
        {
            Assert.Contains("fixedCrossParent ??= LoadFixedCrossParentAsync()",generatedImport);
            Assert.Contains("CrossReader201_25(actor,true,false)",generatedImport);
            Assert.Contains("path,clear,await readFixedCrossParent()",generatedImport);
            Assert.DoesNotContain("case \"主路径\"",generatedImport);
        }
    }

    /// <summary>数字首列和未知导出模式保留门槛，末级固定父键仍不扩大覆盖范围。</summary>
    [Theory]
    [InlineData("area_id", "OverWriteByFirstColumn", "list", CrossTableExcelRenderer.ImportAcceptancePending)]
    [InlineData("country_id", "OverWriteByFirstColumn", "list", CrossTableExcelRenderer.ImportAcceptancePending)]
    [InlineData("place_id", "OverWriteByFirstColumn", "list", CrossTableExcelRenderer.ExportAcceptancePending)]
    [InlineData("country_id", "OnlyNew", "unknown", CrossTableExcelRenderer.ExportAcceptancePending)]
    public void Unverified_R0_parent_paths_keep_publication_gaps(string parentColumn, string importMode, string exportMode, string expected)
    {
        var catalog = ParentScopedCatalog(parentColumn, importMode, exportMode);
        if (importMode == "OverWriteByFirstColumn")
            catalog.ConfigColumns.Single(column => column.Id == "22").LogicalType = "int64";
        var model = GenerationModel.Create(catalog, "Example.ParentCrossExcel");
        Assert.Contains(GenerationCapabilityAudit.Collect(model), gap => gap.PageId == "201" && gap.Reason == expected);
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
    }

    /// <summary>复用原跨表模型及页字段，形成固定逻辑父范围；验收准备与公开打包使用同一目录。</summary>
    internal static MetadataCatalog ParentScopedCatalog(string parentColumn, string importMode, string exportMode)
    {
        var catalog = CrossTableExcelImportColumnsTests.Catalog("cx_parent_excel", "string");
        catalog.YzProjectName = "跨表级联来源父范围验收";
        catalog.ConfigTables.Single(table => table.Id == "2").IsRongyu = 0;
        var page = catalog.ConfigPages.Single(page => page.Id == "201");
        page.Name = "来源明细";
        page.WhereSql += $" AND {parentColumn}=@parent_id";
        page.IsNew = page.IsSave = page.IsImport = page.IsExport = 1;
        page.Extra.Remove("type");
        page.Extra.Remove("importMethod");
        if (importMode.Length > 0) page.Extra["importMethod"] = JsonSerializer.SerializeToElement(importMode);
        if (exportMode.Length > 0) page.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == "25").IsShow = 1;
        // 末级使用已声明的级联列，根和中层在测试目录中显式登记逻辑父字段。
        if (parentColumn != "place_id")
        {
            catalog.ConfigColumns.Add(new()
            {
                Id = "28", ConfigTableId = "2", ColumnName = parentColumn, Name = parentColumn == "country_id" ? "国家ID" : "地区ID", LogicalType = "string",
                LinkConfigTableId = parentColumn == "country_id" ? "3" : "4", SortNo = 30
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id = "20128", ConfigPageId = "201", ConfigColumnId = "28", IsShow = 0, IsEdit = 1, SortNo = 30
            });
        }
        RedundantMultiNameCacheTests.Published(catalog);
        catalog.YzAdminTypes.Single().Name = "验收后台";
        catalog.ConfigMenus.Add(new()
        {
            Id = "2200", YzProjectId = catalog.YzProjectId, YzAdminTypeId = "5", Name = "国家",
            Kind = "GENERATED_PAGE", ConfigPageId = "301"
        });
        return catalog;
    }

    /// <summary>纯 bigint 三级父范围延续字符串 ID 协议、原导出列与固定父键的导入排除。</summary>
    [Theory]
    [InlineData("country_id", "OnlyNew", "list", true)]
    [InlineData("area_id", "OnlyNew", "list", true)]
    [InlineData("place_id", "OnlyNew", "list", false)]
    [InlineData("country_id", "OverWriteByFirstColumn", "edit", true)]
    [InlineData("area_id", "OverWriteByFirstColumn", "edit", true)]
    [InlineData("place_id", "OverWriteByFirstColumn", "edit", false)]
    public void Int64_R0_logical_parents_keep_the_original_excel_contract(string parentColumn, string importMode, string exportMode, bool pathImport)
    {
        var catalog = Int64ParentScopedCatalog(parentColumn, importMode, exportMode);
        var model = GenerationModel.Create(catalog, "Example.ParentCrossExcel");
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = Assert.Single(table.Pages);
        var source = table.Columns.Single(column => column.Source.Id == "25");
        var path = CrossTableWriteCompilation.Resolve(model, table, page, source).Definition!;
        Assert.All(path.Levels, level => Assert.Equal("long?", level.Id.Type));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        Assert.Equal(pathImport, ImportRenderer.Columns(page).Contains(source));
        Assert.True(ExportRenderer.IncludesColumn(page, source));
    }

    /// <summary>同一正式夹具使用纯 bigint 逻辑关系，候选表物理主键沿用原目录。</summary>
    internal static MetadataCatalog Int64ParentScopedCatalog(string parentColumn, string importMode, string exportMode)
    {
        var catalog = ParentScopedCatalog(parentColumn, importMode, exportMode);
        foreach (var column in catalog.ConfigColumns.Where(column => column.ConfigTableId == "2" &&
                     column.ColumnName is "country_id" or "area_id" or "place_id"))
            column.LogicalType = "int64";
        return catalog;
    }

    /// <summary>六种混合存储排列分别验证三级父范围的原追加、覆盖和两种导出。</summary>
    public static IEnumerable<object[]> MixedParentCases()
    {
        for (var mask = 1; mask < 7; mask++)
            foreach (var parent in new[] { "country_id", "area_id", "place_id" })
                foreach (var (import, export) in new[] { ("OnlyNew", "list"), ("OverWriteByFirstColumn", "edit") })
                    yield return [mask, parent, import, export];
    }

    /// <summary>路径保留每层逻辑类型；末级作为固定父键时沿用原导入表头。</summary>
    [Theory]
    [MemberData(nameof(MixedParentCases))]
    public void Mixed_R0_logical_parents_keep_the_original_excel_contract(int mask, string parentColumn, string importMode, string exportMode)
    {
        var catalog = MixedParentScopedCatalog(mask, parentColumn, importMode, exportMode);
        var model = GenerationModel.Create(catalog, "Example.ParentCrossExcel");
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = Assert.Single(table.Pages);
        var source = table.Columns.Single(column => column.Source.Id == "25");
        var path = CrossTableWriteCompilation.Resolve(model, table, page, source).Definition!;
        Assert.Equal(Enumerable.Range(0, 3).Select(index => (mask & (1 << index)) == 0 ? "string?" : "long?"),
            path.Levels.Select(level => level.Id.Type));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        Assert.Equal(parentColumn != "place_id", ImportRenderer.Columns(page).Contains(source));
        Assert.True(ExportRenderer.IncludesColumn(page, source));
    }

    /// <summary>显式登记三个逻辑槽以固定每层类型；候选的物理主键与父外键仍由原目录定义。</summary>
    internal static MetadataCatalog MixedParentScopedCatalog(int mask, string parentColumn, string importMode, string exportMode)
    {
        var catalog = ParentScopedCatalog(parentColumn, importMode, exportMode);
        var fields = new[] { ("country_id", "3", "国家ID"), ("area_id", "4", "地区ID"), ("place_id", "5", "城市ID") };
        for (var index = 0; index < fields.Length; index++)
        {
            var (columnName, target, label) = fields[index];
            var column = catalog.ConfigColumns.SingleOrDefault(item => item.ConfigTableId == "2" && item.ColumnName == columnName);
            if (column is null)
            {
                column = new()
                {
                    Id = (281 + index).ToString(System.Globalization.CultureInfo.InvariantCulture), ConfigTableId = "2",
                    ColumnName = columnName, Name = label, LinkConfigTableId = target, YzProjectId = catalog.YzProjectId
                };
                catalog.ConfigColumns.Add(column);
            }
            column.LogicalType = (mask & (1 << index)) == 0 ? "string" : "int64";
        }
        return catalog;
    }

    /// <summary>三种含字符串的目标父外键组合均验证三级父范围及原追加、覆盖、列表和详情导出。</summary>
    public static IEnumerable<object[]> TargetForeignKeyParentCases()
    {
        foreach (var (middle, leaf) in new[] { ("string", "int64"), ("int64", "string"), ("string", "string") })
            foreach (var parent in new[] { "country_id", "area_id", "place_id" })
                foreach (var (import, export) in new[] { ("OnlyNew", "list"), ("OverWriteByFirstColumn", "edit") })
                    yield return [middle, leaf, parent, import, export];
    }

    /// <summary>候选表父外键独立保留字符串或 bigint，来源槽、固定父范围和原表头各自生效。</summary>
    [Theory]
    [MemberData(nameof(TargetForeignKeyParentCases))]
    public void R0_logical_parents_allow_independent_text_target_foreign_keys(
        string middle, string leaf, string parentColumn, string importMode, string exportMode)
    {
        var catalog = TargetForeignKeyParentScopedCatalog(middle, leaf, parentColumn, importMode, exportMode);
        var model = GenerationModel.Create(catalog, "Example.ParentCrossExcel");
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = Assert.Single(table.Pages);
        var source = table.Columns.Single(column => column.Source.Id == "25");
        var path = CrossTableWriteCompilation.Resolve(model, table, page, source).Definition!;
        Assert.Equal(middle == "string" ? "string?" : "long?", path.Levels[1].Read.Parent!.Type);
        Assert.Equal(leaf == "string" ? "string?" : "long?", path.Levels[2].Read.Parent!.Type);
        Assert.Equal(new[] { "long?", "string?", "long?" }, path.Levels.Select(level => level.Id.Type));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        Assert.Equal(parentColumn != "place_id", ImportRenderer.Columns(page).Contains(source));
        Assert.True(ExportRenderer.IncludesColumn(page, source));
        var excel = CrossTableExcelRenderer.Helpers(model, table, page);
        Assert.Equal((middle == "string" ? 1 : 0) + (leaf == "string" ? 1 : 0),
            excel.AsSpan().Count("var parentKey=parent?.ToString(CultureInfo.InvariantCulture)"));
    }

    /// <summary>复用原三级父范围夹具，仅分别变更两张目标表的物理父外键。</summary>
    internal static MetadataCatalog TargetForeignKeyParentScopedCatalog(
        string middle, string leaf, string parentColumn, string importMode, string exportMode)
    {
        var catalog = MixedParentScopedCatalog(5, parentColumn, importMode, exportMode);
        catalog.ConfigColumns.Single(column => column.Id == "45").LogicalType = middle;
        catalog.ConfigColumns.Single(column => column.Id == "55").LogicalType = leaf;
        return catalog;
    }

    /// <summary>双字符串目标父外键的公开完整包须实际编译，并复用原组件及 Web 认证入口。</summary>
    [Fact]
    public async Task Public_R0_Full_with_text_target_foreign_keys_builds_and_keeps_web_authentication()
    {
        var source = NonRedundantApplicationFixture.Root();
        var catalog = TargetForeignKeyParentScopedCatalog("string", "string", "country_id", "OverWriteByFirstColumn", "edit");
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-r0-excel-text-target-parent");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog, directory, "Example.ParentCrossExcel", source);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, ".metarill-package.json")));
            Assert.Equal("FullApplication", manifest.RootElement.GetProperty("packageMode").GetString());
            Assert.Equal("web-database-v1", manifest.RootElement.GetProperty("authenticationProfile").GetString());
            Assert.False(File.Exists(Path.Combine(directory, CrossTableExcelApplicationPreparer.Marker)));
            const string component = "components/yzp-cascade-tree/yzp-cascade-tree.vue";
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(source, "frontend/metarill_uni_admin", component)),
                await File.ReadAllBytesAsync(Path.Combine(directory, "frontend", component)));
            var build = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", directory);
            foreach (var argument in new[] { "build", "backend/Application.Host/Application.Host.csproj", "-c", "Release", "-warnaserror", "--disable-build-servers", "--verbosity", "quiet" })
                build.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(build);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>公开 R1 保留全部六页、八个跨表控件及原导入导出开关。</summary>
    [Fact]
    public void Proven_R1_six_models_publish_all_eight_original_paths()
    {
        var catalog=GlobalCachedCrossExcelNativePreparation.Catalog(redundant:true);
        var model=GenerationModel.Create(catalog,"Example.PublishedR1Excel");
        var sources=model.Tables.Where(table=>table.Source.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)).ToArray();
        Assert.Equal(6,sources.Length);
        Assert.All(sources,table=>{var page=Assert.Single(table.Pages);Assert.Equal(1,page.Source.IsImport);Assert.Equal(1,page.Source.IsExport);});
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        using var capabilities=JsonDocument.Parse(UniappRenderer.Render(model).Single(file=>file.Path=="generation-capabilities.json").Content);
        var paths=capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray()
            .Where(row=>row.TryGetProperty("sourceMode",out var mode)&&mode.GetString()=="crossTable"&&row.GetProperty("storageMode").GetString()=="redundant").ToArray();
        Assert.Equal(8,paths.Length);
        Assert.All(paths,row=>Assert.True(row.GetProperty("fullApplicationAccepted").GetBoolean()));
    }

    /// <summary>只发布已验证的追加与列表路径导出，覆盖和详情导出仍须独立证明。</summary>
    [Theory]
    [InlineData("importMethod","OverWriteByFirstColumn",CrossTableExcelRenderer.ImportAcceptancePending)]
    [InlineData("exportMethod","edit",CrossTableExcelRenderer.ExportAcceptancePending)]
    public void R1_cross_excel_keeps_unverified_publication_gaps(string method,string mode,string expected)
    {
        var catalog = GlobalCrossCachedTargetFixture.Create(1, 1, "string", "int64", false);
        catalog.ConfigPages.Single(page=>page.Id=="30").Extra[method]=JsonSerializer.SerializeToElement(mode);
        var gaps = GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, "Example.UnprovenR1Excel"));
        Assert.Contains(gaps, gap => gap.Reason == expected);
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
    }

    /// <summary>混合自树和来源重复角色的原读写仍可编译，Excel公开用途须保留独立验收门槛。</summary>
    [Theory]
    [InlineData("mixed-self")]
    [InlineData("repeated-source")]
    [InlineData("overwrite")]
    public void Unproven_R0_compositions_keep_their_excel_gap(string kind)
    {
        var catalog = kind switch
        {
            "mixed-self" => NonRedundantMixedCascadeTests.Catalog("unproven_mix", "string", "int64"),
            "repeated-source" => RepeatedCrossSourceTests.Catalog("unproven_repeat", 3, "string", "int64", false),
            _ => NonRedundantCrossTableGenerationTests.Catalog("unproven_overwrite", "string", "int64")
        };
        // 混合／重复来源夹具本来已发布；该辅助方法会新增后台，只补尚未发布的覆盖导入夹具。
        if (kind == "overwrite") RedundantMultiNameCacheTests.Published(catalog);
        var importPage = catalog.ConfigPages.Single(page => page.Id == "201"); importPage.IsImport = 1;
        if (kind == "overwrite") importPage.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        var model = GenerationModel.Create(catalog, "Example.UnprovenR0Excel");
        var table = model.Tables.Single(table => table.Source.Id == "2");
        var page = table.Pages.Single(page => page.Source.Id == "201");
        Assert.NotEmpty(CrossTableWriteCompilation.ForService(model, table, page));
        Assert.Equal(CrossTableExcelRenderer.ImportAcceptancePending, NonRedundantCrossTableSubset.Reason(model, table, page));
        Assert.Throws<InvalidOperationException>(() => new FrontendPackager().Validate(catalog));
    }

    /// <summary>公开R0可用不应扩大内部准备通道；没有Excel用途仍必须拒绝。</summary>
    [Fact]
    public void Internal_preparation_still_rejects_catalog_without_excel_usage()
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        foreach (var page in catalog.ConfigPages) { page.IsImport = 0; page.IsExport = 0; page.Extra.Remove("importMethod"); page.Extra.Remove("exportMethod"); }
        var model = GenerationModel.Create(catalog, "Example.NoExcel");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        VerifyPathCapabilities(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content, false, false);
        Assert.Throws<InvalidOperationException>(() => CrossTableExcelApplicationPreparer.RequireCurrent(catalog, "Example.NoExcel"));
    }

    /// <summary>原导入／导出是独立开关；只开放一种用途不能把另一种也声明为支持。</summary>
    [Theory]
    [InlineData(1, 0, "")]
    [InlineData(0, 1, "")]
    [InlineData(1, 1, "OnlyNew")]
    public void Original_import_export_switches_are_reported_independently(int imports, int exports, string mode)
    {
        var catalog = GlobalCachedCrossExcelNativePreparation.Catalog();
        foreach (var page in catalog.ConfigPages)
        {
            page.IsImport = imports; page.IsExport = exports;
            if (imports == 1 && mode.Length > 0) page.Extra["importMethod"] = JsonSerializer.SerializeToElement(mode);
            if (imports == 0) page.Extra.Remove("importMethod");
            if (exports == 0) page.Extra.Remove("exportMethod");
        }
        var model = GenerationModel.Create(catalog, "Example.IndependentExcelFlags");
        new FrontendPackager().Validate(catalog);
        VerifyPathCapabilities(UniappRenderer.Render(model).Single(file => file.Path == "generation-capabilities.json").Content, imports == 1, exports == 1);
    }

    /// <summary>六模型包含八个实际跨表控件，能力清单必须与公开包／关闭用途的页面分别一致。</summary>
    private static void VerifyPathCapabilities(string text, bool imports, bool exports)
    {
        using var profile = JsonDocument.Parse(text);
        Assert.Empty(profile.RootElement.GetProperty("unsupportedControls").EnumerateArray());
        var paths = profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray()
            .Where(row => row.TryGetProperty("sourceMode", out var mode) && mode.GetString() == "crossTable" && row.GetProperty("storageMode").GetString() == "nonRedundant").ToArray();
        Assert.Equal(8, paths.Length);
        Assert.All(paths, row => { Assert.Equal(imports, row.GetProperty("pathCellImportSupported").GetBoolean()); Assert.Equal(exports, row.GetProperty("pathCellExportSupported").GetBoolean()); });
    }

    /// <summary>正规公开打包必须保留Web身份、完整元数据及原组件；包使用自身源码严格构建。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_six_model_Full_builds_with_original_pages_and_web_identity(bool overwrite)
    {
        var source = NonRedundantApplicationFixture.Root();
        var catalog = overwrite ? NonRedundantCrossOverwriteApplicationTests.Catalog() : GlobalCachedCrossExcelNativePreparation.Catalog();
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-r0-excel-publication");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog, directory, "Example.PublishedR0Excel", source);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, ".metarill-package.json")));
            Assert.Equal("FullApplication", manifest.RootElement.GetProperty("packageMode").GetString());
            Assert.Equal("web-database-v1", manifest.RootElement.GetProperty("authenticationProfile").GetString());
            Assert.False(File.Exists(Path.Combine(directory, CrossTableExcelApplicationPreparer.Marker)));
            VerifyPathCapabilities(await File.ReadAllTextAsync(Path.Combine(directory, "backend/generated/generation-capabilities.json")), true, true);
            var actual = JsonSerializer.Deserialize<MetadataCatalog>(await File.ReadAllTextAsync(Path.Combine(directory, "backend/metadata/project.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(JsonSerializer.Serialize(catalog), JsonSerializer.Serialize(actual));
            foreach (var asset in new[] { "components/yzp-cascade-tree/yzp-cascade-tree.vue", "common/yzp_runtime/detailLayoutRuntime.js" })
                Assert.Equal(File.ReadAllBytes(Path.Combine(source, "frontend/metarill_uni_admin", asset)), File.ReadAllBytes(Path.Combine(directory, "frontend", asset)));
            foreach (var table in catalog.ConfigTables)
            {
                var hand = Path.Combine(directory, "backend/generated/Services", GenerationModel.Pascal(table.TableName) + "Service.cs");
                await File.AppendAllTextAsync(hand, "\n// 公开R0跨表Excel重复生成必须保留手写业务。\n");
            }
            var partials = Directory.GetFiles(Path.Combine(directory, "backend/generated/Services"), "*.cs")
                .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal)).ToDictionary(path => path, File.ReadAllBytes);
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "backend/generated"), "Example.PublishedR0Excel");
            Assert.Equal(32, partials.Count);
            foreach (var partial in partials) Assert.Equal(partial.Value, await File.ReadAllBytesAsync(partial.Key));
            var build = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", directory);
            foreach (var argument in new[] { "build", "backend/Application.Host/Application.Host.csproj", "-c", "Release", "-warnaserror", "--disable-build-servers", "--verbosity", "quiet" }) build.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(build);
            var hostDirectory = Path.Combine(directory, "backend/Application.Host");
            var host = NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", hostDirectory);
            host.ArgumentList.Add(Path.Combine(hostDirectory, "bin/Release/net10.0/Application.Host.dll"));
            host.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; host.Environment["DOTNET_ENVIRONMENT"] = "Production";
            host.Environment["Authentication__Mode"] = "Web"; host.Environment["Authentication__DevelopmentBootstrap__Enabled"] = "false";
            host.Environment["Database__Provider"] = "mysql"; host.Environment["Database__ExpectedDatabase"] = "offline";
            host.Environment["Database__ConnectionString"] = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";
            host.Environment["Files__RootDirectory"] = Path.Combine(directory, "files"); host.Environment["Exports__RootDirectory"] = Path.Combine(directory, "exports");
            await using var running = await FreshHost.StartProbeAsync(host, "/health");
            foreach (var table in catalog.ConfigTables.Where(table => table.TableName.EndsWith("_cross_consumer", StringComparison.Ordinal)))
            {
                var page = catalog.ConfigPages.Single(page => page.ConfigTableId == table.Id);
                var route = "/api/admin/auto/" + table.TableName + "/" + page.Id + "/";
                foreach (var action in new[] { "page", "importDatas" + page.Id, "initExport" })
                    await FreshWebHttp.SendAsync(running.Client, System.Net.Http.HttpMethod.Post, route + action, new { }, System.Net.HttpStatusCode.Unauthorized);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }
}
