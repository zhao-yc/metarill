using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

public sealed partial class MaintenanceReadProjectionTests
{
    /// <summary>独立角色裁剪无关目标，R0中间与共享前缀保留所有必要祖先；校验真实读取器SQL。</summary>
    [Theory][InlineData("short_name",1,false)][InlineData("target_name",2,false)][InlineData("target_title",2,false)][InlineData("short_name",1,true)][InlineData("target_name",2,true)]
    public async Task Cache_current_read_keeps_only_required_join_closure(string field,int count,bool textId)
    {
        var (provider,sql)=await ReadJoined(field,textId:textId);Assert.Equal(count,Regex.Matches(sql," LEFT JOIN ").Count);Assert.Equal(field=="short_name",!sql.Contains("`maintenance_target`",StringComparison.Ordinal));Assert.Contains("b.`yz_project_id`=@project_scope_number",sql);Assert.Contains("b.`delete_no` IS NULL",sql);Assert.EndsWith(" FOR SHARE",sql);Assert.DoesNotContain("COUNT(",sql);
        foreach(Match match in Regex.Matches(sql,@" LEFT JOIN `[^`]+` (r[0-9]+) ON ")){var alias=match.Groups[1].Value;Assert.Contains(alias+".`yz_project_id`=@project_scope_number",sql);Assert.Contains(alias+".`delete_no` IS NULL",sql);}if(textId&&field=="target_name")Assert.Contains("BINARY CAST(",sql);Assert.Equal(1,provider.Commits);
    }
    /// <summary>非响应字段用于筛选、排序或固定条件时仍保留其联接；大小写沿现有表达式合同。</summary>
    [Theory][InlineData("filter")][InlineData("order")][InlineData("condition")][InlineData("upperFilter")]
    public async Task Hidden_query_dependency_keeps_its_join_and_ancestors(string usage)
    {
        var name=usage=="upperFilter"?"TARGET_TITLE":"target_title";var filters=usage is "filter" or "upperFilter"?new[]{new FixedReadFilter(name,FixedReadComparison.Equal,"目标标题")}:[];var order=usage=="order"?new[]{new FixedReadOrder(name)}:[];var condition=usage=="condition"?Condition("target_title","目标标题"):null;var (provider,sql)=await ReadJoined("short_name",filters,order,condition);Assert.Equal(3,Regex.Matches(sql," LEFT JOIN ").Count);Assert.Contains("`maintenance_target`",sql);Assert.DoesNotContain(" AS `target_title`",sql);Assert.Single(provider.Commands);
    }
    /// <summary>空缓存用途只读主键，但完整结构、反向传播和原计划仍保持。</summary>
    [Fact]public void Empty_cache_projection_drops_joins_without_reducing_full_proof()
    {
        var plan=JoinedPlan();var view=Project(plan,new HashSet<string>());Assert.Equal(new[]{"id"},view.Outputs);Assert.Equal(" FROM `maintenance_source` b",From(view,["id"]));Assert.Equal(3,Regex.Matches((string)Read(plan,"From")," LEFT JOIN ").Count);foreach(var key in new[]{"PhysicalReferences","SourceDependencies","Reverse","Origins","Scope"})Assert.Same(Read(plan,key),Read(view,key));Assert.Equal(Read(plan,"From"),From(plan,plan.Outputs));
    }
    /// <summary>组合投影及元数据后改均不能改变编译依赖，不能重新扩张已收窄输出。</summary>
    [Fact]public void Join_dependencies_and_nested_projection_are_frozen()
    {
        var model=JoinedCatalog(false);var plan=ReadPlan(model);var fields=new HashSet<string>{"short_name","target_name"};var view=Project(plan,fields);fields.Clear();model.ConfigColumns.Clear();model.ConfigTables.Clear();var narrow=Project(view,new HashSet<string>{"short_name"});Assert.Single(Regex.Matches(From(narrow,narrow.Outputs)," LEFT JOIN "));Assert.Equal(3,Regex.Matches(From(plan,plan.Outputs)," LEFT JOIN ").Count);Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(()=>Project(narrow,new HashSet<string>{"target_name"})).InnerException);
    }
    /// <summary>混合消费者补齐整表物理预检后，仍保留裁剪计划和原反向传播字段。</summary>
    [Fact]public void Mixed_consumer_keeps_join_projection_after_physical_proof_copy()
    {
        var catalog=RelationMaintenanceCatalog.Compile(JoinedCatalog(false));var consumers=(System.Collections.IEnumerable)Read(Read(catalog,"Mixed"),"Consumers");var consumer=Assert.Single(consumers.Cast<object>());var plan=(MySqlNonRedundantReadPlan)Read(consumer,"Read");var physical=((System.Collections.IEnumerable)Read(plan,"PhysicalReferences")).Cast<object>().ToArray();Assert.Contains(physical,item=>(string)Read(item,"Table")=="maintenance_source"&&(string)Read(Read(item,"Column"),"Name")=="short_name");Assert.Contains(physical,item=>(string)Read(item,"Table")=="maintenance_source"&&(string)Read(Read(item,"Column"),"Name")=="target_name");Assert.Single(Regex.Matches(From(Project(plan,new HashSet<string>{"short_name"}),["id","short_name"])," LEFT JOIN "));Assert.Equal(3,Regex.Matches(From(plan,plan.Outputs)," LEFT JOIN ").Count);Assert.Contains("target_title",plan.Outputs);
    }
    /// <summary>普通R0用途没有维护裁剪合同，不能擅自更改跨表有效性或既有查询联接。</summary>
    [Fact]public void Ordinary_nonredundant_plan_keeps_original_from()
    {
        var plan=NonRedundantReadTests.Plan(NonRedundantReadTests.Catalog("ordinary_source","ordinary_target"));Assert.Same(Read(plan,"From"),From(plan,["id"]));
    }
    /// <summary>无效查询字段在任何读取前拒绝，不能把未知名称补成联接或SQL。</summary>
    [Theory][InlineData("filter")][InlineData("order")][InlineData("condition")]
    public async Task Unknown_query_dependency_rejects_before_open(string usage)
    {
        var provider=new RecordingProvider(false,"short_name");using var session=new DatabaseSession(Options,null,provider);var plan=Project(JoinedPlan(),new HashSet<string>{"short_name"});await Assert.ThrowsAsync<InvalidOperationException>(()=>new MySqlNonRedundantReader(session).ReadRowsAsync(plan,usage=="filter"?[new("missing",FixedReadComparison.Equal,"x")]:[],usage=="order"?[new("missing")]:[],10,condition:usage=="condition"?Condition("missing","x"):null));Assert.Equal(0,provider.Opens);Assert.Empty(provider.Commands);
    }
    /// <summary>原当前读执行入口和事务对象均实际使用；仅结果由严格命令记录器提供。</summary>
    private static async Task<(RecordingProvider Provider,string Sql)> ReadJoined(string field,IReadOnlyList<FixedReadFilter>? filters=null,IReadOnlyList<FixedReadOrder>? order=null,FixedConditionSqlPlan? condition=null,bool textId=false)
    {
        var provider=new RecordingProvider(false,field);using var session=new DatabaseSession(Options,null,provider);await session.InTransactionAsync(async()=>{var rows=await new MySqlNonRedundantReader(session).ReadCurrentRowsAsync(Project(JoinedPlan(textId),new HashSet<string>{field}),filters??[],order??[],128,condition:condition);Assert.Equal("当前名称",Assert.Single(rows)[field]);Assert.Equal(new[]{"id",field},rows[0].Keys);return true;});return(provider,Assert.Single(provider.Commands));
    }
    /// <summary>固定条件只描述已知列和值，渲染时必须向同一计划请求表达式。</summary>
    private static FixedConditionSqlPlan Condition(string field,string value)=>FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile(field+"='"+value+"'",[field]),new Dictionary<string,string>{{field,"string"}});
    /// <summary>维护编译器接收真实目录，原R1缓存经R0中间落到普通物理目标。</summary>
    private static MySqlNonRedundantReadPlan JoinedPlan(bool textId=false)=>ReadPlan(JoinedCatalog(textId));
    /// <summary>多字段复用两级链，另一个角色直达同一目标；不能按目标表名误合并别名。</summary>
    private static MetadataCatalog JoinedCatalog(bool textId)
    {
        var model=MixedRelationMutationPlanTests.Chain();if(textId)model.ConfigColumns.Single(x=>x.Id=="foreign").LogicalType="string";model.ConfigColumns.AddRange([new(){Id="company_title",ConfigTableId="company",ColumnName="title",LogicalType="string",ColumnLength="100"},new(){Id="short_ref",ConfigTableId="source",ColumnName="short_id",LogicalType="int64",LinkConfigTableId="company"},Cache("short_cache","short_name","short.name"),Cache("title_cache","target_title","target.company.title")]);return model;
    }
    /// <summary>只有元数据声明的物理缓存进入维护，不手写实体或Mapper。</summary>
    private static ColumnMetadata Cache(string id,string name,string path)=>new(){Id=id,ConfigTableId="source",ColumnName=name,LogicalType="string",ColumnLength="100",Extra=new(){["isJoinColumn"]=JsonSerializer.SerializeToElement(1),["columnFullName"]=JsonSerializer.SerializeToElement(path)}};
    /// <summary>观察编译后的内部计划，不给HTTP增加可变字段或SQL入口。</summary>
    private static MySqlNonRedundantReadPlan ReadPlan(MetadataCatalog model)
    {
        var catalog=RelationMaintenanceCatalog.Compile(model);var plans=(System.Collections.IEnumerable)Read(catalog,"Plans");var table=Read(plans.Cast<object>().Single(x=>(string)Read(x,"Key")=="source"),"Value");Assert.Null(table.GetType().GetProperty("Error",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.GetValue(table));return (MySqlNonRedundantReadPlan)Read(table,"Read");
    }
    /// <summary>只读内部FROM选择入口，不对外开放运行期计划编辑。</summary>
    private static string From(MySqlNonRedundantReadPlan plan,IEnumerable<string> fields)=>(string)typeof(MySqlNonRedundantReadPlan).GetMethod("FromForFields",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(plan,[fields])!;
}
