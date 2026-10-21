using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>在本轮正式完整包专属空库验证原V8草稿、首次绑定、历史解绑与当前超级身份，不使用固定授权器。</summary>
internal static class NonRedundantFileStateMySqlProbe
{
    /// <summary>临时调整本轮唯一角色授权并逐项恢复；所有样本都由外层实例绑定租约清理。</summary>
    internal static async Task<long> VerifyAsync(HttpClient owner,HttpClient super,DatabaseSession session,MetadataCatalog catalog,MetadataCatalog files,SqlRowStore security,long permission,long readableFile)
    {
        var original=Assert.Single(await security.ReadAsync("yz_role_permission",Values("id",permission)));
        var root=catalog.ConfigTables.Single(table=>table.Id=="1").TableName;var route="/api/admin/auto/"+root+"/101";
        var library=new SqlRowStore(session,files);var business=new SqlRowStore(session,catalog);var bytes=Encoding.UTF8.GetBytes("原V8文件状态与首次绑定");
        try
        {
            await Grant("[\"is_query\",\"is_new\",\"is_update\"]");
            var plan=await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/file-library/upload-plan",new{originalName="owner-state.txt",sizeBytes=bytes.Length,configColumnId="112"});
            var file=plan.GetProperty("data").GetProperty("fileId").ToString();var fileId=long.Parse(file,CultureInfo.InvariantCulture);
            using(var form=new MultipartFormDataContent())
            {
                form.Add(new ByteArrayContent(bytes),"file","owner-state.txt");using var response=await owner.PostAsync("/api/admin/sys/file-library/local-upload/"+file,form);
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);var result=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(200,result.GetProperty("code").GetInt32());
            }
            // 私有已上传草稿可由上传者读取；完成后没有业务引用就不能继续凭所有权访问。
            await Resolve(owner,file,HttpStatusCode.OK);await Content(owner,file,HttpStatusCode.OK);
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/file-library/complete",new{fileId=file});
            await Resolve(owner,file,HttpStatusCode.Unauthorized);await Content(owner,file,HttpStatusCode.Unauthorized);
            await Content(super,file,HttpStatusCode.OK);
            // 实际后台super下一请求撤销后不保留文件特权，恢复同一角色后重新生效。
            var bootstrap=Assert.Single(await security.ReadAsync("yz_manager",Values("name","nonred-owner")));
            var roleId=long.Parse(Assert.Single(JsonSerializer.Deserialize<string[]>((string)bootstrap["yz_role_ids"]!)!),CultureInfo.InvariantCulture);
            var role=Assert.Single(await security.ReadAsync("yz_role",Values("id",roleId)));
            try{await security.UpdateAsync("yz_role",roleId,Values("is_super",0));await Content(super,file,HttpStatusCode.Unauthorized);}
            finally{await security.UpdateAsync("yz_role",roleId,Values("is_super",role["is_super"]));}
            await Content(super,file,HttpStatusCode.OK);
            // 首次保存例外仅针对上传者且从未有过引用，普通新增页权足够，不要求先有查询权。
            await Grant("[\"is_new\",\"is_update\"]");
            var added=await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/add?parentId=71",new{name="普通上传者文件状态",customerId="101",attachments=new[]{file}});
            var record=added.GetProperty("data").GetProperty("id").GetString()!;var recordId=long.Parse(record,CultureInfo.InvariantCulture);
            Assert.Single(await library.ReadAsync("yz_file_ref",Values("file_id",fileId)));
            await Content(owner,file,HttpStatusCode.Unauthorized);
            var before=JsonSerializer.Serialize(Assert.Single(await business.ReadAsync(root,Values("id",recordId))));
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/update?parentId=71",new{id=record,status=2,attachments=new[]{file}},HttpStatusCode.Unauthorized);
            Assert.Equal(before,JsonSerializer.Serialize(Assert.Single(await business.ReadAsync(root,Values("id",recordId)))));
            await Grant("[\"is_query\",\"is_new\",\"is_update\"]");await Resolve(owner,file,HttpStatusCode.OK);await Content(owner,file,HttpStatusCode.OK);
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/update?parentId=71",new{id=record,attachments=Array.Empty<string>()});
            Assert.Empty(await library.ReadAsync("yz_file_ref",Values("file_id",fileId)));Assert.NotEmpty(await library.ReadIncludingDeletedAsync("yz_file_ref",Values("file_id",fileId)));
            await Content(owner,file,HttpStatusCode.Unauthorized);
            // 原有墓碑历史阻止再次使用首次绑定例外；失败必须同时撤销物理字段写入。
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/update?parentId=71",new{id=record,attachments=new[]{file}},HttpStatusCode.Unauthorized);
            var cleared=Assert.Single(await business.ReadAsync(root,Values("id",recordId)));Assert.Empty(JsonSerializer.Deserialize<string[]>((string)cleared["attachments"]!)!);
            Assert.Empty(await library.ReadAsync("yz_file_ref",Values("file_id",fileId)));
            // 他人上传的就绪文件如果确有当前可读业务引用，原V8允许选择绑定，不能仅按所有权一概拒绝。
            var owned=Assert.Single(await library.ReadAsync("yz_file_library",Values("id",fileId)));var readable=Assert.Single(await library.ReadAsync("yz_file_library",Values("id",readableFile)));Assert.NotEqual(owned["owner_id"],readable["owner_id"]);
            var attached=await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/add?parentId=71",new{name="按真实引用绑定他人文件",customerId="101",attachments=new[]{readableFile.ToString(CultureInfo.InvariantCulture)}});
            var attachedId=long.Parse(attached.GetProperty("data").GetProperty("id").GetString()!,CultureInfo.InvariantCulture);
            Assert.Single(await library.ReadAsync("yz_file_ref",Values("file_id",readableFile,"record_id",attachedId)));
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/update?parentId=71",new{id=attachedId.ToString(CultureInfo.InvariantCulture),attachments=Array.Empty<string>()});
            Assert.Single(await library.ReadAsync("yz_file_ref",Values("file_id",readableFile)));
            // 正式普通身份同一导入事务两行复用新文件，第二行必须读取第一行尚未提交的真实引用和记录。
            await Grant("[\"is_all\"]");
            var batchPlan=await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/file-library/upload-plan",new{originalName="batch-state.txt",sizeBytes=bytes.Length,configColumnId="112"});
            var batchFile=batchPlan.GetProperty("data").GetProperty("fileId").ToString();
            using(var form=new MultipartFormDataContent())
            {
                form.Add(new ByteArrayContent(bytes),"file","batch-state.txt");using var response=await owner.PostAsync("/api/admin/sys/file-library/local-upload/"+batchFile,form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                var result=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(200,result.GetProperty("code").GetInt32());
            }
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,"/api/admin/sys/file-library/complete",new{fileId=batchFile});
            await FreshWebHttp.SendAsync(owner,HttpMethod.Post,route+"/importDatas101",new{parentId="71",requestJson=new[]{new Dictionary<string,object?>{{"记录名","同批附件甲"},{"附件",new[]{batchFile}}},new Dictionary<string,object?>{{"记录名","同批附件乙"},{"附件",new[]{batchFile}}}}});
            var batchRefs=await library.ReadAsync("yz_file_ref",Values("file_id",long.Parse(batchFile,CultureInfo.InvariantCulture)));Assert.Equal(2,batchRefs.Count);Assert.Equal(2,batchRefs.Select(row=>SqlRowStore.Id(row,"record_id")).Distinct().Count());
            return fileId;
        }
        finally{await security.UpdateAsync("yz_role_permission",permission,Values("permission_values",original["permission_values"]));}
        /// <summary>变更仅针对本轮新空库内唯一页授权，正式认证下一请求重新读取。</summary>
        Task<int> Grant(string value)=>security.UpdateAsync("yz_role_permission",permission,Values("permission_values",value));
        /// <summary>直接核对真实流HTTP状态及内容，拒绝时不输出响应原文。</summary>
        async Task Content(HttpClient client,string file,HttpStatusCode expected)
        {
            using var response=await client.GetAsync("/api/admin/sys/file-library/protected-content/"+file);Assert.Equal(expected,response.StatusCode);
            if(expected==HttpStatusCode.OK)Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());
        }
    }
    /// <summary>解析与实际流必须共享同一状态规则，页号或记录号不能替代真实引用。</summary>
    private static Task<JsonElement> Resolve(HttpClient client,string file,HttpStatusCode expected)=>FreshWebHttp.SendAsync(client,HttpMethod.Post,"/api/admin/sys/file-library/resolve",new{fileIds=new[]{file},pageId="101",recordId="9007199254741001"},expected);
    /// <summary>只构造固定基础设施字段和值，表名从已发布目录读取。</summary>
    private static Dictionary<string,object?> Values(params object?[] values){var result=new Dictionary<string,object?>();for(var index=0;index<values.Length;index+=2)result.Add((string)values[index]!,values[index+1]);return result;}
}
