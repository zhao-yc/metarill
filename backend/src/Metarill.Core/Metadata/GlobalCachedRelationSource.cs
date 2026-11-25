using System.Globalization;
using System.Text.Json;
using Metarill.Core.Contracts;
namespace Metarill.Core.Metadata;

public sealed record GlobalCachedRelationBinding(ColumnMetadata Foreign,TableMetadata Target,PageMetadata Page,ColumnMetadata Name,ColumnMetadata Cache)
{
    /// <summary>保存和维护须执行实际发布页范围的名称副本。</summary>
    public bool ScopedSingle { get; init; }
}

/// <summary>原全局 R1 单值来源的发布证明。项目列需要对应的用途证明，页面仍校验真实身份与权限。</summary>
public static class GlobalCachedRelationSource
{
    /// <summary>必须显式全局、同一发布目录、真实主键且完全没有项目列；不能以缺字段自动取得全局许可。</summary>
    public static void Require(MetadataCatalog catalog,TableMetadata source)=>Require(catalog,source,allowsRowProject:false);

    /// <summary>自树缓存另允许真实的行项目列，普通全局 List 的发布范围沿用原约束。</summary>
    public static void RequireSelfTree(MetadataCatalog catalog,TableMetadata source)=>Require(catalog,source,allowsRowProject:true);

