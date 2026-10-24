using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using ClosedXML.Excel;
using Metarill.Core.Contracts;
using Metarill.Runtime.Metadata;
using Xunit;

namespace Metarill.Agent.Tests;

/// <summary>只使用Server200固定三表读取模型与自有权限/会话随机样本；本组源码先离线编译，真实MySQL另由主协调者独占执行。</summary>
[Collection(SecurityRealmRrCollection.Name)]
public sealed class FixedRoleReadUseMySqlTests
{
    private const string Manager=FixedRoleReadUseMySqlFixture.ManagerPage,Role=FixedRoleReadUseMySqlFixture.RolePage;

    /// <summary>manager只获来源查询权限；确认保持长ID、首次选择顺序、内部重复和缺名位置，不读取凭据。</summary>
    [AgentIntegrationFact]
    public async Task Manager_selection_has_real_scopes_names_and_source_query_authorization()
    {
        await using var f=await FixedRoleReadUseMySqlFixture.CreateAsync("manager-selection");var identity=await f.Identity();
        Assert.False(identity.Permissions.Contains(Role+":is_query"));Assert.False(identity.Permissions.Contains(Manager+":is_export"));Assert.True(f.Keys["active-role"]>9007199254740991L,"角色ID必须覆盖JavaScript安全整数边界。");
        f.Commands.Clear();var data=await f.Post("yz_manager",Manager,"selection/selected",new{selectedIds=f.Selected("empty-account","array-account","array-account","null-account")});
        Assert.Equal(f.Selected("empty-account","array-account","null-account"),data.EnumerateArray().Select(row=>row.GetProperty("id").GetString()));
        var ids=data[1].GetProperty("yzRoleIds").EnumerateArray().Select(value=>value.GetString()).ToArray();Assert.Equal(JsonSerializer.Deserialize<string[]>(f.RoleJson),ids);
        var names=data[1].GetProperty("yzRoleNames").EnumerateArray().ToArray();Assert.Equal(7,names.Length);Assert.Equal(f.DisplayName("active-role"),names[0].GetString());Assert.Equal(f.DisplayName("active-role"),names[2].GetString());foreach(var i in new[]{1,3,4,5,6})Assert.Equal(JsonValueKind.Null,names[i].ValueKind);
        Assert.Equal(0,data[0].GetProperty("yzRoleNames").GetArrayLength());Assert.Equal(JsonValueKind.Null,data[2].GetProperty("yzRoleNames").ValueKind);
        foreach(var row in data.EnumerateArray())foreach(var key in new[]{"password","salt","oldPassword"})Assert.False(row.TryGetProperty(key,out _),"selection不能输出凭据或掩码。");
        Assert.Equal(2,f.Commands.Count);AssertSecretFreeSql(f.Commands[0].Sql);var nameSql=f.Commands[1];Assert.Contains("EXISTS",nameSql.Sql,StringComparison.OrdinalIgnoreCase);Assert.Contains("yz_admin_type",nameSql.Sql);Assert.Contains("yz_project_id",nameSql.Sql);Assert.Contains("delete_no",nameSql.Sql);Assert.True(nameSql.Parameters>0,"实际名称SQL必须绑定参数。");
        foreach(var tag in new[]{"foreign-account","deleted-account"}){f.Commands.Clear();await f.Post("yz_manager",Manager,"selection/selected",new{selectedIds=f.Selected(tag)},HttpStatusCode.BadRequest);Assert.Single(f.Commands);}
        f.Commands.Clear();await f.Post("yz_manager",Manager,"selection/selected",new{selectedIds=f.Selected("array-account")},HttpStatusCode.Forbidden,authenticated:false);Assert.True(f.Commands.Count==0,"无真实登录不得执行源查询。");
        await f.Post("yz_manager",Manager,"initExport",new PageRequest{SelectedIds=f.Selected("array-account")},HttpStatusCode.Forbidden);Assert.True(f.Commands.Count==0,"来源query不得代替export。");
        await f.AssertOwnedUnchanged();await f.Complete();
    }

