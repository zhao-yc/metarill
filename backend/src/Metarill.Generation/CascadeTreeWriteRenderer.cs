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
        if(CrossTableWriteCompilation.Resolve(model,table,page,source).Definition is{} knownCross)return SharedCrossTableCompilation.ForSource(model,table,page,source.Source.Id) is not null?"":CrossTableWriteRenderer.Validation(model,table,page,knownCross);
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
        var merged=CascadeTreeMergedWriteRenderer.Applies(table,plan);var trigger=merged?CascadeTreeMergedWriteRenderer.Trigger(table,plan):"creating||"+Changed(plan);
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
                .AppendLine($"    private async Task ValidateCascade{suffix}Async({table.EntityType} entity,RuntimeActor actor,ISet<string> submitted,CancellationToken cancellationToken,bool requireTargetList=true)")
                .AppendLine("    {")
                .AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"级联写入必须加入当前事务。\");ValidateActor(actor);")
                .AppendLine("        if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"级联写入要求真实当前身份。\");")
                .AppendLine($"        var text=Convert.ToString(entity.{source.Property},CultureInfo.InvariantCulture);")
                .AppendLine("        if(string.IsNullOrWhiteSpace(text))")
                .AppendLine("        {")
                .AppendLine(source.Type=="string?"?$"            entity.{source.Property}=entity.{source.Property} is null?null:\"\";entity.{name.Property}=entity.{source.Property} is null?null:\"\";":$"            entity.{name.Property}=null;")
                .AppendLine($"            submitted.Add({Literal(source.Source.ColumnName)});submitted.Add({Literal(name.Source.ColumnName)});return;")
                .AppendLine("        }")
                .AppendLine("        "+ParseKey(plan.Read))
                .AppendLine($"        var path=await CascadeReader{suffix}(actor,true,false,requireTargetList).PathAsync(id,cancellationToken);");
            if(plan.Read.Target.Source.Id==table.Source.Id&&plan.Read.Parent.Source.Id==source.Source.Id)
                result.AppendLine($"        if(path.Path.Any(node=>node.Id=={CascadeTreeTopologyRenderer.NodeKeyText(plan.Read,"entity")}))throw new InvalidOperationException(\"不能将父节点设为自身或后代。\");")
                    .AppendLine("        if(path.Path.Count>=Metarill.Core.Cascades.SelfTreeReader.MaximumDepth)throw new InvalidOperationException(\"级联父路径已达64层，不能再挂一层。\");");
            result.AppendLine("        var actualName=path.Path[^1].Name;");
            if(table.Source.IsRongyu==1&&int.TryParse(name.Source.ColumnLength,NumberStyles.None,CultureInfo.InvariantCulture,out var maximum)&&maximum>0)
                result.AppendLine($"        if(actualName is {{ Length: > {maximum} }})throw new ArgumentException(\"级联名称超过物理列声明长度，未保存。\");");
            result.AppendLine($"        entity.{source.Property}={AssignedKey(plan.Read)};entity.{name.Property}=actualName;")
                .AppendLine($"        submitted.Add({Literal(source.Source.ColumnName)});submitted.Add({Literal(name.Source.ColumnName)});")
                .AppendLine("    }");
        }
        return result.Append(CrossTableWriteRenderer.Service(model,table,page)).Append(SharedCrossTableRenderer.Service(model,table,page)).ToString();
    }
    /// <summary>按目标与来源的关系选择写后校验，所有检查均在原事务内先于观察者执行。</summary>
    internal static string After(GenerationModel model, TableModel table, PageModel page, bool creating, bool importContext = false) =>
        CascadeTreeTopologyRenderer.After(model, table, page, creating) +
        CascadeTreeMergedWriteRenderer.After(model, table, page, creating, importContext) +
        string.Concat(CascadeTreeWriteCompilation.ForService(model, table, page)
            .Where(plan => plan.Read.Target.Source.Id != table.Source.Id)
            .Select(plan => VerifyIndependentTree(model, table, page, plan, creating)));

    /// <summary>目标链已锁定并验证，只需锁读来源行，确认数据库保存了这次授权的节点。</summary>
    private static string VerifyIndependentTree(GenerationModel model, TableModel table, PageModel page,
        CascadeTreeWriteModel plan, bool creating)
    {
        var leaf = plan.Read.Source;
        var cachedName = table.Source.IsRongyu == 1 && NonRedundantReadCompilation.Physical(model, table, plan.Name) ? plan.Name : null;
        var scope = FixedPageQueryCompilation.For(table, page).IdentityScopes.Select(scope => scope.Column).ToArray();
        var fields = new[] { leaf, cachedName }.OfType<ColumnModel>().Concat(scope).DistinctBy(column => column.Source.Id);
        var projection = string.Join(", ", fields.Select(column => column.Property + "=row." + column.Property));
        var scopeMismatch = string.Concat(scope.Select(column => $" || storedTree.{column.Property} != entity.{column.Property}"));
        var verifyName = cachedName is null ? "" :
            $"            if (storedTree.{cachedName.Property} != entity.{cachedName.Property})throw new InvalidOperationException(\"自树关联节点名称在存储后发生变化，未提交。\");\n";
        return $$"""
        if ({{(creating ? "true" : Changed(plan))}})
        {
            var storedTree = await session.Select<{{table.EntityType}}>().Where(row => row.Id == entity.Id).ForUpdate()
                .FirstAsync(row => new {{table.EntityType}} { {{projection}} }, cancellationToken);
            if (storedTree is null{{scopeMismatch}})throw new InvalidOperationException("自树来源记录在存储后不存在或归属已变化，未提交。");
            if (storedTree.{{leaf.Property}} != entity.{{leaf.Property}})throw new InvalidOperationException("自树关联节点在存储后发生变化，未提交。");
{{verifyName}}        }

""";
    }

    /// <summary>显式提交叶键或刷新名称时都维护同一份关联。</summary>
    private static string Changed(CascadeTreeWriteModel plan) =>
        $"submitted.Contains({Literal(plan.Read.Source.Source.ColumnName)}) || submitted.Contains({Literal(plan.Name.Source.ColumnName)})";

    /// <summary>非空文本键使用原值；整数键继续要求原规范十进制表示。</summary>
    internal static string ParseKey(CascadeTreeReadModel plan)=>plan.TextKey?"var id=text!;":
        "if(!long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var id)||id<=0||text!=id.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"级联ID必须是不带前导零的正整数。\");";

    internal static string AssignedKey(CascadeTreeReadModel plan)=>!plan.TextKey&&plan.Source.Type=="string?"?"id.ToString(CultureInfo.InvariantCulture)":"id";
    /// <summary>所有配置文本只能作为转义后的C#字面量。</summary>
    private static string Literal(string text)=>JsonSerializer.Serialize(text);
}
