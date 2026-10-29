using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>共享级联的原生成解析／导出实际执行；R0与派生只编译，不用模拟SQL冒充实库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelSharedGenerationTests
{
    private const string Prefix="shared_excel",Namespace="Example.SharedExcel";
    /// <summary>四个R1物理组合执行双路径聚合；其余R0／派生组合独立编译并保留公开Full门槛。</summary>
    [Theory]
    [InlineData("shared-r1")][InlineData("short-r1")][InlineData("global-r1")][InlineData("global-no-project-r1")]
    [InlineData("shared-r0")][InlineData("short-r0")][InlineData("derived-r1")][InlineData("derived-short-r0")]
    public async Task Shared_paths_compile_and_physical_R1_roundtrips_original_cells(string profile)
    {
        var definition=Definition(profile);var catalog=Catalog(profile);var before=JsonSerializer.Serialize(catalog);
        var model=GenerationModel.Create(catalog,Namespace);var source=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(source.Pages);
        var group=Assert.Single(SharedCrossTableCompilation.ForPage(model,source,page).Groups);Assert.Equal(2,group.Plans.Count);
        var proof=CrossTableExcelApplicationPreparer.RequireCurrent(catalog,Namespace);Assert.NotEmpty(proof.Gaps);
        Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(proof.Baseline,Namespace)));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-shared-cross-excel");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,Namespace);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            Assert.NotNull(assembly.GetType(Namespace+".Generated.Controllers.SharedExcelSourcePage201Controller"));
            var partial=Path.Combine(generated,"Services/SharedExcelSourceService.cs");await File.AppendAllTextAsync(partial,"\n// 共享Excel保留的手写扩展。\n");var bytes=await File.ReadAllBytesAsync(partial);
            new CSharpGenerator().Generate(catalog,generated,Namespace);Assert.Equal(bytes,await File.ReadAllBytesAsync(partial));Assert.Equal(before,JsonSerializer.Serialize(catalog));
            Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
            if(definition.Redundant==1&&!definition.Derived)await Exercise(assembly,catalog,definition.Shorter,definition.Global,definition.NoProject);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>标题只用于独占元数据，原页面生成器／模板及字段归属保持。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var catalog=SharedCrossTableGenerationTests.Catalog(Prefix,d.Type,d.Parent,d.Shorter,d.Derived);
        catalog.ConfigTables.Single(table=>table.Id=="2").IsRongyu=d.Redundant;
        if(d.Global)GlobalSharedCrossGenerationTests.ApplyGlobals(catalog,15,d.NoProject);
        var page=catalog.ConfigPages.Single(page=>page.Id=="201");page.IsImport=1;page.IsExport=1;
        foreach(var (id,name) in new[]{("22","名称"),("25","主路径"),("80","仓库路径")})
        {catalog.ConfigColumns.Single(column=>column.Id==id).Name=name;var field=catalog.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==id);field.IsShow=1;field.IsEdit=1;}
        return catalog;
    }
    /// <summary>明确每个组合，不把未执行的派生／R0 SQL写作解析通过。</summary>
    private static (int Redundant,string Type,string Parent,bool Shorter,bool Derived,bool Global,bool NoProject) Definition(string profile)=>profile switch
    {
        "shared-r1"=>(1,"string","int64",false,false,false,false),"short-r1"=>(1,"int64","string",true,false,false,false),
        "global-r1"=>(1,"string","string",false,false,true,false),"global-no-project-r1"=>(1,"int64","int64",true,false,true,true),
        "shared-r0"=>(0,"string","int64",false,false,false,false),"short-r0"=>(0,"int64","string",true,false,false,false),
        "derived-r1"=>(1,"string","int64",false,true,false,false),"derived-short-r0"=>(0,"int64","string",true,true,false,false),
        _=>throw new ArgumentOutOfRangeException(nameof(profile))
    };
    /// <summary>真实解析器处理两条共享路径，空单元格保留另一控件共享祖先，互斥选择整行拒绝。</summary>
    private static async Task Exercise(Assembly assembly,MetadataCatalog catalog,bool shorter,bool global,bool noProject)
    {
        using var runtime=new SharedExcelReadTransport(assembly,Namespace,"SharedExcelSource");
        var primary=shorter?"国甲/区甲":"国甲/区甲/城甲";var partial=shorter?"国甲/区甲":"国甲/区甲/";var root=shorter?"国甲/":"国甲//";
        Seed("Country",11,"国甲");Seed("Country",12,"国乙");Seed("Area",21,"区甲","SharedExcelCountryId",11);Seed("Area",22,"区乙","SharedExcelCountryId",12);
        Seed("Place",31,"城甲","SharedExcelAreaId",21);Seed("Place",32,"城乙","SharedExcelAreaId",22);Seed("Warehouse",41,"仓甲","SharedExcelAreaId",21);Seed("Warehouse",42,"仓乙","SharedExcelAreaId",22);
        Seed("Country",14,"国甲",status:0);Seed("Country",16,"国甲",deleted:true);
        if(!noProject)Seed("Country",19,"跨项目国",project:2);
        foreach(var reverse in new[]{false,true})
        {
            var ordered=new[]{new KeyValuePair<string,object?>("主路径",primary),new("仓库路径","国甲/区甲/仓甲")};
            var values=await runtime.Parse((reverse?ordered.Reverse():ordered).ToDictionary(pair=>pair.Key,pair=>pair.Value));
            AssertSlots(values,"11","21",shorter?null:"31","41",shorter);
            foreach(var conflict in new[]{new[]{"国乙/区乙/仓乙",primary},new[]{"国甲/区甲/仓甲",root}})
            {
                var input=new[]{new KeyValuePair<string,object?>("主路径",conflict[1]),new("仓库路径",conflict[0])};
                await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse((reverse?input.Reverse():input).ToDictionary(pair=>pair.Key,pair=>pair.Value)));
            }
        }
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",""},{"仓库路径","国甲/区甲/仓甲"}}),"11","21",null,"41",shorter);
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",primary},{"仓库路径",""}}),"11","21",shorter?null:"31",null,shorter);
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",partial},{"仓库路径","国甲/区甲/"}}),"11","21",null,null,shorter);
        AssertSlots(await runtime.Parse(new Dictionary<string,object?>{{"主路径",null},{"仓库路径",""}}),null,null,null,null,shorter);
        var only=await runtime.Parse(new Dictionary<string,object?>{{"主路径",primary}});Assert.False(only.ContainsKey("warehouseId"));
        if(!noProject)
        {
            if(global)Assert.Equal("19",Text((await runtime.Parse(new Dictionary<string,object?>{{"主路径",shorter?"跨项目国/":"跨项目国//"}}))["countryId"]));
            else await Assert.ThrowsAsync<PageImportException>(()=>runtime.Parse(new Dictionary<string,object?>{{"主路径","跨项目国//"}}));
        }
        Source(101,"两条完整",11,21,shorter?null:31,41);Source(102,"主已清末级",11,21,null,41);Source(103,"仓已清末级",11,21,shorter?null:31,null);Source(104,"共同根",11,null,null,null);Source(105,"全部清空",null,null,null,null);
        var locks=runtime.Reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));
        var exported=await runtime.Export();var names=exported.Headers.ToList().IndexOf("名称");var main=exported.Headers.ToList().IndexOf("主路径");var second=exported.Headers.ToList().IndexOf("仓库路径");Assert.True(names>=0&&main>=0&&second>=0);
        var cells=exported.Rows.ToDictionary(row=>row[names]!.ToString()!,row=>(Main:row[main]?.ToString(),Second:row[second]?.ToString()));
        Assert.Equal((primary,"国甲/区甲/仓甲"),cells["两条完整"]);Assert.Equal((partial,"国甲/区甲/仓甲"),cells["主已清末级"]);Assert.Equal((primary,"国甲/区甲/"),cells["仓已清末级"]);Assert.Equal((root,"国甲//"),cells["共同根"]);Assert.Equal(((string?)null,(string?)null),cells["全部清空"]);
        foreach(var pair in cells)
        {
            var values=await runtime.Parse(new Dictionary<string,object?>{{"主路径",pair.Value.Main},{"仓库路径",pair.Value.Second}});
            if(pair.Key=="共同根")AssertSlots(values,"11",null,null,null,shorter);
            else if(pair.Key=="全部清空")AssertSlots(values,null,null,null,null,shorter);
            else AssertSlots(values,"11","21",shorter||pair.Key=="主已清末级"?null:"31",pair.Key=="仓已清末级"?null:"41",shorter);
        }
        Assert.Equal(1,runtime.Snapshots);Assert.Equal(locks,runtime.ExportLockCount);Assert.Empty(runtime.Transport.Commands);
        Assert.Contains("scope:status",runtime.Reads);Assert.Contains(runtime.Reads,value=>value.StartsWith("lock:",StringComparison.Ordinal));
        // 故障放在导出反查的目标列表内：拒权／取消／读取超时必须原样越过输入错误转换。
        foreach(var failure in new Exception[]{new UnauthorizedAccessException("受控拒权"),new OperationCanceledException("受控取消"),new TimeoutException("受控目标读取超时")})
        {
            runtime.FindFailure=failure;var observed=await Record.ExceptionAsync(()=>runtime.Export());
            Assert.Same(failure,observed);Assert.Null(runtime.FindFailure);
        }
        // 导出也必须拒绝同父重名，不能只在导入入口检查唯一性。
        Seed("Warehouse",43,"仓甲","SharedExcelAreaId",21);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Export());
        Assert.True(Assert.IsType<PageImportException>(error.InnerException).IsInputError);Assert.Equal(5,runtime.Snapshots);
        return;
        void Seed(string suffix,long id,string name,string? parent=null,long parentId=0,int status=1,long project=9007199254740993,bool deleted=false)
        {
            var fields=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"Status",status}};
            if(!noProject)fields["YzProjectId"]=project;if(deleted)fields["DeleteNo"]=DateTime.UtcNow;if(parent is not null)fields[parent]=parentId;
            runtime.Add("SharedExcel"+suffix,fields);
        }
        void Source(long id,string name,long? country,long? area,long? place,long? warehouse)
        {
            var fields=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"YzProjectId",9007199254740993L},{"CountryId",country},{"AreaId",area},{"WarehouseId",warehouse}};if(!shorter)fields["PlaceId"]=place;
            runtime.Add("SharedExcelSource",fields);
        }
    }
    /// <summary>只核对真实解析结果中的ID；短控件没有独立城市槽。</summary>
    private static void AssertSlots(Dictionary<string,JsonElement> values,string? country,string? area,string? place,string? warehouse,bool shorter)
    {Assert.Equal(country,Text(values["countryId"]));Assert.Equal(area,Text(values["areaId"]));if(!shorter)Assert.Equal(place,Text(values["placeId"]));Assert.Equal(warehouse,Text(values["warehouseId"]));}
    /// <summary>精确整数文本和null分开比较。</summary>
    private static string? Text(JsonElement value)=>value.ValueKind==JsonValueKind.Null?null:value.ToString();
}

