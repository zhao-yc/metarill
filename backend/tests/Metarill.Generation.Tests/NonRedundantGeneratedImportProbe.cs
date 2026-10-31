using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>复用同一正式生成HTTP/MySQL夹具，导入行来自原Excel解析后的中文表头JSON。</summary>
internal static class NonRedundantGeneratedImportProbe
{
    /// <summary>验证追加、原覆盖范围、物理写入、省略/null及整批回滚，不改前端协议。</summary>
    internal static async Task VerifyAsync(HttpClient client,string endpoint,SqlRowStore store,string table,NonRedGeneratedAuthorization authorization,NonRedRollbackObserver observer,List<long> ownIds,Func<Task<string>> rowsHash)
    {
        var overwrite=endpoint[..^3]+"103";
        var original=(await store.ReadIncludingDeletedAsync(table)).ToDictionary(row=>SqlRowStore.Id(row),row=>JsonSerializer.Serialize(row));
        var untouched=new long[]{9007199254741003,9007199254741004,9007199254741005};
        var beforeDenied=await rowsHash();authorization.Denied="import";
        try
        {
            using var denied=await client.PostAsJsonAsync(endpoint+"/importDatas101",new{parentId="71",requestJson=new[]{Row("记录名","拒权不能落库")}});
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        finally{authorization.Denied=null;}
        Assert.Equal(beforeDenied,await rowsHash());
        foreach(var parent in new string?[]{null,"73","74","999"})
        {
            Assert.Equal(500,(await Import(endpoint,"101",Array.Empty<object>(),parent)).GetProperty("code").GetInt32());
            Assert.Equal(beforeDenied,await rowsHash());
        }
        var appended=await Import(endpoint,"101",new object[]{Row("记录名","追加重复名","customer_id","A甲","status",2),Row("记录名","追加重复名","customer_id",102)});
        Assert.Equal(200,appended.GetProperty("code").GetInt32());Assert.Equal("导入成功2条数据",appended.GetProperty("data").GetString());await CaptureIds();
        var added=(await store.ReadIncludingDeletedAsync(table)).Where(row=>Equals(row["name"],"追加重复名")).ToArray();Assert.Equal(2,added.Length);
        Assert.All(added,row=>{Assert.Equal(54,Convert.ToInt32(row["yz_project_id"],CultureInfo.InvariantCulture));Assert.Equal(71,Convert.ToInt32(row["group_id"],CultureInfo.InvariantCulture));Assert.False(row.ContainsKey("customer_notes"));Assert.False(row.ContainsKey("customer_amount"));});
        Assert.Equal(new long[]{101,102},added.Select(row=>Convert.ToInt64(row["customer_id"],CultureInfo.InvariantCulture)).Order());
        using(var response=await client.GetAsync(endpoint+"/detail/"+SqlRowStore.Id(added.Single(row=>Convert.ToInt64(row["customer_id"],CultureInfo.InvariantCulture)==102))+"?parentId=71"))
        {var detail=await Json(response);Assert.Equal("B注释",detail.GetProperty("data").GetProperty("customerNotes").GetString());}

        Assert.Equal(200,(await Import(overwrite,"103",new object[]{Row("记录名","记录甲","customer_id","B乙","status",1)})).GetProperty("code").GetInt32());
        var changed=await Read(9007199254741001);Assert.Equal(102,Convert.ToInt64(changed["customer_id"],CultureInfo.InvariantCulture));Assert.Equal(1,Convert.ToInt32(changed["status"],CultureInfo.InvariantCulture));Assert.Equal("不得导出的详情",changed["private_payload"]);
        Assert.Equal(200,(await Import(overwrite,"103",new object[]{Row("记录名","记录甲","status",null)})).GetProperty("code").GetInt32());
        changed=await Read(9007199254741001);Assert.Null(changed["status"]);Assert.Equal(102,Convert.ToInt64(changed["customer_id"],CultureInfo.InvariantCulture));
        Assert.Equal(200,(await Import(overwrite,"103",new object[]{Row("记录名","记录甲","customer_id",null)})).GetProperty("code").GetInt32());Assert.Null((await Read(9007199254741001))["customer_id"]);
        Assert.Equal(200,(await Import(overwrite,"103",new object[]{Row("记录名","记录甲","customer_id","A甲","status",2)})).GetProperty("code").GetInt32());
        // 同名但处于其它父、其它项目或已删除的记录不参与覆盖匹配，当前范围创建新记录。
        Assert.Equal(200,(await Import(overwrite,"103",new object[]{Row("记录名","其它父"),Row("记录名","其它项目"),Row("记录名","已删除")})).GetProperty("code").GetInt32());await CaptureIds();
        foreach(var id in untouched)Assert.Equal(original[id],JsonSerializer.Serialize(await Read(id)));
        var beforeDuplicate=await rowsHash();Assert.Equal(500,(await Import(overwrite,"103",new object[]{Row("记录名","追加重复名","status",2)})).GetProperty("code").GetInt32());Assert.Equal(beforeDuplicate,await rowsHash());

        // 第一行已更新后，第二行的坏关系或未开放列必须使整个批次回滚。
        foreach(var invalid in new object[]{Row("记录名","坏行","customer_id","不存在的关系"),Row("记录名","坏行","customer_id","101"),Row("记录名","坏行","customer_id",103),Row("记录名","坏行","customer_id",104),Row("记录名","坏行","customer_notes","禁止写派生列"),Row("记录名","坏行","group_id",72),Row("记录名","坏行","yz_project_id",55),Row("记录名","坏行","id","9007199254741001")})
        {
            var before=await rowsHash();var failure=await Import(overwrite,"103",new[]{Row("记录名","记录乙","status",2),invalid});Assert.Equal(500,failure.GetProperty("code").GetInt32());Assert.Contains("第2行",failure.GetProperty("msg").GetString());Assert.Equal(before,await rowsHash());
        }
        var beforeAppendFailure=await rowsHash();Assert.Equal(500,(await Import(endpoint,"101",new object[]{Row("记录名","前行必须回滚","customer_id","A甲"),Row("记录名","坏后行","customer_id","不存在的关系")})).GetProperty("code").GetInt32());Assert.Equal(beforeAppendFailure,await rowsHash());
        var beforeObserver=await rowsHash();observer.FailureAt=observer.Calls+2;
        try{Assert.Equal(500,(await Import(endpoint,"101",new object[]{Row("记录名","事务第一行","customer_id","A甲"),Row("记录名","事务第二行","customer_id","B乙")})).GetProperty("code").GetInt32());}
        finally{observer.FailureAt=null;}
        Assert.Equal(beforeObserver,await rowsHash());

        /// <summary>成功导入才登记新增主键；失败批次依靠全表哈希证明没有残留。</summary>
        async Task CaptureIds(){foreach(var row in await store.ReadIncludingDeletedAsync(table)){var id=SqlRowStore.Id(row);if(!ownIds.Contains(id))ownIds.Add(id);}}
        /// <summary>直接读取物理仓储，验证派生列没有持久化。</summary>
        async Task<Dictionary<string,object?>> Read(long id)=>(await store.ReadIncludingDeletedAsync(table)).Single(row=>SqlRowStore.Id(row)==id);
        /// <summary>固定测试授权器及异常映射只验证生成合同，不冒充完整Host或原登录。</summary>
        async Task<JsonElement> Import(string route,string page,IEnumerable<object> rows,string? parent="71")
        {using var response=await client.PostAsJsonAsync(route+"/importDatas"+page,new{parentId=parent,requestJson=rows});Assert.True(response.IsSuccessStatusCode,"导入专项HTTP未返回业务状态。");return await Json(response);}
    }
    /// <summary>保持标题及显式null，不把省略字段补成null。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs)
    {var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<pairs.Length;index+=2)row.Add((string)pairs[index]!,pairs[index+1]);return row;}
    /// <summary>只读取实际HTTP响应，不解析日志。</summary>
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return document.RootElement.Clone();}
}
