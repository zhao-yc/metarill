using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Schema;
using Metarill.Paired.Indexes;
using Xunit;
namespace Metarill.Paired.SqlObservation.Tests;

/// <summary>索引工具不能把旧三表DDL换成更简单模型，也不能执行计划之外的结构变更。</summary>
public sealed class RelationIndexProfileTests
{
    /// <summary>真实结构编译器只产生两项正确列序的普通索引，输入元数据不被修改。</summary>
    [Fact]public void Metadata_delta_is_only_two_nonunique_relation_indexes()
    {
        var baseline=Model();var before=JsonSerializer.Serialize(baseline);var desired=RelationIndexProfile.Build(baseline);
        Assert.Equal(before,JsonSerializer.Serialize(baseline));Assert.Equal(baseline.ConfigIndexes.Count+2,desired.ConfigIndexes.Count);Assert.Equal(baseline.ConfigIndexColumns.Count+6,desired.ConfigIndexColumns.Count);
        var changes=RelationIndexProfile.ExpectedChanges(baseline,"test_fixture");Assert.Equal(2,changes.Count);
        Assert.Contains(changes,x=>x.Table=="sample_contract"&&x.Sql.EndsWith("(`sample_customer_id`, `delete_no`, `id`)"));
        Assert.Contains(changes,x=>x.Table=="sample_contract_line"&&x.Sql.EndsWith("(`sample_contract_id`, `delete_no`, `id`)"));
        Assert.All(changes,x=>{Assert.Equal("addIndex",x.Kind);Assert.DoesNotContain("UNIQUE",x.Sql);});
        var plan=new SchemaChangePlan(new string('a',32),"mysql","test_fixture",new string('b',64),new string('c',64),changes);
        RelationIndexProfile.RequireChanges(plan,changes,false);RelationIndexProfile.RequireChanges(plan with{Changes=[]},changes,true);
        Assert.Equal(RelationIndexProfile.PlanSha256(plan),RelationIndexProfile.PlanSha256(plan with{Id=new string('d',32)}));
        Assert.NotEqual(RelationIndexProfile.PlanSha256(plan),RelationIndexProfile.PlanSha256(plan with{Database="other_fixture"}));
        Assert.NotEqual(RelationIndexProfile.PlanSha256(plan),RelationIndexProfile.PlanSha256(plan with{BeforeFingerprint=new string('d',64)}));
        foreach(var invalid in new[]{plan with{Changes=[]},plan with{Changes=[changes[0]]},plan with{Changes=[..changes,new("dropTable","foreign",null,"DROP TABLE foreign")]},plan with{Changes=[changes[0] with{Sql=changes[0].Sql+"; DELETE FROM foreign"},changes[1]]},plan with{Provider="sqlserver"}})
            Assert.Throws<InvalidOperationException>(()=>RelationIndexProfile.RequireChanges(invalid,changes,false));
        Assert.Throws<InvalidOperationException>(()=>RelationIndexProfile.RequireChanges(plan,changes,true));
    }
    /// <summary>修改原包、替代模型、重复升级和物理类型漂移都必须在数据库连接前拒绝。</summary>
    [Fact]public void Invalid_metadata_and_duplicate_profile_are_rejected_offline()
    {
        Assert.Throws<InvalidOperationException>(()=>RelationIndexProfile.ReadBaseline("{}"u8.ToArray()));
        var model=Model();Assert.Throws<InvalidOperationException>(()=>RelationIndexProfile.Build(RelationIndexProfile.Build(model)));
        model.ConfigColumns.Single(c=>c.ColumnName=="sample_customer_id").LogicalType="int64";Assert.Throws<InvalidOperationException>(()=>RelationIndexProfile.Build(model));
        model=Model();model.ConfigTables[0].TableName="simpler_table";Assert.Throws<InvalidOperationException>(()=>RelationIndexProfile.Build(model));
    }
    /// <summary>测试只用无效占位口令，任何账号、库或动作扩张均拒绝，不建立MySQL连接。</summary>
    [Fact]public void Online_gate_binds_both_databases_to_same_ephemeral_user()
    {
        var run=new string('a',32);var database="metarill_fresh_"+run[..16]+"_paired_csharp";var connection="Server=192.168.31.200;Database="+database+";User ID=mtf_"+run[..16]+";Password=unused-synthetic";
        Assert.Equal(database,Program.Options(run,"csharp",database,connection).ExpectedDatabase);Assert.EndsWith("_paired_java",Program.Options(run,"java",database,connection).ExpectedDatabase);
        Assert.Throws<UnauthorizedAccessException>(()=>Program.RequireRun(run,"pending"));Assert.Throws<UnauthorizedAccessException>(()=>Program.RequireRun(run+"\n","approved"));
        Assert.Throws<UnauthorizedAccessException>(()=>Program.Options(run,"java","production",connection));Assert.Throws<UnauthorizedAccessException>(()=>Program.Options(run,"java",database,connection.Replace("192.168.31.200","invalid.example")));Assert.Throws<UnauthorizedAccessException>(()=>Program.Options(run,"java",database,null));Assert.Throws<UnauthorizedAccessException>(()=>Program.Options(run,"java",database,connection.Replace("User ID=mtf_"+run[..16],"User ID=root")));
        foreach(var action in new[]{"preview","apply-java-extra","apply-production","--sql=DROP"})Assert.Throws<ArgumentException>(()=>Program.ParseMode([action]));
        Assert.Equal(("preview","java"),Program.ParseMode(["preview-java"]));Assert.Equal(("offline",(string?)null),Program.ParseMode(["offline"]));
    }
    /// <summary>最小类型合同用于检验编译器；正式入口另以原包字节摘要锁定完整46字段模型。</summary>
    private static MetadataCatalog Model()
    {
        var m=new MetadataCatalog{YzProjectId="810000000000000001",ConfigTables=[new(){Id="1",TableName="sample_customer"},new(){Id="2",TableName="sample_contract"},new(){Id="3",TableName="sample_contract_line"}]};
        foreach(var t in m.ConfigTables)m.ConfigColumns.AddRange([new(){Id=t.Id+"id",ConfigTableId=t.Id,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=t.Id+"delete",ConfigTableId=t.Id,ColumnName="delete_no",LogicalType="datetime"}]);
        m.ConfigColumns.AddRange([new(){Id="customer_ref",ConfigTableId="2",ColumnName="sample_customer_id",LogicalType="string",ColumnLength="50"},new(){Id="contract_ref",ConfigTableId="3",ColumnName="sample_contract_id",LogicalType="string",ColumnLength="50"}]);return m;
    }
}
