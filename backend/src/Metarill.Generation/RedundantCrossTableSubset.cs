using System.Text.Json;
namespace Metarill.Generation;

/// <summary>R1完整用途逐项证明原UI、各级存储／缓存及包内权限身份，未证明的用途仍明确拒绝。</summary>
internal static class RedundantCrossTableSubset
{
    /// <summary>只有当前页面全部跨表控件和实际用途都通过时才声明支持。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,PageModel page,ColumnModel source)=>table.Source.IsRongyu==1&&NonRedundantCrossTableSubset.Declared(source)&&Reason(model,table,page) is null;
    /// <summary>不因控件隐藏、省略或有一个有效同伴就放过另一条坏链。</summary>
    internal static string? Reason(GenerationModel model,TableModel table,PageModel page)
    {
        try
        {
            var companions=table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").ToArray();var trees=companions.Where(NonRedundantCrossTableSubset.Declared).ToArray();
            if(table.Source.IsRongyu!=1||table.IsGlobal||trees.Length==0)return "冗余跨表完整用途要求普通R1来源。";
            if(companions.Where(tree=>!NonRedundantCrossTableSubset.Declared(tree)).Any(tree=>!RedundantSelfTreeSubset.FixedTarget(model,table,page,tree)))return "冗余跨表的自树同伴尚未取得固定物理缓存与传播证明。";
            if(ExactDataManageCompilation.Declared(page)&&ExactDataManageCompilation.Resolve(model,table,page) is not {Derived:false})
                return "冗余跨表精确管理要求物理单值范围列。";
            if(page.Scopes.Any(scope=>scope.Source=="parent"))SharedRedundantParentCompilation.Require(model,table,page);
            RequireRealm(model,table,page);var bindings=RelationPropagationCompilation.Bindings(model).Concat(RelationPropagationCompilation.DerivedBindings(model)).ToArray();
            var plans=new List<CrossTableWriteModel>(trees.Length);
            foreach(var source in trees)
            {
                var plan=CrossTableWriteCompilation.Resolve(model,table,page,source).Definition;if(plan is null)return "冗余跨表完整写入证明缺失，隐藏控件不能绕过。";
                plans.Add(plan);
                if (!CrossTableFrontendProof.KeepsStorageDefinition(model, page, source))
                    return "冗余跨表页级角色覆盖尚未完成统一完整用途证明。";
                if(CrossTableFrontendProof.Reason(model,page,plan) is{} mismatch)return mismatch;
                foreach(var level in plan.Levels)
                {
                    RequireRealm(model,level.Read.Target,level.Read.Page);
                    if((level.Read.DerivedName||FixedPageConditionCompilation.HasLogicalFields(level.Read.Page))&&!MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns.GetValueOrDefault(table.Source.TableName,[]).Contains(level.Name.Source.ColumnName,StringComparer.Ordinal))return "冗余跨表派生名称缺少完整混合传播闭包。";
                    if(!bindings.Any(binding=>binding.Source.Source.Id==table.Source.Id&&(binding.CrossTableColumnId==source.Source.Id&&binding.CrossTableLevelKey==level.Read.Key||SharedCrossTableCompilation.ForSource(model,table,page,source.Source.Id) is{} shared&&shared.Plans.Any(p=>p.Read.Source.Source.Id==binding.CrossTableColumnId&&p.Levels.Any(l=>l.Read.Key==binding.CrossTableLevelKey&&l.Id.Source.Id==level.Id.Source.Id)))&&binding.Foreign.Source.Id==level.Id.Source.Id&&binding.Cache.Source.Id==level.Name.Source.Id&&binding.Target.Source.Id==level.Read.Target.Source.Id&&binding.Value.Source.Id==level.Read.Name.Source.Id&&binding.CrossTablePageId==(level.Read.Target.IsGlobal?level.Read.Page.Source.Id:null)&&binding.CrossTableRecursivePageId==(FixedPageConditionCompilation.HasLogicalFields(level.Read.Page)?level.Read.Page.Source.Id:null)))return "冗余跨表各级名称缓存尚未形成完整静态传播闭包。";
                }
            }
            var slots=plans.SelectMany(plan=>plan.Levels).SelectMany(level=>new[]{level.Id,level.Name}).DistinctBy(column=>column.Source.Id).ToArray();
            var pathIds=trees.Select(tree=>tree.Source.Id).ToHashSet(StringComparer.Ordinal);
            var imports=ImportRenderer.Enabled(page)?ImportRenderer.Columns(page):[];
            var exportedSlots=slots.Where(column=>ExportRenderer.IncludesColumn(page,column)).ToArray();
            var importedSlots=slots.Where(column=>imports.Any(field=>field.Source.Id==column.Source.Id)).ToArray();
            // 固定末级从工作簿排除，但导入仍绑定并验证该跨表路径。
            var importsFixedPath=ImportRenderer.Enabled(page)&&CrossTableExcelRenderer.HasFixedPath(page);
            if(exportedSlots.Length==0&&importedSlots.Length==0&&!importsFixedPath)return null;
            // 已逐个验证自树同伴的写入、名称缓存和传播。Excel 路径列分别复用各自的解析与导出。
            var supported=model.Catalog.DatabaseProvider=="mysql";
            // 路径列按固定树反查，已配置的辅助列使用已证明的物理名称副本。
            if(exportedSlots.Length>0&&(!supported||ExportRenderer.Mode(page) is not ("" or "list" or "edit")))
                return CrossTableExcelRenderer.ExportAcceptancePending;
            if((importedSlots.Length>0||importsFixedPath)&&(!supported||!SupportsImport(model,table,page,plans,imports)||
                importedSlots.Any(column=>!pathIds.Contains(column.Source.Id))))return CrossTableExcelRenderer.ImportAcceptancePending;
            return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return "冗余跨表完整用途未通过："+error.Message;}
    }
    /// <summary>覆盖键取原表排序中的首个导入字段，必须能在来源表上以物理标量匹配。</summary>
    private static bool SupportsImport(GenerationModel model,TableModel table,PageModel page,
        IReadOnlyList<CrossTableWriteModel> plans,IReadOnlyList<ColumnModel> columns)
    {
        var mode=ImportRenderer.Mode(page);
        if(mode is "" or "OnlyNew")return true;
        if(mode!="OverWriteByFirstColumn"||columns.Count==0)return false;
        var key=table.Columns.First(columns.Contains);
        if(key.IsJsonArray||key.IsManagedCascadeName||!NonRedundantReadCompilation.Physical(model,table,key))return false;
        if(NonRedundantCrossTableSubset.Declared(key))
            return (key.Type is "long?" or "string?")&&plans.Any(plan=>plan.Read.Source.Source.Id==key.Source.Id&&plan.Levels[^1].Id.Source.Id==key.Source.Id);
        return ImportRenderer.SupportsScalarOverwriteKey(model,table,page,key);
    }

    /// <summary>包内表、候选页及后台必须明确发布于同一项目，不能从空项目推断授权。</summary>
    private static void RequireRealm(GenerationModel model,TableModel table,PageModel page)
    {
        var project=model.Catalog.YzProjectId;if(table.Source.YzProjectId!=project||page.Source.YzProjectId!=project||!model.Catalog.YzAdminTypes.Any(admin=>admin.Id==page.Source.YzAdminTypeId&&admin.YzProjectId==project))throw new InvalidOperationException("冗余跨表完整应用的表、来源页及候选页必须明确属于包内项目和后台。");
    }
}
