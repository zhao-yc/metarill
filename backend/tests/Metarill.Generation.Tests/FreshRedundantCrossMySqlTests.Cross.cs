using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshRedundantCrossMySqlTests
{
    /// <summary>保留重复表的各层角色和独立名称字段；状态维护页不能冒充候选页。</summary>
    private static (string Key,string Table,string Parent,string Name,string Page)[] Levels(Probe p)
    {
        using var json=JsonDocument.Parse(p.Catalog.ConfigColumns.Single(x=>x.Id=="25").ControlDetail);return json.RootElement.GetProperty("levels").EnumerateArray().Select(x=>{var table=x.GetProperty("table").GetString()!;var id=p.Catalog.ConfigTables.Single(t=>t.TableName==table).Id;return(x.GetProperty("key").GetString()!,table,x.TryGetProperty("parentField",out var parent)?parent.GetString()??"":"",x.TryGetProperty("nameField",out var name)?name.GetString()??"name":"name",p.Catalog.ConfigPages.First(page=>page.ConfigTableId==id&&page.Id!="211").Id);}).ToArray();
    }
    /// <summary>正常、另根、停用和外项目分支只写本轮专属新库；来源的每个真实角色名独立赋值。</summary>
    private static async Task SeedCross(Probe p)
    {
        var levels=Levels(p);for(var group=0;group<4;group++)for(var role=0;role<3;role++)
        {
            var source=levels[role].Table==p.Source;var id=First+group*10+role;var values=Row("id",id,"name",Label(role,group,"name"),"yz_project_id",group==3?Project+1:Project,"status",group==2?0:1);if(role>0)values[levels[role].Parent]=p.Parent(id-1);if(source){values["code"]="e";values["amount"]=1m;foreach(var field in levels.Where(x=>x.Table==p.Source).Select(x=>x.Name).Distinct())values[field]=Label(role,group,field);}await p.Store.InsertAsync(levels[role].Table,values);
        }
    }
    /// <summary>正式身份验证各级物理缓存、原整entity、同实体多角色、撤权及文件晚期失败的整笔回滚。</summary>
    private static async Task VerifyCross(Probe p)
    {
        var levels=Levels(p);var roles=Enumerable.Range(0,3).Where(x=>(p.Mask&(1<<x))!=0).ToArray();var names=new Dictionary<(string Table,string Field,long Id),string>();for(var group=0;group<4;group++)for(var node=0;node<3;node++)foreach(var level in levels.Where(x=>x.Table==levels[node].Table))names[(level.Table,level.Name,First+group*10+node)]=Label(node,group,level.Name);
        string Name(int role,long id)=>names[(levels[role].Table,levels[role].Name,id)];
        string Expected(long[] path)=>string.Join("/",path.Select((id,i)=>Name(i,id)));
        long[] original=[First,First+1,First+2];
        async Task<JsonElement> Options(int role,HttpStatusCode expected=HttpStatusCode.OK)=>await p.Post("cascadeTreeOptions25",new{levelKey=levels[role].Key,selectedPath=new{countryId=Id(First),areaId=Id(First+1),placeId=Id(First+2)}},expected);
        async Task AssertPath(long[] path){var data=await p.Post("cascadeTreePath25",new{leafId=Id(First+2)});Assert.Equal(Expected(path),data.GetProperty("displayText").GetString());Assert.Equal(path.Select(Id),data.GetProperty("path").EnumerateArray().Select(x=>x.GetProperty("id").GetString()));}
        for(var role=0;role<3;role++){var candidates=(await Options(role)).EnumerateArray().ToArray();Assert.Contains(candidates,x=>x.GetProperty("id").GetString()==Id(First+role)&&x.GetProperty("name").GetString()==Name(role,First+role));Assert.DoesNotContain(candidates,x=>x.GetProperty("id").GetString()==Id(First+20+role)||x.GetProperty("id").GetString()==Id(First+30+role));}await AssertPath(original);foreach(var offset in new[]{20,30,999})await p.Post("cascadeTreePath25",new{leafId=Id(First+offset+2)},HttpStatusCode.InternalServerError);
        var bytes=Encoding.UTF8.GetBytes("R1原接口正式文件");var file=await p.Upload(bytes);var added=await p.Post("add",new{name="正式来源",placeId=Id(First+2),attachments=new[]{file}});p.RecordId=added.GetProperty("id").GetString()!;await Stored(original);await p.References(file,1);await p.Download(file,bytes);
        // 原详情整entity回传只读缓存，服务端必须忽略伪造名称并按真实关系重算。
        var entity=(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data");var body=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(entity.GetRawText())!;foreach(var property in new[]{"countryName","areaName","placeName","placeIdDisplayText"})body[property]=JsonSerializer.SerializeToElement("客户端伪造");await p.Post("update",body);await Stored(original);var before=await p.Snapshot();body["creating"]=JsonSerializer.SerializeToElement(true);await p.Post("update",body,HttpStatusCode.BadRequest);Assert.Equal(before,await p.Snapshot());
        before=await p.Snapshot();await p.Post("update",new{id=p.RecordId,placeId=Id(First+22)},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.Post("update",new{id=p.RecordId,attachments=new[]{Id(long.MaxValue)}},HttpStatusCode.Unauthorized);Assert.Equal(before,await p.Snapshot());
        await p.Post("update",new{id=p.RecordId,countryId=Id(First),areaId=(string?)null,placeId=(string?)null});await Stored([First]);await p.Post("update",new{id=p.RecordId,areaId=Id(First+1)});await Stored([First,First+1]);await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Stored(original);
        // 每个实际来源角色均经普通账号原页改名，缓存传播不改变任何已存ID。
        foreach(var role in roles){var value="正式新名"+role;await p.Send(p.Reader,HttpMethod.Post,p.Route+"update",Row("id",Id(First+role),GenerationModel.Camel(levels[role].Name),value));names[(levels[role].Table,levels[role].Name,First+role)]=value;await Stored(original);await AssertPath(original);}
        // 有限层位允许同一实体承担多个角色；改变真实父键后显式重选，不能假定缓存祖先ID自动变化。
        var edits=new List<(int Level,long Id,long? Parent)>();long[] alias;
        if(p.Mask==3){edits.Add((1,First+1,First+1));alias=[First+1,First+1,First+2];}
        else if(p.Mask==5){edits.Add((1,First+1,First+2));alias=[First+2,First+1,First+2];}
        else{edits.Add((1,First+2,p.Mask==7?First+2:First));edits.Add((2,First+2,First+2));alias=p.Mask==7?[First+2,First+2,First+2]:[First,First+2,First+2];}
        foreach(var edit in edits)await ChangeParent(edit.Level,edit.Id,edit.Parent);await AssertPath(alias);await p.Post("update",new{id=p.RecordId,countryId=Id(alias[0]),areaId=Id(alias[1]),placeId=Id(alias[2])});await Stored(alias);foreach(var edit in edits.AsEnumerable().Reverse())await ChangeParent(edit.Level,edit.Id,edit.Level==1&&edit.Id==First+2?null:First+edit.Level-1);await p.Post("update",new{id=p.RecordId,countryId=Id(First),areaId=Id(First+1),placeId=Id(First+2)});await Stored(original);
        var copied=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);var page=await p.Post("page",new PageRequest{PageSize=1,Filters=new(){{"placeId",JsonSerializer.SerializeToElement(new{countryId=Id(First)})}}});Assert.Equal("2",page.GetProperty("total").GetString());Assert.Single(page.GetProperty("records").EnumerateArray());
        // 专属库临时约束使合法文件在最终引用落库时失败；同一请求解除故障后成功，证明真实DML整笔回滚。
        var secondBytes=Encoding.UTF8.GetBytes("晚期文件引用故障");var second=await p.Upload(secondBytes);var constraint="mt_r1_ref_"+Guid.NewGuid().ToString("N")[..12];var installed=false;var lateRole=roles[0];var lateName="晚期事务新名";var lateBody=Row("id",Id(First+lateRole),GenerationModel.Camel(levels[lateRole].Name),lateName,"attachments",new[]{second});
        try
        {
            await Sql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+long.Parse(second,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)+")");installed=true;before=await p.Snapshot();await p.Post("update",lateBody,HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.References(second,0);await Stored(original);await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");installed=false;await p.Post("update",lateBody);names[(levels[lateRole].Table,levels[lateRole].Name,First+lateRole)]=lateName;await p.References(second,1);await p.Download(second,secondBytes);await Stored(original);
        }
        finally{if(installed)await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");}
        await p.Grant(p.SourcePage,"[\"is_update\"]");await Options(roles[0],HttpStatusCode.Forbidden);await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)},HttpStatusCode.Forbidden);await p.Download(file,bytes,HttpStatusCode.Unauthorized);await p.Post("update",new{id=p.RecordId,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null});await Stored([],false);await p.Grant(p.SourcePage,"[\"is_all\"]");await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Stored(original);await p.Download(file,bytes);
        var external=Enumerable.Range(0,3).FirstOrDefault(x=>!roles.Contains(x),-1);if(external>=0){await p.Grant(levels[external].Page,"[]");await Options(external,HttpStatusCode.Forbidden);await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId);await p.Grant(levels[external].Page,"[\"is_all\"]");}await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copied});await p.References(file,1);await p.Download(file,bytes);
        /// <summary>直接检查数据库各级ID和名称，不能只看VO或把冗余存储当稀疏单槽。</summary>
        async Task Stored(long[] path,bool read=true){var raw=Assert.Single(await p.Store.ReadIncludingDeletedAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));for(var role=0;role<3;role++){var key=new[]{"country","area","place"}[role];if(role<path.Length){Assert.Equal(Id(path[role]),SqlRowStore.Text(raw,key+"_id"));Assert.Equal(Name(role,path[role]),SqlRowStore.Text(raw,key+"_name"));}else{Assert.Null(raw[key+"_id"]);Assert.Null(raw[key+"_name"]);}}if(!read)return;var data=(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data");Assert.Equal(path.Length==0?null:Expected(path),data.GetProperty("placeIdDisplayText").ValueKind==JsonValueKind.Null?null:data.GetProperty("placeIdDisplayText").GetString());}
        /// <summary>真实父字段从冻结定义读取，客户端仍用原JSON属性及字符串ID。</summary>
        Task<JsonElement> ChangeParent(int level,long node,long? parent)=>p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+levels[level].Table+"/"+levels[level].Page+"/update",Row("id",Id(node),GenerationModel.Camel(levels[level].Parent),parent is null?null:Id(parent.Value)));
        /// <summary>只执行本轮专属库内精确临时约束，不授触发器或全局观测权限。</summary>
        async Task Sql(string sql){await using var connection=p.Session.Provider.CreateConnection(p.Session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
    /// <summary>物理角色名与普通业务名刻意不同，避免误用name也通过。</summary>
    private static string Label(int role,int group,string field)=>"角色"+role+"业务"+group+(field=="name"?"":"-"+field);
}
