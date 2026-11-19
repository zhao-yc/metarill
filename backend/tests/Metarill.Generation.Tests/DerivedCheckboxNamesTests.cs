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

    /// <summary>控件直接限定物理外键时，候选、保存和 Excel 可以绑定来源级联还原的同一父级。</summary>
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,false,false)]
    [InlineData(false,true,false)]
    [InlineData(true,true,false)]
    [InlineData(false,false,true)]
    [InlineData(true,true,true)]
    public async Task Checkbox_control_parent_uses_the_actual_source_cascade(bool derived,bool globalParent,bool scopeFromPage)
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
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",WhereSql=targetScope});
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
            catalog.ConfigPageColumns.Add(new(){Id="90197",ConfigPageId="901",ConfigColumnId="97",IsShow=1,IsEdit=1});
        }
        catalog.ConfigPageColumns.AddRange([
            new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=1,IsEdit=1,
                Extra=new(){["isDetailShow"]=JsonSerializer.SerializeToElement(1)}},
            new(){Id="90192",ConfigPageId="901",ConfigColumnId="92",IsShow=1}]);
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var table=model.Tables.Single(item=>item.Source.Id=="2");
        var page=table.Pages.Single(item=>item.Source.Id=="201");
        var resolution=DatabaseCheckboxCompilation.Resolve(model,table,page,table.Columns.Single(column=>column.Source.Id=="28"));
        Assert.True(resolution.Definition is not null,resolution.Reason);
        var checkbox=resolution.Definition!;
        Assert.True(checkbox.LogicalSourceParent);
        if(globalParent&&scopeFromPage)
            Assert.Equal("901",checkbox.LogicalTargetPage!.Source.Id);
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
