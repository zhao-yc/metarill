using System.Diagnostics;
using System.Text.Json;
using Metarill.Core.Cascades;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>跨表原协议、父链与固定编译；只读证据不当作保存或完整应用验收。</summary>
public sealed class CrossTableReadTests
{
    /// <summary>不同表相同数字主键不视为循环，路径逐层保留levelKey。</summary>
    [Fact]
    public async Task Original_options_and_paths_keep_level_keys_and_same_ids_across_tables()
    {
        var reader=Reader([new(1,"国",null),new(1,"区","1"),new(1,"城","1")]);var options=await reader.OptionsAsync("地区-二",JsonSerializer.SerializeToElement(new{countryId="1",areaId="",placeId=""}));Assert.Equal("1",Assert.Single(options).Value);
        var finite=new CrossTableReader(Enumerable.Range(0,80).Select(index=>new CrossTableLevel("层"+index,"level"+index+"Id")).ToArray(),_=>{},(index,id,_)=>Task.FromResult<CascadeTreeNode?>(new(id,"层",index==0?null:"1")),(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([]),"/");Assert.Equal(80,(await finite.PathAsync(79,1)).Path.Count);
        Assert.Single(await reader.OptionsAsync("country",default));Assert.Single(await reader.OptionsAsync("country",JsonSerializer.Deserialize<JsonElement>("null")));
        var path=await reader.PathAsync(2,1);Assert.Equal("国/区/城",path.DisplayText);Assert.Equal(new[]{"country","地区-二","place"},path.Path.Select(node=>node.LevelKey));Assert.Equal(3,path.Path.Count);
    }
    /// <summary>未知/重复字段与不一致祖先均拒绝；缺父只返回空，不跳过当前层授权。</summary>
    [Fact]
    public async Task Fixed_keys_and_actual_ancestors_cannot_be_replaced_by_request_data()
    {
        var authorized=new List<int>();var reader=Reader([new(1,"国",null),new(2,"区","1"),new(3,"城","2")],authorized.Add);
        Assert.Empty(await reader.OptionsAsync("place",JsonSerializer.SerializeToElement(new{})));Assert.Contains(2,authorized);
        foreach(var json in new[]{"{\"countryId\":9,\"areaId\":2}","{\"areaId\":2,\"areaId\":2}","{\"table\":\"secret\"}","{\"areaId\":1.0}","[]"})await Assert.ThrowsAsync<ArgumentException>(()=>reader.OptionsAsync("place",JsonSerializer.Deserialize<JsonElement>(json)));
        await Assert.ThrowsAsync<ArgumentException>(()=>reader.OptionsAsync("unknown",JsonSerializer.SerializeToElement(new{})));
        var denied=Reader([new(1,"国",null),new(2,"区","1"),new(3,"城","2")],_=>throw new UnauthorizedAccessException());await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>denied.PathAsync(2,null));
    }
    /// <summary>缺失祖先、无效父键不返回部分成功；完整宽层按原数组返回，取消信号原样传播。</summary>
    [Fact]
    public async Task Broken_chain_full_wide_candidates_and_cancellation_keep_original_contract()
    {
        foreach(var parent in new[]{"999","invalid",""})await Assert.ThrowsAsync<InvalidOperationException>(()=>Reader([new(1,"国",null),new(2,"区",parent),new(3,"城","2")]).PathAsync(2,3));
        var overflow=new CrossTableReader(Levels(),_=>{},(_,_,_)=>Task.FromResult<CascadeTreeNode?>(null),(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(Enumerable.Range(1,501).Select(id=>new CascadeTreeNode(id,"国",null)).ToArray()),"/");Assert.Equal(501,(await overflow.OptionsAsync("country",JsonSerializer.SerializeToElement(new{}))).Count);
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Reader([new(1,"国",null),new(2,"区","1"),new(3,"城","2")]).PathAsync(2,3,cancellation.Token));
    }
    /// <summary>回调即使返回越界父或重复候选也不能伪装为成功，空名称沿原跨表格式跳过。</summary>
    [Fact]
    public async Task Candidate_parent_projection_and_empty_labels_follow_original_contract()
    {
        var reader=Reader([new(1,"国",null),new(2,null,"1"),new(3,"城","2")]);var emptyPath=await reader.PathAsync(2,3);Assert.Equal("国/城",emptyPath.DisplayText);Assert.Equal("",emptyPath.Path[1].Name);var emptyOption=Assert.Single(await reader.OptionsAsync("地区-二",JsonSerializer.SerializeToElement(new{countryId="1"})));Assert.Equal("",emptyOption.Name);Assert.Equal("",emptyOption.Text);
        var bad=new CrossTableReader(Levels(),_=>{},(level,id,_)=>Task.FromResult<CascadeTreeNode?>(new(id,"名",level==0?null:"1")),(_,_,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>([new(2,"错误父","99")]),"/");await Assert.ThrowsAsync<InvalidOperationException>(()=>bad.OptionsAsync("地区-二",JsonSerializer.SerializeToElement(new{countryId=1})));
    }
    /// <summary>固定计划拷贝层定义，测试读取委托不解释任何SQL。</summary>
    private static CrossTableLevel[] Levels()=>[new("country","countryId"),new("地区-二","areaId"),new("place","placeId")];
    /// <summary>按层限定数据，不能跨级借用相同ID的另一行。</summary>
    private static CrossTableReader Reader(CascadeTreeNode[] rows,Action<int>? authorize=null)=>new(Levels(),authorize??(_=>{}),(level,id,_)=>Task.FromResult<CascadeTreeNode?>(rows[level].Id==id?rows[level]:null),(level,parent,_)=>Task.FromResult<IReadOnlyList<CascadeTreeNode>>(level==0||rows[level].ParentId==parent?.ToString()?new[]{rows[level]}:[]),"/");

    /// <summary>四种物理父键组合实际生成编译，原元数据和手写partial均不改变。</summary>
    [Theory]
    [InlineData("string","string")]
    [InlineData("string","int64")]
    [InlineData("int64","string")]
    [InlineData("int64","int64")]
    public async Task Actual_generated_cross_table_controllers_compile_with_fixed_projections(string midType,string leafType)
    {
        var catalog=Catalog("cross",midType,leafType);if(midType=="string"&&leafType=="int64"){var field=catalog.ConfigColumns.Single(column=>column.Id=="25");field.ControlDetail=field.ControlDetail.Replace("\"displayJoiner\":\"/\"","\"displayJoiner\":\""+new string('>',80)+"\"");}var before=JsonSerializer.Serialize(catalog);var model=GenerationModel.Create(catalog,"Example.Cross");var table=model.Tables.Single(table=>table.Source.Id=="2");var page=Assert.Single(table.Pages);var plan=Assert.Single(CrossTableReadCompilation.ForPage(model,table,page));if(midType=="string"&&leafType=="int64")Assert.Equal(80,plan.Joiner.Length);var gap=Assert.Single(GenerationCapabilityAudit.Collect(model),gap=>gap.PageId=="201"&&gap.Column=="place_id"&&gap.Usage=="detail");Assert.Contains("明确属于包内项目和后台",gap.Reason);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-compile");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.Cross");var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);Assert.NotNull(assembly.GetType("Example.Cross.Generated.Controllers.CrossSourcePage201Controller"));var service=File.ReadAllText(Path.Combine(directory,"generated/Services/CrossSourceService.g.cs"));Assert.DoesNotContain("row.Secret",service);Assert.Contains("InReadSnapshotAsync",service);var hand=Path.Combine(directory,"generated/Services/CrossSourceService.cs");var bytes=File.ReadAllBytes(hand);new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),"Example.Cross");Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>未知/歧义/虚拟/越界定义不允许宽查询，原页覆盖只使用该页声明。</summary>
    [Fact]
    public void Ambiguous_targets_bad_parent_links_and_extra_sql_keep_explicit_gaps()
    {
        foreach(var mutate in new Action<MetadataCatalog>[]
        {
            c=>c.ConfigColumns.Single(x=>x.Id=="45").LinkConfigTableId="5",
            c=>c.ConfigPageColumns.RemoveAll(x=>x.ConfigColumnId=="45"),
            c=>c.ConfigColumns.Single(x=>x.Id=="32").Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1),
            c=>c.ConfigColumns.Single(x=>x.Id=="25").ControlDetail=c.ConfigColumns.Single(x=>x.Id=="25").ControlDetail.Replace("\"country\"","\"place\""),
            c=>c.ConfigColumns.Single(x=>x.Id=="25").ControlDetail=c.ConfigColumns.Single(x=>x.Id=="25").ControlDetail.Replace("\"levels\":","\"whereSql\":\"1=1\",\"levels\":"),
            c=>{var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(c.ConfigPages.Single(p=>p.Id=="301")))!;page.Id="302";c.ConfigPages.Add(page);c.ConfigPageColumns.Add(new(){Id="30232",ConfigPageId="302",ConfigColumnId="32"});}
        })
        {var catalog=Catalog();mutate(catalog);var model=GenerationModel.Create(catalog,"Example.Cross");var table=model.Tables.Single(t=>t.Source.Id=="2");var gap=CrossTableReadCompilation.Resolve(model,table,Assert.Single(table.Pages),table.Columns.Single(c=>c.Source.Id=="25"));Assert.Null(gap.Definition);Assert.NotNull(gap.Reason);}
    }
    /// <summary>页级明细仅覆盖当前页，不能让另一页借用有效目标定义而扩大读取。</summary>
    [Fact]
    public void Page_override_is_frozen_per_source_page_without_global_fallback()
    {
        var catalog=Catalog();var source=catalog.ConfigColumns.Single(column=>column.Id=="25");var saved=source.ControlDetail;source.ControlDetail="{\"sourceMode\":\"crossTable\",\"levels\":[]}";catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").Extra["controlDetail"]=JsonSerializer.SerializeToElement(saved);
        catalog.ConfigPages.Add(new(){Id="202",ConfigTableId="2",WhereSql="yz_project_id=@current_yz_project_id"});catalog.ConfigPageColumns.Add(new(){Id="20225",ConfigPageId="202",ConfigColumnId="25"});var model=GenerationModel.Create(catalog,"Example.CrossOverride");var table=model.Tables.Single(table=>table.Source.Id=="2");Assert.Single(CrossTableReadCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="201")));Assert.Empty(CrossTableReadCompilation.ForPage(model,table,table.Pages.Single(page=>page.Source.Id=="202")));
    }
    /// <summary>普通Source加三级目标均由元数据生成，无手写Entity或Mapper。</summary>
    internal static MetadataCatalog Catalog(string prefix="cross",string midType="string",string leafType="int64")
    {
        var catalog=new MetadataCatalog{YzProjectId="9007199254740993"};var names=new[]{"source","country","area","place"};
        for(var index=0;index<names.Length;index++)
        {
            var id=(index+2).ToString();var start=(index+2)*10;catalog.ConfigTables.Add(new(){Id=id,TableName=prefix+"_"+names[index],IsRongyu=1});catalog.ConfigColumns.AddRange([new(){Id=(start+1).ToString(),ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=(start+2).ToString(),ConfigTableId=id,ColumnName="name",LogicalType="string"},new(){Id=(start+3).ToString(),ConfigTableId=id,ColumnName="yz_project_id",LogicalType="int64"},new(){Id=(start+4).ToString(),ConfigTableId=id,ColumnName="delete_no",LogicalType="datetime"}]);
            var page=id+"01";catalog.ConfigPages.Add(new(){Id=page,ConfigTableId=id,WhereSql="yz_project_id=@current_yz_project_id"+(index==0?"":" AND status=1")});catalog.ConfigPageColumns.Add(new(){Id=page+"name",ConfigPageId=page,ConfigColumnId=(start+2).ToString()});
            if(index>0){catalog.ConfigColumns.Add(new(){Id=(start+6).ToString(),ConfigTableId=id,ColumnName="status",LogicalType="int32"});catalog.ConfigColumns.Add(new(){Id=(start+7).ToString(),ConfigTableId=id,ColumnName="secret",LogicalType="string"});}
            if(index>1){catalog.ConfigColumns.Add(new(){Id=(start+5).ToString(),ConfigTableId=id,ColumnName=prefix+"_"+names[index-1]+"_id",LogicalType=index==2?midType:leafType,LinkConfigTableId=(index+1).ToString()});catalog.ConfigPageColumns.Add(new(){Id=page+"parent",ConfigPageId=page,ConfigColumnId=(start+5).ToString()});}
        }
        catalog.ConfigColumns.Add(new(){Id="25",ConfigTableId="2",ColumnName="place_id",LogicalType="string",ControlKind="CascadeTree",LinkConfigTableId="5",ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",displayJoiner="/",levels=new object[]{new{key="country",columnPrefix="country",table=prefix+"_country"},new{key="地区-二",columnPrefix="area",table=prefix+"_area",parentField=prefix+"_country_id"},new{key="place",columnPrefix="place",table=prefix+"_place",parentField=prefix+"_area_id"}}})});catalog.ConfigPageColumns.Add(new(){Id="20125",ConfigPageId="201",ConfigColumnId="25"});return catalog;
    }
}

/// <summary>独立包必须从包内Core源码编译，不借工作区DLL证明闭包。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTablePackageTests
{
    /// <summary>CI与本机均从源码位置确定基础工程，不依赖本机私有环境变量。</summary>
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath]string file="")=>Environment.GetEnvironmentVariable("METARILL_PACKAGE_SOURCE_ROOT")??Environment.GetEnvironmentVariable("METARILL_REPOSITORY_ROOT")??Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    /// <summary>BackendOnly包含新算法并实际构建，未明确发布权限目录的FullApplication仍拒绝。</summary>
    [Fact]
    public async Task Exported_backend_only_builds_cross_table_controllers_with_its_own_core()
    {
        var source=SourceRoot();var catalog=CrossTableReadTests.Catalog();var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-package");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            new ApplicationPackager().Create(catalog,directory,"Example.CrossPackage",source,ApplicationPackageMode.BackendOnly);Assert.True(File.Exists(Path.Combine(directory,"src/Metarill.Core/Cascades/CrossTableReader.cs")));
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){WorkingDirectory=directory,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","--disable-build-servers","--verbosity","quiet"})start.ArgumentList.Add(argument);using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}Assert.True(process.ExitCode==0,await stdout+await stderr);
            var rejected=Assert.Throws<InvalidOperationException>(()=>new ApplicationPackager().Create(catalog,directory+"-full","Example.CrossFull",source,ApplicationPackageMode.FullApplication));Assert.Contains("明确属于包内项目和后台",rejected.Message);Assert.False(Directory.Exists(directory+"-full"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);if(Directory.Exists(directory+"-full"))await TestDirectoryCleanup.DeleteAsync(directory+"-full",boundary);}
    }
}
