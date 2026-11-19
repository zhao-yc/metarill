using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>数据库复选目标name来自R0单值联接，保留原控件和名称数组。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedCheckboxNamesTests
{
    /// <summary>显式与自动名称均从真实元数据生成编译，候选及Excel不访问IsIgnore属性。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Derived_checkbox_names_generate_and_compile(bool automatic)
    {
        var c=Catalog("relation",automatic);var before=JsonSerializer.Serialize(c);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(c,Path.Combine(directory,"generated"),"Example.DerivedCheckbox");await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.Equal(before,JsonSerializer.Serialize(c));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(c,"Example.DerivedCheckbox")));
            Assert.DoesNotContain("name",MetadataStoragePolicy.ForTable(c,c.ConfigTables.Single(table=>table.Id=="3")).PhysicalColumns.Select(column=>column.ColumnName));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>候选、已选校验和 Excel 使用控件条件与实际目标页的同一交集。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task Checkbox_endpoints_apply_the_bound_target_page(bool derived,bool global)
    {
        const string ns="Example.CheckboxExcelScope";
        var catalog=Catalog("checkbox_excel_scope",false);
        if(!derived)
        {
            var name=catalog.ConfigColumns.Single(column=>column.Id=="32");
            name.Extra.Remove("isJoinColumn");
            name.Extra.Remove("columnFullName");
        }
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");
        var whereSql="segment=@formParameters.segment AND status=1";
        if(global)
        {
            var target=catalog.ConfigTables.Single(table=>table.Id=="3");
            target.IsRongyu=1;
            target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId=="3"&&column.ColumnName=="yz_project_id");
            source.Extra.Remove("linkConfigPageId");
            whereSql+=" AND parent_id=@parentId";
        }
        source.ControlDetail=JsonSerializer.Serialize(new{tableId="3",pageId=global?null:"301",nameField="name",whereSql});
        var sourceField=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==source.Id);
        sourceField.IsShow=sourceField.IsEdit=1;
        catalog.ConfigColumns.Add(new(){Id="3005",ConfigTableId="3",ColumnName="sheet_status",LogicalType="int32"});
        var published=catalog.ConfigPages.Single(page=>page.Id=="301");
        published.WhereSql=global?"sheet_status=41":published.WhereSql+" AND sheet_status=41";
        published.OrderBy="sheet_status desc";
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var sourceTable=model.Tables.Single(table=>table.Source.Id=="2");
        var checkbox=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,sourceTable,sourceTable.Pages.Single(page=>page.Source.Id=="201")));
        Assert.Equal("301",checkbox.PhysicalTargetPage!.Source.Id);
        Assert.Equal("sheet_status",checkbox.Order[0].Column.Source.ColumnName);
        Assert.True(checkbox.Order[0].Descending);
        Assert.Contains(checkbox.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.ColumnName=="parent_id");

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-excel-page");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var generated=Assert.Single(Directory.GetFiles(Path.Combine(directory,"generated"),"CheckboxExcelScopeSourceService.g.cs",SearchOption.AllDirectories));
            var code=await File.ReadAllTextAsync(generated);
            var read=derived?"DerivedCheckboxRows":"CheckboxScope";
            Assert.Contains(read+"201_25Async(request.ParentId,actor,form",code);
            Assert.Contains(read+"201_25Async(entity.ParentId",code);
            Assert.Contains(read+"201_25Async(parentId,actor,",code);
            Assert.Contains("CheckboxTargetCondition201_25.ApplyConstants",code);
            Assert.Contains(derived
                ?"condition.And(publishedCondition)"
                :"query=query.Where(row=>row.ParentId==",code);

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.CheckboxExcelScopeSourceService",true)!;
            const System.Reflection.BindingFlags privateStatic=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
            var plan=Assert.IsType<Metarill.Data.Queries.FixedConditionSqlPlan>(
                service.GetField("CheckboxTargetCondition201_25",privateStatic)!.GetValue(null));
            var clause=plan.GetType().GetMethod("RenderConstants",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!
                .Invoke(plan,[new Metarill.Data.MySqlDatabaseProvider(),"scope",new Func<string,string>(field=>field)])!;
            Assert.Contains("sheet_status",Assert.IsType<string>(clause.GetType().GetProperty("Sql")!.GetValue(clause)));
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(clause.GetType().GetProperty("Parameters")!.GetValue(clause));
            Assert.Equal(41,Assert.Single(parameters.Values));
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>业务名称投影和筛选执行显式物理目标页条件，并按原数组顺序保留未命中位置。</summary>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    public void Checkbox_business_names_and_filter_follow_the_published_physical_page(int targetStorage, bool derived, bool withParent)
    {
        const string ns = "Example.CheckboxPageRead";
        var catalog = Catalog("checkbox_page_read", false);
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = targetStorage;
        var targetName = catalog.ConfigColumns.Single(column => column.Id == "32");
        if (!derived)
        {
            targetName.Extra.Remove("isJoinColumn");
            targetName.Extra.Remove("columnFullName");
        }
        var sourcePage = catalog.ConfigPages.Single(page => page.Id == "201");
        var targetPage = catalog.ConfigPages.Single(page => page.Id == "301");
        sourcePage.WhereSql = "yz_project_id=@current_yz_project_id" + (withParent ? " AND parent_id=@parentId" : "");
        targetPage.WhereSql = sourcePage.WhereSql + " AND status=41";
        catalog.ConfigColumns.Single(column => column.Id == "25").ControlDetail =
            """{"tableId":"3","pageId":"301","nameField":"name"}""";

        var model = GenerationModel.Create(catalog, ns);
        var source = model.Tables.Single(table => table.Source.Id == "2");
        var page = source.Pages.Single(item => item.Source.Id == "201");
        var definition = Assert.Single(DatabaseCheckboxCompilation.ForPage(model, source, page));
        Assert.Equal("301", definition.PhysicalTargetPage?.Source.Id);
        var view = NonRedundantReadCompilation.ForPage(model, source, page);
        var binding = Assert.Single(view.MultiTargets);
        Assert.True(binding.ApplyPageScope);
        Assert.Equal("201", binding.SourcePageId);
        Assert.Equal("301", binding.TargetPageId);

        var names = definition.Names.Source.ColumnName;
        var plan = NonRedundantReadCompilation.CompilePage(model, source, ["id", names], [names], [], view.SingleTargets, view.MultiTargets);
        const System.Reflection.BindingFlags internalInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var sql = plan.GetType().GetProperty("MySql", internalInstance)!.GetValue(plan)!;
        var expressions = ReadProperty<IReadOnlyDictionary<string, string>>("Expressions");
        var predicate = Assert.Single(ReadProperty<IReadOnlyDictionary<string, string>>("ArrayNamePredicates")).Value;
        var expression = expressions[names];
        Assert.Contains("CASE WHEN", expression);
        Assert.Contains("multi_value.`status`", expression);
        Assert.Contains("multi_value.`status`", predicate);
        Assert.Contains("multi_value.`delete_no`", expression);
        Assert.Contains("multi_value.`yz_project_id`", predicate);
        Assert.Contains("CAST(multi_ids.member_id AS BINARY)", expression);
        Assert.Contains("CAST(checkbox_ids.member_id AS BINARY)", predicate);
        Assert.Equal(withParent, expression.Contains("b.`parent_id`", StringComparison.Ordinal));
        Assert.Equal(withParent, predicate.Contains("b.`parent_id`", StringComparison.Ordinal));
        var constants = ReadProperty<IReadOnlyDictionary<string, object?>>("Constants");
        var status = Assert.Single(constants);
        Assert.Equal(41, status.Value);
        Assert.Contains(status.Key, expression);
        Assert.Contains(status.Key, predicate);

        // 运行时按页批量回显时也带同一条件和实际来源父键，不依赖内联聚合。
        var projection = ReadProperty<System.Collections.IDictionary>("MultiProjections")[names]!;
        var projectionType = projection.GetType();
        var batchScope = Assert.IsType<string>(projectionType.GetProperty("TargetScope")!.GetValue(projection));
        var owner = projectionType.GetProperty("Owner")!.GetValue(projection);
        Assert.Contains(status.Key, batchScope);
        if (withParent)
        {
            Assert.NotNull(owner);
            var fields = Assert.IsAssignableFrom<IReadOnlyList<string>>(owner.GetType().GetProperty("Fields")!.GetValue(owner));
            Assert.Equal(["parent_id"], fields);
        }
        else Assert.Null(owner);

        var generated = DatabaseCheckboxRenderer.Service(model, source, page);
        Assert.Contains("CheckboxTargetCondition201_25.ApplyConstants", generated);
        Assert.Contains("CheckboxOptions201_25Async", generated);
        Assert.Contains("ValidateCheckbox201_25Async", generated);
        var excel = ExportRenderer.Service(model, source, page);
        Assert.Contains("ExportCheckbox201_25Async", excel);

        T ReadProperty<T>(string name) => Assert.IsAssignableFrom<T>(sql.GetType().GetProperty(name, internalInstance)!.GetValue(sql));
    }


    /// <summary>回显和名称筛选使用原控件固定条件，支持独立控件和显式目标页叠加。</summary>
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(1, true, true, true)]
    public void Checkbox_business_names_apply_control_form_constants_and_actor(int targetStorage,bool pageScope,bool controlParent,bool customParent=false)
    {
        var catalog=Catalog("checkbox_control_read",false);
        catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=targetStorage;
        var label=catalog.ConfigColumns.Single(column=>column.Id=="32");
        if(targetStorage==1)
        {
            label.Extra.Remove("isJoinColumn");
            label.Extra.Remove("columnFullName");
        }
        var field=catalog.ConfigColumns.Single(column=>column.Id=="25");
        field.Extra.Remove("linkConfigPageId");
        catalog.ConfigColumns.Single(column=>column.Id=="28").LogicalType="int64";
        catalog.ConfigColumns.Single(column=>column.Id=="38").LogicalType="int64";
        catalog.ConfigColumns.Add(new(){Id="3051",ConfigTableId="3",ColumnName="assigned_id",LogicalType="int64",YzProjectId=catalog.YzProjectId});
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        var sourceParentField=customParent?"parent_key":"parent_id";
        var targetParentField=customParent?"parent_key":"parent_id";
        if(customParent)
        {
            catalog.ConfigColumns.Single(column=>column.Id=="26").ColumnName=sourceParentField;
            var parent=catalog.ConfigColumns.Single(column=>column.Id=="36");
            parent.ColumnName=targetParentField;
            parent.LogicalType="int32";
            catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql=catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql.Replace("parent_id",sourceParentField,StringComparison.Ordinal);
        }
        sourcePage.WhereSql="yz_project_id=@current_yz_project_id"+(controlParent?" AND "+sourceParentField+"=@parentId":"");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="301");
        targetPage.WhereSql="yz_project_id=@current_yz_project_id AND status<>48";
        var whereSql="status=41 AND segment=@formParameters.checkboxControlReadSource.segment AND assigned_id=@current_yz_manager_id"+
            (controlParent?" AND "+targetParentField+"=@parentId":"");
        field.ControlDetail=JsonSerializer.Serialize(new {tableId="3",pageId=pageScope?"301":null,nameField="name",whereSql});
        var model=GenerationModel.Create(catalog,"Example.ControlRead");
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        var page=source.Pages.Single(page=>page.Source.Id=="201");
        var definition=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,source,page));
        var view=NonRedundantReadCompilation.ForPage(model,source,page);
        var binding=Assert.Single(view.MultiTargets);
        Assert.Equal(pageScope,binding.ApplyPageScope);
        Assert.True(binding.ApplyControlScope);
        Assert.Equal(pageScope?"301":null,binding.TargetPageId);

        var name=definition.Names.Source.ColumnName;
        var plan=NonRedundantReadCompilation.CompilePage(model,source,["id",name],[name],[],view.SingleTargets,view.MultiTargets);
        Assert.True(plan.RequiresAccount);
        const long accountId=9007199254740998L;
        var bound=plan.BindAccount(new Metarill.Core.Security.RuntimeActor(accountId,"测试账号",false,catalog.YzProjectId,"5"));
        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var sql=bound.GetType().GetProperty("MySql",hidden)!.GetValue(bound)!;
        var expression=Read<IReadOnlyDictionary<string,string>>("Expressions")[name];
        var predicate=Read<IReadOnlyDictionary<string,string>>("ArrayNamePredicates")[name];
        var projection=Read<System.Collections.IDictionary>("MultiProjections")[name]!;
        var batch=Assert.IsType<string>(projection.GetType().GetProperty("TargetScope")!.GetValue(projection));
        var parameters=Read<IReadOnlyDictionary<string,object?>>("Constants");
        Assert.Contains(accountId,parameters.Values);
        Assert.Contains(41,parameters.Values);
        Assert.Equal(pageScope,parameters.Values.Contains(48));
        foreach(var command in new[]{expression,predicate,batch})
        {
            Assert.Contains("multi_value.`segment`=b.`segment`",command);
            Assert.Contains("multi_value.`assigned_id`=",command);
            Assert.Equal(controlParent,command.Contains("b.`"+sourceParentField+"`",StringComparison.Ordinal));
            Assert.All(parameters.Keys,key=>Assert.Contains(key,command));
        }
        var owner=projection.GetType().GetProperty("Owner")!.GetValue(projection)!;
        Assert.True(Assert.IsType<bool>(owner.GetType().GetProperty("PreserveScalarTypes")!.GetValue(owner)));
        var fields=Assert.IsAssignableFrom<IReadOnlyList<string>>(owner.GetType().GetProperty("Fields")!.GetValue(owner));
        Assert.Equal(controlParent?[sourceParentField,"segment"]:new[]{"segment"},fields);


        T Read<T>(string property)=>Assert.IsAssignableFrom<T>(sql.GetType().GetProperty(property,hidden)!.GetValue(sql));
    }


    /// <summary>控件直接限定物理外键时，候选、保存和 Excel 可以绑定来源级联还原的同一父级。</summary>
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,true,false)]
    [InlineData(false,false,true)]
    [InlineData(true,true,true)]
    [InlineData(true,true,true,true)]
    public async Task Checkbox_control_parent_uses_the_actual_source_cascade(bool derived,bool globalParent,bool scopeFromPage,bool overridePage=false)
    {
        const string ns="Example.CheckboxControlParent";
        var catalog=NonRedundantCrossTableGenerationTests.Catalog("checkbox_control_parent","int64","int64");
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="201");
        sourcePage.Extra.Remove("type");
        sourcePage.IsImport=sourcePage.IsExport=1;
        sourcePage.WhereSql+=" AND country_id=@parentId";
        if(derived)sourcePage.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
        var parent=catalog.ConfigTables.Single(table=>table.Id=="3");
        if(globalParent)
        {
            parent.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==parent.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigPages.Single(page=>page.ConfigTableId==parent.Id).WhereSql="status=1";
        }
        catalog.ConfigTables.Add(new(){Id="9",TableName="checkbox_control_parent_target",IsRongyu=derived?0:1});
        var targetScope="yz_project_id=@current_yz_project_id AND sheet_status=2";
        if(scopeFromPage)targetScope+=" AND target_scope_id=@parentId";
        var candidateId=overridePage?"902":"901";
        if(overridePage)
            catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigPages.Add(new(){Id=candidateId,ConfigTableId="9",WhereSql=targetScope});
        var targetParent=new ColumnMetadata {Id="96",ConfigTableId="9",ColumnName="target_scope_id",
            LogicalType=derived?"string":"int64",LinkConfigTableId=parent.Id};
        if(globalParent&&derived)
        {
            targetParent.ControlKind="List";
            targetParent.ControlDetail="{\"tableId\":\"3\",\"pageId\":\"301\",\"nameField\":\"name\"}";
        }
        var name=new ColumnMetadata {Id="92",ConfigTableId="9",ColumnName="name",LogicalType="string"};
        catalog.ConfigColumns.AddRange([
            new(){Id="26",ConfigTableId="2",ColumnName="country_id",Name="国家",LogicalType="int64",LinkConfigTableId=parent.Id},
            new(){Id="28",ConfigTableId="2",ColumnName="selected_ids",Name="复选",LogicalType="string",
                ControlKind="MultiCheckBoxDatabase",LinkConfigTableId="9",
                ControlDetail=JsonSerializer.Serialize(new{tableId="9",pageId="901",
                    whereSql=scopeFromPage?"status=1":"target_scope_id=@parentId AND status=1"})},
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            name,
            new(){Id="93",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="94",ConfigTableId="9",ColumnName="status",LogicalType="int32"},
            new(){Id="95",ConfigTableId="9",ColumnName="sheet_status",LogicalType="int32"},
            targetParent]);
        if(derived)
        {
            name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
            name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("label.name");
            catalog.ConfigColumns.Add(new(){Id="97",ConfigTableId="9",ColumnName="label_id",LogicalType="int64",
                ControlKind="List",LinkConfigTableId="4",ControlDetail="{\"tableId\":\"4\",\"pageId\":\"401\",\"nameField\":\"name\"}"});
            catalog.ConfigPageColumns.Add(new(){Id=candidateId+"97",ConfigPageId=candidateId,ConfigColumnId="97",IsShow=1,IsEdit=1});
        }
        catalog.ConfigPageColumns.AddRange([
            new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=1,IsEdit=1,
                Extra=new(){["isDetailShow"]=JsonSerializer.SerializeToElement(1)}},
            new(){Id=candidateId+"92",ConfigPageId=candidateId,ConfigColumnId="92",IsShow=1}]);
        if(overridePage)
            catalog.ConfigPageColumns.Single(field=>field.Id=="20128").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement("{\"pageId\":902}");
        RedundantMultiNameCacheTests.Published(catalog);
        if(overridePage)
        {
            var target=catalog.ConfigTables.Single(item=>item.Id=="9");
            Assert.False(Metarill.Data.Queries.NonRedundantReads.IsScopedMultiTarget(catalog,target,"901"));
            Assert.True(Metarill.Data.Queries.NonRedundantReads.IsScopedMultiTarget(catalog,target,candidateId));
        }
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=table.Pages.Single(item=>item.Source.Id=="201");
        var resolution=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="28"));
        Assert.True(resolution.Definition is not null,resolution.Reason);
        var checkbox=resolution.Definition!;
        Assert.True(checkbox.LogicalSourceParent);
        if(globalParent&&scopeFromPage)
            Assert.Equal(candidateId,checkbox.LogicalTargetPage!.Source.Id);
        else
            Assert.Null(checkbox.LogicalTargetPage);
        Assert.Equal(parent.Id,Assert.Single(checkbox.SourceParents).Source.Id);
        Assert.Contains(checkbox.Scopes,scope=>scope.Source=="parent"&&scope.Column.Source.Id==targetParent.Id);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-checkbox-control-logical-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var generated=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/CheckboxControlParentSourceService.g.cs"));
            Assert.Contains("LogicalParentId201(parentId)",generated);
            Assert.Contains("logicalRelationParentId: parentId",generated);
            if(overridePage)Assert.Contains("CompileMultiGlobalParent(\"9\",\"902\")",generated);
            Assert.Contains("CountryId=Metarill.Data.Queries.NonRedundantReads.Value<long?>(row,\"country_id\")",generated);
            Assert.Contains(derived?"new(\"target_scope_id\",FixedReadComparison.Equal,parentId!)"
                :"row.TargetScopeId==long.Parse(parentId!",generated);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.CheckboxControlParentSourceService",true)!;
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service
                .GetField("ExportReadPlan201",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
            var sqlPlan=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
            var parentExpression=Assert.IsType<string>(sqlPlan.GetType().GetMethod("Expression",hidden)!.Invoke(sqlPlan,["country_id"]));
            Assert.StartsWith("CASE WHEN",parentExpression);
            Assert.Contains(" THEN ",parentExpression);

            targetParent.LinkConfigTableId="4";
            model=GenerationModel.Create(catalog,ns);
            table=model.Tables.Single(item=>item.Source.Id=="2");
            page=table.Pages.Single(item=>item.Source.Id=="201");
            var rejected=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="28"));
            Assert.Null(rejected.Definition);
            Assert.Contains("同一真实父表",rejected.Reason);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>非默认派生名称的候选、原导入导出和两种源存储使用同一固定字段。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Explicit_checkbox_label_uses_one_derived_field_in_generation_and_storage(int mode, bool automatic)
    {
        var catalog = Catalog("custom_checkbox", automatic);
        var owner = catalog.ConfigTables.Single(table => table.Id == "2");
        owner.IsRongyu = mode;
        var field = catalog.ConfigColumns.Single(column => column.Id == "25");
        field.ControlDetail = JsonSerializer.Serialize(new
        {
            tableId = "3", nameField = "caption",
            whereSql = "parent_id=@parentId AND segment=@formParameters.segment AND status=1", orderBy = "caption desc"
        });
        catalog.ConfigColumns.Single(column => column.Id == "32").ColumnName = "caption";
        if (!automatic)
        {
            var explicitNames = catalog.ConfigColumns.Single(column => column.Id == "27");
            explicitNames.ColumnName = "target_captions";
            explicitNames.Extra["columnFullName"] = JsonSerializer.SerializeToElement("target.caption");
        }
        catalog.ConfigColumns.Add(new() { Id = "ordinary_names", ConfigTableId = owner.Id, ColumnName = "target_names", LogicalType = "string" });
        catalog.ConfigPageColumns.Add(new()
        {
            Id="20225",ConfigPageId="202",ConfigColumnId=field.Id,YzProjectId=catalog.YzProjectId,
            Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"orderBy\":\"id desc\"}")}
        });
        var model = GenerationModel.Create(catalog, "Example.CustomCheckbox");
        var table = model.Tables.Single(candidate => candidate.Source.Id == owner.Id);
        var page = table.Pages.Single(candidate => candidate.Source.Id == "201");
        var checkbox = Assert.Single(DatabaseCheckboxCompilation.ForPage(model, table, page));
        Assert.Equal("caption", checkbox.Name.Source.ColumnName);
        Assert.Equal("target_captions", checkbox.Names.Source.ColumnName);
        Assert.True(checkbox.DerivedName);
        var alternate=table.Pages.Single(candidate=>candidate.Source.Id=="202");
        var overrideCheckbox=Assert.Single(DatabaseCheckboxCompilation.ForService(model,table,alternate));
        Assert.Equal("caption",overrideCheckbox.Name.Source.ColumnName);
        Assert.Equal(checkbox.Conditions!.Definition,overrideCheckbox.Conditions!.Definition);
        Assert.Equal("segment",Assert.Single(overrideCheckbox.Conditions.Forms).Column.Source.ColumnName);
        var order=Assert.Single(overrideCheckbox.Order);
        Assert.True(order.Column.IsId);
        Assert.True(order.Descending);
        var physical = MetadataStoragePolicy.ForTable(catalog, owner).PhysicalColumns.Select(column => column.ColumnName);
        Assert.Contains("target_names", physical);
        Assert.Equal(mode == 1, physical.Contains("target_captions"));
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        if (mode == 1)
            Assert.Contains("target_captions", MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns[owner.TableName]);

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-custom-checkbox-label");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), model.Namespace);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(model.Namespace + ".Generated.Services.CustomCheckboxSourceService", true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            Assert.NotNull(service.GetMethod("Import201Async"));
            Assert.NotNull(service.GetMethod("Export201Async"));
            catalog.ConfigPageColumns.Single(item => item.ConfigPageId == "201" && item.ConfigColumnId == field.Id)
                .Extra["controlDetail"] = JsonSerializer.SerializeToElement(
                    field.ControlDetail.Replace("\"caption\"", "\"name\"", StringComparison.Ordinal));
            Assert.NotEmpty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, model.Namespace)));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>完整包从包内源码构建，并挂载原复选组件；重复更新保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_derived_checkbox_keeps_original_component_and_handwritten_code(bool automatic)
    {
        var root=NonRedundantApplicationFixture.Root();var c=Catalog("relation",automatic);var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.DerivedCheckboxApplication",root).FrontendIncluded);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            const string component="uni_modules/uni-data-checkbox/components/uni-data-checkbox/uni-data-checkbox.vue";Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin",component)),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            var start=new System.Diagnostics.ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(Path.Combine(root,"backend/tests/Metarill.Generation.Tests/DatabaseCheckboxVueProbe.mjs"));start.ArgumentList.Add(output);using var process=System.Diagnostics.Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));try{await process.WaitForExitAsync(timeout.Token);}catch(Exception error){await SharedSourceBuildGate.StopOwnProcessAsync(process,stdout,stderr,error);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            using(var capabilities=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")))){var subset=Assert.Single(capabilities.RootElement.GetProperty("supportedControlSubsets").EnumerateArray(),item=>item.GetProperty("controlKind").GetString()=="MultiCheckBoxDatabase");Assert.True(subset.GetProperty("derivedNameSupported").GetBoolean());Assert.False(subset.GetProperty("renamePropagationSupported").GetBoolean());}
            var hand=Path.Combine(output,"backend/generated/Services/RelationSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 派生复选手写保护。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.DerivedCheckboxApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明的名称、存储或条件在stage创建前拒绝，隐藏字段不能绕过。</summary>
    [Theory][InlineData("global")][InlineData("targetR1")][InlineData("sourceR1BadCache")][InlineData("credential")][InlineData("array")][InlineData("broken")][InlineData("decimalKey")][InlineData("derivedScope")][InlineData("overlay")][InlineData("hiddenBad")]
    public void Invalid_derived_checkbox_is_rejected_before_staging(string kind)
    {
        var c=Catalog("relation",false);var name=c.ConfigColumns.Single(column=>column.Id=="32");
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="targetR1")c.ConfigTables.Single(table=>table.Id=="3").IsRongyu=1;
        if(kind=="sourceR1BadCache"){c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;c.ConfigColumns.Single(x=>x.Id=="27").IsVirtualColumn=1;}
        if(kind=="credential"){c.ConfigColumns.Single(column=>column.Id=="42").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(column=>column.Id=="42").ControlKind="FileLibrary";
        if(kind is "broken" or "hiddenBad")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="decimalKey")c.ConfigColumns.Single(column=>column.Id=="37").LogicalType="decimal";
        if(kind=="derivedScope")c.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="头;头;name='X';name asc";
        if(kind=="overlay")c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="25").Extra["controlKind"]=JsonSerializer.SerializeToElement("List");
        if(kind=="hiddenBad")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="25");
        var output=Path.Combine(Path.GetTempPath(),"metarill-derived-checkbox-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.InvalidDerivedCheckbox",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>沿派生多值元数据，仅换回原数据库复选协议；whereSql按来源真实父及表单执行。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool automatic)
    {
        var c=DerivedMultiNamesTests.Catalog(prefix,automatic);var source=c.ConfigColumns.Single(column=>column.Id=="25");source.ControlKind="MultiCheckBoxDatabase";source.ControlDetail="旧第一段;旧第二段;parent_id=@parentId AND segment=@formParameters.segment AND status=1;name desc";
        c.ConfigColumns.AddRange([new(){Id="28",ConfigTableId="2",ColumnName="segment",LogicalType="int32",DefaultValue="1"},new(){Id="38",ConfigTableId="3",ColumnName="segment",LogicalType="int32",DefaultValue="1"},new(){Id="39",ConfigTableId="3",ColumnName="status",LogicalType="int32",DefaultValue="1"}]);
        foreach(var page in c.ConfigPages.Where(page=>page.ConfigTableId=="2"))c.ConfigPageColumns.Add(new(){Id=page.Id+"28",ConfigPageId=page.Id,ConfigColumnId="28"});
        c.ConfigPageColumns.AddRange([new(){Id="30138",ConfigPageId="301",ConfigColumnId="38"},new(){Id="30139",ConfigPageId="301",ConfigColumnId="39"}]);
        foreach(var record in c.ConfigColumns.Cast<MetadataRecord>().Concat(c.ConfigPageColumns))if(string.IsNullOrWhiteSpace(record.YzProjectId))record.YzProjectId=c.YzProjectId;
        if(!automatic)c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="27").Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        return c;
    }
}
