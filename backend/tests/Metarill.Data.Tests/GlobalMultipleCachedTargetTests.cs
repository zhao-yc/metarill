using System.Collections;
using System.Reflection;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Metarill.Data.Relations;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>原多选/数据库复选引用全局名称副本，仍要求完整物理List生产链和普通来源项目。</summary>
public sealed class GlobalMultipleCachedTargetTests
{
    /// <summary>两种数组控件、R0/R1及内层两种物理键均沿同一受管名称证明，编译不修改元数据。</summary>
    [Theory][InlineData(0,"MultiSelectPage","int64")][InlineData(0,"MultiSelectPage","string")][InlineData(0,"MultiCheckBoxDatabase","int64")][InlineData(0,"MultiCheckBoxDatabase","string")]
    [InlineData(1,"MultiSelectPage","int64")][InlineData(1,"MultiSelectPage","string")][InlineData(1,"MultiCheckBoxDatabase","int64")][InlineData(1,"MultiCheckBoxDatabase","string")]
    public void Proven_global_cached_name_is_accepted_for_both_array_controls(int mode,string kind,string inner)
    {
        var c=Catalog(mode,kind,inner);var before=JsonSerializer.Serialize(c);var owner=c.ConfigTables.Single(x=>x.Id=="3");var target=c.ConfigTables.Single(x=>x.Id=="1");var foreign=c.ConfigColumns.Single(x=>x.Id=="34");
        var proof=mode==0?GlobalMultipleRelationTarget.Require(c,owner,foreign,target):GlobalMultipleRelationTarget.RequireCached(c,owner,foreign,target);Assert.Equal("14",proof.Name.Id);Assert.Equal("10",proof.Page.Id);Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>实际SQL仍以窗口顺序及左联接保留缺失位置，名称EXISTS筛选不扩大根行，来源项目不可省略。</summary>
    [Theory][InlineData("MultiSelectPage","int64")][InlineData("MultiSelectPage","string")][InlineData("MultiCheckBoxDatabase","int64")][InlineData("MultiCheckBoxDatabase","string")]
    public void Array_read_preserves_scope_null_positions_and_exact_ids(string kind,string inner)
    {
        var c=Catalog(0,kind,inner);var plan=MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_names"]);var expressions=Get<IReadOnlyDictionary<string,string>>(plan,"Expressions");var sql=expressions["global_source_global_target_names"];
        Assert.Contains("JSON_ARRAYAGG(multi_value.`global_target_name`)",sql);Assert.Contains("LEFT JOIN `global_source`",sql);Assert.Contains("ORDER BY multi_ids.ordinal",sql);Assert.Contains("AS BINARY",sql);Assert.Contains("multi_value.`delete_no` IS NULL",sql);Assert.DoesNotContain("multi_value.`yz_project_id`",sql);Assert.DoesNotContain("JOIN `global_target`",sql);Assert.Contains("b.`yz_project_id`=@project_scope_number",Get<string>(plan,"Scope"));Assert.Contains("LIKE {parameter} ESCAPE '!'",Get<IReadOnlyDictionary<string,string>>(plan,"ArrayNamePredicates")["global_source_global_target_names"]);Assert.Equal("string",NonRedundantReads.Compile(c,"3",["global_source_global_target_names"]).OutputTypes["global_source_global_target_names"]);
    }
    /// <summary>原R1数组缓存绑定仍保留来源项目、无项目全局目标和原200/5000预算。</summary>
    [Theory][InlineData("MultiSelectPage","int64")][InlineData("MultiSelectPage","string")][InlineData("MultiCheckBoxDatabase","int64")][InlineData("MultiCheckBoxDatabase","string")]
    public void Array_cache_enters_existing_physical_propagation_with_original_budget(string kind,string inner)
    {
        var c=Catalog(1,kind,inner);var plan=RelationMutationPlan.Compile(c,[new("3","34","1","14","35",true)]);var binding=Assert.Single(Get<IEnumerable>(plan,"Bindings").Cast<object>());
        Assert.Equal("int64",Get<string>(Get<object>(binding,"Source"),"ProjectType"));Assert.Equal("",Get<string>(Get<object>(binding,"Target"),"ProjectType"));Assert.Equal("global_target_name",Get<string>(binding,"Value"));Assert.Equal("global_source_global_target_names",Get<string>(binding,"Cache"));Assert.True(Get<bool>(binding,"Multiple"));Assert.Equal(kind=="MultiSelectPage"?200:5000,Get<int>(binding,"MaximumItems"));
    }
    /// <summary>全局名称副本的允许范围不把数组伪装成固定单值匹配。</summary>
    [Theory][InlineData("MultiSelectPage")][InlineData("MultiCheckBoxDatabase")]
    public void Array_cannot_become_a_scalar_match(string kind)
    {Assert.Throws<NotSupportedException>(()=>NonRedundantReads.CompileMatch(Catalog(0,kind,"int64"),"3","global_source_global_target_names"));}
    /// <summary>缺来源/生产链/终点证明时读计划和R1传播均拒绝，动态条件不能借名称新能力进入。</summary>
    [Theory][InlineData("sourceProject")][InlineData("sourceGlobal")][InlineData("producerProject")][InlineData("producerR0")][InlineData("privateTerminal")][InlineData("unpublished")][InlineData("missingPage")][InlineData("badPath")][InlineData("credential")][InlineData("virtual")][InlineData("file")][InlineData("array")][InlineData("conditionCache")][InlineData("cycle")][InlineData("multiProducer")]
    public void Unproven_array_target_rejects_read_and_cache_before_connection(string invalid)
    {
        foreach(var kind in new[]{"MultiSelectPage","MultiCheckBoxDatabase"})foreach(var mode in new[]{0,1})
        {
            var c=Catalog(mode,kind,"int64");var cache=c.ConfigColumns.Single(x=>x.Id=="14");var producer=c.ConfigTables.Single(x=>x.Id=="1");var foreign=c.ConfigColumns.Single(x=>x.Id=="34");var upstream=c.ConfigColumns.Single(x=>x.Id=="13");
            switch(invalid)
            {
                case "sourceProject":c.ConfigColumns.RemoveAll(x=>x.Id=="32");break;
                case "sourceGlobal":c.ConfigTables.Single(x=>x.Id=="3").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");break;
                case "producerProject":c.ConfigColumns.Add(new(){Id="19",YzProjectId=c.YzProjectId,ConfigTableId="1",ColumnName="yz_project_id",LogicalType="int64"});break;
                case "producerR0":producer.IsRongyu=0;break;
                case "privateTerminal":c.ConfigTables.Single(x=>x.Id=="2").Extra.Remove("scopeKind");break;
                case "unpublished":c.ConfigPages.Single(x=>x.Id=="20").YzProjectId="101";break;
                case "missingPage":c.ConfigPages.RemoveAll(x=>x.Id=="20");break;
                case "badPath":cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("missing.name");break;
                case "credential":c.ConfigColumns.Single(x=>x.Id=="22").ColumnName="password";upstream.ControlDetail=upstream.ControlDetail.Replace("\"name\"","\"password\"",StringComparison.Ordinal);break;
                case "virtual":cache.IsVirtualColumn=1;break;
                case "file":cache.ControlKind="FileLibrary";break;
                case "array":cache.ControlKind="MultiSelectPage";break;
                case "conditionCache":c.ConfigPages.Single(x=>x.Id=="10").WhereSql="global_target_name='甲'";break;
                case "cycle":upstream.LinkConfigTableId="1";upstream.ControlDetail="{\"tableId\":\"1\",\"table\":\"global_source\",\"pageId\":\"10\",\"nameField\":\"global_target_name\"}";cache.Extra["columnFullName"]=JsonSerializer.SerializeToElement("global_target.global_target_name");break;
                case "multiProducer":upstream.ControlKind="MultiSelectPage";break;
            }
            if(mode==0)Assert.ThrowsAny<Exception>(()=>MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_names"]));
            else Assert.ThrowsAny<Exception>(()=>RelationMutationPlan.Compile(c,[new("3",foreign.Id,"1","14","35",true)]));
        }
    }
    /// <summary>普通物理字符串仍可作名称，不能因新缓存证明强迫所有字段带Join标记。</summary>
    [Theory][InlineData("MultiSelectPage")][InlineData("MultiCheckBoxDatabase")]
    public void Original_physical_name_remains_valid(string kind)
    {var c=Catalog(0,kind,"int64");c.ConfigColumns.Single(x=>x.Id=="14").Extra.Clear();Assert.NotNull(MySqlNonRedundantReadCompiler.Compile(c,"3",["global_source_global_target_names"]));}
    /// <summary>旧空/四段默认name不变，新JSON缓存标识符往返保留，非法名称及凭据仍拒绝。</summary>
    [Theory][InlineData("")][InlineData("a;b;status=1;name desc")][InlineData("{\"nameField\":\"global_target_name\"}")]
    public void Checkbox_label_preserves_old_forms_and_explicit_cached_field(string text)
    {var definition=DatabaseCheckboxDefinition.Parse(text);var name=text.StartsWith('{')?"global_target_name":"name";Assert.Equal(name,definition.NameField);Assert.Equal(name,definition.ReadDefinition(new()).GetProperty("nameField").GetString());}
    /// <summary>名称标识符不能提供SQL、数组、凭据或大小写歧义映射。</summary>
    [Theory][InlineData("{\"nameField\":\"password\"}")][InlineData("{\"nameField\":\"name;drop\"}")][InlineData("{\"nameField\":[]}")][InlineData("{\"NameField\":\"name\"}")]
    public void Invalid_checkbox_label_rejects_during_parse(string text)=>Assert.ThrowsAny<Exception>(()=>DatabaseCheckboxDefinition.Parse(text));
    /// <summary>复用已验三表生产链，仅以验收元数据将普通来源改为一种原数组控件。</summary>
    internal static MetadataCatalog Catalog(int mode,string kind,string inner)
    {
        var c=GlobalCachedTargetTests.Catalog(mode,"int64",inner);var foreign=c.ConfigColumns.Single(x=>x.Id=="34");foreign.ColumnName="global_source_ids";foreign.LogicalType="string";foreign.ColumnLength="max";foreign.ControlKind=kind;
        var cache=c.ConfigColumns.Single(x=>x.Id=="35");cache.ColumnName="global_source_global_target_names";cache.ColumnLength="max";return c;
    }
    /// <summary>只读实际编译结果检查范围，不复制SQL或传播实现。</summary>
    private static T Get<T>(object source,string name)=>(T)source.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(source)!;
}
