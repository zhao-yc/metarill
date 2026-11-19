using Metarill.Core.Metadata;

namespace Metarill.Generation;

internal sealed record FunctionTableSelectionModel(TableModel Target,PageModel Page,ColumnModel Name,
    SelectionControlDefinition Definition,ColumnModel? Parent);

/// <summary>无记录功能表的单选复用实际目标页，表单外键只承接原确认和业务参数。</summary>
internal static class FunctionTableSelectionCompilation
{
    /// <summary>编译原目标页、名称和当前表单父值，附加条件必须已经由目标页执行。</summary>
    internal static FunctionTableSelectionModel Resolve(GenerationModel model,TableModel owner,PageModel page,ColumnModel source)
    {
        if(source.Source.ConfigTableId!=owner.Source.Id||source.Type is not ("long?" or "string?")||!source.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))
            throw new InvalidOperationException("功能表单选字段须为已声明的单值ID。");
        var field=model.Catalog.ConfigPageColumns.SingleOrDefault(item=>item.ConfigPageId==page.Source.Id&&item.ConfigColumnId==source.Source.Id);
        var definition=SelectionControlDefinition.Resolve(source,field);
        var target=model.Tables.SingleOrDefault(table=>table.Pages.Any(candidate=>candidate.Source.Id==definition.PageId));
        if(target is null||target.Source.IsFunctionTable!=0)
            throw new InvalidOperationException("功能表单选需要已发布的实际业务目标页。");
        if(source.Source.LinkConfigTableId.Length>0&&source.Source.LinkConfigTableId!=target.Source.Id||
            ForeignValidationRenderer.Target(model,source) is {} declared&&declared.Source.Id!=target.Source.Id)
            throw new InvalidOperationException("功能表单选目标页与声明关联表不一致。");
        if(!target.IsGlobal&&!target.Columns.Any(column=>column.Source.ColumnName=="yz_project_id"))
            throw new InvalidOperationException("功能表单选目标须声明项目范围或全局范围。");
        var targetPage=target.Pages.Single(candidate=>candidate.Source.Id==definition.PageId);
        if(targetPage.Source.Extra.GetValueOrDefault("type").ToString()!="1")
            throw new InvalidOperationException("功能表单选须使用原type=1单选页。");
        var name=targetPage.Readable.SingleOrDefault(column=>column.Source.ColumnName==definition.NameField||column.JsonName==definition.NameField);
        if(name is null||name.IsJsonArray||ManagerCredentialCompilation.IsCredential(target,name)||
            !name.IsId&&name.Type!="string?"&&!DerivedRelationNamesCompilation.IsTypedLabel(name))
            throw new InvalidOperationException("功能表单选名称须为目标页开放的非凭据标量。");
        if(!FixedPageQueryCompilation.TryProveSelection(FixedPageQueryCompilation.For(target,targetPage),definition,out var reason))
            throw new InvalidOperationException(reason);
        if(!targetPage.Scopes.Any(scope=>scope.Source=="parent"))
            return new(target,targetPage,name,definition,null);
        var syntax=FixedConditionPlan.Compile(targetPage.Source.WhereSql,target.Columns.Select(column=>column.Source.ColumnName));
        var parent=MultiTargetParentScope.Compile(model.Catalog,target.Source,syntax)
            ??throw new InvalidOperationException("功能表单选的实际目标页缺少可证明的父级。");
        var parentFields=page.Readable.Where(column=>column.Source.Id!=source.Source.Id&&!column.IsJsonArray&&
            column.Type is "long?" or "string?"&&
            (column.Source.LinkConfigTableId==parent.Parent.Id||column.Source.LinkConfigTableId.Length==0&&
             column.Source.ColumnName==parent.Parent.TableName+"_id")).Take(2).ToArray();
        if(parentFields.Length!=1)
            throw new InvalidOperationException("功能表单选须开启唯一可读的目标父字段。");
        return new(target,targetPage,name,definition,parentFields[0]);
    }
}