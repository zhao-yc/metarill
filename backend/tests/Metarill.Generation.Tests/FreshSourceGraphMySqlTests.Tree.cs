using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Exports;
using Metarill.Data.Schema;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Exports;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshSourceGraphMySqlTests
{
    /// <summary>同表直接父／书签保留独立业务标识和固定公司名称，外项目及停用行只作反例。</summary>
    private static async Task SeedTree(Probe p)
    {
        foreach(var offset in new[]{0,1,2,3,10,20})
        {
            var id=First+offset;var project=offset==20?Project+1:Project;var company=offset switch{0=>"根公司",1=>"节点公司",2=>"叶公司",3=>"另根公司",_=>"不可选公司"+offset};await p.Store.InsertAsync("source_graph_tree_company",Row("id",Company(id),"name",company,"yz_project_id",project));var values=Row("id",id,"name","可业务"+offset,"company_id",Company(id),"yz_project_id",project,"status",offset==10?0:1,"amount",1m,"code","é","parent_id",p.Parent(offset is 1 or 10?First:offset==2?First+1:null));if(!p.Direct)values["bookmark_id"]=null;await p.Store.InsertAsync(p.Source,values);
        }
        await p.Store.InsertAsync("source_graph_tree_company",Row("id",Company(First+50),"name","换FK公司","yz_project_id",Project));
    }
    /// <summary>正式同表树候选、派生名称、拓扑、原Excel、文件及同页／独立目标撤权。</summary>
    private static async Task VerifyTree(Probe p)
    {
        var control=p.Direct?"5":"6";var field=p.Direct?"parentId":"bookmarkId";var label=p.Direct?"parentName":"bookmarkName";var selected=Id(First+1);var current="节点公司";
        async Task<JsonElement> Options(string? parent=null,HttpStatusCode expected=HttpStatusCode.OK)=>await p.Post("cascadeTreeOptions"+control,new{levelKey=parent is null?"selfTree-0":"selfTree-1",selectedPath=new{parentId=parent??""}},expected);
        async Task<JsonElement> Path(long id,HttpStatusCode expected=HttpStatusCode.OK)=>await p.Post("cascadeTreePath"+control,new{leafId=Id(id)},expected);
        var roots=(await Options()).EnumerateArray().ToArray();Assert.Equal(new[]{Id(First),Id(First+3)},roots.Select(x=>x.GetProperty("id").GetString()).Order());Assert.DoesNotContain(roots,x=>x.GetProperty("id").GetString()==Id(First+20));Assert.Equal(selected,Assert.Single((await Options(Id(First))).EnumerateArray()).GetProperty("id").GetString());Assert.Equal("根公司/节点公司/叶公司",(await Path(First+2)).GetProperty("displayText").GetString());foreach(var id in new[]{First+10,First+20,First+999})await Path(id,HttpStatusCode.InternalServerError);
        var bytes=Encoding.UTF8.GetBytes("同表派生树正式Web文件");var file=await p.Upload(bytes);var added=await p.Post("add",Row("name","可正式新增","companyId",Id(Company(First+3)),field,selected,"attachments",new[]{file}));p.RecordId=added.GetProperty("id").GetString()!;Assert.Equal(current,added.GetProperty(label).GetString());Assert.Equal("根公司/"+current,added.GetProperty(field+"DisplayText").GetString());await p.References(file,1);await p.Download(file,bytes);
        var before=await p.Snapshot();await p.Post("update",Row("id",p.RecordId,field,Id(First+20)),HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());await p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/"+p.Source+"/202/update",new{id=Id(First),parentId=Id(First+2)},HttpStatusCode.InternalServerError);Assert.Equal(before,await p.Snapshot());
        await p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/source_graph_tree_company/301/update",new{id=Id(Company(First+1)),name="正式中公司"});current="正式中公司";Assert.Equal(before,await p.Snapshot());await CheckSelected();await p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/"+p.Source+"/202/update",new{id=selected,companyId=Id(Company(First+50))});current="换FK公司";await CheckSelected();Assert.Equal("根公司/"+current+"/叶公司",(await Path(First+2)).GetProperty("displayText").GetString());
        var copy=(await p.Post("copy/"+p.RecordId,new{})).GetProperty("id").GetString()!;await p.References(file,2);
        // 原树路径按派生名称导入；后行错误不能留下首行或文件引用变化。
        var title=p.Direct?"父节点":"书签";await p.Post("importDatas"+p.SourcePage,new{requestJson=new[]{Row("节点名","可正式导入",title,"根公司")}});before=await p.Snapshot();await p.Post("importDatas"+p.SourcePage,new{requestJson=new[]{Row("节点名","可整批首行",title,"根公司"),Row("节点名","可整批坏行",title,"不存在")}},HttpStatusCode.BadRequest);Assert.Equal(before,await p.Snapshot());
        var ticket=(await p.Post("initExport",new Metarill.Core.Contracts.PageRequest{SelectedIds=[p.RecordId]})).GetString()!;using(var response=await p.Reader.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(ticket))){Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);var headers=sheet.Row(1).CellsUsed().ToDictionary(x=>x.GetString(),x=>x.Address.ColumnNumber);Assert.Equal("根公司/"+current,sheet.Row(2).Cell(headers[title]).GetString());}
        before=await p.Snapshot();await p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/"+p.Source+"/211/update",new{id=Id(First),status=0});await Path(First+2,HttpStatusCode.InternalServerError);Assert.Equal("",(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data").GetProperty(field+"DisplayText").GetString());await p.Send(p.Owner,HttpMethod.Post,"/api/admin/auto/"+p.Source+"/211/update",new{id=Id(First),status=1});Assert.Equal(before,await p.Snapshot());await CheckSelected();
        // 书签来源201与目标202独立；直接父来源／目标均202，不能借同页保留is_all绕过撤权。
        await p.Grant("202",p.Direct?"[\"is_update\"]":"[]");await Options(expected:HttpStatusCode.Forbidden);await p.Post("update",Row("id",p.RecordId,field,selected),HttpStatusCode.Forbidden);if(p.Direct)await p.Download(file,bytes,HttpStatusCode.Unauthorized);else{await CheckSelected();await p.Download(file,bytes);}await p.Post("update",Row("id",p.RecordId,field,null));await p.Grant("202","[\"is_all\"]");await p.Post("update",Row("id",p.RecordId,field,selected));await CheckSelected();
        await p.Grant(p.SourcePage,"[]");await Options(expected:HttpStatusCode.Forbidden);await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId,expected:HttpStatusCode.Forbidden);await p.Download(file,bytes,HttpStatusCode.Unauthorized);await p.Grant(p.SourcePage,"[\"is_all\"]");await p.Download(file,bytes);await p.Send(p.Reader,HttpMethod.Delete,p.Route,new{id=copy});await p.References(file,1);
        Assert.DoesNotContain((await new SchemaService(p.Session).InspectAsync(p.Catalog)).Tables.Single(x=>x.Name==p.Source).Columns,x=>x.Name is "display_label" or "parent_name" or "bookmark_name");
        /// <summary>名称及路径实时读取，物理FK只保存当前精确节点。</summary>
        async Task CheckSelected(){var row=(await p.Send(p.Reader,HttpMethod.Get,p.Route+"detail/"+p.RecordId)).GetProperty("data");Assert.Equal(selected,row.GetProperty(field).GetString());Assert.Equal(current,row.GetProperty(label).GetString());Assert.Equal("根公司/"+current,row.GetProperty(field+"DisplayText").GetString());}
    }
}
