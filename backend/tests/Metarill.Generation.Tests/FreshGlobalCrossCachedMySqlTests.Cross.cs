using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshGlobalCrossCachedMySqlTests
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
        var type=p.Catalog.ConfigColumns.Single(x=>x.Id=="25").LogicalType;await p.Store.InsertAsync(p.Source,Row("id",First+100,"name","外项目来源","yz_project_id",Project+1,"place_id",type=="string"?Id(First+2):(object)(First+2),"country_id",type=="string"?Id(First):(object)First,"area_id",type=="string"?Id(First+1):(object)(First+1),"country_name",Label(0,0),"area_name",Label(1,0),"place_name",Label(2,0)));
    }
    /// <summary>完整包真实普通账号验证跨项目共享目标、混合层隔离、各层物理缓存与文件原事务。</summary>
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
        // 130条来源跨越内部批次；改名和回填保留ID及外项目原值。
        var type=p.Catalog.ConfigColumns.Single(x=>x.Id=="25").LogicalType;object Key(long id)=>type=="string"?Id(id):id;
        for(var i=0;i<130;i++)await p.Store.InsertAsync(p.Source,Row("id",First+1000+i,"name","跨批来源","yz_project_id",Project,"country_id",Key(First),"area_id",Key(First+1),"place_id",Key(First+2),"country_name",names[0],"area_name",names[1],"place_name",names[2]));
        names[0]="跨批根新名";await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+levels[0].Table+"/"+levels[0].Page+"/update",new{id=Id(First),name=names[0]});await Cached();
        foreach(var row in await Own())await p.Store.UpdateAsync(p.Source,SqlRowStore.Id(row),Row("country_name","历史错误","area_name","历史错误","place_name","历史错误"));
        var maintained=await p.Send(p.Owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="2"});Assert.True(maintained.GetProperty("data").GetProperty("affectedRows").GetInt64()>128);await Cached();Assert.Equal(0,(await p.Send(p.Owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="2"})).GetProperty("data").GetProperty("affectedRows").GetInt64());
        // 最末消费者约束制造晚批失败，目标、全部来源和文件引用必须整笔回滚。
        var cacheConstraint="mt_gc_cache_"+Guid.NewGuid().ToString("N")[..10];var cacheInstalled=false;
        try{await Sql("ALTER TABLE `"+p.Source+"` ADD CONSTRAINT `"+cacheConstraint+"` CHECK (`id` <> "+Id(First+1129)+" OR `country_name` <> 'cache-late-failure')");cacheInstalled=true;before=await p.Snapshot();await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+levels[0].Table+"/"+levels[0].Page+"/update",new{id=Id(First),name="cache-late-failure"},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await Cached();}finally{if(cacheInstalled)await Sql("ALTER TABLE `"+p.Source+"` DROP CHECK `"+cacheConstraint+"`");}
        // 原DELETE分别验证根软删及末级物理删除，只清相应名称并保留全部ID。
        foreach(var role in new[]{0,2}){var saved=Assert.Single(await p.Store.ReadIncludingDeletedAsync(levels[role].Table,Row("id",First+role)));await p.Send(p.Reader,HttpMethod.Delete,"/api/admin/auto/"+levels[role].Table+"/"+levels[role].Page+"/",new{id=Id(First+role)});foreach(var row in await Own()){Assert.Null(row[new[]{"country_name","area_name","place_name"}[role]]);Assert.Equal(Id(First+role),SqlRowStore.Text(row,new[]{"country_id","area_id","place_id"}[role]));}if(saved.ContainsKey("delete_no"))await p.Store.UpdateAsync(levels[role].Table,First+role,Row("delete_no",null));else await p.Store.InsertAsync(levels[role].Table,saved);await p.Send(p.Owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="2"});await Cached();}
        foreach(var row in (await Own()).Where(x=>SqlRowStore.Id(x)>=First+1000&&SqlRowStore.Id(x)<=First+1129))await p.Store.UpdateAsync(p.Source,SqlRowStore.Id(row),Row("delete_no",new DateTime(2026,1,1)));
        await p.Post("update",new{id=p.RecordId,placeId=(string?)null});await Sparse(1);await p.Post("update",new{id=p.RecordId,areaId=(string?)null});await Sparse(0);await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Sparse(2);
        // 分页前按逻辑祖先筛选；外项目来源不能因全局目标相同而混入COUNT或记录。
        var copied=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);page=await p.Post("page",new PageRequest{PageSize=1,Filters=new(){{"placeId",JsonSerializer.SerializeToElement(new{countryId=Id(First)})}}});Assert.Equal("2",page.GetProperty("total").GetString());Assert.Single(page.GetProperty("records").EnumerateArray());
        for(var role=0;role<3;role++){await p.Grant(levels[role].Page,"[]");await Options(role,HttpStatusCode.Forbidden);before=await p.Snapshot();await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)},HttpStatusCode.Forbidden);Assert.Equal(before,await p.Snapshot());await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId);await p.Grant(levels[role].Page,"[\"is_all\"]");}
        // 在专属库最终引用DML注入精确故障，解除后同一合法原HTTP必须成功。
        var secondBytes=Encoding.UTF8.GetBytes("全局目标晚期引用");var second=await p.Upload(secondBytes);var constraint="mt_gl_ref_"+Guid.NewGuid().ToString("N")[..12];var installed=false;var late=new{id=p.RecordId,name="晚期来源修改",attachments=new[]{second}};
        try{await Sql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+long.Parse(second,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)+")");installed=true;before=await p.Snapshot();await p.Post("update",late,HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.References(second,0);await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");installed=false;await p.Post("update",late);await p.References(second,1);await p.References(file,1);await p.Download(second,secondBytes);}finally{if(installed)await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");}
        await p.Grant(p.SourcePage,"[\"is_update\"]");await Options(0,HttpStatusCode.Forbidden);await p.Download(second,secondBytes,HttpStatusCode.Unauthorized);await p.Post("update",new{id=p.RecordId,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null});await Sparse(-1);await p.Grant(p.SourcePage,"[\"is_all\"]");await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Sparse(2);await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copied});await p.References(file,0);await p.References(second,1);await p.Download(second,secondBytes);
        /// <summary>三个位保留已选择路径的全部祖先和名称缓存；项目字段必须仍为真实来源项目。</summary>
        async Task Sparse(int depth){var row=Assert.Single(await p.Store.ReadIncludingDeletedAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));Assert.Equal(Id(Project),SqlRowStore.Text(row,"yz_project_id"));for(var role=0;role<3;role++){var value=row[new[]{"country_id","area_id","place_id"}[role]];if(role<=depth){Assert.Equal(Id(First+role),Convert.ToString(value,CultureInfo.InvariantCulture));Assert.Equal(names[role],SqlRowStore.Text(row,new[]{"country_name","area_name","place_name"}[role]));}else{Assert.Null(value);Assert.Null(row[new[]{"country_name","area_name","place_name"}[role]]);}}}
        /// <summary>仅当前项目活跃消费者进入检查范围。</summary>
        async Task<IReadOnlyList<Dictionary<string,object?>>> Own()=>await p.Store.ReadAsync(p.Source,Row("yz_project_id",Project));
        /// <summary>检查物理缓存、关系ID与外项目原值，不能只靠VO证明传播。</summary>
        async Task Cached(){foreach(var row in await Own())for(var role=0;role<3;role++){Assert.Equal(Id(First+role),SqlRowStore.Text(row,new[]{"country_id","area_id","place_id"}[role]));Assert.Equal(names[role],SqlRowStore.Text(row,new[]{"country_name","area_name","place_name"}[role]));}var foreign=Assert.Single(await p.Store.ReadIncludingDeletedAsync(p.Source,Row("id",First+100)));for(var role=0;role<3;role++)Assert.Equal(Label(role,0),SqlRowStore.Text(foreign,new[]{"country_name","area_name","place_name"}[role]));}
        /// <summary>精确临时约束只存在于本次租约，不改平台元数据或长期业务结构。</summary>
        async Task Sql(string sql){await using var connection=p.Session.Provider.CreateConnection(p.Session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
    /// <summary>各层和分支名称均不同，避免路径错层仍通过。</summary>
    private static string Label(int role,int group)=>"层"+role+"分支"+group;
}
