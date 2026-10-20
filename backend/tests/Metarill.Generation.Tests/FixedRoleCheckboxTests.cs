using System.Diagnostics;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定角色复选保留原页面合同；本组只有生成/本机Vue，不访问数据库。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class FixedRoleCheckboxTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("null",null)] [InlineData("[]","[]")] [InlineData("[\"9007199254740993\",2]","[\"9007199254740993\",2]")]
    [InlineData("\"[\\\"9007199254740993\\\",2]\"","[\"9007199254740993\",2]")]
    public void Original_array_encoded_array_and_null_keep_storage_and_order(string raw,string? expected)
    {
        var encoded=RoleCheckboxValue.ReadWrite(Parse(raw));Assert.Equal(expected,encoded);
        var ids=RoleCheckboxValue.ProjectIds(encoded);
        var names=RoleCheckboxValue.ProjectNames(encoded,new Dictionary<long,string?>{{2,"第二角色"}});
        if(expected is null){Assert.Null(ids);Assert.Null(names);return;}
        if(expected=="[]"){Assert.Empty(ids!.Value.EnumerateArray());Assert.Empty(names!.Value.EnumerateArray());return;}
        Assert.Equal(new[]{"9007199254740993","2"},ids!.Value.EnumerateArray().Select(item=>item.GetString()));
        Assert.Equal(new string?[]{null,"第二角色"},names!.Value.EnumerateArray().Select(item=>item.ValueKind==JsonValueKind.Null?null:item.GetString()));
    }

    [Theory]
    [InlineData("\"null\"")] [InlineData("\"\"")] [InlineData("{}")] [InlineData("true")] [InlineData("2")]
    [InlineData("[1.0]")] [InlineData("[1e2]")] [InlineData("[9223372036854775808]")] [InlineData("[\"001\"]")]
    [InlineData("[null]")] [InlineData("[-1]")] [InlineData("[false]")]
    public void Invalid_or_inexact_role_values_are_rejected(string text)=>Assert.Throws<ArgumentException>(()=>RoleCheckboxValue.ReadWrite(Parse(text)));

    [Fact]
    public void Storage_length_is_checked_on_the_final_text_and_duplicates_preserve_positions()
    {
        var atLimit="[\"1\"]"+new string(' ',495);Assert.Equal(500,atLimit.Length);Assert.Equal(atLimit,RoleCheckboxValue.ReadWrite(JsonSerializer.SerializeToElement(atLimit)));
        Assert.Throws<ArgumentException>(()=>RoleCheckboxValue.ReadWrite(JsonSerializer.SerializeToElement(atLimit+" ")));
        Assert.Equal(new[]{"一","一"},RoleCheckboxValue.ProjectNames("[1,\"1\"]",new Dictionary<long,string?>{{1,"一"}})!.Value.EnumerateArray().Select(item=>item.GetString()));
        var omitted=JsonSerializer.Deserialize<Presence>("{}",Json)!;var explicitNull=JsonSerializer.Deserialize<Presence>("{\"yzRoleIds\":null}",Json)!;
        Assert.False(omitted.YzRoleIds.IsSpecified);Assert.True(explicitNull.YzRoleIds.IsSpecified);Assert.Null(RoleCheckboxValue.ReadWrite(explicitNull.YzRoleIds.Value));
    }

    /// <summary>5000个唯一ID最多25个串行批次，重复行不扩大预算；超量不是静默丢弃。</summary>
    [Fact]
    public void Projection_collects_once_deduplicates_and_rejects_over_budget_before_queries()
    {
        var rows=Enumerable.Range(1,5000).Chunk(20).Select(values=>JsonSerializer.Serialize(values)).ToArray();
        var ids=RoleCheckboxValue.ProjectionIds(rows.Concat(rows));Assert.Equal(5000,ids.Length);Assert.Equal(25,ids.Chunk(RoleCheckboxValue.ProjectionBatchSize).Count());
        Assert.Throws<ArgumentException>(()=>RoleCheckboxValue.ProjectionIds(rows.Append("[5001]")));
        Assert.Empty(RoleCheckboxValue.ProjectionIds([null,"[]"]));
    }

    [Theory]
    [InlineData("{\"sql\":\"select 1\"}")] [InlineData("{\"projectId\":\"1\"}")] [InlineData("{\"parentId\":\"1\"}")]
    [InlineData("{\"formParameters\":[]}")] [InlineData("{\"id\":null,\"id\":[]}")]
    public void Candidate_request_rejects_unknown_scope_and_duplicate_fields(string text)=>Assert.Throws<ArgumentException>(()=>RoleCheckboxValue.ReadCandidateRequest(Parse(text)));

    [Fact]
    public void Form_parameters_are_ignored_and_readonly_empty_never_becomes_all()
    {
        Assert.Null(RoleCheckboxValue.ReadCandidateRequest(Parse("{\"formParameters\":{\"yzManager\":{\"yzAdminTypeId\":\"999\"},\"sql\":\"not executed\"}}")));
        Assert.Empty(RoleCheckboxValue.ReadCandidateRequest(Parse("{\"id\":null}"))!);
        Assert.Empty(RoleCheckboxValue.ReadCandidateRequest(Parse("{\"id\":[]}"))!);
    }

    [Theory]
    [InlineData("list",false,false)] [InlineData("create",true,false)] [InlineData("update",false,true)]
    public void Original_option_permission_accepts_any_open_query_create_update(string allowed,bool create,bool update)
    {
        var auth=new Authorization(allowed);Assert.Equal(auth.Actor,RoleCheckboxAuthorization.Require(auth,"p",create,update));
    }

    [Fact]
    public void Disabled_actions_and_non_authorization_errors_are_not_swallowed()
    {
        Assert.Throws<UnauthorizedAccessException>(()=>RoleCheckboxAuthorization.Require(new Authorization("create"),"p",false,false));
        Assert.Throws<UnauthorizedAccessException>(()=>RoleCheckboxAuthorization.Require(new Authorization("none"),"p",true,true));
        Assert.Throws<InvalidOperationException>(()=>RoleCheckboxAuthorization.Require(new Authorization("error"),"p",true,true));
    }

    /// <summary>每个非法证明必须同时保留IR和发布缺口；未完成A协调的有效声明也不能导出Full。</summary>
    [Theory]
    [InlineData("sql")] [InlineData("overlay")] [InlineData("link")] [InlineData("join")] [InlineData("logical")] [InlineData("alias")] [InlineData("owner")] [InlineData("page")] [InlineData("virtual")] [InlineData("overlay-virtual")] [InlineData("target-scope")] [InlineData("join-table")]
    public void Incomplete_fixed_proofs_never_enable_the_role_control(string kind)
    {
        var catalog=Catalog();var source=catalog.ConfigColumns.Single(item=>item.Id==FixedRoleCheckboxCompilation.RoleColumn);
        if(kind=="sql")source.ControlDetail="yz_role;name;1=1;name";
        if(kind=="overlay")catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId==source.Id).Extra["controlDetail"]=JsonSerializer.SerializeToElement("other");
        if(kind=="link")source.LinkConfigTableId=FixedRoleCheckboxCompilation.AdminTable;
        if(kind=="join")source.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);
        if(kind=="logical")source.LogicalType="int64";
        if(kind=="alias")catalog.ConfigColumns.Add(new(){Id="alias-conflict",ConfigTableId=source.ConfigTableId,ColumnName="yz_role_names",LogicalType="string"});
        if(kind=="owner")catalog.ConfigColumns.Single(item=>item.Id=="1692533179766878209").LogicalType="string";
        if(kind=="page")catalog.ConfigPages.RemoveAll(item=>item.Id==FixedRoleCheckboxCompilation.RolePage);
        if(kind=="virtual")source.IsVirtualColumn=1;
        if(kind=="overlay-virtual"){source.IsVirtualColumn=1;source.ControlKind="";catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId==source.Id).Extra["controlKind"]=JsonSerializer.SerializeToElement("MultiCheckBoxDatabase");}
        if(kind=="target-scope")catalog.ConfigPages.Single(item=>item.Id==FixedRoleCheckboxCompilation.RolePage).WhereSql="yz_project_id=@current_yz_project_id";
        if(kind=="join-table")source.Extra["joinTable"]=JsonSerializer.SerializeToElement("yz_admin_type");
        var model=GenerationModel.Create(catalog,"Example.RoleGap");var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=table.Pages.Single();
        Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Column=="yz_role_ids");
        Assert.False(ReadProjectionCompilation.ForPage(model,table,page).Detail.CanCompile);
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
    }

    /// <summary>原角色导出为ID数组文本；已证明用途不再误报，关闭与隐藏列仍按元数据保持。</summary>
    [Theory]
    [InlineData("disabled",false)] [InlineData("flag-list",true)] [InlineData("method-list",true)] [InlineData("method-edit",true)]
    [InlineData("list-without-role",false)] [InlineData("edit-without-role",false)] [InlineData("hidden-all",false)] [InlineData("flag-unknown-mode",true)]
    public void Original_role_id_export_keeps_its_column_switch_without_a_false_name_gap(string kind,bool expected)
    {
        var catalog=Catalog();var source=catalog.ConfigPages.Single(page=>page.Id==FixedRoleCheckboxCompilation.ManagerPage);
        source.IsExport=kind is "flag-list" or "flag-unknown-mode" or "hidden-all"?1:0;
        source.Extra["exportMethod"]=JsonSerializer.SerializeToElement(kind is "method-edit" or "edit-without-role"?"edit":kind is "method-list" or "list-without-role"?"list":kind=="flag-unknown-mode"?"unknown":"");
        var field=catalog.ConfigPageColumns.Single(item=>item.ConfigColumnId==FixedRoleCheckboxCompilation.RoleColumn);
        if(kind=="list-without-role"){field.IsShow=0;field.Extra["isInGrid"]=JsonSerializer.SerializeToElement(0);}
        if(kind=="edit-without-role"){field.IsEdit=0;field.Extra["isInEdit"]=JsonSerializer.SerializeToElement(0);}
        if(kind=="hidden-all")catalog.ConfigPageColumns.Remove(field);
        var model=GenerationModel.Create(catalog,"Example.RoleExportGap");var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=table.Pages.Single();var role=FixedRoleCheckboxCompilation.ForService(model,table,page)!;
        Assert.Equal(expected,ExportRenderer.IncludesColumn(page,role.Source));
        var gaps=GenerationCapabilityAudit.Collect(model).Where(gap=>gap.PageId==page.Source.Id&&gap.Column=="yz_role_ids").ToArray();
        Assert.Single(gaps,gap=>gap.Usage=="runtime");Assert.DoesNotContain(gaps,gap=>gap.Usage=="export");
        Assert.Single(gaps);
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
    }

    /// <summary>实际生成编译、反射DTO/VO/旧请求与原Vue组件，不用字符串断言代替完整程序集。</summary>
    [Fact]
    public async Task Real_generated_code_and_original_checkbox_keep_the_complete_contract()
    {
        var catalog=Catalog();var model=GenerationModel.Create(catalog,"Example.RoleCheckbox");var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=table.Pages.Single();
        foreach(var plan in new[]{ReadProjectionCompilation.ForPage(model,table,page).List,ReadProjectionCompilation.ForPage(model,table,page).Detail})
        {Assert.True(plan.CanCompile,string.Join(";",plan.Gaps.Select(gap=>gap.Reason)));Assert.Contains(plan.OutputFields,item=>item.JsonName=="yzRoleNames"&&item.Kind==ReadProjectionOutputKind.RoleNames);Assert.DoesNotContain(plan.PhysicalColumns,item=>item.Source.ColumnName=="yz_role_names");}
        Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Usage=="runtime"&&gap.Reason.Contains("Runtime A"));
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-role-checkbox");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var generated=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,generated,"Example.RoleCheckbox");
            Assert.Contains("relationDisplayText(item.yzRoleNames)",File.ReadAllText(Path.Combine(generated,"frontend/pages/yzp/yz_manager-list-6031906822946816.vue")));
            var service=File.ReadAllText(Path.Combine(generated,"Services/YzManagerService.g.cs"));
            Assert.Contains("admin.Id == role.YzAdminTypeId && admin.DeleteNo == null && admin.YzProjectId == project",service);Assert.DoesNotContain("role.YzProjectId ==",service);Assert.DoesNotContain("role.IsSuper",service);Assert.Contains("Limit(5001)",service);
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);
            var entityType=assembly.GetType("Example.RoleCheckbox.Generated.Entities.YzManager",true)!;var entity=Activator.CreateInstance(entityType)!;entityType.GetProperty("YzRoleIds")!.SetValue(entity,"[9007199254740993,\"2\"]");
            var voType=assembly.GetType("Example.RoleCheckbox.Generated.VOs.YzManagerPage6031906822946816Vo",true)!;
            var names=RoleCheckboxValue.ProjectNames("[9007199254740993,\"2\"]",new Dictionary<long,string?>{{2,"角色乙"}});
            var vo=voType.GetMethod("FromEntity",[entityType,typeof(JsonElement?)])!.Invoke(null,[entity,names]);
            var value=JsonSerializer.SerializeToElement(vo,voType,Json);Assert.Equal(new[]{"9007199254740993","2"},value.GetProperty("yzRoleIds").EnumerateArray().Select(item=>item.GetString()));Assert.Equal(names!.Value.GetRawText(),value.GetProperty("yzRoleNames").GetRawText());
            var controller=assembly.GetType("Example.RoleCheckbox.Generated.Controllers.YzManagerPage6031906822946816Controller",true)!;
            Assert.NotNull(controller.GetMethod("findNameIdYzRole1692528723801219073"));
            var legacy=(LegacyPageWriteContract)controller.GetField("LegacyWrite",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            var payload=legacy.Normalize(Parse("{\"id\":\"1\",\"yzRoleIds\":[],\"yzRoleNames\":[\"伪名称\"]}"),LegacyPageWriteOperation.Update,catalog.YzProjectId,null);
            Assert.False(payload.TryGetProperty("yzRoleNames",out _));Assert.Empty(payload.GetProperty("yzRoleIds").EnumerateArray());
            Assert.Throws<ArgumentException>(()=>legacy.Normalize(Parse("{\"id\":\"1\",\"yzRoleName\":\"错误别名\"}"),LegacyPageWriteOperation.Update,catalog.YzProjectId,null));
            var dto=assembly.GetType("Example.RoleCheckbox.Generated.DTOs.YzManagerPage6031906822946816UpdateRequest",true)!;
            Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize("{\"id\":1,\"yzRoleNames\":[]}",dto,Json));
            await CheckNoDatabaseContract(assembly);
            await RunVue(Path.Combine(generated,"frontend/pages/yzp/yz_manager-detail-6031906822946816.vue"));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    /// <summary>隐藏角色ID页仍能编译普通保存；内部校验依赖不变成公开候选、DTO/VO或发布完成。</summary>
    [Fact]
    public async Task Hidden_role_ids_keep_internal_helper_registration_and_runtime_gap_without_exposing_fields()
    {
        var catalog=Catalog();catalog.ConfigPageColumns.RemoveAll(field=>field.ConfigPageId==FixedRoleCheckboxCompilation.ManagerPage&&field.ConfigColumnId==FixedRoleCheckboxCompilation.RoleColumn);
        var model=GenerationModel.Create(catalog,"Example.HiddenRole");var table=model.Tables.Single(item=>item.Source.Id==FixedRoleCheckboxCompilation.ManagerTable);var page=table.Pages.Single();
        Assert.Null(FixedRoleCheckboxCompilation.ForPage(model,table,page));Assert.NotNull(FixedRoleCheckboxCompilation.ForService(model,table,page));
        Assert.Contains(GenerationCapabilityAudit.Collect(model),gap=>gap.Column=="yz_role_ids"&&gap.Usage=="runtime");Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(catalog));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-hidden-role-checkbox");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var output=Path.Combine(directory,"generated");new CSharpGenerator().Generate(catalog,output,"Example.HiddenRole");
            var service=File.ReadAllText(Path.Combine(output,"Services/YzManagerService.g.cs"));
            Assert.Contains("RoleOptionsScope6031906822946816",service);Assert.Contains("RoleOptionsScope6031906822946816(actor).Where",service);Assert.DoesNotContain("RoleOptions6031906822946816Async",service);Assert.DoesNotContain("ProjectRoleNames6031906822946816Async",service);
            var controller=File.ReadAllText(Path.Combine(output,"Controllers/YzManagerPage6031906822946816Controller.g.cs"));Assert.DoesNotContain("findNameIdYzRole1692528723801219073",controller);Assert.DoesNotContain("yzRoleNames",controller);
            foreach(var path in new[]{"DTOs/YzManagerPage6031906822946816Requests.g.cs","VOs/YzManagerPage6031906822946816Vo.g.cs"}){var text=File.ReadAllText(Path.Combine(output,path));Assert.DoesNotContain("YzRoleIds {",text);Assert.DoesNotContain("YzRoleNames {",text);}
            var assembly=await GeneratedRelationOptionsMySqlTests.BuildGenerated(directory);var request=assembly.GetType("Example.HiddenRole.Generated.DTOs.YzManagerPage6031906822946816UpdateRequest",true)!;
            Assert.NotNull(request.GetProperty("Name"));Assert.Null(request.GetProperty("YzRoleIds"));Assert.NotNull(assembly.GetType("Example.HiddenRole.Generated.Services.YzManagerService",true)!.GetMethod("Update6031906822946816Async"));
            Assert.Contains("provider.GetRequiredService<IRuntimeAuthorization>()",File.ReadAllText(Path.Combine(output,"GeneratedRegistration.g.cs")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }

    internal static MetadataCatalog Catalog()
    {
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(SourceRoot(),"backend/metadata/platform.json")),Json)!;
        var tables=new[]{FixedRoleCheckboxCompilation.ManagerTable,FixedRoleCheckboxCompilation.RoleTable,FixedRoleCheckboxCompilation.AdminTable};
        catalog.ConfigTables=catalog.ConfigTables.Where(item=>tables.Contains(item.Id)).ToList();
        catalog.ConfigColumns=catalog.ConfigColumns.Where(item=>tables.Contains(item.ConfigTableId)&&item.ColumnName is "id" or "name" or "yz_role_ids" or "yz_admin_type_id" or "yz_project_id" or "delete_no" or "is_super").ToList();
        catalog.ConfigPages=catalog.ConfigPages.Where(item=>item.Id is FixedRoleCheckboxCompilation.ManagerPage or FixedRoleCheckboxCompilation.RolePage or "1696799309006348290").ToList();
        var pages=catalog.ConfigPages.Select(item=>item.Id).ToHashSet();
        catalog.ConfigPageColumns=catalog.ConfigPageColumns.Where(item=>pages.Contains(item.ConfigPageId)&&catalog.ConfigColumns.Any(column=>column.Id==item.ConfigColumnId)&&item.Extra.GetValueOrDefault("columnName").ToString()!="yz_project_id").ToList();
        foreach(var page in catalog.ConfigPages){page.IsCopy=0;page.Extra.Remove("isRepeatPwd");page.Extra["importMethod"]=JsonSerializer.SerializeToElement("");}
        catalog.ConfigMenus=[];catalog.ConfigButtons=[];catalog.ConfigChildren=[];catalog.ConfigCustomPages=[];catalog.ConfigIndexes=[];catalog.ConfigIndexColumns=[];
        return catalog;
    }
    private static JsonElement Parse(string text){using var document=JsonDocument.Parse(text);return document.RootElement.Clone();}
    /// <summary>真实FreeSql翻译相关子查询与真实HTTP拒绝路径；连接对象明确禁止Open及命令。</summary>
    private static async Task CheckNoDatabaseContract(Assembly assembly)
    {
        var opens=0;using var orm=new FreeSql.FreeSqlBuilder().UseConnectionFactory(FreeSql.DataType.MySql,()=>new DeniedConnection(()=>opens++)).UseAutoSyncStructure(false).Build();
        using var session=new DatabaseSession(new(){Provider="mysql",ExpectedDatabase="metarill_business_test",ConnectionString="Server=offline;Database=metarill_business_test;User ID=unused;Password=unused;"},orm);
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpContextAccessor();builder.Services.AddSingleton(session);builder.Services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(868));
        builder.Services.AddScoped<IRuntimeAuthorization>(provider=>new RequestAuthorization(provider.GetRequiredService<IHttpContextAccessor>()));
        assembly.GetType("Example.RoleCheckbox.Generated.GeneratedRegistration",true)!.GetMethod("AddGeneratedServices")!.Invoke(null,[builder.Services,null]);
        builder.Services.AddControllers().AddApplicationPart(assembly);
        await using var app=builder.Build();app.Use(async(context,next)=>{try{await next();}catch(Exception error) when(error is ArgumentException or UnauthorizedAccessException){context.Response.StatusCode=error is UnauthorizedAccessException?401:400;await context.Response.WriteAsJsonAsync(R<string>.Error("离线合同拒绝"));}});app.MapControllers();await app.StartAsync();
        try
        {
            using var client=new HttpClient{BaseAddress=new(app.Urls.Single()),Timeout=TimeSpan.FromSeconds(5)};
            const string endpoint="/api/admin/auto/yz_manager/6031906822946816/findNameIdYzRole1692528723801219073";
            using(var denied=await client.PostAsJsonAsync(endpoint,new{id=Array.Empty<string>()}))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
            client.DefaultRequestHeaders.Add("X-Test-Role-Action","create");
            using(var empty=await client.PostAsJsonAsync(endpoint,new{id=Array.Empty<string>(),formParameters=new{yzManager=new{yzAdminTypeId="999"},projectId="999"}}))
            {Assert.Equal(HttpStatusCode.OK,empty.StatusCode);using var document=JsonDocument.Parse(await empty.Content.ReadAsStringAsync());Assert.Empty(document.RootElement.GetProperty("data").EnumerateArray());}
            using(var bad=await client.PostAsJsonAsync(endpoint,new{sql="select 1"}))Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
            using(var bad=await client.PostAsJsonAsync(endpoint+"?projectId=999",new{id=Array.Empty<string>()}))Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
            var serviceType=assembly.GetType("Example.RoleCheckbox.Generated.Services.YzManagerService",true)!;
            using var scope=app.Services.CreateScope();var service=scope.ServiceProvider.GetRequiredService(serviceType);
            // 独立调用固定范围方法只翻译SQL，不运行查询；授权仍由真实测试宿主上下文建立。
            var accessor=scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();accessor.HttpContext=new DefaultHttpContext();accessor.HttpContext.Request.Headers["X-Test-Role-Action"]="create";
            var actor=new RequestAuthorization(accessor).RequireActor();var query=serviceType.GetMethod("RoleOptionsScope6031906822946816",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(service,[actor])!;
            var roleType=assembly.GetType("Example.RoleCheckbox.Generated.Entities.YzRole",true)!;
            var sql=(string)typeof(FixedRoleCheckboxTests).GetMethod(nameof(Sql),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(roleType).Invoke(null,[query])!;
            Assert.Contains("yz_admin_type",sql);Assert.Contains("EXISTS",sql,StringComparison.OrdinalIgnoreCase);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql,"yz_project_id",System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            Assert.DoesNotContain("is_super",sql,StringComparison.OrdinalIgnoreCase);Assert.Equal(0,opens);
            string? optionsSql=null;
            orm.Aop.CurdBefore+=(_,args)=>{optionsSql=args.Sql;throw new OfflineSqlCapturedException();};
            var optionTask=(Task)serviceType.GetMethod("RoleOptions6031906822946816Async")!.Invoke(service,[null,actor,CancellationToken.None])!;
            Assert.NotNull(await Record.ExceptionAsync(()=>optionTask));Assert.NotNull(optionsSql);Assert.Equal(0,opens);
            var orderStart=optionsSql.IndexOf("ORDER BY",StringComparison.OrdinalIgnoreCase);Assert.True(orderStart>=0,optionsSql);
            var ordering=optionsSql[orderStart..];Assert.Contains("id",ordering,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("name",ordering,StringComparison.OrdinalIgnoreCase);
        }
        finally{await app.StopAsync();}
    }
    private static string Sql<T>(object query) where T:class=>((FreeSql.ISelect<T>)query).ToSql("1");
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    private static async Task RunVue(string detail,[CallerFilePath]string source="")
    {
        var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(source)!,"FixedRoleCheckboxVueProbe.mjs"));info.ArgumentList.Add(detail);
        using var process=Process.Start(info)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await process.WaitForExitAsync(timeout.Token);Assert.True(process.ExitCode==0,await stdout+await stderr);
    }
    private sealed class Presence{public Optional<JsonElement?> YzRoleIds{get;set;}}
    private sealed class OfflineSqlCapturedException:Exception{public OfflineSqlCapturedException():base("离线已捕获SQL，禁止执行命令。") { }}
    private sealed class Authorization(string allowed):IRuntimeAuthorization
    {
        internal RuntimeActor Actor{get;}=new(1,"本机测试",false,"100","1");
        public RuntimeActor RequireActor()=>Actor;
        public void RequirePagePermission(string pageId,string operation){if(allowed=="error")throw new InvalidOperationException("不能吞掉本错误");if(operation!=allowed)throw new UnauthorizedAccessException("无此动作");}
    }
    private sealed class RequestAuthorization(IHttpContextAccessor accessor):IRuntimeAuthorization
    {
        public RuntimeActor RequireActor()=>new(1,"离线HTTP",false,"100","1");
        public void RequirePagePermission(string pageId,string operation){if(accessor.HttpContext?.Request.Headers["X-Test-Role-Action"].ToString()!=operation)throw new UnauthorizedAccessException("无此动作");}
    }
    private sealed class DeniedConnection(Action opening):DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]public override string ConnectionString{get;set;}="Server=offline;Database=metarill_business_test;";
        public override string Database=>"metarill_business_test";public override string DataSource=>"offline";public override string ServerVersion=>"8.4.11";public override ConnectionState State=>ConnectionState.Closed;
        public override void ChangeDatabase(string value)=>throw new InvalidOperationException("离线测试禁止切换库");public override void Open(){opening();throw new InvalidOperationException("离线测试禁止连接");}public override void Close(){}
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolation)=>throw new InvalidOperationException("离线测试禁止事务");protected override DbCommand CreateDbCommand()=>throw new InvalidOperationException("离线测试禁止命令");
    }
}
