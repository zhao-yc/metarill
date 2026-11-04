using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
namespace Metarill.Paired.Indexes;

/// <summary>仅供独占空库验收的共同逻辑文件访问索引；不改变平台通用同步的保留规则。</summary>
public static class FileAccessIndexProfile
{
    public const string Name="paired-common-file-access-indexes-v1";
    public const string Scope="paired-native-common-file-measurement-v1";
    public static IReadOnlyList<string> Tables { get; }=Array.AsReadOnly(new[]{"yz_file_library","yz_file_ref"});
    private static readonly string[] Audit=["id","yz_project_id","insert_time","update_time","insert_manager_id","update_manager_id","insert_manager_real_name","update_manager_real_name","delete_no"];

    /// <summary>只接受固定两库和目标；工具不能借名称参数操作其它库或任意索引。</summary>
    public static void RequireDatabase(string database,string target)
    {
        if(target is not("java" or "csharp")||!Regex.IsMatch(database,@"\Ametarill_fresh_[a-f0-9]{16}_paired_"+target+@"\z"))throw new UnauthorizedAccessException("索引目标不是本轮独占库。");
    }
    /// <summary>字段别名仅映射同一逻辑槽；不增列、不把数值配置列和字符串字段名当成相同索引。</summary>
    public static IReadOnlyList<IndexSchema> Desired(string table,string target)
    {
        if(target is not("java" or "csharp"))throw new ArgumentException("未知目标。");
        var owner=target=="java"?"owner_yz_manager_id":"owner_id";var page=target=="java"?"source_config_page_id":"page_id";var record=target=="java"?"source_record_id":"record_id";
        return table switch
        {
            "yz_file_library"=>[new("mt_file_project_status",false,["yz_project_id","status"]),new("mt_file_folder",false,["yz_project_id","folder_id","delete_no"]),new("mt_file_owner",false,[owner])],
            "yz_file_ref"=>[new("mt_ref_file",false,["file_id","delete_no"]),new("mt_ref_record",false,[page,record,"delete_no"]),new("mt_ref_project_record",false,["yz_project_id",record,page,"delete_no"])],
            _=>throw new InvalidOperationException("不能修改其它表的索引。")
        };
    }
    /// <summary>原索引精确白名单；所有项均非唯一，任何附加约束、名称或列序漂移均拒绝。</summary>
    public static IReadOnlyList<IndexSchema> Original(string table,string target)=>(target,table) switch
    {
        ("java","yz_file_library")=>[new("idx_yz_file_library_project_status",false,["yz_project_id","status"]),new("idx_yz_file_library_folder",false,["yz_project_id","folder_id"]),new("idx_yz_file_library_owner",false,["owner_yz_manager_id"])],
        ("java","yz_file_ref")=>[new("idx_yz_file_ref_file",false,["file_id"]),new("idx_yz_file_ref_source",false,["source_config_page_id","source_record_id","source_config_column_id"])],
        ("csharp","yz_file_library")=>[new("ix_file_folder",false,["yz_project_id","folder_id","delete_no"])],
        ("csharp","yz_file_ref")=>[new("ix_file_delete",false,["file_id","delete_no"]),new("ix_file_record",false,["page_id","record_id","delete_no"]),new("ix_file_scope_record",false,["yz_project_id","record_id","page_id","delete_no"])],
        _=>throw new InvalidOperationException("未知旧文件表索引。")
    };
    /// <summary>固定原Java／当前C#字段集合；只有索引可变，未知版本或额外列不能冒充原初始化结构。</summary>
    public static IReadOnlyList<string> Columns(string table,string target)
    {
        var extra=(target,table) switch
        {
            ("java","yz_file_library")=>new[]{"folder_id","provider_code","access_mode","object_key","storage_locator_json","provider_meta_json","original_name","extension","mime_type","size_bytes","status","owner_yz_manager_id","public_url","hash_value","draft_expire_time","import_no"},
            ("java","yz_file_ref")=>["file_id","source_config_page_id","source_config_table_id","source_config_column_id","source_table_name","source_record_id","source_parent_id","source_column_name","sort_no","owner_yz_manager_id","import_no"],
            ("csharp","yz_file_library")=>["access_mode","content_type","folder_id","name","owner_id","sha256","size","status","storage_key","upload_expires_at","insert_user_name","update_user_name"],
            ("csharp","yz_file_ref")=>["file_id","page_id","record_id","field_name","insert_user_name","update_user_name"],
            _=>throw new InvalidOperationException("未知文件表字段集合。")
        };
        return Audit.Concat(extra).Order(StringComparer.Ordinal).ToArray();
    }
    /// <summary>反查范围由最小逻辑元数据编译，只读两表全部实际结构；并不拿最小目录替代完整快照。</summary>
    public static MetadataCatalog InspectionScope()
    {
        var catalog=new MetadataCatalog{YzProjectId="810000000000000001"};var number=0;
        foreach(var table in Tables){var id=(890000000000000301L+number++).ToString(System.Globalization.CultureInfo.InvariantCulture);catalog.ConfigTables.Add(new(){Id=id,TableName=table});catalog.ConfigColumns.Add(new(){Id=id+"1",ConfigTableId=id,ColumnName="id",LogicalType="int64",IsDbNotNull=1});}
        return catalog;
    }
    /// <summary>索引方向按逐列ASC归一；名称、唯一性和完整列序都参与匹配，不忽略其它索引。</summary>
    public static string IndexSignature(IEnumerable<IndexSchema> indexes)=>JsonSerializer.Serialize(indexes.OrderBy(x=>x.Name,StringComparer.Ordinal).Select(x=>new{x.Name,x.Unique,x.Columns,Directions=x.Directions??Enumerable.Repeat("ASC",x.Columns.Count).ToArray()}),RelationIndexProfile.Json);

