/** 仅序列化工作台明确可写标志；名称、归属和审计字段只用于显示。 */
export function pageFieldPatchPayload(rows) {
 const fields=['id','isInGrid','isInEdit','isMust','isReadOnly','isInFilter','isImportant','isPrint'];
 return rows.map(row=>Object.fromEntries(fields.filter(key=>Object.prototype.hasOwnProperty.call(row,key)).map(key=>[key,row[key]])));
}
/** 顺序提交只携带原记录ID与输入值，合法整数及项目范围仍由服务端校验。 */
export function columnOrderPatchPayload(rows) {
 return rows.map(row=>Object.fromEntries(['id','sortNo'].filter(key=>Object.prototype.hasOwnProperty.call(row,key)).map(key=>[key,row[key]])));
}
