using System.Text.Json;
using Metarill.Data.Schema;
using Metarill.Paired.Indexes;
using Xunit;
namespace Metarill.Paired.SqlObservation.Tests;

/// <summary>只验空独占文件表索引的固定元数据／确认／漂移边界，所有结构为内存模型，不冒充MySQL。</summary>
public sealed class FileAccessIndexProfileTests
{
    /// <summary>原Java启动器按固定索引名称检查；保持名称以防宿主自动增建不对等索引。</summary>
    [Fact]public void Folder_index_retains_original_java_startup_name_on_both_targets()
    {
        foreach(var side in new[]{"java","csharp"})
        {
            var folder=Assert.Single(FileAccessIndexProfile.Desired("yz_file_library",side),i=>i.Name=="idx_yz_file_library_folder");
            Assert.Equal(new[]{"yz_project_id","folder_id","delete_no"},folder.Columns);
            Assert.DoesNotContain(FileAccessIndexProfile.Desired("yz_file_library",side),i=>i.Name=="mt_file_folder");
        }
    }

    /// <summary>Java十一项／C#十项，仅删除旧非唯一索引和添加六个共同索引；不生成改列、数据或主键操作。</summary>
    [Fact]public void Exact_plans_change_only_whitelisted_nonunique_secondary_indexes()
    {
        var java=FileAccessIndexProfile.ExpectedChanges("java");var csharp=FileAccessIndexProfile.ExpectedChanges("csharp");Assert.Equal(11,java.Count);Assert.Equal(10,csharp.Count);
        foreach(var changes in new[]{java,csharp}){Assert.Equal(6,changes.Count(c=>c.Kind=="addIndex"));Assert.All(changes,c=>{Assert.Contains(c.Kind,new[]{"dropIndex","addIndex"});Assert.Null(c.Column);Assert.Contains(c.Table,FileAccessIndexProfile.Tables);Assert.DoesNotContain("PRIMARY",c.Sql);Assert.DoesNotContain("UNIQUE",c.Sql);Assert.DoesNotContain(";",c.Sql);});}
        Assert.Contains(java,c=>c.Sql=="ALTER TABLE `yz_file_library` DROP INDEX `idx_yz_file_library_owner`");Assert.Contains(csharp,c=>c.Sql=="ALTER TABLE `yz_file_ref` ADD INDEX `mt_ref_record` (`page_id`, `record_id`, `delete_no`)");
        Assert.Equal("owner_yz_manager_id",FileAccessIndexProfile.Desired("yz_file_library","java").Single(i=>i.Name=="mt_file_owner").Columns.Single());Assert.Equal("owner_id",FileAccessIndexProfile.Desired("yz_file_library","csharp").Single(i=>i.Name=="mt_file_owner").Columns.Single());
        Assert.DoesNotContain(java.Where(c=>c.Kind=="addIndex"),c=>c.Sql.Contains("source_config_column_id"));Assert.DoesNotContain(csharp,c=>c.Sql.Contains("field_name"));Assert.Throws<UnauthorizedAccessException>(()=>FileAccessIndexProfile.ExpectedChanges("production"));
    }
    /// <summary>完整字段和索引原态／目标态分别匹配；不能用多余、缺少、唯一或变型字段绕过边界。</summary>
    [Fact]public void Native_shapes_and_only_exact_index_state_are_required()
    {
        foreach(var side in new[]{"java","csharp"})
        {
            var old=Schema(side,false);FileAccessIndexProfile.RequireState(old,side,false);FileAccessIndexProfile.RequireState(Schema(side,true),side,true);Assert.Throws<InvalidOperationException>(()=>FileAccessIndexProfile.RequireState(old,side,true));
            var table=old.Tables[0];var index=table.Indexes![0];
            foreach(var changed in new[]{table with{Columns=[..table.Columns,new("unknown_extra","bigint",true)]},table with{Columns=table.Columns.Where(c=>c.Name!="id").ToArray()},table with{Indexes=[..table.Indexes,index with{Name="extra"}]},table with{Indexes=[index with{Unique=true},..table.Indexes.Skip(1)]},table with{Indexes=[index with{Columns=index.Columns.Reverse().ToArray(),Directions=["DESC",..Enumerable.Repeat("ASC",index.Columns.Count-1)]},..table.Indexes.Skip(1)]},table with{Columns=table.Columns.Select(c=>c.Name=="id"?c with{Nullable=true}:c).ToArray()},table with{Columns=table.Columns.Select(c=>c.Name=="status"?c with{StoreType="varchar(99)"}:c).ToArray()},table with{Columns=table.Columns.Select(c=>c.Name=="delete_no"?c with{Generated=true}:c).ToArray()}})
                Assert.Throws<InvalidOperationException>(()=>FileAccessIndexProfile.RequireState(old with{Tables=[changed,old.Tables[1]]},side,false));
            Assert.Throws<InvalidOperationException>(()=>FileAccessIndexProfile.RequireState(old with{Tables=[old.Tables[0]]},side,false));
        }
    }
    /// <summary>精确两表的反查作用域仍由真实逻辑编译器检验；不把最小目录当成列覆盖或更新计划。</summary>
    [Fact]public void Inspection_metadata_compiles_only_the_two_fixed_tables()
    {
        var catalog=FileAccessIndexProfile.InspectionScope();var schema=MySqlSchemaPlanner.Compile(catalog,"fixed_fixture");Assert.Equal(new[]{"yz_file_library","yz_file_ref"},schema.Tables.Select(t=>t.Name));Assert.All(schema.Tables,t=>{var id=Assert.Single(t.Columns);Assert.Equal("id",id.Name);Assert.True(id.PrimaryKey);Assert.False(id.Nullable);Assert.Empty(t.Indexes!);});
    }
    /// <summary>二级索引行可替换，任何列、默认、主键、约束、存储选项或缺快照均改变保护指纹或拒绝。</summary>
    [Fact]public void Protected_fingerprint_preserves_every_nonsecondary_index_part()
    {
        var before=Schema("java",false);var after=Schema("java",true);Assert.Equal(FileAccessIndexProfile.ProtectedFingerprint(before),FileAccessIndexProfile.ProtectedFingerprint(after));var original=FileAccessIndexProfile.ProtectedFingerprint(before);var table=before.Tables[0];
        foreach(var changed in new[]{table with{Columns=table.Columns.Select(c=>c.Name=="status"?c with{DefaultSql="'READY'"}:c).ToArray()},table with{SnapshotDefinition=table.SnapshotDefinition!.Replace("ENGINE=InnoDB","ENGINE=MyISAM")},table with{SnapshotDefinition=table.SnapshotDefinition!.Replace("PRIMARY KEY (`id`)","PRIMARY KEY (`status`)")},table with{SnapshotDefinition=table.SnapshotDefinition!+" CHECK (`id`>0)"}})
            Assert.NotEqual(original,FileAccessIndexProfile.ProtectedFingerprint(before with{Tables=[changed,before.Tables[1]]}));
        Assert.Throws<InvalidOperationException>(()=>FileAccessIndexProfile.ProtectedFingerprint(before with{Tables=[table with{SnapshotDefinition=null},before.Tables[1]]}));
    }
    /// <summary>确认摘要绑定同一运行号、目标库和完整结构；不能把旧计划或另一端的确认挪用。</summary>
    [Fact]public void Confirmation_binds_database_run_full_schema_and_actual_plan()
    {
        var run=new string('a',32);var before=Schema("java",false);var hash=FileAccessIndexProfile.PlanSha256(run,"java",before);Assert.Equal(64,hash.Length);Assert.Equal(hash,FileAccessIndexProfile.PlanSha256(run,"java",before));
        Assert.NotEqual(hash,FileAccessIndexProfile.PlanSha256(run[..16]+new string('b',16),"java",before));Assert.NotEqual(hash,FileAccessIndexProfile.PlanSha256(run,"java",before with{Tables=before.Tables.Select(t=>t with{SnapshotDefinition=t.SnapshotDefinition+" COMMENT='changed'"}).ToArray()}));
        Assert.Throws<UnauthorizedAccessException>(()=>FileAccessIndexProfile.PlanSha256(new string('b',32),"java",before));Assert.Throws<UnauthorizedAccessException>(()=>FileAccessIndexProfile.RequireDatabase("metarill_business_test","java"));Assert.Throws<UnauthorizedAccessException>(()=>FileAccessIndexProfile.RequireDatabase(before.Database+"\n","java"));
    }
    /// <summary>真实STATISTICS必须包含相同主键、唯一性、列序、方向、前缀、可见性和方法；不能只查索引名。</summary>
    [Fact]public void Physical_statistics_reject_unknown_or_incomplete_index_properties()
    {
        foreach(var side in new[]{"java","csharp"})
        {
            var schema=Schema(side,true);var stats=Statistics(schema);Assert.Equal(17,stats.Count);FileAccessIndexMigration.RequireStatistics(stats,schema);var row=stats[1];
            foreach(var bad in new[]{row with{NonUnique=0},row with{Sequence=99},row with{Column=""},row with{Direction="D"},row with{Prefix="8"},row with{Kind="HASH"},row with{Visible="NO"},row with{Index="unrelated"}})
                Assert.Throws<InvalidOperationException>(()=>FileAccessIndexMigration.RequireStatistics([stats[0],bad,..stats.Skip(2)],schema));
            Assert.Throws<InvalidOperationException>(()=>FileAccessIndexMigration.RequireStatistics(stats.Skip(1).ToArray(),schema));Assert.Throws<InvalidOperationException>(()=>FileAccessIndexMigration.RequireStatistics([..stats,row],schema));
        }
    }
    /// <summary>旧用途、结束/缺少确认信息或额外动作不能读凭据或开连接；不执行数据库调用。</summary>
    [Fact]public void Dedicated_scope_and_command_allowlist_are_checked_before_connection()
    {
        var run=new string('a',32);Assert.Equal(run,FileAccessIndexMigration.RequireScope(run,"approved",FileAccessIndexProfile.Scope,FileAccessIndexProfile.Name));
        foreach(var (scope,profile,auth) in new[]{("paired-native-protected-file-measurement-v1",FileAccessIndexProfile.Name,"approved"),(FileAccessIndexProfile.Scope,"wrong","approved"),(FileAccessIndexProfile.Scope,FileAccessIndexProfile.Name,"pending")})Assert.Throws<UnauthorizedAccessException>(()=>FileAccessIndexMigration.RequireScope(run,auth,scope,profile));
        foreach(var action in new[]{"file-preview","file-apply-production","file-apply-java-extra","file-DROP-java"})Assert.Throws<ArgumentException>(()=>FileAccessIndexMigration.ParseMode([action]));Assert.Throws<ArgumentException>(()=>FileAccessIndexMigration.ParseMode(["file-apply-java","extra"]));Assert.Equal(("offline",(string?)null),FileAccessIndexMigration.ParseMode(["file-offline"]));Assert.Equal(("verify","csharp"),FileAccessIndexMigration.ParseMode(["file-verify-csharp"]));
    }
    /// <summary>测试构造有明确类型的内存快照；正式调用必须反查实际库和SHOW CREATE，而不是此模型。</summary>
    private static DatabaseSchema Schema(string side,bool desired)
    {
        var tables=FileAccessIndexProfile.Tables.Select(name=>
        {
            var indexed=FileAccessIndexProfile.Desired(name,side).SelectMany(i=>i.Columns).Append("id").ToHashSet(StringComparer.Ordinal);var columns=FileAccessIndexProfile.Columns(name,side).Select(column=>new ColumnSchema(column,column=="status"?(side=="java"?"varchar(50)":"varchar(20)"):column=="delete_no"?(side=="java"?"datetime":"datetime(6)"):indexed.Contains(column)?"bigint":"varchar(255)",column!="id",column=="id")).ToArray();var indexes=desired?FileAccessIndexProfile.Desired(name,side):FileAccessIndexProfile.Original(name,side);
            var ddl="CREATE TABLE `"+name+"` (\n `id` bigint NOT NULL,\n PRIMARY KEY (`id`),\n "+string.Join(",\n ",indexes.Select(i=>"KEY `"+i.Name+"` ("+string.Join(",",i.Columns.Select(c=>"`"+c+"`"))+")"))+"\n) ENGINE=InnoDB";return new TableSchema(name,columns,ddl,indexes);
        }).ToArray();return new("metarill_fresh_"+new string('a',16)+"_paired_"+side,tables);
    }
    /// <summary>用完整内存索引序列构造STATISTICS替身，反例分别破坏实际属性，不冒充实库元信息。</summary>
    private static List<FileIndexStatistic> Statistics(DatabaseSchema schema)
    {
        var rows=new List<FileIndexStatistic>();foreach(var table in schema.Tables){rows.Add(new(table.Name,"PRIMARY",0,1,"id","A","","BTREE","YES"));foreach(var index in table.Indexes!)for(var i=0;i<index.Columns.Count;i++)rows.Add(new(table.Name,index.Name,1,i+1,index.Columns[i],"A","","BTREE","YES"));}return rows;
    }
}
