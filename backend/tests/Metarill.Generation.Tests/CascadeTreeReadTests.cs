using System.Globalization;
using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
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
    public async Task Broken_or_cyclic_ancestors_overflow_and_cancellation_are_rejected_without_partial_results()
    {
        foreach(var nodes in new[]{new[]{new CascadeTreeNode(1,"断链","2")},new[]{new CascadeTreeNode(1,"循环","2"),new(2,"循环","1")},Enumerable.Range(1,65).Select(i=>new CascadeTreeNode(i,"深",i==1?null:(i-1).ToString())).ToArray()})
        {
            var reader=Reader(nodes);var leaf=nodes[^1].Id;await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.PathAsync(leaf));await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.OptionsAsync(leaf));
        }
        Assert.Equal(64,(await Reader(Enumerable.Range(1,64).Select(i=>new CascadeTreeNode(i,"深",i==1?null:(i-1).ToString())).ToArray()).PathAsync(64)).Path.Count);
        Assert.Equal(500,(await Reader(Enumerable.Range(1,500).Select(i=>new CascadeTreeNode(i,"同层",null)).ToArray()).OptionsAsync(null)).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Reader(Enumerable.Range(1,501).Select(i=>new CascadeTreeNode(i,"同层",null)).ToArray()).OptionsAsync(null));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Reader([new(1,"根",null)]).PathAsync(1,cancel.Token));
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
            Assert.Contains("CascadeCondition201_25.ApplyConstants",service);Assert.Contains("Limit(limit)",service);Assert.Contains("new global::Example.Cascade.Generated.Entities.CascadeTree { Id=row.Id,Name=row.Name,ParentCascadeTreeId=row.ParentCascadeTreeId }",service);
            Assert.DoesNotContain("row.Secret",service);Assert.Contains("cascadeTreeOptions25",File.ReadAllText(Path.Combine(directory,"generated/Controllers/CascadeSourcePage201Controller.g.cs")));
            var compiled=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(compiled.GetType("Example.Cascade.Generated.Controllers.CascadeSourcePage201Controller"));
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
