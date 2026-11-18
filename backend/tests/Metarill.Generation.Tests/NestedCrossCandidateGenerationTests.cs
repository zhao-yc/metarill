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
    [InlineData(false,"ordinary")]
    [InlineData(true,"ordinary")]
    [InlineData(false,"row-project")]
    [InlineData(true,"without-project")]
    [InlineData(false,"cached-ordinary")]
    [InlineData(true,"cached-global")]
    public async Task Logical_target_page_keeps_nested_scope_after_metadata_publication(bool shared,string mode)
    {
        const string ns="Example.NestedCrossScope";
        var catalog=Catalog(shared,mode);
        var before=JsonSerializer.Serialize(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="90");
        var page=Assert.Single(table.Pages);
        var plans=CrossTableWriteCompilation.ForService(model,table,page);
        var plan=Assert.Single(plans,item=>item.Read.Source.Source.Id=="9004");
        if(mode=="cached-global")Assert.Single(SharedCrossTableCompilation.ForPage(model,table,page).Groups);
        else Assert.Single(plans);
        Assert.True(FixedPageConditionCompilation.HasCrossFields(plan.Read.Levels[0].Page));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var cached=mode.StartsWith("cached-",StringComparison.Ordinal);
        if(cached)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("outer_root_name",maintenance.MixedSourceColumns[table.Source.TableName]);
            Assert.Contains(maintenance.MixedTargets,target=>target.Contains("country",StringComparison.Ordinal));
        }

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
            if(mode=="row-project")
            {
                var inner=model.Tables.Single(item=>item.Source.Id=="2");
                var candidateType=assembly.GetType(ns+".Generated.Services."+inner.Name+"Service",true)!;
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(candidateType.TypeHandle);
                Assert.IsType<NonRedundantReadPlan>(candidateType.GetField("CandidateReadPlan201",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                Assert.Null(candidateType.GetField("CandidateReadPlan202",BindingFlags.Static|BindingFlags.NonPublic));
            }
            var targetPlan=(NonRedundantReadPlan)type.GetField("CrossTargetPlan900_9004_0",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            Assert.Equal(new[]{"id","name"},targetPlan.OutputTypes.Keys);
            if(cached)
            {
                var maintenance=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var published=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(maintenance.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("outer_root_name",published.MixedSourceColumns[table.Source.TableName]);
                Assert.Contains(published.MixedTargets,target=>target.Contains("country",StringComparison.Ordinal));
            }
            if(mode is "row-project" or "without-project" or "cached-global")
            {
                var actor=new Metarill.Core.Security.RuntimeActor(1,"异项目账号",false,"999","5"){StoreId=9007199254740993L};
                using var session=new Metarill.Data.DatabaseSession(new(){ExpectedDatabase="offline",
                    ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
                await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new NonRedundantReader(session)
                    .RowsAsync(targetPlan.BindAccount(actor),[],[],1,projectId:actor.ProjectId));
            }
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

    private static MetadataCatalog Catalog(bool shared,string mode)
    {
        var catalog=shared?SharedCrossTableSparseGenerationTests.Catalog("string","int64"):
            NonRedundantCrossFullTests.Catalog("string","int64",false);
        // 无父参数的原目标页保留可见名称，父子页面继续由原范围区分。
        var inner=catalog.ConfigTables.Single(item=>item.Id=="2");
        var innerPage=catalog.ConfigPages.Single(item=>item.Id=="201");
        innerPage.WhereSql+=" AND country_name <> '不可选路径' AND store_id=@current_store_id";
        if(mode is "row-project" or "without-project" or "cached-global")
        {
            inner.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            innerPage.WhereSql="country_name <> '不可选路径' AND store_id=@current_store_id";
        }
        if(mode=="row-project")
        {
            // 复制的父级业务页仍按项目读取，仅原目标页发布全局候选。
            Assert.Contains(innerPage.Extra.GetValueOrDefault("type").ToString(),new[]{"1","2"});
            foreach(var sibling in catalog.ConfigPages.Where(page=>page.ConfigTableId==inner.Id&&page.Id!=innerPage.Id))
                sibling.Extra.Remove("type");
        }
        if(mode is "without-project" or "cached-global")
        {
            var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId==inner.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==project.Id);
        }
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
        if(mode.StartsWith("cached-",StringComparison.Ordinal))
        {
            catalog.ConfigTables.Single(table=>table.Id=="90").IsRongyu=1;
            catalog.ConfigColumns.Add(new(){Id="9009",ConfigTableId="90",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigPages.Single(page=>page.Id=="900").WhereSql+=" AND store_id=@current_store_id";
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
        if(mode=="cached-global")
        {
            // 两条 R1 分支共享同一个递归候选层，只发布一份公共名称副本。
            catalog.ConfigTables.Add(new(){Id="92",TableName="scoped_candidate_alternate",IsRongyu=1});
            foreach(var column in catalog.ConfigColumns.Where(item=>item.ConfigTableId=="91").ToArray())
            {
                var copy=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(column))!;
                copy.Id="92"+column.Id[2..];
                copy.ConfigTableId="92";
                catalog.ConfigColumns.Add(copy);
            }
            catalog.ConfigPages.Add(new(){Id="920",ConfigTableId="92",Name="原备用候选页",YzAdminTypeId="5",
                WhereSql="yz_project_id=@current_yz_project_id",IsNew=1,IsSave=1});
            var control=catalog.ConfigColumns.Single(item=>item.Id=="9004");
            catalog.ConfigColumns.Add(new(){Id="9005",ConfigTableId="90",ColumnName="outer_alt_id",Name="备用候选",
                LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="92",
                ControlDetail=control.ControlDetail.Replace("scoped_candidate_leaf","scoped_candidate_alternate",StringComparison.Ordinal)
                    .Replace("outer_leaf","outer_alt",StringComparison.Ordinal)});
        }
        foreach(var page in catalog.ConfigPages.Where(item=>item.ConfigTableId is "90" or "91" or "92"))
            foreach(var column in catalog.ConfigColumns.Where(item=>item.ConfigTableId==page.ConfigTableId&&item.ColumnName!="yz_project_id"))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsEdit=1,IsShow=1});
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns)
            .Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns).Where(record=>record.YzProjectId.Length==0))
            record.YzProjectId=catalog.YzProjectId;
        return catalog;
    }
}
