using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
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
    public async Task Checkbox_with_stored_global_account_dictionary(int mode,bool explicitPage,bool globalSource=false)
    {
        var catalog = PhysicalLabelCatalog(mode,false);
        if(globalSource)
            catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigColumns.RemoveAll(column => column.Id is "34" or "35");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "34" or "35");
        var dictionary = catalog.ConfigPages.Single(page => page.Id == "10");
        dictionary.Extra.Remove("type");
        dictionary.WhereSql = "status=1 AND store_id=@current_store_id";
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

        const string application = "Example.GlobalStoredCheckbox";
        var model = GenerationModel.Create(catalog,application);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        var owner = model.Tables.Single(table => table.Source.Id == "3");
        var page = Assert.Single(owner.Pages);
        var checkbox = Assert.Single(DatabaseCheckboxCompilation.ForService(model,owner,page));
        Assert.Equal("10",checkbox.GlobalPage?.Source.Id);
        Assert.Equal("16",checkbox.Name.Source.Id);
        Assert.Contains(page.Searchable,column=>column.Source.Id=="40");
        Assert.False(checkbox.DerivedName);

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
            Assert.Contains(mode==0?"FixedReadComparison.JsonArrayAnyContains":"ApplyJsonArrayAnyLike",service);
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
                var scoped=Assert.Single(RelationPropagationCompilation.DerivedBindings(model),binding=>binding.Foreign.Source.Id=="38");
                Assert.True(scoped.Multiple);
                if(globalSource)
                {
                    var generatedMaintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                        assembly.GetType(application+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                    var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                        generatedMaintenance.GetType().GetProperty("Plans",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(generatedMaintenance));
                    var ownerPlan=plans[owner.Source.Id]!;
                    Assert.Null(ownerPlan.GetType().GetProperty("Error")!.GetValue(ownerPlan));
                    AssertOwnedScope(ownerPlan.GetType().GetProperty("Read")!.GetValue(ownerPlan)!);
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

    /// <summary>核对实际生成的业务或维护计划仍以物理项目列作根范围。</summary>
    private static void AssertOwnedScope(object mysql) =>
        Assert.Contains("`yz_project_id`",Assert.IsType<string>(
            mysql.GetType().GetProperty("Scope",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mysql)));

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
