using System.Reflection;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using System.Text.Json;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1父键是实际存储；全局目标声明及共享同伴证明不能改变其页面归属语义。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SharedRedundantParentGenerationTests
{
    /// <summary>四键、长短分支及所有有效普通／全局组合共192目录，逐个父页证明实际字段。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void All_parent_pages_preserve_physical_r1_identity(string type,string parent,bool shorter)
    {
        for(var mask=0;mask<16;mask++){if(shorter&&(mask&4)!=0)continue;foreach(var noProject in new[]{false,true})
        {
            var catalog=Catalog("r1_parent",type,parent,shorter,mask,noProject);var original=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.SharedR1Parent");var table=model.Tables.Single(table=>table.Source.Id=="2");foreach(var page in table.Pages.Where(page=>page.Source.Id!="201")){SharedRedundantParentCompilation.Require(model,table,page);Assert.Null(RedundantCrossTableSubset.Reason(model,table,page));foreach(var scope in page.Scopes.Where(scope=>scope.Source=="parent"))Assert.True(Metarill.Core.Metadata.MetadataStoragePolicy.ForTable(catalog,table.Source).IsPhysical(scope.Column.Source));}Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Equal(original,JsonSerializer.Serialize(catalog));
        }}
    }
    /// <summary>实际完整包保留原Vue与手写partial，普通和全局父页均由同一生成器产生。</summary>
    [Theory][InlineData("string","string",false,0,false)][InlineData("string","int64",true,2,true)][InlineData("int64","string",false,1,true)][InlineData("int64","int64",false,15,true)]
    public async Task Full_original_packages_build_and_repeat_without_overwriting_partial(string type,string parent,bool shorter,int mask,bool noProject)
    {
        var catalog=Catalog("shared_parent",type,parent,shorter,mask,noProject);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-r1-parent");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");try
        {
            new ApplicationPackager().Create(catalog,output,"Example.SharedR1Parent",root);var hand=Path.Combine(output,"backend/generated/Services/SharedParentSourceService.cs");await File.AppendAllTextAsync(hand,"\n// R1父范围手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(catalog,candidate,"Example.SharedR1Parent",root);var updater=new LocalGenerationUpdater();var operations=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,operations);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));Assert.Equal("unchanged",updater.Apply(updater.Preview(output,candidate,operations),updater.Preview(output,candidate,operations).PlanSha256).Status);foreach(var command in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,command);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossParentVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());await NonRedundantApplicationFixture.RunAsync(probe);
        }finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>坏同伴、虚拟全局 R0 名称、动态目标上下文、未发布来源、物理父错页／覆盖及未知精确字段不能发布。</summary>
    [Theory][InlineData("badPeer")][InlineData("globalVirtual")][InlineData("variable")][InlineData("unpublished")][InlineData("singleWrongParentPage")][InlineData("physicalPage")][InlineData("physicalOverlay")][InlineData("unknownExact")][InlineData("cacheParent")][InlineData("selfParent")]
    public void Unproven_r1_parent_never_publishes_full_package(string kind)
    {
        var catalog=Catalog("r1_parent","string","int64",false,kind is "cacheParent" or "selfParent"?0:1,true);if(kind=="cacheParent"){catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql=catalog.ConfigPages.Single(page=>page.Id=="202").WhereSql.Replace("country_id=@parent_id","country_name=@parent_id",StringComparison.Ordinal);}if(kind=="selfParent"){var field=catalog.ConfigColumns.Single(column=>column.Id=="91");field.LinkConfigTableId="2";field.ControlDetail="{\"tableId\":\"2\",\"nameField\":\"name\"}";field.Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("201");}if(kind=="badPeer")catalog.ConfigColumns.Single(column=>column.Id=="80").ControlDetail="{}";if(kind=="globalVirtual"){catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;catalog.ConfigColumns.Single(column=>column.ConfigTableId=="3"&&column.ColumnName=="name").IsVirtualColumn=1;}if(kind=="variable"){catalog.ConfigColumns.Add(new(){Id="399",YzProjectId=catalog.YzProjectId,ConfigTableId="3",ColumnName="yz_project_id",LogicalType="int64"});catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="yz_project_id=@current_yz_project_id";}if(kind=="unpublished")catalog.ConfigTables.Single(table=>table.Id=="2").YzProjectId="";if(kind=="singleWrongParentPage"){catalog.ConfigColumns.Single(column=>column.Id=="91").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("401");catalog.ConfigColumns.RemoveAll(column=>column.Id=="80");catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigColumnId=="80"||field.ConfigPageId=="205");catalog.ConfigPages.RemoveAll(page=>page.Id=="205");catalog.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="205");Metarill.Core.Metadata.MetadataValidation.Validate(catalog);}if(kind=="physicalPage")catalog.ConfigColumns.Single(column=>column.Id=="91").Extra["linkConfigPageId"]=JsonSerializer.SerializeToElement("401");if(kind=="physicalOverlay")catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="206"&&field.ConfigColumnId=="91").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="unknownExact")catalog.ConfigPages.Single(page=>page.Id=="202").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("missing_scope");var output=Path.Combine(Path.GetTempPath(),"metarill-shared-r1-parent-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(catalog,output,"Example.RejectedR1Parent",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>带显式关联的缓存名称也不是父身份，局部证明必须独立拒绝，不能等后续缓存传播门槛拦截。</summary>
    [Fact]public void Managed_name_with_explicit_link_is_rejected_by_parent_proof()
    {
        var catalog=Catalog("r1_name_parent","string","int64",false,0,false);var name=catalog.ConfigColumns.SingleOrDefault(column=>column.ConfigTableId=="2"&&column.ColumnName=="country_name");if(name is null){name=new(){Id="1099",YzProjectId=catalog.YzProjectId,ConfigTableId="2",ColumnName="country_name",LogicalType="string"};catalog.ConfigColumns.Add(name);}name.LinkConfigTableId="3";var source=catalog.ConfigPages.Single(page=>page.Id=="202");source.WhereSql=source.WhereSql.Replace("country_id=@parent_id","country_name=@parent_id",StringComparison.Ordinal);var model=GenerationModel.Create(catalog,"Example.R1NameParent");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=table.Pages.Single(page=>page.Source.Id=="202");Assert.True(table.Columns.Single(column=>column.Source.Id==name.Id).IsManagedCascadeName);Assert.Throws<NotSupportedException>(()=>SharedRedundantParentCompilation.Require(model,table,page));
    }
    /// <summary>文件完整用途必须同时通过生成能力与公共运行时固定元数据证明。</summary>
    [Fact]public void File_parent_full_use_requires_frozen_common_plan()
    {
        var catalog=Catalog("shared_parent","string","int64",false,1,true,true);var model=GenerationModel.Create(catalog,"Example.SharedR1ParentFiles");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Single(new Metarill.Runtime.Files.FileMetadataContext([catalog]).Catalogs);
    }

    /// <summary>原业务查询及文件范围逐级执行账号条件，并以来源实际存储父键为准。</summary>
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,true)]
    public async Task Account_parent_scope_is_fixed_in_business_and_file_plans(bool shared,bool global)
    {
        const string ns="Example.AccountR1Parent";
        var catalog=AccountCatalog(shared,global);
        var model=GenerationModel.Create(catalog,ns);
        Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var source=model.Tables.Single(table=>table.Source.Id=="2");
        const string pageId="204";
        var page=source.Pages.Single(page=>page.Source.Id==pageId);
        Assert.True(SharedRedundantParentCompilation.HasAccountPath(model,source,page));
        var files=new Metarill.Runtime.Files.FileMetadataContext([catalog]);
        Assert.Single(files.Catalogs);
        var fileParent=Assert.Single(Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2",pageId)).Value;
        Assert.Equal("place_id",fileParent.ColumnName);
        Assert.Equal(3,fileParent.Levels.Count);
        Assert.Equal(2,fileParent.Levels.Count(level=>level.Condition?.Variables.Count>0));

        var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-account-parent");
        var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entity=assembly.GetType(ns+".Generated.Entities.SharedParentSource",true)!;
            var serviceType=assembly.GetType(ns+".Generated.Services.SharedParentSourceService",true)!;
            using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused"});
            var mapper=Activator.CreateInstance(assembly.GetType(ns+".Generated.Mappers.SharedParentSourceMapper",true)!,[session]);
            var args=new object?[serviceType.GetConstructors().Single().GetParameters().Length];
            args[0]=session;args[1]=mapper;args[2]=new SnowflakeIdGenerator(864);args[3]=Array.Empty<IRecordMutationObserver>();
            var service=Activator.CreateInstance(serviceType,args)!;
            var scope=serviceType.GetMethod("Scope"+pageId,BindingFlags.Instance|BindingFlags.NonPublic)!;
            var actor=new RuntimeActor(1,"测试账号",false,"12","5"){StoreId=9007199254740993L};
            object Query(RuntimeActor identity,bool locking=false)=>scope.Invoke(service,[identity,"9007199254742000",locking])!;
            var sql=(string)GetType().GetMethod(nameof(Sql),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(entity).Invoke(null,[Query(actor)])!;
            sql=sql.Replace("\u0060","",StringComparison.Ordinal);
            Assert.Contains("EXISTS (SELECT 1 FROM shared_parent_place rp2",sql);
            Assert.Contains("BINARY CAST(rp2.id AS CHAR)",sql);
            Assert.Contains("BINARY CAST(a.place_id AS CHAR)",sql);
            Assert.Contains("INNER JOIN shared_parent_area rp1",sql);
            Assert.Contains("INNER JOIN shared_parent_country rp0",sql);
            Assert.Contains("rp0.store_id",sql);
            Assert.Contains("rp1.store_id",sql);
            Assert.Equal(!global,sql.Contains("rp0.yz_project_id=",StringComparison.Ordinal));
            Assert.DoesNotContain(actor.StoreId!.Value.ToString(),sql);
            Assert.IsType<UnauthorizedAccessException>(Assert.Throws<TargetInvocationException>(()=>Query(actor with{StoreId=null,IsAdministrator=true})).InnerException);
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Query(actor,true)).InnerException);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }


    /// <summary>整数候选、原 Excel 查找和回读从同一固定目标页取得冗余父范围。</summary>
    [Theory]
    [InlineData(false,false,"int32","List",false)]
    [InlineData(true,true,"int64","List",false)]
    [InlineData(true,true,"int64","SelectPage",false)]
    [InlineData(false,false,"int64","List",true)]
    public async Task Fixed_integer_candidate_compiles_scoped_reads_and_excel(bool shared,bool global,string logicalType,
        string control,bool cachedConsumer)
    {
        const string ns="Example.R1ParentCandidate";
        var catalog=AccountCatalog(shared,global);
        var targetPage=catalog.ConfigPages.Single(page=>page.Id=="204");
        if(control=="SelectPage")targetPage.Extra["type"]=JsonSerializer.SerializeToElement(1);
        catalog.ConfigColumns.Add(new(){Id="9010",ConfigTableId="2",ColumnName="code",
            Name="业务编号",LogicalType=logicalType,YzProjectId=catalog.YzProjectId});
        catalog.ConfigPageColumns.Add(new(){Id="9011",ConfigPageId=targetPage.Id,ConfigColumnId="9010",
            IsShow=1,YzProjectId=catalog.YzProjectId,Extra=new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}});
        catalog.ConfigTables.Add(new(){Id="90",TableName="candidate_consumer",IsRongyu=cachedConsumer?1:0,
            YzProjectId=catalog.YzProjectId});
        catalog.ConfigPages.Add(new(){Id="900",ConfigTableId="90",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
            WhereSql="yz_project_id=@current_yz_project_id",IsNew=1,IsSave=1,IsImport=1,IsExport=1});
        catalog.ConfigMenus.Add(new(){Id="90900",YzProjectId=catalog.YzProjectId,YzAdminTypeId="5",
            Name="候选使用方",Kind="GENERATED_PAGE",ConfigPageId="900"});
        var controlDefinition=JsonSerializer.Serialize(new{tableId="2",pageId="204",nameField="code"});
        catalog.ConfigColumns.AddRange([
            new(){Id="9021",ConfigTableId="90",ColumnName="id",LogicalType="int64",IsDbNotNull=1},
            new(){Id="9022",ConfigTableId="90",ColumnName="yz_project_id",LogicalType="int64"},
            new(){Id="9023",ConfigTableId="90",ColumnName="lookup_place_id",Name="归属地",LogicalType="int64",LinkConfigTableId="5"},
            new(){Id="9024",ConfigTableId="90",ColumnName="lookup_target_id",Name="候选记录",LogicalType="int64",
                ControlKind=control,ControlDetail=controlDefinition,LinkConfigTableId="2",
                Extra=new(){["linkConfigPageId"]=JsonSerializer.SerializeToElement("204")}},
            new(){Id="9025",ConfigTableId="90",ColumnName="lookup_target_code",Name="候选编号",LogicalType=logicalType,
                Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement("lookup_target.code")}}
        ]);
        foreach(var id in new[]{"9023","9024","9025"})
        {
            catalog.ConfigColumns.Single(column=>column.Id==id).YzProjectId=catalog.YzProjectId;
            catalog.ConfigPageColumns.Add(new(){Id="900"+id,ConfigPageId="900",ConfigColumnId=id,IsShow=1,
                IsEdit=id=="9025"?0:1,YzProjectId=catalog.YzProjectId});
        }

        var directory=Path.Combine(Path.GetTempPath(),"metarill-r1-account-candidate",Guid.NewGuid().ToString("N"));
        try
        {
            var output=Path.Combine(directory,"generated");
            new CSharpGenerator().Generate(catalog,output,ns);
            var service=await File.ReadAllTextAsync(Path.Combine(output,"Services/CandidateConsumerService.g.cs"));
            Assert.Contains("DerivedOptionsPlan900_9024.BindAccount(actor)",service);
            Assert.Contains("RequireParentAsync(session,OptionsAccountParentScope900_9024",service);
            Assert.Contains("FixedReadComparison.IntegerLabelContains",service);
            Assert.Contains("ImportRelation900_9024Async",service);
            Assert.Contains("DerivedOptionsRows900_9024Async(request.ParentId,groupLookupTargetId.Key",service);
            Assert.Contains("targetParent.ToString(),actor,cancellationToken,locking:true",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var serviceType=assembly.GetType(ns+".Generated.Services.CandidateConsumerService",true)!;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(serviceType.TypeHandle);
            var plan=(Metarill.Data.Queries.NonRedundantReadPlan)serviceType.GetField("DerivedOptionsPlan900_9024",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var paths=(Metarill.Data.Queries.RedundantSharedParentPlan[])serviceType.GetField("OptionsAccountParentScope900_9024",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            await AccountParentCandidateProbe.VerifyAsync(plan,paths,catalog.YzProjectId,global,logicalType);
            Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,ns)));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,Path.GetDirectoryName(directory)!);}
    }

    /// <summary>保留原父链和附件配置，为全部来源页提供相同的账号归属。</summary>
    private static Metarill.Core.Metadata.MetadataCatalog AccountCatalog(bool shared,bool global)
    {
        var catalog=Catalog("shared_parent","string","int64",false,global?1:0,true,true);
        catalog.ConfigPages.RemoveAll(page=>page.Id=="206"||!shared&&page.Id=="205");
        catalog.ConfigMenus.RemoveAll(menu=>menu.ConfigPageId=="206"||!shared&&menu.ConfigPageId=="205");
        catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="206"||field.ConfigColumnId=="91"||
            !shared&&(field.ConfigPageId=="205"||field.ConfigColumnId=="80"));
        catalog.ConfigColumns.RemoveAll(column=>column.Id=="91"||!shared&&column.Id=="80");
        foreach(var configuredPage in catalog.ConfigPages.Where(page=>page.ConfigTableId=="2"||page.Id is "301" or "401"))
            configuredPage.WhereSql+=" AND store_id=@current_store_id";
        foreach(var table in new[]{"2","3","4"})
            catalog.ConfigColumns.Add(new(){Id=table+"991",ConfigTableId=table,YzProjectId=catalog.YzProjectId,
                ColumnName="store_id",LogicalType=table=="4"?"string":"int64"});
        return catalog;
    }

    private static string Sql<T>(object query) where T:class=>((FreeSql.ISelect<T>)query).ToSql();

    /// <summary>同一原目录只改变来源存储声明，来源页、字段、菜单及目标链全部沿用原工厂。</summary>
    internal static Metarill.Core.Metadata.MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter,int mask,bool noProject,bool files=false)
    {
        var catalog=GlobalSharedParentGenerationTests.Catalog(prefix,type,parent,shorter,mask,noProject,files);catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=1;return catalog;
    }
}
