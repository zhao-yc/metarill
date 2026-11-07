using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>同一正式生成HTTP/MySQL夹具的单选扩展，固定授权器不替代原登录。</summary>
internal static class NonRedundantSingleSelectionMySqlProbe
{
    /// <summary>验证名称谓词先于分页/count/统计/导出，原候选、最终外键及目标动作范围均有效。</summary>
    internal static async Task VerifyAsync(HttpClient client,string endpoint,string targetTable,string relationEndpoint,NonRedGeneratedAuthorization authorization,Func<Task<string>> rowsHash)
    {
        var name=new Dictionary<string,JsonElement>{{"customerName",JsonSerializer.SerializeToElement("B")}};
        var page=await Post("/page",new PageRequest{ParentId="71",PageNo=1,PageSize=1,Filters=name});Assert.Equal("9007199254741002",Assert.Single(page.GetProperty("data").GetProperty("records").EnumerateArray()).GetProperty("id").GetString());Assert.Equal("1",page.GetProperty("data").GetProperty("total").ToString());Assert.Equal(20m,page.GetProperty("map").GetProperty("statistics").GetProperty("customerAmount").GetProperty("total").GetDecimal());
        var beyond=await Post("/page",new PageRequest{ParentId="71",PageNo=99,PageSize=1,Filters=name});Assert.Empty(beyond.GetProperty("data").GetProperty("records").EnumerateArray());Assert.Equal("1",beyond.GetProperty("data").GetProperty("total").ToString());Assert.Equal(20m,beyond.GetProperty("map").GetProperty("statistics").GetProperty("customerAmount").GetProperty("total").GetDecimal());
        Assert.Equal("1",(await Post("/count",new PageRequest{ParentId="71",Filters=name})).GetProperty("data").ToString());
        foreach(var literal in new[]{"%","_","A%","A!","' OR 1=1 --","外项目"})
        {var result=await Post("/count",new PageRequest{ParentId="71",Filters=new(){{"customerName",JsonSerializer.SerializeToElement(literal)}}});Assert.Equal(200,result.GetProperty("code").GetInt32());Assert.Equal("0",result.GetProperty("data").ToString());}
        foreach(var invalid in new[]{JsonSerializer.SerializeToElement(1),JsonSerializer.SerializeToElement(true),JsonSerializer.SerializeToElement(new[]{"B"})})Assert.Equal(500,(await Post("/count",new PageRequest{ParentId="71",Filters=new(){{"customerName",invalid}}})).GetProperty("code").GetInt32());
        var andFilters=new Dictionary<string,JsonElement>{{"customerName",JsonSerializer.SerializeToElement("A")},{"customerId",JsonSerializer.SerializeToElement("102")}};Assert.Equal("0",(await Post("/count",new PageRequest{ParentId="71",Filters=andFilters})).GetProperty("data").ToString());
        var candidate=await Post("/"+relationEndpoint+"/page",new{parentId="71",pageNo=1,pageSize=20});Assert.Equal(new[]{"101","102"},candidate.GetProperty("data").GetProperty("records").EnumerateArray().Select(row=>row.GetProperty("id").ToString()).Order());
        Assert.Equal(500,(await Post("/"+relationEndpoint+"/selected",new{parentId="71",selectedIds=new[]{"101","105"}})).GetProperty("code").GetInt32());
        var before=await rowsHash();Assert.Equal(500,(await Post("/add?parentId=71",new{name="拒绝其它父候选",customerId="105"})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("/update?parentId=71",new{id="9007199254741001",customerId="105"})).GetProperty("code").GetInt32());Assert.Equal(before,await rowsHash());
        var target="/api/admin/auto/"+targetTable+"/201";
        using(var response=await client.PostAsJsonAsync(target+"/page",new PageRequest{ParentId="72"})){var result=await Json(response);Assert.Equal("105",Assert.Single(result.GetProperty("data").GetProperty("records").EnumerateArray()).GetProperty("id").GetString());}
        authorization.Denied="list";authorization.DeniedPage="201";
        try
        {
            Assert.Equal(200,(await Post("/page",new PageRequest{ParentId="71",PageSize=1})).GetProperty("code").GetInt32());
            foreach(var path in new[]{endpoint+"/"+relationEndpoint+"/page",target+"/page",endpoint+"/initExport"})
            {using var denied=await client.PostAsJsonAsync(path,new{parentId="71",pageNo=1,pageSize=20});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        }
        finally{authorization.Denied=null;authorization.DeniedPage=null;}
        var grant=(await Post("/initExport",new PageRequest{ParentId="71",PageNo=99,PageSize=1,Filters=name})).GetProperty("data").GetString();
        using(var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant))
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));var sheet=book.Worksheet(1);Assert.Equal(2,sheet.RowsUsed().Count());Assert.Contains(sheet.Row(2).CellsUsed(),cell=>cell.GetString()=="B乙");Assert.Contains(sheet.Row(2).CellsUsed(),cell=>cell.GetString()=="B注释");}
        /// <summary>只消费实际HTTP响应；拒权单独验证HTTP状态，不解析日志。</summary>
        async Task<JsonElement> Post(string path,object data){using var response=await client.PostAsJsonAsync(endpoint+path,data);Assert.True(response.IsSuccessStatusCode,"单选专项HTTP未返回业务状态。");return await Json(response);}
    }
    /// <summary>目标父取实体物理外键；改父时必须合并复核旧选择，空/外项目/软删父不能退全量。</summary>
    internal static async Task VerifyMappedParentAsync(HttpClient client,string source,List<long> ownIds,Func<Task<string>> rowsHash)
    {
        var endpoint=source[..^3]+"104";var before=await rowsHash();
        foreach(var parent in new string?[]{null,"73","74"})Assert.Equal(500,(await Post("/add",new{name="无效目标父",groupId=parent,customerId="101"})).GetProperty("code").GetInt32());
        Assert.Equal(before,await rowsHash());var created=await Post("/add",new{name="独立目标父映射",groupId="71",customerId="101"});Assert.Equal(200,created.GetProperty("code").GetInt32());
        var id=created.GetProperty("data").GetProperty("id").GetString()!;ownIds.Add(long.Parse(id,CultureInfo.InvariantCulture));Assert.Equal("A甲",created.GetProperty("data").GetProperty("customerName").GetString());
        before=await rowsHash();Assert.Equal(500,(await Post("/update",new{id,groupId="72"})).GetProperty("code").GetInt32());Assert.Equal(500,(await Post("/update",new{id,groupId=(string?)null})).GetProperty("code").GetInt32());Assert.Equal(before,await rowsHash());
        var moved=await Post("/update",new{id,groupId="72",customerId="105"});Assert.Equal(200,moved.GetProperty("code").GetInt32());Assert.Equal("其它父候选",moved.GetProperty("data").GetProperty("customerName").GetString());
        var grant=(await Post("/initExport",new PageRequest{Filters=new(){{"customerName",JsonSerializer.SerializeToElement("其它父候选")}}})).GetProperty("data").GetString();
        using(var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant))
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));Assert.Equal(2,book.Worksheet(1).RowsUsed().Count());Assert.Contains(book.Worksheet(1).Row(2).CellsUsed(),cell=>cell.GetString()=="其它父候选");}
        var cleared=await Post("/update",new{id,groupId=(string?)null,customerId=(string?)null});Assert.Equal(200,cleared.GetProperty("code").GetInt32());Assert.Equal(JsonValueKind.Null,cleared.GetProperty("data").GetProperty("customerName").ValueKind);
        /// <summary>依照生成的页级路由提交，业务失败不泄漏响应正文。</summary>
        async Task<JsonElement> Post(string path,object data){using var response=await client.PostAsJsonAsync(endpoint+path,data);Assert.True(response.IsSuccessStatusCode,"目标父专项HTTP未返回业务状态。");return await Json(response);}
    }
    /// <summary>安全读取原R协议，不回显SQL、连接串或响应正文。</summary>
    private static async Task<JsonElement> Json(HttpResponseMessage response){using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return document.RootElement.Clone();}
}
