using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using FreeSql;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>实际生成并编译Controller/Service，内存传输只执行强类型谓词，不代替MySQL聚合验收。</summary>
public sealed class OriginalListStatisticsTests(OriginalStatisticsFixture fixture):IClassFixture<OriginalStatisticsFixture>
{
    /// <summary>分页只影响本页，合计与两个百分比共用真实项目、筛选、逻辑删除范围。</summary>
    [Fact]
    public async Task Original_page_map_statistics_keep_scopes_nulls_and_unpaged_totals()
    {
        using var run=new StatisticsRun(fixture);var result=await run.Page(new(){PageNo=1,PageSize=1,Filters=new(){{"name",JsonSerializer.SerializeToElement("目标")}}});
        var statistics=result.GetProperty("map").GetProperty("statistics");
        Assert.Equal(0m,statistics.GetProperty("amount").GetProperty("currentPage").GetDecimal());
        Assert.Equal(10m,statistics.GetProperty("amount").GetProperty("total").GetDecimal());
        foreach(var field in new[]{"status","averageStatus"})
        {
            Assert.Equal(0m,statistics.GetProperty(field).GetProperty("currentPage").GetDecimal());
            Assert.Equal(50m,statistics.GetProperty(field).GetProperty("total").GetDecimal());
        }
        Assert.Equal(2,result.GetProperty("data").GetProperty("total").GetInt64());Assert.Single(result.GetProperty("data").GetProperty("records").EnumerateArray());
        Assert.False(result.GetProperty("data").TryGetProperty("statistics",out _));
        Assert.All(run.Provider.Calls.Where(call=>call.Method=="SumAsync"||call.Method=="CountAsync"),call=>Assert.Equal(0,call.Page));
        Assert.Equal(new[]{"301:list"},run.Authorization.Checks);Assert.Equal(1,run.Authorization.ActorCalls);
    }

    /// <summary>分母为零时沿原Java省略百分比键，合计仍为零，不能返回NaN或虚构100%。</summary>
    [Fact]
    public async Task Empty_results_keep_original_empty_ratio_objects_and_zero_sums()
    {
        using var run=new StatisticsRun(fixture);var result=await run.Page(new(){Filters=new(){{"name",JsonSerializer.SerializeToElement("没有该名称")}}});
        var statistics=result.GetProperty("map").GetProperty("statistics");
        Assert.Equal(0m,statistics.GetProperty("amount").GetProperty("total").GetDecimal());
        Assert.Equal(0m,statistics.GetProperty("amount").GetProperty("currentPage").GetDecimal());
        Assert.Empty(statistics.GetProperty("status").EnumerateObject());Assert.Empty(statistics.GetProperty("averageStatus").EnumerateObject());
        Assert.Equal(3,run.Provider.Calls.Count);Assert.Single(run.Provider.Calls,call=>call.Method=="CountAsync");Assert.Single(run.Provider.Calls,call=>call.Method=="SumAsync");
    }

    /// <summary>选中集合仍进入原查询白名单，统计不能跨出本次所选记录范围。</summary>
    [Fact]
    public async Task Selected_ids_restrict_statistics_and_records_together()
    {
        using var run=new StatisticsRun(fixture);var result=await run.Page(new(){SelectedIds=["1"]});
        var stats=result.GetProperty("map").GetProperty("statistics");
        Assert.Equal(10m,stats.GetProperty("amount").GetProperty("total").GetDecimal());
        Assert.Equal(100m,stats.GetProperty("status").GetProperty("total").GetDecimal());
        Assert.Equal(1,result.GetProperty("data").GetProperty("total").GetInt64());
    }

    /// <summary>无原统计开关时不增加SQL或map字段，不能因本次恢复改变所有普通分页的成本。</summary>
    [Fact]
    public async Task Disabled_page_keeps_original_controller_and_no_statistic_method()
    {
        var catalog=OriginalStatisticsFixture.Catalog(false);var model=GenerationModel.Create(catalog,"Example.Statistics");var table=Assert.Single(model.Tables);var page=Assert.Single(table.Pages);
        Assert.Null(OriginalListStatisticsCompilation.ForPage(model,table,page));Assert.Empty(OriginalListStatisticsCompilation.Service(model,table,page));
        Assert.DoesNotContain("statistics",OriginalListStatisticsCompilation.Controller(page),StringComparison.OrdinalIgnoreCase);
        using var run=new StatisticsRun(fixture,false);var response=await run.Page(new());Assert.False(response.GetProperty("map").TryGetProperty("statistics",out _));Assert.Equal(2,run.Provider.Calls.Count);
    }

