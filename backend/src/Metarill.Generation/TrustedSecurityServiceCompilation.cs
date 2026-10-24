using System.Text.Json;

namespace Metarill.Generation;

/// <summary>只接受内部factory签发的冻结模块，不从业务JSON、Binding同形字段或磁盘文件存在推断可信。</summary>
internal sealed class TrustedSecurityServiceCompilation
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string,string> Tables=new Dictionary<string,string>(StringComparer.Ordinal)
    {
        ["6031906809708544"]="yz_manager",["1692532858499969026"]="yz_role",["1696799308939239425"]="yz_admin_type"
    };
    private readonly string publishedJson,rootNamespace;
    private TrustedSecurityServiceCompilation(string publishedJson,string rootNamespace){this.publishedJson=publishedJson;this.rootNamespace=rootNamespace;}

    /// <summary>签发检查由factory完成；编译器只绑定其完整私有快照，后续模型变化直接拒绝。</summary>
    internal static GenerationModel Bind(GenerationModel model,SecurityManagementModelSet models)
    {
        var issued=SecurityManagementModuleFactory.RequirePublishedForGeneration(models);
        var snapshot=JsonSerializer.Serialize(issued,Json);
        if(snapshot!=JsonSerializer.Serialize(model.Catalog,Json))throw new InvalidOperationException("可信安全模块的发布目录已变化，不能选择安全partial模板。");
        return model with{TrustedSecurityServices=new(snapshot,model.Namespace)};
    }
    /// <summary>表名和稳定ID同时匹配；普通业务表及普通Generate路径保持可选空壳。</summary>
    internal static bool Applies(GenerationModel model,TableModel table)
    {
        if(model.TrustedSecurityServices is not{} proof)return false;
        if(model.Namespace!=proof.rootNamespace||JsonSerializer.Serialize(model.Catalog,Json)!=proof.publishedJson)throw new InvalidOperationException("可信安全模块编译快照已变化。");
        if(!Tables.TryGetValue(table.Source.Id,out var name))return false;
        if(table.Source.TableName!=name||table.Name!=GenerationModel.Pascal(name)||!ReferenceEquals(table.Source,model.Catalog.ConfigTables.Single(item=>item.Id==table.Source.Id)))throw new InvalidOperationException("可信安全服务的稳定表身份不一致。");
        if(table.Pages.Any(page=>page.Source.IsCopy==1||ImportRenderer.Enabled(page)))throw new InvalidOperationException("可信安全模块首期不开放复制或导入。");
        return true;
    }
    /// <summary>受控模板来自编译资源，只参数化正常命名空间；路径不能由元数据指定。</summary>
    internal static string Template(GenerationModel model,TableModel table)
    {
        if(!Applies(model,table))throw new InvalidOperationException("安全partial缺少可信模块证明。");
        var suffix=".Trusted"+table.Name+"Service.cs.tpl";var assembly=typeof(CSharpGenerator).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name=>name.EndsWith(suffix,StringComparison.Ordinal)))!;
        using var reader=new StreamReader(stream);return reader.ReadToEnd().Replace("{{NAMESPACE}}",model.Namespace,StringComparison.Ordinal).Replace("\r\n","\n").Replace('\r','\n');
    }
    /// <summary>手写方法即使被误清空，生成代码仍验证实际同事务、身份、目标及操作准备。</summary>
    internal static string RequirePrepared(GenerationModel model,TableModel table,string creating,string deleting)=>Applies(model,table)
        ? "        SecurityRealmMutationGuard.RequirePrepared(guards,session.CurrentTransaction,\""+table.Source.TableName+"\",entity.Id,actor,"+creating+","+deleting+");\n":"";
    /// <summary>构造阶段固定守卫集合和真实协调器顺序；实例存在不是准备证明，写入时仍重新核验pending。</summary>
    internal static string GuardsAssignment(GenerationModel model,TableModel table)=>Applies(model,table)
        ? "var fixedGuards=(guards??[]).ToArray(); if(fixedGuards.Length==0 || fixedGuards[0] is not SecurityRealmMutationGuard || fixedGuards.OfType<SecurityRealmMutationGuard>().Count()!=1) throw new InvalidOperationException(\"可信安全Service要求唯一且首位的实际协调器。\"); this.guards=Array.AsReadOnly(fixedGuards);"
        : "this.guards = guards ?? [];";
}
