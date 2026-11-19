using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal static partial class NonRedundantReadCompilation
{
    /// <summary>从实际投影或查询依赖收集项目内名称路径。没有固定条件的中间段只传递下一页的绑定。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> ProjectPathTargets(GenerationModel model,TableModel table,
        PageModel page,IEnumerable<string> fields)
    {
        if(table.IsGlobal||table.Source.IsRongyu!=0)return [];
        var active=new HashSet<(string Table,string Field)>();
        return MergeProjectTargets(fields.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(field=>ProjectPathTarget(model,table,page,[field],active)).OfType<NonRedundantSingleTargetScope>());
    }

    /// <summary>显式联接和原自动名称使用与 SQL 相同的物理关系字段及表级显示名。</summary>
    private static NonRedundantSingleTargetScope? ProjectPathTarget(GenerationModel model,TableModel table,PageModel page,
        string[] path,HashSet<(string Table,string Field)> active)
    {
        if(path[0].Length==0)return null;
        var field=path[0];
        ColumnModel? Find(string name)=>table.Columns.FirstOrDefault(column=>column.Source.ColumnName.Equals(name,StringComparison.OrdinalIgnoreCase));
        if(path.Length>1)
            return Step(Find(field+"_id")??Find(field),path[1..]);

        var column=Find(field);
        if(column is not null)
        {
            if(!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))return null;
            if(!active.Add((table.Source.Id,column.Source.Id)))throw new InvalidOperationException("非冗余字段存在循环依赖。");
            try
            {
                var declared=column.Source.Extra.GetValueOrDefault("columnFullName").ToString().Split('.');
                return declared.Length<2?null:ProjectPathTarget(model,table,page,declared,active);
            }
            finally{active.Remove((table.Source.Id,column.Source.Id));}
        }

        foreach(var foreign in table.Columns.Where(column=>column.Source.ControlKind is "List" or "SelectPage"&&
            column.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)&&
            field.StartsWith(column.Source.ColumnName[..^3]+"_",StringComparison.OrdinalIgnoreCase)))
        {
            var name="name";
            if(!string.IsNullOrWhiteSpace(foreign.Source.ControlDetail))
            {
                using var json=JsonDocument.Parse(foreign.Source.ControlDetail);
                if(json.RootElement.ValueKind==JsonValueKind.Object&&json.RootElement.TryGetProperty("nameField",out var label)&&
                    label.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(label.GetString()))
                    name=label.GetString()!;
            }
            if(name!="id"&&field.Equals(foreign.Source.ColumnName[..^3]+"_"+name,StringComparison.OrdinalIgnoreCase))
                return Step(foreign,[name]);
        }
        return null;

        NonRedundantSingleTargetScope? Step(ColumnModel? foreign,string[] tail)
        {
            if(foreign is null||foreign.Source.ControlKind is not("List" or "SelectPage")||!Physical(model,table,foreign))
                return null;
            var relation=RelationCompilation.ForPage(model,page,scopeSourceId:foreign.Source.Id).SingleOrDefault();
            if(relation is null||relation.Target.IsGlobal||relation.Target.Source.IsRongyu!=0||relation.ParentBinding?.Declared==true)
                return null;
            var downstream=ProjectPathTarget(model,relation.Target,relation.TargetPage,tail,active);
            IReadOnlyList<NonRedundantSingleTargetScope> nested=downstream is null?[]:[downstream];
            if(UsesProjectSingleRead(relation))
                return ProjectTarget(model,table,page,relation) with{NestedTargets=nested};
            return downstream is null?null:new(foreign.Source.Id,relation.TargetPage.Source.Id)
                {ApplyPageCondition=false,NestedTargets=nested};
        }
    }

    /// <summary>同一路径的不同列复用页绑定，同表不同关系角色依然各自编译。</summary>
    private static IReadOnlyList<NonRedundantSingleTargetScope> MergeProjectTargets(IEnumerable<NonRedundantSingleTargetScope> scopes)
    {
        var result=new List<NonRedundantSingleTargetScope>();
        foreach(var group in scopes.GroupBy(scope=>scope.ColumnId,StringComparer.Ordinal))
        {
            var first=group.First();
            if(group.Any(scope=>scope.PageId!=first.PageId||scope.SourceParentId!=first.SourceParentId||
                scope.SourcePageId!=first.SourcePageId||scope.ApplyPageCondition!=first.ApplyPageCondition))
                throw new InvalidOperationException("同一名称路径不能借用不同页面的固定目标范围。");
            result.Add(first with{NestedTargets=MergeProjectTargets(group.SelectMany(scope=>scope.NestedTargets))});
        }
        return result;
    }

    /// <summary>生成固定树字面量，普通单段保持原构造形式。</summary>
    private static string SingleTargetArray(IReadOnlyList<NonRedundantSingleTargetScope> scopes)=>
        "new NonRedundantSingleTargetScope[]{"+string.Join(",",scopes.Select(TargetLiteral))+"}";

    private static string TargetLiteral(NonRedundantSingleTargetScope scope)
    {
        var literal="new("+JsonSerializer.Serialize(scope.ColumnId)+","+JsonSerializer.Serialize(scope.PageId)+","+
            JsonSerializer.Serialize(scope.SourceParentId)+")";
        var properties=new List<string>();
        if(scope.SourcePageId is not null)properties.Add("SourcePageId="+JsonSerializer.Serialize(scope.SourcePageId));
        if(scope.NestedTargets.Count>0)properties.Add("NestedTargets="+SingleTargetArray(scope.NestedTargets));
        if(!scope.ApplyPageCondition)properties.Add("ApplyPageCondition=false");
        return properties.Count==0?literal:literal+"{"+string.Join(",",properties)+"}";
    }
}
