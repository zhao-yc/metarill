using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>共享非冗余级联的正常生成入口必须保留原精确管理及导入导出权限。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedCrossExactScopeGenerationTests
{
    /// <summary>非公开范围列只参与授权查询，两个原级联仍生成一个写入协调组。</summary>
    [Theory]
    [InlineData("scopeKey","scope_key")]
    [InlineData("countryId","country_id")]
    [InlineData("countryName","country_name")]
    public async Task Published_shared_cascade_preserves_exact_scope_for_crud_and_excel(string scope,string field)
    {
        var catalog = Catalog(scope);
        var model = GenerationModel.Create(catalog, "Example.SharedExactScope");
        var table = model.Tables.Single(table => table.Source.Id == "2");
        var page = Assert.Single(table.Pages);
        var shared = SharedCrossTableCompilation.ForPage(model, table, page);
        Assert.True(shared.Gaps.Count == 0, string.Join("；", shared.Gaps.Values.Distinct()));
        Assert.Equal(2, Assert.Single(shared.Groups).Plans.Count);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var dependencies = NonRedundantReadCompilation.ForPage(model, table, page);
        Assert.Contains(field, dependencies.Dependencies);
        Assert.DoesNotContain(dependencies.Outputs, output => output.Storage == "scope_key");

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-shared-exact");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var repository = NonRedundantApplicationFixture.Root();
            new ApplicationPackager().Create(catalog, directory, model.Namespace, repository);
            var path = Path.Combine(directory, "backend/generated/Services/SharedCrossSourceService.g.cs");
            var generated = await File.ReadAllTextAsync(path);
            Assert.Contains("PackSharedCross201_25(entity,submitted,sharedCrossMutations", generated);
            Assert.Contains("ReadFilters201(new PageRequest{ParentId=parentId},actor, \"detail\")", generated);
            Assert.Contains("Scope201(actor, parentId, \"delete\")", generated);
            Assert.Contains("Scope201(actor, parentId, \"copy\")", generated);
            Assert.Contains("RecordMutationOperation.Import ? \"import\" : \"update\"", generated);
            Assert.Contains("ReadFilters201(new PageRequest{ParentId=request.ParentId},actor, \"import\")", generated);
            Assert.Contains("ImportMatchPlan201", generated);
            if(scope!="scopeKey")
            {
                Assert.Contains("ExactReadPlan201=global::Example.SharedExactScope.Generated.GeneratedNonRedundantReadCatalog.CompileCross",generated);
                Assert.Contains("FixedReadFilter.ForSharedExact(\""+field+"\"",generated);
            }
            Assert.Contains("ReadFilters201(request,actor, \"export\")", generated);
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(repository, "frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),
                await File.ReadAllBytesAsync(Path.Combine(directory, "frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            await NonRedundantApplicationFixture.BuildAsync(directory, "restore");
            await NonRedundantApplicationFixture.BuildAsync(directory, "build");
        }
        finally
        {
            await TestDirectoryCleanup.DeleteAsync(directory, boundary);
        }
    }

    /// <summary>复用现有双分支非冗余元数据，声明普通物理匹配列和原覆盖导入。</summary>
    internal static MetadataCatalog Catalog(string scope="scopeKey", bool shorter=false)
    {
        var catalog = shorter
            ? SharedCrossTableSparseGenerationTests.Catalog("int64", "string", shorter:true)
            : SharedCrossTableSparseGenerationTests.Catalog("string", "int64");
        catalog.ConfigColumns.Add(new()
        {
            Id = "90", ConfigTableId = "2", YzProjectId = catalog.YzProjectId,
            ColumnName = "scope_key", LogicalType = "string", ColumnLength = "50"
        });
        var page = catalog.ConfigPages.Single(page => page.Id == "201");
        page.Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement(scope);
        page.Extra["importMethod"] = JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        page.IsExport = 1;
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == page.Id && field.ConfigColumnId == "22")
            .Extra["isReadOnly"] = JsonSerializer.SerializeToElement(1);
        catalog.YzAdminTypes.Single(admin => admin.Id == page.YzAdminTypeId)
            .Extra["exactDataManageMatchField"] = JsonSerializer.SerializeToElement("realName");
        return catalog;
    }
}
