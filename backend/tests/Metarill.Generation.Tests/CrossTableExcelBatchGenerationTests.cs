using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Security;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>真实生成方法及表达式的批读接线；离线ORM传输不证明MySQL性能。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class CrossTableExcelBatchGenerationTests
{
    /// <summary>合法列名不能与其它控件的路径或缓存变量碰撞；真实编译两个共享控件。</summary>
    [Theory]
    [InlineData("cache")][InlineData("paths")]
    public async Task Generated_export_local_names_do_not_collide_with_another_control_property(string prefix)
    {
        var catalog=CrossTableExcelSharedGenerationTests.Catalog("shared-r1");var column=catalog.ConfigColumns.Single(value=>value.Id=="80");column.ColumnName=prefix+"_place_id";
        var detail=System.Text.Json.Nodes.JsonNode.Parse(column.ControlDetail)!;detail["levels"]![2]!["columnPrefix"]=prefix+"_place";column.ControlDetail=detail.ToJsonString();
        foreach(var sibling in catalog.ConfigColumns.Where(value=>value.ConfigTableId=="2"&&value.ColumnName.StartsWith("warehouse_",StringComparison.Ordinal)))sibling.ColumnName=prefix+"_place_"+sibling.ColumnName["warehouse_".Length..];
        var model=GenerationModel.Create(catalog,"Example.BatchNames");Assert.Equal(2,CrossTableWriteCompilation.ForService(model,model.Tables.Single(value=>value.Source.Id=="2"),model.Tables.Single(value=>value.Source.Id=="2").Pages.Single()).Count);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-batch-names");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try{new CSharpGenerator().Generate(catalog,Path.Combine(directory,"generated"),model.Namespace);await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);}
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>不同叶子共享祖先只读一批，混合深度和重复选择不改变原列；反查歧义仍失败。</summary>
    [Theory]
    [InlineData("string","int64")][InlineData("int64","string")]
    public async Task Generated_export_batches_paths_but_keeps_reverse_name_uniqueness_and_scope(string key,string parent)
    {
        const string ns="Example.PathBatch";const long project=9007199254740993,root=9007199254741011,middle=9007199254741021,leaf=9007199254741031;
        var catalog=CrossTableExcelImportColumnsTests.Catalog("pathexcel",key);
        foreach(var column in catalog.ConfigColumns.Where(column=>column.Id is "45" or "55"))column.LogicalType=parent;
        catalog.ConfigPageColumns.Single(field=>field.ConfigColumnId=="25").IsShow=1;var before=JsonSerializer.Serialize(catalog);
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-cross-excel-batch");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var generated=Path.Combine(directory,"generated");var generator=new CSharpGenerator();generator.Generate(catalog,generated,ns);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            using var runtime=new SharedExcelReadTransport(assembly,ns,"PathexcelSource",actor:new RuntimeActor(901,"批量导出",false,project.ToString(CultureInfo.InvariantCulture),"1"));
            void Add(string entity,long id,string name,string? parentField=null,long? parentId=null,int status=1,long owner=project,bool deleted=false)
            {
                var values=new Dictionary<string,object?>{{"Id",id},{"Name",name},{"YzProjectId",owner},{"Status",status}};
                if(parentField is not null)values[parentField]=parentId;if(deleted)values["DeleteNo"]=DateTime.UtcNow;runtime.Add("Pathexcel"+entity,values);
            }
            void Source(long id,string name,long? area,long? place)
            {runtime.Add("PathexcelSource",new Dictionary<string,object?>{{"Id",id},{"Name",name},{"YzProjectId",project},{"Status",1},{"CountryId",root},{"AreaId",area},{"PlaceId",place}});}
            Add("Country",root,"国");Add("Area",middle,"区","PathexcelCountryId",root);
            for(var index=0;index<3;index++){Add("Place",leaf+index,"城"+index,"PathexcelAreaId",middle);Source(101+index,"叶"+index,middle,leaf+index);}
            Source(104,"根",null,null);Source(105,"中",middle,null);Source(106,"重复",middle,leaf);
            foreach(var (id,status,owner,deleted) in new[]{(81L,0,project,false),(82L,1,2L,false),(83L,1,project,true)})Add("Country",id,"国",status:status,owner:owner,deleted:deleted);
            var exported=await runtime.Export();var header=exported.Headers.ToList().IndexOf("级联");var nameColumn=exported.Headers.ToList().IndexOf("名称");Assert.True(header>=0&&nameColumn>=0);
            var cells=exported.Rows.ToDictionary(row=>row[nameColumn]!.ToString()!,row=>row[header]?.ToString());
            Assert.Equal("国//",cells["根"]);Assert.Equal("国/区/",cells["中"]);Assert.Equal("国/区/城0",cells["重复"]);Assert.Equal("国/区/城2",cells["叶2"]);
            var targets=runtime.ReadCalls.Where(call=>call.Entity!="PathexcelSource").ToArray();
            Assert.Equal(15,targets.Length);Assert.All(targets,call=>Assert.Equal("ToListAsync",call.Method));
            Assert.Equal(17,targets.Sum(call=>call.Count));Assert.Equal(3,targets[0].Count);Assert.Equal(1,runtime.Snapshots);Assert.Equal(0,runtime.ExportLockCount);
            // 同一次Service的下一次导出不得复用旧祖先，真实可读重名仍明确拒绝。
            Add("Country",84,"国");await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Export());
            var hand=Path.Combine(generated,"Services/PathexcelSourceService.cs");var bytes=File.ReadAllBytes(hand);generator.Generate(catalog,generated,ns);Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
}
