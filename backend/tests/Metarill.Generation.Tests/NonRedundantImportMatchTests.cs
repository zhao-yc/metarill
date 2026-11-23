using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Data.Queries;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>逻辑覆盖匹配的生成／固定计划验证；不冒充MySQL写入、并发或正式Full验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class NonRedundantImportMatchTests
{
    /// <summary>物理祖先为空的合法稀疏记录，匹配计划仍读取逻辑祖先；表排序决定首列。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Generated_match_plan_uses_logical_ancestor_and_keeps_minimal_private_outputs(string type)
    {
        var catalog=Catalog(type);var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.LogicalImport");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();
        var declared=ImportRenderer.Columns(page);Assert.Equal("name",declared[0].Source.ColumnName);
        var match=NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page))!;
        // 逻辑祖先单值首列已纳入公开生成，稀疏存储仍由下方真实加载的计划核对。
        Assert.Null(NonRedundantCrossTableSubset.Reason(model,table,page));
        Assert.Equal("country_id",match.Key.Source.ColumnName);Assert.Equal(new[]{"country_id","id"},match.Outputs.Order());Assert.Contains("yz_project_id",match.Dependencies);
        var sparse=CrossTableSparseSelection.Compile(table.Source,"25",catalog.ConfigColumns.Where(column=>column.ConfigTableId=="2"));var stored=sparse.PackValidatedPath(new long[]{1,2,3});Assert.Null(stored["country_id"]);Assert.NotNull(stored["place_id"]);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-logical-import-match");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,model.Namespace);
            var hand=Path.Combine(generated,"Services/LogicalOverwriteSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 覆盖匹配再生成保留手写业务。\n");var handBytes=await File.ReadAllBytesAsync(hand);new CSharpGenerator().Generate(catalog,generated,model.Namespace);Assert.Equal(handBytes,await File.ReadAllBytesAsync(hand));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var service=assembly.GetType(model.Namespace+".Generated.Services.LogicalOverwriteSourceService",true)!;
            var plan=(NonRedundantReadPlan)service.GetField("ImportMatchPlan201",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
            Assert.Equal(type,plan.OutputTypes["country_id"]);Assert.Equal(new[]{"country_id","id"},plan.OutputTypes.Keys.Order());
            // 核对真实生成类型装载的适配计划，不用Service源码包含某个方法名作为逻辑读取的证明。
            var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plan)!;
            var expression=(string)mysql.GetType().GetMethod("Expression",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(mysql,new object[]{"country_id"})!;
            Assert.NotEqual("b.`country_id`",expression);Assert.Contains("place_id",expression);
            Assert.NotNull(service.GetMethod("Import201Async"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>已有R1、追加和普通非跨表导入不换读取链，不扩大任何公开用途。</summary>
    [Theory][InlineData("redundant")][InlineData("append")][InlineData("plain")][InlineData("physical-key")]
    public void Other_import_paths_keep_their_original_match_implementation(string kind)
    {
        var catalog=Catalog("string");if(kind=="redundant")catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;
        if(kind=="append")catalog.ConfigPages.Single(page=>page.Id=="201").Extra["importMethod"]=JsonSerializer.SerializeToElement("OnlyNew");
        if(kind=="plain")catalog.ConfigColumns.Single(column=>column.Id=="25").ControlKind="";
        if(kind=="physical-key")catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1000;
        var model=GenerationModel.Create(catalog,"Example.UnchangedImport");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();
        Assert.Null(NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page)));
    }
    /// <summary>普通及共享六模型都能生成、装载覆盖计划并通过公开审计。</summary>
    [Fact]
    public async Task Six_model_backend_compiles_overwrite_and_passes_public_Full_audit()
    {
        var catalog=GlobalCachedCrossExcelNativePreparation.Catalog();var pages=catalog.ConfigPages.Where(page=>catalog.ConfigTables.Any(table=>table.Id==page.ConfigTableId&&table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal))).ToArray();Assert.Equal(6,pages.Length);
        // 原附件数组仍不能作首列；明确以原级联控件为首列，确保六模型实际装载逻辑匹配分支。
        foreach(var page in pages){page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");catalog.ConfigColumns.First(column=>column.ConfigTableId==page.ConfigTableId&&column.ControlKind=="CascadeTree").SortNo=-10000;}
        var model=GenerationModel.Create(catalog,"Example.SixLogicalImports");Assert.Empty(GenerationCapabilityAudit.Collect(model));new FrontendPackager().Validate(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-six-logical-imports");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var page in pages){var table=model.Tables.Single(table=>table.Source.Id==page.ConfigTableId);var service=assembly.GetType(model.Namespace+".Generated.Services."+table.Name+"Service",true)!;var plan=(NonRedundantReadPlan)service.GetField("ImportMatchPlan"+page.Id,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;Assert.Contains("id",plan.OutputTypes.Keys);Assert.DoesNotContain("attachments",plan.OutputTypes.Keys);}
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>名称首列且无逻辑上下文时继续编译原覆盖路径，不创建额外匹配计划。</summary>
    [Fact]
    public async Task Ordinary_physical_key_keeps_original_generated_query_path()
    {
        var catalog=Catalog("string");catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1000;var model=GenerationModel.Create(catalog,"Example.PhysicalImport");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();Assert.Null(NonRedundantImportMatchRenderer.Resolve(model,table,page,ImportDependencyCompilation.ForPage(model,table,page)));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-physical-import-match");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var service=assembly.GetType(model.Namespace+".Generated.Services.LogicalOverwriteSourceService",true)!;Assert.Null(service.GetField("ImportMatchPlan201",BindingFlags.NonPublic|BindingFlags.Static));Assert.NotNull(service.GetMethod("Import201Async"));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>内部BackendOnly包必须从自身Core／Data源码编译；不借工作区DLL、不冒充公开Full。</summary>
    [Fact]
    public async Task Backend_only_package_builds_its_own_logical_overwrite_runtime()
    {
        var catalog=Catalog("string");var boundary=Path.Combine(Path.GetTempPath(),"metarill-own-logical-import");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var result=new ApplicationPackager().Create(catalog,directory,"Example.OwnLogicalImport",NonRedundantApplicationFixture.Root(),ApplicationPackageMode.BackendOnly);Assert.False(result.FrontendIncluded);
            await NonRedundantApplicationFixture.BuildAsync(directory,"restore");await NonRedundantApplicationFixture.BuildAsync(directory,"build");
            Assert.True(File.Exists(Path.Combine(directory,"src/Metarill.Data/Queries/NonRedundantReads.cs")));Assert.True(File.Exists(Path.Combine(directory,"backend/Application.Host/bin/Debug/net10.0/Application.Host.dll")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>候选父取原记录的逻辑祖先，省略不补交、显式null不退回声明默认；实际调用生成解析器且禁止数据库访问。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true,true)]
    public async Task Generated_context_keeps_logical_parent_null_and_omission_separate(bool compoundTarget,bool splitControl=false)
    {
        var catalog=Catalog("string");catalog.ConfigColumns.Single(column=>column.Id=="22").SortNo=-1000;catalog.ConfigPageColumns.Single(field=>field.Id=="20160").IsShow=1;
        var exportPage=catalog.ConfigPages.Single(item=>item.Id=="201");
        exportPage.IsExport=1;
        exportPage.Extra["exportMethod"]=JsonSerializer.SerializeToElement(splitControl?"edit":"list");
        AddStoredPickerTarget(catalog,compoundTarget,splitControl);
        var picker=AddStoredPickerRole(catalog,"2","201","71",splitControl,repeatParent:!compoundTarget);
        var model=GenerationModel.Create(catalog,"Example.ImportContext");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();var dependency=ImportDependencyCompilation.ForPage(model,table,page);Assert.True(dependency.Contextual);Assert.Equal("country_id",Assert.Single(dependency.ContextValues).Source.ColumnName);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-logical-import-context");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            // 存储协议禁止稀疏ID声明DB默认；用手写partial构造默认验证显式null仍覆盖默认。
            await File.WriteAllTextAsync(Path.Combine(directory,"EntityDefaults.cs"),"namespace Example.ImportContext.Generated.Entities; public partial class LogicalOverwriteSource { public LogicalOverwriteSource(){CountryId=\"99\";} }");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var type=assembly.GetType(model.Namespace+".Generated.Services.LogicalOverwriteSourceService",true)!;
            var read=Assert.IsType<NonRedundantReadPlan>(type.GetField("ReadPlan201",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
            var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(read)!;
            var from=Assert.IsType<string>(mysql.GetType().GetProperty("From",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(mysql));
            Assert.Contains("logical_overwrite_picker",from);
            Assert.Contains("logical_overwrite_country_id",from);
            Assert.Contains("CASE WHEN",from);
            Assert.Equal(compoundTarget,from.Contains("approval_country_id",StringComparison.Ordinal));
            if(compoundTarget)
            {
                var generated=File.ReadAllText(Path.Combine(directory,"generated/Services/LogicalOverwriteSourceService.g.cs"));
                Assert.Contains("ApplyTextParent(query,session,\"approval_country_id\",targetParentId!",generated);
                Assert.Contains("targetParentId!=targetParentKey.ToString(CultureInfo.InvariantCulture)",generated);
                Assert.Contains("entity.LogicalOverwriteCountryId ==",generated);
                Assert.Contains("OptionsScope201_71Async(request.ParentId, groupPickerId.Key",generated);
                Assert.NotNull(type.GetMethod("Export201Async"));
            }
            var contextMethod=type.GetMethod("ImportContext201",BindingFlags.NonPublic|BindingFlags.Static)!;
            var defaultEntity=Activator.CreateInstance(assembly.GetType(model.Namespace+".Generated.Entities.LogicalOverwriteSource",true)!)!;Assert.Equal("99",defaultEntity.GetType().GetProperty("CountryId")!.GetValue(defaultEntity));
            var context=contextMethod.Invoke(null,new object[]{new Dictionary<string,object?>{{"country_id","1"}}})!;Assert.Equal("1",context.GetType().GetProperty("CountryId")!.GetValue(context));Assert.NotEqual(defaultEntity.GetType(),context.GetType());
            var nullContext=contextMethod.Invoke(null,new object[]{new Dictionary<string,object?>{{"country_id",null}}})!;Assert.Null(nullContext.GetType().GetProperty("CountryId")!.GetValue(nullContext));
            var constructor=type.GetConstructors().Single();var arguments=new object?[constructor.GetParameters().Length];foreach(var parameter in constructor.GetParameters()){if(parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>))arguments[parameter.Position]=Array.Empty<IRecordMutationObserver>();if(parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>))arguments[parameter.Position]=Array.Empty<IRecordMutationGuard>();}var service=constructor.Invoke(arguments);var parse=type.GetMethod("ParseImport201Async",BindingFlags.NonPublic|BindingFlags.Instance)!;var actor=new RuntimeActor(1,"上下文离线验证",false,catalog.YzProjectId,"1");
            var values=await Parse("{\"name\":\"只提交名称\"}",context);Assert.Equal(new[]{"name"},values.Keys);
            var error=await Assert.ThrowsAsync<PageImportException>(()=>Parse("{\"候选\":\"某候选\"}",nullContext));Assert.Equal(PageImportError.Relation,error.Code);Assert.Equal("候选",error.ColumnName);
            var target=catalog.ConfigTables.Single(item=>item.Id=="6");
            var targetPage=catalog.ConfigPages.Single(item=>item.Id=="601");
            var condition=FixedConditionPlan.Compile(targetPage.WhereSql,
                catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).Select(column=>column.ColumnName));
            var parent=MultiTargetParentScope.CompileSelectionPage(catalog,target,condition,
                allowGlobalParent:false,allowStoredPhysicalParents:true)!;
            var scope=parent.SingleSource(catalog,table.Source,picker,page.Source,allowLogicalListForm:true);
            Assert.Equal("60",scope.Column.Id);
            Assert.Equal("201",Assert.Single(scope.LogicalPages).Id);
            var field=catalog.ConfigPageColumns.Single(setting=>setting.Id=="20160");
            field.IsShow=field.IsEdit=0;
            Assert.Throws<InvalidOperationException>(()=>parent.SingleSource(catalog,table.Source,picker,page.Source,allowLogicalListForm:true));
            /// <summary>没有提供session／mapper，正常名称解析与空父拒绝必须在任何数据库操作之前结束。</summary>
            Task<Dictionary<string,JsonElement>> Parse(string json,object existing)=>(Task<Dictionary<string,JsonElement>>)parse.Invoke(service,new object?[]{JsonSerializer.Deserialize<JsonElement>(json),null,actor,existing,false,CancellationToken.None})!;
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>冗余来源按实际父键重算名称，页面和控件分担的多个目标父字段也参与反查。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stored_list_compound_parent_is_retained_in_redundant_cache(bool splitControl)
    {
        var catalog=Catalog("string");
        AddStoredPickerTarget(catalog,compoundTarget:true,splitControl);
        catalog.ConfigTables.Add(new(){Id="7",TableName="stored_picker_consumer",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="7011",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="7012",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="7013",ConfigTableId="7",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="7014",ConfigTableId="7",ColumnName="country_id",LogicalType="int64",LinkConfigTableId="3"},
            new(){Id="7015",ConfigTableId="7",ColumnName="picker_name",LogicalType="string",Extra=new()
            {
                ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                ["columnFullName"]=JsonSerializer.SerializeToElement("picker.name")
            }}
        ]);
        catalog.ConfigPages.Add(new(){Id="701",ConfigTableId="7",IsImport=1,IsExport=1,
            WhereSql="yz_project_id=@current_yz_project_id AND country_id=@parentId"});
        catalog.ConfigPageColumns.Add(new(){Id="7017015",ConfigPageId="701",ConfigColumnId="7015",IsShow=1});
        AddStoredPickerRole(catalog,"7","701","7016",splitControl);
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,"Example.CompoundStoredList");
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        Assert.Contains("picker_name",maintenance.MixedSourceColumns["stored_picker_consumer"]);
        var generated=BackendRenderer.Render(model).Single(file=>file.Path=="Services/StoredPickerConsumerService.g.cs").Content;
        Assert.Contains("ApplyTextParent(query,session,\"approval_country_id\",targetParentId!",generated);
        Assert.Contains("OptionsScope701_7016Async(request.ParentId, groupPickerId.Key",generated);
        Assert.Contains("Import701Async",generated);
        const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
        var plan=plans["7"]!;
        Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
        var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
        var sql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["picker_name"]));
        Assert.Contains("approval_country_id",sql);
        Assert.Contains("logical_overwrite_country_id",sql);
        Assert.Contains("b."+(char)96+"country_id"+(char)96,sql);
        var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",hidden)!.GetValue(read));
        Assert.Contains(reverse.Cast<object>(),item=>Equals("logical_overwrite_picker",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
            Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                .TryGetValue("picker_name",out var fields)&&fields.Contains("approval_country_id")&&fields.Contains("logical_overwrite_country_id"));

        catalog.ConfigColumns.Single(column=>column.Id=="66").LinkConfigTableId="4";
        var conflicting=GenerationModel.Create(catalog,"Example.ConflictingStoredList");
        var error=Assert.Throws<InvalidOperationException>(()=>MixedRelationPropagationCompilation.ForModel(conflicting));
        Assert.Contains("同一真实父表",error.Message);
    }

    /// <summary>两个来源父键同时约束原下拉，实时回显和名称副本都复核实际记录。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(0, false, true, false)]
    [InlineData(0, true, true, false)]
    [InlineData(0, true, true, true)]
    [InlineData(1, false, true, false)]
    [InlineData(1, true, true, false)]
    [InlineData(1, true, true, true)]
    [InlineData(0, false, false, false, true)]
    [InlineData(0, true, false, false, true)]
    [InlineData(1, false, false, false, true)]
    [InlineData(1, true, false, false, true)]
    [InlineData(0, false, true, false, true)]
    [InlineData(0, true, true, false, true)]
    [InlineData(1, false, true, false, true)]
    [InlineData(1, true, true, false, true)]
    [InlineData(0, true, true, false, false, 1)]
    [InlineData(0, true, true, true, false, 1)]
    [InlineData(0, true, true, false, true, 1)]
    [InlineData(0, true, false, true, false, 1)]
    [InlineData(0, true, false, false, true, 1)]
    [InlineData(1, true, true, false, false, 1)]
    [InlineData(1, true, true, true, false, 1)]
    [InlineData(1, true, true, false, true, 1)]
    [InlineData(1, true, false, true, false, 1)]
    [InlineData(1, true, false, false, true, 1)]
    [InlineData(0, true, true, false, false, 2)]
    [InlineData(0, true, true, true, false, 2)]
    [InlineData(0, true, true, false, true, 2)]
    [InlineData(0, true, false, true, false, 2)]
    [InlineData(0, true, false, false, true, 2)]
    [InlineData(1, true, true, false, false, 2)]
    [InlineData(1, true, true, true, false, 2)]
    [InlineData(1, true, true, false, true, 2)]
    [InlineData(1, true, false, true, false, 2)]
    [InlineData(1, true, false, false, true, 2)]
    [InlineData(0, true, true, false, false, 3)]
    [InlineData(0, true, true, true, false, 3)]
    [InlineData(0, true, true, false, true, 3)]
    [InlineData(0, true, false, true, false, 3)]
    [InlineData(0, true, false, false, true, 3)]
    [InlineData(1, true, true, false, false, 3)]
    [InlineData(1, true, true, true, false, 3)]
    [InlineData(1, true, true, false, true, 3)]
    [InlineData(1, true, false, true, false, 3)]
    [InlineData(1, true, false, false, true, 3)]
    [InlineData(0, false, false, false, false, 4)]
    [InlineData(0, false, false, false, true, 4)]
    [InlineData(0, false, true, false, false, 4)]
    [InlineData(0, false, true, false, true, 4)]
    [InlineData(1, false, false, false, false, 4)]
    [InlineData(1, false, false, false, true, 4)]
    [InlineData(1, false, true, false, false, 4)]
    [InlineData(1, false, true, false, true, 4)]
    [InlineData(0, false, false, false, false, 5)]
    [InlineData(0, false, false, false, true, 5)]
    [InlineData(0, false, true, false, false, 5)]
    [InlineData(0, false, true, false, true, 5)]
    [InlineData(1, false, false, false, false, 5)]
    [InlineData(1, false, false, false, true, 5)]
    [InlineData(1, false, true, false, false, 5)]
    [InlineData(1, false, true, false, true, 5)]
    [InlineData(0, false, false, false, false, 6)]
    [InlineData(0, false, true, false, false, 6)]
    [InlineData(1, false, false, false, true, 6)]
    [InlineData(1, false, true, false, true, 6)]
    public async Task Single_relation_keeps_all_published_parent_columns(int storage,bool compoundTarget,
        bool liveTarget=false,bool controlParent=true,bool popup=false,int parentMode=0) =>
        await VerifyPublishedParentsAsync(storage, compoundTarget, liveTarget, controlParent, popup, parentMode);

    /// <summary>派生只读规则以完整生成服务检查单文本父键、同父文本和整数交集。</summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(0, true)]
    public Task Derived_rule_records_use_the_published_text_parent(int parentMode, bool compoundTarget) =>
        VerifyPublishedParentsAsync(0, compoundTarget, liveTarget: true, controlParent: true, popup: false, parentMode,
            ruleRecord: true);

    /// <summary>实际业务按钮查询和写事务根锁沿用普通页面的文本父键过滤。</summary>
    [Theory]
    [InlineData(0, 3, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 3, true)]
    public Task Business_button_record_uses_the_published_text_parent(int storage, int parentMode, bool compoundTarget) =>
        VerifyPublishedParentsAsync(storage, compoundTarget, liveTarget: true, controlParent: true, popup: false,
            parentMode, buttonRecord: true);

    /// <summary>共享来源页夹具运行原CRUD／Excel范围，以及适用的派生规则和业务按钮。</summary>
    private static async Task VerifyPublishedParentsAsync(int storage, bool compoundTarget, bool liveTarget,
        bool controlParent, bool popup, int parentMode, bool ruleRecord = false, bool buttonRecord = false)
    {
        var catalog=Catalog("string");
        var splitControl=compoundTarget&&controlParent;
        AddStoredPickerTarget(catalog,compoundTarget,splitControl);
        var textTarget=parentMode is 5 or 6;
        if(textTarget)catalog.ConfigColumns.Single(column=>column.Id=="65").LogicalType="string";
        if(liveTarget)catalog.ConfigTables.Single(table=>table.Id=="6").IsRongyu=0;
        catalog.ConfigTables.Add(new(){Id="7",TableName="compound_picker_owner",IsRongyu=storage});
        catalog.ConfigColumns.AddRange([
            new(){Id="7011",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="7012",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="7013",ConfigTableId="7",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="7014",ConfigTableId="7",ColumnName="country_id",LogicalType="int64",LinkConfigTableId="3"},
            new(){Id="7017",ConfigTableId="7",ColumnName="review_country_id",LogicalType="string",LinkConfigTableId="3"},
            new(){Id="7015",ConfigTableId="7",ColumnName="picker_name",LogicalType="string",Extra=new()
            {
                ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                ["columnFullName"]=JsonSerializer.SerializeToElement("picker.name")
            }}
        ]);
        var page=new PageMetadata{Id="701",ConfigTableId="7",IsNew=1,IsSave=1,IsCopy=1,IsImport=1,IsExport=1,
            WhereSql="yz_project_id=@current_yz_project_id AND review_country_id=@parent_id AND country_id=@parentId"};
        var textSource=parentMode is 3 or 4 or 6;
        if(parentMode is 1 or 5||textSource)
        {
            catalog.ConfigColumns.Remove(catalog.ConfigColumns.Single(column=>column.Id=="7017"));
            page.WhereSql="yz_project_id=@current_yz_project_id AND country_id=@parentId";
            if(textSource)
            {
                catalog.ConfigColumns.Single(column=>column.Id=="7014").LogicalType="string";
                catalog.ConfigColumns.Add(new(){Id="7019",ConfigTableId="7",ColumnName="scope_state",LogicalType="int64"});
                page.WhereSql+=" AND scope_state=1";
            }
        }
        else if(parentMode==2)
        {
            catalog.ConfigColumns.Remove(catalog.ConfigColumns.Single(column=>column.Id=="7014"));
            catalog.ConfigPageColumns.Add(new(){Id="7017017",ConfigPageId="701",ConfigColumnId="7017",IsShow=1,IsEdit=1});
            page.WhereSql="yz_project_id=@current_yz_project_id";
        }
        page.Extra["exportMethod"]=JsonSerializer.SerializeToElement(compoundTarget&&(!liveTarget||!splitControl)?"edit":"list");
        if (ruleRecord) page.Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("pickerName == '已审核'");
        if (buttonRecord)
            catalog.ConfigButtons.Add(JsonSerializer.SerializeToElement(new
            {
                id = "86000", configPageId = page.Id, yzProjectId = catalog.YzProjectId, actionType = 1,
                name = "固定业务", place = "Edit", sql = "/admin/custom/parent-scope/check"
            }));
        catalog.ConfigPages.Add(page);
        catalog.ConfigPageColumns.Add(new(){Id="7017015",ConfigPageId="701",ConfigColumnId="7015",IsShow=1});
        // 分写父条件的非冗余字典同时用原首列覆盖路径，真实物理首列与名称回显列分开。
        var overwrite=textSource||textTarget||liveTarget&&splitControl||popup&&compoundTarget;
        if(overwrite)
        {
            page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            catalog.ConfigColumns.Add(new(){Id="7018",ConfigTableId="7",ColumnName="name",Name="记录名",LogicalType="string",SortNo=-100});
            catalog.ConfigPageColumns.Add(new(){Id="7017018",ConfigPageId="701",ConfigColumnId="7018",SortNo=-100});
        }
        var pickerRole=AddStoredPickerRole(catalog,"7","701","7016",splitControl);
        if(popup)
        {
            catalog.ConfigPages.Single(item=>item.Id=="601").Extra["type"]=JsonSerializer.SerializeToElement(1);
            pickerRole.ControlKind="SelectPage";
            pickerRole.ControlDetail=JsonSerializer.Serialize(new{pageId="601",nameField="name",buttonText="选择字典"});
        }
        RedundantMultiNameCacheTests.Published(catalog);

        var model=GenerationModel.Create(catalog,"Example.CompoundListOwner");
        string sql;
        const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        if(storage==0)
        {
            var owner=model.Tables.Single(table=>table.Source.Id=="7");
            var publishedPage=Assert.Single(owner.Pages);
            var view=NonRedundantReadCompilation.ForPage(model,owner,publishedPage);
            var read=NonRedundantReadCompilation.CompilePage(model,owner,["id","picker_name"],["picker_name"],
                [],view.SingleTargets,view.MultiTargets);
            var mysql=typeof(NonRedundantReadPlan).GetProperty("MySql",hidden)!.GetValue(read)!;
            sql=Assert.IsType<string>(mysql.GetType().GetProperty("From",hidden)!.GetValue(mysql));
        }
        else
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("picker_name",maintenance.MixedSourceColumns["compound_picker_owner"]);
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
            var plan=plans["7"]!;
            Assert.Null(plan.GetType().GetProperty("Error")!.GetValue(plan));
            var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
            sql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["picker_name"]));
            var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",hidden)!.GetValue(read));
            Assert.Contains(reverse.Cast<object>(),item=>Equals("logical_overwrite_picker",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                    .TryGetValue("picker_name",out var fields)&&fields.Contains("logical_overwrite_country_id")&&
                        (!compoundTarget||fields.Contains("approval_country_id")));
        }
        Assert.Equal(parentMode is 0 or 2,sql.Contains("CAST(b."+(char)96+"review_country_id"+(char)96+" AS CHAR)",StringComparison.Ordinal));
        Assert.Equal(parentMode!=2,sql.Contains("CAST(b."+(char)96+"country_id"+(char)96+" AS CHAR)",StringComparison.Ordinal));
        Assert.Equal(compoundTarget,sql.Contains("approval_country_id",StringComparison.Ordinal));
        var generated=BackendRenderer.Render(model).Single(file=>file.Path=="Services/CompoundPickerOwnerService.g.cs").Content;
        if(liveTarget&&compoundTarget&&!splitControl)
        {
            Assert.Contains("DerivedOptionsRows701_7016Async(request.ParentId,groupPickerId.Key",generated);
            Assert.Contains("FixedReadFilter.ForTextParent(\"approval_country_id\",targetParentId!)",generated);
        }
        else
            Assert.Contains("OptionsScope701_7016Async(request.ParentId, groupPickerId.Key",generated);
        if(compoundTarget)Assert.Contains("ApplyTextParent(query,session,\"approval_country_id\",targetParentId!",generated);
        if(textTarget)Assert.Contains("ApplyTextParent(query,session,\"logical_overwrite_country_id\",targetParentId!",generated);
        if(overwrite)Assert.Contains("var key = matchValue.Deserialize",generated);
        Assert.Contains("Import701Async",generated);
        Assert.Contains("Export701Async",generated);
        Assert.Equal(parentMode!=2,generated.Contains("targetParentId!=parentId",StringComparison.Ordinal));
        Assert.Contains("targetParentId!=targetParentKey.ToString(CultureInfo.InvariantCulture)",generated);
        if(parentMode==0)
        {
            Assert.Contains("ApplyTextParent(query,session,\"review_country_id\",parentId!",generated);
            Assert.Contains("entity.ReviewCountryId!=entity.CountryId?.ToString(CultureInfo.InvariantCulture)",generated);
            Assert.Contains("originalCandidateValues.ReviewCountryId",generated);
            Assert.Contains("当前单值关联来源页要求规范正整数父键",generated);
        }
        if(parentMode==2)Assert.Contains("entity.ReviewCountryId",generated);
        if(textSource)
        {
            Assert.Contains("ApplyTextParent(query,session,\"country_id\",parentId!",generated);
            Assert.Contains("if(entity.CountryId!=long.Parse(parentId!, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))",generated);
            if(storage==0)
            {
                Assert.Contains("result.Add(FixedReadFilter.ForTextParent(\"country_id\",request.ParentId!))",generated);
                Assert.Contains("filters.Add(FixedReadFilter.ForTextParent(\"country_id\",parentId!))",generated);
            }
        }

        // 编译实际生成包，证明宿主内嵌的目录仍能重新绑定两个来源字段。
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-compound-list-owner");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(model.Namespace+".Generated.Services.CompoundPickerOwnerService",true)!;
            if(popup)
            {
                var detail=await File.ReadAllTextAsync(Path.Combine(directory,"generated/frontend/pages/yzp/compound_picker_owner-detail-701.vue"));
                Assert.Contains("OriginalSelection7016",detail);
                Assert.Contains(parentMode==2?":parent_id=\"entity.reviewCountryId\"":":parent_id=\"parent_id\"",detail);
                Assert.Contains("onPopSelect7016",detail);
                if(compoundTarget||textTarget)
                {
                    var actualTarget=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LogicalOverwritePickerService.g.cs"));
                    var parentColumn=textTarget?"logical_overwrite_country_id":"approval_country_id";
                    Assert.Contains(liveTarget?$"FixedReadFilter.ForTextParent(\"{parentColumn}\",request.ParentId!)":
                        $"ApplyTextParent(query,session,\"{parentColumn}\",parentId!",actualTarget);
                }
            }
            Assert.NotNull(service.GetMethod("Create701Async"));
            Assert.NotNull(service.GetMethod("Update701Async"));
            Assert.NotNull(service.GetMethod("Copy701Async"));
            Assert.NotNull(service.GetMethod("Import701Async"));
            Assert.NotNull(service.GetMethod("Export701Async"));
            if(textSource)await AssertPublishedTextSourceGuards(service,storage,catalog.YzProjectId);
            if (ruleRecord)
                await AssertRuleTextParentAsync(service, generated, textSource ? "country_id" : "review_country_id",
                    mixedParent: !textSource, catalog.YzProjectId);
            if (buttonRecord)
                await AssertButtonTextParentAsync(service, textSource ? "country_id" : "review_country_id",
                    mixedParent: !textSource, catalog.YzProjectId);
            if(storage==0)
                Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan701",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        var target=catalog.ConfigTables.Single(table=>table.Id=="6");
        var targetFields=catalog.ConfigColumns.Where(column=>column.ConfigTableId==target.Id).Select(column=>column.ColumnName);
        var targetTerms=FixedConditionPlan.Compile(catalog.ConfigPages.Single(item=>item.Id=="601").WhereSql,targetFields).Terms;
        var parent=MultiTargetParentScope.CompileListPage(catalog,target,targetTerms)!;
        var source=catalog.ConfigTables.Single(table=>table.Id=="7");
        var picker=catalog.ConfigColumns.Single(column=>column.Id=="7016");
        var binding=parent.SingleSource(catalog,source,picker,page);
        Assert.Equal(parentMode==2?"7017":"7014",binding.Column.Id);
        if(parentMode==0)
            Assert.Equal("7017",Assert.Single(binding.AdditionalColumns).Id);
        else
            Assert.Empty(binding.AdditionalColumns);
        catalog.ConfigColumns.Single(column=>column.Id==(parentMode is 0 or 2?"7017":"7014")).LinkConfigTableId="4";
        Assert.Throws<InvalidOperationException>(()=>parent.SingleSource(catalog,source,picker,page));
    }

    /// <summary>直接执行生成的输入守卫和固定过滤，非规范值必须在实际数据会话前被拒绝。</summary>
    private static async Task AssertPublishedTextSourceGuards(Type service,int storage,string projectId)
    {
        var actor=new RuntimeActor(1,"文本父字段合同",false,projectId,"1");
        var owner=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(service);
        var validate=service.GetMethod("ValidateParents701Async",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var filters=storage==0?service.GetMethod("ReadFilters701",BindingFlags.NonPublic|BindingFlags.Static):null;
        foreach(var invalid in new[]{"071","71 ","+71","0","9223372036854775808"})
        {
            var arguments=new object?[]{invalid,actor,CancellationToken.None}
                .Concat(validate.GetParameters().Skip(3).Select(parameter=>parameter.DefaultValue)).ToArray();
            var error=await Assert.ThrowsAsync<ArgumentException>(async()=>await (Task)validate.Invoke(owner,arguments)!);
            Assert.Contains("规范正整数父",error.Message);
            if(filters is not null)
                Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>
                    filters.Invoke(null,[new PageRequest{ParentId=invalid},actor])).InnerException);
        }
        if(filters is not null)
        {
            const string large="9007199254740993";
            var actual=Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(filters.Invoke(null,[new PageRequest{ParentId=large},actor]));
            Assert.Equal(FixedReadFilter.ForTextParent("country_id",large),Assert.Single(actual,value=>value.Field=="country_id"));
        }
    }

    /// <summary>实际生成程序集从固定计划发出规则 SQL，传输替身既不联网也不解释权限。</summary>
    private static async Task AssertRuleTextParentAsync(Type service, string generated, string textParent,
        bool mixedParent, string projectId)
    {
        var method = service.GetMethod("ReadRuleRecord701CoreAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.NotNull(method);
        var definition = generated[generated.IndexOf("> ReadRuleRecord701CoreAsync", StringComparison.Ordinal)..];
        var filters = definition[..definition.IndexOf("var reader=", StringComparison.Ordinal)];
        Assert.Contains($"""FixedReadFilter.ForTextParent("{textParent}",parentId!)""", filters);
        if (mixedParent)
            Assert.Contains("""new("country_id",FixedReadComparison.Equal,long.Parse(parentId!, CultureInfo.InvariantCulture))""", filters);

        var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField("RuleRecordPlan701", BindingFlags.NonPublic |
            BindingFlags.Static)!.GetValue(null));
        Assert.Contains(textParent, plan.OutputTypes.Keys);
        Assert.Contains("picker_name", plan.OutputTypes.Keys);
        var transport = new DerivedRuleRecordGenerationTests.Transport(plan.OutputTypes) { HasRow = false };
        using var session = new DatabaseSession(new()
        {
            ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused"
        }, null, transport);
        var mapperType = service.Assembly.GetType(service.Namespace!.Replace(".Services", ".Mappers") + ".CompoundPickerOwnerMapper", true)!;
        var mapper = Activator.CreateInstance(mapperType, session)!;
        var target = Activator.CreateInstance(service, [session, mapper, new SnowflakeIdGenerator(1),
            Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>(), null])!;
        var actor = new RuntimeActor(1, "文本父字段合同", false, projectId, "1");

        async Task Read(string parent, bool locking)
        {
            var task = (Task)method.Invoke(target, [1L, parent, "update", actor, false, locking, CancellationToken.None])!;
            await task;
            Assert.Null(task.GetType().GetProperty("Result")!.GetValue(task));
        }

        foreach (var invalid in new[] { "071", "71 ", "+71", "0", "9223372036854775808" })
            await Assert.ThrowsAsync<ArgumentException>(() => Read(invalid, false));
        Assert.Empty(transport.Commands);

        const string large = "9007199254740993";
        await session.InReadSnapshotAsync(async () => { await Read(large, false); return true; });
        await session.InTransactionAsync(async () => { await Read(large, true); return true; });
        Assert.Equal(2, transport.Commands.Count);
        for (var i = 0; i < transport.Commands.Count; i++)
        {
            var command = transport.Commands[i];
            Assert.Contains("BINARY (b.`" + textParent + "`)=BINARY ", command.Sql);
            Assert.Contains(large, command.Values);
            Assert.DoesNotContain(large, command.Sql);
            if (mixedParent) Assert.Contains(9007199254740993L, command.Values);
            Assert.Equal(i == 1, command.Sql.EndsWith("FOR SHARE", StringComparison.Ordinal));
        }
    }

    /// <summary>执行实际生成的按钮读入口，捕获 ORM 参数并核对写事务构造的排他查询。</summary>
    private static async Task AssertButtonTextParentAsync(Type service, string textParent, bool mixedParent, string projectId)
    {
        var transport = new DerivedRuleRecordGenerationTests.Transport(new Dictionary<string, string>());
        using var session = new DatabaseSession(new()
        {
            ExpectedDatabase = "offline",
            ConnectionString = "Server=127.0.0.1;Port=1;Database=offline;User ID=unused"
        }, null, transport);
        var mapper = Activator.CreateInstance(service.Assembly.GetType(service.Namespace!.Replace(".Services", ".Mappers") +
            ".CompoundPickerOwnerMapper", true)!, session)!;
        var actor = new RuntimeActor(1, "业务按钮记录范围", false, projectId, "1");
        var authorization = new ButtonParentAuthorization(actor);
        var target = Activator.CreateInstance(service, [session, mapper, new SnowflakeIdGenerator(1),
            Array.Empty<IRecordMutationObserver>(), Array.Empty<IRecordMutationGuard>(), authorization])!;
        var read = service.GetMethod("ReadButtonRecord701Async")!;
        var rootQuery = service.GetMethod("ButtonRecordQuery701", BindingFlags.NonPublic | BindingFlags.Instance)!;
        const string largeParent = "9007199254740993";
        var textPredicate = $"BINARY (`a`.`{textParent}`)=BINARY @mt_parent_{textParent}";
        var commands = 0;
        session.Orm.Aop.CurdBefore += (_, args) =>
        {
            commands++;
            Assert.Contains(textPredicate, args.Sql);
            Assert.DoesNotContain(largeParent, args.Sql);
            Assert.Equal(largeParent, Assert.Single(args.DbParms, parameter => parameter.ParameterName.TrimStart('@') == $"mt_parent_{textParent}").Value);
            if (mixedParent)
            {
                var bound = System.Text.RegularExpressions.Regex.Match(args.Sql, @"a\.`country_id`\s*=\s*(?:cast\(\s*)?(@[A-Za-z0-9_]+)");
                Assert.True(bound.Success, args.Sql);
                var integerParent = Assert.Single(args.DbParms, parameter =>
                    parameter.ParameterName.TrimStart('@') == bound.Groups[1].Value.TrimStart('@'));
                Assert.Equal(largeParent, Convert.ToString(integerParent.Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            throw new CapturedButtonParentSql();
        };
        Task Read(string? parent) => (Task)read.Invoke(target, [1L, parent, "86000", actor, CancellationToken.None])!;

        await Assert.ThrowsAsync<CapturedButtonParentSql>(() => Read(largeParent));
        foreach (var invalid in new[] { null, "071", "71 ", "+71", "0", "9223372036854775808" })
            await Assert.ThrowsAsync<ArgumentException>(() => Read(invalid));
        Assert.Equal(1, commands);

        await session.InTransactionAsync(() =>
        {
            var query = rootQuery.Invoke(target, [1L, largeParent, "86000", actor, true])!;
            var entity = read.ReturnType.GetGenericArguments().Single();
            var sql = BusinessRecordScopeGenerationTests.SqlOf(entity, query);
            Assert.Contains(textPredicate, sql);
            Assert.Contains("FOR UPDATE", sql, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(true);
        });
        Assert.Empty(transport.Commands);
    }

    /// <summary>在 ORM 建立真实连接前停止；用例只验证固定 SQL 与参数。</summary>
    private sealed class CapturedButtonParentSql : Exception;

    /// <summary>只授已登记按钮，普通查询权限不参与测试合同。</summary>
    private sealed class ButtonParentAuthorization(RuntimeActor actor) : IRuntimeAuthorization
    {
        public RuntimeActor RequireActor() => actor;
        public void RequirePagePermission(string pageId, string operation) => throw new UnauthorizedAccessException();
        public void RequireButtonPermission(string pageId, string buttonId) => Assert.Equal(("701", "86000"), (pageId, buttonId));
    }

    /// <summary>原字典页和控件可分别声明同一父记录的物理字段。</summary>
    private static void AddStoredPickerTarget(MetadataCatalog catalog,bool compoundTarget,bool splitControl)
    {
        catalog.ConfigTables.Add(new(){Id="6",TableName="logical_overwrite_picker",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},
            new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="65",ConfigTableId="6",ColumnName="logical_overwrite_country_id",LogicalType="int64",LinkConfigTableId="3"}
        ]);
        var page=new PageMetadata{Id="601",ConfigTableId="6",
            WhereSql="yz_project_id=@current_yz_project_id AND logical_overwrite_country_id=@parent_id"};
        catalog.ConfigPages.Add(page);
        catalog.ConfigPageColumns.Add(new(){Id="60162",ConfigPageId="601",ConfigColumnId="62"});
        if(!compoundTarget)return;
        catalog.ConfigColumns.Add(new(){Id="66",ConfigTableId="6",ColumnName="approval_country_id",LogicalType="string",LinkConfigTableId="3"});
        if(!splitControl)page.WhereSql+=" AND approval_country_id=@parentId";
    }

    /// <summary>发布同一个普通下拉角色及实际目标页。</summary>
    private static ColumnMetadata AddStoredPickerRole(MetadataCatalog catalog,string tableId,string pageId,string columnId,
        bool splitControl,bool repeatParent=false)
    {
        var picker=new ColumnMetadata
        {
            Id=columnId,ConfigTableId=tableId,ColumnName="picker_id",Name="候选",LogicalType="int64",LinkConfigTableId="6",ControlKind="List",
            ControlDetail=JsonSerializer.Serialize(new{table="logical_overwrite_picker",tableId="6",nameField="name",
                whereSql=splitControl?"approval_country_id=@parentId":repeatParent?"logical_overwrite_country_id=@parentId":""})
        };
        picker.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("601");
        catalog.ConfigColumns.Add(picker);
        catalog.ConfigPageColumns.Add(new(){Id=pageId+columnId,ConfigPageId=pageId,ConfigColumnId=columnId,IsEdit=1,IsShow=1});
        return picker;
    }

    /// <summary>沿用真实跨表目录，仅按元数据显式把祖先原输入列设为覆盖首列，页面列顺序不改。</summary>
    private static MetadataCatalog Catalog(string type)
    {
        var catalog=NonRedundantCrossTableGenerationTests.Catalog("logical_overwrite",type,"int64");var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.ConfigColumns.Add(new(){Id="60",ConfigTableId="2",ColumnName="country_id",Name="国家ID",LogicalType=type,ColumnLength=type=="string"?"64":"",LinkConfigTableId="3",SortNo=-100});catalog.ConfigPageColumns.Add(new(){Id="20160",ConfigPageId="201",ConfigColumnId="60",IsEdit=1,IsShow=0,SortNo=1000});return catalog;
    }
}
