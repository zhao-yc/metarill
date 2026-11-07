using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Security;
using Metarill.Runtime.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests
{
    /// <summary>真实MVC和中间件／真实任务存储；scope结果明确为夹具，不把本例当成SQL或数据库验收。</summary>
    public sealed class AgentRecordWriteTests
    {
        [Theory][InlineData("Update","update")][InlineData("Delete","delete")][InlineData("Copy","copy")]
        public async Task Readonly_original_is_rejected_before_task_or_business_despite_client_echo(string action,string operation)
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory);fixture.Reader.Locked=true;var context=fixture.Context(action,operation,"{\"id\":\"7\",\"name\":\"客户端值\",\"isLocked\":false}");var calls=0;
            await new AgentTaskMiddleware(_=>{calls++;return Task.CompletedTask;}).InvokeAsync(context);
            Assert.Equal(403,context.Response.StatusCode);Assert.Equal(0,calls);Assert.Equal(0,TaskCount(memory));Assert.Equal(1,fixture.Reader.Calls);Assert.Equal(operation,fixture.Reader.Operation);Assert.Equal(7,fixture.Reader.Id);Assert.DoesNotContain("detail",fixture.Authorization.Operations);
            using var response=JsonDocument.Parse(((MemoryStream)context.Response.Body).ToArray());var error=response.RootElement.GetProperty("map").GetProperty("agentError");Assert.Equal("not_applied",error.GetProperty("outcome").GetString());Assert.Equal("fix_request",error.GetProperty("recovery").GetString());Assert.False(error.GetProperty("retryable").GetBoolean());
        }
        [Theory][InlineData("Update","update")][InlineData("Delete","delete")][InlineData("Copy","copy")]
        public async Task Writable_original_enters_task_only_after_scope_and_preserves_body(string action,string operation)
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory);var body="{\"id\":\"7\",\"name\":\"旧整实体\",\"isLocked\":true}";var context=fixture.Context(action,operation,body);var calls=0;
            await new AgentTaskMiddleware(async current=>
            {
                calls++;Assert.Equal(1,fixture.Reader.Calls);Assert.Equal("running",SqlRowStore.Text(Assert.Single(memory.Provider.Rows["sys_agent_task"]),"status"));
                Assert.Equal(body,await new StreamReader(current.Request.Body,Encoding.UTF8,leaveOpen:true).ReadToEndAsync());
                await current.Response.WriteAsJsonAsync(R<object>.Success(new{id="7"}),JsonDefaults.Options);
            }).InvokeAsync(context);
            Assert.Equal(1,calls);Assert.Equal(1,TaskCount(memory));Assert.Equal(operation,fixture.Reader.Operation);Assert.Equal("succeeded",SqlRowStore.Text(Assert.Single(memory.Provider.Rows["sys_agent_task"]),"status"));Assert.DoesNotContain("detail",fixture.Authorization.Operations);
        }
        [Theory][InlineData("{}")][InlineData("{\"id\":null}")][InlineData("{\"id\":\"7\",\"id\":\"8\"}")][InlineData("{\"Id\":\"7\"}")][InlineData("{\"id\":\"1e2\"}")][InlineData("[]")][InlineData("{\"id\":true}")][InlineData("{\"id\":{}}")][InlineData("{\"id\":[]}")][InlineData("{\"id\":9223372036854775808}")][InlineData("{\"id\":1.5}")]
        public async Task Invalid_legacy_id_is_rejected_without_query_or_task(string body)
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory);var context=fixture.Context("Update","update",body);var calls=0;
            await new AgentTaskMiddleware(_=>{calls++;return Task.CompletedTask;}).InvokeAsync(context);Assert.Equal(400,context.Response.StatusCode);Assert.Equal(0,calls);Assert.Equal(0,fixture.Reader.Calls);Assert.Equal(0,TaskCount(memory));
        }
        [Fact]
        public async Task Missing_confirmation_never_reads_original_or_registers_task()
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory);var context=fixture.Context("Update","update","{\"id\":7}");context.Request.Headers.Remove("X-Yzp-Agent-Confirm");
            await new AgentTaskMiddleware(_=>throw new InvalidOperationException("不应调用")).InvokeAsync(context);Assert.Equal(400,context.Response.StatusCode);Assert.Equal(0,fixture.Reader.Calls);Assert.Equal(0,TaskCount(memory));
        }
        [Theory][InlineData("missing")][InlineData("revoked")][InlineData("parent")]
        public async Task Actual_scope_or_operation_denial_cannot_create_task(string scenario)
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory,parentRequired:scenario=="parent");fixture.Reader.Visible=scenario!="missing";fixture.Authorization.Allow=scenario!="revoked";
            var context=fixture.Context("Update","update","{\"id\":7}");if(scenario=="parent")context.Request.QueryString=new("?parentId=8");
            await new AgentTaskMiddleware(_=>throw new InvalidOperationException("不应调用")).InvokeAsync(context);Assert.Equal(403,context.Response.StatusCode);Assert.Equal(0,TaskCount(memory));
        }
        [Fact]
        public async Task Required_parent_and_string_long_id_use_actual_mvc_sources()
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory,parentRequired:true);fixture.Reader.VisibleId=9007199254740993;var context=fixture.Context("Update","update","{\"id\":\"9007199254740993\",\"isLocked\":true}");context.Request.QueryString=new("?parentId=9");
            await new AgentTaskMiddleware(current=>current.Response.WriteAsJsonAsync(R<object>.Success(new{id="9007199254740993"}),JsonDefaults.Options)).InvokeAsync(context);
            Assert.Equal(200,context.Response.StatusCode);Assert.Equal(9007199254740993,fixture.Reader.Id);Assert.Equal("9",fixture.Reader.Parent);Assert.Equal(1,TaskCount(memory));
        }
        [Theory][InlineData("nonsense")][InlineData("isLocked()")][InlineData("this.entity.constructor")]
        public void Unsupported_condition_excludes_writes_without_removing_read_directory(string condition)
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory,condition:condition);fixture.Authorization.AllowDetail=true;
            Assert.Throws<UnauthorizedAccessException>(()=>fixture.Catalog.Describe("page:41001:update"));Assert.Equal("detail",fixture.Catalog.Describe("page:41001:detail").Operation);
        }
        [Fact]
        public void Legacy_controller_without_marker_does_not_publish_unproved_record_write()
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory);fixture.Authorization.AllowDetail=true;
            Assert.Throws<UnauthorizedAccessException>(()=>fixture.Catalog.Describe("page:41002:update"));Assert.Equal("detail",fixture.Catalog.Describe("page:41002:detail").Operation);
        }
        [Fact]
        public async Task Web_requests_keep_original_path_without_agent_readonly_interception()
        {
            using var memory=new PrincipalRealmFixture();using var fixture=new Fixture(memory);var context=fixture.Context("Update","update","{\"id\":7}");context.Items.Remove(typeof(AgentRequestIdentity));fixture.Reader.Locked=true;var calls=0;
            await new AgentTaskMiddleware(_=>{calls++;return Task.CompletedTask;}).InvokeAsync(context);Assert.Equal(1,calls);Assert.Equal(0,fixture.Reader.Calls);Assert.Equal(0,TaskCount(memory));
        }
        private static int TaskCount(PrincipalRealmFixture memory)=>memory.Provider.Rows.TryGetValue("sys_agent_task",out var rows)?rows.Count:0;
        private sealed class Fixture:IDisposable
        {
            internal readonly RecordWriteReader Reader=new();internal readonly RecordWriteAuthorization Authorization=new();internal readonly ServiceProvider Services;internal readonly AgentCapabilityCatalog Catalog;private readonly IActionDescriptorCollectionProvider actions;
            internal Fixture(PrincipalRealmFixture memory,bool parentRequired=false,string condition="isLocked")
            {
                Reader.RequireParent=parentRequired;var source=new MetadataCatalog{YzProjectId="100"};
                source.ConfigTables.Add(new(){Id="41000",TableName="record_write_fixture",YzProjectId="100"});source.ConfigTables.Add(new(){Id="41100",TableName="old_record_fixture",YzProjectId="100"});
                foreach(var(table,id)in new[]{("41000","41001"),("41100","41002")})source.ConfigPages.Add(new(){Id=id,ConfigTableId=table,YzProjectId="100",IsNew=1,IsDelete=1,IsCopy=1,WhereSql=parentRequired?"parent_id=@parentId":"",Extra=new(){["isUpdate"]=JsonSerializer.SerializeToElement(1),["readOnlyCondition"]=JsonSerializer.SerializeToElement(condition)}});
                foreach(var(table,prefix)in new[]{("41000","42"),("41100","43")})
                {source.ConfigColumns.Add(new(){Id=prefix+"01",ConfigTableId=table,ColumnName="id",LogicalType="int64"});source.ConfigColumns.Add(new(){Id=prefix+"02",ConfigTableId=table,ColumnName="name",LogicalType="string"});source.ConfigColumns.Add(new(){Id=prefix+"03",ConfigTableId=table,ColumnName="is_locked",LogicalType="bool"});}
                var services=new ServiceCollection();services.AddLogging();services.AddControllers().AddApplicationPart(typeof(Generated.Controllers.RecordWriteFixtureController).Assembly).AddJsonOptions(options=>JsonDefaults.CopyTo(options.JsonSerializerOptions));
                services.AddSingleton<IAgentHostResources>(new Resources(source));services.AddSingleton<IRuntimeAuthorization>(Authorization);services.AddSingleton(Reader);services.AddSingleton<GeneratedCapabilityIndex>();services.AddSingleton<AgentCapabilityCatalog>();services.AddSingleton(new AgentTaskOptions());services.AddSingleton(new AgentTaskStore(memory.Store,memory.Ids,new()));
                Services=services.BuildServiceProvider();Catalog=Services.GetRequiredService<AgentCapabilityCatalog>();actions=Services.GetRequiredService<IActionDescriptorCollectionProvider>();
            }
            internal DefaultHttpContext Context(string action,string operation,string body)
            {
                var descriptor=actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(value=>value.ControllerTypeInfo.AsType()==typeof(Generated.Controllers.RecordWriteFixtureController)&&value.ActionName==action);
                var context=new DefaultHttpContext{RequestServices=Services};context.Request.Method=operation=="delete"?"DELETE":"POST";context.Request.ContentType="application/json";context.Request.Path="/api/admin/auto/record_write_fixture/41001"+(operation=="copy"?"/copy/7":operation=="delete"?"":"/update");context.Request.RouteValues["id"]="7";context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(descriptor),action));
                context.Items[typeof(AgentRequestIdentity)]=new AgentRequestIdentity(new(Authorization.RequireActor(),new HashSet<string>(),1,2),"agent",2);context.Request.Headers["X-Yzp-Agent-Capability"]="page:41001:"+operation;context.Request.Headers["X-Yzp-Agent-Confirm"]="true";context.Request.Headers["X-Yzp-Agent-Request-Id"]=Guid.NewGuid().ToString("D");context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(body));context.Response.Body=new MemoryStream();return context;
            }
            public void Dispose()=>Services.Dispose();
        }
        private sealed class Resources(MetadataCatalog catalog):IAgentHostResources
        {public AgentHostCatalog AgentCatalog{get;}=AgentHostCatalog.ForApplication(catalog);public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs=>[];public bool IsPlatformRealm(string project,string admin)=>false;}
    }
    public sealed class RecordWriteAuthorization:IRuntimeAuthorization
    {
        internal bool Allow=true,AllowDetail;internal readonly List<string> Operations=[];private readonly RuntimeActor actor=new(8,"原记录测试",false,"100","200");
        public RuntimeActor RequireActor()=>actor;
        public void RequirePagePermission(string page,string operation){Operations.Add(operation);if(!Allow||operation=="detail"&&!AllowDetail)throw new UnauthorizedAccessException("操作已撤销。");}
    }
    public sealed class RecordWriteReader
    {
        internal bool Locked,Visible=true,RequireParent;internal long VisibleId=7,Id;internal string? Parent,Operation;internal int Calls;
        /// <summary>仅用返回行替身证明绑定/时序，真实范围SQL由生成包验证。</summary>
        public Task<RecordWriteEntity?> ReadAgentRecord41001Async(long id,string? parent,string operation,RuntimeActor actor,IRuntimeAuthorization authorization,CancellationToken token)
        {Calls++;Id=id;Parent=parent;Operation=operation;Assert.Equal(authorization.RequireActor(),actor);authorization.RequirePagePermission("41001",operation);return Task.FromResult(Visible&&id==VisibleId&&(!RequireParent||parent=="9")?new RecordWriteEntity{Id=id,IsLocked=Locked}:null);}
    }
    public sealed class RecordWriteEntity
    {
        [JsonPropertyName("id")]public long Id{get;set;}
        [JsonPropertyName("isLocked")][FreeSql.DataAnnotations.Column(Name="is_locked")]public bool IsLocked{get;set;}
        public string Unpublished=>throw new InvalidOperationException("不得读取未发布属性。");
    }
}
namespace Metarill.Agent.Tests.Generated.Controllers
{
    [ApiController,Route("api/admin/auto/record_write_fixture/41001"),GeneratedAgentWriteAccess("100","41001","41000",typeof(RecordWriteEntity),typeof(RecordWriteReader),nameof(RecordWriteReader.ReadAgentRecord41001Async))]
    public sealed class RecordWriteFixtureController(RecordWriteReader reader):ControllerBase
    {
        [HttpPost("update"),LegacyPageWriteRequest(typeof(RecordWriteRequest))]public R<string> Update([FromBody]JsonElement entity,[FromQuery]string? parentId){_=reader;return R<string>.Success();}
        [HttpDelete,LegacyPageWriteRequest(typeof(RecordWriteRequest))]public R<string> Delete([FromBody]JsonElement entity,[FromQuery]string? parentId)=>R<string>.Success();
        [HttpPost("copy/{id}")]public R<string> Copy(long id,[FromQuery]string? parentId)=>R<string>.Success();
        [HttpGet("detail/{id}")]public R<string> Detail(long id)=>R<string>.Success();
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]public sealed class RecordWriteRequest{[JsonPropertyName("id")]public long Id{get;set;}public Optional<string?> Name{get;set;}}
    [ApiController,Route("api/admin/auto/old_record_fixture/41002")]
    public sealed class OldRecordFixtureController:ControllerBase
    {
        [HttpPost("update"),LegacyPageWriteRequest(typeof(RecordWriteRequest))]public R<string> Update([FromBody]JsonElement entity)=>R<string>.Success();
        [HttpGet("detail/{id}")]public R<string> Detail(long id)=>R<string>.Success();
    }
}
