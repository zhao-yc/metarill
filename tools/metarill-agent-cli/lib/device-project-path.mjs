import { createHash } from 'node:crypto';
const lower=value=>value.replace(/[A-Z]/g,char=>char.toLowerCase());
const bs=String.fromCharCode(92);
/** 不访问文件系统，仅核对原设备配置与绑定的明确目录身份。 */
export function normalizeDeviceProjectPath(raw){
 if(typeof raw!=='string'||!raw.trim()||raw.length>4096||raw!==raw.trim()||/\p{Cc}/u.test(raw)||[...raw].some(char=>{const n=char.codePointAt(0);return n>=0xd800&&n<=0xdfff}))throw new Error('设备路径必须是明确的有界绝对目录');
 const drive=raw.length>=3&&/[A-Za-z]/.test(raw[0])&&raw[1]===':'&&(raw[2]==='/'||raw[2]===bs);
 const windows=drive||raw.startsWith(bs+bs)||raw.startsWith('//');const text=windows?raw.replaceAll(bs,'/'):raw;let prefix,parts;
 if(drive){prefix=lower(text.slice(0,2))+'/';parts=text.slice(3).split('/').filter(Boolean)}
 else if(windows){const all=text.slice(2).split('/').filter(Boolean);if(all.length<3)throw new Error('设备路径不能是共享根目录');segment(all[0]);segment(all[1]);prefix='//'+lower(all[0])+'/'+lower(all[1])+'/';parts=all.slice(2)}
 else if(text.startsWith('/')&&!text.includes(bs)){prefix='/';parts=text.slice(1).split('/').filter(Boolean)}
 else throw new Error('设备路径不能是相对路径或隐式盘符路径');
 if(!parts.length)throw new Error('设备路径不能是根目录');
 for(const part of parts){if(part==='.'||part==='..')throw new Error('设备路径不能含点层级');if(windows)segment(part)}
 return (windows?'windows|':'posix|')+prefix+(windows?parts.map(lower):parts).join('/');
}
/** 固定Windows普通目录边界，不将设备名、ADS或尾点/空格当作项目目录。 */
function segment(part){const name=lower(part).split('.')[0];const reserved=['con','prn','aux','nul'].includes(name)||/^(com|lpt)[1-9]$/.test(name);if(part==='.'||part==='..'||/[ .]$/.test(part)||[...part].some(char=>[60,62,58,34,124,63,42].includes(char.charCodeAt(0)))||reserved)throw new Error('设备路径包含特殊Windows名称')}
/** 与C#固定相同版本前缀和UTF8摘要，只向平台发送摘要。 */
export function deviceProjectPathFingerprint(raw){return createHash('sha256').update('metarill-device-path-v1'+String.fromCharCode(10)+normalizeDeviceProjectPath(raw)).digest('hex')}
