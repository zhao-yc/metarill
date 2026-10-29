using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
namespace Metarill.Generation;

internal static partial class BackendRenderer
{
    /// <summary>只对实际记录规则引用的非物理字段接线；不为全部非冗余页扩大读取投影。</summary>
    private static IReadOnlyList<ColumnModel> RuleRecordFields(GenerationModel model,TableModel table,PageModel page)
    {
        if(!NonRedundantReadCompilation.Requires(model,table))return [];
        var roots=new HashSet<string>(StringComparer.Ordinal);
        void Add(IEnumerable<string> fields){foreach(var field in fields)roots.Add(field.Split('.')[0]);}
        var needsReadonly=AgentRecordOperations(page).Count>0;
        foreach(var button in model.Catalog.ConfigButtons.Where(button=>button.ValueKind==JsonValueKind.Object&&button.TryGetProperty("configPageId",out var owner)&&owner.ToString()==page.Source.Id&&button.TryGetProperty("actionType",out var action)&&action.ToString()=="1"))
        {
            if(button.TryGetProperty("visibleScheme",out var scheme)&&scheme.ToString()=="VisibleWhenWritable"&&table.Source.IsFunctionTable!=1)needsReadonly=true;
            if(button.TryGetProperty("visibleRuleJson",out var rule))
            {try{Add(ButtonVisibilityRule.Compile(rule.ValueKind==JsonValueKind.Null?null:rule.ToString()).Fields);}catch(ArgumentException){/* 原发布目录会拒绝坏规则，不能因此放开记录写入。 */}}
        }
        if(needsReadonly&&page.Source.Extra.TryGetValue("readOnlyCondition",out var condition))Add(ReadOnlyConditionRule.Compile(condition.ValueKind==JsonValueKind.Null?null:condition.ToString()).Fields);
        return table.Columns.Where(column=>roots.Contains(column.JsonName)||roots.Contains(column.Source.ColumnName)).Where(column=>!NonRedundantReadCompilation.Physical(model,table,column)||CrossTableWriteCompilation.ForService(model,table,page).Any(plan=>plan.Levels.Any(level=>level.Id.Source.Id==column.Source.Id))).ToArray();
    }
    /// <summary>实体只在需要时增加内部装载状态；显式接口没有可反序列化属性或HTTP字段。</summary>
    private static bool RuleRecordEntity(GenerationModel model,TableModel table)=>table.Pages.Any(page=>RuleRecordFields(model,table,page).Count>0);
    /// <summary>装载快照只初始化一次并复制字典/JSON值；未装载实例继续明确拒绝派生判定。</summary>
    private static string RuleRecordEntityState(GenerationModel model,TableModel table)=>"    /// <summary>逻辑规则字段缺装载时必须拒绝，物理存储空位不能冒充逻辑空值。</summary>\n    bool global::Metarill.Core.Contracts.IGeneratedRuleRecord.RequiresLoadedRuleValue(string physicalColumn)=>physicalColumn is "+string.Join(" or ",table.Pages.SelectMany(page=>RuleRecordFields(model,table,page)).Select(column=>column.Source.ColumnName).Distinct(StringComparer.Ordinal).Select(Literal))+";\n"+"""
    private IReadOnlyDictionary<string,object?>? generatedRuleValues;
    /// <summary>由同程序集固定Service从实际SQL结果捕获，客户端实体不能初始化。</summary>
    internal void SetGeneratedRuleValues(IReadOnlyDictionary<string,object?> values)
    {
        if(generatedRuleValues is not null)throw new InvalidOperationException("派生规则值已初始化。");
        generatedRuleValues=new System.Collections.ObjectModel.ReadOnlyDictionary<string,object?>(values.ToDictionary(pair=>pair.Key,pair=>pair.Value is System.Text.Json.JsonElement json?(object)json.Clone():pair.Value,StringComparer.Ordinal));
    }
    /// <summary>已装载null和未装载分开，规则不能借默认空值变成可写。</summary>
    bool global::Metarill.Core.Contracts.IGeneratedRuleRecord.TryGetLoadedRuleValue(string physicalColumn,out object? value)
    {value=null;return generatedRuleValues is not null&&generatedRuleValues.TryGetValue(physicalColumn,out value);}
"""+Environment.NewLine;
    /// <summary>物理实体值和规则派生值来自同一固定读取快照；条件、父范围和精确管理都在查询前绑定。</summary>
    private static string RuleRecordAccess(GenerationModel model,TableModel table,PageModel page)
    {
        var derived=RuleRecordFields(model,table,page);if(derived.Count==0)return "";
        var physical=table.Columns.Where(column=>NonRedundantReadCompilation.Physical(model,table,column)).ToArray();var columns=physical.Concat(derived).DistinctBy(column=>column.Source.Id).ToArray();
        var fixedQuery=FixedPageQueryCompilation.For(table,page);var exact=ExactDataManageCompilation.Resolve(model,table,page);
        var dependencies=fixedQuery.IdentityScopes.Select(scope=>scope.Column.Source.ColumnName).Concat(page.Condition?.Syntax.Fields??[]).Concat(exact is null?[]:new[]{exact.Column.Source.ColumnName}).Distinct(StringComparer.Ordinal).ToArray();
        // 真实适配器提前证明全部输出及隐藏范围；不能到运行时才静默回退成宽查询。
        _=NonRedundantReads.CompileRuleRecord(model.Catalog,table.Source.Id,columns.Select(column=>column.Source.ColumnName).ToArray(),physical.Select(column=>column.Source.ColumnName).ToArray(),NonRedundantCrossTableCompilation.Bindings(model,table,page),dependencies,logicalOutputs:derived.Select(column=>column.Source.ColumnName).ToArray());
        var cross=CrossTableWriteCompilation.ForService(model,table,page).Count>0;
        var result=new StringBuilder().AppendLine("    /// <summary>本页规则记录的固定完整物理实体和必要派生投影，不参与HTTP输出。</summary>")
            .AppendLine($"    private static readonly NonRedundantReadPlan RuleRecordPlan{page.Source.Id}=global::{model.Namespace}.Generated.GeneratedNonRedundantReadCatalog.CompileRuleRecord({Literal(table.Source.Id)},new string[]{{{string.Join(",",columns.Select(column=>Literal(column.Source.ColumnName)))}}},new string[]{{{string.Join(",",physical.Select(column=>Literal(column.Source.ColumnName)))}}},new string[]{{{string.Join(",",dependencies.Select(Literal))}}},new string[]{{{string.Join(",",derived.Select(column=>Literal(column.Source.ColumnName)))}}}{(cross?NonRedundantCrossTableCompilation.BindingArgument(model,table,page):",Array.Empty<NonRedundantCrossTableScope>()")});")
            .AppendLine("    /// <summary>原权限由公开固定入口建立；这里只消费固定动作、范围和值，不接受客户端字段投影。</summary>")
            .AppendLine($"    private Task<{table.Name}?> ReadRuleRecord{page.Source.Id}Async(long id,string? parentId,string operation,RuntimeActor actor,bool button,bool locking,CancellationToken cancellationToken)=>locking?ReadRuleRecord{page.Source.Id}CoreAsync(id,parentId,operation,actor,button,true,cancellationToken):session.InReadSnapshotAsync(()=>ReadRuleRecord{page.Source.Id}CoreAsync(id,parentId,operation,actor,button,false,cancellationToken),cancellationToken);")
            .AppendLine("    /// <summary>分批多值名称加入同一RR读取快照，当前锁读继续加入外层真实业务事务。</summary>")
            .AppendLine($"    private async Task<{table.Name}?> ReadRuleRecord{page.Source.Id}CoreAsync(long id,string? parentId,string operation,RuntimeActor actor,bool button,bool locking,CancellationToken cancellationToken)")
            .AppendLine("    {").AppendLine("        ValidateActor(actor);if(id<=0)throw new ArgumentException(\"记录标识无效。\");")
            .AppendLine("        if(locking&&session.CurrentTransaction is null)throw new InvalidOperationException(\"规则记录当前读必须加入真实写事务。\");");
        if(ManagerCredentialCompilation.Unsupported(table,page))result.AppendLine("        ManagerCredentialValue.RequirePublishedPage(false);");
        if(NonRedundantCrossParentCompilation.Enabled(model,table,page))result.AppendLine($"        _=LogicalParentId{page.Source.Id}(parentId);");
        result.AppendLine("        var filters=new List<FixedReadFilter>{new(\"id\",FixedReadComparison.Equal,id)};");
        foreach(var(column,source)in fixedQuery.IdentityScopes)
        {
            if(source=="parent")result.AppendLine("        if(string.IsNullOrWhiteSpace(parentId))throw new ArgumentException(\"当前页面要求parentId。\");");
            result.AppendLine($"        filters.Add(new({Literal(column.Source.ColumnName)},FixedReadComparison.Equal,{ScopeValue(column,source=="project"?"actor.ProjectId":"parentId!")}));");
        }
        if(exact is not null)result.AppendLine("        var ruleAuthorization=relationAuthorization??throw new UnauthorizedAccessException(\"精确管理需要宿主真实授权。\");")
            .AppendLine("        if(ruleAuthorization.RequireActor()!=actor)throw new UnauthorizedAccessException(\"规则记录身份与当前真实身份不一致。\");")
            .AppendLine($"        var ruleScope=button?ruleAuthorization.ResolveButtonExactDataManage({Literal(page.Source.Id)},operation,{Literal(exact.ManagerField)}):ruleAuthorization.ResolveExactDataManage({Literal(page.Source.Id)},operation,{Literal(exact.ManagerField)});")
            .AppendLine($"        if(ruleScope.IsRequired)filters.Add(new({Literal(exact.Column.Source.ColumnName)},FixedReadComparison.In,ruleScope.ReadLogical({Literal(exact.Column.Source.LogicalType)})));");
        result.AppendLine("        var reader=new NonRedundantReader(session);")
            .AppendLine($"        var rows=locking?await reader.CurrentRowsAsync(RuleRecordPlan{page.Source.Id},filters,Array.Empty<FixedReadOrder>(),1,cancellationToken,projectId:actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page,true)}):await reader.RowsAsync(RuleRecordPlan{page.Source.Id},filters,Array.Empty<FixedReadOrder>(),1,cancellationToken,projectId:actor.ProjectId{FixedPageConditionCompilation.ReadArgument(page,true)});")
            .AppendLine("        if(rows.Count==0)return null;var row=rows.Single();")
            .AppendLine($"        var entity=new {table.Name}").AppendLine("        {");
        foreach(var column in columns)result.AppendLine($"            {column.Property}=NonRedundantReads.Value<{column.Type}>(row,{Literal(column.Source.ColumnName)}),");
        result.AppendLine("        };");
        for(var index=0;index<derived.Count;index++)result.AppendLine($"        var ruleValue{index}=NonRedundantReads.RuleValue<{derived[index].Type}>(RuleRecordPlan{page.Source.Id},row,{Literal(derived[index].Source.ColumnName)});");
        result.AppendLine("        entity.SetGeneratedRuleValues(new Dictionary<string,object?>")
            .AppendLine("        {");
        for(var index=0;index<derived.Count;index++)
        {
            var column=derived[index];var value="ruleValue"+index;
            if(column.IsJsonArray)value=$"({value} is null?null:(object)System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>({value}))";
            result.AppendLine($"            [{Literal(column.Source.ColumnName)}]={value},");
        }
        return result.AppendLine("        });").AppendLine("        return entity;").AppendLine("    }").ToString();
    }
}
