using System.Globalization;
using ClosedXML.Excel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Data;
using Metarill.Runtime.Exports;
using Metarill.Runtime.Persistence;
using Xunit;
namespace Metarill.Generation.Tests;

public sealed partial class GlobalCachedCrossExcelNativeMySqlTests
{
    /// <summary>只保存本轮已验证项目、实例和真实HTTP客户端；无认证或SQL传输替身。</summary>
    private sealed class Probe(string profile,int role,string key,string parent,bool allGlobal,bool shared,bool shorter,MetadataCatalog catalog,DatabaseOptions options,DatabaseSession session,SqlRowStore store,SqlRowStore files,MetadataCatalog fileCatalog,HttpClient owner,HttpClient reader,SqlRowStore security,Dictionary<string,long> permissions,string hooks)
    {
        private readonly string prefix="gce_"+profile;
        private readonly int ordinal=Array.FindIndex(GlobalCachedCrossExcelMySqlLease.Profiles,p=>GlobalCachedCrossExcelMySqlLease.Profile(p.Role,p.Key,p.Parent,p.AllGlobal,p.Shared,p.Shorter)==profile);
        private string Source=>prefix+"_cross_consumer";
        private TableMetadata SourceTable=>catalog.ConfigTables.Single(table=>table.TableName==Source);
        internal string SourcePage=>catalog.ConfigPages.Single(page=>page.ConfigTableId==SourceTable.Id).Id;
        private string Route=>"/api/admin/auto/"+Source+"/"+SourcePage+"/";
        internal string Stage="本轮种子";
        private readonly string[] labels=["国甲","区甲","城甲"];
        private string? lastFile;
        /// <summary>每组使用独立目标ID区间，错连其它模型不能因相同ID通过。</summary>
        private long At(long id)=>id+ordinal*10000L;
        /// <summary>父键严格保留元数据中的字符串或整数类型。</summary>
        private object Parent(long id)=>parent=="string"?Id(id):id;
        private string Full=>string.Join("/",labels.Take(shorter?2:3));
        private string Secondary=>labels[0]+"/"+labels[1]+"/仓甲";
        private string First=>labels[0]+(shorter?"/":"//");
        private string Middle=>labels[0]+"/"+labels[1]+(shorter?"":"/");
        /// <summary>六组先分别种子，业务ID独立，避免错误跨模型联接因同ID同名蒙混通过。</summary>
        internal async Task Seed()
        {
            Assert.True(ordinal>=0);Assert.Equal(allGlobal?4:2,catalog.ConfigTables.Count(table=>table.TableName.StartsWith(prefix+"_",StringComparison.Ordinal)&&GlobalCrossTableTarget.Declared(table)));
            await session.InTransactionAsync(async()=>
            {
            var lookup=prefix+"_lookup";
            await store.InsertAsync(lookup,Row("id",At(Lookup),"status",1,"name",labels[shared?1:role]));
            var field=catalog.ConfigColumns.Single(column=>column.ConfigTableId==SourceTable.Id&&column.ControlKind=="CascadeTree"&&column.Name=="区域");
            using var definition=JsonDocument.Parse(field.ControlDetail);var levels=definition.RootElement.GetProperty("levels").EnumerateArray().Select(value=>value.Clone()).ToArray();var seeded=new HashSet<string>(StringComparer.Ordinal);
            for(var index=0;index<levels.Length;index++)
            {
                var table=levels[index].GetProperty("table").GetString()!;var values=Target(table,At(new[]{Country,Area,Place}[index]),labels[index]);
                if(index>0)values[levels[index].GetProperty("parentField").GetString()!]=Parent(At(new[]{Country,Area,Place}[index-1]));
                await store.InsertAsync(table,values);seeded.Add(table);
            }
            if(shorter&&!seeded.Contains(prefix+"_cross_other_b"))await store.InsertAsync(prefix+"_cross_other_b",WithParent(Target(prefix+"_cross_other_b",At(Place),"城甲"),"area_id",At(Area)));
            if(shared)await store.InsertAsync(prefix+"_cross_warehouse",WithParent(Target(prefix+"_cross_warehouse",At(Warehouse),"仓甲"),"area_id",At(Area)));
            return true;
            });
        }
        /// <summary>全局表无项目列，普通目标保留实际项目；缓存初值仍按原字典及角色配置。</summary>
        private Dictionary<string,object?> Target(string table,long id,string name)
        {
            var metadata=catalog.ConfigTables.Single(value=>value.TableName==table);var row=Row("id",id,"status",1);
            if(!GlobalCrossTableTarget.Declared(metadata))row["yz_project_id"]=Project;
            if(table==prefix+"_cached_global"){row["lookup_id"]=Parent(At(Lookup));row["lookup_name"]=name;}else row["name"]=name;
            return row;
        }
        /// <summary>固定父键仍按原字符串／整数类型写入，不从JSON回显猜存储。</summary>
        private Dictionary<string,object?> WithParent(Dictionary<string,object?> row,string field,long id){row[field]=Parent(id);return row;}
        /// <summary>真实原选档回调、普通账号覆盖、物理同一行、歧义与末段文件回滚。</summary>
        internal async Task VerifyOverwrite(string evidence,string source)
        {
            var main=catalog.ConfigColumns.Single(column=>column.ConfigTableId==SourceTable.Id&&column.ControlKind=="CascadeTree"&&column.Name=="区域");
            var create=Row("name","覆盖原行",GenerationModel.Camel(main.ColumnName),Id(At(shorter?Area:Place)));
            if(shared)create["warehouseId"]=Id(At(Warehouse));
            var original=long.Parse((await Post(reader,Route+"add",create)).GetProperty("id").GetString()!,CultureInfo.InvariantCulture);
            var storage=shorter?"area_id":"place_id";
            await store.InsertAsync(Source,Row("id",At(Lookup)+1,"name","外项目对照","yz_project_id",Project+1,storage,key=="string"?Id(At(shorter?Area:Place)):At(shorter?Area:Place)));
            await store.InsertAsync(Source,Row("id",At(Lookup)+2,"name","已删除对照","yz_project_id",Project,storage,key=="string"?Id(At(shorter?Area:Place)):At(shorter?Area:Place),"delete_no",new DateTime(2026,1,1)));
            var prior=(await store.ReadIncludingDeletedAsync(Source)).Count;
            var file=Path.Combine(evidence,profile+"-overwrite.xlsx");
            using(var book=new XLWorkbook())
            {
                var sheet=book.AddWorksheet("原覆盖首列");sheet.Cell(1,1).Value="区域";sheet.Cell(1,2).Value="名称";
                sheet.Cell(2,1).Value=Full;sheet.Cell(2,2).Value="覆盖后";book.SaveAs(file);
            }
            var expected=Path.Combine(evidence,profile+"-expected.json");var received=Path.Combine(evidence,profile+"-received.json");
            await File.WriteAllTextAsync(expected,JsonSerializer.Serialize(new[]{Row("区域",Full,"名称","覆盖后")}));
            var node=NonRedundantApplicationFixture.Start(Environment.GetEnvironmentVariable("NODE_HOST_PATH")??"node",source);
            foreach(var argument in new[]{Path.Combine(source,"backend/tests/Metarill.Generation.Tests/OriginalExcelImportProbe.mjs"),source,file,expected,received})node.ArgumentList.Add(argument);
            await NonRedundantApplicationFixture.RunAsync(node);
            using(var rows=JsonDocument.Parse(await File.ReadAllTextAsync(received)))await Post(reader,Route+"importDatas"+SourcePage,new{requestJson=rows.RootElement});
            Assert.Equal(original,SqlRowStore.Id(await Record("覆盖后")));Assert.Equal(prior,(await store.ReadIncludingDeletedAsync(Source)).Count);
            await Sparse("覆盖后",shorter?1:2,At(shorter?Area:Place),shared?2:-1,shared?At(Warehouse):null);
            Assert.Equal("外项目对照",SqlRowStore.Text(await Physical(Source,At(Lookup)+1),"name"));
            Assert.Equal("已删除对照",SqlRowStore.Text(await Physical(Source,At(Lookup)+2),"name"));
            var duplicate=(await Post(reader,Route+"copy/"+Id(original),new{})).GetProperty("id").GetString()!;
            var before=await Snapshot();
            await Import(HttpStatusCode.InternalServerError,Row("区域",Full,"名称","歧义不能覆盖"));
            Assert.Equal(before,await Snapshot());
            await Send(reader,HttpMethod.Delete,Route.TrimEnd('/'),new{id=duplicate});
            // 两行都提供有效末级键；第二行必须先在当前事务看到第一行和附件引用。
            await FileRollback();
            using var independent=new DatabaseSession(options);
            var comparison=await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(Source,Row("name","文件成功"));
            Assert.Equal(2,comparison.Count);
            var active=Assert.Single(comparison,value=>value["delete_no"] is null);
            Assert.Equal(original,SqlRowStore.Id(active));
            var tombstone=Assert.Single(comparison,value=>value["delete_no"] is not null);
            Assert.NotEqual(original,SqlRowStore.Id(tombstone));
            await Revoke(main,HttpStatusCode.Forbidden);
        }

