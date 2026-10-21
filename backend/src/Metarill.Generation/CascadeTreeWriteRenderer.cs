using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>默认值和手写钩子之后，在同一源事务当前读校验完整树链并同步叶子名称。</summary>
internal static class CascadeTreeWriteRenderer
{
    /// <summary>不依赖页面可见性；未知级联不能按普通字符串或宽外键规则放行。</summary>
    internal static string Validation(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        var result=CascadeTreeWriteCompilation.Resolve(model,table,page,source);
        if(result.Definition is not{} plan)return $"        if((creating||submitted.Contains({Literal(source.Source.ColumnName)}))&&entity.{source.Property} is not null)throw new InvalidOperationException({Literal(result.Reason!)});\n";
        return $"        if(creating||submitted.Contains({Literal(source.Source.ColumnName)})||submitted.Contains({Literal(plan.Name.Source.ColumnName)}))await ValidateCascade{page.Source.Id}_{source.Source.Id}Async(entity,actor,submitted,cancellationToken{(CascadeTreeExcelRenderer.OperationAware(model,table,page)?", cascadeOperation != RecordMutationOperation.Import":"")});\n";
    }

    /// <summary>原名称回显可在HTTP边界忽略，内部严格DTO无此字段，服务从当前真实节点计算。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var plan in CascadeTreeWriteCompilation.ForService(model,table,page))
        {
            var source=plan.Read.Source;var name=plan.Name;var suffix=page.Source.Id+"_"+source.Source.Id;
            result.AppendLine("    /// <summary>只在关联被提交、复制/新增或规则显式刷新时维护名称，省略保持原值。</summary>")
                .AppendLine($"    private async Task ValidateCascade{suffix}Async({table.Name} entity,RuntimeActor actor,ISet<string> submitted,CancellationToken cancellationToken,bool requireTargetList=true)")
                .AppendLine("    {")
                .AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"级联写入必须加入当前事务。\");ValidateActor(actor);")
                .AppendLine("        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"级联写入要求真实当前身份。\");")
                .AppendLine($"        var text=Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture);")
                .AppendLine("        if(string.IsNullOrWhiteSpace(text))")
                .AppendLine("        {")
                .AppendLine(source.Type=="string?"?$"            entity.{source.Property}=entity.{source.Property} is null?null:\"\";entity.{name.Property}=entity.{source.Property} is null?null:\"\";":$"            entity.{name.Property}=null;")
                .AppendLine($"            submitted.Add({Literal(source.Source.ColumnName)});submitted.Add({Literal(name.Source.ColumnName)});return;")
                .AppendLine("        }")
                .AppendLine("        if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"级联ID必须是不带前导零的正整数。\");")
                .AppendLine($"        var path=await CascadeReader{suffix}(actor,true,false,requireTargetList).PathAsync(id,cancellationToken);");
            if(plan.Read.Target.Source.Id==table.Source.Id&&plan.Read.Parent.Source.Id==source.Source.Id)
                result.AppendLine("        if(path.Path.Any(node=>node.Id==entity.Id.ToString(CultureInfo.InvariantCulture)))throw new InvalidOperationException(\"不能将父节点设为自身或后代。\");");
            result.AppendLine("        var actualName=path.Path[^1].Name;");
            if(int.TryParse(name.Source.ColumnLength,NumberStyles.None,CultureInfo.InvariantCulture,out var maximum)&&maximum>0)
                result.AppendLine($"        if(actualName is {{ Length: > {maximum} }})throw new ArgumentException(\"级联名称超过物理列声明长度，未保存。\");");
            result.AppendLine($"        entity.{source.Property}={(source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"id")};entity.{name.Property}=actualName;")
                .AppendLine($"        submitted.Add({Literal(source.Source.ColumnName)});submitted.Add({Literal(name.Source.ColumnName)});")
                .AppendLine("    }");
        }
        return result.ToString();
    }
    /// <summary>所有配置文本只能作为转义后的C#字面量。</summary>
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
}
