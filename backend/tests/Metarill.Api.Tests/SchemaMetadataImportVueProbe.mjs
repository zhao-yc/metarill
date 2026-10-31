import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
const [root,application,table,page]=process.argv.slice(2)
const require=createRequire(path.join(root,'frontend/metarill_uni_admin/package.json'))
const {parse,compileScript,compileTemplate}=require('@vue/compiler-sfc')
// 只编译实际生成的原Vue页面，既不改页面也不使用重新设计的UI。
for(const kind of ['list','detail']){
 const file=path.join(application,'frontend/pages/yzp',`${table}-${kind}-${page}.vue`)
 const source=fs.readFileSync(file,'utf8'),{descriptor,errors}=parse(source,{filename:file})
 assert.deepEqual(errors,[]);assert.ok(descriptor.template);assert.ok(descriptor.script)
 compileScript(descriptor,{id:`import-${kind}`})
 const rendered=compileTemplate({source:descriptor.template.content,filename:file,id:`import-${kind}`})
 assert.deepEqual(rendered.errors,[])
 const mirrored=path.join(application,'backend/generated/frontend/pages/yzp',path.basename(file))
 assert.equal(source,fs.readFileSync(mirrored,'utf8'))
 assert.ok(source.includes('/admin/auto/'+table+'/'+page))
}
const menu=JSON.parse(fs.readFileSync(path.join(application,'backend/metadata/project.json'),'utf8')).configMenus
assert.equal(menu.length,2)
console.log(JSON.stringify({originalVueCompiled:true,pages:2,menuNodes:2,browserExecuted:false}))