    /// <summary>全局发布身份与行归属分别验证，项目列只能使用原物理标量和两种 ID 类型。</summary>
    private static void Require(MetadataCatalog catalog,TableMetadata source,bool allowsRowProject)
    {
        if(!long.TryParse(catalog.YzProjectId,NumberStyles.None,CultureInfo.InvariantCulture,out var project)||project<=0||catalog.YzProjectId!=project.ToString(CultureInfo.InvariantCulture))throw Invalid();
        if(!GlobalCrossTableTarget.Declared(source)||source.IsRongyu!=1||source.IsView!=0||source.IsFunctionTable!=0||source.YzProjectId!=catalog.YzProjectId||!ReferenceEquals(catalog.ConfigTables.SingleOrDefault(table=>table.Id==source.Id),source))throw Invalid();
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id).ToArray();
        if(columns.Any(column=>column.YzProjectId.Length>0&&column.YzProjectId!=catalog.YzProjectId))throw Invalid();
        var storage=MetadataStoragePolicy.ForTable(catalog,source);var key=columns.SingleOrDefault(column=>column.ColumnName=="id");
        var rowProject=columns.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        if(rowProject is not null&&(!allowsRowProject||rowProject.LogicalType is not("int64" or "string")||
            !storage.IsPhysical(rowProject)||Joined(rowProject)||rowProject.IsVirtualColumn!=0))
            throw new NotSupportedException("全局R1来源的项目列需要受支持的关系用途及真实物理项目ID。");
        if(key is null||key.LogicalType!="int64"||key.IsDbNotNull!=1||!storage.IsPhysical(key)||Joined(key)||key.IsVirtualColumn!=0)throw Invalid();
        var deleted=columns.SingleOrDefault(column=>column.ColumnName=="delete_no");
        if(deleted is not null&&(deleted.LogicalType!="datetime"||deleted.IsDbNotNull!=0||!storage.IsPhysical(deleted)||Joined(deleted)||deleted.IsVirtualColumn!=0))throw Invalid();
        var pages=catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id).ToArray();
        if(pages.Length==0||pages.Any(page=>page.YzProjectId!=catalog.YzProjectId||!catalog.YzAdminTypes.Any(admin=>admin.Id==page.YzAdminTypeId&&admin.YzProjectId==catalog.YzProjectId)||page.Extra.TryGetValue("exactDataManageMatchField",out var exact)&&!string.IsNullOrWhiteSpace(exact.ToString())))throw Invalid();
    }
    /// <summary>带行项目列时，只允许自树页实际使用的唯一物理 List 父角色进入全局名称缓存。</summary>
    public static IReadOnlyList<PageMetadata> RowProjectSelfTreeParentPages(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        if(!GlobalCrossTableTarget.Declared(source)||source.IsRongyu!=1||foreign.ConfigTableId!=source.Id||
            foreign.ControlKind!="List"||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal))return [];
        var columns=catalog.ConfigColumns.Where(column=>column.ConfigTableId==source.Id).ToArray();
        if(!columns.Any(column=>column.ColumnName=="yz_project_id")||!columns.Any(column=>
            CascadeTreeStorageColumns.IsPhysicalSelfTreeKey(column)||CascadeTreeStorageColumns.DeclaresAlternateNodeKey(column)))return [];
        var fields=RelationStorageColumns.Expand(source,columns).Select(column=>column.ColumnName).ToArray();
        return catalog.ConfigPages.Where(page=>page.ConfigTableId==source.Id&&IsParent(page)).ToArray();

        // 和生成页使用同一个条件解析器，多个父变量不能借相同目标表合并成一个角色。
        bool IsParent(PageMetadata page)
        {
            var terms=FixedConditionPlan.Compile(page.WhereSql,fields).Terms.Where(term=>term.Values.Any(value=>
                value.Kind==FixedConditionValueKind.Variable&&MultiTargetParentScope.IsParameter(value.Text))).ToArray();
            return terms.Length==1&&terms[0].Field==foreign.ColumnName&&
                terms[0].Comparison==FixedConditionComparison.Equal&&terms[0].Values.Count==1;
        }
    }

    /// <summary>新增路径只接本表物理List单值键；R0、多值、级联及猜测目标继续使用既有门槛。</summary>
    public static void RequireSingle(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        Require(catalog,source,allowsRowProject:RowProjectSelfTreeParentPages(catalog,source,foreign).Count>0);
        if(foreign.ControlKind!="List")throw Invalid();
        RequirePhysicalForeign(catalog,source,foreign);
    }

    /// <summary>共同验证下拉和单选的物理来源。允许项目列的具体用途由实际目标页或自树父级继续证明。</summary>
    internal static void RequireCachedSingleSource(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        Require(catalog,source,allowsRowProject:true);
        if(foreign.ControlKind!="List"&&(foreign.ControlKind!="SelectPage"||foreign.LogicalType!="int64"))throw Invalid();
        RequirePhysicalForeign(catalog,source,foreign);
    }

    private static void RequirePhysicalForeign(MetadataCatalog catalog,TableMetadata source,ColumnMetadata foreign)
    {
        var storage=MetadataStoragePolicy.ForTable(catalog,source);
        if(!ReferenceEquals(catalog.ConfigColumns.SingleOrDefault(column=>column.Id==foreign.Id),foreign)||foreign.ConfigTableId!=source.Id||!foreign.ColumnName.EndsWith("_id",StringComparison.Ordinal)||foreign.LogicalType is not("int64" or "string")||!storage.IsPhysical(foreign)||foreign.IsVirtualColumn!=0||Joined(foreign))throw Invalid();
    }
    /// <summary>物理名称副本须绑定精确的单层路径、实际外键和已发布目标页。</summary>
    public static GlobalCachedRelationBinding RequireCache(MetadataCatalog catalog,TableMetadata source,ColumnMetadata cache)
        =>RequireCache(catalog,source,cache,new HashSet<string>(StringComparer.Ordinal));
    /// <summary>缓存被其它全局关系引用时复用调用者的证明栈，所有上游仍须通过相同目录和目标检查。</summary>
    internal static GlobalCachedRelationBinding RequireCache(MetadataCatalog catalog,TableMetadata source,ColumnMetadata cache,ISet<string> resolving)
    {
        // 先检查来源行，再用同一个目标解析证明原下拉、原单选或自树父角色。
        RequireSelfTree(catalog,source);var storage=MetadataStoragePolicy.ForTable(catalog,source);var path=cache.Extra.GetValueOrDefault("columnFullName").ToString().Split('.');
        if(!ReferenceEquals(catalog.ConfigColumns.SingleOrDefault(column=>column.Id==cache.Id),cache)||cache.ConfigTableId!=source.Id||path.Length!=2||!Joined(cache)||cache.IsVirtualColumn!=0||!storage.IsPhysical(cache)||(cache.LogicalType!="string"&&!MultiValueLabel.IsTyped(cache.LogicalType))||cache.ControlKind is not("" or "TextArea")||cache.LinkConfigTableId.Length>0||cache.Extra.GetValueOrDefault("joinTable").ToString().Length>0||cache.Extra.GetValueOrDefault("linkConfigPageId").ToString().Length>0)throw Invalid();
        var foreign=catalog.ConfigColumns.SingleOrDefault(column=>column.ConfigTableId==source.Id&&(column.ColumnName==path[0]+"_id"||column.ColumnName==path[0]));
        if(foreign is null)throw Invalid();
        RequireCachedSingleSource(catalog,source,foreign);
        var target=GlobalSingleRelationTarget.ResolveTarget(catalog,foreign);
        var proof=GlobalCrossTableTarget.Declared(target)
            ?GlobalSingleRelationTarget.RequireCachedValue(catalog,source,foreign,target,resolving)
            :GlobalSingleRelationTarget.RequireProjectParent(catalog,source,foreign,target);
        var stem=foreign.ColumnName[..^3];
        // 同类型副本由写入和回填共同使用，保持业务值类型与精度。
        if(path[1]!=proof.Name.ColumnName||cache.ColumnName!=stem+"_"+proof.Name.ColumnName||cache.LogicalType!=proof.Name.LogicalType)throw Invalid();
        return new(foreign,target,proof.Page,proof.Name,cache){ScopedSingle=proof.RequiresScopedCache};
    }
    /// <summary>与现有元数据标志一致，只承认1/true。</summary>
    private static bool Joined(ColumnMetadata column)=>column.Extra.TryGetValue("isJoinColumn",out var value)&&(value.ValueKind==JsonValueKind.True||value.ToString()=="1");
    /// <summary>不能降级为无发布证明的全局来源。</summary>
    private static NotSupportedException Invalid()=>new("无项目列的全局缓存来源必须为当前目录明确发布、有真实主键和后台页面的R1物理List来源。");
}
