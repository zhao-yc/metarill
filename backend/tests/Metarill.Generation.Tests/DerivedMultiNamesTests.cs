using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>多选名称可以来自R0目标的单值联接，原ID数组与名称数组合同保持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedMultiNamesTests
{
    /// <summary>多选确认、写入和名称反查使用同一目标页的精确范围，匹配字段可以不出现在页面上。</summary>
    [Theory]
    [InlineData(0,1,"physical")]
    [InlineData(1,1,"physical")]
    [InlineData(1,1,"account")]
    [InlineData(1,0,"derived-name")]
    [InlineData(0,0,"derived-name")]
    [InlineData(1,0,"derived-match")]
    public async Task Multi_selection_save_and_import_use_target_exact_scope(int sourceMode,int targetMode,string kind)
    {
        const string ns="Example.MultiExactRange";
        var catalog=Catalog("multi_exact_range",false);
        catalog.ConfigTables.Single(item=>item.Id=="2").IsRongyu=sourceMode;
        catalog.ConfigTables.Single(item=>item.Id=="3").IsRongyu=targetMode;
        var derivedName=kind.Contains("derived-name",StringComparison.Ordinal);
        var derivedMatch=kind=="derived-match";
        var account=kind.StartsWith("account",StringComparison.Ordinal);
        var name=catalog.ConfigColumns.Single(item=>item.Id=="32");
        if(!derivedName)name.Extra.Clear();
        var scopeColumn=new ColumnMetadata{Id="381",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
            ColumnName="scope_code",LogicalType="string"};
        if(derivedMatch)
        {
            scopeColumn.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            scopeColumn.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        }
        catalog.ConfigColumns.Add(scopeColumn);
        catalog.ConfigColumns.Add(new(){Id="382",ConfigTableId="3",YzProjectId=catalog.YzProjectId,
            ColumnName="store_id",LogicalType="int64"});
        var target=catalog.ConfigPages.Single(item=>item.Id=="301");
        target.WhereSql=account?"store_id=@current_store_id":"";
        target.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("scope_code");
        // 旧测试目录使用相同的两个后台记录，所有候选页均应读取同一固定账号字段。
        foreach(var admin in catalog.YzAdminTypes)
            admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");

        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=table.Pages.Single(item=>item.Source.Id=="201");
        var resolution=MultiSelectCompilation.Resolve(model,table,page,table.Columns.Single(item=>item.Source.Id=="25"));
        Assert.True(resolution.Definition is not null,resolution.Reason);
        var exact=Assert.IsType<ExactDataManageModel>(resolution.Definition.ExactTarget);
        Assert.Equal("scope_code",exact.Column.Source.ColumnName);
        Assert.Equal(derivedMatch,exact.Derived);
        Assert.DoesNotContain(model.Tables.Single(item=>item.Source.Id=="3").Pages.Single(item=>item.Source.Id=="301").Readable,
            item=>item.Source.ColumnName=="scope_code");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-exact-range");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,generated,ns);
            var serviceCode=await File.ReadAllTextAsync(Path.Combine(generated,"Services/MultiExactRangeSourceService.g.cs"));
            var saveStart=serviceCode.IndexOf("private async Task ValidateMulti201_25Async",StringComparison.Ordinal);
            var importStart=serviceCode.IndexOf("private async Task<JsonElement> ImportMulti201_25Async",StringComparison.Ordinal);
            Assert.True(saveStart>=0&&importStart>=0);
            var save=serviceCode[saveStart..];
            var import=serviceCode[importStart..];
            Assert.Contains("""multiExactAuthorization.ResolveExactDataManage("301", "list", "real_name")""",save);
            Assert.Contains("""multiImportExactAuthorization.ResolveExactDataManage("301", "list", "real_name")""",import);
            Assert.Contains(derivedMatch?"ExactDataManageSql.ApplyDerived(query, session, global::Example.MultiExactRange.Generated.Services.MultiExactRangeTargetService.ExactReadPlan301":
                "ExactDataManageSql.Apply(query, session, \"scope_code\"",save);
            Assert.Contains(derivedName?"filters.Add(new(\"scope_code\", FixedReadComparison.In":
                derivedMatch?"ExactDataManageSql.ApplyDerived(query, session":"ExactDataManageSql.Apply(query, session, \"scope_code\"",import);
            Assert.True(import.IndexOf("multiImportExactAuthorization.ResolveExactDataManage",StringComparison.Ordinal)<
                import.IndexOf("var matches=",StringComparison.Ordinal));
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType(ns+".Generated.Services.MultiExactRangeSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            if(derivedName)
            {
                var plan=Assert.IsType<NonRedundantReadPlan>(type.GetField("MultiDerivedPlan201_25",
                    System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                var sql=plan.GetType().GetProperty("MySql",HiddenInstance)!.GetValue(plan)!;
                var fields=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(sql.GetType().GetProperty("Expressions",HiddenInstance)!.GetValue(sql));
                Assert.Contains("scope_code",fields.Keys);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>实际生成编译，同时证明显式与自动名称不落物理列。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Derived_multi_names_compile_in_original_crud_and_excel(bool automatic)
    {
        var catalog=Catalog("derived_multi",automatic);var before=JsonSerializer.Serialize(catalog);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedMulti");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.DerivedMulti.Generated.Services.DerivedMultiSourceService"));
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.DoesNotContain("target_names",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="2")).PhysicalColumns.Select(column=>column.ColumnName));
            Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(catalog,catalog.ConfigTables.Single(table=>table.Id=="3")).PhysicalColumns.Select(column=>column.ColumnName));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.DerivedMulti")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>固定整数终点参与候选、原 Excel、名称筛选和缓存闭包；真实生成程序集必须可初始化。</summary>
    [Theory]
    [InlineData(false,0,true,"int32","list")]
    [InlineData(false,1,false,"int64","edit")]
    [InlineData(true,0,false,"int64","edit")]
    [InlineData(true,1,true,"int32","list")]
    [InlineData(false,1,false,"decimal","edit")]
    [InlineData(true,0,true,"time","list")]
    [InlineData(false,1,true,"double","edit")]
    public async Task Derived_scalar_labels_complete_both_array_controls(bool checkbox,int mode,bool automatic,string type,string exportMode)
    {
        const string prefix="derived_integer",ns="Example.DerivedInteger";
        var catalog=Catalog(prefix,automatic);
        catalog.ConfigTables.Single(item=>item.Id=="2").IsRongyu=mode;
        var label=catalog.ConfigColumns.Single(item=>item.Id=="32");label.LogicalType=type;
        label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.reference_code");
        catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",YzProjectId=catalog.YzProjectId,ColumnName="reference_code",LogicalType=type});
        if(checkbox)
        {
            var control=catalog.ConfigColumns.Single(item=>item.Id=="25");
            control.ControlKind="MultiCheckBoxDatabase";
            control.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\",\"whereSql\":\"parent_id=@parentId\"}";
        }
        var sourcePage=catalog.ConfigPages.Single(item=>item.Id=="201");
        sourcePage.Extra["exportMethod"]=JsonSerializer.SerializeToElement(exportMode);
        var field=catalog.ConfigPageColumns.Single(item=>item.ConfigPageId=="201"&&item.ConfigColumnId=="25");
        field.IsEdit=field.IsShow=1;field.Extra["isDetailShow"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(item=>item.Source.Id=="2");var page=source.Pages.Single(item=>item.Source.Id=="201");
        Assert.Empty(GenerationCapabilityAudit.Collect(model));new FrontendPackager().Validate(catalog);
        var expectedType=type switch{"int32"=>"int?","int64"=>"long?","decimal"=>"decimal?",_=>"TimeSpan?"};
        if(checkbox)
        {
            var def=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,source,page));
            Assert.True(def.DerivedName);Assert.True(def.TypedLabel);
            Assert.Contains($"NonRedundantReads.Value<{expectedType}>",DatabaseCheckboxRenderer.Service(model,source,page));
        }
        else
        {
            var def=Assert.Single(MultiSelectCompilation.ForPage(model,source,page));
            Assert.True(def.TypedLabel);Assert.Single(SelectionNameFilterCompilation.ForPage(model,source,page).Bindings);
        }
        if(mode==0||!checkbox)
            Assert.Contains(type switch{"int32"=>"int.TryParse(label","int64"=>"long.TryParse(label",_=>"MultiValueLabel.Parse"},ImportRenderer.Service(model,source,page));
        if(mode==1)
        {
            var maintenance=MixedRelationPropagationCompilation.ForModel(model);
            Assert.Contains("target_names",maintenance.MixedSourceColumns[source.Source.TableName]);
            Assert.Contains(prefix+"_company",maintenance.MixedTargets);
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-integer-names");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.DerivedIntegerSourceService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import201Async"));Assert.NotNull(service.GetMethod("Export201Async"));

            // 联接别名与真实终点类型不一致时，完整功能审计必须拒绝。
            catalog.ConfigColumns.Single(item=>item.Id=="45").LogicalType="string";
            Assert.NotEmpty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,ns)));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>多选和复选的名称解析必须沿当前中间页读取，不借用同表的默认目标页。</summary>
    [Theory]
    [InlineData(false,0,false)]
    [InlineData(true,0,false)]
    [InlineData(false,1,false)]
    [InlineData(true,1,false)]
    [InlineData(false,0,true)]
    [InlineData(true,0,true)]
    [InlineData(false,1,true)]
    [InlineData(true,1,true)]
    public async Task Array_label_matching_uses_the_actual_nested_page(bool checkbox,int storage,bool globalAccount)
    {
        const string ns="Example.DerivedArrayScope";
        var catalog=Catalog("derived_array_scope",false);
        var source=catalog.ConfigTables.Single(table=>table.Id=="2");
        source.IsRongyu=storage;
        var companyTable=catalog.ConfigTables.Single(table=>table.Id=="4");
        companyTable.IsRongyu=0;
        var company=catalog.ConfigColumns.Single(column=>column.Id=="37");
        company.ControlDetail=JsonSerializer.Serialize(new{tableId="4",pageId="401",nameField="name",whereSql="status=17"});
        var companyPage=catalog.ConfigPages.Single(page=>page.Id=="401");
        companyPage.WhereSql+=" AND status>=1";
        catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="status",LogicalType="int32"});
        if(globalAccount)
        {
            companyTable.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="43");
            catalog.ConfigColumns.Add(new(){Id="46",ConfigTableId="4",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
            companyPage.WhereSql="status>=1 AND store_id=@current_store_id";
            foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id))
                sourcePage.WhereSql+=" AND store_id=@current_store_id";
        }
        catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="301"&&field.ConfigColumnId=="37")
            .Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"whereSql\":\"status=3\"}");
        if(checkbox)
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
            control.ControlKind="MultiCheckBoxDatabase";
            control.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\",\"whereSql\":\"parent_id=@parentId\"}";
        }
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-array-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.DerivedArrayScopeSourceService",true)!;
            var fieldName=checkbox?"DerivedCheckboxPlan201_25":"MultiDerivedPlan201_25";
            var plan=service.GetField(fieldName,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)?.GetValue(null);
            Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(plan);
            AssertActualPage(plan);
            if(storage==0)
            {
                var pageRead=service.GetField("ReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null);
                var read=AssertActualPage(pageRead);
                var filters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(read.GetType()
                    .GetProperty("ArrayNamePredicates",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(read));
                Assert.Contains(".`status`",filters["target_names"]);
                AssertActualPage(service.GetField("ExportReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
            }
            else
            {
                if(!checkbox)
                    AssertActualPage(service.GetField("MultiCachePlan201_25_0",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType()
                    .GetProperty("Plans",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(maintenance));
                var maintenancePlan=plans[source.Id]!;
                var read=maintenancePlan.GetType().GetProperty("Read")!.GetValue(maintenancePlan)!;
                AssertConstants(read);
                Assert.Contains("derived_array_scope_company",maintenance.MixedTargets);
                if(globalAccount)
                {
                    const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                    var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["target_names"]));
                    Assert.Contains("b.`store_id`",expression);
                }
            }

            object AssertActualPage(object? value)
            {
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var fixedPlan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(value);
                Assert.Equal(globalAccount,fixedPlan.RequiresAccount);
                var bound=fixedPlan.BindAccount(new Metarill.Core.Security.RuntimeActor(7,"sample",false,catalog.YzProjectId,"5"){StoreId=23});
                var read=bound.GetType().GetProperty("MySql",hidden)!.GetValue(bound)!;
                var constants=AssertConstants(read);
                if(globalAccount)Assert.Contains(23L,constants.Values);
                return read;
            }

            static IReadOnlyDictionary<string,object?> AssertConstants(object read)
            {
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(read.GetType()
                    .GetProperty("Constants",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(read));
                Assert.Contains(3,constants.Values);
                Assert.DoesNotContain(17,constants.Values);
                return constants;
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>保留原弹窗范围，名称导入额外使用当前页的固定条件和整行联动值。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,true)]
    [InlineData(false,true,true)]
    [InlineData(false,false,false,true)]
    public async Task R0_multi_excel_applies_control_conditions_in_addition_to_the_target_page(bool derived,bool overlay,
        bool targetAccount=false,bool joinedCondition=false)
    {
        const string ns="Example.MultiImportScope";
        var catalog=Catalog("multi_import_scope",false);
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        source.ControlDetail=JsonSerializer.Serialize(new{pageId="301",nameField="name",whereSql=Where(31)});
        var field=catalog.ConfigPageColumns.Single(column=>column.ConfigPageId=="201"&&column.ConfigColumnId==source.Id);
        if(overlay)field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql=Where(32)}));
        if(!derived)
        {
            var name=catalog.ConfigColumns.Single(column=>column.Id=="32");
            name.Extra.Remove("isJoinColumn");
            name.Extra.Remove("columnFullName");
        }
        catalog.ConfigColumns.AddRange([
            new(){Id="28",ConfigTableId="2",ColumnName="segment",LogicalType="string"},
            new(){Id="38",ConfigTableId="3",ColumnName="segment",LogicalType="string"},
            new(){Id="39",ConfigTableId="3",ColumnName="sheet_status",LogicalType="int32"},
            new(){Id="310",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"}]);
        if(joinedCondition)
        {
            catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="segment",LogicalType="string"});
            var segment=catalog.ConfigColumns.Single(column=>column.Id=="38");
            segment.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            segment.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.segment");
        }
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"))
            catalog.ConfigPageColumns.Add(new(){Id=page.Id+"28",ConfigPageId=page.Id,ConfigColumnId="28",IsEdit=1,IsShow=1});
        var target=catalog.ConfigPages.Single(page=>page.Id=="301");
        target.WhereSql+=" AND sheet_status<>99";
        if(targetAccount)target.WhereSql+=" AND store_id=@current_store_id";
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page201=table.Pages.Single(item=>item.Source.Id=="201");
        var resolved=MultiSelectCompilation.Resolve(model,table,page201,table.Columns.Single(item=>item.Source.Id=="25"));
        Assert.True(resolved.Definition is not null,resolved.Reason);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-multi-import-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var generated=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/MultiImportScopeSourceService.g.cs"));
            var importAt=generated.IndexOf("private async Task<JsonElement> ImportMulti201_25Async",StringComparison.Ordinal);
            Assert.True(importAt>=0);
            var importCode=generated[importAt..];
            Assert.Contains("MultiImportCondition201_25.BindVariables",importCode);
            Assert.Contains("relationContext[\"segment\"]",importCode);
            Assert.Contains("AccountConditionVariables.Require(actor, \"current_store_id\")",importCode);
            Assert.Contains(targetAccount||joinedCondition?"condition:importCondition,currentRead:true":
                derived?"condition:importCondition.And(MultiPageCondition201_25)":
                "importCondition.ApplyConstants(query,session,\"multi_control_import_201_25\")",importCode);
            var untilImport=generated[..importAt];
            Assert.DoesNotContain("MultiImportCondition201_25.BindVariables",untilImport);
            Assert.Equal("segment",Assert.Single(ImportDependencyCompilation.ForPage(model,table,page201).Dependencies[source.Id]).Source.ColumnName);

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.MultiImportScopeSourceService",true)!;
            const System.Reflection.BindingFlags staticPrivate=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var condition=Assert.IsType<Metarill.Data.Queries.FixedConditionSqlPlan>(service
                .GetField("MultiImportCondition201_25",staticPrivate)!.GetValue(null));
            var bound=condition.BindVariables(new Dictionary<string,object?>
            {
                ["parentId"]="9007199254741197",["current_store_id"]=23L,["formParameters.segment"]="A"
            });
            var clause=bound.GetType().GetMethod("RenderConstants",HiddenInstance)!
                .Invoke(bound,[new Metarill.Data.MySqlDatabaseProvider(),"excel",new Func<string,string>(name=>name)])!;
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(clause.GetType().GetProperty("Parameters")!.GetValue(clause));
            Assert.Equal(new object?[]{overlay?32:31,9007199254741197L,23L,"A"},parameters.Values);
            if(derived||targetAccount||joinedCondition)
            {
                var readField=joinedCondition?"MultiImportReadPlan201_25":"MultiDerivedPlan201_25";
                var read=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                    .GetField(readField,staticPrivate)!.GetValue(null));
                Assert.Equal(targetAccount,read.RequiresAccount);
                var mysql=read.GetType().GetProperty("MySql",HiddenInstance)!.GetValue(read)!;
                foreach(var name in new[]{"sheet_status","segment","store_id"})
                    Assert.EndsWith(".`"+name+"`",Assert.IsType<string>(mysql.GetType()
                        .GetMethod("Expression",HiddenInstance)!.Invoke(mysql,[name])));
                if(joinedCondition)
                    Assert.Contains("LEFT JOIN `multi_import_scope_company`",Assert.IsType<string>(mysql.GetType()
                        .GetProperty("From",HiddenInstance)!.GetValue(mysql)));
            }

            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new
                {whereSql=Where(32)+" AND segment=@parentId"}));
            var rejected=MultiSelectCompilation.Resolve(model,table,page201,table.Columns.Single(item=>item.Source.Id=="25"));
            Assert.Null(rejected.Definition);
            Assert.Contains(joinedCondition?"未证明的父级条件":"父键必须沿用目标页",rejected.Reason);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        static string Where(int status)=>$"sheet_status>={status} AND parent_id=@parentId AND store_id=@current_store_id AND segment=@formParameters.segment";
    }

    /// <summary>同一 R0 名称数组在不同业务页按各自的候选页匹配、显示和导出。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    [InlineData(false,true,true)]
    [InlineData(true,true,true)]
    [InlineData(false,true,false,true)]
    [InlineData(true,true,false,true)]
    public async Task R0_array_page_override_uses_the_actual_target(bool checkbox,bool account,bool overlayOnlyAccount=false,
        bool differentSourceParents=false)
    {
        const string ns="Example.ArrayPageOverride";
        var catalog=Catalog("array_page_override",false);
        catalog.ConfigTables.Single(table=>table.Id=="4").IsRongyu=0;
        catalog.ConfigColumns.Single(column=>column.Id=="37").ControlDetail=
            "{\"tableId\":\"4\",\"pageId\":\"401\",\"nameField\":\"name\",\"whereSql\":\"status=17\"}";
        catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="status",LogicalType="int32"});
        catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="status",LogicalType="int32"});
        if(account)catalog.ConfigColumns.Add(new(){Id="39",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"});
        var original=catalog.ConfigPages.Single(page=>page.Id=="301");
        original.WhereSql+=" AND status>=31"+(account&&!overlayOnlyAccount?" AND store_id=@current_store_id":"");
        var alternative=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;
        alternative.Id="302";
        alternative.WhereSql=alternative.WhereSql.Replace("31","32",StringComparison.Ordinal);
        if(overlayOnlyAccount)alternative.WhereSql+=" AND store_id=@current_store_id";
        catalog.ConfigPages.Add(alternative);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="301").ToArray())
        {
            var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            clone.Id="302"+field.ConfigColumnId;
            clone.ConfigPageId="302";
            catalog.ConfigPageColumns.Add(clone);
        }
        foreach(var (page,minimum) in new[]{("301",3),("302",9)})
            catalog.ConfigPageColumns.Single(field=>field.ConfigPageId==page&&field.ConfigColumnId=="37")
                .Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql="status="+minimum}));
        if(checkbox)
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
            control.ControlKind="MultiCheckBoxDatabase";
            control.ControlDetail=Checkbox("301");
        }
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="202");
        sourcePage.IsImport=sourcePage.IsExport=1;
        if(differentSourceParents)
        {
            catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="secondary_parent_id",
                LogicalType="int64",LinkConfigTableId="1"});
            sourcePage.WhereSql=sourcePage.WhereSql.Replace("parent_id=", "secondary_parent_id=",StringComparison.Ordinal);
        }
        var field201=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25");
        field201.IsShow=field201.IsEdit=1;
        field201.Extra["isDetailShow"]=JsonSerializer.SerializeToElement(1);
        var field202=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field201))!;
        field202.Id="20225";
        field202.ConfigPageId="202";
        field202.Extra["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"302\"}");
        catalog.ConfigPageColumns.Add(field202);
        RedundantMultiNameCacheTests.Published(catalog);
        if (!checkbox && differentSourceParents)
        {
            // 两页可使用不同的来源外键，名称筛选和完整发布按各自页面证明范围。
            var model = GenerationModel.Create(catalog, ns);
            var source = model.Tables.Single(table => table.Source.Id == "2");
            foreach (var page in source.Pages)
            {
                var filter = SelectionNameFilterCompilation.ForPage(model, source, page);
                Assert.Empty(filter.Gaps);
                var name = Assert.Single(filter.Bindings);
                Assert.Equal("targetNames", name.FilterKey);
                Assert.Equal(SelectionNameFilterKind.JoinedJsonArrayAnyLike, name.Kind);
            }
            new FrontendPackager().Validate(catalog);
        }
        if(overlayOnlyAccount)
        {
            var target=catalog.ConfigTables.Single(item=>item.Id=="3");
            Assert.False(Metarill.Data.Queries.NonRedundantReads.IsScopedMultiTarget(catalog,target,"301"));
            Assert.True(Metarill.Data.Queries.NonRedundantReads.IsScopedMultiTarget(catalog,target,"302"));
        }
        if(checkbox)
        {
            var model=GenerationModel.Create(catalog,ns);
            var source=model.Tables.Single(table=>table.Source.Id=="2");
            var adjusted=Assert.Single(DatabaseCheckboxCompilation.ForService(model,source,source.Pages.Single(page=>page.Source.Id=="202")));
            Assert.Equal("302",adjusted.NameTargetPage!.Source.Id);
            Assert.Contains(adjusted.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.ColumnName=="parent_id");
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-r0-array-page-override");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.ArrayPageOverrideSourceService",true)!;
            var actor=new Metarill.Core.Security.RuntimeActor(7,"sample",false,catalog.YzProjectId,"5"){StoreId=23};
            foreach(var (page,condition,otherCondition,root) in new[]{("201",3,9,31),("202",9,3,32)})
            {
                var read=Read("ReadPlan"+page);
                var values=Parameters(read);
                Assert.Contains(condition,values);
                Assert.DoesNotContain(otherCondition,values);
                Assert.DoesNotContain(17,values);
                var usesAccount=account&&(!overlayOnlyAccount||page=="202");
                Assert.Equal(usesAccount,values.Contains(23L));
                if(usesAccount||!checkbox)Assert.Contains(root,values);
                var predicates=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(read.GetType()
                    .GetProperty("ArrayNamePredicates",HiddenInstance)!.GetValue(read));
                Assert.Contains(".`status`",predicates["target_names"]);
                if(differentSourceParents)
                {
                    var (current,other)=page=="201"?("parent_id","secondary_parent_id"):("secondary_parent_id","parent_id");
                    Assert.Contains("b.`"+current+"`",predicates["target_names"]);
                    Assert.DoesNotContain("b.`"+other+"`",predicates["target_names"]);
                    if (!checkbox)
                    {
                        var request = new PageRequest { ParentId = "9007199254740995" };
                        request.Filters.Add("targetNames", JsonSerializer.SerializeToElement("张三"));
                        var readFilters = service.GetMethod("ReadFilters" + page,
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                        var filters = Assert.IsAssignableFrom<IReadOnlyList<FixedReadFilter>>(
                            readFilters.Invoke(null, [request, actor]));
                        Assert.Contains(new FixedReadFilter(current, FixedReadComparison.Equal, 9007199254740995L), filters);
                        Assert.DoesNotContain(filters, filter => filter.Field == other);
                        Assert.Contains(new FixedReadFilter("target_names", FixedReadComparison.JsonArrayAnyContains, "张三"), filters);
                    }
                }

                var match=Read((checkbox?"DerivedCheckboxPlan":"MultiDerivedPlan")+page+"_25");
                Assert.Contains(condition,Parameters(match));
                Assert.DoesNotContain(otherCondition,Parameters(match));
                if(!checkbox)Assert.Contains(condition,Parameters(Read("ExportReadPlan"+page)));
                Assert.NotNull(service.GetMethod("Import"+page+"Async"));
                Assert.NotNull(service.GetMethod("Export"+page+"Async"));
            }

            object Read(string field)
            {
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                    .GetField(field,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                var bound=plan.BindAccount(actor);
                return bound.GetType().GetProperty("MySql",HiddenInstance)!.GetValue(bound)!;
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        static string Checkbox(string pageId)=>JsonSerializer.Serialize(new{tableId="3",pageId,nameField="name",whereSql="parent_id=@parentId"});
        static IEnumerable<object?> Parameters(object read)=>Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(read.GetType()
            .GetProperty("Constants",HiddenInstance)!.GetValue(read)).Values;
    }

    private const System.Reflection.BindingFlags HiddenInstance=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;

    /// <summary>同一公司表参与两个角色，原候选、Excel 和 R1 维护须源自同一固定路径。</summary>
    [Theory]
    [InlineData(false,0,"string","int64")]
    [InlineData(false,1,"int64","string")]
    [InlineData(true,0,"int64","string")]
    [InlineData(true,1,"string","string")]
    public async Task Multi_hop_labels_keep_original_array_endpoints(bool checkbox,int mode,string firstKey,string secondKey)
    {
        const string prefix="derived_roles",ns="Example.DerivedRoles";
        var catalog=Catalog(prefix,automatic:mode==1);
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=mode;
        catalog.ConfigColumns.Single(column=>column.Id=="37").LogicalType=firstKey;
        catalog.ConfigColumns.Single(column=>column.Id=="32").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.supervisor.name");
        catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="supervisor_id",LogicalType=secondKey,
            ControlKind="List",LinkConfigTableId="4",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"id\"}"});
        if(checkbox)
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id=="25");
            control.ControlKind="MultiCheckBoxDatabase";
            control.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\",\"whereSql\":\"parent_id=@parentId\"}";
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-roles");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var model=GenerationModel.Create(catalog,ns);
            Assert.Empty(GenerationCapabilityAudit.Collect(model));
            new FrontendPackager().Validate(catalog);
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var source=assembly.GetType(ns+".Generated.Services.DerivedRolesSourceService",true)!;
            var target=assembly.GetType(ns+".Generated.Services.DerivedRolesTargetService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(source.TypeHandle);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(target.TypeHandle);
            Assert.NotNull(source.GetMethod("Import201Async"));
            Assert.NotNull(source.GetMethod("Export201Async"));
            if(mode==1)
                Assert.Contains(prefix+"_company",MixedRelationPropagationCompilation.ForModel(model).MixedTargets);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>没有自树同伴的R1多选也必须独立生成编译，并发布完整物理缓存传播计划。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Redundant_multi_alone_compiles_and_publishes_derived_cache(bool automatic)
    {
        var c=Catalog("r1_multi",automatic);c.ConfigTables.Single(t=>t.Id=="2").IsRongyu=1;var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.R1Multi");var source=model.Tables.Single(t=>t.Source.Id=="2");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Contains("target_names",MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns[source.Source.TableName]);Assert.Contains("target_names",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(t=>t.Id=="2")).PhysicalColumns.Select(x=>x.ColumnName));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-derived-multi-alone");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),model.Namespace);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType(model.Namespace+".Generated.Services.R1MultiSourceService"));Assert.Equal(before,JsonSerializer.Serialize(c));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整包按原源码构建并挂载原Vue，更新不覆盖手写partial或组件。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_derived_multi_package_preserves_original_vue_and_handwritten_service(bool automatic)
    {
        var root=NonRedundantApplicationFixture.Root();var catalog=Catalog("relation",automatic);catalog.ConfigMenus.Add(new(){Id="9001",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",Name="来源",Kind="GENERATED_PAGE",ConfigPageId="201"});var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog,output,"Example.DerivedMultiApplication",root).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var component in new[]{"common/yzp_runtime/multiSelectFieldMixin.js","common/yzp_runtime/selectionPageMixin.js"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(Path.Combine(root,"backend/tests/Metarill.Generation.Tests/GeneratedMultiSelectVueProbe.mjs"));start.ArgumentList.Add(output);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var subset=Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),item=>item.GetProperty("controlKind").GetString()=="MultiSelectPage");Assert.True(subset.GetProperty("derivedNameSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 派生多选手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.DerivedMultiApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>坏名称、隐藏声明、派生范围和未接线缓存仍须在完整包落盘前拒绝。</summary>
    [Theory][InlineData("global")][InlineData("hiddenName")][InlineData("credential")][InlineData("array")][InlineData("broken")][InlineData("cycle")][InlineData("decimalKey")][InlineData("derivedScope")][InlineData("redundantBadCache")][InlineData("hiddenBad")]
    public void Invalid_derived_multi_is_rejected_before_staging(string kind)
    {
        var c=Catalog("derived_multi",false);var name=c.ConfigColumns.Single(column=>column.Id=="32");
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="hiddenName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="301"&&field.ConfigColumnId=="32");
        if(kind=="credential"){c.ConfigColumns.Single(column=>column.Id=="42").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(column=>column.Id=="42").ControlKind="FileLibrary";
        if(kind is "broken" or "hiddenBad")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="hiddenBad")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");
        if(kind=="cycle"){name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("self.name");c.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="self_id",LogicalType="int64",LinkConfigTableId="3",ControlKind="List",ControlDetail="{\"tableId\":\"3\",\"nameField\":\"id\"}"});}
        if(kind=="decimalKey")c.ConfigColumns.Single(column=>column.Id=="37").LogicalType="decimal";
        if(kind=="derivedScope")c.ConfigPages.Single(page=>page.Id=="301").WhereSql+=" AND name='X'";
        if(kind=="redundantBadCache"){c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;c.ConfigColumns.Single(x=>x.Id=="27").IsVirtualColumn=1;}
        var output=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidDerivedMulti",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>原R1目标物理Join缓存仍需独立证明，不能随R0逻辑名称门禁放开。</summary>
    [Fact]
    public async Task Redundant_target_join_name_retains_old_gate()
    {
        var catalog=Catalog("derived_multi",false);catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=1;var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-multi-physical-gate");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,"Example.UnprovedRedundantTarget",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));}
        finally{await TestDirectoryCleanup.DeleteAsync(output,boundary);}
    }
    /// <summary>目标name由company.name派生，其他页面和多选根定义沿原元数据。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool automatic)
    {
        var c=NonRedundantMultiSelectionTests.Catalog(prefix);c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
        var name=c.ConfigColumns.Single(column=>column.Id=="32");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});
        c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="name",LogicalType="string"},new(){Id="43",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="44",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="37",ConfigTableId="3",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail="{\"tableId\":\"4\",\"nameField\":\"name\"}"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",WhereSql="yz_project_id=@current_yz_project_id"});
        c.ConfigPageColumns.Add(new(){Id="40142",ConfigPageId="401",ConfigColumnId="42"});c.ConfigPageColumns.Add(new(){Id="30137",ConfigPageId="301",ConfigColumnId="37"});
        if(automatic){c.ConfigColumns.RemoveAll(column=>column.Id=="27");c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="27");}
        var source=c.ConfigPages.Single(page=>page.Id=="201");source.IsImport=1;source.IsExport=1;source.IsCopy=1;
        c.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        // 隐藏页默认指向外项目候选，复制／默认值仍必须执行内部最终值校验。
        c.ConfigColumns.Single(column=>column.Id=="25").DefaultValue="[\"9007199254750003\"]";
        var hidden=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(source))!;hidden.Id="202";hidden.IsImport=0;hidden.IsExport=0;hidden.IsCopy=0;hidden.Extra.Remove("importMethod");c.ConfigPages.Add(hidden);
        foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="201"&&field.ConfigColumnId!="25").ToArray()){var clone=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;clone.Id="202"+field.Id;clone.ConfigPageId="202";c.ConfigPageColumns.Add(clone);}
        return RedundantMultiNameCacheTests.Published(c);
    }
}
