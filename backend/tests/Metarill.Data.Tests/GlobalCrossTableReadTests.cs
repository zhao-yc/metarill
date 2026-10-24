using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>显式全局仅改变已证明的跨表目标范围，来源和普通联接继续带真实项目约束。</summary>
public sealed class GlobalCrossTableReadTests
{
    /// <summary>四种键交叉每层全局及全链全局；缺项目列只允许在明确全局目标上出现。</summary>
    [Theory]
    [InlineData(1,"string","string")][InlineData(1,"string","int64")][InlineData(1,"int64","string")][InlineData(1,"int64","int64")]
    [InlineData(2,"string","string")][InlineData(2,"string","int64")][InlineData(2,"int64","string")][InlineData(2,"int64","int64")]
    [InlineData(4,"string","string")][InlineData(4,"string","int64")][InlineData(4,"int64","string")][InlineData(4,"int64","int64")]
    [InlineData(7,"string","string")][InlineData(7,"string","int64")][InlineData(7,"int64","string")][InlineData(7,"int64","int64")]
    public void Global_and_project_roles_keep_separate_alias_scopes(int mask,string type,string parentType)
    {
        var c=Catalog(mask,type,parentType);var before=JsonSerializer.Serialize(c);var plan=MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",["country_name","area_name","place_name","secondary_country_name","secondary_area_name","secondary_place_name","owner_title"],NonRedundantCrossTableReadTests.Scopes(c));var from=(string)typeof(MySqlNonRedundantReadPlan).GetProperty("From",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plan)!;var scope=(string)typeof(MySqlNonRedundantReadPlan).GetProperty("Scope",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(plan)!;
        Assert.Contains("b.`yz_project_id`=@project_scope_number",scope);for(var role=0;role<3;role++)
        {
            var table="global_read_"+new[]{"country","area","place"}[role];var joins=Regex.Matches(from,"LEFT JOIN `"+table+"` (cross[0-9]+) ON");Assert.Equal(2,joins.Count);foreach(Match match in joins){var prefix=match.Groups[1].Value+".`yz_project_id`";if((mask&(1<<role))!=0)Assert.DoesNotContain(prefix,from);else Assert.Contains(prefix+"=@project_scope_",from);Assert.Contains(match.Groups[1].Value+".`delete_no` IS NULL",from);}
        }
        // 普通标量联接未经过跨表全局证明，原范围不得顺带变化。
        if(mask!=7)Assert.Matches("r[0-9]+\\.`yz_project_id`=@project_scope_number",from);else Assert.DoesNotMatch("r[0-9]+\\.`yz_project_id`",from);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>不能把缺失项目、变量范围、派生名称或未发布目录解释为新的全局许可。</summary>
    [Theory][InlineData("implicit")][InlineData("sourceGlobal")][InlineData("projectVariable")][InlineData("parentVariable")][InlineData("unpublishedTable")][InlineData("unpublishedPage")][InlineData("foreignPage")][InlineData("view")][InlineData("function")][InlineData("nonredundant")][InlineData("derivedName")][InlineData("exact")]
    public void Unproven_global_uses_reject_before_sql(string kind)
    {
        var c=Catalog(1,"string","int64");var table=c.ConfigTables.Single(x=>x.Id=="2");var page=c.ConfigPages.Single(x=>x.Id=="21");
        if(kind=="implicit"){table.Extra.Remove("scopeKind");c.ConfigColumns.RemoveAll(x=>x.ConfigTableId=="2"&&x.ColumnName=="yz_project_id");}
        if(kind=="sourceGlobal")c.ConfigTables.Single(x=>x.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="projectVariable")page.WhereSql="yz_project_id=@current_yz_project_id AND is_enabled=1";if(kind=="parentVariable")page.WhereSql="yz_project_id=@parent_id";
        if(kind=="unpublishedTable")table.YzProjectId="";if(kind=="unpublishedPage")page.YzProjectId="";if(kind=="foreignPage")page.YzProjectId="999";if(kind=="view")table.IsView=1;if(kind=="function")table.IsFunctionTable=1;if(kind=="nonredundant")table.IsRongyu=0;
        if(kind=="derivedName")c.ConfigColumns.Single(x=>x.Id=="22").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);if(kind=="exact")page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("id");
        Assert.ThrowsAny<Exception>(()=>NonRedundantReads.CompileWithCrossTables(c,"1",["country_name"],NonRedundantCrossTableReadTests.Scopes(c)));
    }
    /// <summary>不改发布输入；双控件沿原逻辑ID位和冻结目标条件。</summary>
    private static MetadataCatalog Catalog(int mask,string type,string parentType)
    {
        var c=NonRedundantCrossTableReadTests.Catalog("global_read",type,parentType,twoTrees:true);for(var role=0;role<3;role++)if((mask&(1<<role))!=0){var id=(role+2).ToString(System.Globalization.CultureInfo.InvariantCulture);c.ConfigTables.Single(x=>x.Id==id).Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");var page=c.ConfigPages.Single(x=>x.ConfigTableId==id);page.WhereSql=page.WhereSql.Replace("yz_project_id=@current_yz_project_id AND ","",StringComparison.Ordinal);if(mask is 2 or 7)c.ConfigColumns.RemoveAll(x=>x.ConfigTableId==id&&x.ColumnName=="yz_project_id");}return c;
    }
}