/// <summary>只替代R1物理ORM读取传输，生成方法及表达式真实执行，未知SQL／接口调用立即失败。</summary>
internal sealed class SharedExcelReadTransport:IDisposable
{
    internal readonly TrustedSecurityMemoryProvider Transport=new();internal readonly List<string> Reads=[];
    private readonly Dictionary<Type,List<object>> rows=[];private readonly Assembly assembly;private readonly string rootNamespace;private readonly Type serviceType;private readonly object service;private readonly SnapshotProvider provider;private readonly string sourceEntity;private readonly bool sourceCondition;
    private readonly RuntimeActor actor=new(901,"共享Excel解析",false,"9007199254740993","5");
    internal DatabaseSession Session{get;}
    internal int Snapshots=>provider.Snapshots;
    internal Exception? FindFailure{get;set;}
    internal int ExportLockCount{get;private set;}
    /// <summary>仅用实际生成Mapper与Service；额外目标list授权、ADO写入和网络访问均拒绝。</summary>
    internal SharedExcelReadTransport(Assembly assembly,string rootNamespace,string serviceName,bool sourceCondition=false)
    {
        this.assembly=assembly;this.rootNamespace=rootNamespace;sourceEntity=serviceName;this.sourceCondition=sourceCondition;
        var orm=(IFreeSql)Proxy(typeof(IFreeSql),(method,args)=>method.Name=="Select"?Select(method.GetGenericArguments()[0]):throw new InvalidOperationException("共享Excel离线读取禁止ORM写入或联网。"));
        provider=new(Transport,orm);Session=new(new(){Provider="mysql",ExpectedDatabase="offline"},orm,provider);
        var mapper=Activator.CreateInstance(assembly.GetType(rootNamespace+".Generated.Mappers."+serviceName+"Mapper",true)!,Session)!;
        serviceType=assembly.GetType(rootNamespace+".Generated.Services."+serviceName+"Service",true)!;
        service=Activator.CreateInstance(serviceType,Session,mapper,new SnowflakeIdGenerator(951),Array.Empty<IRecordMutationObserver>(),Array.Empty<IRecordMutationGuard>(),new ImportOnly(actor))!;
    }
    /// <summary>只向明确生成类型赋值，未知属性不被忽略；键值转换保持精确整数。</summary>
    internal void Add(string entity,IReadOnlyDictionary<string,object?> fields)
    {
        var type=assembly.GetType(rootNamespace+".Generated.Entities."+entity,true)!;var row=Activator.CreateInstance(type)!;
        foreach(var pair in fields){var property=type.GetProperty(pair.Key)??throw new InvalidOperationException("离线种子属性未生成："+pair.Key);var target=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;property.SetValue(row,pair.Value is null?null:Convert.ChangeType(pair.Value,target,CultureInfo.InvariantCulture));}
        if(!rows.TryGetValue(type,out var values))rows[type]=values=[];values.Add(row);
    }
    /// <summary>真实私有解析加入原会话事务；不直接调用共享工具伪造生成方法已接线。</summary>
    internal Task<Dictionary<string,JsonElement>> Parse(Dictionary<string,object?> row)=>Session.InTransactionAsync(async()=>
    {
        var method=serviceType.GetMethod("ParseImport201Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var arguments=method.GetParameters().Select(parameter=>parameter.Name switch{"row"=>(object?)JsonSerializer.SerializeToElement(row),"parentId"=>null,"actor"=>actor,"existing"=>null,"keyOnly"=>false,"cancellationToken"=>CancellationToken.None,_=>throw new InvalidOperationException("未知生成解析参数。")}).ToArray();
        try{return await (Task<Dictionary<string,JsonElement>>)method.Invoke(service,arguments)!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    });
    /// <summary>公开导出方法实际产生原列及单元格，记录导出结束时的锁数。</summary>
    internal async Task<PageExportRequest> Export(PageRequest? request=null)
    {
        try{return await (Task<PageExportRequest>)serviceType.GetMethod("Export201Async")!.Invoke(service,[request??new PageRequest(),actor,100,CancellationToken.None])!;}
        finally{ExportLockCount=Reads.Count(value=>value.StartsWith("lock:",StringComparison.Ordinal));}
    }
    /// <summary>表达式按真实委托执行；固定SQL只接受明确的状态或来源复合条件，不忽略范围。</summary>
    private object Select(Type type)
    {
        var predicates=new List<Func<object,bool>>();var orders=new List<(Func<object,object?> Key,bool Desc)>();var limit=int.MaxValue;object query=null!;
        query=Proxy(typeof(ISelect<>).MakeGenericType(type),(method,args)=>
        {
            args??=[];
            if(method.Name=="WithTransaction")return query;
            if(method.Name=="Where")
            {
                if(args.OfType<LambdaExpression>().SingleOrDefault() is{} expression){var compiled=expression.Compile();predicates.Add(row=>(bool)compiled.DynamicInvoke(row)!);}
                else
                {
                    var sql=Assert.IsType<string>(args[0]);var parameters=Assert.IsAssignableFrom<IReadOnlyDictionary<string,object?>>(args[1]);
                    predicates.Add(FixedWhere(type,sql,parameters));
                }
                return query;
            }
            if(method.Name is "OrderBy" or "OrderByDescending")
            {
                var expression=Assert.Single(args.OfType<LambdaExpression>());var compiled=expression.Compile();orders.Add((row=>compiled.DynamicInvoke(row),method.Name=="OrderByDescending"));return query;
            }
            if(method.Name=="Limit"){limit=Assert.IsType<int>(args[0]);return query;}
            if(method.Name=="ForUpdate"){Assert.NotNull(Session.CurrentTransaction);Reads.Add("lock:"+type.Name);return query;}
            if(method.Name is not("ToListAsync" or "FirstAsync"))throw new InvalidOperationException("共享Excel离线传输未实现："+method.Name);
            if(method.Name=="ToListAsync"&&type.Name=="SharedExcelCountry"&&FindFailure is{} failure){FindFailure=null;throw failure;}
            IEnumerable<object> filtered=rows.GetValueOrDefault(type,[]).Where(row=>predicates.All(predicate=>predicate(row)));
            IOrderedEnumerable<object>? ordered=null;foreach(var order in orders)ordered=ordered is null?order.Desc?filtered.OrderByDescending(order.Key):filtered.OrderBy(order.Key):order.Desc?ordered.ThenByDescending(order.Key):ordered.ThenBy(order.Key);
            filtered=(ordered??filtered).Take(limit);var projection=args.OfType<LambdaExpression>().SingleOrDefault();var output=projection?.ReturnType??type;var compiledProjection=projection?.Compile();
            if(method.Name=="FirstAsync"){var row=filtered.FirstOrDefault();return TaskValue(output,row is null||compiledProjection is null?row:compiledProjection.DynamicInvoke(row));}
            var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(output))!;foreach(var row in filtered)list.Add(compiledProjection is null?row:compiledProjection.DynamicInvoke(row));return TaskValue(list.GetType(),list);
        });return query;
    }
    /// <summary>只解释status或明确来源模型的三个AND参数比较；括号／运算／字段／值均固定，未知语法拒绝。</summary>
    private Func<object,bool> FixedWhere(Type type,string sql,IReadOnlyDictionary<string,object?> parameters)
    {
        const string comparison=@"\x60(status|code|amount)\x60\s*(>=|=)\s*@([A-Za-z0-9_]+)";
        var matches=Regex.Matches(sql,comparison).Cast<Match>().ToArray();var structure=Regex.Replace(Regex.Replace(sql,comparison,"#"),@"\s+","");
        Assert.Matches(@"^\(*#\)*(?:AND\(*#\)*)*$",structure);
        var balance=0;foreach(var value in structure){if(value=='(')balance++;if(value==')')balance--;Assert.True(balance>=0,"固定SQL括号次序无效。");}Assert.Equal(0,balance);
        Assert.Equal(matches.Length,parameters.Count);Assert.Equal(matches.Length,matches.Select(match=>match.Groups[1].Value).Distinct(StringComparer.Ordinal).Count());
        if(matches.Length!=1){Assert.True(sourceCondition&&type.Name==sourceEntity,"来源复合条件只在明确模型生效。");Assert.Equal(3,matches.Length);Assert.Equal(new[]{"amount","code","status"},matches.Select(match=>match.Groups[1].Value).Order(StringComparer.Ordinal));}
        else Assert.Equal("status",matches[0].Groups[1].Value);
        var tests=new List<Func<object,bool>>();
        foreach(var match in matches)
        {
            var field=match.Groups[1].Value;var operation=match.Groups[2].Value;var expected=parameters[match.Groups[3].Value];
            Assert.Equal(field=="amount"?">=":"=",operation);
            if(field=="code")Assert.Equal("e",Assert.IsType<string>(expected));else Assert.Equal(field=="amount"?0.02m:1m,Convert.ToDecimal(expected,CultureInfo.InvariantCulture));
            var property=type.GetProperty(GenerationModel.Pascal(field))??throw new InvalidOperationException("固定条件属性缺失。");
            tests.Add(row=>property.GetValue(row) is{} value&&(field=="code"?string.Equals(Convert.ToString(value,CultureInfo.InvariantCulture),(string?)expected,StringComparison.Ordinal):operation==">="?Convert.ToDecimal(value,CultureInfo.InvariantCulture)>=Convert.ToDecimal(expected,CultureInfo.InvariantCulture):Convert.ToDecimal(value,CultureInfo.InvariantCulture)==Convert.ToDecimal(expected,CultureInfo.InvariantCulture)));
            Reads.Add("scope:"+field);
        }
        return row=>tests.All(test=>test(row));
    }
    /// <summary>只释放当前离线会话。</summary>
    public void Dispose()=>Session.Dispose();
    /// <summary>通用异步返回只包装传输结果。</summary>
    private static object TaskValue(Type type,object? value)=>typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null,[value])!;
    /// <summary>只创建传输接口代理，不替换生成业务对象。</summary>
    private static object Proxy(Type type,Func<MethodInfo,object?[]?,object?> action){var proxy=DispatchProxy.Create(type,typeof(GeneratedMutationGuardRun.CallProxy));((GeneratedMutationGuardRun.CallProxy)proxy).Action=action;return proxy;}
    /// <summary>解析／路径导出不增加目标list要求；公共入口来源授权另由正式用例覆盖。</summary>
    private sealed class ImportOnly(RuntimeActor actor):IRuntimeAuthorization
    {public RuntimeActor RequireActor()=>actor;public void RequirePagePermission(string page,string operation)=>throw new UnauthorizedAccessException("离线解析不能新增目标list权限。");}
    /// <summary>快照调用只记录请求，内部仍为离线ADO，不证明MySQL隔离和锁。</summary>
    private sealed class SnapshotProvider(TrustedSecurityMemoryProvider inner,IFreeSql orm):IDatabaseProvider
    {
        internal int Snapshots;public string Name=>"offline";public DatabaseCapabilities Capabilities=>inner.Capabilities;
        public void Validate(DatabaseOptions options)=>inner.Validate(options);public IFreeSql CreateOrm(DatabaseOptions options)=>orm;public DbConnection CreateConnection(DatabaseOptions options)=>inner.CreateConnection(options);
        public string QuoteIdentifier(string identifier)=>inner.QuoteIdentifier(identifier);public string ParameterPlaceholder(string name)=>inner.ParameterPlaceholder(name);public string BuildSelectForUpdate(string sql)=>inner.BuildSelectForUpdate(sql);
        public ValueTask<DbTransaction> BeginReadSnapshotAsync(DbConnection connection,CancellationToken token=default){Snapshots++;return connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,token);}
        public Task EnsureReadSnapshotAsync(DbTransaction transaction,CancellationToken token=default)=>throw new InvalidOperationException("离线传输不证明嵌套快照。");
    }
}
