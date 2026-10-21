using System.Data.Common;
using System.Globalization;
using System.Security;
using System.Text.Json;
using Metarill.Core.Metadata;
using Metarill.Core.Security;
using Metarill.Runtime.Persistence;

namespace Metarill.Runtime.Files;

/// <summary>保护文件只按服务端真实引用和当前记录开放；页号、记录号提示不能构造授权。</summary>
internal static class FileReferenceReadAuthorization
{
    /// <summary>先核当前页权，再只读取文件字段及固定范围列；不会查询未授权页的业务记录或其他业务字段。</summary>
    internal static async Task<bool> CanReadAsync(SqlRowStore store,FileMetadataContext registry,IRuntimeAuthorization authorization,long fileId,RuntimeActor actor,CancellationToken token)
    {
        var project=FileReferenceScope.PositiveId(actor.ProjectId);
        foreach(var catalog in registry.Catalogs.Where(catalog=>catalog.YzProjectId==actor.ProjectId))
        foreach(var page in catalog.ConfigPages)
        {
            var table=catalog.ConfigTables.Single(table=>table.Id==page.ConfigTableId);
            var physical=registry.PhysicalColumns(catalog,table);
            var fields=physical.Where(column=>column.ControlKind=="FileLibrary"&&catalog.ConfigPageColumns.Any(field=>field.ConfigPageId==page.Id&&field.ConfigColumnId==column.Id&&Readable(field))).ToDictionary(column=>column.ColumnName,StringComparer.Ordinal);
            if(fields.Count==0)continue;
            try{authorization.RequireFileSourcePagePermission(page.Id);}
            catch(Exception denied)when(denied is UnauthorizedAccessException or SecurityException){continue;}
            IReadOnlyList<FileParentScope> scopes;ExactFileScope? exact;
            try{scopes=FileReferenceScope.Compile(catalog,table,page);exact=ExactScope(catalog,physical,page,authorization);}
            catch(Exception denied)when(denied is NotSupportedException or ArgumentException or UnauthorizedAccessException or SecurityException){continue;}
            var references=await store.ReadAsync("yz_file_ref",new Dictionary<string,object?>{{"file_id",fileId},{"page_id",FileReferenceScope.PositiveId(page.Id)},{"yz_project_id",project}},token);
            foreach(var reference in references)
            {
                var name=SqlRowStore.Text(reference,"field_name");if(!fields.TryGetValue(name,out var field))continue;
                var recordId=SqlRowStore.Id(reference,"record_id");if(recordId<=0)continue;
                var record=await ReadRecordAsync(store,table,physical,field,scopes,exact,recordId,project,token);if(record is null)continue;
                try
                {
                    IReadOnlyList<long> actual;
                    try{actual=FileValidation.ParseFileIds(record.GetValueOrDefault(field.ColumnName));}
                    catch(InvalidOperationException){continue;} // 仅把损坏的存储文件值作为拒权，不吞数据库或连接错误。
                    if(!actual.Contains(fileId))continue;
                    var parents=scopes.Select(scope=>(Scope:scope,Id:FileReferenceScope.PositiveId(record.GetValueOrDefault(scope.Source.ColumnName)))).ToArray();
                    if(parents.Select(parent=>parent.Id).Distinct().Skip(1).Any())continue;
                    var valid=true;
                    foreach(var parent in parents)
                    {
                        if(!await ParentExistsAsync(store,parent.Scope,parent.Id,project,token)){valid=false;break;}
                    }
                    if(valid)return true;
                }
                catch(Exception malformed)when(malformed is UnauthorizedAccessException or ArgumentException or JsonException or FormatException or OverflowException){/* 损坏引用或存储值不提供权限，继续核对其它真实引用。 */}
            }
        }
        return false;
    }
    /// <summary>原isInGrid/isInEdit显式标志优先，不能把隐藏或已移除字段当作当前可读文件。</summary>
    private static bool Readable(PageColumnMetadata field)
    {
        return Flag("isInGrid",field.IsShow==1)||Flag("isInEdit",field.IsEdit==1);
        /// <summary>与生成模型一致，只接受明确true或1，不把未知JSON当成可读。</summary>
        bool Flag(string key,bool fallback)=>field.Extra.TryGetValue(key,out var value)?value.ValueKind==JsonValueKind.True||value.ToString()=="1":fallback;
    }

