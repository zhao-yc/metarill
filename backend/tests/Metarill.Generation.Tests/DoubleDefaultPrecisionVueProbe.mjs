import assert from 'node:assert/strict'
import {readFile} from 'node:fs/promises'
import {mountGenerated} from './GeneratedOriginalVueHarness.mjs'
// 已知IEEE位型来自独立常量，不能从产品格式化结果回推期待值。
const expected={score:'0410000000000000',scorePrevious:'040fffffffffffff'}
const bits=value=>{const buffer=new ArrayBuffer(8),view=new DataView(buffer);view.setFloat64(0,value,false);return view.getBigUint64(0,false).toString(16).padStart(16,'0')}
const source=await readFile(process.argv[2],'utf8'),response=JSON.parse(await readFile(process.argv[3],'utf8')),page=mountGenerated(source,{}, {})
const check=record=>{for(const [key,value] of Object.entries(expected)){assert.equal(typeof record[key],'number');assert.equal(bits(record[key]),value)}}
try{
 await page.flush();check(page.state.entity);check(response);assert.equal(typeof response.id,'string')
 page.state.entity.name='默认精度';page.state.saveEntity();await page.flush();const first=page.writes().at(-1);assert(first);check(first.options.data);check(JSON.parse(JSON.stringify(first.options.data)))
 page.respond(first,response);await page.flush();check(page.state.entity)
 // 读取成功或主动清空不能重新填入新增默认值，保留原整entity回传。
 page.state.entity.score=null;page.state.entity.scorePrevious=null;page.state.saveEntity();await page.flush();const cleared=page.writes().at(-1);assert.notEqual(cleared,first);assert.equal(cleared.options.data.score,null);assert.equal(cleared.options.data.scorePrevious,null)
 console.log('原Vue：相邻double新增默认、实际后端响应、整entity保存及主动null通过；非浏览器／实库验收。')
}finally{page.app.unmount()}
