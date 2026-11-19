using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record ExactDataManageModel(ColumnModel Column,string ManagerField,bool Derived,
    IReadOnlyList<NonRedundantSingleTargetScope> SingleTargets,IReadOnlyList<CrossTableWriteModel>? CrossPlans,bool Shared,bool SelfTree);

/// <summary>生成期固定精确范围字段；请求只能提供筛选值，不能替换账号字段或SQL表达式。</summary>
internal static class ExactDataManageCompilation
{
    /// <summary>未配置页面字段保持原产物；配置后必须能证明发布后台、真实物理列和标量类型。</summary>
    internal static bool Declared(PageModel page)=>Text(page.Source,"exactDataManageMatchField").Length>0;
    /// <summary>只取已声明列，供共享组自身编译时检查，避免反向调用共享组读取计划。</summary>
    internal static ColumnModel? MatchColumn(TableModel table,PageModel page)
    {
        var field=ExactDataManageValues.Field(Text(page.Source,"exactDataManageMatchField"));
        return field.Length==0?null:table.Columns.SingleOrDefault(column=>ExactDataManageValues.Field(column.Source.ColumnName)==field)
            ??throw new InvalidOperationException("精确管理记录匹配字段未声明。");
    }

    /// <summary>物理标量沿原路径；派生字段由专用单值计划证明类型及路径，再用于关联锁定和读取。</summary>
    internal static ExactDataManageModel? Resolve(GenerationModel model,TableModel table,PageModel page)
    {
        if(MatchColumn(table,page) is not{} column)return null;
        var admin=model.Catalog.YzAdminTypes.SingleOrDefault(admin=>admin.Id==page.Source.YzAdminTypeId)??throw new InvalidOperationException("精确管理页面必须声明已发布后台。");
        if(column.IsJsonArray||column.Source.LogicalType.ToLowerInvariant()=="json")throw new NotSupportedException("精确管理的数组记录字段尚需固定范围证明。");
        var field=column.Source.ColumnName;
        IReadOnlyList<CrossTableWriteModel>? paths=null;
        var shared=NonRedundantReads.SharedExactGroup(model.Catalog,table.Source,field);
        if(shared is not null)
            paths=SharedCrossTableCompilation.ForSource(model,table,page,shared.Branches[0].SourceColumnId)?.Plans
                ??throw new NotSupportedException("共享级联精确范围没有完整发布计划。");
        else if(NonRedundantReads.IndependentCrossExact(model.Catalog,table.Source,field) is{} independent)
        {
            var source=table.Columns.Single(item=>item.Source.Id==independent.SourceColumnId);
            paths=[CrossTableWriteCompilation.Resolve(model,table,page,source).Definition
                ??throw new NotSupportedException("独立级联精确范围没有完整发布计划。")];
        }
        var derived=paths is not null||!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column.Source);
        var selfTree=derived&&paths is null&&CascadeTreeStorageColumns.IsLogicalNameField(table.Source,table.Columns.Select(item=>item.Source),field);
        // 自树从实际页面证明祖先链；普通单值关系沿用本页发布目标和物理父键。
        var singleTargets=derived&&paths is null&&!selfTree?NonRedundantReadCompilation.ProjectSingleTargets(model,table,page,[field]):[];
        if(derived)
        {
            var plan=paths is not null
                ?NonRedundantReads.CompileCrossExactMatch(model.Catalog,table.Source.Id,field,NonRedundantCrossTableCompilation.Bindings(paths))
                :selfTree
                    ?NonRedundantReads.CompileSelfTreeExactMatch(model.Catalog,table.Source.Id,page.Source.Id)
                    :singleTargets.Count>0
                        ?NonRedundantReads.CompileProjectPage(model.Catalog,table.Source.Id,[field],[],[],singleTargets)
                        :NonRedundantReads.CompileMatch(model.Catalog,table.Source.Id,field);
            if(plan.OutputTypes[field]!=column.Source.LogicalType)throw new InvalidOperationException("精确管理派生列与真实来源逻辑类型不一致。");
        }
        var manager=ExactDataManageValues.Field(Text(admin,"exactDataManageMatchField"));
        if(GenerationMetadataPublisher.IsSensitiveKey(column.Source.ColumnName)||manager.Length>0&&GenerationMetadataPublisher.IsSensitiveKey(manager))throw new InvalidOperationException("精确管理不能以凭据字段参与匹配。");
        _=ExactDataManageValues.Unrestricted.ReadLogical(column.Source.LogicalType);
        return new(column,manager,derived,singleTargets,paths,shared is not null,selfTree);
    }
    /// <summary>当前锁定只包含匹配字段的联接，保留共享、独立级联和原表级匹配路径。</summary>
    internal static string MatchPlanCall(TableModel table,PageModel page,ExactDataManageModel exact)
    {
        var tableId=Literal(table.Source.Id);
        var field=Literal(exact.Column.Source.ColumnName);
        var outputs="new string[]{"+field+"}";
        if(exact.CrossPlans is not null)
            return $"CompileCross({tableId},{outputs},Array.Empty<string>(){NonRedundantCrossTableCompilation.BindingArgument(exact.CrossPlans)})";
        if(exact.SelfTree)
            return $"CompileSelfTreeExactMatch({tableId},{Literal(page.Source.Id)})";
        if(exact.SingleTargets.Count>0)
            return $"CompileProjectPage({tableId},{outputs},Array.Empty<string>(),Array.Empty<NonRedundantCrossTableScope>(){NonRedundantReadCompilation.SingleTargetArgument(exact.SingleTargets)})";
        return $"CompileMatch({tableId},{field})";
    }
    /// <summary>仅新配置范围的私有方法增加操作参数，既有公开Service签名及普通产物保持。</summary>
    internal static string Parameter(PageModel page,string fallback)=>Declared(page)?", string authorizationOperation = "+Literal(fallback):"";
    /// <summary>操作表达式由生成器固定，非请求属性。</summary>
    internal static string Argument(PageModel page,string expression)=>Declared(page)?", "+expression:"";
    /// <summary>在ORM分页、统计或写前锁定之前附加AND；空集合固定不匹配，参数按名称全部绑定。</summary>
    internal static string Query(GenerationModel model,TableModel table,PageModel page,string query="query",string actor="actor",string operation="authorizationOperation",string prefix="exact",bool button=false,string? locking=null,string? planReference=null)
    {
        var definition=Resolve(model,table,page);if(definition is null)return "";
        var prefixHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(page.Source.Id+"|"+definition.Column.Source.ColumnName+"|"+prefix))).ToLowerInvariant()[..12];
        var plan=planReference??"ExactReadPlan"+page.Source.Id;
        var boundPlan=definition.SelfTree||definition.SingleTargets.Count>0?plan+".BindAccount("+actor+")":
            NonRedundantCrossTableCompilation.BoundPlan(definition.CrossPlans,plan,actor);
        var call=definition.Derived
            ? $"Metarill.Data.Queries.ExactDataManageSql.ApplyDerived({query}, session, {boundPlan}, {Literal(definition.Column.Source.ColumnName)}, {Literal("mt_e_"+prefixHash)}, {prefix}Scope.ReadLogical({Literal(definition.Column.Source.LogicalType)}), {actor}.ProjectId{(locking is null?"":",locking:"+locking)})"
            : $"Metarill.Data.Queries.ExactDataManageSql.Apply({query}, session, {Literal(definition.Column.Source.ColumnName)}, {Literal("mt_e_"+prefixHash)}, {prefix}Scope.ReadLogical({Literal(definition.Column.Source.LogicalType)}))";
        return Authorization(page,definition,actor,operation,prefix,button)+$"        if ({prefix}Scope.IsRequired) {query} = {call};\n";
    }
    /// <summary>非冗余读取使用同一类型集合作为固定计划附加过滤，不能从请求覆盖该条件。</summary>
    internal static string ReadFilter(GenerationModel model,TableModel table,PageModel page)
    {
        var definition=Resolve(model,table,page);if(definition is null)return "";
        return ReadFilter(page, definition, "authorizationOperation", "exact", "result");
    }
    /// <summary>实际查询统一使用目标页授权及固定类型，将筛选绑定到包含该字段的读取计划。</summary>
    internal static string ReadFilter(PageModel page, ExactDataManageModel definition, string operation, string prefix, string filters) =>
        Authorization(page, definition, "actor", operation, prefix)
        + $"        if ({prefix}Scope.IsRequired) {filters}.Add({Filter(definition, prefix + "Scope")});\n";
    /// <summary>保持原标量集合筛选；共享权限额外校验两个分支的当前有效路径。</summary>
    internal static string Filter(ExactDataManageModel definition,string scope,bool spaces=true)
    {
        var field=Literal(definition.Column.Source.ColumnName);
        var value=$"{scope}.ReadLogical({Literal(definition.Column.Source.LogicalType)})";
        var comma=spaces?", ":",";
        return definition.CrossPlans is null?$"new({field}{comma}FixedReadComparison.In{comma}{value})":
            $"FixedReadFilter.{(definition.Shared?"ForSharedExact":"ForCrossExact")}({field}, {value})";
    }
    /// <summary>新增只校验原新增/导入动作，不额外索要查询权；已授权写后回显另走固定记录私有入口。</summary>
    internal static string MutationAuthorization(PageModel page,string operation)
    {
        if(!Declared(page))return "";
        return "        var exactWriteAuthorization = relationAuthorization ?? throw new UnauthorizedAccessException(\"精确管理需要宿主真实授权。\");\n"
            +"        if (exactWriteAuthorization.RequireActor() != actor) throw new UnauthorizedAccessException(\"精确管理身份与当前真实身份不一致。\");\n"
            +$"        exactWriteAuthorization.RequirePagePermission({Literal(page.Source.Id)}, {operation});\n";
    }
    /// <summary>范围仅接受当前真实身份，旧授权器或漏注入宿主都明确拒绝。</summary>
    private static string Authorization(PageModel page,ExactDataManageModel definition,string actor,string operation,string prefix,bool button=false)=>
        $"        var {prefix}Authorization = relationAuthorization ?? throw new UnauthorizedAccessException(\"精确管理需要宿主真实授权。\");\n"
        +$"        if ({prefix}Authorization.RequireActor() != {actor}) throw new UnauthorizedAccessException(\"精确管理身份与当前真实身份不一致。\");\n"
        +$"        var {prefix}Scope = {prefix}Authorization.{(button?"ResolveButtonExactDataManage":"ResolveExactDataManage")}({Literal(page.Source.Id)}, {operation}, {Literal(definition.ManagerField)});\n";
    /// <summary>元数据扩展必须是字符串；损坏声明不能当成未配置。</summary>
    private static string Text(MetadataRecord record,string key)
    {
        if(!record.Extra.TryGetValue(key,out var value)||value.ValueKind==JsonValueKind.Null)return "";
        return value.ValueKind==JsonValueKind.String?value.GetString()?.Trim()??"":throw new InvalidOperationException("精确管理字段声明必须为字符串。");
    }
    /// <summary>所有名称成为固定C#字面量，不能拼入SQL语法。</summary>
    private static string Literal(string value)=>JsonSerializer.Serialize(value);
}
