using System.Text;
using System.Text.Json;
namespace Metarill.Generation;
/// <summary>共享组只走一次协调；非共享链保持原生成文本和事务入口。</summary>
internal static class SharedCrossTableRenderer
{
    /// <summary>只给真正共享的私有验证器追加原快照参数，公开API不变。</summary>
    internal static string Signature(GenerationModel model,TableModel table,PageModel page)=>SharedCrossTableCompilation.ForPage(model,table,page).Groups.Count==0?"":table.Source.IsRongyu==0?",IReadOnlyDictionary<string,Metarill.Core.Cascades.SharedCrossTableSparseMutation>? sharedCrossMutations=null":",IReadOnlyDictionary<string,object?>? originalCrossShared=null";
    /// <summary>更新把锁定行原值传给同一组协调器，不能用客户端字段猜原值。</summary>
    internal static string Argument(GenerationModel model,TableModel table,PageModel page)=>SharedCrossTableCompilation.ForPage(model,table,page).Groups.Count==0?"":table.Source.IsRongyu==0?",sharedCrossMutations:sharedCrossMutations":",originalCrossShared:originalCrossShared";
    /// <summary>读取原行后、DTO和钩子之前捕获全部共享槽；新增和复制以创建语义处理。</summary>
    internal static string Capture(GenerationModel model,TableModel table,PageModel page)
    {
        if(SharedCrossTableSparseRenderer.Enabled(model,table,page))return SharedCrossTableSparseRenderer.Capture(model,table,page);
        var columns=SharedCrossTableCompilation.ForPage(model,table,page).Groups.SelectMany(g=>g.Columns).DistinctBy(c=>c.Source.Id).ToArray();return columns.Length==0?"":"        var originalCrossShared="+Dictionary(columns,"entity")+";\n";
    }
    /// <summary>只有组内首个控件发起整组协调，不能顺序归一化两次覆盖共享字段。</summary>
    internal static string Validation(GenerationModel model,TableModel table,PageModel page,SharedCrossTableGroup group,ColumnModel source)=>source.Source.Id!=group.Key?"":table.Source.IsRongyu==0?SharedCrossTableSparseRenderer.Validation(model,table,page,group):$"        await NormalizeSharedCross{page.Source.Id}_{group.Key}Async(entity,actor,submitted,creating,originalCrossShared,cancellationToken{(ImportRenderer.RelationOperationAware(model,table,page)?",cascadeOperation != RecordMutationOperation.Import":"")});\n";
    /// <summary>先归一化全部共享组，再由普通关联规则校验最终父值；避免按旧父范围放过其它关联。</summary>
    internal static string ValidateAll(GenerationModel model,TableModel table,PageModel page)=>string.Concat(SharedCrossTableCompilation.ForPage(model,table,page).Groups.Select(group=>Validation(model,table,page,group,group.Plans.Single(p=>p.Read.Source.Source.Id==group.Key).Read.Source)));
    /// <summary>真正的目标读取仍复用各控件的原事务、权限和锁定读取工厂。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var output=new StringBuilder();foreach(var group in SharedCrossTableCompilation.ForPage(model,table,page).Groups)
        {
            var suffix=page.Source.Id+"_"+group.Key;var branches=group.Coordinator.Branches.Select(b=>"new Metarill.Core.Cascades.SharedCrossTableBranch("+Literal(b.ControlId)+",new Metarill.Core.Cascades.SharedCrossTableSlot[]{"+string.Join(",",b.Levels.Select(s=>"new("+Literal(s.IdColumn)+","+Literal(s.NameColumn)+","+Literal(s.LogicalType)+","+Literal(s.ScopeKey)+")"))+"})");
            output.AppendLine("    /// <summary>共享规则来自已证明发布元数据，不在请求中解释目录或SQL。</summary>").AppendLine($"    private static readonly Metarill.Core.Cascades.SharedCrossTableCoordinator SharedCross{suffix}=new(new Metarill.Core.Cascades.SharedCrossTableBranch[]{{{string.Join(",",branches)}}});")
                ;
            if(table.Source.IsRongyu==0){output.Append(SharedCrossTableSparseRenderer.GroupService(model,table,page,group));continue;}
            output.AppendLine("    /// <summary>隔离验证各主动路径，先清受影响的其它下级，再把完整组结果交给原DML和写后核验。</summary>")
                .AppendLine($"    private async Task NormalizeSharedCross{suffix}Async({table.Name} entity,RuntimeActor actor,ISet<string> submitted,bool creating,IReadOnlyDictionary<string,object?>? original,CancellationToken cancellationToken,bool requireTargetList=true)")
                .AppendLine("    {").AppendLine("        if(session.CurrentTransaction is null)throw new InvalidOperationException(\"共享级联必须加入当前事务。\");")
                .AppendLine($"        var result=await SharedCross{suffix}.NormalizeAsync({Dictionary(group.Columns,"entity")},original??new Dictionary<string,object?>(),submitted.ToHashSet(StringComparer.Ordinal),creating,async(control,input,token)=>")
                .AppendLine("        {").AppendLine("            switch(control)").AppendLine("            {");
            foreach(var plan in group.Plans)
            {
                var columns=plan.Levels.SelectMany(l=>new[]{l.Id,l.Name}).ToArray();output.AppendLine($"                case {Literal(plan.Read.Source.Source.Id)}:")
                    .AppendLine("                {").AppendLine($"                    var copy=new {table.Name}{{{string.Join(",",columns.Select(c=>c.Property+"=("+c.Type+")input["+Literal(c.Source.ColumnName)+"]"))}}};var local=new HashSet<string>(submitted,StringComparer.Ordinal);")
                    .AppendLine($"                    await NormalizeCross{page.Source.Id}_{plan.Read.Source.Source.Id}Async(copy,actor,local,token,requireTargetList);return {Dictionary(columns,"copy")};").AppendLine("                }");
            }
            output.AppendLine("                default:throw new InvalidOperationException(\"未知共享级联控件。\");").AppendLine("            }").AppendLine("        },cancellationToken);");
            foreach(var c in group.Columns)output.AppendLine($"        entity.{c.Property}=({c.Type})result.Values[{Literal(c.Source.ColumnName)}];");output.AppendLine("        foreach(var field in result.Fields)submitted.Add(field);").AppendLine("    }");
        }
        return output.Append(SharedCrossTableSparseRenderer.Factory(model,table,page)).ToString();
    }
    /// <summary>原表单只追加已确认的跨控件清理规则，组件、DOM、样式与对象负载保持。</summary>
    internal static string? Frontend(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        var group=SharedCrossTableCompilation.ForSource(model,table,page,source.Source.Id);if(group is null)return null;var current=group.Plans.Single(p=>p.Read.Source.Source.Id==source.Source.Id);var rules=new List<object>();
        foreach(var level in current.Levels)
        {
            var descendants=group.Plans.Where(p=>p.Read.Source.Source.Id!=source.Source.Id).SelectMany(p=>p.Levels.Select((l,i)=>(l,i)).Where(x=>x.l.Id.Source.Id==level.Id.Source.Id).SelectMany(x=>p.Levels.Skip(x.i+1))).DistinctBy(l=>l.Id.Source.Id).Select(l=>new[]{l.Id.JsonName,l.Name.JsonName}).ToArray();if(descendants.Length>0)rules.Add(new{field=level.Id.JsonName,descendants});
        }
        return "const incoming=value||{},next=Object.assign({},this.entity,incoming);for(const rule of "+JsonSerializer.Serialize(rules)+"){if(String(this.entity[rule.field]??'')===String(next[rule.field]??''))continue;for(const pair of rule.descendants){if(!Object.prototype.hasOwnProperty.call(incoming,pair[0])){next[pair[0]]='';next[pair[1]]='';}}}this.entity=next;";
    }
    /// <summary>只输出已验证属性和转义字符串，不拼接来自请求的成员名。</summary>
    private static string Dictionary(IEnumerable<ColumnModel> columns,string entity)=>"new Dictionary<string,object?>{"+string.Join(",",columns.DistinctBy(c=>c.Source.Id).Select(c=>"["+Literal(c.Source.ColumnName)+"]="+entity+"."+c.Property))+"}";
    /// <summary>所有配置文本作为C#字符串常量。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
