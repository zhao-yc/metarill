using System.Reflection;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime;
using Metarill.Runtime.Agent;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Agent.Tests
{
    /// <summary>真实MVC发现、严格JSON绑定及中间件前置门禁；模拟实体读取，不冒充实库范围SQL验收。</summary>
    public sealed partial class AgentBusinessActionTests
    {
        [Fact]
        public void Declared_actions_publish_fixed_button_confirmation_and_typed_schema_without_page_permission()
        {
            using var fixture = new Fixture();
            var preview = fixture.Catalog.Describe("action:fixture.preview"); var complete = fixture.Catalog.Describe("action:fixture.complete");
            Assert.Equal("action", preview.Operation); Assert.Equal("31", preview.ButtonId); Assert.False(preview.ConfirmationRequired);
            Assert.True(complete.ConfirmationRequired); Assert.Equal("确认处理？", complete.ConfirmationMessage);
            Assert.Equal("/admin/custom/agent-fixture/preview", preview.Path); Assert.Equal("21", preview.PageId);
            using var schema = JsonDocument.Parse(JsonSerializer.Serialize(preview.RequestSchema));
            Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
            Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("recordKey", out _));
            Assert.Equal(new[] { "recordKey" }, schema.RootElement.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(0, fixture.Authorization.PageChecks); Assert.True(fixture.Authorization.ButtonChecks > 0);
            // 声明不是HTTP注册证明，普通未声明方法与开放正文不会自动进入目录。
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.Describe("action:fixture.raw"));
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.Describe("action:fixture.missing"));
        }
        [Theory]
        [InlineData("wrong-path")]
        [InlineData("wrong-project")]
        [InlineData("wrong-table")]
        [InlineData("duplicate-button")]
        [InlineData("unsupported-readonly-expression")]
        [InlineData("unknown-visibility")]
        [InlineData("page-foreign")]
        [InlineData("table-foreign")]
        [InlineData("not-business")]
        public void Invalid_button_source_is_not_exposed_but_standard_catalog_stays_available(string variant)
        {
            using var fixture = new Fixture(variant);
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.Describe("action:fixture.preview"));
            // 该测试身份普通页权限关闭；无效业务配置不破坏整个目录构建。
            fixture.Catalog.List();
        }
        [Fact]
        public void Published_button_is_immutable_and_revocation_filters_current_directory()
        {
            using var fixture = new Fixture(); var snapshot = fixture.Resources.AgentCatalog.FindButton("31")!;
            fixture.Metadata.ConfigButtons.Clear(); fixture.Metadata.ConfigPages[0].Name = "后改";
            Assert.Equal("21", snapshot.Page.Id); Assert.Equal("合同", snapshot.Page.Name); Assert.NotNull(fixture.Catalog.Describe("action:fixture.preview"));
            fixture.Authorization.AllowButton = false;
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.Describe("action:fixture.preview"));
        }
        [Fact]
        public void Writable_visibility_requires_actual_update_while_normal_does_not()
        {
            using var fixture = new Fixture("writable");
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.Describe("action:fixture.preview"));
            fixture.Authorization.AllowPage = true; Assert.NotNull(fixture.Catalog.Describe("action:fixture.preview"));
            Assert.True(fixture.Authorization.PageChecks > 0);
        }
        [Fact]
        public async Task Actual_mvc_json_name_preserves_long_id_and_parent_before_generated_reader()
        {
            using var fixture = new Fixture(); var context = fixture.Context("Preview");
            await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", context, Encoding.UTF8.GetBytes("{\"recordKey\":9007199254740993,\"parentId\":\"901\"}"));
            Assert.Equal(9007199254740993L, fixture.Reader.Id); Assert.Equal("901", fixture.Reader.Parent);
            Assert.Equal("31", fixture.Reader.Button); Assert.Equal(fixture.Authorization.RequireActor(), fixture.Reader.Actor);
            Assert.Equal(1, fixture.Reader.Calls); Assert.Equal(0, fixture.Authorization.PageChecks);
        }
        [Theory]
        [InlineData("{}")]
        [InlineData("{\"recordKey\":0}")]
        [InlineData("{\"recordKey\":-1}")]
        [InlineData("{\"recordKey\":1,\"extra\":true}")]
        [InlineData("{\"recordKey\":1.1}")]
        [InlineData("null")]
        [InlineData("[]")]
        public async Task Invalid_dto_never_reaches_record_reader(string json)
        {
            using var fixture = new Fixture(); var error = await Assert.ThrowsAsync<AgentTaskException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", fixture.Context("Preview"), Encoding.UTF8.GetBytes(json)));
            Assert.Equal("YZP_INVALID_ACTION_REQUEST", error.Code); Assert.Equal(0, fixture.Reader.Calls);
        }
        [Fact]
        public async Task Scope_denial_endpoint_substitution_and_missing_service_fail_before_business()
        {
            using var fixture = new Fixture(); var body = Encoding.UTF8.GetBytes("{\"recordKey\":7}");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", fixture.Context("Complete"), body));
            Assert.Equal(0, fixture.Reader.Calls);
            fixture.Reader.Visible = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", fixture.Context("Preview"), body));
            fixture.Reader.Visible = true; fixture.Reader.Deny = true;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", fixture.Context("Preview"), body));
            var context = fixture.Context("Preview"); context.Request.Method = "GET";
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.RequireInvocation("action:fixture.preview", context));
        }
        [Fact]
        public async Task Body_and_query_record_bindings_require_a_unique_source_and_multiple_query_values_are_denied()
        {
            using var fixture = new Fixture();
            Assert.Throws<UnauthorizedAccessException>(() => fixture.Catalog.Describe("action:fixture.ambiguous"));
            var context = fixture.Context("Query"); context.Request.QueryString = new("?key=9007199254740993&parentId=901");
            await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.query", context, Encoding.UTF8.GetBytes("{}")); Assert.Equal(9007199254740993L, fixture.Reader.Id);
            context.Request.QueryString = new("?key=7&key=8"); var calls = fixture.Reader.Calls;
            await Assert.ThrowsAsync<AgentTaskException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.query", context, Encoding.UTF8.GetBytes("{}"))); Assert.Equal(calls, fixture.Reader.Calls);
        }
        [Fact]
        public async Task Missing_parent_and_changed_mvc_binding_name_fail_closed()
        {
            using var fixture = new Fixture("parent");
            await Assert.ThrowsAsync<AgentTaskException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", fixture.Context("Preview"), Encoding.UTF8.GetBytes("{\"recordKey\":7}")));
            Assert.Equal(0, fixture.Reader.Calls);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Missing_or_duplicate_service_registration_cannot_supply_record_proof(bool duplicate)
        {
            using var fixture = new Fixture(); var services = new ServiceCollection(); services.AddLogging(); services.AddControllers();
            if (duplicate) { services.AddSingleton(new ActionFixtureReader()); services.AddSingleton(new ActionFixtureReader()); }
            using var provider = services.BuildServiceProvider(); var context = fixture.Context("Preview"); context.RequestServices = provider;
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview", context, Encoding.UTF8.GetBytes("{\"recordKey\":7}")));
            Assert.Equal(0, fixture.Reader.Calls);
        }
        [Fact]
        public void Duplicate_registered_action_and_missing_generated_reader_are_not_discoverable()
        {
            using var fixture = new Fixture(); var actions = fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
            var preview = actions.OfType<ControllerActionDescriptor>().Single(value => value.ControllerTypeInfo == typeof(ActionFixtureController).GetTypeInfo() && value.ActionName == "Preview");
            var duplicate = new AgentCapabilityCatalog(new(new ActionSnapshot(actions.Concat([preview]).ToArray()), fixture.Resources), fixture.Authorization);
            Assert.Throws<UnauthorizedAccessException>(() => duplicate.Describe("action:fixture.preview"));
            var missing = new AgentCapabilityCatalog(new(new ActionSnapshot(actions.Where(value => value is not ControllerActionDescriptor controller || controller.ControllerTypeInfo.GetCustomAttribute<GeneratedPageAccessAttribute>() is null).ToArray()), fixture.Resources), fixture.Authorization);
            Assert.Throws<UnauthorizedAccessException>(() => missing.Describe("action:fixture.preview"));
        }
        /// <summary>只筛选/重复真实MVC发现结果，用于验证注册歧义，不能构造不存在的可调用方法。</summary>
        /// <summary>动作协议测试只替入租约决策；提供程序SQL和跨进程互斥由独立组件验收。</summary>
        private sealed class ExecutionGuard:Metarill.Core.Persistence.IBusinessButtonExecutionGuard
        {
            internal int Calls,Released;internal bool Busy;
            public async Task<T> ExecuteAsync<T>(string project,string button,Func<Task<T>> action,CancellationToken token=default)
            {Calls++;if(Busy)throw new BusinessRuleException("按钮正在执行中，请稍后再试");try{return await action();}finally{Released++;}}
        }
        private sealed class ActionSnapshot(IReadOnlyList<Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor> items) : IActionDescriptorCollectionProvider
        {
            public ActionDescriptorCollection ActionDescriptors { get; } = new(items, 1);
        }
        [Theory]
        [InlineData("Preview", "action:fixture.preview", false)]
        [InlineData("Complete", "action:fixture.complete", true)]
        public async Task Middleware_registers_every_action_before_business_and_finishes_real_task_store(string method, string capability, bool confirm)
        {
            using var memory = new PrincipalRealmFixture(); var options = new AgentTaskOptions(); var tasks = new AgentTaskStore(memory.Store, memory.Ids, options);
            using var fixture = new Fixture(tasks: tasks); var context = fixture.Context(method); var requestId = Guid.NewGuid().ToString("D");
            context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(new(fixture.Authorization.RequireActor(), new HashSet<string>(), 1, 2), "agent", 2);
            context.Request.Headers["X-Yzp-Agent-Capability"] = capability; context.Request.Headers["X-Yzp-Agent-Request-Id"] = requestId;
            if (confirm) context.Request.Headers["X-Yzp-Agent-Confirm"] = "true";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"recordKey\":7}")); context.Response.Body = new MemoryStream(); var calls = 0;
            var middleware = new AgentTaskMiddleware(async current =>
            {
                Assert.Equal("running", SqlRowStore.Text(Assert.Single(memory.Provider.Rows["sys_agent_task"]), "status")); calls++;
                await current.Response.WriteAsJsonAsync(R<object>.Success(new { id = "7" }), JsonDefaults.Options);
            });
            await middleware.InvokeAsync(context);
            Assert.Equal(1, calls); Assert.Equal(1, fixture.Reader.Calls); Assert.Equal(1,fixture.Guard.Calls);Assert.Equal(1,fixture.Guard.Released);
            var task = await tasks.GetAsync(fixture.Authorization.RequireActor(), requestId); Assert.Equal("succeeded", task.Status); Assert.Equal(capability, task.CapabilityId); Assert.Equal(7L, task.ResultId);
            // 内存传输只验证台账调用次序/结果，唯一索引和真实并发去重仍由实库套件验收。
        }
        /// <summary>按钮忙时不调用业务，但已登记动作仍保留unknown和先查任务规则，不能自动重发。</summary>
        [Fact]
        public async Task Busy_button_does_not_execute_action_or_reclassify_existing_task_protocol()
        {
            using var memory=new PrincipalRealmFixture();var tasks=new AgentTaskStore(memory.Store,memory.Ids,new());using var fixture=new Fixture(tasks:tasks);fixture.Guard.Busy=true;
            var context=fixture.Context("Preview");var requestId=Guid.NewGuid().ToString("D");
            context.Items[typeof(AgentRequestIdentity)]=new AgentRequestIdentity(new(fixture.Authorization.RequireActor(),new HashSet<string>(),1,2),"agent",2);
            context.Request.Headers["X-Yzp-Agent-Capability"]="action:fixture.preview";context.Request.Headers["X-Yzp-Agent-Request-Id"]=requestId;
            context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes("{\"recordKey\":7}"));context.Response.Body=new MemoryStream();var calls=0;
            await new AgentTaskMiddleware(_=>{calls++;return Task.CompletedTask;}).InvokeAsync(context);
            Assert.Equal(0,calls);Assert.Equal(1,fixture.Guard.Calls);Assert.Equal(0,fixture.Guard.Released);
            Assert.Single(memory.Provider.Rows["sys_agent_task"]);var task=await tasks.GetAsync(fixture.Authorization.RequireActor(),requestId);Assert.Equal("unknown",task.Outcome);Assert.False(task.Retryable);
            context.Response.Body.Position=0;using var body=await JsonDocument.ParseAsync(context.Response.Body);var error=body.RootElement.GetProperty("map").GetProperty("agentError");
            Assert.Equal(503,context.Response.StatusCode);Assert.Equal("YZP_OUTCOME_UNKNOWN",error.GetProperty("code").GetString());Assert.Equal("query_task",error.GetProperty("recovery").GetString());Assert.False(error.GetProperty("retryable").GetBoolean());
        }
        [Theory]
        [InlineData("Preview", "action:fixture.preview", false, false, 400)]
        [InlineData("Complete", "action:fixture.complete", true, false, 400)]
        [InlineData("Preview", "action:fixture.preview", true, true, 403)]
        public async Task Middleware_rejects_missing_headers_or_invisible_record_before_ledger_and_business(string method, string capability, bool requestId, bool denied, int expected)
        {
            using var memory = new PrincipalRealmFixture(); var tasks = new AgentTaskStore(memory.Store, memory.Ids, new());
            using var fixture = new Fixture(tasks: tasks); var context = fixture.Context(method); fixture.Reader.Visible = !denied;
            context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(new(fixture.Authorization.RequireActor(), new HashSet<string>(), 1, 2), "agent", 2);
            context.Request.Headers["X-Yzp-Agent-Capability"] = capability;
            if (requestId) context.Request.Headers["X-Yzp-Agent-Request-Id"] = Guid.NewGuid().ToString("D");
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"recordKey\":7}")); context.Response.Body = new MemoryStream(); var calls = 0;
            await new AgentTaskMiddleware(_ => { calls++; return Task.CompletedTask; }).InvokeAsync(context);
            Assert.Equal(expected, context.Response.StatusCode); Assert.Equal(0, calls); Assert.Empty(memory.Provider.Rows.GetValueOrDefault("sys_agent_task") ?? []);
        }
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task Explicit_conflict_keeps_unknown_task_and_generic_failure_or_missing_ledger_does_not_claim_conflict(bool conflict, bool ledgerAvailable)
        {
            using var memory = new PrincipalRealmFixture(); var tasks = new AgentTaskStore(memory.Store, memory.Ids, new());
            using var fixture = new Fixture(tasks: tasks); var context = fixture.Context("Complete"); var requestId = Guid.NewGuid().ToString("D");
            context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(new(fixture.Authorization.RequireActor(), new HashSet<string>(), 1, 2), "agent", 2);
            context.Request.Headers["X-Yzp-Agent-Capability"] = "action:fixture.complete"; context.Request.Headers["X-Yzp-Agent-Request-Id"] = requestId; context.Request.Headers["X-Yzp-Agent-Confirm"] = "true";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"recordKey\":7}")); context.Response.Body = new MemoryStream();
            await new AgentTaskMiddleware(_ =>
            {
                if (!ledgerAvailable) memory.Provider.Rows["sys_agent_task"].Clear();
                if (conflict) throw new AgentActionConflictException("合同在预览后已变化，请重新查询和预览");
                throw new InvalidOperationException("SQL口令与内部路径不得回显");
            }).InvokeAsync(context);
            context.Response.Body.Position = 0; using var json = await JsonDocument.ParseAsync(context.Response.Body); var error = json.RootElement.GetProperty("map").GetProperty("agentError");
            Assert.Equal(conflict && ledgerAvailable ? 409 : 503, context.Response.StatusCode); Assert.Equal(conflict && ledgerAvailable ? "YZP_CONFLICT" : "YZP_OUTCOME_UNKNOWN", error.GetProperty("code").GetString());
            Assert.Equal(requestId, error.GetProperty("requestId").GetString()); Assert.Equal("unknown", error.GetProperty("outcome").GetString()); Assert.Equal("query_task", error.GetProperty("recovery").GetString()); Assert.False(error.GetProperty("retryable").GetBoolean());
            Assert.DoesNotContain("SQL口令", json.RootElement.ToString());
            if (ledgerAvailable) { var task = await tasks.GetAsync(fixture.Authorization.RequireActor(), requestId); Assert.Equal("unknown", task.Status); Assert.Equal("unknown", task.Outcome); }
        }
        [Fact]
        public async Task Marked_action_conflict_does_not_reclassify_existing_standard_write_failure()
        {
            using var memory = new PrincipalRealmFixture(); var tasks = new AgentTaskStore(memory.Store, memory.Ids, new()); using var fixture = new Fixture(tasks: tasks); fixture.Authorization.AllowPage = true;
            var action = fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(value => value.ControllerTypeInfo == typeof(Generated.Controllers.ActionFixtureGeneratedController).GetTypeInfo() && value.ActionName == "Create");
            var context = new DefaultHttpContext { RequestServices = fixture.Services }; context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(action), "Create")); context.Request.Method = "POST"; context.Request.ContentType = "application/json"; context.Request.Path = "/api/admin/auto/action_fixture/21/add";
            var requestId = Guid.NewGuid().ToString("D"); context.Items[typeof(AgentRequestIdentity)] = new AgentRequestIdentity(new(fixture.Authorization.RequireActor(), new HashSet<string>(), 1, 2), "agent", 2);
            context.Request.Headers["X-Yzp-Agent-Capability"] = "page:21:create"; context.Request.Headers["X-Yzp-Agent-Request-Id"] = requestId; context.Request.Headers["X-Yzp-Agent-Confirm"] = "true";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}")); context.Response.Body = new MemoryStream();
            await new AgentTaskMiddleware(_ => throw new AgentActionConflictException("普通入口不展示此分类")).InvokeAsync(context);
            Assert.Equal(503, context.Response.StatusCode); context.Response.Body.Position = 0; using var json = await JsonDocument.ParseAsync(context.Response.Body);
            Assert.Equal("YZP_OUTCOME_UNKNOWN", json.RootElement.GetProperty("map").GetProperty("agentError").GetProperty("code").GetString()); Assert.DoesNotContain("普通入口不展示此分类", json.RootElement.ToString());
            Assert.Equal("unknown", (await tasks.GetAsync(fixture.Authorization.RequireActor(), requestId)).Outcome);
        }
        /// <summary>两个调用通道共用真实记录结果，原显示规则不采信客户端实体或JSON覆盖。</summary>
        [Theory][InlineData("real_name")][InlineData("realName")][InlineData("HTTP_status")][InlineData("hTTPStatus")]
        public async Task Published_visibility_uses_generated_record_for_agent_and_original_web(string field)
        {
            var rule=JsonSerializer.Serialize(new{conditions=new[]{new{field,value="允许"}}});using var fixture=new Fixture(rule:rule);
            fixture.Reader.Row.RealName="拒绝";var context=fixture.Context("Preview");
            await Assert.ThrowsAsync<BusinessRuleException>(()=>fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",context,Encoding.UTF8.GetBytes("{\"recordKey\":7}")));
            var handler=new WebHandler();var executor=new OriginalBusinessButtonExecutor(fixture.Resources,fixture.Authorization,fixture.Services,fixture.Services.GetRequiredService<IActionDescriptorCollectionProvider>(),new[]{handler},fixture.Guard);
            var request=new OriginalBusinessButtonRequest("31","7",JsonSerializer.SerializeToElement(new{id="7",realName="允许"}),"AlwaysVisible","{}");
            var failure=await Assert.ThrowsAsync<BusinessRuleException>(()=>executor.ExecuteAsync(request,context));Assert.Equal("当前记录不允许执行该按钮",failure.Message);
            Assert.Equal(0,handler.Calls);Assert.Equal(0,fixture.Guard.Calls);
            // 发布后元数据变化不能改写已编译规则或物理字段映射；实际行的新值仍应逐次读取。
            fixture.Metadata.ConfigButtons.Clear();fixture.Metadata.ConfigColumns[0].ColumnName="Unpublished";
            fixture.Reader.Row.RealName="允许";await fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",context,Encoding.UTF8.GetBytes("{\"recordKey\":7}"));
            await executor.ExecuteAsync(request with{Entity=JsonSerializer.SerializeToElement(new{id="7",realName="拒绝"}),VisibleRuleJson="{\"conditions\":[{\"value\":\"拒绝\"}]}"},context);
            Assert.Equal(1,handler.Calls);Assert.Equal(1,fixture.Guard.Calls);Assert.Equal(1,fixture.Guard.Released);
        }
        [Fact]
        public async Task Unloaded_derived_field_cannot_masquerade_as_an_actual_null_value()
        {
            using var fixture=new Fixture(rule:"{\"conditions\":[{\"field\":\"computedName\",\"operator\":\"isBlank\"}]}");
            await Assert.ThrowsAsync<NotSupportedException>(()=>fixture.Catalog.ValidateActionInvocationAsync("action:fixture.preview",fixture.Context("Preview"),Encoding.UTF8.GetBytes("{\"recordKey\":7}")));
            Assert.Equal(0,fixture.Guard.Calls);
        }
        [Theory][InlineData("{")][InlineData("{\"conditions\":[{\"operator\":\"script\"}]}")][InlineData("{\"conditions\":[],\"conditions\":[]}")]
        public void Invalid_visibility_does_not_publish_an_always_allowed_action(string rule)
        {
            using var fixture=new Fixture(rule:rule);Assert.Null(fixture.Resources.AgentCatalog.FindButton("31"));
            Assert.Throws<UnauthorizedAccessException>(()=>fixture.Catalog.Describe("action:fixture.preview"));fixture.Catalog.List();
        }
        /// <summary>范围守卫之外的Web身份只用于离线协议合同，不充当真实登录验收。</summary>
        private sealed class WebIdentity(RuntimeActor actor):ISecurityIdentityAccessor
        {public AuthenticatedIdentity RequireIdentity()=>new(actor,new HashSet<string>(),1,null){PrincipalProjectId=actor.ProjectId,PrincipalAdminTypeId=actor.AdminTypeId};}
        private sealed class WebHandler:IOriginalBusinessButtonHandler
        {
            internal int Calls;public string ButtonId=>"31";public string Path=>"/admin/custom/agent-fixture/preview";
            public Task<IActionResult> ExecuteAsync(OriginalBusinessButtonRequest request,CancellationToken cancellationToken){Calls++;return Task.FromResult<IActionResult>(new OkResult());}
        }
        private sealed class Fixture : IDisposable
        {
            internal readonly ExecutionGuard Guard=new();
            internal readonly MetadataCatalog Metadata;
            internal readonly Resources Resources;
            internal readonly Authorization Authorization = new();
            internal readonly ActionFixtureReader Reader = new();
            internal readonly ServiceProvider Services;
            internal readonly AgentCapabilityCatalog Catalog;
            private readonly IActionDescriptorCollectionProvider actions;
            internal Fixture(string variant = "", AgentTaskStore? tasks = null,string? rule=null,string? readOnly=null)
            {
                Metadata = new() { YzProjectId = "100", ConfigTables = [new() { Id = "11", TableName = "action_fixture", YzProjectId = variant == "table-foreign" ? "200" : "100" }],
                    ConfigPages = [new() { Id = "21", Name = "合同", ConfigTableId = "11", YzProjectId = variant == "page-foreign" ? "200" : "100", WhereSql = variant == "parent" ? "parent_id=@parentId" : "" }] };
                Metadata.ConfigColumns.Add(new(){Id="12",ConfigTableId="11",ColumnName="real_name",Name="名称",YzProjectId="100"});
                Metadata.ConfigColumns.Add(new(){Id="15",ConfigTableId="11",ColumnName="HTTP_status",Name="大小写字段",YzProjectId="100"});
                Metadata.ConfigColumns.Add(new(){Id="14",ConfigTableId="11",ColumnName="computed_name",Name="派生名称",YzProjectId="100"});
                Metadata.ConfigColumns.Add(new(){Id="13",ConfigTableId="11",ColumnName="amount",Name="金额",LogicalType="decimal",YzProjectId="100"});
                if (variant == "unsupported-readonly-expression") Metadata.ConfigPages[0].Extra["readOnlyCondition"] = JsonSerializer.SerializeToElement("amount.toFixed(2)");
                if(readOnly is not null)Metadata.ConfigPages[0].Extra["readOnlyCondition"]=JsonSerializer.SerializeToElement(readOnly);
                foreach (var (id, method, confirm) in new[] { ("31", "preview", 0), ("32", "complete", 1), ("33", "query", 0), ("34", "raw", 0), ("35", "ambiguous", 0) })
                    Metadata.ConfigButtons.Add(JsonSerializer.SerializeToElement(new { id, configPageId = "21", configTableId = variant == "wrong-table" ? "12" : "11", yzProjectId = variant == "wrong-project" ? "200" : "100",
                        actionType = variant == "not-business" ? 0 : 1, sql = "/admin/custom/agent-fixture/" + (variant == "wrong-path" ? "missing" : method), isNeedConfirm = confirm, confirmMsg = "确认处理？",
                        visibleRuleJson=rule??"", visibleScheme = variant == "unknown-visibility" ? "OnlyDeve" : variant is "writable" or "unsupported-readonly-expression" ? "VisibleWhenWritable" : "Normal" }));
                if (variant == "duplicate-button") Metadata.ConfigButtons.Add(Metadata.ConfigButtons[0]);
                Resources = new(Metadata);
                var services = new ServiceCollection(); services.AddLogging(); services.AddControllers().AddApplicationPart(typeof(ActionFixtureController).Assembly);
                services.AddSingleton<ISecurityIdentityAccessor>(new WebIdentity(Authorization.RequireActor()));
                services.AddSingleton(Reader);
                services.AddSingleton<Metarill.Core.Persistence.IBusinessButtonExecutionGuard>(Guard);
                services.AddSingleton<IRuntimeAuthorization>(Authorization); services.AddSingleton<IAgentHostResources>(Resources); services.AddSingleton<GeneratedCapabilityIndex>(); services.AddSingleton<AgentCapabilityCatalog>();
                if (tasks is not null) { services.AddSingleton(tasks); services.AddSingleton(new AgentTaskOptions()); }
                Services = services.BuildServiceProvider(); actions = Services.GetRequiredService<IActionDescriptorCollectionProvider>();
                Catalog = Services.GetRequiredService<AgentCapabilityCatalog>();
            }
            internal DefaultHttpContext Context(string method)
            {
                var action = actions.ActionDescriptors.Items.OfType<ControllerActionDescriptor>().Single(value => value.ControllerTypeInfo == typeof(ActionFixtureController).GetTypeInfo() && value.ActionName == method);
                var context = new DefaultHttpContext { RequestServices = Services }; context.Request.Method = "POST"; context.Request.ContentType = "application/json";
                context.Request.Path = "/" + action.AttributeRouteInfo!.Template; context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(action), method));
                return context;
            }
            public void Dispose() => Services.Dispose();
        }
        private sealed class Resources(MetadataCatalog source) : IAgentHostResources
        {
            public AgentHostCatalog AgentCatalog { get; } = AgentHostCatalog.ForApplication(source);
            public IReadOnlyList<SecurityResourceCatalog> PublishedCatalogs => [];
            public bool IsPlatformRealm(string project, string admin) => false;
        }
        private sealed class Authorization : IRuntimeAuthorization
        {
            internal bool AllowButton = true, AllowPage; internal int PageChecks, ButtonChecks;
            public RuntimeActor RequireActor() => new(8, "固定主体", false, "100", "91");
            public void RequirePagePermission(string page, string operation) { PageChecks++; if (!AllowPage) throw new SecurityException("普通页权限关闭"); }
            public void RequireButtonPermission(string page, string button) { ButtonChecks++; if (!AllowButton) throw new SecurityException("按钮撤销"); }
        }
    }
    /// <summary>目录/绑定测试只替入记录结果，实际生成SQL由生成模块现有用例验证。</summary>
    public sealed class ActionFixtureReader
    {
        internal readonly ActionFixtureEntity Row=new();
        internal long Id; internal string? Parent, Button; internal RuntimeActor? Actor; internal int Calls; internal bool Visible = true, Deny;
        public Task<ActionFixtureEntity?> ReadButtonRecord21Async(long id, string? parent, string button, RuntimeActor actor, CancellationToken cancellationToken)
        { Calls++; Id = id; Parent = parent; Button = button; Actor = actor; if (Deny) throw new SecurityException("真实记录拒绝"); return Task.FromResult(Visible ? Row : null); }
    }
    public sealed class ActionFixtureEntity
    {
        [JsonPropertyName("id")]public long Id{get;set;}=7;
        [JsonPropertyName("realName")][FreeSql.DataAnnotations.Column(Name="real_name")]public string? RealName{get;set;}="允许";
        [JsonPropertyName("amount")]public decimal Amount{get;set;}=10m;
        [JsonPropertyName("computedName")][FreeSql.DataAnnotations.Column(Name="computed_name",IsIgnore=true)]public string? ComputedName{get;set;}
        [JsonPropertyName("hTTPStatus")][FreeSql.DataAnnotations.Column(Name="HTTP_status")]public string? HTTPStatus{get=>RealName;set=>RealName=value;}
        public string Unpublished=>throw new InvalidOperationException("规则不得读取未发布属性。");
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ActionFixtureRequest { [JsonPropertyName("recordKey")] public long Id { get; set; } public string? ParentId { get; set; } }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class ActionQueryRequest { public string? Notes { get; set; } }
    [ApiController, Route("api/admin/custom/agent-fixture")]
    public sealed class ActionFixtureController : ControllerBase
    {
        [HttpPost("preview"), AgentAction("fixture.preview", "预览合同", "31", RecordIdField = "recordKey")]
        public R<string> Preview([FromBody] ActionFixtureRequest request) => R<string>.Success();
        [HttpPost("complete"), AgentAction("fixture.complete", "处理合同", "32", RecordIdField = "recordKey")]
        public R<string> Complete([FromBody] ActionFixtureRequest request) => R<string>.Success();
        [HttpPost("query"), AgentAction("fixture.query", "查询绑定", "33", RecordIdField = "key")]
        public R<string> Query([FromBody] ActionQueryRequest request, [FromQuery(Name = "key")] string record, [FromQuery] string? parentId) => R<string>.Success();
        [HttpPost("raw"), AgentAction("fixture.raw", "开放正文应拒绝", "34")]
        public R<string> Raw([FromBody] JsonElement request) => R<string>.Success();
        [HttpPost("ambiguous"), AgentAction("fixture.ambiguous", "歧义应拒绝", "35", RecordIdField = "recordKey")]
        public R<string> Ambiguous([FromBody] ActionFixtureRequest request, [FromQuery] string recordKey) => R<string>.Success();
        [HttpPost("unmarked")]
        public R<string> Unmarked([FromBody] ActionFixtureRequest request) => R<string>.Success();
    }
}
namespace Metarill.Agent.Tests.Generated.Controllers
{
    [ApiController, Route("api/admin/auto/action_fixture/21"), GeneratedPageAccess("21", "11", typeof(Metarill.Agent.Tests.ActionFixtureEntity), typeof(Metarill.Agent.Tests.ActionFixtureReader), nameof(Metarill.Agent.Tests.ActionFixtureReader.ReadButtonRecord21Async))]
    public sealed class ActionFixtureGeneratedController(Metarill.Agent.Tests.ActionFixtureReader reader) : ControllerBase
    {
        [HttpGet("detail/{id}")]
        public R<string> Detail(long id) { _ = reader; return R<string>.Success(); }
        /// <summary>真实注册的普通写入口只供中间件兼容测试，业务行为由测试下一层明确替入。</summary>
        [HttpPost("add")]
        public R<string> Create([FromBody] ActionQueryRequest request) => R<string>.Success();
    }
}
