namespace Metarill.Core.Metadata;

/// <summary>原账号角色复选在配置保存和生成时共用的固定元数据归属。</summary>
public static class PlatformRoleCheckboxBinding
{
    public const string ManagerTable = "6031906809708544", ManagerPage = "6031906822946816", RoleColumn = "1692528723801219073";
    public const string RoleTable = "1692532858499969026", RolePage = "1692532858529329154", AdminTable = "1696799308939239425";

    /// <summary>平台安全表不使用普通业务复选候选。</summary>
    public static bool IsProtectedTable(TableMetadata table) =>
        table.Id is ManagerTable or RoleTable or AdminTable || table.TableName is "yz_manager" or "yz_role" or "yz_admin_type";
}
