using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>执行实际生成Service及事务；ORM执行明确由离线代理提供，不证明真实MySQL锁行为。</summary>
public sealed class LogicalTypePartialCompatibilityTests
{
    private const string Platform="1608097412978577409",Table="6217714881724416",Page="6217715532627968";

    [Fact]
    public async Task Actual_generated_update_preserves_legacy_empty_type_but_create_copy_explicit_edits_and_changed_hook_do_not_bypass_required()
    {
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-logical-type-service");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(Catalog(),Path.Combine(root,"generated"),"Example.LogicalCompatibility");
            // 手写钩子只服务于本测试，证明捕获的是锁定原值而不是钩子执行后的值。
            await File.WriteAllTextAsync(Path.Combine(root,"ChangedTypeHook.cs"),"""
using Metarill.Core.Security;
using Example.LogicalCompatibility.Generated.Entities;
namespace Example.LogicalCompatibility.Generated.Services;
public partial class ConfigColumnService
{
    partial void BeforeSave(ConfigColumn entity, RuntimeActor actor, bool creating, ISet<string> submittedFields)
    { if (actor.AccountName == "改变逻辑") entity.LogicalType = "string"; }
}
""");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(root);var offline=new OfflineOrm(assembly);
            using var session=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString=""},offline.Orm,offline);
            var observer=new TailObserver();var mapper=Activator.CreateInstance(assembly.GetType("Example.LogicalCompatibility.Generated.Mappers.ConfigColumnMapper",true)!,session)!;
            var service=Activator.CreateInstance(assembly.GetType("Example.LogicalCompatibility.Generated.Services.ConfigColumnService",true)!,session,mapper,new SnowflakeIdGenerator(901),new IRecordMutationObserver[]{observer},Array.Empty<IRecordMutationGuard>())!;
            var actor=new RuntimeActor(8,"正常修改",true,Platform,"");
            foreach(var logical in new[]{"decimal","int64","double","time","binary"})
            {
                offline.Set(logical,"");var before=offline.Writes;
                var vo=await Invoke("Update"+Page+"Async",Request("Update","{\"id\":\"20\",\"controlDetail\":\"仅改配置\"}"),null,actor,CancellationToken.None);
                Assert.Equal(logical,Get(vo,"LogicalType"));Assert.Equal("",Get(vo,"ColumnType"));Assert.Equal("仅改配置",Get(offline.Row!,"ControlDetail"));Assert.Equal(before+1,offline.Writes);
                Assert.Equal(new[]{"control_detail"},offline.LastColumns);Assert.True(offline.ForUpdateObserved);
            }
            foreach(var body in new[]{"{\"id\":\"20\",\"columnType\":\"\"}","{\"id\":\"20\",\"columnType\":null}","{\"id\":\"20\",\"logicalType\":\"decimal\"}","{\"id\":\"20\",\"logicalType\":\"invalid\"}"})
            {
                offline.Set("decimal","");var before=offline.Writes;await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Update"+Page+"Async",Request("Update",body),null,actor,CancellationToken.None));Assert.Equal(before,offline.Writes);
            }
            foreach(var original in new string?[]{null," "})
            {offline.Set("decimal",original);await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Update"+Page+"Async",Request("Update","{\"id\":\"20\",\"controlDetail\":\"配置\"}"),null,actor,CancellationToken.None));}
            offline.Set("invalid","");await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Update"+Page+"Async",Request("Update","{\"id\":\"20\",\"controlDetail\":\"配置\"}"),null,actor,CancellationToken.None));
            offline.Set("decimal","");await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Update"+Page+"Async",Request("Update","{\"id\":\"20\",\"controlDetail\":\"配置\"}"),null,actor with{AccountName="改变逻辑"},CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Create"+Page+"Async",Request("Create","{\"logicalType\":\"decimal\",\"columnType\":\"\"}"),null,actor,CancellationToken.None));
            offline.Set("decimal","");await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Copy"+Page+"Async",20L,null,actor,CancellationToken.None));
            offline.Set("decimal","");await Assert.ThrowsAsync<ArgumentException>(()=>Invoke("Update99Async",Request("Update","{\"id\":\"20\",\"controlDetail\":\"配置\"}","99"),null,actor,CancellationToken.None));
            offline.Set("decimal","");observer.Fail=true;var snapshot=offline.Snapshot();var rollbacks=offline.Rollbacks;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Invoke("Update"+Page+"Async",Request("Update","{\"id\":\"20\",\"controlDetail\":\"应回滚\"}"),null,actor,CancellationToken.None));
            Assert.Equal(snapshot,offline.Snapshot());Assert.Equal(rollbacks+1,offline.Rollbacks);Assert.Equal(0,offline.RawCommands);

            object Request(string kind,string body,string page=Page)=>JsonSerializer.Deserialize(body,assembly.GetType($"Example.LogicalCompatibility.Generated.DTOs.ConfigColumnPage{page}{kind}Request",true)!,JsonOptions())!;
            async Task<object> Invoke(string method,params object?[] args){var task=(Task)service.GetType().GetMethod(method)!.Invoke(service,args)!;await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;}
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    [Fact]
    public void Compatibility_is_exactly_fixed_platform_page_and_does_not_change_original_metadata_or_ui_required_rule()
    {
        var model=GenerationModel.Create(Catalog(),"Example.FixedTypeProof");var table=model.Tables.Single();var page=table.Pages.Single(value=>value.Source.Id==Page);
        Assert.NotEmpty(PlatformColumnTypeCompatibilityCompilation.Capture(model,table,page));
        foreach(var change in new Action<MetadataCatalog>[]{c=>c.YzProjectId="7",c=>c.ConfigTables.Single().Id="7",c=>c.ConfigTables.Single().TableName="other_column",c=>c.ConfigPages.Single(value=>value.Id==Page).Id="7",c=>c.ConfigColumns.Single(value=>value.ColumnName=="column_type").Id="7",c=>c.ConfigColumns.Single(value=>value.ColumnName=="logical_type").Id="7"})
        {
            var catalog=Catalog();change(catalog);
            // 身份门禁直接使用受控模型变体，不为了测试未知身份修补其余元数据关联。
            var changed=GenerationModel.Create(Catalog(),"Example.FixedTypeProof");var changedTable=changed.Tables.Single();var changedPage=changedTable.Pages.Single(value=>value.Source.Id==Page);
            if(catalog.YzProjectId!=Platform)changed.Catalog.YzProjectId=catalog.YzProjectId;
            changedTable.Source.Id=catalog.ConfigTables.Single().Id;changedTable.Source.TableName=catalog.ConfigTables.Single().TableName;changedPage.Source.Id=catalog.ConfigPages[0].Id;
            foreach(var column in changedTable.Columns)column.Source.Id=catalog.ConfigColumns.Single(value=>value.ColumnName==column.Source.ColumnName).Id;
            Assert.Empty(PlatformColumnTypeCompatibilityCompilation.Capture(changed,changedTable,changedPage));
        }
        var platform=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(BackendRoot(),"metadata/platform.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var physical=platform.ConfigColumns.Single(column=>column.Id=="1670726649369513985");var field=platform.ConfigPageColumns.Single(value=>value.Id=="1670726649369513986");
        Assert.Equal(1,physical.IsDbNotNull);Assert.Equal(1,field.IsRequired);Assert.Equal("1",field.Extra["isMust"].ToString());
        var actual=GenerationModel.Create(platform,"Example.OriginalTypeRule");var actualTable=actual.Tables.Single(value=>value.Source.Id==Table);var actualPage=actualTable.Pages.Single(value=>value.Source.Id==Page);
        var detail=OriginalUniappDetailRenderer.Render(actual,actualTable,actualPage,[]);Assert.Contains("columnType:{rules:[{\"required\":true",detail);
        // 已实现的逻辑类型必须保持强类型映射；固定页面豁免不能收窄普通字段能力。
        foreach(var (logicalType,expectedType) in new[]{("time","TimeSpan?"),("binary","byte[]?")})
        {
            var metadata=Catalog();metadata.ConfigColumns.Single(value=>value.ColumnName=="control_detail").LogicalType=logicalType;
            var supported=GenerationModel.Create(metadata,"Example.UnchangedCapability");
            Assert.Equal(expectedType,supported.Tables.Single().Columns.Single(value=>value.Source.ColumnName=="control_detail").Type);
            Assert.Equal(logicalType,metadata.ConfigColumns.Single(value=>value.ColumnName=="control_detail").LogicalType);
        }
        // 真正未知的类型仍在生成阶段拒绝，不能借兼容旧空物理类型绕过类型合同。
        foreach(var unsupported in new[]{"geometry","xml","invalid"})
        {
            var metadata=Catalog();metadata.ConfigColumns.Single(value=>value.ColumnName=="control_detail").LogicalType=unsupported;
            var error=Assert.Throws<InvalidOperationException>(()=>GenerationModel.Create(metadata,"Example.UnchangedCapability"));
            Assert.Contains("未知逻辑类型："+unsupported,error.Message);
        }
    }

    /// <summary>固定平台字段身份与原必填保持一致，另加普通页面证明豁免不会扩散。</summary>
    private static MetadataCatalog Catalog()
    {
        var catalog=new MetadataCatalog{YzProjectId=Platform};catalog.ConfigTables.Add(new(){Id=Table,TableName="config_column",Name="字段",YzProjectId=Platform});
        Add("1","id","int64");Add("2","yz_project_id","int64");Add("3","delete_no","datetime");Add("4","column_name","string");Add("5","control_detail","string");
        Add("1670726649369513985","column_type","string",true);Add("2200000000000000129","logical_type","string",true);
        foreach(var page in new[]{Page,"99"})
        {catalog.ConfigPages.Add(new(){Id=page,ConfigTableId=Table,Name="字段配置",IsNew=1,IsSave=1,IsCopy=1});foreach(var column in catalog.ConfigColumns.Where(value=>!new[]{"id","yz_project_id","delete_no"}.Contains(value.ColumnName)))catalog.ConfigPageColumns.Add(new(){Id=page+column.Id,ConfigPageId=page,ConfigColumnId=column.Id,IsEdit=1,IsShow=1,IsRequired=column.IsDbNotNull});}
        return catalog;
        void Add(string id,string name,string type,bool required=false)=>catalog.ConfigColumns.Add(new(){Id=id,ConfigTableId=Table,ColumnName=name,Name=name=="column_type"?"数据类型":name,LogicalType=type,IsDbNotNull=required?1:0});
    }
    private static object? Get(object row,string name)=>row.GetType().GetProperty(name)!.GetValue(row);
    private static JsonSerializerOptions JsonOptions(){var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);options.Converters.Add(new StringLongJsonConverter());return options;}
    private static string BackendRoot([CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../.."));
    private sealed class TailObserver:IRecordMutationObserver{internal bool Fail;public Task BeforeCommitAsync(RecordMutationContext context,CancellationToken cancellationToken=default)=>Fail?Task.FromException(new InvalidOperationException("模拟尾部观察器失败")):Task.CompletedTask;}

    /// <summary>离线执行真实生成ORM调用并保存事务前副本，禁止建立任何SQL命令或网络连接。</summary>
    private sealed class OfflineOrm(Assembly assembly):IDatabaseProvider
    {
        internal object? Row;internal int Writes,Rollbacks,RawCommands;internal string[] LastColumns=[];internal bool ForUpdateObserved;
        private readonly Type entity=assembly.GetType("Example.LogicalCompatibility.Generated.Entities.ConfigColumn",true)!;
        internal void Set(string logical,string? physical){Row=Activator.CreateInstance(entity)!;entity.GetProperty("Id")!.SetValue(Row,20L);entity.GetProperty("YzProjectId")!.SetValue(Row,long.Parse(Platform));entity.GetProperty("ColumnName")!.SetValue(Row,"total_amount");entity.GetProperty("LogicalType")!.SetValue(Row,logical);entity.GetProperty("ColumnType")!.SetValue(Row,physical);}
        internal string Snapshot()=>JsonSerializer.Serialize(Row,entity);
        private object? Clone(object? value)=>value is null?null:JsonSerializer.Deserialize(JsonSerializer.Serialize(value,entity),entity);
        internal IFreeSql Orm=>Make<IFreeSql>((method,args)=>method.Name=="Select"?Select(method.ReturnType):method.Name is "Update" or "Insert"?Command(method.ReturnType):throw new NotSupportedException(method.Name));
        private object Select(Type type)
        {
            var conditions=new List<LambdaExpression>();object? query=null;query=Make(type,(method,args)=>
            {
                if(method.Name=="Where"&&args![0] is LambdaExpression condition)conditions.Add(condition);
                if(method.Name=="ForUpdate")ForUpdateObserved=true;
                if(method.Name is "FirstAsync" or "ToOneAsync")
                {
                    // 只投影命中原谓词的副本；不影响完整写锁副本与回滚快照。
                    var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var row=Row is not null&&conditions.All(condition=>(bool)condition.Compile().DynamicInvoke(Row)!)?Clone(Row):null;
                    return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(projection?.ReturnType??entity).Invoke(null,[row is null||projection is null?row:projection.Compile().DynamicInvoke(row)]);
                }
                return query;
            });return query;
        }
        private object Command(Type type)
        {
            object? command=null,source=null;string[] columns=[];command=Make(type,(method,args)=>
            {
                if(method.Name=="SetSource")source=args![0];if(method.Name=="UpdateColumns")columns=(string[])args![0]!;
                if(method.Name=="ExecuteAffrowsAsync")
                {
                    Writes++;LastColumns=columns;
                    if(source is not null){foreach(var column in columns){var property=entity.GetProperty(GenerationModel.Pascal(column))!;property.SetValue(Row,property.GetValue(source));}}
                    return Task.FromResult(1);
                }
                return command;
            });return command;
        }
        public string Name=>"mysql";public DatabaseCapabilities Capabilities=>new("mysql",true,false);public void Validate(DatabaseOptions options){}
        public IFreeSql CreateOrm(DatabaseOptions options)=>throw new NotSupportedException();public DbConnection CreateConnection(DatabaseOptions options)=>new Connection(this);public string QuoteIdentifier(string name)=>new MySqlDatabaseProvider().QuoteIdentifier(name);public string ParameterPlaceholder(string name)=>"@"+name;
        private static T Make<T>(Func<MethodInfo,object?[]?,object?> call)where T:class=>(T)Make(typeof(T),call);
        private static object Make(Type type,Func<MethodInfo,object?[]?,object?> call){var value=DispatchProxy.Create(type,typeof(CallProxy));((CallProxy)value).Call=call;return value;}
        private sealed class Connection(OfflineOrm owner):DbConnection
        {
            private ConnectionState state;[System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="";public override string Database=>"offline";public override string DataSource=>"offline";public override string ServerVersion=>"offline";public override ConnectionState State=>state;public override void ChangeDatabase(string value)=>throw new NotSupportedException();public override void Open()=>state=ConnectionState.Open;public override void Close()=>state=ConnectionState.Closed;protected override DbTransaction BeginDbTransaction(IsolationLevel level)=>new Transaction(this,owner);protected override DbCommand CreateDbCommand(){owner.RawCommands++;throw new InvalidOperationException("离线测试不允许数据库命令。");}
        }
        private sealed class Transaction(Connection connection,OfflineOrm owner):DbTransaction
        {private readonly object? before=owner.Clone(owner.Row);public override IsolationLevel IsolationLevel=>IsolationLevel.ReadCommitted;protected override DbConnection DbConnection=>connection;public override void Commit(){}public override void Rollback(){owner.Rollbacks++;owner.Row=owner.Clone(before);}}
    }
    public class CallProxy:DispatchProxy{internal Func<MethodInfo,object?[]?,object?> Call=null!;protected override object? Invoke(MethodInfo? method,object?[]? args)=>Call(method!,args);}
}
