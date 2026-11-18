using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>未证明的派生路径必须在写文件前拒绝，不以物理列或空回显伪装该路径已支持。</summary>
public sealed class NonRedundantStorageGenerationTests
{
    /// <summary>新输出不留stage；既有输出及手写文件均不因能力拒绝被改写。</summary>
    [Fact]
    public void Missing_nonredundant_read_plan_rejects_before_any_generation_write()
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-storage",Guid.NewGuid().ToString("N"));var catalog=Catalog();
        try
        {
            Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.Nonred"));Assert.False(Directory.Exists(root));
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"developer.cs"),"// 手写文件保持。\n");var before=File.ReadAllBytes(Path.Combine(root,"developer.cs"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.Nonred"));Assert.Equal(before,File.ReadAllBytes(Path.Combine(root,"developer.cs")));Assert.Single(Directory.GetFiles(root));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    /// <summary>完整包同样保留明确能力缺口，不因前端能显示只读项就绕过读取实现。</summary>
    [Fact]
    public void Full_frontend_reports_the_missing_derived_read_before_packaging()
    {
        var catalog=Catalog();var model=GenerationModel.Create(catalog,"Example.Nonred");Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Usage=="runtime"&&gap.Reason.Contains("非冗余固定读取",StringComparison.Ordinal));var error=Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));Assert.Contains("非冗余固定读取",error.Message);
    }
    /// <summary>未声明派生项的普通非冗余业务列依旧生成，不扩大到一律拒绝表模式。</summary>
    [Fact]
    public void Pure_physical_nonredundant_table_keeps_existing_generation()
    {
        var catalog=Catalog();catalog.ConfigColumns.RemoveAll(column=>column.Id=="13");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="13");var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-storage",Guid.NewGuid().ToString("N"));try{Assert.NotEmpty(new CSharpGenerator().Generate(catalog,root,"Example.Nonred").GeneratedFiles);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    /// <summary>未配置辅助列也可能需要自动关系名称读取，不能将它误当作已支持的纯物理CRUD。</summary>
    [Fact]
    public void Nonredundant_relation_without_explicit_display_column_still_requires_read_plan()
    {
        var catalog=Catalog();var column=catalog.ConfigColumns.Single(item=>item.Id=="13");column.Extra.Clear();column.ColumnName="customer_id";column.LogicalType="int64";column.ControlKind="List";var root=Path.Combine(Path.GetTempPath(),"metarill-nonred-storage",Guid.NewGuid().ToString("N"));Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,root,"Example.Nonred"));Assert.False(Directory.Exists(root));
    }
    /// <summary>原布局仍渲染，列表与详情读取只依赖真实字段，两种导出均留空布局单元格。</summary>
    [Theory]
    [InlineData("list")]
    [InlineData("edit")]
    public async Task Nonredundant_layout_preserves_frontend_and_stays_out_of_storage(string exportMode)
    {
        var catalog = LayoutCatalog();
        var pageMetadata = Assert.Single(catalog.ConfigPages);
        pageMetadata.Extra["exportMethod"] = JsonSerializer.SerializeToElement(exportMode);
        var model = GenerationModel.Create(catalog, "Example.Layout");
        var table = Assert.Single(model.Tables);
        var page = Assert.Single(table.Pages);
        var projections = ReadProjectionCompilation.ForPage(model, table, page);
        foreach (var projection in new[] { projections.List, projections.Detail, projections.Selection })
        {
            Assert.Empty(projection.Gaps);
            Assert.NotNull(projection.FixedRead);
            Assert.Equal(new[] { "id", "name" }, projection.OutputFields.Select(field => field.JsonName));
        }
        Assert.DoesNotContain(page.Writable, column => column.IsLayoutOnly);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-nonred-layout");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        var generated = Path.Combine(directory, "generated");
        try
        {
            new CSharpGenerator().Generate(catalog, generated, model.Namespace);
            var detail = File.ReadAllText(Path.Combine(generated, "frontend/pages/yzp/orders-detail-4.vue"));
            var list = File.ReadAllText(Path.Combine(generated, "frontend/pages/yzp/orders-list-4.vue"));
            var service = File.ReadAllText(Path.Combine(generated, "Services/OrdersService.g.cs"));
            var requests = File.ReadAllText(Path.Combine(generated, "DTOs/OrdersPage4Requests.g.cs"));
            var response = File.ReadAllText(Path.Combine(generated, "VOs/OrdersPage4Vo.g.cs"));
            foreach (var label in new[] { "原版标题", "原版标签" })
            {
                Assert.Contains(label, detail);
                Assert.Contains(label, list);
                Assert.Contains(JsonSerializer.Serialize(label), service);
            }
            Assert.Contains("readonlyDisplayText(entity.sectionTitle)", detail);
            Assert.Contains("readonlyDisplayText(entity.sectionTab)", detail);
            Assert.DoesNotContain("\"section_title\"", service);
            Assert.DoesNotContain("\"section_tab\"", service);
            Assert.DoesNotContain("SectionTitle", requests);
            Assert.DoesNotContain("SectionTab", requests);
            Assert.DoesNotContain("SectionTitle", response);
            Assert.DoesNotContain("SectionTab", response);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>布局可呈现在原页面，但布局字段不能作为查询条件、搜索或排序列。</summary>
    [Theory]
    [InlineData("condition")]
    [InlineData("search")]
    [InlineData("sort")]
    public void Nonredundant_layout_cannot_be_used_as_a_query_dependency(string usage)
    {
        var catalog = LayoutCatalog();
        var metadata = Assert.Single(catalog.ConfigPages);
        var field = catalog.ConfigPageColumns.Single(column => column.ConfigColumnId == "13");
        switch (usage)
        {
            case "condition": metadata.WhereSql = "section_title = '标题'"; break;
            case "search": field.Extra["isSearch"] = JsonSerializer.SerializeToElement(1); break;
            case "sort": metadata.OrderBy = "section_title desc"; break;
        }
        var directory = Path.Combine(Path.GetTempPath(), "metarill-nonred-layout", Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => new CSharpGenerator().Generate(catalog, directory, "Example.Layout"));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>同一业务页声明标题、标签及真实字段，保持原列表与表单配置。</summary>
    private static MetadataCatalog LayoutCatalog()
    {
        var catalog = Catalog();
        var title = catalog.ConfigColumns.Single(column => column.Id == "13");
        title.Extra.Clear();
        title.ColumnName = "section_title";
        title.Name = "原版标题";
        title.ControlKind = "Title";
        catalog.ConfigColumns.Add(new() { Id = "14", ConfigTableId = "2", ColumnName = "section_tab", Name = "原版标签", LogicalType = "string", ControlKind = "Tab" });
        catalog.ConfigPageColumns.Add(new() { Id = "24", ConfigPageId = "4", ConfigColumnId = "14" });
        foreach (var field in catalog.ConfigPageColumns)
            field.IsShow = field.IsEdit = 1;
        catalog.ConfigPages[0].IsImport = 1;
        return catalog;
    }

    /// <summary>只保留已声明列及原页面，不构造可伪造的可信模块标记。</summary>
    private static MetadataCatalog Catalog()=>new()
    {
        YzProjectId="1",ConfigTables=[new(){Id="2",TableName="orders",YzProjectId="1",IsRongyu=0}],YzAdminTypes=[new(){Id="3",YzProjectId="1"}],ConfigPages=[new(){Id="4",ConfigTableId="2",YzAdminTypeId="3",YzProjectId="1"}],
        ConfigColumns=[new(){Id="11",ConfigTableId="2",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="12",ConfigTableId="2",ColumnName="name",LogicalType="string"},new(){Id="13",ConfigTableId="2",ColumnName="customer_notes",LogicalType="string",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("customer.notes")}}}],
        ConfigPageColumns=[new(){Id="21",ConfigPageId="4",ConfigColumnId="11"},new(){Id="22",ConfigPageId="4",ConfigColumnId="12"},new(){Id="23",ConfigPageId="4",ConfigColumnId="13"}]
    };
}
