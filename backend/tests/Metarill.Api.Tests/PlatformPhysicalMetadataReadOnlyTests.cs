using System.Runtime.CompilerServices;
using System.Text.Json;
using Metarill.Data;
using Metarill.Data.Schema;
using Metarill.Runtime;
using Metarill.Runtime.Metadata;
using Metarill.Runtime.Persistence;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;
namespace Metarill.Api.Tests;

/// <summary>仅加载当前平台元数据并生成内存结构预览；不执行同步、登录、初始化或任何业务写入。</summary>
public sealed class PlatformPhysicalMetadataReadOnlyTests(ITestOutputHelper output)
{
    /// <summary>真实当前配置必须直接通过完整合成；已知缺少元数据仍保持失败，不能以升级草案代替正式通过。</summary>
    [PlatformCompositionReadOnlyFact]
    public Task Server200_current_platform_metadata_composes_and_previews_without_applying_changes()=>WithCurrent(async(session,registry,current,metadata,token)=>
    {
        var files=MetadataRegistry.Load(Path.Combine(metadata,"files.json"));var security=MetadataRegistry.Load(Path.Combine(metadata,"security.json"));
        DescribeDifferences(current,files,security);
        var physical=new ProjectPhysicalModels(files,security,registry).Compose(current);
        var schema=new SchemaService(session);var plan=await schema.PreviewAsync(physical,token);Assert.Empty(schema.History);
        Assert.Equal(MySqlSchemaPlanner.Fingerprint(MySqlSchemaPlanner.Compile(physical,session.Options.ExpectedDatabase)),plan.DesiredFingerprint);
        output.WriteLine(JsonSerializer.Serialize(new{readOnly=true,currentMetadataAccepted=true,pendingChanges=plan.Changes.Count,ddlExecuted=false,dmlExecuted=false,applied=false}));
    });
    /// <summary>只在内存补齐已发布的缺失内部字段/索引，形成当前库的精确升级草案；不保存元数据、不Apply、不把草案当作当前配置通过。</summary>
    [PlatformCompositionReadOnlyFact]
    public Task Server200_fixed_foundation_upgrade_is_previewed_without_saving_metadata_or_applying_sql()=>WithCurrent(async(session,registry,current,metadata,token)=>
    {
        var files=MetadataRegistry.Load(Path.Combine(metadata,"files.json"));var security=MetadataRegistry.Load(Path.Combine(metadata,"security.json"));
        var models=new ProjectPhysicalModels(files,security,registry);var proposed=JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(JsonSerializer.Serialize(current))!;
        var missing=files.ConfigColumns.Concat(security.ConfigColumns).Where(expected=>current.ConfigColumns.All(column=>column.Id!=expected.Id)).ToArray();
        Assert.All(missing,column=>{Assert.Equal("2300000000000000401",column.Id);Assert.Equal("upload_expires_at",column.ColumnName);Assert.Equal("datetime",column.LogicalType);Assert.Equal(0,column.IsDbNotNull);});
        if(missing.Length>0)Assert.Throws<InvalidOperationException>(()=>models.Compose(current));
        foreach(var column in missing)proposed.ConfigColumns.Add(JsonSerializer.Deserialize<Metarill.Core.Metadata.ColumnMetadata>(JsonSerializer.Serialize(column))!);
        var expectedIndexes=files.ConfigIndexes.Concat(security.ConfigIndexes).ToArray();
        var missingIndexes=expectedIndexes.Where(expected=>current.ConfigIndexes.All(index=>index.GetProperty("id").ToString()!=expected.GetProperty("id").ToString())).ToArray();
        var allowedIndexes=new HashSet<string>(new[]{"2300000000000010000","900235310779076608","900235316722405376","900235322581848064"},StringComparer.Ordinal);
        Assert.All(missingIndexes,index=>Assert.Contains(index.GetProperty("id").ToString(),allowedIndexes));
        foreach(var index in missingIndexes)proposed.ConfigIndexes.Add(index.Clone());
        var expectedFields=files.ConfigIndexColumns.Concat(security.ConfigIndexColumns).ToArray();
        var missingFields=expectedFields.Where(expected=>current.ConfigIndexColumns.All(field=>field.GetProperty("id").ToString()!=expected.GetProperty("id").ToString())).ToArray();
        Assert.All(missingFields,field=>Assert.Contains(field.GetProperty("configIndexId").ToString(),allowedIndexes));
        foreach(var field in missingFields)proposed.ConfigIndexColumns.Add(field.Clone());
        // 全局标识检查包含墓碑/其它项目，不能把当前项目查不到当成可直接插入或复活。
        var store=new SqlRowStore(session,registry.Platform);
        foreach(var item in missing.Select(column=>(Table:"config_column",Id:column.Id))
            .Concat(missingIndexes.Select(index=>(Table:"config_index",Id:index.GetProperty("id").ToString())))
            .Concat(missingFields.Select(field=>(Table:"config_index_column",Id:field.GetProperty("id").ToString()))))
            Assert.Empty(await store.ReadIncludingDeletedAsync(item.Table,new Dictionary<string,object?>{{"id",long.Parse(item.Id,System.Globalization.CultureInfo.InvariantCulture)}},token));
        var physical=models.Compose(proposed);var schema=new SchemaService(session);var full=await schema.PreviewAsync(physical,token);
        // 审批材料只截取本次固定内部字段及索引所属表，避免夹带其它配置表的历史差异。
        var tableIds=missing.Select(column=>column.ConfigTableId).Concat(missingIndexes.Select(index=>index.GetProperty("configTableId").ToString())).ToHashSet(StringComparer.Ordinal);
        if(tableIds.Count==0)tableIds.Add(physical.ConfigTables.Single(table=>table.TableName=="yz_file_library").Id);
        var one=JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(JsonSerializer.Serialize(physical))!;
        one.ConfigTables.RemoveAll(table=>!tableIds.Contains(table.Id));one.ConfigColumns.RemoveAll(column=>!tableIds.Contains(column.ConfigTableId));one.ConfigPages.Clear();one.ConfigPageColumns.Clear();
        one.ConfigIndexes.RemoveAll(index=>!tableIds.Contains(index.GetProperty("configTableId").ToString()));var indexes=one.ConfigIndexes.Select(index=>index.GetProperty("id").ToString()).ToHashSet(StringComparer.Ordinal);
        one.ConfigIndexColumns.RemoveAll(field=>!indexes.Contains(field.GetProperty("configIndexId").ToString()));
        var filePlan=await schema.PreviewAsync(one,token);Assert.Empty(schema.History);
        Assert.All(filePlan.Changes,change=>
        {
            if(change.Kind=="addColumn"){Assert.Equal("yz_file_library",change.Table);Assert.Equal("upload_expires_at",change.Column);}
            else {Assert.Equal("addIndex",change.Kind);Assert.Contains(expectedIndexes,index=>one.ConfigTables.Any(table=>table.Id==index.GetProperty("configTableId").ToString()&&table.TableName==change.Table)&&change.Sql.Contains("INDEX `"+index.GetProperty("indexName").ToString()+"`",StringComparison.Ordinal));}
        });
        output.WriteLine(JsonSerializer.Serialize(new{readOnly=true,currentMetadataAccepted=missing.Length==0,proposedOnly=true,
            missingMetadata=missing.Select(column=>new{column.Id,column.ConfigTableId,column.ColumnName,column.LogicalType,column.IsDbNotNull}),missingIndexes,missingIndexFields=missingFields,
            allProjectPendingChanges=full.Changes.Count,fileTableChanges=filePlan.Changes,
            beforeFingerprint=filePlan.BeforeFingerprint,desiredFingerprint=filePlan.DesiredFingerprint,ddlExecuted=false,dmlExecuted=false,applied=false}));
    });
    /// <summary>两项验收共用实例/库名约束及实际快照加载，回调只能持有本次打开的会话。</summary>
    private async Task WithCurrent(Func<DatabaseSession,MetadataRegistry,Metarill.Core.Metadata.MetadataCatalog,string,CancellationToken,Task> verify)
    {
        var settings=Environment.GetEnvironmentVariable("METARILL_PLATFORM_COMPOSITION_READONLY_SETTINGS")!;
        var uuid=Environment.GetEnvironmentVariable("METARILL_PLATFORM_COMPOSITION_READONLY_SERVER_UUID")!;
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(settings));
        var metarill=Property(json.RootElement,"Metarill");var options=JsonSerializer.Deserialize<DatabaseOptions>(Property(metarill,"Database"),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var parsed=new MySqlConnectionStringBuilder(options.ConnectionString);
        Assert.Equal("mysql",options.Provider);Assert.Contains(parsed.Server,new[]{"192.168.31.200","deve-server.yinzon.com"});
        Assert.Contains(options.ExpectedDatabase,new[]{"metarill_test","metarill_dev"});parsed.ConnectionTimeout=10;options=options with{ConnectionString=parsed.ConnectionString};
        var metadata=MetadataFolder();var registry=new MetadataRegistry(Path.Combine(metadata,"platform.json"),Path.Combine(metadata,"demo.json"));
        using var session=new DatabaseSession(options);var repository=new MetadataRepository(new SqlRowStore(session,registry.Platform),registry);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var current=await session.InReadSnapshotAsync(async()=>
        {
            var identity=await session.Provider.ReadPhysicalDatabaseIdentityAsync(options,session.CurrentTransaction!,timeout.Token);Assert.Equal(uuid,identity.ServerId);
            return await repository.LoadProjectAsync(registry.Platform.YzProjectId,timeout.Token);
        },timeout.Token);
        Assert.Equal(registry.Platform.YzProjectId,current.YzProjectId);Assert.NotEmpty(current.ConfigPages);
        await verify(session,registry,current,metadata,timeout.Token);
    }
    /// <summary>只输出结构字段及默认是否不同，不回显默认文本、账号、口令或原始配置；帮助区分真实漂移与表示差异。</summary>
    private void DescribeDifferences(Metarill.Core.Metadata.MetadataCatalog current,params Metarill.Core.Metadata.MetadataCatalog[] infrastructure)
    {
        foreach(var source in infrastructure)
        foreach(var expected in source.ConfigColumns)
        {
            var actual=current.ConfigColumns.SingleOrDefault(column=>column.Id==expected.Id);
            if(actual is null){output.WriteLine("缺少字段标识："+expected.Id);continue;}
            object Shape(Metarill.Core.Metadata.ColumnMetadata column)=>new{column.Id,column.ConfigTableId,column.ColumnName,column.LogicalType,column.ColumnLength,column.IsDbNotNull,column.IsVirtualColumn,column.Precision,column.Scale,
                temporalPrecision=column.LogicalType is "datetime" or "time"?Metarill.Core.Metadata.MetadataColumnTypePolicy.TemporalPrecision(column):(int?)null};
            if(JsonSerializer.Serialize(Shape(actual))!=JsonSerializer.Serialize(Shape(expected))||actual.DefaultValue!=expected.DefaultValue)
                output.WriteLine(JsonSerializer.Serialize(new{actual=Shape(actual),expected=Shape(expected),defaultDifferent=actual.DefaultValue!=expected.DefaultValue}));
        }
        foreach(var source in infrastructure)
        foreach(var expected in source.ConfigIndexes)
        {
            static string Text(JsonElement row,string name)=>row.TryGetProperty(name,out var property)?property.ToString():"";
            var actual=current.ConfigIndexes.SingleOrDefault(index=>Text(index,"id")==Text(expected,"id"));
            if(actual.ValueKind!=JsonValueKind.Object){output.WriteLine("缺少索引标识："+Text(expected,"id"));continue;}
            object Shape(Metarill.Core.Metadata.MetadataCatalog model,JsonElement index)=>new{id=Text(index,"id"),table=Text(index,"configTableId"),name=Text(index,"indexName"),unique=Text(index,"isUnique"),type=Text(index,"indexType"),enabled=Text(index,"isEnabled"),
                fields=model.ConfigIndexColumns.Where(field=>Text(field,"configIndexId")==Text(index,"id")).Select(field=>new{id=Text(field,"id"),columnId=Text(field,"configColumnId"),name=Text(field,"columnName"),order=Text(field,"sortNo"),direction=Text(field,"sortDirection")}).OrderBy(field=>field.id,StringComparer.Ordinal).ToArray()};
            if(JsonSerializer.Serialize(Shape(current,actual))!=JsonSerializer.Serialize(Shape(source,expected)))
                output.WriteLine(JsonSerializer.Serialize(new{actualIndex=Shape(current,actual),expectedIndex=Shape(source,expected)}));
        }
    }
    /// <summary>明确配置键只能出现一次，不输出包含凭据的原始JSON。</summary>
    private static JsonElement Property(JsonElement value,string name)=>value.EnumerateObject().Single(property=>property.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
    /// <summary>从测试源文件定位当前已验证仓库，不从用户配置指定任意元数据来源。</summary>
    private static string MetadataFolder([CallerFilePath]string source="")
    {
        var root=new DirectoryInfo(Path.GetDirectoryName(source)!);
        while(!File.Exists(Path.Combine(root.FullName,"global.json")))root=root.Parent??throw new InvalidOperationException("找不到测试仓库。");
        return Path.Combine(root.FullName,"backend/metadata");
    }
}

/// <summary>普通回归默认跳过，不猜测连接或复用旧的实例身份。</summary>
public sealed class PlatformCompositionReadOnlyFactAttribute:FactAttribute
{
    /// <summary>仅当私有配置路径和本轮Server-200预检UUID同时显式给出时才执行。</summary>
    public PlatformCompositionReadOnlyFactAttribute()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_PLATFORM_COMPOSITION_READONLY_SETTINGS"))||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METARILL_PLATFORM_COMPOSITION_READONLY_SERVER_UUID")))
            Skip="未显式配置本轮平台结构组合只读验收。";
    }
}
