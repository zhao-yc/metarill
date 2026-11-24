using System.Reflection;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原多选和真实数据库复选共用全局缓存名称，生成、维护、自动名称和原Vue必须闭合。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalMultipleCachedTargetGenerationTests
{
    /// <summary>R0/R1与显式/自动名称不能省略候选、原200/5000预算、维护节点或文件字段。</summary>
    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)]
    public void Both_array_controls_have_one_complete_model_and_maintenance(int mode,bool automatic)
    {
        var c=Catalog(mode,automatic);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalMultipleCachedTarget");Assert.Empty(GenerationCapabilityAudit.Collect(model));var table=model.Tables.Single(x=>x.Source.Id=="3");var page=table.Pages.Single(x=>x.Source.Id=="30");
        var multi=Assert.Single(MultiSelectCompilation.ForPage(model,table,page));Assert.Equal("14",multi.Name.Source.Id);Assert.Equal("10",multi.TargetPage.Source.Id);Assert.Equal("global_source_global_target_names",multi.Names!.Source.ColumnName);
        var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page));Assert.Equal("14",checkbox.Name.Source.Id);Assert.Equal("selected_global_source_global_target_names",checkbox.Names.Source.ColumnName);Assert.DoesNotContain(checkbox.Names,page.Writable);Assert.Contains(page.Writable,x=>x.Source.Id=="39");
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.Contains("global_target_name",maintenance.MixedSourceColumns["global_source"]);Assert.DoesNotContain("cached_consumer",maintenance.MixedSourceColumns.Keys);
        // 数组副本由原物理传播队列唯一处理；中间缓存改名事件再经同一协调器转发，不能把它误算成混合标量节点。
        var bindings=RelationPropagationCompilation.Bindings(model).Where(binding=>binding.Source.Source.Id=="3").ToArray();Assert.Equal(mode==1?2:0,bindings.Length);if(mode==1)Assert.Equal(new[]{"global_source_global_target_names","selected_global_source_global_target_names"},bindings.Select(binding=>binding.Cache.Source.ColumnName).Order(StringComparer.Ordinal));
        if(mode==1)
        {
            var plans=(System.Collections.IEnumerable)maintenance.GetType().GetProperty("Plans",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(maintenance)!;
            var entry=Assert.Single(plans.Cast<object>(),item=>item.GetType().GetProperty("Key")!.GetValue(item)!.ToString()=="3");var plan=entry.GetType().GetProperty("Value")!.GetValue(entry)!;Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));var multiples=(System.Collections.IEnumerable)plan.GetType().GetProperty("Multiples")!.GetValue(plan)!;Assert.Equal(2,multiples.Cast<object>().Count());
        }
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>标量缓存沿原多选和复选编译服务，覆盖 R0 读取和 R1 显式／自动物理数组。</summary>
    [Theory]
    [InlineData(0, false, "int32")]
    [InlineData(0, true, "int64")]
    [InlineData(1, false, "int64")]
    [InlineData(1, true, "int32")]
    [InlineData(0, false, "bool")]
    [InlineData(0, true, "decimal")]
    [InlineData(1, false, "double")]
    [InlineData(1, true, "date")]
    [InlineData(0, false, "datetime")]
    [InlineData(1, true, "time")]
    public async Task Typed_cache_targets_drive_both_array_controls(int mode, bool automatic, string type)
    {
        var catalog = Catalog(mode,automatic);
        foreach (var column in catalog.ConfigColumns.Where(column => column.Id is "14" or "22"))
            column.LogicalType = type;
        catalog.ConfigPageColumns.Single(field => field.Id == "3034").Extra["isSearch"] = JsonSerializer.SerializeToElement(1);
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigColumnId is "35" or "40"))
            field.Extra.Remove("isSearch");

        const string application = "Example.GlobalMultipleIntegerCache";
        var model = GenerationModel.Create(catalog,application);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = Assert.Single(owner.Pages);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var multi = Assert.Single(MultiSelectCompilation.ForPage(model,owner,page));
        var checkbox = Assert.Single(DatabaseCheckboxCompilation.ForPage(model,owner,page));
        Assert.True(multi.TypedLabel);
        Assert.Equal("10",checkbox.GlobalPage!.Source.Id);
        Assert.Equal(type,checkbox.Name.Source.LogicalType);
        Assert.Equal(mode == 1 ? SelectionNameFilterKind.JsonArrayAnyLike : SelectionNameFilterKind.JoinedJsonArrayAnyLike,
            Assert.Single(SelectionNameFilterCompilation.ForPage(model,owner,page).Bindings).Kind);
        var maintenance = MixedRelationPropagationCompilation.ForModel(model);
        Assert.Equal(new[] { "global_target_name" }, maintenance.MixedSourceColumns["global_source"]);
        Assert.Equal(mode == 1 ? 2 : 0, RelationPropagationCompilation.Bindings(model).Count(binding => binding.Source.Source.Id == "3"));

        var boundary = Path.Combine(Path.GetTempPath(),"metarill-global-array-integer-cache");
        var directory = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,application);
            var service = await File.ReadAllTextAsync(Path.Combine(output,"Services/CachedConsumerService.g.cs"));
            Assert.Contains("ImportMulti30_34Async",service);
            Assert.Contains("ImportCheckbox30_38Async",service);
            Assert.Contains("CheckboxGlobalCondition30_38",service);
            Assert.Contains("row.GlobalTargetName==key",service);
            if(type is "int32" or "int64")Assert.Contains("?.ToString(CultureInfo.InvariantCulture)",service);
            else
            {
                Assert.Contains($"MultiValueLabel.Parse(\"{type}\"",service);
                Assert.Contains($"MultiValueLabel.Text(\"{type}\"",service);
                var controller=Assert.Single(Directory.GetFiles(Path.Combine(output,"Controllers"),"GlobalSourcePage10Controller.g.cs"));
                Assert.Contains($"item[\"globalTargetName\"] = MultiValueLabel.Text(\"{type}\"",await File.ReadAllTextAsync(controller));
            }
            Assert.Contains("Export30Async",service);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var generated = assembly.GetType(application + ".Generated.Services.CachedConsumerService",throwOnError:true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(generated.TypeHandle);
        }
        finally
        {
            if (Directory.Exists(directory)) await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>四个完整包独立严格构建，重复更新保护手写代码，并执行真实原复选与多选Vue。</summary>
    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)][InlineData(1,true,"bool")]
    public async Task Full_applications_build_preserve_partial_and_original_array_interactions(int mode,bool automatic,string type="string")
    {
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-cached-target");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var c=Catalog(mode,automatic);
        foreach(var column in c.ConfigColumns.Where(column=>column.Id is "14" or "22"))column.LogicalType=type;
        try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalMultipleCachedTarget",root);var hand=Path.Combine(output,"backend/generated/Services/CachedConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalMultipleCachedTarget.Generated.Services;\npublic partial class CachedConsumerService { /// <summary>重复生成保护手写业务。</summary>\npublic string PreservedBusinessRule()=>\"原手写扩展\"; }\n");var saved=await File.ReadAllBytesAsync(hand);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalMultipleCachedTarget",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(saved,await File.ReadAllBytesAsync(hand));var repeat=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var arg in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(arg);await NonRedundantApplicationFixture.RunAsync(start);
            var node=NonRedundantApplicationFixture.Start("node",output);node.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalMultipleCachedTargetVueProbe.mjs"));node.ArgumentList.Add(output);node.ArgumentList.Add(type);await NonRedundantApplicationFixture.RunAsync(node);
            foreach(var component in new[]{"uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue","common/yzp_runtime/originalSelectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>任意名称/页面覆盖、缺缓存或未证明全局终点不能靠新nameField取得完整包。</summary>
    [Theory][InlineData("producerProject")][InlineData("privateTerminal")][InlineData("badPath")][InlineData("overrideName")][InlineData("overridePage")][InlineData("credential")][InlineData("nonGlobal")][InlineData("file")]
    public void Invalid_cached_array_model_rejects_before_staging(string kind)
    {
        var c=Catalog(1,false);var checkbox=c.ConfigColumns.Single(x=>x.Id=="38");var cache=c.ConfigColumns.Single(x=>x.Id=="14");
        if(kind=="producerProject")c.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64"});if(kind=="privateTerminal")c.ConfigTables.Single(x=>x.Id=="2").Extra.Clear();if(kind=="badPath")cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");if(kind=="overrideName")c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="38").Extra["controlDetail"]=JsonSerializer.SerializeToElement(checkbox.ControlDetail.Replace("global_target_name","status",StringComparison.Ordinal));if(kind=="overridePage")c.ConfigPageColumns.Single(x=>x.ConfigColumnId=="34").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"20\"}");if(kind=="credential")checkbox.ControlDetail=checkbox.ControlDetail.Replace("global_target_name","password",StringComparison.Ordinal);if(kind=="nonGlobal")c.ConfigTables.Single(x=>x.Id=="1").Extra.Clear();if(kind=="file")cache.ControlKind="FileLibrary";
        var output=Path.Combine(Path.GetTempPath(),"metarill-global-multiple-cached-invalid",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidGlobalMultipleCached",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>新JSON名称可使用已发布全局物理字符串；不能因缓存能力而强迫普通字典带Join标记。</summary>
    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)]
    public void Explicit_global_physical_label_keeps_array_name_ownership(int mode,bool automatic)
    {
        var c=PhysicalLabelCatalog(mode,automatic);var model=GenerationModel.Create(c,"Example.GlobalPhysicalLabel");Assert.Empty(GenerationCapabilityAudit.Collect(model));var table=model.Tables.Single(x=>x.Source.Id=="3");var page=table.Pages.Single(x=>x.Source.Id=="30");var multi=Assert.Single(MultiSelectCompilation.ForPage(model,table,page));var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,table,page));Assert.Equal("16",multi.Name.Source.Id);Assert.Equal("16",checkbox.Name.Source.Id);Assert.Equal("global_source_captions",multi.Names!.Source.ColumnName);Assert.Equal("selected_global_source_captions",checkbox.Names.Source.ColumnName);
    }
    /// <summary>原多选的普通文本名称在生成程序集重建后仍使用字典页的固定范围。</summary>
    [Theory]
    [InlineData(0,0)]
    [InlineData(0,1)]
    [InlineData(1,0)]
    [InlineData(1,1)]
    [InlineData(0,0,true)]
    [InlineData(1,0,true)]
    public Task Published_global_multiselect_text_keeps_fixed_dictionary_status(int mode,int targetStorage,bool derivedName=false)=>
        VerifyPublishedGlobalMultiselect(mode,targetStorage,derivedName,null);

    /// <summary>原多选的固定字典候选与字典自身业务分开查询，复用原 Excel 和名称维护合同。</summary>
    [Theory]
    [InlineData(0,"int64")]
    [InlineData(0,"string")]
    [InlineData(1,"int64")]
    [InlineData(1,"string")]
    public Task Stored_global_multiselect_fixed_dictionary_uses_published_candidate_range(int mode,string projectType)=>
        VerifyPublishedGlobalMultiselect(mode,1,false,projectType);

    /// <summary>原详情多选引用无条件字典时，候选面向已发布字典，业务页沿用实际项目。</summary>
    [Theory]
    [InlineData(0,"int64")]
    [InlineData(0,"string")]
    [InlineData(1,"int64")]
    [InlineData(1,"string")]
    public Task Stored_global_multiselect_without_conditions_uses_published_candidate_range(int mode,string projectType)=>
        VerifyPublishedGlobalMultiselect(mode,1,false,projectType,unfiltered:true);
    /// <summary>全局声明的业务表按行项目隔离，仍可使用只配置固定条件或无条件的全局字典。</summary>
    [Theory]
    [InlineData(0,"int64",false)]
    [InlineData(0,"string",false)]
    [InlineData(1,"int64",false)]
    [InlineData(1,"string",false)]
    [InlineData(0,"int64",true)]
    [InlineData(0,"string",true)]
    [InlineData(1,"int64",true)]
    [InlineData(1,"string",true)]
    public Task Global_business_multiselect_uses_published_global_dictionary(int mode,string projectType,bool unfiltered)=>
        VerifyPublishedGlobalMultiselect(mode,1,false,projectType,unfiltered:unfiltered,globalSource:true);

    /// <summary>原多选必须执行字典页明确声明的当前项目约束，即使该页没有账号条件。</summary>
    [Theory]
    [InlineData(0,"int64",false)]
    [InlineData(0,"string",true)]
    [InlineData(1,"int64",true)]
    [InlineData(1,"string",false)]
    public Task Global_business_multiselect_uses_explicit_project_dictionary(int mode,string projectType,bool constants)=>
        VerifyPublishedGlobalMultiselect(mode,1,false,projectType,unfiltered:!constants,globalSource:true,projectScoped:true);

    /// <summary>实际非冗余字典按当前项目返回原多选候选及名称。</summary>
    [Theory]
    [InlineData(0,"int64",false)]
    [InlineData(0,"string",true)]
    [InlineData(1,"int64",true)]
    [InlineData(1,"string",false)]
    public Task Global_business_multiselect_uses_nonredundant_project_dictionary(int mode,string projectType,bool constants)=>
        VerifyPublishedGlobalMultiselect(mode,0,false,projectType,unfiltered:!constants,globalSource:true,projectScoped:true);

    /// <summary>非冗余全局多选字典在同一页限定项目及账号归属。</summary>
    [Theory]
    [InlineData(0,"int64",false,"store_id")]
    [InlineData(0,"string",true,"merchant_id")]
    [InlineData(1,"int64",true,"merchant_id")]
    [InlineData(1,"string",false,"store_id")]
    public Task Global_business_multiselect_uses_project_and_account_dictionary(int mode,string projectType,bool constants,string accountField)=>
        VerifyPublishedGlobalMultiselect(mode,0,false,projectType,unfiltered:!constants,globalSource:true,projectScoped:true,accountField:accountField);

    /// <summary>从实际生成程序集核对名称、关联选档、项目隔离和数据维护计划。</summary>
    private static async Task VerifyPublishedGlobalMultiselect(int mode,int targetStorage,bool derivedName,string? projectType,
        bool unfiltered=false,bool globalSource=false,bool projectScoped=false,string? accountField=null)
    {
        const string application="Example.GlobalTextDictionary";
        var names=derivedName?"global_source_global_target_names":"global_source_captions";
        var catalog=derivedName?GlobalMultipleDerivedTargetGenerationTests.Catalog(mode,false,"string"):PhysicalLabelCatalog(mode,false);
        catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=targetStorage;
        // 同一固定字典由原多选使用。独立复选不参与此样本，以免其发布逻辑替多选保留页面。
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "38" or "40");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "38" or "40");
        var dictionary=catalog.ConfigPages.Single(page=>page.Id=="10");
        dictionary.WhereSql=unfiltered?"":"status=17";
        if(projectScoped)dictionary.WhereSql=WithCurrentProject(dictionary.WhereSql);
        if(projectType is not null)ConfigureStoredGlobalDictionary(catalog,projectType);
        if(projectScoped&&targetStorage==0)MakeProjectDictionaryNonRedundant(catalog);
        if(globalSource)
        {
            catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.Single(column=>column.Id=="32").LogicalType=projectType!;
            // 页面未配置项目条件时，行项目隔离仍须从业务表归属和有效多选关系自动生成。
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="3"))
                page.WhereSql="";
            if(accountField is not null)ConfigureDictionaryAccountScope(catalog,accountField);
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,application)));
        }
        if(derivedName)
        {
            var source=catalog.ConfigColumns.Single(column=>column.Id=="34");
            source.ControlDetail=source.ControlDetail.Insert(1,"\"whereSql\":\"status = 17\",");
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-text-dictionary");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,application);
            var serviceCode=await File.ReadAllTextAsync(Path.Combine(output,"Services/CachedConsumerService.g.cs"));
            Assert.Contains("ImportMulti30_34Async",serviceCode);
            if(projectScoped)
                Assert.Contains("MultiDerivedPlan30_34",serviceCode);
            else
                Assert.Equal(!unfiltered,serviceCode.Contains("MultiPageCondition30_34",StringComparison.Ordinal));
            Assert.Contains("Export30Async",serviceCode);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            if(projectType is not null)
            {
                var model=GenerationModel.Create(catalog,application);
                var target=model.Tables.Single(table=>table.Source.Id=="1");
                if(targetStorage==1)
                    Assert.NotNull(FixedPageQueryCompilation.ManagedGlobalProject(target));
                else
                    Assert.True(NonRedundantReadCompilation.HasGlobalRowProject(target));
                Assert.Empty(GenerationCapabilityAudit.Collect(model));
                Assert.Contains("ImportMulti31_34Async",serviceCode);
                if(projectScoped)
                    Assert.Contains("MultiDerivedPlan31_34",serviceCode);
                else
                    Assert.Equal(!unfiltered,serviceCode.Contains("MultiPageCondition31_34",StringComparison.Ordinal));
                Assert.Contains("Export31Async",serviceCode);
                if(globalSource&&mode==1)
                {
                    var consumer=model.Tables.Single(table=>table.Source.Id=="3");
                    Assert.NotNull(FixedPageQueryCompilation.ManagedGlobalProject(consumer));
                    Assert.Contains("不能修改来源记录项目归属",serviceCode);
                }
                var targetCode=await File.ReadAllTextAsync(Path.Combine(output,"Services/GlobalSourceService.g.cs"));
                using var session=new DatabaseSession(new()
                {
                    Provider="mysql",ExpectedDatabase="offline",
                    ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"
                });
                var sql=NonRedundantGeneratedReadTests.CreateDictionarySql(assembly,application,"GlobalSource",session);
                var actor=DictionaryActor(catalog,accountField);
                if(globalSource&&mode==1)
                {
                    var business=NonRedundantGeneratedReadTests.CreateDictionarySql(assembly,application,"CachedConsumer",session);
                    Assert.Contains("yz_project_id",business("Query30",[new PageRequest(),actor]));
                    Assert.Contains("yz_project_id",business("Scope31",[actor,null]));
                }
                if(targetStorage==0)
                {
                    // 实际原多选分页和已选回读共用独立候选，普通详情仍按业务行查询。
                    var targetService=assembly.GetType(application+".Generated.Services.GlobalSourceService",true)!;
                    var candidate=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(targetService.GetField(
                        "CandidateReadPlan10",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                    candidate=BindDictionaryAccount(candidate,catalog,accountField);
                    var query=candidate.GetType().GetProperty("MySql",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(candidate)!;
                    AssertOwnedScope(query);
                    var scope=Assert.IsType<string>(query.GetType().GetProperty("Scope",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(query));
                    Assert.Contains("`yz_project_id`=@project_scope_"+(projectType=="string"?"text":"number"),scope);
                    Assert.Equal(!unfiltered,scope.Contains("`status`",StringComparison.Ordinal));
                    if(accountField is not null)Assert.Contains("`"+accountField+"`",scope);
                    Assert.Contains(accountField is null?"CandidateReadPlan10":"CandidateReadPlan10.BindAccount(actor)",targetCode);
                }
                else
                {
                    var list=sql("Query10",[new PageRequest(),actor]);
                    Assert.Equal(projectScoped,list.Contains("yz_project_id",StringComparison.Ordinal));
                    Assert.Equal(!unfiltered,list.Contains("status",StringComparison.Ordinal));
                    Assert.Contains("delete_no",list);
                    var selected=sql("Query10",[new PageRequest{SelectedIds=["9007199254740993"]},actor]);
                    Assert.Contains("`id`",selected);
                    Assert.Equal(!unfiltered,selected.Contains("status",StringComparison.Ordinal));
                    Assert.Equal(projectScoped,selected.Contains("yz_project_id",StringComparison.Ordinal));
                    Assert.Contains("yz_project_id",sql("Scope10",[actor,null]));
                    if(!projectScoped)
                        Assert.Contains("yz_project_id",sql("Query10",[new PageRequest(),actor,false]));
                    Assert.Contains("yz_project_id",sql("Query11",[new PageRequest(),actor]));
                    Assert.Contains("yz_project_id",sql("Query12",[new PageRequest(),actor]));
                    Assert.Contains("yz_project_id",sql("Query13",[new PageRequest(),actor]));
                    if(!projectScoped)
                        Assert.Contains("Query10(request, actor, candidateRows: false)",targetCode);
                    Assert.Contains("Query10(request, actor).CountAsync",targetCode);
                    Assert.Contains("Query10(new PageRequest { ParentId = request.ParentId, SelectedIds",targetCode);
                }
                var plan=FixedPageQueryCompilation.For(target,target.Pages.Single(page=>page.Source.Id=="10"));
                Assert.True(FixedPageQueryCompilation.TryProveSelection(model.Catalog,plan,new("10","caption","选择",dictionary.WhereSql,""),out _));
                Assert.Equal(projectScoped,FixedPageQueryCompilation.TryProveSelection(model.Catalog,plan,new("10","caption","选择",
                    "yz_project_id=@current_yz_project_id",""),out _));
                var package=new ApplicationPackager().Create(catalog,Path.Combine(directory,"application"),application,NonRedundantApplicationFixture.Root());
                Assert.Equal(ApplicationPackageMode.FullApplication,package.Mode);
            }
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            object read;
            if(mode==0)
            {
                var service=assembly.GetType(application+".Generated.Services.CachedConsumerService",true)!;
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField("ReadPlan30",
                    BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                plan=BindDictionaryAccount(plan,catalog,accountField);
                read=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                var filters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                    read.GetType().GetProperty("ArrayNamePredicates",hidden)!.GetValue(read));
                Assert.Equal(!unfiltered,filters[names].Contains("multi_value.`status`",StringComparison.Ordinal));
            }
            else
            {
                var generated=assembly.GetType(application+".Generated.GeneratedRelationMaintenance",true)!;
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    generated.GetProperty("Catalog")!.GetValue(null));
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
                var plan=plans["3"]!;
                Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
                read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
                if(derivedName||projectScoped)
                {
                    Assert.Contains(names,maintenance.MixedSourceColumns["cached_consumer"]);
                    Assert.Contains("global_source",maintenance.MixedTargets);
                }
                else
                {
                    var propagation=maintenance.GetType().GetProperty("Propagation",hidden)!.GetValue(maintenance)!;
                    var bindings=Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                        propagation.GetType().GetProperty("Bindings",hidden)!.GetValue(propagation)).Cast<object>();
                    var binding=Assert.Single(bindings,item=>Equals(names,item.GetType().GetProperty("Cache")!.GetValue(item)));
                    if(globalSource)
                    {
                        var consumer=binding.GetType().GetProperty("Source")!.GetValue(binding)!;
                        var target=binding.GetType().GetProperty("Target")!.GetValue(binding)!;
                        Assert.Equal(projectType,consumer.GetType().GetProperty("ProjectType")!.GetValue(consumer));
                        Assert.Equal("",target.GetType().GetProperty("ProjectType")!.GetValue(target));
                        var declaration=assembly.GetType(application+".Generated.GeneratedRelationMutation",true)!;
                        Assert.IsType<Metarill.Data.Relations.RelationMutationPlan>(
                            declaration.GetField("Plan",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                    }
                    var condition=binding.GetType().GetProperty("PublishedCondition")!.GetValue(binding);
                    if(unfiltered)Assert.Null(condition);
                    else Assert.Equal("status=17",condition?.GetType().GetProperty("Definition")!.GetValue(condition));
                }
            }
            if(globalSource)
            {
                var scope=Assert.IsType<string>(read.GetType().GetProperty("Scope",hidden)!.GetValue(read));
                Assert.Contains("`yz_project_id`",scope);
                var projectPredicate="multi_value.`yz_project_id`=@project_scope_"+(projectType=="string"?"text":"number");
                Assert.Equal(projectScoped,CheckboxNameExpression(read,names).Contains(projectPredicate,StringComparison.Ordinal));
                var service=assembly.GetType(application+".Generated.Services.CachedConsumerService",true)!;
                var validate=service.GetMethod("ValidateActor",BindingFlags.Static|BindingFlags.NonPublic)!;
                var otherProject=new RuntimeActor(7,"合成账号",false,"999999","9");
                Assert.IsType<UnauthorizedAccessException>(
                    Assert.Throws<TargetInvocationException>(()=>validate.Invoke(null,[otherProject])).InnerException);
            }
            if(projectScoped)
            {
                var service=assembly.GetType(application+".Generated.Services.CachedConsumerService",true)!;
                var lookup=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField("MultiDerivedPlan30_34",
                    BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                lookup=BindDictionaryAccount(lookup,catalog,accountField);
                var compiled=lookup.GetType().GetProperty("MySql",hidden)!.GetValue(lookup)!;
                AssertOwnedScope(compiled);
            }
            var expression=CheckboxNameExpression(read,names);
            Assert.Equal(!unfiltered,expression.Contains("multi_value.`status`",StringComparison.Ordinal));
            AssertDictionaryNameAccountScope(expression,mode,accountField);
            if(mode==1&&accountField is not null)AssertDictionaryCacheWithoutOwnershipIsRejected(catalog,application,"34");
            var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                read.GetType().GetProperty("Constants",hidden)!.GetValue(read));
            Assert.Equal(!unfiltered,constants.Values.Contains(17));
            if(!globalSource&&unfiltered&&mode==0&&projectType=="int64")VerifyUnfilteredMultiselectPublication(catalog,application);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>字典本身也可维护按项目存储的行；额外页面检验独立项目条件和两种原 Excel 入口。</summary>
    private static void ConfigureStoredGlobalDictionary(MetadataCatalog catalog,string projectType)
    {
        catalog.ConfigColumns.Add(new()
        {
            Id="17",ConfigTableId="1",YzProjectId=catalog.YzProjectId,ColumnName="yz_project_id",LogicalType=projectType
        });
        catalog.ConfigColumns.Single(column=>column.Id=="13").ControlKind="SelectPage";
        foreach(var (id,type,where) in new[]
        {
            ("11",0,""),
            ("12",2,"status=17 AND yz_project_id=@current_yz_project_id"),
            ("13",2,"")
        })
        {
            catalog.ConfigPages.Add(new()
            {
                Id=id,ConfigTableId="1",YzAdminTypeId="9",YzProjectId=catalog.YzProjectId,WhereSql=where,IsSave=1,
                Extra=new(){{"type",JsonSerializer.SerializeToElement(type)}}
            });
            catalog.ConfigPageColumns.Add(new()
            {
                Id=id+"16",ConfigPageId=id,ConfigColumnId="16",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=1
            });
        }
        catalog.ConfigColumns.Single(column=>column.Id=="36").SortNo=-10;
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="30"&&field.ConfigColumnId=="36").SortNo=-10;
        var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="30")))!;
        overwrite.Id="31";
        overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        overwrite.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        catalog.ConfigPages.Add(overwrite);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="30").ToArray())
        {
            var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            copy.Id="31"+field.ConfigColumnId;
            copy.ConfigPageId="31";
            catalog.ConfigPageColumns.Add(copy);
        }
    }

    /// <summary>真实详情引用才能发布无条件候选，账号项目限制和错误目标均不得放宽范围。</summary>
    private static void VerifyUnfilteredMultiselectPublication(MetadataCatalog catalog,string application)
    {
        var reference=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="30"&&field.ConfigColumnId=="34");
        reference.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"whereSql":"yz_project_id=@current_yz_project_id"}""");
        Assert.False(Published());
        reference.Extra.Remove("controlDetail");
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigColumnId=="34"))field.IsEdit=0;
        Assert.False(Published());

        catalog.ConfigTables.Add(new()
        {
            Id="4",TableName="cached_consumer_form",YzProjectId=catalog.YzProjectId,IsFunctionTable=1
        });
        catalog.ConfigPages.Add(new(){Id="40",ConfigTableId="4",YzAdminTypeId="9",YzProjectId=catalog.YzProjectId});
        var selector=new ColumnMetadata
        {
            Id="41",ConfigTableId="4",YzProjectId=catalog.YzProjectId,ColumnName="review_ids",LogicalType="string",
            IsVirtualColumn=1,ControlKind="MultiSelectPage",ControlDetail="""{"pageId":"10","nameField":"caption"}"""
        };
        catalog.ConfigColumns.Add(selector);
        catalog.ConfigPageColumns.Add(new()
        {
            Id="4041",ConfigPageId="40",ConfigColumnId="41",YzProjectId=catalog.YzProjectId,IsEdit=1
        });
        Assert.True(Published());
        selector.LinkConfigTableId="2";
        Assert.False(Published());

        bool Published()
        {
            var model=GenerationModel.Create(catalog,application);
            var dictionary=model.Tables.Single(table=>table.Source.Id=="1");
            return FixedPageQueryCompilation.UsesStoredGlobalSelectionRows(model.Catalog,dictionary,
                dictionary.Pages.Single(page=>page.Source.Id=="10"));
        }
    }
    /// <summary>实际复选候选页可以是普通字典页，账号归属、搜索、Excel 和持久名称用同一发布条件。</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(0, true, true)]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(1, false, true)]
    [InlineData(0, true, false, true)]
    [InlineData(0, false, true, true)]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, true, true)]
    [InlineData(1, false, false, true, true)]
    [InlineData(1, true, true, true, true)]
    [InlineData(0, true, false, true, false, true)]
    [InlineData(0, false, true, true, false, true)]
    [InlineData(1, false, false, true, false, true)]
    [InlineData(1, true, true, true, false, true)]
    public async Task Checkbox_with_stored_global_account_dictionary(int mode,bool explicitPage,bool globalSource=false,bool alternate=false,
        bool projectBound=false,bool pageProject=false)
    {
        var catalog = PhysicalLabelCatalog(mode,false);
        if(globalSource)
            catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column => column.Id is "34" or "35");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "34" or "35");
        if(projectBound||pageProject)
        {
            // 项目过滤样本是物理字典，去掉基类夹具从另一字典派生的下拉缓存。
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "13" or "14");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "13" or "14");
            var relation=catalog.ConfigColumns.Single(column=>column.Id=="38");
            if(projectBound)
                relation.ControlDetail=relation.ControlDetail.Insert(1,"\"whereSql\":\"yz_project_id=@current_yz_project_id\",");
            catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",
                LogicalType=globalSource?"string":"int64"});
        }
        var dictionary = catalog.ConfigPages.Single(page => page.Id == "10");
        dictionary.Extra.Remove("type");
        dictionary.WhereSql = "status=1 AND store_id=@current_store_id";
        if(pageProject)dictionary.WhereSql+=" AND yz_project_id=@current_yz_project_id";
        var business = catalog.ConfigPages.Single(page => page.Id == "30");
        business.WhereSql += " AND store_id=@current_store_id";
        if(!explicitPage)
        {
            var source=catalog.ConfigColumns.Single(column=>column.Id=="38");
            source.ControlDetail=source.ControlDetail.Replace("\"pageId\":\"10\",","",StringComparison.Ordinal);
            source.Extra.Remove("linkConfigPageId");
            Assert.Empty(DatabaseCheckboxDefinition.Parse(source.ControlDetail).PageId);
            // 覆盖模式按表级首列匹配。先放可比较的业务名称，再导入复选数组。
            catalog.ConfigColumns.Single(column=>column.Id=="36").SortNo=-10;
            catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="36").SortNo=-10;
            business.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            business.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        }
        catalog.ConfigColumns.AddRange([
            new() { Id="17", ConfigTableId="1", YzProjectId="100", ColumnName="store_id", LogicalType="int64" },
            new() { Id="37", ConfigTableId="3", YzProjectId="100", ColumnName="store_id", LogicalType="int64" }
        ]);
        if(alternate)
        {
            catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="short_caption",
                Name="简称",LogicalType="string",ColumnLength="100"});
            catalog.ConfigPageColumns.Add(new(){Id="1018",ConfigColumnId="18",ConfigPageId="10",YzProjectId="100",IsShow=1,IsEdit=0});
            var pageWithShortName=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(business))!;
            pageWithShortName.Id="31";
            catalog.ConfigPages.Add(pageWithShortName);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="30").ToArray())
            {
                var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                copy.Id="31"+field.ConfigColumnId;
                copy.ConfigPageId="31";
                if(copy.ConfigColumnId=="38")
                    copy.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"short_caption"}""");
                if(mode==1&&copy.ConfigColumnId=="40")continue;
                catalog.ConfigPageColumns.Add(copy);
            }
            if(mode==1)
            {
                catalog.ConfigColumns.Add(new(){Id="41",ConfigTableId="3",YzProjectId="100",ColumnName="selected_global_source_short_captions",
                    LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                        ["columnFullName"]=JsonSerializer.SerializeToElement("selected_global_source.short_caption")}});
                catalog.ConfigPageColumns.Add(new(){Id="3141",ConfigPageId="31",ConfigColumnId="41",YzProjectId="100",IsShow=1});
            }
        }

        const string application = "Example.GlobalStoredCheckbox";
        var model = GenerationModel.Create(catalog,application);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = owner.Pages.Single(page=>page.Source.Id=="30");
        var checkbox = Assert.Single(DatabaseCheckboxCompilation.ForService(model,owner,page));
        Assert.Equal("10",checkbox.GlobalPage?.Source.Id);
        Assert.Equal("16",checkbox.Name.Source.Id);
        Assert.Contains(page.Searchable,column=>column.Source.Id=="40");
        Assert.False(checkbox.DerivedName);
        if(alternate)
        {
            var second=owner.Pages.Single(candidate=>candidate.Source.Id=="31");
            var label=Assert.Single(DatabaseCheckboxCompilation.ForService(model,owner,second));
            Assert.Equal("short_caption",label.Name.Source.ColumnName);
            Assert.Equal("10",label.GlobalPage?.Source.Id);

            // 简称必须在实际字典页发布，页面覆盖不能借用未公开的字段。
            var labelField=catalog.ConfigPageColumns.Single(field=>field.Id=="1018");
            labelField.IsShow=0;
            try
            {
                var rejected=Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
                Assert.Contains("已开放该字段",rejected.Message);
            }
            finally { labelField.IsShow=1; }
        }

        var boundary = Path.Combine(Path.GetTempPath(),"metarill-global-stored-checkbox");
        var directory = Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,application);
            var service = await File.ReadAllTextAsync(Path.Combine(output,"Services/CachedConsumerService.g.cs"));
            Assert.Contains("CheckboxGlobalCondition30_38.BindAccount(actor)",service);
            Assert.Contains("CheckboxOptions30_38Async",service);
            Assert.Contains("ImportCheckbox30_38Async",service);
            Assert.Contains("Export30Async",service);
            if(projectBound||pageProject)
                Assert.Contains(globalSource?"query=query.Where(row=>row.YzProjectId==actor.ProjectId);":
                    "query=query.Where(row=>row.YzProjectId==long.Parse(actor.ProjectId,CultureInfo.InvariantCulture));",service);
            Assert.Contains(mode==0?"FixedReadComparison.JsonArrayAnyContains":"ApplyJsonArrayAnyLike",service);
            if(alternate)
            {
                Assert.Contains("CheckboxGlobalCondition31_38.BindAccount(actor)",service);
                Assert.Contains("CheckboxOptions31_38Async",service);
                Assert.Contains("ImportCheckbox31_38Async",service);
                Assert.Contains("Export31Async",service);
                if(mode==0)
                    Assert.Contains("row.ShortCaption==name",service);
                else
                {
                    Assert.Contains("entity.SelectedGlobalSourceCaptions=entity.SelectedGlobalSourceIds",service);
                    Assert.Contains("entity.SelectedGlobalSourceShortCaptions=entity.SelectedGlobalSourceIds",service);
                }
            }
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var generated = assembly.GetType(application+".Generated.Services.CachedConsumerService",throwOnError:true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(generated.TypeHandle);
            const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
            if(globalSource)
            {
                var wrongProject=new Metarill.Core.Security.RuntimeActor(7,"合成账号",false,"999","9"){StoreId=9007199254740993L};
                var validate=generated.GetMethod("ValidateActor",hidden)!;
                Assert.IsType<UnauthorizedAccessException>(
                    Assert.Throws<TargetInvocationException>(()=>validate.Invoke(null,[wrongProject])).InnerException);
                if(mode==1)
                {
                    Assert.NotNull(FixedPageQueryCompilation.ManagedGlobalProject(owner));
                    Assert.Contains("entity.YzProjectId = ",service);
                    Assert.Contains("不能修改来源记录项目归属",service);
                }
            }
            if(mode==0)
            {
                var read=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(generated.GetField("ReadPlan30",hidden)!.GetValue(null));
                var actor=new Metarill.Core.Security.RuntimeActor(7,"合成账号",false,"100","9"){StoreId=9007199254740993L};
                Assert.True(read.RequiresAccount);
                var bound=read.BindAccount(actor);
                Assert.Equal("string",bound.OutputTypes["selected_global_source_captions"]);
                if(alternate)
                {
                    var second=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(generated.GetField("ReadPlan31",hidden)!.GetValue(null));
                    Assert.True(second.RequiresAccount);
                    var selected=second.BindAccount(actor);
                    var mysql=selected.GetType().GetProperty("MySql",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(selected)!;
                    var expression=CheckboxNameExpression(mysql,"selected_global_source_captions");
                    Assert.Contains("`short_caption`",expression);
                    Assert.DoesNotContain("`caption`",expression);
                    if(pageProject)
                        Assert.Contains("multi_value.`yz_project_id`=@project_scope_"+(globalSource?"text":"number"),expression);
                }
                if(globalSource)
                {
                    Assert.Equal("string",bound.OutputTypes["attachments"]);
                    var mysql=bound.GetType().GetProperty("MySql",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(bound)!;
                    AssertOwnedScope(mysql);
                    var businessScope=business.WhereSql;
                    try
                    {
                        business.WhereSql="store_id=@current_store_id";
                        // 同页标量候选能够编译，只有业务数组和附件被候选用途拒绝。
                        Assert.Equal("string",Metarill.Data.Queries.NonRedundantReads.CompileGlobalCandidate(
                            catalog,owner.Source.Id,business.Id,["name"]).OutputTypes["name"]);
                        foreach(var column in new[]{"selected_global_source_ids","selected_global_source_captions","attachments"})
                            Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalCandidate(
                                catalog,owner.Source.Id,business.Id,[column]));
                    }
                    finally { business.WhereSql=businessScope; }
                }
                Assert.Throws<UnauthorizedAccessException>(()=>read.BindAccount(actor with{StoreId=null}));
            }
            else
            {
                var maintenance=MixedRelationPropagationCompilation.ForModel(model);
                Assert.Contains("selected_global_source_captions",maintenance.MixedSourceColumns["cached_consumer"]);
                Assert.Contains("global_source",maintenance.MixedTargets);
                var scoped=RelationPropagationCompilation.DerivedBindings(model).Where(binding=>binding.Foreign.Source.Id=="38").ToArray();
                Assert.Equal(alternate?new[]{"caption","short_caption"}:new[]{"caption"},
                    scoped.Select(binding=>binding.Value.Source.ColumnName).Order(StringComparer.Ordinal));
                Assert.All(scoped,binding=>Assert.True(binding.Multiple));
                if(globalSource||alternate)
                {
                    var generatedMaintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                        assembly.GetType(application+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                    var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                        generatedMaintenance.GetType().GetProperty("Plans",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(generatedMaintenance));
                    var ownerPlan=plans[owner.Source.Id]!;
                    Assert.Null(ownerPlan.GetType().GetProperty("Error")!.GetValue(ownerPlan));
                    var read=ownerPlan.GetType().GetProperty("Read")!.GetValue(ownerPlan)!;
                    AssertOwnedScope(read);
                    if(alternate)
                    {
                        Assert.Contains("selected_global_source_short_captions",generatedMaintenance.MixedSourceColumns["cached_consumer"]);
                        var shortName=CheckboxNameExpression(read,"selected_global_source_short_captions");
                        var fullName=CheckboxNameExpression(read,"selected_global_source_captions");
                        Assert.Contains("`short_caption`",shortName);
                        Assert.Contains("`caption`",fullName);
                        var projectPredicate="multi_value.`yz_project_id`=@project_scope_"+(globalSource?"text":"number");
                        Assert.Equal(projectBound||pageProject,shortName.Contains(projectPredicate,StringComparison.Ordinal));
                        Assert.Equal(projectBound||pageProject,fullName.Contains(projectPredicate,StringComparison.Ordinal));
                    }
                }

                business.WhereSql=business.WhereSql.Replace(" AND store_id=@current_store_id","",StringComparison.Ordinal);
                // 发布不得将账号的门店值当作所有持久来源行的门店。
                var missing=GenerationModel.Create(catalog,application);
                var error=Assert.Throws<InvalidOperationException>(()=>RelationPropagationCompilation.DerivedBindings(missing));
                Assert.Contains("物理账号归属",error.Message);
            }
        }
        finally
        {
            if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);
        }
    }

    /// <summary>全局业务复选的固定字典、实时名称及两份物理副本共用实际发布范围。</summary>
    [Theory]
    [InlineData(0,"int64",false)]
    [InlineData(0,"string",false)]
    [InlineData(1,"int64",false)]
    [InlineData(1,"string",false)]
    [InlineData(0,"int64",true)]
    [InlineData(0,"string",true)]
    [InlineData(1,"int64",true)]
    [InlineData(1,"string",true)]
    public Task Global_business_checkbox_uses_static_global_dictionary(int mode,string projectType,bool unfiltered)=>
        VerifyGlobalBusinessCheckboxDictionary(mode,projectType,unfiltered);

    /// <summary>原数据库复选和两份名称副本使用字典页发布的当前项目范围。</summary>
    [Theory]
    [InlineData(0,"int64",true)]
    [InlineData(0,"string",false)]
    [InlineData(1,"int64",false)]
    [InlineData(1,"string",true)]
    public Task Global_business_checkbox_uses_explicit_project_dictionary(int mode,string projectType,bool constants)=>
        VerifyGlobalBusinessCheckboxDictionary(mode,projectType,!constants,projectScoped:true);

    /// <summary>原数据库复选从非冗余字典发布页读取全称和简称，并保留当前项目约束。</summary>
    [Theory]
    [InlineData(0,"int64",true)]
    [InlineData(0,"string",false)]
    [InlineData(1,"int64",false)]
    [InlineData(1,"string",true)]
    public Task Global_business_checkbox_uses_nonredundant_project_dictionary(int mode,string projectType,bool constants)=>
        VerifyGlobalBusinessCheckboxDictionary(mode,projectType,!constants,projectScoped:true,targetStorage:0);

    /// <summary>原数据库复选的项目和账号条件用于候选、实时名称及两份持久名称副本。</summary>
    [Theory]
    [InlineData(0,"int64",true,"merchant_id")]
    [InlineData(0,"string",false,"store_id")]
    [InlineData(1,"int64",false,"store_id")]
    [InlineData(1,"string",true,"merchant_id")]
    public Task Global_business_checkbox_uses_project_and_account_dictionary(int mode,string projectType,bool constants,string accountField)=>
        VerifyGlobalBusinessCheckboxDictionary(mode,projectType,!constants,projectScoped:true,targetStorage:0,accountField:accountField);

    /// <summary>从同一实际生成代码验证字典候选、原筛选及非冗余和冗余名称。</summary>
    private static async Task VerifyGlobalBusinessCheckboxDictionary(int mode,string projectType,bool unfiltered,
        bool projectScoped=false,int targetStorage=1,string? accountField=null)
    {
        const string app="Example.StaticGlobalCheckbox";
        var catalog=PhysicalLabelCatalog(mode,false);
        catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "34" or "35");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "34" or "35");
        catalog.ConfigColumns.Single(column=>column.Id=="32").LogicalType=projectType;
        catalog.ConfigColumns.Add(new(){Id="17",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType=projectType});
        catalog.ConfigColumns.Single(column=>column.Id=="13").ControlKind="SelectPage";
        if(targetStorage==0)MakeProjectDictionaryNonRedundant(catalog);
        catalog.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="short_caption",LogicalType="string"});
        catalog.ConfigPageColumns.Add(new(){Id="1018",ConfigPageId="10",ConfigColumnId="18",YzProjectId="100",IsShow=1});
        var dictionary=catalog.ConfigPages.Single(page=>page.Id=="10");
        dictionary.Extra.Remove("type");
        dictionary.WhereSql=unfiltered?"":"status=17";
        if(projectScoped)dictionary.WhereSql=WithCurrentProject(dictionary.WhereSql);
        var source=catalog.ConfigColumns.Single(column=>column.Id=="38");
        catalog.ConfigPageColumns.Single(field=>field.Id=="3038").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(!unfiltered)
            source.Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");
        if(mode==1&&!unfiltered&&projectType=="int64")
            source.ControlDetail=source.ControlDetail.Insert(1,"\"whereSql\":\"status = 17\",");
        if(unfiltered)
        {
            // 省略页面时仍须绑定唯一发布页，不能从其他页面借用名称。
            source.ControlDetail=source.ControlDetail.Replace("\"pageId\":\"10\",","",StringComparison.Ordinal);
            source.Extra.Remove("linkConfigPageId");
            Assert.Empty(DatabaseCheckboxDefinition.Parse(source.ControlDetail).PageId);
        }
        var business=catalog.ConfigPages.Single(page=>page.Id=="30");
        business.WhereSql="";
        catalog.ConfigColumns.Single(column=>column.Id=="36").SortNo=-10;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="36").SortNo=-10;
        var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(business))!;
        overwrite.Id="31";
        overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        overwrite.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        catalog.ConfigPages.Add(overwrite);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="30").ToArray())
        {
            if(mode==1&&field.ConfigColumnId=="40")continue;
            var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            copy.Id="31"+copy.ConfigColumnId;
            copy.ConfigPageId="31";
            if(copy.ConfigColumnId=="38")
                copy.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"short_caption"}""");
            catalog.ConfigPageColumns.Add(copy);
        }
        if(mode==1)
        {
            catalog.ConfigColumns.Add(new(){Id="41",ConfigTableId="3",YzProjectId="100",ColumnName="selected_global_source_short_captions",
                LogicalType="string",ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("selected_global_source.short_caption")}});
            catalog.ConfigPageColumns.Add(new(){Id="3141",ConfigPageId="31",ConfigColumnId="41",YzProjectId="100",IsShow=1});
        }

        if(accountField is not null)ConfigureDictionaryAccountScope(catalog,accountField);
        var model=GenerationModel.Create(catalog,app);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var consumer=model.Tables.Single(item=>item.Source.Id=="3");
        var vue=OriginalUniappListRenderer.Render(model,consumer,consumer.Pages.Single(page=>page.Source.Id=="30"));
        Assert.Contains("/admin/auto/cached_consumer/30/findNameIdSelectedGlobalSource38",vue);
        if(mode==1&&!unfiltered&&projectType=="int64")
        {
            var pageField=catalog.ConfigPageColumns.Single(field=>field.Id=="3138");
            var original=pageField.Extra["controlDetail"];
            pageField.Extra["controlDetail"]=JsonSerializer.SerializeToElement("""{"nameField":"short_caption","whereSql":"status=18"}""");
            Assert.Contains(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,app)),gap=>
                gap.PageId=="31"&&gap.Reason.Contains("实际发布字典页覆盖",StringComparison.Ordinal));
            pageField.Extra["controlDetail"]=original;
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-static-global-checkbox");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,app);
            var code=await File.ReadAllTextAsync(Path.Combine(output,"Services/CachedConsumerService.g.cs"));
            foreach(var id in new[]{"30","31"})
            {
                Assert.Contains($"CheckboxOptions{id}_38Async",code);
                Assert.Contains($"ImportCheckbox{id}_38Async",code);
                Assert.Contains($"Export{id}Async",code);
            }
            Assert.Equal(accountField is not null,code.Contains("CheckboxGlobalCondition30_38.BindAccount(actor)",StringComparison.Ordinal));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(app+".Generated.Services.CachedConsumerService",true)!;
            const BindingFlags hidden=BindingFlags.NonPublic;
            object MySql(string field)
            {
                var read=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField(field,BindingFlags.Static|hidden)!.GetValue(null));
                read=BindDictionaryAccount(read,catalog,accountField);
                return read.GetType().GetProperty("MySql",BindingFlags.Instance|hidden)!.GetValue(read)!;
            }
            var wrong=new RuntimeActor(7,"合成账号",false,"999","9");
            var validate=service.GetMethod("ValidateActor",BindingFlags.Static|hidden)!;
            Assert.IsType<UnauthorizedAccessException>(Assert.Throws<TargetInvocationException>(()=>validate.Invoke(null,[wrong])).InnerException);
            // 直接执行已生成的候选查询构造；离线测试只生成 SQL，禁止数据库连接。
            var actor=DictionaryActor(catalog,accountField);
            using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,
                ()=>throw new InvalidOperationException("离线验收不连接数据库。")).UseAutoSyncStructure(false).Build();
            using var session=new DatabaseSession(new()
            {
                Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Database=offline;User ID=unused"
            },orm);
            var mapper=Activator.CreateInstance(assembly.GetType(app+".Generated.Mappers.CachedConsumerMapper",true)!,session);
            var instance=Activator.CreateInstance(service,session,mapper,new SnowflakeIdGenerator(),
                Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(),null,new CascadeTreeReadTests.TreeListOnly(actor))!;
            var candidate=service.GetMethod("CheckboxScope30_38Async",BindingFlags.Instance|hidden)!;
            var task=Assert.IsAssignableFrom<Task>(candidate.Invoke(instance,[null,actor,new Dictionary<string,object?>(),false,CancellationToken.None]));
            await task;
            var query=task.GetType().GetProperty("Result")!.GetValue(task);
            var sql=Assert.IsType<string>(typeof(GlobalMultipleCachedTargetGenerationTests).GetMethod(nameof(CheckboxSql),BindingFlags.Static|hidden)!
                .MakeGenericMethod(assembly.GetType(app+".Generated.Entities.GlobalSource",true)!).Invoke(null,[query]));
            Assert.Contains("`global_source`",sql);
            Assert.Contains("`delete_no`",sql);
            Assert.Equal(!unfiltered,sql.Contains("`status`",StringComparison.Ordinal));
            Assert.Equal(projectScoped,sql.Contains("`yz_project_id`",StringComparison.Ordinal));
            if(accountField is not null)Assert.Contains("`"+accountField+"`",sql);
            const string fullNames="selected_global_source_captions";
            const string shortNames="selected_global_source_short_captions";
            void CheckName(object read,string outputName,string field)
            {
                AssertOwnedScope(read);
                var expression=CheckboxNameExpression(read,outputName);
                Assert.Contains("`"+field+"`",expression);
                Assert.Equal(!unfiltered,expression.Contains("multi_value.`status`",StringComparison.Ordinal));
                var projectPredicate="multi_value.`yz_project_id`=@project_scope_"+(projectType=="string"?"text":"number");
                Assert.Equal(projectScoped,expression.Contains(projectPredicate,StringComparison.Ordinal));
                AssertDictionaryNameAccountScope(expression,mode,accountField);
            }
            if(mode==0)
            {
                CheckName(MySql("ReadPlan30"),fullNames,"caption");
                CheckName(MySql("ReadPlan31"),fullNames,"short_caption");
            }
            else
            {
                Assert.Contains("不能修改来源记录项目归属",code);
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(app+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                if(projectScoped)
                {
                    Assert.Contains(fullNames,maintenance.MixedSourceColumns["cached_consumer"]);
                    Assert.Contains(shortNames,maintenance.MixedSourceColumns["cached_consumer"]);
                    Assert.Contains("global_source",maintenance.MixedTargets);
                }
                else
                {
                    var propagation=maintenance.GetType().GetProperty("Propagation",BindingFlags.Instance|hidden)!.GetValue(maintenance)!;
                    var bindings=Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                        propagation.GetType().GetProperty("Bindings",BindingFlags.Instance|hidden)!.GetValue(propagation));
                    Assert.Equal(2,bindings.Cast<object>().Count(binding=>Equals(fullNames,binding.GetType().GetProperty("Cache")!.GetValue(binding))||
                        Equals(shortNames,binding.GetType().GetProperty("Cache")!.GetValue(binding))));
                }
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans",BindingFlags.Instance|hidden)!.GetValue(maintenance));
                var plan=plans["3"]!;
                Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
                var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
                CheckName(read,fullNames,"caption");
                CheckName(read,shortNames,"short_caption");
                if(accountField is not null)AssertDictionaryCacheWithoutOwnershipIsRejected(catalog,app,"38");
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory,boundary); }
    }

    /// <summary>可重算的来源页一致绑定物理归属。候选目标另行保留真实项目条件。</summary>
    private static void ConfigureDictionaryAccountScope(MetadataCatalog catalog,string field)
    {
        var condition=field+"=@current_"+field;
        catalog.ConfigColumns.Add(new(){Id="19",ConfigTableId="1",YzProjectId=catalog.YzProjectId,ColumnName=field,LogicalType="int64"});
        catalog.ConfigColumns.Add(new(){Id="42",ConfigTableId="3",YzProjectId=catalog.YzProjectId,ColumnName=field,LogicalType="int64"});
        catalog.ConfigPages.Single(page=>page.Id=="10").WhereSql+=" AND "+condition;
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="3"))
            page.WhereSql=condition;
    }

    /// <summary>使用高位账号归属 ID 同时证明绑定和精度。</summary>
    private static RuntimeActor DictionaryActor(MetadataCatalog catalog,string? accountField)=>
        new(7,"合成账号",false,catalog.YzProjectId,"9")
        {
            StoreId=accountField=="store_id"?9007199254740993L:null,
            MerchantId=accountField=="merchant_id"?9007199254740995L:null
        };

    /// <summary>读取时要求身份提供实际归属，缺少该归属的账号必须被拒绝。</summary>
    private static Metarill.Data.Queries.NonRedundantReadPlan BindDictionaryAccount(
        Metarill.Data.Queries.NonRedundantReadPlan read,MetadataCatalog catalog,string? accountField)
    {
        Assert.Equal(accountField is not null,read.RequiresAccount);
        if(accountField is null)return read;
        Assert.Throws<UnauthorizedAccessException>(()=>read.BindAccount(DictionaryActor(catalog,null)));
        var bound=read.BindAccount(DictionaryActor(catalog,accountField));
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var mysql=bound.GetType().GetProperty("MySql",hidden)!.GetValue(bound)!;
        var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
            mysql.GetType().GetProperty("Constants",hidden)!.GetValue(mysql));
        Assert.Contains(accountField=="store_id"?9007199254740993L:9007199254740995L,constants.Values);
        return bound;
    }

    /// <summary>任一实际来源页缺少同一物理归属时，缓存发布必须失败。</summary>
    private static void AssertDictionaryCacheWithoutOwnershipIsRejected(MetadataCatalog catalog,string app,string relation)
    {
        var bindings=RelationPropagationCompilation.DerivedBindings(GenerationModel.Create(catalog,app))
            .Where(binding=>binding.Foreign.Source.Id==relation).ToArray();
        Assert.NotEmpty(bindings);
        var definitions=RelationPropagationCompilation.Definitions(bindings);
        var page=catalog.ConfigPages.Single(item=>item.Id=="31");
        var published=page.WhereSql;
        try
        {
            page.WhereSql="";
            var error=Assert.Throws<InvalidOperationException>(()=>Metarill.Data.Relations.DerivedCrossTableCaches.Validate(catalog,definitions,[]));
            Assert.Contains("所有来源页一致的物理账号归属",error.Message);
        }
        finally { page.WhereSql=published; }
    }

    /// <summary>实时名称绑定认证参数，持久名称从实际来源记录重算。</summary>
    private static void AssertDictionaryNameAccountScope(string expression,int mode,string? field)
    {
        if(field is null)return;
        Assert.Contains("multi_value.`"+field+"`",expression);
        if(mode==1)Assert.Contains("CAST(b.`"+field+"` AS CHAR)",expression);
    }

    /// <summary>保留物理字典字段，移除基类样本与本次范围无关的冗余名称副本。</summary>
    private static void MakeProjectDictionaryNonRedundant(MetadataCatalog catalog)
    {
        catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=0;
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "13" or "14");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "13" or "14");
    }

    /// <summary>在测试字典页声明原项目变量；固定值继续独立验证。</summary>
    private static string WithCurrentProject(string conditions)=>
        "yz_project_id=@current_yz_project_id"+(conditions.Length==0?"":" AND "+conditions);

    /// <summary>检查已生成的 FreeSql 查询而不连接数据库。</summary>
    private static string CheckboxSql<T>(object query) where T:class=>((FreeSql.ISelect<T>)query).ToSql("1");

    /// <summary>核对实际生成的业务或维护计划仍以物理项目列作根范围。</summary>
    private static void AssertOwnedScope(object mysql) =>
        Assert.Contains("`yz_project_id`",Assert.IsType<string>(
            mysql.GetType().GetProperty("Scope",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mysql)));

    /// <summary>从实际生成的名称表达式核对最终使用的物理字典列。</summary>
    private static string CheckboxNameExpression(object mysql,string name) =>
        Assert.IsType<string>(mysql.GetType().GetMethod("Expression",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(mysql,[name]));

    /// <summary>普通项目复选支持已声明名称，真实项目谓词不能被名称配置替代。</summary>
    [Theory][InlineData(0,false)][InlineData(0,true)][InlineData(1,false)][InlineData(1,true)]
    public void Custom_non_global_checkbox_label_requires_real_project_scope(int mode,bool automatic)
    {
        var catalog = PhysicalLabelCatalog(mode,automatic);
        catalog.ConfigTables.Single(table => table.Id == "1").Extra.Clear();
        var project = new ColumnMetadata { Id = "17", ConfigTableId = "1", YzProjectId = "100", ColumnName = "yz_project_id", LogicalType = "int64" };
        catalog.ConfigColumns.Add(project);
        const string application = "Example.PrivatePhysicalLabel";
        var model = GenerationModel.Create(catalog,application);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = Assert.Single(owner.Pages);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        Assert.Contains("row.YzProjectId==long.Parse(actor.ProjectId",DatabaseCheckboxRenderer.Service(model,owner,page));

        catalog.ConfigColumns.Remove(project);
        var unscoped = GenerationModel.Create(catalog,application);
        var unscopedOwner = unscoped.Tables.Single(table => table.Source.Id == "3");
        var foreign = unscopedOwner.Columns.Single(column => column.Source.Id == "38");
        var rejected = DatabaseCheckboxCompilation.Resolve(unscoped,unscopedOwner,Assert.Single(unscopedOwner.Pages),foreign);
        Assert.Null(rejected.Definition);
        Assert.Contains("目标缺少真实项目归属",rejected.Reason);
    }
    /// <summary>原全局生产链完整保留，只新增明确原始名称，验证物理分支与受管缓存分支没有混淆。</summary>
    private static MetadataCatalog PhysicalLabelCatalog(int mode,bool automatic)
    {
        var c=Catalog(mode,automatic);c.ConfigColumns.Add(new(){Id="16",ConfigTableId="1",YzProjectId="100",ColumnName="caption",Name="原始标签",LogicalType="string",ColumnLength="100"});c.ConfigPageColumns.Add(new(){Id="1016",ConfigPageId="10",ConfigColumnId="16",YzProjectId="100",IsShow=1,IsEdit=1});foreach(var column in c.ConfigColumns.Where(x=>x.Id is "34" or "38"))column.ControlDetail=column.ControlDetail.Replace("global_target_name","caption",StringComparison.Ordinal);foreach(var column in c.ConfigColumns.Where(x=>x.Id is "35" or "40")){column.ColumnName=column.ColumnName.Replace("global_target_names","captions",StringComparison.Ordinal);column.Extra["columnFullName"]=JsonSerializer.SerializeToElement(column.Id=="35"?"global_source.caption":"selected_global_source.caption");}return c;
    }
    /// <summary>只定义合成目录，不改真实平台；在既有全局生产链上保留两个原数组控件及原文件/维护按钮。</summary>
    internal static MetadataCatalog Catalog(int mode,bool automatic)
    {
        var c=GlobalCachedTargetGenerationTests.Catalog(mode,"int64");c.ConfigPages.Single(x=>x.Id=="10").Extra["type"]=JsonSerializer.SerializeToElement(2);c.ConfigPageColumns.Add(new(){Id="1014",ConfigPageId="10",ConfigColumnId="14",YzProjectId="100",IsShow=1,IsEdit=0});
        foreach(var(id,field,kind)in new[]{("34","global_source_ids","MultiSelectPage"),("38","selected_global_source_ids","MultiCheckBoxDatabase")}){var column=c.ConfigColumns.Single(x=>x.Id==id);column.ColumnName=field;column.ControlKind=kind;column.LogicalType="string";column.ColumnLength="max";}
        foreach(var(id,field)in new[]{("35","global_source_global_target_names"),("40","selected_global_source_global_target_names")}){var column=c.ConfigColumns.Single(x=>x.Id==id);column.ColumnName=field;column.ColumnLength="max";}
        if(automatic){c.ConfigColumns.RemoveAll(x=>x.Id is "35" or "40");c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId is "35" or "40");}return c;
    }
}