    /// <summary>统计字段必须已进入列表可读集合，隐藏字段和不支持的字符串求和不能旁路泄露。</summary>
    [Fact]
    public void Hidden_statistics_are_ignored_and_invalid_visible_numeric_declaration_fails_before_output()
    {
        var catalog=OriginalStatisticsFixture.Catalog();var field=catalog.ConfigPageColumns.Single(field=>field.ColumnName=="name");field.Extra["sumKind"]=JsonSerializer.SerializeToElement(1);
        var model=GenerationModel.Create(catalog,"Example.Statistics");var table=Assert.Single(model.Tables);var page=Assert.Single(table.Pages);
        Assert.Throws<InvalidOperationException>(()=>OriginalListStatisticsCompilation.ForPage(model,table,page));
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        var output=Path.Combine(fixture.DirectoryPath,"invalid-statistics");Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(catalog,output,"Example.InvalidStatistics"));Assert.False(Directory.Exists(output));
        field.IsShow=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);
        model=GenerationModel.Create(catalog,"Example.Statistics");table=Assert.Single(model.Tables);page=Assert.Single(table.Pages);
        Assert.DoesNotContain(OriginalListStatisticsCompilation.ForPage(model,table,page)!,statistic=>statistic.Column.JsonName=="name");
    }

    /// <summary>原比例条件为数值等于2，不能擅自把bool真值、文本或日期解释为这个数值。</summary>
    [Theory]
    [InlineData("bool")][InlineData("string")][InlineData("datetime")]
    public void Ratio_does_not_guess_non_numeric_value_two(string type)
    {
        var catalog=OriginalStatisticsFixture.Catalog();catalog.ConfigColumns.Single(column=>column.ColumnName=="status").LogicalType=type;
        var model=GenerationModel.Create(catalog,"Example.Statistics");var table=Assert.Single(model.Tables);var page=Assert.Single(table.Pages);
        Assert.Throws<InvalidOperationException>(()=>OriginalListStatisticsCompilation.ForPage(model,table,page));
    }
    /// <summary>后端与原前端共用同一开关解释，字符串true不能额外触发查询。</summary>
    [Theory]
    [InlineData("1",true)][InlineData("true",true)][InlineData("\"1\"",true)][InlineData("\"true\"",false)][InlineData("0",false)][InlineData("null",false)]
    public void Statistics_switch_matches_original_frontend_metadata_values(string json,bool enabled)
    {
        var catalog=OriginalStatisticsFixture.Catalog();using var value=JsonDocument.Parse(json);catalog.ConfigPages[0].Extra["isSum"]=value.RootElement.Clone();
        var model=GenerationModel.Create(catalog,"Example.Statistics");var table=Assert.Single(model.Tables);var page=Assert.Single(table.Pages);
        Assert.Equal(enabled,OriginalListStatisticsCompilation.ForPage(model,table,page)is not null);
        Assert.Equal(enabled,OriginalUniappListRenderer.Render(model,table,page).Contains("合计:",StringComparison.Ordinal));
    }
    /// <summary>保留原float运算后按两位四舍五入；107/4000在原口径为2.67而非精确小数的2.68。</summary>
    [Theory]
    [InlineData(1L,6L,"16.67")][InlineData(2L,3L,"66.67")][InlineData(107L,4000L,"2.67")][InlineData(0L,2L,"0")][InlineData(2L,2L,"100")][InlineData(long.MaxValue,1L,"922337203685477580800")]
    public void Percentage_keeps_original_binary_float_rounding(long matching,long total,string expected)=>
        Assert.Equal(decimal.Parse(expected,System.Globalization.CultureInfo.InvariantCulture),LegacyStatisticsMath.Percentage(matching,total));

    /// <summary>权限拒绝发生在任何数据库读取前，统计不另取未认证身份。</summary>
    [Fact]
    public async Task Missing_page_permission_never_starts_statistics_or_page_reads()
    {
        using var run=new StatisticsRun(fixture);run.Authorization.Deny=true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>run.Page(new()));Assert.Empty(run.Provider.Calls);
    }

    /// <summary>页脚结构与文案保持，只保护等待/重置时空statistics对象的属性访问。</summary>
    [Fact]
    public void Footer_keeps_original_labels_and_handles_empty_state_without_new_ui()
    {
        var source=File.ReadAllText(Path.Combine(fixture.DirectoryPath,"generated/frontend/pages/yzp/stat_record-list-301.vue"));
        Assert.Contains("合计:{{statistics.amount && statistics.amount.total}}",source);
        Assert.Contains("全部:{{statistics.status && statistics.status.total}}%",source);
        Assert.Contains("平均:{{statistics.averageStatus && statistics.averageStatus.total}}%",source);
    }
}

