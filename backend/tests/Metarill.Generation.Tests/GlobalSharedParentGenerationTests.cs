using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局共享R0的五种来源父范围由同一目录证明，不借用未经验证的独立全局父表。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalSharedParentGenerationTests
{
    /// <summary>全部有效位置与四键、短控件、有无项目列共176目录，逐页校验逻辑与混合物理父字段。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void Global_shared_parent_catalogs_prove_all_five_parent_pages(string type,string parent,bool shorter)
    {
        for(var mask=1;mask<=15;mask++){if(shorter&&(mask&4)!=0)continue;foreach(var noProject in new[]{false,true})
        {
            var c=Catalog("global_parent",type,parent,shorter,mask,noProject);var before=JsonSerializer.Serialize(c);var m=GenerationModel.Create(c,"Example.GlobalSharedParent");var t=m.Tables.Single(table=>table.Source.Id=="2");foreach(var page in t.Pages){var group=SharedCrossTableCompilation.ForPage(m,t,page);Assert.Empty(group.Gaps);Assert.Equal(2,Assert.Single(group.Groups).Plans.Count);if(page.Source.Id=="201")continue;Assert.True(NonRedundantCrossParentCompilation.Enabled(m,t,page));Assert.NotEmpty(NonRedundantSelfTreeSubset.ParentTables(m,t,page));}
            Assert.Empty(GenerationCapabilityAudit.Collect(m));Assert.Equal(before,JsonSerializer.Serialize(c));
        }}
    }
    /// <summary>四种实际完整包保留原主子页、文件控件、partial及原parentId传输与共享清理。</summary>
    [Theory][InlineData("string","string",false,1,false,false)][InlineData("string","int64",true,2,true,true)][InlineData("int64","string",false,8,true,false)][InlineData("int64","int64",false,15,true,true)]
    public async Task Full_global_parent_packages_build_with_original_vue(string type,string parent,bool shorter,int mask,bool noProject,bool physicalDelete)
    {
        var c=Catalog("shared_parent",type,parent,shorter,mask,noProject,true,physicalDelete);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-shared-parent");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalParentFull",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 全局共享主子父范围手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalParentFull",root);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,operations);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);foreach(var name in new[]{"yzp-cascade-tree","yzp-file-library"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/"+name+"/"+name+".vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/"+name+"/"+name+".vue")));
            foreach(var script in new[]{"SharedCrossParentVueProbe.mjs","SharedCrossFilesVueProbe.mjs"}){var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),script));probe.ArgumentList.Add(Path.Combine(output,"frontend"));if(script=="SharedCrossParentVueProbe.mjs")probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);}
        }finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>全局共享父页的显示分隔符和附件范围使用同一份已证明层级。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Global_file_parent_allows_page_specific_display_joiner(bool redundant)
    {
        var catalog = redundant
            ? SharedRedundantParentGenerationTests.Catalog("shared_parent", "string", "int64", false, 1, true, true)
            : Catalog("shared_parent", "string", "int64", false, 1, true, true);
        var control = catalog.ConfigColumns.Single(column => column.Id == "80");
        var field = catalog.ConfigPageColumns.Single(column => column.ConfigPageId == "202" && column.ConfigColumnId == control.Id);
        var detail = JsonNode.Parse(control.ControlDetail)!.AsObject();
        detail["displayJoiner"] = " > ";
        field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(detail.ToJsonString());

        var model = GenerationModel.Create(catalog, "Example.GlobalFileParent");
        var table = model.Tables.Single(item => item.Source.Id == "2");
        var page = table.Pages.Single(item => item.Source.Id == "202");
        var source = table.Columns.Single(item => item.Source.Id == control.Id);
        Assert.Equal(" > ", CrossTableReadCompilation.Resolve(model, table, page, source).Definition!.Joiner);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
    }

    /// <summary>同一已发布的全局R0名称层可作业务及附件的独立或共享逻辑父，实际父键仍由稀疏路径取得。</summary>
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,true,false)]
    [InlineData(false,true,true)]
    [InlineData(true,false,true)]
    [InlineData(true,false,false,true)]
    [InlineData(true,true,false,true)]
    [InlineData(true,false,false,true,true)]
    [InlineData(true,true,false,true,true)]
    [InlineData(true,false,true,true)]
    [InlineData(true,true,true,true,true)]
    public async Task Published_global_derived_parent_compiles_business_and_file_scopes(bool shared,bool rowProject,bool accountScoped,
        bool alternateDictionary=false,bool redundantSource=false)
    {
        const string packageNamespace="Example.GlobalDerivedParent";
        var catalog=Catalog("shared_parent","string","int64",false,1,!rowProject,true);
        if(redundantSource)catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        target.IsRongyu=0;
        var countryPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        if(accountScoped)
        {
            countryPage.WhereSql+=" AND store_id=@current_store_id";
            catalog.ConfigColumns.Add(new(){Id="99017",ConfigTableId="3",YzProjectId=catalog.YzProjectId,ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql+=" AND country_name <> '停用'";
            if(redundantSource)
            {
                // 物理名称副本的门店归属来自持久行，与当前账号过滤使用同一字段。
                catalog.ConfigColumns.Add(new(){Id="99018",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                    ColumnName="store_id",LogicalType="int64"});
                foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))
                    sourcePage.WhereSql+=" AND store_id=@current_store_id";
            }
            if(!alternateDictionary)
            {
                catalog.ConfigPages.RemoveAll(page=>page.Id=="206");
                catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="206"||field.ConfigColumnId=="91");
                catalog.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="206");
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="91");
            }
        }
        var name=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="3"&&column.ColumnName=="name");
        name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        var company=new TableMetadata{Id="99",YzProjectId=catalog.YzProjectId,TableName="shared_parent_company",IsRongyu=1};
        company.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigTables.Add(company);
        catalog.ConfigPages.Add(new(){Id="9901",ConfigTableId=company.Id,YzAdminTypeId=countryPage.YzAdminTypeId,YzProjectId=catalog.YzProjectId});
        catalog.ConfigColumns.AddRange([
            new(){Id="99011",ConfigTableId=company.Id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="99012",ConfigTableId=company.Id,ColumnName="name",LogicalType="string"},
            new(){Id="99013",ConfigTableId="3",ColumnName="company_id",LogicalType="int64",ControlKind="List",LinkConfigTableId=company.Id,ControlDetail="{\"pageId\":\"9901\"}"},
            new(){Id="99014",ConfigTableId="3",ColumnName="company_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}}]);
        catalog.ConfigPageColumns.AddRange([
            new(){Id="99015",ConfigPageId="9901",ConfigColumnId="99012",IsShow=1},
            new(){Id="99016",ConfigPageId="301",ConfigColumnId="99013",IsEdit=1}]);
        if(!shared)
        {
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "80" or "91");
            catalog.ConfigPages.RemoveAll(page=>page.Id is "205" or "206");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId is "205" or "206"||field.ConfigColumnId is "80" or "91");
            catalog.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId is "205" or "206");
        }
        if(alternateDictionary)
        {
            // 共享父所在目标页使用备用字典，另一业务页沿用表级字典声明。
            company.IsRongyu=rowProject?0:1;
            catalog.ConfigColumns.Add(new(){Id="99020",ConfigTableId=company.Id,YzProjectId=catalog.YzProjectId,
                ColumnName="segment",LogicalType="int32"});
            catalog.ConfigPages.Single(page=>page.Id=="9901").WhereSql="segment=17";
            catalog.ConfigPages.Add(new(){Id="9902",ConfigTableId=company.Id,YzAdminTypeId=countryPage.YzAdminTypeId,
                YzProjectId=catalog.YzProjectId,WhereSql="segment=7"});
            catalog.ConfigPageColumns.Add(new(){Id="990299012",ConfigPageId="9902",ConfigColumnId="99012",IsShow=1});
            catalog.ConfigPageColumns.Single(field=>field.Id=="99016").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("""{"pageId":"9902"}""");
            catalog.ConfigPages.Add(new(){Id="302",ConfigTableId=target.Id,YzProjectId=catalog.YzProjectId,
                YzAdminTypeId=countryPage.YzAdminTypeId});
            catalog.ConfigPageColumns.Add(new(){Id="30299013",ConfigPageId="302",ConfigColumnId="99013",IsShow=1,IsEdit=1});
        }
        var model=GenerationModel.Create(catalog,packageNamespace);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single(item=>item.Source.Id=="202");
        var parentColumn=page.Scopes.Single(scope=>scope.Source=="parent").Column;
        var actor=new RuntimeActor(9,"父级账号范围",false,catalog.YzProjectId,countryPage.YzAdminTypeId)
            {StoreId=9007199254740993L};
        var missingStore=actor with{StoreId=null,IsAdministrator=true};
        void VerifyAccount(FixedConditionSqlPlan? condition)
        {
            var scoped=Assert.IsType<FixedConditionSqlPlan>(condition);
            Assert.Contains("current_store_id",scoped.Variables);
            Assert.Throws<UnauthorizedAccessException>(()=>scoped.BindAccount(missingStore));
            Assert.Empty(scoped.BindAccount(actor).Variables);
        }
        if(redundantSource)
        {
            var parent=Assert.Single(SharedRedundantParentCompilation.ForColumn(model,source,page,parentColumn));
            Assert.Equal("301",parent.Level.Page.Source.Id);
            Assert.True(parent.Level.DerivedName);
        }
        else
        {
            var parent=Assert.Single(SharedGlobalParentCompilation.ForColumn(model,source,page,parentColumn));
            Assert.Equal("301",parent.Level.Page.Source.Id);
            Assert.True(parent.Level.DerivedName);
            Assert.Equal(shared,parent.Shared);
        }
        if(alternateDictionary)
        {
            var mixed=source.Pages.Single(item=>item.Source.Id=="206");
            Assert.Empty(SharedCrossTableCompilation.ForPage(model,source,mixed).Gaps.Values);
            var physical=mixed.Scopes.Single(scope=>scope.Column.Source.ColumnName=="owner_country_id").Column;
            var level=redundantSource?Assert.Single(SharedRedundantParentCompilation.ForColumn(model,source,mixed,physical)).Level:
                Assert.Single(SharedGlobalParentCompilation.ForColumn(model,source,mixed,physical)).Level;
            Assert.Equal("301",level.Page.Source.Id);
            Assert.Equal(name.Id,level.Name.Source.Id);
            // 从文件运行时冻结目录取第 206 页的实际计划，目录建立成功并不代表该页已发布。
            var files=new Metarill.Runtime.Files.FileMetadataContext([catalog]);
            var snapshot=Assert.Single(files.Catalogs);
            var filePage=snapshot.ConfigPages.Single(item=>item.Id==mixed.Source.Id);
            const System.Reflection.BindingFlags nonPublic=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var scope=typeof(Metarill.Runtime.Files.FileMetadataContext).GetMethod("PageScope",nonPublic)!
                .Invoke(files,[snapshot,filePage])!;
            var global=Assert.IsAssignableFrom<IReadOnlyList<ColumnMetadata>>(scope.GetType().GetProperty("GlobalParents",nonPublic)!.GetValue(scope));
            var crosses=Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                scope.GetType().GetProperty("Crosses",nonPublic)!.GetValue(scope));
            if(redundantSource)
            {
                Assert.Empty(global);
                var parentScope=Assert.Single(crosses.Cast<object>(),path=>
                    Equals("r1:"+physical.Source.Id,path.GetType().GetProperty("SourceId",nonPublic)!.GetValue(path)));
                if(accountScoped)
                {
                    var physicalPlan=Assert.IsType<RedundantSharedParentPlan>(
                        parentScope.GetType().GetField("redundant",nonPublic)!.GetValue(parentScope));
                    VerifyAccount(Assert.Single(physicalPlan.Levels).Condition);
                }

                // 允许固定目标页内部使用备用字典，来源页之间仍不能替换这个外键的固定目标页。
                Assert.DoesNotContain(catalog.ConfigColumns,column=>column.ConfigTableId==source.Source.Id&&column.ColumnName=="owner_country_name");
                var incompatible=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog))!;
                incompatible.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==physical.Source.Id)
                    .Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"pageId":"302"}""");
                var fixedOwner=incompatible.ConfigTables.Single(table=>table.Id==source.Source.Id);
                var fixedParent=incompatible.ConfigTables.Single(table=>table.Id==target.Id);
                var parentKey=incompatible.ConfigColumns.Single(column=>column.Id==physical.Source.Id);
                var rejected=Assert.Throws<InvalidOperationException>(()=>
                    GlobalSingleRelationTarget.RequireCached(incompatible,fixedOwner,parentKey,fixedParent));
                Assert.Contains("物理名称副本必须使用同一目标页",rejected.Message);
            }
            else
            {
                Assert.Equal(physical.Source.Id,Assert.Single(global).Id);
                if(accountScoped)
                {
                    var fileLevels=crosses.Cast<object>().SelectMany(path=>
                        Assert.IsAssignableFrom<IReadOnlyList<NonRedundantCrossTableScope>>(
                            path.GetType().GetProperty("ReadBindings",nonPublic)!.GetValue(path)))
                        .SelectMany(binding=>binding.Levels).Where(target=>target.PageId=="301").ToArray();
                    Assert.NotEmpty(fileLevels);
                    Assert.All(fileLevels,level=>VerifyAccount(level.Condition));
                }
            }
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-derived-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),packageNamespace);
            var generated=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=generated.GetType(packageNamespace+".Generated.Services.SharedParentSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Update202Async"));
            var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/SharedParentSourceService.g.cs"));
            if(redundantSource)
            {
                Assert.NotNull(service.GetMethod("Update206Async"));
                Assert.Contains("VerifySharedR1Parent206Async",code);
            }
            else
            {
                Assert.Contains("LogicalParentScope202",code);
                Assert.Contains("VerifyLogicalParent202Async",code);
            }
            if(accountScoped&&!redundantSource)
            {
                Assert.Contains("LogicalParentPlan202.BindAccount(actor)",code);
                Assert.Contains("CrossConditionPlan202.BindAccount(actor)",code);
            }
            if(accountScoped&&alternateDictionary)
            {
                const System.Reflection.BindingFlags staticPlan=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
                if(redundantSource)
                {
                    var accountParents=Assert.IsType<RedundantSharedParentPlan[]>(
                        service.GetField("AccountParentScope206",staticPlan)!.GetValue(null));
                    Assert.Equal(2,accountParents.Length);
                    Assert.All(accountParents,parent=>VerifyAccount(Assert.Single(parent.Levels).Condition));
                }
                else
                {
                    Assert.Contains("LogicalParentPlan206.BindAccount(actor)",code);
                    var scope=Assert.IsType<NonRedundantReadPlan>(
                        service.GetField("LogicalParentPlan206",staticPlan)!.GetValue(null));
                    Assert.True(scope.RequiresAccount);
                    Assert.Throws<UnauthorizedAccessException>(()=>scope.BindAccount(missingStore));
                    Assert.False(scope.BindAccount(actor).RequiresAccount);
                }
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>未通过证明的独立全局父、动态目标、坏同伴、错页、物理父覆盖与未知精确字段继续拒绝。</summary>
    [Theory][InlineData("single")][InlineData("globalR0")][InlineData("variable")][InlineData("unpublished")][InlineData("badPeer")][InlineData("physicalPage")][InlineData("physicalOverlay")][InlineData("unknownExact")]
    public void Unproven_global_parent_never_publishes_complete_package(string kind)
    {
        var c=Catalog("global_parent","string","int64",false,1,true);if(kind=="single"){c.ConfigColumns.RemoveAll(column=>column.Id=="80");c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="80");c.ConfigPages.RemoveAll(page=>page.Id=="205");c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="205");c.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="205");MetadataValidation.Validate(c);_=GenerationModel.Create(c,"Example.ValidSingleGlobalParent");}if(kind=="globalR0"){c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;c.ConfigColumns.Single(column=>column.ConfigTableId=="3"&&column.ColumnName=="name").IsVirtualColumn=1;}if(kind=="variable"){c.ConfigColumns.Add(new(){Id="399",ConfigTableId="3",YzProjectId=c.YzProjectId,ColumnName="yz_project_id",LogicalType="int64"});c.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id";}if(kind=="unpublished")c.ConfigTables.Single(table=>table.Id=="3").YzProjectId="";if(kind=="badPeer")c.ConfigColumns.Single(column=>column.Id=="80").ControlDetail="{}";if(kind=="physicalPage")c.ConfigColumns.Single(column=>column.Id=="91").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("401");if(kind=="physicalOverlay")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="206"&&field.ConfigColumnId=="91").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="unknownExact")c.ConfigPages.Single(page=>page.Id=="202").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("missing_scope");var output=Path.Combine(Path.GetTempPath(),"metarill-global-parent-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.Reject",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>全部页面与字段都来源于原元数据工厂，只增加已发布全局声明。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter,int mask,bool noProject,bool files=false,bool physicalDelete=false)
    {
        var c=files?SharedCrossFilesGenerationTests.Catalog(prefix,type,parent,shorter,false,physicalDelete):SharedCrossParentGenerationTests.Catalog(prefix,type,parent,shorter);GlobalSharedCrossGenerationTests.ApplyGlobals(c,mask,noProject);return c;
    }
}
