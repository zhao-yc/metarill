using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Security;

// 仅测试程序集中的生成形状夹具，不注册到产品，不提供数据库或真实业务替身验收。
namespace Example.McpFixture.Generated.DTOs
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class DemoCustomerPage2200000000000100020CreateRequest {public Optional<string?> Name{get;set;}public Optional<string?> Notes{get;set;}}
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class DemoCustomerPage2200000000000100020UpdateRequest {public long Id{get;set;}public Optional<string?> Notes{get;set;}}
}
namespace Example.McpFixture.Generated.Services
{
    using Example.McpFixture.Generated.DTOs;
    /// <summary>只观察执行器是否调用正确强类型方法、请求存在性、actor及parent，不执行SQL。</summary>
    public sealed class DemoCustomerService
    {
        public List<string> Calls{get;}=[];public object? Request{get;private set;}public string? Parent{get;private set;}public RuntimeActor? Actor{get;private set;}public CancellationToken Token{get;private set;}
        /// <summary>记录真实反射调用边界，不模拟业务持久化成功。</summary>
        private void Observe(string name,object request,string? parent,RuntimeActor actor,CancellationToken token){Calls.Add(name);Request=request;Parent=parent;Actor=actor;Token=token;}
        /// <summary>分页参数由原PageRequest解析。</summary>
        public Task<object> Page2200000000000100020Async(PageRequest request,RuntimeActor actor,CancellationToken token){Observe("list",request,request.ParentId,actor,token);return Task.FromResult<object>(new{records=new[]{new{id=9007199254740993L,notes=(string?)null}},total=1L});}
        /// <summary>单记录路径的parent与id保持。</summary>
        public Task<object> Detail2200000000000100020Async(long id,string? parent,RuntimeActor actor,CancellationToken token){Observe("detail",id,parent,actor,token);return Task.FromResult<object>(new{id,notes=(string?)null});}
        /// <summary>创建使用强类型Optional字段。</summary>
        public Task<object> Create2200000000000100020Async(DemoCustomerPage2200000000000100020CreateRequest request,string? parent,RuntimeActor actor,CancellationToken token){Observe("create",request,parent,actor,token);return Task.FromResult<object>(new{id=9007199254740993L,notes=request.Notes.Value});}
        /// <summary>更新保留字段省略与显式null差异。</summary>
        public Task<object> Update2200000000000100020Async(DemoCustomerPage2200000000000100020UpdateRequest request,string? parent,RuntimeActor actor,CancellationToken token){Observe("update",request,parent,actor,token);return Task.FromResult<object>(new{id=request.Id});}
        /// <summary>删除返回原字符串形状，不包装成另一种成功协议。</summary>
        public Task<string> Delete2200000000000100020Async(long id,string? parent,RuntimeActor actor,CancellationToken token){Observe("delete",id,parent,actor,token);return Task.FromResult("删除成功");}
        /// <summary>复制仍由对应强类型方法承担。</summary>
        public Task<object> Copy2200000000000100020Async(long id,string? parent,RuntimeActor actor,CancellationToken token){Observe("copy",id,parent,actor,token);return Task.FromResult<object>(new{id=9007199254740994L});}
    }
}
