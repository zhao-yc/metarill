using System.Globalization;
using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>自关联级联固定读取合同；不把候选/路径验收当成写入和筛选完成。</summary>
public sealed class CascadeTreeReadTests
{
    [Fact]
    public void Original_requests_keep_exact_high_ids_and_reject_dynamic_fields_duplicate_parent_and_invalid_levels()
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var valid=JsonSerializer.Deserialize<CascadeTreeOptionsRequest>("{\"levelKey\":\"selfTree-1\",\"selectedPath\":{\"parentId\":\"9007199254740993\"}}",json)!;
        Assert.Equal(9007199254740993L,valid.ParseParent());
        foreach(var extra in new[]{"table","sql","projectId","orderBy","structureOnly","requireTargetList"})Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<CascadeTreePathRequest>("{\""+extra+"\":\"secret\"}",json));
        foreach(var path in new[]{"{\"parentId\":\"1\",\"parentId\":\"2\"}","{\"table\":\"secret\"}","[]","{\"parentId\":0}","{\"parentId\":\"1 OR 1=1\"}","{\"parentId\":1.0}"})
            Assert.Throws<ArgumentException>(()=>new CascadeTreeOptionsRequest{LevelKey="selfTree-0",SelectedPath=JsonSerializer.Deserialize<JsonElement>(path)}.ParseParent());
        foreach(var level in new[]{"selfTree-64","selfTree--1","crossTree-1","selfTree-1 OR 1=1"})Assert.Throws<ArgumentException>(()=>new CascadeTreeOptionsRequest{LevelKey=level,SelectedPath=JsonSerializer.Deserialize<JsonElement>("{}")}.ParseParent());
        Assert.Null(new CascadeTreePathRequest().ParseLeaf());
        Assert.Equal(9007199254740993L,JsonSerializer.Deserialize<CascadeTreePathRequest>("{\"leafId\":9007199254740993}",json)!.ParseLeaf());
    }

    [Fact]
    public async Task Paths_keep_original_shape_and_candidate_children_flags_without_loading_the_whole_tree()
    {
        const long first=9007199254740993;var nodes=new[]{new CascadeTreeNode(first,"根",null),new(first+1,"子",first.ToString()),new(first+2,"孙",(first+1).ToString()),new(first+3,"同层",first.ToString())};
        var reader=Reader(nodes);var root=Assert.Single(await reader.OptionsAsync(null));Assert.True(root.HasChildren);Assert.Equal(first.ToString(),root.Id);Assert.Equal(root.Id,root.Value);
        var choices=await reader.OptionsAsync(first);Assert.Equal(new[]{first+1,first+3},choices.Select(row=>long.Parse(row.Id)));Assert.True(choices[0].HasChildren);Assert.False(choices[1].HasChildren);
        var path=await reader.PathAsync(first+2);Assert.Equal("根/子/孙",path.DisplayText);Assert.Equal(new[]{first,first+1,first+2},path.Path.Select(row=>long.Parse(row.Id)));
        Assert.Empty((await reader.PathAsync(null)).Path);
    }

    [Fact]
    public async Task Broken_or_cyclic_ancestors_and_cancellation_are_rejected_without_partial_results()
    {
        foreach(var nodes in new[]{new[]{new CascadeTreeNode(1,"断链","2")},new[]{new CascadeTreeNode(1,"循环","2"),new(2,"循环","1")},Enumerable.Range(1,65).Select(i=>new CascadeTreeNode(i,"深",i==1?null:(i-1).ToString())).ToArray()})
        {
            var reader=Reader(nodes);var leaf=nodes[^1].Id;await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.PathAsync(leaf));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.OptionsAsync(leaf));
        }
        Assert.Equal(64,(await Reader(Enumerable.Range(1,64).Select(i=>new CascadeTreeNode(i,"深",i==1?null:(i-1).ToString())).ToArray()).PathAsync(64)).Path.Count);
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Reader([new(1,"根",null)]).PathAsync(1,cancel.Token));
    }

    /// <summary>原完整数组保留高位ID及固定顺序，每批子层标记必须属于该批。</summary>
    [Theory]
    [InlineData(501)]
    [InlineData(1201)]
    public async Task Wide_level_returns_every_candidate_and_batches_child_probes(int count)
    {
        const long parent=9007199254740993;
        var rows=Enumerable.Range(1,count).Reverse().Select(i=>new CascadeTreeNode(parent+i,"同名",parent.ToString(CultureInfo.InvariantCulture))).ToArray();
        var seen=new List<long>();
        var reader=new SelfTreeReader((id,_)=>Task.FromResult<CascadeTreeNode?>(new(id,"父",null)),
            (id,limit,_)=>{Assert.Equal(parent,id);return Task.FromResult<IReadOnlyList<CascadeTreeNode>>(rows.Take(limit).ToArray());},
            (ids,_)=>{Assert.InRange(ids.Count,1,200);seen.AddRange(ids);return Task.FromResult<IReadOnlySet<long>>(ids.Where(id=>id==rows[0].Id||id==rows[^1].Id).ToHashSet());},"/");
        var options=await reader.OptionsAsync(parent);
        Assert.Equal(rows.Select(row=>row.Id.ToString(CultureInfo.InvariantCulture)),options.Select(option=>option.Id));
        Assert.Equal(count,seen.Distinct().Count());
        Assert.True(options[0].HasChildren);
        Assert.True(options[^1].HasChildren);
        Assert.False(options[1].HasChildren);
        Assert.Equal(options[^1].Id,options[^1].Value);

        var invalid=new SelfTreeReader((id,_)=>Task.FromResult<CascadeTreeNode?>(new(id,"父",null)),
            (_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(rows),
            (ids,_)=>Task.FromResult<IReadOnlySet<long>>(new HashSet<long>{rows.First(row=>!ids.Contains(row.Id)).Id}),"/");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>invalid.OptionsAsync(parent));
    }

    [Fact]
    public void Page_overlay_replaces_field_definition_and_duplicate_or_unexecuted_ranges_stay_gated()
    {
        var catalog=Catalog();var field=catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25");
        var source=catalog.ConfigColumns.Single(column=>column.Id=="25");var original=source.ControlDetail;
        field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(original.Replace("\"displayJoiner\":\"/\"","\"displayJoiner\":\" > \""));
        var model=GenerationModel.Create(catalog,"Example.Cascade");var table=model.Tables.Single(t=>t.Source.Id=="2");Assert.Equal(" > ",Assert.Single(CascadeTreeReadCompilation.ForPage(model,table,Assert.Single(table.Pages))).Joiner);Assert.Equal(original,source.ControlDetail);
        foreach(var text in new[]{"{\"sourceMode\":\"selfTree\",\"sourceMode\":\"crossTable\"}",original.Replace("\"selfTree\":{","\"selfTree\":{\"whereSql\":\"status=0\",")})
        {
            field.Extra["controlDetail"]=JsonSerializer.SerializeToElement(text);model=GenerationModel.Create(catalog,"Example.Cascade");table=model.Tables.Single(t=>t.Source.Id=="2");Assert.Empty(CascadeTreeReadCompilation.ForPage(model,table,Assert.Single(table.Pages)));
        }
    }

    [Theory]
    [InlineData("int64")]
    [InlineData("string")]
    public async Task Fixed_generated_endpoints_compile_with_physical_projections_and_keep_full_application_gate(string parentType)
    {
        var catalog=Catalog("cascade",parentType);var original=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.Cascade");var table=model.Tables.Single(t=>t.Source.Id=="2");var page=Assert.Single(table.Pages);
        Assert.Single(CascadeTreeReadCompilation.ForPage(model,table,page));Assert.Single(CascadeTreeWriteCompilation.ForService(model,table,page));Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Reason.Contains("CascadeTree")&&gap.Reason.Contains("完整应用"));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cascade-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.Cascade");
            var service=File.ReadAllText(Path.Combine(directory,"generated/Services/CascadeSourceService.g.cs"));
            Assert.Contains("CascadeCondition201_25.ApplyConstants",service);Assert.Contains("InReadSnapshotAsync(async()=>{var parent=request.ParseParent()",service);Assert.Contains("new global::Example.Cascade.Generated.Entities.CascadeTree { Id=row.Id,Name=row.Name,ParentCascadeTreeId=row.ParentCascadeTreeId }",service);
            Assert.DoesNotContain("row.Secret",service);Assert.Contains("cascadeTreeOptions25",File.ReadAllText(Path.Combine(directory,"generated/Controllers/CascadeSourcePage201Controller.g.cs")));
            var compiled=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(compiled.GetType("Example.Cascade.Generated.Controllers.CascadeSourcePage201Controller"));
            var actor=new RuntimeActor(9,"自树候选",false,catalog.YzProjectId,"5");
            using var runtime=new SharedExcelReadTransport(compiled,model.Namespace,"CascadeSource",actor:actor,authorization:new TreeListOnly(actor));
            const long first=9007199254740993;
            const int count=621;
            for(var index=0;index<count;index++)Add(first+index,"同名");
            Add(first+count,"末项的子节点",first+count-1);
            Add(first+count+1,"跨项目的子节点",first,project:"2");
            Add(first+count+2,"已停用",status:0);
            var options=await runtime.CascadeOptions("25",new CascadeTreeOptionsRequest{LevelKey="selfTree-0",SelectedPath=JsonSerializer.SerializeToElement(new{parentId=""})});
            Assert.Equal(Enumerable.Range(0,count).Select(index=>(first+index).ToString(CultureInfo.InvariantCulture)),options.Select(option=>option.Id));
            Assert.False(options[0].HasChildren);
            Assert.True(options[^1].HasChildren);
            Assert.Equal(1,runtime.Snapshots);
            Assert.Equal(5,runtime.ReadCalls.Count(call=>call.Entity=="CascadeTree"&&call.Method=="ToListAsync"));

            void Add(long id,string name,long? parent=null,string? project=null,int status=1)=>
                runtime.Add("CascadeTree",new Dictionary<string,object?>{["Id"]=id,["Name"]=name,["ParentCascadeTreeId"]=parent,
                    ["YzProjectId"]=project??catalog.YzProjectId,["Status"]=status});

            Assert.Equal(original,JsonSerializer.Serialize(catalog));
            var hand=Path.Combine(directory,"generated/Services/CascadeSourceService.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.Cascade");Assert.Equal(bytes,File.ReadAllBytes(hand));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    [Fact]
    public void Ambiguous_pages_hidden_names_unowned_targets_and_extra_sql_do_not_create_wide_fallback_queries()
    {
        foreach(var change in new Action<MetadataCatalog>[]
        {
            catalog=>catalog.ConfigPages.Add(new(){Id="302",ConfigTableId="3",WhereSql="yz_project_id=@current_yz_project_id"}),
            catalog=>catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId=="301"&&field.ConfigColumnId=="32"),
            catalog=>{catalog.ConfigColumns.Single(column=>column.Id=="33").ColumnName="other_project";catalog.ConfigPages.Single(page=>page.Id=="301").WhereSql="status=1";},
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="35").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="{\"sourceMode\":\"crossTable\",\"levels\":[]}",
            catalog=>catalog.ConfigColumns.Single(column=>column.Id=="25").ControlDetail="{\"sourceMode\":\"selfTree\",\"selfTree\":{\"table\":\"cascade_tree\",\"parentField\":\"parent_cascade_tree_id\",\"orderBy\":\"rand()\"}}"
        })
        {
            var catalog=Catalog();change(catalog);
            // 第二页补可读字段，确保确实因多页歧义拒绝而不是因为新页为空。
            if(catalog.ConfigPages.Any(page=>page.Id=="302"))foreach(var id in new[]{"32","35"})catalog.ConfigPageColumns.Add(new(){Id="302"+id,ConfigPageId="302",ConfigColumnId=id});
            var model=GenerationModel.Create(catalog,"Example.Cascade");var table=model.Tables.Single(t=>t.Source.Id=="2");var page=Assert.Single(table.Pages);var gap=CascadeTreeReadCompilation.Resolve(model,table,page,table.Columns.Single(c=>c.Source.Id=="25"));Assert.Null(gap.Definition);Assert.NotNull(gap.Reason);
        }
    }

    private sealed class TreeListOnly(RuntimeActor actor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation)
        {
            Assert.Equal("list",operation);
            Assert.Contains(page,new[]{"201","301"});
        }
    }

    /// <summary>按固定查询范围模拟数据库行，只验证算法，不声称实库/HTTP验收。</summary>
    private static SelfTreeReader Reader(CascadeTreeNode[] nodes)=>new(
        (id,_)=>Task.FromResult(nodes.SingleOrDefault(row=>row.Id==id)),
        (parent,limit,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(nodes.Where(row=>row.ParentId==(parent?.ToString(CultureInfo.InvariantCulture))).Take(limit).ToArray()),
        (ids,_)=>Task.FromResult<IReadOnlySet<long>>(nodes.Where(row=>row.ParentId is not null).Select(row=>long.Parse(row.ParentId!)).Where(ids.Contains).ToHashSet()),"/");

    /// <summary>随机实库夹具与离线编译共用同一元数据；隐藏secret列不得进入树读取投影。</summary>
    internal static MetadataCatalog Catalog(string prefix="cascade",string parentType="int64")
    {
        var catalog=new MetadataCatalog{YzProjectId="9007199254740993"};
        foreach(var (id,suffix) in new[]{("2","source"),("3","tree")})
        {
            catalog.ConfigTables.Add(new(){Id=id,TableName=prefix+"_"+suffix,IsRongyu=1});var start=int.Parse(id)*10;
            catalog.ConfigColumns.AddRange([new(){Id=(start+1).ToString(),ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=(start+2).ToString(),ConfigTableId=id,ColumnName="name",LogicalType="string"},new(){Id=(start+3).ToString(),ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},new(){Id=(start+4).ToString(),ConfigTableId=id,ColumnName="delete_no",LogicalType="datetime"}]);
            catalog.ConfigPages.Add(new(){Id=id+"01",ConfigTableId=id,WhereSql="yz_project_id=@current_yz_project_id"+(id=="3"?" AND status=1":"")});
            catalog.ConfigPageColumns.Add(new(){Id=id+"001",ConfigPageId=id+"01",ConfigColumnId=(start+2).ToString()});
        }
        catalog.ConfigColumns.Add(new(){Id="35",ConfigTableId="3",ColumnName="parent_"+prefix+"_tree_id",LogicalType=parentType,LinkConfigTableId="3"});
        catalog.ConfigColumns.Add(new(){Id="36",ConfigTableId="3",ColumnName="secret",LogicalType="string"});catalog.ConfigColumns.Add(new(){Id="37",ConfigTableId="3",ColumnName="status",LogicalType="int32"});
        catalog.ConfigPageColumns.Add(new(){Id="3005",ConfigPageId="301",ConfigColumnId="35"});
        catalog.ConfigColumns.Add(new(){Id="25",ConfigTableId="2",ColumnName="tree_id",LogicalType="string",ControlKind="CascadeTree",ControlDetail=JsonSerializer.Serialize(new{sourceMode="selfTree",displayJoiner="/",selfTree=new{table=prefix+"_tree",idField="id",nameField="name",parentField="parent_"+prefix+"_tree_id",rootMode="blank",filterMatchMode="selfAndDescendants"}})});
        catalog.ConfigPageColumns.Add(new(){Id="2005",ConfigPageId="201",ConfigColumnId="25"});return catalog;
    }
}
