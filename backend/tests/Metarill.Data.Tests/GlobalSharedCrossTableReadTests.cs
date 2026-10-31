using System.Globalization;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>全局共享稀疏读取仅开放已发布的物理目标，生成写入与文件的总门槛另行证明。</summary>
public sealed class GlobalSharedCrossTableReadTests
{
    /// <summary>四键、长短控件、全部有效目标掩码与有无项目列共176份固定读取计划。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void Published_global_peers_compile_without_changing_source_scope(string type,string parent,bool shorter)
    {
        var count=0;for(var mask=1;mask<=15;mask++)
        {
            if(shorter&&(mask&4)!=0)continue;
            foreach(var noProject in new[]{false,true})
            {
                var c=Catalog("global_shared",type,parent,shorter,mask,noProject);var before=JsonSerializer.Serialize(c);var names=SharedCrossTableReadTests.Outputs(shorter);_=MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",names,SharedCrossTableReadTests.Scopes(c));var plan=NonRedundantReads.CompileWithCrossTables(c,"1",names,SharedCrossTableReadTests.Scopes(c));Assert.Equal(type,plan.OutputTypes["country_id"]);Assert.Equal(type,plan.OutputTypes["area_id"]);Assert.Equal(names.Order(StringComparer.Ordinal),plan.OutputTypes.Keys.Order(StringComparer.Ordinal));Assert.Equal(before,JsonSerializer.Serialize(c));Assert.Contains(c.ConfigColumns,x=>x.ConfigTableId=="1"&&x.ColumnName=="yz_project_id");count++;
            }
        }
        Assert.Equal(shorter?14:30,count);
    }
    /// <summary>全局声明不能省略发布证明、固定条件或独立页面／来源项目边界。</summary>
    [Theory][InlineData("variable")][InlineData("unpublished")][InlineData("foreignTable")][InlineData("foreignPage")][InlineData("missingScope")][InlineData("globalR0")][InlineData("sourceGlobal")][InlineData("exact")][InlineData("page")][InlineData("condition")]
    public void Unproven_global_shared_scope_rejects(string kind)
    {
        // 项目变量用例保留物理项目列，明确验证全局变量门禁，而非未知字段错误。
        var c=Catalog("global_shared","string","int64",false,1,kind!="variable");var target=c.ConfigTables.Single(t=>t.Id=="2");var page=c.ConfigPages.Single(p=>p.ConfigTableId==target.Id);var scopes=SharedCrossTableReadTests.Scopes(c).ToArray();
        if(kind=="variable")page.WhereSql="yz_project_id=@current_yz_project_id AND status=1";
        if(kind=="unpublished")target.YzProjectId="";if(kind=="foreignTable")target.YzProjectId="other";if(kind=="foreignPage")page.YzProjectId="other";
        if(kind=="missingScope")target.Extra.Remove("scopeKind");if(kind=="globalR0")target.IsRongyu=0;if(kind=="sourceGlobal")c.ConfigTables[0].Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="exact")page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("owner_id");
        if(kind is "page" or "condition")
        {
            var levels=scopes[1].Levels.ToArray();if(kind=="page"){var copy=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!;copy.Id="other_global_page";c.ConfigPages.Add(copy);levels[0]=levels[0] with{PageId=copy.Id};}
            else levels[0]=levels[0] with{Condition=FixedConditionSqlPlan.Compile(FixedConditionPlan.Compile("status=2",new[]{"status"}),new Dictionary<string,string>{{"status","int32"}})};scopes[1]=new(scopes[1].SourceColumnId,levels);
        }
        var error=Record.Exception(()=>MySqlNonRedundantReadCompiler.CompileWithCrossTables(c,"1",SharedCrossTableReadTests.Outputs(false),scopes));Assert.True(error is InvalidOperationException or NotSupportedException,"未证明的全局共享范围必须在生成SQL前拒绝。");
    }
    /// <summary>目标表2—5依次为国家／地区／城市／仓库，短控件没有城市目标槽。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,bool shorter,int mask,bool noProject)
    {
        if(mask<=0||mask>15||shorter&&(mask&4)!=0)throw new ArgumentOutOfRangeException(nameof(mask));var c=SharedCrossTableReadTests.Catalog(prefix,type,parent,shorter);
        for(var role=0;role<4;role++)if((mask&(1<<role))!=0)
        {
            var id=(role+2).ToString(CultureInfo.InvariantCulture);c.ConfigTables.Single(t=>t.Id==id).Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");c.ConfigPages.Single(p=>p.ConfigTableId==id).WhereSql="status=1";if(noProject)c.ConfigColumns.RemoveAll(column=>column.ConfigTableId==id&&column.ColumnName=="yz_project_id");
        }
        return c;
    }
}
