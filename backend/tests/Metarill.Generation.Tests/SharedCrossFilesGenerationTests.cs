using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Data.Queries;
using Metarill.Core.Metadata;
using Metarill.Runtime.Files;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享父页面的原文件字段与完整源码包共用正式文件运行时，不手改生成代码。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedCrossFilesGenerationTests
{
    /// <summary>原共享父模型只增加真实FileLibrary列；物理删除仍走原元数据开关。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter=false,bool derived=false,bool physicalDelete=false)
    {
        var c=SharedCrossParentGenerationTests.Catalog(prefix,type,parent,shorter,derived);c.ConfigColumns.Add(new(){Id="9001",YzProjectId=c.YzProjectId,ConfigTableId="2",ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});foreach(var page in c.ConfigPages.Where(p=>p.ConfigTableId=="2"))c.ConfigPageColumns.Add(new(){Id=page.Id+"9001",YzProjectId=c.YzProjectId,ConfigPageId=page.Id,ConfigColumnId="9001",IsShow=1,IsEdit=1});if(physicalDelete){var deleted=c.ConfigColumns.Single(x=>x.ConfigTableId=="2"&&x.ColumnName=="delete_no");c.ConfigColumns.Remove(deleted);c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId==deleted.Id);}return c;
    }
    /// <summary>共享父子页可同时用独立级联或自树叶名做数据授权，完整包发布原附件范围。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false,true)]
    [InlineData(false,true,1)]
    [InlineData(false,true,2)]
    [InlineData(false,true,0,true)]
    public async Task Full_shared_file_independent_exact_path_builds(bool selfTree,bool textKey=false,int physicalParents=0,bool globalRoot=false)
    {
        var catalog=Catalog("shared_parent",textKey?"string":"int64",textKey?"string":"int64");
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id==(globalRoot?"206":"202"));
        if(globalRoot)
            GlobalSharedCrossGenerationTests.ApplyGlobals(catalog,mask:1,noProject:true);
        if(physicalParents>0)
        {
            sourcePage.WhereSql="yz_project_id=@current_yz_project_id AND owner_country_id=@parent_id";
            if(physicalParents==2)
            {
                catalog.ConfigColumns.Add(new(){Id="9506",YzProjectId=catalog.YzProjectId,ConfigTableId="2",
                    ColumnName="billing_country_id",LogicalType="int64",LinkConfigTableId="3"});
                sourcePage.WhereSql+=" AND billing_country_id=@parent_id";
            }
        }
        var cascade=catalog.ConfigColumns.Single(column=>column.Id=="25");
        var definition=JsonNode.Parse(cascade.ControlDetail)!;
        var targetId=cascade.LinkConfigTableId;
        var pathColumn="isolated_place_id";
        var match="isolatedCountryName";
        if(selfTree)
        {
            targetId="3";
            pathColumn="tree_id";
            match="treeName";
            catalog.ConfigColumns.Add(new(){Id="9502",YzProjectId=catalog.YzProjectId,ConfigTableId=targetId,
                ColumnName="upstream_id",LogicalType="int64",LinkConfigTableId=targetId});
            catalog.ConfigPageColumns.Add(new(){Id="3019502",YzProjectId=catalog.YzProjectId,
                ConfigPageId="301",ConfigColumnId="9502",IsShow=1});
            definition=JsonSerializer.SerializeToNode(new{sourceMode="selfTree",selfTree=new
            {
                table="shared_parent_country",idField="id",nameField="name",parentField="upstream_id",rootMode="blank"
            }})!;
        }
        else
        {
            foreach(var level in definition["levels"]!.AsArray())
                level!["columnPrefix"]="isolated_"+level["columnPrefix"]!.GetValue<string>();
            if(textKey)
            {
                match="isolatedCountryId";
                definition["levels"]![0]!["idField"]="business_key";
                catalog.ConfigColumns.Add(new(){Id="9503",YzProjectId=catalog.YzProjectId,ConfigTableId="3",
                    ColumnName="business_key",LogicalType="string",ColumnLength="50",IsDbNotNull=1});
                catalog.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id="9504",configTableId="3",isUnique=1}));
                catalog.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id="9505",configIndexId="9504",configColumnId="9503"}));
            }
        }
        catalog.ConfigColumns.Add(new(){Id="9501",YzProjectId=catalog.YzProjectId,ConfigTableId="2",ColumnName=pathColumn,
            LogicalType=textKey?"string":"int64",LinkConfigTableId=targetId,ControlKind="CascadeTree",ControlDetail=definition.ToJsonString()});
        catalog.ConfigPageColumns.Add(new(){Id=sourcePage.Id+"9501",YzProjectId=catalog.YzProjectId,
            ConfigPageId=sourcePage.Id,ConfigColumnId="9501",IsShow=1,IsEdit=1});
        sourcePage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement(match);
        catalog.YzAdminTypes.Single(item=>item.Id=="5").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("realName");

        if(textKey)
        {
            var model=GenerationModel.Create(catalog,"Example.SharedFileExact");
            var source=model.Tables.Single(item=>item.Source.Id==sourcePage.ConfigTableId);
            var control=source.Columns.Single(item=>item.Source.Id=="9501");
            foreach(var page in source.Pages)
            {
                var proof=CrossTableWriteCompilation.Resolve(model,source,page,control);
                Assert.True(proof.Definition is not null,page.Source.Id+" "+proof.Reason);
            }
        }

        string[] storage=physicalParents switch
        {
            1=>["owner_country_id"],
            2=>["owner_country_id","billing_country_id"],
            _=>globalRoot?["country_id","owner_country_id"]:["country_id"]
        };
        await AssertFullSharedFileExactPlan(catalog,sourcePage.Id,
            selfTree?"tree_name":textKey?"isolated_country_id":"isolated_country_name",false,storage);
    }

    /// <summary>普通、物理父键及逻辑父级的完整应用均按当前发布页编译附件范围。</summary>
    [Theory]
    [InlineData("shared",false)]
    [InlineData("compound",false)]
    [InlineData("unparented",false)]
    [InlineData("unparented-condition",false)]
    [InlineData("unparented-condition-stored",false)]
    [InlineData("physical",false)]
    [InlineData("physical-condition",false)]
    [InlineData("physical-condition-stored",true)]
    [InlineData("physical-condition-compound",false)]
    [InlineData("shared",true)]
    [InlineData("physical",true)]
    public async Task Full_file_joined_exact_path_builds(string parentMode,bool textForeignKey)
    {
        var catalog=Catalog("shared_parent","int64","int64");
        var project=catalog.YzProjectId;
        var sourcePage=catalog.ConfigPages.Single(page=>page.Id=="202");
        catalog.ConfigTables.Add(new(){Id="950",YzProjectId=project,TableName="shared_parent_branch",IsRongyu=parentMode.EndsWith("-stored",StringComparison.Ordinal)?1:0});
        catalog.ConfigColumns.AddRange([
            new(){Id="951",YzProjectId=project,ConfigTableId="950",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="952",YzProjectId=project,ConfigTableId="950",ColumnName="name",LogicalType="string"},
            new(){Id="953",YzProjectId=project,ConfigTableId="950",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="954",YzProjectId=project,ConfigTableId="950",ColumnName="store_id",LogicalType="int64"},
            new(){Id="955",YzProjectId=project,ConfigTableId="2",ColumnName="branch_id",LogicalType=textForeignKey?"string":"int64",
                LinkConfigTableId="950",ControlKind="List",ControlDetail="""{"tableId":"950","nameField":"name"}"""},
            new(){Id="956",YzProjectId=project,ConfigTableId="2",ColumnName="branch_name",LogicalType="string",
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("branch.name")}}
        ]);
        catalog.ConfigPages.Add(new(){Id="959",YzProjectId=project,ConfigTableId="950",YzAdminTypeId="5",
            WhereSql="yz_project_id=@current_yz_project_id AND store_id=@current_store_id"});
        catalog.ConfigPageColumns.Add(new(){Id="959952",YzProjectId=project,ConfigPageId="959",ConfigColumnId="952",IsShow=1});
        catalog.ConfigPageColumns.Add(new(){Id="202955",YzProjectId=project,ConfigPageId="202",ConfigColumnId="955",IsShow=1,IsEdit=1});
        catalog.ConfigMenus.Add(new(){Id="700959",YzProjectId=project,YzAdminTypeId="5",Name="分组",
            Kind="GENERATED_PAGE",ConfigPageId="959"});
        sourcePage.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("branchName");
        catalog.YzAdminTypes.Single(item=>item.Id=="5").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("realName");
        if(parentMode=="compound")
        {
            var shared=catalog.ConfigColumns.Single(column=>column.Id=="25");
            var path=JsonNode.Parse(shared.ControlDetail)!;
            foreach(var level in path["levels"]!.AsArray())
                level!["columnPrefix"]="parent_"+level["columnPrefix"]!.GetValue<string>();
            catalog.ConfigColumns.Add(new(){Id="957",YzProjectId=project,ConfigTableId="2",ColumnName="parent_place_id",
                LogicalType="int64",LinkConfigTableId=shared.LinkConfigTableId,ControlKind="CascadeTree",ControlDetail=path.ToJsonString()});
            catalog.ConfigPageColumns.Add(new(){Id="202957",YzProjectId=project,ConfigPageId=sourcePage.Id,
                ConfigColumnId="957",IsShow=1,IsEdit=1});
            sourcePage.WhereSql+=" AND parent_country_id=@parent_id";
        }
        var physicalParent=parentMode.StartsWith("physical",StringComparison.Ordinal);
        if(physicalParent||parentMode.StartsWith("unparented",StringComparison.Ordinal))
        {
            sourcePage.WhereSql="yz_project_id=@current_yz_project_id";
            if(physicalParent)
            {
                sourcePage.WhereSql+=" AND owner_country_id=@parent_id";
                if(parentMode=="physical-condition-stored")
                    catalog.ConfigColumns.Single(column=>column.Id=="91").LogicalType="string";
                if(parentMode=="physical-condition-compound")
                {
                    catalog.ConfigColumns.Add(new(){Id="960",YzProjectId=project,ConfigTableId="2",ColumnName="alternate_country_id",
                        LogicalType="string",LinkConfigTableId="3"});
                    sourcePage.WhereSql+=" AND alternate_country_id=@parentId";
                }
            }
            if(parentMode.Contains("-condition",StringComparison.Ordinal))
                sourcePage.WhereSql+=" AND branch_name <> '隐藏分组'";
        }
        var requiredStorage=parentMode switch
        {
            "compound"=>new[]{"country_id","parent_country_id","parent_area_id","parent_place_id"},
            "physical-condition-compound"=>["owner_country_id","alternate_country_id"],
            _ when physicalParent=>["owner_country_id"],
            "shared"=>["country_id"],
            _=>Array.Empty<string>()
        };
        await AssertFullSharedFileExactPlan(catalog,sourcePage.Id,"branch_name",true,requiredStorage);
    }

    /// <summary>从正式源码包的发布目录查询窄投影，避免以生成前元数据冒充完整包权限。</summary>
    private static async Task AssertFullSharedFileExactPlan(MetadataCatalog catalog,string sourcePageId,string matchColumn,bool requiresAccount,params string[] additionalStorage)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-files-exact");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,"Example.SharedFileExact",NonRedundantApplicationFixture.Root());
            foreach(var operation in new[]{"restore","build"})
                await NonRedundantApplicationFixture.BuildAsync(directory,operation);
            var modelPage=catalog.ConfigPages.Single(item=>item.Id==sourcePageId);
            if(modelPage.WhereSql.Contains("branch_name",StringComparison.Ordinal))
            {
                var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/SharedParentSourceService.g.cs"));
                Assert.Contains("CrossConditionPlan"+sourcePageId,service);
                Assert.Contains("NonRedundantCrossTableScopeSql.ApplyCondition",service);
                foreach(var parent in new[]{"owner_country_id","alternate_country_id"}.Where(field=>
                    modelPage.WhereSql.Contains(field+"=@parent",StringComparison.Ordinal)))
                {
                    Assert.Contains("result.Add(new(\""+parent+"\",FixedReadComparison.Equal,",service);
                    Assert.Contains("query=query.Where(entity=>entity."+GenerationModel.Pascal(parent)+"==",service);
                }
                var model=GenerationModel.Create(catalog,"Example.SharedFileExact");
                var source=model.Tables.Single(item=>item.Source.Id==modelPage.ConfigTableId);
                var pageModel=source.Pages.Single(item=>item.Source.Id==sourcePageId);
                var generatedRead=NonRedundantReadCompilation.ForPage(model,source,pageModel);
                Assert.True(generatedRead.RequiresAccount);
                var readCatalog=await TextCascadeParentRelationTests.PublishedReadCatalog(directory);
                var readPage=readCatalog.ConfigPages.Single(item=>item.Id==sourcePageId);
                Assert.Equal(modelPage.WhereSql,readPage.WhereSql);
                Assert.Contains("store_id=@current_store_id",readCatalog.ConfigPages.Single(item=>item.Id=="959").WhereSql);
                var list=NonRedundantReads.CompileProjectPage(readCatalog,source.Source.Id,["id"],["branch_name"],[],generatedRead.SingleTargets);
                Assert.True(list.RequiresAccount);
                var condition=pageModel.Condition!;
                var readTable=readCatalog.ConfigTables.Single(item=>item.Id==source.Source.Id);
                var scope=NonRedundantReads.CompilePageMembership(readCatalog,readTable,readPage,condition.Syntax.Fields,
                    condition.CrossFields,condition.SelfTreeFields,["id"],[],generatedRead.SingleTargets);
                Assert.True(scope.RequiresAccount);
            }
            var metadata=await File.ReadAllTextAsync(Path.Combine(directory,"backend/metadata/project.json"));
            var published=JsonSerializer.Deserialize<MetadataCatalog>(metadata,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var context=new FileMetadataContext([published]);
            var frozen=Assert.Single(context.Catalogs);
            var page=frozen.ConfigPages.Single(item=>item.Id==sourcePageId);
            var attachment=frozen.ConfigColumns.Single(item=>item.Id=="9001");
            var compiled=typeof(FileMetadataContext).GetMethod("DerivedExactPlan",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.NotNull(compiled);
            var plan=Assert.IsType<NonRedundantReadPlan>(compiled.Invoke(context,[frozen,page,attachment]));
            if(matchColumn=="isolated_country_id"&&additionalStorage.Contains("owner_country_id"))
            {
                var service=await File.ReadAllTextAsync(Path.Combine(directory,"backend/generated/Services/SharedParentSourceService.g.cs"));
                if(additionalStorage.Contains("country_id"))
                {
                    Assert.Contains("LogicalParentScope"+sourcePageId,service);
                    Assert.Contains("SharedGlobalParentKey"+sourcePageId,service);
                    Assert.Contains("owner_country_id",plan.OutputTypes.Keys);
                }
                else
                {
                    foreach(var parent in additionalStorage)
                    {
                        Assert.Contains("result.Add(new(\""+parent+"\",FixedReadComparison.Equal,long.Parse(request.ParentId!",service);
                        Assert.Contains("query=query.Where(entity=>entity."+GenerationModel.Pascal(parent)+"==long.Parse(parentId!",service);
                    }
                    Assert.DoesNotContain("country_id",plan.OutputTypes.Keys);
                }
                Assert.Contains("FixedReadFilter.ForCrossExact(\"isolated_country_id\"",service);
                Assert.Contains("ValidateParents"+sourcePageId+"Async",service);
            }
            Assert.All(additionalStorage,column=>Assert.Contains(column,plan.OutputTypes.Keys));
            Assert.Contains("attachments",plan.OutputTypes.Keys);
            Assert.DoesNotContain(matchColumn,plan.OutputTypes.Keys);
            Assert.Equal(requiresAccount,plan.RequiresAccount);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }


    /// <summary>普通、短主叶和派生目标三种Full实际更新、保护手写partial并核对原文件上下文／挂载级联组件。</summary>
    [Theory][InlineData(false,false,false)][InlineData(true,false,true)][InlineData(false,true,false)]
    public async Task Full_shared_file_packages_build_update_and_keep_original_controls(bool shorter,bool derived,bool physicalDelete)
    {
        var c=Catalog("shared_parent","string","int64",shorter,derived,physicalDelete);var root=NonRedundantApplicationFixture.Root();var context=new FileMetadataContext([c]);Assert.Single(context.Catalogs);var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-files-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");
        try{new ApplicationPackager().Create(c,output,"Example.SharedFileFull",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// 共享父范围文件手写业务保留。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.SharedFileFull",root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);foreach(var name in new[]{"yzp-cascade-tree","yzp-file-library"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/"+name+"/"+name+".vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/"+name+"/"+name+".vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossParentVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);var fileProbe=NonRedundantApplicationFixture.Start("node",output);fileProbe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossFilesVueProbe.mjs"));fileProbe.ArgumentList.Add(Path.Combine(output,"frontend"));await NonRedundantApplicationFixture.RunAsync(fileProbe);}finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