    /// <summary>检查受管两表、完整字段、关键物理类型及精确旧／共同索引，不能借验证动作修改漂移结构。</summary>
    public static void RequireState(DatabaseSchema schema,string target,bool desired)
    {
        RequireDatabase(schema.Database,target);
        if(!schema.Tables.Select(t=>t.Name).Order(StringComparer.Ordinal).SequenceEqual(Tables)||schema.Tables.Count!=2)throw new InvalidOperationException("必须为完整的两张文件基础表。");
        foreach(var table in schema.Tables)
        {
            if(!table.Columns.Select(c=>c.Name).Order(StringComparer.Ordinal).SequenceEqual(Columns(table.Name,target))||table.Columns.Any(c=>c.Generated))throw new InvalidOperationException("文件表字段或生成列不匹配。");
            var keys=Desired(table.Name,target).SelectMany(i=>i.Columns).Append("id").Distinct(StringComparer.Ordinal);
            foreach(var name in keys)
            {
                var column=table.Columns.Single(c=>c.Name==name);var type=name=="status"?(target=="java"?"varchar(50)":"varchar(20)"):name=="delete_no"?(target=="java"?"datetime":"datetime(6)"):"bigint";
                if(column.StoreType!=type||column.PrimaryKey!=(name=="id")||name=="id"&&column.Nullable)throw new InvalidOperationException("文件访问索引字段物理类型不匹配。");
            }
            if(IndexSignature(table.Indexes??[])!=IndexSignature(desired?Desired(table.Name,target):Original(table.Name,target)))throw new InvalidOperationException("文件表索引不属于固定原态或共同态。");
        }
    }
    /// <summary>只生成白名单旧索引删除及共同索引新增；通用规划器继续不自动删索引。</summary>
    public static IReadOnlyList<SchemaChange> ExpectedChanges(string target)
    {
        RequireDatabase("metarill_fresh_"+new string('a',16)+"_paired_"+target,target);var changes=new List<SchemaChange>();
        foreach(var table in Tables)
        {
            foreach(var index in Original(table,target).OrderBy(x=>x.Name,StringComparer.Ordinal))changes.Add(new("dropIndex",table,null,$"ALTER TABLE {MySqlSchemaPlanner.Quote(table)} DROP INDEX {MySqlSchemaPlanner.Quote(index.Name)}"));
            var before=new DatabaseSchema("fixed_fixture",[new(table,[],Indexes:[])]);var after=new DatabaseSchema("fixed_fixture",[new(table,[],Indexes:Desired(table,target))]);changes.AddRange(MySqlSchemaPlanner.Compare(before,after));
        }
        return changes;
    }
    /// <summary>列定义和SHOW CREATE的非二级索引部分逐字保留；只消除索引行及其分隔逗号。</summary>
    public static string ProtectedFingerprint(DatabaseSchema schema)
    {
        var value=schema.Tables.OrderBy(t=>t.Name,StringComparer.Ordinal).Select(t=>new{t.Name,Columns=t.Columns.OrderBy(c=>c.Name,StringComparer.Ordinal),Definition=string.Join("\n",(t.SnapshotDefinition??throw new InvalidOperationException("缺少原DDL快照。")).Split('\n').Select(line=>line.Trim()).Where(line=>!Regex.IsMatch(line,@"\A(?:UNIQUE )?KEY `")).Select(line=>line.TrimEnd(','))) });return RelationIndexProfile.Hash(JsonSerializer.SerializeToUtf8Bytes(value,RelationIndexProfile.Json));
    }
    /// <summary>确认摘要绑定运行号、目标、完整原结构及DDL，不能跨库或替换已审查动作。</summary>
    public static string PlanSha256(string run,string target,DatabaseSchema before)
    {
        Program.RequireRun(run,"approved");RequireState(before,target,false);if(before.Database!="metarill_fresh_"+run[..16]+"_paired_"+target)throw new UnauthorizedAccessException("运行号与计划库不符。");
        return RelationIndexProfile.Hash(JsonSerializer.SerializeToUtf8Bytes(new{profile=Name,run,target,before.Database,BeforeFingerprint=MySqlSchemaPlanner.Fingerprint(before),ProtectedFingerprint=ProtectedFingerprint(before),Changes=ExpectedChanges(target)},RelationIndexProfile.Json));
    }
}
