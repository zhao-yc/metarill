using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    private sealed record RequiredVisibilityPlan(string Name,string Definition,IReadOnlyDictionary<string,ColumnModel> Bindings,IReadOnlySet<string> ObjectRoots);

    /// <summary>页面必填只由显式可写字段声明；级联隐式辅助字段不继承页面规则。</summary>
    private static bool IsPageRequired(GenerationModel model,PageModel page,ColumnModel column) =>
        page.DeclaredWritable.Any(item=>item.Source.Id==column.Source.Id)&&model.Catalog.ConfigPageColumns.Any(field=>
            field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==column.Source.Id&&(field.IsRequired==1||GenerationModel.Enabled(field.Extra,"isMust")));

    /// <summary>把已发布的显示规则绑定到最终实体的实际值，缺少真实读写证明时拒绝发布。</summary>
    private static RequiredVisibilityPlan? RequiredVisibility(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        if(!IsPageRequired(model,page,column)||!column.Source.Extra.TryGetValue("visibleCondition",out var configured)||
            configured.ValueKind==JsonValueKind.Null)return null;
        var definition=configured.ValueKind==JsonValueKind.String?configured.GetString()!:configured.GetRawText();
        if(string.IsNullOrWhiteSpace(definition))return null;
        var rule=ButtonVisibilityRule.Compile(definition);
        if(rule.Fields.Count==0)return null;
        var bindings=new Dictionary<string,ColumnModel>(StringComparer.Ordinal);
        var objectRoots=new HashSet<string>(StringComparer.Ordinal);
        foreach(var path in rule.Fields)
        {
            var segments=path.Split('.');
            if(segments.Any(part=>part.Length==0||part is "__proto__" or "constructor" or "prototype"))
                throw new InvalidOperationException("必填显示条件包含无效的字段路径。");
            var root=segments[0];
            var field=page.Readable.SingleOrDefault(item=>item.JsonName==root);
            if(field is null||field.IsJsonArray||field.IsManagedCascadeName||field.Source.IsVirtualColumn!=0||
                !NonRedundantReadCompilation.Physical(model,table,field)||GenerationModel.Enabled(field.Source.Extra,"isJoinColumn")||
                ManagerCredentialCompilation.IsCredential(table,field)||ExactDataManageValues.IsSensitiveField(field.Source.ColumnName)||
                NonRedundantCrossParentCompilation.IsSlot(model,table,field))
                throw new InvalidOperationException("必填显示条件必须引用本页真实可读字段："+root);
            if(segments.Length>1)
            {
                if(!field.Source.LogicalType.Equals("json",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("必填显示条件嵌套字段需要JSON对象："+root);
                objectRoots.Add(root);
            }
            bindings[root]=field;
        }
        return new("RequiresVisible"+page.Source.Id+"_"+column.Source.Id,definition,bindings,objectRoots);
    }

    /// <summary>生成固定显示规则与窄字段读取，不从请求接收条件JSON或隐藏状态。</summary>
    private static string RequiredVisibilityDeclarations(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var column in page.Writable)
        {
            if(RequiredVisibility(model,table,page,column) is not {} rule)continue;
            var values=string.Join(", ",rule.Bindings.Select(pair=>$"[{Literal(pair.Key)}] = "+
                (rule.ObjectRoots.Contains(pair.Key)?$"RequiredFieldValue.VisibilityObject(entity.{pair.Value.Property})":$"entity.{pair.Value.Property}")));
            result.AppendLine($"    private static readonly global::Metarill.Core.Metadata.ButtonVisibilityRule {rule.Name}Rule=global::Metarill.Core.Metadata.ButtonVisibilityRule.Compile({Literal(rule.Definition)});");
            result.AppendLine($"    private static bool {rule.Name}({table.EntityType} entity)=>{rule.Name}Rule.Matches(new Dictionary<string,object?>{{ {values} }});");
        }
        return result.ToString();
    }
}
