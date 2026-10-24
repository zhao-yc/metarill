using System.Data;
using System.Diagnostics;
using System.Text;
using System.Data.Common;
using System.Reflection;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Agent;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行真实生成Service和DTO；ORM执行由明确离线代理记录，不把它当成MySQL验收。</summary>
public sealed class MaterializedDerivedWriteTests
{
    [Fact]
    public void Published_platform_has_exactly_three_proven_bindings_and_join_is_never_a_request_field()
    {
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(BackendRoot(),"metadata/platform.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var model=GenerationModel.Create(catalog,"Example.DerivedProof");
        var bindings=model.Tables.SelectMany(table=>MaterializedDerivedWriteCompilation.ForTable(model,table)).ToArray();
        Assert.Equal(new[]{"config_column_column_name","config_column_sort_no","config_table_table_name"},bindings.Select(binding=>binding.Storage.Source.ColumnName).Order(StringComparer.Ordinal));
        Assert.Equal(2,bindings.GroupBy(binding=>(binding.ForeignKey.Source.ConfigTableId,binding.ForeignKey.Source.Id)).Count());
        foreach(var table in model.Tables)foreach(var page in table.Pages)Assert.DoesNotContain(page.Writable,column=>GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"));
    }

    [Theory]
    [InlineData("fullName")] [InlineData("undeclaredTarget")] [InlineData("wrongTarget")] [InlineData("targetJoin")] [InlineData("nonredundant")] [InlineData("bothWrongType")] [InlineData("targetArray")] [InlineData("sourceArray")]
    public void Similar_names_or_unproven_relations_do_not_enable_derived_writes(string scenario)
    {
        var catalog=Catalog();var storage=catalog.ConfigColumns.Single(column=>column.ColumnName=="config_table_table_name");
        var foreign=catalog.ConfigColumns.Single(column=>column.ColumnName=="config_table_id");
        if(scenario=="fullName")storage.Extra["columnFullName"]=JsonSerializer.SerializeToElement("config_table.deep.table_name");
        if(scenario=="undeclaredTarget")foreign.LinkConfigTableId="";
        if(scenario=="wrongTarget")foreign.LinkConfigTableId="2";
        if(scenario=="targetJoin")catalog.ConfigColumns.Single(column=>column.ColumnName=="table_name").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(scenario=="nonredundant")catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=0;
        if(scenario=="bothWrongType"){storage.LogicalType="int32";catalog.ConfigColumns.Single(column=>column.ColumnName=="table_name").LogicalType="int32";}
        if(scenario=="targetArray")catalog.ConfigColumns.Single(column=>column.ColumnName=="table_name").ControlKind="FileLibrary";
        if(scenario=="sourceArray")storage.ControlKind="FileLibrary";
        var model=GenerationModel.Create(catalog,"Example.UnknownDerived");var table=model.Tables.Single(table=>table.Source.Id=="3");
        Assert.Empty(MaterializedDerivedWriteCompilation.ForTable(model,table));
        Assert.Contains(JsonSerializer.Serialize("物理派生写入尚未证明：config_table_table_name"),ForeignValidationRenderer.Render(model,table,table.Pages.Single()));
    }

    [Fact]
    public async Task Actual_generated_create_update_copy_and_import_preserve_receipts_and_strict_echo_contract()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-materialized-derived");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var catalog=Catalog();new CSharpGenerator().Generate(catalog,Path.Combine(root,"generated"),"Example.Derived");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new StringLongJsonConverter());
            foreach(var (table,page,field) in new[]{("ConfigPage","301","configTableTableName"),("ConfigPageColumn","401","configColumnColumnName"),("ConfigPageColumn","401","configColumnSortNo")})
            {
                var dto=assembly.GetType($"Example.Derived.Generated.DTOs.{table}Page{page}UpdateRequest",true)!;
                Assert.Null(dto.GetProperty(GenerationModel.Pascal(field)));Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"id\":\"1\",\""+field+"\":null}",dto,options));
                var controller=assembly.GetType($"Example.Derived.Generated.Controllers.{table}Page{page}Controller",true)!;
                Assert.Equal(dto,controller.GetMethod("Update")!.GetCustomAttribute<LegacyPageWriteRequestAttribute>()!.RequestType);
                Assert.DoesNotContain(field,JsonSerializer.Serialize(typeof(GeneratedCapabilityIndex).Assembly.GetType("Metarill.Runtime.Agent.AgentJsonSchema",true)!.GetMethod("Create",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[dto,0])),StringComparison.Ordinal);
                var legacy=(LegacyPageWriteContract)controller.GetField("LegacyWrite",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                var normalized=legacy.Normalize(JsonSerializer.SerializeToElement(new Dictionary<string,object?>{{"id","1"},{field,"伪造缓存"},{"name","保留名称"}}),LegacyPageWriteOperation.Update,"7",null);
                Assert.False(normalized.TryGetProperty(field,out _));Assert.Equal("保留名称",normalized.GetProperty("name").GetString());
                Assert.Throws<ArgumentException>(()=>legacy.Normalize(JsonSerializer.SerializeToElement(new{unknown="伪造"}),LegacyPageWriteOperation.Update,"7",null));
            }
            var schema=File.ReadAllText(Path.Combine(root,"generated/frontend/common/yzp_runtime/page-write-schema.generated.json"));
            Assert.DoesNotContain("configTableTableName",schema);Assert.DoesNotContain("configColumnSortNo",schema);
            // 只读DOM/校验按原页标志保留，DTO收紧不能替用户改界面。
            var detail=File.ReadAllText(Path.Combine(root,"generated/frontend/pages/yzp/config_page-detail-301.vue"));
            Assert.Contains("v-model='entity.configTableTableName'",detail);Assert.Contains("configTableTableName:false",detail);Assert.Contains("configTableTableName:{rules:",detail);
            var runtime=new RecordedOrm(assembly);using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString=""},runtime.Orm,new OfflineProvider());
            var actor=new RuntimeActor(8,"离线真实生成调用",true,"7","");
            var service=runtime.Service("ConfigPage",session);runtime.Set("ConfigTable",new(){["Id"]=10L,["YzProjectId"]=7L,["TableName"]="actual_table"});
            var created=await Invoke(service,"Create301Async",Request("ConfigPage","301","Create","{\"name\":\"新页\",\"configTableId\":\"10\"}"),null,actor,CancellationToken.None);
            Assert.Equal("actual_table",Property(created,"ConfigTableTableName"));
            var sql=System.Text.RegularExpressions.Regex.Replace(runtime.ProjectionSql(),@"\s+"," ");var select=sql[..sql.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.Contains("`id`",select);Assert.Contains("`table_name`",select);Assert.DoesNotContain("`name`",select);Assert.DoesNotContain("`yz_project_id`",select);Assert.Contains("`yz_project_id`",sql);Assert.Contains("`delete_no` IS NULL",sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("FOR UPDATE",sql,StringComparison.OrdinalIgnoreCase);
            Assert.Equal("CountAsync,ForUpdate,ToListAsync",string.Join(',',runtime.Trace.Where(value=>value is "CountAsync" or "ForUpdate" or "ToListAsync")));
            runtime.Trace.Clear();runtime.Set("ConfigPage",new(){["Id"]=20L,["YzProjectId"]=7L,["ConfigTableId"]=10L,["ConfigTableTableName"]="old_cache"});
            var omitted=await Invoke(service,"Update301Async",Request("ConfigPage","301","Update","{\"id\":\"20\",\"name\":\"仅名称\"}"),null,actor,CancellationToken.None);
            Assert.Equal("old_cache",Property(omitted,"ConfigTableTableName"));Assert.DoesNotContain("CountAsync",runtime.Trace);
            var cleared=await Invoke(service,"Update301Async",Request("ConfigPage","301","Update","{\"id\":\"20\",\"configTableId\":null}"),null,actor,CancellationToken.None);
            Assert.Null(Property(cleared,"ConfigTableTableName"));
            runtime.Set("ConfigPage",new(){["Id"]=20L,["YzProjectId"]=7L,["ConfigTableId"]=10L,["ConfigTableTableName"]="copied_stale"});runtime.Trace.Clear();
            var copy=await Invoke(service,"Copy301Async",20L,null,actor,CancellationToken.None);Assert.Equal("actual_table",Property(copy,"ConfigTableTableName"));Assert.Single(runtime.Trace,value=>value=="CountAsync");
            runtime.Set("ConfigPage",new(){["Id"]=20L,["YzProjectId"]=7L,["ConfigTableId"]=10L,["UnknownJoinCache"]="未知旧派生"});
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Invoke(service,"Copy301Async",20L,null,actor,CancellationToken.None));
            runtime.Trace.Clear();var import=assembly.GetType("Example.Derived.Generated.DTOs.ConfigPagePage301ImportRequest",true)!;
            await Invoke(service,"Import301Async",JsonSerializer.Deserialize("{\"requestJson\":[{\"名称\":\"导入页\",\"所属表\":\"10\"}]}",import,options)!,actor,CancellationToken.None);Assert.Single(runtime.Trace,value=>value=="CountAsync");
            var columnService=runtime.Service("ConfigPageColumn",session);runtime.Set("ConfigColumn",new(){["Id"]=30L,["YzProjectId"]=7L,["ColumnName"]="real_column",["SortNo"]=null});runtime.Trace.Clear();
            var fieldVo=await Invoke(columnService,"Create401Async",Request("ConfigPageColumn","401","Create","{\"name\":\"字段\",\"configColumnId\":\"30\"}"),null,actor,CancellationToken.None);
            Assert.Equal("real_column",Property(fieldVo,"ConfigColumnColumnName"));
            var columnSql=System.Text.RegularExpressions.Regex.Replace(runtime.ProjectionSql(),@"\s+"," ");var columnSelect=columnSql[..columnSql.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.Contains("`column_name`",columnSelect);Assert.Contains("`sort_no`",columnSelect);Assert.DoesNotContain("`name`",columnSelect);Assert.DoesNotContain("`yz_project_id`",columnSelect);
            Assert.Null(Property(fieldVo,"ConfigColumnSortNo"));Assert.Single(runtime.Trace,value=>value=="ToListAsync");
            runtime.Count=0;runtime.Trace.Clear();await Assert.ThrowsAsync<InvalidOperationException>(()=>Invoke(service,"Create301Async",Request("ConfigPage","301","Create","{\"configTableId\":\"10\"}"),null,actor,CancellationToken.None));Assert.DoesNotContain("ForUpdate",runtime.Trace);

            object Request(string table,string page,string kind,string json)=>JsonSerializer.Deserialize(json,assembly.GetType($"Example.Derived.Generated.DTOs.{table}Page{page}{kind}Request",true)!,options)!;
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    [Theory]
    [InlineData(1,0,false,true)] [InlineData(1,1,true,false)] [InlineData(0,0,true,false)] [InlineData(0,1,true,false)]
    public void Original_redundant_and_nonredundant_join_controls_keep_their_readonly_and_validation_branch(int redundant,int pageReadonly,bool expectedReadonly,bool expectedRequired)
    {
        var catalog=Catalog();catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=redundant;
        var column=catalog.ConfigColumns.Single(column=>column.ColumnName=="config_table_table_name");var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==column.Id);field.Extra["isReadOnly"]=JsonSerializer.SerializeToElement(pageReadonly);
        var model=GenerationModel.Create(catalog,"Example.OriginalJoin");var table=model.Tables.Single(table=>table.Source.Id=="3");var page=table.Pages.Single();var detail=OriginalUniappDetailRenderer.Render(model,table,page,[]);
        Assert.DoesNotContain(page.Writable,candidate=>candidate.Source.Id==column.Id);
        Assert.Contains("configTableTableName:"+(expectedReadonly?"true":"false"),detail);
        Assert.Equal(expectedRequired,detail.Contains("configTableTableName:{rules:[{\"required\":true",StringComparison.Ordinal));
        Assert.Equal(redundant==1,detail.Contains("v-model='entity.configTableTableName'",StringComparison.Ordinal));
    }

    [Fact]
    public async Task Actual_vue_preserves_original_join_readonly_rules_and_whole_entity_events()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-original-join-vue");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var scenarios=new List<object>();var index=0;
            foreach(var (redundant,flag) in new[]{(1,0),(1,1),(0,0),(0,1)})
            {
                var catalog=Catalog();catalog.ConfigTables.Single(table=>table.Id=="3").IsRongyu=redundant;var column=catalog.ConfigColumns.Single(column=>column.ColumnName=="config_table_table_name");
                catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId==column.Id).Extra["isReadOnly"]=JsonSerializer.SerializeToElement(flag);
                var model=GenerationModel.Create(catalog,"Example.JoinVue");var table=model.Tables.Single(table=>table.Source.Id=="3");var file="detail-"+(index++)+".vue";
                await File.WriteAllTextAsync(Path.Combine(root,file),OriginalUniappDetailRenderer.Render(model,table,table.Pages.Single(),[]));scenarios.Add(new{file,readOnly=redundant==0||flag==1,required=redundant==1&&flag==0});
            }
            await File.WriteAllTextAsync(Path.Combine(root,"join-ui-cases.json"),JsonSerializer.Serialize(scenarios));
            var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            start.ArgumentList.Add(Path.Combine(BackendRoot(),"tests/Metarill.Generation.Tests/MaterializedDerivedVueProbe.mjs"));start.ArgumentList.Add(root);
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await output+await error);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>字段配置与冗余目标都来自真实生成模型，测试不手造派生IR。</summary>
    internal static MetadataCatalog Catalog()
    {
        var catalog=new MetadataCatalog{YzProjectId="7"};int sequence=100;
        foreach(var (id,name) in new[]{("1","config_table"),("2","config_column"),("3","config_page"),("4","config_page_column")})
        {
            catalog.ConfigTables.Add(new(){Id=id,TableName=name,IsRongyu=1,YzProjectId="7"});
            Add(id,"id","int64");Add(id,"name","string","名称");Add(id,"yz_project_id","int64");Add(id,"delete_no","datetime");
        }
        Add("1","table_name","string");Add("2","column_name","string");Add("2","sort_no","int32");
        Add("3","config_table_id","int64","所属表").LinkConfigTableId="1";Join("3","config_table_table_name","string","config_table.table_name");Join("3","unknown_join_cache","string","config_table.unproven");
        Add("4","config_column_id","int64").LinkConfigTableId="2";Join("4","config_column_column_name","string","config_column.column_name");Join("4","config_column_sort_no","int32","config_column.sort_no");
        foreach(var (table,page) in new[]{("3","301"),("4","401")})
        {
            catalog.ConfigPages.Add(new(){Id=page,ConfigTableId=table,IsCopy=1,IsImport=1,IsSave=1});
            foreach(var column in catalog.ConfigColumns.Where(column=>column.ConfigTableId==table&&!new[]{"id","yz_project_id","delete_no"}.Contains(column.ColumnName)))catalog.ConfigPageColumns.Add(new(){Id=(++sequence).ToString(),ConfigPageId=page,ConfigColumnId=column.Id,IsEdit=1,IsShow=1,IsRequired=column.ColumnName=="config_table_table_name"?1:0});
        }
        return catalog;
        ColumnMetadata Add(string table,string name,string type,string label="") {var column=new ColumnMetadata{Id=(++sequence).ToString(),ConfigTableId=table,ColumnName=name,LogicalType=type,Name=label.Length==0?name:label};catalog.ConfigColumns.Add(column);return column;}
        void Join(string table,string name,string type,string path){var column=Add(table,name,type);column.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);column.Extra["columnFullName"]=JsonSerializer.SerializeToElement(path);}
    }
    private static object? Property(object value,string property)=>value.GetType().GetProperty(property)!.GetValue(value);
    private static async Task<object> Invoke(object service,string method,params object?[] args){var task=(Task)service.GetType().GetMethod(method)!.Invoke(service,args)!;await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;}
    private static string BackendRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../.."));

    /// <summary>仅模拟ORM执行结果，执行真实生成生命周期；不解析SQL、不触网、不替代授权或真实锁验收。</summary>
    private sealed class RecordedOrm(Assembly assembly)
    {
        internal List<string> Trace {get;}=[];internal long Count {get;set;}=1;
        private readonly Dictionary<Type,object> rows=[];
        private Type? projectedEntity;private LambdaExpression? projection;private LambdaExpression[] predicates=[];
        /// <summary>将真实生成方法捕获的相同表达式交给真实FreeSql解析，不执行命令或另造WHERE。</summary>
        internal string ProjectionSql()=> (string)GetType().GetMethod(nameof(TypedSql),BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(projectedEntity!,projection!.ReturnType).Invoke(this,[])!;
        private string TypedSql<T,TResult>()where T:class
        {
            using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1"});
            using var connection=new Connection();connection.Open();using var transaction=new Transaction(connection);
            var query=session.Select<T>().WithTransaction(transaction);foreach(var predicate in predicates)query=query.Where((Expression<Func<T,bool>>)predicate);
            return query.ForUpdate().ToSql((Expression<Func<T,TResult>>)projection!);
        }
        internal IFreeSql Orm=>Make<IFreeSql>((method,args)=>method.Name=="Select"?Select(method.GetGenericArguments()[0]):method.Name is "Insert" or "Update"?Command(method.ReturnType):throw new NotSupportedException(method.Name));
        internal void Set(string table,Dictionary<string,object?> values){var type=assembly.GetType("Example.Derived.Generated.Entities."+table,true)!;var row=Activator.CreateInstance(type)!;foreach(var value in values)type.GetProperty(value.Key)!.SetValue(row,value.Value);rows[type]=row;}
        internal object Service(string table,DatabaseSession session){var type=assembly.GetType("Example.Derived.Generated.Services."+table+"Service",true)!;var mapper=Activator.CreateInstance(assembly.GetType("Example.Derived.Generated.Mappers."+table+"Mapper",true)!,session);return Activator.CreateInstance(type,session,mapper,new SnowflakeIdGenerator(830),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>())!;}
        private object Select(Type entity)
        {
            var conditions=new List<LambdaExpression>();object? query=null;query=Make(typeof(ISelect<>).MakeGenericType(entity),(method,args)=>
            {
                Trace.Add(method.Name);
                if(method.Name=="Where"&&args![0] is LambdaExpression condition)conditions.Add(condition);
                if(method.Name=="CountAsync")return Task.FromResult(Count);
                if(method.Name=="FirstAsync")return Completed(entity,rows.GetValueOrDefault(entity));
                if(method.Name=="ToListAsync")
                {
                    var expression=(LambdaExpression)args![0]!;projectedEntity=entity;projection=expression;predicates=conditions.ToArray();var item=expression.Compile().DynamicInvoke(rows[entity]);var list=(System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(expression.ReturnType))!;list.Add(item);return Completed(list.GetType(),list);
                }
                return query;
            });return query;
        }
        private object Command(Type type){object? command=null;command=Make(type,(method,args)=>method.Name=="ExecuteAffrowsAsync"?Task.FromResult(1):command);return command;}
        private static object Completed(Type type,object? value)=>typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value])!;
        private static T Make<T>(Func<MethodInfo,object?[]?,object?> action)where T:class=>(T)Make(typeof(T),action);
        private static object Make(Type type,Func<MethodInfo,object?[]?,object?> action){var proxy=DispatchProxy.Create(type,typeof(Proxy));((Proxy)proxy).Action=action;return proxy;}
    }
    public class Proxy:DispatchProxy {internal Func<MethodInfo,object?[]?,object?> Action=null!;protected override object? Invoke(MethodInfo? method,object?[]? args)=>Action(method!,args);}
    private sealed class OfflineProvider:IDatabaseProvider
    {
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}public string QuoteIdentifier(string value)=>new MySqlDatabaseProvider().QuoteIdentifier(value);public string ParameterPlaceholder(string value)=>"@"+value;public IFreeSql CreateOrm(DatabaseOptions options)=>throw new NotSupportedException();public DbConnection CreateConnection(DatabaseOptions options)=>new Connection();
    }
    private sealed class Connection:DbConnection
    {
        private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this);protected override DbCommand CreateDbCommand()=>throw new NotSupportedException();
    }
    private sealed class Transaction(Connection connection):DbTransaction {public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback(){}}
}
