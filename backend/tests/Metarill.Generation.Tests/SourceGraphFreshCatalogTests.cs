using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>新正式宿主图先验证发布身份和全局ID，再创建独占验收库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class SourceGraphFreshCatalogTests
{
    /// <summary>四种重复来源和两种同表派生树保持原Full边界，不把离线验证记成Web通过。</summary>
    [Theory][InlineData("cross3")][InlineData("cross5")][InlineData("cross6")][InlineData("cross7")][InlineData("tree-parent")][InlineData("tree-bookmark")]
    public void Original_source_graph_models_package_before_database_creation(string profile)
    {
        var c=Catalog(profile);var before=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.SourceGraphFresh");Assert.Empty(GenerationCapabilityAudit.Collect(model));Assert.Equal("5",Assert.Single(c.YzAdminTypes).Id);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-source-graph-catalog");var output=Path.Combine(boundary,Guid.NewGuid().ToString("N"));try{Assert.True(new ApplicationPackager().Create(c,output,model.Namespace,NonRedundantApplicationFixture.Root()).FrontendIncluded);Assert.Equal(before,JsonSerializer.Serialize(c));}finally{if(Directory.Exists(output))Directory.Delete(output,true);}
    }
    /// <summary>固定类型交叉模型，所有动态请求只能选择记录，不能选择查询图。</summary>
    internal static (string Type,string ParentType,int Mask,string Mode,bool Direct) Definition(string profile)=>profile switch
    {
        "cross3"=>("string","string",3,"mixed",false),"cross5"=>("string","int64",5,"derived",false),"cross6"=>("int64","string",6,"physical",false),"cross7"=>("int64","int64",7,"mixed",false),
        "tree-parent"=>("string","string",0,"derived",true),"tree-bookmark"=>("int64","string",0,"derived",false),_=>throw new ArgumentOutOfRangeException(nameof(profile))
    };
    /// <summary>沿已验生成模型增加测试文件及状态维护页；不改平台实际配置和页面模板。</summary>
    internal static MetadataCatalog Catalog(string profile)
    {
        var d=Definition(profile);var tree=d.Mask==0;var c=tree?SameTableDerivedTreeTests.Catalog("source_graph_tree",d.Type,d.ParentType,d.Direct,true):RepeatedCrossSourceTests.Catalog("source_graph_cross",d.Mask,d.Type,d.ParentType,d.Mode);
        if(tree)
        {
            // 表和字段ID跨类别不能冲突；关系中的显式表ID同步转换，字符串表名不变。
            var map=new Dictionary<string,string>{{"1","90001"},{"2","90002"}};foreach(var t in c.ConfigTables)t.Id=map[t.Id];foreach(var f in c.ConfigColumns){f.ConfigTableId=map[f.ConfigTableId];if(map.TryGetValue(f.LinkConfigTableId,out var target))f.LinkConfigTableId=target;if(f.ControlKind=="List"){var json=JsonNode.Parse(f.ControlDetail)!;json["tableId"]=f.LinkConfigTableId;f.ControlDetail=json.ToJsonString();}}foreach(var p in c.ConfigPages)p.ConfigTableId=map[p.ConfigTableId];
        }
        var source=tree?"90001":"2";var page=tree&&d.Direct?"202":"201";foreach(var p in c.ConfigPages)p.IsSave=1;c.ConfigPages.Single(x=>x.Id==page).IsCopy=1;if(tree)c.ConfigPages.Single(x=>x.Id==page).IsExport=1;
        c.ConfigColumns.Add(new(){Id="9001",YzProjectId=c.YzProjectId,ConfigTableId=source,ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});c.ConfigPageColumns.Add(new(){Id=page+"9001",YzProjectId=c.YzProjectId,ConfigPageId=page,ConfigColumnId="9001",IsShow=1,IsEdit=1,SortNo=10000});
        var status=c.ConfigColumns.Single(x=>x.ConfigTableId==source&&x.ColumnName=="status");c.ConfigPages.Add(new(){Id="211",YzProjectId=c.YzProjectId,YzAdminTypeId="5",ConfigTableId=source,IsSave=1,WhereSql="yz_project_id=@current_yz_project_id"});c.ConfigPageColumns.Add(new(){Id="211status",YzProjectId=c.YzProjectId,ConfigPageId="211",ConfigColumnId=status.Id});
        foreach(var p in c.ConfigPages)c.ConfigMenus.Add(new(){Id=(700000+int.Parse(p.Id,System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture),YzProjectId=c.YzProjectId,YzAdminTypeId="5",Name="来源图验收"+p.Id,Kind="GENERATED_PAGE",ConfigPageId=p.Id});return c;
    }
}
