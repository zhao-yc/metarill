using System.Text.Json.Nodes;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class ManagementGenerationUpdateTests
{
    /// <summary>真实管理包同时升级两端，只写选中范围；第四份来源清单及另一端旧归属仍有效。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Managed_runtime_parts_preserve_excluded_source_and_allow_remaining_end(bool backend,bool frontend)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-parts");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var(target,candidate)=ManagedPartPackages(root,source);var paths=new[]{"src/Metarill.Core/Contracts/R.cs","frontend/common/utils.js"};
            var old=paths.ToDictionary(path=>path,path=>File.ReadAllBytes(Path.Combine(target,path)));var oldOwners=paths.ToDictionary(path=>path,path=>ManagedPartOwner(target,path));
            var metadata=PreparedSecurityManagementSource.Read(target);var before=Snapshot(target);var updater=new LocalGenerationUpdater();var parts=new GenerationRuntimeParts(backend,frontend);
            var plan=updater.Preview(target,candidate,["sync-runtime"],parts);Assert.True(plan.CanApply);Assert.Equal(2,plan.SchemaVersion);Assert.Equal(parts,plan.RuntimeParts);Assert.Equal(before,Snapshot(target));
            var planPath=Path.Combine(root,"plan.json");LocalGenerationUpdater.WritePlan(plan,planPath);plan=LocalGenerationUpdater.ReadPlan(planPath);Assert.Equal(parts,plan.RuntimeParts);
            foreach(var path in paths)Assert.Equal(path.StartsWith("frontend/",StringComparison.Ordinal)?frontend:backend,plan.Files.Any(file=>file.Path==path&&file.Action=="replace"));
            Assert.DoesNotContain(plan.Files,file=>file.Action!="preserve"&&file.Scope!="manifest"&&file.Scope!="sync-runtime");
            Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal("committed",new LocalGenerationUpdater().InspectApplicationStatus(target,plan,plan.PlanSha256).Status);
            foreach(var path in paths)
            {
                var selected=path.StartsWith("frontend/",StringComparison.Ordinal)?frontend:backend;
                Assert.Equal(selected?File.ReadAllBytes(Path.Combine(candidate,path)):old[path],File.ReadAllBytes(Path.Combine(target,path)));
                Assert.Equal(selected?ManagedPartOwner(candidate,path):oldOwners[path],ManagedPartOwner(target,path));
            }
            var after=PreparedSecurityManagementSource.Read(target);Assert.Equal(metadata.ApplicationMetadataSha256,after.ApplicationMetadataSha256);Assert.Equal(metadata.Module.Binding,after.Module.Binding);Assert.Equal(metadata.RootNamespace,after.RootNamespace);
            var repeat=updater.Preview(target,candidate,["sync-runtime"],parts);Assert.True(repeat.CanApply);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
            if(!backend||!frontend)
            {
                var remaining=updater.Preview(target,candidate,["sync-runtime"],new(!backend,!frontend));Assert.True(remaining.CanApply);Assert.Equal("committed",updater.Apply(remaining,remaining.PlanSha256).Status);
            }
            foreach(var path in paths)Assert.Equal(File.ReadAllBytes(Path.Combine(candidate,path)),File.ReadAllBytes(Path.Combine(target,path)));
            Assert.True(updater.Preview(target,candidate,["sync-runtime"]).CanApply);Assert.Equal(metadata.ApplicationMetadataSha256,PreparedSecurityManagementSource.Read(target).ApplicationMetadataSha256);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>管理包分端写入后中断，独立更新器必须核实已回滚，并保持第四份来源清单有效。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)]
    public async Task Managed_runtime_parts_failure_restores_source_and_all_ownership(bool backend,bool frontend)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-parts");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var(target,candidate)=ManagedPartPackages(root,source);var before=Snapshot(target);var observed=false;var selected=frontend?"frontend/common/utils.js":"src/Metarill.Core/Contracts/R.cs";
            var updater=new LocalGenerationUpdater{AfterWrite=path=>{if(path==selected){observed=true;throw new IOException("管理分端替换后中断");}}};
            var plan=updater.Preview(target,candidate,["sync-runtime"],new(backend,frontend));Assert.True(plan.CanApply);
            Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.True(observed);Assert.Equal(before,Snapshot(target));
            Assert.Equal("rolled-back",new LocalGenerationUpdater().InspectApplicationStatus(target,plan,plan.PlanSha256).Status);
            Assert.Equal(Catalog(source).YzProjectId,new LocalGenerationUpdater().InspectTarget(target).ProjectId);_=PreparedSecurityManagementSource.Read(target);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>两个独占完整管理包仅模拟已有运行时版本差异，不加载私有配置或访问数据库。</summary>
    private static (string Target,string Candidate) ManagedPartPackages(string root,string source)
    {
        var metadata=Catalog(source);var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");var builder=new ApplicationPackager();
        builder.CreateManagement(metadata,target,"Example.ManagedParts",source,metadata.YzAdminTypes[0].Id);builder.CreateManagement(metadata,candidate,"Example.ManagedParts",source,metadata.YzAdminTypes[0].Id);
        foreach(var path in new[]{"src/Metarill.Core/Contracts/R.cs","frontend/common/utils.js"})
        {
            File.AppendAllText(Path.Combine(candidate,path),"\n// 管理工程分端新版运行时。\n");
            if(path.StartsWith("frontend/",StringComparison.Ordinal))
            {
                var marker=Path.Combine(candidate,"frontend/.metarill-frontend.json");var json=JsonNode.Parse(File.ReadAllText(marker))!;
                json["files"]!.AsArray().Single(file=>file!["path"]!.GetValue<string>()==path["frontend/".Length..])!["sha256"]=Hash(File.ReadAllBytes(Path.Combine(candidate,path)));
                File.WriteAllText(marker,json.ToJsonString());Rehash(candidate,"frontend/.metarill-frontend.json");
            }
            Rehash(candidate,path);
        }
        return(target,candidate);
    }

    /// <summary>比较真实归属记录内容，允许整个清单按规范排序，不用候选摘要替代未选端旧归属。</summary>
    private static string ManagedPartOwner(string root,string relative)
    {
        var package=JsonNode.Parse(File.ReadAllText(Path.Combine(root,".metarill-package.json")))!;
        var value=package["files"]!.AsArray().Single(file=>file!["path"]!.GetValue<string>()==relative)!.ToJsonString();
        if(!relative.StartsWith("frontend/",StringComparison.Ordinal))return value;
        var frontend=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"frontend/.metarill-frontend.json")))!;
        return value+"\n"+frontend["files"]!.AsArray().Single(file=>file!["path"]!.GetValue<string>()==relative["frontend/".Length..])!.ToJsonString();
    }
}
