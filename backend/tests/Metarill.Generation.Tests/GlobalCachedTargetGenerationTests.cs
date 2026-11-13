using System.Text.Json;
using System.Reflection;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局名称缓存目标必须形成可生成的候选、保存、导入导出和维护传播闭包，而不是仅放宽读计划。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedTargetGenerationTests
{
    /// <summary>普通R0/R1来源与两种外键都保留原List/单选、文件及名称字段，维护图不能遗失全局中间节点。</summary>
    [Theory][InlineData(0,"int64")][InlineData(0,"string")][InlineData(1,"int64")][InlineData(1,"string")]
    public void Cached_global_name_target_has_complete_generation_and_maintenance(int mode,string type)
    {
        var c=Catalog(mode,type);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalCachedTarget");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.Contains("global_target",maintenance.MixedTargets);if(mode==1)Assert.Contains("global_source",maintenance.MixedTargets);Assert.Equal(new[]{"global_target_name"},maintenance.MixedSourceColumns["global_source"]);
        if(mode==1)Assert.Equal(new[]{"global_source_global_target_name","selected_global_source_global_target_name"},maintenance.MixedSourceColumns["cached_consumer"].Order(StringComparer.Ordinal));
        else Assert.DoesNotContain("cached_consumer",maintenance.MixedSourceColumns.Keys);
        var source=model.Tables.Single(table=>table.Source.Id=="3");Assert.Contains(source.Pages[0].Writable,column=>column.Source.Id=="34");Assert.Contains(source.Pages[0].Writable,column=>column.Source.Id=="38");Assert.Contains(source.Pages[0].Writable,column=>column.Source.Id=="39");Assert.DoesNotContain(source.Pages[0].Writable,column=>column.Source.Id is "35" or "40");
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>实际完整包独立严格构建，两次生成和原更新器保持手写partial、原选择器/文件组件及生成输出。</summary>
    [Theory][InlineData(0,"string")][InlineData(1,"int64")]
    public async Task Cached_global_name_target_full_application_builds_preserves_custom_code_and_runs_original_vue(int mode,string type)
    {
        var c=Catalog(mode,type);var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cached-target");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"application");var repository=NonRedundantApplicationFixture.Root();
        try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.GlobalCachedTarget",repository).FrontendIncluded);
            var hand=Path.Combine(output,"backend/generated/Services/CachedConsumerService.cs");await File.WriteAllTextAsync(hand,"namespace Example.GlobalCachedTarget.Generated.Services;\npublic partial class CachedConsumerService { /// <summary>测试手写业务扩展，重复生成必须保留。</summary>\n public string PreservedBusinessRule()=>\"全局缓存目标手写扩展\"; }\n");var saved=await File.ReadAllBytesAsync(hand);
            var generated=Directory.GetFiles(Path.Combine(output,"backend/generated"),"*.g.cs",SearchOption.AllDirectories).ToDictionary(path=>Path.GetRelativePath(output,path),File.ReadAllBytes);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalCachedTarget",repository);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(saved,await File.ReadAllBytesAsync(hand));foreach(var pair in generated)Assert.Equal(pair.Value,await File.ReadAllBytesAsync(Path.Combine(output,pair.Key)));
            var repeated=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            var proof=Assert.Single(Directory.GetFiles(output,"GlobalPhysicalRelationValue.cs",SearchOption.AllDirectories));Assert.Contains("IsPublishedScalar",await File.ReadAllTextAsync(proof));
            foreach(var operation in new[]{"restore","build"})
            {
                var process=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",output);foreach(var argument in new[]{operation,"backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet","-p:Configuration=Release"})process.ArgumentList.Add(argument);if(operation=="build"){process.ArgumentList.Add("--no-restore");process.ArgumentList.Add("-warnaserror");}await NonRedundantApplicationFixture.RunAsync(process);
            }
            var node=NonRedundantApplicationFixture.Start("node",output);foreach(var argument in new[]{Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"GlobalCachedTargetVueProbe.mjs"),output})node.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(node);
            foreach(var component in new[]{"yzp-relation-select","yzp-file-library"})Assert.Equal((await File.ReadAllTextAsync(Path.Combine(repository,"frontend/metarill_uni_admin/components",component,component+".vue"))).Replace("\r\n","\n"),await File.ReadAllTextAsync(Path.Combine(output,"frontend/components",component,component+".vue")));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>元数据缺少全局证明、可读名称或真实维护路径时完整包仍拒绝，不能留下半成品。</summary>
    [Theory][InlineData("producerProject")][InlineData("privateTerminal")][InlineData("unpublishedPage")][InlineData("wrongCachePath")][InlineData("missingReadable")][InlineData("fileName")][InlineData("credentialTerminal")][InlineData("dynamicPage")]
    public void Invalid_cached_target_is_rejected_before_full_application_staging(string kind)
    {
        var c=Catalog(0,"int64");var cache=c.ConfigColumns.Single(column=>column.Id=="14");
        switch(kind)
        {
            case "producerProject":c.ConfigColumns.Add(new(){Id="18",ConfigTableId="1",YzProjectId="100",ColumnName="yz_project_id",LogicalType="int64"});break;
            case "privateTerminal":c.ConfigTables.Single(table=>table.Id=="2").Extra.Remove("scopeKind");break;
            case "unpublishedPage":c.ConfigPages.Single(page=>page.Id=="20").YzProjectId="101";break;
            case "wrongCachePath":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");break;
            case "missingReadable":c.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="10"&&field.ConfigColumnId=="13");break;
            case "fileName":cache.ControlKind="FileLibrary";cache.ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}";break;
            case "credentialTerminal":c.ConfigColumns.Single(column=>column.Id=="22").ColumnName="password";c.ConfigColumns.Single(column=>column.Id=="13").ControlDetail=c.ConfigColumns.Single(column=>column.Id=="13").ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"password\"",StringComparison.Ordinal);break;
            case "dynamicPage":c.ConfigPages.Single(page=>page.Id=="10").WhereSql="id=@current_yz_project_id";break;
        }
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cached-target-reject");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.GlobalCachedTarget",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>实际编译后的三个Service仍绑定全局维护目录；普通或超级身份不能换项目后读取或触发查询。</summary>
    [Theory][InlineData(0)][InlineData(1)]
    public async Task Actual_generated_services_keep_original_published_project_guard(int mode)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-cached-target-actor");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(mode,"int64"),Path.Combine(directory,"generated"),"Example.GlobalCachedTargetActor");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            foreach(var administrator in new[]{false,true})foreach(var (name,page) in new[]{("GlobalSource","10"),("GlobalTarget","20"),("CachedConsumer","30")})
            {
                var actor=new RuntimeActor(8,"发布范围观测",administrator,"101","");var mapper=Activator.CreateInstance(assembly.GetType("Example.GlobalCachedTargetActor.Generated.Mappers."+name+"Mapper",true)!,session)!;var constructor=assembly.GetType("Example.GlobalCachedTargetActor.Generated.Services."+name+"Service",true)!.GetConstructors().Single();
                var service=constructor.Invoke(constructor.GetParameters().Select(parameter=>parameter.ParameterType==typeof(DatabaseSession)?(object)session:parameter.ParameterType.IsInstanceOfType(mapper)?mapper:parameter.ParameterType==typeof(IIdGenerator)?new SnowflakeIdGenerator(909):parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>)?Array.Empty<IRecordMutationObserver>():parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>)?Array.Empty<IRecordMutationGuard>():null).ToArray());
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await(Task)service.GetType().GetMethod("Page"+page+"Async")!.Invoke(service,[new PageRequest(),actor,CancellationToken.None])!);
            }
            Assert.Null(session.CurrentTransaction);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>完整的三表配置；普通来源有项目列，全局缓存与物理终点都明确发布，缓存不成为可写控件。</summary>
    internal static MetadataCatalog Catalog(int mode,string type)
    {
        var c=new MetadataCatalog{YzProjectId="100",YzProjectName="全局缓存名称应用",YzAdminTypes=[new(){Id="9",Name="应用后台",YzProjectId="100"}]};
        foreach(var (id,name,label,redundant,global) in new[]{("1","global_source","全局缓存选项",1,true),("2","global_target","共享原始字典",1,true),("3","cached_consumer","消费记录",mode,false)})c.ConfigTables.Add(new(){Id=id,TableName=name,Name=label,YzProjectId="100",IsRongyu=redundant,Extra=global?new(){["scopeKind"]=JsonSerializer.SerializeToElement("global")}:[]});
        Add("11","1","id","ID","int64",key:true);Add("12","1","delete_no","删除标记","datetime");Add("13","1","global_target_id","上游字典","int64");Add("14","1","global_target_name","字典名称","string");Add("15","1","status","状态","int32");
        Add("21","2","id","ID","int64",key:true);Add("22","2","name","名称","string");Add("23","2","delete_no","删除标记","datetime");Add("24","2","status","状态","int32");
        Add("31","3","id","ID","int64",key:true);Add("32","3","yz_project_id","项目","int64");Add("33","3","delete_no","删除标记","datetime");Add("34","3","global_source_id","原下拉",type);
        Add("35","3","global_source_global_target_name","下拉名称","string");Add("36","3","name","名称","string");Add("38","3","selected_global_source_id","原单选","int64");Add("39","3","attachments","附件","string");Add("40","3","selected_global_source_global_target_name","单选名称","string");
        foreach(var (id,table,title) in new[]{("10","1","全局缓存选项"),("20","2","共享字典"),("30","3","消费记录")})c.ConfigPages.Add(new(){Id=id,ConfigTableId=table,Name=title,YzProjectId="100",YzAdminTypeId="9",IsCopy=1,IsImport=1,IsExport=1,WhereSql=table=="3"?"yz_project_id=@current_yz_project_id":"status=1",Extra=new(){["type"]=JsonSerializer.SerializeToElement(table=="3"?0:1)}});
        Relation("13","List","2","20","name");Relation("34","List","1","10","global_target_name");Relation("38","SelectPage","1","10","global_target_name");Join("14","global_target.name");Join("35","global_source.global_target_name");Join("40","selected_global_source.global_target_name");
        var file=c.ConfigColumns.Single(column=>column.Id=="39");file.ColumnLength="max";file.ControlKind="FileLibrary";file.ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}";
        foreach(var page in c.ConfigPages)foreach(var column in c.ConfigColumns.Where(column=>column.ConfigTableId==page.ConfigTableId&&column.ColumnName is not("id" or "yz_project_id" or "delete_no")&&column.Id!="14"))c.ConfigPageColumns.Add(new(){Id=page.Id+column.Id,ConfigPageId=page.Id,ConfigColumnId=column.Id,YzProjectId="100",IsShow=1,IsEdit=column.Id is "35" or "40"?0:1,Extra=column.Id is "35" or "40"?new(){["isSearch"]=JsonSerializer.SerializeToElement(1)}:[]});
        foreach(var page in c.ConfigPages)c.ConfigMenus.Add(new(){Id="700"+page.Id,YzProjectId="100",YzAdminTypeId="9",Name=page.Name,Kind="GENERATED_PAGE",ConfigPageId=page.Id});
        c.ConfigButtons=[JsonSerializer.SerializeToElement(new{id="8001",name="维护表",configPageId="30",configTableId="3",yzProjectId="100",actionType=1,type=0,sql="/admin/sys/system_maintenance/tables",visibleScheme="Normal",place="List",saveOption="NotSave",afterAction="Refresh"}),JsonSerializer.SerializeToElement(new{id="8002",name="回填名称",configPageId="30",configTableId="3",yzProjectId="100",actionType=1,type=0,sql="/admin/sys/system_maintenance/backfill_join_columns",visibleScheme="Normal",place="List",saveOption="NotSave",afterAction="Refresh"})];return c;
        /// <summary>只定义验收元数据，实体、查询和CRUD仍由原生成器创建。</summary>
        void Add(string id,string table,string name,string label,string logical,bool key=false)=>c.ConfigColumns.Add(new(){Id=id,ConfigTableId=table,YzProjectId="100",ColumnName=name,Name=label,LogicalType=logical,ColumnLength=logical=="string"?"100":"",IsDbNotNull=key?1:0});
        /// <summary>显示与候选明确指向同一真实页面，不接受按名称猜测目标。</summary>
        void Relation(string id,string kind,string table,string page,string name){var column=c.ConfigColumns.Single(column=>column.Id==id);column.ControlKind=kind;column.LinkConfigTableId=table;column.ControlDetail=JsonSerializer.Serialize(new{tableId=table,table=table=="1"?"global_source":"global_target",pageId=page,nameField=name,buttonText="选择记录"});}
        /// <summary>名称只作联接/缓存，不成为客户端任意写入的副本。</summary>
        void Join(string id,string path){var column=c.ConfigColumns.Single(column=>column.Id==id);column.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);column.Extra["columnFullName"]=JsonSerializer.SerializeToElement(path);}
    }
}
