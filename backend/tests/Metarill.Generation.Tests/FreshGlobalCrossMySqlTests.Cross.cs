using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshGlobalCrossMySqlTests
{
    /// <summary>层位、目标页和全局声明只从本轮冻结发布目录读取。</summary>
    private static (string Key,string Table,string Parent,string Page,bool Global)[] Levels(Probe p)
    {
        using var json=JsonDocument.Parse(p.Catalog.ConfigColumns.Single(x=>x.Id=="25").ControlDetail);return json.RootElement.GetProperty("levels").EnumerateArray().Select(x=>{var table=p.Catalog.ConfigTables.Single(t=>t.TableName==x.GetProperty("table").GetString());return(x.GetProperty("key").GetString()!,table.TableName,x.TryGetProperty("parentField",out var parent)?parent.GetString()??"":"",p.Catalog.ConfigPages.Single(page=>page.ConfigTableId==table.Id).Id,Metarill.Core.Metadata.GlobalCrossTableTarget.Declared(table));}).ToArray();
    }
    /// <summary>全局正常分支刻意使用外项目值或无项目列；普通目标和来源仍按真实项目隔离。</summary>
    private static async Task SeedCross(Probe p)
    {
        var levels=Levels(p);for(var group=0;group<4;group++)for(var role=0;role<3;role++)
        {
            var level=levels[role];var id=First+group*10+role;var values=Row("id",id,"name",Label(role,group),"status",group==2?0:1);var table=p.Catalog.ConfigTables.Single(x=>x.TableName==level.Table);if(p.Catalog.ConfigColumns.Any(x=>x.ConfigTableId==table.Id&&x.ColumnName=="yz_project_id"))values["yz_project_id"]=level.Global||group==3?Project+1:Project;if(role>0)values[level.Parent]=p.Parent(id-1);await p.Store.InsertAsync(level.Table,values);
        }
        var type=p.Catalog.ConfigColumns.Single(x=>x.Id=="25").LogicalType;await p.Store.InsertAsync(p.Source,Row("id",First+100,"name","外项目来源","yz_project_id",Project+1,"place_id",type=="string"?Id(First+2):(object)(First+2)));
    }
    /// <summary>完整包真实普通账号验证跨项目共享目标、混合层隔离、稀疏读写与文件原事务。</summary>
    private static async Task VerifyCross(Probe p)
    {
        var levels=Levels(p);var names=Enumerable.Range(0,3).Select(x=>Label(x,0)).ToArray();long[] original=[First,First+1,First+2];string Display(int count=3)=>string.Join("/",names.Take(count));
        async Task<JsonElement> Options(int role,HttpStatusCode expected=HttpStatusCode.OK)=>await p.Post("cascadeTreeOptions25",new{levelKey=levels[role].Key,selectedPath=new{countryId=Id(First),areaId=Id(First+1),placeId=Id(First+2)}},expected);
        for(var role=0;role<3;role++){var options=(await Options(role)).EnumerateArray().ToArray();Assert.Contains(options,x=>x.GetProperty("id").GetString()==Id(First+role)&&x.GetProperty("name").GetString()==names[role]);Assert.DoesNotContain(options,x=>x.GetProperty("id").GetString()==Id(First+20+role));if(role==0)Assert.Equal(levels[0].Global,options.Any(x=>x.GetProperty("id").GetString()==Id(First+30)));}
        Assert.Equal(Display(),(await p.Post("cascadeTreePath25",new{leafId=Id(First+2)})).GetProperty("displayText").GetString());foreach(var offset in new[]{20,999})await p.Post("cascadeTreePath25",new{leafId=Id(First+offset+2)},HttpStatusCode.InternalServerError);if(p.Mask==7)Assert.Equal(string.Join("/",Enumerable.Range(0,3).Select(role=>Label(role,3))),(await p.Post("cascadeTreePath25",new{leafId=Id(First+32)})).GetProperty("displayText").GetString());else await p.Post("cascadeTreePath25",new{leafId=Id(First+32)},HttpStatusCode.InternalServerError);
        var bytes=Encoding.UTF8.GetBytes("全局目标原文件");var file=await p.Upload(bytes);var added=await p.Post("add",new{name="本项目来源",placeId=Id(First+2),attachments=new[]{file}});p.RecordId=added.GetProperty("id").GetString()!;Assert.Equal(Display(),added.GetProperty("placeIdDisplayText").GetString());Assert.False(added.TryGetProperty("yzProjectId",out _));await Sparse(2);await p.References(file,1);await p.Download(file,bytes);
        var page=await p.Post("page",new PageRequest{PageSize=1});Assert.Equal("1",page.GetProperty("total").GetString());Assert.DoesNotContain(page.GetProperty("records").EnumerateArray(),x=>x.GetProperty("id").GetString()==Id(First+100));var before=await p.Snapshot();await p.Post("update",new{id=Id(First+100),name="越界来源"},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());
        var entity=(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data");var body=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(entity.GetRawText())!;foreach(var name in new[]{"countryName","areaName","placeName","placeIdDisplayText"})body[name]=JsonSerializer.SerializeToElement("伪造名称");await p.Post("update",body);await Sparse(2);Assert.Equal(Display(),(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data").GetProperty("placeIdDisplayText").GetString());before=await p.Snapshot();body["creating"]=JsonSerializer.SerializeToElement(true);await p.Post("update",body,HttpStatusCode.BadRequest);Assert.Equal(before,await p.Snapshot());
        foreach(var role in Enumerable.Range(0,3))
        {
            names[role]="真实全局混合新名"+role;await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+levels[role].Table+"/"+levels[role].Page+"/update",new{id=Id(First+role),name=names[role]});await Sparse(2);Assert.Equal(Display(),(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data").GetProperty("placeIdDisplayText").GetString());
        }
        await p.Post("update",new{id=p.RecordId,placeId=(string?)null});await Sparse(1);await p.Post("update",new{id=p.RecordId,areaId=(string?)null});await Sparse(0);await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Sparse(2);
        // 分页前按逻辑祖先筛选；外项目来源不能因全局目标相同而混入COUNT或记录。
        var copied=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);page=await p.Post("page",new PageRequest{PageSize=1,Filters=new(){{"placeId",JsonSerializer.SerializeToElement(new{countryId=Id(First)})}}});Assert.Equal("2",page.GetProperty("total").GetString());Assert.Single(page.GetProperty("records").EnumerateArray());
        for(var role=0;role<3;role++){await p.Grant(levels[role].Page,"[]");await Options(role,HttpStatusCode.Forbidden);before=await p.Snapshot();await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)},HttpStatusCode.Forbidden);Assert.Equal(before,await p.Snapshot());await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId);await p.Grant(levels[role].Page,"[\"is_all\"]");}
        // 在专属库最终引用DML注入精确故障，解除后同一合法原HTTP必须成功。
        var secondBytes=Encoding.UTF8.GetBytes("全局目标晚期引用");var second=await p.Upload(secondBytes);var constraint="mt_gl_ref_"+Guid.NewGuid().ToString("N")[..12];var installed=false;var late=new{id=p.RecordId,name="晚期来源修改",attachments=new[]{second}};
        try{await Sql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+long.Parse(second,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)+")");installed=true;before=await p.Snapshot();await p.Post("update",late,HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.References(second,0);await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");installed=false;await p.Post("update",late);await p.References(second,1);await p.References(file,1);await p.Download(second,secondBytes);}finally{if(installed)await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");}
        await p.Grant(p.SourcePage,"[\"is_update\"]");await Options(0,HttpStatusCode.Forbidden);await p.Download(second,secondBytes,HttpStatusCode.Unauthorized);await p.Post("update",new{id=p.RecordId,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null});await Sparse(-1);await p.Grant(p.SourcePage,"[\"is_all\"]");await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Sparse(2);await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copied});await p.References(file,0);await p.References(second,1);await p.Download(second,secondBytes);
        /// <summary>三个位只有实际选择位非空，名称不落物理列；项目字段必须仍为真实来源项目。</summary>
        async Task Sparse(int depth){var row=Assert.Single(await p.Store.ReadIncludingDeletedAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));Assert.Equal(Id(Project),SqlRowStore.Text(row,"yz_project_id"));for(var role=0;role<3;role++){var value=row[new[]{"country_id","area_id","place_id"}[role]];if(role==depth)Assert.Equal(Id(First+role),Convert.ToString(value,CultureInfo.InvariantCulture));else Assert.Null(value);}Assert.DoesNotContain(row.Keys,x=>x is "country_name" or "area_name" or "place_name");}
        /// <summary>精确临时约束只存在于本次租约，不改平台元数据或长期业务结构。</summary>
        async Task Sql(string sql){await using var connection=p.Session.Provider.CreateConnection(p.Session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
    /// <summary>各层和分支名称均不同，避免路径错层仍通过。</summary>
    private static string Label(int role,int group)=>"层"+role+"分支"+group;
}
