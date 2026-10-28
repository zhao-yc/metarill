using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using System.Text.Json;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.AspNetCore.Http;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>固定原三按钮完整包实际编译，再直接执行模板授权类；不使用近似授权器、数据库或HTTP身份替身。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class BusinessButtonApplicationAuthorizationTests
{
    /// <summary>实际Full保持独立按钮、后台/项目、范围和无权/匿名协议，开发宿主缺省方法不会放行。</summary>
    [Fact]
    public async Task Actual_full_package_preserves_business_button_boundary_and_scope()
    {
        // 根命名空间故意与新增权限类同名，模板必须用全局限定避免生成项目命名冲突。
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-business-button-full");var directory=Path.Combine(boundary,Guid.NewGuid().ToString("N"));const string ns="Example.BusinessButtonAuthorization";AssemblyLoadContext? loadContext=null;
        try
        {
            var metadata=GlobalCachedSourceGenerationTests.Catalog();new ApplicationPackager().Create(metadata,directory,ns,NonRedundantApplicationFixture.Root());
            foreach(var operation in new[]{"restore","build"})await BuildAsync(directory,operation);
            var host=Path.Combine(directory,"backend/Application.Host");// 多个正式包的程序集均叫Application.Host；各自加载上下文隔离，公共依赖仍复用测试程序集。
            loadContext=new AssemblyLoadContext("metarill-business-buttons-"+Guid.NewGuid().ToString("N"),isCollectible:true);var assembly=loadContext.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(Path.Combine(host,"bin/Debug/net10.0/Application.Host.dll"))));
            var bytes=await File.ReadAllBytesAsync(Path.Combine(directory,"backend/metadata/project.json"));
            var authority=New("VerifiedPackageSecurityCatalog",bytes);var page=metadata.ConfigPages.Single(page=>page.Id=="81002");
            var identity=new AuthenticatedIdentity(new(8,"原业务按钮账号",false,metadata.YzProjectId,page.YzAdminTypeId),new HashSet<string>(),9,null)
            {PrincipalProjectId=metadata.YzProjectId,PrincipalAdminTypeId=page.YzAdminTypeId,ButtonIds=new HashSet<string>{"86000"},ExactDataManageButtonIds=new HashSet<string>{"86000"},ExactDataManageMatchField="real_name",ExactDataManageMatchValue=JsonSerializer.SerializeToElement("9007199254740993")};
            var context=new DefaultHttpContext();var auth=(IRuntimeAuthorization)New("ApplicationWebAuthorization",new HttpContextAccessor{HttpContext=context},authority);context.Items[typeof(AuthenticatedIdentity)]=identity;
            auth.RequireButtonPermission(page.Id,"86000");Assert.Throws<SecurityException>(()=>auth.RequirePagePermission(page.Id,"detail"));
            var scope=auth.ResolveButtonExactDataManage(page.Id,"86000","realName");Assert.True(scope.IsRequired);Assert.Equal(new long[]{9007199254740993},scope.Read<long>());
            Assert.Throws<NotSupportedException>(()=>auth.ResolveButtonExactDataManage(page.Id,"86000","wrong"));
            Assert.Throws<SecurityException>(()=>auth.RequireButtonPermission("81003","86000"));Assert.Throws<SecurityException>(()=>auth.RequireButtonPermission(page.Id,"86001"));Assert.Throws<SecurityException>(()=>auth.RequireButtonPermission(page.Id,"999"));
            foreach(var scenario in new[]{"project","admin","selected","platform","revoked","super-only"})
            {
                context.Items[typeof(AuthenticatedIdentity)]=scenario switch{
                    "project"=>identity with{PrincipalProjectId="42",Actor=identity.Actor with{ProjectId="42"}},
                    "admin"=>identity with{PrincipalAdminTypeId="42",Actor=identity.Actor with{AdminTypeId="42"}},
                    "selected"=>identity with{Actor=identity.Actor with{ProjectId="42"}},
                    "platform"=>identity with{Actor=identity.Actor with{IsAdministrator=true}},
                    "super-only"=>identity with{IsRealmSuper=true,ButtonIds=new HashSet<string>()},
                    _=>identity with{ButtonIds=new HashSet<string>(),Permissions=new HashSet<string>{page.Id+":is_all"}}};
                Assert.Throws<SecurityException>(()=>auth.ResolveButtonExactDataManage(page.Id,"86000","real_name"));
            }
            context.Items[typeof(AuthenticatedIdentity)]=identity with{GrantId=123};auth.RequireButtonPermission(page.Id,"86000");
            context.Items[typeof(AuthenticatedIdentity)]=identity with{ExactDataManageButtonIds=new HashSet<string>()};Assert.False(auth.ResolveButtonExactDataManage(page.Id,"86000","real_name").IsRequired);
            context.Items.Clear();Assert.Throws<UnauthorizedAccessException>(()=>auth.RequireButtonPermission(page.Id,"86000"));Assert.Throws<UnauthorizedAccessException>(()=>auth.ResolveButtonExactDataManage(page.Id,"86000","real_name"));
            // 真实完整包旧双参数宿主没有当前协调器时，不执行文件回调，更不能缺省复用旧文件身份。
            context.Items[typeof(AuthenticatedIdentity)]=identity;var invoked=false;
            await Assert.ThrowsAsync<NotSupportedException>(()=>auth.WithButtonFileCurrentAsync(page.Id,"86000",identity.Actor,null!,()=>{invoked=true;return Task.CompletedTask;}));
            Assert.False(invoked);Assert.Same(identity,context.Items[typeof(AuthenticatedIdentity)]);
            // 此处所有决策在已认证请求上下文执行，未连接数据库，未声称完成真实业务动作调用。
            // 反射只构造包内真实类型，公共依赖由共享框架提供。
            object New(string name,params object?[] arguments)=>Activator.CreateInstance(assembly.GetType(ns+".Host."+name,true)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,arguments,CultureInfo.InvariantCulture)!;
        }
        finally{loadContext?.Unload();if(Directory.Exists(directory))await TestDirectoryCleanup.DeleteAsync(directory,boundary);}
    }
    /// <summary>只编译完整包内工程，严格构建并由现有子进程边界保留日志和管理超时。</summary>
    private static Task BuildAsync(string directory,string operation)
    {
        var info=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",directory);
        foreach(var value in new[]{operation,"backend/Application.Host/Application.Host.csproj","--nologo","--disable-build-servers","--verbosity","quiet"})info.ArgumentList.Add(value);
        if(operation=="build"){info.ArgumentList.Add("--no-restore");info.ArgumentList.Add("-warnaserror");}
        return NonRedundantApplicationFixture.RunAsync(info);
    }

}
