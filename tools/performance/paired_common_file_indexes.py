"""共同逻辑文件访问索引的原生执行与完整STATISTICS核验；Python只比较计划，不执行DDL。"""
from __future__ import annotations
import re
import paired_http as http
import server200_paired_resources as resources

TABLES=('yz_file_library','yz_file_ref')


def require(ok,code):
    """错误只输出固定位置，不回显连接、SQL正文或实际数据。"""
    if not ok:raise ValueError('measurement-file-index-'+code)


def desired(side):
    """只映射同一逻辑owner／page／record；两个不同类型的字段标识不进入共同索引。"""
    require(side in ('java','csharp'),'side');owner='owner_yz_manager_id' if side=='java' else 'owner_id';page='source_config_page_id' if side=='java' else 'page_id';record='source_record_id' if side=='java' else 'record_id'
    return {'yz_file_library':[('mt_file_project_status',['yz_project_id','status']),('mt_file_folder',['yz_project_id','folder_id','delete_no']),('mt_file_owner',[owner])],'yz_file_ref':[('mt_ref_file',['file_id','delete_no']),('mt_ref_record',[page,record,'delete_no']),('mt_ref_project_record',['yz_project_id',record,page,'delete_no'])]}


def original(side):
    """白名单来自原Java固定DDL和当前C#已核真实初始化计划，不接受调用方自报索引。"""
    require(side in ('java','csharp'),'side')
    return {'yz_file_library':[('idx_yz_file_library_project_status',['yz_project_id','status']),('idx_yz_file_library_folder',['yz_project_id','folder_id']),('idx_yz_file_library_owner',['owner_yz_manager_id'])],'yz_file_ref':[('idx_yz_file_ref_file',['file_id']),('idx_yz_file_ref_source',['source_config_page_id','source_record_id','source_config_column_id'])]} if side=='java' else {'yz_file_library':[('ix_file_folder',['yz_project_id','folder_id','delete_no'])],'yz_file_ref':[('ix_file_delete',['file_id','delete_no']),('ix_file_record',['page_id','record_id','delete_no']),('ix_file_scope_record',['yz_project_id','record_id','page_id','delete_no'])]}


def expected_changes(side):
    """只用于核对C#适配器实际编译的11／10条回执，字符串绝不交给数据库执行。"""
    old=original(side);new=desired(side);result=[]
    for table in TABLES:
        for name,columns in sorted(old[table]):result.append(dict(kind='dropIndex',table=table,column=None,sql=f'ALTER TABLE `{table}` DROP INDEX `{name}`'))
        for name,columns in new[table]:result.append(dict(kind='addIndex',table=table,column=None,sql=f'ALTER TABLE `{table}` ADD INDEX `{name}` ('+', '.join('`'+column+'`' for column in columns)+')'))
    return result


def expected_statistics(side,common):
    """包含主键的完整物理属性；列序、前缀、可见性和BTREE不允许遗漏。"""
    indexes=desired(side) if common else original(side);rows=[]
    for table in TABLES:
        for name,columns in [('PRIMARY',['id']),*indexes[table]]:
            rows.extend(dict(table=table,index=name,nonUnique=0 if name=='PRIMARY' else 1,sequence=n+1,column=column,direction='A',prefix='',kind='BTREE',visible='YES') for n,column in enumerate(columns))
    return sorted(rows,key=lambda row:(row['table'],row['index'],row['sequence']))


def normalize_statistics(rows,side,common=True):
    """先逐物理属性精确比较，再映射别名；不以映射掩盖额外索引或不相同的列。"""
    require(type(rows) is list and all(type(row) is dict and set(row)=={'table','index','nonUnique','sequence','column','direction','prefix','kind','visible'} and type(row['sequence']) is int and type(row['nonUnique']) is int for row in rows),'statistics-shape')
    ordered=sorted(rows,key=lambda row:(row['table'],row['index'],row['sequence']));require(ordered==expected_statistics(side,common),'statistics-drift')
    aliases={'owner_yz_manager_id':'owner_id','source_config_page_id':'page_id','source_record_id':'record_id'}
    return [{**row,'column':aliases.get(row['column'],row['column'])} for row in ordered]


