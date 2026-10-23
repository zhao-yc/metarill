using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>跨表默认值/钩子后按最深ID归一化，各级当前读锁保持到原事务结束。</summary>
internal static class CrossTableWriteRenderer
{
    /// <summary>只有创建或关联字段参与更新时才刷新，省略关联保留原受管名称。</summary>
    internal static string Trigger(CrossTableWriteModel plan,string creating="creating")=>creating+"||"+string.Join("||",plan.Levels.SelectMany(level=>new[]{level.Id,level.Name}).Select(column=>"submitted.Contains("+Literal(column.Source.ColumnName)+")"));
    /// <summary>只有来源表参与目标链时，来源的名称、父键和目标SQL条件变化才影响当前图。</summary>
    internal static bool Merged(TableModel table,CrossTableReadModel plan)=>plan.Levels.Any(level=>level.Target.Source.Id==table.Source.Id);
    /// <summary>依赖从固定元数据取得，不使用请求目录；省略关联时仅复核图而保留已存名称。</summary>
    private static IEnumerable<string> Dependencies(TableModel table,CrossTableWriteModel plan)=>plan.Levels.Where(level=>level.Read.Target.Source.Id==table.Source.Id)
        .SelectMany(level=>new[]{level.Read.Name}.Concat(level.Read.Parent is{} parent?new[]{parent}:[]).Concat(level.Read.Page.Scopes.Select(scope=>scope.Column)).Concat(level.Read.Page.Condition is{} condition?condition.Syntax.Fields.Select(field=>table.Columns.Single(column=>column.Source.ColumnName==field)):[]))
        .Concat(table.Columns.Where(column=>column.Source.ColumnName is "yz_project_id" or "delete_no")).Select(column=>column.Source.ColumnName).Distinct(StringComparer.Ordinal);
    /// <summary>普通独立链保持原触发条件；来源图依赖变化额外触发最终校验。</summary>
    internal static string Trigger(TableModel table,CrossTableWriteModel plan)=>Trigger(plan)+(Merged(table,plan.Read)?string.Concat(Dependencies(table,plan).Select(field=>"||submitted.Contains("+Literal(field)+")")):"");
    /// <summary>每个计划共享读取工厂，私有导入动作决定目标list动作，不由请求选择。</summary>
    internal static string Validation(GenerationModel model,TableModel table,PageModel page,CrossTableWriteModel plan)=>$"        if({Trigger(table,plan)})await NormalizeCross{page.Source.Id}_{plan.Read.Source.Source.Id}Async(entity,actor,submitted,cancellationToken{(CascadeTreeExcelRenderer.OperationAware(model,table,page)?",cascadeOperation != RecordMutationOperation.Import":"")}{(Merged(table,plan.Read)?",creating:creating":"")});\n";
    /// <summary>先反查完整当前路径，再统一重写ID及名称，拒绝链后段错误而不产生半成品。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var output=new StringBuilder();
        foreach(var plan in CrossTableWriteCompilation.ForService(model,table,page))
        {
            var suffix=page.Source.Id+"_"+plan.Read.Source.Source.Id;var merged=Merged(table,plan.Read);
            output.AppendLine("    /// <summary>最深非空ID决定路径；用户祖先ID与名称不作为可信值，部分选择清空后段。</summary>")
                .AppendLine($"    private async Task NormalizeCross{suffix}Async({table.Name} entity,RuntimeActor actor,ISet<string> submitted,CancellationToken cancellationToken,bool requireTargetList=true{(merged?",bool creating=false":"")})")
                .AppendLine("    {").AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"跨表保存必须加入原事务。\");ValidateActor(actor);if(relationAuthorization is null||relationAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"跨表保存要求当前真实身份。\");var deepest=-1;long? selected=null;");
            if(merged)output.AppendLine($"        if(!({Trigger(plan)}))return;");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var id=plan.Levels[index].Id;output.AppendLine($"        var text{index}=Convert.ToString(entity.{id.Property},CultureInfo.InvariantCulture);if(!string.IsNullOrWhiteSpace(text{index})){{if(!long.TryParse(text{index},NumberStyles.None,CultureInfo.InvariantCulture,out var key{index})||key{index}<=0||text{index}!=key{index}.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"跨表级联ID须为规范正整数。\");deepest={index};selected=key{index};}}");
            }
            output.AppendLine($"        var path=deepest<0?null:await CrossReader{suffix}(actor,true,requireTargetList{(merged?",entity,creating":"")}).PathAsync(deepest,selected,cancellationToken);");
            for(var index=0;index<plan.Levels.Count;index++)
            {
                var level=plan.Levels[index];var id=level.Id;var name=level.Name;var clear=id.Type=="string?"?$"entity.{id.Property} is null?null:\"\"":"null";var value=id.Type=="string?"?$"path!.Path[{index}].Id":$"long.Parse(path!.Path[{index}].Id,CultureInfo.InvariantCulture)";
                output.AppendLine($"        if(deepest>={index}){{entity.{id.Property}={value};entity.{name.Property}=path!.Path[{index}].Name;}}else{{entity.{id.Property}={clear};entity.{name.Property}={(id.Type=="string?"?$"entity.{id.Property} is null?null:\"\"":"null")};}}submitted.Add({Literal(id.Source.ColumnName)});submitted.Add({Literal(name.Source.ColumnName)});");
            }
            output.AppendLine("    }");
            output.Append(NonRedundantCrossTableCompilation.PackService(model,table,page,plan));
            var check=plan.Levels.SelectMany(level=>table.Source.IsRongyu==0?new[]{level.Id}:new[]{level.Id,level.Name}).Concat(FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>scope.Column)).DistinctBy(column=>column.Source.Id).ToArray();var projection=string.Join(",",check.Select(column=>column.Property+"=row."+column.Property));
            output.AppendLine("    /// <summary>读取实际存储的受管字段，数据库截断或触发器改写时在观察者前整笔回滚，不补写修复。</summary>")
                .AppendLine($"    private async Task VerifyStoredCross{suffix}Async({table.Name} entity,RuntimeActor actor,ISet<string> submitted,bool creating,CancellationToken cancellationToken,bool requireTargetList=true)")
                .AppendLine("    {").AppendLine($"        if(!({Trigger(table,plan)}))return;var stored=await session.Select<{table.Name}>().Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {table.Name}{{{projection}}},cancellationToken);")
                .AppendLine($"        if(stored is null||{string.Join("||",check.Select(column=>"stored."+column.Property+"!=entity."+column.Property))})throw new InvalidOperationException(\"跨表受管ID、名称或归属在存储后发生变化，未提交。\");");
            if(merged)output.Append(FinalPath(table,page,plan));output.AppendLine("    }");
        }
        return output.ToString();
    }
    /// <summary>归一化后仍须满足调用者的项目和父上下文，不能由路径或手写钩子改归属。</summary>
    internal static string ScopeCheck(GenerationModel model,TableModel table,PageModel page)
    {
        if(CrossTableWriteCompilation.ForService(model,table,page).Count==0)return "";return string.Concat(FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>
        {
            var column=scope.Column;var input=scope.Source=="project"?"actor.ProjectId":"parentId!";var expected=column.Type=="string?"?input:column.Type is "long" or "long?"?$"long.Parse({input},CultureInfo.InvariantCulture)":$"int.Parse({input},CultureInfo.InvariantCulture)";
            return $"        if({(scope.Source=="parent"?NonRedundantCrossParentCompilation.SubmittedCondition(model,table,page,column):"")}entity.{column.Property}!={expected})throw new InvalidOperationException(\"跨表归一化不能改变当前项目或页面父归属。\");\n";
        }));
    }
    /// <summary>所有正常DML之后、通知之前仅核验存储，不新增一次修复DML。</summary>
    internal static string After(GenerationModel model,TableModel table,PageModel page,bool creating,bool importContext=false)=>string.Concat(CrossTableWriteCompilation.ForService(model,table,page).Select(plan=>$"        await VerifyStoredCross{page.Source.Id}_{plan.Read.Source.Source.Id}Async(entity,actor,submitted,{creating.ToString().ToLowerInvariant()},cancellationToken{(importContext?",authorizationOperation != RecordMutationOperation.Import":"")});\n"));
    /// <summary>正常DML后重新读取真实SQL路径，不使用写前覆盖实体或补写；刷新时还须逐级名称一致。</summary>
    private static string FinalPath(TableModel table,PageModel page,CrossTableWriteModel plan)
    {
        var suffix=page.Source.Id+"_"+plan.Read.Source.Source.Id;var result=new StringBuilder().AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"跨表最终校验要求原事务。\");")
            .AppendLine($"        var refresh={Trigger(plan)};var deepest=-1;long? selected=null;");
        for(var index=0;index<plan.Levels.Count;index++){var level=plan.Levels[index];result.AppendLine($"        var actual{index}=Convert.ToString(stored.{level.Id.Property},CultureInfo.InvariantCulture);if(!string.IsNullOrWhiteSpace(actual{index})){{if(!long.TryParse(actual{index},NumberStyles.None,CultureInfo.InvariantCulture,out var key{index})||key{index}<=0||actual{index}!=key{index}.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException(\"跨表已存ID无效。\");deepest={index};selected=key{index};}}");}
        result.AppendLine($"        if(deepest<0)return;var path=await CrossReader{suffix}(actor,true,refresh&&requireTargetList).PathAsync(deepest,selected,cancellationToken);");
        for(var index=0;index<plan.Levels.Count;index++)
        {
            var level=plan.Levels[index];if(level.Read.Target.Source.Id==table.Source.Id)result.AppendLine($"        if(creating&&deepest>={index}&&path.Path[{index}].Id==entity.Id.ToString(CultureInfo.InvariantCulture))throw new InvalidOperationException(\"跨表路径不能包含本次新建节点。\");");
            result.AppendLine($"        if(deepest>={index}&&(actual{index}!=path.Path[{index}].Id||refresh&&stored.{level.Name.Property}!=path.Path[{index}].Name))throw new InvalidOperationException(\"跨表已存各级ID或名称与最终路径不一致，未提交。\");");
        }
        return result.ToString();
    }
    /// <summary>元数据只作为C#字符串字面量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
