using System.Text.Json;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal static partial class NonRedundantReadCompilation
{
    /// <summary>从实际投影或查询依赖收集项目内名称路径。没有固定条件的中间段只传递下一页的绑定。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> ProjectPathTargets(GenerationModel model,TableModel table,
        PageModel page,IEnumerable<string> fields,bool multiValuePath=false)
    {
        if(table.Source.IsRongyu!=0)return [];
        var active=new HashSet<(string Table,string Field)>();
        return MergeProjectTargets(fields.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(field=>ProjectPathTarget(model,table,page,[field],active,multiValuePath)).OfType<NonRedundantSingleTargetScope>());
    }

    /// <summary>显式联接和原自动名称使用与 SQL 相同的物理关系字段及表级显示名。</summary>
    private static NonRedundantSingleTargetScope? ProjectPathTarget(GenerationModel model,TableModel table,PageModel page,
        string[] path,HashSet<(string Table,string Field)> active,bool multiValuePath)
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
                return declared.Length<2?null:ProjectPathTarget(model,table,page,declared,active,multiValuePath);
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
            if(relation is null||table.IsGlobal&&!relation.Target.IsGlobal||relation.ParentBinding?.Declared==true)
                return null;
            if(relation.ScopedStoredList)
                return ProjectTarget(model,table,page,relation);
            if(relation.Target.Source.IsRongyu!=0)
            {
                // 多值名称按实际物理字典页限定可见性，来源页也须保留供运行时复核。
                if(UsesPageBoundGlobalList(relation))
                    return ProjectTarget(model,table,page,relation) with{ApplyPageCondition=false};
                if(!multiValuePath||relation.Target.Source.IsRongyu!=1)return null;
                var storedPage=relation.Target.IsGlobal
                    ?Metarill.Core.Metadata.GlobalSingleRelationTarget.StoredMultiName(model.Catalog,table.Source,page.Source,
                        foreign.Source,relation.Target.Source)?.Page
                    :Metarill.Core.Metadata.ProjectScopedSingleRelationTarget.NestedPage(model.Catalog,table.Source,page.Source,
                        foreign.Source,relation.Target.Source,allowStoredTarget:true) is {Scoped:true} local?local.Page:null;
                return storedPage is null?null:new(foreign.Source.Id,storedPage.Id){SourcePageId=page.Source.Id};
            }
            var downstream=ProjectPathTarget(model,relation.Target,relation.TargetPage,tail,active,multiValuePath);
            IReadOnlyList<NonRedundantSingleTargetScope> nested=downstream is null?[]:[downstream];
            // 即使原 List 未声明附加条件，名称依旧受实际全局页的物理常量限制。
            var globalLiteralScope=multiValuePath&&relation.Target.IsGlobal&&relation.TargetPage.Scopes.Count==0&&
                relation.TargetPage.Condition is {Syntax.Variables.Count:0,CrossFields.Count:0,SelfTreeFields.Count:0};
            var scoped=UsesProjectSingleRead(relation)||globalLiteralScope;
            if(scoped||UsesPageBoundGlobalList(relation))
            {
                var scope=ProjectTarget(model,table,page,relation);
                return scope with{ApplyPageCondition=scoped,NestedTargets=nested,SourcePageId=globalLiteralScope?page.Source.Id:scope.SourcePageId};
            }
            return downstream is null?null:new(foreign.Source.Id,relation.TargetPage.Source.Id)
                {ApplyPageCondition=false,NestedTargets=nested,SourcePageId=relation.Target.IsGlobal?page.Source.Id:null};
        }
    }

    /// <summary>同一路径的不同列复用页绑定，同表不同关系角色依然各自编译。</summary>
    internal static IReadOnlyList<NonRedundantSingleTargetScope> MergeProjectTargets(IEnumerable<NonRedundantSingleTargetScope> scopes)
    {
        var result=new List<NonRedundantSingleTargetScope>();
        foreach(var group in scopes.GroupBy(scope=>scope.ColumnId,StringComparer.Ordinal))
        {
            var first=group.First();
            if(group.Any(scope=>scope.PageId!=first.PageId||scope.SourceParentId!=first.SourceParentId||
                scope.SourcePageId!=first.SourcePageId||scope.ControlSourcePageId!=first.ControlSourcePageId||
                scope.ApplyPageCondition!=first.ApplyPageCondition))
                throw new InvalidOperationException("同一名称路径不能借用不同页面的固定目标范围。");
            var display=group.Select(scope=>scope.DisplayNameColumnId).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            if(display.Length>1)throw new InvalidOperationException("同一名称路径不能借用另一页面的显示列。");
            result.Add(first with{DisplayNameColumnId=display.SingleOrDefault(),NestedTargets=MergeProjectTargets(group.SelectMany(scope=>scope.NestedTargets))});
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
        if(scope.ControlSourcePageId is not null)properties.Add("ControlSourcePageId="+JsonSerializer.Serialize(scope.ControlSourcePageId));
        if(scope.DisplayNameColumnId is not null)properties.Add("DisplayNameColumnId="+JsonSerializer.Serialize(scope.DisplayNameColumnId));
        if(scope.NestedTargets.Count>0)properties.Add("NestedTargets="+SingleTargetArray(scope.NestedTargets));
        if(!scope.ApplyPageCondition)properties.Add("ApplyPageCondition=false");
        return properties.Count==0?literal:literal+"{"+string.Join(",",properties)+"}";
    }
}
