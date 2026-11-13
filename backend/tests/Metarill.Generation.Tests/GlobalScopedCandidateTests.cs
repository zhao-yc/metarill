using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定候选可跨行归属，生成的普通 CRUD 仍只能读写发布身份自己的行。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed partial class GlobalScopedCandidateTests
{
    private const string ApplicationNamespace="Example.GlobalScopedCandidate";

    [Theory]
    [InlineData(false,0,"int64")]
    [InlineData(false,1,"string")]
    [InlineData(true,0,"string")]
    [InlineData(true,1,"int64")]
    public async Task Generated_global_candidate_and_owned_service_paths_are_distinct(bool multiple,int storage,string projectType)
    {
        var catalog=Catalog(multiple,storage,projectType);
        var model=GenerationModel.Create(catalog,ApplicationNamespace);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var table=model.Tables.Single(table=>table.Source.Id=="1");
        var selection=table.Pages.Single(page=>page.Source.Id=="10");
        var owned=table.Pages.Single(page=>page.Source.Id=="19");
        Assert.True(NonRedundantReadCompilation.GlobalSelectionPage(table,selection));
        Assert.False(NonRedundantReadCompilation.GlobalSelectionPage(table,owned));
        Assert.Contains(FixedPageQueryCompilation.For(table,owned).IdentityScopes,scope=>scope.Source=="project");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-scoped-candidate");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ApplicationNamespace);
            var source=string.Join("\\n",Directory.GetFiles(generated,"*.g.cs",SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.Contains("PageAsync(CandidateReadPlan10",source);
            Assert.Contains("StatisticsAsync(CandidateReadPlan10",source);
            Assert.Contains("PageAsync(ReadPlan19",source);
            Assert.Contains("KeyRowsAsync(ReadPlan10",source);
            Assert.Contains("""CompileGlobalCandidate("1","10",""",source);
            Assert.Contains(multiple?"DerivedCheckboxPlan30_38":"DerivedOptionsPlan30_34",source);
            if(multiple)Assert.Contains("KeyRowsAsync(CandidateReadPlan10",source);
            await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
        }
        finally
        {
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    [Fact]
    public void Candidate_plan_rejects_unpublished_or_context_dependent_target_pages()
    {
        var catalog=Catalog(false,0,"int64");
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"1","19",["id"]));
        Assert.Throws<InvalidOperationException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"1","30",["id"]));
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"3","30",["id"]));
        var page=catalog.ConfigPages.Single(page=>page.Id=="10");
        page.WhereSql="yz_project_id=@current_yz_project_id";
        Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileGlobalCandidate(catalog,"1","10",["id"]));
    }

    /// <summary>同一目标的候选页和普通业务页使用不同固定范围，所有元数据来自真实生成模型。</summary>
    private static MetadataCatalog Catalog(bool multiple,int storage,string projectType)
    {
        var catalog=multiple
            ?GlobalMultipleDerivedTargetGenerationTests.Catalog(storage,false,"int64")
            :GlobalDerivedTargetGenerationTests.Catalog(storage,"int64","int64");
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId=catalog.YzProjectId,
            ColumnName="yz_project_id",Name="项目归属",LogicalType=projectType,ColumnLength=projectType=="string"?"20":""});
        catalog.ConfigPages.Add(new(){Id="19",ConfigTableId="1",Name="项目业务记录",YzProjectId=catalog.YzProjectId,YzAdminTypeId="9",
            IsNew=1,IsDelete=1,IsCopy=1,IsExport=1,WhereSql="yz_project_id=@current_yz_project_id",
            Extra=new(){["type"]=JsonSerializer.SerializeToElement(0)}});
        foreach(var column in catalog.ConfigPageColumns.Where(column=>column.ConfigPageId=="10").ToArray())
            catalog.ConfigPageColumns.Add(new(){Id="19"+column.ConfigColumnId,ConfigPageId="19",ConfigColumnId=column.ConfigColumnId,
                YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=column.IsEdit});
        catalog.ConfigMenus.Add(new(){Id="70019",YzProjectId=catalog.YzProjectId,YzAdminTypeId="9",Name="项目业务记录",Kind="GENERATED_PAGE",ConfigPageId="19"});
        return catalog;
    }
}
