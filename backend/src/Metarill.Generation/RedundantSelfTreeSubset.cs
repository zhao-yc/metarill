using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
namespace Metarill.Generation;

/// <summary>R1物理自树完整用途逐项证明；混合跨表仍由自身完整规则验证，不按控件数量放行。</summary>
internal static class RedundantSelfTreeSubset
{
    /// <summary>只给已证明的selfTree提供原控件完整包能力。</summary>
    internal static bool Supports(GenerationModel model,TableModel table,PageModel page,ColumnModel source)=>table.Source.IsRongyu==1&&source.Source.ControlKind=="CascadeTree"&&!NonRedundantCrossTableSubset.Declared(source)&&Reason(model,table,page) is null;
    /// <summary>单控件证明不反向调用跨表门槛，避免相互递归；隐藏同伴也必须完整。</summary>
    internal static bool FixedTarget(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        var overlay=model.Catalog.ConfigPageColumns.SingleOrDefault(f=>f.ConfigPageId==page.Source.Id&&f.ConfigColumnId==source.Source.Id)?.Extra.GetValueOrDefault("controlDetail").ToString();if(!string.IsNullOrWhiteSpace(overlay)&&!JsonNode.DeepEquals(JsonNode.Parse(overlay),JsonNode.Parse(source.Source.ControlDetail)))return false;
        var plan=CascadeTreeWriteCompilation.Resolve(model,table,page,source).Definition;if(plan is null||plan.Read.DerivedName||plan.Read.Target.IsGlobal||plan.Read.Target.Source.IsView!=0||plan.Read.Target.Source.IsFunctionTable!=0)return false;
        Realm(model,plan.Read.Target,plan.Read.TargetPage);return SelfTreePropagationCompilation.ForTable(model,table).Any(binding=>binding.Foreign.Source.Id==source.Source.Id&&binding.Target.Source.Id==plan.Read.Target.Source.Id&&binding.Value.Source.Id==plan.Read.Name.Source.Id&&binding.Cache.Source.Id==plan.Name.Source.Id);
    }
    /// <summary>完整包逐个证明自树、物理父及跨表同伴；未覆盖的精确、派生、全局及自树父参数保持拒绝。</summary>
    internal static string? Reason(GenerationModel model,TableModel table,PageModel page)
    {
        try
        {
            if(table.Source.IsRongyu!=1||table.IsGlobal||table.Source.IsView!=0||table.Source.IsFunctionTable!=0||ExactDataManageCompilation.Declared(page))return "R1自树完整用途要求普通实体及已证明的固定范围。";Realm(model,table,page);
            var trees=table.Columns.Where(c=>c.Source.ControlKind=="CascadeTree"&&!NonRedundantCrossTableSubset.Declared(c)).ToArray();if(trees.Length==0||trees.Any(tree=>!FixedTarget(model,table,page,tree)))return "R1自树的当前路径、物理叶子缓存或传播维护尚未取得完整证明。";
            var cross=table.Columns.Any(NonRedundantCrossTableSubset.Declared);if(cross&&RedundantCrossTableSubset.Reason(model,table,page) is{} gap)return gap;
            if(!cross)Parents(model,table,page);return null;
        }
        catch(Exception error)when(error is InvalidOperationException or NotSupportedException or ArgumentException or JsonException){return "R1自树完整用途未通过："+error.Message;}
    }
    /// <summary>非跨表页面的父只采用现有物理归属语义，不把缓存名称、树控件或自身主键当成父参数。</summary>
    private static void Parents(GenerationModel model,TableModel table,PageModel page)
    {
        var physical=MetadataStoragePolicy.ForTable(model.Catalog,table.Source);
        foreach(var scope in page.Scopes.Where(s=>s.Source=="parent"))
        {
            var column=table.Columns.Single(c=>c.Source.Id==scope.Column.Source.Id);if(column.IsId||column.IsManagedCascadeName||column.IsJsonArray||column.Source.ControlKind=="CascadeTree"||column.Type is not("string?" or "long?")||!physical.IsPhysical(column.Source)||GenerationModel.Enabled(column.Source.Extra,"isJoinColumn"))throw new NotSupportedException("自树父参数需要独立已声明的物理标量外键。");
            var target=model.Tables.SingleOrDefault(t=>column.Source.LinkConfigTableId.Length>0?t.Source.Id==column.Source.LinkConfigTableId:t.Source.TableName+"_id"==column.Source.ColumnName);if(target is null||target.Source.Id==table.Source.Id||target.IsGlobal||target.Source.IsView!=0||target.Source.IsFunctionTable!=0||target.Source.YzProjectId!=model.Catalog.YzProjectId)throw new NotSupportedException("自树父参数没有独立普通实体归属证明。");
            var ts=MetadataStoragePolicy.ForTable(model.Catalog,target.Source);if(!target.Columns.Any(c=>c.IsId&&c.Type=="long"&&ts.IsPhysical(c.Source)&&!GenerationModel.Enabled(c.Source.Extra,"isJoinColumn"))||!target.Columns.Any(c=>c.Source.ColumnName=="yz_project_id"&&(c.Type is "string?" or "long?")&&ts.IsPhysical(c.Source)&&!GenerationModel.Enabled(c.Source.Extra,"isJoinColumn")))throw new NotSupportedException("自树父表缺少物理主键或项目身份。");
        }
    }
    /// <summary>控件目标页与来源后台须明确发布，不能由空项目字段推断授权。</summary>
    private static void Realm(GenerationModel model,TableModel table,PageModel page){if(table.Source.YzProjectId!=model.Catalog.YzProjectId||page.Source.YzProjectId!=model.Catalog.YzProjectId||!model.Catalog.YzAdminTypes.Any(a=>a.Id==page.Source.YzAdminTypeId&&a.YzProjectId==model.Catalog.YzProjectId))throw new InvalidOperationException("R1自树表、来源页及目标页必须明确发布于包内项目和后台。");}
}
