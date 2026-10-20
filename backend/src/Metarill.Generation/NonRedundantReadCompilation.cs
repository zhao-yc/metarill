using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record NonRedundantOutput(string Storage,string Property,string JsonName,string Type);
internal sealed record NonRedundantPagePlan(IReadOnlyList<NonRedundantOutput> Outputs,IReadOnlyList<string> Dependencies);

/// <summary>生成侧提前证明单值用途及物理写边界；数据库表达式由数据适配器编译。</summary>
internal static class NonRedundantReadCompilation
{
    /// <summary>没有派生关系的原纯物理非冗余页继续使用既有强类型路径。</summary>
    internal static bool Requires(GenerationModel model,TableModel table)=>table.Source.IsRongyu==0&&model.Catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Source.Id&&column.IsVirtualColumn==0&&(!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column)||MetadataStoragePolicy.IsRelationColumn(column)));
    /// <summary>所有实体写入复用同一存储计划，不把读取派生属性持久化。</summary>
    internal static bool Physical(GenerationModel model,TableModel table,ColumnModel column)=>MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column.Source);
    /// <summary>完整应用仍另有验收门槛；这里只开放已经接通的单值CRUD读取用途。</summary>
    internal static NonRedundantPagePlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.IsGlobal||page.Source.IsImport==1||page.Source.IsExport==1||ExportRenderer.Enabled(page)||ImportRenderer.Enabled(page)||SelectionPageRenderer.Enabled(page))throw new InvalidOperationException("非冗余全局/导入导出/选择页读取仍待完整接线。");
        if(table.Columns.Any(column=>column.Source.ControlKind is "CascadeTree" or "Title" or "Tab" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "SelectPage"||column.IsJsonArray))throw new InvalidOperationException("非冗余多值、级联、布局读取仍待接线。");
        if(page.Searchable.Any(column=>column.Source.LogicalType is not("string" or "int64" or "int32" or "decimal" or "bool" or "datetime" or "date" or "time")))throw new InvalidOperationException("非冗余筛选字段类型尚未接线。");
        if(page.Scopes.Any(scope=>!Physical(model,table,scope.Column)))throw new InvalidOperationException("非冗余父范围必须绑定物理外键。");
        if(ManagerCredentialCompilation.IsTable(table)||FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null||FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null||FixedOptionCatalogCompilation.ForPage(model,page).Count!=0)throw new InvalidOperationException("非冗余固定安全模块及目录读取尚需独立验收。");
        foreach(var relation in table.Columns.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联写入缺少已接通的明确目标。");ValidateTargetDeclarations(model,relation,target);
            if(target.IsGlobal)throw new InvalidOperationException("全局关联读取尚需完整接线。");
        }
        var outputs=page.Readable.Select(column=>new NonRedundantOutput(column.Source.ColumnName,column.Property,column.JsonName,column.Type)).ToList();
        foreach(var relation in page.Readable.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is not("List" or "SelectPage"))continue;
            var pageField=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==relation.Source.Id);
            if(pageField?.Extra.TryGetValue("controlDetail",out var detail)==true&&!string.IsNullOrWhiteSpace(detail.ToString())&&HasEffectiveOverride(relation.Source.ControlDetail,detail.ToString()))throw new InvalidOperationException("非冗余页级关系控件覆盖尚需接线。");
            var nameField="name";if(!string.IsNullOrWhiteSpace(relation.Source.ControlDetail)){using var document=JsonDocument.Parse(relation.Source.ControlDetail);if(document.RootElement.TryGetProperty("nameField",out var name))nameField=name.GetString()!;}
            if(nameField=="id"||!relation.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联目标缺少已接通的固定候选来源。");
            ValidateTargetDeclarations(model,relation,target);
            var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField);
            if(target.IsGlobal||display is null||!Physical(model,target,display))throw new InvalidOperationException("全局或派生名称候选仍待完整接线。");
            var alias=relation.Source.ColumnName[..^3]+"_"+nameField;MetadataValidation.RequireIdentifier(alias);
            if(!outputs.Any(output=>output.Storage.Equals(alias,StringComparison.OrdinalIgnoreCase)))outputs.Add(new(alias,GenerationModel.Pascal(alias),GenerationModel.Camel(alias),""));
        }
        var dependencies=page.Searchable.Concat(page.Sortable).Concat(FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>scope.Column)).Concat(FixedPageQueryCompilation.For(table,page).DefaultOrder.Select(item=>item.Column)).Select(column=>column.Source.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var compiled=NonRedundantReads.Compile(model.Catalog,table.Source.Id,outputs.Select(output=>output.Storage).ToArray(),dependencies);
        outputs=outputs.Select(output=>output.Type.Length>0?output:output with{Type=compiled.OutputTypes[output.Storage] switch{"string"=>"string?","int64"=>"long?","int32"=>"int?","decimal"=>"decimal?","bool"=>"bool?","datetime" or "date"=>"DateTime?","time"=>"TimeSpan?",_=>throw new InvalidOperationException("自动显示字段类型尚未接线。")}}).ToList();
        if(outputs.Select(output=>output.Property).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count)throw new InvalidOperationException("派生显示属性冲突。");
        return new(outputs,dependencies);
    }
    /// <summary>读写共用同一关联目标；相互冲突的有效声明不能按不同优先级串表。</summary>
    private static void ValidateTargetDeclarations(GenerationModel model,ColumnModel relation,TableModel target)
    {
        if(string.IsNullOrWhiteSpace(relation.Source.ControlDetail))return;
        using var document=JsonDocument.Parse(relation.Source.ControlDetail);var definition=document.RootElement;
        if(definition.TryGetProperty("tableId",out var id)&&model.Tables.SingleOrDefault(table=>table.Source.Id==id.ToString()) is {} byId&&byId.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联读取和写入目标声明不一致。");
        if(definition.TryGetProperty("table",out var name)&&model.Tables.SingleOrDefault(table=>table.Source.TableName==name.ToString()) is {} byName&&byName.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联表名和ID声明不一致。");
        if(definition.TryGetProperty("pageId",out var page)&&model.Tables.SingleOrDefault(table=>table.Pages.Any(item=>item.Source.Id==page.ToString())) is {} byPage&&byPage.Source.Id!=target.Source.Id)throw new InvalidOperationException("关联页面和表声明不一致。");
    }
    /// <summary>沿原逐键覆盖语义比较有效JSON；空覆盖、属性顺序及格式差异不改变读取计划。</summary>
    private static bool HasEffectiveOverride(string source,string overlay)
    {
        try
        {
            var original=JsonNode.Parse(string.IsNullOrWhiteSpace(source)?"{}":source) as JsonObject??throw new InvalidOperationException("关系定义必须是JSON对象。");
            var changes=JsonNode.Parse(overlay) as JsonObject??throw new InvalidOperationException("页级关系定义必须是JSON对象。");var effective=original.DeepClone().AsObject();
            foreach(var pair in changes)effective[pair.Key]=pair.Value?.DeepClone();return !JsonNode.DeepEquals(original,effective);
        }
        catch(Exception error)when(error is JsonException or ArgumentException){throw new InvalidOperationException("关系覆盖必须是有效无重复键的JSON对象。",error);}
    }
    /// <summary>没有页面的依赖表仍必须先验证物理结构；本入口不写任何生成文件。</summary>
    internal static void Validate(GenerationModel model,TableModel table)
    {try{foreach(var page in table.Pages)ForPage(model,table,page);}catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException){throw new InvalidOperationException("非冗余派生字段读取批次未完成："+error.Message,error);}}
}
