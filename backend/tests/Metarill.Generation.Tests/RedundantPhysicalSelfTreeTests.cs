using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Runtime.Files;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>R1物理自树、双树、自表书签及跨表／共享跨表／多选同页通过完整用途证明后才发布。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class RedundantPhysicalSelfTreeTests
{
    /// <summary>四键和八种目录逐页验证缓存、传播与Full能力，不能改写输入配置。</summary>
    [Theory][InlineData("string","string")][InlineData("string","int64")][InlineData("int64","string")][InlineData("int64","int64")]
    public void All_physical_self_and_mixed_pages_have_one_proven_leaf_cache(string type,string parent)
    {
        foreach(var mode in new[]{"single","r0","caption","double","mixed","same","shared","globalShared"})
        {
            var c=Catalog("r1_self",type,parent,mode);Assert.Equal(c.ConfigTables.Count+c.ConfigColumns.Count,c.ConfigTables.Select(t=>t.Id).Concat(c.ConfigColumns.Select(x=>x.Id)).Distinct().Count());Assert.Equal(c.YzAdminTypes.Count,c.YzAdminTypes.Select(a=>a.Id).Distinct().Count());var saved=JsonSerializer.Serialize(c);var model=GenerationModel.Create(c,"Example.R1Self");var source=model.Tables.Single(t=>t.Source.TableName=="r1_self_"+(mode=="same"?"node":"source"));
            Assert.NotEmpty(SelfTreePropagationCompilation.ForTable(model,source));Assert.Empty(GenerationCapabilityAudit.Collect(model));foreach(var page in source.Pages)foreach(var self in CascadeTreeWriteCompilation.ForService(model,source,page)){Assert.True(RedundantSelfTreeSubset.Supports(model,source,page,self.Read.Source));Assert.DoesNotContain(page.Writable,c=>c.Source.Id==self.Name.Source.Id);}
            Assert.Equal(saved,JsonSerializer.Serialize(c));
        }
    }
    /// <summary>原完整包实际构建、挂载原Vue、更新两轮并保护手写partial。</summary>
    [Theory][InlineData("string","int64","single")][InlineData("int64","string","mixed")][InlineData("string","string","shared")][InlineData("int64","int64","globalShared")][InlineData("string","int64","same")][InlineData("int64","string","double")]
    [InlineData("string","int64","single",true)][InlineData("string","int64","r0",true)]
    [InlineData("string","int64","shared",true)][InlineData("int64","int64","globalShared",true)]
    public async Task Full_packages_build_original_controls_and_keep_handwritten_partial(string type,string parent,string mode,bool pageOptions=false)
    {
        var c=Catalog("r1_self",type,parent,mode);
        var shared=mode is "shared" or "globalShared";
        var pageId=shared?"202":"201";
        var controlId=shared?"6101":"25";
        if(pageOptions)
        {
            var sourcePage=c.ConfigPages.Single(page=>page.Id==pageId);
            sourcePage.IsImport=sourcePage.IsExport=1;
            sourcePage.Extra["importMethod"]=JsonSerializer.SerializeToElement(mode is "r0" or "globalShared"?"OverWriteByFirstColumn":"OnlyNew");
            sourcePage.Extra["exportMethod"]=JsonSerializer.SerializeToElement(mode is "r0" or "globalShared"?"edit":"list");
            var control=c.ConfigColumns.Single(column=>column.Id==controlId);
            if(shared)
            {
                // 原覆盖导入按表级顺序选择首列，显式使用业务名称。
                c.ConfigColumns.Single(column=>column.ConfigTableId=="2"&&column.ColumnName=="name").SortNo=-1;
                c.ConfigColumns.Add(new(){Id="6610",ConfigTableId="2",YzProjectId=c.YzProjectId,
                    ColumnName="attachments",Name="附件",LogicalType="string",ColumnLength="max",
                    ControlKind="FileLibrary",ControlDetail="""{"accessMode":"PROTECTED","maxCount":3}"""});
                c.ConfigPageColumns.Add(new(){Id=pageId+"6610",ConfigPageId=pageId,ConfigColumnId="6610",
                    YzProjectId=c.YzProjectId,IsShow=1,IsEdit=1});
            }
            var overlay=JsonNode.Parse(control.ControlDetail)!;
            overlay["displayJoiner"]=" > ";
            var tree=overlay["selfTree"]!.AsObject();
            tree["label"]="页面节点";
            tree["orderBy"]="name desc";
            tree["filterMatchMode"]="exact";
            tree["idField"]="id";
            var field=c.ConfigPageColumns.Single(field=>field.ConfigPageId==pageId&&field.ConfigColumnId==control.Id);
            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(overlay.ToJsonString());
            field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        }
        var model=GenerationModel.Create(c,"Example.R1SelfFull");var table=model.Tables.Single(t=>t.Source.TableName=="r1_self_"+(mode=="same"?"node":"source"));var root=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-r1-self-full");var dir=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var output=Path.Combine(dir,"app");
        if(pageOptions)
        {
            if(shared)
            {
                var parentPlan=Assert.Single(Metarill.Data.Queries.RedundantSharedParents.Compile(c,"2",pageId)).Value;
                Assert.Equal("country_id",parentPlan.ColumnName);
                Assert.Equal("301",Assert.Single(parentPlan.Levels).PageId);
                var files=new FileMetadataContext([c]);
                var published=Assert.Single(files.Catalogs);
                var filePage=published.ConfigPages.Single(item=>item.Id==pageId);
                var pageScope=typeof(FileMetadataContext).GetMethod("PageScope",BindingFlags.Instance|BindingFlags.NonPublic)!;
                Assert.NotNull(pageScope.Invoke(files,[published,filePage]));
            }
            Assert.Empty(GenerationCapabilityAudit.Collect(model));
            var page=table.Pages.Single(item=>item.Source.Id==pageId);
            var control=table.Columns.Single(column=>column.Source.Id==controlId);
            var read=Assert.IsType<CascadeTreeReadModel>(CascadeTreeReadCompilation.Resolve(model,table,page,control).Definition);
            Assert.True(read.Exact);
            Assert.Equal(" > ",read.Joiner);
            Assert.Equal(new[]{("name",true),("id",false)},read.Order
                .Select(entry=>(entry.Column.Source.ColumnName,entry.Descending)));
            var cache=Assert.Single(SelfTreePropagationCompilation.ForTable(model,table));
            Assert.Equal(control.Source.Id,cache.Foreign.Source.Id);
            Assert.Equal(read.Target.Source.Id,cache.Target.Source.Id);
            Assert.Equal(read.Name.Source.Id,cache.Value.Source.Id);
        }
        try
        {
            new ApplicationPackager().Create(c,output,model.Namespace,root);var hand=Path.Combine(output,"backend/generated/Services/"+table.Name+"Service.cs");await File.AppendAllTextAsync(hand,"\n// R1自树及混合级联手写业务。\n");var bytes=await File.ReadAllBytesAsync(hand);var candidate=Path.Combine(dir,"candidate");new ApplicationPackager().Create(c,candidate,model.Namespace,root);var updater=new LocalGenerationUpdater();var actions=new[]{"generate-project-config","sync-runtime","generate-all-page"};var plan=updater.Preview(output,candidate,actions);Assert.True(plan.CanApply);updater.Apply(plan,plan.PlanSha256);Assert.Equal(bytes,await File.ReadAllBytesAsync(hand));var repeated=updater.Preview(output,candidate,actions);Assert.Equal("unchanged",updater.Apply(repeated,repeated.PlanSha256).Status);
            foreach(var operation in new[]{"restore","build"})await NonRedundantApplicationFixture.BuildAsync(output,operation);Assert.Equal(File.ReadAllBytes(Path.Combine(root,"frontend/metarill_uni_admin/components/yzp-cascade-tree/yzp-cascade-tree.vue")),File.ReadAllBytes(Path.Combine(output,"frontend/components/yzp-cascade-tree/yzp-cascade-tree.vue")));
            if(pageOptions)
            {
                var detail=await File.ReadAllTextAsync(Path.Combine(output,$"frontend/pages/yzp/r1_self_source-detail-{pageId}.vue"));
                var definitions=System.Text.RegularExpressions.Regex.Matches(detail,@"<yzp-cascade-tree\b[^>]*:definition='([^']+)'");
                var definition=Assert.Single(definitions,match=>System.Net.WebUtility.HtmlDecode(match.Groups[1].Value)
                    .Contains("\"sourceMode\":\"selfTree\"",StringComparison.Ordinal));
                using var json=JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(definition.Groups[1].Value));
                Assert.Equal("页面节点",json.RootElement.GetProperty("selfTree").GetProperty("label").GetString());
                var controller=await File.ReadAllTextAsync(Path.Combine(output,$"backend/generated/Controllers/R1SelfSourcePage{pageId}Controller.g.cs"));
                Assert.Contains("HttpPost(\"initExport\")",controller);
                Assert.Contains($"HttpPost(\"importDatas{pageId}\")",controller);
                var service=await File.ReadAllTextAsync(Path.Combine(output,"backend/generated/Services/R1SelfSourceService.g.cs"));
                Assert.Contains("FilterIdsAsync(selected.Value,true,cancellationToken)",service);
            }
            else
            {
                var probe=NonRedundantApplicationFixture.Start("node",output);probe.ArgumentList.Add(Path.Combine(NonRedundantApplicationFixture.TestDirectory(),"RedundantPhysicalSelfTreeVueProbe.mjs"));probe.ArgumentList.Add(Path.Combine(output,"frontend"));probe.ArgumentList.Add(mode);await NonRedundantApplicationFixture.RunAsync(probe);
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(dir,boundary);}
    }
    /// <summary>文件与业务的共用父计划拒绝实际树结构变化。</summary>
    [Theory]
    [InlineData("table","r1_self_area")]
    [InlineData("idField","status")]
    [InlineData("nameField","status")]
    [InlineData("parentField","id")]
    public void Shared_parent_proof_rejects_changed_tree_structure(string field,string value)
    {
        var catalog=Catalog("r1_self","string","int64","shared");
        var tree=catalog.ConfigColumns.Single(column=>column.Id=="6101");
        var overrideDefinition=JsonNode.Parse(tree.ControlDetail)!;
        overrideDefinition["selfTree"]![field]=value;
        catalog.ConfigPageColumns.Single(item=>item.ConfigPageId=="202"&&item.ConfigColumnId==tree.Id)
            .Extra["controlDetail"]=JsonSerializer.SerializeToElement(overrideDefinition.ToJsonString());
        Assert.Throws<NotSupportedException>(()=>Metarill.Data.Queries.RedundantSharedParents.Compile(catalog,"2","202"));
    }

    /// <summary>无效隐藏树、动态覆盖、未发布、全局／派生名称、假缓存及未知自树父上下文仍拒绝Full。</summary>
    [Theory][InlineData("hidden")][InlineData("overlay")][InlineData("differentNode")][InlineData("sourceRealm")][InlineData("targetRealm")][InlineData("global")][InlineData("derived")][InlineData("virtualCache")][InlineData("wrongPath")][InlineData("where")][InlineData("ambiguous")][InlineData("unboundSourceParent")]
    public void Invalid_self_companion_stays_closed_before_staging(string kind)
    {
        var c=Catalog("r1_self","string","int64","mixed");var control=c.ConfigColumns.Single(x=>x.Id=="81");if(kind=="hidden"){c.ConfigPageColumns.RemoveAll(f=>f.ConfigColumnId=="81");control.ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{}}";}if(kind=="overlay")c.ConfigPageColumns.Single(f=>f.ConfigPageId=="201"&&f.ConfigColumnId=="81").Extra["controlDetail"]=JsonSerializer.SerializeToElement("{}");if(kind=="sourceRealm")c.ConfigTables.Single(t=>t.Id=="2").YzProjectId="";if(kind=="targetRealm")c.ConfigPages.Single(p=>p.Id=="601").YzProjectId="";if(kind=="global")c.ConfigTables.Single(t=>t.Id=="6").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");if(kind=="derived"){c.ConfigTables.Single(t=>t.Id=="6").IsRongyu=0;c.ConfigColumns.Single(x=>x.Id=="62").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);}if(kind is "virtualCache" or "wrongPath"){var cache=new ColumnMetadata{Id="6099",ConfigTableId="2",YzProjectId=c.YzProjectId,ColumnName="tree_name",LogicalType="string"};c.ConfigColumns.Add(cache);if(kind=="virtualCache")cache.IsVirtualColumn=1;else{cache.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("tree.unrelated");}}if(kind=="where")control.ControlDetail=control.ControlDetail.Replace("\"selfTree\":{","\"whereSql\":\"1=1\",\"selfTree\":{");if(kind=="ambiguous"){var p=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(p=>p.Id=="601")))!;p.Id="602";c.ConfigPages.Add(p);foreach(var f in c.ConfigPageColumns.Where(f=>f.ConfigPageId=="601").ToArray()){var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(f))!;copy.Id="602_"+copy.Id;copy.ConfigPageId="602";c.ConfigPageColumns.Add(copy);}}
        if(kind=="unboundSourceParent")
        {
            c.ConfigColumns.Add(new(){Id="6097",ConfigTableId="2",YzProjectId=c.YzProjectId,
                ColumnName="absent_parent_id",LogicalType="int64"});
            c.ConfigPages.Single(page=>page.Id=="201").WhereSql+=" AND absent_parent_id=@parent_id";
        }
        if(kind=="differentNode")
        {
            c.ConfigColumns.Add(new(){Id="6098",ConfigTableId="6",YzProjectId=c.YzProjectId,
                ColumnName="alternate_name",LogicalType="string"});
            c.ConfigPageColumns.Add(new(){Id="6016098",ConfigPageId="601",ConfigColumnId="6098",YzProjectId=c.YzProjectId});
            var changed=JsonNode.Parse(control.ControlDetail)!;
            changed["selfTree"]!["nameField"]="alternate_name";
            c.ConfigPageColumns.Single(field=>field.ConfigPageId=="201"&&field.ConfigColumnId==control.Id)
                .Extra["controlDetail"]=JsonSerializer.SerializeToElement(changed.ToJsonString());
        }
        var output=Path.Combine(Path.GetTempPath(),"metarill-r1-self-rejected",Guid.NewGuid().ToString("N"));Assert.ThrowsAny<Exception>(()=>new ApplicationPackager().Create(c,output,"Example.RejectedSelf",NonRedundantApplicationFixture.Root()));Assert.False(Directory.Exists(output));
    }
    /// <summary>复用既有目录源头，只有本轮明确的存储与组合变化；不修改实际平台按钮或路径Excel规则。</summary>
    internal static MetadataCatalog Catalog(string prefix,string type,string parent,string mode)
    {
        MetadataCatalog c;
        if(mode=="same")c=CascadeTreeSameRowTests.Catalog(prefix,type,parent);
        else if(mode=="mixed")c=CascadeMixedRequiredTests.Catalog(prefix,type,parent);
        else if(mode is "shared" or "globalShared")
        {
            c=SharedRedundantParentGenerationTests.Catalog(prefix,type,parent,false,mode=="globalShared"?15:0,mode=="globalShared");c.ConfigTables.Add(new(){Id="10",TableName=prefix+"_tree",IsRongyu=1});c.ConfigColumns.AddRange([new(){Id="60101",ConfigTableId="10",ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id="60102",ConfigTableId="10",ColumnName="name",LogicalType="string"},new(){Id="60103",ConfigTableId="10",ColumnName="yz_project_id",LogicalType="int64"},new(){Id="60104",ConfigTableId="10",ColumnName="parent_"+prefix+"_tree_id",LogicalType=parent,LinkConfigTableId="10"},new(){Id="60105",ConfigTableId="10",ColumnName="status",LogicalType="int32"},new(){Id="60106",ConfigTableId="10",ColumnName="delete_no",LogicalType="datetime"},new(){Id="6101",ConfigTableId="2",ColumnName="tree_id",LogicalType=type,ControlKind="CascadeTree",LinkConfigTableId="10",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",displayJoiner="/",selfTree=new{table=prefix+"_tree",parentField="parent_"+prefix+"_tree_id",nameField="name"}})}]);c.ConfigPages.Add(new(){Id="1001",ConfigTableId="10",WhereSql="yz_project_id=@current_yz_project_id AND status=1"});foreach(var id in new[]{"60102","60104"})c.ConfigPageColumns.Add(new(){Id="1001_"+id,ConfigPageId="1001",ConfigColumnId=id,IsShow=1,IsEdit=1});foreach(var p in c.ConfigPages.Where(p=>p.ConfigTableId=="2"))c.ConfigPageColumns.Add(new(){Id=p.Id+"_6101",ConfigPageId=p.Id,ConfigColumnId="6101",IsShow=1,IsEdit=1});
        }
        else
        {
            c=CascadeTreeReadTests.Catalog(prefix,parent);var control=c.ConfigColumns.Single(x=>x.Id=="25");control.LogicalType=type;control.LinkConfigTableId="3";if(mode=="r0")c.ConfigTables.Single(t=>t.Id=="3").IsRongyu=0;if(mode=="caption"){c.ConfigColumns.Single(x=>x.Id=="32").ColumnName="caption";control.ControlDetail=control.ControlDetail.Replace("\"nameField\":\"name\"","\"nameField\":\"caption\"");}if(mode=="double"){var other=JsonSerializer.Deserialize<ColumnMetadata>(JsonSerializer.Serialize(control))!;other.Id="80";other.ColumnName="alternate_id";c.ConfigColumns.Add(other);c.ConfigPageColumns.Add(new(){Id="20180",ConfigPageId="201",ConfigColumnId="80",IsShow=1,IsEdit=1});}
        }
        c.ConfigTables.Single(t=>t.TableName==prefix+"_"+(mode=="same"?"node":"source")).IsRongyu=1;foreach(var p in c.ConfigPages){p.IsImport=0;p.IsExport=0;if(p.ConfigTableId==(mode=="same"?"1":"2"))p.IsCopy=1;}if(mode=="same"){c.ConfigColumns.Single(x=>x.Id=="1").Id="10001";foreach(var f in c.ConfigPageColumns.Where(f=>f.ConfigColumnId=="1"))f.ConfigColumnId="10001";}foreach(var record in c.ConfigTables.Cast<MetadataRecord>().Concat(c.ConfigColumns).Concat(c.ConfigPages).Concat(c.ConfigPageColumns))record.YzProjectId=c.YzProjectId;if(!c.YzAdminTypes.Any(a=>a.Id=="5"))c.YzAdminTypes.Add(new(){Id="5",YzProjectId=c.YzProjectId});foreach(var page in c.ConfigPages)page.YzAdminTypeId="5";return c;
    }
}
