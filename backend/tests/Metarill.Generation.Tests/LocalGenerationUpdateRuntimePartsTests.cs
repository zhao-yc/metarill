using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Generation;
using Xunit;

namespace Metarill.Generation.Tests;

public sealed partial class LocalGenerationUpdateTests
{
    /// <summary>三个原单项目动作的v1封套/计划不多写null；确认摘要仍等于原字段顺序的字节。</summary>
    [Theory]
    [InlineData("generate-project-config")][InlineData("sync-runtime")][InlineData("generate-all-page")]
    public void Runtime_parts_absent_preserves_original_v1_request_bytes(string action)
    {
        var request=LocalGenerationUpdater.CreateRequest(Catalog(),action);
        var old=new{request.Type,request.SchemaVersion,request.Action,request.ProjectId,request.MetadataSha256,request.Metadata,RequestSha256=""};
        Assert.Equal(1,request.SchemaVersion);Assert.Null(request.RuntimeParts);
        Assert.Equal(ProtocolHash(old),request.RequestSha256);
        Assert.DoesNotContain("runtimeParts",JsonSerializer.Serialize(request,Json),StringComparison.Ordinal);
    }

    /// <summary>范围是摘要的一部分；只允许运行时v2，缺字段、未知字段、重复字段与更改版本均拒绝。</summary>
    [Theory]
    [InlineData("missing-backend")][InlineData("unknown")][InlineData("duplicate")][InlineData("version")][InlineData("parts-null")][InlineData("both-false")][InlineData("tamper")]
    public void Runtime_parts_request_rejects_ambiguous_or_tampered_scope(string scenario)
    {
        Directory.CreateDirectory(root);var request=LocalGenerationUpdater.CreateRequest(Catalog(),"sync-runtime",new(true,false));
        var node=JsonSerializer.SerializeToNode(request,Json)!;
        switch(scenario)
        {
            case "missing-backend":node["runtimeParts"]!.AsObject().Remove("backend");break;
            case "unknown":node["runtimeParts"]!["path"]="outside";break;
            case "version":node["schemaVersion"]=1;break;
            case "parts-null":node["runtimeParts"]=null;break;
            case "both-false":node["runtimeParts"]!["backend"]=false;break;
            case "tamper":node["runtimeParts"]!["frontend"]=true;break;
        }
        var text=node.ToJsonString(Json);
        if(scenario=="duplicate")text=text.Replace("\"backend\": true","\"backend\": true,\"backend\": true",StringComparison.Ordinal);
        var path=Path.Combine(root,"request.json");File.WriteAllText(path,text);
        Assert.ThrowsAny<Exception>(()=>LocalGenerationUpdater.ReadRequest(path));
    }

