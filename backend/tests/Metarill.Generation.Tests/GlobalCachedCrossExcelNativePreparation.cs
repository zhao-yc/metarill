using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Testing;
namespace Metarill.Generation.Tests;

/// <summary>将B119原六模型合成一个独占项目；只重编号配置及物理表，不改变关系语义或公开Full门槛。</summary>
internal static class GlobalCachedCrossExcelNativePreparation
{
    internal const string RootNamespace="Metarill.GlobalCrossExcelNative";
    internal const string Root=".local/global-cached-cross-excel-native";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    /// <summary>六组使用同一后台、项目和基础设施，每组表/列/页/按钮均有独立配置ID。</summary>
    internal static MetadataCatalog Catalog()
    {
        var result=new MetadataCatalog{YzProjectId="100",YzProjectName="全局缓存级联六模型验收"};
        var index=0;string? adminTypes=null;
        foreach(var p in GlobalCachedCrossExcelMySqlLease.Profiles)
        {
            var catalog=p.Shared?GlobalCrossCachedTargetFixture.CreateShared(0,p.Key,p.Parent,p.Shorter):GlobalCrossCachedTargetFixture.Create(0,p.Role,p.Key,p.Parent,p.AllGlobal);
            if(catalog.ConfigIndexes.Count!=0||catalog.ConfigIndexColumns.Count!=0||catalog.ConfigChildren.Count!=0||catalog.ConfigCustomPages.Count!=0||catalog.YzAdminTypes.Count!=1||catalog.YzAdminTypes[0].Id!="9"||catalog.YzProjectId!="100")throw new InvalidOperationException("六模型出现未经合并证明的新目录。");
            var currentAdminTypes=JsonSerializer.Serialize(catalog.YzAdminTypes,Json);
            if(adminTypes is null){adminTypes=currentAdminTypes;result.YzAdminTypes=JsonSerializer.Deserialize<List<AdminTypeMetadata>>(currentAdminTypes,Json)!;}
            else if(adminTypes!=currentAdminTypes)throw new InvalidOperationException("六模型后台定义不同，不能静默覆盖。");
            if(catalog.SchemaVersion!=result.SchemaVersion||catalog.BackendTarget!=result.BackendTarget||catalog.DatabaseProvider!=result.DatabaseProvider||catalog.PrimaryColor!=result.PrimaryColor)throw new InvalidOperationException("六模型发布属性不同，不能静默改用默认值。");
            var profile=GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter);
            GlobalCachedCrossExcelMySqlTests.Prefix(catalog,"gce_"+profile);
            var basis=82000000L+(++index)*10000;
            var tables=Map(catalog.ConfigTables,basis+1000);var columns=Map(catalog.ConfigColumns,basis+2000);var pages=Map(catalog.ConfigPages,basis+3000);var menus=Map(catalog.ConfigMenus,basis+5000);
            foreach(var table in catalog.ConfigTables)table.Id=tables[table.Id];
            foreach(var column in catalog.ConfigColumns)
            {
                column.Id=columns[column.Id];column.ConfigTableId=tables[column.ConfigTableId];
                if(column.LinkConfigTableId.Length>0)column.LinkConfigTableId=tables[column.LinkConfigTableId];
                if(column.ControlKind=="List")
                {
                    var control=JsonNode.Parse(column.ControlDetail)!.AsObject();
                    if(control["tableId"] is not null)control["tableId"]=tables[control["tableId"]!.GetValue<string>()];
                    if(control["pageId"] is not null)control["pageId"]=pages[control["pageId"]!.GetValue<string>()];
                    column.ControlDetail=control.ToJsonString();
                }
            }
            foreach(var page in catalog.ConfigPages){page.Id=pages[page.Id];page.ConfigTableId=tables[page.ConfigTableId];}
            for(var field=0;field<catalog.ConfigPageColumns.Count;field++)
            {var value=catalog.ConfigPageColumns[field];value.Id=(basis+4000+field).ToString(CultureInfo.InvariantCulture);value.ConfigPageId=pages[value.ConfigPageId];value.ConfigColumnId=columns[value.ConfigColumnId];}
            foreach(var menu in catalog.ConfigMenus)
            {
                if(menu.Kind!="GENERATED_PAGE"||!string.IsNullOrWhiteSpace(menu.ParentMenuId))throw new InvalidOperationException("六模型只合并原固定页菜单。");
                menu.Id=menus[menu.Id];menu.ConfigPageId=pages[menu.ConfigPageId!];
            }
            var buttons=catalog.ConfigButtons.Select((button,position)=>
            {
                var value=JsonNode.Parse(button.GetRawText())!.AsObject();value["id"]=(basis+6000+position).ToString(CultureInfo.InvariantCulture);
                value["configPageId"]=pages[value["configPageId"]!.GetValue<string>()];value["configTableId"]=tables[value["configTableId"]!.GetValue<string>()];
                return JsonSerializer.SerializeToElement(value);
            }).ToArray();
            result.ConfigTables.AddRange(catalog.ConfigTables);result.ConfigColumns.AddRange(catalog.ConfigColumns);result.ConfigPages.AddRange(catalog.ConfigPages);result.ConfigPageColumns.AddRange(catalog.ConfigPageColumns);result.ConfigMenus.AddRange(catalog.ConfigMenus);result.ConfigButtons.AddRange(buttons);
        }
        return result;
    }
    /// <summary>普通账号的导航与页动作分别授权；只生成本固定项目／后台的菜单行，不赋予维护按钮。</summary>
    internal static IReadOnlyList<Dictionary<string,object?>> NavigationGrants(MetadataCatalog catalog,long role,Func<long> nextId)
    {
        if(catalog.YzProjectId!="100"||catalog.ConfigMenus.Any(menu=>menu.YzProjectId!="100"||menu.YzAdminTypeId!="9"||menu.Kind!="GENERATED_PAGE"||!catalog.ConfigPages.Any(page=>page.Id==menu.ConfigPageId&&page.YzProjectId=="100"&&page.YzAdminTypeId=="9")))
            throw new InvalidOperationException("六模型导航授权超出固定项目／后台或页面范围。");
        return catalog.ConfigMenus.Select(menu=>new Dictionary<string,object?>{{"id",nextId()},{"yz_project_id",100L},{"yz_role_id",role},{"config_menu_id",long.Parse(menu.Id,CultureInfo.InvariantCulture)},{"permission_values","[]"}}).ToArray();
    }
    /// <summary>只为同一组已有记录分配独立数值ID，不依赖旧夹具含文字的ID格式。</summary>
    private static Dictionary<string,string> Map<T>(IReadOnlyList<T> values,long basis)where T:MetadataRecord=>values.Select((value,index)=>(value.Id,NewId:(basis+index).ToString(CultureInfo.InvariantCulture))).ToDictionary(value=>value.Id,value=>value.NewId,StringComparer.Ordinal);
    /// <summary>原六个来源服务均带相同的手写事务故障观察，不替换认证、生成器或基础设施。</summary>
    internal static string Handwritten(string table)
    {
        var type=GenerationModel.Pascal(table);
        return $$"""
using FreeSql;
using Metarill.Core.Security;
namespace {{RootNamespace}}.Generated.Services;
public partial class {{type}}Service
{
    /// <summary>真实首行DML与文件引用可见后拒绝第二行，事务应撤销整批业务及引用。</summary>
    partial void BeforeSave({{RootNamespace}}.Generated.Entities.{{type}} entity,RuntimeActor actor,bool creating,ISet<string> submittedFields)
    {
        if(entity.Name!="后置拒绝")return;
        var prior=mapper.Query().Where(row=>row.Name=="回滚首行").ToList();
        if(prior.Count!=1)throw new InvalidOperationException("没有观察到真实首行DML。");
        var transaction=session.CurrentTransaction??throw new InvalidOperationException("手写规则未加入当前事务。");
        using var command=transaction.Connection!.CreateCommand();command.Transaction=transaction;
        command.CommandText="SELECT COUNT(*) FROM yz_file_ref WHERE record_id=@id AND delete_no IS NULL";
        var parameter=command.CreateParameter();parameter.ParameterName="id";parameter.Value=prior[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture);command.Parameters.Add(parameter);
        var references=Convert.ToInt64(command.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture);
        if(references!=1)throw new InvalidOperationException("没有观察到同事务文件引用。");
        var path=Environment.GetEnvironmentVariable("METARILL_GLOBAL_CROSS_EXCEL_HOOK_RECEIPT")??throw new InvalidOperationException("缺少本轮手写规则回执位置。");
        File.AppendAllText(path,System.Text.Json.JsonSerializer.Serialize(new{table={{JsonSerializer.Serialize(table)}},recordId=prior[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture),references,insideTransaction=true})+"\n");
        throw new InvalidOperationException("本轮合成后置拒绝。");
    }
}
"""+"\n";
    }
    /// <summary>只生成不可发布准备包，两轮正规生成必须保护六份手写partial。</summary>
    internal static void Create(string source,string output)
    {
        var catalog=Catalog();new CrossTableExcelApplicationPreparer().Prepare(catalog,output,RootNamespace,source);
        var generated=CSharpGenerator.SafePath(output,"backend/generated");
        var hands=catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)).ToDictionary(table=>"Services/"+GenerationModel.Pascal(table.TableName)+"Service.cs",table=>Handwritten(table.TableName),StringComparer.Ordinal);
        foreach(var hand in hands)File.WriteAllText(CSharpGenerator.SafePath(generated,hand.Key),hand.Value);
        for(var round=0;round<2;round++)
        {
            new CSharpGenerator().Generate(catalog,generated,RootNamespace);
            if(hands.Any(hand=>File.ReadAllText(CSharpGenerator.SafePath(generated,hand.Key))!=hand.Value))throw new InvalidOperationException("全局级联六模型手写规则被覆盖。");
        }
    }
    /// <summary>复用现有不跟随链接的全文件捕获；不复用八个普通模型的语义白名单。</summary>
    internal static CrossTableExcelPackageProof Capture(string package)=>CrossTableExcelNativePreparation.Capture(package);
    /// <summary>完整文件、原模型、真实认证与六个精确手写钩子共同固定，重新计算摘要不能收编额外源文件。</summary>
    internal static MetadataCatalog Verify(CrossTableExcelPackageProof proof,bool runtime=false)
    {
        if(!Path.IsPathFullyQualified(proof.PackageRoot)||Path.TrimEndingDirectorySeparator(Path.GetFullPath(proof.PackageRoot))!=proof.PackageRoot)throw new InvalidOperationException("准备包根必须规范。");
        const string journal="backend/Application.Host/.local/schema-history.jsonl";
        if(proof.Files.ContainsKey(journal)||File.Exists(CSharpGenerator.SafePath(proof.PackageRoot,".metarill-package.json")))throw new InvalidOperationException("准备包不能含运行状态或冒充公开Full。");
        var actual=Capture(proof.PackageRoot);var paths=actual.Files.Keys.ToHashSet(StringComparer.Ordinal);if(runtime)paths.Remove(journal);
        if(!paths.SetEquals(proof.Files.Keys)||proof.Files.Any(file=>!actual.Files.TryGetValue(file.Key,out var hash)||hash!=file.Value)||actual.ManifestSha256!=proof.ManifestSha256||actual.AssemblySha256!=proof.AssemblySha256||actual.MetadataSha256!=proof.MetadataSha256)throw new InvalidOperationException("六模型固定包集合或摘要漂移。");
        using var marker=JsonDocument.Parse(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,CrossTableExcelApplicationPreparer.Marker)));var value=marker.RootElement;
        if(value.GetProperty("schemaVersion").GetInt32()!=1||value.GetProperty("profile").GetString()!="cross-table-excel-preparation-v1"||value.GetProperty("readyForPublication").GetBoolean()||value.GetProperty("authenticationProfile").GetString()!="web-database-v1"||value.GetProperty("rootNamespace").GetString()!=RootNamespace)throw new InvalidOperationException("必须是六模型原生产Web不可发布准备包。");
        var catalog=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,"backend/metadata/project.json")),Json)!;
        if(!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(catalog),JsonSerializer.SerializeToNode(Catalog())))throw new InvalidOperationException("六模型实际元数据与原定义不符。");
        if(value.GetProperty("metadataSha256").GetString()!=Convert.ToHexStringLower(SHA256.HashData(CSharpGenerator.CanonicalMetadata(catalog)))||value.GetProperty("securityCatalogSha256").GetString()!=proof.MetadataSha256)throw new InvalidOperationException("实际元数据与安全绑定不符。");
        var hands=catalog.ConfigTables.Where(table=>table.TableName.EndsWith("_cross_consumer",StringComparison.Ordinal)).ToDictionary(table=>"backend/generated/Services/"+GenerationModel.Pascal(table.TableName)+"Service.cs",table=>Handwritten(table.TableName),StringComparer.Ordinal);
        var declared=value.GetProperty("files").EnumerateArray().Select(file=>file.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
        var roots=declared.Where(path=>path.EndsWith(".csproj",StringComparison.Ordinal)).Select(path=>path[..path.LastIndexOf('/')]).ToArray();
        foreach(var path in proof.Files.Keys)
            if(!declared.Contains(path)&&path!=CrossTableExcelApplicationPreparer.Marker&&!roots.Any(root=>path.StartsWith(root+"/bin/",StringComparison.Ordinal)||path.StartsWith(root+"/obj/",StringComparison.Ordinal)||path==root+"/packages.lock.json"))throw new InvalidOperationException("六模型包混入额外源文件："+path);
        foreach(var file in value.GetProperty("files").EnumerateArray())
        {
            var path=file.GetProperty("path").GetString()!;
            if(!proof.Files.TryGetValue(path,out var hash)||!hands.ContainsKey(path)&&hash!=file.GetProperty("sha256").GetString())throw new InvalidOperationException("受管来源与准备标记不符。");
        }
        foreach(var hand in hands)if(File.ReadAllText(CSharpGenerator.SafePath(proof.PackageRoot,hand.Key))!=hand.Value)throw new InvalidOperationException("六模型手写钩子不符。");
        return catalog;
    }
    /// <summary>固定包始终只读，任何真实宿主运行都使用新的完整副本。</summary>
    internal static CrossTableExcelPackageProof CopyForExecution(CrossTableExcelPackageProof proof,string destination)
    {
        Verify(proof);var root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));if(Directory.Exists(root)||File.Exists(root))throw new InvalidOperationException("六模型执行副本已存在。");
        _=CSharpGenerator.SafePath(root,".metarill-execution-boundary");Directory.CreateDirectory(root);
        foreach(var file in proof.Files){var target=CSharpGenerator.SafePath(root,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(CSharpGenerator.SafePath(proof.PackageRoot,file.Key),target);}
        var copied=proof with{PackageRoot=root};Verify(copied);return copied;
    }
}
