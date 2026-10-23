using Metarill.Core.Metadata;
namespace Metarill.Generation;
internal sealed record CrossTableWriteLevel(CrossTableLevelModel Read,ColumnModel Id,ColumnModel Name);
internal sealed record CrossTableWriteModel(CrossTableReadModel Read,IReadOnlyList<CrossTableWriteLevel> Levels);
internal sealed record CrossTableWriteResult(CrossTableWriteModel? Definition,string? Reason);

/// <summary>以同一读取链证明各级物理列归属，生成ID输入及只读名称，不增加页面布局字段。</summary>
internal static class CrossTableWriteCompilation
{
    /// <summary>隐藏控件也在默认值、复制和钩子更新后执行，不能靠页面隐藏绕过规则。</summary>
    internal static IReadOnlyList<CrossTableWriteModel> ForService(GenerationModel model,TableModel table,PageModel page)=>table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").Select(column=>Resolve(model,table,page,column).Definition).OfType<CrossTableWriteModel>().ToArray();
    /// <summary>来源目标重叠由写后图状态协调；冲突列、系统字段及共享辅助列保留写门禁。</summary>
    internal static CrossTableWriteResult Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        CrossTableWriteResult Gap(string reason)=>new(null,"跨表级联写入尚未支持："+reason);var read=CrossTableReadCompilation.Resolve(model,table,page,source);if(read.Definition is not{} plan)return Gap(read.Reason??"未证明读取链。");
        if(plan.Levels.Count(level=>level.Target.Source.Id==table.Source.Id)>1)return Gap("来源表重复层位仍需组合验证。");var physical=MetadataStoragePolicy.ForTable(model.Catalog,table.Source);var result=new List<CrossTableWriteLevel>();
        foreach(var level in plan.Levels)
        {
            var id=table.Columns.SingleOrDefault(column=>column.Source.ColumnName==level.Prefix+"_id");var name=table.Columns.SingleOrDefault(column=>column.Source.ColumnName==level.Prefix+"_name");
            if(id is null||name is null||id.IsSystem||name.IsSystem||id.Type is not("string?" or "long?")||name.Type!="string?"||id.IsJsonArray||name.IsJsonArray||!physical.IsPhysical(id.Source)||(table.Source.IsRongyu==1?!physical.IsPhysical(name.Source):physical.IsPhysical(name.Source))||GenerationModel.Enabled(id.Source.Extra,"isJoinColumn"))return Gap("各级ID/名称必须有可证明的非系统物理标量，显式冲突不能覆盖。");
            if(id.Source.Id!=source.Source.Id&&!string.IsNullOrWhiteSpace(id.Source.ControlKind))return Gap("辅助ID的独立控件规则需要协调。");
            if(!string.IsNullOrWhiteSpace(id.Source.LinkConfigTableId)&&id.Source.LinkConfigTableId!=level.Target.Source.Id)return Gap("来源辅助ID关联表不一致。");result.Add(new(level,id,name));
        }
        var fields=result.SelectMany(level=>new[]{level.Id.Source.ColumnName,level.Name.Source.ColumnName}).ToHashSet(StringComparer.Ordinal);
        foreach(var other in table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree"&&column.Source.Id!=source.Source.Id))if(CrossTableReadCompilation.Resolve(model,table,page,other).Definition is{} shared&&shared.Levels.Any(level=>fields.Contains(level.Prefix+"_id")||fields.Contains(level.Prefix+"_name")))return Gap("不同级联共享辅助字段需要聚合协调。");
        if(table.Source.IsRongyu==0)
        {
            try{NonRedundantCrossTableCompilation.Require(model,table,page,plan);}
            catch(Exception error)when(error is InvalidOperationException or ArgumentException or NotSupportedException){return Gap(error.Message);}
        }
        return new(new(plan,result),null);
    }
    /// <summary>原控件隐式携带全部级ID；名称只读，布局、搜索、排序和元数据本身不扩张。</summary>
    internal static GenerationModel Prepare(GenerationModel model)=>model with{Tables=model.Tables.Select(table=>
    {
        var names=table.Pages.SelectMany(page=>ForService(model,table,page)).SelectMany(plan=>plan.Levels).Select(level=>level.Name.Source.Id).ToHashSet(StringComparer.Ordinal);
        ColumnModel Fix(ColumnModel column)=>names.Contains(column.Source.Id)?column with{IsManagedCascadeName=true}:column;
        var pages=table.Pages.Select(page=>
        {
            var plans=ForService(model,table,page);var read=plans.Where(plan=>page.Readable.Any(column=>column.Source.Id==plan.Read.Source.Source.Id)).SelectMany(plan=>plan.Levels).SelectMany(level=>new[]{level.Id,level.Name});var write=plans.Where(plan=>page.Writable.Any(column=>column.Source.Id==plan.Read.Source.Source.Id)).SelectMany(plan=>plan.Levels).Select(level=>level.Id);
            return page with{Readable=page.Readable.Concat(read).DistinctBy(column=>column.Source.Id).Select(Fix).ToArray(),Writable=page.Writable.Concat(write).Where(column=>!names.Contains(column.Source.Id)).DistinctBy(column=>column.Source.Id).Select(Fix).ToArray(),ListColumns=page.ListColumns.Select(Fix).ToArray(),DetailColumns=page.DetailColumns.Select(Fix).ToArray(),Searchable=page.Searchable.Select(Fix).ToArray(),Sortable=page.Sortable.Select(Fix).ToArray()};
        }).ToArray();return table with{Columns=table.Columns.Select(Fix).ToArray(),Pages=pages};
    }).ToArray()};
    /// <summary>来源父范围若使用隐式级ID，目标只能由同一固定链唯一推导。</summary>
    internal static TableModel? ParentTarget(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
    {
        var targets=ForService(model,table,page).SelectMany(plan=>plan.Levels).Where(level=>level.Id.Source.Id==column.Source.Id).Select(level=>level.Read.Target).DistinctBy(target=>target.Source.Id).ToArray();return targets.Length==1?targets[0]:null;
    }
}
