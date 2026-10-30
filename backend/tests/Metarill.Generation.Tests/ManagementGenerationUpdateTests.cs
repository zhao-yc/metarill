using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Metarill.Core.Metadata;
using Xunit;
namespace Metarill.Generation.Tests;

/// <summary>完整管理工程沿原三范围更新与恢复，不改原页面或把磁盘标记当作安全模块授权。</summary>
[Collection(StandaloneProcessBudgetCollection.Name)]
public sealed class ManagementGenerationUpdateTests
{
    /// <summary>真实管理包必须能识别原项目/命名空间，而不是继续停在新模式未接线错误。</summary>
    [Fact]
    public async Task Existing_management_target_identity_is_read_from_the_verified_package()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var metadata=Catalog(source);var target=Path.Combine(root,"target");new ApplicationPackager().CreateManagement(metadata,target,"Example.ManagedUpdate",source,metadata.YzAdminTypes[0].Id);
            var identity=new LocalGenerationUpdater().InspectTarget(target);
            Assert.Equal(metadata.YzProjectId,identity.ProjectId);Assert.Equal("Example.ManagedUpdate",identity.RootNamespace);Assert.Equal(metadata.DatabaseProvider,identity.DatabaseProvider);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    private static readonly string[] All=["generate-project-config","sync-runtime","generate-all-page"];

