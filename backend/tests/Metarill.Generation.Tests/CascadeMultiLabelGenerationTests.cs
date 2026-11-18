using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CascadeMultiLabelGenerationTests
{
    /// <summary>保留原多选和复选入口，发布目录可独立编译候选、导入、导出与维护计划。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiSelectPage",1,false)]
    [InlineData("MultiCheckBoxDatabase",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,false)]
    [InlineData("MultiSelectPage",0,true)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",0,true)]
    [InlineData("MultiCheckBoxDatabase",1,true)]
    public Task Cascade_names_compile_in_original_endpoints(string kind,int mode,bool selfTree)=>
        CompileCascadeLabel(kind,mode,selfTree,indirect:false);

    /// <summary>全局目标只有物理账号条件时，原多值入口仍共享发布范围及名称副本。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,false)]
    [InlineData("MultiSelectPage",1,true,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,false)]
    [InlineData("MultiCheckBoxDatabase",1,false,false)]
    [InlineData("MultiSelectPage",0,true,true)]
    [InlineData("MultiSelectPage",1,false,true)]
    [InlineData("MultiCheckBoxDatabase",0,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true)]
    public async Task Global_account_range_compiles_without_cascades(string kind,int mode,bool rowProject,bool derived)
    {
        const string ns="Example.GlobalAccountMulti";
        var catalog=DerivedRelationNamesTests.Catalog("dr_names",false);
        var target=catalog.ConfigPages.Single(page=>page.Id=="201");
        target.Extra["type"]=JsonSerializer.SerializeToElement(2);
        target.WhereSql="status=1 AND store_id=@current_store_id";
        var companyPage=catalog.ConfigPages.Single(page=>page.Id=="401");
        companyPage.WhereSql="";
        foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "2" or "4"))
            table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(!rowProject)
        {
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="22");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="22");
        }
        catalog.ConfigColumns.Add(new(){Id="217",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
        var foreign=catalog.ConfigColumns.Single(column=>column.Id=="15");
        var label=derived?"company_name":"name";
        foreign.ColumnName="customer_ids";
        foreign.ControlKind=kind;
        foreign.LogicalType="string";
        foreign.ColumnLength="max";
        foreign.DefaultValue="";
        foreign.ControlDetail=JsonSerializer.Serialize(new{tableId="2",pageId="201",nameField=label});
        var display=catalog.ConfigColumns.Single(column=>column.Id=="113");
        display.ColumnName="customer_"+label+"s";
        display.ColumnLength="max";
        display.Extra["columnFullName"]=JsonSerializer.SerializeToElement("customer."+label);
        catalog.ConfigColumns.RemoveAll(column=>column.Id is "16" or "17");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId is "16" or "17");
        var source=catalog.ConfigTables.Single(table=>table.Id=="1");
        source.IsRongyu=mode;
        catalog.ConfigColumns.Add(new(){Id="118",ConfigTableId="1",ColumnName="store_id",LogicalType="int64"});
        foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id))
        {
            page.OrderBy="id desc";
            page.WhereSql+=" AND store_id=@current_store_id";
        }
        catalog.YzAdminTypes.Clear();
        RedundantMultiNameCacheTests.Published(catalog);

        var published=GenerationMetadataPublisher.ForRelations(catalog);
        Assert.Contains("store_id=@current_store_id",published.ConfigPages.Single(page=>page.Id=="201").WhereSql);
        if(mode==1)Assert.Contains("store_id=@current_store_id",published.ConfigPages.Single(page=>page.Id=="101").WhereSql);

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-account-multi");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var code=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesOrderService.g.cs"));
            Assert.Contains("CompileScopedMultiLabel(\"2\",\"201\",\""+label+"\"",code);
            Assert.Contains(".BindAccount(actor)",code);
            Assert.Contains("Import101Async",code);
            Assert.Contains("Export101Async",code);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var service in new[]{"DrNamesOrderService","DrNamesPartyService"})
                RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services."+service,true)!.TypeHandle);
            if(mode==1)
            {
                var runtime=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(runtime.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("dr_names_party",maintenance.MixedTargets);
                if(derived)Assert.Contains("dr_names_company",maintenance.MixedTargets);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>通过普通关联取跨表祖先或自树叶名，发布与维护必须包含中间记录的依赖。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,false)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",0,true)]
    public Task Joined_cascade_names_compile_in_original_endpoints(string kind,int mode,bool selfTree)=>
        CompileCascadeLabel(kind,mode,selfTree,indirect:true);

    /// <summary>原请求绑定当前账号，冗余来源独立初始化传播和维护，覆盖两种控件和树形。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,true)]
    [InlineData("MultiCheckBoxDatabase",0,true,false)]
    [InlineData("MultiSelectPage",1,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,false,false)]
    public Task Account_cascade_names_compile_in_original_endpoints(string kind,int mode,bool selfTree,bool indirect)=>
        CompileCascadeLabel(kind,mode,selfTree,indirect,account:true);

    /// <summary>递归候选接入两种原控件，项目内与全局内层共用发布、回显和维护。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiCheckBoxDatabase",0,true)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",1,false)]
    public Task Recursive_cascade_names_compile_in_original_endpoints(string kind,int mode,bool indirect)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect,account:true,recursive:true);

    /// <summary>多选和复选直接引用的目标页按自身逻辑范围生成原候选和写入。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiSelectPage",1,false)]
    [InlineData("MultiCheckBoxDatabase",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,false)]
    [InlineData("MultiSelectPage",0,true)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",0,true)]
    [InlineData("MultiCheckBoxDatabase",1,true)]
    public Task Selected_page_conditions_compile_in_original_endpoints(string kind,int mode,bool physical)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:physical);

    /// <summary>全局根有无项目列，仍保留内层普通项目、账号条件和原多值生成入口。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,true,true)]
    [InlineData("MultiSelectPage",1,false,false)]
    [InlineData("MultiSelectPage",1,true,true)]
    [InlineData("MultiCheckBoxDatabase",0,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,false)]
    [InlineData("MultiCheckBoxDatabase",1,false,true)]
    public Task Global_selected_page_conditions_compile_in_original_endpoints(string kind,int mode,bool physical,bool rowProject)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:physical,globalRowProject:rowProject);

    /// <summary>真实生成包复用原候选父参数，写入、Excel、回显及维护共用同一来源字段。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,null,false)]
    [InlineData("MultiSelectPage",1,true,null,true)]
    [InlineData("MultiSelectPage",0,true,true,true)]
    [InlineData("MultiSelectPage",1,false,false,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,null,false)]
    [InlineData("MultiCheckBoxDatabase",1,false,null,false)]
    [InlineData("MultiCheckBoxDatabase",0,false,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,false)]
    public Task Selected_page_parent_conditions_compile_in_original_endpoints(string kind,int mode,bool physical,bool? rowProject,bool formParent)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:physical,globalRowProject:rowProject,
            parent:true,formParent:formParent);

    /// <summary>已选、当前写及 Excel 通过实际级联还原目标父记录，物理祖先列为空也能匹配。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,null,false,false)]
    [InlineData("MultiSelectPage",1,true,true,true,false)]
    [InlineData("MultiSelectPage",1,true,null,false,true)]
    [InlineData("MultiCheckBoxDatabase",0,false,false,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,false,false,true)]
    public Task Selected_page_logical_parent_conditions_compile_in_original_endpoints(string kind,int mode,bool physical,bool? rowProject,bool formParent,bool onlyParent)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:physical,globalRowProject:rowProject,
            parent:true,formParent:formParent,logicalParent:true,onlyParent:onlyParent);

    /// <summary>来源只存末级时，原弹窗、写入和 Excel 稳定绑定同一恢复出的父键。</summary>
    [Theory]
    [InlineData("MultiSelectPage",false,null,false)]
    [InlineData("MultiSelectPage",true,true,false)]
    [InlineData("MultiCheckBoxDatabase",false,false,false)]
    [InlineData("MultiCheckBoxDatabase",true,null,true)]
    public Task Selected_page_source_logical_parent_conditions_compile_in_original_endpoints(string kind,bool logicalTarget,bool? rowProject,bool onlyParent)=>
        CompileCascadeLabel(kind,0,selfTree:false,indirect:false,selectedPage:true,physical:onlyParent,globalRowProject:rowProject,
            parent:true,logicalParent:logicalTarget,onlyParent:onlyParent,sourceLogicalParent:true);

    /// <summary>显式全局父沿实际级联层页校验，覆盖普通／全局目标及普通／逻辑来源父。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,null,true,false)]
    [InlineData("MultiSelectPage",1,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",0,false,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,null,true,false)]
    [InlineData("MultiSelectPage",0,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",0,false,false,true,true)]
    public Task Selected_page_global_parent_conditions_compile_in_original_endpoints(string kind,int mode,bool? rowProject,bool noParentProject,bool sourceLogical,bool parentAccount=false)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:mode==1,globalRowProject:rowProject,
            parent:true,logicalParent:true,sourceLogicalParent:sourceLogical,globalParent:true,parentWithoutProject:noParentProject,parentAccount:parentAccount);

    [Theory]
    [InlineData("MultiSelectPage",0,null,true,false)]
    [InlineData("MultiCheckBoxDatabase",0,false,true,true,true)]
    [InlineData("MultiSelectPage",1,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,null,true,false)]
    public Task Selected_page_global_physical_parent_conditions_compile_in_original_endpoints(
        string kind,int mode,bool? rowProject,bool noParentProject,bool sourceLogical,bool parentAccount=false)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:mode==1,globalRowProject:rowProject,
            parent:true,sourceLogicalParent:sourceLogical,globalParent:true,parentWithoutProject:noParentProject,parentAccount:parentAccount);

    /// <summary>固定自树叶名约束原多选和复选目标页，并随名称副本及父参数一起生成。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,true,null,false)]
    [InlineData("MultiSelectPage",1,false,null,true)]
    [InlineData("MultiCheckBoxDatabase",0,false,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,false,false)]
    public Task Selected_self_tree_page_conditions_compile_in_original_endpoints(string kind,int mode,bool physical,bool? rowProject,bool parent)=>
        CompileCascadeLabel(kind,mode,selfTree:true,indirect:false,account:rowProject is null,selectedPage:true,physical:physical,
            globalRowProject:rowProject,parent:parent);

    private static async Task CompileCascadeLabel(string kind,int mode,bool selfTree,bool indirect,bool account=false,
        bool recursive=false,bool selectedPage=false,bool physical=false,bool? globalRowProject=null,bool parent=false,bool formParent=false,bool logicalParent=false,bool onlyParent=false,bool sourceLogicalParent=false,
        bool globalParent=false,bool parentWithoutProject=false,bool parentAccount=false)
    {
        const string ns="Example.CascadeLabels";
        var catalog=selfTree?NonRedundantSelfTreeTests.Catalog("label_cross"):
            NonRedundantCrossTableGenerationTests.Catalog("label_cross","string","int64");
        var display=selfTree?"tree_name":"country_name";
        if(selfTree)
        {
            var target=catalog.ConfigPages.Single(page=>page.Id=="201");
            target.Extra["type"]=JsonSerializer.SerializeToElement(2);
        }
        else
        {
            catalog.ConfigColumns.Add(new(){Id="29",ConfigTableId="2",ColumnName=display,LogicalType="string"});
            catalog.ConfigPageColumns.Add(new(){Id="20129",ConfigPageId="201",ConfigColumnId="29",IsEdit=0});
        }
        if(physical)display="name";
        var targetTable="2";
        var targetPage="201";
        if(indirect)
        {
            targetTable="8";
            targetPage="801";
            catalog.ConfigTables.Add(new(){Id=targetTable,TableName="label_join",IsRongyu=0});
            catalog.ConfigColumns.AddRange([
                new(){Id="81",ConfigTableId=targetTable,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="82",ConfigTableId=targetTable,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="83",ConfigTableId=targetTable,ColumnName="carrier_id",LogicalType="string",ControlKind="List",LinkConfigTableId="2",
                    ControlDetail="{\"tableId\":\"2\",\"nameField\":\"id\"}"},
                new(){Id="84",ConfigTableId=targetTable,ColumnName="name",LogicalType="string",Extra=new()
                    {["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("carrier."+display)}}
            ]);
            catalog.ConfigPages.Add(new(){Id=targetPage,ConfigTableId=targetTable,WhereSql="yz_project_id=@current_yz_project_id",
                Extra=new(){["type"]=JsonSerializer.SerializeToElement(2)}});
            catalog.ConfigPageColumns.Add(new(){Id="80184",ConfigPageId=targetPage,ConfigColumnId="84",IsEdit=0});
            display="name";
        }
        catalog.ConfigTables.Add(new(){Id="9",TableName="label_consumer",IsRongyu=mode});
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",IsImport=1,IsExport=1,WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigColumns.AddRange([
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="92",ConfigTableId="9",ColumnName="name",Name="记录",LogicalType="string"},
            new(){Id="93",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="94",ConfigTableId="9",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="95",ConfigTableId="9",ColumnName="selected_ids",Name="关联",LogicalType="string",ControlKind=kind,
                LinkConfigTableId=targetTable,ControlDetail=JsonSerializer.Serialize(new{tableId=targetTable,pageId=physical?(object)201L:targetPage,nameField=display})}
        ]);
        catalog.ConfigPageColumns.Add(new(){Id="90192",ConfigPageId="901",ConfigColumnId="92"});
        catalog.ConfigPageColumns.Add(new(){Id="90195",ConfigPageId="901",ConfigColumnId="95",Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        if(account)
        {
            var page=catalog.ConfigPages.Single(item=>item.Id=="301");
            catalog.ConfigColumns.Add(new(){Id="scope_node_owner",ConfigTableId=page.ConfigTableId,ColumnName="node_owner_id",LogicalType="int64"});
            catalog.ConfigColumns.Add(new(){Id="scope_record_owner",ConfigTableId="9",ColumnName="record_owner_id",LogicalType="string",ColumnLength="32"});
            page.WhereSql+=" AND node_owner_id=@current_yz_manager_id";
            catalog.ConfigPages.Single(item=>item.Id=="901").WhereSql+=" AND record_owner_id=@current_yz_manager_id";
        }
        if(recursive)AddRecursiveCountry(catalog,global:kind=="MultiCheckBoxDatabase");
        if(selectedPage)
        {
            catalog.ConfigColumns.Add(new(){Id="219",ConfigTableId="2",ColumnName="store_id",LogicalType="int64"});
            catalog.ConfigColumns.Add(new(){Id="919",ConfigTableId="9",ColumnName="store_id",LogicalType="int64"});
            var scopePage=catalog.ConfigPages.Single(page=>page.Id=="201");
            if(!onlyParent)scopePage.WhereSql+=$" AND {(selfTree?"tree_name":"country_name")} <> '排除目标页名称' AND store_id=@current_store_id";
            if(globalRowProject is { } rowProject)
            {
                catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                scopePage.WhereSql=scopePage.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
                if(onlyParent)scopePage.WhereSql="";
                if(selfTree)
                {
                    catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                    var treePage=catalog.ConfigPages.Single(page=>page.Id=="301");
                    treePage.WhereSql=treePage.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
                }
                if(!rowProject)
                {
                    var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");
                    catalog.ConfigColumns.Remove(project);
                    catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==project.Id);
                }
            }
            if(!onlyParent)catalog.ConfigPages.Single(page=>page.Id=="901").WhereSql+=" AND store_id=@current_store_id";
        }
        var parentField=logicalParent?"country_id":"target_scope_id";
        if(parent)
        {
            var parentTable=logicalParent||sourceLogicalParent||globalParent?"3":"71";
            if(!logicalParent&&!sourceLogicalParent&&!globalParent)
            {
                catalog.ConfigTables.Add(new(){Id="71",TableName="label_scope_parent",IsRongyu=1});
                catalog.ConfigColumns.AddRange([
                new(){Id="7101",ConfigTableId="71",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id="7102",ConfigTableId="71",ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id="7103",ConfigTableId="71",ColumnName="delete_no",LogicalType="datetime"},
                ]);
            }
            if(!logicalParent)catalog.ConfigColumns.Add(new(){Id="214",ConfigTableId="2",ColumnName="target_scope_id",LogicalType="string",ColumnLength="24",LinkConfigTableId=parentTable});
            catalog.ConfigColumns.Add(new(){Id="914",ConfigTableId="9",ColumnName="source_scope_id",Name="所属记录",LogicalType="int64",LinkConfigTableId=parentTable});
            var page=catalog.ConfigPages.Single(page=>page.Id=="201");
            page.WhereSql+=(page.WhereSql.Length==0?"":" AND ")+parentField+"=@parent_id";
            if(globalParent)
            {
                var globalDefinition=catalog.ConfigTables.Single(item=>item.Id==parentTable);
                globalDefinition.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                var globalPage=catalog.ConfigPages.Single(item=>item.ConfigTableId==parentTable);
                globalPage.WhereSql=globalPage.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
                if(parentWithoutProject)catalog.ConfigColumns.RemoveAll(column=>column.ConfigTableId==parentTable&&column.ColumnName=="yz_project_id");
                if(parentAccount)
                {
                    catalog.ConfigColumns.Add(new(){Id="3099",ConfigTableId=parentTable,ColumnName="store_id",LogicalType="int64"});
                    globalPage.WhereSql+=" AND store_id=@current_store_id";
                }
                if(!sourceLogicalParent)
                {
                    var foreign=catalog.ConfigColumns.Single(column=>column.Id=="914");
                    foreign.ControlKind="List";
                    foreign.ControlDetail=JsonSerializer.Serialize(new{tableId=parentTable,pageId=globalPage.Id,nameField="name"});
                }
            }
            if(sourceLogicalParent)
            {
                catalog.ConfigColumns.Add(new(){Id="915",ConfigTableId="9",ColumnName="source_area_id",LogicalType="int64",ControlKind="CascadeTree",LinkConfigTableId="4",ControlDetail=JsonSerializer.Serialize(new
                {
                    sourceMode="crossTable",levels=new object[]{new{table="label_cross_country",columnPrefix="source_scope"},
                        new{table="label_cross_area",columnPrefix="source_area",parentField="label_cross_country_id"}}
                })});
                catalog.ConfigPageColumns.Add(new(){Id="901915",ConfigPageId="901",ConfigColumnId="915",IsEdit=1,IsShow=1});
            }
            if(formParent)catalog.ConfigPageColumns.Add(new(){Id="901914",ConfigColumnId="914",ConfigPageId="901",IsEdit=1,IsShow=1});
            else catalog.ConfigPages.Single(page=>page.Id=="901").WhereSql+=" AND source_scope_id=@parentId";
        }
        RedundantMultiNameCacheTests.Published(catalog);

        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        if(recursive)
        {
            var metadata=GenerationMetadataPublisher.ForRelations(catalog);
            Assert.Contains("scope_zone_name",metadata.ConfigPages.Single(page=>page.Id=="301").WhereSql);
            Assert.Contains("有效区域",metadata.ConfigPages.Single(page=>page.Id=="601").WhereSql);
            Assert.Contains(metadata.ConfigPageColumns,field=>field.Id=="30139"&&field.Extra.ContainsKey("controlDetail"));
        }
        if(selectedPage)
        {
            var metadata=GenerationMetadataPublisher.ForRelations(catalog);
            if(!onlyParent)Assert.Contains("排除目标页名称",metadata.ConfigPages.Single(page=>page.Id=="201").WhereSql);
            if(mode==1&&!onlyParent)Assert.Contains("store_id=@current_store_id",metadata.ConfigPages.Single(page=>page.Id=="901").WhereSql);
            if(parent)
            {
                Assert.Contains(parentField+"=@parent_id",metadata.ConfigPages.Single(page=>page.Id=="201").WhereSql);
                if(!formParent)Assert.Contains("source_scope_id=@parentId",metadata.ConfigPages.Single(page=>page.Id=="901").WhereSql);
            }
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-multi-labels");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            if(account)
            {
                var code=File.ReadAllText(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                Assert.Contains(".BindAccount(actor)",code);
                Assert.Contains("记录不属于当前账号的数据范围",code);
            }
            if(selectedPage)
            {
                var code=File.ReadAllText(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                Assert.Contains("CompileScopedMultiLabel(\"2\",\"201\",\""+display+"\"",code);
                Assert.Contains(".BindAccount(actor)",code);
                Assert.DoesNotContain("MultiPageCondition901_95.ApplyConstants",code);
                if(globalParent)
                {
                    Assert.Contains("CompileMultiGlobalParent(\"2\",\"201\")",code);
                    Assert.Contains(".KeyRowsAsync(",code);
                    Assert.DoesNotContain("session.Select<LabelCrossCountry>().Where(row => row.Id == targetParentKey",code);
                    if(!logicalParent)
                    {
                        var targetCode=File.ReadAllText(Path.Combine(directory,"generated/Services/LabelCrossSourceService.g.cs"));
                        Assert.Contains("CompileMultiGlobalParent(\"2\",\"201\")",targetCode);
                        Assert.Contains("ValidateParents201Async(parentId,actor,cancellationToken,locking:false)",targetCode);
                        Assert.Contains("entity.TargetScopeId,actor,cancellationToken",targetCode);
                        Assert.Contains("Task<long> Count201Async",targetCode);
                    }
                }
                if(parent)
                {
                    Assert.Contains("new(\""+parentField+"\",FixedReadComparison.Equal,",code);
                    if(sourceLogicalParent)
                    {
                        Assert.Contains("logicalRelationParentId: parentId",code);
                        Assert.Contains("VerifyLogicalParent901Async",code);
                        Assert.Contains("LogicalParentScope901",code);
                        if(kind=="MultiCheckBoxDatabase")Assert.Contains("entity.SourceScopeId",code); // 导出实体从逻辑投影取值。
                    }
                    else Assert.Contains("entity.SourceScopeId",code);
                }
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var consumer=assembly.GetType(ns+".Generated.Services.LabelConsumerService",true)!;
            RuntimeHelpers.RunClassConstructor(consumer.TypeHandle);
            RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.LabelCrossSourceService",true)!.TypeHandle);
            if(indirect)RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.LabelJoinService",true)!.TypeHandle);
            Assert.NotNull(consumer.GetMethod("Import901Async"));
            Assert.NotNull(consumer.GetMethod("Export901Async"));
            if(mode==1)
            {
                var targets=MixedRelationPropagationCompilation.ForModel(model).MixedTargets;
                Assert.Contains(selfTree?"label_cross_tree":"label_cross_country",targets);
                if(indirect)Assert.Contains("label_cross_source",targets);
                if(parent)Assert.Contains(logicalParent||globalParent?"label_cross_country":"label_scope_parent",targets);
                if(recursive||selectedPage)
                {
                    var target=recursive?"label_scope_zone":selfTree?"label_cross_tree":"label_cross_country";
                    Assert.Contains(target,targets);
                    var runtime=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                    var published=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(runtime.GetProperty("Catalog")!.GetValue(null));
                    Assert.Contains(target,published.MixedTargets);
                }
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>外层国家的候选条件取自内层区域名称，内层仍按当前项目及原固定名称筛选。</summary>
    private static void AddRecursiveCountry(MetadataCatalog catalog,bool global)
    {
        var country=catalog.ConfigTables.Single(table=>table.Id=="3");
        country.IsRongyu=0;
        var candidate=catalog.ConfigPages.Single(page=>page.Id=="301");
        if(global)
        {
            country.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            candidate.WhereSql=candidate.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
            var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId==country.Id&&column.ColumnName=="yz_project_id");
            catalog.ConfigColumns.Remove(project);
            catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==project.Id);
        }
        candidate.WhereSql+=" AND scope_zone_name <> '排除区域'";
        foreach(var (id,role) in new[]{("6","zone"),("7","site")})
        {
            catalog.ConfigTables.Add(new(){Id=id,TableName="label_scope_"+role,IsRongyu=1});
            catalog.ConfigColumns.AddRange([
                new(){Id=id+"1",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},
                new(){Id=id+"2",ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},
                new(){Id=id+"3",ConfigTableId=id,ColumnName="name",LogicalType="string"}
            ]);
            catalog.ConfigPages.Add(new(){Id=id+"01",ConfigTableId=id,
                WhereSql="yz_project_id=@current_yz_project_id"+(role=="zone"?" AND name <> '有效区域'":"")});
            catalog.ConfigPageColumns.Add(new(){Id=id+"013",ConfigPageId=id+"01",ConfigColumnId=id+"3"});
        }
        catalog.ConfigColumns.Add(new(){Id="74",ConfigTableId="7",ColumnName="zone_id",LogicalType="int64",LinkConfigTableId="6"});
        catalog.ConfigPageColumns.Add(new(){Id="70174",ConfigPageId="701",ConfigColumnId="74"});
        var definition=JsonSerializer.Serialize(new
        {
            sourceMode="crossTable",
            levels=new object[]{
                new{table="label_scope_zone",columnPrefix="scope_zone"},
                new{table="label_scope_site",columnPrefix="scope_site",parentField="zone_id"}}
        });
        catalog.ConfigColumns.Add(new(){Id="39",ConfigTableId="3",ColumnName="scope_site_id",LogicalType="int64",
            ControlKind="CascadeTree",LinkConfigTableId="7",ControlDetail=definition});
        catalog.ConfigPageColumns.Add(new(){Id="30139",ConfigPageId="301",ConfigColumnId="39",
            Extra=new(){["controlDetail"]=JsonSerializer.SerializeToElement(definition)}});
    }
}
