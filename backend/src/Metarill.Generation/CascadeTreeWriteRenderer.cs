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
        if(CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is{} knownCross)return CrossTableWriteRenderer.Validation(model,table,page,knownCross);
        // 跨表保存未接线时，各级辅助ID/名称也必须闭合，不能只检查末级而留下部分数据。
        if(CrossTableReadCompilation.Resolve(model,table,page,source).Definition is{} cross)
        {
            var names=cross.Levels.SelectMany(level=>new[]{level.Prefix+"_id",level.Prefix+"_name"}).ToHashSet(StringComparer.Ordinal);
            var fields=table.Columns.Where(column=>!column.IsSystem&&names.Contains(column.Source.ColumnName)).ToArray();
            var crossTrigger="creating||"+string.Join("||",fields.Select(column=>"submitted.Contains("+Literal(column.Source.ColumnName)+")"));
            var populated=string.Join("||",fields.Select(column=>"!string.IsNullOrEmpty(Convert.ToString(entity."+column.Property+",CultureInfo.InvariantCulture))"));
            return $"        if(({crossTrigger})&&({populated}))throw new InvalidOperationException(\"跨表级联保存及逐级字段维护尚未接线，不能单独写入受管ID或名称。\");\n";
        }
        var result=CascadeTreeWriteCompilation.Resolve(model,table,page,source);
        if(result.Definition is not{} plan)return $"        if((creating||submitted.Contains({Literal(source.Source.ColumnName)}))&&entity.{source.Property} is not null)throw new InvalidOperationException({Literal(result.Reason!)});\n";
        var merged=CascadeTreeMergedWriteRenderer.Applies(table,plan);var trigger=merged?CascadeTreeMergedWriteRenderer.Trigger(table,plan):$"creating||submitted.Contains({Literal(source.Source.ColumnName)})||submitted.Contains({Literal(plan.Name.Source.ColumnName)})";
        return $"        if({trigger})await ValidateCascade{page.Source.Id}_{source.Source.Id}Async(entity,actor,submitted,cancellationToken{(CascadeTreeExcelRenderer.OperationAware(model,table,page)?", cascadeOperation != RecordMutationOperation.Import":"")}{(merged?", creating:creating":"")});\n";
    }

    /// <summary>原名称回显可在HTTP边界忽略，内部严格DTO无此字段，服务从当前真实节点计算。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new StringBuilder();
        foreach(var plan in CascadeTreeWriteCompilation.ForService(model,table,page))
        {
            if(CascadeTreeMergedWriteRenderer.Applies(table,plan)){result.Append(CascadeTreeMergedWriteRenderer.Service(table,page,plan));continue;}
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
                result.AppendLine("        if(path.Path.Any(node=>node.Id==entity.Id.ToString(CultureInfo.InvariantCulture)))throw new InvalidOperationException(\"不能将父节点设为自身或后代。\");")
                    .AppendLine("        if(path.Path.Count>=Metarill.Core.Cascades.SelfTreeReader.MaximumDepth)throw new InvalidOperationException(\"级联父路径已达64层，不能再挂一层。\");");
            result.AppendLine("        var actualName=path.Path[^1].Name;");
            if(int.TryParse(name.Source.ColumnLength,NumberStyles.None,CultureInfo.InvariantCulture,out var maximum)&&maximum>0)
                result.AppendLine($"        if(actualName is {{ Length: > {maximum} }})throw new ArgumentException(\"级联名称超过物理列声明长度，未保存。\");");
            result.AppendLine($"        entity.{source.Property}={(source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"id")};entity.{name.Property}=actualName;")
                .AppendLine($"        submitted.Add({Literal(source.Source.ColumnName)});submitted.Add({Literal(name.Source.ColumnName)});")
                .AppendLine("    }");
        }
        return result.Append(CrossTableWriteRenderer.Service(model,table,page)).ToString();
    }
    /// <summary>所有配置文本只能作为转义后的C#字面量。</summary>
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
}