        /// <summary>真实普通账号逐项经过原候选、路径、Excel、复制、文件及权限边界。</summary>
        internal async Task Verify()
        {
            using(var anonymous=new HttpClient{BaseAddress=reader.BaseAddress})await Send(anonymous,HttpMethod.Post,Route+"importDatas"+SourcePage,new{requestJson=Array.Empty<object>()},HttpStatusCode.Unauthorized);
            await Candidates();
            await Import(Input("完整",Full,shared?Secondary:null),Input("根级",First,shared?labels[0]+"//":null),Input("中级",Middle,shared?labels[0]+"/"+labels[1]+"/":null),Input("空值",null,null));
            await Sparse("完整",shorter?1:2,At(shorter?Area:Place),shared?2:-1,shared?At(Warehouse):null);await Sparse("根级",0,At(Country),shared?0:-1,shared?At(Country):null);await Sparse("中级",1,At(Area),shared?1:-1,shared?At(Area):null);await Sparse("空值",-1,null,-1,null);
            await store.InsertAsync(Source,Row("id",At(Lookup)+1,"name","其它项目","yz_project_id",Project+1,shorter?"area_id":"place_id",key=="string"?Id(At(shorter?Area:Place)):At(shorter?Area:Place)));
            var exported=await Export();Assert.Equal(4,exported.Count);Assert.False(exported.ContainsKey("其它项目"));Assert.Equal((Full,shared?Secondary:null),exported["完整"]);Assert.Equal((First,shared?labels[0]+"//":null),exported["根级"]);Assert.Equal((Middle,shared?labels[0]+"/"+labels[1]+"/":null),exported["中级"]);Assert.Equal(((string?)null,(string?)null),exported["空值"]);
            await Import(exported.Select(value=>Input("往返"+value.Key,value.Value.Main,value.Value.Second)).ToArray());await Sparse("往返完整",shorter?1:2,At(shorter?Area:Place),shared?2:-1,shared?At(Warehouse):null);await Sparse("往返根级",0,At(Country),shared?0:-1,shared?At(Country):null);await Sparse("往返中级",1,At(Area),shared?1:-1,shared?At(Area):null);await Sparse("往返空值",-1,null,-1,null);
            // 来源普通新增／修改／复制仍走实际生成接口，不能仅让专用导入路径通过。
            var main=catalog.ConfigColumns.Single(column=>column.ConfigTableId==SourceTable.Id&&column.ControlKind=="CascadeTree"&&column.Name=="区域");
            var body=Row("name","普通新增",GenerationModel.Camel(main.ColumnName),Id(At(shorter?Area:Place)));
            if(shared)body["warehouseId"]=Id(At(Warehouse));var created=(await Post(reader,Route+"add",body)).GetProperty("id").GetString()!;
            await Post(reader,Route+"update",new{id=created,name="普通修改"});await Sparse("普通修改",shorter?1:2,At(shorter?Area:Place),shared?2:-1,shared?At(Warehouse):null);
            var ordinaryCopy=(await Post(reader,Route+"copy/"+created,new{})).GetProperty("id").GetString()!;
            await Post(reader,Route+"update",new{id=ordinaryCopy,name="普通复制"});
            // 全局字典更新必须沿同一生产Service传播，缓存和原XLSX都读取新值。
            var changed=shared?1:role;labels[changed]+="新";var lookupTable=catalog.ConfigTables.Single(table=>table.TableName==prefix+"_lookup");var lookupPage=catalog.ConfigPages.Single(page=>page.ConfigTableId==lookupTable.Id).Id;
            await Post(reader,"/api/admin/auto/"+lookupTable.TableName+"/"+lookupPage+"/update",new{id=Id(At(Lookup)),name=labels[changed]});
            Assert.Equal(labels[changed],SqlRowStore.Text(await Physical(prefix+"_cached_global",At(new[]{Country,Area,Place}[changed])),"lookup_name"));
            exported=await Export();Assert.Equal((Full,shared?Secondary:null),exported["完整"]);
            var before=await Snapshot();await Import(HttpStatusCode.BadRequest,Input("旧名不得写入",shorter?"国甲/区甲":"国甲/区甲/城甲",shared?"国甲/区甲/仓甲":null));Assert.Equal(before,await Snapshot());
            await Import(Input("改名后",Full,shared?Secondary:null));await Sparse("改名后",shorter?1:2,At(shorter?Area:Place),shared?2:-1,shared?At(Warehouse):null);
            if(shared){before=await Snapshot();await Import(HttpStatusCode.BadRequest,Input("共享冲突",First,Secondary));Assert.Equal(before,await Snapshot());}
            await Maintenance(changed);await LatePropagationFailure(lookupTable,lookupPage);await AmbiguousNames(changed);await FileRollback();await Revoke(main);
        }
        /// <summary>候选需要真实来源／目标页权限，路径回读保持原长ID和名称。</summary>
        private async Task Candidates()
        {
            var field=catalog.ConfigColumns.Single(column=>column.ConfigTableId==SourceTable.Id&&column.ControlKind=="CascadeTree"&&column.Name=="区域");
            using var definition=JsonDocument.Parse(field.ControlDetail);var levels=definition.RootElement.GetProperty("levels").EnumerateArray().Select(value=>value.Clone()).ToArray();
            // 原协议拒绝未知层级；短级联只提交其实际声明的两个已选键。
            var selected=levels.Select((level,index)=>(Property:GenerationModel.Camel(level.GetProperty("columnPrefix").GetString()!+"_id"),Value:Id(At(new[]{Country,Area,Place}[index])))).ToDictionary(value=>value.Property,value=>value.Value,StringComparer.Ordinal);
            for(var index=0;index<levels.Length;index++)
            {
                var rows=await Post(reader,Route+"cascadeTreeOptions"+field.Id,new{levelKey=levels[index].GetProperty("key").GetString(),selectedPath=selected});
                Assert.Contains(rows.EnumerateArray(),value=>value.GetProperty("id").GetString()==Id(At(new[]{Country,Area,Place}[index]))&&value.GetProperty("name").GetString()==labels[index]);
            }
            Assert.Equal(Full,(await Post(reader,Route+"cascadeTreePath"+field.Id,new{leafId=Id(At(shorter?Area:Place))})).GetProperty("displayText").GetString());
            var firstTable=catalog.ConfigTables.Single(table=>table.TableName==levels[0].GetProperty("table").GetString());var targetPage=catalog.ConfigPages.Single(page=>page.ConfigTableId==firstTable.Id).Id;await Grant(targetPage,"[]");
            await Send(reader,HttpMethod.Post,Route+"cascadeTreeOptions"+field.Id,new{levelKey="country",selectedPath=selected},HttpStatusCode.Forbidden);await Grant(targetPage,"[\"is_all\"]");
        }
        /// <summary>原维护只认具体按钮；普通页面is_all不自动获得维护权，管理员修复与重复零变化沿原接口。</summary>
        private async Task Maintenance(int changed)
        {
            var table=catalog.ConfigTables.Single(value=>value.TableName==prefix+"_cached_global");var id=At(new[]{Country,Area,Place}[changed]);await store.UpdateAsync(table.TableName,id,Row("lookup_name","本轮陈旧缓存"));var before=await Snapshot();
            const string route="/api/admin/sys/system_maintenance/backfill_join_columns";
            await Send(reader,HttpMethod.Post,route,new{configTableId=table.Id},HttpStatusCode.Unauthorized);Assert.Equal(before,await Snapshot());
            Assert.True((await Post(owner,route,new{configTableId=table.Id})).GetProperty("affectedRows").GetInt32()>0);Assert.Equal(labels[changed],SqlRowStore.Text(await Physical(table.TableName,id),"lookup_name"));Assert.Equal(0,(await Post(owner,route,new{configTableId=table.Id})).GetProperty("affectedRows").GetInt32());
            Assert.Equal((Full,shared?Secondary:null),(await Export())["完整"]);
        }
        /// <summary>缓存末端真实CHECK拒绝时，上游字典及六组业务/文件快照必须整笔保持。</summary>
        private async Task LatePropagationFailure(TableMetadata lookupTable,string lookupPage)
        {
            const string blocked="native_forbidden_cache_name";var constraint="mt_gcxe_"+profile+"_late_cache";var table=prefix+"_cached_global";
            await Sql("ALTER TABLE `"+table+"` ADD CONSTRAINT `"+constraint+"` CHECK (`lookup_name` <> '"+blocked+"')");
            try{var before=await Snapshot();await Send(reader,HttpMethod.Post,"/api/admin/auto/"+lookupTable.TableName+"/"+lookupPage+"/update",new{id=Id(At(Lookup)),name=blocked},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());}
            finally{await Sql("ALTER TABLE `"+table+"` DROP CHECK `"+constraint+"`");}
        }
        /// <summary>同父重名不能猜选，也不能导出成无法唯一还原的路径；仅软删本轮合成重复样本后恢复。</summary>
        private async Task AmbiguousNames(int changed)
        {
            var table=prefix+"_cached_global";var id=At(new[]{Country,Area,Place}[changed])+500;var duplicate=new Dictionary<string,object?>(await Physical(table,At(new[]{Country,Area,Place}[changed]))){["id"]=id};await store.InsertAsync(table,duplicate);
            var before=await Snapshot();await Import(HttpStatusCode.BadRequest,Input("重名不能写入",Full,shared?Secondary:null));Assert.Equal(before,await Snapshot());await Send(reader,HttpMethod.Post,Route+"initExport",new{},HttpStatusCode.InternalServerError);Assert.Equal(before,await Snapshot());
            await store.UpdateAsync(table,id,Row("delete_no",new DateTime(2026,1,1)));Assert.Equal((Full,shared?Secondary:null),(await Export())["完整"]);
        }
        /// <summary>DDL只由本轮固定模型生成，表名和约束名均为静态字母数字；不接受外部SQL或修改既有库。</summary>
        private async Task Sql(string sql){Stage="本轮缓存故障约束";Assert.Null(session.CurrentTransaction);await using var connection=session.Provider.CreateConnection(options);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=sql;await command.ExecuteNonQueryAsync();}
        /// <summary>原上传计划和multipart完成后，第二行手写规则必须实际观察到首行DML及同事务引用才故障。</summary>
        private async Task FileRollback()
        {
            var bytes=Encoding.UTF8.GetBytes("全局缓存级联真实附件-"+profile);var column=catalog.ConfigColumns.Single(value=>value.ConfigTableId==SourceTable.Id&&value.ColumnName=="attachments").Id;
            const string route="/api/admin/sys/file-library/";var file=(await Post(reader,route+"upload-plan",new{originalName="global-cross-excel.txt",mimeType="text/plain",sizeBytes=bytes.Length,configColumnId=column})).GetProperty("fileId").GetString()!;
            using(var content=new MultipartFormDataContent()){content.Add(new ByteArrayContent(bytes),"file","global-cross-excel.txt");await Send(reader,HttpMethod.Post,route+"local-upload/"+file,content);}
            await Post(reader,route+"complete",new{fileId=file});lastFile=file;var first=Input("回滚首行",Full,shared?Secondary:null);first["附件"]=JsonSerializer.Serialize(new[]{file});var before=await Snapshot();
            await Import(HttpStatusCode.InternalServerError,first,Input("后置拒绝",Full,shared?Secondary:null));Assert.Equal(before,await Snapshot());
            var observation=Assert.Single(File.ReadLines(hooks).Select(line=>JsonSerializer.Deserialize<JsonElement>(line)),value=>value.GetProperty("table").GetString()==Source);Assert.True(observation.GetProperty("insideTransaction").GetBoolean());Assert.Equal(1,observation.GetProperty("references").GetInt32());
            first["名称"]="文件成功";await Import(first);var row=await Record("文件成功");var recordId=Id(SqlRowStore.Id(row));Assert.Single(await files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(file,CultureInfo.InvariantCulture),"record_id",SqlRowStore.Id(row))));
            var copied=(await Post(reader,Route+"copy/"+recordId,new{})).GetProperty("id").GetString()!;var references=await files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(file,CultureInfo.InvariantCulture)));Assert.Equal(2,references.Count);Assert.All(references,value=>{Assert.Equal(SourcePage,SqlRowStore.Text(value,"page_id"));Assert.Equal("100",SqlRowStore.Text(value,"yz_project_id"));});
            using(var download=await reader.GetAsync(route+"protected-content/"+file)){Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal(bytes,await download.Content.ReadAsByteArrayAsync());}
            await Send(reader,HttpMethod.Delete,Route.TrimEnd('/'),new{id=copied});Assert.Single(await files.ReadAsync("yz_file_ref",Row("file_id",long.Parse(file,CultureInfo.InvariantCulture))));
        }
        /// <summary>来源撤权后普通导入导出均403、文件按各已验收场景拒绝且业务/文件全量摘要不变，再恢复原明确许可。</summary>
        private async Task Revoke(ColumnMetadata main,HttpStatusCode fileDenied=HttpStatusCode.Unauthorized)
        {
            var before=await Snapshot();await Grant(SourcePage,"[]");
            await Import(HttpStatusCode.Forbidden,Input("拒权不得写入",Full,shared?Secondary:null));await Send(reader,HttpMethod.Post,Route+"initExport",new{},HttpStatusCode.Forbidden);await Send(reader,HttpMethod.Post,Route+"cascadeTreeOptions"+main.Id,new{levelKey="country",selectedPath=new{}},HttpStatusCode.Forbidden);Assert.Equal(before,await Snapshot());
            Assert.NotNull(lastFile);using(var denied=await reader.GetAsync("/api/admin/sys/file-library/protected-content/"+lastFile))Assert.Equal(fileDenied,denied.StatusCode);
            await Grant(SourcePage,"[\"is_all\"]");
        }
        /// <summary>来源只提交原中文表头，普通原请求不新增配置列或隐藏字段。</summary>
        private Dictionary<string,object?> Input(string name,string? main,string? second){var row=Row("名称",name,"区域",main);if(shared)row["仓库"]=second;return row;}
        /// <summary>默认原导入必须同时通过HTTP和R业务成功校验。</summary>
        private Task<JsonElement> Import(params Dictionary<string,object?>[] rows)=>Import(HttpStatusCode.OK,rows);
        /// <summary>原导入正文和页ID均从当前实际目录取得。</summary>
        private Task<JsonElement> Import(HttpStatusCode status,params Dictionary<string,object?>[] rows)=>Send(reader,HttpMethod.Post,Route+"importDatas"+SourcePage,new{requestJson=rows},status);
        /// <summary>真实原票据下载XLSX，并使用B119同一解析器核对原三/四列表头和顺序。</summary>
        private async Task<Dictionary<string,(string? Main,string? Second)>> Export()
        {
            var grant=(await Post(reader,Route+"initExport",new{})).GetString()!;Stage="下载原XLSX";
            using var response=await reader.GetAsync(ExportDownloadAccess.DownloadPath+"?exportId="+Uri.EscapeDataString(grant));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            return GlobalCachedCrossExcelMySqlTests.ReadOriginalWorkbook(await response.Content.ReadAsByteArrayAsync(),shared,shorter);
        }
        /// <summary>独立连接检查真实物理存储；共享祖先只保留两条路径并集的最深必要位置。</summary>
        private async Task Sparse(string name,int depth,long? selected,int secondDepth,long? second)
        {
            var row=await Record(name);Assert.True(SqlRowStore.Id(row)>9007199254740991);var expected=new Dictionary<string,long?>{{"country_id",null},{"area_id",null}};if(!shorter)expected["place_id"]=null;if(shared)expected["warehouse_id"]=null;
            var fields=shorter?new[]{"country_id","area_id"}:new[]{"country_id","area_id","place_id"};if(depth>=0)expected[fields[depth]]=selected;
            if(shared&&secondDepth>=0){var secondary=new[]{"country_id","area_id","warehouse_id"};expected[secondary[secondDepth]]=second;if(depth!=secondDepth)foreach(var ancestor in secondary.Take(Math.Max(depth,secondDepth)))expected[ancestor]=null;}
            foreach(var value in expected)Assert.Equal(value.Value is{} id?Id(id):null,row[value.Key] is null?null:Convert.ToString(row[value.Key],CultureInfo.InvariantCulture));Assert.DoesNotContain(row.Keys,value=>value.EndsWith("_name",StringComparison.Ordinal));
        }
        /// <summary>失败比较覆盖六组全部业务表和文件，包括墓碑，不能只看当前主行。</summary>
        private async Task<string> Snapshot()
        {
            using var independent=new DatabaseSession(options);var business=new SqlRowStore(independent,catalog);var fileStore=new SqlRowStore(independent,fileCatalog);var rows=new SortedDictionary<string,object>(StringComparer.Ordinal);
            foreach(var table in catalog.ConfigTables)rows[table.TableName]=(await business.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();foreach(var table in fileCatalog.ConfigTables)rows[table.TableName]=(await fileStore.ReadIncludingDeletedAsync(table.TableName)).OrderBy(row=>SqlRowStore.Id(row)).ToArray();return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
        }
        /// <summary>撤权只更新本轮新库普通角色的精确页许可。</summary>
        private Task Grant(string page,string values)=>security.UpdateAsync("yz_role_permission",permissions[page],Row("permission_values",values));
        /// <summary>来源物理核对使用新的独立连接，避免拿VO回显当作存储。</summary>
        private async Task<IReadOnlyDictionary<string,object?>> Record(string name){using var independent=new DatabaseSession(options);return Assert.Single(await new SqlRowStore(independent,catalog).ReadIncludingDeletedAsync(Source,Row("name",name)));}
        /// <summary>仅按本轮真实表和参数化主键读取含墓碑数据。</summary>
        private async Task<IReadOnlyDictionary<string,object?>> Physical(string table,long id)=>Assert.Single(await store.ReadIncludingDeletedAsync(table,Row("id",id)));
        /// <summary>只有成功的原R响应才读取data。</summary>
        private async Task<JsonElement> Post(HttpClient sender,string route,object body)=>(await Send(sender,HttpMethod.Post,route,body)).GetProperty("data");
        /// <summary>记录实际业务入口，不把票据或请求正文写入阶段字段。</summary>
        private Task<JsonElement> Send(HttpClient sender,HttpMethod method,string route,object? body=null,HttpStatusCode status=HttpStatusCode.OK){Stage=method.Method+" "+route;return FreshWebHttp.SendAsync(sender,method,route,body,status);}
    }
}
