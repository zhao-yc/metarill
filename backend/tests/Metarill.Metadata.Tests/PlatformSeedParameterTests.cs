using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>真实完整目录种子及ADO参数；仅替代数据库传输，不冒充MySQL首次安装。</summary>
public sealed partial class SecurityIndexSeedIdentityTests
{
    /// <summary>平台与demo全部种子遵守生产物理定义，当前空字符串引用写为NULL，整目录重复无写入。</summary>
    [Fact]
    public async Task Full_published_catalogs_seed_typed_parameters_and_repeat_without_writes()
    {
        var (registry,_)=Load();var provider=new MemoryProvider();var expected=MySqlSchemaPlanner.Compile(registry.Platform,"offline").Tables.ToDictionary(table=>table.Name,StringComparer.Ordinal);
        var inserts=0;var checkedReferences=0;provider.BeforeInsert=(table,record)=>
        {
            ValidateSeedParameters(expected[table],record);inserts++;
            foreach(var name in ReferenceColumns(table)){checkedReferences++;var column=expected[table].Columns.Single(c=>c.Name==name);if(!record.TryGetValue(name,out var value)||value is null)Assert.True(column.Nullable);else Assert.True(Assert.IsType<long>(value)>0);}
        };
        using var session=Session(provider);var repository=new MetadataRepository(new SqlRowStore(session,registry.Platform),registry);
        await repository.SeedAsync(registry.Platform);await repository.SeedAsync(registry.Demo);
        Assert.Equal(SeedCounts(registry.Platform,registry.Demo),provider.Rows.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToDictionary(pair=>pair.Key,pair=>pair.Value.Count,StringComparer.Ordinal));
        Assert.Equal(provider.Rows.Sum(pair=>pair.Value.Count),inserts);Assert.True(inserts>1000);Assert.Equal(provider.Rows.Sum(pair=>pair.Value.Count*ReferenceColumns(pair.Key).Length),checkedReferences);
        Assert.Contains(provider.Rows["config_column"],row=>row.GetValueOrDefault("link_config_table_id") is null);Assert.Contains(provider.Rows["config_column"],row=>row["link_config_table_id"] is long);
        var before=JsonSerializer.Serialize(provider.Rows);provider.Commands.Clear();var prior=inserts;
        await repository.SeedAsync(registry.Platform);await repository.SeedAsync(registry.Demo);
        Assert.Equal(prior,inserts);Assert.Equal(before,JsonSerializer.Serialize(provider.Rows));Assert.All(provider.Commands,sql=>Assert.StartsWith("SELECT",sql,StringComparison.Ordinal));
    }

    /// <summary>既有编辑与墓碑不能因初始化再运行被覆盖或复活。</summary>
    [Fact]
    public async Task Full_seed_keeps_existing_edited_and_deleted_metadata()
    {
        var (registry,_)=Load();var provider=new MemoryProvider();using var session=Session(provider);var repository=new MetadataRepository(new SqlRowStore(session,registry.Platform),registry);
        await repository.SeedAsync(registry.Platform);await repository.SeedAsync(registry.Demo);
        foreach(var table in new[]{"config_column","config_page_column","config_index"})
        {var row=provider.Rows[table][0];row["name"]="已编辑内容须保留";row["delete_no"]=new DateTime(2026,1,1);}
        var before=JsonSerializer.Serialize(provider.Rows);provider.Commands.Clear();
        await repository.SeedAsync(registry.Platform);await repository.SeedAsync(registry.Demo);
        Assert.Equal(before,JsonSerializer.Serialize(provider.Rows));Assert.All(provider.Commands,sql=>Assert.StartsWith("SELECT",sql,StringComparison.Ordinal));
    }

    /// <summary>完整种子末表插入失败时，之前写入同一目录的全部记录回滚；不是跨库或MySQLDDL事务证明。</summary>
    [Fact]
    public async Task Late_full_seed_failure_rolls_back_prior_metadata_in_the_same_catalog()
    {
        var (registry,_)=Load();var provider=new MemoryProvider();var attempted=0;var failure=new InvalidOperationException("本轮末表故障注入");
        provider.BeforeInsert=(table,_)=>{attempted++;if(table=="config_index_column")throw failure;};
        using var session=Session(provider);var repository=new MetadataRepository(new SqlRowStore(session,registry.Platform),registry);
        Assert.Same(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SeedAsync(registry.Platform)));
        Assert.True(attempted>1000);Assert.Equal(1,provider.Rollbacks);Assert.Empty(provider.Rows);
    }

    /// <summary>按生产规划器给出的列型／可空性检查真实驱动参数，不自行编造另一套表结构。</summary>
    private static void ValidateSeedParameters(TableSchema table,IReadOnlyDictionary<string,object?> record)
    {
        var columns=table.Columns.ToDictionary(column=>column.Name,StringComparer.Ordinal);
        Assert.All(record.Keys,key=>Assert.True(columns.ContainsKey(key),"种子含未登记物理字段："+table.Name+"."+key));
        foreach(var column in table.Columns)
        {
            if(!record.TryGetValue(column.Name,out var value)){Assert.True(column.Nullable||column.DefaultSql is not null,"种子缺必填字段："+table.Name+"."+column.Name);continue;}
            if(value is null){Assert.True(column.Nullable,"种子必填字段为NULL："+table.Name+"."+column.Name);continue;}
            if(column.StoreType=="bigint")Assert.IsType<long>(value);
            else if(column.StoreType=="int")Assert.IsType<int>(value);
            else if(column.StoreType.StartsWith("datetime",StringComparison.Ordinal))Assert.IsType<DateTime>(value);
            else if(column.StoreType.StartsWith("varchar",StringComparison.Ordinal)||column.StoreType is "text" or "longtext")
            {
                var text=Assert.IsType<string>(value);var length=Regex.Match(column.StoreType,@"^varchar\((\d+)\)$");
                if(length.Success)Assert.True(text.EnumerateRunes().Count()<=int.Parse(length.Groups[1].Value,CultureInfo.InvariantCulture),"种子超出物理长度："+table.Name+"."+column.Name);
            }
            else throw new InvalidOperationException("本固定目录参数核对未声明类型："+column.StoreType);
        }
        Assert.True(Assert.IsType<long>(record["id"])>0);
    }
    /// <summary>六个已经确认改型的内容引用；其余表不套用这套平台专用清空规则。</summary>
    private static string[] ReferenceColumns(string table)=>table switch
    {"config_child"=>["child_config_page_id"],"config_column"=>["link_config_table_id","link_config_page_id"],"config_page_column"=>["link_config_table_id","link_config_page_id","combine_super_column_id"],_=>[]};
    /// <summary>按原种子调用集合统计行数，保留空集合不读不写的行为。</summary>
    private static Dictionary<string,int> SeedCounts(params MetadataCatalog[] catalogs)
    {
        var result=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var c in catalogs)foreach(var (table,count) in new[]{("config_table",c.ConfigTables.Count),("config_column",c.ConfigColumns.Count),("config_page",c.ConfigPages.Count),("config_page_column",c.ConfigPageColumns.Count),("yz_admin_type",c.YzAdminTypes.Count),("config_menu",c.ConfigMenus.Count),("config_custom_page",c.ConfigCustomPages.Count),("config_button",c.ConfigButtons.Count),("config_child",c.ConfigChildren.Count),("config_index",c.ConfigIndexes.Count),("config_index_column",c.ConfigIndexColumns.Count)})
            if(count>0)result[table]=result.GetValueOrDefault(table)+count;
        return result.OrderBy(pair=>pair.Key,StringComparer.Ordinal).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
    }
}