    /// <summary>分端范围不能顺带确认配置或页面，也不能选择空范围；校验先于本机目录访问。</summary>
    [Theory]
    [InlineData("generate-project-config",true,false)][InlineData("generate-all-page",false,true)][InlineData("sync-runtime",false,false)]
    public void Runtime_parts_rejects_other_actions_or_empty_selection(string action,bool backend,bool frontend)
    {
        var parts=new GenerationRuntimeParts(backend,frontend);
        Assert.Throws<InvalidOperationException>(()=>LocalGenerationUpdater.CreateRequest(Catalog(),action,parts));
        Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Preview("absent-target","absent-candidate",[action],parts));
    }

    /// <summary>实际两个完整包同时有前后端替换/新增/删除，只写所选端；排除端文件与归属保持可继续更新。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public void Runtime_parts_update_preserves_excluded_files_and_manifest_ownership(bool backend,bool frontend)
    {
        var(target,candidate)=RuntimePartPackages();var updater=new LocalGenerationUpdater();var parts=new GenerationRuntimeParts(backend,frontend);
        var old=PartFileSnapshot(target);var before=SourceSnapshot(target);
        var plan=updater.Preview(target,candidate,["sync-runtime"],parts);
        Assert.True(plan.CanApply);Assert.Equal(2,plan.SchemaVersion);Assert.Equal(parts,plan.RuntimeParts);Assert.Equal(before,SourceSnapshot(target));
        var planPath=Path.Combine(root,"parts-plan.json");LocalGenerationUpdater.WritePlan(plan,planPath);plan=LocalGenerationUpdater.ReadPlan(planPath);
        Assert.Equal(parts,plan.RuntimeParts);
        foreach(var prefix in new[]{"frontend/","src/"})
        {
            var selected=prefix=="frontend/"?frontend:backend;
            Assert.Equal(selected,plan.Files.Any(file=>file.Path.StartsWith(prefix,StringComparison.Ordinal)&&file.Action!="preserve"&&file.Scope!="manifest"));
        }
        Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);
        Assert.Equal("committed",updater.InspectApplicationStatus(target,plan,plan.PlanSha256).Status);
        foreach(var(path,bytes) in old)
        {
            var selected=path.StartsWith("frontend/",StringComparison.Ordinal)?frontend:backend;
            if(!selected)Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(target,path)));
        }
        foreach(var prefix in new[]{"frontend/common/yzp_runtime/","src/Metarill.Core/"})
        {
            var selected=prefix.StartsWith("frontend",StringComparison.Ordinal)?frontend:backend;
            Assert.Equal(selected,File.Exists(Path.Combine(target,prefix+"PartAdded."+(prefix.StartsWith("frontend",StringComparison.Ordinal)?"js":"cs"))));
            Assert.Equal(!selected,File.Exists(Path.Combine(target,prefix+"PartDeleted."+(prefix.StartsWith("frontend",StringComparison.Ordinal)?"js":"cs"))));
        }
        var repeat=updater.Preview(target,candidate,["sync-runtime"],parts);Assert.True(repeat.CanApply);Assert.Equal("unchanged",updater.Apply(repeat,repeat.PlanSha256).Status);
        // 所选端完成后，另一端旧清单没有被候选整包接管，仍可独立预览并真实应用。
        if(!backend||!frontend)
        {
            var remaining=updater.Preview(target,candidate,["sync-runtime"],new(!backend,!frontend));Assert.True(remaining.CanApply);
            Assert.Equal("committed",updater.Apply(remaining,remaining.PlanSha256).Status);
        }
        Assert.True(updater.Preview(target,candidate,["sync-runtime"]).CanApply);
    }

    /// <summary>应用替换后故障仍按原事务回滚；持久化v2计划能被独立状态入口核验。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)]
    public void Runtime_parts_rolls_back_and_reports_actual_files(bool backend,bool frontend)
    {
        var(target,candidate)=RuntimePartPackages();var before=SourceSnapshot(target);
        var updater=new LocalGenerationUpdater{AfterWrite=_=>throw new IOException("分端测试替换后失败")};
        var plan=updater.Preview(target,candidate,["sync-runtime"],new(backend,frontend));
        Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.Equal(before,SourceSnapshot(target));
        Assert.Equal("rolled-back",new LocalGenerationUpdater().InspectApplicationStatus(target,plan,plan.PlanSha256).Status);
    }

    /// <summary>即使攻击者重算计划摘要，未选端写入、配置动作或版本降级仍在任何源码写入前拒绝。</summary>
    [Theory]
    [InlineData("scope")][InlineData("action")][InlineData("version")][InlineData("manifest-disguise")]
    public void Runtime_parts_plan_rejects_rehashed_expansion_before_apply_or_status(string scenario)
    {
        var(target,candidate)=RuntimePartPackages();var updater=new LocalGenerationUpdater();var before=SourceSnapshot(target);
        var plan=updater.Preview(target,candidate,["sync-runtime"],new(true,true));
        plan=scenario switch{"scope"=>plan with{RuntimeParts=new(true,false)},"action"=>plan with{Actions=["generate-project-config"]},"manifest-disguise"=>plan with{RuntimeParts=new(true,false),Files=plan.Files.Select(file=>file.Path.StartsWith("frontend/",StringComparison.Ordinal)&&file.Scope=="sync-runtime"?file with{Scope="manifest"}:file).ToArray()},_=>plan with{SchemaVersion=1}};
        plan=plan with{PlanSha256=ProtocolHash(plan with{PlanSha256=""})};
        Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));
        Assert.Throws<InvalidOperationException>(()=>updater.InspectApplicationStatus(target,plan,plan.PlanSha256));Assert.Equal(before,SourceSnapshot(target));
    }

    /// <summary>真实离线准备把范围传至计划，候选同时有两端变化也只应用所选端。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)]
    public void Runtime_parts_request_preparation_carries_scope_to_real_plan(bool backend,bool frontend)
    {
        var(target,_)=Packages(Catalog(),Catalog());var source=PublicSource(target);
        File.AppendAllText(Path.Combine(source,"frontend/metarill_uni_admin/common/utils.js"),"\n// 分端前端新版\n");
        File.AppendAllText(Path.Combine(source,"backend/src/Metarill.Core/Contracts/R.cs"),"\n// 分端后端新版\n");
        var request=LocalGenerationUpdater.CreateRequest(Catalog(),"sync-runtime",new(backend,frontend));
        var requestPath=Path.Combine(root,"parts-request.json");File.WriteAllText(requestPath,JsonSerializer.Serialize(request,Json));
        var updater=new LocalGenerationUpdater();var result=updater.PrepareRequest(requestPath,target,Path.Combine(root,"prepared"),source,Path.Combine(root,"plan.json"));
        Assert.Equal("preview-ready",result.Status);Assert.Equal(request.RuntimeParts,result.Plan.RuntimeParts);Assert.Equal(2,result.Plan.SchemaVersion);
        Assert.Equal(backend,result.Plan.Files.Any(file=>file.Path=="src/Metarill.Core/Contracts/R.cs"&&file.Action=="replace"));
        Assert.Equal(frontend,result.Plan.Files.Any(file=>file.Path=="frontend/common/utils.js"&&file.Action=="replace"));
        Assert.Equal("committed",updater.Apply(result.Plan,result.Plan.PlanSha256).Status);
    }

    /// <summary>前端旧壳缺失时，后端单端不得补建；前端单端只首次创建并保留本地其他壳。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)]
    public void Runtime_parts_respects_missing_frontend_shell_creation(bool backend,bool frontend)
    {
        var(target,candidate)=Packages(Catalog(),Catalog());const string shell="frontend/pages/main/my.vue";
        File.Delete(Path.Combine(target,shell));var updater=new LocalGenerationUpdater();var plan=updater.Preview(target,candidate,["sync-runtime"],new(backend,frontend));
        Assert.True(plan.CanApply);Assert.Equal(frontend,plan.Files.Any(file=>file.Path==shell&&file.Action=="create"));
        updater.Apply(plan,plan.PlanSha256);Assert.Equal(frontend,File.Exists(Path.Combine(target,shell)));
    }

    /// <summary>分端只限制运行时，不会借此消除元数据/页面依赖或自动确认其他按钮。</summary>
    [Theory]
    [InlineData(true,false)][InlineData(false,true)]
    public void Runtime_parts_does_not_skip_configuration_dependencies(bool backend,bool frontend)
    {
        var next=Catalog();next.YzProjectName="另一份元数据";var(target,candidate)=Packages(Catalog(),next);var before=SourceSnapshot(target);var updater=new LocalGenerationUpdater();
        var plan=updater.Preview(target,candidate,["sync-runtime"],new(backend,frontend));
        Assert.False(plan.CanApply);Assert.Contains("generate-project-config",plan.RequiredActions);Assert.Equal(["sync-runtime"],plan.Actions);
        Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.Equal(before,SourceSnapshot(target));
    }

    /// <summary>无分端的旧计划按原字段顺序编码，新增可选字段不得令旧确认摘要失效。</summary>
    [Fact]
    public void Runtime_parts_absent_preserves_original_v1_plan_bytes()
    {
        var plan=new GenerationUpdatePlan(1,"target","candidate","1","Example.App",["sync-runtime"],[],true,new string('1',64),new string('2',64),[],"");
        var old=new{plan.SchemaVersion,plan.TargetDirectory,plan.CandidateDirectory,plan.ProjectId,plan.RootNamespace,plan.Actions,plan.RequiredActions,plan.CanApply,plan.TargetFingerprint,plan.CandidateFingerprint,plan.Files,plan.PlanSha256};
        Assert.Equal(ProtocolHash(old),ProtocolHash(plan));Assert.DoesNotContain("runtimeParts",JsonSerializer.Serialize(plan,Json),StringComparison.Ordinal);
    }

    /// <summary>两份实际完整包登记受管新增和删除；只改变受控测试字节，不接触开发者工程。</summary>
    private (string Target,string Candidate) RuntimePartPackages()
    {
        var(target,candidate)=Packages(Catalog(),Catalog());
        foreach(var folder in new[]{target,candidate})
            foreach(var prefix in new[]{"frontend/common/yzp_runtime/","src/Metarill.Core/"})
                RuntimePartEntry(folder,prefix+"PartDeleted."+(prefix.StartsWith("frontend",StringComparison.Ordinal)?"js":"cs"),"// 可选旧运行时\n");
        foreach(var prefix in new[]{"frontend/common/yzp_runtime/","src/Metarill.Core/"})
        {
            var extension=prefix.StartsWith("frontend",StringComparison.Ordinal)?"js":"cs";
            RuntimePartEntry(candidate,prefix+"PartDeleted."+extension,null);RuntimePartEntry(candidate,prefix+"PartAdded."+extension,"// 可选新运行时\n");
        }
        foreach(var path in new[]{"frontend/common/utils.js","src/Metarill.Core/Contracts/R.cs"})
            RuntimePartEntry(candidate,path,File.ReadAllText(Path.Combine(candidate,path))+"\n// 新版替换\n");
        return(target,candidate);
    }
    /// <summary>测试包的外层和前端清单同时按实际字节登记，删除时同步移除两层归属。</summary>
    private static void RuntimePartEntry(string directory,string path,string? content)
    {
        var full=Path.Combine(directory,path);if(content is null)File.Delete(full);else{Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllText(full,content);}
        string? hash=content is null?null:Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full)));
        void Set(JsonArray files,string entry,string? digest)
        {
            var old=files.SingleOrDefault(item=>item!["path"]!.GetValue<string>()==entry);if(old is not null)files.Remove(old);
            if(digest is not null)files.Add(new JsonObject{["path"]=entry,["sha256"]=digest});
        }
        if(path.StartsWith("frontend/",StringComparison.Ordinal))
        {
            var marker=Path.Combine(directory,"frontend/.metarill-frontend.json");var node=JsonNode.Parse(File.ReadAllText(marker))!;
            Set(node["files"]!.AsArray(),path["frontend/".Length..],hash);File.WriteAllText(marker,node.ToJsonString(Json)+"\n");
        }
        var packagePath=Path.Combine(directory,".metarill-package.json");var package=JsonNode.Parse(File.ReadAllText(packagePath))!;Set(package["files"]!.AsArray(),path,hash);
        if(path.StartsWith("frontend/",StringComparison.Ordinal))Set(package["files"]!.AsArray(),"frontend/.metarill-frontend.json",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"frontend/.metarill-frontend.json")))));
        File.WriteAllText(packagePath,package.ToJsonString(Json)+"\n");
    }
    /// <summary>只采两端源码字节，用于排除端不得覆盖和删除的独立断言。</summary>
    private static Dictionary<string,byte[]> PartFileSnapshot(string directory)=>Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories)
        .Select(path=>(Full:path,Relative:Path.GetRelativePath(directory,path).Replace('\\','/')))
        .Where(item=>(item.Relative.StartsWith("frontend/",StringComparison.Ordinal)&&item.Relative!="frontend/.metarill-frontend.json")||item.Relative.StartsWith("src/",StringComparison.Ordinal))
        .ToDictionary(item=>item.Relative,item=>File.ReadAllBytes(item.Full));
    /// <summary>与产品规范相同的UTF8/LF序列化，旧封套测试独立列出旧字段而非忽略新增字段后比较。</summary>
    private static string ProtocolHash<T>(T value)=>Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,Json)+"\n")));
}