/// <summary>固定公开元数据与一次真实生成编译，不读取私有配置或创建数据库。</summary>
public sealed class OriginalStatisticsFixture:IAsyncLifetime
{
    internal string DirectoryPath {get;}=Path.Combine(Path.GetTempPath(),"metarill-original-statistics",Guid.NewGuid().ToString("N"));
    internal Assembly Assembly {get;private set;}=null!;
    /// <summary>生成启用/禁用两套命名空间，一次真实编译并装载。</summary>
    public async Task InitializeAsync()
    {
        new CSharpGenerator().Generate(Catalog(),Path.Combine(DirectoryPath,"generated"),"Example.Statistics");
        new CSharpGenerator().Generate(Catalog(false),Path.Combine(DirectoryPath,"disabled"),"Example.NoStatistics");
        Assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(DirectoryPath);
    }
    /// <summary>只删除本夹具边界内的自有临时源码。</summary>
    public Task DisposeAsync()=>TestDirectoryCleanup.DeleteAsync(DirectoryPath,Path.Combine(Path.GetTempPath(),"metarill-original-statistics"));
    /// <summary>表和页面的所有读取列均由元数据生成，项目归属与软删除列真实存在。</summary>
    internal static MetadataCatalog Catalog(bool enabled=true)
    {
        var catalog=new MetadataCatalog{YzProjectId="54",YzProjectName="统计离线样本",DatabaseProvider="mysql",
            ConfigTables=[new(){Id="100",TableName="stat_record",Name="统计记录",YzProjectId="54"}],
            ConfigPages=[new(){Id="301",Name="统计记录",ConfigTableId="100",YzProjectId="54",YzAdminTypeId="55",IsNew=0,IsSave=0,IsDelete=0,Extra=new(){{"isSum",JsonSerializer.SerializeToElement(enabled?1:0)}}}],
            YzAdminTypes=[new(){Id="55",Name="样本后台",YzProjectId="54"}]};
        var definitions=new[]{("id","int64"),("delete_no","datetime"),("yz_project_id","int64"),("name","string"),("amount","decimal"),("status","int32"),("average_status","int32")};
        for(var i=0;i<definitions.Length;i++)
        {
            var(name,type)=definitions[i];var id=(101+i).ToString();
            catalog.ConfigColumns.Add(new(){Id=id,Name=name,ConfigTableId="100",YzProjectId="54",ColumnName=name,LogicalType=type,ColumnType=type,ColumnLength=type=="string"?"255":"",IsDbNotNull=name is "id" or "yz_project_id"?1:0});
            if(name=="delete_no")continue;
            var field=new PageColumnMetadata{Id=(201+i).ToString(),Name=name,ConfigPageId="301",ConfigColumnId=id,ColumnName=name,YzProjectId="54",IsEdit=0,IsShow=i>=3?1:0};
            field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(field.IsShow);
            if(name=="name")field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
            if(name is "amount" or "status" or "average_status")field.Extra["sumKind"]=JsonSerializer.SerializeToElement(name=="amount"?1:name=="status"?2:3);
            catalog.ConfigPageColumns.Add(field);
        }
        return catalog;
    }
}

