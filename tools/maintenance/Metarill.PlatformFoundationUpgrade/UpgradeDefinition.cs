using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Data.Schema;
namespace Metarill.Tools.PlatformFoundationUpgrade;

/// <summary>本次开发平台升级的固定发布源；不接受任意对象ID、原生SQL或用户自报可信标记。</summary>
internal sealed class UpgradeDefinition
{
    internal static readonly (string Table,string Id)[] Keys=[("config_column","2300000000000000401"),
        ("config_index","2300000000000010000"),("config_index","900235310779076608"),("config_index","900235316722405376"),("config_index","900235322581848064"),
        ("config_index_column","2300000000000010001"),("config_index_column","2300000000000010002"),("config_index_column","900235310800048128"),("config_index_column","900235310821019648"),
        ("config_index_column","900235316734988288"),("config_index_column","900235316751765504"),("config_index_column","900235322598625280"),("config_index_column","900235322615402496")];
    internal static readonly SchemaChange[] AllowedChanges=[
        new("addIndex","sys_session",null,"ALTER TABLE `sys_session` ADD INDEX `ix_session_account_active` (`account_id`, `delete_no`)"),
        new("addColumn","yz_file_library","upload_expires_at","ALTER TABLE `yz_file_library` ADD COLUMN `upload_expires_at` datetime(6) NULL"),
        new("addIndex","yz_manager",null,"ALTER TABLE `yz_manager` ADD INDEX `yzp_idx_manager_admin_active` (`yz_admin_type_id`, `delete_no`)"),
        new("addIndex","yz_role",null,"ALTER TABLE `yz_role` ADD INDEX `yzp_idx_role_admin_active` (`yz_admin_type_id`, `delete_no`)"),
        new("addIndex","yz_role_permission",null,"ALTER TABLE `yz_role_permission` ADD INDEX `yzp_idx_role_permission_role_active` (`yz_role_id`, `delete_no`)")];
    internal MetadataRegistry Registry {get;}
    internal MetadataCatalog Desired {get;}
    internal MetadataCatalog Patch {get;}
    internal IReadOnlyList<UpgradeRow> Rows {get;}
    internal IReadOnlyDictionary<(string Table,string Id),JsonElement> Records {get;}
    internal string SourceSha256 {get;}
    /// <summary>所有输入只取明确源根目录的四份已发布文件，模型深拷贝，不加载私有设置。</summary>
    internal UpgradeDefinition(string sourceRoot)
    {
        sourceRoot=Path.GetFullPath(sourceRoot);if(!File.Exists(Path.Combine(sourceRoot,"global.json")))throw new InvalidOperationException("升级来源不是Metarill仓库。");
        var metadata=Path.Combine(sourceRoot,"backend/metadata");Registry=new(Path.Combine(metadata,"platform.json"),Path.Combine(metadata,"demo.json"));
        if(Registry.Platform.YzProjectId!="1608097412978577409")throw new InvalidOperationException("升级来源不是固定平台项目。");
        var files=MetadataRegistry.Load(Path.Combine(metadata,"files.json"));var security=MetadataRegistry.Load(Path.Combine(metadata,"security.json"));
        var full=new ProjectPhysicalModels(files,security,Registry).Compose(Registry.Platform);
        var targets=AllowedChanges.Select(change=>change.Table).ToHashSet(StringComparer.Ordinal);
        Desired=Clone(full);Desired.ConfigTables.RemoveAll(table=>!targets.Contains(table.TableName));var tables=Desired.ConfigTables.Select(table=>table.Id).ToHashSet(StringComparer.Ordinal);
        Desired.ConfigColumns.RemoveAll(column=>!tables.Contains(column.ConfigTableId));Desired.ConfigPages.Clear();Desired.ConfigPageColumns.Clear();Desired.ConfigIndexes.RemoveAll(index=>!tables.Contains(Text(index,"configTableId")));
        var indexes=Desired.ConfigIndexes.Select(index=>Text(index,"id")).ToHashSet(StringComparer.Ordinal);Desired.ConfigIndexColumns.RemoveAll(field=>!indexes.Contains(Text(field,"configIndexId")));
        Patch=new(){YzProjectId=Registry.Platform.YzProjectId,ConfigColumns=Registry.Platform.ConfigColumns.Where(column=>Keys.Contains(("config_column",column.Id))).Select(Clone).ToList(),
            ConfigIndexes=Registry.Platform.ConfigIndexes.Where(index=>Keys.Contains(("config_index",Text(index,"id")))).Select(index=>index.Clone()).ToList(),
            ConfigIndexColumns=Registry.Platform.ConfigIndexColumns.Where(field=>Keys.Contains(("config_index_column",Text(field,"id")))).Select(field=>field.Clone()).ToList()};
        var records=new Dictionary<(string Table,string Id),JsonElement>();
        foreach(var column in Patch.ConfigColumns)records.Add(("config_column",column.Id),JsonSerializer.SerializeToElement(column,UpgradePlanCodec.Json));
        foreach(var index in Patch.ConfigIndexes)records.Add(("config_index",Text(index,"id")),index.Clone());
        foreach(var field in Patch.ConfigIndexColumns)records.Add(("config_index_column",Text(field,"id")),field.Clone());
        if(records.Count!=Keys.Length||Keys.Any(key=>!records.ContainsKey(key))||records.Values.Any(record=>Text(record,"yzProjectId")!=Registry.Platform.YzProjectId))throw new InvalidOperationException("固定升级源记录缺失或归属不符。");
        ValidateSourceRecords();
        Records=records;Rows=Array.AsReadOnly(Keys.Select(key=>new UpgradeRow(key.Table,key.Id,UpgradePlanCodec.Hash(JsonSerializer.SerializeToUtf8Bytes(records[key],UpgradePlanCodec.Json)))).ToArray());
        SourceSha256=UpgradePlanCodec.Hash(JsonSerializer.SerializeToUtf8Bytes(new{metadata= new[]{"platform.json","files.json","security.json","demo.json"}.Select(name=>new{name,sha256=UpgradePlanCodec.Hash(File.ReadAllBytes(Path.Combine(metadata,name)))}),Rows,physicalFingerprint=MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(Desired,"metarill_dev"))},UpgradePlanCodec.Json));
    }
    /// <summary>固定升级版本同时锁定对象语义；仅保留相同ID但改变类型/索引用途不能伪装成此维护任务。</summary>
    private void ValidateSourceRecords()
    {
        var column=Patch.ConfigColumns.Single();var table=Registry.Platform.ConfigTables.Single(item=>item.Id==column.ConfigTableId);
        if(table.TableName!="yz_file_library"||column.ColumnName!="upload_expires_at"||column.LogicalType!="datetime"||column.IsDbNotNull!=0||column.IsVirtualColumn!=0||!string.IsNullOrEmpty(column.DefaultValue)||MetadataColumnTypePolicy.TemporalPrecision(column)!=6)throw new InvalidOperationException("固定期限字段的类型或用途已变化。");
        var expected=new[]{("2300000000000010000","sys_session","ix_session_account_active","account_id"),("900235310779076608","yz_role_permission","yzp_idx_role_permission_role_active","yz_role_id"),
            ("900235316722405376","yz_role","yzp_idx_role_admin_active","yz_admin_type_id"),("900235322581848064","yz_manager","yzp_idx_manager_admin_active","yz_admin_type_id")};
        foreach(var(id,owner,name,first)in expected)
        {
            var index=Patch.ConfigIndexes.Single(item=>Text(item,"id")==id);var ownerId=Text(index,"configTableId");
            if(Registry.Platform.ConfigTables.Single(item=>item.Id==ownerId).TableName!=owner||Text(index,"indexName")!=name||Text(index,"isUnique")!="0"||Text(index,"isEnabled")!="1"||Text(index,"indexType")!="INDEX")throw new InvalidOperationException("固定索引的归属或用途已变化。");
            var fields=Patch.ConfigIndexColumns.Where(item=>Text(item,"configIndexId")==id).OrderBy(item=>int.Parse(Text(item,"sortNo"),System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if(fields.Length!=2||fields.Select(item=>Text(item,"sortNo")).Distinct(StringComparer.Ordinal).Count()!=2||!fields.Select(item=>Text(item,"columnName")).SequenceEqual(new[]{first,"delete_no"},StringComparer.Ordinal)
                ||fields.Any(item=>Text(item,"sortDirection")!="ASC"||!Registry.Platform.ConfigColumns.Any(source=>source.Id==Text(item,"configColumnId")&&source.ConfigTableId==ownerId&&source.ColumnName==Text(item,"columnName")&&source.IsVirtualColumn==0)))throw new InvalidOperationException("固定索引字段、方向或顺序已变化。");
        }
    }
    /// <summary>只查询本次三个元数据表的物理白名单，用于插入后的逐字段读回。</summary>
    internal IReadOnlyDictionary<string,JsonElement> StoredFields(string table,string id)
    {
        var definition=Registry.Platform.ConfigTables.Single(item=>item.TableName==table);var columns=MetadataStoragePolicy.ForTable(Registry.Platform,definition).PhysicalColumns.ToDictionary(column=>column.ColumnName,StringComparer.Ordinal);
        // 原种子按物理类型把空引用ID等非字符串空值写为null；字符串空串仍必须逐字保持。
        JsonElement Expected(string name,JsonElement value)=>value.ValueKind==JsonValueKind.String&&value.GetString()==string.Empty&&columns[name].LogicalType is not("string" or "json")?JsonSerializer.SerializeToElement<object?>(null):value;
        return Records[(table,id)].EnumerateObject().Select(field=>(Name:MetadataRegistry.Snake(field.Name),field.Value)).Where(field=>columns.ContainsKey(field.Name)&&field.Name is not("insert_time" or "update_time" or "delete_no")).ToDictionary(field=>field.Name,field=>Expected(field.Name,field.Value),StringComparer.Ordinal);
    }
    internal static string Text(JsonElement value,string key)=>value.TryGetProperty(key,out var result)?result.ToString():"";
    private static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value,UpgradePlanCodec.Json),UpgradePlanCodec.Json)!;
}
