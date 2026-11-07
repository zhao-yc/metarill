using System.Data.Common;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class StandaloneWebAuthenticationTests
{
    /// <summary>真实包编译常量不能由manifest、等价JSON或外部字节改写；异常不回显目录正文。</summary>
    [Theory]
    [InlineData("content")][InlineData("bom")][InlineData("crlf")][InlineData("empty")]
    public void ImmutableCatalog_rejects_modified_bytes_against_real_compiled_binding(string kind)
    {
        var bytes=package.CatalogBytes.ToArray();
        bytes=kind switch{"empty"=>[],"bom"=>[0xef,0xbb,0xbf,..bytes],"crlf"=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\n","\r\n")),_=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"yzProjectName\":", "\"yzProjectName\" :"))};
        var error=Assert.Throws<InvalidOperationException>(()=>package.New("VerifiedPackageSecurityCatalog",bytes));Assert.Contains("编译摘要",error.Message);
    }

    /// <summary>输入数组、完整clone和发布投影三条路径都不能污染私有冻结目录。</summary>
    [Fact]
    public void ImmutableCatalog_retains_private_bytes_and_full_independent_clones()
    {
        var bytes=package.CatalogBytes.ToArray();var source=package.New("VerifiedPackageSecurityCatalog",bytes);bytes[0]=0;
        var first=(MetadataCatalog)package.Invoke(source,"Clone")!;var original=JsonSerializer.Serialize(first);first.YzProjectName="不得污染";first.ConfigPages[0].Extra["unknownLegalProof"]=JsonSerializer.SerializeToElement("完整JSON保留但不能跨clone污染");
        Assert.Equal(original,JsonSerializer.Serialize(package.Invoke(source,"Clone")));
        var published=package.Property<IReadOnlyList<SecurityResourceCatalog>>(source,"PublishedCatalogs");
        Assert.Throws<NotSupportedException>(()=>((IList<SecurityPageResource>)published[0].Pages)[0]=published[0].Pages[0] with{CanCreate=false});
        Assert.Null(package.Registry().GetType().GetProperty("Metadata"));
    }

    /// <summary>执行真实包注册函数，两个接口同scope同实例，完整proof目录与正常读取一致且没有config SQL。</summary>
    [Fact]
    public async Task ImmutableCatalog_actual_di_binds_same_scope_and_complete_transaction_proof()
    {
        using var f=new ImmutableApplication(package);using var scope=f.Services.CreateScope();
        var current=scope.ServiceProvider.GetRequiredService<ISecurityCurrentResources>();var roles=scope.ServiceProvider.GetRequiredService<IRolePermissionCatalog>();Assert.Same(current,roles);
        Assert.DoesNotContain(f.Registrations,item=>item.ServiceType==typeof(SecurityRealmCurrentReader)||item.ServiceType==typeof(SecurityRealmMutationGuard)||item.ServiceType==typeof(RolePermissionBusiness));
        var normal=await roles.ReadAsync(f.Session,package.Metadata.YzProjectId,CancellationToken.None);
        await f.Session.InTransactionAsync(async()=>
        {
            var tx=f.Session.CurrentTransaction!;var resources=await current.ReadForUpdateAsync(f.Session,tx,f.Project,f.Admin,CancellationToken.None);
            Assert.Equal(JsonSerializer.Serialize(normal),JsonSerializer.Serialize(roles.RequireLoaded(f.Session,tx,resources)));
            var copy=roles.RequireLoaded(f.Session,tx,resources);copy.ConfigPages[0].Name="不得污染下一次revision";
            Assert.Equal(JsonSerializer.Serialize(normal),JsonSerializer.Serialize(roles.RequireLoaded(f.Session,tx,resources)));
            Assert.Equal(package.Metadata.ConfigMenus.Any(menu=>menu.YzAdminTypeId==f.Admin.ToString()),await current.HasAdminMenuReferencesForUpdateAsync(f.Session,tx,f.Admin,CancellationToken.None));
            // 发布目录即使逻辑等值也没有本事务proof，不能作为current fallback。
            var published=((ISecurityResourceRegistry)package.Registry()).PublishedCatalogs[0];Assert.Throws<InvalidOperationException>(()=>roles.RequireLoaded(f.Session,tx,published));
            using var anotherScope=f.Services.CreateScope();Assert.Throws<InvalidOperationException>(()=>anotherScope.ServiceProvider.GetRequiredService<IRolePermissionCatalog>().RequireLoaded(f.Session,tx,resources));
            return true;
        });
        Assert.Empty(f.Provider.Commands);Assert.Equal(1,f.Provider.Opens);
    }

    /// <summary>同值投影、另一适配、另一Session或事务均不能冒充原目录证明。</summary>
    [Theory]
    [InlineData("equal")][InlineData("another-generation")][InlineData("another-session")][InlineData("another-transaction")][InlineData("finished")]
    public async Task ImmutableCatalog_refuses_forged_cross_scope_and_expired_proofs(string kind)
    {
        using var f=new ImmutableApplication(package);using var scope=f.Services.CreateScope();var current=scope.ServiceProvider.GetRequiredService<ISecurityCurrentResources>();var roles=scope.ServiceProvider.GetRequiredService<IRolePermissionCatalog>();
        SecurityResourceCatalog proof=null!;DbTransaction old=null!;
        await f.Session.InTransactionAsync(async()=>
        {
            old=f.Session.CurrentTransaction!;proof=await current.ReadForUpdateAsync(f.Session,old,f.Project,f.Admin,CancellationToken.None);
            if(kind=="equal")Assert.Throws<InvalidOperationException>(()=>roles.RequireLoaded(f.Session,old,proof with{}));
            if(kind=="another-generation")
            {
                var other=(IRolePermissionCatalog)package.New("ImmutablePackageSecurityResources",f.Store,package.Authority());Assert.Throws<InvalidOperationException>(()=>other.RequireLoaded(f.Session,old,proof));
            }
            if(kind=="another-session")
            {
                using var other=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="offline"},f.Provider.Orm,f.Provider);
                await other.InTransactionAsync(async()=>{Assert.Throws<InvalidOperationException>(()=>roles.RequireLoaded(other,other.CurrentTransaction!,proof));return true;});
            }
            return true;
        });
        if(kind=="finished")Assert.Throws<InvalidOperationException>(()=>roles.RequireLoaded(f.Session,old,proof));
        if(kind=="another-transaction")await f.Session.InTransactionAsync(async()=>{Assert.Throws<InvalidOperationException>(()=>roles.RequireLoaded(f.Session,f.Session.CurrentTransaction!,proof));return true;});
        Assert.Empty(f.Provider.Commands);
    }

    /// <summary>后台/项目必须来自固定包，未知后台的菜单引用也不能默认为false。</summary>
    [Theory]
    [InlineData("project")][InlineData("admin")][InlineData("negative-admin")]
    public async Task ImmutableCatalog_rejects_unknown_realm_before_issuing_proof(string kind)
    {
        using var f=new ImmutableApplication(package);using var scope=f.Services.CreateScope();var current=scope.ServiceProvider.GetRequiredService<ISecurityCurrentResources>();
        await f.Session.InTransactionAsync(async()=>
        {
            var project=kind=="project"?1:f.Project;var admin=kind=="admin"?1:kind=="negative-admin"?-1:f.Admin;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>current.ReadForUpdateAsync(f.Session,f.Session.CurrentTransaction!,project,admin,CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>current.HasAdminMenuReferencesForUpdateAsync(f.Session,f.Session.CurrentTransaction!,1,CancellationToken.None));return true;
        });Assert.Empty(f.Provider.Commands);
    }

    /// <summary>取消先于适配读取；普通只读也不能把另一Session或包外项目当目录来源。</summary>
    [Fact]
    public async Task ImmutableCatalog_cancellation_and_read_scope_fail_without_opening_connection()
    {
        using var f=new ImmutableApplication(package);using var scope=f.Services.CreateScope();var roles=scope.ServiceProvider.GetRequiredService<IRolePermissionCatalog>();var current=scope.ServiceProvider.GetRequiredService<ISecurityCurrentResources>();
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>roles.ReadAsync(f.Session,package.Metadata.YzProjectId,cancel.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>roles.ReadAsync(f.Session,"1",CancellationToken.None));
        using var other=new DatabaseSession(new(){ExpectedDatabase="offline",ConnectionString="offline"},f.Provider.Orm,f.Provider);await Assert.ThrowsAsync<InvalidOperationException>(()=>roles.ReadAsync(other,package.Metadata.YzProjectId,CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>current.ReadForUpdateAsync(f.Session,null!,f.Project,f.Admin,CancellationToken.None));Assert.Equal(0,f.Provider.Opens);
    }

    /// <summary>仅ADO连接/事务替身，不执行SQL；包模板和实际DI仍使用同一个真实DatabaseSession。</summary>
    private sealed class ImmutableApplication:IDisposable
    {
        internal readonly StandaloneMemoryProvider Provider=new();internal readonly DatabaseSession Session;internal readonly SqlRowStore Store;internal readonly ServiceCollection Registrations=new();internal readonly ServiceProvider Services;internal readonly long Project,Admin;
        internal ImmutableApplication(WebAuthenticationPackageFixture fixture)
        {
            Project=long.Parse(fixture.Metadata.YzProjectId);Admin=long.Parse(fixture.Metadata.YzAdminTypes[0].Id);Session=new(new(){ExpectedDatabase="offline",ConnectionString="offline"},Provider.Orm,Provider);Store=new(Session,fixture.Security);
            Registrations.AddSingleton(Session);Registrations.AddSingleton(Store);fixture.RegisterCatalog(Registrations,fixture.Authority());Services=Registrations.BuildServiceProvider();
        }
        public void Dispose(){Services.Dispose();Session.Dispose();}
    }
}
