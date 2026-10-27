using System.Text.Json;
using System.Text.Json.Nodes;
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
            var trees=table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree").ToArray();
            if(table.Source.IsRongyu!=1||table.IsGlobal||trees.Length==0)return "冗余跨表完整用途要求普通R1来源。";
            if(trees.Any(tree=>!NonRedundantCrossTableSubset.Declared(tree)))return "冗余跨表与自树同页的完整用途仍待证明。";
            if(ExactDataManageCompilation.Declared(page))return "冗余跨表精确权限的完整用途仍待证明。";
            if(page.Scopes.Any(scope=>scope.Source=="parent"))SharedRedundantParentCompilation.Require(model,table,page);
            RequireRealm(model,table,page);var bindings=RelationPropagationCompilation.Bindings(model).Concat(RelationPropagationCompilation.DerivedBindings(model)).ToArray();
            foreach(var source in trees)
            {
                var plan=CrossTableWriteCompilation.Resolve(model,table,page,source).Definition;if(plan is null)return "冗余跨表完整写入证明缺失，隐藏控件不能绕过。";
                var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();
                if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(source.Source.ControlDetail)))return "冗余跨表页级角色覆盖尚未完成统一完整用途证明。";
                if(CrossTableFrontendProof.Reason(plan) is{} mismatch)return mismatch;
                foreach(var level in plan.Levels)
                {
                    RequireRealm(model,level.Read.Target,level.Read.Page);
                    if(level.Read.DerivedName&&!MixedRelationPropagationCompilation.ForModel(model).MixedSourceColumns.GetValueOrDefault(table.Source.TableName,[]).Contains(level.Name.Source.ColumnName,StringComparer.Ordinal))return "冗余跨表派生名称缺少完整混合传播闭包。";
                    if(!bindings.Any(binding=>binding.Source.Source.Id==table.Source.Id&&(binding.CrossTableColumnId==source.Source.Id&&binding.CrossTableLevelKey==level.Read.Key||SharedCrossTableCompilation.ForSource(model,table,page,source.Source.Id) is{} shared&&shared.Plans.Any(p=>p.Read.Source.Source.Id==binding.CrossTableColumnId&&p.Levels.Any(l=>l.Read.Key==binding.CrossTableLevelKey&&l.Id.Source.Id==level.Id.Source.Id)))&&binding.Foreign.Source.Id==level.Id.Source.Id&&binding.Cache.Source.Id==level.Name.Source.Id&&binding.Target.Source.Id==level.Read.Target.Source.Id&&binding.Value.Source.Id==level.Read.Name.Source.Id&&binding.CrossTablePageId==(level.Read.Target.IsGlobal?level.Read.Page.Source.Id:null)))return "冗余跨表各级名称缓存尚未形成完整静态传播闭包。";
                    if(ExportRenderer.IncludesColumn(page,level.Id)||ExportRenderer.IncludesColumn(page,level.Name))return "跨表级联路径单元格导出尚待完整往返证明，完整包不能开放该导出用途。";
                }
                if(ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>column.Source.Id==source.Source.Id))return "跨表级联非空路径导入策略尚待确认，完整包不能开放该导入用途。";
            }
            return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException or ArgumentException){return "冗余跨表完整用途未通过："+error.Message;}
    }
    /// <summary>包内表、候选页及后台必须明确发布于同一项目，不能从空项目推断授权。</summary>
    private static void RequireRealm(GenerationModel model,TableModel table,PageModel page)
    {
        var project=model.Catalog.YzProjectId;if(table.Source.YzProjectId!=project||page.Source.YzProjectId!=project||!model.Catalog.YzAdminTypes.Any(admin=>admin.Id==page.Source.YzAdminTypeId&&admin.YzProjectId==project))throw new InvalidOperationException("冗余跨表完整应用的表、来源页及候选页必须明确属于包内项目和后台。");
    }
}
