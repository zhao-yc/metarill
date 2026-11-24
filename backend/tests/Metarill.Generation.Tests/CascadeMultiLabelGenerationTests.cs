using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
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

    /// <summary>目标页仅用物理账号与可选父参数时，原多值入口共享实际发布范围及名称副本。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,false)]
    [InlineData("MultiSelectPage",1,true,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,false)]
    [InlineData("MultiCheckBoxDatabase",1,false,false)]
    [InlineData("MultiSelectPage",0,true,true)]
    [InlineData("MultiSelectPage",1,false,true)]
    [InlineData("MultiCheckBoxDatabase",0,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true)]
    [InlineData("MultiSelectPage",0,true,false,false,false)]
    [InlineData("MultiSelectPage",1,true,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,false,false,false)]
    [InlineData("MultiSelectPage",0,true,true,false,true)]
    [InlineData("MultiSelectPage",1,true,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",0,true,false,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,false,true)]
    [InlineData("MultiSelectPage",0,false,false,true,true)]
    [InlineData("MultiSelectPage",1,true,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",0,true,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,false,false,true,true)]
    [InlineData("MultiSelectPage",1,false,false,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,true,true,true)]
    public async Task Multi_account_range_compiles_without_cascades(string kind,int mode,bool rowProject,bool derived,
        bool global=true,bool parent=false,bool globalParent=false)
    {
        const string ns="Example.AccountMulti";
        var catalog=DerivedRelationNamesTests.Catalog("dr_names",parent);
        var target=catalog.ConfigPages.Single(page=>page.Id=="201");
        target.Extra["type"]=JsonSerializer.SerializeToElement(2);
        target.WhereSql=(global?"":"yz_project_id=@current_yz_project_id AND ")+
            "status=1 AND store_id=@current_store_id"+(parent?" AND group_id=@parent_id":"");
        if(global)
        {
            var companyPage=catalog.ConfigPages.Single(page=>page.Id=="401");
            companyPage.WhereSql="";
            foreach(var table in catalog.ConfigTables.Where(table=>table.Id is "2" or "4"))
                table.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        }
        if(!rowProject)
        {
            catalog.ConfigColumns.RemoveAll(column=>column.Id=="22");
            catalog.ConfigPageColumns.RemoveAll(column=>column.ConfigColumnId=="22");
        }
        if(globalParent)
        {
            var owner=catalog.ConfigTables.Single(table=>table.Id=="3");
            owner.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
            var ownerPage=catalog.ConfigPages.Single(page=>page.Id=="301");
            ownerPage.WhereSql="store_id=@current_store_id";
            catalog.ConfigColumns.Add(new(){Id="35",ConfigTableId="3",ColumnName="store_id",LogicalType="int64"});
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
            if(parent)Assert.Contains("new(\"group_id\",FixedReadComparison.Equal,",code.Replace(" ", "", StringComparison.Ordinal));
            if(global&&parent)
            {
                var targetCode=File.ReadAllText(Path.Combine(directory,"generated/Services/DrNamesPartyService.g.cs"));
                Assert.Contains("CompileGlobalAccountMultiPage(\"2\",\"201\"",targetCode);
                Assert.Contains("PageAsync(CandidateReadPlan201.BindAccount(actor)",targetCode);
                Assert.Contains("StatisticsAsync(CandidateReadPlan201.BindAccount(actor)",targetCode);
                Assert.Contains("KeyRowsAsync(CandidateReadPlan201.BindAccount(actor)",targetCode);
                if(globalParent)Assert.Contains("CompileMultiGlobalParent(\"2\",\"201\")",targetCode);
            }
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            foreach(var service in new[]{"DrNamesOrderService","DrNamesPartyService"})
                RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services."+service,true)!.TypeHandle);
            if(mode==1)
            {
                var runtime=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(runtime.GetProperty("Catalog")!.GetValue(null));
                Assert.Contains("dr_names_party",maintenance.MixedTargets);
                if(derived)Assert.Contains("dr_names_company",maintenance.MixedTargets);
                if(parent)Assert.Contains("dr_names_group",maintenance.MixedTargets);
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

    /// <summary>带项目归属的全局业务表使用同一发布的物理父级及账号范围。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,true)]
    [InlineData("MultiSelectPage",1,false)]
    [InlineData("MultiCheckBoxDatabase",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,true)]
    [InlineData("MultiSelectPage",0,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true)]
    public Task Global_business_multivalue_uses_published_physical_parent(string kind,int mode,bool rowProject,bool globalParent=false)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:rowProject,
            parent:true,globalParent:globalParent,parentWithoutProject:globalParent,parentAccount:globalParent&&mode==1,
            globalBusinessSource:true);

    /// <summary>全局业务多值字典必须匹配指向同一父表的全部物理父字段。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,true,false)]
    [InlineData("MultiSelectPage",1,false,false)]
    [InlineData("MultiCheckBoxDatabase",0,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,false)]
    [InlineData("MultiSelectPage",0,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true)]
    public Task Global_business_multivalue_uses_compound_physical_parents(string kind,int mode,bool rowProject,bool globalParent)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:rowProject,
            parent:true,globalParent:globalParent,parentWithoutProject:globalParent,parentAccount:globalParent&&mode==1,
            compoundTargetParent:"physical",globalBusinessSource:true);

    /// <summary>全局业务表与字典各有两个物理父字段，均匹配同一实际发布父页。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiCheckBoxDatabase",1,true)]
    public Task Global_business_multivalue_uses_compound_physical_source_and_target(string kind,int mode,bool rowProject)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:rowProject,
            parent:true,globalParent:true,parentWithoutProject:true,parentAccount:true,
            compoundSourceParent:"physicalOnly",compoundTargetParent:"physical",globalCompoundSource:true,globalBusinessSource:true);

    /// <summary>全局字典可只声明父键，也可叠加固定字面条件。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,true,false,false,false)]
    [InlineData("MultiSelectPage",1,false,true,true,false)]
    [InlineData("MultiCheckBoxDatabase",0,false,false,true,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,false,false)]
    [InlineData("MultiSelectPage",0,false,true,false,true)]
    [InlineData("MultiCheckBoxDatabase",0,true,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,true,true)]
    [InlineData("MultiSelectPage",1,true,false,true,false)]
    public Task Global_business_multivalue_uses_static_physical_parent(string kind,int mode,bool rowProject,bool compound,bool literal,bool globalParent)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:rowProject,
            parent:true,globalParent:globalParent,parentWithoutProject:globalParent,parentAccount:globalParent&&mode==1,
            compoundTargetParent:compound?"physical":null,globalBusinessSource:true,onlyParent:true,literalParent:literal);

    /// <summary>普通项目引用全局字典时，也可只按实际发布的父字段和固定条件筛选。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,true,false,false,false)]
    [InlineData("MultiSelectPage",1,false,true,true,false)]
    [InlineData("MultiSelectPage",0,false,true,false,true)]
    [InlineData("MultiSelectPage",1,true,false,true,true)]
    [InlineData("MultiCheckBoxDatabase",0,true,false,true,false)]
    [InlineData("MultiCheckBoxDatabase",1,false,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,true,false,true)]
    [InlineData("MultiCheckBoxDatabase",1,false,false,true,true)]
    public Task Project_business_multivalue_uses_static_global_parent(string kind,int mode,bool rowProject,bool compound,bool literal,bool globalParent)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:rowProject,
            parent:true,globalParent:globalParent,parentWithoutProject:globalParent,parentAccount:globalParent&&mode==1,
            compoundTargetParent:compound?"physical":null,globalBusinessSource:true,onlyParent:true,literalParent:literal,
            ordinaryBusinessSource:true);

    /// <summary>全局字典同时按当前项目和实际父范围隔离，可叠加固定条件或账号等值。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,false,false,false)]
    [InlineData("MultiSelectPage",1,true,false,false,true)]
    [InlineData("MultiSelectPage",1,false,true,true,false)]
    [InlineData("MultiSelectPage",0,true,true,true,false)]
    [InlineData("MultiCheckBoxDatabase",0,false,false,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,true,false,false,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,true,false,false)]
    [InlineData("MultiCheckBoxDatabase",1,true,true,true,false)]
    public Task Global_business_multivalue_uses_project_and_physical_parent(string kind,int mode,bool compound,bool globalParent,bool targetAccount,bool literal)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:true,
            parent:true,globalParent:globalParent,parentWithoutProject:globalParent,parentAccount:globalParent&&mode==1,
            compoundTargetParent:compound?"physical":null,globalBusinessSource:true,onlyParent:!targetAccount,literalParent:literal,
            projectParentScope:true);

    /// <summary>普通项目来源与有行项目的全局来源共用显式项目和真实父范围。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false,false,false)]
    [InlineData("MultiSelectPage",1,true,true,false)]
    [InlineData("MultiCheckBoxDatabase",0,true,true,true)]
    [InlineData("MultiCheckBoxDatabase",1,false,false,true)]
    public Task Project_business_multivalue_uses_project_and_physical_parent(string kind,int mode,bool compound,bool globalParent,bool targetAccount)=>
        CompileCascadeLabel(kind,mode,selfTree:false,indirect:false,selectedPage:true,physical:true,globalRowProject:true,
            parent:true,globalParent:globalParent,parentWithoutProject:globalParent,parentAccount:globalParent&&mode==1,
            compoundTargetParent:compound?"physical":null,globalBusinessSource:true,onlyParent:!targetAccount,literalParent:!targetAccount,
            projectParentScope:true,ordinaryBusinessSource:true,textDictionaryProject:compound);

    /// <summary>原复选的单父与复合父采用同样的物理外键规则。</summary>
    [Theory]
    [InlineData(0,false,false,false,"int32",false)]
    [InlineData(1,false,false,false,"int32",true)]
    [InlineData(0,true,false,true,"int32",false)]
    [InlineData(1,true,false,false,"string",true)]
    [InlineData(0,true,true,false,"string",false)]
    [InlineData(1,true,true,true,"int32",true)]
    [InlineData(1,true,false,false,"int64",true,false)]
    public Task Checkbox_published_page_uses_single_custom_parent(int mode,bool global,bool globalParent,bool projectScope,string type,bool typeTwo,bool rowProject=true)=>
        CompileCascadeLabel("MultiCheckBoxDatabase",mode,selfTree:false,indirect:false,selectedPage:true,physical:true,
            globalRowProject:global?rowProject:null,parent:true,onlyParent:true,globalParent:globalParent,parentWithoutProject:globalParent,
            parentAccount:globalParent&&mode==1,globalBusinessSource:true,projectParentScope:projectScope,
            ordinaryBusinessSource:!global,textDictionaryProject:projectScope&&typeTwo,customCheckboxParentType:type,
            customCheckboxTypeTwo:typeTwo);

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

    /// <summary>多选沿业务子页的同一父记录绑定独立级联或额外物理父键。</summary>
    [Theory]
    [InlineData("physical",false,null,false)]
    [InlineData("physical",false,null,true)]
    [InlineData("independent",true,null,false)]
    [InlineData("independent",false,true,false)]
    public Task Selected_page_compound_source_parent_conditions_compile_in_original_endpoints(string sourceScope,bool logicalTarget,
        bool? globalTarget,bool onlyParent)=>
        CompileCascadeLabel("MultiSelectPage",0,selfTree:false,indirect:false,selectedPage:true,
            physical:!logicalTarget,globalRowProject:globalTarget,parent:true,logicalParent:logicalTarget,
            sourceLogicalParent:true,compoundSourceParent:sourceScope,onlyParent:onlyParent);

    /// <summary>原多选目标页的各父字段同时匹配同一真实父记录。</summary>
    [Theory]
    [InlineData("physical",0)]
    [InlineData("mixed",0)]
    [InlineData("mixed",1)]
    [InlineData("independent",0)]
    public Task Selected_page_compound_target_parent_conditions_compile_in_original_endpoints(string targetScope,int storageMode)=>
        CompileCascadeLabel("MultiSelectPage",storageMode,selfTree:false,indirect:false,selectedPage:true,
            physical:targetScope=="physical",parent:true,logicalParent:targetScope!="physical",
            sourceLogicalParent:targetScope=="independent",onlyParent:targetScope=="physical",compoundTargetParent:targetScope);

    /// <summary>数据库复选绑定普通发布目标页的全部父字段，沿原候选、写入和 Excel。</summary>
    [Theory]
    [InlineData("physical",0,false)]
    [InlineData("mixed",0,false)]
    [InlineData("mixed",1,false)]
    [InlineData("independent",0,false)]
    [InlineData("independent",0,true)]
    public Task Checkbox_published_page_compound_target_parents_compile_in_original_endpoints(string targetScope,int storageMode,bool exact)=>
        CompileCascadeLabel("MultiCheckBoxDatabase",storageMode,selfTree:false,indirect:false,selectedPage:true,
            physical:targetScope=="physical",parent:true,logicalParent:targetScope!="physical",
            sourceLogicalParent:targetScope=="independent",onlyParent:targetScope=="physical",
            compoundTargetParent:targetScope,exact:exact);

    /// <summary>显式全局目标的多个物理父字段同时服从发布父页与目标账号条件。</summary>
    [Theory]
    [InlineData("MultiSelectPage",true,true,0)]
    [InlineData("MultiSelectPage",false,false,0)]
    [InlineData("MultiCheckBoxDatabase",true,true,0)]
    [InlineData("MultiCheckBoxDatabase",false,false,1)]
    [InlineData("MultiCheckBoxDatabase",false,true,1)]
    public Task Global_multivalue_target_compound_physical_parents_compile_in_original_endpoints(
        string kind,bool hasRowProject,bool globalParent,int storageMode)=>
        CompileCascadeLabel(kind,storageMode,selfTree:false,indirect:false,selectedPage:true,physical:true,
            globalRowProject:hasRowProject,parent:true,globalParent:globalParent,
            parentWithoutProject:globalParent,parentAccount:globalParent,compoundTargetParent:"physical");

    /// <summary>显式全局目标以实际级联层位和物理键或第二条路径限定同一父记录。</summary>
    [Theory]
    [InlineData("MultiSelectPage","mixed",true,true,0)]
    [InlineData("MultiSelectPage","independent",false,true,0)]
    [InlineData("MultiSelectPage","mixed",false,false,0)]
    [InlineData("MultiCheckBoxDatabase","independent",true,false,0)]
    [InlineData("MultiCheckBoxDatabase","mixed",false,true,1)]
    public Task Global_multivalue_target_compound_cascade_parents_compile_in_original_endpoints(
        string kind,string targetScope,bool hasRowProject,bool globalParent,int storageMode)=>
        CompileCascadeLabel(kind,storageMode,selfTree:false,indirect:false,selectedPage:true,
            globalRowProject:hasRowProject,parent:true,logicalParent:true,globalParent:globalParent,
            parentWithoutProject:globalParent,parentAccount:globalParent,compoundTargetParent:targetScope,
            sourceLogicalParent:kind=="MultiSelectPage"&&targetScope=="independent");

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

    /// <summary>附加逻辑条件只进入原 Excel 名称反查的发布计划。</summary>
    [Theory]
    [InlineData(false,false,null)]
    [InlineData(true,true,false)]
    [InlineData(false,true,true)]
    [InlineData(false,true,null,true)]
    public Task Multi_import_compiles_control_cascade_conditions(bool selfTree,bool selectedPage,bool? rowProject,bool logicalParent=false)=>
        CompileCascadeLabel("MultiSelectPage",0,selfTree,indirect:false,selectedPage:selectedPage,physical:true,
            globalRowProject:rowProject,parent:logicalParent,logicalParent:logicalParent,importLogical:true);

    /// <summary>精确匹配与级联条件同时进入真实生成宿主，跨表和自树沿原路径读取。</summary>
    [Theory]
    [InlineData(0,true,false,false)]
    [InlineData(1,false,false,false)]
    [InlineData(0,false,true,false)]
    [InlineData(0,true,false,true)]
    [InlineData(1,true,false,true)]
    public Task Multi_exact_scope_combines_cascade_conditions(int mode,bool selfTree,bool logicalParent,bool exactLeaf)=>
        CompileCascadeLabel("MultiSelectPage",mode,selfTree,indirect:false,account:true,selectedPage:true,
            physical:logicalParent, parent:logicalParent, logicalParent:logicalParent, importLogical:logicalParent, exact:true, exactLeaf:exactLeaf);

    /// <summary>全局发布页仍按原级联路径、来源父级及当前角色完成读写。</summary>
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(false,true,true)]
    [InlineData(true,false,true)]
    public Task Global_multi_exact_scope_combines_cascade_conditions(bool selfTree,bool logicalParent,bool rowProject)=>
        CompileCascadeLabel("MultiSelectPage",0,selfTree,indirect:false,selectedPage:true,
            physical:logicalParent,parent:logicalParent,logicalParent:logicalParent,importLogical:logicalParent,
            globalRowProject:rowProject,exact:true,exactLeaf:selfTree);

    /// <summary>原复选对级联角色匹配沿实际范围绑定名称、缓存与父级。</summary>
    [Theory]
    [InlineData(0,false,true,null)]
    [InlineData(1,true,false,null)]
    [InlineData(0,false,true,true)]
    public Task Checkbox_exact_scope_combines_cascade_conditions(int mode,bool selfTree,bool logicalParent,bool? rowProject)=>
        CompileCascadeLabel("MultiCheckBoxDatabase",mode,selfTree,indirect:false,selectedPage:true,
            physical:logicalParent,parent:logicalParent,logicalParent:logicalParent,
            globalRowProject:rowProject,exact:true,exactLeaf:selfTree);

    /// <summary>原弹窗和复选的全局父范围也须执行来源页全部同父条件。</summary>
    [Theory]
    [InlineData("MultiSelectPage","physical","physical",true,0)]
    [InlineData("MultiSelectPage","independent","mixed",false,0)]
    [InlineData("MultiCheckBoxDatabase","physical","mixed",false,0)]
    [InlineData("MultiCheckBoxDatabase","independent","independent",true,0)]
    [InlineData("MultiSelectPage","physicalOnly","physical",false,0)]
    [InlineData("MultiCheckBoxDatabase","physicalOnly","mixed",false,1)]
    public Task Global_multivalue_source_compound_parents_compile_in_original_endpoints(
        string kind,string sourceScope,string targetScope,bool hasRowProject,int storageMode)=>
        CompileCascadeLabel(kind,storageMode,selfTree:false,indirect:false,selectedPage:true,
            physical:targetScope=="physical",globalRowProject:hasRowProject,parent:true,logicalParent:targetScope!="physical",
            sourceLogicalParent:sourceScope!="physicalOnly",globalParent:true,parentWithoutProject:true,parentAccount:true,
            compoundSourceParent:sourceScope,compoundTargetParent:targetScope,globalCompoundSource:true);

    /// <summary>项目内目标仍按项目过滤，同时验证指向同一全局父记录的所有已发布字段。</summary>
    [Theory]
    [InlineData("MultiSelectPage","physical",false,0)]
    [InlineData("MultiSelectPage","mixed",true,0)]
    [InlineData("MultiSelectPage","independent",true,0)]
    [InlineData("MultiCheckBoxDatabase","physical",false,0)]
    [InlineData("MultiCheckBoxDatabase","independent",true,0)]
    [InlineData("MultiCheckBoxDatabase","mixed",false,1)]
    [InlineData("MultiSelectPage","physical",true,0,"physical")]
    [InlineData("MultiCheckBoxDatabase","mixed",false,0,"physicalOnly")]
    public Task Project_multivalue_target_preserves_compound_global_parent(
        string kind,string targetScope,bool logicalSource,int storageMode,string? sourceScope=null)=>
        CompileCascadeLabel(kind,storageMode,selfTree:false,indirect:false,selectedPage:true,
            physical:targetScope=="physical",parent:true,logicalParent:targetScope!="physical",
            sourceLogicalParent:logicalSource,globalParent:true,parentWithoutProject:true,parentAccount:true,
            compoundTargetParent:targetScope,projectTargetGlobalParent:true,
            compoundSourceParent:sourceScope,globalCompoundSource:sourceScope is not null);

    /// <summary>项目内多值页通过单个物理外键绑定实际全局父页，账号条件可只在父页。</summary>
    [Theory]
    [InlineData("MultiSelectPage",0,false)]
    [InlineData("MultiCheckBoxDatabase",0,false)]
    [InlineData("MultiSelectPage",1,true)]
    [InlineData("MultiCheckBoxDatabase",1,true)]
    public Task Project_multivalue_target_preserves_physical_global_parent(string kind,int storageMode,bool exact)=>
        CompileCascadeLabel(kind,storageMode,selfTree:false,indirect:false,selectedPage:true,physical:true,parent:true,
            onlyParent:true,globalParent:true,parentWithoutProject:true,parentAccount:true,exact:exact,projectTargetGlobalParent:true);

    private static async Task CompileCascadeLabel(string kind,int mode,bool selfTree,bool indirect,bool account=false,
        bool recursive=false,bool selectedPage=false,bool physical=false,bool? globalRowProject=null,bool parent=false,bool formParent=false,bool logicalParent=false,bool onlyParent=false,bool sourceLogicalParent=false,
        bool globalParent=false,bool parentWithoutProject=false,bool parentAccount=false,bool importLogical=false,bool exact=false,bool exactLeaf=false,string? compoundSourceParent=null,string? compoundTargetParent=null,
        bool globalCompoundSource=false,bool projectTargetGlobalParent=false,bool globalBusinessSource=false,bool literalParent=false,
        bool projectParentScope=false,bool ordinaryBusinessSource=false,bool textDictionaryProject=false,
        string? customCheckboxParentType=null,bool customCheckboxTypeTwo=false)
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
        var consumerTable=new TableMetadata{Id="9",TableName="label_consumer",IsRongyu=mode};
        if(globalBusinessSource&&!ordinaryBusinessSource)consumerTable.Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        catalog.ConfigTables.Add(consumerTable);
        catalog.ConfigPages.Add(new(){Id="901",ConfigTableId="9",IsImport=1,IsExport=1,WhereSql="yz_project_id=@current_yz_project_id"});
        catalog.ConfigColumns.AddRange([
            new(){Id="91",ConfigTableId="9",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="92",ConfigTableId="9",ColumnName="name",Name="记录",LogicalType="string"},
            new(){Id="93",ConfigTableId="9",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="94",ConfigTableId="9",ColumnName="delete_no",LogicalType="datetime"},
            new(){Id="95",ConfigTableId="9",ColumnName="selected_ids",Name="关联",LogicalType="string",ControlKind=kind,
                LinkConfigTableId=targetTable,ControlDetail=JsonSerializer.Serialize(new{tableId=targetTable,pageId=physical?(object)201L:targetPage,nameField=display})}
        ]);
        if(globalBusinessSource&&mode==1)
        {
            catalog.ConfigColumns.Add(new(){Id="96",ConfigTableId="9",ColumnName="selected_names",LogicalType="string",
                ColumnLength="max",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                    ["columnFullName"]=JsonSerializer.SerializeToElement("selected.name")}});
            catalog.ConfigPageColumns.Add(new(){Id="90196",ConfigPageId="901",ConfigColumnId="96",IsShow=1});
        }
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
            if(!onlyParent)
            {
                var scopeName=globalBusinessSource||compoundTargetParent=="physical"&&(globalRowProject is not null||projectTargetGlobalParent)?"name":
                    selfTree?"tree_name":"country_name";
                scopePage.WhereSql+=$" AND {scopeName} <> '排除目标页名称' AND store_id=@current_store_id";
            }
            if(globalRowProject is { } rowProject)
            {
                catalog.ConfigTables.Single(table=>table.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                scopePage.WhereSql=scopePage.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
                if(onlyParent)scopePage.WhereSql=literalParent?"name <> '排除目标页名称'":"";
                if(projectParentScope)
                    scopePage.WhereSql="yz_project_id=@current_yz_project_id"+(scopePage.WhereSql.Length==0?"":" AND "+scopePage.WhereSql);
                if(textDictionaryProject)
                {
                    var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");
                    project.LogicalType="string";
                    project.ColumnLength="24";
                }
                if(selfTree)
                {
                    catalog.ConfigTables.Single(table=>table.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
                    var treePage=catalog.ConfigPages.Single(page=>page.Id=="301");
                    treePage.WhereSql=exact?"name <> '关闭节点'":
                        treePage.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);
                }
                if(!rowProject)
                {
                    var project=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="yz_project_id");
                    catalog.ConfigColumns.Remove(project);
                    catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId==project.Id);
                }
            }
            if(!onlyParent||projectTargetGlobalParent&&mode==1||globalBusinessSource&&parentAccount)
                catalog.ConfigPages.Single(page=>page.Id=="901").WhereSql+=" AND store_id=@current_store_id";
        }
        var parentField=customCheckboxParentType is not null?"parent_key":logicalParent?"country_id":"target_scope_id";
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
            if(!logicalParent)catalog.ConfigColumns.Add(new(){Id="214",ConfigTableId="2",ColumnName=parentField,
                LogicalType=customCheckboxParentType??"string",ColumnLength=customCheckboxParentType is null or "string"?"24":"",LinkConfigTableId=parentTable});
            catalog.ConfigColumns.Add(new(){Id="914",ConfigTableId="9",ColumnName="source_scope_id",Name="所属记录",LogicalType="int64",LinkConfigTableId=parentTable});
            var page=catalog.ConfigPages.Single(page=>page.Id=="201");
            page.WhereSql+=(page.WhereSql.Length==0?"":" AND ")+parentField+"=@parent_id";
            if(projectTargetGlobalParent&&kind=="MultiCheckBoxDatabase"||customCheckboxParentType is not null&&!customCheckboxTypeTwo)
                page.Extra["type"]=JsonSerializer.SerializeToElement(0);
            if(compoundTargetParent is not null)
            {
                if(compoundTargetParent=="independent")
                {
                    catalog.ConfigColumns.Add(new(){Id="217",ConfigTableId="2",ColumnName="other_area_id",LogicalType="int64",
                        LinkConfigTableId="4",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new
                        {
                            sourceMode="crossTable",levels=new object[]{new{table="label_cross_country",columnPrefix="other_country"},
                                new{table="label_cross_area",columnPrefix="other_area",parentField="label_cross_country_id"}}
                        })});
                    catalog.ConfigPageColumns.Add(new(){Id="201217",ConfigPageId="201",ConfigColumnId="217",IsEdit=1,IsShow=1});
                }
                else catalog.ConfigColumns.Add(new(){Id="217",ConfigTableId="2",ColumnName="other_country_id",LogicalType="int64",LinkConfigTableId=parentTable});
                page.WhereSql+=" AND other_country_id=@parentId";
                if(kind=="MultiCheckBoxDatabase")page.Extra["type"]=JsonSerializer.SerializeToElement(0);
            }
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
                if(!sourceLogicalParent&&!(parentAccount&&(projectTargetGlobalParent||
                    compoundTargetParent is not null&&globalRowProject is not null)))
                {
                    var foreign=catalog.ConfigColumns.Single(column=>column.Id=="914");
                    foreign.ControlKind="List";
                    foreign.ControlDetail=JsonSerializer.Serialize(new{tableId=parentTable,pageId=globalPage.Id,nameField="name"});
                    if(globalBusinessSource&&mode==1)
                        catalog.ConfigColumns.Add(new(){Id="917",ConfigTableId="9",ColumnName="source_scope_name",LogicalType="string",
                            ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
                                ["columnFullName"]=JsonSerializer.SerializeToElement("source_scope.name")}});
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
            if(compoundSourceParent is not null)
            {
                // 物理父条件放在前面；固定缓存和渲染依然必须使用实际逻辑父级。
                var owner=catalog.ConfigPages.Single(page=>page.Id=="901");
                if(compoundSourceParent is "physical" or "physicalOnly")
                {
                    catalog.ConfigColumns.Add(new(){Id="916",ConfigTableId="9",ColumnName="other_country_id",LogicalType="int64",LinkConfigTableId="3"});
                    owner.WhereSql="other_country_id=@parentId AND "+owner.WhereSql;
                }
                else
                {
                    catalog.ConfigColumns.Add(new(){Id="916",ConfigTableId="9",ColumnName="other_area_id",LogicalType="int64",
                        LinkConfigTableId="4",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new
                        {
                            sourceMode="crossTable",levels=new object[]{new{table="label_cross_country",columnPrefix="other_country"},
                                new{table="label_cross_area",columnPrefix="other_area",parentField="label_cross_country_id"}}
                        })});
                    catalog.ConfigPageColumns.Add(new(){Id="901916",ConfigPageId="901",ConfigColumnId="916",IsEdit=1,IsShow=1});
                    owner.WhereSql+=" AND other_country_id=@parentId";
                }
            }
        }
        if(importLogical)
        {
            var control=catalog.ConfigColumns.Single(column=>column.Id=="95");
            var pageField=catalog.ConfigPageColumns.Single(column=>column.Id=="90195");
            var whereSql=selfTree?"tree_name LIKE '指定%'":
                "country_id='9007199254741197' AND country_name <> '排除导入名称'";
            if(logicalParent)whereSql="country_id='9007199254741197' AND country_id=@parentId";
            if(selectedPage)
                pageField.Extra["controlDetail"]=JsonSerializer.SerializeToElement(JsonSerializer.Serialize(new{whereSql}));
            else
                control.ControlDetail=JsonSerializer.Serialize(new{pageId=targetPage,nameField=display,whereSql});
        }
        if(globalBusinessSource)
        {
            var origin=catalog.ConfigPages.Single(page=>page.Id=="901");
            var overwrite=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(origin))!;
            overwrite.Id="902";
            overwrite.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
            overwrite.Extra["exportMethod"]=JsonSerializer.SerializeToElement("edit");
            catalog.ConfigPages.Add(overwrite);
            catalog.ConfigColumns.Single(column=>column.Id=="92").SortNo=-10;
            catalog.ConfigPageColumns.Single(field=>field.Id=="90192").SortNo=-10;
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="901").ToArray())
            {
                var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!;
                copy.Id="902"+field.ConfigColumnId;
                copy.ConfigPageId="902";
                catalog.ConfigPageColumns.Add(copy);
            }
        }
        RedundantMultiNameCacheTests.Published(catalog);
        if(exact)
        {
            var match=exactLeaf?"tree_name":logicalParent||selfTree?"name":"country_name";
            catalog.ConfigPages.Single(page=>page.Id==targetPage).Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement(match);
            foreach(var admin in catalog.YzAdminTypes)
                admin.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("real_name");
        }

        var model=GenerationModel.Create(catalog,ns);
        if(compoundTargetParent is not null)
        {
            var owner=model.Tables.Single(item=>item.Source.Id=="9");
            var child=owner.Pages.Single(item=>item.Source.Id=="901");
            var relation=owner.Columns.Single(item=>item.Source.Id=="95");
            if(kind=="MultiCheckBoxDatabase")
            {
                var resolved=DatabaseCheckboxCompilation.Resolve(model,owner,child,relation);
                Assert.True(resolved.Definition is not null,resolved.Reason);
                Assert.Equal(2,resolved.Definition!.Scopes.Count(scope=>scope.Source=="parent"));
                Assert.NotNull(resolved.Definition.SourceParent);
            }
            else
            {
                var resolved=MultiSelectCompilation.Resolve(model,owner,child,relation);
                Assert.True(resolved.Definition is not null,resolved.Reason);
                Assert.True(resolved.Definition!.SameParent);
            }
        }
        if(globalCompoundSource)
        {
            var owner=model.Tables.Single(item=>item.Source.Id=="9");
            var sourcePage=owner.Pages.Single(item=>item.Source.Id=="901");
            var input=owner.Columns.Single(item=>item.Source.Id=="95");
            if(kind=="MultiSelectPage")
            {
                var resolved=MultiSelectCompilation.Resolve(model,owner,sourcePage,input);
                Assert.True(resolved.Definition is not null,resolved.Reason);
                Assert.True(resolved.Definition!.GlobalParent&&resolved.Definition.SameParent);
                Assert.NotEmpty(resolved.Definition.CompoundSourceDependencies);
            }
            else
            {
                var resolved=DatabaseCheckboxCompilation.Resolve(model,owner,sourcePage,input);
                Assert.True(resolved.Definition is not null,resolved.Reason);
                Assert.True(resolved.Definition!.GlobalParent);
                Assert.Equal(2,resolved.Definition.SourceScopeColumns.Count);
                if(sourceLogicalParent)
                    Assert.Contains(resolved.Definition.CompoundSourceDependencies,field=>field.Source.ColumnName=="source_area_id");
            }
        }
        if(compoundSourceParent is not null&&!globalCompoundSource)
        {
            var owner=model.Tables.Single(item=>item.Source.Id=="9");
            var ownerPage=owner.Pages.Single(item=>item.Source.Id=="901");
            var field=owner.Columns.Single(item=>item.Source.Id=="95");
            var resolution=MultiSelectCompilation.Resolve(model,owner,ownerPage,field);
            Assert.True(resolution.Definition is not null,resolution.Reason);
            Assert.True(resolution.Definition!.SameParent);
            Assert.True(resolution.Definition.LogicalSourceParent);
            Assert.Null(resolution.Definition.TargetParentField);
            if(compoundSourceParent=="independent")
            {
                // 全局目标的表级名称读取需要在两个有效来源页得到等价的同一父键证明。
                var reordered=new PageMetadata{Id="902",ConfigTableId=owner.Source.Id,YzProjectId=catalog.YzProjectId,
                    WhereSql=ownerPage.Source.WhereSql.Replace("source_scope_id=@parentId AND other_country_id=@parentId",
                        "other_country_id=@parentId AND source_scope_id=@parentId",StringComparison.Ordinal)};
                Assert.NotEqual(ownerPage.Source.WhereSql,reordered.WhereSql);
                var target=resolution.Definition.Target;
                var boundPage=resolution.Definition.TargetPage;
                var syntax=FixedConditionPlan.Compile(boundPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
                var binding=MultiTargetParentScope.Compile(catalog,target.Source,syntax)!;
                var actual=binding.Source(catalog,owner.Source,field.Source,ownerPage.Source);
                catalog.ConfigPages.Add(reordered);
                try
                {
                    var published=binding.Source(catalog,owner.Source,field.Source);
                    Assert.Equal(actual.Column.Id,published.Column.Id);
                    Assert.Equal(Assert.Single(actual.AdditionalColumns).Id,Assert.Single(published.AdditionalColumns).Id);
                }
                finally{catalog.ConfigPages.Remove(reordered);}
            }
        }
        if(globalBusinessSource&&onlyParent)
        {
            var target=model.Tables.Single(item=>item.Source.Id=="2");
            var targetPageModel=target.Pages.Single(item=>item.Source.Id=="201");
            foreach(var tree in target.Columns.Where(NonRedundantCrossTableSubset.Declared))
            {
                var resolved=CrossTableWriteCompilation.Resolve(model,target,targetPageModel,tree);
                Assert.True(resolved.Definition is not null,resolved.Reason);
            }
            _=NonRedundantReadCompilation.ForPage(model,target,targetPageModel);
        }
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        new FrontendPackager().Validate(catalog);
        if(globalBusinessSource&&compoundTargetParent=="physical")
        {
            // 同一请求的第二个父字段不能改指另一张表，也不能靠首个字段继续发布。
            var other=catalog.ConfigColumns.Single(column=>column.Id=="217");
            var validParent=other.LinkConfigTableId;
            other.LinkConfigTableId=globalParent?"4":"3";
            try
            {
                var source=catalog.ConfigTables.Single(table=>table.Id=="9");
                var target=catalog.ConfigTables.Single(table=>table.Id=="2");
                var field=catalog.ConfigColumns.Single(column=>column.Id=="95");
                var error=Assert.Throws<InvalidOperationException>(()=>mode==1
                    ?GlobalMultipleRelationTarget.RequireCached(catalog,source,field,target)
                    :GlobalMultipleRelationTarget.Require(catalog,source,field,target));
                Assert.Contains("父",error.Message);
            }
            finally{other.LinkConfigTableId=validParent;}
        }
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
            var targetScope=metadata.ConfigPages.Single(page=>page.Id=="201").WhereSql;
            if(!onlyParent||literalParent)Assert.Contains("排除目标页名称",targetScope);
            if(globalBusinessSource&&onlyParent)Assert.DoesNotContain("@current_store_id",targetScope);
            if(projectParentScope)Assert.Contains("yz_project_id=@current_yz_project_id",targetScope);
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
            var physicalProjectCheckbox=kind=="MultiCheckBoxDatabase"&&compoundTargetParent=="physical"&&!exact&&mode==0&&
                globalRowProject is null&&!projectTargetGlobalParent;
            if(selectedPage)
            {
                var code=File.ReadAllText(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                if(physicalProjectCheckbox)
                {
                    // 纯物理复选走实际候选查询，在同一当前读计划中同时验证两个父键。
                    Assert.Contains("CheckboxScope901_95Async(request.ParentId,actor,form,false,cancellationToken)",code);
                    Assert.Contains("ApplyTextParent(query,session,\"target_scope_id\",parentId!)",code);
                    Assert.Contains("query=query.Where(row=>row.OtherCountryId==",code);
                    Assert.Contains("return locking?query.ForUpdate():query;",code);
                }
                else if(compoundSourceParent is not null&&onlyParent)
                    Assert.Contains("var targetParentId = logicalRelationParentId;",code);
                else if(kind=="MultiCheckBoxDatabase"&&(exact||compoundTargetParent is not null))
                {
                    Assert.Contains(compoundTargetParent is not null?"CompileScopedCheckboxLabel":
                        globalRowProject is not null?"CompileGlobalExactSingleCandidate":
                        logicalParent?"CompileExactCheckboxParentCandidate":
                        projectTargetGlobalParent?"CompileScopedCheckboxLabel":"CompileRelationMembership",code);
                    if(exact)Assert.Contains("""checkboxExactAuthorization.ResolveExactDataManage("201", "list", "real_name")""",code);
                    Assert.Contains("condition:condition,currentRead:locking",code);
                }
                else
                {
                    // 非冗余复选的逻辑目标父级由复选计划承接，校验真实目标页和显示列。
                    var compiler=exact?"CompileExactMultiLabel":
                        kind=="MultiCheckBoxDatabase"&&(mode==0&&logicalParent||customCheckboxParentType is not null)
                            ?"CompileScopedCheckboxLabel":"CompileScopedMultiLabel";
                    Assert.Contains(compiler+"(\"2\",\"201\",\""+display+"\"",code);
                    if(exact)
                    {
                        Assert.Contains("""multiExactAuthorization.ResolveExactDataManage("201", "list", "real_name")""",code);
                        Assert.Contains("""multiImportExactAuthorization.ResolveExactDataManage("201", "list", "real_name")""",code);
                    }
                }
                if(!physicalProjectCheckbox&&(compoundSourceParent is null||!onlyParent))Assert.Contains(".BindAccount(actor)",code);
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
                        if(kind=="MultiSelectPage"||compoundTargetParent is null&&!projectTargetGlobalParent&&
                            (customCheckboxParentType is null||customCheckboxTypeTwo))
                            Assert.Contains("ValidateParents201Async(parentId,actor,cancellationToken,locking:false)",targetCode);
                        else
                            Assert.Contains("PageMultiGlobalParent201.BindAccount(actor)",targetCode);
                        var keyArgument="entity."+GenerationModel.Pascal(parentField)+
                            (customCheckboxParentType is "int32" or "int64"?"?.ToString(CultureInfo.InvariantCulture)":"");
                        Assert.Contains(keyArgument+",actor,cancellationToken",targetCode);
                        Assert.Contains("Task<long> Count201Async",targetCode);
                    }
                }
                if(parent)
                {
                    if(compoundSourceParent is not null&&onlyParent)
                        Assert.Contains("row.TargetScopeId == targetParentId!",code);
                    else if(!physicalProjectCheckbox)Assert.Contains("new(\""+parentField+"\",FixedReadComparison.Equal,",code);
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
            if(projectTargetGlobalParent&&compoundTargetParent is null)
            {
                var checkbox=kind=="MultiCheckBoxDatabase";
                const System.Reflection.BindingFlags staticPrivate=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var plan=Assert.IsType<NonRedundantReadPlan>(consumer.GetField(
                    checkbox?"DerivedCheckboxPlan901_95":"MultiDerivedPlan901_95",staticPrivate)!.GetValue(null));
                var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                var scope=Assert.IsType<string>(sql.GetType().GetProperty("Scope",hidden)!.GetValue(sql));
                Assert.Contains("`yz_project_id`",scope);
                Assert.Contains("`target_scope_id`",scope);
                Assert.Contains("`label_cross_country`",scope);
                Assert.Contains("`store_id`",scope);
                Assert.True(plan.RequiresAccount);
                RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.LabelCrossSourceService",true)!.TypeHandle);
                if(mode==1)
                {
                    var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                        assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                    var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                        maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
                    var read=plans["9"]!.GetType().GetProperty("Read")!.GetValue(plans["9"])!;
                    var owner=model.Tables.Single(item=>item.Source.Id=="9");
                    var ownerPage=owner.Pages.Single(item=>item.Source.Id=="901");
                    var names=checkbox?Assert.Single(DatabaseCheckboxCompilation.ForPage(model,owner,ownerPage)).Names:
                        Assert.Single(MultiSelectCompilation.ForPage(model,owner,ownerPage)).Names!;
                    var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,[names.Source.ColumnName]));
                    Assert.Contains("`target_scope_id`",expression);
                    Assert.Contains("`store_id`",expression);
                }
                var parentKey=catalog.ConfigColumns.Single(item=>item.Id=="214");
                parentKey.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");
                var error=Assert.ThrowsAny<InvalidOperationException>(()=>NonRedundantReads.MultiGlobalParentPageId(catalog,"2","201"));
                Assert.Contains("全局物理父键必须绑定唯一发布页",error.Message);
                parentKey.Extra.Remove("linkConfigPageId");
            }
            if(compoundTargetParent is not null)
            {
                var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                if(!physicalProjectCheckbox)Assert.Contains("new(\"other_country_id\",FixedReadComparison.Equal,",code);
                var checkbox=kind=="MultiCheckBoxDatabase";
                var targetCode=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LabelCrossSourceService.g.cs"));
                if(checkbox)Assert.Contains("CheckboxOptions901_95Async",code);
                else Assert.Contains("SelectionSelected201Async",targetCode);
                if(!physicalProjectCheckbox)
                {
                    const System.Reflection.BindingFlags staticPrivate=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                    const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                    var plan=Assert.IsType<NonRedundantReadPlan>(consumer.GetField(
                        checkbox?"DerivedCheckboxPlan901_95":"MultiDerivedPlan901_95",staticPrivate)!.GetValue(null));
                    var sql=plan.GetType().GetProperty("MySql",hidden)!.GetValue(plan)!;
                    var scope=Assert.IsType<string>(sql.GetType().GetProperty("Scope",hidden)!.GetValue(sql));
                    Assert.Contains("BINARY CAST(",scope);
                    Assert.Contains(compoundTargetParent=="independent"?"`other_area_id`":"`other_country_id`",scope);
                    if(projectTargetGlobalParent)
                    {
                        Assert.Contains("`yz_project_id`",scope);
                        Assert.True(plan.RequiresAccount);
                        if(mode==1)
                        {
                            var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                                assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                            var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                                maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
                            var registered=plans["9"]!;
                            var read=registered.GetType().GetProperty("Read")!.GetValue(registered)!;
                            var owner=model.Tables.Single(item=>item.Source.Id=="9");
                            var sourcePage=owner.Pages.Single(item=>item.Source.Id=="901");
                            var cache=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,owner,sourcePage)).Names;
                            var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,[cache.Source.ColumnName]));
                            Assert.Contains("`yz_project_id`",expression);
                            Assert.Contains("`other_country_id`",expression);
                            Assert.Contains("`store_id`",expression);
                        }
                    }
                }

                var targetMetadata=catalog.ConfigPages.Single(item=>item.Id=="201");
                var previousWhere=targetMetadata.WhereSql;
                var linked=catalog.ConfigColumns.Single(item=>item.Id=="217");
                var previousLink=linked.LinkConfigTableId;
                if(compoundTargetParent=="independent")
                    targetMetadata.WhereSql=previousWhere.Replace("other_country_id=@parentId","other_area_id=@parentId",StringComparison.Ordinal);
                else linked.LinkConfigTableId="4";
                if(globalParent&&(globalRowProject is not null||projectTargetGlobalParent)&&compoundTargetParent!="physical")
                {
                    var error=Assert.ThrowsAny<InvalidOperationException>(()=>NonRedundantReads.MultiGlobalParentPageId(catalog,"2","201"));
                    Assert.Contains("同一真实父表",error.Message);
                    // 全局目标自身依赖路径证明，异父时在生成模型阶段就拒绝。
                    Assert.ThrowsAny<InvalidOperationException>(()=>GenerationModel.Create(catalog,ns));
                }
                else
                {
                    var invalid=GenerationModel.Create(catalog,ns);
                    var invalidOwner=invalid.Tables.Single(item=>item.Source.Id=="9");
                    var invalidPage=invalidOwner.Pages.Single(item=>item.Source.Id=="901");
                    var invalidField=invalidOwner.Columns.Single(item=>item.Source.Id=="95");
                    if(checkbox)Assert.Null(DatabaseCheckboxCompilation.Resolve(invalid,invalidOwner,invalidPage,invalidField).Definition);
                    else Assert.Null(MultiSelectCompilation.Resolve(invalid,invalidOwner,invalidPage,invalidField).Definition);
                }
                targetMetadata.WhereSql=previousWhere;
                linked.LinkConfigTableId=previousLink;

                if(globalParent&&(globalRowProject is not null||projectTargetGlobalParent)&&compoundTargetParent is "physical" or "mixed")
                {
                    // 同一父表的不同发布页不能由两个物理外键或级联与物理外键混用。
                    var parentTable=catalog.ConfigTables.Single(table=>table.Id==previousLink);
                    var publishedParent=catalog.ConfigPages.Single(page=>page.ConfigTableId==parentTable.Id);
                    var another=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(publishedParent))!;
                    another.Id="319";
                    another.WhereSql+=" AND name<>'另一父页'";
                    var canonical=catalog.ConfigColumns.SingleOrDefault(column=>column.Id=="214");
                    if(canonical is not null)canonical.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(publishedParent.Id);
                    linked.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(another.Id);
                    catalog.ConfigPages.Add(another);
                    try
                    {
                        var error=Assert.ThrowsAny<InvalidOperationException>(()=>NonRedundantReads.MultiGlobalParentPageId(catalog,"2","201"));
                        Assert.Contains("同一个实际发布父页",error.Message);
                    }
                    finally
                    {
                        canonical?.Extra.Remove("linkConfigPageId");
                        linked.Extra.Remove("linkConfigPageId");
                        catalog.ConfigPages.Remove(another);
                    }
                }

            }
            if(compoundTargetParent=="physical"&&(globalRowProject is null||kind=="MultiSelectPage"&&globalParent)||
                compoundTargetParent=="independent"&&globalRowProject==false&&kind=="MultiSelectPage"||
                globalCompoundSource&&compoundSourceParent=="independent"&&kind=="MultiCheckBoxDatabase"||
                projectTargetGlobalParent&&compoundTargetParent=="independent"&&kind=="MultiCheckBoxDatabase"||
                customCheckboxParentType=="int32"&&projectParentScope&&globalParent)
            {
                // 独立包编译使用实际发布目录，原页面继续带原控件及候选参数。
                foreach(var configPage in catalog.ConfigPages)
                    catalog.ConfigMenus.Add(new(){Id="700"+configPage.Id,YzProjectId=catalog.YzProjectId,
                        YzAdminTypeId=configPage.YzAdminTypeId,Name="页面"+configPage.Id,Kind="GENERATED_PAGE",ConfigPageId=configPage.Id});
                var full=Path.Combine(directory,"full");
                new ApplicationPackager().Create(catalog,full,ns,NonRedundantApplicationFixture.Root());
                foreach(var operation in new[]{"restore","build"})
                    await NonRedundantApplicationFixture.BuildAsync(full,operation);
                var detail=await File.ReadAllTextAsync(Path.Combine(full,"frontend/pages/yzp/label_consumer-detail-901.vue"));
                if(kind=="MultiCheckBoxDatabase")
                {
                    Assert.Contains("<uni-data-checkbox multiple",detail);
                    Assert.Contains("findNameIdSelected95",detail);
                }
                else
                {
                    Assert.Contains("selectPageDialog95",detail);
                    Assert.Contains(":parent_id=\"multiTargetParent('selectedIds')\"",detail);
                }
            }
            if(globalCompoundSource)
            {
                var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                var dependency=compoundSourceParent=="independent"?"other_area_id":"other_country_id";
                Assert.Contains("submitted.Contains("+JsonSerializer.Serialize(dependency)+")",code);
                if(compoundSourceParent!="independent")
                    Assert.Contains("PageMultiGlobalParent901.BindAccount(actor)",code);
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                const System.Reflection.BindingFlags staticPrivate=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                if(mode==0)
                {
                    var read=Assert.IsType<NonRedundantReadPlan>(consumer.GetField("ReadPlan901",staticPrivate)!.GetValue(null));
                    var sql=read.GetType().GetProperty("MySql",hidden)!.GetValue(read)!;
                    var owner=model.Tables.Single(item=>item.Source.Id=="9");
                    var page=owner.Pages.Single(item=>item.Source.Id=="901");
                    var field=kind=="MultiSelectPage"?Assert.Single(MultiSelectCompilation.ForPage(model,owner,page)).Names!:
                        Assert.Single(DatabaseCheckboxCompilation.ForPage(model,owner,page)).Names;
                    var expression=Assert.IsType<string>(sql.GetType().GetMethod("Expression",hidden)!.Invoke(sql,[field.Source.ColumnName]));
                    Assert.Contains("BINARY CAST(",expression);
                    Assert.Contains("`"+dependency+"`",expression);
                    if(compoundSourceParent=="independent")Assert.Contains("`source_area_id`",expression);
                    if(globalBusinessSource)AssertBothPhysicalSourceFields(expression);
                }
                else
                {
                    var maintenance=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(
                        assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!.GetProperty("Catalog")!.GetValue(null));
                    var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(
                        maintenance.GetType().GetProperty("Plans",hidden)!.GetValue(maintenance));
                    var plan=plans["9"]!;
                    var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
                    var owner=model.Tables.Single(item=>item.Source.Id=="9");
                    var page=owner.Pages.Single(item=>item.Source.Id=="901");
                    var cache=Assert.Single(DatabaseCheckboxCompilation.ForPage(model,owner,page)).Names;
                    var expression=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,[cache.Source.ColumnName]));
                    Assert.Contains("BINARY CAST(",expression);
                    Assert.Contains("`"+dependency+"`",expression);
                    if(globalBusinessSource)AssertBothPhysicalSourceFields(expression);
                }
                if((compoundSourceParent=="physical"||globalBusinessSource&&compoundSourceParent=="physicalOnly")&&compoundTargetParent=="physical")
                {
                    // 来源物理父与其他范围引用同一表，但切到另一发布页时必须拒绝。
                    var parentPage=catalog.ConfigPages.Single(item=>item.Id=="301");
                    var different=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(parentPage))!;
                    different.Id="319";
                    different.WhereSql+=" AND name <> '另一来源父页'";
                    var fixedKeys=catalog.ConfigColumns.Where(column=>column.Id is "214" or "217"||
                        globalBusinessSource&&column.Id=="914").ToArray();
                    var sourceKey=catalog.ConfigColumns.Single(column=>column.Id=="916");
                    foreach(var key in fixedKeys)key.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(parentPage.Id);
                    sourceKey.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement(different.Id);
                    catalog.ConfigPages.Add(different);
                    try
                    {
                        var invalid=Assert.ThrowsAny<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
                        if(globalBusinessSource)
                        {
                            // 全局业务页的能力审计可以先汇总包装错误，同时核对数据层拒绝的准确原因。
                            var dictionary=catalog.ConfigTables.Single(table=>table.Id=="2");
                            var fields=catalog.ConfigColumns.Where(column=>column.ConfigTableId=="2");
                            var definition=FixedConditionPlan.Compile(catalog.ConfigPages.Single(page=>page.Id=="201").WhereSql,
                                fields.Select(column=>column.ColumnName));
                            var targetParent=MultiTargetParentScope.CompileGlobalPage(catalog,dictionary,definition,kind=="MultiCheckBoxDatabase")!;
                            var source=catalog.ConfigTables.Single(table=>table.Id=="9");
                            var relation=catalog.ConfigColumns.Single(column=>column.Id=="95");
                            var binding=targetParent.Source(catalog,source,relation);
                            invalid=Assert.Throws<InvalidOperationException>(()=>
                                NonRedundantReads.RequireCompoundGlobalSourceParent(catalog,"2","201",binding));
                        }
                        Assert.Contains("来源字段与目标字段必须绑定同一个实际发布父页",invalid.Message);
                    }
                    finally
                    {
                        foreach(var key in fixedKeys)key.Extra.Remove("linkConfigPageId");
                        sourceKey.Extra.Remove("linkConfigPageId");
                        catalog.ConfigPages.Remove(different);
                    }
                }
            }
            if(compoundSourceParent is not null&&!globalCompoundSource)
            {
                var owner=model.Tables.Single(item=>item.Source.Id=="9");
                var ownerPage=owner.Pages.Single(item=>item.Source.Id=="901");
                var def=Assert.Single(MultiSelectCompilation.ForPage(model,owner,ownerPage));
                var dependency=compoundSourceParent=="physical"?"other_country_id":"other_area_id";
                Assert.Contains(def.CompoundSourceDependencies,field=>field.Source.ColumnName==dependency);
                var service=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                Assert.Contains("submitted.Contains("+JsonSerializer.Serialize(dependency)+")",service);
                Assert.Contains("var targetParentId = logicalRelationParentId;",service);
                const System.Reflection.BindingFlags staticPrivate=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var export=Assert.IsType<NonRedundantReadPlan>(consumer.GetField("ExportReadPlan901",staticPrivate)!.GetValue(null));
                var sql=export.GetType().GetProperty("MySql",hidden)!.GetValue(export)!;
                var expression=Assert.IsType<string>(sql.GetType().GetMethod("Expression",hidden)!.Invoke(sql,[def.Names!.Source.ColumnName]));
                Assert.Contains("BINARY CAST(",expression);
                Assert.Contains("`"+dependency+"`",expression);

                var published=catalog.ConfigPages.Single(item=>item.Id=="901");
                var originalWhere=published.WhereSql;
                published.WhereSql=originalWhere.Replace("other_country_id=@parentId","source_area_id=@parentId",StringComparison.Ordinal);
                var invalid=GenerationModel.Create(catalog,ns);
                var invalidOwner=invalid.Tables.Single(item=>item.Source.Id=="9");
                var input=invalidOwner.Columns.Single(item=>item.Source.Id=="95");
                Assert.Null(MultiSelectCompilation.Resolve(invalid,invalidOwner,invalidOwner.Pages.Single(item=>item.Source.Id=="901"),input).Definition);
                published.WhereSql=originalWhere;

                if(compoundSourceParent=="physical"&&!onlyParent)
                {
                    // 完整包仍使用原弹窗并把业务子页的父参数带入目标组件。
                    foreach(var configPage in catalog.ConfigPages)
                        catalog.ConfigMenus.Add(new(){Id="700"+configPage.Id,YzProjectId=catalog.YzProjectId,
                            YzAdminTypeId=configPage.YzAdminTypeId,Name="页面"+configPage.Id,Kind="GENERATED_PAGE",ConfigPageId=configPage.Id});
                    var full=Path.Combine(directory,"full");
                    new ApplicationPackager().Create(catalog,full,ns,NonRedundantApplicationFixture.Root());
                    foreach(var operation in new[]{"restore","build"})
                        await NonRedundantApplicationFixture.BuildAsync(full,operation);
                    var detail=await File.ReadAllTextAsync(Path.Combine(full,"frontend/pages/yzp/label_consumer-detail-901.vue"));
                    Assert.Contains("selectPageDialog95",detail);
                    Assert.Contains(":parent_id=\"multiTargetParent('selectedIds')\"",detail);
                    Assert.Contains("\"sameParent\":true",detail);
                }
            }
            if(importLogical)
            {
                const System.Reflection.BindingFlags staticPrivate=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var read=Assert.IsType<NonRedundantReadPlan>(consumer.GetField("MultiImportReadPlan901_95",staticPrivate)!.GetValue(null));
                Assert.Equal(selectedPage,read.RequiresAccount);
                var sql=read.GetType().GetProperty("MySql",hidden)!.GetValue(read)!;
                string Expression(string field)=>Assert.IsType<string>(sql.GetType().GetMethod("Expression",hidden)!.Invoke(sql,[field]));
                if(!logicalParent)Assert.Contains("CASE WHEN",Expression(selfTree?"tree_name":"country_name"));
                if(!selfTree)Assert.Contains("CASE WHEN",Expression("country_id"));
                if(logicalParent)Assert.Equal("country_id",sql.GetType().GetProperty("RequiredMultiParent",hidden)!.GetValue(sql));
                var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                Assert.Contains("PageAsync(MultiImportReadPlan901_95.BindAccount(actor)",code);
                Assert.Contains("condition:importCondition,currentRead:true",code);
            }
            var targetService=assembly.GetType(ns+".Generated.Services.LabelCrossSourceService",true)!;
            RuntimeHelpers.RunClassConstructor(targetService.TypeHandle);
            if(customCheckboxParentType is not null)
            {
                const System.Reflection.BindingFlags planFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags instanceFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var plan=Assert.IsType<NonRedundantReadPlan>(consumer.GetField("DerivedCheckboxPlan901_95",planFlags)!.GetValue(null));
                var sql=plan.GetType().GetProperty("MySql",instanceFlags)!.GetValue(plan)!;
                Assert.Equal(parentField,sql.GetType().GetProperty("RequiredMultiParent",instanceFlags)!.GetValue(sql));
                var types=Assert.IsAssignableFrom<IReadOnlyDictionary<string,string>>(
                    sql.GetType().GetProperty("Types",instanceFlags)!.GetValue(sql));
                Assert.Equal(customCheckboxParentType,types[parentField]);

                var table=catalog.ConfigTables.Single(item=>item.Id=="2");
                var key=catalog.ConfigColumns.Single(item=>item.Id=="214");
                var page=catalog.ConfigPages.Single(item=>item.Id=="201");
                var condition=FixedConditionPlan.Compile(page.WhereSql,catalog.ConfigColumns.Where(column=>column.ConfigTableId=="2")
                    .Select(column=>column.ColumnName));
                // 复选专用父字段不能作为普通多选父字段，也不能省略真实父表关联。
                Assert.Throws<InvalidOperationException>(()=>MultiTargetParentScope.Compile(catalog,table,condition));
                var previousLink=key.LinkConfigTableId;
                key.LinkConfigTableId="";
                try
                {
                    Assert.Throws<InvalidOperationException>(()=>MultiTargetParentScope.CompileCheckboxParent(catalog,table,condition));
                }
                finally{key.LinkConfigTableId=previousLink;}
            }
            var ordinaryStaticDictionary=ordinaryBusinessSource&&globalRowProject is not null&&onlyParent&&!projectParentScope;
            if(projectParentScope||ordinaryStaticDictionary)
            {
                const System.Reflection.BindingFlags planFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags instanceFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                string PlanScope(Type service,string field,string? requiredParent)
                {
                    var plan=Assert.IsType<NonRedundantReadPlan>(service.GetField(field,planFlags)!.GetValue(null));
                    var sql=plan.GetType().GetProperty("MySql",instanceFlags)!.GetValue(plan)!;
                    if(requiredParent is not null)
                        Assert.Equal(requiredParent,sql.GetType().GetProperty("RequiredMultiParent",instanceFlags)!.GetValue(sql));
                    return Assert.IsType<string>(sql.GetType().GetProperty("Scope",instanceFlags)!.GetValue(sql));
                }
                void RequireDictionaryScope(Type service,string field)
                {
                    var scope=PlanScope(service,field,parentField);
                    Assert.Contains("b.`"+parentField+"`",scope);
                    if(projectParentScope)
                        Assert.Contains("b.`yz_project_id`="+(textDictionaryProject?"@project_scope_text":"@project_scope_number"),scope);
                    else
                        Assert.DoesNotContain("b.`yz_project_id`=",scope);
                }
                RequireDictionaryScope(consumer,kind=="MultiCheckBoxDatabase"?"DerivedCheckboxPlan901_95":"MultiDerivedPlan901_95");
                if(kind=="MultiSelectPage")RequireDictionaryScope(targetService,"CandidateReadPlan201");
                if(ordinaryStaticDictionary&&mode==0)
                    Assert.Contains("b.`yz_project_id`=@project_scope_number",PlanScope(consumer,"ReadPlan901",null));
            }
            if(exactLeaf)
            {
                const System.Reflection.BindingFlags planFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
                const System.Reflection.BindingFlags instanceFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var plan=Assert.IsType<NonRedundantReadPlan>(targetService.GetField("ExactReadPlan201",planFlags)!.GetValue(null));
                Assert.Equal("string",plan.OutputTypes["tree_name"]);
                var targetSql=typeof(NonRedundantReadPlan).GetProperty("MySql",instanceFlags)!.GetValue(plan)!;
                var expression=Assert.IsType<string>(targetSql.GetType().GetMethod("Expression",instanceFlags)!.Invoke(targetSql,["tree_name"]));
                Assert.Contains("WITH RECURSIVE",expression);
                Assert.Contains("`delete_no` IS NULL",expression);
                var locking=typeof(MySqlNonRedundantReadPlan).Assembly.GetType("Metarill.Data.Queries.MySqlNonRedundantReadCompiler",true)!
                    .GetMethod("LockNestedReads",planFlags)!;
                var current=Assert.IsType<string>(locking.Invoke(null,[expression]));
                Assert.Contains("FOR SHARE) UNION ALL (SELECT ",current);
                Assert.Contains("FOR SHARE)) SELECT",current);
                Assert.Contains("LIMIT 1 FOR SHARE) END",current);
            }
            if(indirect)RuntimeHelpers.RunClassConstructor(assembly.GetType(ns+".Generated.Services.LabelJoinService",true)!.TypeHandle);
            Assert.NotNull(consumer.GetMethod("Import901Async"));
            Assert.NotNull(consumer.GetMethod("Export901Async"));
            if(globalBusinessSource)
            {
                Assert.NotNull(consumer.GetMethod("Import902Async"));
                Assert.NotNull(consumer.GetMethod("Export902Async"));
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
                var validate=consumer.GetMethod("ValidateActor",flags)!;
                if(!ordinaryBusinessSource||projectParentScope)
                {
                    var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>
                        validate.Invoke(null,[new RuntimeActor(7,"合成账号",false,"999","9")]));
                    Assert.IsType<UnauthorizedAccessException>(error.InnerException);
                }
                var metadata=GenerationMetadataPublisher.ForRelations(catalog);
                var code=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/LabelConsumerService.g.cs"));
                Assert.Contains("if (existing.Count > 1)",code);
                if(ordinaryBusinessSource&&!projectParentScope)Assert.Contains("projectId:actor.ProjectId",code);
                foreach(var id in new[]{"901","902"})
                {
                    var scope=metadata.ConfigPages.Single(page=>page.Id==id).WhereSql;
                    Assert.Contains("source_scope_id=@parentId",scope);
                    Assert.Equal(!onlyParent||parentAccount,scope.Contains("store_id=@current_store_id",StringComparison.Ordinal));
                }
            }
            if(mode==1)
            {
                var targets=MixedRelationPropagationCompilation.ForModel(model).MixedTargets;
                var physicalGlobal=globalBusinessSource||compoundTargetParent=="physical"&&globalRowProject is not null;
                Assert.Contains(physicalGlobal?"label_cross_source":selfTree?"label_cross_tree":"label_cross_country",targets);
                if(indirect)Assert.Contains("label_cross_source",targets);
                if(parent)Assert.Contains(logicalParent||globalParent?"label_cross_country":"label_scope_parent",targets);
                if(recursive||selectedPage)
                {
                    var target=physicalGlobal?"label_cross_source":recursive?"label_scope_zone":selfTree?"label_cross_tree":"label_cross_country";
                    Assert.Contains(target,targets);
                    var runtime=assembly.GetType(ns+".Generated.GeneratedRelationMaintenance",true)!;
                    var published=Assert.IsType<Metarill.Data.Relations.RelationMaintenanceCatalog>(runtime.GetProperty("Catalog")!.GetValue(null));
                    Assert.Contains(target,published.MixedTargets);
                    if(physicalGlobal)
                    {
                        const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                        var plans=Assert.IsAssignableFrom<System.Collections.IDictionary>(published.GetType().GetProperty("Plans",hidden)!.GetValue(published));
                        var plan=plans["9"]!;
                        var read=plan.GetType().GetProperty("Read")!.GetValue(plan)!;
                        var sql=Assert.IsType<string>(read.GetType().GetMethod("Expression",hidden)!.Invoke(read,["selected_names"]));
                        if(compoundTargetParent=="physical")Assert.Contains("`other_country_id`",sql);
                        if(globalBusinessSource)
                        {
                            Assert.Contains("`source_scope_id`",sql);
                            var scope=Assert.IsType<string>(read.GetType().GetProperty("Scope",hidden)!.GetValue(read));
                            Assert.Contains("`yz_project_id`",scope);
                        }
                        Assert.Contains("`"+parentField+"`",sql);
                        if(!onlyParent||parentAccount)Assert.Contains("`store_id`",sql);
                        else Assert.DoesNotContain("`store_id`",sql);
                        if(projectParentScope)Assert.Contains("multi_value.`yz_project_id`="+(textDictionaryProject?"@project_scope_text":"@project_scope_number"),sql);
                        if(literalParent)
                        {
                            var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(
                                read.GetType().GetField("constants",hidden)!.GetValue(read));
                            var parameter=Assert.Single(parameters,pair=>Equals(pair.Value,"排除目标页名称"));
                            Assert.Contains(parameter.Key,sql);
                        }
                    }
                }
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>两个来源父键相等后，实际目标的两个字段仍各自参与绑定。</summary>
    private static void AssertBothPhysicalSourceFields(string expression)
    {
        Assert.Contains("BINARY CAST(b.`source_scope_id`",expression);
        Assert.Contains("BINARY CAST(b.`other_country_id`",expression);
        Assert.Contains("multi_value.`target_scope_id`",expression);
        Assert.Contains("multi_value.`other_country_id`",expression);
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
