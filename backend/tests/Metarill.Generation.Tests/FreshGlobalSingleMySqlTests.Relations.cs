using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Exports;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshGlobalSingleMySqlTests
{
    private const string Shared="gs_single_shared",Tenant="gs_single_tenant",Middle="gs_single_middle";
    private const string Original="甲_%!引号'记录",Second="乙记录";
    /// <summary>相邻大整数、外项目共享值、普通目标隔离及R0中间节点只写本次专属库。</summary>
    private static async Task SeedCross(Probe p)
    {
        await p.Store.InsertAsync("gs_single_parent",Row("id",Parent,"name","来源父甲","yz_project_id",Project));await p.Store.InsertAsync("gs_single_parent",Row("id",Parent+1,"name","来源父乙","yz_project_id",Project));await p.Store.InsertAsync("gs_single_parent",Row("id",Parent+2,"name","外项目父","yz_project_id",Project+1));
        foreach(var(index,name,status,segment)in new[]{(0,Original,1,1),(1,Second,1,1),(2,"停用记录",0,1),(3,"另一组记录",1,2),(4,"删除记录",1,1),(5,"重复名",1,1),(6,"重复名",1,1)})
        {
            var row=Row("id",First+index,p.NameField,name,"score",1.25m+index,"status",status,"segment",segment);if(!p.NoProject)row["yz_project_id"]=Project+1;if(index==4)row["delete_no"]=DateTime.UtcNow;await p.Store.InsertAsync(Shared,row);
        }
        await p.Store.InsertAsync(Tenant,Row("id",First+10000,"name","项目候选","yz_project_id",Project,"status",1,"segment",1));await p.Store.InsertAsync(Tenant,Row("id",First+10001,"name","外项目候选","yz_project_id",Project+1,"status",1,"segment",1));
        for(var index=0;index<3;index++)await p.Store.InsertAsync(Middle,Row("id",First+20000+index,"yz_project_id",index==2?Project+1:Project,"shared_id",First+(index==1?1:0)));
        await p.Store.InsertAsync(p.Source,Row("id",First+900000,"name","外项目来源","yz_project_id",Project+1,"parent_id",Parent,"segment",1,"shared_id",p.Key(First),"selected_id",First+1));
    }
    /// <summary>完整原候选／单选、只读名称、导入导出与权限均经实际生产Web宿主。</summary>
    private static async Task VerifyCross(Probe p)
    {
        var sharedName=p.NameField=="name"?"sharedName":"sharedCaption";var selectedName=p.NameField=="name"?"selectedName":"selectedCaption";var name=Original;
        async Task<JsonElement> Options(string action,HttpStatusCode expected=HttpStatusCode.OK)=>await p.Post("findNameIdShared2190"+action,new{parentId=p.ParentId,pageNo=1,pageSize=1,keyword=name,formParameters=new{segment=1}},expected);
        var candidates=await Options("/page");Assert.Equal("1",candidates.GetProperty("total").GetString());Assert.Equal(Id(First),Assert.Single(candidates.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());var selected=await p.Post("findNameIdShared2190/selected",new{parentId=p.ParentId,selectedIds=new[]{Id(First+1)},formParameters=new{segment=1}});Assert.Equal(Second,Assert.Single(selected.EnumerateArray()).GetProperty("name").GetString());
        var tenant=await p.Post("findNameIdTenant2192/page",new{parentId=p.ParentId,pageNo=1,pageSize=10});Assert.Equal("1",tenant.GetProperty("total").GetString());Assert.Equal(Id(First+10000),Assert.Single(tenant.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
        var single=await p.Post("findNameIdSelected2191/selected",new{parentId=p.ParentId,selectedIds=new[]{Id(First+1)}});Assert.Equal(Second,Assert.Single(single.EnumerateArray()).GetProperty("name").GetString());await p.Post("findNameIdSelected2191/selected",new{parentId=p.ParentId,selectedIds=new[]{Id(First+2)}},HttpStatusCode.InternalServerError);var popup=(await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+Shared+"/303/page",new PageRequest{SelectedIds=[Id(First+1)],PageSize=1})).GetProperty("data");Assert.Equal(Second,Assert.Single(popup.GetProperty("records").EnumerateArray()).GetProperty(p.NameField).GetString());
        var nested=await p.Post("findNameIdNested2200/page",new{parentId=p.ParentId,pageNo=1,pageSize=1,keyword=Original});Assert.Equal("1",nested.GetProperty("total").GetString());Assert.Equal(Id(First+20000),Assert.Single(nested.GetProperty("records").EnumerateArray()).GetProperty("id").GetString());
        var bytes=Encoding.UTF8.GetBytes("全局单值原文件");var file=await p.Upload(bytes);var added=await p.Post("add",new{name="正式单值来源",sharedId=Id(First),selectedId=Id(First+1),tenantId=Id(First+10000),nestedId=Id(First+20000),segment=1,attachments=new[]{file}});p.RecordId=added.GetProperty("id").GetString()!;await Check(added);await p.References(file,1);await p.Download(file,bytes);
        var raw=Assert.Single(await p.Store.ReadIncludingDeletedAsync(p.Source,Row("id",long.Parse(p.RecordId,CultureInfo.InvariantCulture))));Assert.Equal(Id(Project),SqlRowStore.Text(raw,"yz_project_id"));Assert.Equal(Id(First),SqlRowStore.Text(raw,"hidden_id"));Assert.False(added.TryGetProperty("hiddenId",out _));Assert.DoesNotContain(raw.Keys,x=>x=="shared_score"||x=="shared_"+p.NameField||x=="selected_"+p.NameField||x=="nested_label");
        var page=await p.Post("page",new PageRequest{PageSize=1});Assert.Equal("1",page.GetProperty("total").GetString());var before=await p.Snapshot();await p.Post("update",new{id=Id(First+900000),name="越界来源"},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());if(p.ParentScope){await p.Post("update?parentId="+Id(Parent+1),new{id=p.RecordId,name="越界父"},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());}
        foreach(var bad in new object[]{new{id=p.RecordId,sharedId=Id(First+2)},new{id=p.RecordId,tenantId=Id(First+10001)},new{id=p.RecordId,segment=2}}){before=await p.Snapshot();await p.Post("update",bad,HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());}
        var echo=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(added.GetRawText())!;foreach(var field in new[]{sharedName,selectedName,"nestedLabel"})echo[field]=JsonSerializer.SerializeToElement("伪造名称");echo["sharedScore"]=JsonSerializer.SerializeToElement(999m);await Check(await p.Post("update",echo));before=await p.Snapshot();echo["creating"]=JsonSerializer.SerializeToElement(true);await p.Post("update",echo,HttpStatusCode.BadRequest);Assert.Equal(before,await p.Snapshot());
        // 隐藏默认只能在当前真实目标范围中生效，准备状态不改变宿主授权或校验器。
        await p.Store.UpdateAsync(Shared,First,Row("status",0));try{before=await p.Snapshot();await p.Post("add",new{name="隐藏默认拒绝",sharedId=Id(First+1),selectedId=Id(First+1),segment=1},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());}finally{await p.Store.UpdateAsync(Shared,First,Row("status",1));}
        // 关系候选仍需目标查询，普通来源写入沿原新增／编辑动作，不新增目标列表门槛。
        await p.Grant("301","[]");await Options("/page",HttpStatusCode.Forbidden);await p.Grant("303","[]");await p.Grant(p.SourcePage,"[\"is_update\"]");await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+Shared+"/303/page",new PageRequest(),HttpStatusCode.Forbidden);await Check(await p.Post("update",new{id=p.RecordId,sharedId=Id(First),selectedId=Id(First+1)}));await p.Download(file,bytes,HttpStatusCode.Unauthorized);await p.Grant("301","[\"is_all\"]");await p.Grant("303","[\"is_all\"]");await p.Grant(p.SourcePage,"[\"is_all\"]");
        var sourceBefore=await SourceRows();name="共享新_%!名称'";await p.Send(p.Reader,HttpMethod.Post,"/api/admin/auto/"+Shared+"/301/update",Row("id",Id(First),p.NameField,name));Assert.Equal(sourceBefore,await SourceRows());await Check((await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data"));
        page=await p.Post("page",new PageRequest{PageSize=1,Filters=new(){{sharedName,JsonSerializer.SerializeToElement(name)}}});Assert.Equal("1",page.GetProperty("total").GetString());
        var imported=Import("当前共享名称导入",name,Second);await p.Post("importDatas201",new{parentId=p.ParentId,requestJson=new[]{imported}});before=await p.Snapshot();await p.Post("importDatas201",new{parentId=p.ParentId,requestJson=new[]{Import("整批首行",name,Second),Import("整批尾行","不存在",Second)}},HttpStatusCode.BadRequest);Assert.Equal(before,await p.Snapshot());await p.Post("importDatas201",new{parentId=p.ParentId,requestJson=new[]{Import("重名拒绝","重复名",Second)}},HttpStatusCode.BadRequest);Assert.Equal(before,await p.Snapshot());
        var ticket=(await p.Post("initExport",new PageRequest{SelectedIds=[p.RecordId]})).GetString()!;using(var response=await p.Reader.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(x=>x.GetString(),x=>x.Address.ColumnNumber);Assert.Equal(name,sheet.Row(2).Cell(headers["共享下拉"]).GetString());Assert.Equal(Second,sheet.Row(2).Cell(headers["共享单选"]).GetString());Assert.Equal(name,sheet.Row(2).Cell(headers["嵌套下拉"]).GetString());}
        var copied=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);var secondBytes=Encoding.UTF8.GetBytes("单值晚期引用");var second=await p.Upload(secondBytes);var constraint="mt_sg_ref_"+Guid.NewGuid().ToString("N")[..12];var installed=false;var late=new{id=p.RecordId,name="晚期来源修改",attachments=new[]{second}};
        try{await Sql("ALTER TABLE `yz_file_ref` ADD CONSTRAINT `"+constraint+"` CHECK (`file_id` <> "+long.Parse(second,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)+")");installed=true;before=await p.Snapshot();await p.Post("update",late,HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");installed=false;await p.Post("update",late);await p.References(second,1);await p.References(file,1);await p.Download(second,secondBytes);}finally{if(installed)await Sql("ALTER TABLE `yz_file_ref` DROP CHECK `"+constraint+"`");}
        await p.Post("update",new{id=p.RecordId,sharedId=(string?)null,selectedId=(string?)null,nestedId=(string?)null});await p.Post("update",new{id=p.RecordId,sharedId=Id(First),selectedId=Id(First+1),nestedId=Id(First+20000)});await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copied});await p.References(file,0);await p.References(second,1);await p.Download(second,secondBytes);
        /// <summary>原VO、嵌套名称与实际精确ID共同核对，不能用误联到相邻大整数的名称通过。</summary>
        Task Check(JsonElement row){Assert.Equal(Id(First),row.GetProperty("sharedId").GetString());Assert.Equal(Id(First+1),row.GetProperty("selectedId").GetString());Assert.Equal(name,row.GetProperty(sharedName).GetString());Assert.Equal(Second,row.GetProperty(selectedName).GetString());Assert.Equal(name,row.GetProperty("nestedLabel").GetString());Assert.Equal(1.25m,row.GetProperty("sharedScore").GetDecimal());return Task.CompletedTask;}
        /// <summary>目标改名只能改变实时读取，R0来源物理数据不作名称回填。</summary>
        async Task<string> SourceRows()=>JsonSerializer.Serialize((await p.Store.ReadIncludingDeletedAsync(p.Source)).OrderBy(x=>SqlRowStore.Id(x)));
        /// <summary>原中文表头导入，名称仍在真实候选范围中唯一解析。</summary>
        static Dictionary<string,object?> Import(string record,string shared,string selected)=>Row("记录名",record,"共享下拉",shared,"共享单选",selected,"项目下拉","项目候选","嵌套下拉",shared,"分组",1);
        /// <summary>只修改本轮专属库的精确临时约束，不授触发器或全局观测权限。</summary>
        async Task Sql(string sql){await using var connection=p.Session.Provider.CreateConnection(p.Session.Options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
    }
}
