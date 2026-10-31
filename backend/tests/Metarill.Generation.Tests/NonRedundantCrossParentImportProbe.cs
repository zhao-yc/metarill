using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>跨父导入消费原Excel中文标题JSON，真实HTTP/MySQL验证依赖顺序与整批事务。</summary>
internal static class NonRedundantCrossParentImportProbe
{
    /// <summary>追加先解析本行目标父，覆盖可用锁定原对象；显式null不回退，失败整批不落库。</summary>
    internal static async Task VerifyAsync(HttpClient client,string source,SqlRowStore store,string table,NonRedGeneratedAuthorization authorization,NonRedRollbackObserver observer,List<long> ownIds,Func<Task<string>> rowsHash)
    {
        var append=source[..^3]+"104";var overwrite=source[..^3]+"105";
        var before=await rowsHash();authorization.Denied="import";
        try{using var denied=await client.PostAsJsonAsync(append+"/importDatas104",new{requestJson=new[]{Row("记录名","拒权跨父","customer_id","A甲","group_id",71)}});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        finally{authorization.Denied=null;}
        Assert.Equal(before,await rowsHash());
        // 关联列排在父列前，客户端parentId故意不同；只能按本行物理group_id确定候选。
        Assert.Equal(200,(await Import(false,new[]{Row("customer_id","其它父候选","记录名","跨父乙","group_id","父乙","status",1),Row("customer_id","A甲","记录名","跨父甲","group_id",71,"status",1)},"73")).GetProperty("code").GetInt32());await CaptureIds();
        var a=await Read("跨父甲");var b=await Read("跨父乙");Assert.Equal(101,Convert.ToInt64(a["customer_id"],CultureInfo.InvariantCulture));Assert.Equal(71,Convert.ToInt64(a["group_id"],CultureInfo.InvariantCulture));Assert.Equal(105,Convert.ToInt64(b["customer_id"],CultureInfo.InvariantCulture));Assert.Equal(72,Convert.ToInt64(b["group_id"],CultureInfo.InvariantCulture));Assert.False(a.ContainsKey("customer_name"));Assert.False(a.ContainsKey("customer_notes"));
        // 覆盖省略父列沿用已锁定原对象，不把缺失父补成null，也不向DTO提交旧值。
        Assert.Equal(200,(await Import(true,new[]{Row("记录名","跨父甲","customer_id","B乙"),Row("记录名","跨父乙","customer_id","其它父候选")})).GetProperty("code").GetInt32());
        Assert.Equal(102,Convert.ToInt64((await Read("跨父甲"))["customer_id"],CultureInfo.InvariantCulture));Assert.Equal(1,Convert.ToInt32((await Read("跨父甲"))["status"],CultureInfo.InvariantCulture));
        Assert.Equal(200,(await Import(true,new[]{Row("customer_id","其它父候选","记录名","跨父甲","group_id",72)})).GetProperty("code").GetInt32());Assert.Equal(72,Convert.ToInt64((await Read("跨父甲"))["group_id"],CultureInfo.InvariantCulture));
        Assert.Equal(200,(await Import(true,new[]{Row("customer_id",null,"记录名","跨父甲","group_id",null)})).GetProperty("code").GetInt32());Assert.Null((await Read("跨父甲"))["customer_id"]);Assert.Null((await Read("跨父甲"))["group_id"]);
        before=await rowsHash();Assert.Equal(500,(await Import(true,new[]{Row("记录名","跨父甲","customer_id","A甲")})).GetProperty("code").GetInt32());Assert.Equal(before,await rowsHash());
        Assert.Equal(200,(await Import(true,new[]{Row("customer_id","A甲","记录名","跨父甲","group_id",71)})).GetProperty("code").GetInt32());
        Assert.Equal(200,(await Import(true,new[]{Row("customer_id","其它父候选","记录名","跨父覆盖新增","group_id",72)})).GetProperty("code").GetInt32());await CaptureIds();
        Assert.Equal(200,(await Import(false,new[]{Row("记录名","跨父声明默认","customer_id","A甲")})).GetProperty("code").GetInt32());await CaptureIds();Assert.Equal(71,Convert.ToInt64((await Read("跨父声明默认"))["group_id"],CultureInfo.InvariantCulture));
        // 普通导入按来源import动作，不凭空增加目标页list门槛。
        authorization.Denied="list";authorization.DeniedPage="201";
        try{Assert.Equal(200,(await Import(true,new[]{Row("记录名","跨父乙","customer_id","其它父候选")})).GetProperty("code").GetInt32());}
        finally{authorization.Denied=null;authorization.DeniedPage=null;}
        // 第一行确实写入后第二行失败：坏父、越界候选、只改父、空父、类型或标题均整批回滚。
        foreach(var invalid in new[]{
            Row("记录名","跨父乙","customer_id","A甲","group_id",72),
            Row("记录名","跨父乙","customer_id",101,"group_id",72),
            Row("记录名","跨父乙","customer_id","101","group_id",71),
            Row("记录名","跨父乙","customer_id",103,"group_id",73),
            Row("记录名","跨父乙","customer_id",104,"group_id",74),
            Row("记录名","跨父乙","customer_id","其它父候选","group_id",null),
            Row("记录名","跨父甲","group_id",72),
            Row("记录名","新建缺父","customer_id","其它父候选"),
            Row("记录名","跨父乙","group_id","错误类型"),
            Row("记录名","跨父乙","customer_name","派生列不可写"),
            Row("记录名","跨父乙","yz_project_id",55),
            Row("记录名","追加重复名","status",2)})
        {
            before=await rowsHash();var failure=await Import(true,new[]{Row("记录名","跨父甲","status",2),invalid});Assert.Equal(500,failure.GetProperty("code").GetInt32());Assert.Contains("第2行",failure.GetProperty("msg").GetString());Assert.Equal(before,await rowsHash());
        }
        before=await rowsHash();observer.FailureAt=observer.Calls+2;
        try{Assert.Equal(500,(await Import(false,new[]{Row("记录名","跨父事务甲","customer_id","A甲","group_id",71),Row("记录名","跨父事务乙","customer_id","其它父候选","group_id",72)})).GetProperty("code").GetInt32());}
        finally{observer.FailureAt=null;}
        Assert.Equal(before,await rowsHash());
        /// <summary>成功批次登记精确新增ID，不吞掉失败现场。</summary>
        async Task CaptureIds(){foreach(var row in await store.ReadIncludingDeletedAsync(table)){var id=SqlRowStore.Id(row);if(!ownIds.Contains(id))ownIds.Add(id);}}
        /// <summary>读取物理行验证最终父和外键，不以VO模拟写库结果。</summary>
        async Task<Dictionary<string,object?>> Read(string name)=>(await store.ReadIncludingDeletedAsync(table)).Single(row=>Equals(row["name"],name));
        /// <summary>仅使用原路由和原requestJson，HTTP及业务状态分别验证。</summary>
        async Task<JsonElement> Import(bool replace,IEnumerable<Dictionary<string,object?>> rows,string? parentId=null)
        {using var response=await client.PostAsJsonAsync((replace?overwrite+"/importDatas105":append+"/importDatas104"),new{parentId,requestJson=rows});Assert.True(response.IsSuccessStatusCode,"跨父导入HTTP未返回业务状态。");using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return document.RootElement.Clone();}
    }
    /// <summary>保留标题排列及显式null，省略字段不补值。</summary>
    private static Dictionary<string,object?> Row(params object?[] pairs)
    {var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<pairs.Length;index+=2)row.Add((string)pairs[index]!,pairs[index+1]);return row;}
}
