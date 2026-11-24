using System.Diagnostics;
using System.Reflection;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定名称筛选只编译可证明的存储列与原Vue键，实际SQL另由生成接口隔离测试覆盖。</summary>
public sealed class SelectionNameFilterCompilationTests
{
    /// <summary>单值名称case真实加入临时C#项目编译，原Vue仍提交相同辅助键。</summary>
    [Fact]
    public async Task Scalar_name_binding_matches_original_vue_key_and_compiles_real_typed_case()
    {
        var catalog=ScalarCatalog();var model=GenerationModel.Create(catalog,"Example.NameFilters");var table=model.Tables.Single(table=>table.Source.Id=="1");var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);var binding=Assert.Single(plan.Bindings);Assert.Empty(plan.Gaps);Assert.Equal("groupName",binding.FilterKey);Assert.Equal("group_name",binding.StorageColumn.Source.ColumnName);Assert.Equal(SelectionNameFilterKind.ScalarLike,binding.Kind);Assert.True(plan.ConsumedKeys.SetEquals(["groupName"]));Assert.DoesNotContain("groupId",plan.ConsumedKeys);
        var cases=SelectionNameFilterCompilation.RenderCases(plan);Assert.Contains("StringNameQuery.ApplyLike(session, query, entity => entity.GroupName, value)",cases);Assert.Contains("JsonValueKind.String",cases);Assert.DoesNotContain("JsonSerializer.Deserialize<long",cases);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-name-filter-compiler");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.NameFilters");var detail=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/customer-list-101.vue"));Assert.Contains("v-model='filters.groupName'",detail);
            await File.WriteAllTextAsync(Path.Combine(output,"Services/SelectionNameFilterCaseProbe.cs"),"using FreeSql; using System; using System.Text.Json; using System.Collections.Generic; using Example.NameFilters.Generated.Entities; namespace Example.NameFilters.Generated; public static class SelectionNameFilterCaseProbe { public static ISelect<Customer> Apply(Metarill.Data.DatabaseSession session,ISelect<Customer> query,string key,JsonElement raw) { var filter=new KeyValuePair<string,JsonElement>(key,raw); switch(filter.Key){"+cases+"default:throw new ArgumentException();} return query; }}");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.NameFilters.Generated.SelectionNameFilterCaseProbe"));await RunProbe(output,"scalar");
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原单选按实际物理父字典筛选已维护的名称副本，弹窗和 Excel 保留同一发布范围。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Stored_project_selection_searches_the_same_published_name_cache(bool independentParent) =>
        VerifyStoredProjectCache(independentParent,targetHasParent:true);

    /// <summary>无父字典的固定状态也参与物理名称副本筛选、保存和维护。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Stored_project_selection_uses_fixed_page_without_target_parent(bool unparentedSource) =>
        VerifyStoredProjectCache(unparentedSource,targetHasParent:false);

    private static async Task VerifyStoredProjectCache(bool independentParent,bool targetHasParent)
    {
        const string ns = "Example.StoredSelectionFilter";
        var catalog = RedundantMultiNameCacheTests.Published(RelationOptionsContractTests.Catalog());
        var owner = catalog.ConfigTables.Single(table=>table.Id=="2");
        var target = catalog.ConfigTables.Single(table=>table.Id=="3");
        owner.IsRongyu=target.IsRongyu=1;
        var foreign = catalog.ConfigColumns.Single(column=>column.Id=="25");
        foreign.ControlKind="SelectPage";
        foreign.ControlDetail="""{"pageId":"301","tableId":"3","nameField":"name"}""";
        var sourcePage = catalog.ConfigPages.Single(page=>page.Id=="201");
        sourcePage.IsImport=sourcePage.IsExport=1;
        sourcePage.Extra["exportMethod"]=JsonSerializer.SerializeToElement(independentParent?"edit":"list");
        sourcePage.Extra["importMethod"]=JsonSerializer.SerializeToElement(independentParent?"OverWriteByFirstColumn":"OnlyNew");
        if(independentParent)
        {
            sourcePage.WhereSql="yz_project_id=@current_yz_project_id";
            catalog.ConfigPageColumns.Add(new(){Id="20126",YzProjectId=catalog.YzProjectId,ConfigPageId="201",ConfigColumnId="26",IsShow=1,IsEdit=1});
        }
        var targetPage = catalog.ConfigPages.Single(page=>page.Id=="301");
        targetPage.Extra["type"]=JsonSerializer.SerializeToElement(1);
        if(!targetHasParent) targetPage.WhereSql="yz_project_id=@current_yz_project_id";
        targetPage.WhereSql+=" AND status=1";
        catalog.ConfigColumns.AddRange([
            new(){Id="27",ConfigTableId="2",ColumnName="target_name",LogicalType="string",YzProjectId=catalog.YzProjectId,
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("target.name")}},
            new(){Id="37",ConfigTableId="3",ColumnName="status",LogicalType="int32",YzProjectId=catalog.YzProjectId}]);
        catalog.ConfigPageColumns.Add(new(){Id="20127",ConfigPageId="201",ConfigColumnId="27",YzProjectId=catalog.YzProjectId,IsShow=1,IsEdit=0});
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25")
            .Extra["isSearch"]=JsonSerializer.SerializeToElement(1);

        var published=ProjectScopedSingleRelationTarget.Resolve(catalog,owner,foreign,target);
        Assert.NotNull(published);
        Assert.Equal("301",published.Value.Page.Id);
        Assert.Equal("32",published.Value.Name.Id);
        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single();
        var filter=SelectionNameFilterCompilation.ForPage(model,source,page);
        Assert.Empty(filter.Gaps);
        var binding=Assert.Single(filter.Bindings);
        Assert.Equal("targetName",binding.FilterKey);
        Assert.Equal("27",binding.StorageColumn.Source.Id);
        Assert.Equal(SelectionNameFilterKind.ScalarLike,binding.Kind);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-stored-selection-filter");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,ns);
            var serviceCode=await File.ReadAllTextAsync(Path.Combine(output,"Services/RelationSourceService.g.cs"));
            Assert.Contains("""StringNameQuery.ApplyLike(session, query, entity => entity.TargetName, value)""",serviceCode);
            Assert.Contains("ImportRelation201_25Async",serviceCode);
            Assert.Contains("Export201Async",serviceCode);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,
                ()=>throw new InvalidOperationException("离线原单选筛选不连接数据库。")).UseAutoSyncStructure(false).Build();
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Database=offline;User ID=unused;"},orm);
            var mapper=Activator.CreateInstance(assembly.GetType(ns+".Generated.Mappers.RelationSourceMapper",true)!,session)!;
            var serviceType=assembly.GetType(ns+".Generated.Services.RelationSourceService",true)!;
            var arguments=new object?[serviceType.GetConstructors().Single().GetParameters().Length];
            arguments[0]=session;arguments[1]=mapper;arguments[2]=new SnowflakeIdGenerator(845);arguments[3]=Array.Empty<IRecordMutationObserver>();
            var service=Activator.CreateInstance(serviceType,arguments)!;
            var actor=new RuntimeActor(8,"离线名称筛选",false,catalog.YzProjectId,"5");
            var request=new PageRequest{ParentId=independentParent?null:"71",Filters=new(){["targetName"]=JsonSerializer.SerializeToElement("引号_'%")}};
            var query=serviceType.GetMethod("Query201",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[request,actor])!;
            var entityType=assembly.GetType(ns+".Generated.Entities.RelationSource",true)!;
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
            var stored=plans["2"]!;
            var read=stored.GetType().GetProperty("Read")!.GetValue(stored)!;
            var nameSql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["target_name"]));
            var joins=Assert.IsType<string>(read.GetType().GetProperty("From",hidden)!.GetValue(read));
            Assert.Contains("`relation_target`",joins);
            if(targetHasParent) Assert.Contains("`parent_id`",joins+nameSql);
            else Assert.DoesNotContain("`parent_id`",nameSql);
            Assert.Contains("`status`",joins+nameSql);
            var references=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("PhysicalReferences",hidden)!.GetValue(read));
            Assert.Contains(references.Cast<object>(),item =>
            {
                var type=item.GetType();
                var column=type.GetProperty("Column")!.GetValue(item)!;
                return Equals("relation_target",type.GetProperty("Table")!.GetValue(item)) &&
                    Equals("status",column.GetType().GetProperty("Name")!.GetValue(column));
            });
            var sql=(string)typeof(SelectionNameFilterCompilationTests).GetMethod(nameof(FilterSql),BindingFlags.Static|BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType).Invoke(null,[query])!;
            Assert.Contains("`target_name`",sql);
            Assert.Contains("LIKE",sql,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("引号_",sql);
            if(!independentParent) Assert.Contains("`parent_id`",sql);

            var front=Path.Combine(directory,"app");
            Assert.True(new ApplicationPackager().Create(catalog,front,ns,NonRedundantApplicationFixture.Root()).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(front,operation);
            var list=await File.ReadAllTextAsync(Path.Combine(front,"frontend/pages/yzp/relation_source-list-201.vue"));
            Assert.Contains("v-model='filters.targetName'",list);
            await RunProbe(output,independentParent?"stored-independent":"stored-same");
        }
        finally { if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary); }

        if(targetHasParent)return;
        targetPage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");
        Assert.Null(ProjectScopedSingleRelationTarget.Resolve(catalog,owner,foreign,target));
        targetPage.Extra.Remove("exactDataManageMatchField");

        var fixedScope=targetPage.WhereSql;
        targetPage.WhereSql="yz_project_id=@current_yz_project_id AND status=@current_store_id";
        Assert.Null(ProjectScopedSingleRelationTarget.Resolve(catalog,owner,foreign,target));
        targetPage.WhereSql=fixedScope;

        catalog.ConfigColumns.Single(column=>column.Id=="27").Extra.Remove("isJoinColumn");
        Assert.Null(ProjectScopedSingleRelationTarget.Resolve(catalog,owner,foreign,target));
    }

    /// <summary>只渲染生成查询，不执行命令。</summary>
    private static string FilterSql<T>(object query) where T:class => ((FreeSql.ISelect<T>)query).ToSql("1");

    /// <summary>多选按已证明Names列给出JSON数组绑定，没有方言不得退成序列化文本LIKE。</summary>
    [Fact]
    public async Task Redundant_multi_names_use_shared_original_key_and_require_real_json_dialect()
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog();catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);var model=GenerationModel.Create(catalog,"Example.MultiNameFilters");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);var binding=Assert.Single(plan.Bindings);Assert.Empty(plan.Gaps);Assert.Equal("targetNames",binding.FilterKey);Assert.Equal("27",binding.StorageColumn.Source.Id);Assert.Equal(SelectionNameFilterKind.JsonArrayAnyLike,binding.Kind);Assert.Throws<InvalidOperationException>(()=>SelectionNameFilterCompilation.RenderCases(plan));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-name-filter-compiler");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,output,"Example.MultiNameFilters");Assert.Contains("v-model='filters.targetNames'",File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/relation_source-list-201.vue")));await RunProbe(output,"multi");}
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>同名物理列独立开放筛选时保留显式缺口，BackendOnly可编译但Full不承诺已交付。</summary>
    [Fact]
    public async Task Name_key_collision_generates_one_rejection_case_and_shared_full_package_gap()
    {
        var catalog=GeneratedMultiSelectContractTests.Catalog();
        foreach(var id in new[]{"25","27"})catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id).Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.NameCollision");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);
        Assert.Empty(plan.Bindings);Assert.Contains("冲突",Assert.Single(plan.Gaps).Reason);
        Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId=="201"&&gap.Usage=="filter"&&gap.Reason.Contains("冲突",StringComparison.Ordinal));
        var error=Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));Assert.Contains("BackendOnly",error.Message);Assert.Contains("冲突",error.Message);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-name-filter-collision");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var result=new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NameCollision");Assert.Contains(result.Warnings,warning=>warning.Contains("冲突",StringComparison.Ordinal));
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/RelationSourceService.g.cs"));Assert.Single(System.Text.RegularExpressions.Regex.Matches(service,"case \"targetNames\":"));Assert.Contains(JsonSerializer.Serialize(plan.Gaps.Single().Reason),service);
            using var capabilities=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"generated/generation-capabilities.json")));Assert.Contains(capabilities.RootElement.GetProperty("unsupportedControls").EnumerateArray(),gap=>gap.GetProperty("usage").GetString()=="filter"&&gap.GetProperty("reason").GetString()!.Contains("冲突",StringComparison.Ordinal));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var controller=assembly.GetType("Example.NameCollision.Generated.Controllers.RelationSourcePage201Controller",true)!;
            using var description=JsonDocument.Parse(controller.GetCustomAttribute<GeneratedPageFiltersAttribute>()!.SchemaJson);
            Assert.False(description.RootElement.GetProperty("properties").TryGetProperty("targetNames",out _));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>真实原Vue的页级控件覆盖必须与固定Query键一致；正向未知种类显式缺口，反向只保留源ID。</summary>
    [Theory]
    [InlineData("", "SelectPage")][InlineData("", "MultiSelectPage")]
    [InlineData("List", "SelectPage")][InlineData("List", "MultiSelectPage")]
    [InlineData("SelectPage", "Text")][InlineData("MultiSelectPage", "Text")]
    public async Task Effective_filter_kind_matrix_keeps_original_vue_keys_and_fixed_backend_boundaries(string sourceKind,string pageKind)
    {
        var multi=sourceKind=="MultiSelectPage";var catalog=multi?GeneratedMultiSelectContractTests.Catalog():ScalarCatalog();var tableId=multi?"2":"1";var pageId=multi?"201":"101";var columnId=multi?"25":"21";
        var source=catalog.ConfigColumns.Single(column=>column.Id==columnId);source.ControlKind=sourceKind;var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==pageId&&field.ConfigColumnId==columnId);settings.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);settings.Extra["controlKind"]=JsonSerializer.SerializeToElement(pageKind);
        var reverse=pageKind=="Text";var model=GenerationModel.Create(catalog,"Example.FilterKind");var table=model.Tables.Single(table=>table.Source.Id==tableId);var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);
        Assert.Empty(plan.Bindings);if(reverse)Assert.Empty(plan.Gaps);else Assert.Contains("页级controlKind",Assert.Single(plan.Gaps).Reason);
        var filterGaps=GenerationCapabilityAudit.Collect(model).Where(gap=>gap.PageId==pageId&&gap.Usage=="filter").ToArray();
        if(reverse){Assert.Empty(filterGaps);new FrontendPackager().Validate(catalog);}else{Assert.Contains(filterGaps,gap=>gap.Reason.Contains("页级controlKind",StringComparison.Ordinal));Assert.Contains("页级controlKind",Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog)).Message);}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-filter-kind-matrix");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"generated");
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.FilterKind");var service=File.ReadAllText(Path.Combine(output,"Services",table.Name+"Service.g.cs"));var key=multi?"targetIds":"groupId";var name=multi?"targetNames":"groupName";
            Assert.Contains("case \""+key+"\":",service);if(reverse)Assert.DoesNotContain("case \""+name+"\":",service);else Assert.Contains("case \""+name+"\":",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            // 构造真实生成Query但不执行SQL，连接串仅作离线ORM语法解析；不连接本机或共享数据库。
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=127.0.0.1;Database=metarill_business_test;User ID=unused;Password=unused;"});
            var mapper=Activator.CreateInstance(assembly.GetType("Example.FilterKind.Generated.Mappers."+table.Name+"Mapper",true)!,[session])!;var type=assembly.GetType("Example.FilterKind.Generated.Services."+table.Name+"Service",true)!;
            var args=new object?[type.GetConstructors().Single().GetParameters().Length];args[0]=session;args[1]=mapper;args[2]=new SnowflakeIdGenerator(863);args[3]=Array.Empty<IRecordMutationObserver>();var instance=Activator.CreateInstance(type,args)!;
            var query=type.GetMethod("Query"+pageId,BindingFlags.Instance|BindingFlags.NonPublic)!;var actor=new RuntimeActor(863,"离线有效控件合同",false,catalog.YzProjectId,"1");var request=new PageRequest{ParentId="9007199254742000",Filters=new(){[reverse?key:name]=JsonSerializer.SerializeToElement("9007199254740993")}};
            if(reverse)
            {
                Assert.NotNull(query.Invoke(instance,[request,actor]));
                if(!multi){request.Filters[key]=JsonSerializer.SerializeToElement("非法ID文本");Assert.IsType<FormatException>(Assert.Throws<TargetInvocationException>(()=>query.Invoke(instance,[request,actor])).InnerException);}
            }
            else Assert.Contains("页级controlKind",Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(()=>query.Invoke(instance,[request,actor])).InnerException).Message);
            await RunProbe(output,(multi?"multi":"scalar")+(reverse?"-id":""));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>原筛选复选分支优先使用源字段。单选和多选都保持源键。</summary>
    [Theory]
    [InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Checkbox_filter_priority_uses_the_source_field_without_enabling_name_search(bool multi,bool textOverride)
    {
        var catalog=multi?GeneratedMultiSelectContractTests.Catalog():ScalarCatalog();var tableId=multi?"2":"1";var pageId=multi?"201":"101";var columnId=multi?"25":"21";
        catalog.ConfigColumns.Single(column=>column.Id==columnId).Extra["filterControlKind"]=JsonSerializer.SerializeToElement("CheckBox");catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==pageId&&field.ConfigColumnId==columnId).Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        if(textOverride)catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==pageId&&field.ConfigColumnId==columnId).Extra["controlKind"]=JsonSerializer.SerializeToElement("Text");
        var model=GenerationModel.Create(catalog,"Example.CheckboxPriority");var table=model.Tables.Single(table=>table.Source.Id==tableId);var page=table.Pages.Single();var plan=SelectionNameFilterCompilation.ForPage(model,table,page);Assert.Empty(plan.Bindings);Assert.Empty(plan.Gaps);
        Assert.DoesNotContain(GenerationCapabilityAudit.Collect(model), gap => gap.PageId == pageId && gap.Usage == "filter");
        new FrontendPackager().Validate(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-filter-checkbox-priority");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,output,"Example.CheckboxPriority");var key=multi?"targetIds":"groupId";var name=multi?"targetNames":"groupName";var stem=multi?"relation_source":"customer";
            var vue=File.ReadAllText(Path.Combine(output,"frontend/pages/yzp/"+stem+"-list-"+pageId+".vue"));Assert.Contains("multiple v-model='filters."+key+"'",vue);Assert.DoesNotContain("v-model='filters."+name+"'",vue);
            Assert.DoesNotContain("case \""+name+"\":",File.ReadAllText(Path.Combine(output,"Services",table.Name+"Service.g.cs")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }

    /// <summary>源字段显式允许筛选也不能替代存储/投影/目标或非冗余读计划的证明。</summary>
    [Theory]
    [InlineData("missing")][InlineData("hidden")][InlineData("join")][InlineData("type")][InlineData("nonredundant")][InlineData("targetHidden")][InlineData("override")][InlineData("scope")][InlineData("idFallback")][InlineData("targetOwnership")]
    public void Unsupported_metadata_produces_explicit_gap_without_guessing_name_columns(string scenario)
    {
        var catalog=ScalarCatalog();var storage=catalog.ConfigColumns.Single(column=>column.Id=="22");var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="21");
        if(scenario=="missing"){catalog.ConfigColumns.Remove(storage);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="22");}
        if(scenario=="hidden"){var settings=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="22");settings.IsShow=0;settings.IsEdit=0;}
        if(scenario=="join")storage.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(scenario=="type")storage.LogicalType="int64";
        if(scenario=="nonredundant")catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=0;
        if(scenario=="targetHidden")catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201");
        if(scenario=="override")field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"nameField\":\"alias\"}");
        if(scenario=="scope")field.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"whereSql\":\"name like @input\"}");
        if(scenario=="targetOwnership"){catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="";}
        if(scenario=="idFallback")catalog.ConfigColumns.Single(column=>column.Id=="21").ControlDetail="{\"pageId\":\"201\",\"nameField\":\"id\"}";
        var model=GenerationModel.Create(catalog,"Example.NameGap");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());Assert.Empty(plan.Bindings);Assert.Single(plan.Gaps);Assert.Contains("名称筛选尚未支持",plan.Gaps[0].Reason);Assert.Empty(plan.ConsumedKeys);Assert.Throws<InvalidOperationException>(()=>SelectionNameFilterCompilation.RenderCases(plan));
    }
    /// <summary>两个来源控件争用同一辅助列时全部拒绝，不依顺序选中其中一个。</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Ambiguous_alias_has_no_generated_case(bool invalidFirst)
    {
        var catalog=ScalarCatalog();catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="21").Extra.Remove("isSearch");
        catalog.ConfigColumns.Add(new(){Id="205",ConfigTableId="2",ColumnName="full_name",LogicalType="string"});catalog.ConfigPageColumns.Add(new(){Id="1205",ConfigPageId="201",ConfigColumnId="205",IsShow=1});
        foreach(var tuple in new[]{("31","a_id","full_name"),("32","a_full_id","name")}){catalog.ConfigColumns.Add(new(){Id=tuple.Item1,ConfigTableId="1",ColumnName=tuple.Item2,LogicalType="int64",ControlKind="SelectPage",ControlDetail=JsonSerializer.Serialize(new{pageId="201",nameField=tuple.Item3}),LinkConfigTableId="2"});catalog.ConfigPageColumns.Add(new(){Id="f"+tuple.Item1,ConfigPageId="101",ConfigColumnId=tuple.Item1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});}
        if(invalidFirst)catalog.ConfigColumns.Single(column=>column.Id=="31").ControlDetail="{\"pageId\":\"201\",\"nameField\":\"full_name\",\"whereSql\":\"name like @input\"}";
        catalog.ConfigColumns.Add(new(){Id="33",ConfigTableId="1",ColumnName="a_full_name",LogicalType="string"});catalog.ConfigPageColumns.Add(new(){Id="f33",ConfigPageId="101",ConfigColumnId="33",IsShow=1,IsEdit=0});var model=GenerationModel.Create(catalog,"Example.AliasGap");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());Assert.Empty(plan.Bindings);Assert.Equal(2,plan.Gaps.Count);Assert.All(plan.Gaps,gap=>Assert.Contains(invalidFirst?"名称筛选":"多个来源",gap.Reason));
    }
    /// <summary>辅助名来自原字段定义，合法页buttonText覆盖不改输入键或稳定名称。</summary>
    [Fact]
    public void Button_only_overlay_preserves_stable_name_binding()
    {
        var catalog=ScalarCatalog();catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="21").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"buttonText\":\"选择分组\"}");var model=GenerationModel.Create(catalog,"Example.NameOverlay");var table=model.Tables.Single(table=>table.Source.Id=="1");var plan=SelectionNameFilterCompilation.ForPage(model,table,table.Pages.Single());Assert.Empty(plan.Gaps);Assert.Equal(SelectionDisplayFieldCompilation.Resolve(table,table.Columns.Single(column=>column.Source.Id=="21")).JsonName,Assert.Single(plan.Bindings).FilterKey);
    }
    /// <summary>使用真正元数据模型构造单选可读名称物理列，不手写业务实体。</summary>
    private static MetadataCatalog ScalarCatalog()
    {
        var catalog=GeneratorTests.Catalog();catalog.ConfigColumns.AddRange([new(){Id="21",ConfigTableId="1",ColumnName="group_id",LogicalType="int64",ControlKind="SelectPage",ControlDetail="{\"pageId\":\"201\",\"nameField\":\"name\"}",LinkConfigTableId="2"},new(){Id="22",ConfigTableId="1",ColumnName="group_name",LogicalType="string"}]);catalog.ConfigPageColumns.AddRange([new(){Id="121",ConfigPageId="101",ConfigColumnId="21",IsShow=1,IsEdit=1,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}},new(){Id="122",ConfigPageId="101",ConfigColumnId="22",IsShow=1,IsEdit=0}]);catalog.ConfigTables.Add(new(){Id="2",TableName="group_item"});catalog.ConfigColumns.AddRange([new(){Id="201",ConfigTableId="2",ColumnName="id",LogicalType="int64"},new(){Id="202",ConfigTableId="2",ColumnName="name",LogicalType="string"},new(){Id="203",ConfigTableId="2",ColumnName="yz_project_id",LogicalType="int64"}]);catalog.ConfigPages.Add(new(){Id="201",ConfigTableId="2",WhereSql="yz_project_id=@current_yz_project_id",Extra=new(){["type"]=JsonSerializer.SerializeToElement(1)}});catalog.ConfigPageColumns.Add(new(){Id="1202",ConfigPageId="201",ConfigColumnId="202",IsShow=1});return catalog;
    }
    /// <summary>运行实际生成原列表，HTTP只记录公开请求合同，不冒称后端过滤已经接线。</summary>
    private static async Task RunProbe(string output,string scenario,[CallerFilePath]string file="")
    {
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};start.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(file)!,"SelectionNameFilterProbe.mjs"));start.ArgumentList.Add(output);start.ArgumentList.Add(scenario);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
}