/// <summary>只替换ADO/ORM传输，执行实际生成方法及表达式，未知调用立即失败。</summary>
internal sealed class StatisticsRun:IDisposable
{
    internal StatisticsMemoryProvider Provider {get;}=new();
    internal StatisticsAuthorization Authorization {get;}=new();
    private readonly DatabaseSession session;
    private readonly object controller;
    /// <summary>真实生成类型使用同一个禁止联网/写入的传输，字段不手写替代。</summary>
    internal StatisticsRun(OriginalStatisticsFixture fixture,bool statistics=true)
    {
        var prefix=statistics?"Example.Statistics":"Example.NoStatistics";
        var entity=fixture.Assembly.GetType(prefix+".Generated.Entities.StatRecord",true)!;
        // 只构造已知七列样本，跨项目及已删除行都属于本测试。
        object Row(long id,long project,string name,decimal? amount,int? status,int? average,DateTime? deleted=null)
        {
            var row=Activator.CreateInstance(entity)!;
            foreach(var(key,value)in new Dictionary<string,object?>{{"Id",id},{"YzProjectId",project},{"Name",name},{"Amount",amount},{"Status",status},{"AverageStatus",average},{"DeleteNo",deleted}})
                entity.GetProperty(key)!.SetValue(row,value);
            return row;
        }
        Provider.Rows=[Row(1,54,"目标甲",10m,2,2),Row(2,54,"目标乙",null,1,null),Row(3,54,"其他",5m,2,2),Row(4,56,"目标外项目",1000m,2,2),Row(5,54,"目标已删除",999m,2,2,DateTime.UtcNow)];
        session=new(new(){ConnectionString="offline",ExpectedDatabase="offline"},Provider.Orm,Provider);
        var mapper=Activator.CreateInstance(fixture.Assembly.GetType(prefix+".Generated.Mappers.StatRecordMapper",true)!,session)!;
        var service=Activator.CreateInstance(fixture.Assembly.GetType(prefix+".Generated.Services.StatRecordService",true)!,session,mapper,new SnowflakeIdGenerator(941),Array.Empty<Metarill.Core.Persistence.IRecordMutationObserver>(),Array.Empty<Metarill.Core.Persistence.IRecordMutationGuard>())!;
        controller=Activator.CreateInstance(fixture.Assembly.GetType(prefix+".Generated.Controllers.StatRecordPage301Controller",true)!,service,Authorization)!;
    }
    /// <summary>调用真实Controller并按产品JSON选项核对原R.map合同。</summary>
    internal async Task<JsonElement> Page(PageRequest request)
    {
        var task=(Task)controller.GetType().GetMethod("Page")!.Invoke(controller,[request,CancellationToken.None])!;await task;
        return JsonSerializer.SerializeToElement(task.GetType().GetProperty("Result")!.GetValue(task),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    /// <summary>释放本次自有会话，不接触其他测试或服务。</summary>
    public void Dispose()=>session.Dispose();
}

internal sealed class StatisticsAuthorization:IRuntimeAuthorization
{
    internal bool Deny;internal int ActorCalls;internal List<string> Checks {get;}=[];
    /// <summary>测试身份仍由授权器提供，客户端统计请求不能选择项目。</summary>
    public RuntimeActor RequireActor(){ActorCalls++;return new(1,"离线操作者",false,"54","55");}
    /// <summary>先拒绝未授权页面，确保没有先取统计后验权限。</summary>
    public void RequirePagePermission(string page,string operation){Checks.Add(page+":"+operation);if(Deny)throw new UnauthorizedAccessException("离线模拟无页权限。");}
}

internal sealed class StatisticsMemoryProvider:IDatabaseProvider
{
    internal object[] Rows=[];internal List<(string Method,int Page)> Calls {get;}=[];
    internal IFreeSql Orm {get;}
    /// <summary>仅注册可审计的Select代理，不建立连接。</summary>
    internal StatisticsMemoryProvider(){Orm=DispatchProxy.Create<IFreeSql,StatisticsOrmProxy>();((StatisticsOrmProxy)(object)Orm).Provider=this;}
    public string Name=>"offline";public DatabaseCapabilities Capabilities=>new("offline",true,false);
    /// <summary>离线配置只用于定位测试，不解析数据库凭据。</summary>
    public void Validate(DatabaseOptions options){}
    /// <summary>所有读取统一回到本实例代理。</summary>
    public IFreeSql CreateOrm(DatabaseOptions options)=>Orm;
    /// <summary>若测试意外进入ADO立即失败。</summary>
    public DbConnection CreateConnection(DatabaseOptions options)=>throw new InvalidOperationException("统计离线传输禁止连接数据库。");
    /// <summary>仅复用现有MySQL标识符格式，不执行SQL。</summary>
    public string QuoteIdentifier(string value)=>new MySqlDatabaseProvider().QuoteIdentifier(value);
    /// <summary>保留固定参数占位风格。</summary>
    public string ParameterPlaceholder(string value)=>"@"+value;
    /// <summary>统计是普通读取，意外写锁立即失败。</summary>
    public string BuildSelectForUpdate(string sql)=>throw new InvalidOperationException("统计读取不得加写锁。");
}
public class StatisticsOrmProxy:DispatchProxy
{
    internal StatisticsMemoryProvider Provider=null!;
    /// <summary>只允许强类型Select和Dispose，不提供写入或原生SQL旁路。</summary>
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        if(method!.Name=="Dispose")return null;
        if(method.Name=="Select")
        {
            var type=method.GetGenericArguments()[0];var create=typeof(StatisticsOrmProxy).GetMethod(nameof(Create),BindingFlags.NonPublic|BindingFlags.Instance)!.MakeGenericMethod(type);
            return create.Invoke(this,null);
        }
        throw new InvalidOperationException("统计ORM未支持调用："+method.Name);
    }
    /// <summary>每条实际生成查询获得独立筛选/排序/分页状态。</summary>
    private object Create<T>()where T:class
    {
        var query=DispatchProxy.Create<ISelect<T>,StatisticsSelectProxy<T>>();((StatisticsSelectProxy<T>)(object)query).Provider=Provider;return query;
    }
}
public class StatisticsSelectProxy<T>:DispatchProxy where T:class
{
    internal StatisticsMemoryProvider Provider=null!;
    private readonly List<Delegate> predicates=[];private readonly List<(Delegate Key,bool Descending)> orders=[];
    private int page,size;
    /// <summary>真实生成谓词在强类型实体上执行，分页和聚合调用各自记录，禁止未知API静默成功。</summary>
    protected override object? Invoke(MethodInfo? method,object?[]? args)
    {
        var name=method!.Name;args??=[];
        var lambda=args.OfType<LambdaExpression>().FirstOrDefault();
        if(name=="WithTransaction")return this;
        if(name=="Where"){predicates.Add(lambda!.Compile());return this;}
        if(name is "OrderBy" or "OrderByDescending"){orders.Add((lambda!.Compile(),name=="OrderByDescending"));return this;}
        if(name=="Page"){page=(int)args[0]!;size=(int)args[1]!;return this;}
        var rows=Provider.Rows.Cast<T>().Where(row=>predicates.All(predicate=>(bool)predicate.DynamicInvoke(row)!));
        if(name=="CountAsync"){Provider.Calls.Add((name,page));return Task.FromResult(rows.LongCount());}
        if(name=="SumAsync"){Provider.Calls.Add((name,page));var selector=lambda!.Compile();return Task.FromResult(rows.Sum(row=>Convert.ToDecimal(selector.DynamicInvoke(row)??0,System.Globalization.CultureInfo.InvariantCulture)));}
        if(name=="ToListAsync")
        {
            Provider.Calls.Add((name,page));IOrderedEnumerable<T>? sorted=null;
            foreach(var order in orders){Func<T,object?> key=row=>order.Key.DynamicInvoke(row);sorted=sorted is null?(order.Descending?rows.OrderByDescending(key):rows.OrderBy(key)):(order.Descending?sorted.ThenByDescending(key):sorted.ThenBy(key));}
            rows=sorted??rows;if(page>0)rows=rows.Skip((page-1)*size).Take(size);return Task.FromResult(rows.ToList());
        }
        throw new InvalidOperationException("统计Select未支持调用："+name);
    }
}
