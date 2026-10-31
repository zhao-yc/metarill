using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Exports;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Exports;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>本轮独占空库内，以正式完整包Web认证验证实际范围；不向原平台发布样本配置。</summary>
internal static class ExactDataManageMySqlProbe
{
    /// <summary>范围声明先进入生成元数据，原模板不变；只给本样本来源页启用已存在的名称过滤和覆盖模式。</summary>
    internal static void Configure(MetadataCatalog catalog)
    {
        var page=catalog.ConfigPages.Single(page=>page.Id=="101");page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("name");page.Extra["importMethod"]=JsonSerializer.SerializeToElement("OverWriteByFirstColumn");
        catalog.YzAdminTypes.Single(admin=>admin.Id==page.YzAdminTypeId).Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("realName");
        var name=catalog.ConfigColumns.Single(column=>column.ConfigTableId=="1"&&column.ColumnName=="name");
        foreach(var field in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101"&&field.ConfigColumnId==name.Id))field.Extra["isSearch"]=JsonSerializer.SerializeToElement(1);
        InheritedFilePermissionMySqlProbe.Configure(catalog);
    }
    /// <summary>所有安全行及记录都属于外层已验证租约；finally恢复既有流程所需状态，原失败由外层保留。</summary>
    internal static async Task VerifyAsync(HttpClient ordinary,HttpClient super,DatabaseSession session,MetadataCatalog catalog,MetadataCatalog files,SqlRowStore security,long role,long manager,long permission,long readableFile)
    {
        var table=catalog.ConfigTables.Single(table=>table.Id=="1").TableName;var route="/api/admin/auto/"+table+"/101";var business=new SqlRowStore(session,catalog);var library=new SqlRowStore(session,files);
        var reference=Assert.Single(await library.ReadAsync("yz_file_ref",Row("file_id",readableFile)));var a=SqlRowStore.Id(reference,"record_id");const long b=9007199254741002;
        var oldA=Assert.Single(await business.ReadAsync(table,Row("id",a)));var oldB=Assert.Single(await business.ReadAsync(table,Row("id",b)));
        var oldRole=Assert.Single(await security.ReadAsync("yz_role",Row("id",role)));var oldManager=Assert.Single(await security.ReadAsync("yz_manager",Row("id",manager)));var oldPermission=Assert.Single(await security.ReadAsync("yz_role_permission",Row("id",permission)));
        var prefix="mt_"+Guid.NewGuid().ToString("N")[..8];var nameA=prefix+"A";var nameB=prefix+"B";var ids=new SnowflakeIdGenerator(938);var normalRole=ids.NextId();var normalPermission=ids.NextId();var extraPermissions=new List<long>();var createdRole=false;var createdPermission=false;var stage="prepare";
        try
        {
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(a),name=nameA});await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(b),name=nameB});
            // 新应用只靠已发布元数据，不手工补写后台匹配字段。
            await security.UpdateAsync("yz_role",role,Row("is_scope",1));await security.UpdateAsync("yz_role_permission",permission,Row("permission_values","[\"is_all\"]"));
            // 普通角色只授其它页不解除来源精确限制；目标页自己的新范围不干扰原导出显示。
            await security.InsertAsync("yz_role",Row("id",normalRole,"yz_project_id",54L,"yz_admin_type_id",5L,"name",prefix,"is_super",0,"is_scope",0,"is_built_in",0));createdRole=true;
            await security.InsertAsync("yz_role_permission",Row("id",normalPermission,"yz_role_id",normalRole,"yz_project_id",54L,"config_page_id",101L,"permission_values","[]"));createdPermission=true;
            await security.UpdateAsync("yz_manager",manager,Row("yz_role_ids",JsonSerializer.Serialize(new[]{Text(role),Text(normalRole)})));
            // 导出关系名称仍经真实目标页权限，不能因专项范围测试绕过原依赖。
            foreach(var page in catalog.ConfigPages.Where(page=>page.ConfigTableId!="1"))
            {
                var key=ids.NextId();await security.InsertAsync("yz_role_permission",Row("id",key,"yz_role_id",normalRole,"yz_project_id",54L,"config_page_id",long.Parse(page.Id,CultureInfo.InvariantCulture),"permission_values","[\"is_query\"]"));extraPermissions.Add(key);
            }
            stage="scalar-read-file";await Match(nameA);await Page(1);await Detail(a,true);await Detail(b,false);await File(true);await FileList(true);Assert.Equal(new[]{nameA},await Export());
            stage="array-pagination";await Match(JsonSerializer.Serialize(new[]{nameA,nameB}));var first=await Page(2,1,1);var second=await Page(2,2,1);Assert.Equal(new[]{Text(a),Text(b)}.Order(StringComparer.Ordinal),first.Concat(second).Order(StringComparer.Ordinal));await File(true);
            stage="empty-values";
            foreach(var value in new string?[]{null,""," ","[]","[null]"}){await Match(value);await Page(0);await File(false);await FileList(false);}
            stage="malformed-values";
            foreach(var value in new[]{"[{}]","["}){await Match(value);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/page",Request(),HttpStatusCode.BadRequest);await File(false);}
            stage="literal-value";await Match("missing' OR 1=1 --");await Page(0);await File(false);
            stage="mixed-query";await Match(nameA);
            await Normal("[\"is_query\"]");await Page(2);Assert.Equal(new[]{nameA},await Export());await File(true);
            stage="mixed-export";await Normal("[\"is_export\"]");await Page(1);Assert.Equal(new[]{nameA,nameB}.Order(StringComparer.Ordinal),(await Export()).Order(StringComparer.Ordinal));
            stage="mixed-update";await Normal("[\"is_update\"]");await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(b),status=2});Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(table,Row("id",b))),"status"));await Detail(b,false);
            stage="scoped-update-reject";await Normal("[]");await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(b),status=1},HttpStatusCode.InternalServerError);Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(table,Row("id",b))),"status"));
            // 范围内修改必须实际成功，防止驱动错误碰巧满足范围外HTTP500断言。
            stage="scoped-update-success";await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(a),status=1});Assert.Equal(1,SqlRowStore.Id(Assert.Single(await business.ReadAsync(table,Row("id",a))),"status"));
            stage="copy-delete-reject";await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/copy/"+b+"?parentId=71",new{},HttpStatusCode.InternalServerError);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"?parentId=71",new{id=Text(b)},HttpStatusCode.InternalServerError);Assert.Single(await business.ReadAsync(table,Row("id",b)));
            stage="import-original-action";await Normal("[\"is_update\"]");
            // 覆盖只匹配import的原Scope；范围外同名按原规则追加，不能借普通update权覆盖原记录。
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/importDatas101",new{parentId="71",requestJson=new[]{Row("记录名",nameB,"customer_id","A甲","status",1)}});
            Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(table,Row("id",b))),"status"));var appended=Assert.Single(await business.ReadAsync(table,Row("name",nameB)),row=>SqlRowStore.Id(row)!=b);Assert.Equal(1,SqlRowStore.Id(appended,"status"));
            await FreshWebHttp.SendAsync(super,HttpMethod.Delete,route+"?parentId=71",new{id=Text(SqlRowStore.Id(appended))});
            stage="scoped-import-overwrite";
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/importDatas101",new{parentId="71",requestJson=new[]{Row("记录名",nameA,"status",2)}});
            Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(table,Row("id",a))),"status"));Assert.Single(await business.ReadAsync(table,Row("name",nameA)));
            stage="next-request-role-and-value";await Normal("[]");await Match(nameB);await Page(1);await File(false);await FileList(false);await Match(nameA);await File(true);
            await security.UpdateAsync("yz_role",role,Row("is_scope",0));await Page(2);await security.UpdateAsync("yz_role",role,Row("is_scope",1));await Page(1);
            stage="inherited-files";await InheritedFilePermissionMySqlProbe.VerifyAsync(ordinary,super,session,catalog,security,role,manager,normalRole,normalPermission,extraPermissions);
        }
        catch(Exception error){throw new InvalidOperationException("精确管理正式Web/实库阶段失败："+stage,error);}
        finally
        {
            await business.UpdateAsync(table,a,Row("name",oldA["name"],"status",oldA.GetValueOrDefault("status")));await business.UpdateAsync(table,b,Row("name",oldB["name"],"status",oldB.GetValueOrDefault("status")));
            await security.UpdateAsync("yz_manager",manager,Row("real_name",oldManager.GetValueOrDefault("real_name"),"yz_role_ids",oldManager["yz_role_ids"]));await security.UpdateAsync("yz_role",role,Row("is_scope",oldRole.GetValueOrDefault("is_scope")));await security.UpdateAsync("yz_role_permission",permission,Row("permission_values",oldPermission["permission_values"]));
            foreach(var key in extraPermissions)await security.UpdateAsync("yz_role_permission",key,Row("delete_no",DateTime.UtcNow));if(createdPermission)await security.UpdateAsync("yz_role_permission",normalPermission,Row("delete_no",DateTime.UtcNow));if(createdRole)await security.UpdateAsync("yz_role",normalRole,Row("delete_no",DateTime.UtcNow));
        }
        /// <summary>原请求只提供普通筛选，精确条件不接受客户端传入。</summary>
        PageRequest Request(int page=1,int size=20)=>new(){ParentId="71",PageNo=page,PageSize=size,Filters=new(){{"name",JsonSerializer.SerializeToElement(prefix)}}};
        /// <summary>分页前total、独立count及逐行名称共同核验，不以记录数量代替范围过滤。</summary>
        async Task<string[]> Page(long expected,int page=1,int size=20)
        {
            var data=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/page",Request(page,size))).GetProperty("data");Assert.Equal(expected,long.Parse(data.GetProperty("total").ToString(),CultureInfo.InvariantCulture));var rows=data.GetProperty("records").EnumerateArray().ToArray();Assert.All(rows,row=>Assert.Contains(row.GetProperty("name").GetString(),new[]{nameA,nameB}));
            var count=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/count",Request());Assert.Equal(expected,long.Parse(count.GetProperty("data").ToString(),CultureInfo.InvariantCulture));return rows.Select(row=>row.GetProperty("id").GetString()!).ToArray();
        }
        /// <summary>沿原详情HTTP200/R500的记录不可读语义，不把它误当成R200/null成功。</summary>
        async Task Detail(long id,bool visible)
        {
            using var response=await ordinary.GetAsync(route+"/detail/"+id+"?parentId=71");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var body=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(visible?200:500,body.GetProperty("code").GetInt32());
            Assert.Equal(visible,body.TryGetProperty("data",out var data)&&data.ValueKind!=JsonValueKind.Null);
        }
        /// <summary>真实文件流和解析入口共用来源记录范围。</summary>
        async Task File(bool visible){using var response=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+readableFile);Assert.Equal(visible?HttpStatusCode.OK:HttpStatusCode.Unauthorized,response.StatusCode);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/sys/file-library/resolve",new{fileIds=new[]{Text(readableFile)}},expected:visible?HttpStatusCode.OK:HttpStatusCode.Unauthorized);}
        /// <summary>原浏览入口只列当前真正可读的文件。</summary>
        async Task FileList(bool visible){var data=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/sys/file-library/list",new{configColumnId="112",folderId=""})).GetProperty("data");Assert.Equal(visible,data.EnumerateArray().Any(row=>row.GetProperty("id").GetString()==Text(readableFile)));}
        /// <summary>导出消费独立export动作，在真实下载XLSX中核验结果。</summary>
        async Task<string[]> Export(){var ticket=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/initExport",Request())).GetProperty("data").GetString();using var response=await ordinary.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+ticket);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var workbook=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));return workbook.Worksheet(1).RowsUsed().SelectMany(row=>row.CellsUsed()).Select(cell=>cell.GetString()).Where(text=>text.StartsWith(prefix,StringComparison.Ordinal)).Distinct().ToArray();}
        /// <summary>真实认证下一请求重读账号配置，不修改请求身份或注入授权替身。</summary>
        Task<int> Match(string? value)=>security.UpdateAsync("yz_manager",manager,Row("real_name",value));
        /// <summary>只变更本专项普通角色的单一来源页授权。</summary>
        Task<int> Normal(string values)=>security.UpdateAsync("yz_role_permission",normalPermission,Row("permission_values",values));
    }
    /// <summary>构造固定样本行与原接口单元格，不拼接SQL。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<values.Length;i+=2)row.Add((string)values[i]!,values[i+1]);return row;}
    /// <summary>长ID以字符串通过原接口传递。</summary>
    private static string Text(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
