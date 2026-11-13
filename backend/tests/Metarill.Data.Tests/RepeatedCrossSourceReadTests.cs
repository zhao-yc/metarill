using System.Text.Json;
using System.Text.Json.Nodes;
using Metarill.Core.Metadata;
using Metarill.Data.Queries;
using Xunit;
namespace Metarill.Data.Tests;

/// <summary>数据层固定角色联接证明不依赖生成器，重复表不能合并别名或把稀疏位当真实父键。</summary>
public sealed class RepeatedCrossSourceReadTests
{
    /// <summary>四个重复角色图、四种键、三种名称模式均编译完整逻辑ID及名称。</summary>
    [Theory]
    [InlineData(3,"string","string","physical")]
    [InlineData(3,"string","string","derived")]
    [InlineData(3,"string","string","mixed")]
    [InlineData(3,"string","int64","physical")]
    [InlineData(3,"string","int64","derived")]
    [InlineData(3,"string","int64","mixed")]
    [InlineData(3,"int64","string","physical")]
    [InlineData(3,"int64","string","derived")]
    [InlineData(3,"int64","string","mixed")]
    [InlineData(3,"int64","int64","physical")]
    [InlineData(3,"int64","int64","derived")]
    [InlineData(3,"int64","int64","mixed")]
    [InlineData(5,"string","string","physical")]
    [InlineData(5,"string","string","derived")]
    [InlineData(5,"string","string","mixed")]
    [InlineData(5,"string","int64","physical")]
    [InlineData(5,"string","int64","derived")]
    [InlineData(5,"string","int64","mixed")]
    [InlineData(5,"int64","string","physical")]
    [InlineData(5,"int64","string","derived")]
    [InlineData(5,"int64","string","mixed")]
    [InlineData(5,"int64","int64","physical")]
    [InlineData(5,"int64","int64","derived")]
    [InlineData(5,"int64","int64","mixed")]
    [InlineData(6,"string","string","physical")]
    [InlineData(6,"string","string","derived")]
    [InlineData(6,"string","string","mixed")]
    [InlineData(6,"string","int64","physical")]
    [InlineData(6,"string","int64","derived")]
    [InlineData(6,"string","int64","mixed")]
    [InlineData(6,"int64","string","physical")]
    [InlineData(6,"int64","string","derived")]
    [InlineData(6,"int64","string","mixed")]
    [InlineData(6,"int64","int64","physical")]
    [InlineData(6,"int64","int64","derived")]
    [InlineData(6,"int64","int64","mixed")]
    [InlineData(7,"string","string","physical")]
    [InlineData(7,"string","string","derived")]
    [InlineData(7,"string","string","mixed")]
    [InlineData(7,"string","int64","physical")]
    [InlineData(7,"string","int64","derived")]
    [InlineData(7,"string","int64","mixed")]
    [InlineData(7,"int64","string","physical")]
    [InlineData(7,"int64","string","derived")]
    [InlineData(7,"int64","string","mixed")]
    [InlineData(7,"int64","int64","physical")]
    [InlineData(7,"int64","int64","derived")]
    [InlineData(7,"int64","int64","mixed")]
    public void Roles_preserve_fixed_scalar_outputs_and_metadata(int mask,string type,string parentType,string mode)
    {
        var c=Catalog(mask,type,parentType,mode);var before=JsonSerializer.Serialize(c);var plan=NonRedundantReads.CompileWithCrossTables(c,"1",["id","country_id","country_name","area_id","area_name","place_id","place_name"],Scopes(c));Assert.Equal(7,plan.OutputTypes.Count);foreach(var prefix in new[]{"country","area","place"}){Assert.Equal(type,plan.OutputTypes[prefix+"_id"]);Assert.Equal("string",plan.OutputTypes[prefix+"_name"]);}Assert.Equal(before,JsonSerializer.Serialize(c));
    }
    /// <summary>所有父结构都须是独立物理外键，未知范围、隐藏绑定及凭据终点不能借重复表绕过。</summary>
    [Theory][InlineData("sparseParent")][InlineData("derivedParent")][InlineData("global")][InlineData("view")][InlineData("extra")][InlineData("credential")][InlineData("missingBinding")]
    public void Invalid_role_proofs_fail_before_connecting(string kind)
    {
        var c=Catalog(7,"string","string","derived");var source=c.ConfigColumns.Single(x=>x.Id=="15");var json=JsonNode.Parse(source.ControlDetail)!;
        if(kind=="sparseParent")json["levels"]![1]!["parentField"]="country_id";
        if(kind=="derivedParent"){var parent=c.ConfigColumns.Single(x=>x.Id=="16");parent.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);parent.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.id");}
        if(kind=="global")c.ConfigTables.Single(x=>x.Id=="1").Extra["scopeKind"]=JsonSerializer.SerializeToElement("global");
        if(kind=="view")c.ConfigTables.Single(x=>x.Id=="1").IsView=1;
        if(kind=="extra")json["levels"]![1]!["whereSql"]="1=1";
        if(kind=="credential"){c.ConfigColumns.Single(x=>x.Id=="62").ColumnName="password";c.ConfigColumns.Single(x=>x.Id=="19").Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.password");}
        source.ControlDetail=json.ToJsonString();var ex=Assert.ThrowsAny<Exception>(()=>NonRedundantReads.CompileWithCrossTables(c,"1",["id","country_name","area_name","place_name"],kind=="missingBinding"?[]:Scopes(c)));if(kind=="sparseParent")Assert.Contains("稀疏选择位",ex.Message);
    }
    /// <summary>固定页只含项目变量；动作权限由生成宿主负责，本计划不能授予用户权限。</summary>
    private static NonRedundantCrossTableScope[] Scopes(MetadataCatalog c)
    {
        using var json=JsonDocument.Parse(c.ConfigColumns.Single(x=>x.Id=="15").ControlDetail);var levels=json.RootElement.GetProperty("levels").EnumerateArray().Select(x=>c.ConfigTables.Single(t=>t.TableName==x.GetProperty("table").GetString()).Id+"01").Select(id=>new NonRedundantCrossTableTargetScope(id)).ToArray();return[new("15",levels)];
    }
    /// <summary>一个来源表和未占用角色的独立目标，真实父字段始终与country/area/place_id存储位分离。</summary>
    private static MetadataCatalog Catalog(int mask,string type,string parentType,string mode)
    {
        var c=new MetadataCatalog{YzProjectId="9007199254740993"};var sourceRoles=Enumerable.Range(0,3).Where(i=>(mask&(1<<i))!=0).ToArray();var targets=Enumerable.Range(0,3).Select(i=>sourceRoles.Contains(i)?"1":(i+2).ToString()).ToArray();var names=new[]{"country","area","place"};
        foreach(var id in targets.Append("1").Distinct(StringComparer.Ordinal)){var table=id=="1"?"repeat_data_source":"repeat_data_target_"+id;c.ConfigTables.Add(new(){Id=id,YzProjectId=c.YzProjectId,TableName=table,IsRongyu=id=="1"?0:1});c.ConfigColumns.AddRange([Column(id+"1",id,"id","int64",true),Column(id+"2",id,"name","string"),Column(id+"3",id,"yz_project_id","int64"),Column(id+"4",id,"delete_no","datetime")]);c.ConfigPages.Add(new(){Id=id+"01",YzProjectId=c.YzProjectId,ConfigTableId=id,WhereSql="yz_project_id=@current_yz_project_id"});}
        foreach(var index in Enumerable.Range(1,2).Where(i=>targets[i]!="1")){var parent=Column(targets[index]+"5",targets[index],"parent_id",parentType);parent.LinkConfigTableId=targets[index-1];c.ConfigColumns.Add(parent);}
        var rootParent=Column("16","1","real_country_id",parentType);rootParent.LinkConfigTableId=targets[0];var leafParent=Column("17","1","real_area_id",parentType);leafParent.LinkConfigTableId=targets[1];c.ConfigColumns.AddRange([rootParent,leafParent]);
        if(mode!="physical"){c.ConfigTables.Add(new(){Id="6",YzProjectId=c.YzProjectId,TableName="repeat_data_company",IsRongyu=1});c.ConfigColumns.AddRange([Column("61","6","id","int64",true),Column("62","6","name","string"),Column("63","6","yz_project_id","int64"),Column("64","6","delete_no","datetime")]);var fk=Column("18","1","company_id","int64");fk.LinkConfigTableId="6";var label=Column("19","1","display_label","string");label.Extra["isJoinColumn"]=JsonSerializer.SerializeToElement(1);label.Extra["columnFullName"]=JsonSerializer.SerializeToElement("company.name");c.ConfigColumns.AddRange([fk,label]);}
        var entries=Enumerable.Range(0,3).Select(i=>new{key=names[i],table=c.ConfigTables.Single(t=>t.Id==targets[i]).TableName,columnPrefix=names[i],parentField=i==0?null:targets[i]=="1"?(i==1?"real_country_id":"real_area_id"):"parent_id",nameField=targets[i]=="1"&&mode!="physical"&&(mode!="mixed"||i!=sourceRoles[0])?"display_label":"name"}).ToArray();var control=Column("15","1","place_id",type);control.LinkConfigTableId=targets[2];control.ControlKind="CascadeTree";control.ControlDetail=JsonSerializer.Serialize(new{sourceMode="crossTable",levels=entries});c.ConfigColumns.Add(control);return c;
    }
    /// <summary>所有实例只在内存编译，主键按实际实体约束声明。</summary>
    private static ColumnMetadata Column(string id,string table,string name,string type,bool required=false)=>new(){Id=id,ConfigTableId=table,ColumnName=name,LogicalType=type,IsDbNotNull=required?1:0,ColumnLength="64"};
}
