using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>固定原壳只能缺失时首次创建，计划/日志与恢复仍绑定本机确认，不提升为可覆盖运行时。</summary>
public sealed class LocalGenerationUpdateShellCreationTests : IAsyncDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"metarill-shell-create",Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    /// <summary>模拟旧包未提供的新骨架；两个原动作仍分别确认，不自动扩大配置与运行时scope。</summary>
    [Fact]
    public void Missing_unregistered_shells_are_created_only_by_explicit_runtime_scope_and_existing_app_is_preserved()
    {
        var (target,candidate)=Packages();var paths=new[]{"frontend/common/project.js","frontend/pages/main/main.vue","frontend/pages/main/change_password.vue"};
        foreach(var path in paths)Unregister(target,path);
        var pagesPath=Path.Combine(target,"frontend/pages.json");var pages=JsonNode.Parse(File.ReadAllText(pagesPath))!;var routes=pages["pages"]!.AsArray();routes.Remove(routes.Single(route=>route!["path"]!.GetValue<string>()=="pages/main/change_password"));File.WriteAllText(pagesPath,pages.ToJsonString(Json)+"\n");UpdateRegisteredHash(target,"frontend/pages.json");
        var appPath=Path.Combine(target,"frontend/App.vue");File.AppendAllText(appPath,"\n<!-- 用户自有App保留 -->\n");var app=File.ReadAllBytes(appPath);var updater=new LocalGenerationUpdater();
        var configOnly=updater.Preview(target,candidate,["generate-project-config"]);Assert.False(configOnly.CanApply);Assert.Contains("sync-runtime",configOnly.RequiredActions);
        var runtimeOnly=updater.Preview(target,candidate,["sync-runtime"]);Assert.False(runtimeOnly.CanApply);Assert.Contains("generate-project-config",runtimeOnly.RequiredActions);
        var plan=updater.Preview(target,candidate,["generate-project-config","sync-runtime"]);Assert.True(plan.CanApply);Assert.Empty(plan.RequiredActions);
        foreach(var path in paths)Assert.Contains(plan.Files,file=>file.Path==path&&file.Action=="create"&&file.Scope=="sync-runtime"&&file.BeforeSha256 is null);
        Assert.DoesNotContain(plan.Files,file=>file.Path=="frontend/manifest.json"&&file.Action!="preserve"||file.Path=="frontend/common/application.config.js"&&file.Action!="preserve"||file.Path.Contains("credentials",StringComparison.Ordinal));
        updater.Apply(plan,plan.PlanSha256);Assert.Equal(app,File.ReadAllBytes(appPath));foreach(var path in paths)Assert.Equal(File.ReadAllBytes(Path.Combine(candidate,path)),File.ReadAllBytes(Path.Combine(target,path)));
        var repeated=updater.Preview(target,candidate,["generate-project-config","sync-runtime"]);Assert.True(repeated.CanApply);Assert.All(repeated.Files,file=>Assert.Equal("preserve",file.Action));
    }
    /// <summary>声明过但已丢失的原壳也只create；真实中断后拒绝第三态和篡改日志，确证本次字节才撤除新文件。</summary>
    [Fact]
    public void Missing_shell_create_has_strict_journal_and_recovers_without_overwriting_later_user_edit()
    {
        var (target,candidate)=Packages();const string relative="frontend/common/project.js";var file=Path.Combine(target,relative);File.Delete(file);
        var updater=new LocalGenerationUpdater{AfterWrite=path=>{if(path==relative){File.WriteAllText(file,"// 用户后来写入，恢复不得覆盖\n");throw new IOException("测试故障");}}};var plan=updater.Preview(target,candidate,["sync-runtime"]);
        Assert.True(plan.CanApply);Assert.Equal("create",Assert.Single(plan.Files,item=>item.Action!="preserve").Action);Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));
        var activePath=Path.Combine(target,".local/metarill-updates/active.json");var active=JsonNode.Parse(File.ReadAllText(activePath))!;var id=active["transactionId"]!.GetValue<string>();var journalPath=Path.Combine(target,".local/metarill-updates/transactions",id,"journal.json");var journalText=File.ReadAllText(journalPath);var journal=JsonNode.Parse(journalText)!;var created=Assert.Single(journal["files"]!.AsArray());
        Assert.Equal(relative,created!["path"]!.GetValue<string>());Assert.Equal("create",created["action"]!.GetValue<string>());Assert.Equal("sync-runtime",created["scope"]!.GetValue<string>());Assert.Null(created["beforeSha256"]);
        var later=File.ReadAllBytes(file);Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256));Assert.Equal(later,File.ReadAllBytes(file));Assert.True(File.Exists(activePath));
        foreach(var mutation in new[]{"replace","delete","scope","before","missing-proof","wrong-proof","duplicate-proof"})
        {
            var bad=JsonNode.Parse(journalText)!;var entry=bad["files"]![0]!;
            if(mutation=="scope")entry["scope"]="generate-all-page";else if(mutation=="before")entry["beforeSha256"]=new string('a',64);
            else if(mutation=="missing-proof")bad.AsObject().Remove("confirmedFrontendCreates");else if(mutation=="wrong-proof")bad["confirmedFrontendCreates"]=new JsonArray(-1);else if(mutation=="duplicate-proof")bad["confirmedFrontendCreates"]=new JsonArray(0,0);else entry["action"]=mutation;
            File.WriteAllText(journalPath,bad.ToJsonString(Json));Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256));Assert.Equal(later,File.ReadAllBytes(file));
        }
        File.WriteAllText(journalPath,journalText);File.WriteAllBytes(file,File.ReadAllBytes(Path.Combine(candidate,relative)));
        var receipt=new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256);Assert.Equal("rolled-back",receipt.Status);Assert.False(File.Exists(file));Assert.False(File.Exists(activePath));
        Assert.True(new LocalGenerationUpdater().Preview(target,candidate,["sync-runtime"]).CanApply);
    }
    /// <summary>在最后摘要预检之后真正创建目标，最终原子发布不得覆盖，自动回滚/显式恢复均不得删除第三方文件。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void File_appearing_after_create_precheck_is_not_overwritten_or_deleted_by_rollback(bool sameBytesAsCandidate)
    {
        var (target,candidate)=Packages();const string relative="frontend/common/project.js";var file=Path.Combine(target,relative);File.Delete(file);var later=sameBytesAsCandidate?File.ReadAllText(Path.Combine(candidate,relative)):"export default { business: '最后预检后的第三方文件' };\n";var wrote=false;
        var updater=new LocalGenerationUpdater{BeforeWrite=path=>{if(path==relative)File.WriteAllText(file,later);},AfterWrite=path=>{if(path==relative)wrote=true;}};var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.True(plan.CanApply);
        Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.False(wrote);Assert.Equal(later,File.ReadAllText(file));
        var active=JsonNode.Parse(File.ReadAllText(Path.Combine(target,".local/metarill-updates/active.json")))!;var id=active["transactionId"]!.GetValue<string>();var journal=JsonNode.Parse(File.ReadAllText(Path.Combine(target,".local/metarill-updates/transactions",id,"journal.json")))!;Assert.Equal("manual-recovery",journal["status"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256));Assert.Equal(later,File.ReadAllText(file));Assert.True(File.Exists(Path.Combine(target,".local/metarill-updates/active.json")));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(file)!,"*.metarill-update-*"));
        var reviewed=Path.Combine(root,"reviewed-project.js");File.Move(file,reviewed);Assert.Equal("rolled-back",new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256).Status);Assert.Equal(later,File.ReadAllText(reviewed));Assert.False(File.Exists(file));
    }
    /// <summary>真实发布已完成但归属确认未刷盘即中断时，不凭候选同字节误认所有权。</summary>
    [Fact]
    public void Published_shell_without_durable_confirmation_requires_manual_review_before_recovery()
    {
        var (target,candidate)=Packages();const string relative="frontend/common/project.js";var file=Path.Combine(target,relative);File.Delete(file);
        var updater=new LocalGenerationUpdater{AfterCreatePublication=path=>{if(path==relative)throw new IOException("模拟发布后确认前中断");}};var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.Equal(File.ReadAllBytes(Path.Combine(candidate,relative)),File.ReadAllBytes(file));
        var active=JsonNode.Parse(File.ReadAllText(Path.Combine(target,".local/metarill-updates/active.json")))!;var id=active["transactionId"]!.GetValue<string>();var journal=JsonNode.Parse(File.ReadAllText(Path.Combine(target,".local/metarill-updates/transactions",id,"journal.json")))!;Assert.Equal(3,journal["schemaVersion"]!.GetValue<int>());Assert.Empty(journal["confirmedFrontendCreates"]!.AsArray());
        Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256));Assert.True(File.Exists(file));var reviewed=Path.Combine(root,"reviewed-published.js");File.Move(file,reviewed);Assert.Equal("rolled-back",new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256).Status);Assert.Equal(File.ReadAllBytes(Path.Combine(candidate,relative)),File.ReadAllBytes(reviewed));Assert.False(File.Exists(file));
    }
    /// <summary>旧包尚未登记但开发者已自行创建的同名扩展仍保留，不因补建能力重新宣称归属或覆盖。</summary>
    [Fact]
    public void Existing_unregistered_project_extension_is_never_created_replaced_or_claimed()
    {
        var (target,candidate)=Packages();const string relative="frontend/common/project.js";Unregister(target,relative);var file=Path.Combine(target,relative);File.WriteAllText(file,"export default { customBusiness() { return '用户业务'; } };\n");var before=File.ReadAllBytes(file);
        var updater=new LocalGenerationUpdater();var plan=updater.Preview(target,candidate,["sync-runtime"]);Assert.True(plan.CanApply);Assert.Contains(plan.Files,item=>item.Path==relative&&item.Action=="preserve");Assert.Equal("unchanged",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(before,File.ReadAllBytes(file));
        var marker=JsonNode.Parse(File.ReadAllText(Path.Combine(target,".metarill-package.json")))!;Assert.DoesNotContain(marker["files"]!.AsArray(),entry=>entry!["path"]!.GetValue<string>()==relative);
    }
    /// <summary>每个用例使用本任务GUID目录中的真实Full包，完全不连接数据库。</summary>
    private (string Target,string Candidate) Packages([CallerFilePath]string sourceFile="")
    {
        var source=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!,"../../.."));var metadata=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");new ApplicationPackager().Create(metadata,target,"Example.FirstShell",source);new ApplicationPackager().Create(metadata,candidate,"Example.FirstShell",source);return(target,candidate);
    }
    /// <summary>只在测试副本移除未曾交付的宿主文件及两层归属，生成元数据和业务文件不变。</summary>
    private static void Unregister(string target,string relative)
    {
        File.Delete(Path.Combine(target,relative));var frontendPath=Path.Combine(target,"frontend/.metarill-frontend.json");var frontend=JsonNode.Parse(File.ReadAllText(frontendPath))!;var frontendFiles=frontend["files"]!.AsArray();frontendFiles.Remove(frontendFiles.Single(entry=>entry!["path"]!.GetValue<string>()==relative["frontend/".Length..]));File.WriteAllText(frontendPath,frontend.ToJsonString(Json)+"\n");
        var packagePath=Path.Combine(target,".metarill-package.json");var package=JsonNode.Parse(File.ReadAllText(packagePath))!;var entries=package["files"]!.AsArray();entries.Remove(entries.Single(entry=>entry!["path"]!.GetValue<string>()==relative));File.WriteAllText(packagePath,package.ToJsonString(Json)+"\n");UpdateRegisteredHash(target,"frontend/.metarill-frontend.json");
    }
    /// <summary>测试中的旧宿主配置变化同步更新原清单，避免凭无效hash触发错误而假绿。</summary>
    private static void UpdateRegisteredHash(string target,string relative)
    {
        var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(target,relative))));var packagePath=Path.Combine(target,".metarill-package.json");var package=JsonNode.Parse(File.ReadAllText(packagePath))!;package["files"]!.AsArray().Single(entry=>entry!["path"]!.GetValue<string>()==relative)!["sha256"]=hash;File.WriteAllText(packagePath,package.ToJsonString(Json)+"\n");
        if(relative=="frontend/.metarill-frontend.json")return;
        var frontendPath=Path.Combine(target,"frontend/.metarill-frontend.json");var frontend=JsonNode.Parse(File.ReadAllText(frontendPath))!;frontend["files"]!.AsArray().Single(entry=>entry!["path"]!.GetValue<string>()==relative["frontend/".Length..])!["sha256"]=hash;File.WriteAllText(frontendPath,frontend.ToJsonString(Json)+"\n");UpdateRegisteredHash(target,"frontend/.metarill-frontend.json");
    }
    public async ValueTask DisposeAsync()=>await TestDirectoryCleanup.DeleteAsync(root,Path.Combine(Path.GetTempPath(),"metarill-shell-create"));
}
