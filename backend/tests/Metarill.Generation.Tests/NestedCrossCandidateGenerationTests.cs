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
    [InlineData(false,"ordinary",true)]
    [InlineData(true,"without-project",true)]
    [InlineData(false,"cached-ordinary",true)]
    [InlineData(true,"cached-global",true)]
    public async Task Logical_target_page_keeps_nested_scope_after_metadata_publication(bool shared,string mode,bool selfTreeScope=false)
    {
        const string ns="Example.NestedCrossScope";
        var catalog=Catalog(shared,mode,selfTreeScope);
        var before=JsonSerializer.Serialize(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="90");
        var page=Assert.Single(table.Pages);
        var control=table.Columns.Single(column=>column.Source.Id=="9004");
        var proof=CrossTableWriteCompilation.Resolve(model,table,page,control);
        Assert.True(proof.Definition is not null,proof.Reason);
        var plans=CrossTableWriteCompilation.ForService(model,table,page);
        var plan=Assert.Single(plans,item=>item.Read.Source.Source.Id=="9004");
        if(mode=="cached-global"||shared&&selfTreeScope)Assert.Single(SharedCrossTableCompilation.ForPage(model,table,page).Groups);
        else Assert.Single(plans);
        Assert.True(selfTreeScope?FixedPageConditionCompilation.HasSelfTreeFields(plan.Read.Levels[0].Page):
            FixedPageConditionCompilation.HasCrossFields(plan.Read.Levels[0].Page));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var cached=mode.StartsWith("cached-",StringComparison.Ordinal);
        if(cached)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("outer_root_name",maintenance.MixedSourceColumns[table.Source.TableName]);
            Assert.Contains(maintenance.MixedTargets,target=>target.Contains(selfTreeScope?"visibility":"country",StringComparison.Ordinal));
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
            if(selfTreeScope&&!cached)
            {
                var read=Assert.IsType<NonRedundantReadPlan>(type.GetField("ReadPlan900",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(read)!;
                var sql=Assert.IsType<string>(mysql.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(mysql,["outer_root_name"]));
                Assert.Contains("WITH RECURSIVE",sql);
                Assert.Contains(".`visibility_id`",sql);
                Assert.Contains(".`store_id`=@",sql);
                Assert.DoesNotContain("不可选路径",sql);
                var actor=new Metarill.Core.Security.RuntimeActor(12,"候选测试",false,catalog.YzProjectId,"5");
                Assert.Throws<UnauthorizedAccessException>(()=>read.BindAccount(actor));
                read.BindAccount(actor with{StoreId=9007199254740993L});
                Assert.Contains("LogicalParentScope900",service);
                Assert.Contains("Export900Async",service);
                var registry=new Metarill.Runtime.Files.FileMetadataContext([catalog]);
                var frozen=registry.Catalogs.Single();
                var fileScope=registry.GetType().GetMethod("PageScope",BindingFlags.Instance|BindingFlags.NonPublic)!
                    .Invoke(registry,[frozen,frozen.ConfigPages.Single(item=>item.Id=="900")]);
                Assert.NotNull(fileScope);
            }
            if(cached)
            {
                Assert.Contains("RecursiveCandidate=global::",service);
                Assert.Contains("RedundantSharedParentScopeSql.Apply(query",service);
                Assert.Contains("VerifySharedR1Parent900Async",service);
                Assert.Single(SharedRedundantParentCompilation.AccountPlans(model,table,page));
                var maintenance=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var published=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(maintenance.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("outer_root_name",published.MixedSourceColumns[table.Source.TableName]);
                Assert.Contains(published.MixedTargets,target=>target.Contains(selfTreeScope?"visibility":"country",StringComparison.Ordinal));
                var entity=assembly.GetType(ns+".Generated.Entities.ScopedCandidateOuter",true)!;
                using var database=new Metarill.Data.DatabaseSession(new(){ExpectedDatabase="offline",
                    ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
                var mapper=Activator.CreateInstance(assembly.GetType(ns+".Generated.Mappers.ScopedCandidateOuterMapper",true)!,[database]);
                var arguments=new object?[type.GetConstructors().Single().GetParameters().Length];
                arguments[0]=database;arguments[1]=mapper;
                arguments[2]=new Metarill.Core.Security.SnowflakeIdGenerator(864);
                arguments[3]=Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>();
                var instance=Activator.CreateInstance(type,arguments)!;
                var scope=type.GetMethod("Scope900",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var account=new Metarill.Core.Security.RuntimeActor(1,"父范围测试",false,catalog.YzProjectId,"5"){StoreId=9007199254740993L,MerchantId=33};
                var query=scope.Invoke(instance,[account,"9007199254742000",false])!;
                var sql=(string)GetType().GetMethod(nameof(Sql),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(entity).Invoke(null,[query])!;
                Assert.Contains("r1_page_0_0_recursive_mt_candidate_",sql);
                Assert.Contains("rp0",sql);
                Assert.DoesNotContain("不可选路径",sql);
                Assert.IsType<UnauthorizedAccessException>(Assert.Throws<TargetInvocationException>(()=>
                    scope.Invoke(instance,[account with{StoreId=null},"9007199254742000",false])).InnerException);
                var registry=new Metarill.Runtime.Files.FileMetadataContext([catalog]);
                var frozen=registry.Catalogs.Single();
                Assert.NotNull(registry.GetType().GetMethod("PageScope",BindingFlags.Instance|BindingFlags.NonPublic)!
                    .Invoke(registry,[frozen,frozen.ConfigPages.Single(item=>item.Id=="900")]));
            }
            if(mode is "row-project" or "without-project" or "cached-global")
            {
                var actor=new Metarill.Core.Security.RuntimeActor(1,"异项目账号",false,"999","5"){StoreId=9007199254740993L,MerchantId=33};
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

    private static string Sql<T>(FreeSql.ISelect<T> query) where T:class=>query.ToSql();

    private static MetadataCatalog Catalog(bool shared,string mode,bool selfTreeScope)
    {
        var catalog=shared?SharedCrossTableSparseGenerationTests.Catalog("string","int64"):
            NonRedundantCrossFullTests.Catalog("string","int64",false);
        // 无父参数的原目标页保留可见名称，父子页面继续由原范围区分。
        var inner=catalog.ConfigTables.Single(item=>item.Id=="2");
        var innerPage=catalog.ConfigPages.Single(item=>item.Id=="201");
        var logicalName=selfTreeScope?"visibility_name":"country_name";
        innerPage.WhereSql+=$" AND {logicalName} <> '不可选路径' AND store_id=@current_store_id";
        if(mode is "row-project" or "without-project" or "cached-global")
        {
            inner.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            innerPage.WhereSql=$"{logicalName} <> '不可选路径' AND store_id=@current_store_id";
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
            var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="900");
            sourcePage.WhereSql+=" AND store_id=@current_store_id AND "+
                (mode=="cached-global"?"outer_root_id":"outer_leaf_id")+"=@parent_id";
            catalog.ConfigColumns.Add(new(){Id="9010",ConfigTableId="90",ColumnName="attachments",Name="附件",
                LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",
                ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});
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
        if(mode=="cached-global"||selfTreeScope&&shared)
        {
            // 两条分支共享同一个递归候选层，验证生成、路径投影和附件父级共用该范围。
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
        if(selfTreeScope)
        {
            var global=mode is "without-project" or "cached-global";
            var cached=mode.StartsWith("cached-",StringComparison.Ordinal);
            var visibility=new TableMetadata{Id="93",TableName="scoped_candidate_visibility",IsRongyu=1};
            if(global)visibility.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigTables.Add(visibility);
            catalog.ConfigColumns.AddRange([
                new(){Id="9301",ConfigTableId="93",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="9302",ConfigTableId="93",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="9303",ConfigTableId="93",ColumnName="name",LogicalType="string"},
                new(){Id="9304",ConfigTableId="93",ColumnName="parent_id",LogicalType="int64",LinkConfigTableId="93"},
                new(){Id="9305",ConfigTableId="93",ColumnName="status",LogicalType="int32"},
                new(){Id="8999",ConfigTableId=inner.Id,ColumnName="visibility_id",LogicalType="int64",ControlKind="CascadeTree",
                    LinkConfigTableId="93",ControlDetail="""{"sourceMode":"selfTree","selfTree":{"table":"scoped_candidate_visibility","parentField":"parent_id"}}"""}
            ]);
            if(!cached)
            {
                catalog.ConfigColumns.Add(new(){Id="9010",ConfigTableId="90",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",
                    ControlKind="FileLibrary",ControlDetail="""{"accessMode":"PROTECTED","maxCount":3}"""});
                catalog.ConfigPages.Single(page=>page.Id=="900").WhereSql+=" AND outer_root_id=@parent_id";
            }
            else if(!global)
            {
                catalog.ConfigColumns.AddRange([
                    new(){Id="9306",ConfigTableId="93",ColumnName="merchant_id",LogicalType="int64"},
                    new(){Id="9011",ConfigTableId="90",ColumnName="merchant_id",LogicalType="int64"}
                ]);
                catalog.ConfigPages.Single(page=>page.Id=="900").WhereSql+=" AND merchant_id=@current_merchant_id";
            }
            if(!global)catalog.ConfigColumns.Add(new(){Id="8998",ConfigTableId=inner.Id,ColumnName="visibility_name",LogicalType="string",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("visibility.name")}});
            var condition=global?"status=1":"yz_project_id=@current_yz_project_id AND status=1";
            if(cached&&!global)condition+=" AND merchant_id=@current_merchant_id";
            catalog.ConfigPages.Add(new(){Id="930",ConfigTableId="93",YzAdminTypeId="5",WhereSql=condition});
        }
        foreach(var page in catalog.ConfigPages.Where(item=>item.ConfigTableId is "90" or "91" or "92" or "93"))
            foreach(var column in catalog.ConfigColumns.Where(item=>item.ConfigTableId==page.ConfigTableId&&item.ColumnName!="yz_project_id"))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,IsEdit=1,IsShow=1});
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns)
            .Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns).Where(record=>record.YzProjectId.Length==0))
            record.YzProjectId=catalog.YzProjectId;
        return catalog;
    }
}
