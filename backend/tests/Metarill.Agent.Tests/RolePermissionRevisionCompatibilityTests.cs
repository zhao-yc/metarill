using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Metarill.Runtime.Persistence;
using Metarill.Runtime.Security;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>共享核心的revision与平台原完整JSON预像逐字兼容，不用新的瘦DTO或格式替换历史草稿。</summary>
public sealed class RolePermissionRevisionCompatibilityTests
{
    /// <summary>保留完整目录、长ID、日期、null、扩展字典顺序及非ASCII，然后执行实际私有revision方法。</summary>
    [Fact]
    public void Revision_bytes_and_hash_match_the_original_full_catalog_json_contract()
    {
        const string project="2300000000000000001",admin="2300000000000000002";var timestamp=new DateTime(2024,2,29,13,14,15,789,DateTimeKind.Utc);
        var page=new PageMetadata{Id="2300000000000000009",Name="角色页：中文🎈",ConfigTableId="1692532858499969026",YzProjectId=project,YzAdminTypeId=admin,IsNew=1,IsSave=1};
        page.Extra["zzExtra"]=JsonSerializer.SerializeToElement(new Dictionary<string,object?>{{"乙",null},{"甲","非ASCII与\n换行"}});page.Extra["aaExtra"]=JsonSerializer.SerializeToElement(timestamp,JsonDefaults.Options);
        var menus=new[]{new MenuMetadata{Id="2300000000000000012",Name="末菜单",Kind="DIRECTORY",YzProjectId=project,YzAdminTypeId=admin},new MenuMetadata{Id="2300000000000000011",Name="首菜单",Kind="GENERATED_PAGE",YzProjectId=project,YzAdminTypeId=admin,ConfigPageId=page.Id}};
        var buttons=new[]{JsonSerializer.SerializeToElement(new Dictionary<string,object?>{{"id","2300000000000000014"},{"configPageId",page.Id},{"name","按钮中文"},{"extra",null}})};
        IReadOnlySet<string> tables=new HashSet<string>{"1692532858499969026","100"};var role=new Dictionary<string,object?>{{"id",9007199254740993L},{"yz_admin_type_id",long.Parse(admin)},{"is_super",0},{"is_scope",null}};
        var rows=new[]{new Dictionary<string,object?>{{"z",null},{"id",9007199254740995L},{"date",timestamp},{"a","中文授权"},{"permission_values","[\"is_query\"]"}},new Dictionary<string,object?>{{"id",9007199254740994L},{"date",null},{"a","先排此行"}}};
        var expected=new{role=new{id=SqlRowStore.Text(role,"id"),adminTypeId=admin,isSuper="0",isScope=""},catalog=new{roleTableIds=tables.Order(StringComparer.Ordinal),menus=menus.OrderBy(menu=>menu.Id,StringComparer.Ordinal),pages=new[]{page}.OrderBy(value=>value.Id,StringComparer.Ordinal),buttons=buttons.OrderBy(value=>value.GetProperty("id").ToString(),StringComparer.Ordinal)},rows=rows.OrderBy(row=>SqlRowStore.Id(row)).Select(row=>row.OrderBy(field=>field.Key,StringComparer.Ordinal).ToDictionary(field=>field.Key,field=>field.Value,StringComparer.Ordinal))};
        var legacy=JsonSerializer.Serialize(expected,JsonDefaults.Options);var assembly=typeof(RolePermissionBusiness).Assembly;var serializer=(JsonSerializerOptions)assembly.GetType("Metarill.Runtime.Security.RolePermissionRevisionJson",true)!.GetProperty("Options",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.Equal(legacy,JsonSerializer.Serialize(expected,serializer));Assert.Contains("2024-02-29 13:14:15",legacy);Assert.Contains("\"9007199254740995\"",legacy);Assert.Contains("null",legacy);
        var catalogType=typeof(RolePermissionBusiness).GetNestedType("RealmCatalog",BindingFlags.NonPublic)!;var catalog=Activator.CreateInstance(catalogType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[menus,new[]{page},buttons,tables],null)!;
        var revision=(string)typeof(RolePermissionBusiness).GetMethod("Revision",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[role,catalog,rows])!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(legacy))),revision);
        // 原JSON扩展字段顺序也是既有预像事实，提取不能新排序或只保留显示名称。
        var oldOrder=page.Extra.Keys.ToArray();var reversed=page.Extra.Reverse().ToArray();page.Extra.Clear();foreach(var pair in reversed)page.Extra[pair.Key]=pair.Value;
        Assert.NotEqual(oldOrder,page.Extra.Keys);Assert.NotEqual(legacy,JsonSerializer.Serialize(expected,JsonDefaults.Options));
        Assert.Equal(JsonSerializer.Serialize(expected,JsonDefaults.Options),JsonSerializer.Serialize(expected,serializer));
    }
}
