using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>非冗余来源参与固定跨表链，逻辑祖先与实际稀疏存储不能混用。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantCrossSourceTests
{
    /// <summary>来源根／中／末级、四种叶子父键与独立派生名称分别生成全部消费者。</summary>
    [Theory]
    [InlineData(0,"string","string",false)]
    [InlineData(0,"string","string",true)]
    [InlineData(0,"string","int64",false)]
    [InlineData(0,"string","int64",true)]
    [InlineData(0,"int64","string",false)]
    [InlineData(0,"int64","string",true)]
    [InlineData(0,"int64","int64",false)]
    [InlineData(0,"int64","int64",true)]
    [InlineData(1,"string","string",false)]
    [InlineData(1,"string","string",true)]
    [InlineData(1,"string","int64",false)]
    [InlineData(1,"string","int64",true)]
    [InlineData(1,"int64","string",false)]
    [InlineData(1,"int64","string",true)]
    [InlineData(1,"int64","int64",false)]
    [InlineData(1,"int64","int64",true)]
    [InlineData(2,"string","string",false)]
    [InlineData(2,"string","string",true)]
    [InlineData(2,"string","int64",false)]
    [InlineData(2,"string","int64",true)]
    [InlineData(2,"int64","string",false)]
    [InlineData(2,"int64","string",true)]
    [InlineData(2,"int64","int64",false)]
    [InlineData(2,"int64","int64",true)]
    public async Task Every_source_level_compiles_physical_and_derived_current_paths(int sourceLevel,string type,string parentType,bool derived)
    {
        var c=Catalog("nr_source_graph",sourceLevel,type,parentType,derived);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.NonRedundantSource");var table=model.Tables.Single(x=>x.Source.Id=="2");var page=Assert.Single(table.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));Assert.Equal("2",plan.Levels[sourceLevel].Read.Target.Source.Id);Assert.Equal(derived,plan.Levels[sourceLevel].Read.DerivedName);if(derived)Assert.Contains("company_id",plan.Levels[sourceLevel].Read.NameDependencies!);Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-source");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>来源兼任候选时，派生名称及写后重验依赖沿该层实际发布页。</summary>
    [Theory]
    [InlineData(0,false)]
    [InlineData(1,true)]
    [InlineData(2,true)]
    public async Task Same_source_names_use_the_actual_dictionary_for_write_dependencies(int sourceLevel,bool parented)
    {
        const string prefix="nr_source_dictionary", ns="Example.NrSourceDictionary";
        var catalog=Catalog(prefix,sourceLevel,"string","int64",true);
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        var targetPage=sourcePage;
        if(parented)
        {
            targetPage=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(sourcePage))!;
            targetPage.Id="202";
            catalog.ConfigPages.Add(targetPage);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"&&field.ConfigColumnId!="25").ToArray())
            {
                var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                copy.Id="202_"+field.ConfigColumnId;
                copy.ConfigPageId=targetPage.Id;
                catalog.ConfigPageColumns.Add(copy);
            }
            sourcePage.WhereSql+=" AND country_id=@parent_id";
        }
        var dictionary=catalog.ConfigTables.Single(table=>table.Id=="6");
        dictionary.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="63");
        catalog.ConfigColumns.AddRange([
            new(){Id="65",ConfigTableId="6",ColumnName="status",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="66",ConfigTableId="6",ColumnName="segment",LogicalType="int32",YzProjectId=catalog.YzProjectId},
            new(){Id="67",ConfigTableId="6",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId},
            new(){Id="93",ConfigTableId="2",ColumnName="company_group",LogicalType="int32",YzProjectId=catalog.YzProjectId}
        ]);
        catalog.ConfigPages.Single(page=>page.Id=="601").WhereSql="status=17";
        catalog.ConfigPages.Add(new(){Id="602",ConfigTableId="6",YzProjectId=catalog.YzProjectId,
            YzAdminTypeId=sourcePage.YzAdminTypeId,WhereSql="status=3 AND store_id=@current_store_id"});
        catalog.ConfigPageColumns.Add(new(){Id="60262",ConfigPageId="602",ConfigColumnId="62",YzProjectId=catalog.YzProjectId,IsShow=1});
        catalog.ConfigPageColumns.Add(new(){Id=targetPage.Id+"93",ConfigPageId=targetPage.Id,ConfigColumnId="93",
            YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1});
        var company=catalog.ConfigColumns.Single(column=>column.Id=="91");
        company.ControlKind="List";
        company.ControlDetail="""{"tableId":"6","pageId":"601","nameField":"name"}""";
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==targetPage.Id&&field.ConfigColumnId=="91")
            .Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"602","whereSql":"segment=@formParameters.companyGroup"}""");

        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var generatedPage=source.Pages.Single(page=>page.Source.Id==sourcePage.Id);
        var control=source.Columns.Single(column=>column.Source.Id=="25");
        var resolved=CrossTableReadCompilation.Resolve(model,source,generatedPage,control);
        Assert.True(resolved.Definition is not null,resolved.Reason);
        var role=resolved.Definition!.Levels[sourceLevel];
        Assert.Equal(targetPage.Id,role.Page.Source.Id);
        Assert.Contains("company_id",role.NameDependencies!);
        Assert.Contains("company_group",role.NameDependencies!);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-same-source-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,ns);
            var code=await File.ReadAllTextAsync(Path.Combine(output,"Services/NrSourceDictionarySourceService.g.cs"));
            var verify=code.IndexOf("private async Task VerifyStoredCross201_25Async",StringComparison.Ordinal);
            Assert.True(verify>=0);
            var beforeRead=code[verify..code.IndexOf("var stored",verify,StringComparison.Ordinal)];
            Assert.Contains("submitted.Contains(\"company_id\")",beforeRead);
            Assert.Contains("submitted.Contains(\"company_group\")",beforeRead);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.NrSourceDictionarySourceService",true)!;
            const System.Reflection.BindingFlags fields=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                service.GetField($"CrossTargetPlan201_25_{sourceLevel}",fields)!.GetValue(null));
            var actor=new Metarill.Core.Security.RuntimeActor(8,"字典名称",false,catalog.YzProjectId,sourcePage.YzAdminTypeId);
            Assert.Throws<UnauthorizedAccessException>(()=>plan.BindAccount(actor));
            var bound=plan.BindAccount(actor with{StoreId=9007199254740993L});
            const System.Reflection.BindingFlags instance=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var sql=bound.GetType().GetProperty("MySql",instance)!.GetValue(bound)!;
            var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(sql.GetType().GetProperty("Constants",instance)!.GetValue(sql));
            Assert.Contains(9007199254740993L,constants.Values);
            Assert.Contains(3,constants.Values);
            Assert.DoesNotContain(17,constants.Values);
            if(!parented)
            {
                // 使用真实生成的写后方法执行一次事务，独立模拟已持久化的字段值。
                using var runtime=new SharedExcelReadTransport(assembly,ns,"NrSourceDictionarySource",actor:actor);
                runtime.Add("NrSourceDictionarySource",new Dictionary<string,object?>
                {
                    ["Id"]=11L,["YzProjectId"]=long.Parse(actor.ProjectId,System.Globalization.CultureInfo.InvariantCulture),
                    ["CompanyGroup"]=7
                });
                var matching=new Dictionary<string,object?>{["CompanyGroup"]=7};
                var different=new Dictionary<string,object?>{["CompanyGroup"]=3};
                await runtime.VerifyStoredCross("25",11,matching,["company_group"]);
                var mismatch=await Assert.ThrowsAsync<InvalidOperationException>(()=>
                    runtime.VerifyStoredCross("25",11,different,["company_group"]));
                Assert.Contains("派生名称依赖字段",mismatch.Message);
                await runtime.VerifyStoredCross("25",11,different,["company_id"]);
                Assert.Equal(1,runtime.Transport.Rollbacks);
                Assert.Equal(2,runtime.Transport.Commits);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>实际锁测试失败时保留生成源码，名称外键必须进入写后触发条件。</summary>
    [Fact]
    public async Task Only_name_fk_change_keeps_generated_current_path_validation()
    {
        var c=Catalog("nr_source_diag",1,"string","string",true);var model=GenerationModel.Create(c,"Example.NrSourceDiagnostic");var table=model.Tables.Single(x=>x.Source.Id=="2");var page=Assert.Single(table.Pages);var plan=Assert.Single(CrossTableWriteCompilation.ForService(model,table,page));Assert.Contains("company_id",plan.Levels[1].Read.NameDependencies!);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-source-diag");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,directory,model.Namespace);var code=await File.ReadAllTextAsync(Path.Combine(directory,"Services/NrSourceDiagSourceService.g.cs"));var root=NonRedundantApplicationFixture.Root();var evidence=Path.Combine(root,".local/nonredundant-cross-source");Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"derived-service-proof.txt"),code);var start=code.IndexOf("private async Task VerifyStoredCross201_25Async",StringComparison.Ordinal);Assert.True(start>=0);Assert.Contains("submitted.Contains(\"company_id\")",code[start..code.IndexOf("var stored",start,StringComparison.Ordinal)]);}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>来源三个层位的物理／派生名称Full实际构建，原Vue及手写partial随原更新动作保留。</summary>
    [Theory][InlineData(0,false)][InlineData(1,false)][InlineData(2,false)][InlineData(0,true)][InlineData(1,true)][InlineData(2,true)]
    public async Task Full_source_graph_keeps_original_components_and_extensions(int sourceLevel,bool derived)
    {
        var c=Catalog("nr_source_full",sourceLevel,"string","int64",derived);var boundary=Path.Combine(Path.GetTempPath(),"metarill-nr-cross-source-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var root=NonRedundantApplicationFixture.Root();
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.NrSourceFull",root).FrontendIncluded);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            using(var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var cap=Assert.Single(json.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),x=>x.TryGetProperty("sourceMode",out var mode)&&mode.GetString()=="crossTable");Assert.True(cap.GetProperty("sameSourceTableSupported").GetBoolean());Assert.Equal(derived,cap.GetProperty("derivedNameSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/NrSourceFullSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 非冗余来源图的手写扩展保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var next=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(c))!;next.ConfigColumns.Single(x=>x.Id=="22").Name="来源业务标识";var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(next,candidate,"Example.NrSourceFull",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"NonRedundantCrossSourceVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(derived?"true":"false");probe.ArgumentList.Add(sourceLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));await NonRedundantApplicationFixture.RunAsync(probe);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>来源角色的稀疏父位、逻辑范围、全局、隐藏名称及未确认Excel用途继续明确拒绝。</summary>
    [Theory][InlineData("repeatSparseParent")][InlineData("global")][InlineData("sourceCondition")][InlineData("derivedParent")][InlineData("hiddenName")][InlineData("extra")][InlineData("pathImport")][InlineData("view")]
    public void Unproven_source_graph_uses_stay_gated_before_staging(string kind)
    {
        var c=Catalog("nr_source_bad",1,"string","int64",true);var source=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(source.ControlDetail)!;
        if(kind=="repeatSparseParent"){json["levels"]![0]!["table"]="nr_source_bad_source";json["levels"]![1]!["parentField"]="country_id";c.ConfigColumns.Single(x=>x.Id=="88").LinkConfigTableId="2";}
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="sourceCondition")c.ConfigPages.Single(x=>x.Id=="201").WhereSql+=" AND country_id='9007199254745001'";
        if(kind=="derivedParent"){var parent=c.ConfigColumns.Single(x=>x.Id=="88");parent.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);parent.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="hiddenName"){var name=c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="92");name.IsShow=0;name.IsEdit=0;}
        if(kind=="extra")json["levels"]![1]!["whereSql"]="status=1";
        if(kind=="pathImport"){var page=c.ConfigPages.Single(x=>x.Id=="201");page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("unknown");}
        if(kind=="view")c.ConfigTables.Single(x=>x.Id=="2").IsView=1;
        source.ControlDetail=json.ToJsonString();var output=Path.Combine(Path.GetTempPath(),"metarill-nr-source-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.NrSourceReject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>只改存储与树显示定义，业务标识name和派生display_label保持独立。</summary>
    internal static MetadataCatalog Catalog(string prefix,int sourceLevel,string type,string parentType,bool derived)
    {
        var c=CrossTableSourceGraphTests.Catalog(prefix,sourceLevel,type,parentType);c.ConfigTables.Single(x=>x.Id=="2").IsRongyu=0;var field=c.ConfigPageColumns.Single(x=>x.ConfigPageId=="201"&&x.ConfigColumnId=="25");field.IsShow=0;field.Extra["isDetailShow"]=JsonSerializer.SerializeToElement(1);field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(derived)
        {
            c.ConfigTables.Add(new(){Id="6",TableName=prefix+"_company",IsRongyu=1});
            c.ConfigColumns.AddRange([new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"},new(){Id="91",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="6"},new(){Id="92",ConfigTableId="2",ColumnName="display_label",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}}]);
            c.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.AddRange([new(){Id="60162",ConfigPageId="601",ConfigColumnId="62"},new(){Id="20191",ConfigPageId="201",ConfigColumnId="91"},new(){Id="20192",ConfigPageId="201",ConfigColumnId="92",IsEdit=0}]);var control=c.ConfigColumns.Single(x=>x.Id=="25");var json=JsonNode.Parse(control.ControlDetail)!;json["levels"]![sourceLevel]!["nameField"]="display_label";control.ControlDetail=json.ToJsonString();
        }
        return RedundantMultiNameCacheTests.Published(c);
    }
}
