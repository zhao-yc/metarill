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
        var catalog=CrossTableExcelImportColumnsTests.Catalog("pathexcel",key);catalog.ConfigPages.Single(page=>page.Id=="201").OrderBy="id desc";
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
            void Source(long id,string name,long? area,long? place,long country=root)
            {runtime.Add("PathexcelSource",new Dictionary<string,object?>{{"Id",id},{"Name",name},{"YzProjectId",project},{"Status",1},{"CountryId",country},{"AreaId",area},{"PlaceId",place}});}
            Add("Country",root,"国");Add("Area",middle,"区","PathexcelCountryId",root);
            for(var index=0;index<3;index++){Add("Place",leaf+index,"城"+index,"PathexcelAreaId",middle);Source(101+index,"叶"+index,middle,leaf+index);}
            Source(104,"根",null,null);Source(105,"中",middle,null);Source(106,"重复",middle,leaf);
            foreach(var (id,status,owner,deleted) in new[]{(81L,0,project,false),(82L,1,2L,false),(83L,1,project,true)})Add("Country",id,"国",status:status,owner:owner,deleted:deleted);
            var exported=await runtime.Export();var header=exported.Headers.ToList().IndexOf("级联");var nameColumn=exported.Headers.ToList().IndexOf("名称");Assert.True(header>=0&&nameColumn>=0);
            var cells=exported.Rows.ToDictionary(row=>row[nameColumn]!.ToString()!,row=>row[header]?.ToString());
            Assert.Equal("国//",cells["根"]);Assert.Equal("国/区/",cells["中"]);Assert.Equal("国/区/城0",cells["重复"]);Assert.Equal("国/区/城2",cells["叶2"]);
            var targets=runtime.ReadCalls.Where(call=>call.Entity!="PathexcelSource").ToArray();
            Assert.Equal(8,targets.Length);Assert.All(targets,call=>Assert.Equal("ToListAsync",call.Method));
            Assert.Equal(10,targets.Sum(call=>call.Count));Assert.Equal(3,targets[0].Count);Assert.Equal(1,runtime.Snapshots);Assert.Equal(0,runtime.ExportLockCount);
            // 不同父下允许同名地区和城市，名称缓存必须同时绑定固定层角色与真实父键。
            Add("Country",root+1000,"国乙");Add("Area",middle+1000,"区","PathexcelCountryId",root+1000);Add("Place",leaf+1000,"城0","PathexcelAreaId",middle+1000);Source(107,"异父同名",middle+1000,leaf+1000,root+1000);
            runtime.ReadCalls.Clear();var second=await runtime.Export();var other=Assert.Single(second.Rows,row=>row[nameColumn]?.ToString()=="异父同名");Assert.Equal("国乙/区/城0",other[header]);
            targets=runtime.ReadCalls.Where(call=>call.Entity!="PathexcelSource").ToArray();Assert.Equal(11,targets.Length);Assert.Equal(16,targets.Sum(call=>call.Count));Assert.Equal(2,runtime.Snapshots);
            // 同一Service的新导出必须重新检查名称唯一性，不能复用上一次查询的唯一候选。
            Add("Area",middle+1001,"区","PathexcelCountryId",root+1000);await Assert.ThrowsAsync<InvalidOperationException>(()=>runtime.Export());
            // 导入当前读仍每次实时反查并锁读完整路径，不能使用导出的名称缓存。
            runtime.ReadCalls.Clear();for(var repeat=0;repeat<2;repeat++){var parsed=await runtime.Parse(new Dictionary<string,object?>{{"级联","国/区/城0"}});Assert.Equal(leaf.ToString(CultureInfo.InvariantCulture),parsed["placeId"].ToString());}
            Assert.Equal(12,runtime.ReadCalls.Count(call=>call.Entity!="PathexcelSource"));
            // 在最后一次真实名称查询后换掉身份，下一次根名称缓存命中仍必须拒绝，不能省略Scope检查。
            var originalActor=new RuntimeActor(901,"批量导出",false,project.ToString(CultureInfo.InvariantCulture),"1");var identity=new SwitchingIdentity(originalActor);
            using var switched=new SharedExcelReadTransport(assembly,ns,"PathexcelSource",actor:originalActor,authorization:identity);
            foreach(var (entity,id,label,field,parentId) in new[]{("Country",root,"国","",0L),("Area",middle,"区","PathexcelCountryId",root),("Place",leaf,"城","PathexcelAreaId",middle)})
            {var values=new Dictionary<string,object?>{{"Id",id},{"Name",label},{"YzProjectId",project},{"Status",1}};if(field.Length>0)values[field]=parentId;switched.Add("Pathexcel"+entity,values);}
            switched.Add("PathexcelSource",new Dictionary<string,object?>{{"Id",201L},{"Name","叶"},{"YzProjectId",project},{"Status",1},{"CountryId",root},{"AreaId",middle},{"PlaceId",leaf}});
            switched.Add("PathexcelSource",new Dictionary<string,object?>{{"Id",101L},{"Name","根"},{"YzProjectId",project},{"Status",1},{"CountryId",root}});
            var flipped=false;switched.AfterRead=(entity,limit)=>{if(entity=="PathexcelPlace"&&limit==2){identity.Current=new(999,"已换身份",false,"99","1");flipped=true;}};
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>switched.Export());Assert.True(flipped);Assert.Equal(6,switched.ReadCalls.Count(call=>call.Entity!="PathexcelSource"));
            var hand=Path.Combine(generated,"Services/PathexcelSourceService.cs");var bytes=File.ReadAllBytes(hand);generator.Generate(catalog,generated,ns);Assert.Equal(bytes,File.ReadAllBytes(hand));Assert.Equal(before,JsonSerializer.Serialize(catalog));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>只替换请求身份读口，不授予额外目标list权限，不替换生成业务方法。</summary>
    private sealed class SwitchingIdentity(RuntimeActor actor):IRuntimeAuthorization
    {
        internal RuntimeActor Current=actor;
        /// <summary>模拟当前请求上下文发生身份切换。</summary>
        public RuntimeActor RequireActor()=>Current;
        /// <summary>导出仍沿来源权限，不借本夹具增加目标查询授权。</summary>
        public void RequirePagePermission(string page,string operation)=>throw new UnauthorizedAccessException();
    }
}
