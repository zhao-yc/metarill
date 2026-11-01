using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Metarill.Core.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Metarill.Runtime.Compatibility;
using Metarill.Runtime.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Metarill.Api.Tests;

/// <summary>原固定请求及实际Vue详情方法请求经真实Program匹配；动作前返回，不冒充业务或权限验收。</summary>
public sealed class OriginalFrontendRouteInventoryTests(ITestOutputHelper output)
{
    /// <summary>三端分别取条件编译分支；解析错误不能缩小清单，原详情独立捕获，其它动态请求保持未证明。</summary>
    [Fact]
    public async Task Original_fixed_and_executed_detail_requests_match_actual_program_actions_and_known_legacy_operations()
    {
        var root=Root();var inventory=await Inventory(root);Assert.Empty(inventory.Errors);
        Assert.Equal(new[]{"APP","H5","WEIXIN"},inventory.Fixed.Select(item=>item.Platform).Distinct().Order());
        Assert.True(inventory.ScannedFiles>=100);Assert.NotEmpty(inventory.Unresolved);
        // 详情URL来自实际原Vue方法，不为动态变量手工替换理想值；其余动态调用仍保持未证明。
        var details=await Inventory(root,"original-detail-requests.mjs");Assert.Empty(details.Errors);Assert.Empty(details.Unresolved);
        Assert.True(details.ScannedFiles>=18);Assert.NotEmpty(details.Fixed);
        Assert.Equal(new[]{"APP","H5","WEIXIN"},details.Fixed.Select(item=>item.Platform).Distinct().Order());
        Assert.Contains(details.Fixed,item=>item.Method=="DELETE");
        Assert.Contains(details.Fixed,item=>item.Method=="POST"&&item.Route.EndsWith("/add",StringComparison.Ordinal));
        Assert.Contains(details.Fixed,item=>item.Method=="POST"&&item.Route.EndsWith("/update",StringComparison.Ordinal));
        Assert.Contains(details.Fixed,item=>item.Method=="GET"&&item.Route.EndsWith("/detail/9007199254740993",StringComparison.Ordinal));
        var routes=inventory.Fixed.Concat(details.Fixed).GroupBy(item=>(item.Method,item.Route)).ToArray();Assert.True(routes.Length>=50);
        Assert.Contains(routes,group=>group.Key==("GET","/admin/custom/index/config_columns"));
        Assert.Contains(routes,group=>group.Key==("POST","/admin/custom/index/sync_runtime_all_projects"));
        output.WriteLine($"三端原固定请求：源码{inventory.ScannedFiles}份，声明{inventory.Fixed.Length}条，唯一方法/路径{inventory.Fixed.GroupBy(item=>(item.Method,item.Route)).Count()}组，原AST动态未证明{inventory.Unresolved.Length}条，解析错误{inventory.Errors.Length}条。");
        output.WriteLine($"独立执行原详情：源码{details.ScannedFiles}份，捕获请求{details.Fixed.Length}条，唯一路由{details.Fixed.GroupBy(item=>(item.Method,item.Route)).Count()}组；与固定路由合并后{routes.Length}组。仍不证明其它动态请求或业务权限。");
        Assert.Contains(routes,group=>group.Key==("POST","/admin/custom/page_detail_layout/save"));
        var directory=Path.Combine(Path.GetTempPath(),"metarill-original-routes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var settings=Path.Combine(directory,"settings.json");var previous=Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        var database=new{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1"};
        await File.WriteAllTextAsync(settings,JsonSerializer.Serialize(new{Metarill=new{Database=database,BusinessDatabase=database}}));
        Environment.SetEnvironmentVariable("METARILL_SETTINGS",settings);
        try
        {
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
            {
                services.Configure<MvcOptions>(options=>options.Filters.Insert(0,new Probe()));
                // 路由测试不消费平台既有导出文件；其它Program的路由和DI注册仍真实保留。
                foreach(var service in services.Where(item=>item.ServiceType==typeof(IHostedService)&&item.ImplementationType?.Name=="ExportCleanupWorker").ToArray())services.Remove(service);
            }));
            using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var failures=new List<string>();
            var registry=factory.Services.GetRequiredService<MetadataRegistry>();var disabled=0;
            foreach(var group in routes)
            {
                using var request=new HttpRequestMessage(new HttpMethod(group.Key.Method),"/api"+group.Key.Route);
                using var response=await client.SendAsync(request);
                var detail=group.FirstOrDefault(item=>item.Action is not null);
                if(detail is not null)
                {
                    // 原源码仍含不可用方法；按生产元数据核对关闭的路由，不能为消除404恢复聚合表普通写入口。
                    var page=registry.Platform.ConfigPages.Single(item=>item.Id==detail.Route.Split('/')[4]);
                    var enabled=detail.Action switch
                    {
                        "add"=>page.IsNew==1,"update"=>Metarill.Core.Metadata.PageUpdatePolicy.CanUpdate(page),
                        "delete"=>page.IsDelete==1,"copy"=>page.IsCopy==1,"detail"=>true,
                        _=>throw new InvalidOperationException("未登记的原详情动作。")
                    };
                    if(!enabled)
                    {
                        disabled++;Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
                        Assert.False(response.Headers.Contains("X-Original-Route-Probe"));continue;
                    }
                }
                if(response.StatusCode!=HttpStatusCode.NoContent||!response.Headers.TryGetValues("X-Original-Route-Probe",out var matched)||matched.Single()!="supported")
                {
                    var source=group.First();
                    failures.Add(group.Key.Method+" "+group.Key.Route+"，状态"+(int)response.StatusCode+"，来源"+source.File+":"+source.Line);
                }
            }
            Assert.True(disabled>0,"必须实际检查当前聚合表禁用的普通CRUD，不能静默遗漏。");
            output.WriteLine($"当前生产元数据禁用普通路由{disabled}组均保持404，其余{routes.Length-disabled}组匹配实际动作；前端按钮开关一致性另验。");
            // 阴性对照必须失败：未知地址、错误方法和未登记的Legacy动作不能被路由探针误判通过。
            using(var absent=await client.GetAsync("/api/admin/__original_route_probe_missing__"))
            {Assert.Equal(HttpStatusCode.NotFound,absent.StatusCode);Assert.False(absent.Headers.Contains("X-Original-Route-Probe"));}
            using(var wrong=await client.GetAsync("/api/admin/custom/page_detail_layout/save"))
            {Assert.Equal(HttpStatusCode.MethodNotAllowed,wrong.StatusCode);Assert.False(wrong.Headers.Contains("X-Original-Route-Probe"));}
            using(var unsupported=await client.PostAsync("/api/admin/custom/config_table/__original_route_probe_unknown__",null))
            {Assert.Equal(HttpStatusCode.NoContent,unsupported.StatusCode);Assert.Equal("unsupported-legacy",Assert.Single(unsupported.Headers.GetValues("X-Original-Route-Probe")));}
            Assert.True(failures.Count==0,"原固定请求未接通实际动作或旧按钮白名单：\n"+string.Join("\n",failures));
        }
        finally{Environment.SetEnvironmentVariable("METARILL_SETTINGS",previous);File.Delete(settings);Directory.Delete(directory);}
    }

    /// <summary>原列表实际正文使用真实Program的JSON设置及动作参数类型解码；仍不执行MVC业务／权限／SQL。</summary>
    [Fact]
    public async Task Original_list_payloads_match_actual_page_request_contract_and_program_routes()
    {
        var inventory=await Inventory(Root(),"original-list-requests.mjs");Assert.Empty(inventory.Errors);Assert.Equal(20,inventory.ScannedFiles);
        Assert.Equal(19,inventory.StandardFiles);Assert.Equal(16,inventory.LegacyFiles);Assert.Equal(3,inventory.MixinFiles);
        Assert.Equal(3,inventory.Unresolved.Length);Assert.All(inventory.Unresolved,item=>{Assert.EndsWith("config_menu-list-2200000000000099000.vue",item.File);Assert.Contains("菜单工作台",item.Reason);});
        var directory=Path.Combine(Path.GetTempPath(),"metarill-original-list-body-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var settings=Path.Combine(directory,"settings.json");var previous=Environment.GetEnvironmentVariable("METARILL_SETTINGS");
        var database=new{ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused;Connection Timeout=1"};
        await File.WriteAllTextAsync(settings,JsonSerializer.Serialize(new{Metarill=new{Database=database,BusinessDatabase=database}}));Environment.SetEnvironmentVariable("METARILL_SETTINGS",settings);
        try
        {
            await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
            {
                services.Configure<MvcOptions>(options=>options.Filters.Insert(0,new Probe()));
                foreach(var service in services.Where(item=>item.ServiceType==typeof(IHostedService)&&item.ImplementationType?.Name=="ExportCleanupWorker").ToArray())services.Remove(service);
            }));
            using var client=factory.CreateClient(new(){AllowAutoRedirect=false});var options=factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;var routes=new HashSet<string>(StringComparer.Ordinal);var decoded=0;
            foreach(var item in inventory.Fixed)
            {
                Assert.Equal("page",item.Action);var body=item.Payload??throw new InvalidOperationException("原列表请求正文缺失。");
                // 真实动作声明须仍为标准PageRequest，不能只因JSON能解成另一种对象就算接通。
                if(routes.Add(item.Route))
                {
                    using var request=new HttpRequestMessage(HttpMethod.Post,"/api"+item.Route){Content=new StringContent(body.GetRawText(),Encoding.UTF8,"application/json")};request.Headers.Add("X-Original-List-Body-Type","inspect");using var response=await client.SendAsync(request);
                    Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);Assert.Equal(typeof(PageRequest).FullName,Assert.Single(response.Headers.GetValues("X-Original-List-Body-Type")));
                }
                var parsed=JsonSerializer.Deserialize<PageRequest>(body.GetRawText(),options)!;Assert.Equal(body.GetProperty("pageNo").GetInt32(),parsed.PageNo);Assert.Equal(body.GetProperty("pageSize").GetInt32(),parsed.PageSize);Assert.Empty(parsed.Filters);Assert.Empty(parsed.SelectedIds);
                Assert.NotNull(parsed.OrderBy);Assert.Null(parsed.OrderBy.Column);Assert.Null(parsed.OrderBy.Field);Assert.Null(parsed.OrderBy.Order);
                Assert.Equal(body.TryGetProperty("parentId",out var parent)&&parent.ValueKind!=JsonValueKind.Null?parent.GetString():null,parsed.ParentId);decoded++;
            }
            Assert.Equal(19,routes.Count);Assert.Equal(inventory.Fixed.Length,decoded);Assert.True(decoded>1500);
            foreach(var bad in new[]{"{\"filters\":[]}","{\"pageNo\":\"NaN\"}","{\"pageSize\":1.5}"})Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<PageRequest>(bad,options));
            // PageRequest既有兼容合同忽略未映射字段；额外身份提示不能成为DTO状态，本探针不声明真实授权通过。
            var hinted=JsonSerializer.Deserialize<PageRequest>("{\"projectId\":\"9007199254740995\",\"adminTypeId\":\"7\",\"isSuper\":true}",options)!;
            Assert.Equal(JsonSerializer.Serialize(new PageRequest(),options),JsonSerializer.Serialize(hinted,options));
            output.WriteLine($"原20列表中19标准页、16页内／3原状态机；{decoded}实际正文由Program JSON配置解码，19真实动作声明PageRequest。菜单包装3条未覆盖保持；无动作、权限、SQL或浏览器执行。");
        }
        finally{Environment.SetEnvironmentVariable("METARILL_SETTINGS",previous);File.Delete(settings);Directory.Delete(directory);}
    }

    /// <summary>在资源阶段结束请求，早于模型绑定与控制器实例/动作执行；只验证实际路由选择。</summary>
    private sealed class Probe:IAsyncResourceFilter
    {
        public Task OnResourceExecutionAsync(ResourceExecutingContext context,ResourceExecutionDelegate next)
        {
            var action=(ControllerActionDescriptor)context.ActionDescriptor;var supported=true;
            if(action.ControllerName=="CompatibilityButton"&&action.ActionName=="Legacy")
            {
                // 通配路由不能证明功能存在，必须由生产策略验证固定地址确有登记。
                try
                {
                    var registry=context.HttpContext.RequestServices.GetRequiredService<MetadataRegistry>();
                    var route=context.HttpContext.Request.Path.Value![4..];var table=route.Split('/')[3];
                    if(table=="index")table="yz_project";
                    var source=registry.Platform.ConfigTables.Single(item=>item.TableName==table);
                    var page=registry.Platform.ConfigPages.First(item=>item.ConfigTableId==source.Id);
                    _=CompatibilityButtonPolicy.RequireAction(new(1,"仅路由策略离线测试",true,"1",""),registry,
                        new Dictionary<string,object?>{{"yz_project_id",registry.Platform.YzProjectId},{"action_type","1"},{"config_page_id",page.Id},{"sql",route}});
                }
                catch(InvalidOperationException){supported=false;}
            }
            if(context.HttpContext.Request.Headers.ContainsKey("X-Original-List-Body-Type"))
            {
                var body=Assert.Single(action.Parameters,parameter=>parameter.BindingInfo?.BindingSource==BindingSource.Body);
                context.HttpContext.Response.Headers["X-Original-List-Body-Type"]=body.ParameterType.FullName;
            }
            context.HttpContext.Response.Headers["X-Original-Route-Probe"]=supported?"supported":"unsupported-legacy";
            context.Result=new StatusCodeResult(StatusCodes.Status204NoContent);return Task.CompletedTask;
        }
    }

    /// <summary>仅运行固定审查脚本：AST扫描或隔离捕获原详情方法；不执行模块顶层、生命周期或发送外部请求。</summary>
    private static async Task<RouteInventory> Inventory(string root,string tool="original-frontend-routes.mjs")
    {
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=root};
        start.ArgumentList.Add(Path.Combine(root,"tools/acceptance",tool));start.ArgumentList.Add(Path.Combine(root,"frontend/metarill_uni_admin"));
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try{await process.WaitForExitAsync(timeout.Token);}
        catch{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}throw;}
        Assert.Equal(0,process.ExitCode);Assert.Equal("",await errors);
        return JsonSerializer.Deserialize<RouteInventory>(await output,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>测试源码位置确定真实仓库，不读取调用者提供的任意目录。</summary>
    private static string Root([CallerFilePath]string source="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../.."));
    private sealed record RouteSource(string File,int Line,string Method,string Route,string Platform,string? Action=null,JsonElement? Payload=null);
    private sealed record RouteUnresolved(string File,int Line,string Reason,string Platform);
    private sealed record RouteError(string File,string Reason,string Platform);
    private sealed record RouteInventory(int ScannedFiles,RouteSource[] Fixed,RouteUnresolved[] Unresolved,RouteError[] Errors,int StandardFiles=0,int LegacyFiles=0,int MixinFiles=0);
}
