using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>原前端请求形状经正式完整包Web/HTTP验证目录树、模糊搜索、整理与删除，不改前端或注入授权替身。</summary>
internal static class NonRedundantFileManagementMySqlProbe
{
    /// <summary>所有样本限定外层本轮空库，保留原被引用文件供后续撤权检查。</summary>
    internal static async Task VerifyAsync(HttpClient ordinary,HttpClient super,DatabaseSession session,MetadataCatalog catalog,MetadataCatalog files,long readableFile,long unboundOwnedFile)
    {
        const string route="/api/admin/sys/file-library";var library=new SqlRowStore(session,files);
        var root=await FreshWebHttp.SendAsync(super,HttpMethod.Post,route+"/folders",new{name="共享根",configColumnId="112"});var rootId=root.GetProperty("data").GetProperty("id").GetString()!;
        var child=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/folders",new{name=" 子目录 ",parentId=rootId,configColumnId="112"});var childId=child.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(rootId,child.GetProperty("data").GetProperty("parentId").GetString());Assert.Equal("子目录",child.GetProperty("data").GetProperty("name").GetString());
        var tree=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/folders/list",new{configColumnId="112"});Assert.Contains(tree.GetProperty("data").EnumerateArray(),row=>row.GetProperty("id").GetString()==rootId);Assert.Contains(tree.GetProperty("data").EnumerateArray(),row=>row.GetProperty("id").GetString()==childId&&row.GetProperty("parentId").GetString()==rootId);
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/folders/list");
        // 完整包沿既有业务状态错误契约返回500；拒绝后逐笔核对目录/引用没有被改写。
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/folders",new{name="子目录",parentId=rootId,configColumnId="112"},HttpStatusCode.InternalServerError);
        Assert.Single(await library.ReadAsync("yz_file_library_folder",Row("parent_folder_id",long.Parse(rootId,CultureInfo.InvariantCulture),"name","子目录")));
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Put,route+"/folders/"+rootId+"/move",new{targetParentId=childId},HttpStatusCode.InternalServerError);
        Assert.Null(Assert.Single(await library.ReadAsync("yz_file_library_folder",Row("id",long.Parse(rootId,CultureInfo.InvariantCulture))))["parent_folder_id"]);
        var renamedFolder=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Put,route+"/folders/"+rootId,new{name="共同整理"});Assert.Equal(rootId,renamedFolder.GetProperty("data").GetProperty("id").GetString());Assert.Equal("共同整理",renamedFolder.GetProperty("data").GetProperty("name").GetString());
        var initial=Assert.Single(await library.ReadAsync("yz_file_library",Row("id",readableFile)));
        var moved=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/move",new{fileId=readableFile.ToString(CultureInfo.InvariantCulture),targetFolderId=childId});Assert.Equal("移动成功",moved.GetProperty("data").GetString());
        var renamed=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Put,route+"/"+readableFile+"/name",new{name="跨目录文件权限.pdf"});Assert.Equal("跨目录文件权限.pdf",renamed.GetProperty("data").GetProperty("originalName").GetString());Assert.Equal("text/plain",renamed.GetProperty("data").GetProperty("mimeType").GetString());Assert.Equal(readableFile.ToString(CultureInfo.InvariantCulture),renamed.GetProperty("data").GetProperty("id").GetString());
        var listed=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/list",new{configColumnId="112",keyword="",folderId=childId});Assert.Contains(listed.GetProperty("data").EnumerateArray(),IsFile);
        var atRoot=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/list",new{configColumnId="112",keyword="",folderId=""});Assert.DoesNotContain(atRoot.GetProperty("data").EnumerateArray(),IsFile);
        var searched=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/list",new{configColumnId="112",keyword="跨件 权限",folderId=""});Assert.Contains(searched.GetProperty("data").EnumerateArray(),IsFile);
        tree=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/folders/list",new{configColumnId="112"});Assert.Equal("1",Assert.Single(tree.GetProperty("data").EnumerateArray(),row=>row.GetProperty("id").GetString()==childId).GetProperty("fileCount").ToString());
        var raw=Assert.Single(await library.ReadAsync("yz_file_library",Row("id",readableFile)));Assert.Equal(initial["storage_key"],raw["storage_key"]);Assert.Equal(initial["sha256"],raw["sha256"]);Assert.Equal(initial["content_type"],raw["content_type"]);
        using(var response=await ordinary.GetAsync(route+"/protected-content/"+readableFile)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("text/plain",response.Content.Headers.ContentType?.MediaType);Assert.Equal((string)initial["sha256"]!,Convert.ToHexStringLower(SHA256.HashData(await response.Content.ReadAsByteArrayAsync())));}
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"/"+readableFile,expected:HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(super,HttpMethod.Delete,route+"/"+readableFile,expected:HttpStatusCode.InternalServerError);
        Assert.Single(await library.ReadAsync("yz_file_library",Row("id",readableFile)));Assert.NotEmpty(await library.ReadAsync("yz_file_ref",Row("file_id",readableFile)));
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"/folders/"+childId,expected:HttpStatusCode.InternalServerError);
        Assert.Single(await library.ReadAsync("yz_file_library_folder",Row("id",long.Parse(childId,CultureInfo.InvariantCulture))));
        var free=Assert.Single(await library.ReadAsync("yz_file_library",Row("id",unboundOwnedFile)));Assert.NotEqual(free["owner_id"],initial["owner_id"]);
        var deleted=await FreshWebHttp.SendAsync(super,HttpMethod.Delete,route+"/"+unboundOwnedFile);Assert.Equal("删除成功",deleted.GetProperty("data").GetString());Assert.Empty(await library.ReadAsync("yz_file_library",Row("id",unboundOwnedFile)));
        // 只在本轮独占库构造其它项目目录，猜ID不能访问、移动或改写它。
        var foreign=new SnowflakeIdGenerator(941).NextId();await library.InsertAsync("yz_file_library_folder",Row("id",foreign,"name","其它项目目录","yz_project_id",55L,"owner_id",initial["owner_id"],"insert_time",DateTime.UtcNow));
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Put,route+"/folders/"+foreign,new{name="不能修改"},HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/move",new{fileId=readableFile.ToString(CultureInfo.InvariantCulture),targetFolderId=foreign.ToString(CultureInfo.InvariantCulture)},HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/folders",new{name="不能越界",parentId=foreign.ToString(CultureInfo.InvariantCulture),configColumnId="112"},HttpStatusCode.Unauthorized);
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/list",new{configColumnId="999999",folderId=""},HttpStatusCode.Unauthorized);
        raw=Assert.Single(await library.ReadAsync("yz_file_library",Row("id",readableFile)));Assert.Equal(childId,raw["folder_id"]!.ToString());
        var detached=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Put,route+"/folders/"+childId+"/move",new{targetParentId=""});Assert.Equal(JsonValueKind.Null,detached.GetProperty("data").GetProperty("parentId").ValueKind);
        await FreshWebHttp.SendAsync(ordinary,HttpMethod.Post,route+"/move",new{fileId=readableFile.ToString(CultureInfo.InvariantCulture),targetFolderId=""});
        var removed=await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"/folders/"+childId);Assert.Equal("删除成功",removed.GetProperty("data").GetString());await FreshWebHttp.SendAsync(ordinary,HttpMethod.Delete,route+"/folders/"+rootId);
        /// <summary>原列表id必须为字符串长ID，不用已转换后的宽松值掩盖协议差异。</summary>
        bool IsFile(JsonElement row)=>row.GetProperty("id").GetString()==readableFile.ToString(CultureInfo.InvariantCulture);
    }
    /// <summary>仅构造本轮固定基础设施字段，不执行调用方SQL或覆盖已存在对象。</summary>
    private static Dictionary<string,object?> Row(params object?[] values){var result=new Dictionary<string,object?>();for(var i=0;i<values.Length;i+=2)result.Add((string)values[i]!,values[i+1]);return result;}
}
