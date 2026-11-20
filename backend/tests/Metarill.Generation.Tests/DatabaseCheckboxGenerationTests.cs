using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>真实生成并编译通用复选，原下拉使用ID成员筛选，名称从物理缓存或固定联接读取。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DatabaseCheckboxGenerationTests
{
    /// <summary>冗余及显式／自动非冗余名称均生成同一原候选路由，不写手工业务或新弹层。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Generates_checkbox_contracts_and_protects_handwritten_partial(bool nonRedundant,bool automatic)
    {
        var catalog=Catalog(nonRedundant,automatic);var page=catalog.ConfigPages.Single(item=>item.Id=="201");page.IsImport=1;page.IsExport=1;var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-generation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,"Example.Checkbox");
            var source=File.ReadAllText(Path.Combine(output,"Services/RelationSourceService.g.cs"));
            Assert.Contains("CheckboxScope201_25Async",source);Assert.Contains("DatabaseCheckboxValue.Parse",source);Assert.Contains("query.ForUpdate()",source);Assert.DoesNotContain("RequirePagePermission(\"301\", \"list\")",source);
            var controller=File.ReadAllText(Path.Combine(output,"Controllers/RelationSourcePage201Controller.g.cs"));Assert.Contains("findNameIdTarget25",controller);Assert.Contains("RelationOptionAuthorization.Require",controller);
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};start.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/DatabaseCheckboxVueProbe.mjs"));start.ArgumentList.Add(output);
            using(var process=System.Diagnostics.Process.Start(start)!){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);}
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var request=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201CreateRequest");Assert.Null(request.GetProperty("TargetNames"));
            var vo=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201Vo");Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetIds")!.PropertyType);Assert.Equal(typeof(JsonElement?),vo.GetProperty("TargetNames")!.PropertyType);
            if(nonRedundant)
            {
                var service=assembly.GetTypes().Single(type=>type.Name=="RelationSourceService");var filters=(IReadOnlyList<FixedReadFilter>)service.GetMethod("ReadFilters201",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new PageRequest{ParentId="71",Filters=new(){{"targetIds",JsonSerializer.SerializeToElement("9007199254740993")}}},new RuntimeActor(1,"复选合同",false,"54","5")])!;
                Assert.Equal(FixedReadComparison.JsonArrayHasId,Assert.Single(filters,item=>item.Field=="target_ids").Comparison);
                Assert.DoesNotContain("target_names",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="2")).PhysicalColumns.Select(column=>column.ColumnName));
            }
            else Assert.Contains("JsonArrayNameQuery.ApplyJsonArrayHasId",source);
            var hand=Path.Combine(output,"Services/RelationSourceService.cs");File.AppendAllText(hand,"\n// 手写复选规则保留\n");var original=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,output,"Example.Checkbox");Assert.Equal(original,File.ReadAllBytes(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>页级简称沿用原复选选项及名称数组，条件及 Excel 按同一份配置编译。</summary>
    [Theory]
    [InlineData(false,1,true)]
    [InlineData(true,0,false)]
    public async Task Checkbox_page_label_override_reuses_candidate_range_and_name_array(bool automatic,int targetStorage,bool explicitPage)
    {
        var catalog=Published(Catalog(true,automatic));
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");target.IsRongyu=targetStorage;
        catalog.ConfigColumns.Add(new(){Id="39",ConfigTableId=target.Id,YzProjectId=catalog.YzProjectId,ColumnName="short_name",Name="简称",LogicalType="string",ColumnLength="100"});
        catalog.ConfigPageColumns.Add(new(){Id="3009",ConfigPageId="301",ConfigColumnId="39",YzProjectId=catalog.YzProjectId,IsShow=1});
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(!explicitPage)source.Extra.Remove("linkConfigPageId");
        var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==source.Id);
        settings.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"short_name"}""");
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;
        if(automatic)
        {
            page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        }
        else catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="27").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        const string ns="Example.CheckboxPageLabel";
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(table=>table.Source.Id=="2");var pageModel=table.Pages.Single();
        var effective=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,pageModel));
        Assert.Equal("short_name",effective.Name.Source.ColumnName);
        Assert.Equal("targetNames",effective.Names.JsonName);
        Assert.Contains(effective.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.ColumnName=="parent_id");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-page-label");
        var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(root,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var detail=await File.ReadAllTextAsync(Path.Combine(generated,"frontend/pages/yzp/relation_source-detail-201.vue"));
            Assert.Contains("readonlyDisplayText(entity.targetNames)",detail);
            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/RelationSourceService.g.cs"));
            Assert.Contains("CheckboxScope201_25Async",service);
            Assert.Contains("ShortName",service);
            if(!automatic)Assert.Contains("FixedReadComparison.JsonArrayAnyContains",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType=assembly.GetTypes().Single(type=>type.Name=="RelationSourceService");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            Assert.NotNull(serviceType.GetMethod("Import201Async"));
            Assert.NotNull(serviceType.GetMethod("Export201Async"));
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>原冗余复选在名称页和简称页各自显示及筛选，从任一入口保存都更新两个名称副本。</summary>
    [Theory]
    [InlineData(0, true, false, false, false)]
    [InlineData(1, false, false, false, false)]
    [InlineData(0, true, true, false, false)]
    [InlineData(1, false, true, false, false)]
    [InlineData(0, true, true, true, false)]
    [InlineData(1, false, false, true, false)]
    [InlineData(0, true, true, true, true)]
    [InlineData(1, false, false, true, true)]
    [InlineData(0, true, true, false, false, true, false)]
    [InlineData(1, true, false, false, false, true, false)]
    [InlineData(1, false, true, false, false, true, false)]
    [InlineData(0, true, true, false, false, true, true)]
    [InlineData(0, true, true, false, false, false, false, "current_yz_manager_id")]
    [InlineData(0, true, true, false, false, true, false, "current_store_id")]
    [InlineData(0, true, true, false, false, true, true, "current_yz_manager_id")]
    public async Task Redundant_checkbox_page_label_uses_its_own_cache_and_updates_all(int targetStorage,bool includeNamePage,bool fixedTarget,bool fixedParent,bool independentParent,bool global=false,bool globalRowProject=false,string accountVariable="")
    {
        var catalog=Published(Catalog(false));
        var target=catalog.ConfigTables.Single(table=>table.Id=="3");
        target.IsRongyu=targetStorage;
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        targetPage.WhereSql=global?"":"yz_project_id=@current_yz_project_id";
        if(global)
        {
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            if(!globalRowProject)
            {
                catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==target.Id&&column.ColumnName=="yz_project_id");
                catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="33");
            }
        }
        if(fixedParent)targetPage.WhereSql+=" AND parent_id=@parentId";
        if(fixedTarget)
        {
            catalog.ConfigColumns.Add(new(){Id="39",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
                ColumnName="active_flag",LogicalType="int32"});
            targetPage.WhereSql+=global?"active_flag=1":" AND active_flag=1";
        }
        var accountScoped=accountVariable.Length>0;
        var sourceOwner=accountVariable=="current_store_id"?"store_id":"record_owner_id";
        var targetOwner=accountVariable=="current_store_id"?"store_id":"dictionary_owner_id";
        if(accountScoped)
        {
            catalog.ConfigColumns.Add(new(){Id="41",ConfigTableId="2",YzProjectId=catalog.YzProjectId,
                ColumnName=sourceOwner,LogicalType="string",ColumnLength="24"});
            catalog.ConfigColumns.Add(new(){Id="42",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
                ColumnName=targetOwner,LogicalType="int64"});
            targetPage.WhereSql+=" AND "+targetOwner+"=@"+accountVariable;
        }
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        source.ControlDetail=accountScoped?"""{"tableId":"3","pageId":"301","nameField":"name"}""":
            """{"tableId":"3","nameField":"name"}""";
        var cache=new ColumnMetadata
        {
            Id="29",ConfigTableId="2",YzProjectId=catalog.YzProjectId,ColumnName="target_alias",
            Name="目标简称",LogicalType="string",ColumnLength="max",
            Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("target.alias")}
        };
        catalog.ConfigColumns.Add(cache);
        var current=catalog.ConfigPages.Single(page=>page.Id=="201");
        current.IsImport=current.IsExport=1;
        if(global)
        {
            current.WhereSql="yz_project_id=@current_yz_project_id";
            var alias=catalog.ConfigColumns.Single(column=>column.ConfigTableId==target.Id&&column.ColumnName=="alias");
            if(!catalog.ConfigPageColumns.Any(field=>field.ConfigPageId==targetPage.Id&&field.ConfigColumnId==alias.Id))
                catalog.ConfigPageColumns.Add(new(){Id="3099",ConfigPageId=targetPage.Id,ConfigColumnId=alias.Id,
                    YzProjectId=catalog.YzProjectId,IsShow=1});
        }
        if(accountScoped)current.WhereSql="yz_project_id=@current_yz_project_id AND "+sourceOwner+"=@"+accountVariable;
        if(independentParent)
        {
            current.WhereSql="yz_project_id=@current_yz_project_id";
            catalog.ConfigPageColumns.Add(new(){Id="2026",ConfigPageId=current.Id,ConfigColumnId="26",
                YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
            if(targetStorage==1)source.Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        }
        if(includeNamePage)
        {
            var original=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(current))!;
            original.Id="202";
            original.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            original.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
            catalog.ConfigPages.Add(original);
            foreach(var setting in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==current.Id).ToArray())
            {
                var field=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(setting))!;
                field.Id="202"+field.ConfigColumnId;
                field.ConfigPageId=original.Id;
                catalog.ConfigPageColumns.Add(field);
            }
        }
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==current.Id&&field.ConfigColumnId==source.Id)
            .Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"alias"}""");
        catalog.ConfigPageColumns.Add(new(){Id="2029",ConfigPageId=current.Id,ConfigColumnId=cache.Id,
            YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});

        const string ns="Example.RedundantCheckboxPageLabel";
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(table=>table.Source.Id=="2");
        var actual=table.Pages.Single(page=>page.Source.Id==current.Id);
        var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,actual));
        Assert.Equal("alias",checkbox.Name.Source.ColumnName);
        Assert.Equal("targetAlias",checkbox.Names.JsonName);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var cacheBindings=fixedParent||accountScoped?RelationPropagationCompilation.DerivedBindings(model):RelationPropagationCompilation.Bindings(model);
        var propagation=cacheBindings.Where(binding=>binding.Foreign.Source.Id==source.Id).ToArray();
        Assert.Equal(new[]{"target_alias","target_names"},propagation.Select(binding=>binding.Cache.Source.ColumnName));
        if(fixedTarget||fixedParent||global)
            Assert.All(RelationPropagationCompilation.Definitions(propagation),definition=>Assert.Equal("301",definition.CheckboxTargetPageId));
        if(fixedParent)Assert.All(RelationPropagationCompilation.Definitions(propagation),definition=>
            Assert.Equal("26",definition.CheckboxSourceParentId));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-redundant-checkbox-page");
        var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(root,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            string Original(string kind,string id)=>Path.Combine(generated,$"frontend/pages/yzp/relation_source-{kind}-{id}.vue");
            var detail=await File.ReadAllTextAsync(Original("detail","201"));
            var list=await File.ReadAllTextAsync(Original("list","201"));
            Assert.Contains("readonlyDisplayText(entity.targetAlias)",detail);
            Assert.Contains("relationDisplayText(item.targetAlias)",list);
            Assert.Contains("filters.targetAlias",list);
            Assert.Contains("filters.targetIds",list);
            if(independentParent)
            {
                Assert.Contains("String(that.entity.parentId??'')",detail);
                Assert.Contains("parentId:optionParent",detail);
                Assert.DoesNotContain("targetParentId:optionParent",detail);
                Assert.Contains("this.$watch(()=>this.entity.parentId",detail);
                Assert.Contains("\"formParentField\":\"parentId\"",list);
                Assert.Contains("\"formFields\":[\"parentId\"]",list);
                Assert.Contains(targetStorage==1?"targetIdsRange":"target__list",list);
            }
            if(includeNamePage)
                Assert.Contains("relationDisplayText(item.targetNames)",await File.ReadAllTextAsync(Original("list","202")));

            var service=await File.ReadAllTextAsync(Path.Combine(generated,"Services/RelationSourceService.g.cs"));
            Assert.Contains("case \"targetAlias\":",service);
            Assert.Contains("ApplyJsonArrayAnyLike(session,query,entity=>entity.TargetAlias",service);
            if(independentParent&&targetStorage==1)
                Assert.Contains("ApplyJsonArrayHasAnyId(session,query,entity=>entity.TargetIds",service);
            else Assert.Contains("ApplyJsonArrayHasId(session,query,entity=>entity.TargetIds",service);
            foreach(var page in table.Pages)
            {
                var marker="private async Task ValidateCheckbox"+page.Source.Id+"_25Async(";
                var start=service.IndexOf(marker,StringComparison.Ordinal);
                var end=service.IndexOf("\n    }",start,StringComparison.Ordinal);
                var write=service[start..end];
                Assert.Contains("entity.TargetAlias=",write);
                Assert.Contains("entity.TargetNames=",write);
                Assert.Contains("selected.Select(id=>",write);
                if(fixedTarget)
                {
                    Assert.Contains((global?"CheckboxGlobalCondition":"CheckboxTargetCondition")+page.Source.Id+"_25",service);
                    Assert.Contains("active_flag",service);
                }
                if(accountScoped)
                {
                    var condition=(global?"CheckboxGlobalCondition":"CheckboxTargetCondition")+page.Source.Id+"_25";
                    Assert.Contains(condition+".BindAccount(actor).ApplyConstants",service);
                    Assert.Contains("AccountConditionVariables.Require(actor, "+JsonSerializer.Serialize(accountVariable),service);
                    Assert.Contains("ForUpdate()",service);
                }
                if(fixedParent)
                {
                    Assert.Contains(".Where(row=>row.ParentId==",service);
                    Assert.Contains("entity.ParentId",write);
                    if(independentParent)Assert.Contains("selected.Length==0&&",write);
                }
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);
            var serviceType=assembly.GetTypes().Single(type=>type.Name=="RelationSourceService");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            foreach(var page in model.Tables.Single(table=>table.Source.Id=="2").Pages)
            {
                Assert.NotNull(serviceType.GetMethod("Import"+page.Source.Id+"Async"));
                Assert.NotNull(serviceType.GetMethod("Export"+page.Source.Id+"Async"));
            }
            var request=assembly.GetTypes().Single(type=>type.Name=="RelationSourcePage201CreateRequest");
            Assert.Null(request.GetProperty("TargetAlias"));
            Assert.Null(request.GetProperty("TargetNames"));
            var package=new ApplicationPackager().Create(catalog,Path.Combine(root,"application"),ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
            var maintenance=assembly.GetTypes().Single(type=>type.Name=="GeneratedRelationMaintenance")
                .GetProperty("Catalog")!.GetValue(null)!;
            var plans=(System.Collections.IDictionary)maintenance.GetType()
                .GetProperty("Plans",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(maintenance)!;
            var maintenancePlan=plans["2"]!;
            Assert.Null(maintenancePlan.GetType().GetProperty("Error")!.GetValue(maintenancePlan));
            var read=maintenancePlan.GetType().GetProperty("Read")!.GetValue(maintenancePlan)!;
            Assert.NotNull(read);
            if(global&&!accountScoped)
            {
                var propagationType=assembly.GetTypes().Single(type=>type.Name=="GeneratedRelationMutation");
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(propagationType.TypeHandle);
                var expression=read.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
                foreach(var (cacheName,columnName) in new[]{("target_alias","alias"),("target_names","name")})
                {
                    var sql=Assert.IsType<string>(expression.Invoke(read,[cacheName]));
                    Assert.Contains($"multi_value.`{columnName}`",sql);
                    Assert.Equal(fixedTarget,sql.Contains("active_flag",StringComparison.Ordinal));
                    Assert.DoesNotContain("multi_value.`yz_project_id`",sql);
                }
                var definitions=RelationPropagationCompilation.Definitions(propagation);
                var published=RelationPropagationCompilation.PublishedMaintenanceMetadata(model,definitions);
                published.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId==targetPage.Id&&
                    field.ConfigColumnId==checkbox.Name.Source.Id);
                Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Relations.RelationMaintenanceCatalog.Compile(published,definitions));
            }
            if(fixedParent)
            {
                var expression=read.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
                foreach(var name in new[]{"target_alias","target_names"})
                {
                    var sql=Assert.IsType<string>(expression.Invoke(read,[name]));
                    Assert.Contains("b.`parent_id`",sql);
                    Assert.Contains("multi_value.`parent_id`",sql);
                    if(fixedTarget)Assert.Contains("active_flag",sql);
                }
                var affected=(IReadOnlyList<string>)maintenance.GetType().GetProperty("MixedTargets")!.GetValue(maintenance)!;
                Assert.Contains("relation_parent",affected);
                Assert.Contains("relation_target",affected);
            }
            else if(accountScoped)
            {
                var expression=read.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
                Assert.False(Assert.IsType<bool>(read.GetType().GetProperty("RequiresAccount",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(read)));
                foreach(var name in new[]{"target_alias","target_names"})
                {
                    var sql=Assert.IsType<string>(expression.Invoke(read,[name]));
                    Assert.Contains($"BINARY CAST(b.`{sourceOwner}` AS CHAR)",sql);
                    Assert.Contains($"BINARY CAST(multi_value.`{targetOwner}` AS CHAR)",sql);
                    Assert.Contains("multi_value.`active_flag`",sql);
                }
                var affected=(IReadOnlyList<string>)maintenance.GetType().GetProperty("MixedTargets")!.GetValue(maintenance)!;
                Assert.Contains("relation_target",affected);
                var definitions=RelationPropagationCompilation.Definitions(propagation);
                var published=RelationPropagationCompilation.PublishedMaintenanceMetadata(model,definitions);
                Assert.Contains("@"+accountVariable,published.ConfigPages.Single(page=>page.Id=="202").WhereSql);
                published.ConfigPages.Single(page=>page.Id=="202").WhereSql="yz_project_id=@current_yz_project_id";
                Assert.Throws<InvalidOperationException>(()=>
                    Metarill.Data.Relations.RelationMaintenanceCatalog.CompileWithDerivedCrossTables(published,[],definitions));
            }
            else if(fixedTarget)
            {
                var expression=read.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
                foreach(var name in new[]{"target_alias","target_names"})
                    Assert.Contains("multi_value.`active_flag`",Assert.IsType<string>(expression.Invoke(read,[name])));
                var propagationPlan=maintenance.GetType().GetProperty("Propagation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(maintenance)!;
                var bindings=(System.Collections.IEnumerable)propagationPlan.GetType().GetProperty("Bindings",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(propagationPlan)!;
                Assert.Equal(2,bindings.Cast<object>().Count());
                foreach(var binding in bindings)
                {
                    var condition=binding.GetType().GetProperty("PublishedCondition")!.GetValue(binding)!;
                    Assert.Contains("active_flag",Assert.IsAssignableFrom<IReadOnlyList<string>>(condition.GetType().GetProperty("Fields")!.GetValue(condition)));
                }
            }

            // 同一缓存被多个页面读写时，不允许某一页用额外条件改变派生范围。
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==current.Id&&field.ConfigColumnId==source.Id)
                .Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"alias","whereSql":"name='固定名称'"}""");
            var mismatch=GenerationModel.Create(catalog,ns);
            Assert.Contains(GenerationCapabilityAudit.Collect(mismatch), gap=>gap.PageId==current.Id&&
                gap.Reason.Contains("同一固定目标范围",StringComparison.Ordinal));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>同一逻辑目标父范围连接原候选、写入和两份名称回填，实际父键由稀疏级联还原。</summary>
    [Theory]
    [InlineData(null,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(null,true,"current_yz_manager_id")]
    [InlineData(false,false,"current_store_id")]
    [InlineData(true,true,"current_yz_manager_id")]
    [InlineData(null,false,"","physical")]
    [InlineData(null,true,"current_yz_manager_id","logical")]
    [InlineData(false,false,"current_store_id","logical")]
    [InlineData(true,true,"current_yz_manager_id","physical")]
    [InlineData(null,false,"","","form")]
    [InlineData(false,true,"current_store_id","logical","form")]
    [InlineData(true,false,"current_yz_manager_id","physical","mixed")]
    public async Task Redundant_checkbox_shares_names_under_a_logical_target_parent(bool? globalWithRowProject,bool explicitCheckbox,
        string accountVariable="",string additionalParent="",string formParentMode="")
    {
        const string ns="Example.CheckboxLogicalParent";
        var catalog=NonRedundantCrossTableGenerationTests.Catalog("checkbox_parent_label","string","int64");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        targetPage.WhereSql+=" AND country_id=@parentId AND active_flag=1";
        if(globalWithRowProject is {} rowProject)
        {
            catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            targetPage.WhereSql="country_id=@parentId AND active_flag=1";
            if(!rowProject)catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");
        }
        catalog.ConfigColumns.AddRange([
            new(){Id="281",ConfigTableId="2",ColumnName="alias",LogicalType="string"},
            new(){Id="282",ConfigTableId="2",ColumnName="active_flag",LogicalType="int32"},
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="92",ConfigTableId="9",ColumnName="name",LogicalType="string"},
            new(){Id="93",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="94",ConfigTableId="9",ColumnName="owner_country_id",LinkConfigTableId="3",LogicalType="string",ColumnLength="24"},
            new(){Id="95",ConfigTableId="9",ColumnName="selected_ids",Name="复选",LogicalType="string",ColumnLength="max",
                ControlKind="MultiCheckBoxDatabase",LinkConfigTableId="2",
                ControlDetail="""{"tableId":"2","pageId":"201","nameField":"name"}"""},
            new(){Id="96",ConfigTableId="9",ColumnName="selected_alias",LogicalType="string",ColumnLength="max",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("selected.alias")}}
        ]);
        if(additionalParent.Length>0)
        {
            targetPage.Extra["type"]=JsonSerializer.SerializeToElement(0);
            targetPage.WhereSql+=" AND alternate_country_id=@parent_id";
            if(additionalParent=="physical")
                catalog.ConfigColumns.Add(new(){Id="284",ConfigTableId="2",ColumnName="alternate_country_id",
                    LinkConfigTableId="3",LogicalType="string",ColumnLength="24"});
            else
            {
                catalog.ConfigColumns.Add(new(){Id="284",ConfigTableId="2",ColumnName="alternate_area_id",
                    LinkConfigTableId="4",LogicalType="int64",ControlKind="CascadeTree",
                    ControlDetail=JsonSerializer.Serialize(new
                    {
                        sourceMode="crossTable",
                        levels=new object[]{new{table="checkbox_parent_label_country",columnPrefix="alternate_country"},
                            new{table="checkbox_parent_label_area",columnPrefix="alternate_area",parentField="checkbox_parent_label_country_id"}}
                    })});
                catalog.ConfigPageColumns.Add(new(){Id="201284",ConfigPageId=targetPage.Id,ConfigColumnId="284",IsEdit=1,IsShow=1});
            }
        }
        var usesFormParent=formParentMode.Length>0;
        if(usesFormParent)
        {
            var parentField=catalog.ConfigColumns.Single(column=>column.Id=="94");
            parentField.LogicalType="int64";
            parentField.ColumnLength="";
            parentField.ControlKind="List";
            parentField.ControlDetail="""{"tableId":"3","pageId":"301","nameField":"id"}""";
            parentField.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("301");
            catalog.ConfigPageColumns.Add(new(){Id="30131",ConfigPageId="301",ConfigColumnId="31",IsShow=1});
        }
        var useAccount=accountVariable.Length>0;
        var storeScope=accountVariable=="current_store_id";
        var targetOwner=storeScope?"store_id":"dictionary_owner_id";
        var sourceOwner=storeScope?"store_id":"record_owner_id";
        if(useAccount)
        {
            catalog.ConfigColumns.Add(new(){Id="283",ConfigTableId="2",ColumnName=targetOwner,LogicalType="int64"});
            catalog.ConfigColumns.Add(new(){Id="97",ConfigTableId="9",ColumnName=sourceOwner,LogicalType="string",ColumnLength="24"});
            targetPage.WhereSql+=" AND "+targetOwner+"=@"+accountVariable;
        }
        catalog.ConfigPageColumns.Add(new(){Id="201281",ConfigPageId="201",ConfigColumnId="281",IsShow=1});
        catalog.ConfigTables.Add(new(){Id="9",TableName="checkbox_parent_label_consumer",IsRongyu=1});
        foreach(var id in new[]{"901","902"})
        {
            var aliasPage=id=="902";
            var formParent=usesFormParent&&(formParentMode!="mixed"||aliasPage);
            var page=new PageMetadata {Id=id,ConfigTableId="9",IsImport=1,IsExport=1,
                WhereSql="yz_project_id=@current_yz_project_id"+(formParent?"":" AND owner_country_id=@parentId")};
            if(formParent)
                catalog.ConfigPageColumns.Add(new(){Id=id+"94",ConfigPageId=id,ConfigColumnId="94",IsShow=1,IsEdit=1,
                    Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
            if(useAccount)page.WhereSql+=" AND "+sourceOwner+"=@"+accountVariable;
            if(aliasPage)
            {
                page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
                page.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
            }
            catalog.ConfigPages.Add(page);
            catalog.ConfigPageColumns.Add(new(){Id=id+"92",ConfigPageId=id,ConfigColumnId="92",IsShow=1,IsEdit=1});
            var setting=new PageColumnMetadata {Id=id+"95",ConfigPageId=id,ConfigColumnId="95",IsShow=1,IsEdit=1,
                Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}};
            if(aliasPage)
            {
                setting.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"alias"}""");
                catalog.ConfigPageColumns.Add(new(){Id=id+"96",ConfigPageId=id,ConfigColumnId="96",IsShow=1,IsEdit=1});
            }
            catalog.ConfigPageColumns.Add(setting);
        }
        if(explicitCheckbox)
            catalog.ConfigColumns.Single(column=>column.Id=="95").Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        Published(catalog);

        var model=GenerationModel.Create(catalog,ns);
        var owner=model.Tables.Single(table=>table.Source.Id=="9");
        foreach(var page in owner.Pages)
        {
            var relation=owner.Columns.Single(column=>column.Source.Id=="95");
            var resolution=DatabaseCheckboxCompilation.Resolve(model,owner,page,relation);
            Assert.True(resolution.Definition is not null,resolution.Reason);
            Assert.Equal("201",resolution.Definition!.LogicalTargetPage?.Source.Id);
            Assert.Equal("94",resolution.Definition.SourceParent?.Source.Id);
            Assert.Equal(usesFormParent&&(page.Source.Id=="902"||formParentMode!="mixed")?"94":null,
                resolution.Definition.FormParentField?.Source.Id);
            Assert.Equal(additionalParent.Length>0?2:1,resolution.Definition.Scopes.Count(scope=>scope.Source=="parent"));
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var bindings=RelationPropagationCompilation.Definitions(RelationPropagationCompilation.DerivedBindings(model)
            .Where(binding=>binding.Foreign.Source.Id=="95").ToArray());
        Assert.Equal(2,bindings.Length);
        Assert.All(bindings,binding=>Assert.Equal("201",binding.CheckboxTargetPageId));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-logical-parent-names");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,ns);
            var code=await File.ReadAllTextAsync(Path.Combine(output,"Services/CheckboxParentLabelConsumerService.g.cs"));
            Assert.Contains("CompileScopedCheckboxLabel",code);
            Assert.Contains("entity.SelectedAlias=",code);
            Assert.Contains("entity.SelectedNames=",code);
            Assert.Contains("names[id].CacheName",code);
            Assert.Contains("names[id].CacheAlias",code);
            Assert.Contains("currentRead:locking",code);
            Assert.Contains(explicitCheckbox?"ApplyJsonArrayHasAnyId":"ApplyJsonArrayHasId",code);
            var list=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/checkbox_parent_label_consumer-list-902.vue"));
            var detail=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/checkbox_parent_label_consumer-detail-902.vue"));
            Assert.Contains("findNameIdSelected95",list);
            Assert.Contains("findNameIdSelected95",detail);
            Assert.Contains("selectedAlias",list);
            Assert.Contains("selectedAlias",detail);
            if(usesFormParent)
            {
                Assert.Contains("String(that.entity.ownerCountryId??'')",detail);
                Assert.Contains("this.$watch(()=>this.entity.ownerCountryId",detail);
                Assert.Contains("parentId:optionParent",detail);
                Assert.Contains("\"formParentField\":\"ownerCountryId\"",list);
                Assert.Contains("submitted.Contains(\"owner_country_id\")",code);
                Assert.Contains("selected.Length==0&&string.IsNullOrWhiteSpace(entity.OwnerCountryId?.ToString(CultureInfo.InvariantCulture))?[]:await DerivedCheckboxRows902_95Async(entity.OwnerCountryId?.ToString(CultureInfo.InvariantCulture)",code);
            }

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType(ns+".Generated.Services.CheckboxParentLabelConsumerService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            foreach(var id in new[]{"901","902"})
            {
                Assert.NotNull(type.GetMethod("Import"+id+"Async"));
                Assert.NotNull(type.GetMethod("Export"+id+"Async"));
            }
            var candidate=Assert.IsType<NonRedundantReadPlan>(type.GetField("DerivedCheckboxPlan902_95",
                BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
            Assert.Equal("string",candidate.OutputTypes["alias"]);
            Assert.Equal("string",candidate.OutputTypes["name"]);
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            Assert.Equal(useAccount,candidate.RequiresAccount);
            if(additionalParent.Length>0)
            {
                var sqlPlan=candidate.GetType().GetProperty("MySql",hidden)!.GetValue(candidate)!;
                var scope=Assert.IsType<string>(sqlPlan.GetType().GetProperty("Scope",hidden)!.GetValue(sqlPlan));
                var alternate=Assert.IsType<string>(sqlPlan.GetType().GetMethod("Expression",hidden)!
                    .Invoke(sqlPlan,["alternate_country_id"]));
                Assert.Contains(alternate,scope);
                if(additionalParent=="logical")
                {
                    Assert.StartsWith("CASE WHEN",alternate);
                    Assert.Contains("b.`alternate_area_id`",alternate);
                }
                else Assert.Equal("b.`alternate_country_id`",alternate);
            }
            if(useAccount)
            {
                const long accountId=9007199254740993L;
                const long storeId=9007199254740994L;
                var actor=new RuntimeActor(accountId,"账号",false,catalog.YzProjectId,"5"){StoreId=storeId};
                var bound=candidate.BindAccount(actor);
                Assert.False(bound.RequiresAccount);
                var sqlPlan=bound.GetType().GetProperty("MySql",hidden)!.GetValue(bound)!;
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(sqlPlan.GetType()
                    .GetProperty("Constants",hidden)!.GetValue(sqlPlan));
                Assert.Contains<object?>(storeScope?storeId:accountId,constants.Values);
                Assert.DoesNotContain<object?>(storeScope?accountId:storeId,constants.Values);
                if(storeScope)Assert.Throws<UnauthorizedAccessException>(()=>candidate.BindAccount(actor with{StoreId=null}));
            }
            var maintenance=assembly.GetTypes().Single(t=>t.Name=="GeneratedRelationMaintenance")
                .GetProperty("Catalog")!.GetValue(null)!;
            var affected=(IReadOnlyList<string>)maintenance.GetType().GetProperty("MixedTargets")!.GetValue(maintenance)!;
            Assert.Contains("checkbox_parent_label_source",affected);
            Assert.Contains("checkbox_parent_label_country",affected);
            Assert.Contains("checkbox_parent_label_area",affected);
            var plans=(System.Collections.IDictionary)maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance)!;
            var plan=plans["9"]!;
            Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
            var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
            foreach(var name in new[]{"selected_alias","selected_names"})
            {
                var sql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,[name]));
                Assert.Contains("b.`owner_country_id`",sql);
                Assert.Contains("multi_value.`active_flag`",sql);
                Assert.Contains("CASE WHEN",sql);
                if(additionalParent=="logical")Assert.Contains("multi_value.`alternate_area_id`",sql);
                else if(additionalParent=="physical")Assert.Contains("multi_value.`alternate_country_id`",sql);
                if(useAccount)
                {
                    Assert.Contains($"BINARY CAST(b.`{sourceOwner}` AS CHAR)",sql);
                    Assert.Contains($"BINARY CAST(multi_value.`{targetOwner}` AS CHAR)",sql);
                }
            }
            Assert.False(Assert.IsType<bool>(read.GetType().GetProperty("RequiresAccount",hidden)!.GetValue(read)));
            var package=new ApplicationPackager().Create(catalog,Path.Combine(directory,"application"),ns,NonRedundantApplicationFixture.Root());
            Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);

            var published=RelationPropagationCompilation.PublishedMaintenanceMetadata(model,bindings);
            if(additionalParent.Length>0)
            {
                // 两个父条件必须绑定同一真实父表，精简目录重新装载时也要拒绝不同角色。
                var publishedTarget=published.ConfigPages.Single(page=>page.Id=="201");
                var where=publishedTarget.WhereSql;
                if(additionalParent=="logical")
                    publishedTarget.WhereSql=where.Replace("alternate_country_id=@parent_id","alternate_area_id=@parent_id",StringComparison.Ordinal);
                else published.ConfigColumns.Single(column=>column.Id=="284").LinkConfigTableId="4";
                Assert.Throws<InvalidOperationException>(()=>
                    Metarill.Data.Relations.RelationMaintenanceCatalog.CompileWithDerivedCrossTables(published,[],bindings));
                publishedTarget.WhereSql=where;
                if(additionalParent=="physical")published.ConfigColumns.Single(column=>column.Id=="284").LinkConfigTableId="3";
            }
            if(useAccount)
            {
                var sourcePage=published.ConfigPages.Single(page=>page.Id=="902");
                var publishedScope=sourcePage.WhereSql;
                sourcePage.WhereSql=publishedScope.Replace(" AND "+sourceOwner+"=@"+accountVariable,"",StringComparison.Ordinal);
                Assert.NotEqual(publishedScope,sourcePage.WhereSql);
                Assert.Throws<InvalidOperationException>(()=>
                    Metarill.Data.Relations.RelationMaintenanceCatalog.CompileWithDerivedCrossTables(published,[],bindings));
                sourcePage.WhereSql=publishedScope;
            }
            if(usesFormParent)
            {
                var savedParent=Assert.Single(published.ConfigPageColumns,field=>field.ConfigPageId=="902"&&field.ConfigColumnId=="94");
                Assert.Equal(1,savedParent.IsEdit);
                published.ConfigPageColumns.Remove(savedParent);
            }
            else published.ConfigPages.Single(page=>page.Id=="902").WhereSql="yz_project_id=@current_yz_project_id";
            Assert.Throws<InvalidOperationException>(()=>
                Metarill.Data.Relations.RelationMaintenanceCatalog.CompileWithDerivedCrossTables(published,[],bindings));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>整数业务名称在原复选中仍输出文本，Excel 与维护使用同一物理字段。</summary>
    [Theory]
    [InlineData(true,false,"int32","list")]
    [InlineData(true,true,"int64","edit")]
    [InlineData(false,false,"int64","list")]
    [InlineData(false,true,"int32","edit")]
    public async Task Integer_checkbox_labels_publish_original_candidate_workbook_and_maintenance(bool nonRedundant,bool automatic,string type,string exportMode)
    {
        var catalog=Published(Catalog(nonRedundant,automatic));
        catalog.ConfigColumns.Single(column=>column.Id=="32").LogicalType=type;
        var pageMetadata=catalog.ConfigPages.Single(page=>page.Id=="201");pageMetadata.IsImport=pageMetadata.IsExport=1;
        pageMetadata.Extra["exportMethod"]=JsonSerializer.SerializeToElement(exportMode);
        var field=catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId=="25");field.IsEdit=field.IsShow=1;
        field.Extra["isDetailShow"]=JsonSerializer.SerializeToElement(1);

        const string ns="Example.IntegerCheckbox";
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="2");var page=Assert.Single(table.Pages);
        var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page));
        Assert.True(checkbox.IntegerLabel);
        Assert.Equal(automatic&&!nonRedundant,MultiNameStorageCompilation.IsImplicit(model,checkbox.Names));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));new FrontendPackager().Validate(catalog);
        var importer=ImportRenderer.Service(model,table,page);
        var exporter=ExportRenderer.Service(model,table,page);
        if(nonRedundant)
        {
            Assert.Equal("string",NonRedundantReads.Compile(catalog,"2",["target_names"]).OutputTypes["target_names"]);
            Assert.Contains(type=="int32"?"int.TryParse(label":"long.TryParse(label",importer);
            Assert.Contains("row.Name==name",importer);
            Assert.Contains("names[id]?.ToString(CultureInfo.InvariantCulture)",exporter);
        }
        else
        {
            Assert.Contains("DatabaseCheckboxValue.ProjectIds",importer);
            Assert.DoesNotContain("ExportCheckbox201_25Async",exporter);
            Assert.Contains("entity.TargetIds,",exporter);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-integer-checkbox-labels");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetTypes().Single(item=>item.Name=="RelationSourceService");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import201Async"));Assert.NotNull(service.GetMethod("Export201Async"));
            var generated=await File.ReadAllTextAsync(Path.Combine(output,"Services/RelationSourceService.g.cs"));
            Assert.Contains("var label=row.Name?.ToString(CultureInfo.InvariantCulture)",generated);
            Assert.Contains("new DatabaseCheckboxOption(id,label,id,label)",generated);
            if(!nonRedundant)
            {
                Assert.Contains("row=>row.Name?.ToString(CultureInfo.InvariantCulture)",generated);
                var maintenance=assembly.GetTypes().Single(item=>item.Name=="GeneratedRelationMaintenance").GetProperty("Catalog")!.GetValue(null)!;
                var plans=(System.Collections.IDictionary)maintenance.GetType().GetProperty("Plans",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(maintenance)!;
                var plan=plans["2"]!;
                Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
                Assert.NotNull(plan.GetType().GetProperty("Read")!.GetValue(plan));
            }
            var probe=Path.Combine(NonRedundantApplicationFixture.Root(),"backend/tests/Metarill.Generation.Tests/DatabaseCheckboxVueProbe.mjs");
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
            foreach(var arg in new[]{probe,output,type})start.ArgumentList.Add(arg);
            using var process=System.Diagnostics.Process.Start(start)!;
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try{await process.WaitForExitAsync(timeout.Token);}
            catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}
            Assert.True(process.ExitCode==0,await stdout+await stderr);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>使用包内基础源码构建正式宿主，重复更新只替换归属产物，原复选组件与手写代码保持。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task Full_checkbox_package_builds_and_updates_original_frontend_without_replacing_handwritten_code(bool nonRedundant,bool automatic)
    {
        var catalog=Published(Catalog(nonRedundant,automatic));var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            var model=GenerationModel.Create(catalog,"Example.CheckboxApplication");Assert.Empty(GenerationCapabilityAudit.Collect(model));var package=new ApplicationPackager().Create(catalog,output,model.Namespace,NonRedundantApplicationFixture.Root());Assert.True(package.FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            const string component="uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue";Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(NonRedundantApplicationFixture.Root(),"frontend/metarill_uni_admin",component)),await File.ReadAllBytesAsync(Path.Combine(output,"frontend",component)));
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subset=Assert.Single(profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray());Assert.Equal("MultiCheckBoxDatabase",subset.GetProperty("controlKind").GetString());Assert.Equal(5000,subset.GetProperty("maximumItems").GetInt32());Assert.Equal(!nonRedundant,subset.GetProperty("renamePropagationSupported").GetBoolean());
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 通用复选包手写业务保留。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,model.Namespace,NonRedundantApplicationFixture.Root());var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明目标、隐藏坏条件及跨项目发布不能借原UI外壳通过Full门禁。</summary>
    [Theory][InlineData("global")][InlineData("derivedName")][InlineData("targetRealm")][InlineData("hiddenInvalid")][InlineData("provider")][InlineData("kindOverlay")]
    public async Task Full_checkbox_rejects_unproven_or_unpublished_declarations_before_output(string kind)
    {
        var catalog=Published(Catalog());var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(kind=="global")catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="derivedName")catalog.ConfigColumns.Single(column=>column.Id=="32").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="targetRealm")catalog.ConfigTables.Single(table=>table.Id=="3").YzProjectId="";
        if(kind=="hiddenInvalid"){source.ControlDetail="头;头;password=@actor;id desc";catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");}
        if(kind=="provider")catalog.DatabaseProvider="postgresql";
        if(kind=="kindOverlay")catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["controlKind"]=JsonSerializer.SerializeToElement("List");
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,"Example.RejectedCheckbox",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));}finally{if(Directory.Exists(output))await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>发布测试使用明确的同项目表、字段、页面及菜单后台，绝不借空归属模拟正式包。</summary>
    private static MetadataCatalog Published(MetadataCatalog catalog)
    {
        foreach(var record in catalog.ConfigTables.Cast<MetadataRecord>().Concat(catalog.ConfigColumns).Concat(catalog.ConfigPages).Concat(catalog.ConfigPageColumns))record.YzProjectId=catalog.YzProjectId;
        catalog.YzAdminTypes.Add(new(){Id="5",YzProjectId=catalog.YzProjectId,Name="复选后台"});foreach(var page in catalog.ConfigPages)page.YzAdminTypeId="5";
        catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="复选关系",Kind="GENERATED_PAGE",ConfigPageId="201"});return catalog;
    }
    /// <summary>缺少真实项目列或错误范围类型在静态模型阶段拒绝，不产出会宽读或无法编译的候选。</summary>
    [Theory][InlineData("sourceProject")][InlineData("targetProjectCache")][InlineData("parentProjectCache")][InlineData("boolParentScope")]
    public void Checkbox_requires_real_record_identity_and_typed_scope_before_rendering(string kind)
    {
        var catalog=Catalog(false);var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        if(kind=="sourceProject"){catalog.ConfigColumns.Single(column=>column.Id=="23").ColumnName="project_unknown";catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="parent_id=@parentId";}
        if(kind=="targetProjectCache")catalog.ConfigColumns.Single(column=>column.Id=="33").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="parentProjectCache")catalog.ConfigColumns.Single(column=>column.Id=="13").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="boolParentScope"){catalog.ConfigColumns.Single(column=>column.Id=="36").LogicalType="bool";catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id";}
        var model=GenerationModel.Create(catalog,"Example.CheckboxIdentity");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var result=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id==source.Id));Assert.Null(result.Definition);Assert.NotNull(result.Reason);
    }
    /// <summary>原多选模型仅换回数据库复选定义，页面布局及既有父范围保持。</summary>
    internal static MetadataCatalog Catalog(bool nonRedundant=true,bool automatic=false,string prefix="relation")
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog(prefix);var source=catalog.ConfigColumns.Single(column=>column.Id=="25");source.ControlKind="MultiCheckBoxDatabase";source.ControlDetail="旧第一段;旧第二段;parent_id=@parentId;name desc";
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(nonRedundant){catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;var name=catalog.ConfigColumns.Single(column=>column.Id=="27");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.name");}
        if(automatic){catalog.ConfigColumns.RemoveAll(column=>column.Id=="27");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
        return catalog;
    }
}
