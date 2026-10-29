using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
namespace Metarill.Generation;

/// <summary>跨表路径验收准备结果保留全部缺口，不表示可发布完整应用。</summary>
internal sealed record PreparedCrossTableExcelApplication(IReadOnlyList<string> Files,string MetadataSha256,IReadOnlyList<string> RedactedPaths,IReadOnlyList<GenerationCapabilityGap> BackendGaps);

/// <summary>仅内部准备原宿主/前端与实际路径模型；不对公开Full或更新器开放豁免。</summary>
internal sealed class CrossTableExcelApplicationPreparer
{
    internal const string Marker=".metarill-cross-excel-preparation.json";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    private readonly Action<string,string> publish;
    /// <summary>默认沿同一原子发布器，只发布到明确的新准备目录。</summary>
    internal CrossTableExcelApplicationPreparer():this(PackagePublication.Publish){}
    /// <summary>故障注入逐实例保存，不改全局发布状态。</summary>
    internal CrossTableExcelApplicationPreparer(Action<string,string> publish)=>this.publish=publish;

    /// <summary>先脱敏并证明只有已知Excel缺口，再组合生产完整源码；保留原页面、操作、元数据和正式Web绑定。</summary>
    internal PreparedCrossTableExcelApplication Prepare(MetadataCatalog input,string outputDirectory,string rootNamespace,string sourceRoot)
    {
        CSharpGenerator.ValidateNamespace(rootNamespace);
        var publication=GenerationMetadataPublisher.Publish(input);
        var catalog=publication.Metadata;var proof=RequireCurrent(catalog,rootNamespace);
        var output=Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));_=CSharpGenerator.SafePath(output,Marker);
        if(File.Exists(output)||Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("跨表Excel准备输出必须不存在或为空，不能覆盖现有工程。");
        var stage=output+".preparing-"+Guid.NewGuid().ToString("N");var candidate=Path.Combine(stage,"application");var preserve=false;
        if(Directory.Exists(stage)||File.Exists(stage))throw new InvalidOperationException("跨表Excel准备临时根已存在。");
        try
        {
            // 只在基底关闭Excel操作，完整源码、真实认证/权限/文件/Agent/MCP仍由原生产打包器装配。
            var ordinary=new ApplicationPackager().Create(proof.Baseline,candidate,rootNamespace,sourceRoot);
            var generated=new CSharpGenerator().Generate(catalog,CSharpGenerator.SafePath(candidate,"backend/generated"),rootNamespace);
            var frontend=new FrontendPackager().PrepareCrossTableExcel(catalog,CSharpGenerator.SafePath(candidate,"backend/generated/frontend"),sourceRoot,includeAgent:true);
            foreach(var file in frontend.Files)WriteBytes(candidate,"frontend/"+file.Key,file.Value);
            var text=JsonSerializer.Serialize(catalog,Json)+"\n";Write(candidate,"backend/metadata/project.json",text);
            var securityHash=Hash(Encoding.UTF8.GetBytes(text));
            Write(candidate,"backend/Application.Host/PackageSecurityCatalogBinding.g.cs",ApplicationPackager.SecurityCatalogBindingSource(rootNamespace,securityHash));
            // 必须在发布准备目录前移除普通包标记，不能被CLI/本地助手误认成正式Full候选。
            File.Delete(CSharpGenerator.SafePath(candidate,".metarill-package.json"));
            Write(candidate,"README.md","跨表Excel验收准备包，尚未发布。正式SQL、身份、事务及完整往返仍待验收，不能作为可用完整应用交付。\n\n"+File.ReadAllText(CSharpGenerator.SafePath(candidate,"README.md")));
            var redactions=publication.RedactedPaths.Concat(ordinary.RedactedPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var files=Directory.EnumerateFiles(candidate,"*",SearchOption.AllDirectories).Select(file=>new{path=Path.GetRelativePath(candidate,file).Replace('\\','/'),sha256=Hash(File.ReadAllBytes(file))}).OrderBy(file=>file.path,StringComparer.Ordinal).ToArray();
            Write(candidate,Marker,JsonSerializer.Serialize(new{schemaVersion=1,profile="cross-table-excel-preparation-v1",readyForPublication=false,authenticationProfile="web-database-v1",rootNamespace,projectId=catalog.YzProjectId,metadataSha256=generated.MetadataSha256,securityCatalogSha256=securityHash,redactedPaths=redactions,backendGaps=frontend.BackendGaps,files},Json)+"\n");
            publish(candidate,output);
            return new(Array.AsReadOnly(files.Select(file=>file.path).Append(Marker).ToArray()),generated.MetadataSha256,Array.AsReadOnly(redactions),frontend.BackendGaps);
        }
        catch(PackagePublicationException error)
        {
            if(error.StageDirectory==stage||error.StageDirectory.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){preserve=true;error.PreserveContainingStage(stage);}
            throw;
        }
        finally
        {
            // 仅清本次随机、规范化且与明确输出同父的stage；发布异常的可恢复现场保留。
            if(!preserve&&Directory.Exists(stage)&&Path.GetDirectoryName(stage)==Path.GetDirectoryName(output)&&Path.GetFileName(stage).StartsWith(Path.GetFileName(output)+".preparing-",StringComparison.Ordinal))Directory.Delete(stage,true);
        }
    }

    /// <summary>关闭Excel后必须通过全部公开Full检查；实际模型只容许已知路径验收缺口，不能隐藏其它坏链或菜单。</summary>
    internal static (MetadataCatalog Baseline,IReadOnlyList<GenerationCapabilityGap> Gaps) RequireCurrent(MetadataCatalog catalog,string rootNamespace)
    {
        if(catalog.DatabaseProvider!="mysql")throw new InvalidOperationException("跨表Excel准备只覆盖当前MySQL验收，不能声明其它数据库已支持。");
        var model=GenerationModel.Create(catalog,rootNamespace);
        var pages=model.Tables.SelectMany(table=>table.Pages.Where(page=>CrossTableWriteCompilation.ForService(model,table,page).Count>0)).Select(page=>page.Source.Id).ToHashSet(StringComparer.Ordinal);
        var gaps=GenerationCapabilityAudit.Collect(model);
        if(pages.Count==0||gaps.Count==0)throw new InvalidOperationException("模型没有待验收的跨表Excel用途，不需要准备通道。");
        const string hidden="非冗余crossTable的实际用途未通过完整包证明，隐藏控件不能绕过门禁。";
        if(gaps.Any(gap=>!pages.Contains(gap.PageId)||gap.Reason!=CrossTableExcelRenderer.ImportAcceptancePending&&gap.Reason!=CrossTableExcelRenderer.ExportAcceptancePending&&gap.Reason!=hidden))
            throw new InvalidOperationException("跨表Excel准备存在其它未接线能力，不能用验收通道绕过。");
        var baseline=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.Serialize(catalog,Json),Json)!;
        foreach(var page in baseline.ConfigPages.Where(page=>pages.Contains(page.Id)))
        {page.IsImport=0;page.IsExport=0;page.Extra.Remove("importMethod");page.Extra.Remove("exportMethod");}
        new FrontendPackager().Validate(baseline);
        const string schema="cross_excel_preparation";
        if(MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(catalog,schema))!=MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(baseline,schema)))
            throw new InvalidOperationException("跨表Excel准备不能改变业务物理结构。");
        return(baseline,Array.AsReadOnly(gaps.ToArray()));
    }
    /// <summary>只写已校验的包内相对路径，二进制原资源不作文本转换。</summary>
    private static void WriteBytes(string root,string path,byte[] bytes){var target=CSharpGenerator.SafePath(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllBytes(target,bytes);}
    /// <summary>框架说明和元数据统一UTF8/LF，保持绑定摘要可重复。</summary>
    private static void Write(string root,string path,string value)=>WriteBytes(root,path,Encoding.UTF8.GetBytes(value.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n')));
    /// <summary>只记录摘要，不输出元数据或文件正文。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
