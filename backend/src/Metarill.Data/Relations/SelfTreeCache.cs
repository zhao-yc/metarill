using System.Text.Json;
using Metarill.Core.Contracts;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Data.Queries;
namespace Metarill.Data.Relations;

/// <summary>R1自树只缓存真实叶子名称；固定控制定义同时约束传播及维护，路径展示仍由原树读取器完成。</summary>
public static class SelfTreeCache
{
    /// <summary>编译普通自树的固定缓存定义，维持原三参数入口。</summary>
    public static RelationCacheDefinition Compile(MetadataCatalog catalog,string sourceTableId,string controlId)
        =>Compile(catalog,sourceTableId,controlId,null);

    /// <summary>实际自树页包含已声明级联条件时，整条路径参与传播。</summary>
    public static bool HasScopedPage(MetadataCatalog catalog,string? pageId)=>HasScopedPage(catalog,pageId,onlySelfTree:false);

    /// <summary>单独识别新式叶名条件，以便发布时验证整链 SQL 与所有账属列。</summary>
    internal static bool HasSelfTreeScopedPage(MetadataCatalog catalog,string? pageId)=>HasScopedPage(catalog,pageId,onlySelfTree:true);

    private static bool HasScopedPage(MetadataCatalog catalog,string? pageId,bool onlySelfTree)
    {
        if(pageId is null)return false;
        var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==pageId)??throw Invalid();
        var table=catalog.ConfigTables.Single(item=>item.Id==page.ConfigTableId);
        var fields=RelationStorageColumns.Expand(table,catalog.ConfigColumns.Where(column=>column.ConfigTableId==table.Id));
        return FixedConditionPlan.Compile(page.WhereSql,fields.Select(column=>column.ColumnName)).Fields
            .Any(field=>CascadeTreeStorageColumns.IsLogicalNameField(table,fields,field)||
                !onlySelfTree&&CrossTableStorageColumns.IsLogicalPathField(table,fields,field));
    }

    /// <summary>显式全局来源固定使用整条祖先链；普通来源兼容原有叶名路径。</summary>
    public static bool UsesWholePath(MetadataCatalog catalog,TableMetadata source,string? pageId)=>
        pageId is not null&&(GlobalCrossTableTarget.Declared(source)||HasScopedPage(catalog,pageId));

    /// <summary>核对已发布页和物理归属；维护从来源行确定目标范围。</summary>
    public static RelationCacheDefinition Compile(MetadataCatalog catalog,string sourceTableId,string controlId,string? publishedPageId)
    {
        var source=catalog.ConfigTables.Single(t=>t.Id==sourceTableId);var sourceColumns=RelationStorageColumns.Expand(source,catalog.ConfigColumns.Where(c=>c.ConfigTableId==source.Id));var foreign=sourceColumns.Single(c=>c.Id==controlId);var sourceStorage=MetadataStoragePolicy.ForTable(catalog,source);
        if(source.IsRongyu!=1||source.IsView!=0||source.IsFunctionTable!=0||foreign.ControlKind!="CascadeTree"||foreign.LogicalType is not("string" or "int64")||!Scalar(sourceStorage,foreign)||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal))throw Invalid();
        using var json=JsonDocument.Parse(foreign.ControlDetail);var root=json.RootElement;Object(root);if(Text(root,"sourceMode","")!="selfTree"||!root.TryGetProperty("selfTree",out var tree))throw Invalid();Object(tree);
        if(Extra(root)||Extra(tree)||Text(tree,"rootMode","blank")!="blank"||Text(tree,"filterMatchMode","selfAndDescendants") is not("exact" or "selfAndDescendants"))throw Invalid();
        var target=catalog.ConfigTables.Single(t=>t.TableName==Text(tree,"table",""));if(target.IsRongyu is not(0 or 1)||target.IsView!=0||target.IsFunctionTable!=0||foreign.LinkConfigTableId.Length>0&&foreign.LinkConfigTableId!=target.Id)throw Invalid();
        var columns=RelationStorageColumns.Expand(target,catalog.ConfigColumns.Where(c=>c.ConfigTableId==target.Id));var storage=MetadataStoragePolicy.ForTable(catalog,target);var value=columns.Single(c=>c.ColumnName==Text(tree,"nameField","name"));var parent=columns.Single(c=>c.ColumnName==Text(tree,"parentField",""));
        var derived=!storage.IsPhysical(value);
        var typedCache=GlobalPhysicalRelationValue.IsPublishedTypedCache(catalog,storage,target,value);
        if(value.Id==parent.Id||value.ColumnName=="id"||value.LogicalType!="string"&&!MultiValueLabel.IsTyped(value.LogicalType)||(!derived&&!typedCache&&!Scalar(storage,value))||derived&&(target.IsRongyu!=0||value.IsVirtualColumn!=0||storage.Kind(value)!=MetadataFieldStorage.Join)||parent.LogicalType is not("string" or "int64")||!Scalar(storage,parent)||parent.ColumnName is "id" or "yz_project_id" or "insert_manager_id" or "update_manager_id"||parent.LinkConfigTableId.Length>0&&parent.LinkConfigTableId!=target.Id)throw Invalid();
        // 派生叶子须由同一匹配编译器证明，缓存一律使用原名称文本。
        var global=GlobalCrossTableTarget.Declared(target);
        var globalSource=GlobalCrossTableTarget.Declared(source);
        if(globalSource)
        {
            GlobalCachedRelationSource.RequireSelfTree(catalog,source);
            // 普通目标只供处于相同发布项目的真实项目行使用，共享来源不能保存项目私有叶子。
            if(!global&&(!sourceColumns.Any(column=>column.ColumnName=="yz_project_id")||
                target.YzProjectId!=catalog.YzProjectId||
                !columns.Any(column=>column.ColumnName=="yz_project_id"&&column.LogicalType is "int64" or "string"&&
                    GlobalPhysicalRelationValue.IsScalar(storage,target,column))))throw Invalid();
        }
        var scoped=HasScopedPage(catalog,publishedPageId);
        var keyName=Text(tree,"idField","id");
        if(keyName!="id")
        {
            var key=CascadeTreeStorageColumns.ResolveNodeKey(catalog,target,keyName).Key;
            if(global||globalSource||derived||scoped||key.Id==parent.Id||key.Id==value.Id)throw Invalid();
            if(publishedPageId is not null)
            {
                var page=catalog.ConfigPages.SingleOrDefault(item=>item.Id==publishedPageId&&item.ConfigTableId==target.Id)??throw Invalid();
                _=MySqlPublishedRelationCondition.Compile(catalog,target,page,projectParent:true);
            }
        }
        if(global||scoped||globalSource||derived&&publishedPageId is not null)
        {
            var page=catalog.ConfigPages.SingleOrDefault(p=>p.Id==publishedPageId&&p.ConfigTableId==target.Id)??throw Invalid();
            if(source.YzProjectId!=catalog.YzProjectId||page.YzProjectId!=catalog.YzProjectId||
                !catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId))throw Invalid();
            if(scoped||globalSource||derived&&!global)
            {
                var selected=MySqlNonRedundantReadCompiler.CascadeLabelPage(catalog,target,value.ColumnName,parent.ColumnName,allowSelfTree:true);
                if(selected.Page.Id!=page.Id)throw Invalid();
                if(!scoped&&globalSource&&selected.Condition?.Variables.Count>0)
                    _=AccountScopedCrossCache.Compile(catalog,source,target,page,globalTreeSource:globalSource);
            }
            if(scoped)
            {
                var conditionFields=FixedConditionPlan.Compile(page.WhereSql,columns.Select(column=>column.ColumnName)).Fields;
                var treeNames=MySqlNonRedundantReadCompiler.SelfTreeConditionFields(catalog,target,page,columns,conditionFields);
                _=AccountScopedCrossCache.CompileRecursive(catalog,source,target,page,globalTreeSource:globalSource,selfTreeFields:treeNames);
                if(global)GlobalCrossTableTarget.RequireScopedSelfTreeName(catalog,target,page,value);
            }
            else if(global)GlobalCrossTableTarget.RequireSelfTreeName(catalog,target,page,value);
        }
        else if(publishedPageId is not null&&keyName=="id")throw Invalid();
        if(!global&&derived)
        {
            var labelType=publishedPageId is null
                ?NonRedundantReads.CompileMatch(catalog,target.Id,value.ColumnName).OutputTypes[value.ColumnName]
                :NonRedundantReads.CompilePublishedScalarMatch(catalog,target.Id,publishedPageId,value.ColumnName).OutputTypes[value.ColumnName];
            if(labelType!=value.LogicalType)throw Invalid();
        }
        var name=foreign.ColumnName[..^3]+"_name";var cache=sourceColumns.Single(c=>c.ColumnName==name);if(cache.Id==foreign.Id||cache.LogicalType!="string"||cache.IsVirtualColumn!=0||!sourceStorage.IsPhysical(cache)||cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0)throw Invalid();
        if(Enabled(cache)){var path=cache.Extra.GetValueOrDefault("columnFullName").ToString();if(path.Length>0&&path!=foreign.ColumnName[..^3]+"."+value.ColumnName&&path!=foreign.ColumnName+"."+value.ColumnName)throw Invalid();}
        return new(source.Id,foreign.Id,target.Id,value.Id,cache.Id,false){SelfTreePageId=publishedPageId};
    }
    /// <summary>普通关系不变；只有明确selfTree声明可以采用固定_name而非目标字段后缀。</summary>
    internal static bool Require(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign,TableMetadata target,ColumnMetadata value,ColumnMetadata cache,RelationCacheDefinition definition,bool cross)
    {
        if(cross||definition.Multiple||foreign.ControlKind!="CascadeTree")
        {
            if(definition.SelfTreePageId is not null)throw Invalid();
            return false;
        }
        using var json=JsonDocument.Parse(foreign.ControlDetail);Object(json.RootElement);
        if(Text(json.RootElement,"sourceMode","crossTable")!="selfTree")
        {
            if(definition.SelfTreePageId is not null)throw Invalid();
            return false;
        }
        var proof=Compile(catalog,source.Id,foreign.Id,definition.SelfTreePageId);
        if(proof.TargetTableId!=target.Id||proof.ValueColumnId!=value.Id||proof.CacheColumnId!=cache.Id)throw Invalid();
        return true;
    }
    /// <summary>已证明的自树缓存读取与传播共用同一业务键；旧主键关系使用默认键。</summary>
    internal static string? AlternateKey(MetadataCatalog catalog,TableMetadata target,ColumnMetadata foreign)
    {
        using var json=JsonDocument.Parse(foreign.ControlDetail);
        var name=Text(json.RootElement.GetProperty("selfTree"),"idField","id");
        return name=="id"?null:CascadeTreeStorageColumns.ResolveNodeKey(catalog,target,name).Key.ColumnName;
    }

    /// <summary>结构、ID及名称必须物理且非凭据；数组和联接不充当树拓扑。</summary>
    private static bool Scalar(MetadataStoragePlan storage,ColumnMetadata column)=>storage.IsPhysical(column)&&column.IsVirtualColumn==0&&!Enabled(column)&&!ExactDataManageValues.IsSensitiveField(column.ColumnName)&&column.ControlKind is not("FileLibrary" or "MultiSelectPage" or "MultiCheckBoxDatabase" or "MultiList" or "CheckBoxList");
    /// <summary>固定定义保留排序与外观配置，不忽略附加查询或在线页映射。</summary>
    private static bool Extra(JsonElement value)=>new[]{"whereSql","where","sql","pageId"}.Any(k=>value.TryGetProperty(k,out _));
    /// <summary>重复键不能由JSON后值覆盖改变目标或字段。</summary>
    private static void Object(JsonElement value){if(value.ValueKind!=JsonValueKind.Object||value.EnumerateObject().Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=value.EnumerateObject().Count())throw Invalid();}
    /// <summary>只读取文本，拒绝把数值或对象隐式解释为标识符。</summary>
    private static string Text(JsonElement value,string key,string fallback)=>!value.TryGetProperty(key,out var item)||item.ValueKind==JsonValueKind.Null?fallback:item.ValueKind==JsonValueKind.String?string.IsNullOrWhiteSpace(item.GetString())?fallback:item.GetString()!:throw Invalid();
    /// <summary>联接标记沿既有true／1合同。</summary>
    private static bool Enabled(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
    /// <summary>不将未知定义降级为普通外键。</summary>
    private static NotSupportedException Invalid()=>new("自树叶子名称缓存尚未取得固定物理字段证明。");
}
