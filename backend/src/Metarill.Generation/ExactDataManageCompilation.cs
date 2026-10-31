using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;

namespace Metarill.Generation;

internal sealed record ExactDataManageModel(ColumnModel Column,string ManagerField,bool Derived);

/// <summary>生成期固定精确范围字段；请求只能提供筛选值，不能替换账号字段或SQL表达式。</summary>
internal static class ExactDataManageCompilation
{
    /// <summary>未配置页面字段保持原产物；配置后必须能证明发布后台、真实物理列和标量类型。</summary>
    internal static bool Declared(PageModel page)=>Text(page.Source,"exactDataManageMatchField").Length>0;
    /// <summary>物理标量沿原路径；派生字段由专用单值计划证明类型及路径，再用于关联锁定和读取。</summary>
    internal static ExactDataManageModel? Resolve(GenerationModel model,TableModel table,PageModel page)
    {
        var field=ExactDataManageValues.Field(Text(page.Source,"exactDataManageMatchField"));if(field.Length==0)return null;
        var admin=model.Catalog.YzAdminTypes.SingleOrDefault(admin=>admin.Id==page.Source.YzAdminTypeId)??throw new InvalidOperationException("精确管理页面必须声明已发布后台。");
        var column=table.Columns.SingleOrDefault(column=>ExactDataManageValues.Field(column.Source.ColumnName)==field)??throw new InvalidOperationException("精确管理记录匹配字段未声明。");
        if(column.IsJsonArray||column.Source.LogicalType.ToLowerInvariant()=="json")throw new NotSupportedException("精确管理的数组记录字段尚需固定范围证明。");
        var derived=!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column.Source);
        if(derived)
        {
            var plan=Metarill.Data.Queries.NonRedundantReads.CompileMatch(model.Catalog,table.Source.Id,column.Source.ColumnName);
            if(plan.OutputTypes[column.Source.ColumnName]!=column.Source.LogicalType)throw new InvalidOperationException("精确管理派生列与真实来源逻辑类型不一致。");
        }
        var manager=ExactDataManageValues.Field(Text(admin,"exactDataManageMatchField"));
        if(GenerationMetadataPublisher.IsSensitiveKey(column.Source.ColumnName)||manager.Length>0&&GenerationMetadataPublisher.IsSensitiveKey(manager))throw new InvalidOperationException("精确管理不能以凭据字段参与匹配。");
        return new(column,manager,derived);
    }
    /// <summary>仅新配置范围的私有方法增加操作参数，既有公开Service签名及普通产物保持。</summary>
    internal static string Parameter(PageModel page,string fallback)=>Declared(page)?", string authorizationOperation = "+Literal(fallback):"";
    /// <summary>操作表达式由生成器固定，非请求属性。</summary>
    internal static string Argument(PageModel page,string expression)=>Declared(page)?", "+expression:"";
    /// <summary>在ORM分页、统计或写前锁定之前附加AND；空集合固定不匹配，参数按名称全部绑定。</summary>
    internal static string Query(GenerationModel model,TableModel table,PageModel page,string query="query",string actor="actor",string operation="authorizationOperation",string prefix="exact",bool button=false,string? locking=null)
    {
        var definition=Resolve(model,table,page);if(definition is null)return "";
        var prefixHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(page.Source.Id+"|"+definition.Column.Source.ColumnName+"|"+prefix))).ToLowerInvariant()[..12];
        var call=definition.Derived
            ? $"Metarill.Data.Queries.ExactDataManageSql.ApplyDerived({query}, session, ExactReadPlan{page.Source.Id}, {Literal(definition.Column.Source.ColumnName)}, {Literal("mt_e_"+prefixHash)}, {prefix}Scope.ReadLogical({Literal(definition.Column.Source.LogicalType)}), {actor}.ProjectId{(locking is null?"":",locking:"+locking)})"
            : $"Metarill.Data.Queries.ExactDataManageSql.Apply({query}, session, {Literal(definition.Column.Source.ColumnName)}, {Literal("mt_e_"+prefixHash)}, {prefix}Scope.ReadLogical({Literal(definition.Column.Source.LogicalType)}))";
        return Authorization(page,definition,actor,operation,prefix,button)+$"        if ({prefix}Scope.IsRequired) {query} = {call};\n";
    }
    /// <summary>非冗余读取使用同一类型集合作为固定计划附加过滤，不能从请求覆盖该条件。</summary>
    internal static string ReadFilter(GenerationModel model,TableModel table,PageModel page)
    {
        var definition=Resolve(model,table,page);if(definition is null)return "";
        return Authorization(page,definition,"actor","authorizationOperation","exact")
            +$"        if (exactScope.IsRequired) result.Add(new({Literal(definition.Column.Source.ColumnName)}, FixedReadComparison.In, exactScope.ReadLogical({Literal(definition.Column.Source.LogicalType)})));\n";
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
