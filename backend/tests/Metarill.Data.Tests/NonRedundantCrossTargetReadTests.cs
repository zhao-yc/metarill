using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>跨表R0名称在同一SQL投影中解析，双控件和普通联接不共享错误别名或物理来源。</summary>
public sealed class NonRedundantCrossTargetReadTests
{
    /// <summary>物理／派生目标均保持四种ID及父键，名称条件只用冻结参数。</summary>
    [Theory]
    [InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void Dual_paths_and_scalar_join_use_fixed_nonredundant_targets(string sourceType,string parentType,bool derived)
    {
        var c=Catalog(sourceType,parentType,derived);var before=JsonSerializer.Serialize(c);var p=MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",["country_id","country_name","area_name","place_name","secondary_country_name","owner_title"],Scopes(c,derived));var sql=(string)typeof(MySqlNonRedundantReadPlan).GetProperty("From",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(p)!;Assert.Equal(derived?14:7,sql.Split(" LEFT JOIN ").Length-1);Assert.Equal(before,JsonSerializer.Serialize(c));var origins=(System.Collections.IDictionary)typeof(MySqlNonRedundantReadPlan).GetProperty("Origins",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(p)!;Assert.False(origins.Contains("country_id"));Assert.False(origins.Contains("country_name"));Assert.True(origins.Contains("owner_title"));
    }
    /// <summary>终点、拓扑与派生范围未证明时不能落成半条SQL。</summary>
    [Theory][InlineData("credential")][InlineData("array")][InlineData("derivedParent")][InlineData("derivedScope")][InlineData("rootTarget")][InlineData("view")][InlineData("global")][InlineData("stringNameKey")]
    public void Unproven_targets_remain_rejected(string kind)
    {
        var c=Catalog("string","int64",true);
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="52").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="22").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        if(kind=="array")c.ConfigColumns.Single(x=>x.Id=="52").ControlKind="FileLibrary";
        if(kind=="derivedParent"){var p=c.ConfigColumns.Single(x=>x.Id=="36");p.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);p.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="derivedScope")c.ConfigPages.Single(x=>x.Id=="21").WhereSql+=" AND title='未证明条件'";
        if(kind=="rootTarget"){var source=c.ConfigColumns.Single(x=>x.Id=="15");source.ControlDetail=source.ControlDetail.Replace("read_cross_country","read_cross_source",StringComparison.Ordinal).Replace("\"nameField\":\"title\"","\"nameField\":\"name\"",StringComparison.Ordinal);}
        if(kind=="view")c.ConfigTables.Single(x=>x.Id=="2").IsView=1;
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="2").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="stringNameKey")c.ConfigColumns.Single(x=>x.Id=="29").LogicalType="string";
        Assert.ThrowsAny<Exception>(()=>NonRedundantReads.CompileWithCrossTables(c,"1",["country_name"],Scopes(c,true)));
    }
    /// <summary>普通AllRows仍先检查冻结字段／值，不能为不分页而绕过参数验证。</summary>
    [Fact]
    public async Task All_rows_rejects_unknown_filter_before_connection()
    {
        var c=Catalog("string","int64",false);var p=NonRedundantReads.CompileWithCrossTables(c,"1",["country_name"],Scopes(c,false));using var session=new DatabaseSession(new DatabaseOptions{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=invalid;Connection Timeout=1"});await Assert.ThrowsAsync<InvalidOperationException>(()=>new NonRedundantReader(session).AllRowsAsync(p,[new("not_published",FixedReadComparison.Equal,"x")],[]));
    }
    /// <summary>目标固定条件在原物理名称和新派生名称间分别绑定，不将派生条件默默忽略。</summary>
    private static NonRedundantCrossTableScope[] Scopes(MetadataCatalog c,bool derived)=>derived?c.ConfigColumns.Where(x=>x.ConfigTableId=="1"&&x.ControlKind=="CascadeTree").Select(x=>new NonRedundantCrossTableScope(x.Id,new[]{"21","31","41"}.Select(id=>new NonRedundantCrossTableTargetScope(id,FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("is_enabled=1",["is_enabled"]),new Dictionary<string,string>{{"is_enabled","bool"}}))).ToArray())).ToArray():NonRedundantCrossTableReadTests.Scopes(c);
    /// <summary>源与两个控件沿原目录，三层目标各自R0并可联接同一公司表。</summary>
    private static MetadataCatalog Catalog(string sourceType,string parentType,bool derived)
    {
        var c=NonRedundantCrossTableReadTests.Catalog("read_cross",sourceType,parentType,twoTrees:true);foreach(var t in c.ConfigTables.Where(x=>x.Id!="1"))t.IsRongyu=0;
        if(derived)
        {
            c.ConfigTables.Add(new(){Id="5",TableName="read_cross_company",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="51",ConfigTableId="5",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="52",ConfigTableId="5",ColumnName="name",LogicalType="string"},new(){Id="53",ConfigTableId="5",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="54",ConfigTableId="5",ColumnName="delete_no",LogicalType="datetime"}]);
            foreach(var target in c.ConfigTables.Where(x=>x.Id is "2" or "3" or "4")){var name=c.ConfigColumns.Single(x=>x.ConfigTableId==target.Id&&x.ColumnName=="title");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");c.ConfigColumns.Add(new(){Id=target.Id+"9",ConfigTableId=target.Id,ColumnName="company_id",LogicalType="int64",LinkConfigTableId="5"});c.ConfigPages.Single(x=>x.ConfigTableId==target.Id).WhereSql="yz_project_id=@current_yz_project_id AND is_enabled=1";}
        }
        foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages))record.YzProjectId=c.YzProjectId;return c;
    }
}
