using System.Text.Json.Serialization;
using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record GenerationCapabilityGap(
    [property:JsonPropertyName("pageId")]string PageId,
    [property:JsonPropertyName("column")]string Column,
    [property:JsonPropertyName("usage")]string Usage,
    [property:JsonPropertyName("reason")]string Reason);

/// <summary>原UI分支恢复后，控件存在不等于后端完成；生成提示和完整包门禁共用同一缺口事实。</summary>
internal static class GenerationCapabilityAudit
{
    /// <summary>审计实际读取用途与固定安全服务依赖，结果不受客户端请求影响。</summary>
    internal static IReadOnlyList<GenerationCapabilityGap> Collect(GenerationModel model)
    {
        var gaps=new List<GenerationCapabilityGap>();
        foreach(var table in model.Tables)
        foreach(var page in table.Pages)
        {
            // 完整包只开放正式编译通过的标量非冗余链路；未接线的用途仍逐页报告，不以存储模式一律拒绝。
            if (NonRedundantReadCompilation.Requires(model, table))
            {
                try
                {
                    _ = NonRedundantReadCompilation.ForPage(model, table, page);
                    RequirePublishedRealm(model, table, page);
                    foreach (var relation in RelationCompilation.ForPage(model, page)) RequirePublishedRealm(model, relation.Target, relation.TargetPage);
                    foreach(var tree in CascadeTreeReadCompilation.ForService(model,table,page).Where(plan=>NonRedundantSelfTreeSubset.Supports(model,table,page,plan.Source)))RequirePublishedRealm(model,tree.Target,tree.TargetPage);
                }
                catch (Exception error) when (error is InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
                { gaps.Add(new(page.Source.Id, "", "runtime", "非冗余固定读取未完成：" + error.Message)); }
            }
            // 带联接标记的多选缓存即使隐藏来源字段，也必须有明确完整的内部写入证明。
            foreach(var source in table.Columns.Where(column=>table.Source.IsRongyu==1&&column.Source.ControlKind=="MultiSelectPage"))
            {var proof=MultiSelectCompilation.Resolve(model,table,page,source,forService:true);if(proof.Definition is null)gaps.Add(new(page.Source.Id,source.Source.ColumnName,"runtime",proof.Reason!));}
            // 隐藏复选仍参与复制／默认值／依赖变化，不能通过隐藏控件绕过整表写入证明。
            foreach(var source in table.Columns.Where(column=>DatabaseCheckboxCompilation.Generic(table,column)))
            {
                var checkbox=DatabaseCheckboxCompilation.Resolve(model,table,page,source);
                if(checkbox.Definition is not{} definition){gaps.Add(new(page.Source.Id,source.Source.ColumnName,"runtime",checkbox.Reason!));continue;}
                try{RequirePublishedRealm(model,table,page);if(definition.Target.Source.YzProjectId!=model.Catalog.YzProjectId||definition.Name.Source.YzProjectId!=model.Catalog.YzProjectId)throw new InvalidOperationException("复选目标表及名称必须明确属于包内项目。");}
                catch(InvalidOperationException error){gaps.Add(new(page.Source.Id,source.Source.ColumnName,"runtime",error.Message));}
            }
            // 原统计声明的类型和可读边界必须在完整包stage创建之前验证。
            _=OriginalListStatisticsCompilation.ForPage(model,table,page);
            if (ManagerCredentialCompilation.Gap(table,page) is {} credentialGap) gaps.Add(new(page.Source.Id,"password","detail",credentialGap));
            // 虚拟列在模型物理列过滤前存在；固定目录声明不能因该过滤而从Full门禁静默消失。
            foreach(var field in model.Catalog.ConfigPageColumns.Where(field=>field.ConfigPageId==page.Source.Id))
            {
                // GenerationModel已验证同页字段的同表归属；这里只审实际声明，不能从其它页借覆盖。
                var source=model.Catalog.ConfigColumns.Single(column=>column.Id==field.ConfigColumnId && column.ConfigTableId==table.Source.Id);
                if(source.IsVirtualColumn!=0 && FixedOptionCatalogCompilation.HasDeclaration(source,field))
                    gaps.Add(new(page.Source.Id,source.ColumnName,"detail","固定版本目录必须使用已声明的非联接物理版本字段。"));
                if(source.IsVirtualColumn!=0 && FixedRoleCheckboxCompilation.Declared(source,field))
                    gaps.Add(new(page.Source.Id,source.ColumnName,"detail","MultiCheckBoxDatabase固定角色声明必须使用非联接物理数组字段。"));
                if(source.IsVirtualColumn!=0 && FixedRoleAdminNameCompilation.Declared(table,page,source))
                    gaps.Add(new(page.Source.Id,source.ColumnName,"detail","固定角色后台名称必须使用非联接物理后台ID。"));
            }
            foreach(var column in page.Readable)
                if(ControlRenderer.Render(model,table,page,column).UnsupportedReason is {} reason)
                    gaps.Add(new(page.Source.Id,column.Source.ColumnName,"detail",reason));
            // 账号写协调按固定服务身份保留；字段证明失败或字段被移除不能让尚未验收的服务提前发布。
            if(FixedRoleCheckboxCompilation.RequiresRuntimeCoordination(table,page))
                gaps.Add(new(page.Source.Id,"yz_role_ids","runtime","MultiCheckBoxDatabase角色最终合并后台校验及撤权协调仍待Runtime A验收。"));
            // 原冗余角色导出本来就是ID数组JSON文本；选择回执与后台名单选导出已有固定投影，不能再以旧假设阻断。
            // 原选择页按辅助名称键搜索；只有同一固定编译计划确认可执行时才解除筛选缺口。
            var names=SelectionNameFilterCompilation.ForPage(model,table,page);
            foreach(var gap in names.Gaps)gaps.Add(new(page.Source.Id,gap.Source.Source.ColumnName,"filter",gap.Reason));
            foreach(var column in page.Searchable.Where(column=>!SelectionNameFilterCompilation.UsesNameInput(model,page,column)&&!SelectionNameFilterCompilation.UsesSourceTextOverride(model,page,column)))
                if(ControlRenderer.RenderFilter(model,page,column).UnsupportedReason is {} reason)
                    gaps.Add(new(page.Source.Id,column.Source.ColumnName,"filter",reason));
        }
        return gaps;
    }
    /// <summary>完整包的正式权限目录按显式项目发布，不能把生成夹具的空项目当成已授权页面。</summary>
    private static void RequirePublishedRealm(GenerationModel model, TableModel table, PageModel page)
    {
        var project = model.Catalog.YzProjectId;
        if (table.Source.YzProjectId != project || page.Source.YzProjectId != project ||
            !model.Catalog.YzAdminTypes.Any(admin => admin.Id == page.Source.YzAdminTypeId && admin.YzProjectId == project))
            throw new InvalidOperationException("非冗余完整应用的本表、来源页及候选页必须明确属于包内项目和后台。");
    }
}
