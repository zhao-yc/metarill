import fs from 'node:fs';
import path from 'node:path';
const root = path.resolve(import.meta.dirname, '..');
// 开源重建只读仓库内受控结构声明，不再从外部旧仓库可选补列。
const schemaReference = JSON.parse(fs.readFileSync(path.join(root, 'backend/metadata/platform-schema-reference.json'),'utf8'));
if(schemaReference.schemaVersion!==1||!Array.isArray(schemaReference.tables)||new Set(schemaReference.tables.map(t=>t.tableName)).size!==schemaReference.tables.length)throw new Error('平台结构参考版本或表声明重复。');
const input = JSON.parse(fs.readFileSync(path.join(root, 'backend/metadata/platform-source.json'),'utf8'));
const projectId = input.yzProjectId || '1608097412978577409';
const adminId = input.yzAdminTypes[0].id;
// 维护固定标识清单，增加列或索引不能使已发布种子的其余标识漂移。
const identifiersPath=path.join(root,'backend/metadata/identifiers.json');
const identifiers=fs.existsSync(identifiersPath)?JSON.parse(fs.readFileSync(identifiersPath,'utf8')):{};
for(const target of ['platform','demo']){
 const filename=path.join(root,'backend/metadata',target+'.json');if(!fs.existsSync(filename))continue;
 const previous=JSON.parse(fs.readFileSync(filename,'utf8'));
 const tableNames=new Map(previous.configTables.map(t=>[t.id,t.tableName]));
 for(const t of previous.configTables)identifiers['table:'+t.tableName]??=t.id;
 for(const c of previous.configColumns)identifiers['column:'+tableNames.get(c.configTableId)+':'+c.columnName]??=c.id;
 for(const f of previous.configPageColumns)identifiers['pagefield:'+f.configPageId+':'+f.configColumnId]??=f.id;
 for(const i of previous.configIndexes||[])identifiers['index:'+tableNames.get(i.configTableId)+':'+i.indexName]??=i.id;
 for(const c of previous.configIndexColumns||[])identifiers['indexfield:'+c.configIndexId+':'+c.configColumnId]??=c.id;
 const menu=previous.configMenus?.find(m=>m.configPageId==='2200000000000099000');if(menu)identifiers['menu:workbench']??=menu.id;
}
let nextId=Object.values(identifiers).reduce((max,value)=>BigInt(value)>max?BigInt(value):max,2200000000000101000n)+1n;
const id = key => {if(!key)throw new Error('新增元数据必须声明稳定标识键');return identifiers[key]??=(nextId++).toString();};
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
 if(!c){c={id:id('column:'+table.tableName+':'+name),configTableId:table.id,configTableName:table.name,columnName:name,name:label,yzProjectId:projectId,sortNo:columns.filter(x=>x.configTableId===table.id).length*10};columns.push(c);}
 c.logicalType=type;c.isDbNotNull=nullable?0:1;c.columnLength=String(length);c.defaultValue='';return c;
}
for(const t of tables) {
 const structure=schemaReference.tables.find(reference=>reference.tableName===t.tableName);
 if(!structure||!Array.isArray(structure.columns)||new Set(structure.columns.map(c=>c.name)).size!==structure.columns.length)throw new Error('平台结构参考缺表或字段声明重复：'+t.tableName);
 for(const spec of structure.columns){
  if(typeof spec.name!=='string'||typeof spec.type!=='string'||!/^[a-zA-Z_][a-zA-Z0-9_]*$/.test(spec.name)||!/^[a-zA-Z]+$/.test(spec.type)||(spec.length!==null&&typeof spec.length!=='string'))throw new Error('平台结构参考字段不合法。');
  if(['config_first_menu_id','config_first_menu_name','parent_menu_id','parent_menu_name','name_2','name_3','name2','name3'].includes(spec.name))continue;
  // 保留原导入位置、字段顺序与addColumn/standard默认策略，不迁移或重解释已发布元数据类型。
  if(!columns.some(c=>c.configTableId===t.id&&c.columnName===spec.name)){
   const c=addColumn(t,spec.name,spec.name,logical({columnName:spec.name,columnType:spec.type}),spec.name!=='id',spec.length?.split(',')[0]||(/text/.test(spec.type)?'max':'255'));
   c.columnType=spec.type;
  }
 }
 for(const [name,label,type,nullable] of standard)addColumn(t,name,label,type,nullable,type==='string'?'100':'');
 if(!columns.some(c=>c.configTableId===t.id&&c.columnName==='yz_project_id'))addColumn(t,'yz_project_id','项目','int64',true,'');
 if(['config_page','config_table','config_custom_page'].includes(t.tableName)){const c=addColumn(t,'yz_admin_type_id','所属后台','int64',true,'');const target=tables.find(x=>x.tableName==='yz_admin_type');c.controlKind='List';c.linkConfigTableId=target.id;c.linkConfigPageId=pages.find(p=>p.configTableId===target.id)?.id||'';c.controlDetail=JSON.stringify({table:'yz_admin_type',tableId:target.id,nameField:'name'});}
 if(t.tableName==='config_table')addColumn(t,'scope_kind','数据归属模式','string',true,'30');
 if(t.tableName==='yz_role_permission'){addColumn(t,'config_page_id','页面','int64',true,'');addColumn(t,'config_menu_id','菜单','int64',true,'');addColumn(t,'config_button_id','按钮','int64',true,'');}
 if(t.tableName==='config_index')addColumn(t,'is_unique','唯一索引','int32',true,'');
 if(t.tableName==='config_column'){addColumn(t,'logical_type','逻辑类型','string',false,'30');addColumn(t,'precision','数值精度','int32',true,'');addColumn(t,'scale','小数位数','int32',true,'');}
 if(t.tableName==='yz_manager') {const p=columns.find(c=>c.configTableId===t.id&&c.columnName==='password');p.columnLength='255';}
}
function newTable(tableName,name,specs,pageId=null) {
 const t={id:id('table:'+tableName),name,tableName,yzProjectId:projectId,sortNo:tables.length*10,isFunctionTable:0,isView:0,isRongyu:1};tables.push(t);
 for(const [cn,label,type='string',nullable=true,len='255'] of [...standard,...specs])addColumn(t,cn,label,type,nullable,len);
 if(pageId){pages.push({id:pageId,name,configTableId:t.id,yzAdminTypeId:adminId,yzProjectId:projectId,sortNo:100,isNew:1,isSave:1,isDelete:1,pageSize:20,whereSql:'yz_project_id=@current_yz_project_id'});}
 return t;
}
const menuTable=newTable('config_menu','菜单',[['name','名称','string',false,'50'],['parent_menu_id','父菜单','int64'],['yz_project_id','项目','int64',false],['yz_admin_type_id','所属后台','int64',false],['kind','菜单类型','string',false,'30'],['config_page_id','目标页面','int64'],['config_custom_page_id','自定义页面','int64'],['navigate_url','页面地址','string',true,'500'],['icon_name','图标','string',true,'50'],['sort_no','排序','int32'],['is_hidden','隐藏','int32'],['is_show_count','数量角标','int32'],['visible_scheme','可见方案','string',true,'50']],'2200000000000099000');
Object.assign(pages.find(p=>p.id==='2200000000000099000'),{isNew:0,isSave:0,isUpdate:0,isDelete:0});
for(const p of pages)if(['yz_role_permission','config_index','config_index_column'].includes(tables.find(t=>t.id===p.configTableId)?.tableName))Object.assign(p,{isNew:0,isSave:0,isUpdate:0,isDelete:0});
menus.push({id:id('menu:workbench'),name:'菜单',parentMenuId:input.configFirstMenus[0].id,kind:'GENERATED_PAGE',configPageId:'2200000000000099000',yzAdminTypeId:adminId,yzProjectId:projectId,sortNo:35});
newTable('sys_session','登录会话',[['account_id','账号','int64',false],['token_hash','令牌摘要','string',false,'64'],['expires_at','过期时间','datetime',false],['kind','授权类型','string',false,'20'],['grant_id','设备授权','int64'],['project_id','授权项目','int64']]);
newTable('sys_agent_grant','Agent设备授权',[['account_id','账号','int64'],['project_id','项目','int64',false],['device_code_hash','设备码摘要','string',false,'64'],['user_code','用户码','string',false,'16'],['client_name','设备名称','string',false,'100'],['status','授权状态','string',false,'20'],['expires_at','过期时间','datetime',false],['refresh_hash','刷新摘要','string',true,'64'],['client_id','客户端','string',true,'100'],['last_poll_at','上次轮询','datetime'],['poll_interval_seconds','轮询间隔秒','int32'],['device_redeemed_at','设备兑换时间','datetime'],['refresh_expires_at','刷新有效期','datetime']]);
newTable('sys_agent_task','Agent请求任务',[['account_id','账号','int64',false],['project_id','项目','int64',false],['request_id','请求标识','string',false,'36'],['capability','能力','string',false,'150'],['request_hash','请求摘要','string',false,'64'],['status','状态','string',false,'20'],['outcome','结果状态','string',false,'20'],['result_id','结果标识','int64'],['http_status','HTTP状态','int32'],['business_code','业务状态','int32'],['expires_at','过期时间','datetime',false]]);
newTable('yz_file_library','文件库',[['name','名称','string',false,'255'],['content_type','文件类型','string',false,'150'],['size','文件大小','int64',false],['storage_key','存储标识','string',false,'100'],['access_mode','访问模式','string',false,'20'],['status','文件状态','string',false,'20'],['owner_id','所有者','int64',false],['folder_id','文件夹','int64'],['yz_project_id','项目','int64',false],['sha256','校验摘要','string',true,'64']]);
newTable('yz_file_library_folder','文件夹',[['name','名称','string',false,'100'],['parent_folder_id','上级','int64'],['owner_id','所有者','int64',false],['yz_project_id','项目','int64',false]]);
newTable('yz_file_ref','文件引用',[['file_id','文件','int64',false],['page_id','页面','int64',false],['record_id','记录','int64',false],['field_name','字段','string',false,'100'],['yz_project_id','项目','int64',false]]);
for(const c of columns) {
 c.yzProjectId=projectId;c.logicalType=c.logicalType||logical(c);
 c.columnLength=String(c.columnLength??'255');c.sortNo=Number(c.sortNo||0);c.defaultValue='';
 if(c.columnName==='id'){c.logicalType='int64';c.isDbNotNull=1;}
 if(c.columnName==='logical_type')c.defaultValue='string';
 if(c.columnName==='detail_page_buttons_position')c.defaultValue='0';
 if(c.columnName.startsWith('is_')&&c.logicalType==='int32'&&c.isDbNotNull===1)c.defaultValue='0';
 if(c.logicalType==='int64'||c.logicalType==='int32')c.columnLength='';
 if(c.columnName==='sort_no'||c.columnName==='page_size')c.logicalType='int32';
 if(c.logicalType==='string'&&!c.columnLength)c.columnLength='255';
 // 虚拟布局字段已经在来源过滤，不进入物理结构。
}
for(const c of columns)if(c.columnName==='config_second_menu_id')c.columnName='config_menu_id';
columns=columns.filter((c,i,all)=>all.findIndex(x=>x.configTableId===c.configTableId&&x.columnName===c.columnName)===i);
for(const t of tables)if(t.tableName==='yz_project')t.scopeKind='global';
const byTable=new Map(tables.map(t=>[t.id,columns.filter(c=>c.configTableId===t.id)]));
const oldFields=new Map(oldPageColumns.map(f=>[f.configPageId+':'+f.configColumnId,f]));
for(const c of columns){if(c.columnName==='config_second_menu_id'){c.columnName='config_menu_id';c.name='菜单';c.logicalType='int64';}if(c.joinTable==='config_second_menu'){c.joinTable='config_menu';c.linkConfigTableId=menuTable.id;c.linkConfigPageId='2200000000000099000';c.controlDetail=JSON.stringify({table:'config_menu',tableId:menuTable.id,nameField:'name'});}if(c.linkConfigTableId&&!tables.some(t=>t.id===c.linkConfigTableId)){c.linkConfigTableId='';c.linkConfigPageId='';c.controlKind='';c.controlDetail='';}}
let pageColumns=[];
for(const p of pages){
 const t=tables.find(t=>t.id===p.configTableId);
 p.yzProjectId=projectId;p.yzAdminTypeId=adminId;p.pageSize=Number(p.pageSize||20);
 p.isSave=Number(p.isEdit??1)===1&&Number(p.isUpdate??p.isSave??1)===1?1:0;
 if(p.whereSql && !/^[a-z_]+\s*=\s*@(?:current_yz_project_id|parent_id|parentId)(?:\s+and\s+[a-z_]+\s*=\s*@(?:current_yz_project_id|parent_id|parentId))*$/i.test(p.whereSql))p.whereSql='yz_project_id=@current_yz_project_id';
 for(const c of byTable.get(t.id)||[]){
  const old=oldFields.get(p.id+':'+c.id);
  if(c.columnName==='id'||c.columnName==='delete_no'||c.columnName.includes('password')||c.columnName==='salt')continue;
  pageColumns.push({...old,id:old?.id||id('pagefield:'+p.id+':'+c.id),name:c.name,configPageId:p.id,configColumnId:c.id,columnName:c.columnName,yzProjectId:projectId,sortNo:Number(old?.sortNo||c.sortNo||0),isShow:old?.isShow??old?.isInGrid??1,isEdit:old?.isEdit??old?.isInEdit??(standard.some(s=>s[0]===c.columnName)?0:1),isRequired:old?.isRequired??old?.isMust??c.isDbNotNull});
 }
}
const finalPageIds=new Set(pages.map(p=>p.id));
menus=menus.filter(m=>!m.configPageId||finalPageIds.has(m.configPageId));
menus=menus.map(m=>({...m,yzProjectId:projectId}));
const catalog={schemaVersion:1,backendTarget:'csharp',yzProjectId:projectId,yzProjectName:'Metarill',databaseProvider:'mysql',configTables:tables,configColumns:columns,configPages:pages,configPageColumns:pageColumns,yzAdminTypes:input.yzAdminTypes,configMenus:menus,configCustomPages:input.menuCustomPages.map(p=>({id:p.id,name:p.pagePath.split('/').at(-1),pagePath:p.pagePath,yzProjectId:projectId,yzAdminTypeId:adminId})),configButtons:input.configButtons.filter(b=>!b.configPageId||finalPageIds.has(b.configPageId)).map(b=>({...b,yzProjectId:projectId})),configChildren:input.configChildren.filter(c=>(!c.configPageId||finalPageIds.has(c.configPageId))&&(!c.childConfigPageId||finalPageIds.has(c.childConfigPageId))).map(c=>({...c,yzProjectId:projectId})),configIndexes:[],configIndexColumns:[]};
const indexes=[],indexColumns=[];
function index(tableName,name,fields,unique=false) {
 const t=tables.find(t=>t.tableName===tableName); if(!t)return;
 const cols=fields.map(f=>columns.find(c=>c.configTableId===t.id&&c.columnName===f)); if(cols.some(c=>!c))return;
 const idx={id:id('index:'+t.tableName+':'+name),configTableId:t.id,indexName:name,isUnique:unique?1:0,indexType:unique?'UNIQUE':'INDEX',isEnabled:1,yzProjectId:projectId};indexes.push(idx);
 cols.forEach((c,i)=>indexColumns.push({id:id('indexfield:'+idx.id+':'+c.id),configIndexId:idx.id,configColumnId:c.id,columnName:c.columnName,sortNo:i*10,sortDirection:'ASC',yzProjectId:projectId}));
}
index('yz_manager','ux_manager_name',['name'],true);
// 安全当前读与删除引用按角色/后台和活动标记定位；不重复追加MySQL二级索引已携带的主键。
index('yz_role_permission','yzp_idx_role_permission_role_active',['yz_role_id','delete_no']);
index('yz_role','yzp_idx_role_admin_active',['yz_admin_type_id','delete_no']);
index('yz_manager','yzp_idx_manager_admin_active',['yz_admin_type_id','delete_no']);
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
index('yz_file_ref','ix_file_scope_record',['yz_project_id','record_id','page_id','delete_no']);
index('yz_file_ref','ix_file_delete',['file_id','delete_no']);
index('yz_file_library','ix_file_folder',['yz_project_id','folder_id','delete_no']);
index('yz_file_library_folder','ix_folder_parent',['yz_project_id','parent_folder_id','delete_no']);
// 保留已发布索引工作台的物理索引；稳定ID由identifiers清单维护，完整重建不能丢失。
index('config_index','ix_index_table',['config_table_id','delete_no']);
index('config_index','ix_index_project',['yz_project_id','delete_no']);
index('config_index_column','ix_index_column_parent',['config_index_id','delete_no','sort_no']);
index('config_index_column','ix_index_column_project',['yz_project_id','delete_no']);
catalog.configIndexes=indexes;catalog.configIndexColumns=indexColumns;
fs.writeFileSync(path.join(root,'backend/metadata/platform.json'),JSON.stringify(catalog,null,2)+'\n');
const demoProject='2200000000000100000', demoAdmin='2200000000000100001';
const demoTable={id:'2200000000000100010',name:'示例客户',tableName:'demo_customer',yzProjectId:demoProject,sortNo:10};
const demoColumns=[...standard,['yz_project_id','项目','int64',false],['name','名称','string',false],['notes','备注','string',true,'max'],['amount','金额','decimal',true],['is_enabled','启用','int32',true]].map(([cn,label,type,nullable,length],i)=>({id:String(2200000000000100100n+BigInt(i)),name:label,configTableId:demoTable.id,columnName:cn,logicalType:type,columnType:type,columnLength:length|| (type==='string'?'255':''),isDbNotNull:nullable?0:1,sortNo:i*10,defaultValue:'',yzProjectId:demoProject}));
const demoPage={id:'2200000000000100020',name:'示例客户',configTableId:demoTable.id,yzAdminTypeId:demoAdmin,yzProjectId:demoProject,isNew:1,isSave:1,isUpdate:1,isEdit:1,isDelete:1,isCopy:1,isImport:1,isExport:1,importMethod:'insert',exportMethod:'list',pageSize:20,whereSql:'yz_project_id=@current_yz_project_id'};
const demo={schemaVersion:1,backendTarget:'csharp',yzProjectId:demoProject,yzProjectName:'示例项目',databaseProvider:'mysql',configTables:[demoTable],configColumns:demoColumns,configPages:[demoPage],configPageColumns:demoColumns.filter(c=>!standard.some(s=>s[0]===c.columnName)).map(c=>({id:id('pagefield:'+demoPage.id+':'+c.id),name:c.name,configPageId:demoPage.id,yzProjectId:demoProject,configColumnId:c.id,columnName:c.columnName,isShow:1,isEdit:c.columnName==='yz_project_id'?0:1,isRequired:c.isDbNotNull,isInFilter:c.columnName==='name'?1:0,isSort:['name','amount'].includes(c.columnName)?1:0,sortNo:c.sortNo})),yzAdminTypes:[{id:demoAdmin,name:'示例后台',yzProjectId:demoProject}],configMenus:[{id:'2200000000000100030',name:'示例',kind:'DIRECTORY',parentMenuId:null,yzAdminTypeId:demoAdmin,yzProjectId:demoProject,sortNo:10},{id:'2200000000000100031',name:'客户',kind:'GENERATED_PAGE',parentMenuId:'2200000000000100030',configPageId:demoPage.id,yzAdminTypeId:demoAdmin,yzProjectId:demoProject,sortNo:10}],configButtons:[],configChildren:[],configIndexes:[],configIndexColumns:[]};
fs.writeFileSync(path.join(root,'backend/metadata/demo.json'),JSON.stringify(demo,null,2)+'\n');
fs.writeFileSync(identifiersPath,JSON.stringify(Object.fromEntries(Object.entries(identifiers).sort(([a],[b])=>a.localeCompare(b))),null,2)+'\n');
console.log(JSON.stringify({表:tables.length,字段:columns.length,页面:pages.length,页面字段:pageColumns.length,菜单:menus.length}));