    /// <summary>role选择回执只获来源query；同项目后台名可见，外项目、软删和缺失后台不泄出名称。</summary>
    [AgentIntegrationFact]
    public async Task Role_selection_projects_only_current_project_active_admin_names()
    {
        await using var f=await FixedRoleReadUseMySqlFixture.CreateAsync("role-selection");var identity=await f.Identity();Assert.False(identity.Permissions.Contains(FixedRoleReadUseMySqlFixture.AdminPage+":is_query"));
        var selected=f.Selected("missing-admin-role","active-role","foreign-role","active-role","deleted-admin-role");f.Commands.Clear();
        var data=await f.Post("yz_role",Role,"selection/selected",new{selectedIds=selected});Assert.Equal(f.Selected("missing-admin-role","active-role","foreign-role","deleted-admin-role"),data.EnumerateArray().Select(row=>row.GetProperty("id").GetString()));
        Assert.Equal(f.DisplayName("active-admin"),data[1].GetProperty("yzAdminTypeName").GetString());foreach(var i in new[]{0,2,3})Assert.Equal(JsonValueKind.Null,data[i].GetProperty("yzAdminTypeName").ValueKind);Assert.Equal(2,f.Commands.Count);
        var sql=f.Commands[1];Assert.Contains("yz_admin_type",sql.Sql);Assert.Contains("yz_project_id",sql.Sql);Assert.Contains("delete_no",sql.Sql);Assert.True(sql.Parameters>0);var projection=sql.Sql[..sql.Sql.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)];Assert.DoesNotContain("yz_project_id",projection);
        f.Commands.Clear();await f.Post("yz_role",Role,"selection/selected",new{selectedIds=f.Selected("deleted-role")},HttpStatusCode.BadRequest);Assert.Single(f.Commands);
        f.Commands.Clear();await f.Post("yz_manager",Manager,"selection/selected",new{selectedIds=f.Selected("array-account")},HttpStatusCode.Forbidden);Assert.True(f.Commands.Count==0,"其它来源页面权限不得借用。");
        await f.AssertOwnedUnchanged();await f.Complete();
    }

    /// <summary>只给export而不给query/new/update，实际HTTP生成真实XLSX；角色保原IDJSON，后台名单选为string/null。</summary>
    [AgentIntegrationFact]
    public async Task Export_only_http_keeps_original_json_and_fixed_admin_name_in_real_xlsx()
    {
        await using var f=await FixedRoleReadUseMySqlFixture.CreateAsync("export");var identity=await f.Identity();foreach(var page in new[]{Manager,Role})foreach(var action in new[]{"is_query","is_new","is_update","is_all"})Assert.False(identity.Permissions.Contains(page+":"+action));
        f.Commands.Clear();var ticket=(await f.Post("yz_manager",Manager,"initExport",new PageRequest{SelectedIds=f.Selected("array-account","null-account","empty-account")})).GetString()!;
        Assert.Single(f.Commands);AssertSecretFreeSql(f.Commands[0].Sql);var file=await f.Exports.OpenDownloadAsync(ticket);
        await using(file.Stream){using var book=new XLWorkbook(file.Stream);var sheet=book.Worksheet(1);var column=sheet.Row(1).CellsUsed().Single(cell=>cell.GetString()=="角色").Address.ColumnNumber;var nameColumn=NameColumn(sheet,f,"yz_manager");var expected=new Dictionary<string,string>{{f.DisplayName("array-account"),f.RoleJson},{f.DisplayName("null-account"),""},{f.DisplayName("empty-account"),"[]"}};
            foreach(var row in Enumerable.Range(2,3)){var name=sheet.Cell(row,nameColumn).GetString();Assert.True(expected.Remove(name,out var value),"导出只能包含本次所选自有账号。");Assert.Equal(value,sheet.Cell(row,column).GetString());if(name==f.DisplayName("array-account"))Assert.Equal(XLDataType.Text,sheet.Cell(row,column).DataType);if(name==f.DisplayName("null-account"))Assert.True(sheet.Cell(row,column).IsEmpty());}Assert.True(expected.Count==0);Assert.Equal(4,sheet.LastRowUsed()!.RowNumber());}
        f.Commands.Clear();ticket=(await f.Post("yz_role",Role,"initExport",new PageRequest{SelectedIds=f.Selected("active-role","foreign-role","deleted-admin-role","missing-admin-role")})).GetString()!;
        Assert.Equal(2,f.Commands.Count);file=await f.Exports.OpenDownloadAsync(ticket);await using(file.Stream){using var book=new XLWorkbook(file.Stream);var sheet=book.Worksheet(1);var column=sheet.Row(1).CellsUsed().Single(cell=>cell.GetString()=="后台类型").Address.ColumnNumber;var nameColumn=NameColumn(sheet,f,"yz_role");var expectedNames=new HashSet<string>(new[]{"active-role","foreign-role","deleted-admin-role","missing-admin-role"}.Select(f.DisplayName),StringComparer.Ordinal);
            foreach(var row in Enumerable.Range(2,4)){var name=sheet.Cell(row,nameColumn).GetString();Assert.True(expectedNames.Remove(name),"导出只能包含本次所选自有角色。");if(name==f.DisplayName("active-role"))Assert.Equal(f.DisplayName("active-admin"),sheet.Cell(row,column).GetString());else Assert.True(sheet.Cell(row,column).IsEmpty());}Assert.True(expectedNames.Count==0);Assert.Equal(5,sheet.LastRowUsed()!.RowNumber());}
        f.Commands.Clear();await f.Post("yz_manager",Manager,"selection/selected",new{selectedIds=f.Selected("array-account")},HttpStatusCode.Forbidden);Assert.True(f.Commands.Count==0,"export权限不得代替selection的来源query。");
        await f.AssertOwnedUnchanged();await f.Complete();
    }

    /// <summary>不读测试配置、不建连接或TestServer；先真正编译三表私有生成合同并证明canonical字节不变。</summary>
    [Fact]
    public async Task Private_role_read_model_really_compiles_without_mutating_canonical()
    {
        var root=SourceRoot();var source=Path.Combine(root,"backend/metadata/platform.json");var before=SHA256.HashData(await File.ReadAllBytesAsync(source));var catalog=FixedRoleReadUseMySqlFixture.PrivateCatalog(source);
        var directory=Path.Combine(root,".local/fixed-role-read-mysql-offline",Guid.NewGuid().ToString("N"));var assembly=await FixedRoleReadUseMySqlFixture.CompileAsync(directory,catalog);
        foreach(var (name,page) in new[]{("YzManager",Manager),("YzRole",Role)}){var type=assembly.GetType("Example.FixedRoleMySql.Generated.Services."+name+"Service",true)!;Assert.NotNull(type.GetMethod("SelectionSelected"+page+"Async"));Assert.NotNull(type.GetMethod("Export"+page+"Async"));}
        var managerSource=await File.ReadAllTextAsync(Path.Combine(directory,"generated/Services/YzManagerService.g.cs"));Assert.Contains("RequireRoleNameAction"+Manager+"(actor,\"list\")",managerSource);Assert.Contains("exportAuthorization.RequirePagePermission(\""+Manager+"\", \"export\")",managerSource);
        var after=SHA256.HashData(await File.ReadAllBytesAsync(source));Assert.True(before.SequenceEqual(after),"私有开关不得修改canonical元数据文件。");var original=MetadataRegistry.Load(source).ConfigPages.Single(page=>page.Id==Manager);Assert.Equal(0,original.IsExport);Assert.Equal(0,original.IsCopy);Assert.True(string.IsNullOrEmpty(original.Extra.GetValueOrDefault("importMethod").ToString()));
    }
    /// <summary>沿原元数据名称表头定位源记录，不为测试擅自增加排序开关或假定数据库自然顺序。</summary>
    private static int NameColumn(IXLWorksheet sheet,FixedRoleReadUseMySqlFixture fixture,string table)
    {
        var tableId=fixture.Registry.Platform.ConfigTables.Single(item=>item.TableName==table).Id;var header=fixture.Registry.Platform.ConfigColumns.Single(item=>item.ConfigTableId==tableId&&item.ColumnName=="name").Name;
        var matches=sheet.Row(1).CellsUsed().Where(cell=>cell.GetString()==header).ToArray();Assert.True(matches.Length==1,"原名称表头必须唯一。");return matches[0].Address.ColumnNumber;
    }
    /// <summary>私有pin字段从严解析；合成标识仅用于离线格式验证，不含任何真实实例值。</summary>
    [Fact]
    public void Instance_pin_requires_exact_private_fields_before_connection()
    {
        var valid="{\"expectedHostname\":\"example-fixture-host\",\"expectedServerUuid\":\"11111111-2222-3333-4444-555555555555\"}";
        var pin=FixedRoleReadUseMySqlFixture.ParseInstancePin(valid);Assert.Equal("example-fixture-host",pin.ExpectedHostname);
        foreach(var text in new[]{"{}",valid.Replace("example-fixture-host","",StringComparison.Ordinal),valid.Replace("11111111-2222-3333-4444-555555555555","invalid",StringComparison.Ordinal),valid.Replace("11111111-2222-3333-4444-555555555555","00000000-0000-0000-0000-000000000000",StringComparison.Ordinal),valid[..^1]+",\"expectedHostname\":\"duplicate\"}",valid[..^1]+",\"sql\":\"unexpected\"}"})
            Assert.Throws<InvalidOperationException>(()=>FixedRoleReadUseMySqlFixture.ParseInstancePin(text));
    }
    /// <summary>编译期源码路径定位当前clone，不依赖调用工作目录。</summary>
    private static string SourceRoot([CallerFilePath]string file="")=>Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../.."));
    /// <summary>只断言查询列名，不输出响应实体、密码、盐、token或整行字典。</summary>
    private static void AssertSecretFreeSql(string sql){foreach(var column in new[]{"password","salt","old_password"})Assert.DoesNotContain(column,sql,StringComparison.OrdinalIgnoreCase);}
}
