import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(import.meta.dirname, '..');
const sourceRoot = process.env.YZP_REFERENCE_ROOT || 'D:/svn/yzp';
const input = JSON.parse(fs.readFileSync(path.join(root, 'backend/metadata/platform-source.json'),'utf8'));
const projectId = input.yzProjectId || '1608097412978577409';
const adminId = input.yzAdminTypes[0].id;
let nextId = 2200000000000000000n;
const id = () => String(nextId++);
const snake = value => value.replace(/[A-Z]/g, c => '_' + c.toLowerCase());
const camel = value => value.replace(/_([a-z])/g, (_, c) => c.toUpperCase());
const standard = [
 ['id','标识','int64',false],['insert_time','创建时间','datetime',true],['update_time','修改时间','datetime',true],
 ['insert_manager_id','创建账号','int64',true],['update_manager_id','修改账号','int64',true],
 ['insert_user_name','创建账号名','string',true],['update_user_name','修改账号名','string',true],
 ['insert_manager_real_name','创建姓名','string',true],['update_manager_real_name','修改姓名','string',true],
 ['delete_no','删除时间','datetime',true]
];
const excludedTables = new Set(['config_first_menu','config_second_menu','yz_yzp_version','copy_table']);
let tables = input.configTables.filter(t=>!excludedTables.has(t.tableName)).map(t=>({...t, yzProjectId:projectId, configFirstMenuId:undefined, configFirstMenuName:undefined}));
const allowedTables=new Set(tables.map(t=>t.id));
let columns=input.configColumns.filter(c=>c.isVirtualColumn!==1 && allowedTables.has(c.configTableId) && c.columnName!=='config_first_menu_id' && c.columnName!=='config_first_menu_name').map(c=>({...c}));
let pages=input.configPages.filter(p=>allowedTables.has(p.configTableId)).map(p=>({...p, yzAdminTypeId:adminId, configFirstMenuId:undefined, configFirstMenuName:undefined, pageSize:Number(p.pageSize||20), sortNo:Number(p.sortNo||0)}));
const allowedPages=new Set(pages.map(p=>p.id));
const oldPageColumns=input.configPageColumns.filter(p=>allowedPages.has(p.configPageId));
const firstMenus=new Map(input.configFirstMenus.map(m=>[m.id,m]));
let menus=input.configFirstMenus.map(m=>({...m,kind:'DIRECTORY',parentMenuId:null,yzAdminTypeId:m.yzAdminTypeId||adminId,sortNo:Number(m.sortNo||0)}));
for(const m of input.configSecondMenus) {
 if(m.configPageId && !allowedPages.has(m.configPageId)) continue;
 menus.push({...m,kind:m.configPageId?'GENERATED_PAGE':m.configCustomPageId?'CUSTOM_PAGE':'DIRECTORY',parentMenuId:m.parentConfigSecondMenuId||m.configFirstMenuId,yzAdminTypeId:firstMenus.get(m.configFirstMenuId)?.yzAdminTypeId||adminId,sortNo:Number(m.sortNo||0),configFirstMenuId:undefined,configFirstMenuName:undefined,parentConfigSecondMenuId:undefined,parentConfigSecondMenuName:undefined});
}
function logical(c) {
 if(c.columnName==='id'||(/^(config_|yz_|parent_|insert_manager_|update_manager_)/.test(c.columnName)&&c.columnName.endsWith('_id'))) return 'int64';
 const type=(c.columnType||'').toLowerCase();
 if(type.includes('bigint'))return 'int64';
 if(['int','integer','smallint','tinyint'].includes(type)||c.columnName==='sort_no'||c.columnName==='page_size')return 'int32';
 if(/decimal|numeric|money|float|double/.test(type))return 'decimal';
 if(type==='date')return 'date';
 if(type==='time')return 'time';
 if(/date|time/.test(type))return 'datetime';
 if(/binary|blob/.test(type))return 'binary';
 if(type==='json')return 'json';
 return 'string';
}
function addColumn(table,name,label,type='string',nullable=true,length='255') {
 let c=columns.find(c=>c.configTableId===table.id&&c.columnName===name);
 if(!c){c={id:id(),configTableId:table.id,configTableName:table.name,columnName:name,name:label,yzProjectId:projectId,sortNo:columns.filter(x=>x.configTableId===table.id).length*10};columns.push(c);}
 c.logicalType=type;c.isDbNotNull=nullable?0:1;c.columnLength=String(length);c.defaultValue='';return c;
}
for(const t of tables) {
 const sqlPath=path.join(sourceRoot,'backend/yzp/src/main/resources/yzp-config/schema',t.tableName+'.sql');
 if(fs.existsSync(sqlPath)){
  const sql=fs.readFileSync(sqlPath,'utf8');
  const re=/^\s*\x60([a-zA-Z_][a-zA-Z0-9_]*)\x60\s+([a-zA-Z]+)(?:\(([^)]+)\))?/gm;
  for(const m of sql.matchAll(re)){
   if(['config_first_menu_id','config_first_menu_name','parent_menu_id','parent_menu_name','name_2','name_3','name2','name3'].includes(m[1]))continue;
   if(!columns.some(c=>c.configTableId===t.id&&c.columnName===m[1])){
    const c=addColumn(t,m[1],m[1],logical({columnName:m[1],columnType:m[2]}),m[1]!=='id',m[3]?.split(',')[0]||(/text/.test(m[2])?'max':'255'));
    c.columnType=m[2];
   }
  }
 }
 for(const [name,label,type,nullable] of standard)addColumn(t,name,label,type,nullable,type==='string'?'100':'');
 if(!columns.some(c=>c.configTableId===t.id&&c.columnName==='yz_project_id'))addColumn(t,'yz_project_id','项目','int64',true,'');
 if(t.tableName==='config_page'||t.tableName==='config_table')addColumn(t,'yz_admin_type_id','所属后台','int64',true,'');
 if(t.tableName==='yz_role_permission'){addColumn(t,'config_page_id','页面','int64',true,'');addColumn(t,'config_menu_id','菜单','int64',true,'');}
 if(t.tableName==='config_index')addColumn(t,'is_unique','唯一索引','int32',true,'');
 if(t.tableName==='config_column'){addColumn(t,'logical_type','逻辑类型','string',false,'30');addColumn(t,'precision','数值精度','int32',true,'');addColumn(t,'scale','小数位数','int32',true,'');}
 if(t.tableName==='yz_manager') {const p=columns.find(c=>c.configTableId===t.id&&c.columnName==='password');p.columnLength='255';}
}
function newTable(tableName,name,specs,pageId=null) {
 const t={id:id(),name,tableName,yzProjectId:projectId,sortNo:tables.length*10,isFunctionTable:0,isView:0,isRongyu:1};tables.push(t);
 for(const [cn,label,type='string',nullable=true,len='255'] of [...standard,...specs])addColumn(t,cn,label,type,nullable,len);
 if(pageId){pages.push({id:pageId,name,configTableId:t.id,yzAdminTypeId:adminId,yzProjectId:projectId,sortNo:100,isNew:1,isSave:1,isDelete:1,pageSize:20,whereSql:'yz_project_id=@current_yz_project_id'});}
 return t;
}
const menuTable=newTable('config_menu','菜单',[['name','名称','string',false,'50'],['parent_menu_id','父菜单','int64'],['yz_project_id','项目','int64',false],['yz_admin_type_id','所属后台','int64',false],['kind','菜单类型','string',false,'30'],['config_page_id','目标页面','int64'],['config_custom_page_id','自定义页面','int64'],['navigate_url','页面地址','string',true,'500'],['icon_name','图标','string',true,'50'],['sort_no','排序','int32'],['is_hidden','隐藏','int32'],['is_show_count','数量角标','int32'],['visible_scheme','可见方案','string',true,'50']],'2200000000000099000');
menus.push({id:id(),name:'菜单',parentMenuId:input.configFirstMenus[0].id,kind:'GENERATED_PAGE',configPageId:'2200000000000099000',yzAdminTypeId:adminId,yzProjectId:projectId,sortNo:35});
newTable('sys_session','登录会话',[['account_id','账号','int64',false],['token_hash','令牌摘要','string',false,'64'],['expires_at','过期时间','datetime',false],['kind','授权类型','string',false,'20'],['grant_id','设备授权','int64'],['project_id','授权项目','int64']]);
newTable('sys_agent_grant','Agent设备授权',[['account_id','账号','int64'],['project_id','项目','int64',false],['device_code_hash','设备码摘要','string',false,'64'],['user_code','用户码','string',false,'16'],['client_name','设备名称','string',false,'100'],['status','授权状态','string',false,'20'],['expires_at','过期时间','datetime',false],['refresh_hash','刷新摘要','string',true,'64']]);
newTable('sys_agent_task','Agent请求任务',[['account_id','账号','int64',false],['project_id','项目','int64',false],['request_id','请求标识','string',false,'36'],['capability','能力','string',false,'150'],['request_hash','请求摘要','string',false,'64'],['status','状态','string',false,'20'],['outcome','结果状态','string',false,'20'],['result_id','结果标识','int64'],['http_status','HTTP状态','int32'],['business_code','业务状态','int32'],['expires_at','过期时间','datetime',false]]);
newTable('yz_file_library','文件库',[['name','名称','string',false,'255'],['content_type','文件类型','string',false,'150'],['size','文件大小','int64',false],['storage_key','存储标识','string',false,'100'],['access_mode','访问模式','string',false,'20'],['status','文件状态','string',false,'20'],['owner_id','所有者','int64',false],['folder_id','文件夹','int64'],['yz_project_id','项目','int64',false],['sha256','校验摘要','string',true,'64']]);
newTable('yz_file_library_folder','文件夹',[['name','名称','string',false,'100'],['parent_folder_id','上级','int64'],['owner_id','所有者','int64',false],['yz_project_id','项目','int64',false]]);
newTable('yz_file_ref','文件引用',[['file_id','文件','int64',false],['page_id','页面','int64',false],['record_id','记录','int64',false],['field_name','字段','string',false,'100'],['yz_project_id','项目','int64',false]]);
for(const c of columns) {
 c.logicalType=c.logicalType||logical(c);
 c.columnLength=String(c.columnLength??'255');c.sortNo=Number(c.sortNo||0);c.defaultValue='';
 if(c.columnName==='id'){c.logicalType='int64';c.isDbNotNull=1;}
 if(c.logicalType==='int64'||c.logicalType==='int32')c.columnLength='';
 if(c.columnName==='sort_no'||c.columnName==='page_size')c.logicalType='int32';
 if(c.logicalType==='string'&&!c.columnLength)c.columnLength='255';
 // 虚拟布局字段已经在来源过滤，不进入物理结构。
}
const byTable=new Map(tables.map(t=>[t.id,columns.filter(c=>c.configTableId===t.id)]));
const oldFields=new Map(oldPageColumns.map(f=>[f.configPageId+':'+f.configColumnId,f]));
let pageColumns=[];
for(const p of pages){
 const t=tables.find(t=>t.id===p.configTableId);
 p.yzProjectId=projectId;p.yzAdminTypeId=adminId;p.pageSize=Number(p.pageSize||20);
 if(p.whereSql && !/^[a-z_]+\s*=\s*@(?:current_yz_project_id|parent_id|parentId)(?:\s+and\s+[a-z_]+\s*=\s*@(?:current_yz_project_id|parent_id|parentId))*$/i.test(p.whereSql))p.whereSql='yz_project_id=@current_yz_project_id';
 for(const c of byTable.get(t.id)||[]){
  const old=oldFields.get(p.id+':'+c.id);
  if(c.columnName==='id'||c.columnName==='delete_no'||c.columnName.includes('password')||c.columnName==='salt')continue;
  pageColumns.push({...old,id:old?.id||id(),name:c.name,configPageId:p.id,configColumnId:c.id,columnName:c.columnName,yzProjectId:projectId,sortNo:Number(old?.sortNo||c.sortNo||0),isShow:old?.isShow??1,isEdit:old?.isEdit??(standard.some(s=>s[0]===c.columnName)?0:1)});
 }
}
const finalPageIds=new Set(pages.map(p=>p.id));
menus=menus.filter(m=>!m.configPageId||finalPageIds.has(m.configPageId));
const catalog={schemaVersion:1,backendTarget:'csharp',yzProjectId:projectId,yzProjectName:'Metarill',databaseProvider:'mysql',configTables:tables,configColumns:columns,configPages:pages,configPageColumns:pageColumns,yzAdminTypes:input.yzAdminTypes,configMenus:menus,configButtons:input.configButtons.filter(b=>!b.configPageId||finalPageIds.has(b.configPageId)),configChildren:input.configChildren.filter(c=>(!c.configPageId||finalPageIds.has(c.configPageId))&&(!c.childConfigPageId||finalPageIds.has(c.childConfigPageId))),configIndexes:[],configIndexColumns:[]};
const indexes=[],indexColumns=[];
function index(tableName,name,fields,unique=false) {
 const t=tables.find(t=>t.tableName===tableName); if(!t)return;
 const cols=fields.map(f=>columns.find(c=>c.configTableId===t.id&&c.columnName===f)); if(cols.some(c=>!c))return;
 const idx={id:id(),configTableId:t.id,indexName:name,isUnique:unique?1:0,indexType:unique?'UNIQUE':'INDEX',isEnabled:1,yzProjectId:projectId};indexes.push(idx);
 cols.forEach((c,i)=>indexColumns.push({id:id(),configIndexId:idx.id,configColumnId:c.id,columnName:c.columnName,sortNo:i*10,sortDirection:'ASC',yzProjectId:projectId}));
}
index('yz_manager','ux_manager_name',['name'],true);
index('sys_session','ux_session_token',['token_hash'],true);
index('sys_agent_grant','ux_grant_device',['device_code_hash'],true);
index('sys_agent_grant','ux_grant_user',['user_code'],true);
index('sys_agent_task','ux_task_request',['account_id','project_id','request_id'],true);
index('config_menu','ix_menu_parent',['yz_project_id','yz_admin_type_id','parent_menu_id','delete_no','sort_no']);
index('config_table','ix_table_project',['yz_project_id','delete_no']);
index('config_column','ix_column_table',['config_table_id','delete_no','sort_no']);
index('config_page','ix_page_table',['config_table_id','delete_no']);
index('config_page_column','ix_page_field',['config_page_id','config_column_id','delete_no']);
index('yz_file_ref','ix_file_record',['page_id','record_id','delete_no']);
catalog.configIndexes=indexes;catalog.configIndexColumns=indexColumns;
fs.writeFileSync(path.join(root,'backend/metadata/platform.json'),JSON.stringify(catalog,null,2)+'\n');
const demoProject='2200000000000100000', demoAdmin='2200000000000100001';
const demoTable={id:'2200000000000100010',name:'示例客户',tableName:'demo_customer',yzProjectId:demoProject,sortNo:10};
const demoColumns=[...standard,['yz_project_id','项目','int64',false],['name','名称','string',false],['notes','备注','string',true,'max'],['amount','金额','decimal',true],['is_enabled','启用','int32',true]].map(([cn,label,type,nullable,length],i)=>({id:String(2200000000000100100n+BigInt(i)),name:label,configTableId:demoTable.id,columnName:cn,logicalType:type,columnType:type,columnLength:length|| (type==='string'?'255':''),isDbNotNull:nullable?0:1,sortNo:i*10,defaultValue:'',yzProjectId:demoProject}));
const demoPage={id:'2200000000000100020',name:'示例客户',configTableId:demoTable.id,yzAdminTypeId:demoAdmin,yzProjectId:demoProject,isNew:1,isSave:1,isDelete:1,isCopy:1,pageSize:20,whereSql:'yz_project_id=@current_yz_project_id'};
const demo={schemaVersion:1,backendTarget:'csharp',yzProjectId:demoProject,yzProjectName:'示例项目',databaseProvider:'mysql',configTables:[demoTable],configColumns:demoColumns,configPages:[demoPage],configPageColumns:demoColumns.filter(c=>!standard.some(s=>s[0]===c.columnName)).map(c=>({id:id(),name:c.name,configPageId:demoPage.id,configColumnId:c.id,columnName:c.columnName,isShow:1,isEdit:c.columnName==='yz_project_id'?0:1,isRequired:c.isDbNotNull,sortNo:c.sortNo})),yzAdminTypes:[{id:demoAdmin,name:'示例后台',yzProjectId:demoProject}],configMenus:[{id:'2200000000000100030',name:'示例',kind:'DIRECTORY',parentMenuId:null,yzAdminTypeId:demoAdmin,yzProjectId:demoProject,sortNo:10},{id:'2200000000000100031',name:'客户',kind:'GENERATED_PAGE',parentMenuId:'2200000000000100030',configPageId:demoPage.id,yzAdminTypeId:demoAdmin,yzProjectId:demoProject,sortNo:10}],configButtons:[],configChildren:[],configIndexes:[],configIndexColumns:[]};
fs.writeFileSync(path.join(root,'backend/metadata/demo.json'),JSON.stringify(demo,null,2)+'\n');
console.log(JSON.stringify({表:tables.length,字段:columns.length,页面:pages.length,页面字段:pageColumns.length,菜单:menus.length}));
