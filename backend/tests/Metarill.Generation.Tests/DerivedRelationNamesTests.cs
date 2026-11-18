using System.Reflection;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>普通R0及已发布R1来源的List使用R0目标的联接名称，候选、原导入导出和写回显必须共用同一计划。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class DerivedRelationNamesTests
{
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
            Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,"Example.DerivedNames")));
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
    public async Task Single_relation_target_leaf_condition_reaches_original_services(bool selection,bool derived,bool parent,
        bool? globalWithProject,bool redundant,bool cross=false,bool listCondition=false,bool fixedTargetLogic=false,bool globalParent=false,bool logicalParent=false,bool logicalSource=false)
    {
        const string prefix="dr_names";
        var catalog=Catalog(prefix,parent);
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
        var parentOnly=logicalParent&&selection&&globalParent;
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
            catalog.ConfigColumns.Single(column=>column.Id=="19").LinkConfigTableId="6";
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="29");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="29");
            target.WhereSql=target.WhereSql.Contains("group_id=@parent_id",StringComparison.Ordinal)
                ?target.WhereSql.Replace("group_id=@parent_id","country_id=@parent_id",StringComparison.Ordinal)
                :target.WhereSql+" AND country_id=@parent_id";
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
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-single-relation-page-scope");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.ScopedSingleRelation");
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            var scope=derived?"DerivedOptionsPlan101_15":"OptionsMembership101_15";
            if(globalWithProject is not null)
            {
                if(listCondition)
                {
                    Assert.Contains("CompileGlobalListCandidate(\"2\",\"201\"",service);
                    Assert.Contains($"ApplyCondition(query,session,{scope}.BindAccount(actor),OptionsCondition101_15.BindVariables",service);
                    Assert.DoesNotContain($"ApplyCandidate(query,session,{scope}.BindAccount(actor)",service);
                    Assert.Contains("AccountConditionVariables.Require(actor",service);
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
                Assert.DoesNotContain("query = query.Where(entity => entity.YzProjectId",service[start..end]);
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
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var name in new[]{"DrNamesOrderService","DrNamesPartyService"})
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    assembly.GetType("Example.ScopedSingleRelation.Generated.Services."+name,true)!.TypeHandle);

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
    public async Task Single_account_page_uses_published_scope_without_cascade(
        bool selection,bool derived,bool rowProject,bool cached,bool global=true,bool parent=false,
        bool globalParent=false,bool independentParent=false)
    {
        var catalog=Catalog("dr_names",parent);
        var target=catalog.ConfigPages.Single(page=>page.Id=="201");
        target.WhereSql=(global?"":"yz_project_id=@current_yz_project_id AND ")+
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
            whereSql=selection?"store_id=@current_store_id":""});
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
                page.WhereSql+=" AND store_id=@current_store_id";
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
            Assert.Contains("OptionsPageCondition101_15.BindAccount(actor).ApplyConstants",service);
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
                Assert.Contains("DerivedOptionsPlan101_15.BindAccount(actor)",service);
                if(!global)Assert.Contains("OptionsPageCondition101_15.BindAccount(actor)",service);
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
                if(globalParent)Assert.Contains("dr_names_group",cache.MixedTargets);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
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

    /// <summary>声明同项目四表：来源订单→非冗余客户→公司名称；原字段和菜单方式均由元数据生成。</summary>
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