    /// <summary>配置和页面依赖沿原预览，应用时保留真正手写扩展/AppID/私有设置，来源四清单共同收敛。</summary>
    [Fact]
    public async Task Managed_update_preserves_handwritten_code_and_configuration_then_reaches_a_noop()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var old=Catalog(source);var next=Catalog(source);next.YzProjectName="配置更新后";next.ConfigColumns.Single(column=>column.ColumnName=="name").Name="新的显示名称";
            var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");var builder=new ApplicationPackager();builder.CreateManagement(old,target,"Example.ManagedUpdate",source,old.YzAdminTypes[0].Id);builder.CreateManagement(next,candidate,"Example.ManagedUpdate",source,next.YzAdminTypes[0].Id);
            var hand=Path.Combine(target,"backend/generated/Services/YzRoleService.cs");await File.AppendAllTextAsync(hand,"\npublic partial class YzRoleService { internal static string PreservedExtension() => \"保留业务扩展\"; }\n");var handwritten=await File.ReadAllBytesAsync(hand);
            var manifestPath=Path.Combine(target,"frontend/manifest.json");var manifest=JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;manifest["appid"]="__UNI__KEEP_LOCAL";await File.WriteAllTextAsync(manifestPath,manifest.ToJsonString());var appid=await File.ReadAllBytesAsync(manifestPath);
            var privatePath=Path.Combine(target,"backend/Application.Host/appsettings.local.json");await File.WriteAllTextAsync(privatePath,"{\"说明\":\"保留本地私有配置\"}");var privateBytes=await File.ReadAllBytesAsync(privatePath);
            var updater=new LocalGenerationUpdater();var incomplete=updater.Preview(target,candidate,["generate-all-page"]);Assert.False(incomplete.CanApply);Assert.Contains("generate-project-config",incomplete.RequiredActions);
            var plan=updater.Preview(target,candidate,All);Assert.True(plan.CanApply);Assert.Contains(plan.Files,file=>file.Path==PreparedSecurityManagementSource.SourceMarker&&file.Scope=="manifest");
            var result=updater.Apply(plan,plan.PlanSha256);Assert.Equal("committed",result.Status);
            Assert.Equal(handwritten,await File.ReadAllBytesAsync(hand));Assert.Equal(appid,await File.ReadAllBytesAsync(manifestPath));Assert.Equal(privateBytes,await File.ReadAllBytesAsync(privatePath));
            var restored=PreparedSecurityManagementSource.Read(target);Assert.Equal(next.YzProjectName,SecurityManagementModuleFactory.RequireApplicationForPreparation(restored.Module).YzProjectName);
            Assert.Equal(next.YzProjectId,updater.InspectTarget(target).ProjectId);var repeat=updater.Preview(target,candidate,All);Assert.True(repeat.CanApply);Assert.All(repeat.Files,file=>Assert.Equal("preserve",file.Action));
            var build=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet",target);
            foreach(var argument in new[]{"build","backend/Application.Host/Application.Host.csproj","-c","Release","--nologo","--disable-build-servers","--verbosity","quiet","-warnaserror"})build.ArgumentList.Add(argument);await NonRedundantApplicationFixture.RunAsync(build);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>运行时范围不要求重写业务元数据；固定来源声明按实际合并内容生成，不破坏后续识别。</summary>
    [Fact]
    public async Task Managed_runtime_only_updates_keep_source_identity_and_other_scopes_unselected()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var metadata=Catalog(source);var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");var builder=new ApplicationPackager();builder.CreateManagement(metadata,target,"Example.ManagedUpdate",source,metadata.YzAdminTypes[0].Id);builder.CreateManagement(metadata,candidate,"Example.ManagedUpdate",source,metadata.YzAdminTypes[0].Id);
            const string relative="src/Metarill.Core/Contracts/R.cs";var runtime=Path.Combine(target,relative);Assert.True(File.Exists(runtime));await File.AppendAllTextAsync(runtime,"\n// 模拟已登记的旧运行时版本。\n");Rehash(target,relative);
            var before=PreparedSecurityManagementSource.Read(target).ApplicationMetadataSha256;var updater=new LocalGenerationUpdater();var plan=updater.Preview(target,candidate,["sync-runtime"]);
            Assert.True(plan.CanApply);Assert.Empty(plan.RequiredActions);Assert.DoesNotContain(plan.Files,file=>file.Scope is "generate-project-config" or "generate-all-page");
            Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(candidate,relative)),await File.ReadAllBytesAsync(runtime));Assert.Equal(before,PreparedSecurityManagementSource.Read(target).ApplicationMetadataSha256);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>来源清单已经写入后才注入失败，必须经过原恢复日志校验恢复所有旧字节。</summary>
    [Fact]
    public async Task Failure_after_management_source_write_rolls_back_the_entire_original_package()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var old=Catalog(source);var next=Catalog(source);next.YzProjectName="应恢复原配置";var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");var builder=new ApplicationPackager();builder.CreateManagement(old,target,"Example.ManagedUpdate",source,old.YzAdminTypes[0].Id);builder.CreateManagement(next,candidate,"Example.ManagedUpdate",source,next.YzAdminTypes[0].Id);
            var before=Snapshot(target);var observed=false;var updater=new LocalGenerationUpdater{AfterWrite=path=>{if(path==PreparedSecurityManagementSource.SourceMarker){observed=true;throw new IOException("来源写后故障");}}};
            var plan=updater.Preview(target,candidate,All);Assert.True(plan.CanApply);Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));Assert.True(observed);Assert.Equal(before,Snapshot(target));Assert.Equal(old.YzProjectId,new LocalGenerationUpdater().InspectTarget(target).ProjectId);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>原本地助手的单动作请求从已验证目标继承模式与后台，不给浏览器新增绑定参数。</summary>
    [Fact]
    public async Task Existing_request_preparation_keeps_management_binding_and_original_three_actions()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var metadata=Catalog(source);var target=Path.Combine(root,"target");new ApplicationPackager().CreateManagement(metadata,target,"Developer.ManagedNamespace",source,metadata.YzAdminTypes[0].Id);var before=Snapshot(target);
            foreach(var action in All)
            {
                var next=Catalog(source);if(action=="generate-project-config")next.YzProjectName="请求新配置";if(action=="generate-all-page")next.ConfigColumns.Single(column=>column.ColumnName=="name").Name="请求新标签";
                var request=LocalGenerationUpdater.CreateRequest(next,action);var input=Path.Combine(root,action+".json");await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                var candidate=Path.Combine(root,action);var planPath=Path.Combine(root,action+"-plan.json");var result=new LocalGenerationUpdater().PrepareRequest(input,target,candidate,source,planPath);
                Assert.Equal(before,Snapshot(target));Assert.Equal("Developer.ManagedNamespace",result.Plan.RootNamespace);Assert.Equal(new[]{action},result.Plan.Actions);Assert.Equal(result.Plan.PlanSha256,LocalGenerationUpdater.ReadPlan(planPath).PlanSha256);
                var binding=PreparedSecurityManagementSource.Read(candidate);Assert.Equal(metadata.YzAdminTypes[0].Id,binding.Module.Binding.ManagementAdminTypeId);Assert.Equal("Developer.ManagedNamespace",binding.RootNamespace);
                Assert.Equal(action=="sync-runtime"?"preview-ready":"dependencies-required",result.Status);
                // 实际管理生成产物随项目配置变化；按真实差异提示页面依赖，不能擅自扩范围。
                if(action=="generate-project-config")Assert.Equal(new[]{"generate-all-page"},result.Plan.RequiredActions);
                if(action=="generate-all-page")Assert.Contains("generate-project-config",result.Plan.RequiredActions);
            }
            Assert.False(File.Exists(Path.Combine(target,".local/metarill-updates/active.json")));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>管理来源不是磁盘自报信任；绑定篡改、删掉声明及伪造普通模式全部在预览前拒绝。</summary>
    [Fact]
    public async Task Forged_management_bindings_or_removed_source_ownership_never_write_the_target()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var metadata=Catalog(source);var target=Path.Combine(root,"target");var candidate=Path.Combine(root,"candidate");var builder=new ApplicationPackager();builder.CreateManagement(metadata,target,"Example.ManagedUpdate",source,metadata.YzAdminTypes[0].Id);builder.CreateManagement(metadata,candidate,"Example.ManagedUpdate",source,metadata.YzAdminTypes[0].Id);
            var before=Snapshot(target);var packagePath=Path.Combine(candidate,".metarill-package.json");var original=await File.ReadAllTextAsync(packagePath);
            foreach(var scenario in new[]{"module","admin","application","mode","sourceOwnership","unknownSource"})
            {
                var package=JsonNode.Parse(original)!;byte[]? sourceBytes=null;var sourcePath=Path.Combine(candidate,PreparedSecurityManagementSource.SourceMarker);
                switch(scenario)
                {
                    case "module":package["managementModule"]!["moduleSha256"]=new string('0',64);break;
                    case "admin":package["managementModule"]!["managementAdminTypeId"]="999999999999999999";break;
                    case "application":package["managementModule"]!["applicationMetadataSha256"]=new string('0',64);break;
                    case "mode":package["packageMode"]="FullApplication";break;
                    case "sourceOwnership":package["files"]!.AsArray().Remove(package["files"]!.AsArray().Single(file=>file!["path"]!.GetValue<string>()==PreparedSecurityManagementSource.SourceMarker));sourceBytes=await File.ReadAllBytesAsync(sourcePath);File.Delete(sourcePath);break;
                    case "unknownSource":sourceBytes=await File.ReadAllBytesAsync(sourcePath);var descriptor=JsonNode.Parse(sourceBytes)!;descriptor["allowUnsignedModule"]=true;await File.WriteAllTextAsync(sourcePath,descriptor.ToJsonString());package["files"]!.AsArray().Single(file=>file!["path"]!.GetValue<string>()==PreparedSecurityManagementSource.SourceMarker)!["sha256"]=Hash(await File.ReadAllBytesAsync(sourcePath));break;
                }
                await File.WriteAllTextAsync(packagePath,package.ToJsonString());Assert.ThrowsAny<Exception>(()=>new LocalGenerationUpdater().Preview(target,candidate,All));Assert.Equal(before,Snapshot(target));
                if(sourceBytes is not null)await File.WriteAllBytesAsync(sourcePath,sourceBytes);
            }
            await File.WriteAllTextAsync(packagePath,original);Assert.True(new LocalGenerationUpdater().Preview(target,candidate,All).CanApply);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>两个真实有效包也不能用受管更新切换普通/管理模式或改绑到另一个已发布后台。</summary>
    [Fact]
    public async Task Valid_candidates_cannot_switch_management_mode_or_administrator_binding()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var metadata=Catalog(source);var other=Catalog(source).YzAdminTypes[0];other.Id="2200000000000100099";other.Name="另一个后台";metadata.YzAdminTypes.Add(other);
            var plain=Path.Combine(root,"plain");var first=Path.Combine(root,"first");var second=Path.Combine(root,"second");var builder=new ApplicationPackager();builder.Create(metadata,plain,"Example.ManagedUpdate",source);builder.CreateManagement(metadata,first,"Example.ManagedUpdate",source,metadata.YzAdminTypes[0].Id);builder.CreateManagement(metadata,second,"Example.ManagedUpdate",source,other.Id);
            foreach(var pair in new[]{(plain,first),(first,plain),(first,second)})
            {
                var before=Snapshot(pair.Item1);Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Preview(pair.Item1,pair.Item2,All));Assert.Equal(before,Snapshot(pair.Item1));
            }
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>恢复失败现场保留后，新更新器只凭已确认日志和旧身份恢复，不依赖仍存在的候选。</summary>
    [Fact]
    public async Task Explicit_recovery_restores_a_management_transaction_after_the_candidate_is_gone()
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var old=Catalog(source);var next=Catalog(source);next.YzProjectName="需要显式恢复";var target=Path.Combine(root,"target");var candidate=Path.Combine(root,Guid.NewGuid().ToString("N"));var builder=new ApplicationPackager();builder.CreateManagement(old,target,"Example.ManagedUpdate",source,old.YzAdminTypes[0].Id);builder.CreateManagement(next,candidate,"Example.ManagedUpdate",source,next.YzAdminTypes[0].Id);var before=Snapshot(target);
            var updater=new LocalGenerationUpdater{AfterWrite=path=>{if(path==PreparedSecurityManagementSource.SourceMarker)throw new IOException("来源写后中断");},BeforeRestore=_=>throw new IOException("模拟恢复中断")};var plan=updater.Preview(target,candidate,All);Assert.True(plan.CanApply);Assert.Throws<InvalidOperationException>(()=>updater.Apply(plan,plan.PlanSha256));
            var active=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(target,".local/metarill-updates/active.json")))!;var id=active["transactionId"]!.GetValue<string>();Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().InspectTarget(target));
            Assert.Throws<InvalidOperationException>(()=>new LocalGenerationUpdater().Recover(target,id,new string('0',64)));await TestDirectoryCleanup.DeleteAsync(candidate,root);
            Assert.Equal("rolled-back",new LocalGenerationUpdater().Recover(target,id,plan.PlanSha256).Status);Assert.Equal(before,Snapshot(target));Assert.Equal(old.YzProjectId,new LocalGenerationUpdater().InspectTarget(target).ProjectId);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>真实CLI复现过合法A/a扩展创建成功却更新失败；普通与管理包均沿同一元数据边界修复。</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Case_sensitive_business_maps_survive_request_preparation_and_real_package_update(bool managed)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            var old=Catalog(source);old.ConfigTables[0].Extra["caseSensitiveMap"]=JsonSerializer.SerializeToElement(new Dictionary<string,int>{{"A",1},{"a",2}});var next=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(old,new JsonSerializerOptions(JsonSerializerDefaults.Web)),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;next.ConfigColumns.Single(column=>column.ColumnName=="name").Name="保留字典的更新";
            var target=Path.Combine(root,"target");var builder=new ApplicationPackager();if(managed)builder.CreateManagement(old,target,"Example.CaseSensitive",source,old.YzAdminTypes[0].Id);else builder.Create(old,target,"Example.CaseSensitive",source);
            var updater=new LocalGenerationUpdater();Assert.Equal(old.YzProjectId,updater.InspectTarget(target).ProjectId);var request=LocalGenerationUpdater.CreateRequest(next,"generate-all-page");var input=Path.Combine(root,"request.json");await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));Assert.Equal(request.RequestSha256,LocalGenerationUpdater.ReadRequest(input).RequestSha256);
            var before=Snapshot(target);var candidate=Path.Combine(root,"candidate");var preparation=updater.PrepareRequest(input,target,candidate,source,Path.Combine(root,"plan.json"));Assert.Equal(before,Snapshot(target));Assert.Equal("dependencies-required",preparation.Status);Assert.Contains("generate-project-config",preparation.Plan.RequiredActions);
            var plan=updater.Preview(target,candidate,All);Assert.True(plan.CanApply);Assert.Equal("committed",updater.Apply(plan,plan.PlanSha256).Status);Assert.Equal(old.YzProjectId,updater.InspectTarget(target).ProjectId);
            using var json=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target,"backend/metadata/project.json")));var map=json.RootElement.GetProperty("configTables").EnumerateArray().Single(table=>table.GetProperty("id").GetString()==old.ConfigTables[0].Id).GetProperty("caseSensitiveMap");Assert.Equal(1,map.GetProperty("A").GetInt32());Assert.Equal(2,map.GetProperty("a").GetInt32());
            Assert.All(updater.Preview(target,candidate,All).Files,file=>Assert.Equal("preserve",file.Action));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>业务字典允许A/a，不允许同名重复；请求封套和元数据模型仍拒绝忽略大小写的别名。</summary>
    [Theory]
    [InlineData("envelope")] [InlineData("catalog")] [InlineData("model")] [InlineData("businessDuplicate")]
    public async Task Metadata_dictionary_support_does_not_allow_protocol_aliases_or_duplicate_keys(string scenario)
    {
        var source=NonRedundantApplicationFixture.Root();var boundary=Path.Combine(Path.GetTempPath(),"metarill-managed-update");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var request=LocalGenerationUpdater.CreateRequest(Catalog(source),"sync-runtime");var node=JsonNode.Parse(JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
            switch(scenario)
            {
                case "envelope":node["Action"]=request.Action;break;
                case "catalog":node["metadata"]!["YZPROJECTID"]=request.ProjectId;break;
                case "model":node["metadata"]!["configTables"]![0]!["ID"]=node["metadata"]!["configTables"]![0]!["id"]!.GetValue<string>();break;
                case "businessDuplicate":node["metadata"]!["configTables"]![0]!["caseSensitiveMap"]=new JsonObject{{"A",1}};break;
            }
            var text=node.ToJsonString();if(scenario=="businessDuplicate")text=text.Replace("\"caseSensitiveMap\":{\"A\":1}","\"caseSensitiveMap\":{\"A\":1,\"A\":2}",StringComparison.Ordinal);
            var path=Path.Combine(root,"request.json");await File.WriteAllTextAsync(path,text);var error=Assert.Throws<InvalidOperationException>(()=>LocalGenerationUpdater.ReadRequest(path));Assert.Equal(scenario=="envelope"?"JSON包含重复字段。":"元数据JSON包含重复或大小写冲突字段。",error.Message);
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }

    /// <summary>仅对模拟旧运行时重新登记原有路径，不为未知源码扩展测试权限。</summary>
    private static void Rehash(string root,string relative)
    {
        var file=Path.Combine(root,".metarill-package.json");var manifest=JsonNode.Parse(File.ReadAllText(file))!;manifest["files"]!.AsArray().Single(entry=>entry!["path"]!.GetValue<string>()==relative)!["sha256"]=Hash(File.ReadAllBytes(Path.Combine(root,relative)));File.WriteAllText(file,manifest.ToJsonString());
    }
    /// <summary>事务日志在.local内，恢复比较所有原工程字节而非仅比较声明。</summary>
    private static string Snapshot(string root)=>string.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(path=>!Path.GetRelativePath(root,path).StartsWith(".local"+Path.DirectorySeparatorChar,StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(path=>Path.GetRelativePath(root,path)+":"+Hash(File.ReadAllBytes(path))));
    /// <summary>测试摘要不打印文件内容或本机配置。</summary>
    private static string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));

    /// <summary>仅从已提交业务样例开始，不加载私有设置或连接数据库。</summary>
    private static MetadataCatalog Catalog(string source)=>JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
