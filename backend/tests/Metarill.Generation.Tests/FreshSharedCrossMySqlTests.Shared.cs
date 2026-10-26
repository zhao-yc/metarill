using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshSharedCrossMySqlTests
{
    private static string Prefix=>SharedCrossFreshCatalogTests.Prefix;
    /// <summary>每个分支与层位使用不同的精确目标ID。</summary>
    private static long Node(int role,int group=0)=>First+group*10+role;
    /// <summary>派生名称终点与目标实体使用独立ID空间。</summary>
    private static long Company(int role,int group=0)=>First+500+group*10+role;
    /// <summary>普通List候选按共同国家区分，避免错误父范围仍匹配。</summary>
    private static long Item(int group)=>First+700+group;
    /// <summary>四组真实目标区分正常、异父、失效与异项目，不用同名节点掩盖错层。</summary>
    private static async Task SeedShared(Probe p)
    {
        var roles=new[]{"country","area","place","warehouse"};
        for(var group=0;group<4;group++)
        {
            for(var role=0;role<4;role++)
            {
                var row=Row("id",Node(role,group),"yz_project_id",group==3?Project+1:Project,"status",group==2?0:1);var name=new[]{"国","区","城","仓"}[role]+group;
                if(p.Derived&&role<3){await p.Store.InsertAsync(Prefix+"_company",Row("id",Company(role,group),"name",name,"yz_project_id",group==3?Project+1:Project));row["company_id"]=Company(role,group);}else row["name"]=name;
                if(role>0)row[Prefix+"_"+(role==1?"country":"area")+"_id"]=p.Parent(Node(role==1?0:1,group));await p.Store.InsertAsync(Prefix+"_"+roles[role],row);
            }
            await p.Store.InsertAsync(Prefix+"_item",Row("id",Item(group),"name","候选"+group,"yz_project_id",Project,"country_id",Key(p,Node(0,group))));
        }
    }
    /// <summary>原正式HTTP分别验R0和R1；没有固定授权器或生成后修补。</summary>
    private static async Task VerifyShared(Probe p)
    {
        foreach(var cached in new[]{false,true}){p.Cached=cached;await VerifySource(p);p.RecordIds.Add(cached,p.RecordId);}await VerifyPropagation(p);p.Cached=true;p.RecordId=p.RecordIds[true];
    }
    /// <summary>共享变更、普通List依赖、源／目标撤权、复制与文件约束都通过真实普通账号。</summary>
    private static async Task VerifySource(Probe p)
    {
        var primary=p.Shorter?"areaId":"placeId";
        Dictionary<string,object?> Select(int group,int? other=null)=>Row(primary,Id(Node(p.Shorter?1:2,group)),"warehouseId",Id(Node(3,other??group)),"aItemId",null);
        async Task<JsonElement> Update(Dictionary<string,object?> values,HttpStatusCode status=HttpStatusCode.OK){values["id"]=p.RecordId;return await p.Post("update",values,status);}
        async Task<JsonElement> Detail()=> (await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data");
        async Task<Dictionary<string,object?>> Raw()=>Assert.Single(await p.Store.ReadAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));
        var options=await p.Post("cascadeTreeOptions"+p.PrimaryControl,new{levelKey="country",selectedPath=new{}});Assert.Contains(options.EnumerateArray(),x=>x.GetProperty("id").GetString()==Id(Node(0)));foreach(var group in new[]{2,3})Assert.DoesNotContain(options.EnumerateArray(),x=>x.GetProperty("id").GetString()==Id(Node(0,group)));
        Assert.Equal("国0/区0/仓0",(await p.Post("cascadeTreePath"+p.WarehouseControl,new{leafId=Id(Node(3))})).GetProperty("displayText").GetString());foreach(var group in new[]{2,3})await p.Post("cascadeTreePath"+p.WarehouseControl,new{leafId=Id(Node(3,group))},HttpStatusCode.InternalServerError);
        var bytes=Encoding.UTF8.GetBytes("共享正式来源"+p.Source);var file=await p.Upload(bytes);var create=Select(0);create["name"]="共享正式记录";create["aItemId"]=Id(Item(0));create["attachments"]=new[]{file};p.RecordId=(await p.Post("add",create)).GetProperty("id").GetString()!;await Storage(0,true,true);await p.References(file,1);await p.Download(file,bytes);
        await Update(JsonSerializer.Deserialize<Dictionary<string,object?>>((await Detail()).GetRawText())!);await Storage(0,true,true);var copied=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);
        var before=await p.Snapshot();await Update(Select(0,1),HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await Update(Row(primary,Id(Node(p.Shorter?1:2,1))),HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());
        await p.Grant("801","[]");await Update(Row(primary,Id(Node(p.Shorter?1:2,1)),"aItemId",null));await Storage(1,true,false);await p.Grant("801","[\"is_all\"]");await Update(Row("aItemId",Id(Item(1))));await Storage(1,true,false);await Update(Select(0));await Storage(0,true,true);
        foreach(var page in new[]{"301","401",p.Shorter?"401":"501","801"}.Distinct(StringComparer.Ordinal))
        {
            await p.Grant(page,"[]");before=await p.Snapshot();await Update(Select(0),HttpStatusCode.Forbidden);Assert.Equal(before,await p.Snapshot());await Detail();await p.Grant(page,"[\"is_all\"]");
        }
        // 直接修改共同国家，只保留新国家，两个原控件的地区／城市／仓库都必须清空。
        await Update(Row("countryId",Id(Node(0,1)),"aItemId",null));var rootOnly=await Raw();Assert.Equal(Id(Node(0,1)),SqlRowStore.Text(rootOnly,"country_id"));Assert.All(rootOnly.Where(x=>x.Key is "area_id" or "place_id" or "warehouse_id").Select(x=>x.Value),v=>Assert.True(v is null or ""));Assert.Equal("国1",(await Detail()).GetProperty("countryName").GetString());await Update(Select(0));await Storage(0,true,true);
        await p.Grant(p.SourcePage,"[\"is_query\"]");before=await p.Snapshot();await Update(Select(1),HttpStatusCode.Forbidden);Assert.Equal(before,await p.Snapshot());await p.Grant(p.SourcePage,"[\"is_all\"]");
        // 外项目来源直接种子只用于隔离验收，不能进入本账号分页COUNT。
        var foreign=(await Raw()).ToDictionary(x=>x.Key,x=>x.Value);foreign["id"]=First+100;foreign["yz_project_id"]=Project+1;foreign["attachments"]=null;await p.Store.InsertAsync(p.Source,foreign);var pageRows=await p.Post("page",new PageRequest{PageSize=1,Filters=new(){{p.Shorter?"areaId":"placeId",JsonSerializer.SerializeToElement(new{countryId=Id(Node(0))})}}});Assert.Equal("2",pageRows.GetProperty("total").GetString());Assert.Single(pageRows.GetProperty("records").EnumerateArray());
        // 源原始位出现祖先／后代并存时复制必须拒绝，不能静默修复后成功。
        if(!p.Cached){await p.Store.UpdateAsync(p.Source,long.Parse(p.RecordId,CultureInfo.InvariantCulture),Row("country_id",Key(p,Node(0))));before=await p.Snapshot();await p.Post("copy/"+p.RecordId,new{},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.Store.UpdateAsync(p.Source,long.Parse(p.RecordId,CultureInfo.InvariantCulture),Row("country_id",null));}
        await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copied});await p.References(file,1);
        // 来源只有编辑权时仍可明确清空旧关系，候选和保护下载继续按原查询权拒绝。
        await p.Grant(p.SourcePage,"[\"is_update\"]");await p.Post("cascadeTreeOptions"+p.PrimaryControl,new{levelKey="country",selectedPath=new{}},HttpStatusCode.Forbidden);await p.Download(file,bytes,HttpStatusCode.Unauthorized);await Update(new[]{"countryId","areaId",primary,"warehouseId"}.Distinct().ToDictionary(x=>x,_=>(object?)null));var cleared=await Raw();Assert.All(cleared.Where(x=>x.Key is "country_id" or "area_id" or "place_id" or "warehouse_id").Select(x=>x.Value),Assert.Null);await p.Grant(p.SourcePage,"[\"is_all\"]");await Update(Select(0));
        // 文件引用的真实末尾DML失败，来源关系／原引用都必须回滚；解除精确约束后原请求成功。
        var secondBytes=Encoding.UTF8.GetBytes("共享晚期文件"+p.Source);var second=await p.Upload(secondBytes);var constraint="mt_shared_ref_"+Guid.NewGuid().ToString("N")[..10];var installed=false;var late=Row(primary,Id(Node(p.Shorter?1:2,1)),"aItemId",null,"attachments",new[]{second});
        try
        {
            await Sql(p,"ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+long.Parse(second,CultureInfo.InvariantCulture)+")");installed=true;before=await p.Snapshot();await Update(late,HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.References(file,1);await p.References(second,0);await Sql(p,"ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");installed=false;await Update(late);await Storage(1,true,false);await p.References(file,0);await p.References(second,1);await p.Download(second,secondBytes);await Update(Select(0));await Storage(0,true,true);
        }
        finally{if(installed)await Sql(p,"ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");}
        /// <summary>逻辑回显与原始稀疏／缓存列分别验证，不以VO代替物理存储。</summary>
        async Task Storage(int group,bool a,bool b)
        {
            var read=await Detail();Assert.Equal("国"+group,read.GetProperty("countryName").GetString());Assert.Equal("区"+group,read.GetProperty("areaName").GetString());var raw=await Raw();Assert.Equal(Id(Project),SqlRowStore.Text(raw,"yz_project_id"));var expected=new Dictionary<string,long?>{{"country",p.Cached?Node(0,group):null},{"area",p.Cached||p.Shorter&&a&&!b?Node(1,group):null},{"warehouse",b?Node(3,group):null}};if(!p.Shorter)expected["place"]=a?Node(2,group):null;
            foreach(var pair in expected){Assert.Equal(pair.Value is{} id?Id(id):null,raw[pair.Key+"_id"] is{} value?Convert.ToString(value,CultureInfo.InvariantCulture):null);if(p.Cached)Assert.Equal(pair.Value is null?null:(pair.Key switch{"country"=>"国","area"=>"区","place"=>"城",_=>"仓"})+group,raw[pair.Key+"_name"]);else Assert.False(raw.ContainsKey(pair.Key+"_name"));}
        }
    }
    /// <summary>两个来源共享目标改名：R0实时读取，R1跨批缓存、原维护及晚期约束回滚。</summary>
    private static async Task VerifyPropagation(Probe p)
    {
        p.Cached=true;p.RecordId=p.RecordIds[true];var current=Assert.Single(await p.Store.ReadAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));for(var i=0;i<130;i++){var row=current.ToDictionary(x=>x.Key,x=>x.Value);row["id"]=First+1000+i;row["attachments"]=null;await p.Store.InsertAsync(p.Source,row);}
        foreach(var page in new[]{"201","1201"})await p.Grant(page,"[\"is_query\"]");var target=Prefix+(p.Derived?"_company":"_country");var targetPage=p.Derived?"601":"301";var targetId=p.Derived?Company(0):Node(0);
        async Task Rename(string text,HttpStatusCode status=HttpStatusCode.OK)=>await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+target+"/"+targetPage+"/update",new{id=Id(targetId),name=text},status);
        await Rename("共享正式新名");await Cached("共享正式新名");p.Cached=false;p.RecordId=p.RecordIds[false];Assert.Equal("共享正式新名",(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data").GetProperty("countryName").GetString());Assert.Null(Assert.Single(await p.Store.ReadAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))))["country_id"]);p.Cached=true;p.RecordId=p.RecordIds[true];
        // 最后一批消费者才拒绝的精确约束，必须回滚目标、全部已更新缓存及文件引用。
        var constraint="mt_shared_cache_"+Guid.NewGuid().ToString("N")[..10];var installed=false;
        try{await Sql(p,"ALTER TABLE `"+p.Source+"` ADD CONSTRAINT `"+constraint+"` CHECK (`id` <> "+(First+1129).ToString(CultureInfo.InvariantCulture)+" OR `country_name` <> 'late-shared-name')");installed=true;var before=await p.Snapshot();await Rename("late-shared-name",HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await Cached("共享正式新名");}
        finally{if(installed)await Sql(p,"ALTER TABLE `"+p.Source+"` DROP CHECK `"+constraint+"`");}
        foreach(var row in await p.Store.ReadAsync(p.Source,Row("yz_project_id",Project)))await p.Store.UpdateAsync(p.Source,SqlRowStore.Id(row),Row("country_name","待回填"));var maintained=await p.Send(p.Owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="12"});Assert.True(maintained.GetProperty("data").GetProperty("affectedRows").GetInt64()>128);await Cached("共享正式新名");Assert.Equal(0,(await p.Send(p.Owner,HttpMethod.Post,"/api/admin/sys/system_maintenance/backfill_join_columns",new{configTableId="12"})).GetProperty("data").GetProperty("affectedRows").GetInt64());foreach(var page in new[]{"201","1201"})await p.Grant(page,"[\"is_all\"]");
        /// <summary>逐行核对实际共享缓存与共同祖先，异项目消费者仍为旧名称。</summary>
        async Task Cached(string name){var rows=await p.Store.ReadAsync(Prefix+"_cached",Row("yz_project_id",Project));Assert.Equal(131,rows.Count);foreach(var row in rows){Assert.Equal(name,row["country_name"]);Assert.Equal(Id(Node(0)),SqlRowStore.Text(row,"country_id"));Assert.Equal(Id(Node(1)),SqlRowStore.Text(row,"area_id"));Assert.Equal(Id(Node(3)),SqlRowStore.Text(row,"warehouse_id"));}Assert.Equal("国0",Assert.Single(await p.Store.ReadIncludingDeletedAsync(Prefix+"_cached",Row("id",First+100)))["country_name"]);}
    }
    /// <summary>只在本轮独占库执行精确故障约束，凭据不出现在SQL文本。</summary>
    private static async Task Sql(Probe p,string sql){await using var c=p.Session.Provider.CreateConnection(p.Session.Options);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    /// <summary>选中键和父键分别保留各自元数据类型。</summary>
    private static object Key(Probe p,long id)=>SharedCrossFreshCatalogTests.Definition(p.Profile).Type=="string"?Id(id):id;
}
