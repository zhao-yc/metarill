using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record NonRedundantSelectionModel(TableModel Target,PageModel TargetPage,ColumnModel Name,SelectionControlDefinition Definition);

/// <summary>单值选择页的固定目标、名称和候选范围证明；不解释客户端SQL或改变原弹层。</summary>
internal static class NonRedundantSelectionCompilation
{
    /// <summary>页面覆盖沿原标量合并；显示来源必须保持同表同名称，候选附加条件由目标页承接。</summary>
    internal static NonRedundantSelectionModel Resolve(GenerationModel model,TableModel table,PageModel page,ColumnModel source)
    {
        if(table.Source.IsRongyu!=0||source.Source.ControlKind!="SelectPage"||source.Source.ConfigTableId!=table.Source.Id||source.Type!="long?"||!source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal)||!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(source.Source))throw new InvalidOperationException("非冗余单选必须绑定本表物理int64外键。");
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==source.Source.Id);
        var definition=SelectionControlDefinition.Resolve(source,field);var baseline=SelectionControlDefinition.Resolve(source,null);
        if(definition.NameField!=baseline.NameField)throw new InvalidOperationException("非冗余单选页级名称覆盖改变了固定显示来源。");
        var target=model.Tables.SingleOrDefault(table=>table.Pages.Any(page=>page.Source.Id==definition.PageId))??throw new InvalidOperationException("非冗余单选缺少明确目标页。");
        if(ForeignValidationRenderer.Target(model,source)?.Source.Id!=target.Source.Id)throw new InvalidOperationException("非冗余单选的显示、候选和写入目标不一致。");
        var targetPage=target.Pages.Single(page=>page.Source.Id==definition.PageId);
        if(!targetPage.Source.Extra.TryGetValue("type",out var type)||type.ToString()!="1")throw new InvalidOperationException("非冗余SelectPage目标必须是原type=1单选页。");
        var name=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==definition.NameField);
        if(target.IsGlobal||name is null||name.Type!="string?"||name.IsJsonArray||!targetPage.Readable.Any(column=>column.Source.Id==name.Source.Id)||ManagerCredentialCompilation.IsCredential(target,name))throw new InvalidOperationException("非冗余单选目标名称必须是已开放的字符串列。");
        if(!NonRedundantReadCompilation.Physical(model,target,name))DerivedRelationNamesCompilation.RequireSelection(model,table,target,targetPage,name);
        if(!target.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"))throw new InvalidOperationException("非冗余单选目标缺少项目归属。");
        if(!FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(target,targetPage),definition,out var reason))throw new InvalidOperationException(reason);
        var selection=new NonRedundantSelectionModel(target,targetPage,name,definition);
        var display=SelectionDisplayFieldCompilation.Resolve(table,source);
        if(display.Source.Id!=source.Source.Id&&!IsDisplay(source,display,selection))throw new InvalidOperationException("非冗余单选辅助名称与真实外键/名称来源不一致。");
        return selection;
    }
    /// <summary>原名称筛选键只接受指向同一外键/名称的显式联接列，避免借别的派生列改变筛选含义。</summary>
    internal static bool IsDisplay(ColumnModel source,ColumnModel storage,NonRedundantSelectionModel selection)
    {
        var path=storage.Source.Extra.GetValueOrDefault("columnFullName").ToString();
        return storage.Type=="string?"&&storage.Source.IsVirtualColumn==0&&GenerationModel.Enabled(storage.Source.Extra,"isJoinColumn")&&path.Equals(source.Source.ColumnName[..^3]+"."+selection.Name.Source.ColumnName,StringComparison.OrdinalIgnoreCase);
    }
}
