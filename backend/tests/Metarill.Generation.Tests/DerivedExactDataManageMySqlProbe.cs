using System.Globalization;
using System.Net;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>在外层独占空库中以正式Web身份验派生匹配，不向原平台发布样本配置。</summary>
internal static class DerivedExactDataManageMySqlProbe
{
    /// <summary>独立视图仅用于本轮验收；隐藏匹配列、父范围和原模板仍由元数据生成。</summary>
    internal static void Configure(MetadataCatalog catalog)
    {
        var original=catalog.ConfigPages.Single(page=>page.Id=="101");var page=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(original))!;
        page.Id="106";page.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_notes");catalog.ConfigPages.Add(page);
        foreach(var source in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="101").ToArray())
        {
            var field=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(source))!;field.Id="106"+field.Id;field.ConfigPageId="106";
            if(field.ConfigColumnId=="16"){field.IsShow=0;field.IsEdit=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
            catalog.ConfigPageColumns.Add(field);
        }
        var numeric=JsonSerializer.Deserialize<PageMetadata>(JsonSerializer.Serialize(page))!;numeric.Id="107";numeric.Extra["exactDataManageMatchField"]=JsonSerializer.SerializeToElement("customer_amount");catalog.ConfigPages.Add(numeric);
        foreach(var originalField in catalog.ConfigPageColumns.Where(field=>field.ConfigPageId=="106").ToArray())
        {var copy=JsonSerializer.Deserialize<PageColumnMetadata>(JsonSerializer.Serialize(originalField))!;copy.Id="107"+copy.Id;copy.ConfigPageId="107";catalog.ConfigPageColumns.Add(copy);}
    }
    /// <summary>只改本轮已拥有的账号、角色和随机业务行；每个阶段检查实际结果，最后恢复原流程状态。</summary>
    internal static async Task VerifyAsync(HttpClient ordinary,HttpClient super,DatabaseSession session,MetadataCatalog catalog,MetadataCatalog files,SqlRowStore security,long role,long manager,long file)
    {
        var order=catalog.ConfigTables.Single(table=>table.Id=="1").TableName;var party=catalog.ConfigTables.Single(table=>table.Id=="2").TableName;
        var business=new SqlRowStore(session,catalog);var library=new SqlRowStore(session,files);var reference=Assert.Single(await library.ReadAsync("yz_file_ref",Row("file_id",file)));var a=SqlRowStore.Id(reference,"record_id");const long b=9007199254741002;
        var oldA=Assert.Single(await business.ReadAsync(order,Row("id",a)));var oldB=Assert.Single(await business.ReadAsync(order,Row("id",b)));var oldTargets=(await business.ReadAsync(party)).Where(row=>SqlRowStore.Id(row) is 101 or 102).ToDictionary(row=>SqlRowStore.Id(row));
        var oldManager=Assert.Single(await security.ReadAsync("yz_manager",Row("id",manager)));var oldRole=Assert.Single(await security.ReadAsync("yz_role",Row("id",role)));
        var ids=new SnowflakeIdGenerator(939);var normalRole=ids.NextId();var targetPermission=ids.NextId();var permission=ids.NextId();var createdRole=false;var createdTarget=false;var createdPermission=false;
        var prefix="md_"+Guid.NewGuid().ToString("N")[..8];var nameA=prefix+"甲";var nameB=prefix+"乙";var matchA=prefix+"A";var matchB=prefix+"B";var route="/api/admin/auto/"+order+"/106";var originalRoute="/api/admin/auto/"+order+"/101";var stage="prepare";
        try
        {
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(a),name=nameA,status=1,customerId="101",attachments=new[]{Text(file)}});
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(b),name=nameB,status=2,customerId="102"});
            await business.UpdateAsync(party,101,Row("notes",matchA));await business.UpdateAsync(party,102,Row("notes",matchB));
            await security.InsertAsync("yz_role",Row("id",normalRole,"yz_project_id",54L,"yz_admin_type_id",5L,"name",prefix,"is_super",0,"is_scope",0,"is_built_in",0));createdRole=true;
            await security.InsertAsync("yz_role_permission",Row("id",targetPermission,"yz_role_id",normalRole,"yz_project_id",54L,"config_page_id",201L,"permission_values","[\"is_query\"]"));createdTarget=true;
            await security.InsertAsync("yz_role_permission",Row("id",permission,"yz_role_id",role,"yz_project_id",54L,"config_page_id",106L,"permission_values","[\"is_all\"]"));createdPermission=true;
            await security.UpdateAsync("yz_role",role,Row("is_scope",1));await security.UpdateAsync("yz_manager",manager,Row("yz_role_ids",JsonSerializer.Serialize(new[]{Text(role),Text(normalRole)}),"real_name",matchA));
            var expectedBytes=await super.GetByteArrayAsync("/api/admin/sys/file-library/protected-content/"+file);
            stage="scalar-hidden-paging-files";await Page([a]);await FileAccess(true);Assert.Equal(new[]{nameA},await Export());
            stage="array-before-pagination";await Match(JsonSerializer.Serialize(new[]{matchA,matchB}));await Page([a,b]);await Match(matchA);
            stage="empty-matches";foreach(var value in new string?[]{null,"","[]"}){await Match(value);await Page([]);await FileAccess(false);}await Match(matchA);
            stage="scoped-writes";await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(a),status=2});Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(order,Row("id",a))),"status"));
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(b),status=1},HttpStatusCode.InternalServerError);Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(order,Row("id",b))),"status"));
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/copy/"+b+"?parentId=71",new{},HttpStatusCode.InternalServerError);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"?parentId=71",new{id=Text(b)},HttpStatusCode.InternalServerError);Assert.Single(await business.ReadAsync(order,Row("id",b)));
            var copied=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/copy/"+a+"?parentId=71",new{})).GetProperty("data").GetProperty("id").GetString()!;
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"?parentId=71",new{id=copied});Assert.Empty(await business.ReadAsync(order,Row("id",long.Parse(copied,CultureInfo.InvariantCulture))));
            stage="scoped-import-overwrite";await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/importDatas106",new{parentId="71",requestJson=new[]{Row("记录名",nameA,"status",1)}});Assert.Equal(1,SqlRowStore.Id(Assert.Single(await business.ReadAsync(order,Row("id",a))),"status"));Assert.Single(await business.ReadAsync(order,Row("name",nameA)));
            stage="actual-target-change";await business.UpdateAsync(party,101,Row("notes",matchA+"新"));await Page([]);await FileAccess(false);await Match(matchA+"新");await Page([a]);await FileAccess(true);
            stage="deleted-or-foreign-target";
            foreach(var bad in new[]{Row("delete_no",DateTime.UtcNow),Row("yz_project_id",55L)})
            {await business.UpdateAsync(party,101,bad);await Page([]);await FileAccess(false);await business.UpdateAsync(party,101,bad.Keys.ToDictionary(key=>key,key=>oldTargets[101].GetValueOrDefault(key),StringComparer.Ordinal));}
            await Page([a]);await FileAccess(true);
            stage="permission-revocation";await security.UpdateAsync("yz_role_permission",permission,Row("permission_values","[]"));await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/page",Request(),HttpStatusCode.Forbidden);await FileAccess(false);
            await security.UpdateAsync("yz_role_permission",permission,Row("permission_values","[\"is_all\"]"));await Page([a]);await FileAccess(true);
            stage="numeric-exact-representation";var numericRoute="/api/admin/auto/"+order+"/107";
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,numericRoute+"/update?parentId=71",new{id=Text(a),attachments=new[]{Text(file)}});
            await security.UpdateAsync("yz_role_permission",permission,Row("config_page_id",107L));await Match("1e1");
            var numericPage=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,numericRoute+"/page",Request());Assert.Equal("1",numericPage.GetProperty("data").GetProperty("total").ToString());Assert.Equal(Text(a),Assert.Single(numericPage.GetProperty("data").GetProperty("records").EnumerateArray()).GetProperty("id").GetString());await FileAccess(true);
            await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,numericRoute+"/update?parentId=71",new{id=Text(a),status=2});Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(order,Row("id",a))),"status"));
            foreach(var malformed in new[]{"10.0000000000000000000000000000001","1e-100"})
            {
                if(malformed=="1e-100")await business.UpdateAsync(party,101,Row("amount",0m));
                await Match(malformed);await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,numericRoute+"/page",Request(),HttpStatusCode.BadRequest);await FileAccess(false);
                await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,numericRoute+"/update?parentId=71",new{id=Text(a),status=1},HttpStatusCode.BadRequest);Assert.Equal(2,SqlRowStore.Id(Assert.Single(await business.ReadAsync(order,Row("id",a))),"status"));
            }
            await business.UpdateAsync(party,101,Row("amount",oldTargets[101]["amount"]));await Match("10.0000");await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,numericRoute+"/page",Request());await FileAccess(true);
            await security.UpdateAsync("yz_role_permission",permission,Row("config_page_id",106L));await Match(matchA+"新");await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/update?parentId=71",new{id=Text(a),attachments=new[]{Text(file)}});await Page([a]);await FileAccess(true);

            /// <summary>只核当前视图正式SQL输出，隐藏依赖不出现在响应中，total和count在分页前一致。</summary>
            async Task Page(long[] expected)
            {
                var found=new List<long>();for(var index=1;index<=Math.Max(1,expected.Length);index++)
                {var body=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/page",Request(index,1));var data=body.GetProperty("data");Assert.Equal(expected.Length,int.Parse(data.GetProperty("total").ToString(),CultureInfo.InvariantCulture));foreach(var row in data.GetProperty("records").EnumerateArray()){Assert.False(row.TryGetProperty("customerNotes",out _));found.Add(long.Parse(row.GetProperty("id").GetString()!,CultureInfo.InvariantCulture));}}
                Assert.Equal(expected.Order(),found.Order());Assert.Equal(expected.Length,int.Parse((await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/count",Request())).GetProperty("data").ToString(),CultureInfo.InvariantCulture));
            }
            /// <summary>真实流内容和resolve/list共用派生来源范围；其它页提示不能构造文件权限。</summary>
            async Task FileAccess(bool visible)
            {
                using var response=await ordinary.GetAsync("/api/admin/sys/file-library/protected-content/"+file);Assert.Equal(visible?HttpStatusCode.OK:HttpStatusCode.Unauthorized,response.StatusCode);if(visible)Assert.Equal(expectedBytes,await response.Content.ReadAsByteArrayAsync());
                await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/sys/file-library/resolve",new{fileIds=new[]{Text(file)}},visible?HttpStatusCode.OK:HttpStatusCode.Unauthorized);
                var list=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,"/api/admin/sys/file-library/list",new{configColumnId="112",folderId=""})).GetProperty("data");Assert.Equal(visible,list.EnumerateArray().Any(row=>row.GetProperty("id").GetString()==Text(file)));
            }
            /// <summary>实际下载XLSX校验export范围，不能用列表结果代替。</summary>
            async Task<string[]> Export()
            {var ticket=(await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/initExport",Request())).GetProperty("data").GetString();using var response=await ordinary.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+ticket);Assert.Equal(HttpStatusCode.OK,response.StatusCode);using var workbook=new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));return workbook.Worksheet(1).RowsUsed().SelectMany(row=>row.CellsUsed()).Select(cell=>cell.GetString()).Where(text=>text.StartsWith(prefix,StringComparison.Ordinal)).Distinct().ToArray();}
        }
        catch(Exception error){throw new InvalidOperationException("派生精确管理正式Web/实库阶段失败："+stage,error);}
        finally
        {
            foreach(var row in oldTargets)await business.UpdateAsync(party,row.Key,Row("notes",row.Value.GetValueOrDefault("notes"),"amount",row.Value.GetValueOrDefault("amount"),"delete_no",row.Value.GetValueOrDefault("delete_no"),"yz_project_id",row.Value["yz_project_id"]));
            await security.UpdateAsync("yz_manager",manager,Row("real_name",oldManager.GetValueOrDefault("real_name"),"yz_role_ids",oldManager["yz_role_ids"]));await security.UpdateAsync("yz_role",role,Row("is_scope",oldRole.GetValueOrDefault("is_scope")));
            if(createdPermission)await security.UpdateAsync("yz_role_permission",permission,Row("delete_no",DateTime.UtcNow));if(createdTarget)await security.UpdateAsync("yz_role_permission",targetPermission,Row("delete_no",DateTime.UtcNow));if(createdRole)await security.UpdateAsync("yz_role",normalRole,Row("delete_no",DateTime.UtcNow));
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,originalRoute+"/update?parentId=71",Row("id",Text(a),"name",oldA["name"],"status",oldA.GetValueOrDefault("status"),"customerId",oldA.GetValueOrDefault("customer_id"),"attachments",JsonSerializer.Deserialize<JsonElement>((string)oldA["attachments"]!)));
            await FreshWebHttp.SendAsync(super,HttpMethod.Post,originalRoute+"/update?parentId=71",Row("id",Text(b),"name",oldB["name"],"status",oldB.GetValueOrDefault("status"),"customerId",oldB.GetValueOrDefault("customer_id")));
        }
        /// <summary>客户端仍只提交普通父范围、名称筛选和分页。</summary>
        PageRequest Request(int page=1,int size=20)=>new(){ParentId="71",PageNo=page,PageSize=size,Filters=new(){{"name",JsonSerializer.SerializeToElement(prefix)}}};
        /// <summary>真实身份下一请求读取当前账号窄值。</summary>
        Task<int> Match(string? value)=>security.UpdateAsync("yz_manager",manager,Row("real_name",value));
    }
    /// <summary>固定键值由测试代码定义，不拼接业务SQL。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var i=0;i<values.Length;i+=2)row.Add((string)values[i]!,values[i+1]);return row;}
    /// <summary>长ID沿原HTTP合同以字符串传输。</summary>
    private static string Text(long value)=>value.ToString(CultureInfo.InvariantCulture);
}
