using System.Collections.ObjectModel;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record FixedPageCondition(string Definition,FixedConditionPlan Syntax,IReadOnlyDictionary<string,string> Types);

/// <summary>将原页面AND条件拆成真实身份/父范围与固定常量；请求不能提供SQL或替换变量来源。</summary>
internal static class FixedPageConditionCompilation
{
    /// <summary>先校验全部项、物理字段及逻辑类型，再建立页面模型；未知账号变量和派生表达式仍拒绝。</summary>
    internal static (IReadOnlyList<(ColumnModel Column,string Source)> Scopes,FixedPageCondition? Condition) Compile(MetadataCatalog catalog,TableMetadata table,IReadOnlyList<ColumnModel> columns,string definition)
    {
        try
        {
            var syntax=FixedConditionPlan.Compile(definition,columns.Select(column=>column.Source.ColumnName));
            var scopes=new List<(ColumnModel Column,string Source)>();var constants=new List<FixedConditionTerm>();
            foreach(var term in syntax.Terms)
            {
                var variables=term.Values.Where(value=>value.Kind==FixedConditionValueKind.Variable).ToArray();
                if(variables.Length==0){constants.Add(term);continue;}
                if(term.Comparison!=FixedConditionComparison.Equal||term.Values.Count!=1||variables.Length!=1)throw Rejected();
                var variable=variables[0].Text;
                var source=variable.Equals("current_yz_project_id",StringComparison.OrdinalIgnoreCase)?"project":variable.Equals("parent_id",StringComparison.OrdinalIgnoreCase)||variable.Equals("parentId",StringComparison.OrdinalIgnoreCase)?"parent":throw Rejected();
                var column=columns.Single(column=>column.Source.ColumnName==term.Field);
                if((column.Source.ColumnName=="yz_project_id")!=(source=="project"))throw Rejected();
                scopes.Add((column,source));
            }
            if(constants.Count==0)return(scopes.AsReadOnly(),null);
            var physical=MetadataStoragePolicy.ForTable(catalog,table);var identity=new TableModel(table,GenerationModel.Pascal(table.TableName),columns,[]);
            var fields=constants.Select(term=>term.Field).Distinct(StringComparer.Ordinal).ToArray();
            foreach(var field in fields)
            {
                var column=columns.Single(column=>column.Source.ColumnName==field);
                if(!physical.IsPhysical(column.Source)||column.IsJsonArray||column.Source.LogicalType.Equals("json",StringComparison.OrdinalIgnoreCase)||ManagerCredentialCompilation.IsCredential(identity,column))throw Rejected();
            }
            var constantDefinition=string.Join(" AND ",constants.Select(Term));
            var plan=FixedConditionPlan.Compile(constantDefinition,fields);
            var types=fields.ToDictionary(field=>field,field=>columns.Single(column=>column.Source.ColumnName==field).Source.LogicalType,StringComparer.Ordinal);
            _=FixedConditionSqlPlan.Compile(plan,types);
            return(scopes.AsReadOnly(),new(constantDefinition,plan,new ReadOnlyDictionary<string,string>(types)));
        }
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or NotSupportedException)
        {throw new InvalidOperationException("页面WhereSql尚未获得已接线的固定物理标量条件支持。",error);}
    }
    /// <summary>多选辅助名称在模型准备后才标记为数组，再次核对，不能借普通字符串初始形状接入JSON范围。</summary>
    internal static GenerationModel ValidateFinal(GenerationModel model)
    {
        foreach(var table in model.Tables)foreach(var page in table.Pages)
            if(page.Condition is{} condition&&condition.Syntax.Fields.Any(field=>table.Columns.Single(column=>column.Source.ColumnName==field).IsJsonArray))throw Rejected();
        return model;
    }
    /// <summary>按已经解析的节点规范化，字符串中的AND、引号和逗号保持原值，绝不按文本分割SQL。</summary>
    internal static string Term(FixedConditionTerm term)=>term.ToDefinition();
    /// <summary>固定条件只在类型装载时编译，普通请求绑定参数而不解释元数据。</summary>
    internal static string Declaration(PageModel page,string name)
    {
        if(page.Condition is not{} condition)return "";
        return "    /// <summary>发布时固定的页面常量条件，不能由请求替换。</summary>\n"+
            $"    private static readonly Metarill.Data.Queries.FixedConditionSqlPlan {name}=Metarill.Data.Queries.FixedConditionSqlPlan.Compile(Metarill.Core.Metadata.FixedConditionPlan.Compile({JsonSerializer.Serialize(condition.Definition)},new string[]{{{string.Join(",",condition.Syntax.Fields.Select(field=>JsonSerializer.Serialize(field)))}}}),new Dictionary<string,string>{{{string.Join(",",condition.Types.Select(pair=>"["+JsonSerializer.Serialize(pair.Key)+"]="+JsonSerializer.Serialize(pair.Value)))}}});\n";
    }
    /// <summary>单表范围和关系候选都追加同一固定AND计划。</summary>
    internal static string Apply(PageModel page,string name,string prefix)=>page.Condition is null?"":$"        query={name}.ApplyConstants(query,session,{JsonSerializer.Serialize(prefix)});\n";
    /// <summary>非冗余SQL按已编译表达式绑定相同条件；写后回执明确不传页面筛选。</summary>
    internal static string ReadArgument(PageModel page,bool named=false)=>page.Condition is null?"":", "+(named?"condition: ":"")+"PageCondition"+page.Source.Id;
    /// <summary>统一拒绝未接线定义，不回退为忽略条件的宽查询。</summary>
    private static InvalidOperationException Rejected()=>new("页面条件包含未接线字段、变量或操作。");
}
