using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

/// <summary>关系附加条件只接入已证明的物理字段、常量和原身份/父变量；表单依赖从同一发布计划接通。</summary>
internal sealed record RelationConditionModel(string Definition, FixedConditionPlan Syntax,
    IReadOnlyDictionary<string, string> Types, IReadOnlyList<RelationFormBinding> Forms);

/// <summary>表单变量固定绑定本对象已声明的物理标量，嵌套槽沿用原表名驼峰结构。</summary>
internal sealed record RelationFormBinding(string Variable,ColumnModel Column,IReadOnlyList<string> Path,IReadOnlyList<string> FallbackPath);

internal static class RelationConditionsCompilation
{
    /// <summary>生成前验证整个条件及字面量类型，不把未支持的变量或派生字段降级为宽查询。</summary>
    internal static (IReadOnlyList<(ColumnModel Column, string Source)> Scopes, RelationConditionModel? Conditions) Compile(
        GenerationModel model, TableModel owner, PageModel page, TableModel target, string definition)
    {
        try { return CompileCore(model,owner,page,target,definition); }
        catch(ArgumentException error) { throw new InvalidOperationException("关系固定条件不能按已发布物理类型安全编译。",error); }
    }

    /// <summary>完整分析后才签发关系条件，语法和类型错误统一保留生成失败合同。</summary>
    private static (IReadOnlyList<(ColumnModel Column,string Source)> Scopes,RelationConditionModel? Conditions) CompileCore(GenerationModel model,TableModel owner,PageModel page,TableModel target,string definition)
    {
        var syntax=FixedConditionPlan.Compile(definition,target.Columns.Select(column=>column.Source.ColumnName));
        var scopes=new List<(ColumnModel Column,string Source)>();
        var forms=new Dictionary<string,RelationFormBinding>(StringComparer.Ordinal);
        var storage=MetadataStoragePolicy.ForTable(model.Catalog,owner.Source);
        foreach(var term in syntax.Terms)
        foreach(var value in term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable))
        {
            var variable=value.Text;
            if(variable.StartsWith("formParameters.",StringComparison.Ordinal))
            {
                var path=variable["formParameters.".Length..].Split('.');
                if(path.Length!=1&&(path.Length!=2||path[0]!=GenerationModel.Camel(owner.Source.TableName)))throw new InvalidOperationException("表单变量必须绑定本对象的明确物理属性或原表名槽。");
                var column=owner.Columns.SingleOrDefault(column=>column.JsonName==path[^1]);
                if(column is null||!page.Readable.Concat(page.Writable).Any(field=>field.Source.Id==column.Source.Id)||!storage.IsPhysical(column.Source)||column.IsJsonArray||(column.IsSystem&&column.Source.ColumnName!="yz_project_id")||ManagerCredentialCompilation.IsCredential(owner,column))throw new InvalidOperationException("表单依赖不是当前页开放的非凭据物理标量。");
                var targetColumn=target.Columns.Single(column=>column.Source.ColumnName==term.Field);
                if(Kind(column.Source.LogicalType)!=Kind(targetColumn.Source.LogicalType))throw new InvalidOperationException("表单依赖与候选字段逻辑类型不一致。");
                forms.TryAdd(variable,new(variable,column,Array.AsReadOnly(path),Array.AsReadOnly(path.Length==1?new[]{GenerationModel.Camel(owner.Source.TableName),path[0]}:new[]{path[^1]})));continue;
            }
            if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1)
                throw new InvalidOperationException("身份或父键变量只支持原等值绑定。");
            var source=variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"project"
                :variable.Equals("parentId",StringComparison.OrdinalIgnoreCase)||variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)?"parent"
                :throw new InvalidOperationException("账号扩展变量尚未完成全部入口接入。");
            var scoped=target.Columns.Single(column=>column.Source.ColumnName==term.Field);
            if((scoped.Source.ColumnName=="yz_project_id")!=(source=="project"))throw new InvalidOperationException("项目范围必须绑定真实身份。");
            scopes.Add((scoped,source));
        }
        // 原简单范围继续走既有编译器，避免无配置项目的再生成和兼容边界发生变化。
        if(forms.Count==0&&syntax.Terms.All(term=>term.Values.Any(value=>value.Kind==FixedConditionValueKind.Variable)))
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
        return (scopes.AsReadOnly(),new(definition,syntax,types,Array.AsReadOnly(forms.Values.ToArray())));
    }

    /// <summary>生成静态不可变计划，在首次装载时校验同版运行时；普通请求只绑定真实Actor、目标父及已声明的表单叶值。</summary>
    internal static string Declaration(RelationModel relation,string suffix)
    {
        if(relation.Conditions is not {} condition)return "";
        var fields=string.Join(", ",condition.Syntax.Fields.Select(field=>JsonSerializer.Serialize(field)));
        var types=string.Join(", ",condition.Types.Select(pair=>"["+JsonSerializer.Serialize(pair.Key)+"] = "+JsonSerializer.Serialize(pair.Value)));
        return "    /// <summary>发布时固定的关系条件；请求不能替换字段、操作符或常量。</summary>\n"
            +$"    private static readonly Metarill.Data.Queries.FixedConditionSqlPlan OptionsCondition{suffix} = Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile({JsonSerializer.Serialize(condition.Definition)}, new string[] {{ {fields} }}), new Dictionary<string, string> {{ {types} }});\n";
    }

    /// <summary>表单只按声明叶值绑定，客户端不能覆盖当前身份或真实父。</summary>
    internal static string Apply(RelationModel relation,string suffix)
    {
        if(relation.Conditions is not {} condition)return "";
        var variables=string.Join(", ",condition.Syntax.Variables.Select(variable=>"["+JsonSerializer.Serialize(variable)+"] = "+(variable.StartsWith("formParameters.",StringComparison.Ordinal)?"Metarill.Core.Contracts.FixedConditionFormValue.Require(formValues, "+JsonSerializer.Serialize(variable)+")":variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"actor.ProjectId":"targetParentId")));
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
        return $"            var relationQuery{number} = await OptionsScope{page.Source.Id}_{relation.SourceColumn.Source.Id}Async({Value(parent)}, {Value(target)}, actor, cancellationToken, true, false{WriteArgument(relation)});\n"
            +$"            relationQuery{number} = relationQuery{number}.Where(row => relationIds{number}.Contains(row.Id));\n";
    }
    /// <summary>只有声明表单依赖的候选签名增加私有窄值槽，其他生成产物逐字保持。</summary>
    internal static string Signature(RelationModel relation)=>HasForms(relation)?", IReadOnlyDictionary<string, object?>? formValues = null":"";
    /// <summary>仅声明了物理表单变量时扩展候选内部合同。</summary>
    internal static bool HasForms(RelationModel relation)=>relation.Conditions?.Forms.Count>0;
    /// <summary>三个候选入口都按已编译路径读取同一原表单槽。</summary>
    internal static string ReadArgument(RelationModel relation,string suffix)=>HasForms(relation)?", formValues: ReadOptionsForm"+suffix+"(request.FormParameters)":"";
    /// <summary>生成固定路径提取器，不复制整个请求或让客户端选择属性路径。</summary>
    internal static string Reader(RelationModel relation,string suffix)=>!HasForms(relation)?"":
        "    /// <summary>只提取已发布联动依赖的标量，不信任客户端身份键。</summary>\n"
        +$"    private static IReadOnlyDictionary<string, object?> ReadOptionsForm{suffix}(JsonElement? formParameters) => new Dictionary<string, object?> {{ "+string.Join(", ",relation.Conditions!.Forms.Select(binding=>"["+JsonSerializer.Serialize(binding.Variable)+"] = Metarill.Data.Queries.FixedConditionSqlPlan.BindScalar(Metarill.Core.Contracts.FixedConditionFormValue.Read(formParameters, new string[] { "+string.Join(", ",binding.Path.Select(part=>JsonSerializer.Serialize(part)))+" }, new string[] { "+string.Join(", ",binding.FallbackPath.Select(part=>JsonSerializer.Serialize(part)))+" }), "+JsonSerializer.Serialize(binding.Column.Source.LogicalType)+")"))+" };\n";
    /// <summary>最终写入从真实实体取值，不能用候选请求缓存伪造依赖。</summary>
    private static string WriteArgument(RelationModel relation)=>!HasForms(relation)?"":", "+EntityValues(relation,"entity");
    /// <summary>写入和导出都从窄投影实际实体取值，生成表达式不接受客户端替换。</summary>
    internal static string EntityValues(RelationModel relation,string entity)=>"new Dictionary<string, object?> { "+string.Join(", ",relation.Conditions!.Forms.Select(binding=>"["+JsonSerializer.Serialize(binding.Variable)+"] = "+entity+"."+binding.Column.Property))+" }";
    /// <summary>导入使用解析拓扑中的默认/原记录/单元格值，缺失时整体拒绝。</summary>
    internal static string ImportArgument(RelationModel relation)=>!HasForms(relation)?"":", new Dictionary<string, object?> { "+string.Join(", ",relation.Conditions!.Forms.Select(binding=>"["+JsonSerializer.Serialize(binding.Variable)+"] = Metarill.Data.Queries.FixedConditionSqlPlan.BindScalar(relationContext["+JsonSerializer.Serialize(binding.Column.JsonName)+"], "+JsonSerializer.Serialize(binding.Column.Source.LogicalType)+")"))+" }";
    /// <summary>逻辑类型兼容只在生成时判定，实际数值范围仍由参数计划严格核验。</summary>
    private static string Kind(string type)=>type.ToLowerInvariant() switch
    {"id" or "long" or "int64" or "bigint" or "integer" or "int" or "int32"=>"integer","string" or "text" or "varchar"=>"string","decimal" or "money" or "number"=>"decimal","double"=>"double","date" or "datetime" or "timestamp"=>"date","bool" or "boolean"=>"bool",_=>throw new InvalidOperationException("表单条件依赖类型尚未证明。")};
    /// <summary>仅保留候选外键、依赖及目标父的窄原值，不缓存整个实体或账号。</summary>
    internal static string CaptureMutation(GenerationModel model,TableModel table,PageModel page)
    {
        var fields=MutationFields(model,table,page);return fields.Count==0?"":
            "        // 未登记字段不会持久化，校验不能使用与最终数据库记录不同的手写值。\n"
            +"        var originalCandidateValues = new { "+string.Join(", ",fields.Select(column=>"entity."+column.Property))+" };\n";
    }
    /// <summary>未登记的钩子变化必须恢复或明确提交；创建/复制全部写入实体，不需要这一更新保护。</summary>
    internal static string ValidateMutation(GenerationModel model,TableModel table,PageModel page)=>string.Concat(MutationFields(model,table,page).Select(column=>
        $"        if (!submitted.Contains({JsonSerializer.Serialize(column.Source.ColumnName)}) && !EqualityComparer<{column.Type}>.Default.Equals(entity.{column.Property}, originalCandidateValues.{column.Property})) throw new InvalidOperationException(\"手写规则修改候选关联或依赖时必须登记提交字段。\");\n"));
    /// <summary>完整关系计划决定窄观察字段；没有附加条件的既有生成代码保持逐字不变。</summary>
    private static IReadOnlyList<ColumnModel> MutationFields(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new List<ColumnModel>();
        foreach(var relation in RelationCompilation.ForPage(model,page).Where(relation=>relation.Conditions is not null))
        {
            result.Add(relation.SourceColumn);result.AddRange(relation.Conditions!.Forms.Select(binding=>binding.Column));
            var targetParent=ControlRenderer.RelationContext(page,relation,false).TargetParentField;
            if(targetParent is not null)result.Add(table.Columns.Single(column=>column.JsonName==targetParent));
        }
        foreach(var multi in MultiSelectCompilation.ForService(model,table,page).Where(def=>MultiNameWriteCompilation.ForSource(model,table,def).Count>0))
        {result.Add(multi.Source);result.AddRange(MultiNameWriteCompilation.ForSource(model,table,multi).Select(binding=>binding.Cache));result.AddRange(page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>scope.Column));if(multi.TargetParentField is{} field)result.Add(table.Columns.Single(column=>column.JsonName==field));}
        foreach(var checkbox in DatabaseCheckboxCompilation.ForService(model,table,page)){result.Add(checkbox.Source);result.AddRange(checkbox.Dependencies);if(table.Source.IsRongyu==1)result.Add(checkbox.Names);}
        return result.DistinctBy(column=>column.Source.Id).ToArray();
    }
    /// <summary>真实物理父键按原字符串ID协议格式化，空值保持空值。</summary>
    private static string Value(ColumnModel? column)=>column is null?"null":column.Type=="string?"?"entity."+column.Property:"entity."+column.Property+(column.Type=="long"?".ToString(CultureInfo.InvariantCulture)":"?.ToString(CultureInfo.InvariantCulture)");
}
