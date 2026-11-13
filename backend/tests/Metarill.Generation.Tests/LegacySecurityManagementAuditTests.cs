using System.Security.Cryptography;
using System.Text.Json;
using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Generation.Tests;

/// <summary>旧管理包只恢复编译内原资产，新包明确采用含日志的新资产；不接受磁盘自报版本。</summary>
public sealed class LegacySecurityManagementAuditTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true,NewLine="\n"};
    /// <summary>用真实旧工厂模型生成五份来源，再从磁盘重签发；新模型只多日志而不改页面。</summary>
    [Fact]
    public async Task Original_management_source_roundtrips_after_audit_foundation_upgrade()
    {
        var source=NonRedundantApplicationFixture.Root();var app=JsonSerializer.Deserialize<MetadataCatalog>(File.ReadAllText(Path.Combine(source,"backend/metadata/demo.json")),Json)!;
        var assembly=typeof(CSharpGenerator).Assembly;using var checksum=new StreamReader(assembly.GetManifestResourceStream("Metarill.Generation.SecurityManagement.V1.Legacy.sha256")!);var legacySha=(await checksum.ReadToEndAsync()).Trim();
        var original=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id,legacySha);var current=SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id);
        Assert.Throws<InvalidOperationException>(()=>SecurityManagementModuleFactory.RequireCurrentForPublication(original));SecurityManagementModuleFactory.RequireCurrentForPublication(current);
        Assert.NotEqual(original.Binding.ModuleSha256,current.Binding.ModuleSha256);Assert.Equal(10,original.Physical.ConfigTables.Count);Assert.Equal(11,current.Physical.ConfigTables.Count);
        Assert.DoesNotContain(original.Physical.ConfigTables,table=>table.TableName=="yz_manager_log");Assert.Single(current.Physical.ConfigTables,table=>table.TableName=="yz_manager_log");
        Assert.Equal(JsonSerializer.Serialize(original.Published.ConfigPages,Json),JsonSerializer.Serialize(current.Published.ConfigPages,Json));Assert.Equal(JsonSerializer.Serialize(original.Published.ConfigPageColumns,Json),JsonSerializer.Serialize(current.Published.ConfigPageColumns,Json));
        var boundary=Path.Combine(Path.GetTempPath(),"metarill-legacy-audit-module");var root=Path.Combine(boundary,Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);const string ns="Example.LegacyAudit";var generated=new CSharpGenerator().Generate(original,Path.Combine(root,"backend/generated"),ns);
            byte[] Write(string path,object value){var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,Json)+"\n");var target=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllBytes(target,bytes);return bytes;}
            var appBytes=Write(PreparedSecurityManagementSource.ApplicationPath,GenerationMetadataPublisher.Publish(app).Metadata);var published=Write("backend/metadata/project.json",original.Published);var foundations=SecurityManagementModuleFactory.RequireFoundationsForPreparation(original);
            Write("backend/metadata/files.json",foundations.Files);Write("backend/metadata/security.json",foundations.Security);
            var owned=PreparedSecurityManagementSource.OwnedMetadataPaths.Select(path=>new{path,sha256=Hash(File.ReadAllBytes(Path.Combine(root,path)))}).ToArray();
            Write(".metarill-management-preparation.json",new{schemaVersion=1,profile="management-preparation-v1",readyForPublication=false,rootNamespace=ns,projectId=app.YzProjectId,managementAdminTypeId=app.YzAdminTypes[0].Id,moduleSha256=legacySha,applicationMetadataSha256=Hash(appBytes),metadataSha256=generated.MetadataSha256,securityCatalogSha256=Hash(published),files=owned});
            var restored=PreparedSecurityManagementSource.Read(root);Assert.Equal(legacySha,restored.Module.Binding.ModuleSha256);Assert.Equal(CSharpGenerator.CanonicalMetadata(original.Published),CSharpGenerator.CanonicalMetadata(restored.Module.Published));
            Assert.Throws<InvalidOperationException>(()=>SecurityManagementModuleFactory.Compose(app,app.YzAdminTypes[0].Id,new string('0',64)));
        }
        finally{await TestDirectoryCleanup.DeleteAsync(root,boundary);}
    }
    /// <summary>摘要只校验公开元数据字节，不记录任何本机配置。</summary>
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
