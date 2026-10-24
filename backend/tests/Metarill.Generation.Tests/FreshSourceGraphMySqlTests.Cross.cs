using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshSourceGraphMySqlTests
{
    /// <summary>固定层定义保留各角色，不按重复表去重；目标页从真实发布元数据唯一确定。</summary>
    private static (string Key,string Table,string Parent,string Page)[] Levels(Probe p)
    {
        using var json=JsonDocument.Parse(p.Catalog.ConfigColumns.Single(x=>x.Id=="25").ControlDetail);return json.RootElement.GetProperty("levels").EnumerateArray().Select(x=>{var table=x.GetProperty("table").GetString()!;var id=p.Catalog.ConfigTables.Single(t=>t.TableName==table).Id;return(x.GetProperty("key").GetString()!,table,x.TryGetProperty("parentField",out var parent)?parent.GetString()??"":"",p.Catalog.ConfigPages.First(page=>page.ConfigTableId==id).Id);}).ToArray();
    }
    /// <summary>四条分支分别正常、另根、停用及外项目，只写本次随机库。</summary>
    private static async Task SeedCross(Probe p)
    {
        var levels=Levels(p);for(var group=0;group<4;group++)for(var role=0;role<3;role++)
        {
            var id=First+group*10+role;var source=levels[role].Table==p.Source;var project=group==3?Project+1:Project;var values=Row("id",id,"name",Business(role,group),"yz_project_id",project,"status",group==2?0:1);if(role>0)values[levels[role].Parent]=p.Parent(id-1);if(source){values["code"]="e";values["amount"]=1m;if(p.Mode!="physical"){await p.Store.InsertAsync("source_graph_cross_company",Row("id",Company(id),"name",CompanyName(role,group),"yz_project_id",project));values["company_id"]=Company(id);}}await p.Store.InsertAsync(levels[role].Table,values);
        }
    }
    /// <summary>正式候选、路径、当前改名、同实体角色、稀疏保存及同页撤权使用原HTTP。</summary>
    private static async Task VerifyCross(Probe p)
    {
        var levels=Levels(p);var roles=Enumerable.Range(0,3).Where(x=>(p.Mask&(1<<x))!=0).ToArray();var business=Enumerable.Range(0,3).ToDictionary(x=>First+x,x=>Business(x,0));var companies=Enumerable.Range(0,3).ToDictionary(x=>Company(First+x),x=>CompanyName(x,0));var foreign=Enumerable.Range(0,3).ToDictionary(x=>First+x,x=>Company(First+x));
        bool Joined(int role)=>roles.Contains(role)&&p.Mode!="physical"&&(p.Mode!="mixed"||role!=roles[0]);
        string Name(int role,long id)=>Joined(role)?companies[foreign[id]]:business[id];
        string Expected(long[]? path=null)=>string.Join("/",(path??[First,First+1,First+2]).Select((id,index)=>Name(index,id)));
        async Task<JsonElement> Options(int role,HttpStatusCode expected=HttpStatusCode.OK)=>await p.Post("cascadeTreeOptions25",new{levelKey=levels[role].Key,selectedPath=new{countryId=Id(First),areaId=Id(First+1),placeId=Id(First+2)}},expected);
        async Task AssertPath(long[]? ids=null){var response=await p.Post("cascadeTreePath25",new{leafId=Id(First+2)});Assert.Equal(Expected(ids),response.GetProperty("displayText").GetString());Assert.Equal((ids??[First,First+1,First+2]).Select(Id),response.GetProperty("path").EnumerateArray().Select(x=>x.GetProperty("id").GetString()));}
        for(var role=0;role<3;role++){var candidates=(await Options(role)).EnumerateArray().ToArray();Assert.Contains(candidates,x=>x.GetProperty("id").GetString()==Id(First+role)&&x.GetProperty("name").GetString()==Name(role,First+role));Assert.DoesNotContain(candidates,x=>x.GetProperty("id").GetString()==Id(First+20+role)||x.GetProperty("id").GetString()==Id(First+30+role));}await AssertPath();foreach(var offset in new[]{20,30,999})await p.Post("cascadeTreePath25",new{leafId=Id(First+offset+2)},HttpStatusCode.InternalServerError);
        var bytes=Encoding.UTF8.GetBytes("重复来源正式Web文件");var file=await p.Upload(bytes);var added=await p.Post("add",new{name="正式来源",placeId=Id(First+2),attachments=new[]{file}});p.RecordId=added.GetProperty("id").GetString()!;Assert.Equal(Expected(),added.GetProperty("placeIdDisplayText").GetString());await Sparse(2,First+2);await p.References(file,1);await p.Download(file,bytes);
        var before=await p.Snapshot();await p.Post("update",new{id=p.RecordId,placeId=Id(First+22)},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.Post("update",new{id=p.RecordId,attachments=new[]{Id(long.MaxValue)}},HttpStatusCode.Unauthorized);Assert.Equal(before,await p.Snapshot());
        var partial=await p.Post("update",new{id=p.RecordId,countryId=Id(First),areaId=(string?)null,placeId=(string?)null});Assert.Equal(Name(0,First),partial.GetProperty("placeIdDisplayText").GetString());await Sparse(0,First);partial=await p.Post("update",new{id=p.RecordId,areaId=Id(First+1)});Assert.Equal(Name(0,First)+"/"+Name(1,First+1),partial.GetProperty("placeIdDisplayText").GetString());await Sparse(1,First+1);await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await Sparse(2,First+2);
        foreach(var role in roles)
        {
            var node=First+role;if(Joined(role)){before=await p.Snapshot();await p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/source_graph_cross_company/601/update",new{id=Id(foreign[node]),name="正式公司新名"+role});companies[foreign[node]]="正式公司新名"+role;Assert.Equal(before,await p.Snapshot());await p.Send(p.Owner,HttpMethod.Post,p.Route+"update",new{id=Id(node),companyId=Id(Company(First+10+role))});foreign[node]=Company(First+10+role);companies[foreign[node]]=CompanyName(role,1);}else{await p.Send(p.Owner,HttpMethod.Post,p.Route+"update",new{id=Id(node),name="正式业务新名"+role});business[node]="正式业务新名"+role;}
            Assert.Equal(Expected(),(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data").GetProperty("placeIdDisplayText").GetString());await AssertPath();
        }
        // 通过真实CRUD让同一实体承担重复角色，有限跨表层位不应被错误当作递归自树。
        var edits=new List<(int Level,long Id,long? Parent)>();long[] aliasPath;
        if(p.Mask==3){edits.Add((1,First+1,First+1));aliasPath=[First+1,First+1,First+2];}
        else if(p.Mask==5){edits.Add((1,First+1,First+2));aliasPath=[First+2,First+1,First+2];}
        else{edits.Add((1,First+2,p.Mask==7?First+2:First));edits.Add((2,First+2,First+2));aliasPath=p.Mask==7?[First+2,First+2,First+2]:[First,First+2,First+2];}
        foreach(var edit in edits)await ChangeParent(edit.Level,edit.Id,edit.Parent);await AssertPath(aliasPath);Assert.Equal(Expected(aliasPath),(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data").GetProperty("placeIdDisplayText").GetString());foreach(var edit in edits.AsEnumerable().Reverse())await ChangeParent(edit.Level,edit.Id,edit.Level==1&&edit.Id==First+2?null:First+edit.Level-1);await AssertPath();
        var copied=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);var page=(await p.Post("page",new PageRequest{PageSize=1,Filters=new(){{"placeId",JsonSerializer.SerializeToElement(new{countryId=Id(First)})}}}));Assert.Equal("2",page.GetProperty("total").GetString());Assert.Single(page.GetProperty("records").EnumerateArray());
        // 同页目标撤权保留更新许可；显式重选拒绝，清空不额外要求目标查询。
        await p.Grant(p.SourcePage,"[\"is_update\"]");await Options(roles[0],HttpStatusCode.Forbidden);await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)},HttpStatusCode.Forbidden);await p.Download(file,bytes,HttpStatusCode.Unauthorized);await p.Post("update",new{id=p.RecordId,countryId=(string?)null,areaId=(string?)null,placeId=(string?)null});await Sparse(-1,0);await p.Grant(p.SourcePage,"[\"is_all\"]");await p.Post("update",new{id=p.RecordId,placeId=Id(First+2)});await p.Download(file,bytes);
        var external=Enumerable.Range(0,3).FirstOrDefault(x=>!roles.Contains(x),-1);if(external>=0){await p.Grant(levels[external].Page,"[]");await Options(external,HttpStatusCode.Forbidden);await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId);await p.Grant(levels[external].Page,"[\"is_all\"]");}
        await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copied});await p.References(file,1);await p.Download(file,bytes);
        /// <summary>实际物理存储只有所选一级，名称和祖先不回填。</summary>
        async Task Sparse(int depth,long value){var raw=Assert.Single(await p.Store.ReadIncludingDeletedAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));for(var i=0;i<3;i++){var stored=raw[new[]{"country_id","area_id","place_id"}[i]];if(i==depth)Assert.Equal(Id(value),Convert.ToString(stored,CultureInfo.InvariantCulture));else Assert.Null(stored);}Assert.DoesNotContain((await new SchemaService(p.Session).InspectAsync(p.Catalog)).Tables.Single(x=>x.Name==p.Source).Columns,x=>x.Name is "country_name" or "area_name" or "place_name" or "display_label");}
        /// <summary>真实父FK名称来自冻结元数据，HTTP仅提交原字段及字符串ID。</summary>
        Task<JsonElement> ChangeParent(int level,long node,long? parent)=>p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/"+levels[level].Table+"/"+levels[level].Page+"/update",Row("id",Id(node),GenerationModel.Camel(levels[level].Parent),parent is null?null:Id(parent.Value)));
    }
    /// <summary>业务名称不与派生名称混用。</summary>
    private static string Business(int role,int group)=>"角色"+role+"业务"+group;
    /// <summary>公司名称与节点实体ID独立。</summary>
    private static string CompanyName(int role,int group)=>"角色"+role+"公司"+group;
    /// <summary>公司主键始终是另一组长整数。</summary>
    private static long Company(long id)=>checked(id+100000L);
}
