using Metarill.Core.Metadata;
using Xunit;

namespace Metarill.Metadata.Tests;

/// <summary>元数据类型词表与局部更新豁免的纯规则，不将其等同于生成器支持列表。</summary>
public sealed class MetadataColumnTypePolicyTests
{
    [Fact]
    public void Metadata_types_include_existing_time_binary_and_double_without_assuming_generation_support()
    {
        foreach(var type in new[]{"int64","int32","string","decimal","double","date","time","datetime","bool","json","binary"})
        {Assert.True(MetadataColumnTypePolicy.IsSupportedLogicalType(type));Assert.Equal(type,MetadataColumnTypePolicy.Resolve("amount",type,"",false,false,false));}
        foreach(var type in new string?[]{null,""," ","int","Int64","point","varbinary"})Assert.False(MetadataColumnTypePolicy.IsSupportedLogicalType(type));
        Assert.Equal("decimal",MetadataColumnTypePolicy.Resolve("amount",null,"decimal",true,false,false));
        Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("amount",null,"decimal",true,true,false));
        Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("amount",null,"",true,false,false));
        Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("account_id","string","nvarchar",false,true,false));
        Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("sort_no","int64","bigint",false,true,false));
        Assert.Equal("int64",MetadataColumnTypePolicy.Resolve("client_id",null,"int",true,false,true));
        Assert.Equal("int64",MetadataColumnTypePolicy.Resolve("client_id","int32","int",false,false,true));
        Assert.Equal("int32",MetadataColumnTypePolicy.Resolve("sort_no",null,"nvarchar",true,false,true));
        Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("client_id","int32","int",false,true,true));
        Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("client_id","int32","int",false,false,false));
        foreach(var physical in new[]{"unknown","point","varbinary"})Assert.Throws<ArgumentException>(()=>MetadataColumnTypePolicy.Resolve("client_id",null,physical,true,false,true));
    }

    [Fact]
    public void Only_exact_empty_unchanged_original_and_omitted_type_fields_can_preserve_legacy_value()
    {
        Assert.True(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical("","decimal",false,false,"","decimal"));
        foreach(var physical in new string?[]{null," ","nvarchar"})Assert.False(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical(physical,"decimal",false,false,physical,"decimal"));
        Assert.False(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical("","invalid",false,false,"","invalid"));
        Assert.False(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical("","decimal",true,false,"","decimal"));
        Assert.False(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical("","decimal",false,true,"","decimal"));
        Assert.False(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical("","decimal",false,false,null,"decimal"));
        Assert.False(MetadataColumnTypePolicy.CanPreserveLegacyEmptyPhysical("","decimal",false,false,"","string"));
    }
}
