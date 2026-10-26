using System.Collections.ObjectModel;
using Metarill.Core.Cascades;
using Xunit;
namespace Metarill.Data.Tests;
/// <summary>共享级联按已确认规则清理其它下级；所有路径先隔离校验，不修改调用者输入。</summary>
public sealed class SharedCrossTableCoordinatorTests
{
    /// <summary>局部换叶子推导新公共上级，旧其它分支自动清空，不必查询已清除的旧叶子。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Changed_leaf_clears_other_descendants_atomically(string type)
    {
        var c=Coordinator(type);var before=Values(type);var current=new Dictionary<string,object?>(before){["city_id"]=V(type,11)};var calls=new List<(string,long?)>();var result=await c.NormalizeAsync(current,before,Set("city_id"),false,Reader(type,calls));Assert.Equal(V(type,3),result.Values["country_id"]);Assert.Equal(V(type,4),result.Values["area_id"]);Assert.Equal(V(type,11),result.Values["city_id"]);Assert.True(result.Values["warehouse_id"] is null or "");Assert.Equal("area4",result.Values["area_name"]);Assert.Equal(new[]{("a",(long?)11),("b",(long?)4)},calls);Assert.Equal(V(type,20),current["warehouse_id"]);Assert.Contains("warehouse_id",result.Fields);
    }
    /// <summary>只提交公共上级也必须清本组全部旧下级，不能被旧叶子反向覆盖。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Direct_parent_change_clears_both_old_branches(string type)
    {
        var original=Values(type);var values=new Dictionary<string,object?>(original){["country_id"]=V(type,3)};var result=await Coordinator(type).NormalizeAsync(values,original,Set("country_id"),false,Reader(type));Assert.Equal(V(type,3),result.Values["country_id"]);foreach(var id in new[]{"area_id","city_id","warehouse_id"})Assert.True(result.Values[id] is null or "");
    }
    /// <summary>两个新叶子共享同一路径可同时保存，交换元数据顺序不改变结果。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Compatible_new_leaves_merge_and_conflicting_ones_reject(string type)
    {
        var original=Values(type);var values=new Dictionary<string,object?>(original){["city_id"]=V(type,11),["warehouse_id"]=V(type,21)};var result=await Coordinator(type).NormalizeAsync(values,original,Set("city_id","warehouse_id"),false,Reader(type));Assert.Equal(V(type,21),result.Values["warehouse_id"]);Assert.Equal(V(type,4),result.Values["area_id"]);values["warehouse_id"]=V(type,22);await Assert.ThrowsAsync<InvalidOperationException>(()=>Coordinator(type).NormalizeAsync(values,original,Set("city_id","warehouse_id"),false,Reader(type)));Assert.Equal(V(type,1),values["country_id"]);
    }
    /// <summary>整entity中的另一个旧叶子即使被提交，也不能阻止清掉已失效分支。</summary>
    [Fact]public async Task Unchanged_full_entity_field_is_not_another_new_selection()
    {
        var original=Values("string");var values=new Dictionary<string,object?>(original){["city_id"]="11"};var result=await Coordinator("string").NormalizeAsync(values,original,Set("country_id","area_id","city_id","warehouse_id"),false,Reader("string"));Assert.Equal("",result.Values["warehouse_id"]);Assert.Equal("3",result.Values["country_id"]);
    }
    /// <summary>只清自己叶子保留公共祖先及其它有效叶子，不给未提交分支增加查询权限要求。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Clearing_only_own_leaf_keeps_other_selection(string type)
    {
        var original=Values(type);var values=new Dictionary<string,object?>(original){["city_id"]=null};var calls=new List<(string,long?)>();var result=await Coordinator(type).NormalizeAsync(values,original,Set("city_id"),false,Reader(type,calls));Assert.Null(result.Values["city_id"]);Assert.Equal(V(type,20),result.Values["warehouse_id"]);Assert.Single(calls);Assert.DoesNotContain("warehouse_id",result.Fields);
    }
    /// <summary>控件全部清空含公共父级，其它分支也变为空；null与字符串空值保持原形。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Clearing_shared_control_clears_whole_affected_group(bool explicitNull)
    {
        var original=Values("string");var values=new Dictionary<string,object?>(original);foreach(var id in new[]{"country_id","area_id","city_id"})values[id]=explicitNull?null:"";var result=await Coordinator("string").NormalizeAsync(values,original,Set("country_id","area_id","city_id"),false,Reader("string"));Assert.True(result.Values["warehouse_id"] is null or "");Assert.Equal(explicitNull?null:"",result.Values["country_id"]);Assert.Equal(explicitNull?null:"",result.Values["city_id"]);
    }
    /// <summary>省略整组不调用路径委托，保持原字段和缓存。</summary>
    [Fact]public async Task Omitted_group_does_not_read_or_write()
    {
        var original=Values("string");var calls=new List<(string,long?)>();var result=await Coordinator("string").NormalizeAsync(original,original,Set("business_name"),false,Reader("string",calls));Assert.Empty(calls);Assert.Empty(result.Fields);Assert.Equal(original,result.Values);
    }
    /// <summary>短控件的叶子也是共享父时，先处理长控件不能吞掉短控件的新输入。</summary>
    [Fact]public async Task Shared_primary_leaf_keeps_its_original_new_selection_until_validated()
    {
        var slots=Slots("string");var coordinator=new SharedCrossTableCoordinator([new("z",slots.Take(2).ToArray()),new("a",new[]{slots[0],slots[1],Slot("warehouse","string")})]);var original=Values("string");original.Remove("city_id");original.Remove("city_name");var values=new Dictionary<string,object?>(original){["area_id"]="4",["warehouse_id"]="22"};await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.NormalizeAsync(values,original,Set("area_id","warehouse_id"),false,Reader("string")));Assert.Equal("4",values["area_id"]);
    }
    /// <summary>新增省略父级由真正叶子推导，另一个控件同步显示公共祖先。</summary>
    [Fact]public async Task Creating_from_one_leaf_populates_shared_prefix()
    {
        var values=Values("string");foreach(var key in values.Keys.ToArray())values[key]=null;values["city_id"]="11";var result=await Coordinator("string").NormalizeAsync(values,new Dictionary<string,object?>(),Set("city_id"),true,Reader("string"));Assert.Equal("3",result.Values["country_id"]);Assert.Equal("4",result.Values["area_id"]);Assert.Null(result.Values["warehouse_id"]);
    }
    /// <summary>失败或取消不把部分结果写入调用者字典，回调不能额外返回字段。</summary>
    [Fact]public async Task Callback_contract_and_cancellation_preserve_input()
    {
        var values=Values("string");using var stop=new CancellationTokenSource();stop.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Coordinator("string").NormalizeAsync(values,values,Set("city_id"),false,Reader("string"),stop.Token));await Assert.ThrowsAsync<InvalidOperationException>(()=>Coordinator("string").NormalizeAsync(values,values,Set("city_id"),false,(id,input,token)=>Task.FromResult<IReadOnlyDictionary<string,object?>>(new Dictionary<string,object?>(input){["unknown_id"]="1"})));Assert.Equal("1",values["country_id"]);
    }
    /// <summary>类型、范围、祖先、重复控件及不相连的定义均不能形成组。</summary>
    [Theory][InlineData("type")][InlineData("scope")][InlineData("ancestor")][InlineData("duplicate")][InlineData("disconnected")]
    public void Invalid_frozen_layout_rejects(string kind)
    {
        var a=Slots("string");var b=new[]{a[0],a[1],Slot("warehouse","string")};if(kind=="type")b[0]=b[0] with{LogicalType="int64"};if(kind=="scope")b[0]=b[0] with{ScopeKey="different"};if(kind=="ancestor")b=[Slot("region","string"),a[1],b[2]];if(kind=="disconnected")b=[Slot("region","string"),Slot("district","string")];Assert.Throws<ArgumentException>(()=>new SharedCrossTableCoordinator([new("a",a),new(kind=="duplicate"?"a":"b",b)]));
    }
    /// <summary>第三分支只共享国家，改第一分支国家仍清理第三分支的地区／港口，不能只处理相邻两个控件。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Three_connected_branches_clear_all_affected_descendants(string type)
    {
        var a=Slots(type);var coordinator=new SharedCrossTableCoordinator([new("a",a),new("b",new[]{a[0],a[1],Slot("warehouse",type)}),new("c",new[]{a[0],Slot("district",type),Slot("port",type)})]);var original=Values(type);original["district_id"]=V(type,8);original["district_name"]="district8";original["port_id"]=V(type,90);original["port_name"]="port90";var current=new Dictionary<string,object?>(original){["city_id"]=V(type,11)};var result=await coordinator.NormalizeAsync(current,original,Set("city_id"),false,Reader(type));Assert.Equal(V(type,3),result.Values["country_id"]);Assert.Equal(V(type,4),result.Values["area_id"]);foreach(var prefix in new[]{"warehouse","district","port"}){Assert.True(result.Values[prefix+"_id"] is null or "");Assert.True(result.Values[prefix+"_name"] is null or "");}Assert.Equal(V(type,90),current["port_id"]);
    }
    /// <summary>只构造固定测试规则；实际数据库权限与行锁由生成的回调验证。</summary>
    private static SharedCrossTableCoordinator Coordinator(string type)=>new([new("a",Slots(type)),new("b",new[]{Slot("country",type),Slot("area",type),Slot("warehouse",type)})]);
    /// <summary>同层字段的范围指纹固定且跨控件一致。</summary>
    private static SharedCrossTableSlot Slot(string prefix,string type)=>new(prefix+"_id",prefix+"_name",type,prefix);
    /// <summary>默认主分支仅与另一分支共用前两级。</summary>
    private static SharedCrossTableSlot[] Slots(string type)=>[Slot("country",type),Slot("area",type),Slot("city",type)];
    /// <summary>精确大整数以类型自己的形状传递，不经过double。</summary>
    private static object V(string type,long value)=>type=="string"?value.ToString(System.Globalization.CultureInfo.InvariantCulture):value;
    /// <summary>缓存不作为可信输入，固定读回调重算。</summary>
    private static Dictionary<string,object?> Values(string type)=>new(StringComparer.Ordinal){{"country_id",V(type,1)},{"country_name","country1"},{"area_id",V(type,2)},{"area_name","area2"},{"city_id",V(type,10)},{"city_name","city10"},{"warehouse_id",V(type,20)},{"warehouse_name","warehouse20"}};
    /// <summary>只记录冻结控件的规范路径查找，模拟目标读取而不写数据库。</summary>
    private static Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> Reader(string type,List<(string,long?)>? calls=null)=>(id,input,token)=>
    {
        token.ThrowIfCancellationRequested();var result=new Dictionary<string,object?>(input);var prefixes=input.Keys.Where(k=>k.EndsWith("_id",StringComparison.Ordinal)).Select(k=>k[..^3]).ToArray();long? selected=null;var depth=-1;for(var i=0;i<prefixes.Length;i++)if(input[prefixes[i]+"_id"] is{} raw&&raw.ToString()!=""){selected=long.Parse(raw.ToString()!,System.Globalization.CultureInfo.InvariantCulture);depth=i;}calls?.Add((id,selected));long[] path=selected switch{1=>[1],3=>[3],2=>[1,2],4=>[3,4],10=>[1,2,10],11=>[3,4,11],20=>[1,2,20],21=>[3,4,21],22=>[5,6,22],null=>[],_=>throw new InvalidOperationException("未登记测试节点。")};for(var i=0;i<prefixes.Length;i++){var prefix=prefixes[i];if(i<path.Length){result[prefix+"_id"]=V(type,path[i]);result[prefix+"_name"]=prefix+path[i];}else{var empty=type=="string"&&input[prefix+"_id"] is not null?(object)"":null;result[prefix+"_id"]=empty;result[prefix+"_name"]=empty is null?null:"";}}return Task.FromResult<IReadOnlyDictionary<string,object?>>(new ReadOnlyDictionary<string,object?>(result));
    };
    /// <summary>字段存在性属于独立输入，不能从值是否为空猜测。</summary>
    private static HashSet<string> Set(params string[] fields)=>new(fields,StringComparer.Ordinal);
}
