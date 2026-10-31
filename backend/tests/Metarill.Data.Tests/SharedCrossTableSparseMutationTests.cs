using System.Collections.ObjectModel;
using Metarill.Core.Cascades;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>生成服务使用同请求状态区分稀疏原值和完整路径，不靠客户端声明原字段。</summary>
public sealed class SharedCrossTableSparseMutationTests
{
    /// <summary>局部改末级先按原逻辑祖先协调，再一次打包；旧其它分支被清除，真实写后路径保持。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Partial_changed_leaf_clears_other_branch_and_verifies_packed_frontier(string type)
    {
        var f=Fixture(type);var current=f.Raw.ToDictionary();current["place_id"]=Value(type,11);var old=new List<string>();var now=new List<string>();var result=await f.State.NormalizeAsync(current,Set("place_id"),false,Reader(type,now),Reader(type,old));Assert.Equal(Value(type,3),result.Values["country_id"]);Assert.True(result.Values["warehouse_id"] is null or "");Assert.Equal(2,old.Count);var packed=f.State.Pack()!;Assert.Single(packed,p=>p.Value is not null);Assert.Equal(Value(type,11),packed["place_id"]);await f.State.VerifyStoredAsync(packed,Reader(type));Assert.Equal(Value(type,20),f.Raw["warehouse_id"]);
    }
    /// <summary>同父改城市保留仓库；预先恢复和钩子后才发现变化得到同一结果，原路径缓存不重复读。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Same_parent_hook_change_preserves_other_selection_and_old_snapshot_is_cached(bool hydrate)
    {
        var f=Fixture("string");var old=new List<string>();var current=(hydrate?await f.State.HydrateAsync(Reader("string",old)):f.Raw).ToDictionary();current["place_id"]="12";var result=await f.State.NormalizeAsync(current,Set(),false,Reader("string"),Reader("string",old));Assert.Equal("20",result.Values["warehouse_id"]);Assert.Equal("1",result.Values["country_id"]);Assert.Equal(2,old.Count);var packed=f.State.Pack()!;Assert.Equal(2,packed.Count(p=>p.Value is not null));Assert.Null(packed["area_id"]);await f.State.VerifyStoredAsync(packed,Reader("string"));
    }
    /// <summary>整组省略不查询旧目标或重新打包；DML触发器偷改原始ID仍拒绝。</summary>
    [Fact]public async Task Omitted_group_uses_no_path_reads_but_raw_storage_is_verified()
    {
        var f=Fixture("string");var calls=new List<string>();var result=await f.State.NormalizeAsync(f.Raw,Set("business_name"),false,Reader("string",calls),Reader("string",calls));Assert.Empty(calls);Assert.Empty(result.Fields);Assert.Null(f.State.Pack());await f.State.VerifyStoredAsync(f.Ids,Reader("string",calls));Assert.Empty(calls);var changed=f.Ids.ToDictionary();changed["warehouse_id"]="21";await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.VerifyStoredAsync(changed,Reader("string")));
    }
    /// <summary>普通选择器依赖逻辑祖先时恢复只读预期，关系仍省略；目标父被DML改写仍拒绝。</summary>
    [Fact]public async Task Dependent_foreign_write_restores_parent_without_repacking_omitted_group()
    {
        var f=Fixture("string");var old=new List<string>();var result=await f.State.NormalizeAsync(f.Raw,Set("item_id"),false,(id,input,token)=>throw new InvalidOperationException("未选择新路径"),Reader("string",old),restoreForDependencies:true);Assert.Equal("1",result.Values["country_id"]);Assert.Equal("2",result.Values["area_id"]);Assert.Empty(result.Fields);Assert.Null(f.State.Pack());Assert.Equal(2,old.Count);await f.State.VerifyStoredAsync(f.Ids,Reader("string"));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.VerifyStoredAsync(f.Ids,async(id,input,token)=>{var values=(await Reader("string")(id,input,token)).ToDictionary();values["country_id"]="3";values["area_id"]="4";return values;}));Assert.Null(f.Ids["country_id"]);
    }
    /// <summary>明确全清不查询坏旧目标；字符串空值也以原R0合同写成空稀疏集合。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Explicit_full_clear_avoids_unresolvable_old_paths(bool emptyStrings)
    {
        var f=Fixture("string");var current=f.Raw.ToDictionary();foreach(var key in f.Ids.Keys)current[key]=emptyStrings?"":null;var calls=new List<string>();await f.State.NormalizeAsync(current,Set(f.Ids.Keys.ToArray()),false,Reader("string",calls),(id,input,token)=>throw new InvalidOperationException("不得读取旧节点"));var packed=f.State.Pack()!;Assert.All(packed.Values,Assert.Null);await f.State.VerifyStoredAsync(packed,Reader("string"));Assert.All(calls,id=>Assert.NotEqual("old",id));
    }
    /// <summary>只清一个独立叶子仍保留另一分支，公共祖先不丢失，最后仅存仓库。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Clearing_own_leaf_keeps_shared_ancestors_from_other_frontier(string type)
    {
        var f=Fixture(type);var current=f.Raw.ToDictionary();current["place_id"]=null;var result=await f.State.NormalizeAsync(current,Set("place_id"),false,Reader(type),Reader(type));Assert.Equal(Value(type,1),result.Values["country_id"]);Assert.Equal(Value(type,2),result.Values["area_id"]);var packed=f.State.Pack()!;Assert.Single(packed,p=>p.Value is not null);Assert.Equal(Value(type,20),packed["warehouse_id"]);await f.State.VerifyStoredAsync(packed,Reader(type));
    }
    /// <summary>短控件没有持久化末端时，从仓库一次恢复公共地区；不读取空分支。</summary>
    [Fact]public async Task Short_control_restores_shared_primary_from_only_stored_long_branch()
    {
        var f=Fixture("string",true);var calls=new List<string>();var logical=await f.State.HydrateAsync(Reader("string",calls));Assert.Equal("2",logical["area_id"]);Assert.Equal(new[]{"41"},calls);var current=logical.ToDictionary();current["area_id"]="4";var result=await f.State.NormalizeAsync(current,Set("area_id"),false,Reader("string"),Reader("string",calls));Assert.Equal("3",result.Values["country_id"]);Assert.True(result.Values["warehouse_id"] is null or "");Assert.Single(calls);var packed=f.State.Pack()!;Assert.Single(packed,p=>p.Value is not null);Assert.Equal("4",packed["area_id"]);await f.State.VerifyStoredAsync(packed,Reader("string"));
    }
    /// <summary>创建无原值读取，复制式两末端及独立表相同ID仍整组保存。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public async Task Creating_two_frontiers_does_not_query_original_or_duplicate_ancestors(string type)
    {
        var f=Fixture(type);var current=f.Raw.ToDictionary();current["warehouse_id"]=Value(type,10);await f.State.NormalizeAsync(current,Set("place_id","warehouse_id"),true,Reader(type),(id,input,token)=>throw new InvalidOperationException("新增无旧值"));var packed=f.State.Pack()!;Assert.Equal(2,packed.Count(p=>p.Value is not null));Assert.Null(packed["country_id"]);Assert.Null(packed["area_id"]);await f.State.VerifyStoredAsync(packed,Reader(type));
    }
    /// <summary>Source ID未变但目标父路径被触发器改成另一条有效路径，也不能绕过预期逻辑核验。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Changed_actual_parent_or_target_name_rejects_after_DML(bool nameOnly)
    {
        var f=Fixture("string");await f.State.NormalizeAsync(f.Raw,Set("place_id"),false,Reader("string"),Reader("string"));var packed=f.State.Pack()!;await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.VerifyStoredAsync(packed,async(id,input,token)=>{var result=(await Reader("string")(id,input,token)).ToDictionary();if(nameOnly)result["country_name"]="触发器改名";else{result["country_id"]="3";result["area_id"]="4";}return result;}));
    }
    /// <summary>歧义的旧原始位、两个新选择冲突和回调扩权分别拒绝，不把部分结果写入调用者。</summary>
    [Theory][InlineData("old")][InlineData("new")][InlineData("callback")]
    public async Task Invalid_inputs_and_paths_do_not_produce_packed_writes(string kind)
    {
        var f=Fixture("string");if(kind=="old"){var raw=f.Raw.ToDictionary();raw["area_id"]="2";var state=new SharedCrossTableSparseMutation(f.Selection,f.Coordinator,raw);await Assert.ThrowsAsync<InvalidOperationException>(()=>state.HydrateAsync(Reader("string")));return;}var current=f.Raw.ToDictionary();current["place_id"]="11";if(kind=="new")current["warehouse_id"]="20";
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.NormalizeAsync(current,Set("place_id","warehouse_id"),true,kind=="callback"?(id,input,token)=>Task.FromResult<IReadOnlyDictionary<string,object?>>(new Dictionary<string,object?>(input){["extra_id"]="1"}):Reader("string"),Reader("string")));Assert.Throws<InvalidOperationException>(()=>f.State.Pack());Assert.Equal("20",f.Raw["warehouse_id"]);
    }
    /// <summary>组和协调器必须一致，取消与重复生命周期不能产生越序存储验证。</summary>
    [Fact]public async Task Lifecycle_and_group_contracts_are_explicit()
    {
        var f=Fixture("string");Assert.Throws<InvalidOperationException>(()=>f.State.Pack());await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.VerifyStoredAsync(f.Ids,Reader("string")));using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.State.HydrateAsync(Reader("string"),cancel.Token));await f.State.NormalizeAsync(f.Raw,Set(),false,Reader("string"),Reader("string"));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.HydrateAsync(Reader("string")));await Assert.ThrowsAsync<InvalidOperationException>(()=>f.State.NormalizeAsync(f.Raw,Set(),false,Reader("string"),Reader("string")));Assert.Throws<ArgumentException>(()=>new SharedCrossTableSparseMutation(f.Selection,f.Coordinator,new Dictionary<string,object?>()));
    }
    /// <summary>共享存储元数据与协调规则使用同一槽位，真实SQL权限由生成回调负责。</summary>
    private static (SharedCrossTableSparseSelection Selection,SharedCrossTableCoordinator Coordinator,SharedCrossTableSparseMutation State,Dictionary<string,object?> Raw,Dictionary<string,object?> Ids) Fixture(string type,bool shorter=false)
    {
        var c=SharedCrossTableSparseSelectionTests.Catalog(type,shorter);var plan=SharedCrossTableSparseSelection.Compile(c.ConfigTables[0],"31",c.ConfigColumns);var coordinator=new SharedCrossTableCoordinator(plan.Branches.Select(b=>new SharedCrossTableBranch(b.SourceColumnId,b.Slots.Select(s=>new SharedCrossTableSlot(s.ColumnName,s.ColumnName[..^3]+"_name",s.LogicalType,s.ColumnName)).ToArray())).ToArray());var raw=plan.Slots.SelectMany(s=>new[]{s.ColumnName,s.ColumnName[..^3]+"_name"}).ToDictionary(f=>f,_=>(object?)null);if(!shorter)raw["place_id"]=Value(type,10);raw["warehouse_id"]=Value(type,20);return(plan,coordinator,new(plan,coordinator,raw),raw,plan.Slots.ToDictionary(s=>s.ColumnName,s=>raw[s.ColumnName]));
    }
    /// <summary>最深ID反查固定测试路径；返回完整字段，不信任请求中的祖先或名称。</summary>
    private static Func<string,IReadOnlyDictionary<string,object?>,CancellationToken,Task<IReadOnlyDictionary<string,object?>>> Reader(string type,List<string>? calls=null)=>(control,input,token)=>
    {
        token.ThrowIfCancellationRequested();calls?.Add(control);var prefixes=input.Keys.Where(k=>k.EndsWith("_id",StringComparison.Ordinal)).Select(k=>k[..^3]).ToArray();long? selected=null;foreach(var prefix in prefixes)if(input[prefix+"_id"] is{} raw&&raw.ToString()!="")selected=long.Parse(raw.ToString()!,System.Globalization.CultureInfo.InvariantCulture);long[] path=selected switch{1=>[1],2=>[1,2],3=>[3],4=>[3,4],10=>[1,2,10],11=>[3,4,11],12=>[1,2,12],20=>[1,2,20],21=>[3,4,21],null=>[],_=>throw new InvalidOperationException("未登记节点")};var values=new Dictionary<string,object?>(input);for(var i=0;i<prefixes.Length;i++){values[prefixes[i]+"_id"]=i<path.Length?Value(type,path[i]):null;values[prefixes[i]+"_name"]=i<path.Length?prefixes[i]+path[i]:null;}return Task.FromResult<IReadOnlyDictionary<string,object?>>(new ReadOnlyDictionary<string,object?>(values));
    };
    /// <summary>整数与字符串保持各自精确类型。</summary>
    private static object Value(string type,long id)=>type=="string"?id.ToString(System.Globalization.CultureInfo.InvariantCulture):id;
    /// <summary>字段存在性是独立输入，不从空值推断省略。</summary>
    private static HashSet<string> Set(params string[] fields)=>new(fields,StringComparer.Ordinal);
}
