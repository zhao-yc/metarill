using System.Globalization;
using System.Net;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class FreshDerivedRelationsMySqlTests
{
    /// <summary>只附加树场景，所有请求仍进入实际Production/Web宿主和真实角色判断。</summary>
    private sealed class TreeScenario(bool automatic,SqlRowStore store)
    {
        private const long Tree=First+40000,Company=First+30000;
        private const string Route="/api/admin/auto/derived_fresh_source/201/";
        private const string InitialPath="树根/树中/树叶";
        internal const string RenamedPath="正式新树根/树中/正式新树叶";
        /// <summary>全部固定样本位于本轮专属空库；包含空根、越项目、删除、禁用父、环和断链。</summary>
        internal async Task SeedAsync()
        {
            var names=new[]{"树根","树中","树叶","其它根","隐藏树公司"};for(var i=0;i<names.Length;i++)await store.InsertAsync("derived_fresh_company",Row("id",Company+i,"yz_project_id",Project,"name",names[i]));
            for(var i=0;i<11;i++)
            {
                long? parent=i switch{1=>Tree,2=>Tree+1,7=>Tree+6,8=>Tree+9,9=>Tree+8,10=>Tree+999,_=>null};object? key=automatic?parent?.ToString(CultureInfo.InvariantCulture):parent;if(automatic&&i==3)key="";
                await store.InsertAsync("derived_fresh_tree",Row("id",Tree+i,"yz_project_id",i==4?Project+1:Project,"parent_derived_fresh_tree_id",key,"company_id",Company+(i<4?i:4),"status",i==6?0:1,"delete_no",i==5?new DateTime(2026,1,1):null));
            }
        }
        /// <summary>候选只发送原levelKey／selectedPath，来源父参数沿原URL。</summary>
        internal Task<JsonElement> Options(HttpClient client,string? parent=null,HttpStatusCode expected=HttpStatusCode.OK)=>Send(client,HttpMethod.Post,Route+"cascadeTreeOptions220?parentId="+Parent,new{levelKey=parent is null?"selfTree-0":"selfTree-1",selectedPath=new{parentId=parent??""}},expected);
        /// <summary>空路径也必须检查真实来源和目标权限。</summary>
        private Task<JsonElement> Path(HttpClient client,string leaf,HttpStatusCode expected=HttpStatusCode.OK)=>Send(client,HttpMethod.Post,Route+"cascadeTreePath220?parentId="+Parent,new{leafId=leaf},expected);
        /// <summary>直接目标撤权不能靠空路径绕过，不更改原选择页查询继承规则。</summary>
        internal async Task DeniedTargetAsync(HttpClient client){await Options(client,expected:HttpStatusCode.Forbidden);await Path(client,"",HttpStatusCode.Forbidden);}
        /// <summary>实际账号读取原五字段候选及完整路径，所有坏祖先必须整体拒绝。</summary>
        internal async Task ReadScopesAsync(HttpClient client)
        {
            var roots=(await Options(client)).GetProperty("data").EnumerateArray().ToArray();Assert.Equal(new[]{Tree,Tree+3},roots.Select(x=>long.Parse(x.GetProperty("id").GetString()!,CultureInfo.InvariantCulture)).Order());var root=roots.Single(x=>x.GetProperty("id").GetString()==Tree.ToString());Assert.True(root.GetProperty("hasChildren").GetBoolean());Assert.Equal(new[]{"hasChildren","id","name","text","value"},root.EnumerateObject().Select(x=>x.Name).Order());
            var child=Assert.Single((await Options(client,Tree.ToString())).GetProperty("data").EnumerateArray());Assert.Equal((Tree+1).ToString(),child.GetProperty("id").GetString());Assert.True(child.GetProperty("hasChildren").GetBoolean());var path=(await Path(client,(Tree+2).ToString())).GetProperty("data");Assert.Equal(InitialPath,path.GetProperty("displayText").GetString());Assert.DoesNotContain("companyId",path.GetRawText());
            foreach(var bad in new[]{Tree+4,Tree+5,Tree+6,Tree+7,Tree+8,Tree+10,Tree+999})await Path(client,bad.ToString(),HttpStatusCode.InternalServerError);
        }
        /// <summary>原表单只提交叶子ID；元数据决定源字符串或int64，不经过浮点。</summary>
        internal void AddSelection(Dictionary<string,object?> row)=>row["treeId"]=(Tree+2).ToString(CultureInfo.InvariantCulture);
        /// <summary>原Excel使用中文列标题和完整路径，名称不作为任意SQL条件。</summary>
        internal void AddImport(Dictionary<string,object?> row)=>row["树节点"]=InitialPath;
        /// <summary>回显与复制均重新计算路径，不能信任整entity回传的名称。</summary>
        internal void AssertPath(JsonElement row,bool renamed=false){Assert.Equal((Tree+2).ToString(),row.GetProperty("treeId").GetString());Assert.Equal(renamed?"正式新树叶":"树叶",row.GetProperty("treeName").GetString());Assert.Equal(renamed?RenamedPath:InitialPath,row.GetProperty("treeIdDisplayText").GetString());}
        /// <summary>真实写接口重查完整祖先，失败不能改变本轮来源数据。</summary>
        internal async Task InvalidWritesAsync(HttpClient client,string id,Func<Task<string>> snapshot)
        {
            var before=await snapshot();foreach(var bad in new[]{Tree+4,Tree+5,Tree+6,Tree+7,Tree+8,Tree+10,Tree+999}){await Send(client,HttpMethod.Post,Route+"update?parentId="+Parent,new{id,treeId=bad.ToString()},HttpStatusCode.InternalServerError);Assert.Equal(before,await snapshot());}
        }
        /// <summary>来源仍可读时，重新选择叶子也不能绕过目标查询撤权。</summary>
        internal async Task DeniedWriteAsync(HttpClient client,string id,Func<Task<string>> snapshot){var before=await snapshot();await Send(client,HttpMethod.Post,Route+"update?parentId="+Parent,new{id,treeId=(Tree+2).ToString()},HttpStatusCode.Forbidden);Assert.Equal(before,await snapshot());}
        /// <summary>源导入授权可解析路径，第二行树路径失败必须回滚整批而不是部分成功。</summary>
        internal async Task ImportRollbackAsync(HttpClient client,Func<Task<string>> snapshot)
        {
            var first=Import("树整批首行","甲_%'公司","乙公司");AddImport(first);var second=Import("树整批尾行","甲_%'公司","乙公司");second["树节点"]="不存在/树叶";var before=await snapshot();await Send(client,HttpMethod.Post,Route+"importDatas201",new{parentId=Parent.ToString(),requestJson=new[]{first,second}},HttpStatusCode.BadRequest);Assert.Equal(before,await snapshot());
        }
        /// <summary>真实维护接口改名后，路径、叶子名和分页前后代筛选立即变化，源物理记录保持。</summary>
        internal async Task RenameAndFilterAsync(HttpClient owner,HttpClient client,string id,Func<Task<string>> snapshot)
        {
            var before=await snapshot();foreach(var pair in new[]{(Company,"正式新树根"),(Company+2,"正式新树叶")})await Send(owner,HttpMethod.Post,"/api/admin/auto/derived_fresh_company/401/update",new{id=pair.Item1.ToString(),name=pair.Item2});Assert.Equal(before,await snapshot());AssertPath((await Send(client,HttpMethod.Get,Route+"detail/"+id+"?parentId="+Parent)).GetProperty("data"),true);Assert.Equal(RenamedPath,(await Path(client,(Tree+2).ToString())).GetProperty("data").GetProperty("displayText").GetString());
            var request=new PageRequest{ParentId=Parent.ToString(),PageSize=1,Filters=new(){{"treeId",JsonSerializer.SerializeToElement(new{treeId=Tree.ToString(),treeName="不参与匹配"})}}};var page=(await Send(client,HttpMethod.Post,Route+"page",request)).GetProperty("data");Assert.Equal("3",page.GetProperty("total").GetString());Assert.Single(page.GetProperty("records").EnumerateArray());Assert.Equal("3",(await Send(client,HttpMethod.Post,Route+"count",request)).GetProperty("data").ToString());
        }
        /// <summary>仅用真实标准维护页隐藏／恢复根；源回显不能返回部分路径或修改已存叶子ID。</summary>
        internal async Task LiveScopeAsync(HttpClient owner,HttpClient client,string id,Func<Task<string>> snapshot)
        {
            var before=await snapshot();await Send(owner,HttpMethod.Post,"/api/admin/auto/derived_fresh_tree/502/update",new{id=Tree.ToString(),status=0});Assert.Equal(before,await snapshot());var detail=(await Send(client,HttpMethod.Get,Route+"detail/"+id+"?parentId="+Parent)).GetProperty("data");Assert.Equal((Tree+2).ToString(),detail.GetProperty("treeId").GetString());Assert.Equal(JsonValueKind.Null,detail.GetProperty("treeName").ValueKind);Assert.Equal("",detail.GetProperty("treeIdDisplayText").GetString());await Path(client,(Tree+2).ToString(),HttpStatusCode.InternalServerError);await Send(client,HttpMethod.Post,Route+"update?parentId="+Parent,new{id,treeId=(Tree+2).ToString()},HttpStatusCode.InternalServerError);Assert.Equal(before,await snapshot());await Send(client,HttpMethod.Post,Route+"initExport",new PageRequest{ParentId=Parent.ToString(),SelectedIds=[id]},HttpStatusCode.InternalServerError);await Send(owner,HttpMethod.Post,"/api/admin/auto/derived_fresh_tree/502/update",new{id=Tree.ToString(),status=1});AssertPath((await Send(client,HttpMethod.Get,Route+"detail/"+id+"?parentId="+Parent)).GetProperty("data"),true);
        }
        /// <summary>原控件空字符串保留源类型合同，随后显式null独立清空，不能与字段省略混同。</summary>
        internal async Task ClearedAsync(HttpClient client,string id,JsonElement row)
        {
            if(automatic)Assert.Equal(JsonValueKind.Null,row.GetProperty("treeId").ValueKind);else Assert.Equal("",row.GetProperty("treeId").GetString());Assert.Equal(JsonValueKind.Null,row.GetProperty("treeName").ValueKind);Assert.Equal("",row.GetProperty("treeIdDisplayText").GetString());var cleared=(await Send(client,HttpMethod.Post,Route+"update?parentId="+Parent,new{id,treeId=(object?)null})).GetProperty("data");Assert.Equal(JsonValueKind.Null,cleared.GetProperty("treeId").ValueKind);
        }
    }
}
