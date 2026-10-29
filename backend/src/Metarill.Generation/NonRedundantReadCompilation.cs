using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;

namespace Metarill.Generation;

internal sealed record NonRedundantOutput(string Storage,string Property,string JsonName,string Type,bool IsJsonArray=false);
internal sealed record NonRedundantPagePlan(IReadOnlyList<NonRedundantOutput> Outputs,IReadOnlyList<string> Dependencies,IReadOnlyList<string> ReadOutputs);

/// <summary>生成侧提前证明单值／固定多选用途和物理写边界；数据库表达式由数据适配器编译。</summary>
internal static class NonRedundantReadCompilation
{
    /// <summary>没有派生关系的原纯物理非冗余页继续使用既有强类型路径。</summary>
    internal static bool Requires(GenerationModel model,TableModel table)=>table.Source.IsRongyu==0&&model.Catalog.ConfigColumns.Any(column=>column.ConfigTableId==table.Source.Id&&column.IsVirtualColumn==0&&(!MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column)||MetadataStoragePolicy.IsRelationColumn(column)));
    /// <summary>所有实体写入复用同一存储计划，不把读取派生属性持久化。</summary>
    internal static bool Physical(GenerationModel model,TableModel table,ColumnModel column)=>MetadataStoragePolicy.ForTable(model.Catalog,table.Source).IsPhysical(column.Source);
    /// <summary>只编译已经接通的标量用途；完整包还须通过全部控件、范围及独立宿主验证。</summary>
    internal static NonRedundantPagePlan ForPage(GenerationModel model,TableModel table,PageModel page)
    {
        if(table.IsGlobal)throw new InvalidOperationException("非冗余全局读取仍待完整接线。");
        // 导入沿用标准Service写事务，仅开放本页真实物理可写列，派生读取不参与覆盖匹配。
        if(ImportRenderer.Enabled(page)&&ImportRenderer.Columns(page).Any(column=>!Physical(model,table,column)))throw new InvalidOperationException("非冗余导入只能写入物理列。");
        // 物理文件ID数组沿原FileLibrary事务写入；其它多值、派生文件及布局仍须独立编译。
        if(table.Columns.Any(column=>column.Source.ControlKind is "Title" or "Tab" || column.IsJsonArray && (column.Source.ControlKind is not ("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase") && !column.IsManagedMultiNames || column.Source.ControlKind=="FileLibrary"&&!Physical(model,table,column))))throw new InvalidOperationException("非冗余多值、级联、布局或派生文件读取仍待接线。");
        if(page.Searchable.Any(column=>column.Source.LogicalType is not("string" or "int64" or "int32" or "decimal" or "bool" or "datetime" or "date" or "time" or "binary")))throw new InvalidOperationException("非冗余筛选字段类型尚未接线。");
        if(page.Scopes.Any(scope=>!Physical(model,table,scope.Column)))throw new InvalidOperationException("非冗余父范围必须绑定物理外键。");
        if(ManagerCredentialCompilation.IsTable(table)||FixedRoleCheckboxCompilation.ForPage(model,table,page) is not null||FixedRoleAdminNameCompilation.ForPage(model,table,page) is not null||FixedOptionCatalogCompilation.ForPage(model,page).Count!=0)throw new InvalidOperationException("非冗余固定安全模块及目录读取尚需独立验收。");
        foreach(var checkbox in table.Columns.Where(column=>column.Source.ControlKind=="MultiCheckBoxDatabase"))
            if(DatabaseCheckboxCompilation.Resolve(model,table,page,checkbox).Definition is null)throw new InvalidOperationException("非冗余数据库复选缺少已证明的字段与候选范围。");
        foreach(var multi in table.Columns.Where(column=>column.Source.ControlKind=="MultiSelectPage"))
            if(MultiSelectCompilation.Resolve(model,table,page,multi,forService:true).Definition is null)throw new InvalidOperationException("非冗余多选缺少已证明的字段与候选范围。");
        foreach(var tree in table.Columns.Where(column=>column.Source.ControlKind=="CascadeTree"))
        {
            if(CrossTableWriteCompilation.Resolve(model,table,page,tree).Definition is{} cross){NonRedundantCrossTableCompilation.Require(model,table,page,cross.Read);continue;}
            if(CascadeTreeWriteCompilation.Resolve(model,table,page,tree).Definition is not{} plan||plan.Read.Target.IsGlobal)throw new InvalidOperationException("非冗余级联要求已证明的同项目selfTree读写计划。");
            var managed=CascadeTreeViewRenderer.Name(table,tree);
            if(managed is not null&&(page.Searchable.Concat(page.Sortable).Any(column=>column.Source.Id==managed.Source.Id)||page.Condition?.Syntax.Fields.Contains(managed.Source.ColumnName)==true))throw new InvalidOperationException("非冗余级联名称直接筛选/排序尚待专用读取用途验证。");
        }
        var crossIds=CrossTableWriteCompilation.ForService(model,table,page).SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var relation in table.Columns.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is "CascadeTree" or "MultiCheckBoxDatabase"||crossIds.Contains(relation.Source.Id))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联写入缺少已接通的明确目标。");ValidateTargetDeclarations(model,relation,target);
            if(target.IsGlobal){if(relation.Source.ControlKind=="MultiSelectPage")_=GlobalMultipleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);else _=GlobalSingleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);}
        }
        foreach(var selection in table.Columns.Where(column=>column.Source.ControlKind=="SelectPage"))NonRedundantSelectionCompilation.Resolve(model,table,page,selection);
        var outputs=page.Readable.Select(column=>new NonRedundantOutput(column.Source.ColumnName,column.Property,column.JsonName,column.Type,column.IsJsonArray)).ToList();
        foreach(var name in CascadeTreeViewRenderer.ImplicitNames(model,table,page))outputs.Add(new(name.Source.ColumnName,name.Property,name.JsonName,name.Type));
        foreach(var multi in MultiSelectCompilation.ForPage(model,table,page).Where(multi=>multi.Names is not null))
            if(!outputs.Any(output=>output.Storage==multi.Names!.Source.ColumnName))outputs.Add(new(multi.Names!.Source.ColumnName,multi.Names.Property,multi.Names.JsonName,"string?",true));
        foreach(var checkbox in DatabaseCheckboxCompilation.ForPage(model,table,page).Where(def=>page.Readable.Any(field=>field.Source.Id==def.Source.Source.Id)))
            if(!outputs.Any(output=>output.Storage==checkbox.Names.Source.ColumnName))outputs.Add(new(checkbox.Names.Source.ColumnName,checkbox.Names.Property,checkbox.Names.JsonName,"string?",true));
        foreach(var relation in page.Readable.Where(column=>MetadataStoragePolicy.IsRelationColumn(column.Source)))
        {
            if(relation.Source.ControlKind is not("List" or "SelectPage"))continue;
            var pageField=model.Catalog.ConfigPageColumns.SingleOrDefault(field=>field.ConfigPageId==page.Source.Id&&field.ConfigColumnId==relation.Source.Id);
            if(relation.Source.ControlKind!="SelectPage"&&pageField?.Extra.TryGetValue("controlDetail",out var detail)==true&&!string.IsNullOrWhiteSpace(detail.ToString())&&HasEffectiveOverride(relation.Source.ControlDetail,detail.ToString())&&!ProvenConditionOverride(model,page,relation,detail.ToString()))throw new InvalidOperationException("非冗余页级关系控件覆盖尚需接线。");
            var nameField="name";if(!string.IsNullOrWhiteSpace(relation.Source.ControlDetail)){using var document=JsonDocument.Parse(relation.Source.ControlDetail);if(document.RootElement.TryGetProperty("nameField",out var name))nameField=name.GetString()!;}
            if(nameField=="id"||!relation.Source.ColumnName.EndsWith("_id",StringComparison.Ordinal))continue;
            var target=ForeignValidationRenderer.Target(model,relation)??throw new InvalidOperationException("关联目标缺少已接通的固定候选来源。");
            ValidateTargetDeclarations(model,relation,target);
            var display=target.Columns.SingleOrDefault(column=>column.Source.ColumnName==nameField);
            if(display is null)throw new InvalidOperationException("未知名称候选仍待完整接线。");
            if(target.IsGlobal)_=GlobalSingleRelationTarget.Require(model.Catalog,table.Source,relation.Source,target.Source);
            if(!Physical(model,target,display))
            {
                var candidate=RelationCompilation.ForPage(model,page).SingleOrDefault(candidate=>candidate.SourceColumn.Source.Id==relation.Source.Id)??throw new InvalidOperationException("派生名称缺少固定候选关系。");
                _=DerivedRelationNamesCompilation.Require(model,table,page,candidate);
            }
            var alias=relation.Source.ColumnName[..^3]+"_"+nameField;MetadataValidation.RequireIdentifier(alias);
            if(!outputs.Any(output=>output.Storage.Equals(alias,StringComparison.OrdinalIgnoreCase)))outputs.Add(new(alias,GenerationModel.Pascal(alias),GenerationModel.Camel(alias),""));
        }
        var selectionNames=SelectionNameFilterCompilation.ForPage(model,table,page);
        var dependencies=page.Searchable.Concat(page.Sortable).Concat(ExactDataManageCompilation.Resolve(model,table,page) is {} exact ? new[]{exact.Column} : Array.Empty<ColumnModel>()).Concat(FixedPageQueryCompilation.For(table,page).IdentityScopes.Select(scope=>scope.Column)).Concat(FixedPageQueryCompilation.For(table,page).DefaultOrder.Select(item=>item.Column)).Select(column=>column.Source.ColumnName).Concat(selectionNames.Bindings.Select(binding=>binding.StorageColumn.Source.ColumnName)).Concat(page.Condition?.Syntax.Fields??Array.Empty<string>()).Concat(CrossTableFilterCompilation.ForPage(model,table,page).SelectMany(plan=>plan.Levels).Select(level=>level.Id.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // 默认排序、隐藏范围、精确匹配及跨表各级筛选均是读取依赖；窄导出也不能漏掉祖先ID表达式。
        if(CascadeTreeWriteCompilation.ManagedNames(table).Any(name=>dependencies.Contains(name.Source.ColumnName,StringComparer.OrdinalIgnoreCase)))throw new InvalidOperationException("非冗余级联名称的筛选/排序/匹配用途尚待完整路径计划验证。");
        // 图只取来源已授权行的所选ID；隐藏ID为私有投影依赖，不加入VO或页面字段。
        var readOutputs=outputs.Select(output=>output.Storage).Concat(CascadeTreeViewRenderer.ForPage(model,table,page).Select(plan=>plan.Source.Source.ColumnName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var compiled=NonRedundantReads.CompileWithCrossTables(model.Catalog,table.Source.Id,readOutputs,NonRedundantCrossTableCompilation.Bindings(model,table,page),dependencies,deferCascadeNames:true);
        outputs=outputs.Select(output=>output.Type.Length>0?output:output with{Type=compiled.OutputTypes[output.Storage] switch{"string"=>"string?","int64"=>"long?","int32"=>"int?","decimal"=>"decimal?","bool"=>"bool?","datetime" or "date"=>"DateTime?","time"=>"TimeSpan?",_=>throw new InvalidOperationException("自动显示字段类型尚未接线。")}}).ToList();
        if(outputs.Select(output=>output.Property).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=outputs.Count)throw new InvalidOperationException("派生显示属性冲突。");
        return new(outputs,dependencies,readOutputs);
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
    /// <summary>仅放开完整候选计划已接通的where覆盖；目标、名称及其它定义不变，自动显示读取仍沿原来源。</summary>
    private static bool ProvenConditionOverride(GenerationModel model,PageModel page,ColumnModel source,string overlay)
    {
        var original=JsonNode.Parse(string.IsNullOrWhiteSpace(source.Source.ControlDetail)?"{}":source.Source.ControlDetail) as JsonObject??throw new InvalidOperationException("关系定义必须是JSON对象。");
        var changes=JsonNode.Parse(overlay) as JsonObject??throw new InvalidOperationException("页级关系定义必须是JSON对象。");var effective=original.DeepClone().AsObject();
        foreach(var pair in changes)effective[pair.Key]=pair.Value?.DeepClone();
        original.Remove("whereSql");effective.Remove("whereSql");
        if(!JsonNode.DeepEquals(original,effective))return false;
        var compiled=RelationCompilation.ForPage(model,page).SingleOrDefault(relation=>relation.SourceColumn.Source.Id==source.Source.Id);
        return compiled?.Conditions is not null&&compiled.Target.Source.Id==ForeignValidationRenderer.Target(model,source)?.Source.Id;
    }
    /// <summary>没有页面的依赖表仍必须先验证物理结构；本入口不写任何生成文件。</summary>
    internal static void Validate(GenerationModel model,TableModel table)
    {try{foreach(var page in table.Pages)ForPage(model,table,page);}catch(Exception error)when(error is InvalidOperationException or NotSupportedException or JsonException){throw new InvalidOperationException("非冗余派生字段读取批次未完成："+error.Message,error);}}
}
