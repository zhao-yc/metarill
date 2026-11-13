using System.Collections;
using System.Data.Common;
using Metarill.Core.Exports;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实生成/严格编译八种存储键组合；R1实际解析只替代ADO/ORM传输，不冒充实库或公开完整包验收。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelLosslessGenerationTests
{
    /// <summary>八组合实际编译；R1执行解析和导出，R0不假装执行SQL。</summary>
    [Theory]
    [InlineData("string","string",1)][InlineData("string","int64",1)][InlineData("int64","string",1)][InlineData("int64","int64",1)]
    [InlineData("string","string",0)][InlineData("string","int64",0)][InlineData("int64","string",0)][InlineData("int64","int64",0)]
    public async Task Real_generated_import_export_compile_and_R1_parser_restores_all_levels(string sourceType,string parentType,int redundant)
    {
        var catalog=CrossTableExcelImportColumnsTests.Catalog("pathexcel",sourceType);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "45" or "55"))column.LogicalType=parentType;
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=redundant;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").IsShow=1;
        catalog.ConfigColumns.Add(new(){Id="28",ConfigTableId="2",ColumnName="country_id",Name="祖先ID",LogicalType=sourceType,LinkConfigTableId="3"});
        catalog.ConfigPageColumns.Add(new(){Id="20128",ConfigPageId="201",ConfigColumnId="28",IsShow=0,IsEdit=1,SortNo=30});
        if(redundant==1)
        {
            var scoped=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;
            scoped.Id="202";scoped.Extra.Remove("type");scoped.WhereSql+=" AND country_id=@parent_id";catalog.ConfigPages.Add(scoped);
            foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").ToArray())
            {var copy=JsonSerializer.Deserialize<Metarill.Core.Metadata.PageColumnMetadata>(JsonSerializer.Serialize(field))!;copy.Id="202"+field.Id;copy.ConfigPageId="202";catalog.ConfigPageColumns.Add(copy);}
        }
        var before=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-lossless");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generator=new CSharpGenerator();generator.Generate(catalog,Path.Combine(directory,"generated"),"Example.PathExcel");
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType("Example.PathExcel.Generated.Controllers.PathexcelSourcePage201Controller"));Assert.Equal(before,JsonSerializer.Serialize(catalog));
            if(redundant==1)await ExerciseParser(assembly);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>真实私有解析器在原DatabaseSession事务中运行；按父/项目/状态过滤，不返回预设成功ID。</summary>
    private static async Task ExerciseParser(Assembly assembly)
    {
        DatabaseSession? currentSession=null;var transport=new TrustedSecurityMemoryProvider();var rows=new Dictionary<Type,List<object>>();var reads=new List<string>();
        object Select(Type type)
        {
            var predicates=new List<Func<object,bool>>();var take=int.MaxValue;object query=null!;
            query=Proxy(typeof(ISelect<>).MakeGenericType(type),(method,args)=>{
                args??=[];
                if(method.Name=="Where")
                {
                    var expression=args!.OfType<LambdaExpression>().SingleOrDefault();
                    if(expression is not null){var predicate=expression.Compile();predicates.Add(row=>(bool)predicate.DynamicInvoke(row)!);}
                    else
                    {
                        // 仅解释本夹具实际发布的单一status等式；未知SQL必须失败，不能静默忽略范围。
                        var sql=Assert.IsType<string>(args[0]);var match=Regex.Match(sql,@"^\(*\s*\x60status\x60\s*=\s*@([A-Za-z0-9_]+)\s*\)*$");
                        Assert.True(match.Success,"离线解析器不支持该固定条件SQL");var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(args[1]);Assert.Single(parameters);
                        var expected=Convert.ToInt32(parameters[match.Groups[1].Value],System.Globalization.CultureInfo.InvariantCulture);Assert.Equal(1,expected);
                        predicates.Add(row=>Convert.ToInt32(type.GetProperty("Status")!.GetValue(row),System.Globalization.CultureInfo.InvariantCulture)==expected);reads.Add("scope:status");
                    }
                    return query;
                }
                if(method.Name=="Limit"){take=(int)args![0]!;return query;}
                if(method.Name=="ForUpdate"){Assert.NotNull(currentSession?.CurrentTransaction);reads.Add("lock:"+type.Name);}
                var filtered=rows.GetValueOrDefault(type,[]).Where(row=>predicates.All(predicate=>predicate(row))).Take(take).ToArray();
                if(method.Name=="ToListAsync")
                {var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var output=projection?.ReturnType??type;var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(output))!;foreach(var row in filtered)list.Add(projection is null?row:projection.Compile().DynamicInvoke(row));return TaskValue(list.GetType(),list);}
                if(method.Name=="FirstAsync")
                {var projection=args!.OfType<LambdaExpression>().SingleOrDefault();var row=filtered.FirstOrDefault();return TaskValue(projection?.ReturnType??type,row is null||projection is null?row:projection.Compile().DynamicInvoke(row));}
                return query;
            });return query;
        }
        var orm=(IFreeSql)Proxy(typeof(IFreeSql),(method,args)=>method.Name=="Select"?Select(method.GetGenericArguments()[0]):throw new InvalidOperationException("解析测试禁止写ORM或联网"));
        var provider=new ExcelSnapshotTransport(transport,orm);using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="offline"},orm,provider);currentSession=session;
        var actor=new RuntimeActor(901,"路径解析",false,"9007199254740993","1");var authorization=new ImportOnly(actor);
        var mapper=Activator.CreateInstance(assembly.GetType("Example.PathExcel.Generated.Mappers.PathexcelSourceMapper")!,session)!;
        var type=assembly.GetType("Example.PathExcel.Generated.Services.PathexcelSourceService")!;
        var service=Activator.CreateInstance(type,session,mapper,new SnowflakeIdGenerator(949),Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(),Array.Empty<Metarill.Core.Persistence.IRecordMutationGuard>(),authorization)!;
        Add("Country",11,"国甲");Add("Country",12,"国乙");Add("Area",21,"区甲","PathexcelCountryId",11);Add("Area",22,"区乙","PathexcelCountryId",12);Add("Place",31,"城市甲","PathexcelAreaId",21);Add("Place",32,"城市乙","PathexcelAreaId",22);
        Add("Country",14,"国甲",status:0);Add("Country",15,"国甲",project:2);Add("Country",16,"国甲",deleted:true);
        Add("Place",34,"9007199254740993","PathexcelAreaId",21);
        var numeric=await Parse(new Dictionary<string,object?>{{"级联",9007199254740993L}});Assert.Equal("34",Text(numeric["placeId"]));
        foreach(var item in new[]{("国甲/区甲/城市甲","11","21","31"),("国甲//","11",null,null),("国甲/区甲/","11","21",null),("城市甲","11","21","31"),("国甲/区甲","11","21",null)})
        {
            var values=await Parse(new Dictionary<string,object?>{{"名称","样本"},{"级联",item.Item1}});
            Assert.Equal(item.Item2,Text(values["countryId"]));Assert.Equal(item.Item3,Text(values["areaId"]));Assert.Equal(item.Item4,Text(values["placeId"]));
        }
        var clear=await Parse(new Dictionary<string,object?>{{"级联",null},{"祖先ID","11"}});Assert.Equal("11",Text(clear["countryId"]));Assert.Null(Text(clear["placeId"]));
        foreach(var reverse in new[]{false,true})
        {
            var entries=new[]{new KeyValuePair<string,object?>("祖先ID","12"),new("级联","国甲//")};
            await Assert.ThrowsAsync<PageImportException>(()=>Parse((reverse?entries.Reverse():entries).ToDictionary(pair=>pair.Key,pair=>pair.Value)));
        }
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲/区乙/城市乙"}}));
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲//"}},"202","12"));
        Assert.Equal("12",Text((await Parse(new Dictionary<string,object?>{{"级联",null}},"202","12"))["countryId"]));
        Assert.Equal("12",Text((await Parse(new Dictionary<string,object?>{{"级联","国乙//"}},"202","12"))["countryId"]));
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"祖先ID","12"},{"级联","国乙//"}},"202","12"));
        var locksBeforeExport=reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));
        AddSource(101,"根",11,null,null);AddSource(102,"中",11,21,null);AddSource(103,"叶",11,21,31);
        var exported=await (Task<PageExportRequest>)type.GetMethod("Export201Async")!.Invoke(service,[new PageRequest(),actor,100,CancellationToken.None])!;
        var names=exported.Headers.ToList().IndexOf("名称");var paths=exported.Headers.ToList().IndexOf("级联");Assert.True(names>=0&&paths>=0);
        var cells=exported.Rows.ToDictionary(row=>row[names]!.ToString()!,row=>row[paths]?.ToString());
        Assert.Equal("国甲//",cells["根"]);Assert.Equal("国甲/区甲/",cells["中"]);Assert.Equal("国甲/区甲/城市甲",cells["叶"]);Assert.Equal(1,provider.Snapshots);Assert.Equal(locksBeforeExport,reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal)));
        Add("Place",33,"国甲","PathexcelAreaId",21);
        await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲"}}));
        Assert.Equal("11",Text((await Parse(new Dictionary<string,object?>{{"级联","国甲//"}}))["countryId"]));
        Add("Country",13,"国甲");await Assert.ThrowsAsync<PageImportException>(()=>Parse(new Dictionary<string,object?>{{"级联","国甲//"}}));
        Assert.Contains("scope:status",reads);Assert.Contains(reads,value=>value.StartsWith("lock:",StringComparison.Ordinal));Assert.Empty(transport.Commands);Assert.Equal(7,transport.Rollbacks);
        return;

        void Add(string name,long id,string label,string? parent=null,long parentId=0,int status=1,long project=9007199254740993,bool deleted=false)
        {
            var entityType=assembly.GetType("Example.PathExcel.Generated.Entities.Pathexcel"+name)!;var row=Activator.CreateInstance(entityType)!;
            void Set(string field,object? value){var property=entityType.GetProperty(field)!;var destination=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;property.SetValue(row,value is null?null:Convert.ChangeType(value,destination,System.Globalization.CultureInfo.InvariantCulture));}
            Set("Id",id);Set("Name",label);Set("YzProjectId",project);Set("Status",status);if(deleted)Set("DeleteNo",DateTime.UtcNow);if(parent is not null)Set(parent,parentId);
            if(!rows.TryGetValue(entityType,out var data))rows[entityType]=data=[];data.Add(row);
        }
        void AddSource(long id,string name,long country,long? area,long? place)
        {
            var entityType=assembly.GetType("Example.PathExcel.Generated.Entities.PathexcelSource")!;var row=Activator.CreateInstance(entityType)!;
            foreach(var (field,value) in new (string,object?)[]{("Id",id),("Name",name),("YzProjectId",9007199254740993L),("Status",1),("CountryId",country),("AreaId",area),("PlaceId",place)})
            {var property=entityType.GetProperty(field)!;var destination=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;property.SetValue(row,value is null?null:Convert.ChangeType(value,destination,System.Globalization.CultureInfo.InvariantCulture));}
            if(!rows.TryGetValue(entityType,out var data))rows[entityType]=data=[];data.Add(row);
        }
        async Task<Dictionary<string,JsonElement>> Parse(Dictionary<string,object?> row,string page="201",string? parent=null)
        {
            return await session.InTransactionAsync(async()=>{
                Task task;try{task=(Task)type.GetMethod("ParseImport"+page+"Async",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[JsonSerializer.SerializeToElement(row),parent,actor,CancellationToken.None])!;}
                catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
                await task;return (Dictionary<string,JsonElement>)task.GetType().GetProperty("Result")!.GetValue(task)!;
            });
        }
    }
    /// <summary>只记录生成导出是否请求适配器快照，内部ADO仍为离线传输，不证明MySQL隔离或锁。</summary>
    private sealed class ExcelSnapshotTransport(TrustedSecurityMemoryProvider inner,IFreeSql orm):IDatabaseProvider
    {
        internal int Snapshots;
        public string Name=>"offline";
        public DatabaseCapabilities Capabilities=>inner.Capabilities;
        public void Validate(DatabaseOptions options)=>inner.Validate(options);
        public IFreeSql CreateOrm(DatabaseOptions options)=>orm;
        public DbConnection CreateConnection(DatabaseOptions options)=>inner.CreateConnection(options);
        public string QuoteIdentifier(string identifier)=>inner.QuoteIdentifier(identifier);
        public string ParameterPlaceholder(string name)=>inner.ParameterPlaceholder(name);
        public string BuildSelectForUpdate(string sql)=>inner.BuildSelectForUpdate(sql);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken token=default)
        {Snapshots++;return connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token);}
        public Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken token=default)=>throw new InvalidOperationException("离线替身不证明嵌套快照");
    }
    /// <summary>统一读取离线回执中的null与精确整数文本。</summary>
    private static string? Text(JsonElement value)=>value.ValueKind==JsonValueKind.Null?null:value.ToString();
    /// <summary>按真实泛型结果构造异步传输，不替代业务方法。</summary>
    private static object TaskValue(Type type,object? value)=>typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value])!;
    /// <summary>仅代理数据库传输接口；业务与生成表达式仍实际执行。</summary>
    private static object Proxy(Type type,Func<MethodInfo,object?[]?,object?> action){var proxy=DispatchProxy.Create(type,typeof(GeneratedMutationGuardRun.CallProxy));((GeneratedMutationGuardRun.CallProxy)proxy).Action=action;return proxy;}
    /// <summary>导入不借用普通CRUD或目标list权；解析器之外的来源授权由公共入口单独验收。</summary>
    private sealed class ImportOnly(RuntimeActor actor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation)=>throw new UnauthorizedAccessException("解析不能增加页面list要求");
    }
}
