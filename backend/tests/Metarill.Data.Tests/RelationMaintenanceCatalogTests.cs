using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>维护计划必须来自宿主发布模型，失败在发出任何数据库写入前暴露。</summary>
public sealed class RelationMaintenanceCatalogTests
{
    /// <summary>原下拉六字段与运行时冻结值保持，后续目录修改不能改变当前维护目标。</summary>
    [Fact]
    public void Published_table_options_are_frozen_and_keep_original_shape()
    {
        var model=Catalog();var plan=RelationMaintenanceCatalog.Compile(model);var option=Assert.Single(plan.Tables);Assert.Equal("source",option.Id);Assert.Equal(option.Id,option.Value);Assert.Equal("来源（maintenance_source）",option.Name);Assert.Equal(option.Name,option.Text);Assert.Equal("来源",option.RawName);model.ConfigTables[0].TableName="changed_after_compile";model.ConfigTables[0].Name="改名";Assert.Equal("maintenance_source",Assert.Single(plan.Tables).TableName);Assert.Equal(64,plan.Fingerprint.Length);
    }
    /// <summary>普通单级／多级路径与R0中间节点共享读取编译器，不依赖旧缓存物理列。</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Scalar_and_nonredundant_intermediate_plans_compile(bool intermediate)
    {
        var model=Catalog();if(intermediate){model.ConfigTables[1].IsRongyu=0;var name=model.ConfigColumns.Single(column=>column.Id=="target_name");name.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");model.ConfigTables.Add(new(){Id="company",TableName="maintenance_company",IsRongyu=1});AddBasics(model,"company");model.ConfigColumns.Add(new(){Id="target_company",ConfigTableId="target",ColumnName="company_id",LogicalType="int64",LinkConfigTableId="company"});model.ConfigColumns.Add(new(){Id="company_name",ConfigTableId="company",ColumnName="name",LogicalType="string",ColumnLength="100"});}
        var plan=RelationMaintenanceCatalog.Compile(model);using var database=Offline();using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new RelationMaintenanceExecutor(database).BackfillAsync(plan,"source",cancellation.Token));
    }
    /// <summary>同一表任何不支持字段必须拒绝整表，不能只执行可编译字段。</summary>
    [Theory][InlineData("missingPath")][InlineData("virtual")][InlineData("mismatch")][InlineData("project")][InlineData("credential")][InlineData("conflict")]
    public async Task Invalid_member_rejects_whole_table_before_connection(string kind)
    {
        var model=Catalog();var name=model.ConfigColumns.Single(column=>column.Id=="cache");if(kind=="missingPath")name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("unknown.name");if(kind=="virtual")name.IsVirtualColumn=1;if(kind=="mismatch")name.LogicalType="int64";if(kind=="project")model.ConfigColumns.RemoveAll(column=>column.Id=="target_project");if(kind=="credential"){model.ConfigColumns.Single(column=>column.Id=="target_name").ColumnName="password";name.Extra["columnFullName"]=JsonSerializer.SerializeToElement("target.password");}if(kind=="conflict")model.ConfigColumns.Single(column=>column.Id=="foreign").ControlDetail="{\"tableId\":\"target\",\"table\":\"wrong\"}";
        var plan=RelationMaintenanceCatalog.Compile(model);using var database=Offline();await Assert.ThrowsAsync<NotSupportedException>(()=>new RelationMaintenanceExecutor(database).BackfillAsync(plan,"source"));
    }
    /// <summary>非冗余表不出现在维护下拉，不能通过伪造表ID写入。</summary>
    [Fact]
    public async Task Nonredundant_or_unknown_table_cannot_be_selected()
    {
        var plan=RelationMaintenanceCatalog.Compile(Catalog());using var database=Offline();await Assert.ThrowsAsync<ArgumentException>(()=>new RelationMaintenanceExecutor(database).BackfillAsync(plan,"target"));await Assert.ThrowsAsync<ArgumentException>(()=>new RelationMaintenanceExecutor(database).BackfillAsync(plan,"source;DROP"));
    }
    /// <summary>整表没有派生列时沿原零结果返回，不建立数据库连接或启动自动修改结构。</summary>
    [Fact]
    public async Task Empty_table_returns_original_zero_result_without_database()
    {
        var model=Catalog();model.ConfigColumns.RemoveAll(column=>column.Id=="cache");var plan=RelationMaintenanceCatalog.Compile(model);using var database=Offline();var result=await new RelationMaintenanceExecutor(database).BackfillAsync(plan,"source");Assert.Equal(0,result.JoinColumnCount);Assert.Equal(0,result.AffectedRows);Assert.Empty(result.Details);Assert.Equal("maintenance_source",result.TableName);
    }
    /// <summary>测试结构仅含两个实体；数据访问必须在取消／拒绝前保持离线。</summary>
    private static DatabaseSession Offline()=>new(new(){Provider="mysql",ExpectedDatabase="offline",ConnectionString="Server=127.0.0.1;Port=1;Database=offline;User ID=unused;Password=unused"});
    internal static MetadataCatalog Catalog()
    {
        var model=new MetadataCatalog{YzProjectId="54",ConfigTables=[new(){Id="source",TableName="maintenance_source",Name="来源",IsRongyu=1},new(){Id="target",TableName="maintenance_target",IsRongyu=0}]};AddBasics(model,"source");AddBasics(model,"target");model.ConfigColumns.AddRange([new(){Id="foreign",ConfigTableId="source",ColumnName="target_id",LogicalType="int64",LinkConfigTableId="target"},new(){Id="cache",ConfigTableId="source",ColumnName="target_name",LogicalType="string",ColumnLength="100",Extra=new(){{"isJoinColumn",JsonSerializer.SerializeToElement(1)},{"columnFullName",JsonSerializer.SerializeToElement("target.name")}}},new(){Id="target_name",ConfigTableId="target",ColumnName="name",LogicalType="string",ColumnLength="100"}]);return model;
    }
    /// <summary>每一层独立保留真实项目和软删物理列。</summary>
    private static void AddBasics(MetadataCatalog model,string table)=>model.ConfigColumns.AddRange([new(){Id=table+"_id",ConfigTableId=table,ColumnName="id",LogicalType="int64",IsDbNotNull=1},new(){Id=table+"_project",ConfigTableId=table,ColumnName="yz_project_id",LogicalType="int64"},new(){Id=table+"_delete",ConfigTableId=table,ColumnName="delete_no",LogicalType="datetime"}]);
}
