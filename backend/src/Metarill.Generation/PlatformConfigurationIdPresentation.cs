using System.Text.Json;

namespace Metarill.Generation;

/// <summary>平台六个旧字符串关联只优化存储类型，前端仍按原配置呈现；不改变后台类型、DTO或能力审计。</summary>
internal static class PlatformConfigurationIdPresentation
{
    private static readonly (string Table,string Column,string Id,string LegacyType)[] Fields=[
        ("config_child","child_config_page_id","1679391146044956674","nvarchar"),
        ("config_column","link_config_table_id","1670726646479638529","nvarchar"),
        ("config_column","link_config_page_id","1670726646546747393","nvarchar"),
        ("config_page_column","link_config_table_id","2200000000000000135","varchar"),
        ("config_page_column","link_config_page_id","2200000000000000137","varchar"),
        ("config_page_column","combine_super_column_id","2200000000000000139","varchar")];
    /// <summary>只为原平台目录创建展示副本；原模型及签发证明不变，其他项目不按字段名猜测回退。</summary>
    internal static GenerationModel ForFrontend(GenerationModel model)
    {
        if(model.Catalog.YzProjectId!="1608097412978577409"||model.TrustedSecurityServices is not null)return model;
        var matches=Fields.Where(rule=>model.Catalog.ConfigColumns.Any(column=>column.Id==rule.Id&&column.ColumnName==rule.Column&&column.LogicalType=="int64"&&model.Catalog.ConfigTables.Any(table=>table.Id==column.ConfigTableId&&table.TableName==rule.Table))).ToArray();
        if(matches.Length==0)return model;
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var catalog=JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(JsonSerializer.Serialize(model.Catalog,options),options)!;
        foreach(var rule in matches)
        {
            var column=catalog.ConfigColumns.Single(column=>column.Id==rule.Id);
            column.LogicalType="string";column.ColumnType=rule.LegacyType;column.ColumnLength="50";
            if(column.Extra.ContainsKey("columnTypeShow"))column.Extra["columnTypeShow"]=JsonSerializer.SerializeToElement("字符");
        }
        return GenerationModel.Create(catalog,model.Namespace);
    }
}
