using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>正式完整包中的非冗余物理文件：真实上传/引用/复制删除/导入回滚，不替换文件业务或授权器。</summary>
internal static class NonRedundantFilesMySqlProbe
{
    /// <summary>返回仍有有效记录引用的保护文件，供真实只读账号及下一请求撤权检查。</summary>
    internal static async Task<long> VerifyAsync(HttpClient client,DatabaseSession session,MetadataCatalog catalog,MetadataCatalog files)
    {
        var root=catalog.ConfigTables.Single(table=>table.Id=="1").TableName;var source="/api/admin/auto/"+root+"/101";var store=new SqlRowStore(session,catalog);var library=new SqlRowStore(session,files);var bytes=Encoding.UTF8.GetBytes("Metarill nonredundant files reference transaction");var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var plan=await FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/sys/file-library/upload-plan",new{originalName="nonredundant.txt",mimeType="text/html",sizeBytes=bytes.Length,configColumnId="112",sha256=hash});var file=plan.GetProperty("data").GetProperty("fileId").ToString();
        using(var form=new MultipartFormDataContent())
        {form.Add(new ByteArrayContent(bytes),"file","nonredundant.txt");using var response=await client.PostAsync("/api/admin/sys/file-library/local-upload/"+file,form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var body=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(200,body.GetProperty("code").GetInt32());}
        var complete=await FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/sys/file-library/complete",new{fileId=file});Assert.Equal("ready",complete.GetProperty("data").GetProperty("status").GetString());Assert.Equal(hash,complete.GetProperty("data").GetProperty("sha256").GetString());Assert.Equal("text/plain",complete.GetProperty("data").GetProperty("mimeType").GetString());
        var created=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/add?parentId=71",new{name="非冗余文件记录",customerId="101",attachments=new[]{file}});var id=created.GetProperty("data").GetProperty("id").GetString()!;AssertArray(created.GetProperty("data"),file);await References(1,id);
        var raw=Assert.Single(await store.ReadAsync(root,new Dictionary<string,object?>{{"id",long.Parse(id,CultureInfo.InvariantCulture)}}));Assert.Equal(new[]{file},JsonSerializer.Deserialize<string[]>((string)raw["attachments"]!)!);Assert.False(raw.ContainsKey("customer_notes"));
        var preserved=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/update?parentId=71",new{id,status=2});AssertArray(preserved.GetProperty("data"),file);await References(1,id);
        var copied=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/copy/"+id+"?parentId=71",new{});var copyId=copied.GetProperty("data").GetProperty("id").GetString()!;AssertArray(copied.GetProperty("data"),file);await References(2,id,copyId);
        await FreshWebHttp.SendAsync(client,HttpMethod.Delete,source+"?parentId=71",new{id=copyId});await References(1,id);
        // 第一行先撤掉附件引用，第二行坏关系后，记录与文件引用须在同一真实事务恢复。
        var before=await Snapshot();var overwrite="/api/admin/auto/"+root+"/103/importDatas103";
        var failure=await FreshWebHttp.SendAsync(client,HttpMethod.Post,overwrite,new{parentId="71",requestJson=new[]{new Dictionary<string,object?>{{"记录名","非冗余文件记录"},{"附件",Array.Empty<string>()}},new Dictionary<string,object?>{{"记录名","坏关系后行"},{"customer_id",103}}}},HttpStatusCode.BadRequest);Assert.Contains("第2行",failure.GetProperty("msg").GetString());Assert.Equal(before,await Snapshot());await References(1,id);
        // 仅导入页没有普通新增/编辑权限，仍须按原导入动作新增文件记录和跨页清理引用。
        var importOnly="/api/admin/auto/"+root+"/105";
        await FreshWebHttp.SendAsync(client,HttpMethod.Post,importOnly+"/importDatas105",new{requestJson=new[]{new Dictionary<string,object?>{{"记录名","文件仅导入新增"},{"customer_id","A甲"},{"group_id","父甲"},{"附件",new[]{file}}}}});
        var imported=Assert.Single(await store.ReadAsync(root,new Dictionary<string,object?>{{"name","文件仅导入新增"}}));var importedId=SqlRowStore.Id(imported).ToString(CultureInfo.InvariantCulture);
        var importedRefs=await library.ReadAsync("yz_file_ref",new Dictionary<string,object?>{{"record_id",SqlRowStore.Id(imported)}});Assert.Equal("105",Assert.Single(importedRefs)["page_id"]!.ToString());
        await FreshWebHttp.SendAsync(client,HttpMethod.Delete,source+"?parentId=71",new{id=importedId});await References(1,id);
        await FreshWebHttp.SendAsync(client,HttpMethod.Post,overwrite,new{parentId="71",requestJson=new[]{new Dictionary<string,object?>{{"记录名","非冗余文件记录"},{"附件",Array.Empty<string>()}}}});await References(0);
        var cleared=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/update?parentId=71",new{id,attachments=(string[]?)null});Assert.Equal(JsonValueKind.Null,cleared.GetProperty("data").GetProperty("attachments").ValueKind);await References(0);
        var empty=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/update?parentId=71",new{id,attachments=Array.Empty<string>()});Assert.Equal(0,empty.GetProperty("data").GetProperty("attachments").GetArrayLength());await References(0);
        var restored=await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/update?parentId=71",new{id,attachments=new[]{file}});AssertArray(restored.GetProperty("data"),file);await References(1,id);
        var grant=(await FreshWebHttp.SendAsync(client,HttpMethod.Post,source+"/initExport",new PageRequest{ParentId="71",Filters=new(){{"name",JsonSerializer.SerializeToElement("非冗余文件记录")}}})).GetProperty("data").GetString();
        using(var response=await client.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+grant))
        {Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var book=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));Assert.Equal(2,book.Worksheet(1).RowsUsed().Count());Assert.Contains(book.Worksheet(1).Row(2).CellsUsed(),cell=>cell.GetString().Contains(file,StringComparison.Ordinal));}
        using(var download=await client.GetAsync("/api/admin/sys/file-library/protected-content/"+file)){Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(bytes,await download.Content.ReadAsByteArrayAsync());}
        return long.Parse(file,CultureInfo.InvariantCulture);
        /// <summary>逐行验证引用的真实项目、页面和字段，不以VO中的数组推定引用已提交。</summary>
        async Task References(int count,params string[] records)
        {
            var refs=await library.ReadAsync("yz_file_ref",new Dictionary<string,object?>{{"file_id",long.Parse(file,CultureInfo.InvariantCulture)}});Assert.Equal(count,refs.Count);Assert.Equal(records.Order(),refs.Select(row=>row["record_id"]!.ToString()!).Order());Assert.All(refs,row=>{Assert.Equal("54",row["yz_project_id"]!.ToString());Assert.Equal("101",row["page_id"]!.ToString());Assert.Equal("attachments",row["field_name"]);});
        }
        /// <summary>全量业务行与本文件的含墓碑引用一起做摘要，识别半次删除或新增引用残留。</summary>
        async Task<string> Snapshot()
        {
            var value=new{business=(await store.ReadIncludingDeletedAsync(root)).OrderBy(row=>SqlRowStore.Id(row)).ToArray(),refs=(await library.ReadIncludingDeletedAsync("yz_file_ref",new Dictionary<string,object?>{{"file_id",long.Parse(file,CultureInfo.InvariantCulture)}})).OrderBy(row=>SqlRowStore.Id(row)).ToArray(),file=await library.ReadIncludingDeletedAsync("yz_file_library",new Dictionary<string,object?>{{"id",long.Parse(file,CultureInfo.InvariantCulture)}})};return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
        }
    }
    /// <summary>原接口必须返回原生JSON数组和字符串长ID，不能只核对反序列化后的等价值。</summary>
    private static void AssertArray(JsonElement value,string file){var array=value.GetProperty("attachments");Assert.Equal(JsonValueKind.Array,array.ValueKind);Assert.Equal(file,Assert.Single(array.EnumerateArray()).GetString());}
}
