using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成候选、读图和 Excel 名称入口共用带逻辑条件的发布页。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NestedCrossCandidateGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logical_target_page_keeps_nested_scope_after_metadata_publication(bool shared)
    {
        const string ns="Example.NestedCrossScope";
        var catalog=Catalog(shared);
        var before=JsonSerializer.Serialize(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="90");
        var page=Assert.Single(table.Pages);
        var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));
        Assert.True(FixedPageConditionCompilation.HasCrossFields(plan.Read.Levels[0].Page));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nested-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,ns);
            var service=await File.ReadAllTextAsync(Path.Combine(output,"Services/ScopedCandidateOuterService.g.cs"));
            Assert.Contains("CompileCrossPageCandidate(\"2\",\"201\"",service);
            Assert.Contains("CrossTargetPlan900_9004_0.BindAccount(actor)",service);
            Assert.Contains("CrossTargetRows900_9004_0Async(actor,",service);
            Assert.Contains("reader.CurrentRowsAsync(plan,bound",service);
            Assert.Contains("CrossExcelFind900_9004Async",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType(ns+".Generated.Services.ScopedCandidateOuterService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            var targetPlan=(NonRedundantReadPlan)type.GetField("CrossTargetPlan900_9004_0",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            Assert.Equal(new[]{"id","name"},targetPlan.OutputTypes.Keys);
            if(shared)
            {
                // 完整应用重新生成必须识别新增运行时文件，也要重新证明精简目录里的候选范围。
                var packager=new ApplicationPackager();
                var root=NonRedundantApplicationFixture.Root();
                var installed=Path.Combine(directory,"installed");
                var candidate=Path.Combine(directory,"candidate");
                packager.Create(catalog,installed,ns,root);
                packager.Create(catalog,candidate,ns,root);
                var updater=new LocalGenerationUpdater();
                var update=updater.Preview(installed,candidate,["sync-runtime","generate-all-page"]);
                Assert.True(update.CanApply);
                Assert.Equal("unchanged",updater.Apply(update,update.PlanSha256).Status);
            }
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    private static MetadataCatalog Catalog(bool shared)
    {
        var catalog=shared?SharedCrossTableSparseGenerationTests.Catalog("string","int64"):
            NonRedundantCrossFullTests.Catalog("string","int64",false);
        // 无父参数的原目标页保留可见名称，父子页面继续由原范围区分。
        var inner=catalog.ConfigTables.Single(item=>item.Id=="2");
        var innerPage=catalog.ConfigPages.Single(item=>item.Id=="201");
        innerPage.WhereSql+=" AND country_name <> '不可选路径' AND store_id=@current_store_id";
        catalog.ConfigColumns.Add(new(){Id="8900",ConfigTableId=inner.Id,ColumnName="store_id",LogicalType="int64"});
        catalog.ConfigTables.AddRange([
            new(){Id="90",TableName="scoped_candidate_outer",IsRongyu=0},
            new(){Id="91",TableName="scoped_candidate_leaf",IsRongyu=1}
        ]);
        foreach(var id in new[]{"90","91"})
        {
            catalog.ConfigColumns.AddRange([
                new(){Id=id+"01",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=id+"02",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=id+"03",ConfigTableId=id,ColumnName="name",Name="记录名",LogicalType="string",SortNo=-1}
            ]);
            catalog.ConfigPages.Add(new(){Id=id+"0",ConfigTableId=id,Name="原候选页",YzAdminTypeId="5",
                WhereSql="yz_project_id=@current_yz_project_id",IsNew=1,IsSave=1,
                IsImport=id=="90"?1:0,IsExport=id=="90"?1:0});
            catalog.ConfigMenus.Add(new(){Id="800"+id,YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="原候选",Kind="GENERATED_PAGE",ConfigPageId=id+"0"});
        }
        catalog.ConfigColumns.Add(new(){Id="9104",ConfigTableId="91",ColumnName="root_id",LogicalType="int64",LinkConfigTableId=inner.Id});
        catalog.ConfigColumns.Add(new(){Id="9004",ConfigTableId="90",ColumnName="outer_leaf_id",Name="跨表候选",
            LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="91",
            ControlDetail=JsonSerializer.Serialize(new
            {
                sourceMode="crossTable",
                levels=new object[]{
                    new{table=inner.TableName,columnPrefix="outer_root",nameField="name"},
                    new{table="scoped_candidate_leaf",columnPrefix="outer_leaf",nameField="name",parentField="root_id"}
                }
            })});
        foreach(var page in catalog.ConfigPages.Where(item=>item.ConfigTableId is "90" or "91"))
            foreach(var column in catalog.ConfigColumns.Where(item=>item.ConfigTableId==page.ConfigTableId&&item.ColumnName!="yz_project_id"))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsEdit=1,IsShow=1});
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns)
            .Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns).Where(record=>record.YzProjectId.Length==0))
            record.YzProjectId=catalog.YzProjectId;
        return catalog;
    }
}
