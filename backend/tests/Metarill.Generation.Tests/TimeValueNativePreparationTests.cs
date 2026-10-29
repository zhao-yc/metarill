using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>离线冻结原模型并拒绝漂移；正式数据库尚未连接，不冒充真实上传通过。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class TimeValueNativePreparationTests
{
    /// <summary>真实完整包严格编译、驱动依赖和源目录验证，正常运行只放行固定结构审计日志。</summary>
    [Fact]
    public async Task Original_time_package_is_frozen_before_database_lease()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-time-values-native");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));var package=Path.Combine(directory,"prepared");
        try
        {
            TimeValueNativePreparation.Create(source,package);var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",package);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(start);
            using(var mapping=new Metarill.Data.DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1"}))
                Assert.Equal("time",mapping.Orm.CodeFirst.GetDbInfo(typeof(TimeSpan)).dbtype);
            var proof=TimeValueNativePreparation.Capture(package);var catalog=TimeValueNativePreparation.Verify(proof);Assert.Equal(3,catalog.ConfigTables.Count);Assert.Equal(50,catalog.ConfigColumns.Count);Assert.Equal(6,catalog.ConfigIndexes.Count);Assert.Empty(GenerationCapabilityAudit.Collect(GenerationModel.Create(catalog,TimeValueNativePreparation.RootNamespace)));
            foreach(var relative in new[]{"src/Metarill.Core/Contracts/FileUploadLifetime.cs","backend/metadata/files.json","backend/Application.Host/bin/Release/net10.0/Application.Host.dll","backend/Application.Host/bin/Release/net10.0/Application.Host.deps.json"})
            {var file=CSharpGenerator.SafePath(package,relative);var bytes=await File.ReadAllBytesAsync(file);await File.AppendAllTextAsync(file,"漂移");Assert.Throws<InvalidOperationException>(()=>TimeValueNativePreparation.Verify(proof));await File.WriteAllBytesAsync(file,bytes);}
            var copied=TimeValueNativePreparation.CopyForExecution(proof,Path.Combine(directory,"execution/application"));var journal=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/schema-history.jsonl");Directory.CreateDirectory(Path.GetDirectoryName(journal)!);await File.WriteAllTextAsync(journal,"{}\n");Assert.Throws<InvalidOperationException>(()=>TimeValueNativePreparation.Verify(copied));TimeValueNativePreparation.Verify(copied,true);
            var unknown=CSharpGenerator.SafePath(copied.PackageRoot,"backend/Application.Host/.local/unknown.json");await File.WriteAllTextAsync(unknown,"{}");Assert.Throws<InvalidOperationException>(()=>TimeValueNativePreparation.Verify(copied,true));File.Delete(unknown);Assert.Throws<InvalidOperationException>(()=>TimeValueNativePreparation.CopyForExecution(proof,copied.PackageRoot));TimeValueNativePreparation.Verify(proof);
            var mvc=TimeValueNativePreparation.CopyForExecution(proof,Path.Combine(directory,"mvc/application"));await CheckOriginalInputError(mvc);
            TimeValueNativePreparation.Verify(proof);
            var exported=Environment.GetEnvironmentVariable("METARILL_TIME_VALUE_PREPARE_OUTPUT");if(!string.IsNullOrWhiteSpace(exported))
            {Assert.Equal(CSharpGenerator.SafePath(source,".local/time-values-native/prepared"),Path.GetFullPath(exported));var result=TimeValueNativePreparation.CopyForExecution(proof,exported);await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(exported)!,"prepared-receipt.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));}
        }
        finally{if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>正式节点设置必须实际生成正整数且单调，编译通过不能掩盖首次初始化才触发的范围错误。</summary>
    [Fact]
    public void Formal_worker_generates_real_ids_before_database_lease()
    {
        var ids=new Metarill.Core.Security.SnowflakeIdGenerator(TimeValueNativeMySqlTests.WorkerId);
        var first=ids.NextId();var second=ids.NextId();Assert.True(first>0);Assert.True(second>first);
    }
    /// <summary>无数据库/进程的合成租约测试，错误用途/状态/产品/路径均在任何副作用前拒绝。</summary>
    [Theory][InlineData("purpose")][InlineData("authorization")][InlineData("ended")][InlineData("product")][InlineData("run")][InlineData("path")][InlineData("relative")][InlineData("proof")]
    public void Native_lease_rejects_unapproved_or_foreign_input_without_side_effects(string invalid)
    {
        var root=Path.Combine(Path.GetTempPath(),"metarill-time-values-gate",Guid.NewGuid().ToString("N"));var model=JsonSerializer.SerializeToNode(new{Purpose=TimeValueNativeLease.Purpose,AuthorizationStatus="approved",LeaseStatus="running",ProductCommit=TimeValueNativePreparation.ProductCommit,RunId="0123456789abcdef",TimeValuePreparation=new{PackageRoot=root}})!;
        Assert.Equal("0123456789abcdef",TimeValueNativeLease.Validate(JsonSerializer.SerializeToElement(model),root));
        if(invalid=="purpose")model["Purpose"]="other";if(invalid=="authorization")model["AuthorizationStatus"]="pending";if(invalid=="ended")model["LeaseStatus"]="ended";if(invalid=="product")model["ProductCommit"]=new string('0',40);if(invalid=="run")model["RunId"]="../old";if(invalid=="path")model["TimeValuePreparation"]!["PackageRoot"]=Path.Combine(root,"foreign");if(invalid=="relative")model["TimeValuePreparation"]!["PackageRoot"]="prepared";if(invalid=="proof")model.AsObject().Remove("TimeValuePreparation");
        Assert.Throws<InvalidOperationException>(()=>TimeValueNativeLease.Validate(JsonSerializer.SerializeToElement(model),root));Assert.False(Directory.Exists(root));
    }
    /// <summary>真实完整包区分MVC解析错误与手动DTO拒绝；仅开发身份/离线样本，不冒充正式登录。</summary>
    private static async Task CheckOriginalInputError(GlobalCachedPackageProof proof)
    {
        var hostDirectory=CSharpGenerator.SafePath(proof.PackageRoot,"backend/Application.Host");var program=Path.Combine(hostDirectory,"Program.cs");
        var current=await File.ReadAllTextAsync(program);Assert.Contains("options.InvalidModelStateResponseFactory",current);
        // 探针只写隔离MVC执行副本，验证非管理路径仍由框架原工厂处理；正式准备包不含此控制器。
        await File.WriteAllTextAsync(Path.Combine(hostDirectory,"NonAdminModelStateProbe.cs"),"""
[Microsoft.AspNetCore.Mvc.ApiController]
[Microsoft.AspNetCore.Mvc.Route("__contract/non-admin")]
public sealed class NonAdminModelStateProbeController:Microsoft.AspNetCore.Mvc.ControllerBase
{
    /// <summary>畸形JSON应在MVC边界失败，方法不能进入任何业务。</summary>
    [Microsoft.AspNetCore.Mvc.HttpPost]
    public object Post([Microsoft.AspNetCore.Mvc.FromBody] System.Collections.Generic.Dictionary<string,string> body)=>throw new System.InvalidOperationException("探针动作不应执行。");
}
""");

        var shapeDirectory=CSharpGenerator.SafePath(NonRedundantApplicationFixture.Root(),".local/time-values-native/contract-check/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(shapeDirectory);
        ProcessStartInfo Start()
        {
            var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",hostDirectory);
            info.ArgumentList.Add(CSharpGenerator.SafePath(hostDirectory,"bin/Release/net10.0/Application.Host.dll"));
            info.Environment["ASPNETCORE_ENVIRONMENT"]="Development";info.Environment["Authentication__Mode"]="Development";
            info.Environment["Authentication__DevelopmentBootstrap__Enabled"]="true";info.Environment["Authentication__DevelopmentBootstrap__Username"]="offline-time-owner";info.Environment["Authentication__DevelopmentBootstrap__Password"]="offline-time-password-2026";
            info.Environment["Database__Provider"]="mysql";info.Environment["Database__ExpectedDatabase"]="offline";info.Environment["Database__ConnectionString"]="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Connection Timeout=1";return info;
        }
        async Task Build()
        {
            var start=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",proof.PackageRoot);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","-warnaserror","--verbosity","quiet","--disable-build-servers"})start.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(start);
        }
        // 负向副本只移除这一工厂，真实启动观察旧MVC响应；冻结准备包完全不改。
        var withoutFactory=string.Join("\n",current.Split('\n').Where(line=>!line.Contains("options.InvalidModelStateResponseFactory",StringComparison.Ordinal)));Assert.NotEqual(current,withoutFactory);
        await File.WriteAllTextAsync(program,withoutFactory);await Build();
        await using(var previous=await FreshHost.StartAsync(Start(),"/health"))
        {
            await FreshWebHttp.LoginAsync(previous.Client,"offline-time-owner","offline-time-password-2026");
            using var response=await previous.Client.PostAsync("/api/admin/auto/sample_contract/81002/add",new StringContent("{",System.Text.Encoding.UTF8,"application/json"));
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());await Shape("negative-mvc",response,json.RootElement);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,response.StatusCode);Assert.False(json.RootElement.TryGetProperty("code",out _));Assert.Equal("application/problem+json",response.Content.Headers.ContentType?.MediaType);
            using var baselineOauth=await previous.Client.PostAsync("/api/agent/oauth/token",new StringContent("{",System.Text.Encoding.UTF8,"application/json"));
            using var baselineOauthJson=JsonDocument.Parse(await baselineOauth.Content.ReadAsStringAsync());await Shape("negative-baselineOauth-closed",baselineOauth,baselineOauthJson.RootElement);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized,baselineOauth.StatusCode);Assert.Equal(401,baselineOauthJson.RootElement.GetProperty("code").GetInt32());

        }
        await File.WriteAllTextAsync(program,current);await Build();
        await using var host=await FreshHost.StartAsync(Start(),"/health");await FreshWebHttp.LoginAsync(host.Client,"offline-time-owner","offline-time-password-2026");
        var index=0;
        foreach(var body in new[]{JsonSerializer.Serialize(new{name="离线无效时刻",clockMicro="bad-time"}),JsonSerializer.Serialize(new{name=new{value="错误类型"}}),JsonSerializer.Serialize(new{name="未知字段",unpublishedField=1})})
        {
            using var response=await host.Client.PostAsync("/api/admin/auto/sample_contract/81002/add",new StringContent(body,System.Text.Encoding.UTF8,"application/json"));
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());await Shape("manual-"+(index++),response,json.RootElement);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(400,json.RootElement.GetProperty("code").GetInt32());Assert.Equal("请求参数不符合页面定义。",json.RootElement.GetProperty("msg").GetString());
        }
        using var malformed=await host.Client.PostAsync("/api/admin/auto/sample_contract/81002/add",new StringContent("{",System.Text.Encoding.UTF8,"application/json"));
        using var content=JsonDocument.Parse(await malformed.Content.ReadAsStringAsync());await Shape("positive-mvc",malformed,content.RootElement);
        Assert.Equal(System.Net.HttpStatusCode.OK,malformed.StatusCode);Assert.Equal(500,content.RootElement.GetProperty("code").GetInt32());Assert.Equal("请求字段类型、字段名或必填项不正确。",content.RootElement.GetProperty("msg").GetString());
        using var oauth=await host.Client.PostAsync("/api/agent/oauth/token",new StringContent("{",System.Text.Encoding.UTF8,"application/json"));
        using var oauthContent=JsonDocument.Parse(await oauth.Content.ReadAsStringAsync());await Shape("oauth-unchanged",oauth,oauthContent.RootElement);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,oauth.StatusCode);Assert.Equal(401,oauthContent.RootElement.GetProperty("code").GetInt32());
        using var nonAdmin=await host.Client.PostAsync("/__contract/non-admin",new StringContent("{",System.Text.Encoding.UTF8,"application/json"));
        using var nonAdminJson=JsonDocument.Parse(await nonAdmin.Content.ReadAsStringAsync());await Shape("non-admin-original",nonAdmin,nonAdminJson.RootElement);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,nonAdmin.StatusCode);Assert.False(nonAdminJson.RootElement.TryGetProperty("code",out _));Assert.Equal("application/problem+json",nonAdmin.Content.Headers.ContentType?.MediaType);
        Task Shape(string name,HttpResponseMessage response,JsonElement json)
        {
            var hasCode=json.TryGetProperty("code",out var code);
            return File.WriteAllTextAsync(Path.Combine(shapeDirectory,name+".json"),JsonSerializer.Serialize(new{name,httpStatus=(int)response.StatusCode,contentType=response.Content.Headers.ContentType?.MediaType,hasCode,code=hasCode?code.GetInt32():0}));
        }
    }
}
