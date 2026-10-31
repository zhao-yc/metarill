using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Persistence;
using Metarill.Core.Security;
using Metarill.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>可信来源、真实编译及实际A守卫生命周期；离线传输不证明MySQL或独立Full已发布。</summary>
public sealed class TrustedSecurityServicePartialsTests(TrustedSecurityServiceFixture fixture):IClassFixture<TrustedSecurityServiceFixture>
{
    private static readonly string[] Names=["YzManager","YzRole","YzAdminType"];
    /// <summary>普通JSON或同形Binding不能选模板，签发后的编译快照变化也拒绝。</summary>
    [Fact]
    public void Only_factory_issued_context_selects_required_security_templates()
    {
        var catalog=fixture.Models.Published;catalog.ConfigTables[0].Extra["moduleId"]=JsonSerializer.SerializeToElement("security-management-v1");
        var ordinary=GenerationModel.Create(catalog,TrustedSecurityServiceFixture.Namespace);var rendered=BackendRenderer.Render(ordinary).ToArray();
        Assert.DoesNotContain("IAdminAccountInitializer",rendered.Single(file=>file.Path=="GeneratedRegistration.g.cs").Content);
        Assert.DoesNotContain(rendered,file=>file.Path=="Controllers/AdminAccountInitializationController.g.cs");
        var trusted=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        Assert.Contains("IAdminAccountInitializer",BackendRenderer.Render(trusted).Single(file=>file.Path=="GeneratedRegistration.g.cs").Content);
        foreach(var name in Names){Assert.DoesNotContain("RequirePrepared",rendered.Single(file=>file.Path=="Services/"+name+"Service.cs").Content);Assert.DoesNotContain("private partial void",rendered.Single(file=>file.Path=="Services/"+name+"Service.g.cs").Content);}
        var forged=new SecurityManagementModelSet(fixture.Models.Physical,fixture.Models.Published,fixture.Models.Binding,fixture.Models.PendingAdapters);
        Assert.Throws<InvalidOperationException>(()=>new CSharpGenerator().Generate(forged,Path.Combine(fixture.DirectoryPath,"forged"),TrustedSecurityServiceFixture.Namespace));
        var model=TrustedSecurityServiceCompilation.Bind(GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace),fixture.Models);
        model.Catalog.ConfigPages[0].Name+="被改动";Assert.Throws<InvalidOperationException>(()=>BackendRenderer.Render(model).ToArray());
    }
    /// <summary>三服务均使用必需方法与真实预备检查，手写文件逐字保留；可信接线不放宽普通目录门禁。</summary>
    [Fact]
    public void First_creation_preserves_existing_partials_and_required_runtime_guards()
    {
        var output=Path.Combine(fixture.DirectoryPath,"preserve");var generator=new CSharpGenerator();var first=generator.Generate(fixture.Models,output,TrustedSecurityServiceFixture.Namespace);
        var original=new Dictionary<string,byte[]>();foreach(var name in Names)
        {
            var path="Services/"+name+"Service.cs";Assert.Contains(path,first.CreatedHandwrittenFiles);var file=Path.Combine(output,path);Assert.Contains("private partial void BeforeSave",File.ReadAllText(file));File.AppendAllText(file,"\n// 开发者自己的业务扩展，不能覆盖。\n");original[file]=File.ReadAllBytes(file);
            var generated=File.ReadAllText(Path.Combine(output,"Services/"+name+"Service.g.cs"));Assert.Contains("private partial void BeforeSave",generated);Assert.Contains("fixedGuards",generated);Assert.Contains("SecurityRealmMutationGuard.RequirePrepared",generated);Assert.DoesNotContain("Copy"+fixture.Catalog.ConfigPages.Single(page=>page.ConfigTableId==fixture.Catalog.ConfigTables.Single(table=>GenerationModel.Pascal(table.TableName)==name).Id).Id+"Async",generated);
        }
        var second=generator.Generate(fixture.Models,output,TrustedSecurityServiceFixture.Namespace);Assert.Empty(second.CreatedHandwrittenFiles);foreach(var item in original)Assert.Equal(item.Value,File.ReadAllBytes(item.Key));Assert.DoesNotContain(second.Warnings,warning=>warning.Contains("Runtime A",StringComparison.Ordinal));
        var ordinary=GenerationModel.Create(fixture.Models.Published,TrustedSecurityServiceFixture.Namespace);
        Assert.Contains(GenerationCapabilityAudit.Collect(ordinary),gap=>gap.Usage=="runtime"&&gap.Column=="yz_role_ids");
        Assert.Throws<InvalidOperationException>(()=>new FrontendPackager().Validate(ordinary.Catalog));
        var manager=File.ReadAllText(Path.Combine(output,"Services/YzManagerService.g.cs"));Assert.True(manager.IndexOf("BeforeSave(entity, actor, true",StringComparison.Ordinal)<manager.IndexOf("ManagerCredentialValue.RequireDigest",StringComparison.Ordinal));
    }
    /// <summary>旧空壳或缺失实现不能以文件存在假装接好，真实编译器拒绝必需partial。</summary>
    [Theory]
    [InlineData("YzManager",false)][InlineData("YzRole",false)][InlineData("YzAdminType",false)][InlineData("YzManager",true)]
    public async Task Missing_or_old_empty_partial_fails_real_compilation(string name,bool empty)
    {
        var directory=Path.Combine(fixture.DirectoryPath,"compile-negative-"+Guid.NewGuid().ToString("N"));new CSharpGenerator().Generate(fixture.Models,Path.Combine(directory,"generated"),TrustedSecurityServiceFixture.Namespace);
        var path=Path.Combine(directory,"generated/Services/"+name+"Service.cs");if(empty)File.WriteAllText(path,"namespace "+TrustedSecurityServiceFixture.Namespace+".Generated.Services; public partial class "+name+"Service {}\n");else File.Delete(path);
        var result=await TrustedSecurityServiceFixture.Build(directory);Assert.NotEqual(0,result.ExitCode);Assert.Contains("CS8795",result.Output);
    }
    /// <summary>实际AddGenerated构造守卫必须唯一且首位，普通业务服务仍允许无守卫。</summary>
    [Theory]
    [InlineData("missing")][InlineData("fake")][InlineData("reversed")][InlineData("duplicate")]
    public async Task Missing_fake_or_misordered_guards_cannot_construct_trusted_services(string kind)
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var guard=run.Scope.ServiceProvider.GetRequiredService<SecurityRealmMutationGuard>();
        IRecordMutationGuard[] guards=kind switch{"missing"=>[],"fake"=>[new NoopGuard()],"reversed"=>[new NoopGuard(),guard],_=>[guard,guard]};
        foreach(var name in Names)Assert.Throws<InvalidOperationException>(()=>run.Construct(name,guards));
        Assert.Empty(run.Writes);
    }
    /// <summary>原完整账号输入经过真实认证/A协调/Hash/摘要证明/最终归属后才到Mapper，省略密码保持摘要。</summary>
    [Fact]
    public async Task Actual_generated_manager_lifecycle_hashes_and_preserves_omitted_credentials()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var service=run.Service("YzManager");var admin=fixture.Models.Binding.ManagementAdminTypeId;
        var json=JsonSerializer.Serialize(new{name="new-trusted",realName="创建名称",password=TrustedSecurityServiceRun.Password,isDisabled=1,yzAdminTypeId=admin,yzRoleIds=new[]{"10"}});
        var result=await run.Call(service,"Create6031906822946816Async",run.Request("YzManager","6031906822946816","Create",json),null,run.Identity.Actor,CancellationToken.None);
        Assert.Equal("********",result!.GetType().GetProperty("Password")!.GetValue(result));
        var owned=run.Transport.Rows["yz_manager"].Where(row=>Equals(row.GetValueOrDefault("name"),"new-trusted")).ToArray();Assert.True(owned.Length==1,"只能读取本次新建的自有账号一行。");var row=owned[0];var digest=Sql(row,"password");Assert.True(PasswordHasher.Verify(TrustedSecurityServiceRun.Password,digest));Assert.Equal(0,Convert.ToInt32(row["is_disabled"]));
        var update=run.Request("YzManager","6031906822946816","Update",JsonSerializer.Serialize(new{id=Sql(row,"id"),realName="只改名称"}));await run.Call(service,"Update6031906822946816Async",update,null,run.Identity.Actor,CancellationToken.None);
        Assert.True(digest==Sql(row,"password"),"省略口令必须保留已有摘要字节。");Assert.Equal("只改名称",Sql(row,"real_name"));Assert.Equal(2,run.Writes.Count);
        Assert.False(run.Identity.IsRealmSuper);Assert.False(run.Identity.Actor.IsAdministrator);
    }
    /// <summary>普通编辑用户沿原角色页动作可以保存super属性，不因模块下发增加平台super门槛。</summary>
    [Fact]
    public async Task Actual_generated_role_creation_keeps_original_page_permission_rule()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var service=run.Service("YzRole");var request=run.Request("YzRole","1692532858529329154","Create",JsonSerializer.Serialize(new{name="ordinary-can-edit-super",sortNo=10,isSuper=1,yzAdminTypeId=fixture.Models.Binding.ManagementAdminTypeId}));
        await run.Call(service,"Create1692532858529329154Async",request,null,run.Identity.Actor,CancellationToken.None);
        var owned=run.Transport.Rows["yz_role"].Where(row=>Equals(row.GetValueOrDefault("name"),"ordinary-can-edit-super")).ToArray();Assert.True(owned.Length==1,"只能读取本次自有角色一行。");Assert.Equal(1,Convert.ToInt32(owned[0]["is_super"]));Assert.False(run.Identity.IsRealmSuper);Assert.False(run.Identity.Actor.IsAdministrator);
    }

    /// <summary>必需方法即使存在但被人为清空，也只能失败关闭；生成摘要证明阻止原文到达Mapper。</summary>
    [Fact]
    public async Task Empty_manager_hook_cannot_save_plaintext_through_real_create_method()
    {
        var directory=Path.Combine(fixture.DirectoryPath,"empty-method");new CSharpGenerator().Generate(fixture.Models,Path.Combine(directory,"generated"),TrustedSecurityServiceFixture.Namespace);
        System.IO.Directory.CreateDirectory(Path.Combine(directory,"host"));foreach(var path in System.IO.Directory.GetFiles(Path.Combine(fixture.DirectoryPath,"host")))File.Copy(path,Path.Combine(directory,"host",Path.GetFileName(path)));
        var partial=Path.Combine(directory,"generated/Services/YzManagerService.cs");var source=File.ReadAllText(partial);var start=source.IndexOf("    private partial void BeforeSave",StringComparison.Ordinal);var body=source.IndexOf('{',start);var end=source.IndexOf("\n    }",body,StringComparison.Ordinal);File.WriteAllText(partial,source[..(body+1)]+"\n        // 测试清空手写规则，生成层必须独立拒绝原文。"+source[end..]);
        var result=await TrustedSecurityServiceFixture.Build(directory);Assert.True(result.ExitCode==0,result.Output);
        var alternate=new TrustedSecurityServiceFixture{Models=fixture.Models,Catalog=fixture.Catalog,CatalogBytes=fixture.CatalogBytes,Assembly=System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(await File.ReadAllBytesAsync(result.AssemblyPath)))};
        await using var run=await TrustedSecurityServiceRun.Create(alternate);var before=run.Fingerprint();var request=run.Request("YzManager","6031906822946816","Create",JsonSerializer.Serialize(new{name="empty-hook",password=TrustedSecurityServiceRun.Password,isDisabled=1,yzAdminTypeId=fixture.Models.Binding.ManagementAdminTypeId}));
        var failure=await Assert.ThrowsAsync<InvalidOperationException>(()=>run.Call(run.Service("YzManager"),"Create6031906822946816Async",request,null,run.Identity.Actor,CancellationToken.None));Assert.Contains("摘要钩子未完成",failure.Message);Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Writes);
    }

    /// <summary>候选同工作区仍不意味着可写入另一后台，最后真实关系拒绝使整个源行保持不变。</summary>
    [Fact]
    public async Task Final_merged_role_rejection_rolls_back_source_and_never_reaches_mapper()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var project=long.Parse(fixture.Catalog.YzProjectId);run.Add("yz_admin_type",("id",999L),("yz_project_id",project),("name","其他后台"));run.Add("yz_role",("id",11L),("name","其他后台角色"),("yz_admin_type_id",999L),("yz_project_id",project));
        var before=run.Fingerprint();var request=run.Request("YzManager","6031906822946816","Create",JsonSerializer.Serialize(new{name="must-rollback",password=TrustedSecurityServiceRun.Password,isDisabled=1,yzAdminTypeId=fixture.Models.Binding.ManagementAdminTypeId,yzRoleIds=new[]{"11"}}));
        await Assert.ThrowsAsync<ArgumentException>(()=>run.Call(run.Service("YzManager"),"Create6031906822946816Async",request,null,run.Identity.Actor,CancellationToken.None));Assert.Equal(before,run.Fingerprint());Assert.Empty(run.Writes);
    }
    /// <summary>真实pending不能用于另一目标/身份/动作/事务；原List被修改也不能删掉最终必需协调器。</summary>
    [Fact]
    public async Task Prepared_proof_and_fixed_snapshot_are_checked_again_at_final_write()
    {
        await using var run=await TrustedSecurityServiceRun.Create(fixture);var guard=run.Scope.ServiceProvider.GetRequiredService<SecurityRealmMutationGuard>();var mutable=new List<IRecordMutationGuard>{guard};var service=run.Construct("YzRole",mutable);mutable.Clear();
        var role=run.Existing("YzRole",10);role.GetType().GetProperty("YzAdminTypeId")!.SetValue(role,long.Parse(fixture.Models.Binding.ManagementAdminTypeId));role.GetType().GetProperty("IsSuper")!.SetValue(role,0);
        await run.Session.InTransactionAsync(async()=>
        {
            Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",role,run.Identity.Actor,false,new HashSet<string>()));
            await run.Call(service,"PrepareMutationAsync","1692532858529329154",RecordMutationOperation.Update,run.Identity.Actor,CancellationToken.None,10L,new Dictionary<string,object?>());
            TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",role,run.Identity.Actor,false,new HashSet<string>());
            Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",run.Entity("YzRole",12),run.Identity.Actor,false,new HashSet<string>()));
            Assert.Throws<UnauthorizedAccessException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",role,run.Identity.Actor with{AccountId=99},false,new HashSet<string>()));
            Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeDelete",role,run.Identity.Actor));
            Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",role,run.Identity.Actor,true,new HashSet<string>()));
            await using(var other=await TrustedSecurityServiceRun.Create(fixture))
            {
                var wrongTransactionService=other.Construct("YzRole",[guard]);
                await other.Session.InTransactionAsync(()=>{Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(wrongTransactionService,"BeforeSave",role,other.Identity.Actor,false,new HashSet<string>()));return Task.FromResult(true);});
            }
            var original=TrustedSecurityServiceFixture.Invoke(service,"MutationScopeValues",role);
            await run.Call(service,"ValidateMutationAsync","1692532858529329154",RecordMutationOperation.Update,role,run.Identity.Actor,original,new HashSet<string>(),CancellationToken.None);
            Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",role,run.Identity.Actor,false,new HashSet<string>()));return true;
        });
        await run.Session.InTransactionAsync(()=>{Assert.Throws<InvalidOperationException>(()=>TrustedSecurityServiceFixture.Invoke(service,"BeforeSave",role,run.Identity.Actor,false,new HashSet<string>()));return Task.FromResult(true);});Assert.Empty(run.Writes);
    }
    /// <summary>只取已知键用于布尔/数量断言，不把账号整行或摘要打印到失败输出。</summary>
    private static string Sql(IReadOnlyDictionary<string,object?> row,string key)=>Convert.ToString(row.GetValueOrDefault(key),System.Globalization.CultureInfo.InvariantCulture)??"";
    private sealed class NoopGuard:IRecordMutationGuard{public Task BeforeMutationAsync(RecordMutationPreparation preparation,CancellationToken cancellationToken=default)=>Task.CompletedTask;}
}
