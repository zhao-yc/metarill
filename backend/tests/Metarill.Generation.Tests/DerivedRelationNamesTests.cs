using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>普通R0及已发布R1来源的List使用R0目标的联接名称，候选、原导入导出和写回显必须共用同一计划。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedRelationNamesTests
{
    /// <summary>单选的附加条件只进入原 Excel 名称反查，与已发布父范围一同绑定。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    [InlineData(false,false,true)]
    [InlineData(true,false,true)]
    [InlineData(false,true,true)]
    [InlineData(false,false,true,true)]
    [InlineData(false,true,true,true)]
    [InlineData(true,false,true,true)]
    [InlineData(false,false,true,true,1)]
    [InlineData(false,true,true,true,2)]
    [InlineData(false,true,true,true,3)]
    public async Task Single_excel_applies_control_conditions(bool physical,bool logical,bool cached=false,bool globalTarget=false,int globalSource=0)
    {
        const string ns="Example.SingleImportCondition";
        var catalog=Catalog("dr_selection",parentAndForm:true);
        if(cached)
        {
            catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;
            if(logical||globalTarget&&!physical)
            {
                // 冗余名称副本用实际来源行的门店归属，导入条件再绑定当前账号和工作簿字段。
                catalog.ConfigColumns.Add(new(){Id="998",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
                foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                    sourcePage.WhereSql+=" AND store_id=@current_store_id";
            }
        }
        var targetTable=catalog.ConfigTables.Single(table=>table.Id=="2");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="201");
        targetPage.Extra["type"]=JsonSerializer.SerializeToElement(1);
        if(globalTarget)
        {
            targetTable.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigTables.Single(table=>table.Id=="4").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="";
            targetPage.WhereSql=physical?"status=1":"status=1 AND group_id=@parent_id";
        }
        if(globalSource!=0)
        {
            foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "1" or "3"))
                table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "16" or "17" or "32"||globalSource==1&&column.Id=="12");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "16" or "17" or "32"||globalSource==1&&field.ConfigColumnId=="12");
            if(globalSource==3)catalog.ConfigColumns.Single(column=>column.Id=="12").LogicalType="string";
            foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                sourcePage.OrderBy="id desc";
        }
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
        foreign.ControlKind="SelectPage";
        if(physical)
        {
            targetTable.IsRongyu=1;
            // 物理名称场景去掉夹具遗留的上游公司下拉，只保留本次发布的单选。
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="28"||globalTarget&&column.Id=="27");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="28"||globalTarget&&field.ConfigColumnId=="27");
            var display=catalog.ConfigColumns.Single(column=>column.Id=="113");
            display.ColumnName="customer_name";
            display.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer.name");
        }
        catalog.ConfigColumns.Add(new(){Id="217",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
        if(logical)
        {
            catalog.ConfigColumns.AddRange([
                new(){Id="45",ConfigTableId="4",ColumnName="parent_company_id",LogicalType="int64",LinkConfigTableId="4"},
                new(){Id="216",ConfigTableId="2",ColumnName="tree_id",ControlKind="CascadeTree",LogicalType="int64",LinkConfigTableId="4",
                    ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table="dr_selection_company",parentField="parent_company_id"}})}]);
            catalog.ConfigPageColumns.Add(new(){Id="40145",ConfigPageId="401",ConfigColumnId="45"});
            catalog.ConfigPageColumns.Add(new(){Id="201216",ConfigPageId="201",ConfigColumnId="216"});
        }
        if(logical||globalTarget&&!physical)targetPage.WhereSql+=" AND store_id=@current_store_id";
        var whereSql="status>=1"+(globalTarget&&physical?"":" AND group_id=@parent_id")+
            " AND store_id=@current_store_id AND segment=@formParameters.segment";
        foreign.ControlDetail=JsonSerializer.Serialize(new{pageId="201",nameField=physical?"name":"company_name",whereSql});
        var field=catalog.ConfigPageColumns.Single(column=>column.ConfigPageId=="101"&&column.ConfigColumnId=="15");
        field.Extra["isDetailShow"]=JsonSerializer.SerializeToElement(1);
        if(logical)field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(
            JsonSerializer.Serialize(new{whereSql=whereSql+" AND tree_name <> '不导入'"}));
        RedundantMultiNameCacheTests.Published(catalog);
        var model=GenerationModel.Create(catalog,ns);
        var source=model.Tables.Single(table=>table.Source.Id=="1");
        var page=source.Pages.Single(item=>item.Source.Id=="101");
        var relation=Assert.Single(RelationCompilation.ForPage(model,page,forExcel:true),item=>item.SourceColumn.Source.Id==foreign.Id);
        Assert.Null(relation.Conditions);
        Assert.Equal("segment",Assert.Single(ImportDependencyCompilation.ForPage(model,source,page).Dependencies[foreign.Id]).Source.ColumnName);
        new FrontendPackager().Validate(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-import-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/DrSelectionOrderService.g.cs"));
            var at=code.IndexOf("private async Task<JsonElement> ImportRelation101_15Async",StringComparison.Ordinal);
            Assert.True(at>=0);
            Assert.DoesNotContain("SingleImportCondition101_15.BindVariables",code[..at]);
            var import=code[at..];
            Assert.Contains("OptionsScope101_15Async(parentId,targetParentId,actor,cancellationToken,true,false)",import);
            Assert.Contains("SingleImportCondition101_15.BindVariables",import);
            var overwrite=code[code.IndexOf("private async Task<JsonElement> ImportRelation103_15Async",StringComparison.Ordinal)..];
            Assert.Contains("OptionsScope103_15Async(parentId,targetParentId,actor,cancellationToken,true,false)",overwrite);
            Assert.Contains("SingleImportCondition103_15.BindVariables",overwrite);
            Assert.Contains("AccountConditionVariables.Require(actor, \"current_store_id\")",import);
            Assert.Contains("relationContext[\"segment\"]",import);
            Assert.Contains(physical?"importCondition.ApplyConstants(query,session,\"single_control_import_101_15\")":
                "PageAsync(SingleImportReadPlan101_15.BindAccount(actor)",import);

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.DrSelectionOrderService",true)!;
            const BindingFlags staticPrivate=BindingFlags.NonPublic|BindingFlags.Static;
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            var condition=Assert.IsType<Metarill.Data.Queries.FixedConditionSqlPlan>(service
                .GetField("SingleImportCondition101_15",staticPrivate)!.GetValue(null));
            var bound=condition.BindVariables(new Dictionary<string,object?>
            {
                ["parent_id"]="9007199254741197",["current_store_id"]=23L,["formParameters.segment"]=3
            });
            var clause=bound.GetType().GetMethod("RenderConstants",hidden)!
                .Invoke(bound,[new Metarill.Data.MySqlDatabaseProvider(),"excel",new Func<string,string>(name=>name)])!;
            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(clause.GetType().GetProperty("Parameters")!.GetValue(clause));
            var expected=new List<object?>{1};
            if(!globalTarget||!physical)expected.Add(9007199254741197L);
            expected.AddRange([23L,3]);
            if(logical)expected.Add("不导入");
            Assert.Equal(expected,parameters.Values);
            if(globalSource!=0)
            {
                Assert.Contains("ValidateActor(actor)",code);
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(assembly
                    .GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("customer_company_name",maintenance.MixedSourceColumns["dr_selection_order"]);
            }
            if(logical)
            {
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(service.GetField("SingleImportReadPlan101_15",staticPrivate)!.GetValue(null));
                Assert.True(plan.RequiresAccount);
                var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                Assert.Contains("CASE WHEN",Assert.IsType<string>(sql.GetType().GetMethod("Expression",hidden)!.Invoke(sql,["tree_name"])));
            }

            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new
                {whereSql=whereSql+" AND segment=@parentId"}));
            var invalid=Assert.Throws<InvalidOperationException>(()=>NonRedundantSelectionCompilation.Resolve(model,source,page,relation.SourceColumn));
            Assert.Contains("父键必须沿用目标页",invalid.Message);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>真实生成编译，派生名称不允许回退为Entity忽略属性上的物理SQL。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Derived_list_name_compiles_candidates_import_and_export(bool parentAndForm,bool cached)
    {
        var catalog=Catalog("dr_names",parentAndForm);if(cached)catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;var before=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-relation");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedNames");
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            Assert.Contains("DerivedOptionsPlan101_15",service);Assert.DoesNotContain("entity.CompanyName.Contains(keyword)",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.DerivedNames.Generated.Services.DrNamesOrderService"));
            if(cached)
            {
                // 同一外键的所有已声明副本应进入实际下发目录，包含直接字段与派生名称。
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType("Example.DerivedNames.Generated.GeneratedRelationMaintenance",true)!
                        .GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("dr_names_party",maintenance.MixedTargets);
                Assert.Contains("dr_names_company",maintenance.MixedTargets);
                Assert.Contains("customer_company_name",maintenance.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("customer_notes",maintenance.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("customer_amount",maintenance.MixedSourceColumns["dr_names_order"]);
            }
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.DerivedNames")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>外层客户候选的派生公司名继承客户页实际选择的公司范围。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    [InlineData(false,false,true)][InlineData(true,false,true)]
    [InlineData(false,false,true,true)][InlineData(true,false,false,true)]
    [InlineData(false,false,true,false,"constant")][InlineData(false,false,true,false,"form")]
    [InlineData(false,false,true,false,"account")][InlineData(false,true,false,false,"parent")]
    [InlineData(false,false,false,false,"combined")]
    public async Task Derived_list_and_business_reads_keep_the_intermediate_pages_fixed_scope(
        bool selection,bool parent,bool unscopedMiddle=false,bool deeper=false,string? control=null)
    {
        var catalog=Catalog("dr_names",parent);
        catalog.ConfigTables.Single(table=>table.Id=="4").IsRongyu=0;
        catalog.ConfigColumns.Add(new(){Id="45",ConfigTableId="4",ColumnName="store_id",LogicalType="int64",YzProjectId=catalog.YzProjectId});
        var companyPage=catalog.ConfigPages.Single(page=>page.Id=="401");
        companyPage.WhereSql+=" AND store_id=@current_store_id AND name <> '原页排除'";
        var actualPage=companyPage;
        if (selection)
        {
            companyPage.Extra["type"]=JsonSerializer.SerializeToElement(1);
            actualPage=new(){Id="402",ConfigTableId="4",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
                WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id AND name <> '当前页排除'",
                Extra=new(){["type"]=JsonSerializer.SerializeToElement(1)}};
            catalog.ConfigPages.Add(actualPage);
            catalog.ConfigPageColumns.Add(new(){Id="40244",ConfigPageId="402",ConfigColumnId="44",YzProjectId=catalog.YzProjectId});
            var foreign=catalog.ConfigColumns.Single(column=>column.Id=="27");
            foreign.ControlKind="SelectPage";
            foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="4",pageId="401",nameField="name"});
            catalog.ConfigPageColumns.Single(field=>field.Id=="20127").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{pageId=actualPage.Id}));
        }
        else
        {
            catalog.ConfigColumns.Single(column=>column.Id=="27").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("401");
        }

        if (parent)
        {
            catalog.ConfigColumns.Add(new(){Id="46",ConfigTableId="4",ColumnName="group_id",LogicalType="int64",
                LinkConfigTableId="3",YzProjectId=catalog.YzProjectId});
            if(control!="parent")
                foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="4"))
                    page.WhereSql+=" AND group_id=@parent_id";
        }
        if(control is not null)
        {
            if(control!="combined")companyPage.WhereSql="yz_project_id=@current_yz_project_id";
            catalog.ConfigColumns.Add(new(){Id="49",ConfigTableId="4",ColumnName="status",LogicalType="int32"});
            var foreign=catalog.ConfigColumns.Single(column=>column.Id=="27");
            foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="4",nameField="name",whereSql="status=17"});
            var condition=control switch
            {
                "form"=>"status IN (@formParameters.drNamesParty.cacheSegment,3)",
                "account"=>"store_id=@current_store_id AND status=3",
                "parent"=>"group_id=@parent_id AND status=3",
                _=>"status=3"
            };
            catalog.ConfigPageColumns.Single(field=>field.Id=="20127").Extra["controlDetail"]=
                JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql=condition}));
            if(control=="form")
            {
                catalog.ConfigColumns.Add(new(){Id="229",ConfigTableId="2",ColumnName="cache_segment",LogicalType="int64"});
                catalog.ConfigPageColumns.Add(new(){Id="201229",ConfigPageId="201",ConfigColumnId="229",IsShow=1,IsEdit=1});
            }
        }

        if(unscopedMiddle)
            catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="yz_project_id=@current_yz_project_id";
        if(deeper)
        {
            catalog.ConfigTables.Add(new(){Id="8",TableName="dr_names_region",YzProjectId=catalog.YzProjectId,IsRongyu=0});
            catalog.ConfigColumns.AddRange([
                new(){Id="81",ConfigTableId="8",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="82",ConfigTableId="8",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="83",ConfigTableId="8",ColumnName="delete_no",LogicalType="datetime"},
                new(){Id="84",ConfigTableId="8",ColumnName="name",LogicalType="string"},
                new(){Id="85",ConfigTableId="8",ColumnName="store_id",LogicalType="int64"},
                new(){Id="47",ConfigTableId="4",ColumnName="region_id",LogicalType="int64",ControlKind="List",LinkConfigTableId="8",
                    Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("801")}}
            ]);
            catalog.ConfigPages.Add(new(){Id="801",ConfigTableId="8",YzAdminTypeId="5",YzProjectId=catalog.YzProjectId,
                WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id AND name <> '末段排除'"});
            catalog.ConfigPageColumns.Add(new(){Id="80184",ConfigPageId="801",ConfigColumnId="84"});
            // 公司页没有公开区域控件；单选原辅助列仍与其显示名一致，另一个派生列读取区域名称。
            catalog.ConfigColumns.Add(new(){Id="212",ConfigTableId="2",ColumnName="chain_label",LogicalType="string",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("company.region.name")}});
            catalog.ConfigPageColumns.Add(new(){Id="201212",ConfigPageId="201",ConfigColumnId="212",IsShow=1,IsEdit=0});
            catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail=
                JsonSerializer.Serialize(new{tableId="2",table="dr_names_party",nameField="chain_label"});
            var display=catalog.ConfigColumns.Single(column=>column.Id=="113");
            display.ColumnName="customer_chain_label";
            display.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer.chain_label");
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-project-join");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.DerivedProjectJoin");
            var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            Assert.Contains("CompileProjectRelation(\"2\"",service);
            var sourceParent=parent?"\"29\"":"null";
            Assert.Contains($"new(\"27\",\"{actualPage.Id}\",{sourceParent})",service);
            Assert.Contains("DerivedOptionsPlan101_15.BindAccount(actor)",service);
            Assert.Contains("ReadPlan101.BindAccount(actor)",service);
            Assert.Contains("ExportReadPlan101.BindAccount(actor)",service);
            if(unscopedMiddle)Assert.Contains("ApplyPageCondition=false",service);
            Assert.Contains("OptionsSelected101_15Async",service);
            Assert.Contains("Import101Async",service);
            Assert.Contains("Export101Async",service);

            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type=assembly.GetType("Example.DerivedProjectJoin.Generated.Services.DrNamesOrderService",true)!;
            const System.Reflection.BindingFlags scope=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            var actor=new Metarill.Core.Security.RuntimeActor(9,"demo",false,catalog.YzProjectId,"5"){StoreId=6351};
            foreach(var planName in new[]{"DerivedOptionsPlan101_15","ReadPlan101","ExportReadPlan101"})
            {
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(type.GetField(planName,
                    System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null));
                var bound=plan.BindAccount(actor);
                var mysql=bound.GetType().GetProperty("MySql",scope)!.GetValue(bound)!;
                var sql=Assert.IsType<string>(mysql.GetType().GetProperty("From",scope)!.GetValue(mysql));
                Assert.Contains("EXISTS (SELECT 1 FROM `dr_names_company`",sql);
                if(control is null or "account" or "combined")Assert.Contains(".`store_id`",sql);
                if(parent)
                {
                    Assert.Contains("FROM `dr_names_group`",sql);
                    var owner=planName=="DerivedOptionsPlan101_15"?"b":"r0";
                    Assert.Contains($"=BINARY CAST({owner}.`group_id` AS CHAR)",sql);
                    Assert.Matches(@"=BINARY CAST\(mt_\w+_page\.`group_id` AS CHAR\)",sql);
                }
                var values=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(mysql.GetType().GetProperty("Constants",scope)!.GetValue(mysql)).Values;
                if(control is null or "account" or "combined")Assert.Contains(6351L,values);
                if(control is null or "combined")Assert.Contains(selection?"当前页排除":"原页排除",values);
                if(selection)Assert.DoesNotContain("原页排除",values);
                if(control is not null)
                {
                    Assert.Contains(".`status`",sql);
                    Assert.Contains(3,values);
                    Assert.DoesNotContain(17,values);
                    if(control=="form")
                    {
                        var owner=planName=="DerivedOptionsPlan101_15"?"b":"r0";
                        Assert.Contains(owner+".`cache_segment`",sql);
                        Assert.Contains("BETWEEN -2147483648 AND 2147483647",sql);
                    }
                }
                if(deeper)
                {
                    Assert.Contains("EXISTS (SELECT 1 FROM `dr_names_region`",sql);
                    Assert.Contains("末段排除",values);
                }
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>固定自树条件通过原单值候选、保存与 Excel 共用同一已发布计划。</summary>
    [Theory]
    [InlineData(false, false, false, null, false)]
    [InlineData(true, false, true, null, false)]
    [InlineData(false, true, true, null, false)]
    [InlineData(true, true, false, null, false)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, true, false, true, false, true)]
    [InlineData(false, true, false, true, true, true)]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(false, false, false, null, true)]
    [InlineData(true, false, true, null, true)]
    [InlineData(false, true, true, null, true, true)]
    [InlineData(true, true, false, null, true, true)]
    [InlineData(false, false, false, null, false, false, true)]
    [InlineData(false, true, true, null, false, false, true)]
    [InlineData(false, false, false, null, false, true, true)]
    [InlineData(false, true, true, null, false, true, true)]
    [InlineData(false, false, false, false, false, false, true)]
    [InlineData(false, true, true, true, false, false, true, true)]
    [InlineData(false, false, false, true, false, true, true)]
    [InlineData(false, true, true, false, false, true, true, true)]
    [InlineData(false, false, false, true, true, false, true, true)]
    [InlineData(false, true, false, false, true, true, true, true)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false, true)]
    [InlineData(true, false, true, false, true, true)]
    [InlineData(false, false, true, true, true, false, false, false, true)]
    [InlineData(true, true, true, false, false, true, false, false, true)]
    [InlineData(false,true,true,true,true,true,true,true,true,true)]
    [InlineData(true,true,true,false,false,true,false,false,true,true)]
    [InlineData(true,false,true,true,true,true,false,false,false,true)]
    [InlineData(false,true,true,true,false,true,true,true,true,true,true)]
    [InlineData(true,true,true,false,false,true,false,false,true,true,true)]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true)]
    [InlineData(false,true,true,true,false,true,true,true,true,true,true,true)]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true,true)]
    [InlineData(true,true,true,false,true,true,false,false,true,true)]
    [InlineData(false,true,true,false,false,true,true,true,true,true,true,true,true)]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true,true,true)]
    [InlineData(false,true,true,false,false,true,true,true,true,true,true,true,true,true)]
    [InlineData(false,false,true,true,false,true,false,false,false,false,true,true,true,true)]
    [InlineData(true,true,true,false,false,true,false,false,true,true,true,true,true,true)]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true,true,true,true)]
    [InlineData(false,true,true,false,false,true,true,true,true,true,true,true,true,true,"int64")]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true,true,true,true,"string")]
    [InlineData(true,true,true,false,false,true,false,false,true,true,true,true,true,true,null,true)]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true,true,true,true,"string",true)]
    [InlineData(true,true,true,false,false,true,false,false,true,true,true,true,true,true,null,true,true)]
    [InlineData(true,false,true,true,false,true,false,false,false,false,true,true,true,true,"string",true,true)]
    [InlineData(false,false,true,null,true,true,false,false,false,true)]
    [InlineData(true,true,true,null,true,true,false,false,false,true)]
    [InlineData(false,true,true,null,false,true,true,true,false,true,true)]
    [InlineData(true,false,true,null,false,true,false,false,false,true,true)]
    [InlineData(false,false,true,null,true,true,false,false,true,true)]
    [InlineData(true,true,true,null,true,true,false,false,true,true)]
    [InlineData(false,true,true,null,false,true,true,true,true,true,true)]
    [InlineData(true,false,true,null,false,true,false,false,true,true,true)]
    [InlineData(false,true,true,null,false,true,false,false,true,true,false)]
    [InlineData(true,false,true,null,false,true,false,false,true,true,false,false,false,false,null,false,false,true)]
    [InlineData(true,true,true,null,true,true,false,false,true,true,false,false,false,false,null,false,false,true)]
    public async Task Single_relation_target_leaf_condition_reaches_original_services(bool selection,bool derived,bool parent,
        bool? globalWithProject,bool redundant,bool cross=false,bool listCondition=false,bool fixedTargetLogic=false,bool globalParent=false,bool logicalParent=false,bool logicalSource=false,bool nested=false,bool nestedCache=false,bool globalCache=false,string? rowProjectType=null,bool pageOnly=false,bool buttonOverride=false,bool independentScope=false,
        bool terminalProjectScope=false)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,parent);
        if(terminalProjectScope)AddNestedProjectCandidateScope(catalog,prefix);
        var target=catalog.ConfigPages.Single(page=>page.Id=="201");
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
        if(selection)
        {
            // 选择页原附加条件已经由目标页执行。去掉该夹具独立的 List 表单条件。
            target.Extra["type"]=JsonSerializer.SerializeToElement(1);
            foreign.ControlKind="SelectPage";
        }
        var label=derived?"company_name":"name";
        var logicalField=cross?"country_name":"tree_name";
        var controlWhere=selection||listCondition?logicalField+" <> '排除叶名'":parent?"segment=@formParameters.segment":"";
        var parentOnly=logicalParent&&selection&&globalParent&&globalWithProject is not null;
        if(parentOnly)controlWhere="";
        if(listCondition&&parent)controlWhere+= " AND segment=@formParameters.segment";
        if(listCondition&&globalWithProject is not null)controlWhere+=" AND store_id=@current_store_id";
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField=label,whereSql=controlWhere});
        if(listCondition&&cross)
            catalog.ConfigPageColumns.Single(column=>column.ConfigPageId=="101"&&column.ConfigColumnId=="15")
                .Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new
                {
                    whereSql=controlWhere.Replace("排除叶名","覆盖条件",StringComparison.Ordinal)
                }));
        if(!derived)
        {
            var display=catalog.ConfigColumns.Single(column=>column.Id=="113");
            display.ColumnName="customer_name";
            display.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer.name");
        }

        catalog.ConfigTables.Add(new(){Id="6",TableName=prefix+"_tree",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="62",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="63",ConfigTableId="6",ColumnName="name",LogicalType="string"},
            new(){Id="64",ConfigTableId="6",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="65",ConfigTableId="6",ColumnName="parent_dr_names_tree_id",LogicalType="int64",LinkConfigTableId="6"},
            new(){Id="216",ConfigTableId="2",ColumnName="tree_id",LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="6",
                ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",selfTree=new{table=prefix+"_tree",parentField="parent_dr_names_tree_id"}})},
            new(){Id="217",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"}
        ]);
        catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id AND name <> '停用树节点'"});
        foreach(var (page,field) in new[]{("601","63"),("601","65"),("201","216")})
            catalog.ConfigPageColumns.Add(new(){Id=page+field,ConfigPageId=page,ConfigColumnId=field});
        if(cross)
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id=="216");
            control.ColumnName="area_id";control.LinkConfigTableId="7";
            control.ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]
            {
                new{table=prefix+"_tree",columnPrefix="country"},
                new{table=prefix+"_area",columnPrefix="area",parentField="dr_names_tree_id"}
            }});
            catalog.ConfigTables.Add(new(){Id="7",TableName=prefix+"_area",IsRongyu=1});
            catalog.ConfigColumns.AddRange([
                new(){Id="71",ConfigTableId="7",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="72",ConfigTableId="7",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="73",ConfigTableId="7",ColumnName="name",LogicalType="string"},
                new(){Id="74",ConfigTableId="7",ColumnName="dr_names_tree_id",LogicalType="int64",LinkConfigTableId="6"}
            ]);
            catalog.ConfigPages.Add(new(){Id="701",ConfigTableId="7",WhereSql="yz_project_id=@current_yz_project_id"});
            catalog.ConfigPageColumns.Add(new(){Id="70173",ConfigPageId="701",ConfigColumnId="73"});
            catalog.ConfigPageColumns.Add(new(){Id="70174",ConfigPageId="701",ConfigColumnId="74"});
        }
        if(!listCondition&&!parentOnly)
            target.WhereSql+=" AND "+logicalField+" <> '排除叶名' AND store_id=@current_store_id";
        else if(fixedTargetLogic)
            target.WhereSql+=" AND "+logicalField+" <> '目标页排除' AND store_id=@current_store_id";
        else if(globalWithProject is null)
            target.WhereSql+=" AND store_id=@current_store_id";
        if(globalWithProject is {} keepProject)
        {
            foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "2" or "4" or "6"))
                table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            target.WhereSql=target.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
            if(listCondition&&parent)target.WhereSql=target.WhereSql.Replace(" AND group_id=@parent_id","",StringComparison.Ordinal);
            if(selection&&parent)target.IsExport=1;
            catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="";
            catalog.ConfigPages.Single(page=>page.Id=="601").WhereSql="name <> '停用树节点'";
            if(!keepProject)
            {
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="22");
                catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="22");
            }
        }
        if(logicalParent)
        {
            if(!globalParent)
            {
                catalog.ConfigTables.Single(table=>table.Id=="6").Extra.Remove("scopeKind");
                catalog.ConfigPages.Single(page=>page.Id=="601").WhereSql="yz_project_id=@current_yz_project_id AND name <> '停用树节点'";
            }
            if(globalParent&&globalWithProject is null)
            {
                catalog.ConfigTables.Single(table=>table.Id=="6").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                catalog.ConfigPages.Single(page=>page.Id=="601").WhereSql="name <> '停用全局父' AND store_id=@current_store_id";
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="62");
                catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="62");
                catalog.ConfigColumns.Add(new(){Id="66",ConfigTableId="6",ColumnName="store_id",LogicalType="int64"});
            }
            catalog.ConfigColumns.Single(column=>column.Id=="19").LinkConfigTableId="6";
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="29");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="29");
            target.WhereSql=target.WhereSql.Contains("group_id=@parent_id",StringComparison.Ordinal)
                ?target.WhereSql.Replace("group_id=@parent_id","country_id=@parent_id",StringComparison.Ordinal)
                :target.WhereSql+" AND country_id=@parent_id";
        }
        if(independentScope)
        {
            // 独立表单通过原可编辑物理字段提供父键，弹层从行数据取值。
            var parentColumn=catalog.ConfigColumns.Single(column=>column.Id=="19");
            foreach(var source in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
            {
                source.WhereSql="yz_project_id=@current_yz_project_id";
                var field=catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==source.Id&&item.ConfigColumnId==parentColumn.Id);
                if(field is null)
                {
                    field=new(){Id=source.Id+"_"+parentColumn.Id,ConfigPageId=source.Id,ConfigColumnId=parentColumn.Id};
                    catalog.ConfigPageColumns.Add(field);
                }
                field.IsShow=1;field.IsEdit=1;
                field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(1);
                field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(1);
            }
        }
        if(logicalSource)
        {
            if(!logicalParent)
            {
                catalog.ConfigColumns.Single(column=>column.Id=="29").LinkConfigTableId="6";
                catalog.ConfigTables.Single(table=>table.Id=="6").Extra.Remove("scopeKind");
                catalog.ConfigPages.Single(page=>page.Id=="601").WhereSql="yz_project_id=@current_yz_project_id AND name <> '停用树节点'";
            }
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="19");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="19");
            catalog.ConfigColumns.Add(new(){Id="120",ConfigTableId="1",ColumnName="origin_area_id",LogicalType="int64",
                LinkConfigTableId="7",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new
                {
                    sourceMode="crossTable",levels=new object[]{
                        new{table=prefix+"_tree",columnPrefix="origin_country"},
                        new{table=prefix+"_area",columnPrefix="origin_area",parentField="dr_names_tree_id"}}
                })});
            foreach(var source in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
            {
                source.WhereSql=source.WhereSql.Replace("group_id=@parent_id","origin_country_id=@parent_id",StringComparison.Ordinal);
                catalog.ConfigPageColumns.Add(new(){Id=source.Id+"120",ConfigPageId=source.Id,ConfigColumnId="120",IsShow=1,IsEdit=1});
            }
            if(!selection)
            {
                var original=catalog.ConfigColumns.Single(column=>column.Id=="120");
                catalog.ConfigColumns.Add(new(){Id="121",ConfigTableId="1",ColumnName="origin_other_id",LogicalType="int64",
                    LinkConfigTableId="7",ControlKind="CascadeTree",
                    ControlDetail=original.ControlDetail.Replace("origin_area","origin_other",StringComparison.Ordinal)});
                foreach(var source in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                    catalog.ConfigPageColumns.Add(new(){Id=source.Id+"121",ConfigPageId=source.Id,ConfigColumnId="121",IsShow=1,IsEdit=1});
            }
        }
        if(parentOnly&&redundant)target.WhereSql="country_id=@parent_id";
        if(globalParent&&!logicalParent)
        {
            catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="32");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="32");
            catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="name <> '父页隐藏'";
        }
        if(redundant)
        {
            catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;
            if(!selection&&globalWithProject is null&&!parent)
            {
                // 先输入较晚的后台页，物理副本和原候选仍须按已发布页顺序选择201。
                foreign.Extra.Remove("linkConfigPageId");
                catalog.ConfigPages.Insert(0,new(){Id="202",ConfigTableId="2",YzAdminTypeId="5"});
            }
            // 夹具只保留本次要检查的名称副本，原来源父页仍照常校验。
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "16" or "17");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "16" or "17");
            catalog.ConfigColumns.Add(new(){Id="118",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
            {
                page.OrderBy="id desc";
                page.WhereSql+=" AND store_id=@current_store_id";
                if(selection)
                    foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId=="15"))
                        field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            }
        }
        if(nested)
        {
            // 上游原控件选择全局中间表的派生名称，其依赖仍由中间表实际父级确定。
            catalog.ConfigTables.Single(table=>table.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            if(selection)catalog.ConfigPages.Single(page=>page.Id=="101").Extra["type"]=JsonSerializer.SerializeToElement(1);
            var display=catalog.ConfigColumns.Single(column=>column.Id=="113").ColumnName;
            catalog.ConfigTables.Add(new(){Id="8",TableName=prefix+"_observer",IsRongyu=nestedCache?1:0});
            catalog.ConfigPages.Add(new(){Id="801",ConfigTableId="8",WhereSql="scope_country_id=@parent_id"+
                (nestedCache?" AND store_id=@current_store_id":""),IsImport=1,IsExport=1});
            if(nestedCache)catalog.ConfigColumns.Add(new(){Id="86",ConfigTableId="8",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigColumns.AddRange([
                new(){Id="81",ConfigTableId="8",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="82",ConfigTableId="8",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="83",ConfigTableId="8",ColumnName="scope_country_id",LogicalType="int64",LinkConfigTableId="6"},
                new(){Id="84",ConfigTableId="8",ColumnName="middle_id",Name="中间记录",LogicalType="int64",
                    ControlKind=selection?"SelectPage":"List",LinkConfigTableId="1",
                    ControlDetail=JsonSerializer.Serialize(new{tableId="1",pageId="101",nameField=display})},
                new(){Id="85",ConfigTableId="8",ColumnName="middle_"+display,LogicalType="string",Extra=new()
                {
                    ["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("middle."+display)
                }}
            ]);
            if(globalCache)
            {
                catalog.ConfigTables.Single(table=>table.Id=="8").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                if(rowProjectType is null)catalog.ConfigColumns.RemoveAll(column=>column.Id=="82");
                else catalog.ConfigColumns.Single(column=>column.Id=="82").LogicalType=rowProjectType;
                if(pageOnly)
                {
                    // 原选择页编辑器保存页面和显示配置，表 ID 由页面解析。
                    var picker=catalog.ConfigColumns.Single(column=>column.Id=="84");
                    picker.LinkConfigTableId="";
                    picker.ControlDetail=JsonSerializer.Serialize(new{pageId="101",nameField=display,buttonText="选择",whereSql="",orderBy=""});
                }
            }
            catalog.ConfigPageColumns.AddRange([
                new(){Id="80184",ConfigPageId="801",ConfigColumnId="84",IsShow=1,IsEdit=1},
                new(){Id="80185",ConfigPageId="801",ConfigColumnId="85",IsShow=1,IsEdit=0}
            ]);
            if(buttonOverride)
                catalog.ConfigPageColumns.Single(column=>column.Id=="80184").Extra["controlDetail"]=
                    JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{buttonText="查找中间记录"}));
        }
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-relation-page-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ScopedSingleRelation");
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            var scope=derived?"DerivedOptionsPlan101_15":"OptionsMembership101_15";
            if(globalWithProject is not null||logicalParent)
            {
                if(listCondition)
                {
                    Assert.Contains("CompileGlobalListCandidate(\"2\",\"201\"",service);
                    Assert.Contains($"ApplyCondition(query,session,{scope}.BindAccount(actor),OptionsCondition101_15.BindVariables",service);
                    Assert.DoesNotContain($"ApplyCandidate(query,session,{scope}.BindAccount(actor)",service);
                    if(globalWithProject is not null)Assert.Contains("AccountConditionVariables.Require(actor",service);
                    if(parent)Assert.Contains("FixedConditionFormValue.Require(formValues",service);
                    if(cross)Assert.Contains(JsonSerializer.Serialize("覆盖条件")[1..^1],service);
                    if(derived)Assert.Contains("condition=condition is null?bound:condition.And(bound)",service);
                }
                else
                {
                    Assert.Contains("CompileCrossPageCandidate(\"2\",\"201\"",service);
                    Assert.Contains($"ApplyCandidate(query,session,{scope}.BindAccount(actor),\"options_page_101_15\",actor.ProjectId,locking{(parent?",targetParentId":"")})",service);
                }
                var start=service.IndexOf(">> OptionsScope101_15Async(",StringComparison.Ordinal);
                Assert.True(start>=0);
                var end=service.IndexOf("\n    }",start,StringComparison.Ordinal);
                if(globalParent)
                    Assert.Contains("OptionsGlobalParent101_15.BindAccount(actor)",service[start..end]);
                else if(!logicalSource)
                    Assert.Contains("sourceParentKey && entity.YzProjectId == ",service[start..end]);
                if(logicalSource)
                {
                    Assert.Contains("CrossReader101_120(actor,locking,false).PathAsync(0,LogicalParentId101(parentId)",service[start..end]);
                    Assert.Contains("targetParentId!=parentId",service[start..end]);
                    Assert.Contains("OptionsScope101_15Async(logicalRelationParentId, null",service);
                    Assert.Contains("logicalRelationParentId: parentId",service);
                }
                Assert.Equal(globalWithProject is null,service[start..end].Contains("query = query.Where(entity => entity.YzProjectId",StringComparison.Ordinal));
                if(logicalParent)
                {
                    Assert.DoesNotContain("query = query.Where(entity => entity.CountryId",service[start..end]);
                    if(listCondition)Assert.Contains("actor.ProjectId,locking,targetParentId)",service[start..end]);
                }
            }
            else
            {
                Assert.Contains("CompileRelationMembership(",service);
                if(listCondition)
                {
                    Assert.Contains($"ApplyCondition(query,session,{scope}.BindAccount(actor),OptionsCondition101_15.BindVariables",service);
                    Assert.Contains("\"options_101_15\",actor.ProjectId,locking)",service);
                    Assert.Contains("OptionsPageCondition101_15.BindAccount(actor)",service);
                    if(parent)Assert.Contains("FixedConditionFormValue.Require(formValues",service);
                    if(cross)Assert.Contains(JsonSerializer.Serialize("覆盖条件")[1..^1],service);
                }
                else Assert.Contains($"ApplyCondition(query,session,{scope}.BindAccount(actor),OptionsPageCondition101_15.BindAccount(actor)",service);
            }
            Assert.Contains("OptionsSelected101_15Async",service);
            Assert.Contains("OptionsPage101_15Async",service);
            Assert.Contains("OptionsScope104_15Async",service);
            Assert.DoesNotContain("public Task<IReadOnlyList<GeneratedOption>> OptionsSelected104_15Async",service);
            Assert.DoesNotContain("public async Task<IReadOnlyList<GeneratedOption>> OptionsSelected104_15Async",service);
            Assert.Contains("Import101Async",service);
            Assert.Contains("Export101Async",service);
            if(selection&&parent&&globalWithProject is not null)
            {
                var targetService=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesPartyService.g.cs"));
                Assert.Contains("CompileGlobalSingleParentPage(\"2\",\"201\"",targetService);
                Assert.Contains("KeyRowsAsync(CandidateReadPlan201.BindAccount(actor)",targetService);
                Assert.Contains("ExportReadPlan201=global::Example.ScopedSingleRelation.Generated.GeneratedNonRedundantReadCatalog.CompileGlobalSingleParentPage(",targetService);
                if(logicalParent)
                {
                    var detail=File.ReadAllText(Path.Combine(directory,"generated/frontend/pages/yzp/dr_names_order-detail-101.vue"));
                    Assert.Contains(":parent_id=\"parent_id\"",detail);
                }
            }
            if(logicalParent&&globalWithProject is null)
            {
                if(selection)
                {
                    var detail=File.ReadAllText(Path.Combine(directory,"generated/frontend/pages/yzp/dr_names_order-detail-101.vue"));
                    Assert.Contains(independentScope?":parent_id=\"entity.groupId\"":":parent_id=\"parent_id\"",detail);
                }
                if(!redundant)Assert.Contains("ReadPlan101=global::Example.ScopedSingleRelation.Generated.GeneratedNonRedundantReadCatalog.CompileProjectPage(",service);
                if(globalParent&&!logicalSource)
                {
                    if(independentScope)
                    {
                        var block=service[service.IndexOf("private async Task ValidateRelations101Async(",StringComparison.Ordinal)..];
                        Assert.Contains("OptionsGlobalParent101_15.BindAccount(actor)",block);
                        Assert.Contains("submitted.Contains(\"group_id\")) && entity.CustomerId",block);
                    }
                    else Assert.Contains("PageMultiGlobalParent101.BindAccount(actor)",service);
                }
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var name in new[]{"DrNamesOrderService","DrNamesPartyService"})
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    assembly.GetType("Example.ScopedSingleRelation.Generated.Services."+name,true)!.TypeHandle);

            if(terminalProjectScope)
            {
                Assert.Contains("singleTargets:new NonRedundantSingleTargetScope[]",service);
                Assert.Contains("DerivedOptionsPlan101_15.BindAccount(actor)",service);
                var type=assembly.GetType("Example.ScopedSingleRelation.Generated.Services.DrNamesOrderService",true)!;
                var plan=Assert.IsType<Metarill.Data.Queries.NonRedundantReadPlan>(
                    type.GetField("DerivedOptionsPlan101_15",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                var actor=new Metarill.Core.Security.RuntimeActor(5,"验收",false,catalog.YzProjectId,"5"){StoreId=6351};
                var bound=plan.BindAccount(actor);
                const BindingFlags instance=BindingFlags.Instance|BindingFlags.NonPublic;
                var mysql=bound.GetType().GetProperty("MySql",instance)!.GetValue(bound)!;
                var sql=Assert.IsType<string>(mysql.GetType().GetProperty("From",instance)!.GetValue(mysql));
                Assert.Contains("EXISTS (SELECT 1 FROM `dr_names_company`",sql);
                Assert.Contains("EXISTS (SELECT 1 FROM `dr_names_region`",sql);
                var logicalSql=Assert.IsType<string>(mysql.GetType().GetMethod("Expression",instance)!.Invoke(mysql,[logicalField]));
                Assert.Contains("dr_names_tree",sql+logicalSql);
                var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    mysql.GetType().GetProperty("Constants",instance)!.GetValue(mysql));
                Assert.True(constants.Values.Count(value=>Equals(value,6351L))>=2);
                Assert.Contains("公司筛选",constants.Values);
                Assert.Contains("区域筛选",constants.Values);
                Assert.Equal(logicalParent?"country_id":null,mysql.GetType().GetProperty("RequiredMultiParent",instance)!.GetValue(mysql));
            }

            if(nested)
            {
                var observer=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesObserverService.g.cs"));
                Assert.Contains("CompileCrossPageCandidate(\"1\",\"101\"",observer);
                Assert.Contains("OptionsSelected801_84Async",observer);
                Assert.Contains("Import801Async",observer);
                Assert.Contains("Export801Async",observer);
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    assembly.GetType("Example.ScopedSingleRelation.Generated.Services.DrNamesObserverService",true)!.TypeHandle);
                if(nestedCache)
                {
                    var cache=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                        assembly.GetType("Example.ScopedSingleRelation.Generated.GeneratedRelationMaintenance",true)!
                            .GetProperty("Catalog")!.GetValue(null));
                    Assert.Contains("dr_names_tree",cache.MixedTargets);
                    Assert.Contains("dr_names_area",cache.MixedTargets);
                    Assert.Contains("dr_names_party",cache.MixedTargets);
                    Assert.Contains("dr_names_order",cache.MixedTargets);
                    Assert.Contains("dr_names_observer",cache.MixedSourceColumns.Keys);
                    if(globalCache)
                    {
                        Assert.Contains("当前身份不属于已发布全局来源目录",observer);
                        Assert.Contains("ProjectRelationMutationPropagator(session).AfterMutationAsync",observer);
                        if(rowProjectType is not null)
                        {
                            // 业务页及写入保持行项目隔离，候选、名称读取和维护仍共用发布目标页。
                            var projectValue="long.Parse(actor.ProjectId, CultureInfo.InvariantCulture)"+
                                (rowProjectType=="string"?".ToString(CultureInfo.InvariantCulture)":"");
                            Assert.Contains("query = query.Where(entity => entity.YzProjectId == "+projectValue+")",observer);
                            Assert.Contains("entity.YzProjectId = "+projectValue+";",observer);
                            Assert.Contains("if(entity.YzProjectId!="+projectValue+")",observer);
                            Assert.Contains("CompileCrossPageCandidate(\"1\",\"101\"",observer);
                        }
                        if(selection)
                        {
                            var detail=File.ReadAllText(Path.Combine(directory,"generated/frontend/pages/yzp/dr_names_observer-detail-801.vue"));
                            Assert.Contains(":parent_id=\"parent_id\"",detail);
                            if(buttonOverride)
                            {
                                Assert.Contains("'>查找中间记录</button>",detail);
                                Assert.Contains("dr_names_order-list-101.vue",detail);
                            }
                        }
                    }
                }
            }

            if(buttonOverride)
            {
                // 纯文案可以继承。重设范围、排序或实体仍会在发布前被拒绝。
                var overrideField=catalog.ConfigPageColumns.Single(column=>column.Id=="80184");
                foreach(var item in new[]{new{Key="whereSql",Value="id IS NOT NULL"},new{Key="orderBy",Value="id asc"},
                    new{Key="tableId",Value="2"},new{Key="table",Value=prefix+"_party"}})
                {
                    overrideField.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(
                        new Dictionary<string,string>{{"buttonText","查找中间记录"},{item.Key,item.Value}}));
                    var error=Record.Exception(()=>GenerationModel.Create(catalog,"Example.ScopedSingleRelation"));
                    Assert.True(error is NotSupportedException or InvalidOperationException);
                }
            }

            if(redundant)
            {
                var model=GenerationModel.Create(catalog,"Example.ScopedSingleRelation");
                var cache=MixedRelationPropagationCompilation.ForModel(model);
                Assert.Contains("dr_names_tree",cache.MixedTargets);
                Assert.Contains("dr_names_party",cache.MixedTargets);
                if(cross)Assert.Contains("dr_names_area",cache.MixedTargets);
                Assert.Contains("customer_"+label,cache.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("ProjectRelationMutationPropagator(session).AfterMutationAsync",service);
                var published=assembly.GetType("Example.ScopedSingleRelation.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null);
                Assert.Contains("dr_names_tree",Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(published).MixedTargets);
            }
            if(globalWithProject is not null&&!cross)
            {
                catalog.ConfigTables.Single(table=>table.Id=="6").Extra.Remove("scopeKind");
                if(listCondition)
                    Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileGlobalListCandidate(
                        catalog,"2","201",[logicalField],["id","name"],[]));
                else
                    Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.NonRedundantReads.CompileCrossPageCandidate(
                        catalog,"2","201",["id","name"],[]));
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>物理账号条件覆盖单值候选、单选目标页、当前读、工作簿和名称副本。</summary>
    [Theory]
    [InlineData(false,false,false,false)]
    [InlineData(false,true,true,false)]
    [InlineData(true,false,true,false)]
    [InlineData(true,true,false,false)]
    [InlineData(false,false,true,true)]
    [InlineData(false,true,false,true)]
    [InlineData(true,false,false,true)]
    [InlineData(true,true,true,true)]
    [InlineData(false,false,true,true,false,false)]
    [InlineData(true,true,true,true,false,false)]
    [InlineData(false,true,true,true,false,true)]
    [InlineData(true,false,true,true,false,true)]
    [InlineData(false,false,true,false,false,false)]
    [InlineData(false,true,true,false,false,true)]
    [InlineData(true,true,true,false,false,false)]
    [InlineData(true,false,true,false,false,true)]
    [InlineData(false,false,false,false,true,true)]
    [InlineData(true,true,true,false,true,true)]
    [InlineData(false,true,true,true,true,true)]
    [InlineData(true,false,false,true,true,true)]
    [InlineData(false,false,false,false,true,true,true)]
    [InlineData(true,true,true,false,true,true,true)]
    [InlineData(false,true,true,true,true,true,true)]
    [InlineData(true,false,false,true,true,true,true)]
    [InlineData(true,false,true,true,true,true,true,true)]
    [InlineData(false,false,true,true,false,true,false,false,true,false)]
    [InlineData(true,true,true,true,false,true,false,false,true,false)]
    [InlineData(false,true,true,true,false,false,false,false,true,true)]
    [InlineData(true,false,true,true,false,true,false,false,true,true)]
    [InlineData(true,false,true,true,false,true,false,true,true,true)]
    public async Task Single_account_page_uses_published_scope_without_cascade(
        bool selection,bool derived,bool rowProject,bool cached,bool global=true,bool parent=false,
        bool globalParent=false,bool independentParent=false,bool physicalOnly=false,bool fixedConstant=true)
    {
        var catalog=Catalog("dr_names",parent);
        var target=catalog.ConfigPages.Single(page=>page.Id=="201");
        target.WhereSql=physicalOnly
            ?"yz_project_id=@current_yz_project_id"+(fixedConstant?" AND status=1":"")+(parent?" AND group_id=@parent_id":"")
            :(global?"":"yz_project_id=@current_yz_project_id AND ")+
                "status=1 AND store_id=@current_store_id"+(parent?" AND group_id=@parent_id":"");
        if(selection)target.Extra["type"]=JsonSerializer.SerializeToElement(1);
        if(global&&parent&&selection)target.IsExport=1;
        if(global)
        {
            foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "2" or "4"))
                table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="";
        }
        if(globalParent)
        {
            catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="32");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="32");
            catalog.ConfigColumns.Add(new(){Id="317",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="name <> '父页隐藏' AND store_id=@current_store_id";
        }
        if(independentParent)
        {
            var group=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="1"&&column.ColumnName=="group_id");
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
            {
                page.WhereSql="yz_project_id=@current_yz_project_id";
                var field=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==group.Id);
                if(field is null)
                {
                    field=new(){Id=page.Id+group.Id,ConfigPageId=page.Id,ConfigColumnId=group.Id};
                    catalog.ConfigPageColumns.Add(field);
                }
                field.IsShow=1;field.IsEdit=1;
                field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(1);
                field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(1);
            }
        }
        if(!rowProject)
        {
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="22");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="22");
        }
        catalog.ConfigColumns.Add(new(){Id="217",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var label=derived?"company_name":"name";
        foreign.ControlKind=selection?"SelectPage":"List";
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField=label,
            whereSql=selection&&!physicalOnly?"store_id=@current_store_id":""});
        var display=catalog.ConfigColumns.Single(column=>column.Id=="113");
        display.ColumnName="customer_"+label;
        display.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer."+label);
        if(cached)
        {
            catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;
            catalog.ConfigColumns.RemoveAll(column=>column.Id is "16" or "17");
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "16" or "17");
            catalog.ConfigColumns.Add(new(){Id="118",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
            {
                page.OrderBy="id desc";
                if(!physicalOnly)page.WhereSql+=" AND store_id=@current_store_id";
                if(selection)
                    foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId=="15"))
                        field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            }
        }
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);
        var separateTarget=selection&&!cached&&!global&&!parent;
        if(separateTarget)
        {
            var other=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(target))!;
            other.Id="202";
            other.WhereSql="yz_project_id=@current_yz_project_id AND status=2 AND store_id=@current_store_id";
            catalog.ConfigPages.Add(other);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
            {
                var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                copy.Id="202_"+field.ConfigColumnId;
                copy.ConfigPageId="202";
                catalog.ConfigPageColumns.Add(copy);
            }
            catalog.ConfigPageColumns.Add(new(){Id="10415",ConfigPageId="104",ConfigColumnId="15",IsEdit=0,IsShow=1,
                YzProjectId=catalog.YzProjectId,Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement("{\"pageId\":\"202\"}")}});
        }

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-account-page");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.SingleAccountPage");
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            if(!physicalOnly)Assert.Contains("OptionsPageCondition101_15.BindAccount(actor).ApplyConstants",service);
            else
            {
                var validation=service[service.IndexOf("private async Task ValidateRelations101Async(",StringComparison.Ordinal)..];
                Assert.Contains("await OptionsScope101_15Async(",validation);
                Assert.Contains("originalCandidateValues.CustomerId",service);
                if(parent)Assert.Contains("entity.GroupId == long.Parse(targetParentId!",service);
                if(independentParent)
                {
                    Assert.Contains("originalCandidateValues.GroupId",service);
                    Assert.Contains("submitted.Contains(\"group_id\")) && entity.CustomerId",validation);
                }
            }
            Assert.Contains("OptionsPage101_15Async",service);
            Assert.Contains("OptionsSelected101_15Async",service);
            Assert.Contains("OptionsScope104_15Async",service);
            if(globalParent)
            {
                Assert.Contains("OptionsGlobalParent101_15.BindAccount(actor)",service);
                Assert.Contains("targetParentKey",service);
                Assert.Equal(!independentParent,service.Contains("PageMultiGlobalParent101.BindAccount(actor)",StringComparison.Ordinal));
                Assert.Contains("currentRead:locking",service);
            }
            Assert.Contains("Import101Async",service);
            Assert.Contains("Export101Async",service);
            Assert.Contains(".ForUpdate()",service);
            if(!cached&&!global)
            {
                Assert.Contains("ReadPlan101=global::Example.SingleAccountPage.Generated.GeneratedNonRedundantReadCatalog.CompileProjectPage(",service);
                Assert.Contains("ExportReadPlan101=global::Example.SingleAccountPage.Generated.GeneratedNonRedundantReadCatalog.CompileProjectPage(",service);
                Assert.Contains("ReadPlan101.BindAccount(actor)",service);
                Assert.Contains(separateTarget?"new(\"15\",\"202\",null)":"new(\"15\",\"201\",",service);
            }
            if(derived)
            {
                Assert.Contains(global?"CompileGlobalCandidate(\"2\",\"201\"":"CompileRelation(\"2\"",service);
                Assert.Contains(physicalOnly?"DerivedOptionsPlan101_15,scope.Filters":"DerivedOptionsPlan101_15.BindAccount(actor)",service);
                if(!global&&!physicalOnly)Assert.Contains("OptionsPageCondition101_15.BindAccount(actor)",service);
                if(physicalOnly&&fixedConstant)Assert.Contains("OptionsPageCondition101_15",service);
                Assert.Contains("currentRead:true",service);
            }
            if(global&&parent&&selection)
            {
                var targetService=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesPartyService.g.cs"));
                Assert.Contains("CompileGlobalSingleParentPage(\"2\",\"201\"",targetService);
                Assert.Contains("KeyRowsAsync(CandidateReadPlan201.BindAccount(actor)",targetService);
                Assert.Contains("ExportReadPlan201=global::Example.SingleAccountPage.Generated.GeneratedNonRedundantReadCatalog.CompileGlobalSingleParentPage(",targetService);
                if(globalParent)
                {
                    var detail=File.ReadAllText(Path.Combine(directory,"generated/frontend/pages/yzp/dr_names_order-detail-101.vue"));
                    Assert.Contains(independentParent?":parent_id=\"entity.groupId\"":":parent_id=\"parent_id\"",detail);
                }
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var serviceName in new[]{"DrNamesOrderService","DrNamesPartyService"})
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    assembly.GetType("Example.SingleAccountPage.Generated.Services."+serviceName,true)!.TypeHandle);
            if(globalParent)
            {
                var frozen=Assert.IsType<MetadataCatalog>(assembly.GetType("Example.SingleAccountPage.Generated.GeneratedNonRedundantReadCatalog",true)!
                    .GetField("Snapshot",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null));
                Assert.Equal("name <> '父页隐藏' AND store_id=@current_store_id",frozen.ConfigPages.Single(page=>page.Id=="301").WhereSql);
            }
            if(cached)
            {
                var compiled=assembly.GetType("Example.SingleAccountPage.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null);
                var cache=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(compiled);
                Assert.Contains("customer_"+label,cache.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("dr_names_party",cache.MixedTargets);
                if(globalParent||physicalOnly&&parent)Assert.Contains("dr_names_group",cache.MixedTargets);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>冗余字典的物理筛选与实际页面一同进入候选、来源名称读取、写入和 Excel。</summary>
    [Theory]
    [InlineData(0, "constant")]
    [InlineData(0, "parent")]
    [InlineData(0, "account")]
    [InlineData(1, "constant")]
    [InlineData(1, "parent")]
    [InlineData(1, "account")]
    public async Task List_uses_the_selected_page_of_a_stored_dictionary(int sourceStorage, string scope)
    {
        const string ns = "Example.StoredListPage";
        var catalog = Catalog("dr_names", scope == "parent");
        var ownerTable = catalog.ConfigTables.Single(table => table.Id == "1");
        var targetTable = catalog.ConfigTables.Single(table => table.Id == "2");
        ownerTable.IsRongyu = sourceStorage;
        targetTable.IsRongyu = 1;
        // 混合项目使用原 V8 固定候选。同项目的非冗余实体用于确定模式。
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 0;
        var obsolete = catalog.ConfigColumns.Where(column => column.Id is "27" or "28").Select(column => column.Id).ToHashSet();
        catalog.ConfigColumns.RemoveAll(column => obsolete.Contains(column.Id));
        catalog.ConfigPageColumns.RemoveAll(column => obsolete.Contains(column.ConfigColumnId));
        var cache = catalog.ConfigColumns.Single(column => column.Id == "113");
        cache.ColumnName = "customer_name";
        cache.Extra["columnFullName"] = JsonSerializer.SerializeToElement("customer.name");
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "15");
        foreign.ControlDetail = """{"tableId":"2","nameField":"name","pageId":"202"}""";
        var target = catalog.ConfigPages.Single(page => page.Id == "201");
        var alternate = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(target))!;
        alternate.Id = "202";
        alternate.WhereSql = "yz_project_id=@current_yz_project_id AND status=87" +
            (scope == "parent" ? " AND group_id=@parent_id" : "");
        alternate.OrderBy = "id desc";
        if (scope == "account")
        {
            catalog.ConfigColumns.Add(new() { Id = "11201", ConfigTableId = "1", YzProjectId = catalog.YzProjectId,
                ColumnName = "store_id", LogicalType = "int64" });
            catalog.ConfigColumns.Add(new() { Id = "21201", ConfigTableId = "2", YzProjectId = catalog.YzProjectId,
                ColumnName = "store_id", LogicalType = "int64" });
            foreach (var page in catalog.ConfigPages.Where(page => page.ConfigTableId == "1"))
                page.WhereSql += " AND store_id=@current_store_id";
            alternate.WhereSql += " AND store_id=@current_store_id";
        }
        catalog.ConfigPages.Add(alternate);
        foreach (var column in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == "201").ToArray())
        {
            var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(column))!;
            copy.Id = "202" + copy.Id;
            copy.ConfigPageId = alternate.Id;
            catalog.ConfigPageColumns.Add(copy);
        }
        if (sourceStorage == 0)
        {
            // 仅来源 101 改页，其它来源页继续沿用默认绑定。
            foreign.ControlDetail = """{"tableId":"2","nameField":"name"}""";
            catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "101" && field.ConfigColumnId == foreign.Id)
                .Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"pageId":"202"}""");
        }

        var model = GenerationModel.Create(catalog, ns);
        var owner = model.Tables.Single(table => table.Source.Id == "1");
        var relation = Assert.Single(RelationCompilation.ForPage(model,owner.Pages.Single(page => page.Source.Id == "101")),
            relation => relation.SourceColumn.Source.Id == foreign.Id);
        Assert.Equal("202", relation.TargetPage.Source.Id);
        Assert.True(Assert.Single(relation.OptionsOrder!).Descending);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-stored-list-page");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var code = await File.ReadAllTextAsync(Path.Combine(output, "Services", "DrNamesOrderService.g.cs"));
            Assert.Contains("status=87", code.Replace(" ", "", StringComparison.Ordinal));
            Assert.Contains("ImportRelation101_15Async", code);
            Assert.Contains("OptionsScope101_15Async", code);
            Assert.Contains(".ForUpdate().ToListAsync(row => new { row.Id }", code);
            if (scope == "parent") Assert.Contains("if (locking) { if ((await", code);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.DrNamesOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);

            if (sourceStorage == 1)
            {
                var binding = Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                    item => item.Foreign.Source.Id == foreign.Id);
                Assert.Equal("202", binding.ScopedSinglePageId);
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("customer_name", maintenance.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("dr_names_party", maintenance.MixedTargets);
                if (scope == "parent") Assert.Contains("dr_names_group", maintenance.MixedTargets);
            }
            else
            {
                var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan101", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                var mysql = plan.GetType().GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plan)!;
                var from = Assert.IsType<string>(mysql.GetType().GetProperty("From", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mysql));
                Assert.Contains(".\u0060status\u0060", from);
                if (scope == "parent") Assert.Contains(".\u0060group_id\u0060", from);
                if (scope == "account") Assert.Contains(".\u0060store_id\u0060", from);
                var actor = new Metarill.Core.Security.RuntimeActor(7, "合成账号", false, catalog.YzProjectId, "5") { StoreId = 9007199254740993L };
                var bound = plan.BindAccount(actor);
                var boundSql = mysql.GetType().GetProperty("Constants", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var constants = Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(boundSql.GetValue(
                    bound.GetType().GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bound)));
                Assert.Contains(constants.Values, value => value is int number && number == 87);
                if (scope == "account") Assert.Contains(9007199254740993L, constants.Values);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }



    /// <summary>全局字典按原固定下拉选择实际页面，候选、业务名称和 Excel 共用其条件。</summary>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 0, true, true)]
    [InlineData(0, 1, false, true)]
    [InlineData(1, 1, true, true)]
    [InlineData(0, 0, true, false, true)]
    [InlineData(0, 1, true, false, true)]
    [InlineData(0, 0, true, true, true)]
    [InlineData(0, 1, true, true, true)]
    [InlineData(0, 0, true, false, true, true)]
    [InlineData(0, 0, true, false, true, true, true)]
    public async Task Fixed_list_uses_the_chosen_published_global_dictionary_page(
        int sourceStorage,int targetStorage,bool overlay,bool controlCondition=false,bool perPage=false,bool account=false,
        bool parent=false)
    {
        const string ns = "Example.GlobalListPage";
        var catalog = Catalog("dr_names", false);
        var source = catalog.ConfigTables.Single(table => table.Id == "1");
        var target = catalog.ConfigTables.Single(table => table.Id == "2");
        source.IsRongyu = sourceStorage;
        target.IsRongyu = targetStorage;
        target.Extra["scopeKind"] = JsonSerializer.SerializeToElement("global");
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 0;
        catalog.ConfigColumns.RemoveAll(column => column.Id is "22" or "27" or "28");
        catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "22" or "27" or "28");
        var display = catalog.ConfigColumns.Single(column => column.Id == "113");
        display.ColumnName = "customer_name";
        display.Extra["columnFullName"] = JsonSerializer.SerializeToElement("customer.name");
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "15");
        foreign.ControlDetail = overlay ? """{"tableId":"2","nameField":"name","pageId":"201"}"""
            : """{"tableId":"2","nameField":"name","pageId":"202"}""";
        if (controlCondition)
        {
            var definition=JsonSerializer.Deserialize<Dictionary<string,string>>(foreign.ControlDetail)!;
            definition["whereSql"]="status<>72";
            foreign.ControlDetail=JsonSerializer.Serialize(definition);
        }
        var original = catalog.ConfigPages.Single(page => page.Id == "201");
        original.WhereSql = "status=12";
        var alternate = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;
        alternate.Id = "202";
        alternate.WhereSql = "status=83";
        alternate.OrderBy = "id desc";
        if (account)
        {
            catalog.ConfigColumns.Add(new() { Id = "21200", ConfigTableId = "2", YzProjectId = catalog.YzProjectId,
                ColumnName = "store_id", LogicalType = "int64" });
            alternate.WhereSql += " AND store_id=@current_store_id";
        }
        if (parent)
        {
            catalog.ConfigColumns.Add(new() { Id = "21202", ConfigTableId = "2", YzProjectId = catalog.YzProjectId,
                ColumnName = "group_id", LogicalType = "int64", LinkConfigTableId = "3" });
            alternate.WhereSql += " AND group_id=@parent_id";
        }
        catalog.ConfigPages.Add(alternate);
        foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigPageId == original.Id).ToArray())
        {
            var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
            copy.Id = "202" + copy.Id;
            copy.ConfigPageId = alternate.Id;
            catalog.ConfigPageColumns.Add(copy);
        }
        if (perPage)
        {
            var field=catalog.ConfigPageColumns.Single(item=>item.ConfigPageId=="101"&&item.ConfigColumnId==foreign.Id);
            field.Extra["controlDetail"] = JsonSerializer.SerializeToElement(controlCondition
                ? """{"pageId":"202","whereSql":"status<>93"}""" : """{"pageId":"202"}""");
        }
        else if (overlay)
        {
            // 隐藏来源页也有默认外键，通过该页实际的控件配置证明相同发布范围。
            catalog.ConfigPageColumns.Add(new() { Id = "10415", ConfigPageId = "104", ConfigColumnId = foreign.Id,
                YzProjectId = catalog.YzProjectId, IsShow = 0, IsEdit = 0 });
            foreach (var field in catalog.ConfigPageColumns.Where(field => field.ConfigColumnId == foreign.Id))
                field.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"pageId":"202"}""");
        }

        var model = GenerationModel.Create(catalog, ns);
        var owner = model.Tables.Single(table => table.Source.Id == source.Id);
        var page = owner.Pages.Single(item => item.Source.Id == "101");
        var options = Assert.Single(RelationCompilation.ForPage(model,page,forExcel:true),
            item => item.SourceColumn.Source.Id == foreign.Id);
        Assert.Equal(alternate.Id, options.TargetPage.Source.Id);
        Assert.True(Assert.Single(options.OptionsOrder!).Descending);
        if (perPage)
        {
            var second = owner.Pages.Single(item=>item.Source.Id=="103");
            var originalOptions = Assert.Single(RelationCompilation.ForPage(model,second,forExcel:true),
                item=>item.SourceColumn.Source.Id==foreign.Id);
            Assert.Equal(original.Id,originalOptions.TargetPage.Source.Id);
            Assert.Equal(controlCondition?"status<>93":null,options.Conditions?.Definition);
            Assert.Equal(controlCondition?"status<>72":null,originalOptions.Conditions?.Definition);
            Assert.Throws<InvalidOperationException>(()=>GlobalSingleRelationTarget.Require(catalog,source,foreign,target));
            source.IsRongyu=1;
            try
            {
                var conflict=Assert.Throws<InvalidOperationException>(()=>
                    GlobalSingleRelationTarget.RequireCached(catalog,source,foreign,target));
                Assert.Contains("物理名称副本必须使用同一目标页",conflict.Message);
            }
            finally { source.IsRongyu=sourceStorage; }
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-global-list-page");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var code = await File.ReadAllTextAsync(Path.Combine(output, "Services", "DrNamesOrderService.g.cs"));
            Assert.Contains("status=83", code.Replace(" ", "", StringComparison.Ordinal));
            if (perPage) Assert.Contains("status=12",code.Replace(" ","",StringComparison.Ordinal));
            Assert.Contains("OptionsScope101_15Async", code);
            Assert.Contains("ImportRelation101_15Async", code);
            Assert.Contains("ImportRelation103_15Async", code);
            Assert.Contains("Export101Async", code);
            if (perPage)
            {
                await VerifyGate("101","202","201");
                await VerifyGate("103","201","202");
            }
            if (parent) Assert.Contains("targetParentId",code);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);

            async Task VerifyGate(string sourceId,string targetId,string otherId)
            {
                var pageName=owner.Pages.Single(item=>item.Source.Id==sourceId).Name;
                var controller=await File.ReadAllTextAsync(Path.Combine(output,"Controllers",pageName+"Controller.g.cs"));
                Assert.Contains($"RequirePagePermission(\"{targetId}\", \"list\")",controller);
                Assert.DoesNotContain($"RequirePagePermission(\"{otherId}\", \"list\")",controller);
            }
            var service = assembly.GetType(ns + ".Generated.Services.DrNamesOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            object plan;
            if (sourceStorage == 0)
            {
                var read = ReadPage("ReadPlan101");
                if (account)
                {
                    Assert.True(read.RequiresAccount);
                    var actor=new Metarill.Core.Security.RuntimeActor(7,"合成账号",false,catalog.YzProjectId,"5")
                        { StoreId = 9007199254740993L };
                    read=read.BindAccount(actor);
                    Assert.Contains(9007199254740993L,Constants(MySql(read)).Values);
                }
                plan = MySql(read);
                if (parent)
                {
                    var from=Assert.IsType<string>(plan.GetType().GetProperty("From",hidden)!.GetValue(plan));
                    Assert.Contains(".\u0060group_id\u0060",from);
                }
                if (perPage)
                {
                    foreach (var member in new[]{"ReadPlan103","ReadPlan104"})
                    {
                        var other=ReadPage(member);
                        Assert.False(other.RequiresAccount);
                        var otherValues=Constants(MySql(other)).Values;
                        Assert.Contains(12,otherValues);
                        Assert.DoesNotContain(83,otherValues);
                        Assert.DoesNotContain(93,otherValues);
                        Assert.Equal(controlCondition,otherValues.Contains(72));
                    }
                }
            }
            else
            {
                var binding = Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                    item => item.Foreign.Source.Id == foreign.Id);
                Assert.Equal(alternate.Id, binding.ScopedSinglePageId);
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("customer_name", maintenance.MixedSourceColumns["dr_names_order"]);
                var plans = Assert.IsAssignableFrom<System.Collections.IDictionary>(
                    maintenance.GetType().GetProperty("Plans", hidden)!.GetValue(maintenance));
                plan = plans[source.Id]!.GetType().GetProperty("Read")!.GetValue(plans[source.Id])!;
            }
            var values = Constants(plan).Values;
            Assert.Contains(83,values);
            Assert.DoesNotContain(12,values);
            Assert.Equal(controlCondition,values.Contains(perPage?93:72));
            if(perPage) Assert.DoesNotContain(72,values);

            NonRedundantReadPlan ReadPage(string name)=>Assert.IsType<NonRedundantReadPlan>(
                service.GetField(name,BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
            static object MySql(NonRedundantReadPlan read)=>read.GetType().GetProperty("MySql",hidden)!.GetValue(read)!;
            static IReadOnlyDictionary<string,object?> Constants(object read)=>Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                read.GetType().GetProperty("Constants",hidden)!.GetValue(read));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        foreign.Extra["linkConfigPageId"] = JsonSerializer.SerializeToElement("301");
        Assert.Throws<InvalidOperationException>(() =>
            _=sourceStorage==1?GlobalSingleRelationTarget.RequireCached(catalog,source,foreign,target):
                GlobalSingleRelationTarget.Require(catalog,source,foreign,target));
    }

    /// <summary>纯冗余项目的旧实体候选仍使用原非空覆盖规则。</summary>
    [Fact]
    public void List_blank_condition_keeps_the_nonempty_definition_in_legacy_mode()
    {
        var catalog = RelationOptionsContractTests.Catalog();
        foreach (var table in catalog.ConfigTables) table.IsRongyu = 1;
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "25");
        foreign.ControlDetail = """{"tableId":"3","nameField":"name","whereSql":"name like '%甲%'"}""";
        catalog.ConfigPageColumns.Single(field => field.ConfigColumnId == foreign.Id).Extra["controlDetail"] =
            JsonSerializer.SerializeToElement("""{"whereSql":null}""");
        var model = GenerationModel.Create(catalog, "Example.LegacyListCondition");
        var page = model.Tables.Single(table => table.Source.Id == foreign.ConfigTableId).Pages.Single();
        var options = Assert.Single(RelationCompilation.ForPage(model,page));
        Assert.Equal("name like '%甲%'", options.Conditions?.Definition);
    }

    /// <summary>原固定候选明确清空本页附加条件时，目标页常量、共享副本范围及原 Excel 出入口仍有效。</summary>
    [Theory]
    [InlineData(0, 0, "\"\"")]
    [InlineData(0, 0, "null")]
    [InlineData(0, 0, "\"  \"")]
    [InlineData(0, 1, "\"\"")]
    [InlineData(1, 0, "null")]
    [InlineData(1, 1, "\"\"")]
    public async Task List_explicit_blank_condition_clears_only_the_page_control(int sourceStorage, int targetStorage, string clear)
    {
        const string ns = "Example.ListPageCondition";
        var catalog = Catalog("dr_names", false);
        var source = catalog.ConfigTables.Single(table => table.Id == "1");
        var target = catalog.ConfigTables.Single(table => table.Id == "2");
        source.IsRongyu = sourceStorage;
        target.IsRongyu = targetStorage;
        catalog.ConfigTables.Single(table => table.Id == "3").IsRongyu = 0;
        if (targetStorage == 1)
        {
            catalog.ConfigColumns.RemoveAll(column => column.Id is "27" or "28");
            catalog.ConfigPageColumns.RemoveAll(field => field.ConfigColumnId is "27" or "28");
            var display = catalog.ConfigColumns.Single(column => column.Id == "113");
            display.ColumnName = "customer_name";
            display.Extra["columnFullName"] = JsonSerializer.SerializeToElement("customer.name");
        }
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "15");
        var definition = JsonSerializer.Deserialize<Dictionary<string,string>>(foreign.ControlDetail)!;
        if (targetStorage == 1) definition["nameField"] = "name";
        definition["whereSql"] = "status=72";
        foreign.ControlDetail = JsonSerializer.Serialize(definition);
        catalog.ConfigPages.Single(page => page.Id == "201").WhereSql = "yz_project_id=@current_yz_project_id AND status>=87";
        var overlay = JsonSerializer.SerializeToElement("{\"whereSql\":" + clear + "}");
        var visible = catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "101" && field.ConfigColumnId == foreign.Id);
        visible.Extra["controlDetail"] = overlay;
        catalog.ConfigPageColumns.Single(field => field.ConfigPageId == "103" && field.ConfigColumnId == foreign.Id)
            .Extra["controlDetail"] = overlay;
        var hidden = catalog.ConfigPages.Single(page => page.Id == "104");
        if (sourceStorage == 1)
        {
            // 即使第二页隐藏外键，混用清空与继承条件也不能发布同一份冗余名称。
            Assert.Throws<InvalidOperationException>(() =>
                ProjectScopedSingleRelationTarget.Resolve(catalog,source,foreign,target));
            catalog.ConfigPageColumns.Add(new() { Id = "10415", ConfigPageId = hidden.Id, ConfigColumnId = foreign.Id,
                YzProjectId = catalog.YzProjectId, IsShow = 0, IsEdit = 0,
                Extra = new() { ["controlDetail"] = overlay } });
        }

        var model = GenerationModel.Create(catalog, ns);
        var owner = model.Tables.Single(table => table.Source.Id == source.Id);
        var page = owner.Pages.Single(item => item.Source.Id == "101");
        var options = Assert.Single(RelationCompilation.ForPage(model,page,forExcel:true), item => item.SourceColumn.Source.Id == foreign.Id);
        Assert.Null(options.Conditions);
        Assert.Equal("201", options.TargetPage.Source.Id);
        Assert.Empty(ProjectScopedSingleRelationTarget.ControlScope(catalog, source, foreign, target, page.Source).Terms);

        if (sourceStorage == 0)
        {
            var inherited = Assert.Single(RelationCompilation.ForPage(model,owner.Pages.Single(item => item.Source.Id == hidden.Id),true),
                item => item.SourceColumn.Source.Id == foreign.Id);
            Assert.Equal("status=72", inherited.Conditions?.Definition);
        }

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-list-page-condition");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var code = await File.ReadAllTextAsync(Path.Combine(output, "Services", "DrNamesOrderService.g.cs"));
            Assert.DoesNotContain("OptionsCondition101_15", code);
            Assert.Contains("OptionsScope101_15Async", code);
            Assert.Contains("ImportRelation101_15Async", code);
            Assert.DoesNotContain("OptionsCondition103_15", code);
            Assert.Contains("ImportRelation103_15Async", code);
            Assert.Contains("OptionsPageCondition101_15", code);
            Assert.Contains("OptionsPageCondition103_15", code);
            Assert.Contains(JsonSerializer.Serialize("status>=87"), code);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.DrNamesOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);

            if (sourceStorage == 1)
            {
                Assert.Empty(ProjectScopedSingleRelationTarget.ControlScope(catalog, source, foreign, target).Terms);
                var binding = Assert.Single(RelationPropagationCompilation.DerivedBindings(model),
                    item => item.Foreign.Source.Id == foreign.Id);
                Assert.Equal("201", binding.ScopedSinglePageId);
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("customer_" + definition["nameField"], maintenance.MixedSourceColumns["dr_names_order"]);
                Assert.Contains("dr_names_party", maintenance.MixedTargets);
            }
            else
            {
                var constants = Constants("ReadPlan101");
                Assert.Contains(constants.Values, value => value is int number && number == 87);
                Assert.DoesNotContain(constants.Values, value => value is int number && number == 72);
                Assert.Contains(Constants("ReadPlan104").Values, value => value is int number && number == 72);
            }

            IReadOnlyDictionary<string,object?> Constants(string member)
            {
                var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField(member,
                    BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                var sql = plan.GetType().GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plan)!;
                return Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                    sql.GetType().GetProperty("Constants", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sql));
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>同一实体的实际目标页约束候选、来源回显、写校验和 Excel，冗余副本绑定同一页。</summary>
    [Theory]
    [InlineData(false, "column")]
    [InlineData(false, "overlay")]
    [InlineData(true, "column")]
    [InlineData(true, "overlay-all")]
    [InlineData(false, "page-only")]
    public async Task List_uses_the_configured_page_on_the_same_target(bool redundant, string binding)
    {
        const string ns = "Example.ListPublishedPage";
        var catalog = Catalog("dr_names", false);
        catalog.ConfigTables.Single(table => table.Id == "1").IsRongyu = redundant ? 1 : 0;
        var target = catalog.ConfigPages.Single(page => page.Id == "201");
        var alternate = JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(target))!;
        alternate.Id = "202";
        alternate.WhereSql = "yz_project_id=@current_yz_project_id AND status=83";
        alternate.OrderBy = "id desc";
        catalog.ConfigPages.Add(alternate);
        foreach (var setting in catalog.ConfigPageColumns.Where(setting => setting.ConfigPageId == target.Id).ToArray())
        {
            var copy = JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(setting))!;
            copy.Id = "202" + setting.Id;
            copy.ConfigPageId = alternate.Id;
            catalog.ConfigPageColumns.Add(copy);
        }
        var foreign = catalog.ConfigColumns.Single(column => column.Id == "15");
        if (binding is "column" or "page-only")
        {
            var definition = JsonSerializer.Deserialize<Dictionary<string,string>>(foreign.ControlDetail)!;
            definition["pageId"] = alternate.Id;
            if (binding == "page-only")
            {
                definition.Remove("tableId");
                definition.Remove("table");
                foreign.LinkConfigTableId = "";
            }
            foreign.ControlDetail = JsonSerializer.Serialize(definition);
        }
        else
        {
            foreach (var setting in catalog.ConfigPageColumns.Where(setting => setting.ConfigColumnId == foreign.Id &&
                         (binding == "overlay-all" || setting.ConfigPageId == "101")))
                setting.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"pageId":"202"}""");
        }
        if (binding == "overlay-all")
        {
            // 隐藏页也必须能够证明共享缓存范围，不能仅比较有控件的页面。
            catalog.ConfigPageColumns.Add(new() { Id = "10415", ConfigPageId = "104", ConfigColumnId = "15",
                YzProjectId = catalog.YzProjectId, IsShow = 0, IsEdit = 0,
                Extra = new() { ["controlDetail"] = JsonSerializer.SerializeToElement("""{"pageId":"202"}""") } });
        }
        var model = GenerationModel.Create(catalog, ns);
        var owner = model.Tables.Single(table => table.Source.Id == "1");
        var page = owner.Pages.Single(candidate => candidate.Source.Id == "101");
        var relation = Assert.Single(RelationCompilation.ForPage(model,page), item => item.SourceColumn.Source.Id == foreign.Id);
        Assert.Equal("202", relation.TargetPage.Source.Id);
        Assert.True(Assert.Single(relation.OptionsOrder!).Descending);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-list-published-page");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var text = await File.ReadAllTextAsync(Path.Combine(output, "Services", "DrNamesOrderService.g.cs"));
            Assert.Contains("""status=83""", text.Replace(" ", "", StringComparison.Ordinal));
            Assert.Contains("OptionsPageCondition101_15", text);
            Assert.Contains("DerivedOptionsScope101_15Async", text);
            Assert.Contains("ImportRelation101_15Async", text);
            Assert.Contains("DerivedOptionsRows101_15Async", text);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.DrNamesOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var directoryType = assembly.GetType(ns + ".Generated.GeneratedNonRedundantReadCatalog", true)!;
            var frozen = Assert.IsType<MetadataCatalog>(directoryType.GetField("Snapshot", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
            Assert.Contains("status=83", frozen.ConfigPages.Single(item => item.Id == "202").WhereSql);
            if (redundant)
            {
                var maintenanceType = assembly.GetType(ns + ".Generated.GeneratedRelationMaintenance", true)!;
                var maintenance = Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                    maintenanceType.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("customer_company_name", maintenance.MixedSourceColumns["dr_names_order"]);
                var bindings = RelationPropagationCompilation.DerivedBindings(model);
                Assert.Contains(bindings, item => item.Foreign.Source.Id == "15" && item.ScopedSinglePageId == "202");
            }
            else
            {
                var plan = Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan101", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
                var mysql = plan.GetType().GetProperty("MySql", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plan)!;
                var constants = Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(mysql.GetType()
                    .GetProperty("Constants", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mysql));
                Assert.Contains(constants.Values, value => value is int number && number == 83);
            }
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        var sourceField = catalog.ConfigPageColumns.Single(setting => setting.ConfigPageId == "101" && setting.ConfigColumnId == foreign.Id);
        if (redundant)
        {
            sourceField.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"pageId":"201"}""");
            var sourceTable = catalog.ConfigTables.Single(table => table.Id == "1");
            var targetTable = catalog.ConfigTables.Single(table => table.Id == "2");
            Assert.Throws<InvalidOperationException>(() =>
                ProjectScopedSingleRelationTarget.Resolve(catalog, sourceTable, foreign, targetTable));
        }
        sourceField.Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"pageId":"301"}""");
        model = GenerationModel.Create(catalog, ns);
        page = model.Tables.Single(table => table.Source.Id == "1").Pages.Single(candidate => candidate.Source.Id == "101");
        Assert.Throws<InvalidOperationException>(() => RelationCompilation.ForPage(model, page));
    }

    /// <summary>独立字典排序覆盖原数组、分页搜索与已选回读，并保持名称及 Excel 的原投影。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task List_candidates_use_independent_join_order(bool redundant, bool physicalName, bool inherit = false)
    {
        const string ns = "Example.IndependentListOrder";
        var catalog = Catalog("dr_names", false);
        foreach (var sourcePage in catalog.ConfigPages.Where(page => page.ConfigTableId == "1"))
            sourcePage.WhereSql = "yz_project_id=@current_yz_project_id";
        catalog.ConfigTables.Single(table => table.Id == "1").IsRongyu = redundant ? 1 : 0;
        var field = catalog.ConfigColumns.Single(column => column.Id == "15");
        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(field.ControlDetail)!;
        if (physicalName)
        {
            // 物理字段仍沿现有写入与 Excel，只有候选查询使用额外的联接排序。
            settings["nameField"] = "name";
            catalog.ConfigColumns.Single(column => column.Id == "113").ColumnName = "customer_name";
            catalog.ConfigColumns.Single(column => column.Id == "113").Extra["columnFullName"] =
                JsonSerializer.SerializeToElement("customer.name");
        }
        var orderBy = $"sort_alias desc, {settings["nameField"]} asc";
        if (inherit)
            catalog.ConfigPages.Single(page => page.Id == "201").OrderBy = orderBy;
        else
            settings["orderBy"] = orderBy;
        field.ControlDetail = JsonSerializer.Serialize(settings);
        var dictionary = catalog.ConfigPages.Single(page => page.Id == "401");
        dictionary.WhereSql += " AND priority>=10";
        catalog.ConfigColumns.Add(new() { Id = "4701", YzProjectId = catalog.YzProjectId, ConfigTableId = "4", ColumnName = "priority", LogicalType = "int32" });
        catalog.ConfigPageColumns.Add(new() { Id = "4014701", YzProjectId = catalog.YzProjectId, ConfigPageId = "401", ConfigColumnId = "4701", IsShow = 1 });
        var sort = new ColumnMetadata { Id = "2101", YzProjectId = catalog.YzProjectId, ConfigTableId = "2", ColumnName = "sort_alias", LogicalType = "int32" };
        sort.Extra["isJoinColumn"] = JsonSerializer.SerializeToElement(1);
        sort.Extra["columnFullName"] = JsonSerializer.SerializeToElement("company.priority");
        catalog.ConfigColumns.Add(sort);
        var sortField = new PageColumnMetadata { Id = "2012101", YzProjectId = catalog.YzProjectId, ConfigPageId = "201", ConfigColumnId = "2101", IsShow = 1 };
        catalog.ConfigPageColumns.Add(sortField);
        var model = GenerationModel.Create(catalog, ns);
        var page = model.Tables.Single(table => table.Source.Id == "1").Pages.Single(item => item.Source.Id == "101");
        var relation = Assert.Single(RelationCompilation.ForPage(model, page), item => item.SourceColumn.Source.Id == field.Id);
        Assert.Equal(new[] { ("sort_alias", true), (settings["nameField"], false), ("id", false) },
            relation.OptionsOrder!.Select(item => (item.Column.Source.ColumnName, item.Descending)));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-list-independent-order");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.DrNamesOrderService", true)!;
            var mapper = assembly.GetType(ns + ".Generated.Mappers.DrNamesOrderMapper", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
            var order = Assert.IsType<NonRedundantReadPlan>(service.GetField("OptionsSortPlan101_15", hidden)!.GetValue(null));
            var labels = Assert.IsType<NonRedundantReadPlan>(service.GetField("DerivedOptionsPlan101_15", hidden)!.GetValue(null));
            Assert.Equal("int32", order.OutputTypes["sort_alias"]);
            Assert.DoesNotContain("sort_alias", labels.OutputTypes.Keys);
            Assert.NotNull(service.GetMethod("Import101Async"));
            Assert.NotNull(service.GetMethod("Export101Async"));
            await AccountParentCandidateProbe.VerifyListOrderAsync(service, mapper, catalog.YzProjectId, settings["nameField"]);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }

        catalog.ConfigPageColumns.RemoveAll(column => column.Id == sortField.Id);
        model = GenerationModel.Create(catalog, ns);
        page = model.Tables.Single(table => table.Source.Id == "1").Pages.Single(item => item.Source.Id == "101");
        Assert.Throws<InvalidOperationException>(() => RelationCompilation.ForPage(model, page));
    }

    /// <summary>原下拉的联接文本与物理字段共用实际候选范围，并按显式排序和主键稳定分页。</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task List_with_joined_text_uses_its_configured_order(bool redundant, bool overrideOrder, bool contextual)
    {
        const string ns = "Example.JoinedTextListOrder";
        var catalog = Catalog("dr_names", contextual);
        var source = catalog.ConfigTables.Single(table => table.Id == "1");
        source.IsRongyu = redundant ? 1 : 0;

        var field = catalog.ConfigColumns.Single(column => column.Id == "15");
        var control = JsonSerializer.Deserialize<Dictionary<string, string>>(field.ControlDetail)!;
        control["orderBy"] = "status desc, company_name desc";
        field.ControlDetail = JsonSerializer.Serialize(control);
        if (overrideOrder)
            catalog.ConfigPageColumns.Single(column => column.ConfigPageId == "101" && column.ConfigColumnId == field.Id)
                .Extra["controlDetail"] = JsonSerializer.SerializeToElement("""{"orderBy":"company_name desc, id desc"}""");
        var model = GenerationModel.Create(catalog, ns);
        var owner = model.Tables.Single(table => table.Source.Id == "1");
        var page = owner.Pages.Single(item => item.Source.Id == "101");
        var relation = Assert.Single(RelationCompilation.ForPage(model, page), item => item.SourceColumn.Source.Id == field.Id);
        var configured = Assert.IsAssignableFrom<IReadOnlyList<(ColumnModel Column, bool Descending)>>(relation.OptionsOrder);
        Assert.Equal(overrideOrder ? new[] { ("company_name", true), ("id", true) } :
            new[] { ("status", true), ("company_name", true), ("id", false) },
            configured.Select(item => (item.Column.Source.ColumnName, item.Descending)));

        var boundary = Path.Combine(Path.GetTempPath(), "metarill-list-text-order");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            var output = Path.Combine(directory, "generated");
            new CSharpGenerator().Generate(catalog, output, ns);
            var code = await File.ReadAllTextAsync(Path.Combine(output, "Services", "DrNamesOrderService.g.cs"));
            var order = overrideOrder ? """[new("company_name", true), new("id", true)]""" :
                """[new("status", true), new("company_name", true), new("id", false)]""";
            Assert.Contains(order, code);
            Assert.Contains("FixedReadComparison.Contains,request.Keyword", code);
            Assert.Contains("request.PageNo,request.PageSize", code);
            Assert.Contains("OptionsSelected101_15Async", code);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service = assembly.GetType(ns + ".Generated.Services.DrNamesOrderService", true)!;
            RuntimeHelpers.RunClassConstructor(service.TypeHandle);
            var plan = Assert.IsType<NonRedundantReadPlan>(
                service.GetField("DerivedOptionsPlan101_15", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
            Assert.Equal("string", plan.OutputTypes["company_name"]);
            Assert.DoesNotContain("status", plan.OutputTypes.Keys);
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>业务编号与外键不同，保持联接整数投影、原候选文本及 Excel 精确匹配。</summary>
    [Theory]
    [InlineData("int32", "int", "-2000000001", false)]
    [InlineData("int64", "long", "9007199254740993", true)]
    [InlineData("bool", "bool", "true", false)]
    [InlineData("decimal", "decimal", "1234567890.123456", true)]
    [InlineData("double", "double", "0.125", false)]
    [InlineData("date", "DateTime", "2026-11-18", false)]
    [InlineData("datetime", "DateTime", "2026-11-18 13:14:15.123456", true)]
    [InlineData("time", "TimeSpan", "27:12:00.123456", true)]
    public async Task Derived_list_scalar_name_compiles_lookup_and_preserves_precision(string logicalType, string clrType, string expected, bool contextual)
    {
        var catalog = Catalog("dr_names", contextual);
        UseIntegerName(catalog, logicalType);
        var control = catalog.ConfigColumns.Single(column => column.Id == "15");
        var options = JsonSerializer.Deserialize<Dictionary<string, string>>(control.ControlDetail)!;
        options["orderBy"] = "status desc, company_name desc";
        control.ControlDetail = JsonSerializer.Serialize(options);
        var boundary = Path.Combine(Path.GetTempPath(), "metarill-derived-integer-list");
        var directory = Path.Combine(boundary, Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog, Path.Combine(directory, "generated"), "Example.IntegerList");
            var service = await File.ReadAllTextAsync(Path.Combine(directory, "generated/Services/DrNamesOrderService.g.cs"));
            var integer=logicalType is "int32" or "int64";
            Assert.Contains(integer?"FixedReadComparison.IntegerLabelContains":"FixedReadComparison.TypedLabelContains", service);
            Assert.Contains("FixedReadComparison.Equal, key", service);
            Assert.Contains(integer?"!" + clrType + ".TryParse(label":$"({clrType})MultiValueLabel.Parse(\"{logicalType}\"", service);
            Assert.Contains("""[new("status", true), new("company_name", true), new("id", false)]""", service);
            var exportCell = Assert.Single(service.Split('\n'), line => line.Contains("entity.CustomerId is null ? null : namesCustomerId.GetValueOrDefault(", StringComparison.Ordinal));
            Assert.DoesNotContain("??", exportCell);
            var assembly = await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var type = assembly.GetType("Example.IntegerList.Generated.Services.DrNamesOrderService")!;
            var readOption = type.GetMethod("ReadDerivedOption101_15", BindingFlags.Static | BindingFlags.NonPublic)!;
            object value = logicalType switch
            {
                "int32" => int.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                "int64" => long.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
                _ => Metarill.Core.Contracts.MultiValueLabel.Parse(logicalType,expected)
            };
            var row = new Dictionary<string, object?> { ["id"] = 9007199254740995L, ["company_name"] = value };
            var option = readOption.Invoke(null, [row])!;
            Assert.Equal(9007199254740995L, (long)option.GetType().GetProperty("Id")!.GetValue(option)!);
            Assert.Equal(expected, (string)option.GetType().GetProperty("Name")!.GetValue(option)!);
            Assert.Equal(expected, (string)option.GetType().GetProperty("Text")!.GetValue(option)!);
            row["company_name"] = null;
            Assert.Null(option.GetType().GetProperty("Text")!.GetValue(readOption.Invoke(null, [row])));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog, "Example.IntegerList")));
        }
        finally { await TestDirectoryCleanup.DeleteAsync(directory, boundary); }
    }

    /// <summary>List 与 SelectPage 共用同一派生业务编号模型，原公司名称仍是字符串。</summary>
    internal static void UseIntegerName(MetadataCatalog catalog, string logicalType)
    {
        // 客户选公司时仍使用原字符串名称，订单选客户时使用独立的公司编号联接列。
        catalog.ConfigColumns.Add(new()
        {
            Id = "45", ConfigTableId = "4", YzProjectId = catalog.YzProjectId, ColumnName = "code", LogicalType = logicalType
        });
        catalog.ConfigPageColumns.Add(new()
        {
            Id = "40145", ConfigPageId = "401", ConfigColumnId = "45", YzProjectId = catalog.YzProjectId
        });
        foreach (var column in catalog.ConfigColumns.Where(column => column.Id is "28" or "113"))
            column.LogicalType = logicalType;
        catalog.ConfigColumns.Single(column => column.Id == "28").Extra["columnFullName"] = JsonSerializer.SerializeToElement("company.code");
    }

    /// <summary>实际完整包包含原List组件和只读名称，重新更新保护手写partial。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_package_keeps_original_list_and_protects_handwritten_service(bool contextual)
    {
        var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var catalog=Catalog("dr_names",contextual);
        try
        {
            Assert.True(new ApplicationPackager().Create(catalog,output,"Example.DerivedNameApplication",root).FrontendIncluded);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var component in new[]{"common/yzp_runtime/remoteRelationOptions.js","components/yzp-relation-select/yzp-relation-select.vue"})
            {
                // 打包器的既有文本合同统一LF；只规范来源行尾，包内其余UTF8字节必须完全相同。
                var original=File.ReadAllText(Path.Combine(root,"frontend/metarill_uni_admin",component)).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
                Assert.Equal(Encoding.UTF8.GetBytes(original),File.ReadAllBytes(Path.Combine(output,"frontend",component)));
            }
            using var profile=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/generation-capabilities.json")));var subsets=profile.RootElement.GetProperty("supportedControlSubsets").EnumerateArray().Where(item=>item.GetProperty("controlKind").GetString()=="List").ToArray();Assert.Equal(2,subsets.Length);Assert.All(subsets,item=>Assert.True(item.GetProperty("derivedNameSupported").GetBoolean()));
            var hand=Path.Combine(output,"backend/generated/Services/DrNamesOrderService.cs");await File.AppendAllTextAsync(hand,"\n// 派生名称不覆盖手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.DerivedNameApplication",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>隐藏有效关系只生成私有写校验；目标条件不能因控件不可见而消失。</summary>
    [Fact]
    public async Task Hidden_derived_relation_keeps_private_validation_without_public_options()
    {
        var catalog=Catalog("dr_names",true);catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-hidden");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.HiddenDerivedName");var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));Assert.Contains("OptionsCondition101_15",service);var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            // 用真实编译类型验证可见性与返回值，不依赖实体名称是否写了完整命名空间。
            var type=assembly.GetType("Example.HiddenDerivedName.Generated.Services.DrNamesOrderService")!;
            var entity=assembly.GetType("Example.HiddenDerivedName.Generated.Entities.DrNamesParty")!;
            var method=type.GetMethod("OptionsScope101_15Async",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.NotNull(method);Assert.True(method.IsPrivate);Assert.Equal(typeof(Task<>).MakeGenericType(typeof(FreeSql.ISelect<>).MakeGenericType(entity)),method.ReturnType);
            Assert.Null(type.GetMethod("Options101_15Async",BindingFlags.Instance|BindingFlags.Public));}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未证明目标、派生范围及隐藏坏声明在写入stage之前拒绝，不能回退宽范围或IsIgnore SQL。</summary>
    [Theory][InlineData("global")][InlineData("derivedScope")][InlineData("virtual")][InlineData("brokenPath")][InlineData("privateName")][InlineData("unpublishedRedundantOwner")][InlineData("hiddenScope")]
    public void Invalid_derived_name_configuration_is_rejected_before_staging(string kind)
    {
        var c=Catalog("dr_names",true);
        if(kind=="global")c.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind is "derivedScope" or "hiddenScope")c.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND company_name='X'";
        if(kind=="hiddenScope")c.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="15");
        if(kind=="virtual")c.ConfigColumns.Single(column=>column.Id=="28").IsVirtualColumn=1;
        if(kind=="brokenPath")c.ConfigColumns.Single(column=>column.Id=="28").Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");
        if(kind=="privateName")c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="201"&&field.ConfigColumnId=="28");
        if(kind=="unpublishedRedundantOwner"){var owner=c.ConfigTables.Single(table=>table.Id=="1");owner.IsRongyu=1;owner.YzProjectId="";}
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-derived-name-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.RejectedDerivedName",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }

    /// <summary>名称副本只按实际记录和控件条件重算，发布后的维护目录保留反查字段。</summary>
    [Theory][InlineData("constant")][InlineData("form")][InlineData("account")][InlineData("parent")][InlineData("nested")]
    public async Task Control_only_scope_is_compiled_in_the_published_name_cache(string mode)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,mode=="parent");
        var source=catalog.ConfigTables.Single(table=>table.Id=="1");
        source.IsRongyu=1;
        var party=catalog.ConfigPages.Single(page=>page.Id=="201");
        party.WhereSql="yz_project_id=@current_yz_project_id";
        var relation=catalog.ConfigColumns.Single(column=>column.Id=="15");
        relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",nameField="company_name",whereSql=mode switch
        {
            "constant"=>"status=3",
            "form"=>"status IN (@formParameters.drNamesOrder.cacheSegment,1)",
            "account"=>"store_id=@current_store_id",
            "parent"=>"group_id=@parent_id AND status=3",
            _=>""
        }});
        if(mode=="account")
        {
            catalog.ConfigColumns.AddRange([
                new(){Id="119",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"},
                new(){Id="229",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"}
            ]);
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id))
                page.WhereSql+=" AND store_id=@current_store_id";
        }
        if(mode=="form")AddForm("119",source.Id);
        if(mode=="nested")
        {
            AddForm("229","2");
            catalog.ConfigTables.Single(table=>table.Id=="4").IsRongyu=0;
            catalog.ConfigColumns.Add(new(){Id="49",ConfigTableId="4",ColumnName="status",LogicalType="int32"});
            var company=catalog.ConfigColumns.Single(column=>column.Id=="27");
            company.ControlDetail=JsonSerializer.Serialize(new{tableId="4",nameField="name"});
            var field=catalog.ConfigPageColumns.Single(column=>column.Id=="20127");
            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(
                new{whereSql="status IN (@formParameters.drNamesParty.cacheSegment,3)"}));
        }
        RedundantMultiNameCacheTests.Published(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-control-cache");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ControlCache");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                assembly.GetType("Example.ControlCache.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null));
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
            var plan=plans[source.Id]!;
            var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
            var sql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["customer_company_name"]));
            var values=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                read.GetType().GetProperty("Constants",hidden)!.GetValue(read)).Values;
            if(mode is "form" or "nested")
            {
                Assert.Contains(".`cache_segment`",sql);
                Assert.Contains("BETWEEN -2147483648 AND 2147483647",sql);
                Assert.Contains(mode=="form"?1:3,values);
                Assert.Equal(mode=="form",sql.Contains("b.`cache_segment`",StringComparison.Ordinal));
            }
            if(mode=="account")Assert.Contains("b.`store_id`",sql);
            if(mode=="parent")Assert.Contains("b.`group_id`",sql);
            if(mode is "constant" or "parent")Assert.Contains(3,values);

            var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",hidden)!.GetValue(read));
            var changedField=mode=="account"?"store_id":"status";
            Assert.Contains(reverse.Cast<object>(),item=>
                Equals(mode=="nested"?"dr_names_company":"dr_names_party",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                    .TryGetValue("customer_company_name",out var fields)&&fields.Contains(changedField));
            if(mode=="nested")
                Assert.Contains(reverse.Cast<object>(),item=>Equals("dr_names_party",item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                    Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                        .TryGetValue("customer_company_name",out var fields)&&fields.Contains("cache_segment"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        void AddForm(string id,string table)
        {
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName="cache_segment",LogicalType="int64"});
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==table))
                catalog.ConfigPageColumns.Add(new(){Id=page.Id+"_"+id,ConfigPageId=page.Id,ConfigColumnId=id,IsShow=1,IsEdit=1});
        }
    }

    /// <summary>中间页的同父来自已选级联，另一个页面可独立使用物理父键。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    [InlineData(false,true,true)][InlineData(true,true,true)]
    public async Task Nested_project_cache_keeps_the_actual_middle_parent(bool logicalTarget,bool alternatePage,bool sharedSource=false)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,true);
        var source=catalog.ConfigTables.Single(table=>table.Id=="1");
        source.IsRongyu=1;
        catalog.ConfigTables.Single(table=>table.Id=="4").IsRongyu=0;
        catalog.ConfigTables.Add(new(){Id="91",TableName=prefix+"_area",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="911",ConfigTableId="91",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="912",ConfigTableId="91",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="913",ConfigTableId="91",ColumnName="name",LogicalType="string"},
            new(){Id="914",ConfigTableId="91",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"},
            new(){Id="115",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"},
            new(){Id="45",ConfigTableId="4",ColumnName="store_id",LogicalType="int64"},
            ScopePath("218","2","origin")
        ]);
        catalog.ConfigPages.Add(new(){Id="9101",ConfigTableId="91",YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigPageColumns.AddRange([
            new(){Id="9101913",ConfigPageId="9101",ConfigColumnId="913",IsShow=1},
            new(){Id="9101914",ConfigPageId="9101",ConfigColumnId="914",IsShow=1}
        ]);
        catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id AND name <> '停用国家'";
        var middle=catalog.ConfigPages.Single(page=>page.Id=="201");
        middle.WhereSql=middle.WhereSql.Replace("group_id=@parent_id","origin_country_id=@parent_id",StringComparison.Ordinal);
        catalog.ConfigPageColumns.Add(new(){Id="201218",ConfigPageId=middle.Id,ConfigColumnId="218",IsShow=1,IsEdit=1});
        if(sharedSource)
        {
            var other=ScopePath("219","2","origin_alt");
            other.ControlDetail=other.ControlDetail.Replace("origin_alt_country","origin_country",StringComparison.Ordinal);
            catalog.ConfigColumns.Add(other);
            catalog.ConfigPageColumns.Add(new(){Id="201219",ConfigPageId=middle.Id,ConfigColumnId=other.Id,IsShow=1,IsEdit=1});
        }
        var company=catalog.ConfigPages.Single(page=>page.Id=="401");
        company.WhereSql+=" AND store_id=@current_store_id AND "+(logicalTarget?"territory_country_id":"group_id")+"=@parent_id";
        if(logicalTarget)
        {
            catalog.ConfigColumns.Add(ScopePath("46","4","territory"));
            catalog.ConfigPageColumns.Add(new(){Id="40146",ConfigPageId=company.Id,ConfigColumnId="46",IsShow=1,IsEdit=1});
        }
        else
            catalog.ConfigColumns.Add(new(){Id="46",ConfigTableId="4",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"});
        catalog.ConfigColumns.Single(column=>column.Id=="27").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(company.Id);
        // 外层目标不再使用原夹具的表单附加条件；两个候选都只由各自实际同父范围决定。
        catalog.ConfigColumns.Single(column=>column.Id=="15").ControlDetail=
            JsonSerializer.Serialize(new{tableId="2",nameField="company_name"});
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id))
            page.WhereSql+=" AND store_id=@current_store_id";
        if(alternatePage)
        {
            catalog.ConfigPages.Add(new(){Id="202",ConfigTableId="2",YzAdminTypeId="5",
                WhereSql="yz_project_id=@current_yz_project_id AND group_id=@parent_id"});
            foreach(var column in catalog.ConfigPageColumns.Where(column=>column.ConfigPageId==middle.Id).ToArray())
            {
                var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(column))!;
                copy.Id="202_"+copy.ConfigColumnId;copy.ConfigPageId="202";catalog.ConfigPageColumns.Add(copy);
            }
        }
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nested-logical-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NestedLogicalCache");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                assembly.GetType("Example.NestedLogicalCache.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null));
            Assert.Contains("dr_names_area",maintenance.MixedTargets);
            Assert.Contains("dr_names_group",maintenance.MixedTargets);
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
            var plan=plans[source.Id]!;
            var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
            var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["customer_company_name"]));
            var middleAlias=System.Text.RegularExpressions.Regex.Match(expression,@"^CASE WHEN (r\d+)\.").Groups[1].Value;
            Assert.NotEmpty(middleAlias);
            Assert.Contains(middleAlias+".`origin_area_id`",expression);
            if(sharedSource)Assert.Contains(middleAlias+".`origin_alt_area_id`",expression);
            Assert.Contains("b.`store_id`",expression);
            Assert.DoesNotContain(middleAlias+".`group_id`",expression);
            var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                read.GetType().GetProperty("Reverse",hidden)!.GetValue(read));
            var areaDependencies=reverse.Cast<object>()
                .Where(item=>Equals("dr_names_area",item.GetType().GetProperty("TargetTable")!.GetValue(item)))
                .Select(item=>Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(
                    item.GetType().GetProperty("Outputs")!.GetValue(item)))
                .Where(outputs=>outputs.ContainsKey("customer_company_name"));
            Assert.Contains(areaDependencies,outputs=>outputs["customer_company_name"].Contains("group_id"));
            var party=assembly.GetType("Example.NestedLogicalCache.Generated.Services.DrNamesPartyService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(party.TypeHandle);
            var partyService=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesPartyService.g.cs"));
            Assert.Contains("ReadPlan201.BindAccount(actor)",partyService);
            Assert.Contains("SourcePageId=\"201\"",partyService);
            if(alternatePage&&logicalTarget)Assert.Contains("SourcePageId=\"202\"",partyService);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        ColumnMetadata ScopePath(string id,string table,string stem)=>new()
        {
            Id=id,ConfigTableId=table,ColumnName=stem+"_area_id",LogicalType="int64",ControlKind="CascadeTree",
            LinkConfigTableId="91",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=new object[]{
                new{table=prefix+"_group",columnPrefix=stem+"_country"},
                new{table=prefix+"_area",columnPrefix=stem+"_area",parentField="group_id"}}})
        };
    }

    /// <summary>生成维护目录重新装载后仍保留纯项目中间页和末端范围，保存与回填共用传播计划。</summary>
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)]
    public async Task Nested_project_cache_publishes_terminal_filters(bool fixedCompany,bool selection)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,false);
        AddNestedProjectCandidateScope(catalog,prefix);
        var source=catalog.ConfigTables.Single(table=>table.Id=="1");
        source.IsRongyu=1;
        catalog.ConfigColumns.Add(new(){Id="1910",ConfigTableId=source.Id,ColumnName="store_id",LogicalType="int64"});
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id))
            page.WhereSql+=" AND store_id=@current_store_id";
        catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="yz_project_id=@current_yz_project_id";
        if(!fixedCompany)
            catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="yz_project_id=@current_yz_project_id";
        if(selection)
        {
            var defaultPage=catalog.ConfigPages.Single(page=>page.Id=="19001");
            defaultPage.Extra["type"]=JsonSerializer.SerializeToElement(1);
            var actualPage=new PageMetadata{Id="19002",ConfigTableId="190",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
                WhereSql=defaultPage.WhereSql.Replace("区域筛选","区域覆盖",StringComparison.Ordinal),
                Extra=new(){["type"]=JsonSerializer.SerializeToElement(1)}};
            catalog.ConfigPages.Add(actualPage);
            catalog.ConfigPageColumns.Add(new(){Id="190021903",ConfigPageId=actualPage.Id,ConfigColumnId="1903",IsShow=1,IsEdit=0});
            var foreign=catalog.ConfigColumns.Single(column=>column.Id=="47");
            foreign.ControlKind="SelectPage";
            foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="190",pageId=defaultPage.Id,nameField="name"});
            foreign.Extra.Remove("linkConfigPageId");
            catalog.ConfigPageColumns.Add(new(){Id="40147",ConfigPageId="401",ConfigColumnId=foreign.Id,IsShow=1,IsEdit=1,
                Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{pageId=actualPage.Id}))}});
        }
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-nested-project-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.NestedProjectCache");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                assembly.GetType("Example.NestedProjectCache.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null));
            Assert.Contains("dr_names_region",maintenance.MixedTargets);
            Assert.Contains("dr_names_company",maintenance.MixedTargets);
            Assert.Contains("customer_company_name",maintenance.MixedSourceColumns["dr_names_order"]);
            const BindingFlags scope=BindingFlags.NonPublic|BindingFlags.Instance;
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                maintenance.GetType().GetProperty("Plans",scope)!.GetValue(maintenance));
            var plan=plans[source.Id]!;
            var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
            var constants=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                read.GetType().GetProperty("Constants",scope)!.GetValue(read));
            Assert.Contains(selection?"区域覆盖":"区域筛选",constants.Values);
            if(selection)Assert.DoesNotContain("区域筛选",constants.Values);
            var backend=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/DrNamesRegionService.g.cs"));
            Assert.Contains("ProjectRelationMutationPropagator(session).AfterMutationAsync",backend);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>纯项目中间页向显式全局字典联接时，副本沿实际页继承全局范围。</summary>
    [Theory][InlineData("constant")][InlineData("form")][InlineData("account")]
    [InlineData("pageAccount")][InlineData("selection")][InlineData("deep")][InlineData("project")]
    public async Task Project_cache_inherits_the_actual_global_terminal_scope(string mode)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,false);
        var source=catalog.ConfigTables.Single(table=>table.Id=="1");
        source.IsRongyu=1;
        catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql="yz_project_id=@current_yz_project_id";
        var deep=mode=="deep";
        if(deep)
        {
            AddNestedProjectCandidateScope(catalog,prefix);
            catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="yz_project_id=@current_yz_project_id";
        }
        var target=catalog.ConfigTables.Single(table=>table.Id==(deep?"190":"4"));
        target.IsRongyu=0;
        target.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        var targetPage=catalog.ConfigPages.Single(page=>page.Id==(deep?"19001":"401"));
        targetPage.WhereSql=mode=="pageAccount"?"store_id=@current_store_id AND status=3":"status>=1";
        Add("99",target.Id,"status","int32");
        var foreign=catalog.ConfigColumns.Single(column=>column.Id==(deep?"47":"27"));
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId=target.Id,pageId=targetPage.Id,nameField="name",whereSql="status=17"});
        if(mode=="form")
        {
            Add("229","2","cache_segment","int64");
            catalog.ConfigPageColumns.Add(new(){Id="201229",ConfigPageId="201",ConfigColumnId="229",IsShow=1,IsEdit=1});
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="42");
        }
        if(mode is "account" or "pageAccount")
        {
            Add("119",source.Id,"store_id","int64");
            Add("98",target.Id,"store_id","int64");
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id))
                page.WhereSql+=" AND store_id=@current_store_id";
        }
        if(mode=="selection")
        {
            var outer=catalog.ConfigColumns.Single(column=>column.Id=="15");
            outer.ControlKind="SelectPage";
            outer.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField="company_name"});
            catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(1);
        }

        var middlePageId=deep?"401":"201";
        var column=catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==middlePageId&&field.ConfigColumnId==foreign.Id);
        if(column is null)
        {
            column=new(){Id=middlePageId+foreign.Id,ConfigPageId=middlePageId,ConfigColumnId=foreign.Id};
            catalog.ConfigPageColumns.Add(column);
        }
        if(mode=="pageAccount")
            foreign.ControlDetail=JsonSerializer.Serialize(new{tableId=target.Id,pageId=targetPage.Id,nameField="name"});
        else column.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql=mode switch
        {
            "form"=>"status IN (@formParameters.drNamesParty.cacheSegment,3)",
            "account"=>"store_id=@current_store_id AND status=3",
            "project"=>"yz_project_id=@current_yz_project_id AND status=3",
            _=>"status=3"
        }}));
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-project-to-global-cache");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ProjectToGlobalCache");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                assembly.GetType("Example.ProjectToGlobalCache.Generated.GeneratedRelationMaintenance",true)!
                    .GetProperty("Catalog")!.GetValue(null));
            Assert.Contains(target.TableName,maintenance.MixedTargets);
            Assert.Contains("dr_names_party",maintenance.MixedTargets);
            Assert.Contains("customer_company_name",maintenance.MixedSourceColumns["dr_names_order"]);
            const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
            var plan=plans[source.Id]!;
            var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
            var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["customer_company_name"]));
            var values=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                read.GetType().GetProperty("Constants",hidden)!.GetValue(read)).Values;
            Assert.Contains(3,values);
            Assert.DoesNotContain(17,values);
            if(mode=="form")
            {
                Assert.Contains(".`cache_segment`",expression);
                Assert.DoesNotContain("b.`cache_segment`",expression);
            }
            if(mode is "account" or "pageAccount")Assert.Contains("b.`store_id`",expression);
            if(mode=="project")Assert.Contains(".`yz_project_id`=@project_scope_number",expression);

            const string cache="customer_company_name";
            var reverse=Assert.IsAssignableFrom<System.Collections.IEnumerable>(read.GetType().GetProperty("Reverse",hidden)!.GetValue(read));
            Assert.Contains(reverse.Cast<object>(),item=>
                Equals(target.TableName,item.GetType().GetProperty("TargetTable")!.GetValue(item))&&
                Assert.IsAssignableFrom<IReadOnlyDictionary<string,IReadOnlySet<string>>>(item.GetType().GetProperty("Outputs")!.GetValue(item))
                    .TryGetValue(cache,out var dependencies)&&dependencies.Contains("status"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        void Add(string id,string table,string name,string type)=>catalog.ConfigColumns.Add(new()
            {Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,YzProjectId=catalog.YzProjectId});
    }

    /// <summary>逻辑来源的原候选必须保留公司、区域两层独立的固定范围。</summary>
    [Theory][InlineData(false,false,false,false)][InlineData(true,true,false,false)]
    [InlineData(false,true,true,false)][InlineData(true,true,false,true)][InlineData(false,true,true,true)]
    public Task Logical_project_candidate_keeps_terminal_ranges(bool selection,bool cross,bool listCondition,bool logicalParent)=>
        Single_relation_target_leaf_condition_reaches_original_services(selection,derived:true,parent:logicalParent,
            globalWithProject:null,redundant:false,cross:cross,listCondition:listCondition,fixedTargetLogic:listCondition&&logicalParent,
            logicalParent:logicalParent,terminalProjectScope:true);

    /// <summary>候选源使用逻辑条件，两段普通名称联接分别受固定页的门店与常量限制。</summary>
    private static void AddNestedProjectCandidateScope(MetadataCatalog catalog,string prefix)
    {
        catalog.ConfigTables.Single(table=>table.Id=="4").IsRongyu=0;
        catalog.ConfigTables.Add(new(){Id="190",TableName=prefix+"_region",YzProjectId=catalog.YzProjectId,IsRongyu=0});
        catalog.ConfigColumns.AddRange([
            new(){Id="1901",ConfigTableId="190",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="1902",ConfigTableId="190",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="1903",ConfigTableId="190",ColumnName="name",LogicalType="string"},
            new(){Id="1904",ConfigTableId="190",ColumnName="store_id",LogicalType="int64"},
            new(){Id="45",ConfigTableId="4",ColumnName="store_id",LogicalType="int64"},
            new(){Id="47",ConfigTableId="4",ColumnName="region_id",LogicalType="int64",LinkConfigTableId="190",ControlKind="List",
                Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("19001")}}
        ]);
        var company=catalog.ConfigPages.Single(page=>page.Id=="401");
        company.WhereSql+=" AND store_id=@current_store_id AND name <> '公司筛选'";
        catalog.ConfigColumns.Single(column=>column.Id=="27").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(company.Id);
        catalog.ConfigPages.Add(new(){Id="19001",ConfigTableId="190",YzAdminTypeId="5",YzProjectId=catalog.YzProjectId,
            WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id AND name <> '区域筛选'"});
        catalog.ConfigPageColumns.Add(new(){Id="190011904",ConfigPageId="19001",ConfigColumnId="1903",IsShow=1,IsEdit=0});
        catalog.ConfigColumns.Single(column=>column.Id=="28").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.region.name");
    }

    /// <summary>原下拉和单选在同一发布父级读取全部实际路径，并复用追加、覆盖和导出接口。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public Task Single_relation_preserves_compound_source_parent(bool selection,bool independent)=>
        VerifySingleRelationCompoundParents(selection,independent,null);

    /// <summary>原下拉及单选必须验证目标页所有同父字段，保留已发布的实际逻辑路径。</summary>
    [Theory]
    [InlineData(false,"physical",false,false)]
    [InlineData(false,"mixed",false,false)]
    [InlineData(true,"mixed",false,false)]
    [InlineData(true,"independent",false,false)]
    [InlineData(true,"independent",false,true)]
    [InlineData(false,"mixed",true,false)]
    [InlineData(false,"physical",true,false)]
    public Task Single_relation_preserves_compound_target_parent(bool selection,string targetScope,bool redundant,bool exact)=>
        VerifySingleRelationCompoundParents(selection,false,targetScope,redundant,exact);

    /// <summary>原下拉和单选对已发布全局目标执行全部同父条件，保留真实项目和账号范围。</summary>
    [Theory]
    [InlineData(false,"physical",false,false,false)]
    [InlineData(true,"physical",true,true,false)]
    [InlineData(false,"mixed",false,true,true)]
    [InlineData(true,"independent",false,true,false)]
    [InlineData(true,"independent",true,false,false)]
    public Task Global_single_relation_preserves_compound_target_parent(
        bool selection,string scope,bool rowProject,bool globalParent,bool redundant)=>
        VerifySingleRelationCompoundParents(selection,false,scope,redundant,
            globalTargetWithProject:rowProject,globalParent:globalParent);

    /// <summary>同一全局父记录须满足来源页的所有物理和逻辑父条件，名称联接也不能只检查其中一个。</summary>
    [Theory]
    [InlineData(false,false,"physical",false)]
    [InlineData(true,true,"independent",false)]
    [InlineData(false,true,"mixed",true)]
    [InlineData(true,false,"mixed",true)]
    [InlineData(false,false,"physical",true,true,false)]
    [InlineData(false,false,"mixed",false,true,true)]
    public Task Global_single_relation_preserves_compound_source_parent(
        bool selection,bool independent,string targetScope,bool rowProject,bool physicalSource=false,bool redundant=false)=>
        VerifySingleRelationCompoundParents(selection,independent,targetScope,redundant,
            globalTargetWithProject:rowProject,globalParent:true,compoundGlobalSource:true,physicalSource:physicalSource);

    /// <summary>项目内下拉和单选同时验证项目归属与真实全局父发布页。</summary>
    [Theory]
    [InlineData(false,false,"physical",false,false,true,false)]
    [InlineData(true,true,"independent",false,false,true,false)]
    [InlineData(false,false,"mixed",false,false,true,false)]
    [InlineData(true,false,"independent",false,true,false,false)]
    [InlineData(false,false,"physical",true,false,true,true)]
    [InlineData(false,false,"mixed",true,false,false,false)]
    public Task Project_single_relation_preserves_compound_global_parent(
        bool selection,bool independent,string targetScope,bool redundant,bool exact,bool compoundSource,bool physicalSource)=>
        VerifySingleRelationCompoundParents(selection,independent,targetScope,redundant,exact,globalParent:true,
            compoundGlobalSource:compoundSource,physicalSource:physicalSource,projectTargetGlobalParent:true);

    /// <summary>项目内原候选仅有一个物理父键时仍须执行实际全局父页范围。</summary>
    [Theory]
    [InlineData(false,false,false,false)]
    [InlineData(true,false,true,false)]
    [InlineData(true,true,false,true)]
    [InlineData(false,true,false,false)]
    public Task Project_single_relation_preserves_physical_global_parent(
        bool selection,bool redundant,bool independent,bool exact)=>
        VerifySingleRelationCompoundParents(selection,independent,"physicalSingle",redundant,exact,globalParent:true,
            compoundGlobalSource:independent,projectTargetGlobalParent:true);

    private static async Task VerifySingleRelationCompoundParents(bool selection,bool independent,string? targetScope,
        bool redundant=false,bool exact=false,bool? globalTargetWithProject=null,bool globalParent=false,bool compoundGlobalSource=false,
        bool physicalSource=false,bool projectTargetGlobalParent=false)
    {
        var hasGlobalParentFixture=globalTargetWithProject is not null||projectTargetGlobalParent;
        const string prefix="single_compound";
        const string ns="Example.CompoundSingle";
        var catalog=Catalog(prefix,true);
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
        foreign.ControlKind=selection?"SelectPage":"List";
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField="company_name"});
        if(selection)catalog.ConfigPages.Single(page=>page.Id=="201").Extra["type"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigTables.Add(new(){Id="6",TableName=prefix+"_zone",IsRongyu=1});
        catalog.ConfigColumns.AddRange([
            new(){Id="61",ConfigTableId="6",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="62",ConfigTableId="6",ColumnName="name",LogicalType="string"},
            new(){Id="63",ConfigTableId="6",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="64",ConfigTableId="6",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"}
        ]);
        catalog.ConfigPages.Add(new(){Id="601",ConfigTableId="6",WhereSql="yz_project_id=@current_yz_project_id AND name<>'停用'"});
        foreach(var field in new[]{"62","64"})
            catalog.ConfigPageColumns.Add(new(){Id="601"+field,ConfigPageId="601",ConfigColumnId=field,IsShow=1,IsEdit=1});
        var logicalSource=!redundant&&!physicalSource;
        if(redundant)catalog.ConfigTables.Single(table=>table.Id=="1").IsRongyu=1;
        if(logicalSource)AddPath("120","origin");
        if(independent)AddPath("121","alternate");
        if(physicalSource)
            catalog.ConfigColumns.Add(new(){Id="198",ConfigTableId="1",ColumnName="backup_group_id",
                LogicalType="string",ColumnLength="24",LinkConfigTableId="3"});
        foreach(var page in catalog.ConfigPages.Where(item=>item.ConfigTableId=="1"))
        {
            // 物理外键在前，仍必须用获授权的逻辑父参数进行写入，不能读取稀疏父槽。
            if(!logicalSource)
            {
                page.WhereSql="yz_project_id=@current_yz_project_id AND group_id=@parent_id";
                if(physicalSource)page.WhereSql+=" AND backup_group_id=@parentId";
                continue;
            }
            var scope=hasGlobalParentFixture&&!compoundGlobalSource?"origin_group_id=@parent_id":
                independent?"origin_group_id=@parent_id AND alternate_group_id=@parent_id":
                    "group_id=@parent_id AND origin_group_id=@parent_id";
            page.WhereSql=projectTargetGlobalParent?"yz_project_id=@current_yz_project_id AND "+scope:scope;
            var id=page.Id;
            catalog.ConfigPageColumns.Add(new(){Id=id+"120",ConfigPageId=id,ConfigColumnId="120",IsShow=1,IsEdit=1});
            if(independent)
                catalog.ConfigPageColumns.Add(new(){Id=id+"121",ConfigPageId=id,ConfigColumnId="121",IsShow=1,IsEdit=1});
        }
        if(targetScope is not null)
        {
            var target=catalog.ConfigPages.Single(item=>item.Id=="201");
            if(targetScope=="physical")
            {
                catalog.ConfigColumns.Add(new(){Id="220",ConfigTableId="2",ColumnName="depot_group_id",LogicalType="int64",LinkConfigTableId="3"});
            }
            else if(targetScope!="physicalSingle")
            {
                AddPath("220","depot","2");
                catalog.ConfigPageColumns.Add(new(){Id="201220",ConfigPageId="201",ConfigColumnId="220",IsShow=1,IsEdit=1});
            }
            if(targetScope=="independent")
            {
                AddPath("221","delivery","2");
                target.WhereSql=target.WhereSql.Replace("group_id=@parent_id","delivery_group_id=@parent_id",
                    StringComparison.Ordinal);
                catalog.ConfigPageColumns.Add(new(){Id="201221",ConfigPageId="201",ConfigColumnId="221",IsShow=1,IsEdit=1});
            }
            if(targetScope!="physicalSingle")target.WhereSql+=" AND depot_group_id=@parentId";
            if(exact)target.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");
            if(selection&&targetScope=="independent"&&!exact)
                foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField="company_name",
                    whereSql="depot_group_name <> '排除仓组'"});
        }
        if(hasGlobalParentFixture)
        {
            var target=catalog.ConfigPages.Single(page=>page.Id=="201");
            if(globalTargetWithProject is not null)
            {
                foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "2" or "4"))
                    table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                target.WhereSql=target.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
                catalog.ConfigPages.Single(page=>page.Id=="401").WhereSql="name <> '停用公司'";
            }
            // 下拉样本只在全局父页放账号条件，核对父级自身的范围也会传入候选。
            if(targetScope!="physicalSingle"||selection)target.WhereSql+=" AND store_id=@current_store_id";
            catalog.ConfigColumns.Add(new(){Id="218",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigColumns.Add(new(){Id="118",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
            if(redundant)
                foreach(var sourcePage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="1"))
                    sourcePage.WhereSql+=" AND store_id=@current_store_id";
            else if(!compoundGlobalSource||independent)
            {
                // 只有物理父条件参与当前页面时才保留该字段。
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="19");
                catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="19");
            }
            if(targetScope=="independent")
            {
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="29");
                catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="29");
            }
            if(globalTargetWithProject==false)
            {
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="22");
                catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="22");
            }
            if(globalParent)
            {
                catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                catalog.ConfigColumns.Add(new(){Id="38",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"});
                catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="name <> '停用父记录' AND store_id=@current_store_id";
                catalog.ConfigColumns.RemoveAll(column=>column.Id=="32");
                catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="32");
            }
        }
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);
        if(exact)
            foreach(var admin in catalog.YzAdminTypes)
                admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        var model=GenerationModel.Create(catalog,ns);
        var owner=model.Tables.Single(table=>table.Source.Id=="1");
        var source=owner.Pages.Single(page=>page.Source.Id=="101");
        var relation=RelationCompilation.ForPage(model,source,true).Single(option=>option.SourceColumn.Source.Id=="15");
        Assert.Equal(logicalSource,relation.LogicalSourceParent);
        if(targetScope is not null)
        {
            Assert.NotNull(relation.FixedParent);
            if(targetScope=="physicalSingle")
            {
                Assert.Empty(relation.FixedParent!.AdditionalTargets);
                Assert.True(relation.FixedParent.Global);
                Assert.False(relation.FixedParent.Logical);
            }
            else Assert.Single(relation.FixedParent!.AdditionalTargets);
        }
        Assert.True(ControlRenderer.RelationContext(source,relation,false).SameParent);
        if(compoundGlobalSource)
        {
            var parent=relation.FixedParent!.SingleSource(catalog,owner.Source,relation.SourceColumn.Source);
            Assert.Equal(parent.Column.Id,relation.FixedSourceParent!.Source.Id);
            var fields=new[]{parent.Column.ColumnName,Assert.Single(parent.AdditionalColumns).ColumnName};
            var expected=physicalSource?new[]{"backup_group_id","group_id"}:
                independent?new[]{"alternate_group_id","origin_group_id"}:new[]{"group_id","origin_group_id"};
            Assert.Equal(expected,fields.Order(StringComparer.Ordinal).ToArray());
        }
        else Assert.Equal(logicalSource?"origin_group_id":"group_id",relation.FixedSourceParent!.Source.ColumnName);
        new FrontendPackager().Validate(catalog);

        var scopedTargetField=targetScope switch
        {
            "physicalSingle"=>"group_id",
            "physical"=>"depot_group_id",
            _=>"depot_zone_id"
        };
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-compound-single-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var text=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/SingleCompoundOrderService.g.cs"));
            if(logicalSource)
            {
                Assert.Contains("CrossReader101_120(actor,false,false).PathAsync(0,LogicalParentId101(parentId)",text);
                Assert.Contains("CrossReader101_120(actor,locking,false).PathAsync(0,LogicalParentId101(parentId)",text);
            }
            if(independent)
            {
                Assert.Contains("CrossReader101_121(actor,false,false).PathAsync(0,LogicalParentId101(parentId)",text);
                Assert.Contains("CrossReader101_121(actor,locking,false).PathAsync(0,LogicalParentId101(parentId)",text);
            }
            if(logicalSource)Assert.Contains("OptionsScope101_15Async(logicalRelationParentId, null",text);
            if(hasGlobalParentFixture)Assert.Contains(".BindAccount(actor)",text);
            if(compoundGlobalSource&&!independent)
                Assert.Contains("PageMultiGlobalParent101.BindAccount(actor)",text);
            if(physicalSource)
                Assert.Contains("if(entity.BackupGroupId!=long.Parse(parentId!, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))throw",text);
            Assert.Contains("targetParentId!=parentId",text);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var service=assembly.GetType(ns+".Generated.Services.SingleCompoundOrderService",true)!;
            Assert.NotNull(service.GetMethod("Import101Async"));
            Assert.NotNull(service.GetMethod("Import103Async"));
            Assert.NotNull(service.GetMethod("Export101Async"));

            if(targetScope is not null)
            {
                RuntimeHelpers.RunClassConstructor(service.TypeHandle);
                const BindingFlags hidden=BindingFlags.NonPublic|BindingFlags.Instance;
                var plan=Assert.IsType<NonRedundantReadPlan>(service.GetField("DerivedOptionsPlan101_15",
                    BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
                var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                var condition=Assert.IsType<string>(sql.GetType().GetProperty("Scope",hidden)!.GetValue(sql));
                if(targetScope!="physicalSingle")Assert.Contains("BINARY CAST(",condition);
                Assert.Contains("`"+scopedTargetField+"`",condition);
                if(targetScope=="physicalSingle")
                {
                    Assert.Contains("`single_compound_group`",condition);
                    Assert.Contains("`store_id`",condition);
                }
                if(hasGlobalParentFixture)
                {
                    Assert.True(plan.RequiresAccount);
                    if(projectTargetGlobalParent)Assert.Contains("`yz_project_id`",condition);
                    var target=assembly.GetType(ns+".Generated.Services.SingleCompoundPartyService",true)!;
                    RuntimeHelpers.RunClassConstructor(target.TypeHandle);
                    if(!redundant)
                    {
                        var read=Assert.IsType<NonRedundantReadPlan>(service.GetField("ReadPlan101",
                            BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null));
                        var readSql=read.GetType().GetProperty("MySql",hidden)!.GetValue(read)!;
                        var joins=Assert.IsType<string>(readSql.GetType().GetProperty("From",hidden)!.GetValue(readSql));
                        Assert.Contains("BINARY CAST(",joins);
                        Assert.Contains("`"+scopedTargetField+"`",joins);
                        if(compoundGlobalSource&&physicalSource)
                            Assert.Contains("CASE WHEN BINARY CAST(b.`backup_group_id` AS CHAR)=BINARY CAST(b.`group_id` AS CHAR)",joins);
                        else if(compoundGlobalSource&&independent)
                        {
                            Assert.Contains("`alternate_zone_id`",joins);
                            Assert.Contains("`origin_zone_id`",joins);
                        }
                        else if(compoundGlobalSource)
                            Assert.Contains("CASE WHEN BINARY CAST(b.`group_id` AS CHAR)=",joins);
                    }
                }
                if(exact)Assert.Contains("""ResolveExactDataManage("201", "list", "real_name")""",text);
                if(selection&&targetScope=="independent"&&!exact)Assert.Contains("CompileSingleImportLabel",text);
                if(redundant)
                {
                    var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                        assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                    var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
                    var stored=plans["1"]!;
                    var read=stored.GetType().GetProperty("Read")!.GetValue(stored)!;
                    var nameSql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["customer_company_name"]));
                    Assert.Contains("BINARY CAST(",nameSql);
                    Assert.Contains("`"+scopedTargetField+"`",nameSql);
                    if(physicalSource)Assert.Contains("`backup_group_id`",nameSql);
                }
            }

            if(selection&&(independent||targetScope=="independent"&&!exact||targetScope=="physicalSingle"&&exact))
            {
                foreach(var publishedPage in catalog.ConfigPages)
                    catalog.ConfigMenus.Add(new(){Id="700"+publishedPage.Id,YzProjectId=catalog.YzProjectId,
                        YzAdminTypeId=publishedPage.YzAdminTypeId,Name="页面"+publishedPage.Id,
                        Kind="GENERATED_PAGE",ConfigPageId=publishedPage.Id});
                var output=Path.Combine(directory,"full");
                new ApplicationPackager().Create(catalog,output,ns,NonRedundantApplicationFixture.Root());
                foreach(var operation in new[]{"restore","build"})
                    await NonRedundantApplicationFixture.BuildAsync(output,operation);
                var detail=await File.ReadAllTextAsync(Path.Combine(output,"frontend/pages/yzp/single_compound_order-detail-101.vue"));
                Assert.Contains("selectPageDialog15",detail);
                Assert.Contains(":parent_id=\"parent_id\"",detail);
            }

            if(compoundGlobalSource&&!independent&&targetScope=="physical")
            {
                // 目标和链路已明确落在旧父页，来源外键指向新父页时必须拒绝。
                var parent=catalog.ConfigPages.Single(page=>page.Id=="301");
                var other=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(parent))!;
                other.Id="303";
                other.WhereSql+=" AND name <> '另一来源父页'";
                var first=catalog.ConfigColumns.Single(column=>column.Id=="29");
                var second=catalog.ConfigColumns.Single(column=>column.Id=="220");
                var sourceKey=catalog.ConfigColumns.Single(column=>column.Id==(physicalSource?"198":"19"));
                var otherSource=physicalSource?catalog.ConfigColumns.Single(column=>column.Id=="19"):null;
                var fixedKeys=new[]{first,second}.Concat(otherSource is null?[]:[otherSource]).ToArray();
                foreach(var key in fixedKeys)key.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(parent.Id);
                sourceKey.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(other.Id);
                catalog.ConfigPages.Add(other);
                try
                {
                    var invalid=Assert.ThrowsAny<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
                    Assert.Contains("来源字段与目标字段必须绑定同一个实际发布父页",invalid.Message);
                }
                finally
                {
                    foreach(var key in fixedKeys)key.Extra.Remove("linkConfigPageId");
                    sourceKey.Extra.Remove("linkConfigPageId");
                    catalog.ConfigPages.Remove(other);
                }
            }
            if(hasGlobalParentFixture&&globalParent&&targetScope=="physical")
            {
                // 两个外键虽指向同一张表，绑定不同的固定发布页仍不能生成。
                var parent=catalog.ConfigPages.Single(page=>page.Id=="301");
                var other=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(parent))!;
                other.Id="302";
                other.WhereSql+=" AND name <> '另一父页'";
                var first=catalog.ConfigColumns.Single(column=>column.Id=="29");
                var second=catalog.ConfigColumns.Single(column=>column.Id=="220");
                first.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(parent.Id);
                second.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(other.Id);
                catalog.ConfigPages.Add(other);
                try
                {
                    var invalid=Assert.ThrowsAny<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
                    Assert.Contains("同一个实际发布父页",invalid.Message);
                }
                finally
                {
                    first.Extra.Remove("linkConfigPageId");
                    second.Extra.Remove("linkConfigPageId");
                    catalog.ConfigPages.Remove(other);
                }
            }
            if(targetScope=="physicalSingle")
            {
                // 外键仍指向全局父表，只把父发布页错绑到另一张表，不能回退到任意页面。
                catalog.ConfigColumns.Single(column=>column.Id=="29").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("601");
                var invalid=Assert.ThrowsAny<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
                Assert.Contains("全局物理父键必须绑定唯一发布页",invalid.Message);
                return;
            }
            if(targetScope is not null)
            {
                var target=catalog.ConfigPages.Single(item=>item.Id=="201");
                if(targetScope=="physical")catalog.ConfigColumns.Single(column=>column.Id=="220").LinkConfigTableId="6";
                else target.WhereSql=target.WhereSql.Replace("depot_group_id=@parentId","depot_zone_id=@parentId",StringComparison.Ordinal);
            }
            else catalog.ConfigPages.Single(page=>page.Id=="101").WhereSql+=" AND origin_zone_id=@parent_id";
            Assert.ThrowsAny<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}

        void AddPath(string id,string role,string tableId="1")
        {
            catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId=tableId,ColumnName=role+"_zone_id",LogicalType="int64",
                ControlKind="CascadeTree",LinkConfigTableId="6",ControlDetail=JsonSerializer.Serialize(new
                {
                    sourceMode="crossTable",levels=new object[]{new{table=prefix+"_group",columnPrefix=role+"_group"},
                        new{table=prefix+"_zone",columnPrefix=role+"_zone",parentField="group_id"}}
                })});
        }
    }

    /// <summary>声明来源订单、非冗余客户、父分组及公司名称的固定元数据。</summary>
    internal static MetadataCatalog Catalog(string prefix,bool parentAndForm)
    {
        var c=NonRedundantGeneratedFixture.Catalog(prefix);c.ConfigTables.Single(table=>table.Id=="2").IsRongyu=0;
        c.ConfigTables.Add(new(){Id="4",TableName=prefix+"_company",IsRongyu=1});
        c.ConfigColumns.AddRange([new(){Id="41",ConfigTableId="4",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="42",ConfigTableId="4",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="43",ConfigTableId="4",ColumnName="delete_no",LogicalType="datetime"},new(){Id="44",ConfigTableId="4",ColumnName="name",LogicalType="string"}]);
        c.ConfigPages.Add(new(){Id="401",ConfigTableId="4",YzAdminTypeId="5",WhereSql="yz_project_id=@current_yz_project_id"});
        c.ConfigPageColumns.Add(new(){Id="40144",ConfigPageId="401",ConfigColumnId="44"});
        c.ConfigColumns.Add(new(){Id="27",ConfigTableId="2",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="4",ControlKind="List",ControlDetail=JsonSerializer.Serialize(new{tableId="4",nameField="name"})});
        c.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="company_name",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("company.name")}});
        c.ConfigColumns.Add(new(){Id="210",ConfigTableId="2",ColumnName="status",LogicalType="int32"});
        foreach(var id in new[]{"27","28","210"})c.ConfigPageColumns.Add(new(){Id="201"+id,ConfigPageId="201",ConfigColumnId=id});
        var relation=c.ConfigColumns.Single(column=>column.Id=="15");relation.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");relation.ControlDetail=JsonSerializer.Serialize(new{tableId="2",table=prefix+"_party",nameField="company_name",whereSql=parentAndForm?"segment=@formParameters.segment":""});
        var target=c.ConfigPages.Single(page=>page.Id=="201");target.WhereSql="yz_project_id=@current_yz_project_id AND status=1";
        if(parentAndForm)
        {
            c.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName="group_id",LogicalType="int64",LinkConfigTableId="3"});c.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29"});target.WhereSql+=" AND group_id=@parent_id";
            foreach(var (id,table) in new[]{("112","1"),("211","2")}){c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,ColumnName="segment",Name="分组条件",LogicalType="int32",DefaultValue="1"});c.ConfigPageColumns.Add(new(){Id=table+id,ConfigPageId=table=="1"?"101":"201",ConfigColumnId=id});}
        }
        relation.Name="客户";
        c.ConfigColumns.Add(new(){Id="113",ConfigTableId="1",ColumnName="customer_company_name",Name="客户公司",LogicalType="string",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("customer.company_name")}});
        c.ConfigPageColumns.Add(new(){Id="101113",ConfigPageId="101",ConfigColumnId="113",IsEdit=0,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1),["isSort"]=JsonSerializer.SerializeToElement(1)}});
        NonRedundantGeneratedFixture.EnableImports(c);c.ConfigPages.Single(page=>page.Id=="101").IsExport=1;
        // 隐藏页故意继承一个不可选默认FK，验证最终服务不能因控件不可见而略过候选条件。
        relation.DefaultValue="9007199254748001";
        var hidden=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(page=>page.Id=="101")))!;hidden.Id="104";hidden.IsImport=0;hidden.IsExport=0;hidden.IsCopy=0;hidden.Extra.Remove("importMethod");c.ConfigPages.Add(hidden);
        foreach(var field in c.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId!="15").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="104"+field.Id;copy.ConfigPageId="104";c.ConfigPageColumns.Add(copy);}
        RedundantMultiNameCacheTests.Published(c);return c;
    }
}