    private sealed record ExactFileScope(ColumnMetadata Column,IReadOnlyList<object?> Values);
    /// <summary>文件真实引用另附来源页的查询精确范围，不能只看页面动作；值经同一逻辑类型转换并参数化。</summary>
    private static ExactFileScope? ExactScope(MetadataCatalog catalog,IReadOnlyList<ColumnMetadata> physical,PageMetadata page,IRuntimeAuthorization authorization)
    {
        var field=ExactDataManageValues.Field(Configured(page));if(field.Length==0)return null;
        var column=physical.SingleOrDefault(column=>ExactDataManageValues.Field(column.ColumnName)==field)??throw new NotSupportedException("文件来源页精确范围没有固定物理字段证明。");
        var admin=catalog.YzAdminTypes.SingleOrDefault(admin=>admin.Id==page.YzAdminTypeId)??throw new NotSupportedException("文件来源页后台未发布。");
        var scope=authorization.ResolveFileSourceExactDataManage(page.Id,ExactDataManageValues.Field(Configured(admin)));
        return scope.IsRequired?new(column,scope.ReadLogical(column.LogicalType)):null;
        /// <summary>损坏声明不能静默解释为未配置。</summary>
        static string Configured(MetadataRecord record)=>!record.Extra.TryGetValue("exactDataManageMatchField",out var value)||value.ValueKind==JsonValueKind.Null?"":value.ValueKind==JsonValueKind.String?value.GetString()??"":throw new NotSupportedException("精确管理字段声明无效。");
    }

    /// <summary>只投影必要列；观察者内复用真实事务以读取本批刚写的记录，独立请求才创建自有连接。</summary>
    private static async Task<Dictionary<string,object?>?> ReadRecordAsync(SqlRowStore store,TableMetadata table,IReadOnlyList<ColumnMetadata> columns,ColumnMetadata field,IReadOnlyList<FileParentScope> scopes,ExactFileScope? exact,long record,long project,CancellationToken token)
    {
        if(exact is not null&&exact.Values.Count==0)return null;
        var id=columns.SingleOrDefault(column=>column.ColumnName=="id");if(id is null)return null;
        var owner=columns.SingleOrDefault(column=>column.ColumnName=="yz_project_id");
        if(owner is null&&(!table.Extra.TryGetValue("scopeKind",out var global)||global.ToString()!="global"))return null;
        var names=new[]{field.ColumnName,"id"}.Concat(owner is null?Array.Empty<string>():[owner.ColumnName]).Concat(scopes.Select(scope=>scope.Source.ColumnName)).Distinct(StringComparer.Ordinal).ToArray();
        var provider=store.Session.Provider;var transaction=store.Session.CurrentTransaction;
        await using var ownedConnection=transaction is null?provider.CreateConnection(store.Session.Options):null;
        var connection=transaction?.Connection??ownedConnection??throw new InvalidOperationException("文件读取事务连接不可用。");
        if(ownedConnection is not null)await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        var query="SELECT "+string.Join(",",names.Select(provider.QuoteIdentifier))+" FROM "+provider.QuoteIdentifier(table.TableName)+" WHERE "+provider.QuoteIdentifier("id")+"="+Parameter(command,store,"record_id",record);
        if(owner is not null)query+=" AND "+provider.QuoteIdentifier(owner.ColumnName)+"="+Parameter(command,store,"project_id",FileReferenceScope.IdentityValue(owner,project));
        if(columns.Any(column=>column.ColumnName=="delete_no"))query+=" AND "+provider.QuoteIdentifier("delete_no")+" IS NULL";
        if(exact is not null)query+=" AND "+provider.QuoteIdentifier(exact.Column.ColumnName)+" IN ("+string.Join(",",exact.Values.Select((value,index)=>Parameter(command,store,"exact_"+index,value!)))+")";
        command.CommandText=query;await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;var row=new Dictionary<string,object?>(StringComparer.Ordinal);for(var index=0;index<reader.FieldCount;index++)row.Add(reader.GetName(index),await reader.IsDBNullAsync(index,token)?null:reader.GetValue(index));return row;
    }
    /// <summary>父身份从实际记录读取，复用当前事务可看见本批刚建父；父仍须有效且同项目。</summary>
    private static async Task<bool> ParentExistsAsync(SqlRowStore store,FileParentScope scope,long id,long project,CancellationToken token)
    {
        var provider=store.Session.Provider;var transaction=store.Session.CurrentTransaction;
        await using var ownedConnection=transaction is null?provider.CreateConnection(store.Session.Options):null;
        var connection=transaction?.Connection??ownedConnection??throw new InvalidOperationException("文件读取事务连接不可用。");
        if(ownedConnection is not null)await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="SELECT "+provider.QuoteIdentifier("id")+" FROM "+provider.QuoteIdentifier(scope.Parent.TableName)+" WHERE "+provider.QuoteIdentifier("id")+"="+Parameter(command,store,"parent_id",id)+" AND "+provider.QuoteIdentifier("yz_project_id")+"="+Parameter(command,store,"parent_project",FileReferenceScope.IdentityValue(scope.Project,project))+(scope.HasSoftDelete?" AND "+provider.QuoteIdentifier("delete_no")+" IS NULL":"");return await command.ExecuteScalarAsync(token) is not null;
    }
    /// <summary>固定参数名和值分开传递，不拼接用户值。</summary>
    private static string Parameter(DbCommand command,SqlRowStore store,string name,object value){var parameter=command.CreateParameter();parameter.ParameterName=name;parameter.Value=value;command.Parameters.Add(parameter);return store.Session.Provider.ParameterPlaceholder(name);}
}
