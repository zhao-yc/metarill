using System.Text.Json;

namespace Metarill.Generation;

/// <summary>只给已编译且父范围明确的原List追加远程接线，常量及复选保持原协议。</summary>
internal static class OriginalRemoteOptionsCompilation
{
    /// <summary>固定字段、路由、表单依赖和可证明父来源；请求不能自行选择目标、表或SQL。</summary>
    internal static string? Definition(GenerationModel model, PageModel page, ColumnModel column, string list, bool filter)
    {
        var relation=RelationCompilation.ForPage(model,page).SingleOrDefault(item=>item.SourceColumn.Source.Id==column.Source.Id);
        if(relation is null)return null;
        var context=ControlRenderer.RelationContext(page,relation,filter);
        if(context.UnresolvedTarget)return null;
        var source=model.Tables.Single(table=>table.Source.Id==page.Source.ConfigTableId);
        // 无表单条件时维持原九项描述与输出字节；只有发布依赖才下发窄槽。
        var definition=new Dictionary<string,object?>
        {
            ["id"]=column.Source.Id,["field"]=column.JsonName,["list"]=list,
            ["endpoint"]="/admin/auto/"+source.Source.TableName+"/"+page.Source.Id+"/"+relation.LegacyEndpoint,
            ["requireParent"]=relation.SourceParents.Count>0,["requireTargetParent"]=relation.TargetParents.Count>0&&!context.ServerDerivedTargetParent,
            ["sameParent"]=context.SameParent,["targetParentField"]=context.TargetParentField,["readonlyField"]=filter?null:column.JsonName+"ReadOnly"
        };
        if(RelationConditionsCompilation.HasForms(relation))
            definition["formFields"]=relation.Conditions!.Forms.Select(binding=>new {field=binding.Column.JsonName,table=GenerationModel.Camel(source.Source.TableName)}).Distinct().ToArray();
        return JsonSerializer.Serialize(definition);
    }

    /// <summary>保留原zxz标签、localdata和change，只追加其既有remote属性与三个事件。</summary>
    internal static string Attributes(ColumnModel column)
    {
        var key="'"+column.Source.Id+"'";var state="originalRemoteOptions["+key+"]";
        return $" remote :key=\"'original-options-{column.Source.Id}-'+{state}.revision\" :remote-selected=\"{state}.selected\" :remote-loading=\"{state}.loading\" :remote-has-more=\"{state}.hasMore\" @open=\"openOriginalRemoteOptions({key})\" @inputChange=\"searchOriginalRemoteOptions({key},$event)\" @loadMore=\"moreOriginalRemoteOptions({key})\"";
    }
}
