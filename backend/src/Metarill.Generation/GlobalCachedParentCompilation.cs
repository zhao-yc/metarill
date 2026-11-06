namespace Metarill.Generation;

/// <summary>无项目列R1来源的原物理List父上下文；只验证已发布父实体及活动状态，不扩大页面权限。</summary>
internal static class GlobalCachedParentCompilation
{
    /// <summary>父字段必须确为当前页的固定parent范围，并复用同一已证明List关系。</summary>
    internal static GlobalCachedSourceBinding? ForColumn(GenerationModel model,TableModel table,PageModel page,ColumnModel column)
        =>page.Scopes.Any(scope=>scope.Source=="parent"&&scope.Column.Source.Id==column.Source.Id)?GlobalCachedSourceCompilation.ForModel(model).SingleOrDefault(binding=>binding.Source.Source.Id==table.Source.Id&&binding.Foreign.Source.Id==column.Source.Id):null;
    /// <summary>同一父表的不同来源列分别验证，不能只凭表名代替角色。</summary>
    internal static IReadOnlyList<GlobalCachedSourceBinding> ForTable(GenerationModel model,TableModel table,PageModel page,TableModel parent)
        =>page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>ForColumn(model,table,page,scope.Column)).OfType<GlobalCachedSourceBinding>().Where(binding=>binding.Target.Source.Id==parent.Source.Id).ToArray();
    /// <summary>只合并同一直接父角色与纯常量的原候选当前读，其他来源父或动态范围仍由原OptionsScope处理。</summary>
    internal static bool CanCombineCandidate(GenerationModel model,TableModel table,PageModel page,RelationModel relation)
        =>relation.Target.IsGlobal&&relation.TargetPage.Condition is not null&&relation.Conditions is null
            &&relation.ParentBinding?.Declared!=true&&relation.TargetParents.Count==0&&relation.Scopes.Count==0
            &&relation.TargetPage.Scopes.Count==0&&relation.SourceParents.Count==1&&relation.SourceParents[0].Source.Id==relation.Target.Source.Id
            &&page.Scopes.Count(scope=>scope.Source=="parent")==1
            &&ForColumn(model,table,page,relation.SourceColumn)?.Target.Source.Id==relation.Target.Source.Id
            &&SharedGlobalParentCompilation.ForTable(model,table,page,relation.Target).Count==0
            &&SharedRedundantParentCompilation.ForTable(model,table,page,relation.Target).Count==0;
    /// <summary>只有独立标量追加、单一固定物理父且无其他关联写入校验，才允许合并后续行。</summary>
    internal static RelationModel? ForAppendRecheck(GenerationModel model,TableModel table,PageModel page)
    {
        if(!ImportRenderer.Enabled(page)||ImportRenderer.Mode(page) is "OverWriteByFirstColumn" or "custom"||
            BackendRenderer.HasPhysicalFileColumns(model,table)||TrustedSecurityServiceCompilation.Applies(model,table)||
            ExactDataManageCompilation.Declared(page)||ImportRenderer.RelationOperationAware(model,table,page)||
            NonRedundantCrossParentCompilation.Enabled(model,table,page)||
            GlobalCachedSourceCompilation.ForModel(model).Count(binding=>binding.Source.Source.Id==table.Source.Id)!=1||
            BoundRelationValidationRenderer.RequiresAuthorization(table)||
            SharedCrossTableRenderer.ValidateAll(model,table,page).Length!=0||
            page.Writable.Any(column=>column.IsJsonArray))
            return null;
        var relation=RelationCompilation.ForPage(model,page,true).SingleOrDefault(candidate=>CanCombineCandidate(model,table,page,candidate));
        if(relation is null||relation.SourceColumn.IsJsonArray||MaterializedDerivedWriteCompilation.ForTable(model,table).Any(binding=>binding.ForeignKey.Source.Id==relation.SourceColumn.Source.Id))
            return null;
        return table.Columns.Any(column=>!column.IsSystem&&column!=relation.SourceColumn&&
            (column.Source.ControlKind is "List" or "SelectPage" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "CascadeTree"||
             FixedOptionCatalogCompilation.Resolve(model,page,column).Declared||ForeignValidationRenderer.Target(model,column) is not null)) ? null : relation;
    }

    /// <summary>插入到名称刷新之间必须没有其他生成的写后操作。</summary>
    internal static bool CanBatchAppendInsert(GenerationModel model,TableModel table,PageModel page) =>
        ForAppendRecheck(model,table,page) is not null&&
        TimeFieldCompilation.AfterWrite(model,table).Length==0&&
        CrossTableWriteRenderer.After(model,table,page,true,false).Length==0&&
        CascadeTreeTopologyRenderer.After(model,table,page,true).Length==0&&
        CascadeTreeMergedWriteRenderer.After(model,table,page,true,false).Length==0&&
        NonRedundantCrossParentCompilation.VerifyCall(model,table,page).Length==0&&
        SharedRedundantParentCompilation.VerifyCall(model,table,page).Length==0;

    /// <summary>只延后单一固定父规则，存在额外写前处理时继续逐步校验。</summary>
    internal static bool CanDeferAppendCandidate(GenerationModel model,TableModel table,PageModel page)
    {
        if(!CanBatchAppendInsert(model,table,page))return false;
        var cache=GlobalCachedSourceCompilation.ForModel(model).Single(binding=>binding.Source.Source.Id==table.Source.Id).Cache;
        // 本次合并维护的唯一缓存允许保留派生标记，生成实体只能以空值进入原关联校验。
        return table.Columns.All(column=>column.Type is "long" or "long?" or "int?" or "string?" or "decimal?" or "bool?" or "DateTime?")&&
            table.Columns.All(column=>!GenerationModel.Enabled(column.Source.Extra,"isJoinColumn")||
                column.Source.Id==cache.Source.Id&&string.IsNullOrWhiteSpace(column.Source.DefaultValue)&&!page.Writable.Contains(column))&&
            SharedCrossTableSparseRenderer.Capture(model,table,page).Length==0&&
            CrossTableWriteRenderer.ScopeCheck(model,table,page).Length==0&&
            NonRedundantCrossTableCompilation.PackCall(model,table,page).Length==0;
    }

    /// <summary>已取得的父锁只从原新增入口传入，其余调用保留默认空值和原校验。</summary>
    internal static bool HasCombinedCandidate(GenerationModel model,TableModel table,PageModel page)
        =>RelationCompilation.ForPage(model,page,true).Any(relation=>CanCombineCandidate(model,table,page,relation));
    /// <summary>写入取当前写锁，候选来源父只读；原父范围规则仍不借用目标页面的列表权限。</summary>
    internal static string Call(GlobalCachedSourceBinding binding,PageModel page,string parent,string locking)
        =>$"        await ValidateGlobalCachedParent{page.Source.Id}_{binding.Foreign.Source.Id}Async({parent},actor,{locking},cancellationToken);\n";
    /// <summary>与普通物理父规则一致，只校验真实ID/软删除；目录及Actor绑定由独立证明和服务入口保证。</summary>
    internal static string Service(GenerationModel model,TableModel table,PageModel page)
    {
        var result=new System.Text.StringBuilder();
        foreach(var binding in page.Scopes.Where(scope=>scope.Source=="parent").Select(scope=>ForColumn(model,table,page,scope.Column)).OfType<GlobalCachedSourceBinding>().DistinctBy(binding=>binding.Foreign.Source.Id))
        {
            result.AppendLine("    /// <summary>全局父只属于当前发布目录；parentId不是项目或权限证明。</summary>")
                .AppendLine($"    private async Task ValidateGlobalCachedParent{page.Source.Id}_{binding.Foreign.Source.Id}Async(string? parentId,RuntimeActor actor,bool locking,CancellationToken cancellationToken)")
                .AppendLine("    {").AppendLine("        ValidateActor(actor);")
                .AppendLine("        if(!long.TryParse(parentId,NumberStyles.None,CultureInfo.InvariantCulture,out var key)||key<=0||parentId!=key.ToString(CultureInfo.InvariantCulture))throw new ArgumentException(\"当前页面要求规范正整数parentId。\");")
                .AppendLine($"        var query=session.Select<{binding.Target.EntityType}>().Where(entity=>entity.Id==key);");
            if(binding.Target.Columns.Any(column=>column.Source.ColumnName=="delete_no"))result.AppendLine("        query=query.Where(entity=>entity.DeleteNo==null);");
            result.AppendLine("        if(locking){if((await query.ForUpdate().Limit(1).ToListAsync(entity=>new{entity.Id},cancellationToken)).Count!=1)throw new InvalidOperationException(\"父记录不存在或已删除。\");}")
                .AppendLine("        else if(!await query.AnyAsync(cancellationToken))throw new InvalidOperationException(\"父记录不存在或已删除。\");").AppendLine("    }");
        }
        return result.ToString();
    }
}
