using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Contracts;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Files;
using Metarill.Data;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>固定原V8完整样板证明无项目列来源，不移除文件、主子页、按钮或名称副本。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalCachedSourceGenerationTests
{
    /// <summary>全目录形成维护和反向传播，隐式名称仅加入原响应，不添加额外控件。</summary>
    [Fact]
    public void Original_sample_compiles_complete_cache_and_page_model_without_mutation()
    {
        var c=Catalog();var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.GlobalSource");Assert.Empty(GenerationCapabilityAudit.Collect(model));
        Assert.Equal(3,model.Tables.Count);Assert.Equal(46,c.ConfigColumns.Count);Assert.Equal(3,c.ConfigButtons.Count);Assert.Single(c.ConfigChildren);
        foreach(var table in model.Tables.Where(table=>table.Source.TableName!="sample_customer"))
        {
            var page=Assert.Single(table.Pages);var cache=Assert.Single(table.Columns,column=>column.Source.Extra.ContainsKey("columnFullName"));
            var foreign=table.Columns.Single(column=>column.Source.ControlKind=="List");Assert.Equal(page.Readable.Contains(foreign),page.Readable.Contains(cache));Assert.DoesNotContain(cache,page.Writable);Assert.DoesNotContain(cache,page.ListColumns);Assert.DoesNotContain(cache,page.DetailColumns);
            var proof=GlobalCachedRelationSource.RequireCache(c,table.Source,cache.Source);Assert.Equal("name",proof.Name.ColumnName);
        }
        var maintenance=MixedRelationPropagationCompilation.ForModel(model);Assert.Contains("sample_customer",maintenance.MixedTargets);Assert.Contains("sample_contract",maintenance.MixedTargets);
        Assert.Equal(new[]{"sample_customer_name"},maintenance.MixedSourceColumns["sample_contract"]);Assert.Equal(new[]{"sample_contract_name"},maintenance.MixedSourceColumns["sample_contract_line"]);
        Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>新全局维护计划以及反向事件在建连接前拒绝目录外项目，投影复制不能遗失身份约束。</summary>
    [Fact]
    public async Task Wrong_project_rejects_read_statistics_and_mutation_before_connection()
    {
        var model=GenerationModel.Create(Catalog(),"Example.GlobalSource");var maintenance=MixedRelationPropagationCompilation.ForModel(model);
        var plans=((IEnumerable)Property(maintenance,"Plans")).Cast<object>();var table=plans.Single(pair=>(string)Property(pair,"Key")=="810000000000000012");var state=Property(table,"Value");Assert.Null(NullableProperty(state,"Error"));
        var plan=Assert.IsType<MySqlNonRedundantReadPlan>(Property(state,"Read"));Assert.True((bool)Property(plan,"RequiresPublishedProjectIdentity"));Assert.DoesNotContain("yz_project_id",(string)Property(plan,"Scope"));Assert.Contains("delete_no",(string)Property(plan,"Scope"));
        var projected=(MySqlNonRedundantReadPlan)typeof(MySqlNonRedundantReadPlan).GetMethod("ForCacheFields",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plan,[new HashSet<string>{"sample_customer_name"}])!;Assert.True((bool)Property(projected,"RequiresPublishedProjectIdentity"));
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});var reader=new MySqlNonRedundantReader(session);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.ReadPageAsync(plan,[],[],1,20,projectId:"810000000000000002"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.ReadStatisticsAsync(plan,[],[],projectId:"810000000000000002"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>reader.ReadRowsAsync(projected,[],[],1,projectId:"810000000000000002"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new ProjectRelationMutationPropagator(session).AfterMutationAsync(maintenance,"sample_customer",1,"810000000000000002",new HashSet<string>{"name"},false,false));Assert.Null(session.CurrentTransaction);
    }
    /// <summary>完整发布包实际独立编译；再生成保持partial业务规则、原组件和所有生成文件稳定。</summary>
    [Fact]
    public async Task Original_full_package_builds_and_regenerates_without_replacing_handwritten_rule()
    {
        var c=Catalog();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-source-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var root=NonRedundantApplicationFixture.Root();var output=Path.Combine(directory,"application");try
        {
            Assert.True(new ApplicationPackager().Create(c,output,"Example.GlobalSource",root).FrontendIncluded);
            var hand=Path.Combine(output,"backend/generated/Services/SampleContractService.cs");await File.WriteAllTextAsync(hand,HandwrittenRule());var saved=await File.ReadAllBytesAsync(hand);
            var generated=Directory.GetFiles(Path.Combine(output,"backend/generated"),"*",SearchOption.AllDirectories).Where(path=>path.EndsWith(".g.cs",StringComparison.Ordinal)).ToDictionary(path=>Path.GetRelativePath(output,path),File.ReadAllBytes);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalSource",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(saved,await File.ReadAllBytesAsync(hand));
            foreach(var pair in generated)Assert.Equal(pair.Value,await File.ReadAllBytesAsync(Path.Combine(output,pair.Key)));
            var repeated=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            foreach(var name in new[]{"yzp-file-library","yzp-relation-select"})Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components",name,name+".vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components",name,name+".vue")));
            var service=File.ReadAllText(Path.Combine(output,"backend/generated/Services/SampleContractService.g.cs"));Assert.Contains("actor.ProjectId!=\"810000000000000001\"",service);Assert.Contains("entity.SampleCustomerName",service);
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>原Text为空类型别名，通配选择配置原样输出；未知或混合通配仍在写文件前拒绝。</summary>
    [Theory][InlineData("*")][InlineData("image,file,video")][InlineData("audio")][InlineData("*,image")]
    public async Task Original_text_and_file_modes_preserve_controls_or_reject(string accept)
    {
        var c=Catalog();var file=c.ConfigColumns.Single(column=>column.ControlKind=="FileLibrary");file.ControlDetail=JsonSerializer.Serialize(new{accept,accessMode="PROTECTED",maxCount=5});var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-source-controls");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try
        {
            if(accept is "audio" or "*,image"){Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(c,directory,"Example.GlobalSource"));Assert.False(Directory.Exists(directory));return;}
            var generator=new CSharpGenerator();generator.Generate(c,directory,"Example.GlobalSource");var pages=Directory.GetFiles(Path.Combine(directory,"frontend/pages/yzp"),"*.vue").ToDictionary(path=>Path.GetFileName(path)!,File.ReadAllBytes);Assert.Contains(pages.Values,bytes=>System.Text.Encoding.UTF8.GetString(bytes).Contains("accept='"+accept+"'",StringComparison.Ordinal));
            foreach(var column in c.ConfigColumns.Where(column=>column.ControlKind=="Text"))column.ControlKind="";generator.Generate(c,directory,"Example.GlobalSource");foreach(var pair in pages)Assert.Equal(pair.Value,File.ReadAllBytes(Path.Combine(directory,"frontend/pages/yzp",pair.Key!)));
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>实际编译生成服务，普通和超级身份都不能用目录外项目读取全局记录。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Generated_service_rejects_wrong_project_before_query_execution(bool administrator)
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-source-actor");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try
        {
            new CSharpGenerator().Generate(Catalog(),Path.Combine(directory,"generated"),"Example.GlobalSourceActor");await File.WriteAllTextAsync(Path.Combine(directory,"generated/Services/SampleContractService.cs"),HandwrittenRule().Replace("Example.GlobalSource","Example.GlobalSourceActor",StringComparison.Ordinal));var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"});
            var actor=new RuntimeActor(8,"真实范围校验",administrator,"810000000000000002","");
            foreach(var (name,page) in new[]{("SampleCustomer","81001"),("SampleContract","81002"),("SampleContractLine","81003")})
            {
                var mapper=Activator.CreateInstance(assembly.GetType("Example.GlobalSourceActor.Generated.Mappers."+name+"Mapper",true)!,session)!;
                var constructor=assembly.GetType("Example.GlobalSourceActor.Generated.Services."+name+"Service",true)!.GetConstructors().Single();
                var service=constructor.Invoke(constructor.GetParameters().Select(parameter=>parameter.ParameterType==typeof(DatabaseSession)?(object)session:parameter.ParameterType.IsInstanceOfType(mapper)?mapper:parameter.ParameterType==typeof(IIdGenerator)?new SnowflakeIdGenerator(902):parameter.ParameterType==typeof(IEnumerable<IRecordMutationObserver>)?Array.Empty<IRecordMutationObserver>():parameter.ParameterType==typeof(IEnumerable<IRecordMutationGuard>)?Array.Empty<IRecordMutationGuard>():null).ToArray());
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await(Task)service.GetType().GetMethod("Page"+page+"Async")!.Invoke(service,[new PageRequest(),actor,CancellationToken.None])!);
                if(name=="SampleContract")
                {
                    var entity=Activator.CreateInstance(assembly.GetType("Example.GlobalSourceActor.Generated.Entities.SampleContract",true)!)!;var amount=entity.GetType().GetProperty("Amount")!;amount.SetValue(entity,-0.01m);var hook=service.GetType().GetMethod("BeforeSave",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    var failure=Assert.Throws<TargetInvocationException>(()=>hook.Invoke(service,[entity,actor,true,new HashSet<string>()]));Assert.Equal("合同金额不能小于零",Assert.IsType<BusinessRuleException>(failure.InnerException).Message);
                    foreach(var value in new decimal?[]{0m,null}){amount.SetValue(entity,value);hook.Invoke(service,[entity,actor,false,new HashSet<string>()]);}Assert.Equal("v8-sample-preserved",service.GetType().GetMethod("SampleBusinessSentinel")!.Invoke(service,null));
                }
            }
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>无项目列全局父只在原List发布证明成立时省略项目谓词；缺证明不能降级放行。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void File_parent_scope_reuses_published_global_source_proof(bool unproven)
    {
        var c=Catalog();var child=c.ConfigTables.Single(table=>table.TableName=="sample_contract_line");var page=c.ConfigPages.Single(page=>page.ConfigTableId==child.Id);
        var file=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(c.ConfigColumns.Single(column=>column.ControlKind=="FileLibrary")))!;file.Id="89001";file.ConfigTableId=child.Id;c.ConfigColumns.Add(file);
        c.ConfigPageColumns.Add(new(){Id="89002",ConfigPageId=page.Id,ConfigColumnId=file.Id,YzProjectId=c.YzProjectId,IsShow=1,IsEdit=1});if(unproven)child.Extra.Remove("scopeKind");
        var context=new FileMetadataContext([c]);var snapshot=Assert.Single(context.Catalogs);var frozenPage=snapshot.ConfigPages.Single(item=>item.Id==page.Id);var method=typeof(FileMetadataContext).GetMethod("PageScope",BindingFlags.Instance|BindingFlags.NonPublic)!;
        if(unproven){var error=Assert.Throws<TargetInvocationException>(()=>method.Invoke(context,[snapshot,frozenPage]));Assert.IsType<NotSupportedException>(error.InnerException);return;}
        var scope=method.Invoke(context,[snapshot,frozenPage])!;var parent=Assert.Single(((IEnumerable)Property(scope,"Parents")).Cast<object>());Assert.Null(NullableProperty(parent,"Project"));Assert.True((bool)Property(parent,"HasSoftDelete"));
    }
    /// <summary>缺副本、表页发布或关系定义冲突在完整生成之前拒绝，不留下部分后端或页面。</summary>
    [Theory][InlineData("missingCache")][InlineData("wrongPath")][InlineData("unknownControl")][InlineData("sourceProject")][InlineData("targetPage")][InlineData("overlay")]
    public void Unproven_source_never_writes_partial_application(string kind)
    {
        var c=Catalog();var source=c.ConfigTables.Single(table=>table.TableName=="sample_contract");var foreign=c.ConfigColumns.Single(column=>column.ConfigTableId==source.Id&&column.ControlKind=="List");var cache=c.ConfigColumns.Single(column=>column.ConfigTableId==source.Id&&column.Extra.ContainsKey("columnFullName"));
        if(kind=="missingCache")c.ConfigColumns.Remove(cache);if(kind=="wrongPath")cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("sample_customer.password");if(kind=="unknownControl")c.ConfigColumns.Single(column=>column.ConfigTableId==source.Id&&column.ColumnName=="name").ControlKind="UnknownInput";if(kind=="sourceProject")source.YzProjectId="810000000000000002";if(kind=="targetPage")c.ConfigPages.Single(page=>page.Id=="81001").YzProjectId="810000000000000002";if(kind=="overlay")c.ConfigPageColumns.Single(field=>field.ConfigColumnId==foreign.Id).Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");
        var directory=Path.Combine(Path.GetTempPath(),"metarill-global-source-reject",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new CSharpGenerator().Generate(c,directory,"Example.GlobalSourceReject"));Assert.False(Directory.Exists(directory));
    }
    /// <summary>固定公开样板来自已冻结Java原元数据及实际DDL转换，不读本机私有配置。</summary>
    internal static MetadataCatalog Catalog()=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"Fixtures/v8-global-cached-sample.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    /// <summary>金额规则属于手写partial，再生成不能覆盖；由后续真实保存验收验证回滚行为。</summary>
    internal static string HandwrittenRule()=>"""
using Example.GlobalSource.Generated.Entities;
using Metarill.Core.Security;
using Metarill.Core.Contracts;
namespace Example.GlobalSource.Generated.Services;
/// <summary>原样板手写金额校验。</summary>
public partial class SampleContractService
{
    /// <summary>原样板手写扩展的保留标记。</summary>
    public string SampleBusinessSentinel()=>"v8-sample-preserved";
    /// <summary>对象校验在原事务内运行，负金额不能写入。</summary>
    partial void BeforeSave(SampleContract entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    {
        if(entity.Amount is{} amount&&amount<0)throw new BusinessRuleException("合同金额不能小于零");
    }
}
""";
    /// <summary>只观察冻结计划的私有只读属性，不扩宽生产API。</summary>
    private static object Property(object target,string name)=>NullableProperty(target,name)??throw new InvalidOperationException("测试计划属性不存在："+name);
    private static object? NullableProperty(object target,string name)=>target.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(target);
}
