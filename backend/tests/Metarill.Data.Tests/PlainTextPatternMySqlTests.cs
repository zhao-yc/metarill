using System.Data.Common;
using System.Text.Json;
using Metarill.Data.Queries;
using Metarill.Data.Schema;
using MySqlConnector;
using Xunit;

namespace Metarill.Data.Tests;

public sealed partial class JsonArrayNameQueryMySqlTests
{
    /// <summary>原参数化LIKE作为独立基线，实测R1/R0分页、COUNT与字面名称模式；只操作唯一合成表。</summary>
    [MySqlIntegrationFact]public async Task Ordinary_text_pattern_and_literal_names_keep_original_mysql_semantics()
    {
        var options=await ReadOptions();using var session=new DatabaseSession(options);var schema=new SchemaService(session);var table="mt_test_json_names_"+Guid.NewGuid().ToString("N")[..20];var catalog=Catalog(table);catalog.YzProjectId="17";
        await using(var pin=new MySqlConnection(options.ConnectionString)){await pin.OpenAsync();await using var cmd=pin.CreateCommand();cmd.CommandText="SELECT DATABASE(),@@hostname,@@server_uuid";await using var r=await cmd.ExecuteReaderAsync();Assert.True(await r.ReadAsync());Assert.Equal("metarill_business_test",r.GetString(0));Assert.Equal("3107e214a8a3",r.GetString(1));Assert.Equal("6d3d024a-9d73-11f1-9939-ce69308a21dc",r.GetString(2));}
        DatabaseSchema? installed=null;IReadOnlyList<NamesRow> written=[];var created=false;var calls=new List<(string Sql,object?[] Values)>();
        output.WriteLine("本轮唯一合成表："+table);
        session.Orm.Aop.CommandBefore+=(_,args)=>{if(args.Command.CommandText.Contains("mt_text_pattern_",StringComparison.Ordinal))calls.Add((args.Command.CommandText,args.Command.Parameters.Cast<DbParameter>().Select(p=>p.Value).ToArray()));};
        try
        {
            Assert.Empty((await schema.InspectAsync(catalog)).Tables);var plan=await schema.PreviewAsync(catalog);Assert.Equal("createTable",Assert.Single(plan.Changes).Kind);created=true;Assert.True((await schema.ApplyAsync(plan)).Succeeded);installed=await schema.InspectAsync(catalog);
            var seeds=new[]{new NamesRow{Id=1,ProjectId=17,ParentId=5,Names="Café"},new NamesRow{Id=2,ProjectId=17,ParentId=5,Names="Cafe"},new NamesRow{Id=3,ProjectId=17,ParentId=5,Names="xAy"},new NamesRow{Id=4,ProjectId=17,ParentId=5,Names="x_y"},new NamesRow{Id=5,ProjectId=17,ParentId=5,Names="中文'_%!\\"},new NamesRow{Id=6,ProjectId=18,ParentId=5,Names="Cafe"},new NamesRow{Id=7,ProjectId=17,ParentId=6,Names="Cafe"},new NamesRow{Id=8,ProjectId=17,ParentId=5,Names="Cafe",DeleteNo=new(2026,1,1)}};
            await session.Orm.Insert(seeds).AsTable(table).ExecuteAffrowsAsync();written=await session.Select<NamesRow>().AsTable((_,_)=>table).OrderBy(r=>r.Id).ToListAsync();Assert.Equal(8,written.Count);
            var readCatalog=JsonSerializer.Deserialize<Metarill.Core.Metadata.MetadataCatalog>(JsonSerializer.Serialize(catalog))!;readCatalog.ConfigTables[0].IsRongyu=0;var readPlan=MySqlNonRedundantReadCompiler.Compile(readCatalog,"json_names",["id","names","parent_id"]);var reader=new MySqlNonRedundantReader(session);
            foreach(var keyword in new[]{"cafe","x_y","%","'","中文","%_!","%' OR 1=1 --"})
            {
                var expected=await Baseline(keyword);var query=StringNameQuery.ApplyPattern(session,session.Select<NamesRow>().AsTable((_,_)=>table).Where(r=>r.ProjectId==17&&r.ParentId==5&&r.DeleteNo==null),r=>r.Names,keyword);
                Assert.Equal(expected,await query.OrderBy(r=>r.Id).ToListAsync(r=>r.Id));Assert.Equal(expected.Count,await query.CountAsync());Assert.Equal(expected.Take(1),await query.Page(1,1).ToListAsync(r=>r.Id));
                var sparse=await reader.ReadPageAsync(readPlan,[new("parent_id",FixedReadComparison.Equal,5L),new("names",FixedReadComparison.LikePattern,keyword)],[new("id")],1,20,projectId:"17");Assert.Equal(expected.Count,sparse.Total);Assert.Equal(expected,sparse.Records.Select(r=>Convert.ToInt64(r["id"])));
                var statistics=await reader.ReadStatisticsAsync(readPlan,[new("parent_id",FixedReadComparison.Equal,5L),new("names",FixedReadComparison.LikePattern,keyword)],["parent_id"],projectId:"17");Assert.Equal(expected.Count,statistics.Total);Assert.Equal(expected.Count==0?(decimal?)null:expected.Count*5m,statistics.Sums["parent_id"]);
                Assert.Contains(calls,c=>c.Values.Any(v=>Equals(v,"%"+keyword+"%"))&&!c.Sql.Contains(keyword,StringComparison.Ordinal)&&!c.Sql.Contains("ESCAPE",StringComparison.Ordinal));
            }
            var literal=StringNameQuery.ApplyLike(session,session.Select<NamesRow>().AsTable((_,_)=>table).Where(r=>r.ProjectId==17&&r.ParentId==5&&r.DeleteNo==null),r=>r.Names,"x_y");Assert.Equal(new[]{4L},await literal.ToListAsync(r=>r.Id));
            var sparseLiteral=await reader.ReadPageAsync(readPlan,[new("parent_id",FixedReadComparison.Equal,5L),new("names",FixedReadComparison.Contains,"x_y")],[],1,20,projectId:"17");Assert.Equal(4L,Convert.ToInt64(Assert.Single(sparseLiteral.Records)["id"]));
        }
        finally{if(created&&installed is not null)await Cleanup(session,schema,catalog,table,installed,written);}
        await using(var independent=new MySqlConnection(options.ConnectionString)){await independent.OpenAsync();await using var c=independent.CreateCommand();c.CommandText="SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=@schema AND TABLE_NAME=@table";c.Parameters.AddWithValue("schema",options.ExpectedDatabase);c.Parameters.AddWithValue("table",table);Assert.Equal(0L,Convert.ToInt64(await c.ExecuteScalarAsync()));}
        // 原参数化LIKE作为独立基线，不调用待测模式helper。
        async Task<List<long>> Baseline(string keyword)
        {
            await using var c=session.Provider.CreateConnection(options);await c.OpenAsync();await using var command=c.CreateCommand();command.CommandText="SELECT id FROM "+session.Provider.QuoteIdentifier(table)+" WHERE yz_project_id=17 AND parent_id=5 AND delete_no IS NULL AND names LIKE @pattern ORDER BY id";var p=command.CreateParameter();p.ParameterName="@pattern";p.Value="%"+keyword+"%";command.Parameters.Add(p);await using var r=await command.ExecuteReaderAsync();var ids=new List<long>();while(await r.ReadAsync())ids.Add(r.GetInt64(0));return ids;
        }
    }
}
