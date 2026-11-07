using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
namespace Metarill.Paired.Indexes;

/// <summary>性能专用的显式索引元数据升级；原三表字段、默认值和原有索引不变。</summary>
public static class RelationIndexProfile
{
    public const string Name="original-three-table-relation-indexes-v1";
    public const string BaselineMetadataSha256="12bc9e1ffa6cecccd78d4adca78a671064ed9f01607f90a46d10160fabe71ec2";
    // 扩展夹具仅启用原三页Excel能力；固定包内字节摘要，不接受任意同形元数据。
    public const string ExtendedWorkloadMetadataSha256="2e30000176bf5fd035eb1cc9b6295dd314701ffc5d4770942eaa65f77ce31134";
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static readonly (string Table,string Foreign,string Index,string Id)[] Additions=
    [
        ("sample_contract","sample_customer_id","ix_sample_contract_customer","890000000000000001"),
        ("sample_contract_line","sample_contract_id","ix_sample_contract_line_contract","890000000000000002")
    ];

    /// <summary>先核原包精确字节，不能用更简单的模型替换已通过语义比对的三表。</summary>
    public static MetadataCatalog ReadBaseline(byte[] bytes)
    {
        var hash=Hash(bytes);
        if(hash!=BaselineMetadataSha256&&hash!=ExtendedWorkloadMetadataSha256)throw new InvalidOperationException("原同模型元数据摘要不符。");
        return JsonSerializer.Deserialize<MetadataCatalog>(bytes,Json)??throw new InvalidOperationException("缺少原元数据。");
    }

    /// <summary>仅追加两索引六字段引用，使用结构编译器产生DDL，不拼写ALTER语句。</summary>
    public static MetadataCatalog Build(MetadataCatalog baseline)
    {
        MetadataValidation.Validate(baseline);
        if(!baseline.ConfigTables.Select(t=>t.TableName).Order(StringComparer.Ordinal).SequenceEqual(new[]{"sample_contract","sample_contract_line","sample_customer"}))throw new InvalidOperationException("必须为原三张业务表。");
        var result=JsonSerializer.Deserialize<MetadataCatalog>(JsonSerializer.SerializeToUtf8Bytes(baseline,Json),Json)!;
        for(var number=0;number<Additions.Length;number++)
        {
            var item=Additions[number];var table=result.ConfigTables.Single(t=>t.TableName==item.Table);
            if(result.ConfigIndexes.Any(x=>Text(x,"id")==item.Id||Text(x,"indexName")==item.Index))throw new InvalidOperationException("索引元数据已含本轮升级或名字冲突。");
            var names=new[]{item.Foreign,"delete_no","id"};
            var columns=names.Select(name=>result.ConfigColumns.Single(c=>c.ConfigTableId==table.Id&&c.ColumnName==name)).ToArray();
            if(columns[0].LogicalType!="string"||columns[0].ColumnLength!="50"||columns[1].LogicalType!="datetime"||columns[2].LogicalType!="int64")throw new InvalidOperationException("索引字段与原物理类型不一致。");
            result.ConfigIndexes.Add(JsonSerializer.SerializeToElement(new{id=item.Id,configTableId=table.Id,indexName=item.Index,isUnique=0,isEnabled=1,yzProjectId=result.YzProjectId}));
            for(var position=0;position<columns.Length;position++)
            {
                var id=(890000000000000101L+number*3+position).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if(result.ConfigIndexColumns.Any(x=>Text(x,"id")==id))throw new InvalidOperationException("索引字段元数据ID冲突。");
                result.ConfigIndexColumns.Add(JsonSerializer.SerializeToElement(new{id,configIndexId=item.Id,configColumnId=columns[position].Id,columnName=names[position],sortNo=position,sortDirection="ASC",yzProjectId=result.YzProjectId}));
            }
        }
        MetadataValidation.Validate(result);return result;
    }

    /// <summary>计划必须精确等于元数据编译的两个增索引操作，拒绝改列、建表、部分或附加语句。</summary>
    public static IReadOnlyList<SchemaChange> ExpectedChanges(MetadataCatalog baseline,string database)
        =>MySqlSchemaPlanner.Compare(MySqlSchemaPlanner.Compile(baseline,database),MySqlSchemaPlanner.Compile(Build(baseline),database));

    /// <summary>当前库必须只缺这两索引；verify则要求完全无差异，不能将漂移当成已完成。</summary>
    public static void RequireChanges(SchemaChangePlan plan,IReadOnlyList<SchemaChange> expected,bool verify)
    {
        if(plan.Provider!="mysql"||expected.Count!=2||expected.Any(x=>x.Kind!="addIndex")||
           (verify?plan.Changes.Count!=0:!plan.Changes.SequenceEqual(expected)))throw new InvalidOperationException("实际结构差异不是固定两索引升级。");
    }

    /// <summary>确认摘要绑定库、前后结构与实际DDL；新进程预览ID不参与摘要，执行仍走SchemaService原签名与漂移校验。</summary>
    public static string PlanSha256(SchemaChangePlan plan)
        =>Hash(JsonSerializer.SerializeToUtf8Bytes(new{profile=Name,plan.Provider,plan.Database,plan.BeforeFingerprint,plan.DesiredFingerprint,plan.Changes},Json));
    /// <summary>摘要只处理元数据与计划；不记录连接、密码或用户输入。</summary>
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Text(JsonElement element,string name)=>element.TryGetProperty(name,out var value)?value.ToString():"";
}