def protected_schema(schema):
    """独立逐值保护字段与SHOW CREATE非二级索引部分，不能仅信服务端的布尔回执。"""
    require(type(schema) is dict and set(schema)=={'database','tables'} and type(schema['tables']) is list and len(schema['tables'])==2,'schema-shape');result={}
    for table in schema['tables']:
        require(type(table) is dict and set(table)=={'name','columns','snapshotDefinition','indexes'} and table.get('name') in TABLES and table['name'] not in result and type(table.get('columns')) is list and len(table['columns'])>0 and type(table.get('snapshotDefinition')) is str and type(table.get('indexes')) is list,'schema-table')
        require(all(type(column) is dict and set(column)=={'name','storeType','nullable','primaryKey','defaultSql','generated'} and type(column['name']) is str and type(column['storeType']) is str and all(type(column[key]) is bool for key in ['nullable','primaryKey','generated']) for column in table['columns']) and len({column['name'] for column in table['columns']})==len(table['columns']),'schema-columns')
        lines=[line.strip().rstrip(',') for line in table['snapshotDefinition'].split('\n') if not re.match(r'^(?:UNIQUE )?KEY `',line.strip())]
        result[table['name']]=dict(columns=sorted(table['columns'],key=lambda column:column['name']),definition='\n'.join(lines))
    require(set(result)==set(TABLES),'schema-tables');return result


def require_receipt(value,side,mode,result,preview=None):
    """计划、实际DDL、CAS、空表、完整快照和STATISTICS逐项闭合，不把部分执行当完成。"""
    require(resources.is_common_file_measurement(value) and value.get('fileIndexProfile')==resources.COMMON_FILE_INDEX_PROFILE,'purpose');require(side in ('java','csharp') and mode in ('preview','apply','verify'),'mode')
    keys={'format','runId','target','operation','database','planSha256','changes','succeeded','appliedStatements','protectedFingerprint','beforeSchema','schema','indexSnapshot','emptyTablesVerified','columnsAndNonIndexDdlPreserved'}
    require(type(result) is dict and set(result)==keys,'receipt-shape');database='metarill_fresh_'+value['runId'][:16]+'_paired_'+side
    require(result['format']==resources.COMMON_FILE_INDEX_PROFILE and result['runId']==value['runId'] and result['target']==side and result['operation']==mode and result['database']==database and result['succeeded'] is True and result['emptyTablesVerified'] is True and result['columnsAndNonIndexDdlPreserved'] is True,'receipt-binding')
    require(all(type(result[key]) is str and re.fullmatch('[a-f0-9]{64}',result[key]) for key in ['planSha256','protectedFingerprint']),'receipt-hash');require(result['changes']==([] if mode=='verify' else expected_changes(side)) and type(result['appliedStatements']) is int and result['appliedStatements']==(len(expected_changes(side)) if mode=='apply' else 0),'receipt-ddl')
    before=protected_schema(result['beforeSchema']);after=protected_schema(result['schema']);require(result['beforeSchema']['database']==database==result['schema']['database'] and before==after,'protected-schema')
    normalize_statistics(result['indexSnapshot'],side,mode!='preview')
    # 快照中的索引也要与STATISTICS一致，不能让其中一个遗漏索引再以另一个的名称冒充。
    actual=[]
    for table in result['schema']['tables']:
        names={column['name'] for column in table['columns']}
        require(all(type(index) is dict and set(index) in ({'name','unique','columns'},{'name','unique','columns','directions'}) for index in table['indexes']),'schema-index-shape')
        for index in [('PRIMARY',False,['id'],None),*((i['name'],i['unique'],i['columns'],i.get('directions')) for i in table['indexes'])]:
            name,unique,columns,directions=index;require(type(unique) is bool and type(columns) is list and len(columns)>0 and all(column in names for column in columns) and (directions is None or type(directions) is list and len(directions)==len(columns) and all(direction in ('ASC','DESC') for direction in directions)),'schema-index')
            for n,column in enumerate(columns):actual.append(dict(table=table['name'],index=name,nonUnique=0 if unique or name=='PRIMARY' else 1,sequence=n+1,column=column,direction='D' if directions and directions[n]=='DESC' else 'A',prefix='',kind='BTREE',visible='YES'))
    require(sorted(actual,key=lambda row:(row['table'],row['index'],row['sequence']))==sorted(result['indexSnapshot'],key=lambda row:(row['table'],row['index'],row['sequence'])),'schema-statistics')
    if mode=='apply':require(preview is not None and result['planSha256']==preview['planSha256'] and result['beforeSchema']==preview['schema'],'confirmation')
    if preview is not None:require(result['protectedFingerprint']==preview['protectedFingerprint'] and after==protected_schema(preview['schema']),'protected-preview')
    if mode in ('preview','verify'):require(result['beforeSchema']==result['schema'],'read-no-change')
    return result


