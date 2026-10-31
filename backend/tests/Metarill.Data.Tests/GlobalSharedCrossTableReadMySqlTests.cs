namespace Metarill.Data.Tests;

/// <summary>Server-200隔离表覆盖全局共享前缀、独立末端、混合范围及短控件，不计为生成写入验收。</summary>
public sealed class GlobalSharedCrossTableReadMySqlTests
{
    /// <summary>保留项目列的全局国家来自另一个项目，来源项目过滤仍保留。</summary>
    [MySqlIntegrationFact]public Task String_string_global_country()=>SharedCrossTableReadMySqlTests.Exercise("string","string",globalMask:1);
    /// <summary>共享地区无项目列，其余层级仍使用项目范围。</summary>
    [MySqlIntegrationFact]public Task String_integer_global_area_without_project()=>SharedCrossTableReadMySqlTests.Exercise("string","int64",globalMask:2,noProject:true);
    /// <summary>独立城市全局不改变另一仓库分支的范围。</summary>
    [MySqlIntegrationFact]public Task Integer_string_global_place()=>SharedCrossTableReadMySqlTests.Exercise("int64","string",globalMask:4);
    /// <summary>独立仓库没有项目列，四键组合继续精确恢复。</summary>
    [MySqlIntegrationFact]public Task Integer_integer_global_warehouse_without_project()=>SharedCrossTableReadMySqlTests.Exercise("int64","int64",globalMask:8,noProject:true);
    /// <summary>全部目标全局且无项目列，来源仍是正常项目实体。</summary>
    [MySqlIntegrationFact]public Task All_targets_without_project()=>SharedCrossTableReadMySqlTests.Exercise("string","int64",globalMask:15,noProject:true);
    /// <summary>短控件的共同国家／地区均全局，只有仓库独立存储。</summary>
    [MySqlIntegrationFact]public Task Short_string_global_shared_prefix()=>SharedCrossTableReadMySqlTests.Exercise("string","int64",shorter:true,globalMask:3);
    /// <summary>短控件实际三层都无项目列，当前读仍锁定所有已引用节点。</summary>
    [MySqlIntegrationFact]public Task Short_integer_all_without_project()=>SharedCrossTableReadMySqlTests.Exercise("int64","string",shorter:true,globalMask:11,noProject:true);
}
