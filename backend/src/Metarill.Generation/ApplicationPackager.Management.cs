using System.Text.Json;
using System.Text.Json.Nodes;
namespace Metarill.Generation;

public sealed partial class ApplicationPackager
{
    /// <summary>显式绑定一个已发布后台，装配真实固定管理模块；模式独立于普通Full，完整验收状态另行记录。</summary>
    public ApplicationPackageResult CreateManagement(Metarill.Core.Metadata.MetadataCatalog catalog,string outputDirectory,string rootNamespace,string sourceRoot,string managementAdminTypeId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var module=SecurityManagementModuleFactory.Compose(catalog,managementAdminTypeId);
        var output=Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));_=CSharpGenerator.SafePath(output,".metarill-package.json");
        if(File.Exists(output)||Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("完整管理包输出目录必须不存在或为空。");
        var stage=output+".management-staging-"+Guid.NewGuid().ToString("N");var candidate=Path.Combine(stage,"application");var preserve=false;
        if(Directory.Exists(stage)||File.Exists(stage))throw new InvalidOperationException("完整管理包临时目录已存在，拒绝接管。");
        try
        {
            // 同一个生产闭包负责脱敏、基础源码、真实管理Service/工作台和宿主；不从业务JSON开启特权。
            var prepared=new SecurityManagementApplicationPreparer().Prepare(module,candidate,rootNamespace,sourceRoot);
            var context=PreparedSecurityManagementSource.Read(candidate);
            if(prepared.BackendGaps.Count!=0)throw new InvalidOperationException("完整管理包仍有后端能力缺口，未生成正式包清单。");
            var preparation=CSharpGenerator.SafePath(candidate,".metarill-management-preparation.json");var binding=JsonNode.Parse(File.ReadAllText(preparation))!;
            var sourceBinding=binding.DeepClone().AsObject();sourceBinding["profile"]="management-source-v1";
            foreach(var key in new[]{"readyForPublication","redactedPaths","requiredBackendAdapters","backendGaps"})sourceBinding.Remove(key);
            // 通用来源声明只登记其实际读取的五份元数据，完整源码归属由外层包清单负责。
            sourceBinding["files"]=new JsonArray(binding["files"]!.AsArray().Where(file=>PreparedSecurityManagementSource.OwnedMetadataPaths.Contains(file!["path"]!.GetValue<string>(),StringComparer.Ordinal)).Select(file=>file!.DeepClone()).ToArray());
            Write(candidate,PreparedSecurityManagementSource.SourceMarker,sourceBinding.ToJsonString(JsonOptions)+"\n");File.Delete(preparation);
            var warnings=new[]{"完整管理模式支持预览、确认应用与恢复；正式身份、数据库、初始化和工作台矩阵仍待完整验收。","初始化遇到有效Agent刷新授权时当前会拒绝，处理策略仍待确认。"};
            var readme=File.ReadAllText(CSharpGenerator.SafePath(candidate,"README.md"));var newline=readme.IndexOf('\n');
            if(newline<0)throw new InvalidOperationException("管理准备说明格式不完整。");
            readme=readme[(newline+1)..].TrimStart('\r','\n').Replace("FullApplication：","FullManagementApplication：",StringComparison.Ordinal);
            Write(candidate,"README.md","完整管理模式：FullManagementApplication。\n\n"+string.Join("\n",warnings)+"\n\n"+readme);
            // 元数据来源重新签发与完整包模式相互独立，声明本身不代表验收通过或更新授权。
            var verified=PreparedSecurityManagementSource.Read(candidate);
            if(verified.ApplicationMetadataSha256!=context.ApplicationMetadataSha256)throw new InvalidOperationException("完整管理包业务来源在装配中变化。");
            var files=Directory.EnumerateFiles(candidate,"*",SearchOption.AllDirectories).Select(file=>new{path=Path.GetRelativePath(candidate,file).Replace('\\','/'),sha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))}).OrderBy(file=>file.path,StringComparer.Ordinal).ToArray();
            Write(candidate,".metarill-package.json",JsonSerializer.Serialize(new{schemaVersion=1,rootNamespace,packageMode=ApplicationPackageMode.FullManagementApplication.ToString(),frontendIncluded=true,authenticationProfile="web-database-v1",securityCatalogSha256=binding["securityCatalogSha256"]!.GetValue<string>(),metadataSha256=prepared.MetadataSha256,managementModule=new{moduleId=verified.Module.Binding.ModuleId,moduleSha256=verified.Module.Binding.ModuleSha256,managementAdminTypeId=verified.Module.Binding.ManagementAdminTypeId,applicationMetadataSha256=verified.ApplicationMetadataSha256},redactedPaths=prepared.RedactedPaths,warnings,files},JsonOptions)+"\n");
            publish(candidate,output);
            return new(Array.AsReadOnly(files.Select(file=>file.path).Append(".metarill-package.json").ToArray()),prepared.MetadataSha256,prepared.RedactedPaths,Array.AsReadOnly(warnings)){FrontendIncluded=true,Mode=ApplicationPackageMode.FullManagementApplication};
        }
        catch(PackagePublicationException error)
        {
            if(error.StageDirectory==stage||error.StageDirectory.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){preserve=true;error.PreserveContainingStage(stage);}throw;
        }
        finally
        {
            // 仅清理本轮独占且与目标同父的随机stage；发布故障保留完整包与诊断。
            if(!preserve&&Directory.Exists(stage)&&Path.GetDirectoryName(stage)==Path.GetDirectoryName(output)&&Path.GetFileName(stage).StartsWith(Path.GetFileName(output)+".management-staging-",StringComparison.Ordinal))Directory.Delete(stage,true);
        }
    }
}
