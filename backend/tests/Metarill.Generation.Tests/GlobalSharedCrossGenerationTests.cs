using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>全局共享生成从每个控制列固定完整页证明，原组件和生成协议保持。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class GlobalSharedCrossGenerationTests
{
    /// <summary>四键与全部有效全局位置共176目录，R0和R1均经过同一生成证明。</summary>
    [Theory][InlineData("string","string",false)][InlineData("string","int64",false)][InlineData("int64","string",false)][InlineData("int64","int64",false)]
    [InlineData("string","string",true)][InlineData("string","int64",true)][InlineData("int64","string",true)][InlineData("int64","int64",true)]
    public void Global_shared_generation_has_complete_deduplicated_cache_scope(string type,string parent,bool shorter)
    {
        for(var mask=1;mask<=15;mask++)
        {
            if(shorter&&(mask&4)!=0)continue;foreach(var noProject in new[]{false,true})
            {
                var c=Catalog(type,parent,shorter,mask,noProject);var before=JsonSerializer.Serialize(c);var m=GenerationModel.Create(c,"Example.GlobalShared");
                foreach(var table in m.Tables.Where(t=>t.Source.Id is "2" or "12")){var page=Assert.Single(table.Pages);Assert.Empty(SharedCrossTableCompilation.ForPage(m,table,page).Gaps);Assert.Equal(2,Assert.Single(SharedCrossTableCompilation.ForPage(m,table,page).Groups).Plans.Count);}
                var bindings=RelationPropagationCompilation.Bindings(m).Where(b=>b.Source.Source.Id=="12").ToArray();Assert.Equal(shorter?3:4,bindings.Length);var definitions=RelationPropagationCompilation.Definitions(bindings);_=RelationMutationPlan.Compile(c,definitions);_=RelationMaintenanceCatalog.Compile(c,definitions);Assert.All(bindings,b=>Assert.Equal(b.Target.IsGlobal?b.Target.Pages.Single().Source.Id:null,b.CrossTablePageId));
                // 独立仓库只保留一个缓存定义，却仍携带当前控制列共同全局祖先页。
                var warehouse=bindings.Single(b=>b.Cache.Source.ColumnName=="warehouse_name");using var control=JsonDocument.Parse(c.ConfigColumns.Single(column=>column.Id==warehouse.CrossTableColumnId).ControlDetail);var expected=control.RootElement.GetProperty("levels").EnumerateArray().Where(level=>c.ConfigTables.Any(target=>target.TableName==level.GetProperty("table").GetString()&&target.Extra.GetValueOrDefault("scopeKind").ToString()=="global")).Select(level=>level.GetProperty("key").GetString()!).Order(StringComparer.Ordinal).ToArray();Assert.Equal(expected,warehouse.CrossTableGlobalPages?.Keys.Order(StringComparer.Ordinal).ToArray()??[]);Assert.Empty(GenerationCapabilityAudit.Collect(m));Assert.Equal(before,JsonSerializer.Serialize(c));
            }
        }
    }
    /// <summary>实际Full构建及两轮更新保留原页面、共享清理交互和手写扩展。</summary>
    [Theory][InlineData("string","string",false,1,false)][InlineData("string","int64",true,2,true)][InlineData("int64","string",false,8,true)][InlineData("int64","int64",false,15,true)]
    public async Task Full_global_shared_builds_with_original_frontend(string type,string parent,bool shorter,int mask,bool noProject)
    {
        var c=Catalog(type,parent,shorter,mask,noProject);var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-global-shared-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(directory,"app");try
        {
            new ApplicationPackager().Create(c,output,"Example.GlobalSharedFull",root);foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);
            var candidate=Path.Combine(directory,"candidate");new ApplicationPackager().Create(c,candidate,"Example.GlobalSharedFull",root);var updater=new LocalGenerationUpdater();var plan=updater.Preview(output,candidate,["generate-project-config","sync-runtime","generate-all-page"]);Assert.True(plan.CanApply);Assert.Equal("unchanged",updater.Apply(plan,plan.PlanSha256).Status);
            Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"SharedCrossTableVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(shorter.ToString().ToLowerInvariant());probe.ArgumentList.Add(SharedCrossFreshCatalogTests.Prefix);probe.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]=Path.Combine(root,".local/global-shared-aggregate/process-vue",type+"-"+parent+"-"+shorter);Directory.CreateDirectory(probe.Environment["METARILL_TEST_PROCESS_EVIDENCE_DIRECTORY"]!);await NonRedundantApplicationFixture.RunAsync(probe);
        }finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>页覆盖、全局项目变量、发布归属、全局R0及来源自参与仍按整组拒绝。</summary>
    [Theory][InlineData("overlay")][InlineData("variable")][InlineData("unpublished")][InlineData("foreignPage")][InlineData("globalR0")][InlineData("sourceGlobal")]
    public void Invalid_global_peer_cannot_publish_partial_group(string kind)
    {
        var c=Catalog("string","int64",false,1,false);var target=c.ConfigTables.Single(t=>t.Id=="3");var page=c.ConfigPages.Single(p=>p.Id=="301");if(kind=="overlay"){var overlay=System.Text.Json.Nodes.JsonNode.Parse(c.ConfigColumns.Single(column=>column.Id=="10080").ControlDetail)!;overlay["levels"]![0]!["nameField"]="missing";c.ConfigPageColumns.Single(p=>p.ConfigPageId=="1201"&&p.ConfigColumnId=="10080").Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay.ToJsonString());}if(kind=="variable")page.WhereSql="yz_project_id=@current_yz_project_id";if(kind=="unpublished")target.YzProjectId="";if(kind=="foreignPage")page.YzProjectId="999";if(kind=="globalR0")target.IsRongyu=0;if(kind=="sourceGlobal")c.ConfigTables.Single(t=>t.Id=="12").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");var model=GenerationModel.Create(c,"Example.GlobalSharedReject");var table=model.Tables.Single(t=>t.Source.Id=="12");var group=SharedCrossTableCompilation.ForPage(model,table,Assert.Single(table.Pages));Assert.Empty(group.Groups);Assert.NotEmpty(group.Gaps);
    }
    /// <summary>普通共享正式元数据只改显式全局目标；不允许派生全局名称或改变原页面。</summary>
    internal static MetadataCatalog Catalog(string type,string parent,bool shorter,int mask,bool noProject)
    {
        var c=SharedCrossFreshCatalogTests.Catalog(shorter?"short":"plain");foreach(var column in c.ConfigColumns.Where(column=>column.ConfigTableId is "2" or "12"&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&column.ColumnName!="id"&&column.ColumnName!="yz_project_id"&&column.ColumnName!="a_item_id"))column.LogicalType=type;
        c.ConfigColumns.Single(column=>column.Id=="106").LogicalType=type;
        foreach(var column in c.ConfigColumns.Where(column=>column.ConfigTableId is "4" or "5" or "8"&&column.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&column.ColumnName!="id"&&column.ColumnName!="yz_project_id"))column.LogicalType=parent;
        ApplyGlobals(c,mask,noProject);return c;
    }
    /// <summary>所有共享生成实库模型复用相同显式目标标记与页面字段一致性规则。</summary>
    internal static void ApplyGlobals(MetadataCatalog c,int mask,bool noProject)
    {
        var ids=new[]{"3","4","5","8"};for(var role=0;role<4;role++)if((mask&(1<<role))!=0){var id=ids[role];c.ConfigTables.Single(t=>t.Id==id).Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");c.ConfigPages.Single(p=>p.ConfigTableId==id).WhereSql="status=1";if(noProject){var removed=c.ConfigColumns.Where(column=>column.ConfigTableId==id&&column.ColumnName=="yz_project_id").Select(column=>column.Id).ToHashSet(StringComparer.Ordinal);c.ConfigColumns.RemoveAll(column=>removed.Contains(column.Id));c.ConfigPageColumns.RemoveAll(field=>removed.Contains(field.ConfigColumnId));}}
    }

}
