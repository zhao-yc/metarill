using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>关系附加条件只接入已证明的物理字段、常量和原身份/父变量；表单依赖另行完整接入。</summary>
internal sealed record RelationConditionModel(string Definition, FixedConditionPlan Syntax,
    IReadOnlyDictionary<string, string> Types);

internal static class RelationConditionsCompilation
{
    /// <summary>生成前验证整个条件及字面量类型，不把未支持的变量或派生字段降级为宽查询。</summary>
    internal static (IReadOnlyList<(ColumnModel Column, string Source)> Scopes, RelationConditionModel? Conditions) Compile(
        GenerationModel model, TableModel target, string definition)
    {
        try { return CompileCore(model,target,definition); }
        catch(ArgumentException error) { throw new InvalidOperationException("关系固定条件不能按已发布物理类型安全编译。",error); }
    }

    /// <summary>完整分析后才签发关系条件，语法和类型错误统一保留生成失败合同。</summary>
    private static (IReadOnlyList<(ColumnModel Column,string Source)> Scopes,RelationConditionModel? Conditions) CompileCore(GenerationModel model,TableModel target,string definition)
    {
        var syntax=FixedConditionPlan.Compile(definition,target.Columns.Select(column=>column.Source.ColumnName));
        var scopes=new List<(ColumnModel Column,string Source)>();
        foreach(var term in syntax.Terms.Where(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
        {
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||term.Values[0].Kind!=FixedConditionValueKind.Variable)
                throw new InvalidOperationException("关系上下文只支持原身份或父键的等值绑定。");
            var variable=term.Values[0].Text;
            var source=variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"project"
                :variable.Equals("parentId",StringComparison.OrdinalIgnoreCase)||variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)?"parent"
                :throw new InvalidOperationException("关系表单或账号扩展变量尚未完成全部入口接入。");
            var column=target.Columns.Single(column=>column.Source.ColumnName==term.Field);
            if((column.Source.ColumnName=="yz_project_id")!=(source=="project"))throw new InvalidOperationException("项目范围必须绑定真实身份。");
            scopes.Add((column,source));
        }
        // 原简单范围继续走既有编译器，避免无配置项目的再生成和兼容边界发生变化。
        if(syntax.Terms.All(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
            return (GenerationModel.CompileScopes(definition,target.Columns),null);
        var physical=MetadataStoragePolicy.ForTable(model.Catalog,target.Source);
        foreach(var field in syntax.Fields)
        {
            var column=target.Columns.Single(column=>column.Source.ColumnName==field);
            if(!physical.IsPhysical(column.Source)||column.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,column))
                throw new InvalidOperationException("关系固定条件必须使用非凭据的物理标量字段。");
        }
        var types=syntax.Fields.ToDictionary(field=>field,field=>target.Columns.Single(column=>column.Source.ColumnName==field).Source.LogicalType,StringComparer.Ordinal);
        FixedConditionSqlPlan.Compile(syntax,types);
        return (scopes.AsReadOnly(),new(definition,syntax,types));
    }

    /// <summary>生成静态不可变计划，在首次装载时校验同版运行时；普通请求只绑定真实Actor和目标父。</summary>
    internal static string Declaration(RelationModel relation,string suffix)
    {
        if(relation.Conditions is not {} condition)return "";
        var fields=string.Join(", ",condition.Syntax.Fields.Select(field=>JsonSerializer.Serialize(field)));
        var types=string.Join(", ",condition.Types.Select(pair=>"["+JsonSerializer.Serialize(pair.Key)+"] = "+JsonSerializer.Serialize(pair.Value)));
        return "    /// <summary>发布时固定的关系条件；请求不能替换字段、操作符或常量。</summary>\n"
            +$"    private static readonly Metarill.Data.Queries.FixedConditionSqlPlan OptionsCondition{suffix} = Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile({JsonSerializer.Serialize(condition.Definition)}, new string[] {{ {fields} }}), new Dictionary<string, string> {{ {types} }});\n";
    }

    /// <summary>客户端formParameters完全不能覆盖当前身份；本阶段只给出已证明的原父变量。</summary>
    internal static string Apply(RelationModel relation,string suffix)
    {
        if(relation.Conditions is not {} condition)return "";
        var variables=string.Join(", ",condition.Syntax.Variables.Select(variable=>"["+JsonSerializer.Serialize(variable)+"] = "+(variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"actor.ProjectId":"targetParentId")));
        return $"        query = OptionsCondition{suffix}.Apply(query, session, new Dictionary<string, object?> {{ {variables} }}, \"options_{suffix}\");\n";
    }

    /// <summary>保存只从默认值及钩子处理后的真实实体读取父字段，不采用客户端候选请求上下文。</summary>
    internal static string WriteQuery(GenerationModel model,TableModel table,PageModel page,RelationModel relation,string number)
    {
        var context=ControlRenderer.RelationContext(page,relation,false);
        if(context.UnresolvedTarget)throw new InvalidOperationException("固定条件写入缺少已证明的目标父上下文。");
        var parent=page.Scopes.FirstOrDefault(scope=>scope.Source=="parent").Column;
        var target=context.TargetParentField is null?null:table.Columns.Single(column=>column.JsonName==context.TargetParentField);
        if(target is not null&&!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(target.Source))throw new InvalidOperationException("固定条件目标父必须来自真实物理字段。");
        return $"            var relationQuery{number} = await OptionsScope{page.Source.Id}_{relation.SourceColumn.Source.Id}Async({Value(parent)}, {Value(target)}, actor, cancellationToken, true, false);\n"
            +$"            relationQuery{number} = relationQuery{number}.Where(row => relationIds{number}.Contains(row.Id));\n";
    }
    /// <summary>真实物理父键按原字符串ID协议格式化，空值保持空值。</summary>
    private static string Value(ColumnModel? column)=>column is null?"null":column.Type=="string?"?"entity."+column.Property:"entity."+column.Property+(column.Type=="long"?".ToString(CultureInfo.InvariantCulture)":"?.ToString(CultureInfo.InvariantCulture)");
}
