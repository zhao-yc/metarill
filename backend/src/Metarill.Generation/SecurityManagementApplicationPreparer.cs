using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

/// <summary>内部同源装配结果：保留所有待接适配器，绝不能当普通FullApplication发布。</summary>
internal sealed record PreparedSecurityManagementApplication(IReadOnlyList<string> Files,string MetadataSha256,
    IReadOnlyList<string> RedactedPaths,IReadOnlyList<SecurityModuleDependency> RequiredBackendAdapters,IReadOnlyList<GenerationCapabilityGap> BackendGaps);

/// <summary>将已存在的三份安全partial、工作台和正式宿主装配成独立准备包；不解除原初始化/发布门禁。</summary>
internal sealed class SecurityManagementApplicationPreparer
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    private readonly Action<string,string> publish;
    internal SecurityManagementApplicationPreparer():this(PackagePublication.Publish){}
    internal SecurityManagementApplicationPreparer(Action<string,string> publish)=>this.publish=publish;
    /// <summary>入口只接收工厂真签发对象；先普通脱敏，再重新签发相同项目/后台的管理目录。</summary>
    internal PreparedSecurityManagementApplication Prepare(SecurityManagementModelSet module,string outputDirectory,string rootNamespace,string sourceRoot)
    {
        var application=SecurityManagementModuleFactory.RequireApplicationForPreparation(module);
        var output=Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));_=CSharpGenerator.SafePath(output,".metarill-management-preparation.json");
        if(File.Exists(output)||Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("管理准备输出必须不存在或为空，不能覆盖现有工程。");
        var stage=output+".preparing-"+Guid.NewGuid().ToString("N");var candidate=Path.Combine(stage,"application");
        if(Directory.Exists(stage)||File.Exists(stage))throw new InvalidOperationException("管理准备临时目录已存在，拒绝接管。");
        var preserve=false;
        try
        {
            // 基础源码、依赖、许可证、源码链接守卫、Web/Agent/MCP宿主全部复用生产完整包闭包。
            var ordinary=new ApplicationPackager().Create(application,candidate,rootNamespace,sourceRoot);
            var published=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(candidate,"backend/metadata/project.json")),Json)!;
            var trusted=SecurityManagementModuleFactory.Compose(published,module.Binding.ManagementAdminTypeId);
            var catalog=SecurityManagementModuleFactory.RequirePublishedForGeneration(trusted);
            var generated=new CSharpGenerator().Generate(trusted,CSharpGenerator.SafePath(candidate,"backend/generated"),rootNamespace);
            var frontend=new FrontendPackager().PrepareSecurityManagement(trusted,CSharpGenerator.SafePath(candidate,"backend/generated/frontend"),sourceRoot,includeAgent:true);
            // 完整闭包重新写原页面和组件，不能只把工作台临时源码复制到业务宿主。
            foreach(var file in frontend.Files)WriteBytes(candidate,"frontend/"+file.Key,file.Value);
            var projectText=JsonSerializer.Serialize(catalog,Json)+"\n";Write(candidate,"backend/metadata/project.json",projectText);
            var hash=Hash(Encoding.UTF8.GetBytes(projectText));
            Write(candidate,"backend/Application.Host/PackageSecurityCatalogBinding.g.cs",ApplicationPackager.SecurityCatalogBindingSource(rootNamespace,hash));
            // 未确认的初始化及其余缺口明确保留；没有普通包标记，因此不能被更新器误当正式Full候选。
            File.Delete(CSharpGenerator.SafePath(candidate,".metarill-package.json"));
            var warning="管理模块同源准备包，尚未发布。原后台初始化规则及剩余适配/正式验收未完成，不能作为完整可用管理应用交付。";
            Write(candidate,"README.md",warning+"\n\n"+File.ReadAllText(CSharpGenerator.SafePath(candidate,"README.md")));
            var files=Directory.EnumerateFiles(candidate,"*",SearchOption.AllDirectories).Select(path=>new{path=Path.GetRelativePath(candidate,path).Replace('\\','/'),sha256=Hash(File.ReadAllBytes(path))}).OrderBy(file=>file.path,StringComparer.Ordinal).ToArray();
            Write(candidate,".metarill-management-preparation.json",JsonSerializer.Serialize(new{schemaVersion=1,profile="management-preparation-v1",readyForPublication=false,rootNamespace,projectId=catalog.YzProjectId,managementAdminTypeId=trusted.Binding.ManagementAdminTypeId,moduleSha256=trusted.Binding.ModuleSha256,metadataSha256=generated.MetadataSha256,securityCatalogSha256=hash,redactedPaths=ordinary.RedactedPaths,requiredBackendAdapters=frontend.RequiredBackendAdapters,backendGaps=frontend.BackendGaps,files},Json)+"\n");
            publish(candidate,output);
            return new(Array.AsReadOnly(files.Select(file=>file.path).Append(".metarill-management-preparation.json").ToArray()),generated.MetadataSha256,Array.AsReadOnly(ordinary.RedactedPaths.ToArray()),frontend.RequiredBackendAdapters,frontend.BackendGaps);
        }
        catch(PackagePublicationException error)
        {
            if(error.StageDirectory==stage||error.StageDirectory.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){preserve=true;error.PreserveContainingStage(stage);}
            throw;
        }
        finally
        {
            // 只清理本次新建、规范化且与明确输出同父的随机临时根；发布失败保留供诊断。
            if(!preserve&&Directory.Exists(stage)&&Path.GetDirectoryName(stage)==Path.GetDirectoryName(output)&&Path.GetFileName(stage).StartsWith(Path.GetFileName(output)+".preparing-",StringComparison.Ordinal))Directory.Delete(stage,true);
        }
    }
    /// <summary>原资产按已验证包内相对路径写入，不跟随链接或接受外部绝对路径。</summary>
    private static void WriteBytes(string root,string relative,byte[] bytes){var path=CSharpGenerator.SafePath(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}
    /// <summary>源码、目录和说明统一UTF8/LF；不对原二进制资产作文本转换。</summary>
    private static void Write(string root,string relative,string value)=>WriteBytes(root,relative,Encoding.UTF8.GetBytes(value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n')));
    /// <summary>受管文件散列只记录内容摘要，不记录原始配置。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
