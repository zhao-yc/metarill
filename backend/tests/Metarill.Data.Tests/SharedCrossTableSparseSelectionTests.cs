using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>共享非冗余存储必须保留多个独立末端；此基础证明不替代后续SQL、生成与实库验收。</summary>
public sealed class SharedCrossTableSparseSelectionTests
{
    private const long Large=9007199254740993;
    /// <summary>每种部分选择组合保存最少独立节点，不缓存共同祖先；两个表的相同ID仍是两个槽。</summary>
    [Theory][InlineData("string",false)][InlineData("int64",false)][InlineData("string",true)][InlineData("int64",true)]
    public void All_partial_pairs_keep_exact_independent_frontiers(string type,bool reverse)
    {
        var c=Catalog(type);if(reverse)c.ConfigColumns.Reverse();var before=JsonSerializer.Serialize(c);var plan=Compile(c);for(var a=0;a<=3;a++)for(var b=0;b<=3;b++)
        {
            var paths=Paths(new[]{Large,Large+1,Large+2}.Take(a).ToArray(),new[]{Large,Large+1,Large+2}.Take(b).ToArray());var packed=plan.PackValidatedPaths(paths);var expected=a==3&&b==3?2:Math.Max(a,b)>0?1:0;Assert.Equal(expected,packed.Count(p=>p.Value is not null));Assert.Equal(expected,plan.ReadStored(packed).Count);Assert.Equal(4,packed.Count);Assert.All(packed.Values.Where(v=>v is not null),v=>Assert.Equal(type=="string"?typeof(string):typeof(long),v!.GetType()));if(a==3&&b==3){Assert.Equal(Value(type,Large+2),packed["place_id"]);Assert.Equal(Value(type,Large+2),packed["warehouse_id"]);Assert.Null(packed["area_id"]);Assert.Null(packed["country_id"]);}Assert.Throws<NotSupportedException>(()=>((IDictionary<string,object?>)packed).Add("extra",1L));
        }
        Assert.Equal(before,JsonSerializer.Serialize(c));var frozen=plan.PackValidatedPaths(Paths([Large,Large+1,Large+2],[Large,Large+1,Large+3]));c.ConfigColumns.Single(x=>x.Id=="31").ColumnName="changed_after_compile";Assert.Equal(2,plan.ReadStored(frozen).Count);
    }
    /// <summary>短控件叶子与长控件父级共用；仓库本身即可恢复地区，不额外保存地区位。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Shared_primary_leaf_is_not_stored_twice(string type)
    {
        var c=Catalog(type,true);var plan=Compile(c);var packed=plan.PackValidatedPaths(Paths([Large,Large+1],[Large,Large+1,Large+2]));Assert.Single(packed,p=>p.Value is not null);Assert.Null(packed["area_id"]);var selected=plan.ReadBranchSelections(packed);Assert.Null(selected["31"]);Assert.Equal(new(2,Large+2),selected["41"]);Assert.Equal(Value(type,Large+2),packed["warehouse_id"]);
    }
    /// <summary>三个分支仅部分共用祖先时，城市、仓库与港口都不能被另一分支打包覆盖。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Three_connected_branches_preserve_all_distinct_leaves(string type)
    {
        var c=Catalog(type);c.ConfigColumns.Add(new(){Id="51",ConfigTableId="1",ColumnName="port_id",LogicalType=type,ControlKind="CascadeTree",ControlDetail=Definition("district","port")});var plan=Compile(c);var paths=Paths([Large,Large+1,Large+2],[Large,Large+1,Large+3]);paths.Add("51",new long[]{Large,Large+4,Large+5});var packed=plan.PackValidatedPaths(paths);Assert.Equal(3,plan.ReadStored(packed).Count);Assert.Null(packed["country_id"]);Assert.Null(packed["area_id"]);Assert.Null(packed["district_id"]);Assert.Equal(Value(type,Large+5),packed["port_id"]);
    }
    /// <summary>不一致的共同国家或地区必须拒绝，不用控件顺序覆盖。</summary>
    [Theory][InlineData(true)][InlineData(false)]
    public void Conflicting_shared_ancestors_reject_without_mutating_inputs(bool root)
    {
        var c=Catalog("string");var plan=Compile(c);var a=new long[]{Large,Large+1,Large+2};var b=new long[]{root?Large+10:Large,root?Large+11:Large+10,Large+3};var paths=Paths(a,b);Assert.Throws<InvalidOperationException>(()=>plan.PackValidatedPaths(paths));Assert.Equal(Large,a[0]);Assert.Equal(Large+3,b[2]);
    }
    /// <summary>原存储中的祖先与后代同时存在属于歧义，不静默删掉看似多余的历史值。</summary>
    [Theory][InlineData("string")][InlineData("int64")]
    public void Stored_redundant_ancestor_and_invalid_types_reject(string type)
    {
        var plan=Compile(Catalog(type));var row=plan.PackValidatedPaths(Paths([Large,Large+1,Large+2],[])).ToDictionary();row["area_id"]=Value(type,Large+1);Assert.Throws<InvalidOperationException>(()=>plan.ReadStored(row));row["area_id"]=null;foreach(var wrong in type=="string"?new object[]{Large,"01","0","-1"," 1",1.1m}:new object[]{"1",1,0L,-1L,1.0m}){row["place_id"]=wrong;Assert.Throws<InvalidOperationException>(()=>plan.ReadStored(row));}row["place_id"]=type=="string"?"":null;Assert.Empty(plan.ReadStored(row));row.Remove("country_id");Assert.Throws<ArgumentException>(()=>plan.ReadStored(row));row["extra_id"]=null;Assert.Throws<ArgumentException>(()=>plan.ReadStored(row));
    }
    /// <summary>不完整组、未知控件、超深与非法ID不能进入编码器。</summary>
    [Theory][InlineData("missing")][InlineData("unknown")][InlineData("depth")][InlineData("zero")][InlineData("negative")]
    public void Invalid_path_inputs_reject(string kind)
    {
        var plan=Compile(Catalog("string"));var paths=Paths([1,2,3],[1,2,4]);if(kind=="missing")paths.Remove("41");if(kind=="unknown"){paths.Remove("41");paths.Add("unknown",[]);}if(kind=="depth")paths["31"]=new long[]{1,2,3,4};if(kind=="zero")paths["31"]=new long[]{1,0};if(kind=="negative")paths["31"]=new long[]{-1};Assert.Throws<ArgumentException>(()=>plan.PackValidatedPaths(paths));
    }
    /// <summary>存储约束与共享目标路径必须完全一致，坏同伴不能因解析失败而绕过。</summary>
    [Theory][InlineData("virtual")][InlineData("join")][InlineData("notNull")][InlineData("default")][InlineData("type")][InlineData("control")][InlineData("name")][InlineData("parent")][InlineData("ancestor")][InlineData("badPeer")][InlineData("foreign")][InlineData("duplicate")][InlineData("redundant")]
    public void Invalid_shared_metadata_rejects(string kind)
    {
        var c=Catalog("string");var root=c.ConfigColumns.Single(x=>x.Id=="12");var source=c.ConfigColumns.Single(x=>x.Id=="41");if(kind=="virtual")root.IsVirtualColumn=1;if(kind=="join")root.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="notNull")root.IsDbNotNull=1;if(kind=="default")root.DefaultValue="1";if(kind=="type")root.LogicalType="decimal";if(kind=="control")root.ControlKind="List";if(kind=="foreign")root.ConfigTableId="other";if(kind=="duplicate")root.ColumnName="area_id";if(kind=="redundant")c.ConfigTables[0].IsRongyu=1;
        if(kind is "name" or "parent" or "ancestor" or "badPeer"){var json=JsonNode.Parse(source.ControlDetail)!;if(kind=="name")json["levels"]![0]!["nameField"]="caption";if(kind=="parent")json["levels"]![1]!["parentField"]="other_country_id";if(kind=="ancestor")json["levels"]![0]!["columnPrefix"]="other_country";if(kind=="badPeer")json["levels"]![2]!["key"]="country";source.ControlDetail=json.ToJsonString();}var before=JsonSerializer.Serialize(c);Assert.Throws<InvalidOperationException>(()=>Compile(c));Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>整组结构仅保存ID，旧单控件写入口仍拒绝共享；未指定范围的SQL不能冒充逻辑祖先。</summary>
    [Fact]public void Single_control_entry_stays_closed_and_structure_excludes_names()
    {
        var c=Catalog("string");Assert.Throws<InvalidOperationException>(()=>CrossTableSparseSelection.Compile(c.ConfigTables[0],"31",c.ConfigColumns));Assert.DoesNotContain(MetadataStoragePolicy.ForTable(c,c.ConfigTables[0]).PhysicalColumns,column=>column.ColumnName.EndsWith("_name",StringComparison.Ordinal));Assert.Throws<NotSupportedException>(()=>NonRedundantReads.Compile(c,"1",["country_id"]));
    }
    /// <summary>基础模型只声明存储；真实候选页范围另由发布／SQL证明。</summary>
    internal static MetadataCatalog Catalog(string type,bool shorter=false)
    {
        var c=new MetadataCatalog{YzProjectId="1",ConfigTables=[new(){Id="1",TableName="shared_source",IsRongyu=0}],ConfigColumns=[new(){Id="11",ConfigTableId="1",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="12",ConfigTableId="1",ColumnName="country_id",LogicalType=type},new(){Id="31",ConfigTableId="1",ColumnName=shorter?"area_id":"place_id",LogicalType=type,ControlKind="CascadeTree",ControlDetail=shorter?Definition("area",null):Definition("area","place")},new(){Id="41",ConfigTableId="1",ColumnName="warehouse_id",LogicalType=type,ControlKind="CascadeTree",ControlDetail=Definition("area","warehouse")}]};if(!shorter)c.ConfigColumns.Add(new(){Id="13",ConfigTableId="1",ColumnName="area_id",LogicalType=type});return c;
    }
    /// <summary>固定命名及层级沿用原跨表负载。</summary>
    private static string Definition(string middle,string? leaf)=>JsonSerializer.Serialize(new{sourceMode="crossTable",levels=(leaf is null?new object[]{new{key="country",table="shared_country",columnPrefix="country"},new{key=middle,table="shared_"+middle,columnPrefix=middle,parentField="country_id"}}:new object[]{new{key="country",table="shared_country",columnPrefix="country"},new{key=middle,table="shared_"+middle,columnPrefix=middle,parentField="country_id"},new{key=leaf,table="shared_"+leaf,columnPrefix=leaf,parentField=middle+"_id"}})});
    /// <summary>只向本表传完整字段，跨表元数据不混入存储计划。</summary>
    private static SharedCrossTableSparseSelection Compile(MetadataCatalog c)=>SharedCrossTableSparseSelection.Compile(c.ConfigTables[0],"31",c.ConfigColumns);
    /// <summary>组负载的键是固定控制列ID，不接受请求指定字段名。</summary>
    private static Dictionary<string,IReadOnlyList<long>> Paths(long[] a,long[] b)=>new(StringComparer.Ordinal){{"31",a},{"41",b}};
    /// <summary>真实大整数保持各自逻辑类型。</summary>
    private static object Value(string type,long id)=>type=="string"?id.ToString(System.Globalization.CultureInfo.InvariantCulture):id;
}