def upgrade(root,value,state,state_path,require_ownership,job,required_jobs):
    """只在原十二初始化已核清且本轮所有权完整时运行六个独占短进程，不执行普通业务或自由SQL。"""
    require(resources.is_common_file_measurement(value) and value.get('fileIndexProfile')==resources.COMMON_FILE_INDEX_PROFILE,'purpose')
    complete={entry.get('target') for entry in state.get('jobs',[]) if entry.get('containerAbsent') is True and re.fullmatch('[a-f0-9]{64}',entry.get('id',''))};require({'init-'+name for name in required_jobs}<=complete,'original-initialization')
    result={}
    for side in ('java','csharp'):
        require_ownership();before=require_receipt(value,side,'preview',job(root,value,'csharp-file-index-'+side+'-preview',state,state_path));applied=require_receipt(value,side,'apply',job(root,value,'csharp-file-index-'+side+'-apply',state,state_path,expected_index_plan=before['planSha256']),before);verified=require_receipt(value,side,'verify',job(root,value,'csharp-file-index-'+side+'-verify',state,state_path),before);result[side]=dict(preview=before,applied=applied,verified=verified)
    require(normalize_statistics(result['java']['verified']['indexSnapshot'],'java')==normalize_statistics(result['csharp']['verified']['indexSnapshot'],'csharp'),'logical-parity');return result


def inspect_indexes(query,run,side):
    """负载前后从独立连接只读17行实际STATISTICS，不依赖迁移回执或数据库仍为空。"""
    require(type(run) is str and re.fullmatch('[a-f0-9]{32}',run) and side in ('java','csharp'),'read-scope');database='metarill_fresh_'+run[:16]+'_paired_'+side
    sql="SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,SEQ_IN_INDEX,COALESCE(COLUMN_NAME,''),COALESCE(COLLATION,''),COALESCE(CAST(SUB_PART AS CHAR),''),INDEX_TYPE,IS_VISIBLE FROM information_schema.STATISTICS WHERE TABLE_SCHEMA='"+database+"' AND TABLE_NAME IN ('yz_file_library','yz_file_ref') ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX LIMIT 65;"
    rows=[]
    for line in query(sql).splitlines():
        row=line.split('\t');require(len(row)==9 and row[2] in ('0','1') and row[3].isascii() and row[3].isdigit(),'read-row');rows.append(dict(zip(['table','index','nonUnique','sequence','column','direction','prefix','kind','visible'],[row[0],row[1],int(row[2]),int(row[3]),*row[4:]])))
    logical=normalize_statistics(rows,side);return dict(target=side,rows=rows,logicalSha256=http.digest(logical),physicalAttributesVerified=True)
