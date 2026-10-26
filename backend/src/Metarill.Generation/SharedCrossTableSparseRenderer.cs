using System.Text;
using System.Text.Json;
namespace Metarill.Generation;

/// <summary>共享R0按组生成每次变更状态；普通模型及共享R1输出保持原样。</summary>
internal static class SharedCrossTableSparseRenderer
{
    /// <summary>只在已经完整证明的共享R0页面接线。</summary>
    internal static bool Enabled(GenerationModel model,TableModel table,PageModel page)=>table.Source.IsRongyu==0&&SharedCrossTableCompilation.ForPage(model,table,page).Groups.Count>0;
    /// <summary>创建／更新／复制分别创建请求内状态，原值在DTO与钩子之前复制。</summary>
    internal static string Capture(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?$"        var sharedCrossMutations=CreateSharedCrossMutations{page.Source.Id}(entity);\n":"";
    /// <summary>新建与复制的私有校验器也传入同笔状态，不新增HTTP字段。</summary>
    internal static string Argument(GenerationModel model,TableModel table,PageModel page)=>Enabled(model,table,page)?",sharedCrossMutations:sharedCrossMutations":"";
    /// <summary>协调在普通关联校验之前，校验器只读取生成的组键。</summary>
    internal static string Validation(GenerationModel model,TableModel table,PageModel page,SharedCrossTableGroup group)=>$"        await NormalizeSharedCross{page.Source.Id}_{group.Key}Async(entity,actor,submitted,creating,sharedCrossMutations![{L(group.Key)}],cancellationToken{(ImportRenderer.RelationOperationAware(model,table,page)?",cascadeOperation != RecordMutationOperation.Import":"")});\n";
    /// <summary>每页独立的请求状态工厂，生成初始快照不对目标执行查询。</summary>
    internal static string Factory(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page))return "";var groups=SharedCrossTableCompilation.ForPage(model,table,page).Groups;return "    /// <summary>同笔事务共享快照与写入预期，禁止跨请求复用。</summary>\n"+$"    private static IReadOnlyDictionary<string,Metarill.Core.Cascades.SharedCrossTableSparseMutation> CreateSharedCrossMutations{page.Source.Id}({table.Name} entity)=>new Dictionary<string,Metarill.Core.Cascades.SharedCrossTableSparseMutation>{{{string.Join(",",groups.Select(g=>"["+L(g.Key)+"]=new(SharedCrossStorage"+page.Source.Id+"_"+g.Key+",SharedCross"+page.Source.Id+"_"+g.Key+","+D(g.Columns,"entity")+")"))}}};\n";
    }
    /// <summary>部分更新先恢复原逻辑值；整组省略和明确全字段清空不提前读取旧目标。</summary>
    internal static string HydrateCall(GenerationModel model,TableModel table,PageModel page)
    {
        if(!Enabled(model,table,page))return "";var b=new StringBuilder();foreach(var g in SharedCrossTableCompilation.ForPage(model,table,page).Groups){var ids=g.Plans.SelectMany(p=>p.Levels).Select(l=>l.Id).DistinctBy(c=>c.Source.Id).ToArray();var available=ids.Where(c=>page.Writable.Any(w=>w.Source.Id==c.Source.Id)).ToArray();if(available.Length==0)continue;var any=string.Join("||",available.Select(c=>"request."+c.Property+".IsSpecified"));var all=available.Length==ids.Length?string.Join("&&",available.Select(c=>"request."+c.Property+".IsSpecified")):"false";b.AppendLine($"        if(({any})&&!({all}))await HydrateSharedCross{page.Source.Id}_{g.Key}Async(entity,actor,sharedCrossMutations[{L(g.Key)}],cancellationToken);");}return b.ToString();
    }
    /// <summary>每组只编码一次，不能调用成员旧PackCross相互覆盖。</summary>
    internal static string PackCall(GenerationModel model,TableModel table,PageModel page)=>!Enabled(model,table,page)?"":string.Concat(SharedCrossTableCompilation.ForPage(model,table,page).Groups.Select(g=>$"        PackSharedCross{page.Source.Id}_{g.Key}(entity,submitted,sharedCrossMutations[{L(g.Key)}]);\n"));
    /// <summary>复制原始位须为合法的独立末端集合，不能静默丢弃坏历史祖先。</summary>
    internal static string CopyValidation(GenerationModel model,TableModel table,PageModel page)=>!Enabled(model,table,page)?"":string.Concat(SharedCrossTableCompilation.ForPage(model,table,page).Groups.Select(g=>$"        _=SharedCrossStorage{page.Source.Id}_{g.Key}.ReadStored({D(g.Columns.Where(c=>c.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)),"source")});\n"));
    /// <summary>DML与通知之间核验真实ID和实际完整路径，不补写修复。</summary>
    internal static string After(GenerationModel model,TableModel table,PageModel page)=>!Enabled(model,table,page)?"":string.Concat(SharedCrossTableCompilation.ForPage(model,table,page).Groups.Select(g=>$"        await VerifySharedCross{page.Source.Id}_{g.Key}Async(entity,actor,parentId,sharedCrossMutations[{L(g.Key)}],cancellationToken);\n"));
    /// <summary>读原值与最终核验复用原当前锁读；只有本次主动路径才检查候选list动作。</summary>
    internal static string GroupService(GenerationModel model,TableModel table,PageModel page,SharedCrossTableGroup group)
    {
        var dependencyFields=group.Plans.SelectMany(p=>p.Levels).Select(l=>l.Id.Source.Id).ToHashSet(StringComparer.Ordinal);var dependencyJson=group.Plans.SelectMany(p=>p.Levels).Select(l=>l.Id.JsonName).ToHashSet(StringComparer.Ordinal);var dependents=RelationCompilation.ForPage(model,page).Concat(RelationCompilation.ForPage(model,page,true)).DistinctBy(r=>r.SourceColumn.Source.Id).Where(r=>r.Conditions?.Forms.Any(f=>dependencyFields.Contains(f.Column.Source.Id))==true||ControlRenderer.RelationContext(page,r,false).TargetParentField is{} parent&&dependencyJson.Contains(parent)).Select(r=>r.SourceColumn).DistinctBy(c=>c.Source.Id).ToArray();var restore=dependents.Length==0?"false":"!creating&&("+string.Join("||",dependents.Select(c=>"(submitted.Contains("+L(c.Source.ColumnName)+")&&"+(c.Type=="string?"?"!string.IsNullOrWhiteSpace(entity."+c.Property+")"+(c.IsJsonArray?"&&entity."+c.Property+"!=\"[]\"":""):"entity."+c.Property+" is not null")+")"))+")";
        var suffix=page.Source.Id+"_"+group.Key;var b=new StringBuilder();var ids=group.Plans.SelectMany(p=>p.Levels).Select(l=>l.Id).DistinctBy(c=>c.Source.Id).ToArray();var names=group.Plans.SelectMany(p=>p.Levels).Select(l=>l.Name).DistinctBy(c=>c.Source.Id).ToArray();
        b.AppendLine($"    private static readonly Metarill.Core.Metadata.SharedCrossTableSparseSelection SharedCrossStorage{suffix}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileSharedSelection({L(table.Source.Id)},{L(group.Key)});")
            .AppendLine("    /// <summary>固定分支委托只返回真实路径的本组字段，不接收请求目录或SQL。</summary>")
            .AppendLine($"    private async Task<IReadOnlyDictionary<string,object?>> ReadSharedCross{suffix}PathAsync(string control,IReadOnlyDictionary<string,object?> input,RuntimeActor actor,CancellationToken token,bool requireTargetList)")
            .AppendLine("    {").AppendLine("        switch(control)").AppendLine("        {");
        foreach(var plan in group.Plans){var columns=plan.Levels.SelectMany(l=>new[]{l.Id,l.Name}).ToArray();b.AppendLine($"            case {L(plan.Read.Source.Source.Id)}:").AppendLine("            {").AppendLine($"                var copy=new {table.Name}{{{string.Join(",",columns.Select(c=>c.Property+"=("+c.Type+")input["+L(c.Source.ColumnName)+"]"))}}};var local=new HashSet<string>(StringComparer.Ordinal);").AppendLine($"                await NormalizeCross{page.Source.Id}_{plan.Read.Source.Source.Id}Async(copy,actor,local,token,requireTargetList);return {D(columns,"copy")};").AppendLine("            }");}
        b.AppendLine("            default:throw new InvalidOperationException(\"未知共享R0控件。\");").AppendLine("        }").AppendLine("    }")
            .AppendLine("    /// <summary>部分合并前只读取实际末端并恢复共同祖先，同请求原值缓存不重复读。</summary>")
            .AppendLine($"    private async Task HydrateSharedCross{suffix}Async({table.Name} entity,RuntimeActor actor,Metarill.Core.Cascades.SharedCrossTableSparseMutation state,CancellationToken token)").AppendLine("    {")
            .AppendLine($"        var values=await state.HydrateAsync((control,input,ct)=>ReadSharedCross{suffix}PathAsync(control,input,actor,ct,false),token);");
        foreach(var c in group.Columns)b.AppendLine($"        entity.{c.Property}=({c.Type})values[{L(c.Source.ColumnName)}];");b.AppendLine("    }")
            .AppendLine("    /// <summary>DTO／钩子之后按原逻辑值协调，省略与null不混淆，冲突主动路径整笔拒绝。</summary>")
            .AppendLine($"    private async Task NormalizeSharedCross{suffix}Async({table.Name} entity,RuntimeActor actor,ISet<string> submitted,bool creating,Metarill.Core.Cascades.SharedCrossTableSparseMutation state,CancellationToken token,bool requireTargetList=true)").AppendLine("    {")
            .AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"共享R0必须加入原事务。\");")
            .AppendLine($"        var result=await state.NormalizeAsync({D(group.Columns,"entity")},submitted.ToHashSet(StringComparer.Ordinal),creating,(control,input,ct)=>ReadSharedCross{suffix}PathAsync(control,input,actor,ct,requireTargetList),(control,input,ct)=>ReadSharedCross{suffix}PathAsync(control,input,actor,ct,false),token,restoreForDependencies:{restore});");
        foreach(var c in group.Columns)b.AppendLine($"        entity.{c.Property}=({c.Type})result.Values[{L(c.Source.ColumnName)}];");b.AppendLine("        foreach(var field in result.Fields)submitted.Add(field);").AppendLine("    }")
            .AppendLine("    /// <summary>一次整组打包，仅物理末端进入DML，逻辑预期保留在请求状态。</summary>")
            .AppendLine($"    private static void PackSharedCross{suffix}({table.Name} entity,ISet<string> submitted,Metarill.Core.Cascades.SharedCrossTableSparseMutation state)").AppendLine("    {").AppendLine("        var packed=state.Pack();")
            .AppendLine("        if(packed is null)").AppendLine("        {");
        foreach(var c in ids)b.AppendLine($"            entity.{c.Property}=({c.Type})state.OriginalStoredIds[{L(c.Source.ColumnName)}];");foreach(var c in names)b.AppendLine($"            entity.{c.Property}=null;");b.AppendLine("            return;").AppendLine("        }");
        foreach(var c in ids)b.AppendLine($"        entity.{c.Property}=({c.Type})packed[{L(c.Source.ColumnName)}];submitted.Add({L(c.Source.ColumnName)});");foreach(var c in names)b.AppendLine($"        entity.{c.Property}=null;submitted.Remove({L(c.Source.ColumnName)});");b.AppendLine("    }");
        var identity=FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(s=>s.Column).DistinctBy(c=>c.Source.Id).ToArray();var projection=ids.Concat(identity).DistinctBy(c=>c.Source.Id).ToArray();b.AppendLine("    /// <summary>真实原始位先核验，随后以当前锁读恢复路径比较写入前预期；作用域不能被数据库改写。</summary>")
            .AppendLine($"    private async Task VerifySharedCross{suffix}Async({table.Name} entity,RuntimeActor actor,string? parentId,Metarill.Core.Cascades.SharedCrossTableSparseMutation state,CancellationToken token)").AppendLine("    {")
            .AppendLine($"        var stored=await Scope{page.Source.Id}(actor,parentId).Where(row=>row.Id==entity.Id).ForUpdate().FirstAsync(row=>new {table.Name}{{{string.Join(",",projection.Select(c=>c.Property+"=row."+c.Property))}}},token);")
            .AppendLine($"        if(stored is null{string.Concat(identity.Select(c=>"||stored."+c.Property+"!=entity."+c.Property))})throw new InvalidOperationException(\"共享R0存储后的真实作用域不一致，未提交。\");")
            .AppendLine($"        await state.VerifyStoredAsync({D(ids,"stored")},(control,input,ct)=>ReadSharedCross{suffix}PathAsync(control,input,actor,ct,false),token);").AppendLine("    }");return b.ToString();
    }
    /// <summary>输出字段来自固定模型；所有配置文本作为转义字符串常量。</summary>
    private static string D(IEnumerable<ColumnModel> columns,string entity)=>"new Dictionary<string,object?>{"+string.Join(",",columns.DistinctBy(c=>c.Source.Id).Select(c=>"["+L(c.Source.ColumnName)+"]="+entity+"."+c.Property))+"}";
    /// <summary>不将元数据直接拼成C#字符串内容。</summary>
    private static string L(string value)=>JsonSerializer.Serialize(value);
}
