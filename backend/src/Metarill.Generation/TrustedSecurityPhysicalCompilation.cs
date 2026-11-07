using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Metarill.Generation;

/// <summary>物理合成只在编译时签发的管理目录中移除管理页声明，通用元数据不获得安全CRUD特权。</summary>
internal static class TrustedSecurityPhysicalCompilation
{
    /// <summary>普通生成保持严格合成；可信生成绑定完整目录摘要且只移除固定三页，结构仍经原合成器校验。</summary>
    internal static string Method(GenerationModel model)
    {
        var preparation="";
        if(model.TrustedSecurityServices is not null)
        {
            var tables=model.Tables.Where(table=>TrustedSecurityServiceCompilation.Applies(model,table)).ToArray();
            var pages=tables.SelectMany(table=>table.Pages).Select(page=>page.Source.Id).Order(StringComparer.Ordinal).ToArray();
            if(tables.Length!=3||pages.Length!=3)throw new InvalidOperationException("可信管理物理模型缺少固定三表三页。");
            var sha=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(model.Catalog,new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
            preparation=$$"""
        // 只有本次工厂签发目录的完整快照可使用固定管理页；调用方目录及物理输入仍不能变更。
        var json=System.Text.Json.JsonSerializer.Serialize(application,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        if(System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)))!={{JsonSerializer.Serialize(sha)}})
            throw new System.InvalidOperationException("管理宿主物理目录与编译快照不一致，请配套再生成并构建。");
        application=System.Text.Json.JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(json,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        string[] managementPages=[{{string.Join(",",pages.Select(page=>JsonSerializer.Serialize(page)))}}];
        application.ConfigPages.RemoveAll(page=>managementPages.Contains(page.Id,System.StringComparer.Ordinal));
        application.ConfigPageColumns.RemoveAll(field=>managementPages.Contains(field.ConfigPageId,System.StringComparer.Ordinal));

""";
        }
        var merge="Metarill.Core.Metadata.MetadataCatalogComposition.MergePhysical(Metarill.Core.Metadata.MetadataCatalogComposition.MergePhysical(application,files),security)";
        var completion=model.TrustedSecurityServices is null?"        return "+merge+";\n":
            "        var physical="+merge+";\n"+
            "        if(System.Text.Json.JsonSerializer.Serialize(physical,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!=System.Text.Json.JsonSerializer.Serialize(application,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))\n"+
            "            throw new System.InvalidOperationException(\"运行时基础物理对象超出可信管理编译目录。\");\n        return physical;\n";
        return "    /// <summary>只为结构命令建立物理模型，不修改发布页面、输入对象或数据库。</summary>\n"+
            "    public static Metarill.Core.Metadata.MetadataCatalog ComposePhysicalMetadata(Metarill.Core.Metadata.MetadataCatalog application,Metarill.Core.Metadata.MetadataCatalog files,Metarill.Core.Metadata.MetadataCatalog security)\n    {\n"+
            "        System.ArgumentNullException.ThrowIfNull(application);System.ArgumentNullException.ThrowIfNull(files);System.ArgumentNullException.ThrowIfNull(security);\n"+preparation+
            completion+"    }\n";
    }
}
