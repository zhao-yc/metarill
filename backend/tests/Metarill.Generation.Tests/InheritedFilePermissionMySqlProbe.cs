using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>仅在正式完整包专属新库用真实Web身份验证选择/父子来源及文件独立范围。</summary>
internal static class InheritedFilePermissionMySqlProbe
{
    /// <summary>真实Party文件列和Group父子边进入生成元数据，不直接建表或修补生成产物。</summary>
    internal static void Configure(MetadataCatalog catalog)
    {
        catalog.ConfigPages.Single(page=>page.Id=="201").Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");
        catalog.ConfigPages.Single(page=>page.Id=="301").IsSave=0;
        catalog.ConfigColumns.Add(new(){Id="213",YzProjectId="54",ConfigTableId="2",ColumnName="linked_files",Name="关联附件",LogicalType="string",ColumnLength="max",ControlKind="FileLibrary",ControlDetail="{\"accessMode\":\"PROTECTED\",\"maxCount\":3}"});
        catalog.ConfigPageColumns.Add(new(){Id="201213",YzProjectId="54",ConfigPageId="201",ConfigColumnId="213",IsEdit=1,IsShow=1});
        // 选择页和内嵌子表是同表的两个原视图，避免把选择列表当作内嵌编辑列表。
        var child=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(catalog.ConfigPages.Single(page=>page.Id=="201")))!;child.Id="202";child.Extra["type"]=JsonSerializer.SerializeToElement(0);catalog.ConfigPages.Add(child);
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="201").Select(field=>JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(field))!).ToArray())
        {field.Id="202"+field.Id;field.ConfigPageId="202";catalog.ConfigPageColumns.Add(field);}
        catalog.ConfigChildren.Add(JsonSerializer.SerializeToElement(new{id="901",yzProjectId="54",parentConfigPageId="301",childConfigPageId="202"}));
    }
    /// <summary>所有对象均归外层已验证随机库；逐笔恢复角色、值及样本，最终仍由租约清理全部库。</summary>
    internal static async Task VerifyAsync(HttpClient ordinary,HttpClient super,DatabaseSession session,MetadataCatalog catalog,SqlRowStore security,long scopedRole,long manager,long ordinaryRole,long sourcePermission,IReadOnlyList<long> extraPermissions)
    {
        const string library="/api/admin/sys/file-library";var store=new SqlRowStore(session,catalog);var table=catalog.ConfigTables.Single(table=>table.Id=="2").TableName;var route="/api/admin/auto/"+table+"/201";var childRoute="/api/admin/auto/"+table+"/202";
        var oldA=Assert.Single(await store.ReadAsync(table,Row("id",101L)));var oldB=Assert.Single(await store.ReadAsync(table,Row("id",102L)));Assert.Null(oldA.GetValueOrDefault("linked_files"));Assert.Null(oldB.GetValueOrDefault("linked_files"));
        var oldManager=Assert.Single(await security.ReadAsync("yz_manager",Row("id",manager)));var oldRole=Assert.Single(await security.ReadAsync("yz_role",Row("id",scopedRole)));var oldSource=Assert.Single(await security.ReadAsync("yz_role_permission",Row("id",sourcePermission)));var extras=new List<Dictionary<string,object?>>();
        foreach(var key in extraPermissions)extras.Add(Assert.Single(await security.ReadAsync("yz_role_permission",Row("id",key))));
        var ids=new SnowflakeIdGenerator(939);var targetGrant=ids.NextId();var childGrant=ids.NextId();var grants=new List<long>();var uploaded=new Dictionary<long,byte[]>();var attached=false;var stage="upload";Exception? primary=null;
        try
        {
            var a=await Upload(101);var b=await Upload(102);attached=true;
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id="101",linkedFiles=new[]{Text(a)}});await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id="102",linkedFiles=new[]{Text(b)}});
            foreach(var row in extras)await security.UpdateAsync("yz_role_permission",SqlRowStore.Id(row),Row("permission_values","[]"));await Source("[\"is_query\"]");
            stage="selection-query-only";await Page();await Details();await Files(false,false);await Listing(false,false);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/add?parentId=71",new{name="不应新增"},HttpStatusCode.Forbidden);Assert.Empty(await store.ReadAsync(table,Row("name","不应新增")));
            stage="direct-file-scope";await AddGrant(targetGrant,scopedRole,"201","[\"is_query\"]");await security.UpdateAsync("yz_manager",manager,Row("real_name",oldA["name"]));await Page();await Details();await Files(true,false);await Listing(true,false);
            stage="revoke-and-refresh";await Grant(targetGrant,"[]");await Page();await Files(false,false);await Grant(targetGrant,"[\"is_query\"]");await Files(true,false);
            await security.UpdateAsync("yz_role",scopedRole,Row("is_scope",0));await Files(true,true);await security.UpdateAsync("yz_role",scopedRole,Row("is_scope",1));await Files(true,false);
            stage="child-reference-without-grant";await Bind(childRoute);await Files(false,false);
            stage="real-child-query";await AddGrant(childGrant,ordinaryRole,"301","[\"is_query\"]");await Page();await Files(true,true);await Listing(true,true);
            stage="child-wrong-action";await Grant(childGrant,"[\"is_update\"]");await Files(false,false);await Listing(false,false);
            stage="child-all-disabled-update";await Grant(childGrant,"[\"is_all\"]");await Files(true,true);
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,childRoute+"/update?parentId=71",new{id="101",notes="不能借父页全功能绕过关闭的修改"},HttpStatusCode.Forbidden);Assert.Equal(oldA["notes"],Assert.Single(await store.ReadAsync(table,Row("id",101L)))["notes"]);
            stage="child-revoked";await Grant(childGrant,"[]");await Page();await Details();await Files(false,false);
            stage="restore-selection-reference";await Bind(route);await Files(true,false);
            // 当前匹配值只在文件来源需要：普通页面仍不限制，文件随下一请求切换。
            stage="file-only-value-change";await security.UpdateAsync("yz_manager",manager,Row("real_name",oldB["name"]));await Page();await Files(false,true);await Listing(false,true);
        }
        catch(Exception error){primary=error;throw new InvalidOperationException("继承文件正式Web/实库阶段失败："+stage,error);}
        finally
        {
            try
            {
                await security.UpdateAsync("yz_manager",manager,Row("real_name",oldManager.GetValueOrDefault("real_name")));await security.UpdateAsync("yz_role",scopedRole,Row("is_scope",oldRole.GetValueOrDefault("is_scope")));await Source((string)oldSource["permission_values"]!);
                foreach(var row in extras)await Grant(SqlRowStore.Id(row),(string)row["permission_values"]!);foreach(var key in grants)await security.UpdateAsync("yz_role_permission",key,Row("delete_no",DateTime.UtcNow));
                if(attached){await Restore(101,oldA);await Restore(102,oldB);}foreach(var file in uploaded.Keys)await FreshWebHttp.SendAsync(super,HttpMethod.Delete,library+"/"+file);
            }
            catch(Exception cleanup)when(primary is not null){throw new AggregateException("继承文件实测与样本恢复同时失败，首因保留。",primary,cleanup);}
        }
        /// <summary>实际上传内容经原路由进入文件库，不伪造引用行。</summary>
        async Task<long> Upload(long record)
        {
            var bytes=Encoding.UTF8.GetBytes("继承文件"+record);var plan=await FreshWebHttp.SendAsync(super,HttpMethod.Post,library+"/upload-plan",new{originalName="inheritance-"+record+".txt",sizeBytes=bytes.Length,configColumnId="213"});var key=long.Parse(plan.GetProperty("data").GetProperty("fileId").ToString(),CultureInfo.InvariantCulture);uploaded.Add(key,bytes);
            using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes),"file","inheritance-"+record+".txt");using var response=await super.PostAsync(library+"/local-upload/"+key,form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var body=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(200,body.GetProperty("code").GetInt32());await FreshWebHttp.SendAsync(super,HttpMethod.Post,library+"/complete",new{fileId=Text(key)});return key;
        }
        /// <summary>原文件观察者以最后写入页更新真实来源引用；不能伪造同字段的并行引用。</summary>
        async Task Bind(string targetRoute)
        {
            var index=0;foreach(var file in uploaded.Keys){await FreshWebHttp.SendAsync(super,HttpMethod.Post,targetRoute+"/update?parentId=71",new{id=index++==0?"101":"102",linkedFiles=new[]{Text(file)}});}
        }
        /// <summary>列表继承查询仍在真实项目/父范围内，不以仅成功响应证明正确。</summary>
        async Task Page(){var body=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/page",new PageRequest{ParentId="71"});var data=body.GetProperty("data");Assert.Equal("2",data.GetProperty("total").ToString());Assert.Equal(new[]{"101","102"},data.GetProperty("records").EnumerateArray().Select(row=>row.GetProperty("id").GetString()!).Order(StringComparer.Ordinal));}
        /// <summary>普通详情仍可见两个目标，文件拒绝不能是因为普通详情已被限制。</summary>
        async Task Details(){foreach(var id in new[]{101,102}){var body=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Get,route+"/detail/"+id+"?parentId=71");Assert.Equal(id.ToString(CultureInfo.InvariantCulture),body.GetProperty("data").GetProperty("id").GetString());}}
        /// <summary>验证真实流内容及解析；两个文件只由不同来源记录保护。</summary>
        async Task Files(bool a,bool b)
        {
            var expected=new[]{a,b};var index=0;foreach(var pair in uploaded)
            {using var response=await ordinary.GetAsync(library+"/protected-content/"+pair.Key);Assert.Equal(expected[index]?HttpStatusCode.OK:HttpStatusCode.Unauthorized,response.StatusCode);if(expected[index])Assert.Equal(pair.Value,await response.Content.ReadAsByteArrayAsync());await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,library+"/resolve",new{fileIds=new[]{Text(pair.Key)}},expected:expected[index]?HttpStatusCode.OK:HttpStatusCode.Unauthorized);index++;}
        }
        /// <summary>原文件列表按实际文件读权限显示，不能借选择页普通查询取消文件范围。</summary>
        async Task Listing(bool a,bool b){var data=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,library+"/list",new{configColumnId="213",folderId=""})).GetProperty("data");var keys=data.EnumerateArray().Select(row=>row.GetProperty("id").GetString()).ToArray();var expected=new[]{a,b};var index=0;foreach(var file in uploaded.Keys)Assert.Equal(expected[index++],keys.Contains(Text(file)));}
        /// <summary>只新增本次两个明确来源授权，目标归属在正式认证中重读。</summary>
        async Task AddGrant(long id,long owner,string page,string values){await security.InsertAsync("yz_role_permission",Row("id",id,"yz_project_id",54L,"yz_role_id",owner,"config_page_id",long.Parse(page,CultureInfo.InvariantCulture),"permission_values",values));grants.Add(id);}
        /// <summary>授权更新仅作用本轮已创建行。</summary>
        Task<int> Grant(long id,string values)=>security.UpdateAsync("yz_role_permission",id,Row("permission_values",values));
        /// <summary>普通角色来源页只授查询，不额外授目标页或文件权限。</summary>
        Task<int> Source(string values)=>Grant(sourcePermission,values);
        /// <summary>经原Service恢复字段，释放真实文件引用后才能删除文件。</summary>
        Task<JsonElement> Restore(long id,Dictionary<string,object?> row)=>FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(id),linkedFiles=(object?)null,notes=row.GetValueOrDefault("notes")});
    }
    /// <summary>固定样本列只构造值字典，不拼接SQL。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)result.Add((string)values[i]!,values[i+1]);return result;}
    /// <summary>长ID继续按原接口字符串传递。</summary>
    private static string Text(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
